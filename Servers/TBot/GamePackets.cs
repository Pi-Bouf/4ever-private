using TLogin.Protocol;

namespace TBot;

/// <summary>
/// The client↔server game-plane (CS_MAP) message ids and (de)serializers the bot needs once it is on
/// the map server. Ids are <c>CS_MAP (0x5280) + offset</c> from
/// <c>Lib/Own/TProtocol/include/CSProtocol.h</c>; layouts match the C++ TMapSvr handlers/senders.
/// </summary>
public static class GameMsg
{
    public const ushort CS_CONNECT_REQ  = Msg.CS_MAP + 0x0001; // → map server  (CSSender.cpp:31)
    public const ushort CS_CONNECT_ACK  = Msg.CS_MAP + 0x0002; // ← only on error (CSHandler.cpp:260)
    public const ushort CS_CHARINFO_ACK = Msg.CS_MAP + 0x0005; // ← full char + spawn (CSSender.cpp:121)
    public const ushort CS_ENTER_ACK    = Msg.CS_MAP + 0x0006; // ← a nearby player came into view
    public const ushort CS_CONREADY_REQ = Msg.CS_MAP + 0x0008; // → "ready" (CSHandler.cpp:402)
    public const ushort CS_MOVE_REQ     = Msg.CS_MAP + 0x0009; // → movement (CSSenderAll.cpp:69)
    public const ushort CS_MOVE_ACK     = Msg.CS_MAP + 0x000A; // ← movement broadcast
}

/// <summary>The spawn-relevant fields parsed out of CS_CHARINFO_ACK.</summary>
public sealed record CharSpawn(string Name, byte Level, ushort MapId, float X, float Y, float Z, ushort Dir);

public static class GamePackets
{
    // The anti-tamper key shared by the login + connect checksums (CSSender.cpp:42, CSHandler.cpp:285).
    private const long ChecksumKey = 0x336c3aebf71a8b08;

    /// <summary>
    /// Recomputes the CS_CONNECT_REQ checksum exactly as the client (CSSender.cpp:44) and map server
    /// (CSHandler.cpp:287) do: a 32-bit wrapping product, zero-extended to 64-bit, then a small mixing loop.
    /// </summary>
    public static long ComputeConnectChecksum(ushort version, uint userId, uint key, uint charId)
    {
        uint product = unchecked((uint)version * userId + key * charId); // 32-bit wrap, matches DWORD math
        long checksum = product;                                         // zero-extend (value is non-negative)
        long index = checksum % 8;                                       // % sizeof(INT64)
        long body = checksum / 8;
        for (long i = 0; i < index; i++)
        {
            checksum ^= body;
            checksum += ChecksumKey;
        }
        return checksum;
    }

    public static PacketWriter BuildConnect(ushort version, byte channel, uint userId, uint charId,
        uint key, uint clientIp, ushort clientPort)
    {
        long checksum = ComputeConnectChecksum(version, userId, key, charId);
        var w = new PacketWriter(GameMsg.CS_CONNECT_REQ);
        w.WriteUInt16(version);
        w.WriteByte(channel);
        w.WriteUInt32(userId);
        w.WriteUInt32(charId);
        w.WriteUInt32(key);
        w.WriteUInt32(clientIp);
        w.WriteUInt16(clientPort);
        w.WriteInt64(checksum);
        return w;
    }

    public static PacketWriter BuildConReady() => new(GameMsg.CS_CONREADY_REQ);

    public static PacketWriter BuildMove(ushort mapId, float x, float y, float z, ushort pitch, ushort dir,
        byte mouseDir, byte keyDir, byte action, byte ghost, float speed)
    {
        var w = new PacketWriter(GameMsg.CS_MOVE_REQ);
        w.WriteUInt16(mapId);
        w.WriteFloat(x);
        w.WriteFloat(y);
        w.WriteFloat(z);
        w.WriteUInt16(pitch);
        w.WriteUInt16(dir);
        w.WriteByte(mouseDir);
        w.WriteByte(keyDir);
        w.WriteByte(action);
        w.WriteByte(ghost);
        w.WriteFloat(speed);
        return w;
    }

    /// <summary>
    /// Extracts the spawn (name, level, map, position, dir) from a CS_CHARINFO_ACK packet.
    ///
    /// The deployed C++ map's CHARINFO stat block differs from the source layout by a few bytes, so rather
    /// than parse every field we read the reliable head (dwID, 3 secure bytes, titleID, name, then the
    /// 13-byte appearance + level) and then *scan* for the position block: m_wMapID(WORD) followed by
    /// posX/posY/posZ(float) and dir(WORD). The intervening stats are small integers whose little-endian
    /// bytes are tiny/denormal as floats, so the first offset where posX and posZ are both world-scale
    /// (&gt;1, finite) is unambiguously the position. This is robust to the exact stat-field count.
    /// </summary>
    public static CharSpawn ParseCharInfo(byte[] buf)
    {
        // Head: [16 header][4 dwID][3 secure][2 titleID][4 nameLen][name]
        const int nameLenOff = 16 + 4 + 3 + 2;
        int nameLen = BitConverter.ToInt32(buf, nameLenOff);
        int nameOff = nameLenOff + 4;
        string name = System.Text.Encoding.Latin1.GetString(buf, nameOff, Math.Clamp(nameLen, 0, buf.Length - nameOff));
        int afterName = nameOff + nameLen;
        byte level = afterName + 13 < buf.Length ? buf[afterName + 13] : (byte)0; // 13 appearance bytes, then level

        for (int o = afterName; o + 14 <= buf.Length; o++)
        {
            float x = BitConverter.ToSingle(buf, o);
            float z = BitConverter.ToSingle(buf, o + 8);
            if (!IsWorldCoord(x) || !IsWorldCoord(z)) continue;
            float y = BitConverter.ToSingle(buf, o + 4);
            ushort dir = BitConverter.ToUInt16(buf, o + 12);
            if (!float.IsFinite(y) || Math.Abs(y) > 100000f || dir > 3600) continue;
            ushort mapId = BitConverter.ToUInt16(buf, o - 2);
            return new CharSpawn(name, level, mapId, x, y, z, dir);
        }
        throw new InvalidOperationException("could not locate position block in CS_CHARINFO_ACK");
    }

    private static bool IsWorldCoord(float v) => float.IsFinite(v) && v > 1f && v < 200000f;
}
