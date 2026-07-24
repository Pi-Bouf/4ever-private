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
    public PacketReader? TryReceive(TimeSpan timeout)
    {
        _tcp.ReceiveTimeout = Math.Max(1, (int)timeout.TotalMilliseconds);
        byte[]? header;
        try
        {
            header = ReadExactly(PacketHeader.Size);
        }
        catch (IOException ex) when (ex.InnerException is SocketException { SocketErrorCode: SocketError.TimedOut })
        {
            return null;
        }

        int wSize = PacketHeader.ReadSize(header);
        var packet = new byte[wSize];
        Array.Copy(header, packet, PacketHeader.Size);
        if (wSize > PacketHeader.Size)
            Array.Copy(ReadExactly(wSize - PacketHeader.Size), 0, packet, PacketHeader.Size, wSize - PacketHeader.Size);

        _cipher.DecodeFromServer(packet);
        LastPacket = packet;
        return new PacketReader(packet);
    }

    private byte[] ReadExactly(int count)
    {
        var buf = new byte[count];
        int read = 0;
        while (read < count)
        {
            int n = _stream.Read(buf, read, count - read);
            if (n == 0) throw new IOException("Connection closed by server.");
            read += n;
        }
        return buf;
    }

    public void Dispose()
    {
        _stream.Dispose();
        _tcp.Dispose();
    }
}
