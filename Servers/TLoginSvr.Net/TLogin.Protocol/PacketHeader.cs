using System.Buffers.Binary;

namespace TLogin.Protocol;

/// <summary>
/// The fixed 16-byte little-endian packet header used by the 4Story TNetLib wire format.
/// Mirrors <c>struct _tagPACKETHEADER</c> in <c>Servers/TNetLib/Packet.h</c>:
/// <code>
/// offset 0  WORD  m_wSize     // total packet length including this header
/// offset 2  WORD  m_wID       // message id
/// offset 4  DWORD m_dwNumber  // per-direction sequence number
/// offset 8  INT64 m_llChkSUM  // body checksum
/// </code>
/// </summary>
public static class PacketHeader
{
    public const int Size = 16;
    public const int MaxPacketSize = 0xFFFF;

    public static ushort ReadSize(byte[] buf) => BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(0));
    public static void WriteSize(byte[] buf, ushort v) => BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(0), v);

    public static ushort ReadId(byte[] buf) => BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(2));
    public static void WriteId(byte[] buf, ushort v) => BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(2), v);

    public static uint ReadNumber(byte[] buf) => BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(4));
    public static void WriteNumber(byte[] buf, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(4), v);

    public static long ReadChecksum(byte[] buf) => BinaryPrimitives.ReadInt64LittleEndian(buf.AsSpan(8));
    public static void WriteChecksum(byte[] buf, long v) => BinaryPrimitives.WriteInt64LittleEndian(buf.AsSpan(8), v);
}
