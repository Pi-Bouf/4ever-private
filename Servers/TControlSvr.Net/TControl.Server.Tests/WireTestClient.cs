using System.Net.Sockets;
using TControl.Protocol;

namespace TControl.Server.Tests;

/// <summary>A minimal TCP client speaking the plaintext 16-byte-framed control wire, for tests.</summary>
internal sealed class WireTestClient : IDisposable
{
    private readonly TcpClient _tcp;
    private readonly NetworkStream _stream;

    public WireTestClient(string host, int port)
    {
        _tcp = new TcpClient();
        _tcp.Connect(host, port);
        _stream = _tcp.GetStream();
    }

    public void Send(PacketWriter w) => Send(w.ToArray());
    public void Send(byte[] packet) => _stream.Write(packet, 0, packet.Length);

    /// <summary>Read exactly one framed packet (blocking, with the stream's read timeout).</summary>
    public byte[] Receive(TimeSpan timeout)
    {
        _stream.ReadTimeout = (int)timeout.TotalMilliseconds;
        var header = ReadExactly(PacketHeader.Size);
        int size = PacketHeader.ReadSize(header);
        if (size < PacketHeader.Size) throw new InvalidDataException($"bad size {size}");
        var packet = new byte[size];
        Array.Copy(header, packet, PacketHeader.Size);
        if (size > PacketHeader.Size)
        {
            var body = ReadExactly(size - PacketHeader.Size);
            Array.Copy(body, 0, packet, PacketHeader.Size, body.Length);
        }
        return packet;
    }

    /// <summary>Read packets until one with <paramref name="id"/> arrives (discarding others), or throw on timeout.</summary>
    public byte[] ReceiveUntil(ushort id, TimeSpan timeout)
    {
        var deadline = timeout;
        while (true)
        {
            var p = Receive(deadline);
            if (PacketHeader.ReadId(p) == id) return p;
        }
    }

    private byte[] ReadExactly(int count)
    {
        var buf = new byte[count];
        int off = 0;
        while (off < count)
        {
            int n = _stream.Read(buf, off, count - off);
            if (n == 0) throw new IOException("peer closed");
            off += n;
        }
        return buf;
    }

    public void Dispose()
    {
        _stream.Dispose();
        _tcp.Dispose();
    }
}
