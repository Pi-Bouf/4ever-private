using System.Buffers.Binary;
using System.Text;

namespace TLogin.Protocol;

/// <summary>
/// Reads an incoming (already-decrypted) packet body, mirroring the C++ <c>CPacket::operator&gt;&gt;</c>
/// overloads. Reads past the end of the packet return zero / empty, matching the C++ behaviour where
/// <c>Read</c> zero-fills when <c>CanRead</c> is false.
/// </summary>
public sealed class PacketReader
{
    private static readonly Encoding DefaultEncoding = Encoding.Latin1;

    private readonly byte[] _buf;
    private readonly Encoding _encoding;
    private readonly int _end;
    private int _pos;

    public PacketReader(byte[] packet, Encoding? encoding = null)
    {
        _buf = packet;
        _encoding = encoding ?? DefaultEncoding;
        // wSize bounds the readable region but never exceeds the actual buffer.
        int size = packet.Length >= PacketHeader.Size ? PacketHeader.ReadSize(packet) : packet.Length;
        _end = Math.Min(size, packet.Length);
        _pos = PacketHeader.Size;
    }

    public ushort Id => PacketHeader.ReadId(_buf);
    public uint Number => PacketHeader.ReadNumber(_buf);
    public int Remaining => Math.Max(0, _end - _pos);

    private bool CanRead(int length) => _pos + length <= _end;

    public byte ReadByte()
    {
        if (!CanRead(1)) return 0;
        return _buf[_pos++];
    }

    public sbyte ReadSByte() => unchecked((sbyte)ReadByte());

    public ushort ReadUInt16()
    {
        if (!CanRead(2)) return 0;
        ushort v = BinaryPrimitives.ReadUInt16LittleEndian(_buf.AsSpan(_pos));
        _pos += 2;
        return v;
    }

    public short ReadInt16() => unchecked((short)ReadUInt16());

    public uint ReadUInt32()
    {
        if (!CanRead(4)) return 0;
        uint v = BinaryPrimitives.ReadUInt32LittleEndian(_buf.AsSpan(_pos));
        _pos += 4;
        return v;
    }

    public int ReadInt32() => unchecked((int)ReadUInt32());

    public long ReadInt64()
    {
        if (!CanRead(8)) return 0;
        long v = BinaryPrimitives.ReadInt64LittleEndian(_buf.AsSpan(_pos));
        _pos += 8;
        return v;
    }

    public float ReadFloat()
    {
        if (!CanRead(4)) return 0;
        float v = BinaryPrimitives.ReadSingleLittleEndian(_buf.AsSpan(_pos));
        _pos += 4;
        return v;
    }

    /// <summary>Reads a 4-byte length prefix and the following bytes as a string.</summary>
    public string ReadString()
    {
        int len = ReadInt32();
        if (len <= 0 || !CanRead(len)) return string.Empty;
        string s = _encoding.GetString(_buf, _pos, len);
        _pos += len;
        return s;
    }
}
