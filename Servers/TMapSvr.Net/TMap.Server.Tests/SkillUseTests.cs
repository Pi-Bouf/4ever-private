using TMap.Data;
using TMap.Protocol;
using TMap.Server.Map;
using Xunit;

namespace TMap.Server.Tests;

/// <summary>Phase 14 — CS_SKILLUSE (the caster-side attack announce): skill-known / MP (&lt;) / HP (&lt;=) /
/// cooldown guards, deduct the caster's own HP/MP, arm the reuse cooldown, and broadcast CS_SKILLUSE_ACK
/// (+ CS_HPMP_ACK on a nonzero cost) to the near players. Plus direct unit tests of the ported
/// GetRequiredMP/HP + reuse-cooldown math.</summary>
public class SkillUseTests
{
    private const ushort Sid = 100;

    // Flat vitals: MaxHP/PureMaxHP = 200, MaxMP/PureMaxMP = 100 (RateX 0 ⇒ stat-independent).
    private static TemplateStore VitalStore()
    {
        var t = new TemplateStore { Rate1st = 1.0f };
        t.Formulas[8] = new FormulaRow(200, 0f, 0f);   // FTYPE_HP
        t.Formulas[19] = new FormulaRow(100, 0f, 0f);  // FTYPE_MP
        t.Classes[1] = new StatSeed(1, 1, 1, 1, 1, 1);
        t.Races[1] = new StatSeed(1, 1, 1, 1, 1, 1);
        return t;
    }

    // A skill template with sensible defaults; only the fields a test cares about are set.
    private static SkillTemplate Tmpl(ushort id = Sid, uint useMp = 0, byte useMpType = 0, uint useHp = 0,
        byte useHpType = 0, byte startLevel = 1, byte nextLevel = 0, uint reuseDelay = 0, int reuseDelayInc = 0,
        uint kindDelay = 0, byte kind = 0, byte speedApply = 1, float rate = 1f)
        => new(id, Kind: kind, UseMp: useMp, UseMpType: useMpType, UseHp: useHp, UseHpType: useHpType,
               StartLevel: startLevel, MaxLevel: 100, NextLevel: nextLevel, ReuseDelay: reuseDelay,
               ReuseDelayInc: reuseDelayInc, LoopDelay: 0, KindDelay: kindDelay, SpeedApply: speedApply,
               Positive: 0, MapId: 0, Rate1stX: rate);

    private static Character Caster(uint hp = 200, uint mp = 100)
        => new() { CharId = 9, Name = "Caster", Class = 1, Race = 1, Hp = hp, Mp = mp };

    private static Skill Learn(Character ch, SkillTemplate tmpl, byte level = 1)
    {
        var sk = new Skill { SkillId = tmpl.Id, Level = level, Template = tmpl };
        ch.Skills.Add(sk);
        return sk;
    }

    private static async Task<(MapTestHarness h, ClientSession s, FakeClientChannel c)> Enter(Character ch)
    {
        var h = new MapTestHarness(VitalStore());
        var (s, c) = await h.EnterAsync(9, 1, 1, name: "Caster", preSeeded: ch);
        c.Clear();
        return (h, s, c);
    }

    private static byte Result(FakeClientChannel c)
        => new PacketReader(c.Last(Msg.CS_SKILLUSE_ACK)!).ReadByte();

    // ---------------- handler: cost + broadcast ----------------

    [Fact]
    public async Task Cast_DeductsMpCost_AndBroadcastsAckAndHpMp()
    {
        var ch = Caster(mp: 100);
        Learn(ch, Tmpl(useMp: 30, useMpType: 2)); // 30% of PureMaxMP(100) = 30
        var (h, s, c) = await Enter(ch);

        await h.Service.DispatchClientAsync(s, MapTestHarness.SkillUseReq(9, Sid));

        Assert.Equal((byte)SkillUseResult.Success, Result(c));
        Assert.Equal(70u, ch.Mp);              // 100 − 30
        Assert.Equal(200u, ch.Hp);             // untouched
        Assert.True(c.Has(Msg.CS_HPMP_ACK));   // cost > 0 ⇒ bar broadcast
    }

    [Fact]
    public async Task Cast_ZeroCost_NoHpMpAck()
    {
        var ch = Caster(mp: 100);
        Learn(ch, Tmpl(useMpType: 0)); // free skill
        var (h, s, c) = await Enter(ch);

        await h.Service.DispatchClientAsync(s, MapTestHarness.SkillUseReq(9, Sid));

        Assert.Equal((byte)SkillUseResult.Success, Result(c));
        Assert.Equal(100u, ch.Mp);              // unchanged
        Assert.False(c.Has(Msg.CS_HPMP_ACK));   // no cost ⇒ no bar
    }

    [Fact]
    public async Task Cast_HpCost_DeductsHp()
    {
        var ch = Caster(hp: 200);
        Learn(ch, Tmpl(useHp: 25, useHpType: 2)); // 25% of PureMaxHP(200) = 50
        var (h, s, c) = await Enter(ch);

        await h.Service.DispatchClientAsync(s, MapTestHarness.SkillUseReq(9, Sid));

        Assert.Equal((byte)SkillUseResult.Success, Result(c));
        Assert.Equal(150u, ch.Hp);             // 200 − 50
        Assert.True(c.Has(Msg.CS_HPMP_ACK));
    }

    [Fact]
    public async Task NotEnoughMp_NeedMp_NoDeduct()
    {
        var ch = Caster(mp: 20);
        Learn(ch, Tmpl(useMp: 30, useMpType: 2)); // cost 30 > 20
        var (h, s, c) = await Enter(ch);

        await h.Service.DispatchClientAsync(s, MapTestHarness.SkillUseReq(9, Sid));

        Assert.Equal((byte)SkillUseResult.NeedMp, Result(c));
        Assert.Equal(20u, ch.Mp);              // untouched
        Assert.False(c.Has(Msg.CS_HPMP_ACK));
    }

    [Fact]
    public async Task NotEnoughHp_NeedHp_UsesLessOrEqual()
    {
        // HP guard is <= (can't drop to/below 0): cost 50 with Hp exactly 50 ⇒ NEEDHP.
        var ch = Caster(hp: 50);
        Learn(ch, Tmpl(useHp: 25, useHpType: 2)); // 25% of 200 = 50
        var (h, s, c) = await Enter(ch);

        await h.Service.DispatchClientAsync(s, MapTestHarness.SkillUseReq(9, Sid));

        Assert.Equal((byte)SkillUseResult.NeedHp, Result(c));
        Assert.Equal(50u, ch.Hp);              // untouched
    }

    [Fact]
    public async Task UnknownSkill_NotFound()
    {
        var ch = Caster();
        Learn(ch, Tmpl(id: Sid));              // knows 100
        var (h, s, c) = await Enter(ch);

        await h.Service.DispatchClientAsync(s, MapTestHarness.SkillUseReq(9, skillId: 999)); // casts 999

        Assert.Equal((byte)SkillUseResult.NotFound, Result(c));
    }

    [Fact]
    public async Task Recast_WhileOnCooldown_SpeedyUse()
    {
        var ch = Caster(mp: 100);
        Learn(ch, Tmpl(reuseDelay: 5000));     // free skill, 5s reuse
        var (h, s, c) = await Enter(ch);
        h.Service.NowMs = 100_000;             // nonzero clock so the arm is observable

        await h.Service.DispatchClientAsync(s, MapTestHarness.SkillUseReq(9, Sid)); // success, arms cooldown
        Assert.Equal((byte)SkillUseResult.Success, Result(c));
        c.Clear();

        await h.Service.DispatchClientAsync(s, MapTestHarness.SkillUseReq(9, Sid)); // same tick ⇒ still cooling
        Assert.Equal((byte)SkillUseResult.SpeedyUse, Result(c));
    }

    [Fact]
    public async Task Recast_AfterCooldown_Succeeds()
    {
        var ch = Caster(mp: 100);
        Learn(ch, Tmpl(reuseDelay: 5000));
        var (h, s, c) = await Enter(ch);
        h.Service.NowMs = 100_000;

        await h.Service.DispatchClientAsync(s, MapTestHarness.SkillUseReq(9, Sid));
        c.Clear();
        h.Service.NowMs = 105_000;             // 5000 elapsed ⇒ cooldown over

        await h.Service.DispatchClientAsync(s, MapTestHarness.SkillUseReq(9, Sid));
        Assert.Equal((byte)SkillUseResult.Success, Result(c));
    }

    [Fact]
    public async Task SkillUseAck_PayloadIsByteExact()
    {
        var ch = Caster();
        Learn(ch, Tmpl(), level: 7);           // skill level 7
        var (h, s, c) = await Enter(ch);
        ch.Country = 3; ch.AidCountry = 4;     // set after the enter handshake (which overlays country)

        var targets = new (uint, byte)[] { (0x20001u, 2) };
        await h.Service.DispatchClientAsync(s,
            MapTestHarness.SkillUseReq(9, Sid, actionId: 5, actId: 11, aniId: 22, targets: targets));

        var r = new PacketReader(c.Last(Msg.CS_SKILLUSE_ACK)!);
        Assert.Equal((byte)SkillUseResult.Success, r.ReadByte()); // bResult
        Assert.Equal(9u, r.ReadUInt32());        // dwAttackID
        Assert.Equal((byte)1, r.ReadByte());     // bAttackType OT_PC
        Assert.Equal(Sid, r.ReadUInt16());       // wSkillID
        Assert.Equal((ushort)0, r.ReadUInt16()); // wBackSkill (right after wSkillID)
        Assert.Equal((byte)5, r.ReadByte());     // bActionID
        Assert.Equal(11u, r.ReadUInt32());       // dwActID
        Assert.Equal(22u, r.ReadUInt32());       // dwAniID
        Assert.Equal((byte)7, r.ReadByte());     // bSkillLevel
        r.ReadUInt16();                          // wAttackLevel (0 — no AL formula)
        Assert.Equal((byte)10, r.ReadByte());    // bAttackerLevel (enter level 10)
        r.ReadUInt32(); r.ReadUInt32();          // dwPysMin/Max
        r.ReadUInt32(); r.ReadUInt32();          // dwMgMin/Max
        Assert.Equal((ushort)0, r.ReadUInt16()); // wTransHP
        Assert.Equal((ushort)0, r.ReadUInt16()); // wTransMP
        Assert.Equal((byte)0, r.ReadByte());     // bCurseProb
        Assert.Equal((byte)0, r.ReadByte());     // bEquipSpecial
        Assert.Equal((byte)1, r.ReadByte());     // bCanSelect
        Assert.Equal((byte)3, r.ReadByte());     // bAttackCountry == ch.Country
        Assert.Equal((byte)4, r.ReadByte());     // bAttackAidCountry == ch.AidCountry
        r.ReadByte();                            // bCP
        r.ReadFloat(); r.ReadFloat(); r.ReadFloat(); // fGndPos
        Assert.Equal((byte)1, r.ReadByte());     // bTargetCount
        Assert.Equal(0x20001u, r.ReadUInt32());  // target id echoed
        Assert.Equal((byte)2, r.ReadByte());     // target type echoed
    }

    [Fact]
    public async Task NonPcCaster_Ignored()
    {
        var ch = Caster();
        Learn(ch, Tmpl());
        var (h, s, c) = await Enter(ch);

        // attackType OT_MON (2) — summon/recall casters are deferred this phase ⇒ silent.
        await h.Service.DispatchClientAsync(s, MapTestHarness.SkillUseReq(9, Sid, attackType: 2));
        Assert.False(c.Has(Msg.CS_SKILLUSE_ACK));
    }

    // ---------------- direct unit tests: ported cost / cooldown math ----------------

    [Fact]
    public void RequiredMp_Type1_FlatScaledByRate()
    {
        // level 1, StartLevel 1, NextLevel 0 ⇒ exp = 1; rate 100 ⇒ CostRate = 100^1/100 = 1.0 ⇒ flat useMp.
        var sk = new Skill { Level = 1, Template = Tmpl(useMp: 50, useMpType: 1, startLevel: 1, nextLevel: 0, rate: 100f) };
        Assert.Equal(50u, sk.GetRequiredMp(9999)); // type 1 ignores maxMp
    }

    [Fact]
    public void RequiredMp_Type1_LevelScaled()
    {
        // level 2, StartLevel 2, NextLevel 1 ⇒ exp = 2 + 1 = 3; rate 10 ⇒ CostRate = 10^3/100 = 10 ⇒ useMp×10.
        var sk = new Skill { Level = 2, Template = Tmpl(useMp: 5, useMpType: 1, startLevel: 2, nextLevel: 1, rate: 10f) };
        Assert.Equal(50u, sk.GetRequiredMp(0));
    }

    [Fact]
    public void RequiredMp_Type2_PercentOfPureMax()
    {
        var sk = new Skill { Level = 1, Template = Tmpl(useMp: 30, useMpType: 2) };
        Assert.Equal(60u, sk.GetRequiredMp(200)); // 200 × 30 / 100
    }

    [Fact]
    public void RequiredHp_NoTemplate_IsZero()
        => Assert.Equal(0u, new Skill { Level = 1 }.GetRequiredHp(500));

    [Fact]
    public void Cooldown_ArmsAndExpires()
    {
        var sk = new Skill { Level = 1, Template = Tmpl(reuseDelay: 5000) };
        Assert.True(sk.CanUse(100_000));                    // fresh (never used)
        sk.UseSkill(100_000, atkSpeed: 0, rate: 100);       // delay = (5000+0)·100/100 = 5000
        Assert.False(sk.CanUse(100_000));
        Assert.Equal(5000u, sk.GetReuseRemainTick(100_000));
        Assert.False(sk.CanUse(104_999));                   // 1 remaining
        Assert.True(sk.CanUse(105_000));                    // elapsed
    }

    [Fact]
    public void Cooldown_ReDoesNotShortenExistingLongerCooldown()
    {
        // C++ Use() only overwrites when the new delay exceeds what's already remaining.
        var sk = new Skill { Level = 1, Template = Tmpl(reuseDelay: 5000) };
        sk.UseSkill(100_000, atkSpeed: 0, rate: 100);       // 5000 remaining
        sk.UseSkill(101_000, atkSpeed: 0, rate: 20);        // new delay 1000 < remaining 4000 ⇒ ignored
        Assert.Equal(4000u, sk.GetReuseRemainTick(101_000));
    }
}
