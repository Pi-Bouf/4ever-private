using System.Net.Sockets;
using TLogin.Protocol;

namespace TBot;

/// <summary>
/// A TCP connection that speaks the 4Story wire protocol the way the real game client does, against
/// both the login server and the (C++) map server — the same client cipher is used for both because
/// TMapSvr also marks client sessions <c>SESSION_CLIENT</c> + <c>m_bUseCrypt</c> (TMapSvr.cpp:896).
///
/// Send: XOR body + header, then RC4 over the whole buffer (RC4 outermost), wSize kept plaintext.
/// Recv: XOR only — the server applies no RC4 outbound. Mirrors TLogin's TcpTestClient.
/// </summary>
public sealed class BotConnection : IDisposable
{
    private readonly TcpClient _tcp;
    private readonly NetworkStream _stream;
    private readonly ClientCipher _cipher;

    /// <summary>Raw bytes of the most recently received (decrypted) packet — for diagnostics.</summary>
    public byte[]? LastPacket { get; private set; }

    /// <summary>Packets whose checksum did not verify after decoding — a server sending corrupt data.</summary>
    public int BadPackets { get; private set; }

    public BotConnection(string host, int port, bool useCrypt = true)
    {
        _cipher = new ClientCipher { UseCrypt = useCrypt };
        _tcp = new TcpClient();
        _tcp.Connect(host, port);
        _tcp.NoDelay = true;
        _stream = _tcp.GetStream();
    }

    /// <summary>The local endpoint's IPv4 address as the little-endian uint inet_addr would produce.</summary>
    public uint LocalIpAsUInt()
    {
        if (_tcp.Client.LocalEndPoint is System.Net.IPEndPoint ep)
        {
            byte[] b = ep.Address.MapToIPv4().GetAddressBytes(); // network order: b[0] is first octet
            return (uint)(b[0] | (b[1] << 8) | (b[2] << 16) | (b[3] << 24));
        }
        return 0;
    }

    public void Send(PacketWriter w)
    {
        byte[] buf = w.ToArray();
        _cipher.EncodeToServer(buf);
        _stream.Write(buf, 0, buf.Length);
        _stream.Flush();
    }

    /// <summary>Reads the next packet, decrypts it (XOR only), and returns a reader. Throws on timeout.</summary>
    public PacketReader Receive(TimeSpan timeout)
    {
        var r = TryReceive(timeout);
        if (r is null) throw new TimeoutException("No packet received within the timeout.");
        return r;
    }

    /// <summary>Like <see cref="Receive"/> but returns null on a read timeout instead of throwing.</summary>
    /// <remarks>Bytes are accumulated in a buffer, and the wait is a <see cref="Socket.Poll(int, SelectMode)"/> before
    /// each read rather than a socket receive timeout. With a timeout, one that landed mid-packet (a header read but not
    /// its body, or half a header — common when polling every 1 ms under load) threw (which looked like a server kick) or
    /// lost the bytes (which desynced the stream), and on Windows a timed-out blocking read occasionally failed with
    /// IOPending instead of TimedOut.</remarks>
    public PacketReader? TryReceive(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            if (_len >= PacketHeader.Size)
            {
                int wSize = PacketHeader.ReadSize(_buf);
                if (_len >= wSize)
                {
                    var packet = _buf.AsSpan(0, wSize).ToArray();
                    Buffer.BlockCopy(_buf, wSize, _buf, 0, _len - wSize);
                    _len -= wSize;
                    if (!_cipher.DecodeFromServer(packet)) BadPackets++;
                    LastPacket = packet;
                    return new PacketReader(packet);
                }
                if (wSize > _buf.Length) Array.Resize(ref _buf, wSize);
            }

            // Readable = data waiting, or the peer closed (then Read returns 0). Partial data stays buffered on timeout.
            var remaining = deadline - DateTime.UtcNow;
            int waitUs = (int)Math.Clamp(remaining.TotalMicroseconds, 0, int.MaxValue);
            if (!_tcp.Client.Poll(waitUs, SelectMode.SelectRead)) return null;
            int n = _stream.Read(_buf, _len, _buf.Length - _len);
            if (n == 0) throw new IOException("Connection closed by server.");
            _len += n;
        }
    }

    private byte[] _buf = new byte[16 * 1024];
    private int _len;

    public void Dispose()
    {
        _stream.Dispose();
        _tcp.Dispose();
    }
}
