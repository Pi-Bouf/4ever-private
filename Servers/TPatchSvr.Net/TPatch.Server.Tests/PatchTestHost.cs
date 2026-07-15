using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using TPatch.Data;
using TPatch.Server.Net;

namespace TPatch.Server.Tests;

/// <summary>
/// Spins up a real <see cref="PacketServer"/> + <see cref="PatchService"/> + batch task on an ephemeral
/// port for end-to-end tests, over an injected <see cref="IPatchSource"/>. No DB, no sockets faked — the
/// exact production net path (same pattern as TWorldSvr.Net's WorldTestHost).
/// </summary>
public sealed class PatchTestHost : IAsyncDisposable
{
    private readonly CancellationTokenSource _cts = new(TimeSpan.FromSeconds(20));
    private readonly Task _listen;
    private readonly Task _batch;

    public PatchService Service { get; }
    public int Port { get; }

    // 127.0.0.1 packed as inet_addr, and a fixed login port, so tests can assert exact reply bytes.
    public const uint LoginIp = 0x0100007F;
    public const ushort LoginPort = 4815;

    public PatchTestHost(IPatchSource source, PatchServerOptions? options = null)
    {
        var opt = options ?? new PatchServerOptions { FtpUrl = "http://test/patch", PreFtpUrl = "http://test/pre" };
        Service = new PatchService(source, opt, LoginIp, LoginPort, NullLogger<PatchService>.Instance);
        var server = new PacketServer(NullLogger<PacketServer>.Instance);
        var batch = Channel.CreateUnbounded<(PacketConnection Conn, byte[]? Packet)>(new UnboundedChannelOptions { SingleReader = true });

        Port = FreeTcpPort();

        _batch = Task.Run(async () =>
        {
            try
            {
                await foreach (var item in batch.Reader.ReadAllAsync(_cts.Token))
                {
                    if (item.Packet is null) Service.OnDisconnect(item.Conn);
                    else await Service.DispatchAsync(item.Conn, item.Packet);
                }
            }
            catch (OperationCanceledException) { }
        });

        _listen = server.ListenAsync(
            Port,
            onPacket: (conn, data) => { batch.Writer.TryWrite((conn, data)); return ValueTask.CompletedTask; },
            onDisconnected: conn => { batch.Writer.TryWrite((conn, null)); return ValueTask.CompletedTask; },
            token: _cts.Token);
    }

    public async Task<TcpTestClient> ConnectAsync()
    {
        for (int attempt = 0; attempt < 50; attempt++)
        {
            try { return new TcpTestClient("127.0.0.1", Port); }
            catch (SocketException) { await Task.Delay(50, _cts.Token); }
        }
        throw new TimeoutException("Server did not start listening.");
    }

    private static int FreeTcpPort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try { await Task.WhenAll(_listen, _batch); } catch { }
        _cts.Dispose();
    }
}
