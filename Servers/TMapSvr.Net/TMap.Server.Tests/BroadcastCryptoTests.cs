using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using TMap.Protocol;
using TMap.Protocol.Crypto;
using TMap.Server.Net;
using Xunit;

namespace TMap.Server.Tests;

/// <summary>
/// Found by two TBot clients side by side: each missed its own copy of every notice also sent to a neighbour, and
/// received packets with garbage ids instead. A broadcast hands one array to every viewer's connection, and each
/// connection's send loop encrypts in place with its own sequence number — so all copies but one went out
/// encrypted twice. One real client never sees it.
/// </summary>
public class BroadcastCryptoTests
{
    [Fact]
    public async Task OneArraySentToTwoConnections_ArrivesIntactOnBoth()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var clients = new List<TcpClient>();
        var conns = new List<ClientConnection>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        for (int i = 0; i < 2; i++)
        {
            var tc = new TcpClient();
            await tc.ConnectAsync(IPAddress.Loopback, port);
            var conn = new ClientConnection(await listener.AcceptSocketAsync(), NullLogger.Instance);
            _ = conn.RunAsync((_, _) => ValueTask.CompletedTask, cts.Token);
            clients.Add(tc); conns.Add(conn);
        }

        var w = new PacketWriter(Msg.CS_PARTYATTR_ACK);
        w.WriteUInt32(1029); w.WriteUInt16(0x104); w.WriteUInt32(1030); w.WriteUInt16(0);
        byte[] shared = w.ToArray();
        byte[] original = (byte[])shared.Clone();

        foreach (var c in conns) c.Send(shared);

        foreach (var tc in clients)
        {
            var got = await ReadOne(tc.GetStream(), original.Length, cts.Token);
            long key = ProtocolKeys.XorKeys[1 % ProtocolKeys.KeyCount];   // the client's first packet: number 1
            PacketCrypto.DecryptHeader(got, key);
            Assert.True(PacketCrypto.DecryptBody(got, got.Length, key));
            Assert.Equal(Msg.CS_PARTYATTR_ACK, PacketHeader.ReadId(got));
            Assert.Equal(original.AsSpan(PacketHeader.Size).ToArray(), got.AsSpan(PacketHeader.Size).ToArray());
        }
        Assert.Equal(original, shared);   // the caller's array is left alone

        foreach (var c in conns) await c.DisposeAsync();
        foreach (var tc in clients) tc.Dispose();
    }

    private static async Task<byte[]> ReadOne(NetworkStream s, int size, CancellationToken ct)
    {
        var buf = new byte[size];
        int read = 0;
        while (read < size)
        {
            int n = await s.ReadAsync(buf.AsMemory(read), ct);
            if (n == 0) throw new IOException("closed");
            read += n;
        }
        return buf;
    }
}
