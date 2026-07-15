using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using TMap.Protocol;

namespace TMap.Server.Net;

/// <summary>
/// The single outbound connection from this map server to the World server (the MW/SM/DM planes).
/// Server↔server traffic is <b>plaintext</b> — no cipher. Mirrors the C++ <c>m_world</c>
/// (a <c>CTMapSession</c>) that <c>InitNetwork</c> connects to <c>WorldIP:WorldPort</c> and immediately
/// sends <c>MW_CONNECT_ACK</c> on. Reconnects with backoff if the link drops.
/// </summary>
public sealed class WorldLink : IWorldSink
{
    private readonly string _host;
    private readonly int _port;
    private readonly ILogger _logger;
    private readonly Channel<byte[]> _sendQueue =
        Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });

    private volatile Socket? _socket;

    public WorldLink(string host, int port, ILogger logger)
    {
        _host = host;
        _port = port;
        _logger = logger;
    }

    public bool IsConnected => _socket is { Connected: true };

    public void Send(byte[] packet) => _sendQueue.Writer.TryWrite(packet);
    public void Send(PacketWriter writer) => Send(writer.ToArray());

    /// <summary>
    /// Runs the connect/receive/reconnect loop until cancellation. <paramref name="onConnected"/> fires
    /// after each (re)connect (send MW_CONNECT_ACK there); <paramref name="onPacket"/> receives each framed
    /// plaintext packet; <paramref name="onDisconnected"/> fires when a live link drops.
    /// </summary>
    public async Task RunAsync(
        Func<ValueTask> onConnected,
        Func<byte[], ValueTask> onPacket,
        Func<ValueTask> onDisconnected,
        CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            Socket socket;
            try
            {
                socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                await socket.ConnectAsync(_host, _port, token);
            }
            catch (OperationCanceledException) { break; }
            catch (SocketException ex)
            {
                _logger.LogWarning("World link connect to {Host}:{Port} failed ({Msg}); retrying in 3s.", _host, _port, ex.Message);
                try { await Task.Delay(TimeSpan.FromSeconds(3), token); } catch { break; }
                continue;
            }

            _socket = socket;
            _logger.LogInformation("World link connected to {Host}:{Port}.", _host, _port);

            using var sendCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            var sendLoop = SendLoopAsync(socket, sendCts.Token);

            try
            {
                await onConnected();
                await ReceiveLoopAsync(socket, onPacket, token);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "World link receive faulted.");
            }
            finally
            {
                _socket = null;
                sendCts.Cancel();
                try { await sendLoop; } catch { /* ignore */ }
                try { socket.Shutdown(SocketShutdown.Both); } catch { /* already closed */ }
                socket.Dispose();
                try { await onDisconnected(); } catch { /* ignore */ }
            }

            if (token.IsCancellationRequested) break;
            _logger.LogWarning("World link dropped; reconnecting in 3s.");
            try { await Task.Delay(TimeSpan.FromSeconds(3), token); } catch { break; }
        }
    }

    private async Task ReceiveLoopAsync(Socket socket, Func<byte[], ValueTask> onPacket, CancellationToken token)
    {
        var framer = new PacketFramer();
        var buffer = new byte[64 * 1024];
        while (!token.IsCancellationRequested)
        {
            int n;
            try
            {
                n = await socket.ReceiveAsync(buffer, SocketFlags.None, token);
            }
            catch (OperationCanceledException) { break; }
            catch (SocketException) { break; }
            catch (ObjectDisposedException) { break; }

            if (n == 0) break; // world closed

            framer.Append(buffer.AsSpan(0, n));
            try
            {
                while (framer.TryReadPacket(out var packet))
                    await onPacket(packet);
            }
            catch (InvalidDataException ex)
            {
                _logger.LogWarning(ex, "World link: malformed framing; dropping link.");
                return;
            }
        }
    }

    private async Task SendLoopAsync(Socket socket, CancellationToken token)
    {
        try
        {
            await foreach (var packet in _sendQueue.Reader.ReadAllAsync(token))
            {
                int sent = 0;
                while (sent < packet.Length)
                    sent += await socket.SendAsync(packet.AsMemory(sent), SocketFlags.None, token);
            }
        }
        catch (OperationCanceledException) { /* reconnecting / shutting down */ }
        catch (SocketException) { /* world gone */ }
        catch (ObjectDisposedException) { /* socket disposed */ }
    }
}
