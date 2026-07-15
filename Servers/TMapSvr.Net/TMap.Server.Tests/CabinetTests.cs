using TMap.Data;
using TMap.Protocol;
using TMap.Server.Map;
using Xunit;

namespace TMap.Server.Tests;

/// <summary>
/// Phase 37 — the player CABINET (item warehouse): open/list/itemlist/put-in/take-out against per-character
/// in-memory storage (up to 3 cabinets × 16 items). Deposits are gated by the <c>ITEMTRADE_CABINET</c> bit,
/// merge into same-stacks then spill to one new slot; take-out is whole-stack only and charges a per-cabinet
/// fee (deducted only on a successful move). All DB-free.
/// </summary>
public class CabinetTests
{
    private const byte Back = 0xFF;
    private const byte CabBit = 4;              // ITEMTRADE_CABINET
    private const byte SellBit = 2;             // ITEMTRADE_SELL (not cabinet-storable)

    private static ItemTemplate Tmpl(ushort id, byte stack = 10, byte isSell = CabBit)
        => new(id, 0, new float[4], Stack: stack, IsSell: isSell);

    private static Item Itm(byte slot, ushort tmplId, byte count, ItemTemplate t)
        => new() { ItemSlot = slot, TemplateId = tmplId, Count = count, Template = t };

    private static Character Hero()
        => new() { CharId = 1, Name = "Hero", Level = 5, MaxHp = 100, Hp = 100 };

    private static async Task<(MapTestHarness h, ClientSession s, FakeClientChannel c, Character ch)> Enter(Character ch)
    {
        var h = new MapTestHarness();
        var (s, c) = await h.EnterAsync(1, 1, 1, x: 100, z: 100, preSeeded: ch);
        c.Clear();
        return (h, s, c, s.Char!);
    }

    // ==================== open ====================

    [Fact]
    public async Task Open_DefaultCabinet_Succeeds_FreeOfCharge()
    {
        var (h, s, c, ch) = await Enter(Hero());

        await h.Service.DispatchClientAsync(s, MapTestHarness.CabinetOpenReq(0));

        var r = new PacketReader(c.Last(Msg.CS_CABINETOPEN_ACK)!);
        Assert.Equal((byte)CabinetResult.Success, r.ReadByte());
        Assert.Equal((byte)0, r.ReadByte());
        Assert.True(ch.FindCabinet(0) is { Use: true });
        Assert.False(c.Has(Msg.CS_MONEY_ACK));           // cabinet 0 is free
    }

    [Fact]
    public async Task Open_SecondCabinet_ChargesTenThousand()
    {
        var ch = Hero();
        ch.EarnMoney(1_000_000);
        long before = ch.MoneyTotal;
        var (h, s, c, _) = await Enter(ch);

        await h.Service.DispatchClientAsync(s, MapTestHarness.CabinetOpenReq(1));

        Assert.Equal((byte)CabinetResult.Success, new PacketReader(c.Last(Msg.CS_CABINETOPEN_ACK)!).ReadByte());
        Assert.Equal(before - 10000, ch.MoneyTotal);
        Assert.True(c.Has(Msg.CS_MONEY_ACK));
    }

    [Fact]
    public async Task Open_SecondCabinet_Broke_NeedMoney()
    {
        var (h, s, c, ch) = await Enter(Hero());        // no money

        await h.Service.DispatchClientAsync(s, MapTestHarness.CabinetOpenReq(1));

        Assert.Equal((byte)CabinetResult.NeedMoney, new PacketReader(c.Last(Msg.CS_CABINETOPEN_ACK)!).ReadByte());
        Assert.Null(ch.FindCabinet(1));
    }

    [Fact]
    public async Task Open_AlreadyOpen_ReturnsAlready()
    {
        var (h, s, c, _) = await Enter(Hero());
        await h.Service.DispatchClientAsync(s, MapTestHarness.CabinetOpenReq(0));
        c.Clear();

        await h.Service.DispatchClientAsync(s, MapTestHarness.CabinetOpenReq(0));

        Assert.Equal((byte)CabinetResult.Already, new PacketReader(c.Last(Msg.CS_CABINETOPEN_ACK)!).ReadByte());
    }

    [Fact]
    public async Task Open_OutOfRangeId_ReturnsMax()
    {
        var (h, s, c, ch) = await Enter(Hero());

        await h.Service.DispatchClientAsync(s, MapTestHarness.CabinetOpenReq(3));   // >= CABINET_COUNT

        Assert.Equal((byte)CabinetResult.Max, new PacketReader(c.Last(Msg.CS_CABINETOPEN_ACK)!).ReadByte());
        Assert.Empty(ch.Cabinets);
    }

    // ==================== list / itemlist ====================

    [Fact]
    public async Task List_ReturnsPerCabinetOpenState()
    {
        var ch = Hero();
        ch.Cabinets.Add(new Cabinet { CabinetId = 2, Use = false });   // a loaded-but-unopened cabinet
        var (h, s, c, _) = await Enter(ch);
        await h.Service.DispatchClientAsync(s, MapTestHarness.CabinetOpenReq(0));
        c.Clear();

        await h.Service.DispatchClientAsync(s, MapTestHarness.CabinetListReq());

        var r = new PacketReader(c.Last(Msg.CS_CABINETLIST_ACK)!);
        Assert.Equal((byte)2, r.ReadByte());             // two cabinets, ascending id
        Assert.Equal((byte)0, r.ReadByte()); Assert.Equal((byte)1, r.ReadByte());   // {0, use}
        Assert.Equal((byte)2, r.ReadByte()); Assert.Equal((byte)0, r.ReadByte());   // {2, unused}
    }

    [Fact]
    public async Task ItemList_UnopenedCabinet_NotUse()
    {
        var ch = Hero();
        ch.Cabinets.Add(new Cabinet { CabinetId = 1, Use = false });
        var (h, s, c, _) = await Enter(ch);

        await h.Service.DispatchClientAsync(s, MapTestHarness.CabinetItemListReq(1));

        Assert.Equal((byte)CabinetResult.NotUse, new PacketReader(c.Last(Msg.CS_CABINETITEMLIST_ACK)!).ReadByte());
    }

    [Fact]
    public async Task ItemList_MissingCabinet_Silent()
    {
        var (h, s, c, _) = await Enter(Hero());

        await h.Service.DispatchClientAsync(s, MapTestHarness.CabinetItemListReq(2));

        Assert.False(c.Has(Msg.CS_CABINETITEMLIST_ACK));
    }

    // ==================== put-in ====================

    private static async Task<(MapTestHarness h, ClientSession s, FakeClientChannel c, Character ch)> OpenedWith(
        params Item[] backpackItems)
    {
        var ch = Hero();
        var bag = new Inven { InvenId = Back };
        bag.Items.AddRange(backpackItems);
        ch.Invens.Add(bag);
        var t = await Enter(ch);
        await t.h.Service.DispatchClientAsync(t.s, MapTestHarness.CabinetOpenReq(0));
        t.c.Clear();
        return t;
    }

    [Fact]
    public async Task Putin_NewStack_MovesFromBag_AndAssignsStId1()
    {
        var tp = Tmpl(200);
        var (h, s, c, ch) = await OpenedWith(Itm(slot: 5, tmplId: 200, count: 3, tp));

        await h.Service.DispatchClientAsync(s, MapTestHarness.CabinetPutinReq(0, Back, itemSlot: 5, count: 3));

        var stored = Assert.Single(ch.FindCabinet(0)!.Items);
        Assert.Equal(200, stored.TemplateId);
        Assert.Equal(3, stored.Count);
        Assert.Equal(1u, stored.StItemId);
        Assert.Null(ch.FindInven(Back)!.FindItem(5));    // whole stack left the bag
        Assert.True(c.Has(Msg.CS_CABINETITEMLIST_ACK));
    }

    [Fact]
    public async Task Putin_NotCabinetTradable_Rejected()
    {
        var tp = Tmpl(200, isSell: SellBit);             // sellable but NOT cabinet-storable
        var (h, s, c, ch) = await OpenedWith(Itm(5, 200, 3, tp));

        await h.Service.DispatchClientAsync(s, MapTestHarness.CabinetPutinReq(0, Back, 5, 3));

        Assert.Equal((byte)CabinetResult.NotUse, new PacketReader(c.Last(Msg.CS_CABINETITEMLIST_ACK)!).ReadByte());
        Assert.Empty(ch.FindCabinet(0)!.Items);
        Assert.Equal(3, ch.FindInven(Back)!.FindItem(5)!.Count);   // untouched
    }

    [Fact]
    public async Task Putin_MergesIntoExistingStack()
    {
        var tp = Tmpl(200, stack: 10);
        var (h, s, c, ch) = await OpenedWith(Itm(5, 200, 5, tp));

        await h.Service.DispatchClientAsync(s, MapTestHarness.CabinetPutinReq(0, Back, 5, 2));   // 2 → new stack (stId 1, cnt 2)
        await h.Service.DispatchClientAsync(s, MapTestHarness.CabinetPutinReq(0, Back, 5, 3));   // 3 → merge into stId 1

        var stored = Assert.Single(ch.FindCabinet(0)!.Items);       // still one stack
        Assert.Equal(1u, stored.StItemId);
        Assert.Equal(5, stored.Count);                              // 2 + 3 merged
        Assert.Null(ch.FindInven(Back)!.FindItem(5));               // bag emptied
    }

    [Fact]
    public async Task Putin_Full_NoMergeable_ReturnsFull()
    {
        var tp = Tmpl(200);
        var (h, s, c, ch) = await OpenedWith(Itm(5, 200, 1, tp));
        var cab = ch.FindCabinet(0)!;
        for (uint i = 1; i <= 16; i++)                              // 16 DISTINCT (non-mergeable) stored items
            cab.Items.Add(new Item { StItemId = i, TemplateId = (ushort)(300 + i), Count = 1, Template = Tmpl((ushort)(300 + i)) });

        await h.Service.DispatchClientAsync(s, MapTestHarness.CabinetPutinReq(0, Back, 5, 1));

        Assert.Equal((byte)CabinetResult.Full, new PacketReader(c.Last(Msg.CS_CABINETITEMLIST_ACK)!).ReadByte());
        Assert.Equal(16, cab.Items.Count);
        Assert.Equal(1, ch.FindInven(Back)!.FindItem(5)!.Count);    // not deposited
    }

    // ==================== take-out ====================

    [Fact]
    public async Task Takeout_WholeStack_ToBag_ChargesFee()
    {
        var tp = Tmpl(200);
        var (h, s, c, ch) = await OpenedWith(Itm(5, 200, 3, tp));
        ch.EarnMoney(1000);
        long before = ch.MoneyTotal;
        await h.Service.DispatchClientAsync(s, MapTestHarness.CabinetPutinReq(0, Back, 5, 3));
        c.Clear();

        await h.Service.DispatchClientAsync(s, MapTestHarness.CabinetTakeoutReq(0, stItemId: 1, count: 3, invenId: Back, itemSlot: 6));

        Assert.Empty(ch.FindCabinet(0)!.Items);                      // gone from the cabinet
        Assert.Equal(3, ch.FindInven(Back)!.FindItem(6)!.Count);     // back in the bag at slot 6
        Assert.Equal(before - 100, ch.MoneyTotal);                   // cabinet-0 use fee
        Assert.True(c.Has(Msg.CS_MONEY_ACK));
    }

    [Fact]
    public async Task Takeout_WrongCount_NoMove()
    {
        var tp = Tmpl(200);
        var (h, s, c, ch) = await OpenedWith(Itm(5, 200, 3, tp));
        ch.EarnMoney(1000);
        await h.Service.DispatchClientAsync(s, MapTestHarness.CabinetPutinReq(0, Back, 5, 3));
        c.Clear();

        await h.Service.DispatchClientAsync(s, MapTestHarness.CabinetTakeoutReq(0, 1, count: 2, invenId: Back, itemSlot: 6)); // != stored 3

        Assert.Single(ch.FindCabinet(0)!.Items);                     // still stored
        Assert.Null(ch.FindInven(Back)!.FindItem(6));
    }

    [Fact]
    public async Task Takeout_BagFull_NoMove_FeeNotCharged()
    {
        var tp = Tmpl(200);
        var ch = Hero();
        var bag = new Inven { InvenId = Back, SlotCount = 1 };       // a one-slot bag
        bag.Items.Add(Itm(0, 200, 3, tp));
        ch.Invens.Add(bag);
        ch.EarnMoney(1000);
        var (h, s, c) = (new MapTestHarness(), default(ClientSession)!, default(FakeClientChannel)!);
        (s, c) = await h.EnterAsync(1, 1, 1, x: 100, z: 100, preSeeded: ch);
        await h.Service.DispatchClientAsync(s, MapTestHarness.CabinetOpenReq(0));
        await h.Service.DispatchClientAsync(s, MapTestHarness.CabinetPutinReq(0, Back, itemSlot: 0, count: 3)); // bag now empty
        bag.Items.Add(new Item { ItemSlot = 0, TemplateId = 999, Count = 1, Template = Tmpl(999) });           // refill the only slot
        long before = ch.MoneyTotal;
        c.Clear();

        await h.Service.DispatchClientAsync(s, MapTestHarness.CabinetTakeoutReq(0, 1, count: 3, invenId: Back, itemSlot: 0));

        Assert.Single(ch.FindCabinet(0)!.Items);                     // could not fit → stayed in the cabinet
        Assert.Equal(before, ch.MoneyTotal);                         // fee deducted only on success
    }

    // ==================== wire layout ====================

    [Fact]
    public async Task ItemList_WireLayout_OmitsSlotByte()
    {
        var tp = Tmpl(200);
        var (h, s, c, _) = await OpenedWith(Itm(5, 200, 3, tp));
        await h.Service.DispatchClientAsync(s, MapTestHarness.CabinetPutinReq(0, Back, 5, 3));
        c.Clear();

        await h.Service.DispatchClientAsync(s, MapTestHarness.CabinetItemListReq(0));

        var r = new PacketReader(c.Last(Msg.CS_CABINETITEMLIST_ACK)!);
        Assert.Equal((byte)CabinetResult.Success, r.ReadByte());
        Assert.Equal((byte)0, r.ReadByte());             // bCabinetID
        Assert.Equal(1u, r.ReadUInt32());                // item count
        Assert.Equal(1u, r.ReadUInt32());                // dwStItemID
        Assert.Equal((ushort)200, r.ReadUInt16());       // wItemID FIRST (addItemId=false → no leading slot byte)
    }

    // ==================== persistence enqueue (armed) ====================

    [Fact]
    public void EnqueueCabinetItemSave_StampsCabinetStorage()
    {
        var svc = new MapTestHarness().Service;
        svc.ApplyItemIdSeed(1000);
        var s = new ClientSession(new FakeClientChannel()) { State = EnterState.InGame, IsMain = true, CharId = 1 };
        s.Char = new Character { CharId = 1, DbLoaded = true };
        var it = new Item { StItemId = 4, TemplateId = 200, Count = 3 };

        svc.EnqueueCabinetItemSave(s, cabinetId: 2, it);

        Assert.True(svc.PendingItemUpserts.ContainsKey(it.DlId));
        var (_, data) = svc.PendingItemUpserts[it.DlId];
        Assert.Equal(Proto.StorageCabinet, data.StorageType);   // stored as cabinet, not inven
        Assert.Equal(4u, data.StorageId);                       // = StItemId
        Assert.Equal((byte)2, data.ItemSlot);                   // = cabinet id
    }
}
