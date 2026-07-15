using TMap.Protocol;
using TMap.Server.Map;
using Xunit;

namespace TMap.Server.Tests;

public class EnterHandshakeTests
{
    [Fact]
    public async Task WorldConnect_SendsConnectAck_WithServerIdAndChannels()
    {
        var h = new MapTestHarness();
        await h.Service.OnWorldConnectedAsync();

        Assert.True(h.Service.WorldReady);
        var pkt = h.World.Last(Msg.MW_CONNECT_ACK);
        Assert.NotNull(pkt);
        var r = new PacketReader(pkt!);
        Assert.Equal(Proto.MakeServerId(1, SvrType.Map), r.ReadUInt16());
        Assert.Equal((byte)1, r.ReadByte());   // channel count
        Assert.Equal((byte)1, r.ReadByte());   // channel id
    }

    [Fact]
    public async Task Connect_AnnouncesToWorld_WithAddChar()
    {
        var h = new MapTestHarness();
        var client = new FakeClientChannel();
        var s = new ClientSession(client);

        await h.Service.DispatchClientAsync(s, MapTestHarness.ConnectReq(charId: 7, userId: 3, key: 0xABCD,
            channel: 1, ip: 0x0100007F, port: 5000));

        Assert.Equal(EnterState.Entering, s.State);
        Assert.Equal(7u, s.CharId);
        Assert.Same(s, h.Service.State.FindByChar(7));

        var pkt = h.World.Last(Msg.MW_ADDCHAR_ACK);
        Assert.NotNull(pkt);
        var r = new PacketReader(pkt!);
        Assert.Equal(7u, r.ReadUInt32());        // charId
        Assert.Equal(0xABCDu, r.ReadUInt32());   // key
        Assert.Equal(0x0100007Fu, r.ReadUInt32());// ip
        Assert.Equal((ushort)5000, r.ReadUInt16());// port
        Assert.Equal(3u, r.ReadUInt32());        // userId
    }

    [Fact]
    public async Task Connect_BadVersion_RejectsWithInvalidVer_NoAddChar()
    {
        var h = new MapTestHarness();
        var client = new FakeClientChannel();
        var s = new ClientSession(client);

        await h.Service.DispatchClientAsync(s, MapTestHarness.ConnectReq(1, 1, 1, version: 0x1234));

        Assert.False(h.World.Has(Msg.MW_ADDCHAR_ACK));
        var ack = client.Last(Msg.CS_CONNECT_ACK);
        Assert.NotNull(ack);
        Assert.Equal((byte)ConnectResult.InvalidVer, new PacketReader(ack!).ReadByte());
    }

    [Fact]
    public async Task Connect_BadChecksum_ClosesConnection_NoAck()
    {
        var h = new MapTestHarness();
        var client = new FakeClientChannel();
        var s = new ClientSession(client);

        await h.Service.DispatchClientAsync(s, MapTestHarness.ConnectReq(1, 1, 1, checksum: 0xDEAD));

        Assert.True(client.Closed);
        Assert.False(h.World.Has(Msg.MW_ADDCHAR_ACK));
        Assert.False(client.Has(Msg.CS_CONNECT_ACK));
    }

    [Fact]
    public async Task FullHandshake_ReachesInGame_AndDrivesEveryAck()
    {
        var h = new MapTestHarness();
        await h.Service.OnWorldConnectedAsync();
        var (s, client) = await h.EnterAsync(charId: 42, userId: 9, key: 0x1234, name: "Eldrin", mapId: 3);

        Assert.Equal(EnterState.InGame, s.State);

        // Every world-facing ACK in the handshake was emitted.
        Assert.True(h.World.Has(Msg.MW_ADDCHAR_ACK));
        Assert.True(h.World.Has(Msg.MW_ENTERSVR_ACK));
        Assert.True(h.World.Has(Msg.MW_CHARDATA_ACK));
        Assert.True(h.World.Has(Msg.MW_ROUTE_ACK));
        Assert.True(h.World.Has(Msg.MW_ENTERCHAR_ACK));
        Assert.True(h.World.Has(Msg.MW_CHECKMAIN_ACK));

        // The client got its char info and the grant.
        Assert.True(client.Has(Msg.CS_CHARINFO_ACK));
        var connAck = client.Last(Msg.CS_CONNECT_ACK);
        Assert.NotNull(connAck);
        Assert.Equal((byte)ConnectResult.Success, new PacketReader(connAck!).ReadByte());

        // The char picked up the world-provided name / map / position.
        Assert.Equal("Eldrin", s.Char!.Name);
        Assert.Equal((ushort)3, s.Char.MapId);
        Assert.Equal(3663f, s.Char.PosX);
    }

    [Fact]
    public async Task EnterSvrAck_CarriesCharSummary()
    {
        var h = new MapTestHarness();
        var client = new FakeClientChannel();
        var s = new ClientSession(client);
        await h.Service.DispatchClientAsync(s, MapTestHarness.ConnectReq(42, 9, 0x1234));
        await h.Service.DispatchWorldAsync(MapTestHarness.EnterSvrReq(42, 0x1234));

        var pkt = h.World.Last(Msg.MW_ENTERSVR_ACK);
        Assert.NotNull(pkt);
        var r = new PacketReader(pkt!);
        Assert.Equal(42u, r.ReadUInt32());       // charId
        Assert.Equal(0x1234u, r.ReadUInt32());   // key
        Assert.Equal("Char42", r.ReadString());  // synthesized name (DB-free)
        r.ReadByte();  // level
        r.ReadByte();  // realSex
        r.ReadByte();  // class
        r.ReadByte();  // race
        r.ReadByte();  // sex
        r.ReadByte();  // face
        r.ReadByte();  // hair
        r.ReadByte();  // helmetHide
        r.ReadByte();  // country
        r.ReadByte();  // aidCountry
        r.ReadUInt32();// region
        r.ReadByte();  // channel
        r.ReadUInt16();// mapId
        r.ReadFloat(); r.ReadFloat(); r.ReadFloat(); // pos
        r.ReadByte();  // logout
        r.ReadByte();  // save
        Assert.Equal((byte)ConnectResult.Success, r.ReadByte());
    }

    [Fact]
    public async Task CharInfoAck_HasParseablePositionBlock()
    {
        var h = new MapTestHarness();
        var (s, client) = await h.EnterAsync(charId: 5, userId: 1, key: 7, name: "Pittt", mapId: 0);

        var pkt = client.Last(Msg.CS_CHARINFO_ACK);
        Assert.NotNull(pkt);
        var info = ParseCharInfo(pkt!);
        Assert.Equal(5u, info.CharId);
        // CS_CHARINFO_ACK is emitted at MW_CHARINFO_REQ, which the world sends BEFORE MW_ENTERCHAR_REQ
        // (where the real name arrives). With no DB the name is the synthesized "Char5" at this point; with
        // a DB it would be the loaded char name. Either way the position block must parse correctly.
        Assert.Equal("Char5", info.Name);
        Assert.Equal((ushort)0, info.MapId);
        Assert.Equal(3663f, info.X);
        Assert.Equal(557f, info.Z);
    }

    [Fact]
    public async Task DuplicateChar_Rejected()
    {
        var h = new MapTestHarness();
        await h.EnterAsync(charId: 100, userId: 1, key: 1);

        var client2 = new FakeClientChannel();
        var s2 = new ClientSession(client2);
        await h.Service.DispatchClientAsync(s2, MapTestHarness.ConnectReq(100, 2, 2));

        Assert.True(client2.Closed);
        var ack = client2.Last(Msg.CS_CONNECT_ACK);
        Assert.NotNull(ack);
        Assert.Equal((byte)ConnectResult.AlreadyExist, new PacketReader(ack!).ReadByte());
    }

    // Exact parser for the map's CS_CHARINFO_ACK layout (validates the builder byte-for-byte).
    private static (uint CharId, string Name, ushort MapId, float X, float Y, float Z) ParseCharInfo(byte[] p)
    {
        var r = new PacketReader(p);
        uint charId = r.ReadUInt32();
        r.ReadByte(); r.ReadByte(); r.ReadByte();   // 3 secure bytes
        r.ReadUInt16();                              // titleId
        string name = r.ReadString();
        r.ReadByte();                                // startAct
        for (int i = 0; i < 13; i++) r.ReadByte();   // class..helmetHide + level (13 bytes)
        r.ReadUInt16();                              // partyId
        r.ReadUInt32(); r.ReadUInt32(); r.ReadUInt32(); // guildId, fame, fameColor
        r.ReadByte(); r.ReadByte();                  // duty, peer
        r.ReadString();                              // guildName
        r.ReadUInt32();                              // tacticsId
        r.ReadString();                              // tacticsName
        r.ReadUInt32(); r.ReadUInt32(); r.ReadUInt32(); // gold, silver, cooper
        r.ReadUInt32(); r.ReadUInt32(); r.ReadUInt32(); // prevExp, nextExp, exp
        r.ReadUInt32(); r.ReadUInt32(); r.ReadUInt32(); r.ReadUInt32(); // maxHP, HP, maxMP, MP
        r.ReadUInt32();                              // partyChiefId
        r.ReadUInt16();                              // commanderId
        r.ReadUInt32();                              // region
        ushort mapId = r.ReadUInt16();
        float x = r.ReadFloat(); float y = r.ReadFloat(); float z = r.ReadFloat();
        return (charId, name, mapId, x, y, z);
    }
}
