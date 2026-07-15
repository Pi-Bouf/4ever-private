namespace TPatch.Protocol;

/// <summary>
/// Reassembles whole packets from a TCP byte stream, framed by the leading little-endian <c>wSize</c>
/// field. Throws <see cref="InvalidDataException"/> on an out-of-range size. Identical in spirit to the
/// C++ <c>CSession::CheckMessage</c> loop.
/// </summary>
public sealed class PacketFramer
{
    private byte[] _buf = new byte[8192];
    private int _len;

    public int Buffered => _len;

    public void Append(ReadOnlySpan<byte> data)
    {
        if (_len + data.Length > _buf.Length)
        {
            int newSize = _buf.Length * 2;
            while (newSize < _len + data.Length) newSize *= 2;
            Array.Resize(ref _buf, newSize);
        }
        data.CopyTo(_buf.AsSpan(_len));
        _len += data.Length;
    }

    public bool TryReadPacket(out byte[] packet)
    {
        packet = Array.Empty<byte>();
        if (_len < PacketHeader.Size) return false;

        int size = PacketHeader.ReadSize(_buf);
        if (size < PacketHeader.Size || size >= PacketHeader.MaxPacketSize)
            throw new InvalidDataException($"Invalid packet size {size}.");

        if (_len < size) return false;

        packet = new byte[size];
        Array.Copy(_buf, packet, size);

        _len -= size;
        if (_len > 0)
            Array.Copy(_buf, size, _buf, 0, _len);

        return true;
    }
}
