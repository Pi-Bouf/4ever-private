using TMap.Data;
using TMap.Protocol;
using TMap.Server.Map;
using Xunit;

namespace TMap.Server.Tests;

/// <summary>Phase 21 — player revival (CS_REVIVAL): a dead player revives at a chosen point with a fraction
/// of HP/MP by type (NPC 30% / GHOST 40%, HP ≥ 1), back to MT_NORMAL, broadcasting CS_REVIVAL_ACK +
/// CS_HPMP_ACK. A live player's request is ignored.</summary>
public class PlayerRevivalTests
{
    // MaxHP 200 / MaxMP 100.
    private static TemplateStore VitalStore(uint maxHp = 200)
    {
        var t = new TemplateStore { Rate1st = 1.0f };
        t.Formulas[8] = new FormulaRow(maxHp, 0f, 0f);
        t.Formulas[19] = new FormulaRow(100, 0f, 0f);
        t.Classes[1] = new StatSeed(1, 1, 1, 1, 1, 1);
        t.Races[1] = new StatSeed(1, 1, 1, 1, 1, 1);
        return t;
    }

    private static Character Corpse()
        => new() { CharId = 9, Name = "Ghost", Class = 1, Race = 1, Hp = 0, Mp = 0 };

    private static async Task<(MapTestHarness h, ClientSession s, FakeClientChannel c, Character ch)> Enter(
        Character ch, uint maxHp = 200)
    {
        var h = new MapTestHarness(VitalStore(maxHp));
        var (s, c) = await h.EnterAsync(9, 1, 1, name: "Ghost", x: 100, z: 100, preSeeded: ch);
        c.Clear();
        return (h, s, c, ch);
    }

    [Fact]
    public async Task Revive_Npc_Restores30Percent_AndBroadcasts()
    {
        var (h, s, c, ch) = await Enter(Corpse());

        await h.Service.DispatchClientAsync(s, MapTestHarness.RevivalReq(200, 0, 300, RevivalType.Npc));

        Assert.Equal(60u, ch.Hp);   // 30% of 200
        Assert.Equal(30u, ch.Mp);   // 30% of 100
        Assert.Equal((byte)0, ch.Mode); // MT_NORMAL
        Assert.True(c.Has(Msg.CS_REVIVAL_ACK));
        Assert.True(c.Has(Msg.CS_HPMP_ACK));
    }

    [Fact]
    public async Task Revive_Ghost_Restores40Percent()
    {
        var (h, s, c, ch) = await Enter(Corpse());

        await h.Service.DispatchClientAsync(s, MapTestHarness.RevivalReq(100, 0, 100, RevivalType.Ghost));

        Assert.Equal(80u, ch.Hp);   // 40% of 200
        Assert.Equal(40u, ch.Mp);   // 40% of 100
    }

    [Fact]
    public async Task Revive_RepositionsPlayer_AndAckCarriesPos()
    {
        var (h, s, c, ch) = await Enter(Corpse());

        await h.Service.DispatchClientAsync(s, MapTestHarness.RevivalReq(500, 600, 700, RevivalType.Npc));

        Assert.Equal(500f, ch.PosX);
        Assert.Equal(600f, ch.PosY);
        Assert.Equal(700f, ch.PosZ);

        var r = new PacketReader(c.Last(Msg.CS_REVIVAL_ACK)!);
        Assert.Equal(9u, r.ReadUInt32());   // dwCharID
        Assert.Equal(500f, r.ReadFloat());  // fPosX
        Assert.Equal(600f, r.ReadFloat());  // fPosY
        Assert.Equal(700f, r.ReadFloat());  // fPosZ
    }

    [Fact]
    public async Task Revive_HpClampsToOne_WhenFractionTruncatesToZero()
    {
        var (h, s, c, ch) = await Enter(Corpse(), maxHp: 1); // 1 × 0.3 = 0 → clamp to 1

        await h.Service.DispatchClientAsync(s, MapTestHarness.RevivalReq(100, 0, 100, RevivalType.Npc));

        Assert.Equal(1u, ch.Hp);
    }

    [Fact]
    public async Task Revive_LivePlayer_Ignored()
    {
        var ch = Corpse();
        ch.Hp = 150; // alive
        var (h, s, c, _) = await Enter(ch);

        await h.Service.DispatchClientAsync(s, MapTestHarness.RevivalReq(100, 0, 100, RevivalType.Npc));

        Assert.Equal(150u, ch.Hp);              // unchanged
        Assert.False(c.Has(Msg.CS_REVIVAL_ACK));
    }

    [Fact]
    public async Task Revive_ThenRegenResumes()
    {
        // After revival the player is NORMAL + alive, so Phase-16 HP regen ticks again.
        var t = VitalStore();
        t.Formulas[9] = new FormulaRow(10, 0f, 0f);   // FTYPE_HPR +10
        var h = new MapTestHarness(t);
        var (s, c) = await h.EnterAsync(9, 1, 1, name: "Ghost", x: 100, z: 100, preSeeded: Corpse());
        c.Clear();

        await h.Service.DispatchClientAsync(s, MapTestHarness.RevivalReq(100, 0, 100, RevivalType.Npc));
        var ch = s.Char!;
        Assert.Equal(60u, ch.Hp);                     // revived at 30%
        h.Service.NowMs = 100_000;
        ch.RecoverHpTick = 0;                          // due
        h.Service.RunRecover(100_000);
        Assert.Equal(70u, ch.Hp);                      // +10 regen (alive + NORMAL)
    }
}
