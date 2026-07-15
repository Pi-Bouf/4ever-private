using System.Buffers.Binary;

namespace TPatch.Protocol;

/// <summary>
/// The fixed <b>8-byte</b> little-endian TNetLib packet header used by the patch plane, mirroring
/// <c>struct _tagPACKETHEADER</c> in <c>TPatchSvr/Packet.h</c> (and the identical copy in
/// <c>Tools/TLauncher/Packet.h</c>):
/// <code>
/// offset 0  WORD  m_wSize     // total packet length including this header
/// offset 2  WORD  m_wID       // message id
/// offset 4  DWORD m_dwChkSUM  // checksum — always written 0, never validated
/// </code>
/// <para>
/// ⚠️ This is deliberately <b>NOT</b> the 16-byte header TWorldSvr.Net uses. The patch link is the older,
/// simpler TNetLib framing: plaintext, no per-session cipher, no sequence number, an 8-byte header, and a
/// checksum field that both the server and the launcher leave zero.
/// </para>
/// </summary>
public static class PacketHeader
{
    public const int Size = 8;
    public const int MaxPacketSize = 0xFFFF;

    public static ushort ReadSize(byte[] buf) => BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(0));
    public static void WriteSize(byte[] buf, ushort v) => BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(0), v);

    public static ushort ReadId(byte[] buf) => BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(2));
    public static void WriteId(byte[] buf, ushort v) => BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(2), v);

    public static uint ReadChecksum(byte[] buf) => BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(4));
    public static void WriteChecksum(byte[] buf, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(4), v);
}
