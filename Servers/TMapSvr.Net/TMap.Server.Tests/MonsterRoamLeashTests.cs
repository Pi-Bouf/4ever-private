using TMap.Data;
using TMap.Protocol;
using TMap.Server.Map;
using Xunit;

namespace TMap.Server.Tests;

/// <summary>
/// Two monster behaviours measured wrong against the real client:
/// <list type="bullet">
/// <item><b>Spinning on the spot.</b> The roam radius came from the spawn's <c>bRange</c> — the placement
/// scatter, 0 for most live spawns — instead of <c>bArea</c> (3–5). With radius 0 every roam target was the
/// anchor itself, so idle monsters turned toward a point they were already standing on.</item>
/// <item><b>Never giving up.</b> The chase leash was a fixed 800 instead of each monster's <c>wChaseRange</c>
/// (live: mostly 50 or 90), and giving up hard-reset the monster instead of firing <c>AT_LEAVELB</c>, which is
/// what sends a scripted monster home.</item>
/// </list>
/// </summary>
public class MonsterRoamLeashTests
{
    // ---------------- spawn: the right columns ----------------

    private static TemplateStore SpawnStore(byte range, byte area, ushort chaseRange)
    {
        const ushort monId = 500, attrId = 900; const byte level = 5;
        var t = new TemplateStore();
        t.MonsterTemplates[monId] = new MonsterTemplate(monId, level, attrId, ChaseRange: chaseRange);
        t.MonAttrs[TemplateStore.MonAttrKey(attrId, level)] = new MonAttrRow(attrId, level, 200, 50, 0);
        t.MonsterSpawns.Add(new MonsterSpawnDef(
            new MonSpawnRow(Id: 7, MapId: 0, PosX: 100, PosY: 0, PosZ: 100, Dir: 0, Country: 0,
                Count: 1, Range: range, Prob: 100, Region: 7, Delay: 0, Event: 0, Area: area),
            new List<MapMonRow> { new(SpawnId: 7, MonId: monId, Leader: 0, Essential: 0, Prob: 100) }));
        return t;
    }

    [Fact]
    public void ChartSpawn_RoamsOnBArea_NotBRange()
    {
        // The dominant live shape: bRange 0, bArea 3.
        var h = new MapTestHarness(SpawnStore(range: 0, area: 3, chaseRange: 50));
        h.Service.InitMonsterSpawns();
        h.Service.RunMonsterRegen(0);

        var mon = Assert.Single(h.State.AllMonsters());
        Assert.Equal(3f, mon.Area);
    }

    [Fact]
    public void ChartSpawn_TakesTheChartLeash()
    {
        var h = new MapTestHarness(SpawnStore(range: 0, area: 3, chaseRange: 50));
        h.Service.InitMonsterSpawns();
        h.Service.RunMonsterRegen(0);

        Assert.Equal(50f, Assert.Single(h.State.AllMonsters()).ChaseRange);
    }

    // ---------------- roam destination ----------------

    private static Monster Roamer(float x, float z, float area)
        => new() { Id = 0x30001, ChartId = 500, Level = 5, MaxHp = 100, Hp = 100, MaxMp = 50, Mp = 50,
            PosX = x, PosZ = z, StartX = 100, StartY = 0, StartZ = 100, Area = area, RoamNextMs = 0, Mode = 0,
            Region = 7, Channel = 1, MapId = 0 };

    private static async Task<(float x, float z)> RoamTarget(Monster mon)
    {
        var h = new MapTestHarness();
        var (_, c) = await h.EnterAsync(1, 1, 1, x: 100, z: 100);
        h.Service.SpawnRng = new Random(1);
        h.Service.SpawnMonster(mon);
        c.Clear();
        h.Service.RunMonsterAI(10_000);
        var r = new PacketReader(c.Last(Msg.CS_MONACTION_ACK)!);
        r.ReadUInt32(); r.ReadByte();
        float x = r.ReadFloat(); r.ReadFloat(); float z = r.ReadFloat();
        return (x, z);
    }

    [Fact]
    public async Task InsideItsArea_ItPicksAPointOnTheCircle()
    {
        var (x, z) = await RoamTarget(Roamer(101, 100, area: 4));
        Assert.InRange(MathF.Sqrt((x - 100) * (x - 100) + (z - 100) * (z - 100)), 3.9f, 4.1f);
    }

    [Fact]
    public async Task OutsideItsArea_ItWalksBackToTheAnchor()
    {
        // C++ MoveNext bGo: drifted past bArea ⇒ the destination is the anchor itself.
        var (x, z) = await RoamTarget(Roamer(110, 100, area: 4));
        Assert.Equal(100f, x);
        Assert.Equal(100f, z);
    }

    // ---------------- the leash ----------------

    private static byte[] MonMove(uint monId, float x, float z)
    {
        var w = new PacketWriter(Msg.CS_MONMOVE_REQ);
        w.WriteUInt16(1); w.WriteUInt32(monId); w.WriteByte(2); w.WriteByte(1); w.WriteUInt16(0);
        w.WriteFloat(x); w.WriteFloat(0); w.WriteFloat(z);
        w.WriteUInt16(0); w.WriteUInt16(0); w.WriteByte(0); w.WriteByte(0); w.WriteByte(9);   // TA_FOLLOW
        return w.ToArray();
    }

    private static Monster Fighter(float chaseRange, AiScript? ai = null) => new()
    {
        Id = 0x50001, ChartId = 500, Level = 5, MaxHp = 100, Hp = 100, PosX = 100, PosZ = 100,
        StartX = 100, StartZ = 100, Mode = 1 /* MT_BATTLE */, TargetId = 1, TargetType = 1, HostId = 1,
        Status = 1, Country = 3, ChaseRange = chaseRange, Channel = 1, MapId = 0, Region = 7, Ai = ai,
    };

    [Fact]
    public async Task PulledWithinTheLeash_KeepsFighting()
    {
        var h = new MapTestHarness();
        var (s, _) = await h.EnterAsync(1, 1, 1, x: 100, z: 100);
        var mon = Fighter(chaseRange: 50);
        h.Service.SpawnMonster(mon);

        await h.Service.DispatchClientAsync(s, MonMove(mon.Id, 140, 100));   // 40 from the anchor

        Assert.Equal(1, mon.Mode);
    }

    [Fact]
    public async Task PulledPastTheLeash_GivesUp()
    {
        var h = new MapTestHarness();
        var (s, _) = await h.EnterAsync(1, 1, 1, x: 100, z: 100);
        var mon = Fighter(chaseRange: 50);
        h.Service.SpawnMonster(mon);

        await h.Service.DispatchClientAsync(s, MonMove(mon.Id, 160, 100));   // 60 from the anchor

        Assert.NotEqual(1, mon.Mode);   // the old fixed 800 would have kept it chasing
    }

    [Fact]
    public async Task AScriptedMonsterGivingUp_GoesHome_KeepingItsHost()
    {
        // Script 1's shape: AT_LEAVELB → ChgMode (BATTLE → GOHOME).
        var chg = new AiCommandTemplate(16, AiCommandKind.ChgMode);
        var script = new AiScript(1);
        script.Bind((byte)AiTrigger.LeaveLb, 0, new AiBinding(chg, 0, false));

        var h = new MapTestHarness();
        var (s, _) = await h.EnterAsync(1, 1, 1, x: 100, z: 100);
        var mon = Fighter(chaseRange: 50, ai: script);
        h.Service.SpawnMonster(mon);

        await h.Service.DispatchClientAsync(s, MonMove(mon.Id, 160, 100));

        Assert.Equal(2, mon.Mode);        // MT_GOHOME, not a hard reset to MT_NORMAL
        Assert.Equal(1u, mon.HostId);     // its host client keeps driving it home
        Assert.Equal(0u, mon.TargetId);   // ChgMode cleared the target
    }
}
