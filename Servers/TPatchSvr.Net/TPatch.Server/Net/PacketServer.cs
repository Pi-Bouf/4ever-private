using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace TPatch.Server.Net;

/// <summary>
/// Async TCP acceptor for inbound patch clients (the launcher) and the control server. Replaces the C++
/// IOCP accept thread. Each accepted socket becomes a <see cref="PacketConnection"/> handled on its own
/// task; framed packets are handed to <c>onPacket</c> for enqueue onto the single batch task.
/// </summary>
public sealed class PacketServer
{
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<PacketConnection, byte> _connections = new();

    public PacketServer(ILogger logger) => _logger = logger;

    public int ConnectionCount => _connections.Count;

    public async Task ListenAsync(
        int port,
        Func<PacketConnection, byte[], ValueTask> onPacket,
        Func<PacketConnection, ValueTask>? onDisconnected,
        CancellationToken token)
    {
        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Any, port));
        listener.Listen(backlog: 64);
        _logger.LogInformation("Patch server listening on port {Port}.", port);

        while (!token.IsCancellationRequested)
        {
            Socket socket;
            try
            {
                socket = await listener.AcceptAsync(token);
            }
            catch (OperationCanceledException) { break; }
            catch (SocketException ex)
            {
                _logger.LogWarning(ex, "Accept failed.");
                continue;
            }

            _ = HandleConnectionAsync(socket, onPacket, onDisconnected, token);
        }
    }

    private async Task HandleConnectionAsync(
        Socket socket,
        Func<PacketConnection, byte[], ValueTask> onPacket,
        Func<PacketConnection, ValueTask>? onDisconnected,
        CancellationToken token)
    {
        var conn = new PacketConnection(socket, _logger);
        _connections.TryAdd(conn, 0);
        _logger.LogDebug("Client connected: {Endpoint}", conn.RemoteEndPoint);

        try
        {
            await conn.RunAsync(onPacket, token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Connection {Endpoint} faulted.", conn.RemoteEndPoint);
        }
        finally
        {
            _connections.TryRemove(conn, out _);
            if (onDisconnected is not null)
            {
                try { await onDisconnected(conn); } catch { }
            }
            await conn.DisposeAsync();
            _logger.LogDebug("Client disconnected: {Endpoint}", conn.RemoteEndPoint);
        }
    }
}
