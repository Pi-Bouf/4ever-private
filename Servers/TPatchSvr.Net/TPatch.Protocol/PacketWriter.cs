using System.Buffers.Binary;
using System.Text;

namespace TPatch.Protocol;

/// <summary>
/// Builds an outgoing packet body, mirroring the C++ <c>CPacket::operator&lt;&lt;</c> overloads.
/// Strings are length-prefixed with a 4-byte little-endian int and carry no NUL terminator; all
/// scalars are little-endian. The 8-byte header is reserved up front; <see cref="ToArray"/> finalizes
/// <c>wSize</c>. The checksum stays zero — the patch link is plaintext (see <see cref="PacketHeader"/>).
/// </summary>
public sealed class PacketWriter
{
    private static readonly Encoding DefaultEncoding = Encoding.Latin1;

    private readonly Encoding _encoding;
    private byte[] _buf;
    private int _len;

    public PacketWriter(ushort id, Encoding? encoding = null, int capacity = 256)
    {
        _encoding = encoding ?? DefaultEncoding;
        _buf = new byte[Math.Max(PacketHeader.Size, capacity)];
        _len = PacketHeader.Size;
        PacketHeader.WriteId(_buf, id);
    }

    private void Ensure(int extra)
    {
        if (_len + extra <= _buf.Length) return;
        int newSize = _buf.Length * 2;
        while (newSize < _len + extra) newSize *= 2;
        Array.Resize(ref _buf, newSize);
    }

    public PacketWriter WriteByte(byte v)
    {
        Ensure(1);
        _buf[_len++] = v;
        return this;
    }

    public PacketWriter WriteBool(bool v) => WriteByte(v ? (byte)1 : (byte)0);

    public PacketWriter WriteUInt16(ushort v)
    {
        Ensure(2);
        BinaryPrimitives.WriteUInt16LittleEndian(_buf.AsSpan(_len), v);
        _len += 2;
        return this;
    }

    public PacketWriter WriteInt16(short v) => WriteUInt16(unchecked((ushort)v));

    public PacketWriter WriteUInt32(uint v)
    {
        Ensure(4);
        BinaryPrimitives.WriteUInt32LittleEndian(_buf.AsSpan(_len), v);
        _len += 4;
        return this;
    }

    public PacketWriter WriteInt32(int v) => WriteUInt32(unchecked((uint)v));

    public PacketWriter WriteInt64(long v)
    {
        Ensure(8);
        BinaryPrimitives.WriteInt64LittleEndian(_buf.AsSpan(_len), v);
        _len += 8;
        return this;
    }

    public PacketWriter WriteUInt64(ulong v) => WriteInt64(unchecked((long)v));

    /// <summary>Writes a 4-byte length prefix followed by the (NUL-free) string bytes.</summary>
    public PacketWriter WriteString(string? s)
    {
        s ??= string.Empty;
        byte[] bytes = _encoding.GetBytes(s);
        WriteInt32(bytes.Length);
        Ensure(bytes.Length);
        Array.Copy(bytes, 0, _buf, _len, bytes.Length);
        _len += bytes.Length;
        return this;
    }

    /// <summary>Finalizes the header <c>wSize</c> field and returns a right-sized copy of the packet.</summary>
    public byte[] ToArray()
    {
        var result = new byte[_len];
        Array.Copy(_buf, result, _len);
        PacketHeader.WriteSize(result, (ushort)_len);
        return result;
    }
}
