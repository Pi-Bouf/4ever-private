using TMap.Data;
using TMap.Protocol;
using TMap.Server.Map;
using Xunit;

namespace TMap.Server.Tests;

/// <summary>Phase 16 — HP/MP regen (Recover): players regen HP (NORMAL-only) + MP (always) by the flat
/// GetHPR/GetMPR amount every RECOVER_TIME; monsters regen 25%/tick (non-BATTLE); combat suppresses HP via
/// battle mode + anchor reset; a change broadcasts CS_HPMP_ACK.</summary>
public class RecoverTests
{
    private const uint RecoverInit = 5000;

    // MaxHP 200 / MaxMP 100; HP regen +10 (FTYPE_HPR init 10), MP regen +5 (FTYPE_MPR init 5).
    private static TemplateStore VitalStore()
    {
        var t = new TemplateStore { Rate1st = 1.0f };
        t.Formulas[8] = new FormulaRow(200, 0f, 0f);   // FTYPE_HP
        t.Formulas[9] = new FormulaRow(10, 0f, 0f);    // FTYPE_HPR
        t.Formulas[19] = new FormulaRow(100, 0f, 0f);  // FTYPE_MP
        t.Formulas[20] = new FormulaRow(5, 0f, 0f);    // FTYPE_MPR
        t.Classes[1] = new StatSeed(1, 1, 1, 1, 1, 1);
        t.Races[1] = new StatSeed(1, 1, 1, 1, 1, 1);
        return t;
    }

    private static Character Wounded(uint hp, uint mp)
        => new() { CharId = 9, Name = "Hurt", Class = 1, Race = 1, Hp = hp, Mp = mp };

    private static async Task<(MapTestHarness h, ClientSession s, FakeClientChannel c)> Enter(Character ch)
    {
        var h = new MapTestHarness(VitalStore());
        var (s, c) = await h.EnterAsync(9, 1, 1, name: "Hurt", preSeeded: ch);
        c.Clear();
        return (h, s, c);
    }

    private static Monster Mob(uint id, uint maxHp = 100, uint hp = 50)
        => new() { Id = id, ChartId = 500, Level = 5, MaxHp = maxHp, Hp = hp, MaxMp = 100, Mp = 50,
            DefendPower = 100, PosX = 100, PosZ = 100, Region = 7, Channel = 1, MapId = 0, Mode = 0 };

    // ---------------- players ----------------

    [Fact]
    public async Task Player_RegensHpAndMp_OnDueTick()
    {
        var ch = Wounded(hp: 50, mp: 50);
        var (h, s, c) = await Enter(ch);

        h.Service.RunRecover(3000); // now >= 0 + RECOVER_TIME(3000)

        Assert.Equal(60u, ch.Hp);   // 50 + 10
        Assert.Equal(55u, ch.Mp);   // 50 + 5
        Assert.True(c.Has(Msg.CS_HPMP_ACK));
    }

    [Fact]
    public async Task Player_HpClampsToMax()
    {
        var ch = Wounded(hp: 195, mp: 100);
        var (h, s, c) = await Enter(ch);

        h.Service.RunRecover(3000);

        Assert.Equal(200u, ch.Hp);  // 195 + 10 clamped to MaxHP 200
    }

    [Fact]
    public async Task Player_NoRegenWhenFull_NoBroadcast()
    {
        var ch = Wounded(hp: 200, mp: 100);
        var (h, s, c) = await Enter(ch);

        h.Service.RunRecover(3000);

        Assert.Equal(200u, ch.Hp);
        Assert.Equal(100u, ch.Mp);
        Assert.False(c.Has(Msg.CS_HPMP_ACK)); // nothing changed
    }

    [Fact]
    public async Task Player_NotYetDue_NoRegen()
    {
        var ch = Wounded(hp: 50, mp: 50);
        var (h, s, c) = await Enter(ch);

        h.Service.RunRecover(2999); // just under RECOVER_TIME

        Assert.Equal(50u, ch.Hp);
        Assert.Equal(50u, ch.Mp);
    }

    [Fact]
    public async Task Player_HpSuppressedInBattle_ButMpContinues()
    {
        var ch = Wounded(hp: 50, mp: 50);
        var (h, s, c) = await Enter(ch);
        ch.Mode = 1;                 // MT_BATTLE (MP ungated, HP gated to NORMAL)

        h.Service.RunRecover(3000);

        Assert.Equal(50u, ch.Hp);    // HP suppressed while in battle
        Assert.Equal(55u, ch.Mp);    // MP still regenerates
    }

    [Fact]
    public async Task Player_LeavesBattleAfterTimeout_ThenHpResumes()
    {
        var ch = Wounded(hp: 50, mp: 100);
        var (h, s, c) = await Enter(ch);
        ch.EnterBattle(1000, RecoverInit); // Mode=BATTLE, anchors=6000, lastAtk=1000

        h.Service.RunRecover(3000);
        Assert.Equal((byte)1, ch.Mode);    // still in battle (1000 + 5000 not < 3000)
        Assert.Equal(50u, ch.Hp);          // HP suppressed

        h.Service.RunRecover(6001);        // 1000 + 5000 < 6001 ⇒ battle→normal (after the recover pass)
        Assert.Equal((byte)0, ch.Mode);
        Assert.Equal(50u, ch.Hp);          // anchor is 6000; 6001 < 6000+3000 ⇒ not yet due

        h.Service.RunRecover(9000);        // now NORMAL and due (9000 >= 6000+3000)
        Assert.Equal(60u, ch.Hp);
    }

    // ---------------- monsters ----------------

    [Fact]
    public async Task Monster_Regens25PercentOfMax_AndBroadcasts()
    {
        var h = new MapTestHarness(VitalStore());
        var (s, c) = await h.EnterAsync(1, 1, 1, x: 100, z: 100); // watcher in view
        var mob = Mob(0x20001, maxHp: 100, hp: 50);
        h.Service.SpawnMonster(mob);
        c.Clear();

        h.Service.RunRecover(3000);

        Assert.Equal(75u, mob.Hp);   // 50 + 100/4
        Assert.True(c.Has(Msg.CS_HPMP_ACK));
    }

    [Fact]
    public async Task Monster_HpSuppressedInBattle()
    {
        var h = new MapTestHarness(VitalStore());
        await h.EnterAsync(1, 1, 1, x: 100, z: 100);
        var mob = Mob(0x20002, maxHp: 100, hp: 40);
        mob.Mode = 1; // MT_BATTLE
        h.Service.SpawnMonster(mob);

        h.Service.RunRecover(3000);

        Assert.Equal(40u, mob.Hp);   // monster HP regen suppressed while MT_BATTLE
    }

    [Fact]
    public async Task Hit_EntersBattle_SuppressesMonsterRegen_ThenHealsWhenDisengaged()
    {
        var h = new MapTestHarness(VitalStore());
        var (s, c) = await h.EnterAsync(1, 1, 1, x: 100, z: 100);
        var mob = Mob(0x20003, maxHp: 100, hp: 100);
        h.Service.SpawnMonster(mob);
        h.Service.CombatRng = new Random(1);
        h.Service.NowMs = 1000;
        c.Clear();

        await h.Service.DispatchClientAsync(s, MapTestHarness.DefendReq(1, 0x20003)); // damages + battle
        Assert.Equal((byte)1, mob.Mode);      // monster entered battle on being hit
        uint hurt = mob.Hp;
        Assert.True(hurt < 100);

        h.Service.RunRecover(3000);
        Assert.Equal(hurt, mob.Hp);            // suppressed while in battle

        mob.Mode = 0;                          // disengaged (Phase-19 AI drops aggro → MT_NORMAL)
        h.Service.RunRecover(9000);            // now NORMAL and due ⇒ HP regen
        Assert.True(mob.Hp > hurt);
    }

    // ---------------- direct: GetHPR/GetMPR formula ----------------

    [Fact]
    public void HpMpRecover_ComesFromFormula()
    {
        var t = VitalStore();
        var ch = Wounded(50, 50);
        Assert.Equal(10u, StatEngine.HpRecover(ch, t)); // FTYPE_HPR init 10
        Assert.Equal(5u, StatEngine.MpRecover(ch, t));  // FTYPE_MPR init 5
    }
}
