using System.Buffers.Binary;
using System.Net;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TPatch.Data;
using TPatch.Server.Net;

namespace TPatch.Server;

/// <summary>One unit of work for the batch task: a framed packet, or a disconnect (Packet null).</summary>
internal readonly record struct BatchItem(PacketConnection Conn, byte[]? Packet);

/// <summary>
/// Hosted service that boots the patch server: resolves the login endpoint (from TLoadService when a DB is
/// present, else from config), picks a data source, then runs the TCP acceptor and the single batch task
/// that serializes all handling (mirroring the C++ IOCP workers + one shared session map).
/// </summary>
public sealed class PatchWorker : BackgroundService
{
    private readonly PatchServerOptions _opt;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<PatchWorker> _log;

    public PatchWorker(IOptions<PatchServerOptions> opt, ILoggerFactory loggerFactory)
    {
        _opt = opt.Value;
        _loggerFactory = loggerFactory;
        _log = loggerFactory.CreateLogger<PatchWorker>();
    }

    // Group ID for the login service in the topology proc (SVRGRP_LOGINSVR from 4StoryDlg.h).
    private const byte SvrGrpLoginSvr = 2;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        IPatchSource source = EmptyPatchSource.Instance;
        string loginAddress = _opt.LoginAddress;
        ushort loginPort = (ushort)_opt.LoginPort;

        if (!string.IsNullOrWhiteSpace(_opt.Db.ConnectionString))
        {
            var db = new PatchDatabase(_opt.Db.ConnectionString);
            try
            {
                await db.PingAsync(stoppingToken);
                source = db;
                if (_opt.ResolveLoginFromDb)
                {
                    var login = await db.LoadServiceAsync(0, SvrGrpLoginSvr, stoppingToken);
                    if (login is { } ep)
                    {
                        loginAddress = ep.Ip;
                        loginPort = ep.Port;
                        _log.LogInformation("Login endpoint resolved from TLoadService: {Ip}:{Port}.", ep.Ip, ep.Port);
                    }
                    else
                    {
                        _log.LogWarning("TLoadService returned no login endpoint; using config {Ip}:{Port}.", loginAddress, loginPort);
                    }
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Patch DB unavailable at startup; serving with no patch data.");
            }
        }
        else
        {
            _log.LogWarning("No patch DB configured; serving with no patch data (empty file lists).");
        }

        uint loginIp = PackIp(loginAddress);
        var service = new PatchService(source, _opt, loginIp, loginPort, _loggerFactory.CreateLogger<PatchService>());

        _log.LogInformation("Patch server={Sid} group={Grp} port={Port} ftp='{Ftp}' login={Ip}:{Port2} db={Db}",
            _opt.ServerId, _opt.GroupId, _opt.Port, _opt.FtpUrl, loginAddress, loginPort,
            source is PatchDatabase ? "live" : "empty");

        // Single batch task: all packet handling + session-map mutation happens here, in order.
        var batch = Channel.CreateUnbounded<BatchItem>(new UnboundedChannelOptions { SingleReader = true });
        var batchTask = RunBatchAsync(batch.Reader, service, stoppingToken);

        var server = new PacketServer(_loggerFactory.CreateLogger<PacketServer>());
        await server.ListenAsync(
            _opt.Port,
            onPacket: (conn, data) => { batch.Writer.TryWrite(new BatchItem(conn, data)); return ValueTask.CompletedTask; },
            onDisconnected: conn => { batch.Writer.TryWrite(new BatchItem(conn, null)); return ValueTask.CompletedTask; },
            token: stoppingToken);

        batch.Writer.TryComplete();
        try { await batchTask; } catch (OperationCanceledException) { }
    }

    private async Task RunBatchAsync(ChannelReader<BatchItem> reader, PatchService service, CancellationToken token)
    {
        try
        {
            await foreach (var item in reader.ReadAllAsync(token))
            {
                if (item.Packet is null) service.OnDisconnect(item.Conn);
                else await service.DispatchAsync(item.Conn, item.Packet);
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// Packs a dotted-quad into the 32-bit value the C++ sends as <c>sin_addr.s_addr</c> — i.e. the four
    /// network-order octets, which a little-endian <c>WriteUInt32</c> then emits back in order (the launcher
    /// feeds this straight to <c>inet_ntoa</c>). Endianness-safe via <see cref="BinaryPrimitives"/>.
    /// </summary>
    private static uint PackIp(string ip)
    {
        if (!IPAddress.TryParse(ip, out var addr)) return 0;
        Span<byte> bytes = stackalloc byte[4];
        if (!addr.TryWriteBytes(bytes, out int written) || written != 4) return 0; // IPv4 only, as the C++ SOCKADDR_IN
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes);
    }
}
