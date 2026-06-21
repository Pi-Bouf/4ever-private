using TWorld.Protocol;
using TWorld.Server.World;
using Xunit;

namespace TWorld.Server.Tests;

/// <summary>Phase 5c — castle/territory ownership broadcasts. A map reports a capture; the world awards the
/// guild stat-exp and fans the new ownership out to every connected map. No DB.</summary>
public class CastleTests
{
    private const byte ServerId = 1, ServerType = 4;
    private static ushort WId => (ushort)((ServerType << 8) | ServerId);

    private static void Connect(TcpTestClient client)
    {
        var c = new PacketWriter(Msg.MW_CONNECT_ACK);
        c.WriteUInt16(WId); c.WriteByte(1); c.WriteByte(0);
        client.Send(c);
    }

    private static void EnterChar(TcpTestClient client, uint charId, uint key)
    {
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
    }

    /// <summary>Make a one-castle guild whose chief and one member are both entered/online.</summary>
    private static Guild SetupGuild(WorldTestHost host, uint guildId, uint chiefId, uint memberId)
    {
        var chief = host.State.Characters[chiefId];
        var member = host.State.Characters[memberId];
        var guild = new Guild { Id = guildId, Chief = chiefId, Name = "Knights", Country = (byte)Contry.Craxion, StatLevel = 5 };
        guild.Members[chiefId] = new GuildMember { CharId = chiefId, OnlineChar = chief };
        guild.Members[memberId] = new GuildMember { CharId = memberId, OnlineChar = member };
        host.State.Guilds[guildId] = guild;
        chief.Guild = guild; member.Guild = guild;
        return guild;
    }

    [Fact]
    public async Task CastleOccupy_BroadcastsOwnership_AndAwardsGuildStatExp()
    {
        await using var host = new WorldTestHost();
        using var client = await host.ConnectAsync();
        Connect(client);

        // StatLevel 5 -> next-level threshold 12000, so +2000 stays below it (no roll-over).
        host.State.Guilds[5] = new Guild { Id = 5, Name = "Knights", Country = (byte)Contry.Craxion, StatLevel = 5 };

        var occ = new PacketWriter(Msg.MW_CASTLEOCCUPY_ACK);
        occ.WriteByte(1); occ.WriteUInt16(200); occ.WriteUInt32(5); occ.WriteByte((byte)Contry.Craxion); occ.WriteUInt32(0);
        client.Send(occ);

        var req = client.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.MW_CASTLEOCCUPY_REQ, req.Id);
        Assert.Equal((byte)1, req.ReadByte());           // type
        Assert.Equal((ushort)200, req.ReadUInt16());     // castleId
        Assert.Equal(5u, req.ReadUInt32());              // guildId
        Assert.Equal((byte)Contry.Craxion, req.ReadByte());
        Assert.Equal("Knights", req.ReadString());

        await Task.Delay(40);
        Assert.Equal(Proto.CastleOccupyStatExp, host.State.Guilds[5].StatExp);
    }

    [Fact]
    public async Task LocalOccupy_BroaCapture_FlipsCountryAndClearsGuild()
    {
        await using var host = new WorldTestHost();
        using var client = await host.ConnectAsync();
        Connect(client);

        host.State.Guilds[9] = new Guild { Id = 9, Name = "Raiders", Country = (byte)Contry.Broa, StatLevel = 5 };

        var occ = new PacketWriter(Msg.MW_LOCALOCCUPY_ACK);
        occ.WriteByte(1); occ.WriteUInt16(50); occ.WriteByte(0); occ.WriteUInt32(9); occ.WriteByte((byte)Contry.Craxion); // curCountry != None
        client.Send(occ);

        var req = client.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.MW_LOCALOCCUPY_REQ, req.Id);
        Assert.Equal((byte)1, req.ReadByte());           // type
        Assert.Equal((ushort)50, req.ReadUInt16());      // localId
        Assert.Equal((byte)1, req.ReadByte());           // country flipped 0 -> 1
        Assert.Equal(0u, req.ReadUInt32());              // guildId cleared for Broa
    }

    [Fact]
    public async Task MissionOccupy_Broadcasts()
    {
        await using var host = new WorldTestHost();
        using var client = await host.ConnectAsync();
        Connect(client);

        var occ = new PacketWriter(Msg.MW_MISSIONOCCUPY_ACK);
        occ.WriteByte(1); occ.WriteUInt16(77); occ.WriteByte((byte)Contry.Defugel);
        client.Send(occ);

        var req = client.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.MW_MISSIONOCCUPY_REQ, req.Id);
        Assert.Equal((byte)1, req.ReadByte());
        Assert.Equal((ushort)77, req.ReadUInt16());
        Assert.Equal((byte)Contry.Defugel, req.ReadByte());
    }

    [Fact]
    public async Task CastleApply_AssignsMemberSlot_EchoesAndBroadcastsCount()
    {
        const uint chiefId = 30, keyC = 0xC1, memberId = 31, keyM = 0xC2;
        await using var host = new WorldTestHost();
        using var client = await host.ConnectAsync();
        Connect(client);
        EnterChar(client, chiefId, keyC);
        EnterChar(client, memberId, keyM);
        var guild = SetupGuild(host, 10, chiefId, memberId);

        var ap = new PacketWriter(Msg.MW_CASTLEAPPLY_ACK);
        ap.WriteUInt32(chiefId); ap.WriteUInt32(keyC); ap.WriteUInt16(200); ap.WriteUInt32(memberId); ap.WriteByte(1);
        client.Send(ap);

        // 1) echo to the chief
        var e1 = client.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.MW_CASTLEAPPLY_REQ, e1.Id);
        Assert.Equal(chiefId, e1.ReadUInt32()); Assert.Equal(keyC, e1.ReadUInt32());
        Assert.Equal((byte)CastleApplyResult.Success, e1.ReadByte());
        Assert.Equal((ushort)200, e1.ReadUInt16()); Assert.Equal(memberId, e1.ReadUInt32()); Assert.Equal((byte)1, e1.ReadByte());

        // 2) echo to the assigned member
        var e2 = client.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.MW_CASTLEAPPLY_REQ, e2.Id);
        Assert.Equal(memberId, e2.ReadUInt32()); Assert.Equal(keyM, e2.ReadUInt32());

        // 3) applicant-count broadcast
        var cnt = client.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.MW_CASTLEAPPLICANTCOUNT_REQ, cnt.Id);
        Assert.Equal((ushort)200, cnt.ReadUInt16()); Assert.Equal(10u, cnt.ReadUInt32());
        Assert.Equal((byte)1, cnt.ReadByte());   // camp
        Assert.Equal((byte)1, cnt.ReadByte());   // count

        Assert.Equal((ushort)200, guild.Members[memberId].Castle);
        Assert.Equal((byte)1, guild.Members[memberId].Camp);
    }

    [Fact]
    public async Task CastleOccupy_ResetsApplicantsOfBothGuilds()
    {
        const uint chiefId = 32, keyC = 0xC3, memberId = 33, keyM = 0xC4;
        await using var host = new WorldTestHost();
        using var client = await host.ConnectAsync();
        Connect(client);
        EnterChar(client, chiefId, keyC);
        EnterChar(client, memberId, keyM);
        var guild = SetupGuild(host, 11, chiefId, memberId);
        guild.Members[memberId].Castle = 200;
        guild.Members[memberId].Camp = 1;

        var occ = new PacketWriter(Msg.MW_CASTLEOCCUPY_ACK);
        occ.WriteByte(1); occ.WriteUInt16(200); occ.WriteUInt32(11); occ.WriteByte((byte)Contry.Craxion); occ.WriteUInt32(0);
        client.Send(occ);

        // ResetCastleApply notifies the online member (CASTLEAPPLY_REQ, castle 0), then the occupy broadcast.
        var reset = client.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.MW_CASTLEAPPLY_REQ, reset.Id);
        reset.ReadUInt32(); reset.ReadUInt32(); reset.ReadByte();
        Assert.Equal((ushort)0, reset.ReadUInt16());      // castle cleared

        Assert.Equal(Msg.MW_CASTLEOCCUPY_REQ, client.Receive(TimeSpan.FromSeconds(5)).Id);

        await Task.Delay(40);
        Assert.Equal((ushort)0, guild.Members[memberId].Castle);
    }

    /// <summary>Write a CASTLEWARINFO_ACK snapshot for one castle whose single local is occupied by the
    /// given (guildId, occupyType) pairs (padded to 6 slots with empty guild 0).</summary>
    private static void SendWarInfoSnapshot(TcpTestClient client, ushort castle, params (uint Guild, OccupyType Type)[] slots)
    {
        var w = new PacketWriter(Msg.MW_CASTLEWARINFO_ACK);
        w.WriteUInt16(castle);
        w.WriteUInt32(0);          // dwGuild (ignored)
        w.WriteByte(1);            // one local
        w.WriteUInt16(1);          // localId
        for (int j = 0; j < 6; j++)
        {
            if (j < slots.Length) { w.WriteUInt32(slots[j].Guild); w.WriteByte((byte)slots[j].Type); }
            else { w.WriteUInt32(0); w.WriteByte(0); }
        }
        client.Send(w);
    }

    [Fact]
    public async Task CastleWarInfo_AggregatesAndSelectsDefenderAttacker()
    {
        await using var host = new WorldTestHost();
        using var client = await host.ConnectAsync();
        Connect(client);

        // Defugel guild with the higher bonus (defender); Craxion guild the attacker.
        host.State.Guilds[100] = new Guild { Id = 100, Name = "GuildD", Country = (byte)Contry.Defugel };
        host.State.Guilds[101] = new Guild { Id = 101, Name = "GuildC", Country = (byte)Contry.Craxion };

        // guildD: 2× accept (20), guildC: 1× accept (10).
        SendWarInfoSnapshot(client, 200,
            (100, OccupyType.Accept), (100, OccupyType.Accept), (101, OccupyType.Accept));

        var info = client.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.MW_CASTLEWARINFO_REQ, info.Id);
        Assert.Equal((ushort)200, info.ReadUInt16());
        Assert.Equal(100u, info.ReadUInt32());                 // defGuild = GuildD
        Assert.Equal("GuildD", info.ReadString());
        Assert.Equal((byte)Contry.Defugel, info.ReadByte());   // defCountry
        Assert.Equal((ushort)20, info.ReadUInt16());           // countryPoint[D]
        Assert.Equal(101u, info.ReadUInt32());                 // atkGuild = GuildC
        Assert.Equal("GuildC", info.ReadString());
        Assert.Equal((ushort)10, info.ReadUInt16());           // countryPoint[C]

        Assert.Equal(2u, info.ReadUInt32());                   // mapGuild count
        Assert.Equal(100u, info.ReadUInt32()); Assert.Equal(20u, info.ReadUInt32());   // ascending by guildId
        Assert.Equal(101u, info.ReadUInt32()); Assert.Equal(10u, info.ReadUInt32());

        Assert.Equal((byte)2, info.ReadByte());                // top-3 entries (one per country)
        Assert.Equal((byte)Contry.Defugel, info.ReadByte()); Assert.Equal("GuildD", info.ReadString()); Assert.Equal((ushort)20, info.ReadUInt16());
        Assert.Equal((byte)Contry.Craxion, info.ReadByte()); Assert.Equal("GuildC", info.ReadString()); Assert.Equal((ushort)10, info.ReadUInt16());

        // Stored selection.
        Assert.Equal(100u, host.State.CastleWarInfo[200].DefGuild);
        Assert.Equal(101u, host.State.CastleWarInfo[200].AtkGuild);
    }

    [Fact]
    public async Task CastleWarInfo_QueryZero_ResendsStoredScoreboard()
    {
        await using var host = new WorldTestHost();
        using var client = await host.ConnectAsync();
        Connect(client);
        host.State.Guilds[100] = new Guild { Id = 100, Name = "GuildD", Country = (byte)Contry.Defugel };

        SendWarInfoSnapshot(client, 200, (100, OccupyType.Accept));
        Assert.Equal(Msg.MW_CASTLEWARINFO_REQ, client.Receive(TimeSpan.FromSeconds(5)).Id);  // recompute broadcast

        // castle==0 -> resend the stored scoreboard for every castle to the asking server.
        var q = new PacketWriter(Msg.MW_CASTLEWARINFO_ACK);
        q.WriteUInt16(0);
        client.Send(q);

        var resend = client.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.MW_CASTLEWARINFO_REQ, resend.Id);
        Assert.Equal((ushort)200, resend.ReadUInt16());
        Assert.Equal(100u, resend.ReadUInt32());               // defGuild persisted from the recompute
    }

    [Fact]
    public async Task EndWar_Broadcasts()
    {
        await using var host = new WorldTestHost();
        using var client = await host.ConnectAsync();
        Connect(client);

        var ew = new PacketWriter(Msg.MW_ENDWAR_ACK);
        ew.WriteUInt16(200);
        client.Send(ew);

        var req = client.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.MW_ENDWAR_REQ, req.Id);
        Assert.Equal((ushort)200, req.ReadUInt16());
    }
}
