using TMap.Data;
using TMap.Protocol;
using TMap.Server.Map;
using Xunit;

namespace TMap.Server.Tests;

/// <summary>Phase 12 — the monster spawn pipeline: SE_DEFAULT spawns build slots at bring-up and the regen
/// tick (prob roll → weighted type pick → position → vitals) produces monsters that plug into the Phase-11
/// visibility layer.</summary>
public class MonsterSpawnTests
{
    // A one-spawn store: spawn id 7 at (x,z) on map 0, `count` slots, monster type 500 (level 5, MaxHP 200).
    private static TemplateStore SpawnStore(byte count = 2, byte range = 0, byte prob = 100, uint delay = 0,
        byte evt = 0, byte essential = 0, uint maxHp = 200, float x = 100, float z = 100, uint dp = 0)
    {
        const ushort monId = 500, attrId = 900;
        const byte level = 5;
        var t = new TemplateStore();
        t.MonsterTemplates[monId] = new MonsterTemplate(monId, level, attrId);
        t.MonAttrs[TemplateStore.MonAttrKey(attrId, level)] = new MonAttrRow(attrId, level, maxHp, 50, dp);
        t.MonsterSpawns.Add(new MonsterSpawnDef(
            new MonSpawnRow(Id: 7, MapId: 0, PosX: x, PosY: 0, PosZ: z, Dir: 0, Country: 0,
                Count: count, Range: range, Prob: prob, Region: 7, Delay: delay, Event: evt),
            new List<MapMonRow> { new(SpawnId: 7, MonId: monId, Leader: 0, Essential: essential, Prob: 100) }));
        return t;
    }

    [Fact]
    public void DefaultSpawn_ProducesCountMonsters_AtAnchor()
    {
        var h = new MapTestHarness(SpawnStore(count: 2));
        h.Service.InitMonsterSpawns();
        h.Service.RunMonsterRegen(0);

        Assert.Equal(2, h.State.AllMonsters().Count());
        var m = h.State.FindMonster(Monster.MakeId(spawnId: 7, channel: 1, slot: 0));
        Assert.NotNull(m);
        Assert.Equal((ushort)500, m!.ChartId);
        Assert.Equal((byte)5, m.Level);
        Assert.Equal(200u, m.MaxHp);
        Assert.Equal(200u, m.Hp);       // spawned at full HP
        Assert.Equal(100f, m.PosX);
        Assert.Equal(100f, m.PosZ);
        Assert.NotNull(h.State.FindMonster(Monster.MakeId(7, 1, 1))); // second slot too
    }

    [Fact]
    public void NonDefaultEventSpawn_IsIgnored()
    {
        var h = new MapTestHarness(SpawnStore(evt: 1)); // not SE_DEFAULT
        h.Service.InitMonsterSpawns();
        h.Service.RunMonsterRegen(0);
        Assert.Empty(h.State.AllMonsters());
    }

    [Fact]
    public void ZeroProbability_NeverSpawns()
    {
        var h = new MapTestHarness(SpawnStore(prob: 0));
        h.Service.InitMonsterSpawns();
        h.Service.RunMonsterRegen(0);
        Assert.Empty(h.State.AllMonsters());
    }

    [Fact]
    public void Delay_GatesTheInitialSpawn()
    {
        var h = new MapTestHarness(SpawnStore(count: 1, delay: 5000));
        h.Service.InitMonsterSpawns();

        h.Service.RunMonsterRegen(1000);
        Assert.Empty(h.State.AllMonsters());   // delay not elapsed

        h.Service.RunMonsterRegen(5000);
        Assert.Single(h.State.AllMonsters());  // now due
    }

    [Fact]
    public void MissingAttrRow_SkipsSpawn()
    {
        var t = SpawnStore(count: 1);
        t.MonAttrs.Clear();                     // template present, no attr ⇒ C++ FindMonAttr null ⇒ FALSE
        var h = new MapTestHarness(t);
        h.Service.InitMonsterSpawns();
        h.Service.RunMonsterRegen(0);
        Assert.Empty(h.State.AllMonsters());
    }

    [Fact]
    public void RangeScatter_PlacesWithinRadius()
    {
        var h = new MapTestHarness(SpawnStore(count: 1, range: 10, x: 500, z: 500)) ;
        h.Service.SpawnRng = new Random(12345);
        h.Service.InitMonsterSpawns();
        h.Service.RunMonsterRegen(0);

        var m = h.State.AllMonsters().Single();
        float dx = m.PosX - 500f, dz = m.PosZ - 500f;
        Assert.True(dx * dx + dz * dz <= 10f * 10f);   // within the scatter radius
    }

    [Fact]
    public void EssentialOnlyTypes_DoNotSpawn()
    {
        // Essential monsters are spawned by the C++ immediate path (deferred); the weighted pick excludes them.
        var h = new MapTestHarness(SpawnStore(count: 1, essential: 1));
        h.Service.InitMonsterSpawns();
        h.Service.RunMonsterRegen(0);
        Assert.Empty(h.State.AllMonsters());
    }

    [Fact]
    public async Task SpawnedMonster_IsAnnouncedToNearbyPlayer()
    {
        var h = new MapTestHarness(SpawnStore(count: 1, x: 100, z: 100));
        var (s, c) = await h.EnterAsync(1, 1, 1, x: 100, z: 100); // player standing on the spawn anchor
        c.Clear();

        h.Service.InitMonsterSpawns();
        h.Service.RunMonsterRegen(0);

        var add = c.Last(Msg.CS_ADDMON_ACK);
        Assert.NotNull(add);
        var r = new PacketReader(add!);
        Assert.Equal(Monster.MakeId(7, 1, 0), r.ReadUInt32()); // the spawned monster's id
    }
}
