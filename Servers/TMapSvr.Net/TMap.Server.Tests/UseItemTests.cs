using TMap.Data;
using TMap.Protocol;
using TMap.Server.Map;
using Xunit;

namespace TMap.Server.Tests;

/// <summary>
/// The rest of C++ OnCS_ITEMUSE_REQ: money pouches, special boxes, exp books, the premium and exp-boost "during" items (their
/// bonus, countdown, save and load), the action items and the plain used-up kinds.
/// </summary>
public class UseItemTests
{
    private const byte Backpack = 0xFF, ItUse = 7, ItActItems = 25;
    private const byte IkAp = 28, IkGoldPremium = 40, IkMoney = 51, IkExpBonus = 56, IkGainExp = 81, IkSpecialBox = 115;
    private const ushort Tid = 400, Prize = 401, ArcherPrize = 402, AnyPrize = 403, PremiumSkill = 900, ExpSkill = 903;
    private const byte TimeHours = 0x41, Days = 0x42;

    private static SkillTemplate Skill(ushort id)
    {
        var t = new SkillTemplate(id, 0, 0, 0, 0, 0, 1, 1, 1, 0, 0, 0, 0, 0, Positive: 1, MapId: 0xFFFF, Duration: 0);
        t.Data.Add(new SkillDataRow(3 /* SA_BUFF */, 1 /* SDT_ABILITY */, 0, (byte)(id % 7 + 1), 1, 1, 0, 0));   // each its own stat
        return t;
    }

    private static ItemTemplate Tpl(ushort id, byte type = ItUse, byte kind = 0, ushort useValue = 0, float price = 0, uint speedInc = 0,
        byte useType = 0, ushort useTime = 0, byte stack = 1)
        => new(id, 0, new[] { 1f, 0f, 0f, 0f }, Type: type, Kind: kind, UseValue: useValue, Price: price, SpeedInc: speedInc,
            UseType: useType, UseTime: useTime, Stack: stack, DefaultLevel: 1);

    private static FullItemRow Row(ushort id, byte count) => new(0, 0, 0, id, 0, count, 0, 0, 0, 0, 0, 0,
        new byte[6], new ushort[6], new uint[6], 0, 0, 0);

    private readonly TemplateStore _t = Store();

    private static TemplateStore Store()
    {
        var t = new TemplateStore { Rate1st = 1.0f };
        t.Classes[0] = new StatSeed(1, 1, 1, 1, 1, 1);
        t.Races[0] = new StatSeed(1, 1, 1, 1, 1, 1);
        t.Skills[PremiumSkill] = Skill(PremiumSkill);
        t.Skills[ExpSkill] = Skill(ExpSkill);
        foreach (var it in new[] { Tpl(Prize, stack: 20), Tpl(ArcherPrize), Tpl(AnyPrize) }) t.Items[it.ItemId] = it;
        t.SpecialBoxes[7] = new()
        {
            new SpecialBoxRow(7, 0, 0, Row(Prize, 10)),                    // a warrior's
            new SpecialBoxRow(7, 0, 2, Row(ArcherPrize, 1)),               // an archer's
            new SpecialBoxRow(7, 3, 6, Row(AnyPrize, 1)),                  // anyone's, for 3 days
        };
        return t;
    }

    private long _now = 1_800_000_000;

    private async Task<(MapTestHarness h, ClientSession s, FakeClientChannel c, Character ch)> Setup(ItemTemplate tpl, byte bagSlots = 0)
    {
        _t.Items[tpl.ItemId] = tpl;
        var h = new MapTestHarness(_t);
        h.Service.UnixNow = () => _now;
        var ch = new Character { CharId = 1, Name = "Ann", Level = 20, MaxHp = 100, Hp = 100, MaxMp = 50, Mp = 50 };
        var bag = new Inven { InvenId = Backpack };
        if (bagSlots != 0) bag.SlotCount = bagSlots;
        bag.Items.Add(new Item { ItemSlot = 0, TemplateId = tpl.ItemId, Count = 3, Template = tpl });
        ch.Invens.Add(bag);
        var (s, c) = await h.EnterAsync(1, 1, 1, x: 100, z: 100, name: "Ann", preSeeded: ch);
        h.Service.LootRng = new FixedRandom(0);
        c.Clear(); h.World.Clear();
        return (h, s, c, ch);
    }

    private static Task Use(MapTestHarness h, ClientSession s, ushort id = Tid) => h.Service.DispatchClientAsync(s, MapTestHarness.ItemUseReq(id, Backpack, 0, 0));
    private static ItemUseResult Result(FakeClientChannel c) => (ItemUseResult)new PacketReader(c.Last(Msg.CS_ITEMUSE_ACK)!).ReadByte();
    private static int Left(Character ch) => ch.FindInven(Backpack)!.FindItem(0)?.Count ?? 0;

    // ================================ money / boxes / exp books ================================

    [Fact]
    public async Task AMoneyPouch_GivesAtLeastItsValue_AndShowsIt()
    {
        var (h, s, c, ch) = await Setup(Tpl(Tid, kind: IkMoney, useValue: 45, price: 30));

        await Use(h, s);

        Assert.Equal(ItemUseResult.Success, Result(c));
        Assert.Equal(45_000u, new PacketReader(c.Last(Msg.CS_OPENMONEY_ACK)!).ReadUInt32());   // r = 0: the floor
        Assert.True(c.Has(Msg.CS_MONEY_ACK));
        Assert.Equal(2, Left(ch));
    }

    [Fact]
    public async Task AMoneyPouch_RollsAgain_WhileTheRollIsHigh()
    {
        var (h, s, c, _) = await Setup(Tpl(Tid, kind: IkMoney, useValue: 1, price: 30));
        h.Service.LootRng = new FixedRandom(99);                                   // 0.99 every time: five rolls

        await Use(h, s);

        uint roll = (uint)(30f * 1000 * 0.99f * 0.99f);
        Assert.Equal(roll * 5, new PacketReader(c.Last(Msg.CS_OPENMONEY_ACK)!).ReadUInt32());
    }

    [Fact]
    public async Task ASpecialBox_GivesItsItems_ForThePlayersClass()
    {
        var (h, s, c, ch) = await Setup(Tpl(Tid, kind: IkSpecialBox, useValue: 7));

        await Use(h, s);

        Assert.Equal(ItemUseResult.Success, Result(c));
        var items = ch.FindInven(Backpack)!.Items;
        Assert.Equal(10, items.Single(i => i.TemplateId == Prize).Count);
        Assert.DoesNotContain(items, i => i.TemplateId == ArcherPrize);
        Assert.Equal(_now + 3 * 86400, items.Single(i => i.TemplateId == AnyPrize).EndTime);
        Assert.Equal(2, Left(ch));
    }

    [Fact]
    public async Task ASpecialBox_ThatDoesNotFit_IsKept()
    {
        var (h, s, c, ch) = await Setup(Tpl(Tid, kind: IkSpecialBox, useValue: 7), bagSlots: 2);

        await Use(h, s);

        Assert.Equal(ItemUseResult.Full, Result(c));
        Assert.Equal(3, Left(ch));
        Assert.Single(ch.FindInven(Backpack)!.Items);
    }

    [Fact]
    public async Task AnUnknownBoxGroup_IsKept()
    {
        var (h, s, c, ch) = await Setup(Tpl(Tid, kind: IkSpecialBox, useValue: 8));

        await Use(h, s);

        Assert.Equal(ItemUseResult.Full, Result(c));
        Assert.Equal(3, Left(ch));
    }

    [Fact]
    public async Task AnExpBook_GivesItsExp()
    {
        var (h, s, c, ch) = await Setup(Tpl(Tid, kind: IkGainExp, speedInc: 100));

        await Use(h, s);

        Assert.Equal(ItemUseResult.Success, Result(c));
        Assert.Equal(100u, ch.Exp);
    }

    [Fact]
    public async Task AnyOtherUseKind_IsJustUsedUp()
    {
        var (h, s, c, ch) = await Setup(Tpl(Tid, kind: IkAp));

        await Use(h, s);

        Assert.Equal(ItemUseResult.Success, Result(c));
        Assert.Equal(2, Left(ch));
    }

    [Fact]
    public async Task AnActionItem_PlaysItsSkill_AndIsKept()
    {
        var (h, s, c, ch) = await Setup(Tpl(Tid, type: ItActItems, useValue: 77));

        await Use(h, s);

        var r = new PacketReader(c.Last(Msg.CS_ACTION_ACK)!);
        r.ReadByte(); Assert.Equal(1u, r.ReadUInt32()); r.ReadByte(); r.ReadByte(); r.ReadUInt32(); r.ReadUInt32();
        Assert.Equal((ushort)77, r.ReadUInt16());
        Assert.False(c.Has(Msg.CS_ITEMUSE_ACK));
        Assert.Equal(3, Left(ch));
    }

    // ================================ exp boost ================================

    [Fact]
    public async Task AnExpBoost_RunsForItsHours_WithItsBonusAndBuff()
    {
        var (h, s, c, ch) = await Setup(Tpl(Tid, kind: IkExpBonus, useValue: 20, useType: TimeHours, useTime: 1));

        await Use(h, s);

        Assert.Equal(ItemUseResult.Success, Result(c));
        Assert.Equal(((byte)1, 3600u), (ch.ExpItem!.Type, ch.ExpItem.RemainTime));
        Assert.Equal(3_600_000u, ch.MaintainSkills.Single(m => m.SkillId == ExpSkill).MaintainTick);
        Assert.Equal(120u, MapService.WithExpBonus(ch, 100));
    }

    [Fact]
    public async Task ASecondBoost_IsRefused()
    {
        var (h, s, c, ch) = await Setup(Tpl(Tid, kind: IkExpBonus, useValue: 20, useType: TimeHours, useTime: 1));
        await Use(h, s);

        await Use(h, s);

        Assert.Equal(ItemUseResult.OverlapExpBonus, Result(c));
        Assert.Equal(2, Left(ch));
    }

    [Fact]
    public async Task TheBoost_CountsDownWhilePlayed_AndEnds()
    {
        var (h, s, _, ch) = await Setup(Tpl(Tid, kind: IkExpBonus, useValue: 20, useType: TimeHours, useTime: 1));
        await Use(h, s);
        await h.Service.OnTimerAsync();                                           // starts the count

        _now += 4; await h.Service.OnTimerAsync();
        Assert.Equal(3596u, ch.ExpItem!.RemainTime);
        _now += 60; await h.Service.OnTimerAsync();                               // at most 10 s a step
        Assert.Equal(3586u, ch.ExpItem.RemainTime);

        ch.ExpItem.RemainTime = 3;
        _now += 5; await h.Service.OnTimerAsync();                                // to 0…
        _now += 1; await h.Service.OnTimerAsync();                                // …then over
        Assert.Null(ch.ExpItem);
        Assert.DoesNotContain(ch.MaintainSkills, m => m.SkillId == ExpSkill);
        Assert.Equal(100u, MapService.WithExpBonus(ch, 100));
    }

    // ================================ premium ================================

    [Fact]
    public async Task APremium_IsThePcBang_WithItsBuffs_AndTheDailyBonus()
    {
        var (h, s, c, ch) = await Setup(Tpl(Tid, kind: IkGoldPremium, useValue: PremiumSkill, useType: Days, useTime: 30));

        await Use(h, s);

        Assert.Equal(ItemUseResult.Success, Result(c));
        Assert.Equal(((byte)2, _now + 30 * 86400L), (ch.Premium!.Type, ch.Premium.EndTime));
        Assert.Equal((byte)2, ch.InPcBang);
        var r = new PacketReader(c.Last(Msg.CS_RESETPCBANG_ACK)!);
        Assert.Equal((1u, (byte)2), (r.ReadUInt32(), r.ReadByte()));
        Assert.Contains(ch.MaintainSkills, m => m.SkillId == PremiumSkill);
        Assert.Equal(4 * 3600 * 1000u, ch.MaintainSkills.Single(m => m.SkillId == ExpSkill).MaintainTick);
        Assert.Equal(120u, MapService.WithExpBonus(ch, 100));
    }

    [Fact]
    public async Task UnderAPremium_NeitherAnotherPremiumNorABoost()
    {
        var (h, s, c, ch) = await Setup(Tpl(Tid, kind: IkGoldPremium, useValue: PremiumSkill, useType: Days, useTime: 30));
        await Use(h, s);
        await Use(h, s);
        Assert.Equal(ItemUseResult.OverlapPremium, Result(c));

        var boost = Tpl(Prize, kind: IkExpBonus, useValue: 20, useType: TimeHours, useTime: 1);
        _t.Items[Prize] = boost;
        ch.FindInven(Backpack)!.Items.Add(new Item { ItemSlot = 5, TemplateId = Prize, Count = 1, Template = boost });
        await h.Service.DispatchClientAsync(s, MapTestHarness.ItemUseReq(Prize, Backpack, 5, 0));
        Assert.Equal(ItemUseResult.OverlapExpBonus, Result(c));
    }

    [Fact]
    public async Task ThePremiumBonus_StopsAfterFourHoursADay()
    {
        var (h, s, _, ch) = await Setup(Tpl(Tid, kind: IkGoldPremium, useValue: PremiumSkill, useType: Days, useTime: 30));
        await Use(h, s);

        ch.PcBangTime = 4 * 3600;

        Assert.Equal(100u, MapService.WithExpBonus(ch, 100));
    }

    [Fact]
    public void AGainExpBuff_AddsItsValue()
    {
        var ch = new Character();
        var t = Skill(1500);
        t.Data.Add(new SkillDataRow(3, SkillTemplate.SdtStatus, 0, 69 /* GAINEXP */, 1, 50, 0, 0));
        ch.MaintainSkills.Add(new MaintainSkill { SkillId = 1500, Template = t });

        Assert.Equal(150u, MapService.WithExpBonus(ch, 100));
    }

    // ================================ save / load ================================

    [Fact]
    public async Task TheRunningItems_AreSaved_AndComeBack()
    {
        var premium = Tpl(Tid, kind: IkGoldPremium, useValue: PremiumSkill, useType: Days, useTime: 30);
        var (h, s, _, ch) = await Setup(premium);
        await Use(h, s);
        ch.PcBangTime = 600;

        var d = h.Service.WithDuringItems(MapService.BuildCharSave(ch), ch, _now);

        Assert.True(d.SaveDuring);
        Assert.Equal(new DuringItemRow(Tid, 2, 30 * 86400, _now + 30 * 86400L), d.Premium);
        Assert.Null(d.ExpItem);
        Assert.Equal(600u, d.PcBangTime);

        var back = new Character();
        _now += 86400;                                                            // a day later: 29 days left
        h.Service.LoadDuringItems(back, new DuringLoad(new() { d.Premium! }, new(), 2, 0, 0));
        Assert.Equal((29 * 86400u, d.Premium!.EndTime), (back.Premium!.RemainTime, back.Premium.EndTime));
        Assert.Equal((byte)2, back.InPcBang);
    }

    [Fact]
    public async Task AnOverItem_IsNotSaved()
    {
        var (h, s, _, ch) = await Setup(Tpl(Tid, kind: IkExpBonus, useValue: 20, useType: TimeHours, useTime: 1));
        await Use(h, s);
        ch.ExpItem!.RemainTime = 0;

        Assert.Null(h.Service.WithDuringItems(MapService.BuildCharSave(ch), ch, _now).ExpItem);
    }
}
