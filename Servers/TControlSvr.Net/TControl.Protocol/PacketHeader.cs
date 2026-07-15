using System.Buffers.Binary;

namespace TControl.Protocol;

/// <summary>
/// The fixed 16-byte little-endian TNetLib packet header (identical wire format to TWorld/TLogin),
/// mirroring <c>struct _tagPACKETHEADER</c>:
/// <code>
/// offset 0  WORD  m_wSize     // total packet length including this header
/// offset 2  WORD  m_wID       // message id
/// offset 4  DWORD m_dwNumber  // sequence number (0 on plaintext server traffic)
/// offset 8  INT64 m_llChkSUM  // body checksum (0 on plaintext server traffic)
/// </code>
/// TControl speaks the server↔server / control↔manager planes, which are <b>plaintext</b> (no RC4/XOR),
/// so dwNumber and llChkSUM stay zero and are never validated.
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
