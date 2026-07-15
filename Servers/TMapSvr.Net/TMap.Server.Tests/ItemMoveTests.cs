using TMap.Data;
using TMap.Protocol;
using TMap.Server.Map;
using Xunit;

namespace TMap.Server.Tests;

/// <summary>Phase 7 — CS_MOVEITEM: move/swap/split/merge/drop within the inventory, and equip/unequip.</summary>
public class ItemMoveTests
{
    private const byte Backpack = 0xFF, Equip = 0xFE, Drop = 0xFC;

    private static Character MakeChar(byte cls = 2, byte level = 10)
    {
        var ch = new Character { CharId = 9, Name = "Mover", Class = cls, Level = level };
        ch.Invens.Add(new Inven { InvenId = Backpack });
        ch.Invens.Add(new Inven { InvenId = Equip });
        return ch;
    }

    private static Item Stackable(byte slot, byte count, ushort template = 100)
        => new() { ItemSlot = slot, TemplateId = template, Count = count,
                   Template = new ItemTemplate(template, 0, new[] { 1f, 0f, 0f, 0f }, Stack: 20) };

    // Equippable to ES_HEAD (slot 3) by class 2, level 1.
    private static Item Helmet(byte slot, byte count = 1, byte level = 1)
        => new() { ItemSlot = slot, TemplateId = 5000, Count = count,
                   Template = new ItemTemplate(5000, 0, new[] { 1f, 0f, 0f, 0f }, Type: 2,
                       SlotId: 1u << 3, ClassId: 1u << 2, DefaultLevel: level, PrmSlot: 3) };

    private static MoveItemResult Result(FakeClientChannel c)
        => (MoveItemResult)new PacketReader(c.Last(Msg.CS_MOVEITEM_ACK)!).ReadByte();

    private static async Task<(ClientSession s, FakeClientChannel c)> Enter(Character ch)
        => await new MapTestHarness().EnterAsync(9, 1, 1, name: "Mover", preSeeded: ch);

    [Fact]
    public async Task Move_ToEmptySlot_RelocatesItem()
    {
        var ch = MakeChar();
        ch.FindInven(Backpack)!.Items.Add(Stackable(0, 5));
        var h = new MapTestHarness();
        var (s, c) = await h.EnterAsync(9, 1, 1, name: "Mover", preSeeded: ch);
        c.Clear();

        await h.Service.DispatchClientAsync(s, MapTestHarness.MoveItemReq(Backpack, 0, Backpack, 5, 5));

        Assert.Equal(MoveItemResult.Success, Result(c));
        Assert.Null(ch.FindInven(Backpack)!.FindItem(0));
        Assert.Equal(5, ch.FindInven(Backpack)!.FindItem(5)!.Count);
        Assert.True(c.Has(Msg.CS_DELITEM_ACK) && c.Has(Msg.CS_ADDITEM_ACK));
    }

    [Fact]
    public async Task Move_PartialCount_SplitsStack()
    {
        var ch = MakeChar();
        ch.FindInven(Backpack)!.Items.Add(Stackable(0, 10));
        var h = new MapTestHarness();
        var (s, c) = await h.EnterAsync(9, 1, 1, name: "Mover", preSeeded: ch);

        await h.Service.DispatchClientAsync(s, MapTestHarness.MoveItemReq(Backpack, 0, Backpack, 5, 3));

        Assert.Equal(MoveItemResult.Success, Result(c));
        Assert.Equal(7, ch.FindInven(Backpack)!.FindItem(0)!.Count); // source decremented
        Assert.Equal(3, ch.FindInven(Backpack)!.FindItem(5)!.Count); // split off
    }

    [Fact]
    public async Task Move_OntoDifferentItem_Swaps()
    {
        var ch = MakeChar();
        ch.FindInven(Backpack)!.Items.Add(Stackable(0, 5, template: 100));
        ch.FindInven(Backpack)!.Items.Add(Stackable(5, 5, template: 200));
        var h = new MapTestHarness();
        var (s, c) = await h.EnterAsync(9, 1, 1, name: "Mover", preSeeded: ch);

        await h.Service.DispatchClientAsync(s, MapTestHarness.MoveItemReq(Backpack, 0, Backpack, 5, 5));

        Assert.Equal(MoveItemResult.Success, Result(c));
        Assert.Equal(200, ch.FindInven(Backpack)!.FindItem(0)!.TemplateId); // items swapped slots
        Assert.Equal(100, ch.FindInven(Backpack)!.FindItem(5)!.TemplateId);
    }

    [Fact]
    public async Task Move_OntoIdenticalStack_Merges()
    {
        var ch = MakeChar();
        ch.FindInven(Backpack)!.Items.Add(Stackable(0, 5));
        ch.FindInven(Backpack)!.Items.Add(Stackable(5, 3));
        var h = new MapTestHarness();
        var (s, c) = await h.EnterAsync(9, 1, 1, name: "Mover", preSeeded: ch);

        await h.Service.DispatchClientAsync(s, MapTestHarness.MoveItemReq(Backpack, 0, Backpack, 5, 5));

        Assert.Equal(MoveItemResult.Success, Result(c));
        Assert.Null(ch.FindInven(Backpack)!.FindItem(0));             // source emptied
        Assert.Equal(8, ch.FindInven(Backpack)!.FindItem(5)!.Count);  // 3 + 5
    }

    [Fact]
    public async Task Drop_FullStack_DeletesItem()
    {
        var ch = MakeChar();
        ch.FindInven(Backpack)!.Items.Add(Stackable(0, 5));
        var h = new MapTestHarness();
        var (s, c) = await h.EnterAsync(9, 1, 1, name: "Mover", preSeeded: ch);
        c.Clear();

        await h.Service.DispatchClientAsync(s, MapTestHarness.MoveItemReq(Backpack, 0, Drop, 0, 5));

        Assert.Equal(MoveItemResult.Success, Result(c));
        Assert.Null(ch.FindInven(Backpack)!.FindItem(0));
        Assert.True(c.Has(Msg.CS_DELITEM_ACK));
    }

    [Fact]
    public async Task Drop_PartialStack_Decrements()
    {
        var ch = MakeChar();
        ch.FindInven(Backpack)!.Items.Add(Stackable(0, 10));
        var h = new MapTestHarness();
        var (s, c) = await h.EnterAsync(9, 1, 1, name: "Mover", preSeeded: ch);

        await h.Service.DispatchClientAsync(s, MapTestHarness.MoveItemReq(Backpack, 0, Drop, 0, 3));

        Assert.Equal(MoveItemResult.Success, Result(c));
        Assert.Equal(7, ch.FindInven(Backpack)!.FindItem(0)!.Count);
    }

    [Fact]
    public async Task PartialDrop_ConsumesByTemplateFromFirstStack()
    {
        // Audit fold-in: a partial drop routes through C++ UseItem(wItemID, count) — consuming `count` of the
        // template across bags in slot order (the FIRST matching stack), not necessarily the dragged one.
        var ch = MakeChar();
        ch.FindInven(Backpack)!.Items.Add(Stackable(0, 5, template: 100)); // first match
        ch.FindInven(Backpack)!.Items.Add(Stackable(3, 5, template: 100)); // dragged stack
        var h = new MapTestHarness();
        var (s, c) = await h.EnterAsync(9, 1, 1, name: "Mover", preSeeded: ch);

        await h.Service.DispatchClientAsync(s, MapTestHarness.MoveItemReq(Backpack, 3, Drop, 0, 3));

        Assert.Equal(MoveItemResult.Success, Result(c));
        Assert.Equal(2, ch.FindInven(Backpack)!.FindItem(0)!.Count); // 3 consumed from slot 0 (first match)
        Assert.Equal(5, ch.FindInven(Backpack)!.FindItem(3)!.Count); // dragged stack untouched
    }

    [Fact]
    public async Task Equip_SendsSelfHpMp_AndTwoSuccessAcks()
    {
        // Audit fold-in: ChangeEquipItem sends its own MOVEITEM_ACK + a self CS_HPMP_ACK; the handler tail
        // sends a second MOVEITEM_ACK — matching the C++ double-ack on equip/unequip.
        var ch = MakeChar(cls: 2, level: 10);
        ch.FindInven(Backpack)!.Items.Add(Helmet(0));
        var h = new MapTestHarness();
        var (s, c) = await h.EnterAsync(9, 1, 1, name: "Mover", preSeeded: ch);
        c.Clear();

        await h.Service.DispatchClientAsync(s, MapTestHarness.MoveItemReq(Backpack, 0, Equip, 3, 1));

        Assert.Equal(MoveItemResult.Success, Result(c));
        Assert.True(c.Has(Msg.CS_HPMP_ACK));                    // self HP/MP bar after equip
        Assert.Equal(2, c.WithId(Msg.CS_MOVEITEM_ACK).Count()); // ChangeEquipItem + the handler tail
    }

    [Fact]
    public async Task Move_FromEmptySlot_NoSrcItem()
    {
        var ch = MakeChar();
        var h = new MapTestHarness();
        var (s, c) = await h.EnterAsync(9, 1, 1, name: "Mover", preSeeded: ch);

        await h.Service.DispatchClientAsync(s, MapTestHarness.MoveItemReq(Backpack, 0, Backpack, 5, 1));
        Assert.Equal(MoveItemResult.NoSrcItem, Result(c));
    }

    [Fact]
    public async Task Equip_ValidItem_MovesToEquip_AndBroadcastsAppearance()
    {
        var ch = MakeChar(cls: 2, level: 10);
        ch.FindInven(Backpack)!.Items.Add(Helmet(0));
        var h = new MapTestHarness();
        // A neighbour at the same spawn cell should receive the equip appearance broadcast.
        var (sn, cn) = await h.EnterAsync(1, 1, 1, name: "Bystander");
        var (s, c) = await h.EnterAsync(9, 9, 9, name: "Mover", preSeeded: ch);
        c.Clear(); cn.Clear();

        await h.Service.DispatchClientAsync(s, MapTestHarness.MoveItemReq(Backpack, 0, Equip, 3, 1));

        Assert.Equal(MoveItemResult.Success, Result(c));
        Assert.Null(ch.FindInven(Backpack)!.FindItem(0));
        Assert.Equal(5000, ch.FindInven(Equip)!.FindItem(3)!.TemplateId); // now equipped in ES_HEAD
        Assert.True(c.Has(Msg.CS_EQUIP_ACK));   // actor sees its own appearance update
        Assert.True(cn.Has(Msg.CS_EQUIP_ACK));  // and so does the neighbour in view
    }

    [Fact]
    public async Task Equip_TooLowLevel_Rejected()
    {
        var ch = MakeChar(cls: 2, level: 10);
        ch.FindInven(Backpack)!.Items.Add(Helmet(0, level: 50)); // requires level 50
        var h = new MapTestHarness();
        var (s, c) = await h.EnterAsync(9, 1, 1, name: "Mover", preSeeded: ch);

        await h.Service.DispatchClientAsync(s, MapTestHarness.MoveItemReq(Backpack, 0, Equip, 3, 1));

        Assert.Equal(MoveItemResult.LowLevel, Result(c));
        Assert.NotNull(ch.FindInven(Backpack)!.FindItem(0)); // still in the bag
    }

    [Fact]
    public async Task Equip_EldEnchantAtOrAboveRequiredLevel_StillEnforcesFullLevel()
    {
        // Audit fix: C++ GetEquipLevel subtracts ELD only when DefaultLevel > ELD; an ELD ≥ the requirement
        // must NOT drop the required level to 0/negative (which would wrongly let any level equip it).
        var ch = MakeChar(cls: 2, level: 10);
        var helmet = Helmet(0, level: 50);   // requires level 50
        helmet.Ext[Item.IevEld] = 60;        // ELD enchant ≥ the requirement
        ch.FindInven(Backpack)!.Items.Add(helmet);
        var h = new MapTestHarness();
        var (s, c) = await h.EnterAsync(9, 1, 1, name: "Mover", preSeeded: ch);

        await h.Service.DispatchClientAsync(s, MapTestHarness.MoveItemReq(Backpack, 0, Equip, 3, 1));

        Assert.Equal(MoveItemResult.LowLevel, Result(c));    // EquipLevel stays 50 (> char level 10)
        Assert.NotNull(ch.FindInven(Backpack)!.FindItem(0)); // not equipped
    }

    [Fact]
    public async Task Equip_WrongClass_Rejected()
    {
        var ch = MakeChar(cls: 5, level: 10); // helmet is class-2 only
        ch.FindInven(Backpack)!.Items.Add(Helmet(0));
        var h = new MapTestHarness();
        var (s, c) = await h.EnterAsync(9, 1, 1, name: "Mover", preSeeded: ch);

        await h.Service.DispatchClientAsync(s, MapTestHarness.MoveItemReq(Backpack, 0, Equip, 3, 1));
        Assert.Equal(MoveItemResult.NoMatchClass, Result(c));
    }

    [Fact]
    public async Task Unequip_ToEmptyBagSlot_MovesBack()
    {
        var ch = MakeChar(cls: 2, level: 10);
        ch.FindInven(Equip)!.Items.Add(Helmet(3));
        var h = new MapTestHarness();
        var (s, c) = await h.EnterAsync(9, 1, 1, name: "Mover", preSeeded: ch);
        c.Clear();

        await h.Service.DispatchClientAsync(s, MapTestHarness.MoveItemReq(Equip, 3, Backpack, 0, 1));

        Assert.Equal(MoveItemResult.Success, Result(c));
        Assert.Null(ch.FindInven(Equip)!.FindItem(3));
        Assert.Equal(5000, ch.FindInven(Backpack)!.FindItem(0)!.TemplateId);
        Assert.True(c.Has(Msg.CS_EQUIP_ACK));
    }
}
