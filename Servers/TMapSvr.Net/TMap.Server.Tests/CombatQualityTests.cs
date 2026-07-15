using TMap.Data;
using TMap.Server.Map;
using Xunit;

namespace TMap.Server.Tests;

/// <summary>Phase 28 — combat quality: the C++ <c>GetAtkHitType</c> hit-type roll (miss / normal / crit) and
/// the <c>FTYPE_PCD</c>/<c>FTYPE_MCD</c> crit-damage formula, as pure static functions on <see cref="MapService"/>.
/// The always-hit / never-crit / full-crit constructions are seed-independent (the roll is compared against 0 or
/// ≥ 100); the probability behaviour is checked over a fixed 20 000-roll sample (deterministic — one seeded
/// <see cref="System.Random"/>). The physical-vs-magic branch selection + the DEFEND_ACK wiring are exercised by
/// the existing combat/monster-attack suites (which still pass).</summary>
public class CombatQualityTests
{
    private const byte HtMiss = 0, HtNormal = 1, HtCritical = 2;

    // A PAR/MAR formula that always connects: with nL = 0 and defLevel = 0, val = RateY·100 = 200, capped to
    // Init (100) ⇒ dwAtk = 100 ⇒ every rng.Next(100) < 100.
    private static readonly FormulaRow AlwaysHit = new(Init: 100, RateX: 1.0f, RateY: 2.0f);

    // ---------------- HitTypeVsMonster ----------------

    [Fact]
    public void HitTypeVsMonster_NullFormula_AlwaysNormal()   // DB-free: no chart ⇒ connects, no crit possible
        => Assert.Equal(HtNormal, MapService.HitTypeVsMonster(new Random(1), null, 10, 10, 0, critRate: 100, attackLevel: 50));

    [Fact]
    public void HitTypeVsMonster_CritRateSentinel_Miss()      // bCR == 0xFF ⇒ cannot hit
        => Assert.Equal(HtMiss, MapService.HitTypeVsMonster(new Random(1), AlwaysHit, 10, 10, 0, critRate: 0xFF, attackLevel: 50));

    [Fact]
    public void HitTypeVsMonster_AlwaysHit_NoCrit_Normal()    // dwAtk = 100, crit 0 ⇒ every roll is a normal hit
    {
        var rng = new Random(7);
        for (int i = 0; i < 500; i++)
            Assert.Equal(HtNormal, MapService.HitTypeVsMonster(rng, AlwaysHit, 10, 10, monsterDefLevel: 0, critRate: 0, attackLevel: 50));
    }

    [Fact]
    public void HitTypeVsMonster_AlwaysHit_FullCrit_Critical() // dwAtk = 100, crit 100 ⇒ every hit crits
    {
        var rng = new Random(7);
        for (int i = 0; i < 500; i++)
            Assert.Equal(HtCritical, MapService.HitTypeVsMonster(rng, AlwaysHit, 10, 10, monsterDefLevel: 0, critRate: 100, attackLevel: 50));
    }

    [Fact]
    public void HitTypeVsMonster_HighDefendLevel_FloorsHitRateTo20Percent()
    {
        // A huge defend-level vs attack-level drives the formula below 0 ⇒ dwAtk clamps to the 20 floor.
        var rng = new Random(12345);
        int hits = 0, n = 20_000;
        for (int i = 0; i < n; i++)
            if (MapService.HitTypeVsMonster(rng, AlwaysHit, 10, 10, monsterDefLevel: 100_000, critRate: 0, attackLevel: 1) != HtMiss)
                hits++;
        double rate = hits * 100.0 / n;
        Assert.InRange(rate, 17.0, 23.0);   // ~20% (the clamp floor)
    }

    [Fact]
    public void HitTypeVsMonster_CritRate_SplitsHits()
    {
        // Always-hit, crit 30% ⇒ ~30% of the (always landing) swings crit.
        var rng = new Random(999);
        int crits = 0, n = 20_000;
        for (int i = 0; i < n; i++)
            if (MapService.HitTypeVsMonster(rng, AlwaysHit, 10, 10, monsterDefLevel: 0, critRate: 30, attackLevel: 50) == HtCritical)
                crits++;
        double rate = crits * 100.0 / n;
        Assert.InRange(rate, 27.0, 33.0);
    }

    // ---------------- HitTypeVsPlayer ----------------

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void HitTypeVsPlayer_AttackLevel0Or1_Miss(ushort al)   // C++ !wAL || wAL == 1
        => Assert.Equal(HtMiss, MapService.HitTypeVsPlayer(new Random(1), critRate: 100, attackLevel: al));

    [Fact]
    public void HitTypeVsPlayer_Connects_NoCrit()    // a PC is never dodged; crit 0 ⇒ normal
    {
        var rng = new Random(3);
        for (int i = 0; i < 500; i++)
            Assert.Equal(HtNormal, MapService.HitTypeVsPlayer(rng, critRate: 0, attackLevel: 20));
    }

    [Fact]
    public void HitTypeVsPlayer_FullCrit()
        => Assert.Equal(HtCritical, MapService.HitTypeVsPlayer(new Random(3), critRate: 100, attackLevel: 20));

    // ---------------- CritDamage ----------------

    [Fact]
    public void CritDamage_AppliesFormulaBonus()
    {
        // FTYPE_PCD-style: Init 1 ⇒ rand%max(1,1) = Next(1) = 0 always; RateX 20 ⇒ bonus = base·(20+0)/100.
        var f = new FormulaRow(Init: 1, RateX: 20f, RateY: 0f);
        Assert.Equal(120u, MapService.CritDamage(new Random(1), f, baseVal: 100)); // 100 + 100·20/100
        Assert.Equal(240u, MapService.CritDamage(new Random(1), f, baseVal: 200)); // 200 + 200·20/100
    }

    [Fact]
    public void CritDamage_BonusScalesWithInitRoll()
    {
        // Live PCD/MCD (Init 21, RateX 20): the bonus is base·(20 + rand%21)/100 ⇒ +20%..+40% of base.
        var f = new FormulaRow(Init: 21, RateX: 20f, RateY: 0f);
        var rng = new Random(42);
        for (int i = 0; i < 2000; i++)
        {
            uint dmg = MapService.CritDamage(rng, f, baseVal: 1000);
            Assert.InRange(dmg, 1200u, 1400u);   // 1000 + 1000·(20..40)/100
        }
    }

    [Fact]
    public void CritDamage_NullFormula_NoBonus()
        => Assert.Equal(100u, MapService.CritDamage(new Random(1), null, baseVal: 100)); // rateX 0, init 0 ⇒ base unchanged
}
