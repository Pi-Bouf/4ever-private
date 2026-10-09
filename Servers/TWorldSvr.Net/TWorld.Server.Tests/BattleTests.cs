using TWorld.Protocol;
using TWorld.Server.World;
using Xunit;

namespace TWorld.Server.Tests;

/// <summary>Phase-4c battle-time tests: the local-field NORMAL→BATTLE→PEACE→NORMAL cycle broadcasts
/// LOCALENABLE, and a castle day selects the castle window instead. Driven via the tick seam. No DB.</summary>
public class BattleTests
{
    private static void Connect(TcpTestClient client, byte serverId)
    {
        ushort wid = (ushort)((4 << 8) | serverId);
        var c = new PacketWriter(Msg.MW_CONNECT_ACK);
        c.WriteUInt16(wid); c.WriteByte(1); c.WriteByte(0);
        client.Send(c);
    }

    private static BattleSchedule Schedule(Action<BattleSchedule> setup)
    {
        var s = new BattleSchedule();
        setup(s);
        return s;
    }

    [Fact]
    public async Task LocalField_RunsFullCycle_AndBroadcasts()
    {
        await using var host = new WorldTestHost(s => s.Battles = Schedule(sc =>
        {
            var local = sc[BattleType.Local];
            local.BattleStart = 100; local.BattleDur = 20; local.AlarmStart = 30; local.AlarmEnd = 10; local.PeaceDur = 5;
        }));
        using var map = await host.ConnectAsync();
        Connect(map, 1);
        await Task.Delay(80);

        // Sunday=1.. ; pick Wednesday(4) — no castle day configured, so the local window is chosen.
        const byte dow = 4;
        var statuses = new List<byte>();
        void Drain()
        {
            for (int i = 0; i < 20; i++)
            {
                PacketReader p;
                try { p = map.Receive(TimeSpan.FromMilliseconds(150)); }
                catch { break; }
                if (p.Id == Msg.MW_LOCALENABLE_REQ) statuses.Add(p.ReadByte());
            }
        }

        foreach (uint t in new uint[] { 70, 90, 101, 110, 121, 122, 123, 124, 126 })
        {
            host.Service.BattleTick(t, dow);
            Drain();
        }

        Assert.Contains((byte)BattleStatus.Battle, statuses);
        Assert.Contains((byte)BattleStatus.Peace, statuses);
        Assert.Equal(BattleStatus.Normal, host.State.Battles![BattleType.Local].Status);   // cycled back
    }

    private static List<PacketReader> Drain(TcpTestClient c)
    {
        var got = new List<PacketReader>();
        for (int i = 0; i < 30; i++) { try { got.Add(c.Receive(TimeSpan.FromMilliseconds(150))); } catch { break; } }
        return got;
    }

    [Fact]
    public async Task ANewMap_IsToldWhereEveryWarStands()
    {
        await using var host = new WorldTestHost(s => s.Battles = Schedule(sc =>
        {
            sc[BattleType.Local].BattleStart = 70200;
            sc[BattleType.Castle].Day = 1; sc[BattleType.Castle].BattleStart = 70200;
            sc[BattleType.SkyGarden].Day = 7; sc[BattleType.SkyGarden].BattleStart = 77400;
        }));
        using var map = await host.ConnectAsync();
        Connect(map, 1);

        var got = Drain(map);
        var local = got.First(p => p.Id == Msg.MW_LOCALENABLE_REQ);
        Assert.Equal(((byte)0, 0u, 70200u, (byte)1, 70200u), (local.ReadByte(), local.ReadUInt32(), local.ReadUInt32(), local.ReadByte(), local.ReadUInt32()));
        Assert.Contains(got, p => p.Id == Msg.MW_MISSIONENABLE_REQ);
        var sky = got.First(p => p.Id == Msg.MW_SKYGARDENENABLE_REQ);
        Assert.Equal(((byte)BattleStatus.SkyGardenStart, 0u, (byte)7, 77400u), (sky.ReadByte(), sky.ReadUInt32(), sky.ReadByte(), sky.ReadUInt32()));
    }

    [Fact]
    public async Task AWarPhaseFromOutside_GoesToEveryMap()
    {
        await using var host = new WorldTestHost(s => s.Battles = Schedule(_ => { }));
        using var map = await host.ConnectAsync();
        Connect(map, 1);
        using var tool = await host.ConnectAsync();                   // the control server / a test tool on the server plane
        Drain(map);

        var w = new PacketWriter(Msg.SM_BATTLESTATUS_REQ);
        w.WriteByte(0 /* BT_LOCAL */); w.WriteByte((byte)BattleStatus.Battle); w.WriteUInt32(0); w.WriteUInt32(1800);
        tool.Send(w);

        var local = Drain(map).First(p => p.Id == Msg.MW_LOCALENABLE_REQ);
        Assert.Equal(((byte)BattleStatus.Battle, 1800u), (local.ReadByte(), local.ReadUInt32()));
        Assert.Equal(BattleStatus.Normal, host.State.Battles![BattleType.Local].Status);   // the schedule itself is left alone
    }

    [Fact]
    public async Task CastleDay_SelectsCastleWindow()
    {
        await using var host = new WorldTestHost(s => s.Battles = Schedule(sc =>
        {
            var castle = sc[BattleType.Castle];
            castle.Day = 4;                 // Wednesday
            castle.BattleStart = 100; castle.BattleDur = 20; castle.AlarmStart = 30; castle.AlarmEnd = 10; castle.PeaceDur = 5;
        }));
        using var map = await host.ConnectAsync();
        Connect(map, 1);
        await Task.Delay(80);
        // The connect itself tells the map where every war stands (a LOCALENABLE among them): drain it.
        for (int i = 0; i < 20; i++) { try { map.Receive(TimeSpan.FromMilliseconds(100)); } catch { break; } }

        // Drive into the battle window on the castle day -> a CASTLEENABLE (not LOCALENABLE) must appear.
        bool sawCastle = false;
        foreach (uint t in new uint[] { 101, 110 })
        {
            host.Service.BattleTick(t, dayOfWeek: 4);
            for (int i = 0; i < 10; i++)
            {
                PacketReader p;
                try { p = map.Receive(TimeSpan.FromMilliseconds(150)); }
                catch { break; }
                if (p.Id == Msg.MW_CASTLEENABLE_REQ) sawCastle = true;
                Assert.NotEqual(Msg.MW_LOCALENABLE_REQ, p.Id);
            }
        }
        Assert.True(sawCastle, "expected a CASTLEENABLE on the castle day");
        Assert.Equal(BattleStatus.Battle, host.State.Battles![BattleType.Castle].Status);
    }

    [Fact]
    public async Task CastleWar_OnPeace_ClearsScoreboard_AndRecomputesWeekRecords()
    {
        await using var host = new WorldTestHost(s => s.Battles = Schedule(sc =>
        {
            var castle = sc[BattleType.Castle];
            castle.Day = 4;
            castle.BattleStart = 100; castle.BattleDur = 20; castle.AlarmStart = 30; castle.AlarmEnd = 10; castle.PeaceDur = 5;
        }));
        using var map = await host.ConnectAsync();
        Connect(map, 1);
        await Task.Delay(80);

        // A leftover scoreboard from the war, and a guild member with one fresh + one stale PvP record.
        host.State.CastleWarInfo[200] = new CastleWarInfo { Id = 200 };
        var guild = new Guild { Id = 7 };
        var member = new GuildMember { CharId = 70 };
        member.Records.Add(new GuildPvpRecord { CharId = 70, Date = 99, KillCount = 5 });  // within 7 days of day 100
        member.Records.Add(new GuildPvpRecord { CharId = 70, Date = 90, KillCount = 3 });  // older than a week -> pruned
        guild.Members[70] = member;
        host.State.Guilds[7] = guild;

        void Drain() { for (int i = 0; i < 10; i++) { try { map.Receive(TimeSpan.FromMilliseconds(120)); } catch { break; } } }

        host.Service.BattleTick(101, dayOfWeek: 4, recentDay: 100); Drain();   // -> BATTLE
        host.Service.BattleTick(121, dayOfWeek: 4, recentDay: 100); Drain();   // -> PEACE (war end)

        Assert.Equal(BattleStatus.Peace, host.State.Battles![BattleType.Castle].Status);
        Assert.Empty(host.State.CastleWarInfo);                 // scoreboard cleared
        Assert.Equal(100u, host.State.RecentRecordDate);
        Assert.Single(member.Records);                          // stale record pruned
        Assert.Equal((ushort)5, member.WeekRecord.KillCount);   // only the fresh record counts
    }
}
