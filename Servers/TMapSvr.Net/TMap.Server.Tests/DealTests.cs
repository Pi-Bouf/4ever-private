using TMap.Data;
using TMap.Protocol;
using TMap.Server.Map;
using Xunit;

namespace TMap.Server.Tests;

/// <summary>
/// Phase 39 — player-to-player DEAL (trade): ASK → RLY(accept) → ADD(offer) → confirm×2 → atomic swap. Items are
/// staged as copies and only leave the bag at execution; a full guard (offers still valid + both bags can
/// receive) precedes any mutation, so a full bag aborts cleanly. All DB-free, two sessions.
/// </summary>
public class DealTests
{
    private const byte Back = 0xFF;
    private const ushort Tradable = 300, Bound = 301;

    private static TemplateStore Store()
    {
        var t = new TemplateStore();
        t.Items[Tradable] = new ItemTemplate(Tradable, 0, new float[4], IsSell: 1);   // ITEMTRADE_DEAL
        t.Items[Bound] = new ItemTemplate(Bound, 0, new float[4], IsSell: 0);         // not tradable
        return t;
    }

    private static Character BagChar(uint id, string name, byte slotCount = 100)
    {
        var ch = new Character { CharId = id, Name = name, Level = 5, MaxHp = 100, Hp = 100 };
        ch.Invens.Add(new Inven { InvenId = Back, SlotCount = slotCount });
        return ch;
    }

    private static void AddItem(Character ch, byte slot, ushort tmpl, TemplateStore store)
        => ch.FindInven(Back)!.Items.Add(new Item { ItemSlot = slot, TemplateId = tmpl, Count = 1, Template = store.Items[tmpl] });

    private static async Task<(MapTestHarness h, ClientSession a, FakeClientChannel ca, ClientSession b,
        FakeClientChannel cb)> Two(TemplateStore store, Character alice, Character bob)
    {
        var h = new MapTestHarness(store);
        var (a, ca) = await h.EnterAsync(1, 1, 1, name: "Alice", x: 100, z: 100, preSeeded: alice);
        var (b, cb) = await h.EnterAsync(2, 2, 2, name: "Bob", x: 100, z: 100, preSeeded: bob);
        a.Char!.Name = "Alice"; b.Char!.Name = "Bob";     // ensure FindByName resolves
        ca.Clear(); cb.Clear();
        return (h, a, ca, b, cb);
    }

    // Drives ASK + accept so both sides are in the open (DEAL_START) window.
    private static async Task Open(MapTestHarness h, ClientSession a, ClientSession b)
    {
        await h.Service.DispatchClientAsync(a, MapTestHarness.DealAskReq("Bob"));
        await h.Service.DispatchClientAsync(b, MapTestHarness.DealRlyReq(0, "Alice"));
    }

    // ==================== handshake ====================

    [Fact]
    public async Task Ask_NotifiesTarget()
    {
        var store = Store();
        var (h, a, _, b, cb) = await Two(store, BagChar(1, "Alice"), BagChar(2, "Bob"));

        await h.Service.DispatchClientAsync(a, MapTestHarness.DealAskReq("Bob"));

        Assert.True(cb.Has(Msg.CS_DEALITEMASK_ACK));
        Assert.Equal("Alice", new PacketReader(cb.Last(Msg.CS_DEALITEMASK_ACK)!).ReadString());
    }

    [Fact]
    public async Task Accept_OpensWindowForBoth()
    {
        var store = Store();
        var (h, a, ca, b, cb) = await Two(store, BagChar(1, "Alice"), BagChar(2, "Bob"));

        await Open(h, a, b);

        Assert.True(ca.Has(Msg.CS_DEALITEMSTART_ACK));
        Assert.True(cb.Has(Msg.CS_DEALITEMSTART_ACK));
        Assert.True(a.Deal.InProgress);
        Assert.True(b.Deal.InProgress);
        Assert.Equal("Bob", a.Deal.TargetName);
        Assert.Equal("Alice", b.Deal.TargetName);
    }

    [Fact]
    public async Task Decline_EndsBoth()
    {
        var store = Store();
        var (h, a, ca, b, cb) = await Two(store, BagChar(1, "Alice"), BagChar(2, "Bob"));
        await h.Service.DispatchClientAsync(a, MapTestHarness.DealAskReq("Bob"));

        await h.Service.DispatchClientAsync(b, MapTestHarness.DealRlyReq(1, "Alice"));   // ASK_NO

        Assert.Equal((byte)DealResult.Deny, new PacketReader(ca.Last(Msg.CS_DEALITEMEND_ACK)!).ReadByte());
        Assert.False(a.Deal.InProgress);
        Assert.False(b.Deal.InProgress);
    }

    [Fact]
    public async Task Add_NotifiesPartner_WithOffer()
    {
        var store = Store();
        var alice = BagChar(1, "Alice"); AddItem(alice, 0, Tradable, store);
        var (h, a, _, b, cb) = await Two(store, alice, BagChar(2, "Bob"));
        await Open(h, a, b);
        cb.Clear();

        await h.Service.DispatchClientAsync(a, MapTestHarness.DealAddReq(0, 0, 0, (Back, 0)));

        var r = new PacketReader(cb.Last(Msg.CS_DEALITEMADD_ACK)!);   // partner sees what they'll receive
        r.ReadUInt32(); r.ReadUInt32(); r.ReadUInt32();               // gold/silver/cooper
        Assert.Equal((byte)1, r.ReadByte());                         // item count
        Assert.Equal(Tradable, r.ReadUInt16());                      // wItemID (addItemId=false → no slot byte)
        Assert.Equal((byte)DealStatus.AddItem, a.Deal.Dealing);
    }

    // ==================== full swap ====================

    [Fact]
    public async Task FullFlow_SwapsItemAndMoney()
    {
        var store = Store();
        var alice = BagChar(1, "Alice"); AddItem(alice, 0, Tradable, store);   // Alice offers an item
        var bob = BagChar(2, "Bob"); bob.EarnMoney(5000);                      // Bob offers 5 silver
        var (h, a, _, b, _) = await Two(store, alice, bob);
        await Open(h, a, b);

        await h.Service.DispatchClientAsync(a, MapTestHarness.DealAddReq(0, 0, 0, (Back, 0)));   // Alice: the item
        await h.Service.DispatchClientAsync(b, MapTestHarness.DealAddReq(0, 5, 0));              // Bob: 5000 money
        await h.Service.DispatchClientAsync(a, MapTestHarness.DealReq(1));                       // first confirm (arm)
        await h.Service.DispatchClientAsync(b, MapTestHarness.DealReq(1));                       // second → execute

        // Item moved Alice → Bob; money moved Bob → Alice.
        Assert.Null(a.Char!.FindInven(Back)!.FindItem(0));                                       // Alice lost the item
        Assert.Contains(b.Char!.FindInven(Back)!.Items, it => it.TemplateId == Tradable);        // Bob got it
        Assert.Equal(5000, a.Char.MoneyTotal);                                                   // Alice got the money
        Assert.Equal(0, b.Char.MoneyTotal);                                                      // Bob paid it
        Assert.False(a.Deal.InProgress);                                                         // both cleared
        Assert.False(b.Deal.InProgress);
    }

    [Fact]
    public async Task FirstConfirm_DoesNotExecute()
    {
        var store = Store();
        var alice = BagChar(1, "Alice"); AddItem(alice, 0, Tradable, store);
        var (h, a, _, b, _) = await Two(store, alice, BagChar(2, "Bob"));
        await Open(h, a, b);
        await h.Service.DispatchClientAsync(a, MapTestHarness.DealAddReq(0, 0, 0, (Back, 0)));
        await h.Service.DispatchClientAsync(b, MapTestHarness.DealAddReq(0, 0, 0));

        await h.Service.DispatchClientAsync(a, MapTestHarness.DealReq(1));   // only Alice confirms

        Assert.NotNull(a.Char!.FindInven(Back)!.FindItem(0));   // nothing swapped yet
        Assert.True(a.Deal.InProgress);
        Assert.Equal((byte)DealStatus.Conform, a.Deal.Status);
    }

    [Fact]
    public async Task Cancel_EndsBoth()
    {
        var store = Store();
        var alice = BagChar(1, "Alice"); AddItem(alice, 0, Tradable, store);
        var (h, a, ca, b, _) = await Two(store, alice, BagChar(2, "Bob"));
        await Open(h, a, b);
        await h.Service.DispatchClientAsync(a, MapTestHarness.DealAddReq(0, 0, 0, (Back, 0)));

        await h.Service.DispatchClientAsync(a, MapTestHarness.DealReq(0));   // okey 0 = cancel

        Assert.Equal((byte)DealResult.Cancel, new PacketReader(ca.Last(Msg.CS_DEALITEMEND_ACK)!).ReadByte());
        Assert.False(a.Deal.InProgress);
        Assert.False(b.Deal.InProgress);
        Assert.NotNull(a.Char!.FindInven(Back)!.FindItem(0));   // item stays
    }

    // ==================== gates ====================

    [Fact]
    public async Task Add_UntradableItem_Rejected()
    {
        var store = Store();
        var alice = BagChar(1, "Alice"); AddItem(alice, 0, Bound, store);   // no ITEMTRADE_DEAL bit
        var (h, a, ca, b, _) = await Two(store, alice, BagChar(2, "Bob"));
        await Open(h, a, b);

        await h.Service.DispatchClientAsync(a, MapTestHarness.DealAddReq(0, 0, 0, (Back, 0)));

        Assert.Equal((byte)DealResult.NoItem, new PacketReader(ca.Last(Msg.CS_DEALITEMEND_ACK)!).ReadByte());
        Assert.False(a.Deal.InProgress);   // deal torn down
        Assert.NotNull(a.Char!.FindInven(Back)!.FindItem(0));
    }

    [Fact]
    public async Task Add_ReceiverBagFull_Aborts()
    {
        var store = Store();
        var alice = BagChar(1, "Alice"); AddItem(alice, 0, Tradable, store);
        var bob = BagChar(2, "Bob", slotCount: 1);                          // 1-slot bag…
        AddItem(bob, 0, Tradable, store);                                   // …already full
        var (h, a, ca, b, cb) = await Two(store, alice, bob);
        await Open(h, a, b);

        await h.Service.DispatchClientAsync(a, MapTestHarness.DealAddReq(0, 0, 0, (Back, 0)));

        Assert.Equal((byte)DealResult.CantRecv, new PacketReader(ca.Last(Msg.CS_DEALITEMEND_ACK)!).ReadByte());
        Assert.False(a.Deal.InProgress);
        Assert.NotNull(a.Char!.FindInven(Back)!.FindItem(0));   // no swap; Alice keeps her item
    }

    [Fact]
    public async Task Add_NotEnoughMoney_Aborts()
    {
        var store = Store();
        var (h, a, ca, b, _) = await Two(store, BagChar(1, "Alice"), BagChar(2, "Bob"));   // Alice broke
        await Open(h, a, b);

        await h.Service.DispatchClientAsync(a, MapTestHarness.DealAddReq(0, 9, 0));   // offers 9000 she doesn't have

        Assert.Equal((byte)DealResult.NoMoney, new PacketReader(ca.Last(Msg.CS_DEALITEMEND_ACK)!).ReadByte());
        Assert.False(a.Deal.InProgress);
    }
}
