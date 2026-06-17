using System.Buffers.Binary;

namespace TLogin.Protocol.Crypto;

/// <summary>
/// The XOR body/header obfuscation plus checksum layer, ported byte-for-byte from
/// <c>CPacket::Encrypt/Decrypt/EncryptHeader/DecryptHeader</c> in <c>Servers/TNetLib/Packet.cpp</c>.
/// </summary>
public static class PacketCrypto
{
    /// <summary>XOR-encrypts the body and stores the checksum of the plaintext into the header.</summary>
    public static void EncryptBody(byte[] buf, int wSize, long key)
    {
        int dataSize = wSize - PacketHeader.Size;
        if (dataSize <= 0)
        {
            PacketHeader.WriteChecksum(buf, 0);
            return;
        }

        int body = dataSize / 8;
        int left = dataSize % 8;
        long checksum = 0;

        int off = PacketHeader.Size;
        for (int i = 0; i < body; i++)
        {
            long block = BinaryPrimitives.ReadInt64LittleEndian(buf.AsSpan(off));
            checksum ^= block;                 // checksum over plaintext
            block ^= key;
            BinaryPrimitives.WriteInt64LittleEndian(buf.AsSpan(off), block);
            off += 8;
        }

        long crc = 0;
        for (int i = 0; i < left; i++)
        {
            byte plain = buf[off + i];
            checksum ^= plain;                 // (BYTE) widened
            buf[off + i] = (byte)(plain ^ KeyByte(key, i));
            crc = ((crc >> 4) & 0x0FFD) ^ key;
            checksum += crc;
        }

        PacketHeader.WriteChecksum(buf, checksum);
    }

    /// <summary>XOR-decrypts the body and verifies the checksum. Returns false on mismatch.</summary>
    public static bool DecryptBody(byte[] buf, int wSize, long key)
    {
        int dataSize = wSize - PacketHeader.Size;
        long expected = PacketHeader.ReadChecksum(buf);

        if (dataSize <= 0)
            return expected == 0;

        int body = dataSize / 8;
        int left = dataSize % 8;
        long checksum = 0;

        int off = PacketHeader.Size;
        for (int i = 0; i < body; i++)
        {
            long block = BinaryPrimitives.ReadInt64LittleEndian(buf.AsSpan(off));
            block ^= key;
            BinaryPrimitives.WriteInt64LittleEndian(buf.AsSpan(off), block);
            checksum ^= block;                 // checksum over recovered plaintext
            off += 8;
        }

        long crc = 0;
        for (int i = 0; i < left; i++)
        {
            byte plain = (byte)(buf[off + i] ^ KeyByte(key, i));
            buf[off + i] = plain;
            checksum ^= plain;
            crc = ((crc >> 4) & 0x0FFD) ^ key;
            checksum += crc;
        }

        return checksum == expected;
    }

    /// <summary>Obfuscates header bytes 2..15 (wID, dwNumber, llChkSUM); wSize is left in plaintext.</summary>
    public static void EncryptHeader(byte[] buf, long key)
    {
        ushort wSize = PacketHeader.ReadSize(buf);
        ushort wID = PacketHeader.ReadId(buf); // plaintext id captured before mutation
        for (int i = 2; i < PacketHeader.Size - 2; i++)
            buf[2 + i] ^= (byte)(key + wID + i);
        buf[2] ^= (byte)(key + wSize + 0);
        buf[3] ^= (byte)(key + wSize + 1);
    }

    public static void DecryptHeader(byte[] buf, long key)
    {
        ushort wSize = PacketHeader.ReadSize(buf);
        buf[2] ^= (byte)(key + wSize + 0);
        buf[3] ^= (byte)(key + wSize + 1);
        ushort wID = PacketHeader.ReadId(buf); // now recovered to plaintext
        for (int i = 2; i < PacketHeader.Size - 2; i++)
            buf[2 + i] ^= (byte)(key + wID + i);
    }

    // Little-endian byte i of the 64-bit key (mirrors (LPBYTE)&key on x86).
    private static byte KeyByte(long key, int i) => (byte)(key >> (8 * i));
}
