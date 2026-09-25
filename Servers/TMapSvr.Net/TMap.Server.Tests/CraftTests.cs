using TMap.Data;
using TMap.Protocol;
using TMap.Server.Map;
using Xunit;

namespace TMap.Server.Tests;

/// <summary>Dice that always land on the same number (C++ <c>rand()</c> → <paramref name="value"/>).</summary>
internal sealed class FixedRandom(int value) : Random
{
    public override int Next(int maxValue) => maxValue <= 0 ? 0 : Math.Min(value, maxValue - 1);
    public override int Next(int minValue, int maxValue) => Math.Clamp(value, minValue, maxValue - 1);
    public override long NextInt64(long maxValue) => maxValue <= 0 ? 0 : Math.Min(value, maxValue - 1);
}

/// <summary>Crafting: C++ OnCS_ITEMUPGRADE_REQ / OnCS_REFINE_REQ / OnCS_ITEMCHANGE_REQ.</summary>
public class CraftTests
{
    private const ushort Sword = 1000, Sword2 = 1001, UpScroll = 2000, Purify = 2001, Gem = 2002, KeepGem = 18191,
        Wrap = 2004, ClearMagic = 2005, MagicScroll = 2006, Catalyst = 2100, Box = 3000, Prize = 3001;
    private const ushort NpcId = 90;

    private static TemplateStore Store()
    {
        var t = new TemplateStore { Rate1st = 1.03f };
        ItemTemplate Tpl(ushort id, byte type, byte kind, byte grade = 0, ushort use = 0, byte canGrade = 0,
            byte canMagic = 0, byte canWrap = 0, byte refineMax = 0, byte canRepair = 0, uint dura = 0, byte stack = 1)
            => new(id, refineMax, new float[4], Type: type, Kind: kind, Grade: grade, UseValue: use, CanGrade: canGrade,
                CanMagic: canMagic, CanWrap: canWrap, CanRepair: canRepair, DuraMax: dura, Stack: stack, Price: 1f,
                SlotId: 0, SubSlot: 0xFF);
        t.Items[Sword] = Tpl(Sword, 1, 1, canGrade: 1, canMagic: 1, canWrap: 1, refineMax: 5, canRepair: 1, dura: 1000);
        t.Items[Sword2] = Tpl(Sword2, 1, 1, canGrade: 1, refineMax: 5, canRepair: 1, dura: 1000);
        t.Items[UpScroll] = Tpl(UpScroll, 9, 29, stack: 10);
        t.Items[Purify] = Tpl(Purify, 9, 30, grade: 3, stack: 10);
        t.Items[Gem] = Tpl(Gem, 9, 102, use: 100, stack: 10);
        t.Items[KeepGem] = Tpl(KeepGem, 9, 102, use: 100, stack: 10);
        t.Items[Wrap] = Tpl(Wrap, 9, 79, stack: 10);
        t.Items[ClearMagic] = Tpl(ClearMagic, 9, 77, stack: 10);
        t.Items[MagicScroll] = Tpl(MagicScroll, 9, 31, grade: 100, stack: 10);
        t.Items[Catalyst] = Tpl(Catalyst, 14, 0, stack: 10);
        t.Items[Box] = Tpl(Box, 7, 78, use: 5, stack: 10);
        t.Items[Prize] = Tpl(Prize, 7, 0, stack: 50);
        for (int i = 0; i < 25; i++) t.ItemGradeProb[i] = 50;
        t.GemProbs[0] = 33; t.GemProbs[3] = 8;
        t.RefineCostByLevel[1] = 10;
        var opt = new MagicTemplate(7, 0, 100, Kind: 1, IsMagic: 1, IsRare: 1, RareBound: 100);   // kind 1 (1HAND)
        t.Magics[7] = opt;
        t.MagicsByKind[1] = new List<MagicTemplate> { opt };
        t.CashGamble[5] = new List<CashGambleRow>
        {
            new(1, 10, 5, 0, new FullItemRow(0, 0, 0, Prize, 0, 5, 0, 0, 0, 0, 0, 0, new byte[6], new ushort[6], new uint[6], 0, 0, 0)),
        };
        t.CashGambleTotal[5] = 10;
        return t;
    }

    private static async Task<(MapTestHarness h, ClientSession s, FakeClientChannel c, Character ch, Inven bag)> Setup(
        int dice, params (byte slot, ushort id, byte count)[] items)
    {
        var t = Store();
        var h = new MapTestHarness(t);
        var ch = new Character { CharId = 1, Name = "Smith", MaxHp = 100, Hp = 100 };
        var bag = new Inven { InvenId = 0 };
        foreach (var (slot, id, count) in items)
            bag.Items.Add(new Item { ItemSlot = slot, TemplateId = id, Template = t.Items[id], Count = count, DuraMax = 1000, DuraCur = 1000 });
        ch.Invens.Add(bag);
        var (s, c) = await h.EnterAsync(1, 1, 1, preSeeded: ch);
        ch.Level = 50; ch.Gold = 1; ch.Silver = 0; ch.Cooper = 0;
        h.Service.AddNpc(new Npc { Id = NpcId, Type = 5, Country = 3 });
        h.Service.CraftRng = new FixedRandom(dice);
        c.Clear();
        return (h, s, c, ch, bag);
    }

    private static byte[] Upgrade(byte tSlot, byte gSlot, ushort npc = NpcId, ushort color = 0)
    {
        var w = new PacketWriter(Msg.CS_ITEMUPGRADE_REQ);
        w.WriteByte(0); w.WriteByte(tSlot); w.WriteByte(0); w.WriteByte(gSlot); w.WriteUInt16(npc);
        w.WriteByte(0xFC); w.WriteByte(0xFF); w.WriteUInt16(color);
        return w.ToArray();
    }

    private static byte Result(FakeClientChannel c, ushort ack) => new PacketReader(c.Last(ack)!).ReadByte();

    private static Item At(Inven bag, byte slot) => bag.Items.Single(i => i.ItemSlot == slot);

    // ================= upgrade =================

    [Fact]
    public async Task AnUpgradeThatLands_SetsTheItemToLevel24_AndSpendsTheScroll()
    {
        var (h, s, c, _, bag) = await Setup(0, (0, Sword, 1), (1, UpScroll, 3));

        await h.Service.DispatchClientAsync(s, Upgrade(0, 1));

        Assert.Equal(0, Result(c, Msg.CS_ITEMUPGRADE_ACK));           // ITEMUPGRADE_SUCCESS
        Assert.Equal(24, At(bag, 0).Level);                           // this build's max-on-success
        Assert.NotEqual(0, At(bag, 0).GradeEffect);                   // ≥ 17 earns an effect
        Assert.Equal(2, At(bag, 1).Count);
    }

    [Fact]
    public async Task AnUpgradeThatFails_DestroysTheItem()
    {
        var (h, s, c, _, bag) = await Setup(99, (0, Sword, 1), (1, UpScroll, 1));   // 99 ≥ 50 %

        await h.Service.DispatchClientAsync(s, Upgrade(0, 1));

        Assert.Equal(3, Result(c, Msg.CS_ITEMUPGRADE_ACK));           // ITEMUPGRADE_FAIL
        Assert.Empty(bag.Items);                                       // item destroyed, scroll spent
    }

    [Fact]
    public async Task WithoutAnNpc_NothingHappens()
    {
        var (h, s, c, _, bag) = await Setup(0, (0, Sword, 1), (1, UpScroll, 1));

        await h.Service.DispatchClientAsync(s, Upgrade(0, 1, npc: 12345));

        Assert.Equal(5, Result(c, Msg.CS_ITEMUPGRADE_ACK));           // NPCCALLERROR
        Assert.Equal(2, bag.Items.Count);                              // nothing spent
    }

    [Fact]
    public async Task AScrollOfTheWrongType_IsRefused()
    {
        var (h, s, c, _, _) = await Setup(0, (0, Sword, 1), (1, Box, 1));

        await h.Service.DispatchClientAsync(s, Upgrade(0, 1));

        Assert.Equal(1, Result(c, Msg.CS_ITEMUPGRADE_ACK));           // NOITEM
    }

    [Fact]
    public async Task APurifyScroll_TakesItsGradeOff()
    {
        var (h, s, c, _, bag) = await Setup(0, (0, Sword, 1), (1, Purify, 1));
        At(bag, 0).Level = 20; At(bag, 0).GradeEffect = 5;

        await h.Service.DispatchClientAsync(s, Upgrade(0, 1));

        Assert.Equal(17, At(bag, 0).Level);
        Assert.Equal(5, At(bag, 0).GradeEffect);                      // still ≥ 17
    }

    [Fact]
    public async Task AGem_Lands_OrResetsTheGems()
    {
        var (h, s, _, _, bag) = await Setup(0, (0, Sword, 1), (1, Gem, 2));
        await h.Service.DispatchClientAsync(s, Upgrade(0, 1));          // 1 ≤ 33 · 2
        Assert.Equal(1, At(bag, 0).Gem);

        h.Service.CraftRng = new FixedRandom(99);                      // 100 > 20 · 2 (gem 1 prob is 0 here)
        await h.Service.DispatchClientAsync(s, Upgrade(0, 1));
        Assert.Equal(0, At(bag, 0).Gem);
    }

    [Fact]
    public async Task TheKeepGemScroll_OnlyStepsBackFromThree()
    {
        var (h, s, c, _, bag) = await Setup(99, (0, Sword, 1), (1, KeepGem, 1));
        At(bag, 0).Gem = 3;

        await h.Service.DispatchClientAsync(s, Upgrade(0, 1));

        Assert.Equal(8, Result(c, Msg.CS_ITEMUPGRADE_ACK));           // GEM_FAIL
        Assert.Equal(2, At(bag, 0).Gem);
    }

    [Fact]
    public async Task TheKeepGemScroll_BelowThree_KeepsTheGemsAsTheyAre()
    {
        var (h, s, c, _, bag) = await Setup(99, (0, Sword, 1), (1, KeepGem, 1));
        At(bag, 0).Gem = 1;

        await h.Service.DispatchClientAsync(s, Upgrade(0, 1));

        Assert.Equal(8, Result(c, Msg.CS_ITEMUPGRADE_ACK));           // GEM_FAIL
        Assert.Equal(1, At(bag, 0).Gem);                               // neither reset nor stepped back
    }

    [Fact]
    public async Task Wrapping_MarksTheItem()
    {
        var (h, s, c, _, bag) = await Setup(0, (0, Sword, 1), (1, Wrap, 1));

        await h.Service.DispatchClientAsync(s, Upgrade(0, 1));

        Assert.Equal(12, Result(c, Msg.CS_ITEMUPGRADE_ACK));          // SUCCESS_WRAP
        Assert.Equal(1u, At(bag, 0).Ext[Item.IevWrap]);
        Assert.False(At(bag, 0).CanUse);
    }

    [Fact]
    public async Task AppearanceTransfer_CopiesTheOtherItem()
    {
        var (h, s, c, _, bag) = await Setup(0, (0, Sword, 1), (1, Sword2, 1));

        await h.Service.DispatchClientAsync(s, Upgrade(0, 1));

        Assert.Equal(23, Result(c, Msg.CS_ITEMUPGRADE_ACK));          // MOGG_SUCCESS
        Assert.Equal(Sword2, At(bag, 0).MoggItemId);
        Assert.Single(bag.Items);                                      // the donor is spent
    }

    [Fact]
    public async Task AMagicScroll_RollsAnOption()
    {
        var (h, s, c, _, bag) = await Setup(0, (0, Sword, 1), (1, MagicScroll, 1));

        await h.Service.DispatchClientAsync(s, Upgrade(0, 1));

        var r = new PacketReader(c.Last(Msg.CS_ITEMMAGICGRADE_ACK)!);
        Assert.Equal(0, r.ReadByte());                                 // SUCCESS
        Assert.Contains(At(bag, 0).Magic, m => m.Id == 7);
    }

    [Fact]
    public async Task ClearMagic_RemovesEveryOption()
    {
        var (h, s, c, _, bag) = await Setup(0, (0, Sword, 1), (1, ClearMagic, 1));
        At(bag, 0).Magic.Add(new MagicOption(7, 10));

        await h.Service.DispatchClientAsync(s, Upgrade(0, 1));

        Assert.Equal(11, Result(c, Msg.CS_ITEMMAGICGRADE_ACK));       // SUCCESS_MAGICCLEAR
        Assert.Empty(At(bag, 0).Magic);
    }

    // ================= refine =================

    private static byte[] Refine(byte needCost, ushort npc, params byte[] materialSlots)
    {
        var w = new PacketWriter(Msg.CS_REFINE_REQ);
        w.WriteByte(needCost); w.WriteByte(0); w.WriteByte(0); w.WriteByte((byte)materialSlots.Length);
        w.WriteUInt16(npc); w.WriteByte(0xFC); w.WriteByte(0xFF);
        foreach (var m in materialSlots) { w.WriteByte(0); w.WriteByte(m); }
        return w.ToArray();
    }

    [Fact]
    public async Task AskingTheRefineCost_ChangesNothing()
    {
        var (h, s, c, _, bag) = await Setup(0, (0, Sword, 1), (1, Catalyst, 1));

        await h.Service.DispatchClientAsync(s, Refine(1, NpcId, 1));

        Assert.True(c.Has(Msg.CS_REFINECOST_ACK));
        Assert.Equal(2, bag.Items.Count);
    }

    [Fact]
    public async Task ARefineThatLands_RaisesTheRefineAndDurability_AndSpendsTheMaterial()
    {
        var (h, s, c, _, bag) = await Setup(0, (0, Sword, 1), (1, Catalyst, 1));

        await h.Service.DispatchClientAsync(s, Refine(0, NpcId, 1));

        Assert.Equal(0, Result(c, Msg.CS_REFINE_ACK));
        Assert.Equal(1, At(bag, 0).RefineCur);
        Assert.Equal(1000u + 1000u * 8 / 100, At(bag, 0).DuraMax);  // + dura · (8 + rand%8) / 100
        Assert.Single(bag.Items);
    }

    [Fact]
    public async Task RefiningWithoutAnNpc_AlwaysFails_ButStillSpends()
    {
        // Faithful: C++ CalcProb returns 0 when the NPC id resolves to nothing.
        var (h, s, c, _, bag) = await Setup(0, (0, Sword, 1), (1, Catalyst, 1));

        await h.Service.DispatchClientAsync(s, Refine(0, 12345, 1));

        Assert.Equal(6, Result(c, Msg.CS_REFINE_ACK));                // ITEMREPAIR_FAIL
        Assert.Equal(0, At(bag, 0).RefineCur);
        Assert.Single(bag.Items);                                      // the catalyst is gone anyway
    }

    [Fact]
    public async Task RefiningWithNoMaterial_IsRefused()
    {
        var (h, s, c, _, _) = await Setup(0, (0, Sword, 1));

        await h.Service.DispatchClientAsync(s, Refine(0, NpcId));

        Assert.Equal(1, Result(c, Msg.CS_REFINE_ACK));                // NOTFOUND (the C++ would divide by zero)
    }

    // ================= gamble box =================

    private static byte[] Change(byte slot)
    {
        var w = new PacketWriter(Msg.CS_ITEMCHANGE_REQ);
        w.WriteByte(0); w.WriteByte(slot);
        return w.ToArray();
    }

    [Fact]
    public async Task OpeningABox_GivesThePrize_AndSpendsTheBox()
    {
        var (h, s, c, _, bag) = await Setup(0, (0, Box, 2));

        await h.Service.DispatchClientAsync(s, Change(0));

        var r = new PacketReader(c.Last(Msg.CS_ITEMCHANGE_ACK)!);
        Assert.Equal(0, r.ReadByte());
        Assert.Equal(Prize, r.ReadUInt16());
        Assert.Equal(1, r.ReadByte());                                 // rand() % 5 + 1 with rand 0
        Assert.Contains(bag.Items, i => i.TemplateId == Prize);
        Assert.Equal(1, At(bag, 0).Count);
    }

    [Fact]
    public async Task AFullInventory_KeepsTheBox()
    {
        var (h, s, c, _, bag) = await Setup(0, (0, Box, 1));
        bag.SlotCount = 1;                                             // no room for the prize

        await h.Service.DispatchClientAsync(s, Change(0));

        Assert.Equal(4, Result(c, Msg.CS_ITEMCHANGE_ACK));            // ITEMCHANGE_FULL
        Assert.Equal(1, At(bag, 0).Count);
    }
}
