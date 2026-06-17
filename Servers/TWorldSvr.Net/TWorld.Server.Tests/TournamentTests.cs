using TWorld.Protocol;
using TWorld.Server.World;
using Xunit;

namespace TWorld.Server.Tests;

/// <summary>Phase-4d tournament tests: the bracket config is announced to a connecting map, and the
/// read-only sub-commands return the same info. No DB.</summary>
public class TournamentTests
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

    private static TournamentState SeedTournament()
    {
        var t = new TournamentState { FirstGroupCount = 3, Group = 0, Step = 0, MaxLevel = 0 };
        var e = new TournamentEntry { Group = 1, EntryId = 2, Name = "Open", Type = 1, Class = 0xFF, Fee = 1000, FeeBack = 500, PermitItemId = 42, PermitCount = 1, MinLevel = 0, MaxLevel = 0xFF };
        e.Rewards.Add(new TournamentReward { ChartType = 1, ItemId = 777, Count = 5 });
        t.Entries[e.EntryId] = e;
        return t;
    }

    private static void AssertEntryBytes(PacketReader p)
    {
        Assert.Equal(Msg.MW_TOURNAMENTINFO_REQ, p.Id);
        Assert.Equal((byte)3, p.ReadByte());      // firstGroupCount
        Assert.Equal((byte)0, p.ReadByte());      // group
        Assert.Equal((byte)0, p.ReadByte());      // step
        Assert.Equal((byte)1, p.ReadByte());      // entry count
        Assert.Equal((byte)1, p.ReadByte());      // entry group
        Assert.Equal((byte)2, p.ReadByte());      // entryId
        Assert.Equal("Open", p.ReadString());
        Assert.Equal((byte)1, p.ReadByte());      // type
        Assert.Equal(0xFFu, p.ReadUInt32());      // class
        Assert.Equal(1000u, p.ReadUInt32());      // fee
        Assert.Equal(500u, p.ReadUInt32());       // feeBack
        Assert.Equal((ushort)42, p.ReadUInt16()); // permit item
        Assert.Equal((byte)1, p.ReadByte());      // permit count
        Assert.Equal((byte)0, p.ReadByte());      // min level
        Assert.Equal((byte)0xFF, p.ReadByte());   // max level
        Assert.Equal((byte)1, p.ReadByte());      // reward count
        Assert.Equal((byte)1, p.ReadByte());      // reward chart type
        Assert.Equal((ushort)777, p.ReadUInt16());// reward item
        Assert.Equal((byte)5, p.ReadByte());      // reward count
    }

    [Fact]
    public async Task ConnectingMap_ReceivesTournamentInfo()
    {
        await using var host = new WorldTestHost(s => s.Tournament = SeedTournament());
        using var map = await host.ConnectAsync();
        Connect(map, 1);
        AssertEntryBytes(map.Receive(TimeSpan.FromSeconds(5)));
    }

    private static void SeedNamedChar(WorldState state, uint charId, string name)
    {
        var gl = state.GuildLevels.TryGetValue(1, out var l) ? l : (state.GuildLevels[1] = new GuildLevel { Level = 1, MaxCnt = 20 });
        var g = new Guild { Id = 4000 + charId, Name = "G" + charId, Chief = charId, ChiefName = name, Level = 1, LevelChart = gl };
        g.Members[charId] = new GuildMember { CharId = charId, Name = name, Level = 1, Duty = (byte)GuildDuty.Chief };
        state.Guilds[g.Id] = g;
        state.CharGuild[charId] = g.Id;
    }

    [Fact]
    public async Task Apply_DuringNormalStep_RegistersPlayer()
    {
        const uint charId = 9601, key = 0xCD;
        await using var host = new WorldTestHost(s =>
        {
            var t = SeedTournament();
            t.Step = (byte)TnmtStep.Normal;   // registration window open
            s.Tournament = t;
        });
        using var map = await host.ConnectAsync();
        Connect(map, 1);
        Assert.Equal(Msg.MW_TOURNAMENTINFO_REQ, map.Receive(TimeSpan.FromSeconds(5)).Id); // connect announce
        EnterChar(map, charId, key);

        var apply = new PacketWriter(Msg.MW_TOURNAMENT_ACK);
        apply.WriteUInt32(charId); apply.WriteUInt32(key); apply.WriteUInt16(Msg.MW_TOURNAMENTAPPLY_ACK);
        apply.WriteByte(2); apply.WriteString("HW1"); apply.WriteUInt32(0x0A0A0A0A);   // entryId, hwid, ip
        map.Send(apply);

        var res = map.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.MW_TOURNAMENT_REQ, res.Id);
        Assert.Equal(charId, res.ReadUInt32());
        Assert.Equal(key, res.ReadUInt32());
        Assert.Equal(Msg.MW_TOURNAMENTAPPLY_REQ, res.ReadUInt16());
        Assert.Equal((byte)TournamentResult.Success, res.ReadByte());
        Assert.Equal((byte)2, res.ReadByte());                       // entryId
        Assert.Equal(Msg.MW_TOURNAMENT_REQ, map.Receive(TimeSpan.FromSeconds(5)).Id); // follow-up APPLYINFO

        await Task.Delay(80);
        Assert.True(host.State.Tournament!.Players.ContainsKey(charId));
        Assert.True(host.State.Tournament!.Entries[2].Normal.ContainsKey(charId));
    }

    [Fact]
    public async Task PartyAdd_DuringPartyStep_AddsToChiefParty()
    {
        const uint chiefId = 9701, mateId = 9702, ck = 0x1, mk = 0x2;
        await using var host = new WorldTestHost(s =>
        {
            var t = SeedTournament();
            t.Step = (byte)TnmtStep.Party;
            // entry 3 is a party division; the chief is already registered into it.
            var party = new TournamentEntry { Group = 1, EntryId = 3, Name = "Party", Type = (byte)TournamentEntryType.Party, Class = 0xFF, MinLevel = 0, MaxLevel = 0xFF };
            t.Entries[3] = party;
            var chief = new TnmtPlayer { CharId = chiefId, Name = "Chief", EntryId = 3, ChiefId = chiefId, Level = 20 };
            t.Players[chiefId] = chief;
            party.Normal[chiefId] = chief;
            s.Tournament = t;
            SeedNamedChar(s, chiefId, "Chief");
            SeedNamedChar(s, mateId, "Mate");
        });
        using var cc = await host.ConnectAsync();
        using var mc = await host.ConnectAsync();
        Connect(cc, 1); Connect(mc, 2);
        Assert.Equal(Msg.MW_TOURNAMENTINFO_REQ, cc.Receive(TimeSpan.FromSeconds(5)).Id);
        Assert.Equal(Msg.MW_TOURNAMENTINFO_REQ, mc.Receive(TimeSpan.FromSeconds(5)).Id);
        EnterChar(cc, chiefId, ck); EnterChar(mc, mateId, mk);

        var add = new PacketWriter(Msg.MW_TOURNAMENT_ACK);
        add.WriteUInt32(chiefId); add.WriteUInt32(ck); add.WriteUInt16(Msg.MW_TOURNAMENTPARTYADD_ACK);
        add.WriteString("Mate");
        cc.Send(add);

        var res = cc.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.MW_TOURNAMENT_REQ, res.Id);
        res.ReadUInt32(); res.ReadUInt32();
        Assert.Equal(Msg.MW_TOURNAMENTPARTYADD_REQ, res.ReadUInt16());
        Assert.Equal((byte)TournamentResult.Success, res.ReadByte());
        Assert.Equal("Mate", res.ReadString());

        await Task.Delay(100);
        Assert.True(host.State.Tournament!.Players[chiefId].Party.ContainsKey(mateId));
    }

    [Fact]
    public async Task Betting_AndFinalResult_PaysTheChampionBackers()
    {
        const uint bettor = 300, winnerId = 100, loserId = 200, key = 0xEE;
        await using var host = new WorldTestHost(s =>
        {
            var t = SeedTournament();
            t.Id = 1; t.Step = (byte)TnmtStep.Enter; t.Group = 1; t.Base = 10;   // entry 2 is group 1, tournament active
            var winner = new TnmtPlayer { CharId = winnerId, Name = "Win", EntryId = 2, ChiefId = winnerId, Country = 0 };
            var loser = new TnmtPlayer { CharId = loserId, Name = "Lose", EntryId = 2, ChiefId = loserId, Country = 1 };
            t.Players[winnerId] = winner; t.Players[loserId] = loser;
            t.Entries[2].Player[winnerId] = winner; t.Entries[2].Player[loserId] = loser;
            s.Tournament = t;
        });
        using var map = await host.ConnectAsync();
        Connect(map, 1);
        Assert.Equal(Msg.MW_TOURNAMENTINFO_REQ, map.Receive(TimeSpan.FromSeconds(5)).Id);
        EnterChar(map, bettor, key);

        // Enter the arena gate with 1000 money -> 10 tickets, then bet all on the winner.
        var gate = new PacketWriter(Msg.MW_TOURNAMENTENTERGATE_ACK);
        gate.WriteUInt32(bettor); gate.WriteUInt32(key); gate.WriteUInt32(1000); gate.WriteByte(1);
        map.Send(gate);

        var join = new PacketWriter(Msg.MW_TOURNAMENT_ACK);
        join.WriteUInt32(bettor); join.WriteUInt32(key); join.WriteUInt16(Msg.MW_TOURNAMENTEVENTJOIN_ACK);
        join.WriteByte(2); join.WriteUInt32(winnerId);
        map.Send(join);
        Assert.Equal(Msg.MW_TOURNAMENT_REQ, map.Receive(TimeSpan.FromSeconds(5)).Id); // EVENTJOIN result

        await Task.Delay(60);
        Assert.Equal(10u, host.State.Tournament!.Players[winnerId].Sum);   // pool = 10 tickets

        // Report the final: winner beats loser.
        var result = new PacketWriter(Msg.MW_TOURNAMENTRESULT_ACK);
        result.WriteByte((byte)TnmtStep.Final); result.WriteByte(1);   // step, ret(win)
        result.WriteUInt32(winnerId); result.WriteUInt32(loserId);
        result.WriteUInt32(0); result.WriteUInt32(0);                  // blue/red hide ticks
        map.Send(result);

        bool sawResult = false, sawPayout = false;
        for (int i = 0; i < 30; i++)
        {
            PacketReader p;
            try { p = map.Receive(TimeSpan.FromMilliseconds(200)); }
            catch { break; }
            if (p.Id == Msg.MW_TOURNAMENTRESULT_REQ) sawResult = true;
            else if (p.Id == Msg.MW_TOURNAMENTBATPOINT_REQ)
            {
                sawPayout = true;
                Assert.Equal(bettor, p.ReadUInt32());
                p.ReadString();
                Assert.Equal(100u, p.ReadUInt32());   // base(10) * bet(10) * rate(1)
            }
        }
        Assert.True(sawResult, "expected a TOURNAMENTRESULT broadcast");
        Assert.True(sawPayout, "expected a TOURNAMENTBATPOINT payout to the backer");
        Assert.Equal((byte)TnmtWin.Win, host.State.Tournament!.Players[winnerId].Result[2]);
    }

    [Fact]
    public async Task Scheduler_FiresMatchStep_AndSeedsBracket()
    {
        await using var host = new WorldTestHost(s =>
        {
            var t = SeedTournament();
            t.ScheduleActive = true;
            t.ScheduleInitialized = true;            // don't recompute from the calendar window
            t.Steps.Add(new TournamentStep { Group = 0, StepId = (byte)TnmtStep.Normal, Period = 60, Start = 50, End = 110 });
            t.Steps.Add(new TournamentStep { Group = 0, StepId = (byte)TnmtStep.Match, Period = 60, Start = 100, End = 160 });
            // four registered solo players in entry 2
            for (uint i = 0; i < 4; i++)
            {
                var p = new TnmtPlayer { CharId = 10 + i, Country = 0, Name = "P" + i, Level = (byte)(10 + i), EntryId = 2 };
                t.Players[p.CharId] = p;
                t.Entries[2].Normal[p.CharId] = p;
            }
            s.Tournament = t;
        });
        using var map = await host.ConnectAsync();
        Connect(map, 1);
        Assert.Equal(Msg.MW_TOURNAMENTINFO_REQ, map.Receive(TimeSpan.FromSeconds(5)).Id);

        host.Service.TournamentTick(60);    // fires NORMAL
        host.Service.TournamentTick(110);   // fires MATCH -> SelectPlayer + bracket broadcast

        await Task.Delay(80);
        Assert.Equal((byte)TnmtStep.Match, host.State.Tournament!.Step);
        Assert.Equal(4, host.State.Tournament!.Entries[2].Player.Count);   // bracket seeded
        Assert.True(host.State.Tournament!.Selected);

        bool sawEnable = false, sawMatch = false;
        for (int i = 0; i < 30; i++)
        {
            PacketReader p;
            try { p = map.Receive(TimeSpan.FromMilliseconds(200)); }
            catch { break; }
            if (p.Id == Msg.MW_TOURNAMENTENABLE_REQ) sawEnable = true;
            else if (p.Id == Msg.MW_TOURNAMENTMATCH_REQ) { sawMatch = true; Assert.Equal((byte)4, p.ReadByte()); }
        }
        Assert.True(sawEnable, "expected a TOURNAMENTENABLE broadcast");
        Assert.True(sawMatch, "expected a TOURNAMENTMATCH broadcast");
    }

    [Fact]
    public async Task ApplyInfo_ReturnsRegistrationView()
    {
        const uint charId = 9501, key = 0xAB;
        await using var host = new WorldTestHost(s => { var t = SeedTournament(); t.Step = (byte)TnmtStep.Normal; s.Tournament = t; });
        using var map = await host.ConnectAsync();
        Connect(map, 1);
        Assert.Equal(Msg.MW_TOURNAMENTINFO_REQ, map.Receive(TimeSpan.FromSeconds(5)).Id); // drain the connect announce
        EnterChar(map, charId, key);

        var req = new PacketWriter(Msg.MW_TOURNAMENT_ACK);
        req.WriteUInt32(charId); req.WriteUInt32(key); req.WriteUInt16(Msg.MW_TOURNAMENTAPPLYINFO_ACK);
        map.Send(req);

        var p = map.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.MW_TOURNAMENT_REQ, p.Id);
        Assert.Equal(charId, p.ReadUInt32());
        Assert.Equal(key, p.ReadUInt32());
        Assert.Equal(Msg.MW_TOURNAMENTAPPLYINFO_REQ, p.ReadUInt16());
        Assert.Equal((byte)1, p.ReadByte());        // entry count
        Assert.Equal((byte)1, p.ReadByte());        // entry group
        Assert.Equal((byte)2, p.ReadByte());        // entryId
        Assert.Equal("Open", p.ReadString());
        Assert.Equal((byte)1, p.ReadByte());        // type
        Assert.Equal(0xFFu, p.ReadUInt32());        // class
        Assert.False(p.ReadBool());                 // not my entry yet
        Assert.Equal(1000u, p.ReadUInt32());        // fee
        Assert.Equal(500u, p.ReadUInt32());         // feeBack
        Assert.Equal((byte)1, p.ReadByte());        // permit count
        Assert.Equal((byte)0, p.ReadByte());        // min level
        p.ReadByte();                                // max level
        Assert.Equal((byte)8, p.ReadByte());        // free slots (TOURNAMENT_SLOT - 0)
        Assert.Equal((ushort)0, p.ReadUInt16());    // normal count
    }
}
