using TMap.Protocol;
using TMap.Server.Map;
using Xunit;

namespace TMap.Server.Tests;

/// <summary>Phase 18 — monster idle roam: on its roam timer, an alive/idle monster a player can see picks a
/// destination on its spawn-radius circle and broadcasts CS_MONACTION_ACK (WALK + dest) to viewers; it stays
/// dormant with no viewer, and doesn't roam in battle / when dead / before the timer is due.</summary>
public class MonsterRoamTests
{
    private static Monster Roamer(float area = 10f, long roamNext = 0, byte mode = 0, uint hp = 100)
        => new() { Id = 0x30001, ChartId = 500, Level = 5, MaxHp = 100, Hp = hp, MaxMp = 50, Mp = 50,
            DefendPower = 10, PosX = 100, PosZ = 100, StartX = 100, StartY = 0, StartZ = 100,
            Area = area, RoamNextMs = roamNext, Mode = mode, Region = 7, Channel = 1, MapId = 0 };

    private static async Task<(MapTestHarness h, FakeClientChannel c)> WithViewer(Monster mon,
        float vx = 100, float vz = 100)
    {
        var h = new MapTestHarness();
        var (_, c) = await h.EnterAsync(1, 1, 1, x: vx, z: vz);
        h.Service.SpawnRng = new Random(1);
        h.Service.SpawnMonster(mon);
        c.Clear();
        return (h, c);
    }

    [Fact]
    public async Task Roam_BroadcastsMonAction_OnCircleRadius()
    {
        var mon = Roamer(area: 10f);
        var (h, c) = await WithViewer(mon);

        h.Service.RunMonsterAI(10_000);

        var r = new PacketReader(c.Last(Msg.CS_MONACTION_ACK)!);
        Assert.Equal(0x30001u, r.ReadUInt32());     // dwMonID
        Assert.Equal((byte)3, r.ReadByte());        // bAction TA_WALK
        float x = r.ReadFloat(); float y = r.ReadFloat(); float z = r.ReadFloat();
        Assert.Equal(0u, r.ReadUInt32());           // dwTargetID (none)
        Assert.Equal((byte)0, r.ReadByte());        // bTargetType
        float dist = MathF.Sqrt((x - 100) * (x - 100) + (z - 100) * (z - 100));
        Assert.InRange(dist, 9.9f, 10.1f);          // a point on the radius-10 circle around the anchor
    }

    [Fact]
    public async Task Roam_NoViewer_Dormant()
    {
        var mon = Roamer(area: 10f);
        var (h, c) = await WithViewer(mon, vx: 5000, vz: 5000); // viewer far outside the monster's 3×3

        h.Service.RunMonsterAI(10_000);

        Assert.False(c.Has(Msg.CS_MONACTION_ACK));
    }

    [Fact]
    public async Task Battle_NoTarget_DropsAggro_NoRoamThatTick()
    {
        var mon = Roamer(area: 10f, mode: 1); // MT_BATTLE but no aggro target
        var (h, c) = await WithViewer(mon);

        h.Service.RunMonsterAI(10_000);

        Assert.False(c.Has(Msg.CS_MONACTION_ACK)); // drop-aggro is silent
        Assert.Equal((byte)0, mon.Mode);           // back to MT_NORMAL
    }

    [Fact]
    public async Task Roam_Dead_DoesNotRoam()
    {
        var mon = Roamer(area: 10f, hp: 0);
        var (h, c) = await WithViewer(mon);

        h.Service.RunMonsterAI(10_000);

        Assert.False(c.Has(Msg.CS_MONACTION_ACK));
    }

    [Fact]
    public async Task Roam_ZeroRadius_DoesNotRoam()
    {
        var mon = Roamer(area: 0f);
        var (h, c) = await WithViewer(mon);

        h.Service.RunMonsterAI(10_000);

        Assert.False(c.Has(Msg.CS_MONACTION_ACK));
    }

    [Fact]
    public async Task Roam_RespectsTimer()
    {
        var mon = Roamer(area: 10f, roamNext: 10_000);
        var (h, c) = await WithViewer(mon);

        h.Service.RunMonsterAI(5_000);              // before the roam is due
        Assert.False(c.Has(Msg.CS_MONACTION_ACK));

        h.Service.RunMonsterAI(10_000);             // due
        Assert.True(c.Has(Msg.CS_MONACTION_ACK));
    }
}
