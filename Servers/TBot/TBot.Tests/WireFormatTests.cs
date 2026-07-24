using TBot;
using TLogin.Protocol;
using Xunit;

namespace TBot.Tests;

/// <summary>
/// Server-free wire-format checks for the bot's game-plane (CS_MAP) code. They pair the bot's
/// <see cref="ClientCipher"/> with the repo's server <see cref="SessionCipher"/> so that "the bot
/// encodes what the C++ map server would accept, and decodes what it would send" is proven without a
/// live server. The cipher itself is already covered by TLogin.Protocol.Tests.CryptoTests.
/// </summary>
public class WireFormatTests
{
    [Fact]
    public void Move_Encoded_ByClient_Decodes_OnServer()
    {
        var client = new ClientCipher();
        var server = new SessionCipher(); // server's inbound = DecryptInbound

        byte[] packet = GamePackets
            .BuildMove(mapId: 7, x: 100.5f, y: 0f, z: 200.25f, pitch: 0, dir: 1234,
                       mouseDir: 4, keyDir: 4, action: 0, ghost: 0, speed: 3.0f)
            .ToArray();

        client.EncodeToServer(packet);
        Assert.True(server.DecryptInbound(packet), "server failed to decrypt the bot's CS_MOVE_REQ");

        var r = new PacketReader(packet);
        Assert.Equal(GameMsg.CS_MOVE_REQ, r.Id);
        Assert.Equal((ushort)7, r.ReadUInt16());      // mapId
        Assert.Equal(100.5f, r.ReadFloat());          // x
        Assert.Equal(0f, r.ReadFloat());              // y
        Assert.Equal(200.25f, r.ReadFloat());         // z
        Assert.Equal((ushort)0, r.ReadUInt16());      // pitch
        Assert.Equal((ushort)1234, r.ReadUInt16());   // dir
        Assert.Equal(4, r.ReadByte());                // mouseDir
        Assert.Equal(4, r.ReadByte());                // keyDir
        Assert.Equal(0, r.ReadByte());                // action
        Assert.Equal(0, r.ReadByte());                // ghost
        Assert.Equal(3.0f, r.ReadFloat());            // speed
    }

    [Fact]
    public void CharInfo_FromServer_ParsesSpawn()
    {
        // Build a CS_CHARINFO_ACK exactly as CTPlayer::SendCS_CHARINFO_ACK does, up to a few bytes past
        // the spawn block (a stand-in for the inventory/skill tail), then push it through the server's
        // outbound cipher and parse it with the bot's reader.
        var w = new PacketWriter(GameMsg.CS_CHARINFO_ACK);
        w.WriteUInt32(42);          // m_dwID
        w.WriteByte(0);             // bSecureCreated
        w.WriteByte(0);             // m_bSecureCurUnlocked
        w.WriteByte(0);             // bSecureDisabled
        w.WriteUInt16(0);           // m_wTitleID
        w.WriteString("Pittt");     // m_strNAME
        w.WriteByte(0);             // bStartAct
        w.WriteByte(2);             // class
        w.WriteByte(0);             // race
        w.WriteByte(1);             // country
        w.WriteByte(0);             // aidCountry
        w.WriteByte(1);             // sex
        w.WriteByte(0);             // hair
        w.WriteByte(0);             // face
        w.WriteByte(0);             // body
        w.WriteByte(0);             // pants
        w.WriteByte(0);             // hand
        w.WriteByte(0);             // foot
        w.WriteByte(0);             // helmetHide
        w.WriteByte(19);            // level
        w.WriteUInt32(0);           // partyId
        w.WriteUInt32(0);           // guildId
        w.WriteUInt32(0);           // fame
        w.WriteUInt32(0);           // fameColor
        w.WriteByte(0);             // guildDuty
        w.WriteByte(0);             // guildPeer
        w.WriteString("");          // guildName
        w.WriteUInt32(0);           // tacticsId
        w.WriteString("");          // tacticsName
        w.WriteUInt32(0);           // gold
        w.WriteUInt32(0);           // silver
        w.WriteUInt32(0);           // cooper
        w.WriteUInt32(0);           // prevExp
        w.WriteUInt32(0);           // nextExp
        w.WriteUInt32(0);           // exp
        w.WriteUInt32(500);         // maxHP
        w.WriteUInt32(500);         // HP
        w.WriteUInt32(200);         // maxMP
        w.WriteUInt32(200);         // MP
        w.WriteUInt32(0);           // partyChief
        w.WriteUInt32(0);           // commander
        w.WriteUInt32(0);           // region
        w.WriteUInt16(13);          // wMapID
        w.WriteFloat(123.5f);       // x
        w.WriteFloat(1f);           // y
        w.WriteFloat(456.75f);      // z
        w.WriteUInt16(900);         // dir
        w.WriteByte(0);             // (start of the tail we don't parse)
        byte[] packet = w.ToArray();

        var server = new SessionCipher();
        server.EncryptOutbound(packet, packet.Length); // server→client: XOR only, no RC4

        var client = new ClientCipher();
        Assert.True(client.DecodeFromServer(packet), "client failed to decode CS_CHARINFO_ACK");

        var r = new PacketReader(packet);
        Assert.Equal(GameMsg.CS_CHARINFO_ACK, r.Id);
        var spawn = GamePackets.ParseCharInfo(r);

        Assert.Equal("Pittt", spawn.Name);
        Assert.Equal(19, spawn.Level);
        Assert.Equal((ushort)13, spawn.MapId);
        Assert.Equal(123.5f, spawn.X);
        Assert.Equal(1f, spawn.Y);
        Assert.Equal(456.75f, spawn.Z);
        Assert.Equal((ushort)900, spawn.Dir);
    }

    [Fact]
    public void ConnectChecksum_MatchesIndependentComputation()
    {
        const ushort version = 0x2918;
        const uint userId = 1000;
        const uint key = 0x12345679; // odd, so the mixing loop actually runs
        const uint charId = 7;

        long actual = GamePackets.ComputeConnectChecksum(version, userId, key, charId);

        // Independent re-derivation of CSSender.cpp:44-53 / CSHandler.cpp:287-296.
        long expected = ReferenceConnectChecksum(version, userId, key, charId);
        Assert.Equal(expected, actual);

        // Deterministic.
        Assert.Equal(actual, GamePackets.ComputeConnectChecksum(version, userId, key, charId));
    }

    private static long ReferenceConnectChecksum(ushort version, uint userId, uint key, uint charId)
    {
        unchecked
        {
            uint product = (uint)version * userId + key * charId; // DWORD wrap on x86
            long checksum = product;
            long index = checksum % 8;
            long body = checksum / 8;
            const long mixKey = 0x336c3aebf71a8b08;
            for (long i = 0; i < index; i++)
            {
                checksum ^= body;
                checksum += mixKey;
            }
            return checksum;
        }
    }
}
