using TMap.Data;
using TMap.Protocol;
using TMap.Server.Map;
using Xunit;

namespace TMap.Server.Tests;

/// <summary>
/// Phase 40 — personal STORE (player vendor): open (list bag items at prices) → browse → buy (money buyer→seller,
/// item copied to buyer, offer + seller stack decremented) → auto-close when the last offer sells out. Same-map,
/// in-memory; offered items stay in the seller's bag until sold. All DB-free, two sessions.
/// </summary>
public class StoreTests
{
    private const byte Back = 0xFF;
    private const ushort Tradable = 300, Bound = 301;

    private static TemplateStore Store()
    {
        var t = new TemplateStore();
        t.Items[Tradable] = new ItemTemplate(Tradable, 0, new float[4], Stack: 99, IsSell: 1);   // ITEMTRADE_DEAL
        t.Items[Bound] = new ItemTemplate(Bound, 0, new float[4], IsSell: 0);
        return t;
    }

    private static Character BagChar(uint id, string name, byte slotCount = 100)
    {
        var ch = new Character { CharId = id, Name = name, Level = 5, MaxHp = 100, Hp = 100 };
        ch.Invens.Add(new Inven { InvenId = Back, SlotCount = slotCount });
        return ch;
    }

    private static void AddItem(Character ch, byte slot, ushort tmpl, byte count, TemplateStore store)
        => ch.FindInven(Back)!.Items.Add(new Item { ItemSlot = slot, TemplateId = tmpl, Count = count, Template = store.Items[tmpl] });

    private static async Task<(MapTestHarness h, ClientSession seller, FakeClientChannel cs, ClientSession buyer,
        FakeClientChannel cb)> Two(TemplateStore store, Character seller, Character buyer)
    {
        var h = new MapTestHarness(store);
        var (s, cs) = await h.EnterAsync(1, 1, 1, name: "Seller", x: 100, z: 100, preSeeded: seller);
        var (b, cb) = await h.EnterAsync(2, 2, 2, name: "Buyer", x: 100, z: 100, preSeeded: buyer);
        s.Char!.Name = "Seller"; b.Char!.Name = "Buyer";
        cs.Clear(); cb.Clear();
        return (h, s, cs, b, cb);
    }

    // ==================== open / close ====================

    [Fact]
    public async Task Open_ListsItem_AndBroadcastsToNeighbor()
    {
        var store = Store();
        var seller = BagChar(1, "Seller"); AddItem(seller, 0, Tradable, 5, store);
        var (h, s, cs, _, cb) = await Two(store, seller, BagChar(2, "Buyer"));

        await h.Service.DispatchClientAsync(s, MapTestHarness.StoreOpenReq("MyShop", (0, 1, 0, 0, Back, 0, 5)));

        Assert.Equal((byte)StoreResult.Success, new PacketReader(cs.Last(Msg.CS_STOREOPEN_ACK)!).ReadByte());
        Assert.True(s.Store.IsOpen);
        Assert.Equal("MyShop", s.Store.Name);
        Assert.True(cs.Has(Msg.CS_STOREITEMLIST_ACK));   // seller sees their own list
        Assert.True(cb.Has(Msg.CS_STOREOPEN_ACK));       // the nearby buyer gets the store icon
    }

    [Fact]
    public async Task Open_UntradableItem_Fails()
    {
        var store = Store();
        var seller = BagChar(1, "Seller"); AddItem(seller, 0, Bound, 1, store);
        var (h, s, cs, _, _) = await Two(store, seller, BagChar(2, "Buyer"));

        await h.Service.DispatchClientAsync(s, MapTestHarness.StoreOpenReq("Shop", (0, 1, 0, 0, Back, 0, 1)));

        Assert.Equal((byte)StoreResult.ItemNotDeal, new PacketReader(cs.Last(Msg.CS_STOREOPEN_ACK)!).ReadByte());
        Assert.False(s.Store.IsOpen);
    }

    [Fact]
    public async Task Open_MoreThanOwned_Fails()
    {
        var store = Store();
        var seller = BagChar(1, "Seller"); AddItem(seller, 0, Tradable, 2, store);
        var (h, s, cs, _, _) = await Two(store, seller, BagChar(2, "Buyer"));

        await h.Service.DispatchClientAsync(s, MapTestHarness.StoreOpenReq("Shop", (0, 1, 0, 0, Back, 0, 5)));   // owns 2, lists 5

        Assert.Equal((byte)StoreResult.ItemNoItemCount, new PacketReader(cs.Last(Msg.CS_STOREOPEN_ACK)!).ReadByte());
        Assert.False(s.Store.IsOpen);
    }

    [Fact]
    public async Task Close_TearsDown_AndBroadcasts()
    {
        var store = Store();
        var seller = BagChar(1, "Seller"); AddItem(seller, 0, Tradable, 5, store);
        var (h, s, cs, _, _) = await Two(store, seller, BagChar(2, "Buyer"));
        await h.Service.DispatchClientAsync(s, MapTestHarness.StoreOpenReq("Shop", (0, 1, 0, 0, Back, 0, 5)));
        cs.Clear();

        await h.Service.DispatchClientAsync(s, MapTestHarness.StoreCloseReq());

        Assert.False(s.Store.IsOpen);
        Assert.True(cs.Has(Msg.CS_STORECLOSE_ACK));       // broadcast includes self
    }

    // ==================== browse ====================

    [Fact]
    public async Task Browse_ReturnsSellerOffers()
    {
        var store = Store();
        var seller = BagChar(1, "Seller"); AddItem(seller, 0, Tradable, 5, store);
        var (h, s, _, b, cb) = await Two(store, seller, BagChar(2, "Buyer"));
        await h.Service.DispatchClientAsync(s, MapTestHarness.StoreOpenReq("Shop", (0, 1, 0, 0, Back, 0, 5)));
        cb.Clear();

        await h.Service.DispatchClientAsync(b, MapTestHarness.StoreItemListReq("Seller"));

        var r = new PacketReader(cb.Last(Msg.CS_STOREITEMLIST_ACK)!);
        Assert.Equal(1u, r.ReadUInt32());            // seller char id
        Assert.Equal("Shop", r.ReadString());        // store name
        Assert.Equal((byte)1, r.ReadByte());         // one offer
        Assert.Equal((byte)0, r.ReadByte());         // slot key
        Assert.Equal(0u, r.ReadUInt32());            // credits
        Assert.Equal(0u, r.ReadUInt32());            // gold
        Assert.Equal(1u, r.ReadUInt32());            // silver
        Assert.Equal(0u, r.ReadUInt32());            // cooper
        Assert.Equal(Tradable, r.ReadUInt16());      // item block (addItemId=false → wItemID first)
    }

    // ==================== buy ====================

    private static async Task<(MapTestHarness h, ClientSession s, ClientSession b)> Opened(
        TemplateStore store, byte offerCount, byte stackCount, byte buyerSlots = 100)
    {
        var seller = BagChar(1, "Seller"); AddItem(seller, 0, Tradable, stackCount, store);
        var buyer = BagChar(2, "Buyer", buyerSlots); buyer.EarnMoney(1_000_000);
        var (h, s, _, b, _) = await Two(store, seller, buyer);
        await h.Service.DispatchClientAsync(s, MapTestHarness.StoreOpenReq("Shop", (0, 1, 0, 0, Back, 0, offerCount)));
        return (h, s, b);
    }

    [Fact]
    public async Task Buy_TransfersItemAndMoney()
    {
        var store = Store();
        var (h, s, b) = await Opened(store, offerCount: 5, stackCount: 5);
        long buyerBefore = b.Char!.MoneyTotal;

        await h.Service.DispatchClientAsync(b, MapTestHarness.StoreItemBuyReq("Seller", item: 0, count: 2));   // 2 × 1 silver

        Assert.Equal(3, s.Char!.FindInven(Back)!.FindItem(0)!.Count);                             // seller stack 5 → 3
        Assert.Contains(b.Char.FindInven(Back)!.Items, it => it.TemplateId == Tradable && it.Count == 2);  // buyer got 2
        Assert.Equal(buyerBefore - 2000, b.Char.MoneyTotal);                                      // paid 2 silver
        Assert.Equal(2000, s.Char.MoneyTotal);                                                    // seller earned it
        Assert.Equal(3, s.Store.Find(0)!.Count);                                                  // offer 5 → 3
        Assert.True(s.Store.IsOpen);                                                              // still open
    }

    [Fact]
    public async Task Buy_LastOffer_AutoClosesStore()
    {
        var store = Store();
        var (h, s, b) = await Opened(store, offerCount: 5, stackCount: 5);

        await h.Service.DispatchClientAsync(b, MapTestHarness.StoreItemBuyReq("Seller", item: 0, count: 5));   // buy all

        Assert.Null(s.Char!.FindInven(Back)!.FindItem(0));   // seller stack gone
        Assert.False(s.Store.IsOpen);                        // store auto-closed
        Assert.Empty(s.Store.Items);
    }

    [Fact]
    public async Task Buy_PartialOffer_LeavesSellerRemainder()
    {
        var store = Store();
        var (h, s, b) = await Opened(store, offerCount: 2, stackCount: 5);   // lists 2 of a 5-stack

        await h.Service.DispatchClientAsync(b, MapTestHarness.StoreItemBuyReq("Seller", item: 0, count: 2));

        Assert.Equal(3, s.Char!.FindInven(Back)!.FindItem(0)!.Count);   // seller keeps the unlisted 3
        Assert.False(s.Store.IsOpen);                                   // but the only offer sold out → closed
    }

    [Fact]
    public async Task Buy_NotEnoughMoney_Fails()
    {
        var store = Store();
        var seller = BagChar(1, "Seller"); AddItem(seller, 0, Tradable, 5, store);
        var (h, s, _, b, cb) = await Two(store, seller, BagChar(2, "Buyer"));   // buyer broke
        await h.Service.DispatchClientAsync(s, MapTestHarness.StoreOpenReq("Shop", (0, 1, 0, 0, Back, 0, 5)));
        cb.Clear();

        await h.Service.DispatchClientAsync(b, MapTestHarness.StoreItemBuyReq("Seller", 0, 1));

        Assert.Equal((byte)StoreResult.ItemNeedMoney, new PacketReader(cb.Last(Msg.CS_STOREITEMBUY_ACK)!).ReadByte());
        Assert.Equal(5, s.Char!.FindInven(Back)!.FindItem(0)!.Count);   // untouched
    }

    [Fact]
    public async Task Buy_BuyerBagFull_Fails()
    {
        var store = Store();
        var seller = BagChar(1, "Seller"); AddItem(seller, 0, Tradable, 5, store);
        var buyer = BagChar(2, "Buyer", slotCount: 1); buyer.EarnMoney(1_000_000);
        AddItem(buyer, 0, Bound, 1, store);                          // fill the buyer's only slot (non-mergeable)
        var (h, s, _, b, cb) = await Two(store, seller, buyer);
        await h.Service.DispatchClientAsync(s, MapTestHarness.StoreOpenReq("Shop", (0, 1, 0, 0, Back, 0, 5)));
        long buyerBefore = b.Char!.MoneyTotal;
        cb.Clear();

        await h.Service.DispatchClientAsync(b, MapTestHarness.StoreItemBuyReq("Seller", 0, 1));

        Assert.Equal((byte)StoreResult.ItemInvenFull, new PacketReader(cb.Last(Msg.CS_STOREITEMBUY_ACK)!).ReadByte());
        Assert.Equal(buyerBefore, b.Char.MoneyTotal);                // not charged
        Assert.Equal(5, s.Char!.FindInven(Back)!.FindItem(0)!.Count); // seller untouched
    }
}
