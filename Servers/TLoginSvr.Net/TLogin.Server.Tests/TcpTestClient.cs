using System.Net.Sockets;
using TLogin.Protocol;
using TLogin.Protocol.Crypto;

namespace TLogin.Server.Tests;

/// <summary>
/// A minimal TCP client that speaks the wire protocol the way the real game client does, for
/// end-to-end tests: RC4-outermost on send, XOR-only on receive, with wSize kept plaintext.
/// </summary>
public sealed class TcpTestClient : IDisposable
{
    private readonly TcpClient _tcp;
    private readonly NetworkStream _stream;
    private uint _sendNumber;
    private uint _recvNumber;

    public TcpTestClient(string host, int port)
    {
        _tcp = new TcpClient();
        _tcp.Connect(host, port);
        _stream = _tcp.GetStream();
    }

    public void Send(PacketWriter w)
    {
        byte[] buf = w.ToArray();
        _sendNumber++;
        int wSize = PacketHeader.ReadSize(buf);
        long key = ProtocolKeys.XorKeys[_sendNumber % ProtocolKeys.KeyCount];
        PacketHeader.WriteNumber(buf, _sendNumber);
        PacketCrypto.EncryptBody(buf, wSize, key);
        PacketCrypto.EncryptHeader(buf, key);
        Rc4.Apply(ProtocolKeys.Rc4Key, buf, wSize);
        PacketHeader.WriteSize(buf, (ushort)wSize);
        _stream.Write(buf, 0, buf.Length);
        _stream.Flush();
    }

    public PacketReader Receive(TimeSpan timeout)
    {
        _tcp.ReceiveTimeout = (int)timeout.TotalMilliseconds;
        byte[] header = ReadExactly(PacketHeader.Size);
        int wSize = PacketHeader.ReadSize(header);
        byte[] packet = new byte[wSize];
        Array.Copy(header, packet, PacketHeader.Size);
        if (wSize > PacketHeader.Size)
        {
            byte[] body = ReadExactly(wSize - PacketHeader.Size);
            Array.Copy(body, 0, packet, PacketHeader.Size, body.Length);
        }

        _recvNumber++;
        long key = ProtocolKeys.XorKeys[_recvNumber % ProtocolKeys.KeyCount];
        PacketCrypto.DecryptHeader(packet, key);
        PacketCrypto.DecryptBody(packet, wSize, key);
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
