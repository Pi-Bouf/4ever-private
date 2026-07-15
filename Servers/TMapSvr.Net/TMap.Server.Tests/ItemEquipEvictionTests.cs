using TMap.Data;
using TMap.Protocol;
using TMap.Server.Map;
using Xunit;

namespace TMap.Server.Tests;

/// <summary>Phase 9 — CS_MOVEITEM two-handed auto-eviction: equipping a 2H weapon evicts the off-hand
/// occupant into the bags (or MI_INVENFULL), the reverse (1H/shield onto a 2H) stays a straight swap, and
/// unequip-into-occupied normalizes to the equip path.</summary>
public class ItemEquipEvictionTests
{
    private const byte Backpack = 0xFF, Equip = 0xFE;

    private static Character MakeChar(byte cls = 2, byte level = 10)
    {
        var ch = new Character { CharId = 9, Name = "Smith", Class = cls, Level = level };
        ch.Invens.Add(new Inven { InvenId = Backpack });
        ch.Invens.Add(new Inven { InvenId = Equip });
        return ch;
    }

    // A two-hander occupies ES_PRMWEAPON(0) and reserves ES_SNDWEAPON(1) via its real sub-slot.
    private static Item TwoHander(byte slot, byte cls = 2)
        => new() { ItemSlot = slot, TemplateId = 6001, Count = 1,
            Template = new ItemTemplate(6001, 0, new[] { 1f, 0f, 0f, 0f }, Type: 1,
                ClassId: 1u << cls, SlotId: 1u << Proto.EsPrmWeapon, PrmSlot: Proto.EsPrmWeapon,
                SubSlot: Proto.EsSndWeapon, DefaultLevel: 1) };

    // A shield goes in the off-hand ES_SNDWEAPON(1); a 1H in ES_PRMWEAPON(0). Both have no sub-slot.
    private static Item Shield(byte slot, byte cls = 2)
        => new() { ItemSlot = slot, TemplateId = 6002, Count = 1,
            Template = new ItemTemplate(6002, 0, new[] { 1f, 0f, 0f, 0f }, Type: 6,
                ClassId: 1u << cls, SlotId: 1u << Proto.EsSndWeapon, PrmSlot: Proto.EsSndWeapon,
                SubSlot: Proto.InvalidSlot, DefaultLevel: 1) };

    private static Item OneHander(byte slot, byte cls = 2)
        => new() { ItemSlot = slot, TemplateId = 6003, Count = 1,
            Template = new ItemTemplate(6003, 0, new[] { 1f, 0f, 0f, 0f }, Type: 1,
                ClassId: 1u << cls, SlotId: 1u << Proto.EsPrmWeapon, PrmSlot: Proto.EsPrmWeapon,
                SubSlot: Proto.InvalidSlot, DefaultLevel: 1) };

    // Equippable to ES_HEAD (slot 3) by class 2 — used for the unequip-normalization test.
    private static Item Helmet(byte slot, ushort id)
        => new() { ItemSlot = slot, TemplateId = id, Count = 1,
            Template = new ItemTemplate(id, 0, new[] { 1f, 0f, 0f, 0f }, Type: 2,
                ClassId: 1u << 2, SlotId: 1u << 3, PrmSlot: 3, DefaultLevel: 1) };

    private static Item Filler(byte slot, ushort id = 999)
        => new() { ItemSlot = slot, TemplateId = id, Count = 1,
            Template = new ItemTemplate(id, 0, new[] { 1f, 0f, 0f, 0f }, Stack: 1) };

    private static MoveItemResult Result(FakeClientChannel c)
        => (MoveItemResult)new PacketReader(c.Last(Msg.CS_MOVEITEM_ACK)!).ReadByte();

    private static async Task<(MapTestHarness h, ClientSession s, FakeClientChannel c)> Enter(Character ch)
    {
        var h = new MapTestHarness();
        var (s, c) = await h.EnterAsync(9, 1, 1, name: "Smith", preSeeded: ch);
        c.Clear();
        return (h, s, c);
    }

    [Fact]
    public async Task Equip2H_WithOccupiedOffHand_EvictsShieldToBag()
    {
        var ch = MakeChar();
        ch.FindInven(Equip)!.Items.Add(Shield(Proto.EsSndWeapon));  // off-hand occupied
        ch.FindInven(Backpack)!.Items.Add(TwoHander(0));            // 2H waiting in the bag
        var (h, s, c) = await Enter(ch);

        await h.Service.DispatchClientAsync(s, MapTestHarness.MoveItemReq(Backpack, 0, Equip, Proto.EsPrmWeapon, 1));

        Assert.Equal(MoveItemResult.Success, Result(c));
        Assert.Equal(6001, ch.FindInven(Equip)!.FindItem(Proto.EsPrmWeapon)!.TemplateId); // 2H equipped
        Assert.Null(ch.FindInven(Equip)!.FindItem(Proto.EsSndWeapon));                    // off-hand cleared
        Assert.Contains(ch.FindInven(Backpack)!.Items, i => i.TemplateId == 6002);        // shield evicted to bag
        Assert.True(c.Has(Msg.CS_DELITEM_ACK) && c.Has(Msg.CS_ADDITEM_ACK) && c.Has(Msg.CS_EQUIP_ACK));
    }

    [Fact]
    public async Task Equip2H_WithEmptyOffHand_NoEviction()
    {
        var ch = MakeChar();
        ch.FindInven(Backpack)!.Items.Add(TwoHander(0));
        var (h, s, c) = await Enter(ch);

        await h.Service.DispatchClientAsync(s, MapTestHarness.MoveItemReq(Backpack, 0, Equip, Proto.EsPrmWeapon, 1));

        Assert.Equal(MoveItemResult.Success, Result(c));
        Assert.Equal(6001, ch.FindInven(Equip)!.FindItem(Proto.EsPrmWeapon)!.TemplateId);
        Assert.Empty(ch.FindInven(Backpack)!.Items); // nothing evicted back
    }

    [Fact]
    public async Task Equip2H_WhenBagIsFull_InvenFull_NothingMoves()
    {
        var ch = MakeChar();
        var bag = ch.FindInven(Backpack)!;
        bag.SlotCount = 2;                       // only slots 0 and 1 exist
        bag.Items.Add(TwoHander(0));             // slot 0 taken by the 2H being equipped
        bag.Items.Add(Filler(1));                // slot 1 taken ⇒ no room to evict the shield
        ch.FindInven(Equip)!.Items.Add(Shield(Proto.EsSndWeapon));
        var (h, s, c) = await Enter(ch);

        await h.Service.DispatchClientAsync(s, MapTestHarness.MoveItemReq(Backpack, 0, Equip, Proto.EsPrmWeapon, 1));

        Assert.Equal(MoveItemResult.InvenFull, Result(c));
        Assert.Equal(6002, ch.FindInven(Equip)!.FindItem(Proto.EsSndWeapon)!.TemplateId); // shield still equipped
        Assert.Equal(6001, bag.FindItem(0)!.TemplateId);                                  // 2H still in the bag
        Assert.False(c.Has(Msg.CS_EQUIP_ACK));                                            // no appearance change
    }

    [Fact]
    public async Task EquipShieldIntoOffHand_WhileTwoHanderEquipped_BothHandWeapon()
    {
        var ch = MakeChar();
        ch.FindInven(Equip)!.Items.Add(TwoHander(Proto.EsPrmWeapon)); // 2H reserves the off-hand
        ch.FindInven(Backpack)!.Items.Add(Shield(5));
        var (h, s, c) = await Enter(ch);

        await h.Service.DispatchClientAsync(s, MapTestHarness.MoveItemReq(Backpack, 5, Equip, Proto.EsSndWeapon, 1));

        Assert.Equal(MoveItemResult.BothHandWeapon, Result(c));
        Assert.Equal(6002, ch.FindInven(Backpack)!.FindItem(5)!.TemplateId);              // shield stays in bag
        Assert.Null(ch.FindInven(Equip)!.FindItem(Proto.EsSndWeapon));
    }

    [Fact]
    public async Task Equip1H_OverEquippedTwoHander_StraightSwap_NoEviction()
    {
        var ch = MakeChar();
        ch.FindInven(Equip)!.Items.Add(TwoHander(Proto.EsPrmWeapon));
        ch.FindInven(Backpack)!.Items.Add(OneHander(5));
        var (h, s, c) = await Enter(ch);

        await h.Service.DispatchClientAsync(s, MapTestHarness.MoveItemReq(Backpack, 5, Equip, Proto.EsPrmWeapon, 1));

        Assert.Equal(MoveItemResult.Success, Result(c));
        Assert.Equal(6003, ch.FindInven(Equip)!.FindItem(Proto.EsPrmWeapon)!.TemplateId); // 1H now equipped
        Assert.Equal(6001, ch.FindInven(Backpack)!.FindItem(5)!.TemplateId);              // 2H swapped to its old bag slot
        Assert.Single(ch.FindInven(Backpack)!.Items);                                     // no extra evicted item
    }

    [Fact]
    public async Task UnequipIntoOccupiedSlot_NormalizesToEquipSwap()
    {
        var ch = MakeChar();
        ch.FindInven(Equip)!.Items.Add(Helmet(3, id: 5000));      // helmet A equipped
        ch.FindInven(Backpack)!.Items.Add(Helmet(0, id: 5001));   // helmet B in the bag slot we target
        var (h, s, c) = await Enter(ch);

        // Unequip A (equip slot 3) onto the occupied bag slot 0 → swap: B equips, A returns to slot 0.
        await h.Service.DispatchClientAsync(s, MapTestHarness.MoveItemReq(Equip, 3, Backpack, 0, 1));

        Assert.Equal(MoveItemResult.Success, Result(c));
        Assert.Equal(5001, ch.FindInven(Equip)!.FindItem(3)!.TemplateId);      // helmet B now equipped
        Assert.Equal(5000, ch.FindInven(Backpack)!.FindItem(0)!.TemplateId);   // helmet A back in the bag
        Assert.True(c.Has(Msg.CS_EQUIP_ACK));
    }
}
