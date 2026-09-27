using TMap.Data;
using TMap.Protocol;
using TMap.Server.Map;
using Xunit;

namespace TMap.Server.Tests;

/// <summary>
/// The passive side of skills: weapon masteries (C++ <c>CTObjBase::CanEquip</c> MI_NOSKILL), the self-buffs that need a
/// weapon falling off with it (<c>CTPlayer::CheckEquipSkill</c>), and the remain skills' permanent rows
/// (<c>m_vRemainSkill</c> in <c>CalcAbilityValue</c>).
/// </summary>
public class PassiveSkillTests
{
    private const byte Backpack = 0xFF, Equip = 0xFE, Archer = 2, BowKind = 9;
    private const byte SaContinue = 1, SaBuff = 3, SaPassive = 4, SdtEquip = 0, SdtAbility = 1, SdtStatus = 6;
    private const byte SviIncrease = 1, SviDecrease = 2, MtypeMhp = 50, AtkMode = 7;

    private static SkillTemplate Skill(ushort id, uint weapon = 0, ushort posture = 0, SkillDataRow[]? rows = null)
    {
        var t = new SkillTemplate(id, Kind: 0, UseMp: 0, UseMpType: 0, UseHp: 0, UseHpType: 0,
            StartLevel: 1, MaxLevel: 1, NextLevel: 1, ReuseDelay: 0, ReuseDelayInc: 0, LoopDelay: 0, KindDelay: 0,
            SpeedApply: 0, Positive: 1, MapId: 0, WeaponId: weapon, Posture: posture);
        t.Data.AddRange(rows ?? System.Array.Empty<SkillDataRow>());
        return t;
    }

    private static SkillDataRow Row(byte action, byte type, byte exec, ushort value = 0, byte inc = SviIncrease)
        => new(action, type, 0, exec, inc, value, 0, 0);

    private static SkillTemplate BowMastery() => Skill(10, rows: new[] { Row(SaPassive, SdtEquip, BowKind) });

    private static Item Bow(byte slot)
        => new() { ItemSlot = slot, TemplateId = 6000, Count = 1,
                   Template = new ItemTemplate(6000, 0, new[] { 1f, 0f, 0f, 0f }, SlotId: 1u << 0,
                       ClassId: 1u << Archer, PrmSlot: 0, Kind: BowKind, EquipSkill: 1) };

    private static Character Archer1()
    {
        var ch = new Character { CharId = 9, Name = "Robin", Class = Archer, Level = 10 };
        ch.Invens.Add(new Inven { InvenId = Backpack });
        ch.Invens.Add(new Inven { InvenId = Equip });
        return ch;
    }

    private static MoveItemResult Result(FakeClientChannel c)
        => (MoveItemResult)new PacketReader(c.Last(Msg.CS_MOVEITEM_ACK)!).ReadByte();

    // ================================ weapon masteries ================================

    [Fact]
    public async Task AWeaponNeedingAMastery_CannotBeWornWithoutIt()
    {
        var ch = Archer1();
        ch.FindInven(Backpack)!.Items.Add(Bow(0));
        var h = new MapTestHarness();
        var (s, c) = await h.EnterAsync(9, 1, 1, name: "Robin", preSeeded: ch);

        await h.Service.DispatchClientAsync(s, MapTestHarness.MoveItemReq(Backpack, 0, Equip, 0, 1));

        Assert.Equal(MoveItemResult.NoSkill, Result(c));
        Assert.NotNull(ch.FindInven(Backpack)!.FindItem(0));
    }

    [Fact]
    public async Task TheMastery_LetsItBeWorn()
    {
        var ch = Archer1();
        ch.FindInven(Backpack)!.Items.Add(Bow(0));
        ch.Skills.Add(new Skill { SkillId = 10, Level = 1, Template = BowMastery() });
        var h = new MapTestHarness();
        var (s, c) = await h.EnterAsync(9, 1, 1, name: "Robin", preSeeded: ch);

        await h.Service.DispatchClientAsync(s, MapTestHarness.MoveItemReq(Backpack, 0, Equip, 0, 1));

        Assert.Equal(MoveItemResult.Success, Result(c));
        Assert.NotNull(ch.FindInven(Equip)!.FindItem(0));
    }

    // ================================ buffs tied to a weapon ================================

    private static MaintainSkill Buff(SkillTemplate t, uint attackId = 9)
        => new() { SkillId = t.Id, Level = 1, Template = t, AttackId = attackId, AttackType = 1 /* OT_PC */ };

    private static async Task<(MapTestHarness h, ClientSession s, FakeClientChannel c, Character ch)> ArmedArcher(
        params MaintainSkill[] buffs)
    {
        var ch = Archer1();
        ch.Skills.Add(new Skill { SkillId = 10, Level = 1, Template = BowMastery() });
        ch.FindInven(Equip)!.Items.Add(Bow(0));
        var h = new MapTestHarness();
        var (s, c) = await h.EnterAsync(9, 1, 1, name: "Robin", preSeeded: ch);
        ch.MaintainSkills.AddRange(buffs);
        c.Clear();
        return (h, s, c, ch);
    }

    [Fact]
    public async Task TakingOffTheBow_EndsTheSelfBuffsThatNeedIt()
    {
        var needsBow = Skill(300, weapon: 1u << (BowKind - 1));
        var anyWeapon = Skill(301);
        var fromAFriend = Skill(302, weapon: 1u << (BowKind - 1));
        var (h, s, c, ch) = await ArmedArcher(Buff(needsBow), Buff(anyWeapon), Buff(fromAFriend, attackId: 77));

        await h.Service.DispatchClientAsync(s, MapTestHarness.MoveItemReq(Equip, 0, Backpack, 0, 1));

        Assert.Equal(new ushort[] { 301, 302 }, ch.MaintainSkills.Select(m => m.SkillId));
        Assert.True(c.Has(Msg.CS_SKILLEND_ACK));
    }

    [Fact]
    public async Task KeepingTheBow_KeepsThem()
    {
        var needsBow = Skill(300, weapon: 1u << (BowKind - 1));
        var (h, s, _, ch) = await ArmedArcher(Buff(needsBow));

        await h.Service.DispatchClientAsync(s, MapTestHarness.MoveItemReq(Backpack, 0, Backpack, 1, 1));   // nothing there

        Assert.Single(ch.MaintainSkills);
    }

    [Fact]
    public async Task ALostStance_TakesItsStanceBuffsWithIt()
    {
        var stance = Skill(310, weapon: 1u << (BowKind - 1), rows: new[] { Row(SaBuff, SdtStatus, AtkMode) });
        var ofTheStance = Skill(311, posture: 310);
        var other = Skill(312, posture: 999);
        var (h, s, _, ch) = await ArmedArcher(Buff(stance), Buff(ofTheStance), Buff(other));

        await h.Service.DispatchClientAsync(s, MapTestHarness.MoveItemReq(Equip, 0, Backpack, 0, 1));

        Assert.Equal(new ushort[] { 312 }, ch.MaintainSkills.Select(m => m.SkillId));
    }

    // ================================ remain skills ================================

    /// <summary>A remain skill (an SA_CONTINUE row) whose continue and passive rows both raise max HP.</summary>
    private static SkillTemplate Remain(ushort id = 41)
        => Skill(id, rows: new[] { Row(SaContinue, SdtAbility, MtypeMhp, 30), Row(SaPassive, SdtAbility, MtypeMhp, 20) });

    [Fact]
    public void ALearnedRemainSkill_CountsForGood()
    {
        var ch = new Character { CharId = 1 };
        ch.Skills.Add(new Skill { SkillId = 41, Level = 1, Template = Remain() });

        Assert.Equal(50, StatEngine.CalcAbilityValue(ch, 0, MtypeMhp));
    }

    [Fact]
    public void APassiveRow_OnASkillThatIsNotRemainType_CountsForNothing()
    {
        var ch = new Character { CharId = 1 };
        ch.Skills.Add(new Skill { SkillId = 42, Level = 1, Template = Skill(42, rows: new[] { Row(SaPassive, SdtAbility, MtypeMhp, 20) }) });

        Assert.Equal(0, StatEngine.CalcAbilityValue(ch, 0, MtypeMhp));
    }

    [Fact]
    public void TheRemainTerm_IsAddedAfterTheBuffsAreClampedAtZero()
    {
        var t = new TemplateStore { Rate1st = 1.0f };
        t.Formulas[8] = new FormulaRow(50, 5.0f, 0f);    // MaxHP = 50 + CON·5
        t.Classes[1] = new StatSeed(3, 0, 5, 0, 0, 2);
        t.Races[1] = new StatSeed(5, 0, 10, 0, 0, 8);
        var ch = new Character { Class = 1, Race = 1, Level = 1 };
        Assert.Equal(130u, StatEngine.MaxHp(ch, t));

        var curse = Skill(900, rows: new[] { Row(SaBuff, SdtAbility, MtypeMhp, 200, SviDecrease) });
        ch.MaintainSkills.Add(new MaintainSkill { SkillId = 900, Level = 1, Template = curse });
        ch.Skills.Add(new Skill { SkillId = 41, Level = 1, Template = Remain() });

        Assert.Equal(50u, StatEngine.MaxHp(ch, t));       // max(0, max(0, 130 − 200) + 50), not max(0, −20)
    }
}
