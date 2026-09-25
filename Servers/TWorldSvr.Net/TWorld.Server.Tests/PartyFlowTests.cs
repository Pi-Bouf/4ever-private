using TWorld.Protocol;
using TWorld.Server.World;
using Xunit;

namespace TWorld.Server.Tests;

/// <summary>
/// The party round trips against C++ TWorldSvr (SSHandler.cpp:2334-2860, TWorldSvr.cpp:2817-2985). The map server
/// finds the player a <c>MW_PARTY*_REQ</c> is for by its header charId/key, so each check is on who a packet is
/// <b>addressed to</b> and what it says about whom.
/// </summary>
public class PartyFlowTests
{
    private const byte ServerId = 1, ServerType = 4;
    private static ushort WId => (ushort)((ServerType << 8) | ServerId);
    private const byte PartyNoUser = 3, PartyChgChief = 10;

    private static void Connect(TcpTestClient client)
    {
        var c = new PacketWriter(Msg.MW_CONNECT_ACK);
        c.WriteUInt16(WId); c.WriteByte(1); c.WriteByte(0);
        client.Send(c);
    }

    private static Character EnterChar(WorldTestHost host, TcpTestClient client, uint charId, string name)
    {
        uint key = charId + 0x1000;
        var add = new PacketWriter(Msg.MW_ADDCHAR_ACK);
        add.WriteUInt32(charId); add.WriteUInt32(key); add.WriteUInt32(0x0100007F); add.WriteUInt16(5816); add.WriteUInt32(charId + 1000);
        client.Send(add);
        Assert.Equal(Msg.MW_ENTERSVR_REQ, client.Receive(TimeSpan.FromSeconds(5)).Id);
        var cd = new PacketWriter(Msg.MW_CHARDATA_ACK);
        cd.WriteUInt32(charId); cd.WriteUInt32(key); cd.WriteByte(0); cd.WriteByte(20);
        cd.WriteUInt32(500); cd.WriteUInt32(500); cd.WriteUInt32(200); cd.WriteUInt32(200);
        cd.WriteByte(0); cd.WriteByte(0); cd.WriteByte(0);
        client.Send(cd);
        Assert.Equal(Msg.MW_ENTERCHAR_REQ, client.Receive(TimeSpan.FromSeconds(5)).Id);
        var ack = new PacketWriter(Msg.MW_ENTERCHAR_ACK);
        ack.WriteUInt32(charId); ack.WriteUInt32(key);
        client.Send(ack);
        Assert.Equal(Msg.MW_CHECKMAIN_REQ, client.Receive(TimeSpan.FromSeconds(5)).Id);
        var cm = new PacketWriter(Msg.MW_CHECKMAIN_ACK);
        cm.WriteUInt32(charId); cm.WriteUInt32(key);
        client.Send(cm);
        Assert.Equal(Msg.MW_CONRESULT_REQ, client.Receive(TimeSpan.FromSeconds(5)).Id);

        var ch = host.State.Characters[charId];
        ch.Name = name;
        host.State.CharactersByName[name] = ch;
        return ch;
    }

    /// <summary>Everything the world sends until it goes quiet.</summary>
    private static List<PacketReader> Drain(TcpTestClient client)
    {
        var all = new List<PacketReader>();
        while (true)
        {
            try { all.Add(client.Receive(TimeSpan.FromMilliseconds(300))); }
            catch (Exception) { return all; }
        }
    }

    private static void Join(TcpTestClient client, string origin, string target)
    {
        var w = new PacketWriter(Msg.MW_PARTYJOIN_ACK);
        w.WriteString(origin); w.WriteString(target); w.WriteByte(0); w.WriteByte(Ask.Yes);
        w.WriteUInt32(700); w.WriteUInt32(650); w.WriteUInt32(300); w.WriteUInt32(250);
        client.Send(w);
    }

    // MW_PARTYJOIN_REQ: receiver charId, key, partyId, member name, member charId, chief, commander, …
    private static List<(uint To, string Member, uint MemberId, uint Chief)> Joins(List<PacketReader> packets)
        => packets.Where(p => p.Id == Msg.MW_PARTYJOIN_REQ).Select(p =>
        {
            uint to = p.ReadUInt32(); p.ReadUInt32(); p.ReadUInt16();
            string name = p.ReadString(); uint id = p.ReadUInt32(); uint chief = p.ReadUInt32();
            return (to, name, id, chief);
        }).ToList();

    // MW_PARTYATTR_REQ: charId, key, partyId, type, chief, commander.
    private static Dictionary<uint, (ushort Party, uint Chief)> Attrs(List<PacketReader> packets)
    {
        var d = new Dictionary<uint, (ushort, uint)>();
        foreach (var p in packets.Where(p => p.Id == Msg.MW_PARTYATTR_REQ))
        {
            uint to = p.ReadUInt32(); p.ReadUInt32();
            ushort party = p.ReadUInt16(); p.ReadByte(); uint chief = p.ReadUInt32();
            d[to] = (party, chief);
        }
        return d;
    }

    private static async Task<(WorldTestHost, TcpTestClient, Character, Character, Character)> Setup()
    {
        var host = new WorldTestHost();
        var client = await host.ConnectAsync();
        Connect(client);
        var a = EnterChar(host, client, 80, "Alice");
        var b = EnterChar(host, client, 81, "Bob");
        var c = EnterChar(host, client, 82, "Carol");
        return (host, client, a, b, c);
    }

    [Fact]
    public async Task Join_NewParty_EachSideIsToldAboutTheOther()
    {
        var (host, client, a, b, _) = await Setup();
        await using var _h = host; using var _c = client;

        Join(client, "Alice", "Bob");
        var packets = Drain(client);

        var joins = Joins(packets);
        Assert.Equal(2, joins.Count);
        Assert.Contains((81u, "Alice", 80u, 80u), joins);   // Bob learns about Alice
        Assert.Contains((80u, "Bob", 81u, 80u), joins);     // Alice learns about Bob
        var attrs = Attrs(packets);
        Assert.Equal(80u, attrs[80].Chief);
        Assert.Equal(80u, attrs[81].Chief);
        Assert.NotEqual((ushort)0, attrs[81].Party);
        Assert.Same(a.Party, b.Party);
        Assert.Equal(650u, b.HP);                           // SetCharStatus from the joiner's report
    }

    [Fact]
    public async Task Join_ThirdMember_MeetsEveryone()
    {
        var (host, client, _, _, c) = await Setup();
        await using var _h = host; using var _c = client;

        Join(client, "Alice", "Bob");
        Drain(client);
        Join(client, "Alice", "Carol");
        var joins = Joins(Drain(client));

        Assert.Equal(4, joins.Count);
        Assert.Contains((82u, "Alice", 80u, 80u), joins);
        Assert.Contains((82u, "Bob", 81u, 80u), joins);
        Assert.Contains((80u, "Carol", 82u, 80u), joins);
        Assert.Contains((81u, "Carol", 82u, 80u), joins);
        Assert.Equal(3, c.Party!.Size);
    }

    [Fact]
    public async Task Kick_FromThree_EveryMemberGetsItsOwnDel_AndTheLeaverIsCleared()
    {
        var (host, client, a, b, c) = await Setup();
        await using var _h = host; using var _c = client;
        Join(client, "Alice", "Bob"); Drain(client);
        Join(client, "Alice", "Carol"); Drain(client);
        ushort partyId = a.Party!.Id;

        var del = new PacketWriter(Msg.MW_PARTYDEL_ACK);
        del.WriteUInt16(partyId); del.WriteUInt32(82); del.WriteByte(1);
        client.Send(del);
        var packets = Drain(client);

        var dels = packets.Where(p => p.Id == Msg.MW_PARTYDEL_REQ).Select(p =>
        {
            uint to = p.ReadUInt32(); p.ReadUInt32(); uint target = p.ReadUInt32(); uint chief = p.ReadUInt32();
            p.ReadUInt16(); ushort party = p.ReadUInt16(); byte kick = p.ReadByte();
            return (to, target, chief, party, kick);
        }).ToList();
        Assert.Equal(3, dels.Count);
        Assert.Contains((80u, 82u, 80u, partyId, (byte)1), dels);
        Assert.Contains((81u, 82u, 80u, partyId, (byte)1), dels);
        Assert.Contains((82u, 82u, 0u, (ushort)0, (byte)1), dels);   // the leaver: no chief, no party
        Assert.Equal(((ushort)0, 0u), Attrs(packets)[82]);
        Assert.Null(c.Party);
        Assert.Equal(2, a.Party!.Size);
        Assert.Same(a.Party, b.Party);
    }

    [Fact]
    public async Task Leave_FromTwo_DissolvesTheParty()
    {
        var (host, client, a, b, _) = await Setup();
        await using var _h = host; using var _c = client;
        Join(client, "Alice", "Bob"); Drain(client);
        ushort partyId = a.Party!.Id;

        var del = new PacketWriter(Msg.MW_PARTYDEL_ACK);
        del.WriteUInt16(partyId); del.WriteUInt32(81); del.WriteByte(0);
        client.Send(del);
        var packets = Drain(client);

        Assert.Equal(new[] { 80u, 81u, 80u }, packets.Where(p => p.Id == Msg.MW_PARTYDEL_REQ).Select(p => p.ReadUInt32()).ToArray());
        var attrs = Attrs(packets);
        Assert.Equal(((ushort)0, 0u), attrs[80]);
        Assert.Equal(((ushort)0, 0u), attrs[81]);
        Assert.Null(a.Party);
        Assert.Null(b.Party);
        Assert.False(host.State.Parties.ContainsKey(partyId));
    }

    [Fact]
    public async Task ChgChief_TellsTheOldChief_AndUpdatesEveryMember()
    {
        var (host, client, a, _, _) = await Setup();
        await using var _h = host; using var _c = client;
        Join(client, "Alice", "Bob"); Drain(client);

        var w = new PacketWriter(Msg.MW_CHGPARTYCHIEF_ACK);
        w.WriteUInt32(80); w.WriteUInt32(80 + 0x1000); w.WriteUInt32(81);
        client.Send(w);
        var packets = Drain(client);

        var chg = Assert.Single(packets, p => p.Id == Msg.MW_CHGPARTYCHIEF_REQ);
        Assert.Equal(80u, chg.ReadUInt32()); chg.ReadUInt32();
        Assert.Equal(PartyChgChief, chg.ReadByte());
        var attrs = Attrs(packets);
        Assert.Equal(81u, attrs[80].Chief);
        Assert.Equal(81u, attrs[81].Chief);
        Assert.Equal(81u, a.Party!.ChiefId);
    }

    [Fact]
    public async Task ChgType_EachMemberIsAddressed()
    {
        var (host, client, _, _, _) = await Setup();
        await using var _h = host; using var _c = client;
        Join(client, "Alice", "Bob"); Drain(client);

        var w = new PacketWriter(Msg.MW_CHGPARTYTYPE_ACK);
        w.WriteUInt32(80); w.WriteUInt32(80 + 0x1000); w.WriteByte(3);
        client.Send(w);
        var to = Drain(client).Where(p => p.Id == Msg.MW_CHGPARTYTYPE_REQ).Select(p =>
        {
            uint id = p.ReadUInt32(); p.ReadUInt32(); byte ret = p.ReadByte(); byte type = p.ReadByte();
            return (id, ret, type);
        }).ToList();
        Assert.Equal(new[] { (80u, (byte)0, (byte)3), (81u, (byte)0, (byte)3) }, to);
    }

    [Fact]
    public async Task Add_UnknownTarget_TellsTheInviter()
    {
        var (host, client, _, _, _) = await Setup();
        await using var _h = host; using var _c = client;

        var w = new PacketWriter(Msg.MW_PARTYADD_ACK);
        w.WriteString("Alice"); w.WriteString("Nobody"); w.WriteByte(0);
        w.WriteUInt32(1); w.WriteUInt32(1); w.WriteUInt32(1); w.WriteUInt32(1);
        client.Send(w);

        var r = Assert.Single(Drain(client), p => p.Id == Msg.MW_PARTYADD_REQ);
        Assert.Equal(80u, r.ReadUInt32()); r.ReadUInt32();
        Assert.Equal("Alice", r.ReadString());
        Assert.Equal("Nobody", r.ReadString());
        r.ReadByte();
        Assert.Equal(PartyNoUser, r.ReadByte());
    }
}
