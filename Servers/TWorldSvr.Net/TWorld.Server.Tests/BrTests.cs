using TWorld.Protocol;
using TWorld.Server.World;
using Xunit;

namespace TWorld.Server.Tests;

/// <summary>Phase-4b Battle-Royale tests: queue join, premade-team formation through the real handlers,
/// and the class-bucketed match build driven via the tick seam. No DB.</summary>
public class BrTests
{
    private static void Connect(TcpTestClient client, byte serverId)
    {
        ushort wid = (ushort)((4 << 8) | serverId);
        var c = new PacketWriter(Msg.MW_CONNECT_ACK);
        c.WriteUInt16(wid); c.WriteByte(1); c.WriteByte(0);
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

        // Complete the main-server check handshake added to the enter flow (CHECKMAIN_REQ -> _ACK -> CONRESULT_REQ).
        Assert.Equal(Msg.MW_CHECKMAIN_REQ, client.Receive(TimeSpan.FromSeconds(5)).Id);
        var cm = new PacketWriter(Msg.MW_CHECKMAIN_ACK);
        cm.WriteUInt32(charId); cm.WriteUInt32(key);
        client.Send(cm);
        Assert.Equal(Msg.MW_CONRESULT_REQ, client.Receive(TimeSpan.FromSeconds(5)).Id);
    }

    private static void SeedNamedChar(WorldState state, uint charId, string name)
    {
        var gl = state.GuildLevels.TryGetValue(1, out var l) ? l : (state.GuildLevels[1] = new GuildLevel { Level = 1, MaxCnt = 20 });
        var g = new Guild { Id = 5000 + charId, Name = "G" + charId, Chief = charId, ChiefName = name, Level = 1, LevelChart = gl };
        g.Members[charId] = new GuildMember { CharId = charId, Name = name, Level = 1, Duty = (byte)GuildDuty.Chief };
        state.Guilds[g.Id] = g;
        state.CharGuild[charId] = g.Id;
    }

    private static BrState NewBr() => new(alarmDur: 2, buyTimeDur: 2, battleDur: 2, minPlayerCount: 2);

    [Fact]
    public async Task Queue_Add_Succeeds()
    {
        const uint charId = 8001, key = 0xBEEF;
        await using var host = new WorldTestHost(s => { var br = NewBr(); br.Status = BattleStatus.Alarm; br.Times.Add(100); s.Br = br; });
        using var c = await host.ConnectAsync();
        Connect(c, 1);
        EnterChar(c, charId, key);

        var add = new PacketWriter(Msg.MW_ADDTOBRQUEUE_REQ);
        add.WriteUInt32(charId); add.WriteUInt32(key); add.WriteByte(0);   // onlyReady = 0 -> join
        c.Send(add);

        var ack = c.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.MW_ADDTOBRQUEUE_ACK, ack.Id);
        Assert.Equal((byte)0, ack.ReadByte());   // BOWREG_SUCCESS
        Assert.Equal(charId, ack.ReadUInt32());

        await Task.Delay(80);
        Assert.True(host.State.Br!.Reg.ContainsKey(charId));
    }

    [Fact]
    public async Task Premade_Invite_Accept_FormsTeam()
    {
        const uint chiefId = 8101, mateId = 8102, ck = 0x11, mk = 0x22;
        await using var host = new WorldTestHost(s => { SeedNamedChar(s, chiefId, "Chief"); SeedNamedChar(s, mateId, "Mate"); s.Br = NewBr(); });
        using var cc = await host.ConnectAsync();
        using var mc = await host.ConnectAsync();
        Connect(cc, 1); Connect(mc, 2);
        EnterChar2(cc, chiefId, ck); EnterChar2(mc, mateId, mk);

        // Chief invites Mate.
        var invite = new PacketWriter(Msg.MW_BRTEAMMATEADD_REQ);
        invite.WriteUInt32(chiefId); invite.WriteUInt32(ck); invite.WriteString("Mate");
        cc.Send(invite);

        var prompt = mc.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.MW_BRTEAMMATEADD_ACK, prompt.Id);
        Assert.Equal((byte)TeamAdd.Success, prompt.ReadByte());
        prompt.ReadUInt32(); prompt.ReadUInt32();
        Assert.Equal("Chief", prompt.ReadString());

        // Mate accepts.
        var result = new PacketWriter(Msg.MW_BRTEAMMATEADDRESULT_ACK);
        result.WriteUInt32(mateId); result.WriteUInt32(mk); result.WriteByte((byte)TeamAdd.Success); result.WriteString("Chief");
        mc.Send(result);

        await Task.Delay(120);
        Assert.True(host.State.Br!.PremadeTeams.ContainsKey(chiefId));
        Assert.Equal(2, host.State.Br!.PremadeTeams[chiefId].Players.Count);

        // The chief's client should have received a team update.
        var upd = cc.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.MW_UPDATEBRTEAM_ACK, upd.Id);
    }

    [Fact]
    public async Task Match_Build_BucketsRoles_IntoTeams()
    {
        await using var host = new WorldTestHost(s =>
        {
            var br = NewBr();
            br.Times.Add(100);
            br.Start = 100;
            br.Type = (byte)BrType.Team;
            br.ModeVote[1] = (byte)BrMode.TwoV2;          // force 2v2 (party size 2)
            // four solo registrants: two attack-role (Archer), two support-role (Priest)
            br.Reg[1] = new BrTempPlayer { CharId = 1, Key = 1, Class = (byte)TClass.Archer, Name = "A1" };
            br.Reg[2] = new BrTempPlayer { CharId = 2, Key = 2, Class = (byte)TClass.Archer, Name = "A2" };
            br.Reg[3] = new BrTempPlayer { CharId = 3, Key = 3, Class = (byte)TClass.Priest, Name = "S1" };
            br.Reg[4] = new BrTempPlayer { CharId = 4, Key = 4, Class = (byte)TClass.Priest, Name = "S2" };
            s.Br = br;
        });
        using var brServer = await host.ConnectAsync();
        Connect(brServer, Proto.BrServerId);     // server id 50 -> registers as the BR battle host
        await Task.Delay(100);
        Assert.NotNull(host.State.Br!.BrServer);

        foreach (uint t in new uint[] { 101, 102, 103, 104 })   // alarm -> peace (build match)
            await host.Service.BrTickAsync(t);

        await Task.Delay(80);
        Assert.Equal(2, host.State.Br!.Teams.Count);            // two 2-player teams
        Assert.Empty(host.State.Br!.Reg);                        // queue fully consumed

        bool sawTeams = false;
        for (int i = 0; i < 40; i++)
        {
            PacketReader p;
            try { p = brServer.Receive(TimeSpan.FromMilliseconds(300)); }
            catch { break; }
            if (p.Id == Msg.MW_ADDBRTEAMS_ACK) sawTeams = true;
        }
        Assert.True(sawTeams, "expected an ADDBRTEAMS_ACK to the BR server");
    }

    // Enters a char whose name is supplied by a seeded guild roster (needed for name-based BR lookups).
    private static void EnterChar2(TcpTestClient client, uint charId, uint key) => EnterChar(client, charId, key);
}
