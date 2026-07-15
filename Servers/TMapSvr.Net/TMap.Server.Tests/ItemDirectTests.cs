using TMap.Server.Map;
using Xunit;

namespace TMap.Server.Tests;

/// <summary>Phase 27 — incremental item persistence (the fast-path; C++ <c>TSaveItemDirect</c>). Exercises the
/// DB-free-testable decision logic: the enqueue gate (<see cref="MapService.EnqueueItemSave"/> /
/// <see cref="MapService.EnqueueItemDelete"/> only for a main, DB-loaded char once the item-id base is seeded),
/// dlID stamping + preservation, the save/delete mutual-exclusion (pick-up-then-drop nets to a delete), and the
/// per-tick drain (<see cref="MapService.FlushItemDirect"/>, which no-ops the write DB-free). The actual
/// <c>TSaveItemDirect</c>/delete SQL runs only against a live DB (verified by a live round-trip).</summary>
public class ItemDirectTests
{
    private static MapService Armed(long seed = 1000)
    {
        var svc = new MapTestHarness().Service;
        svc.ApplyItemIdSeed(seed);   // arm the fast-path without a live TInitGenItemID seed
        return svc;
    }

    private static ClientSession MainSession()
    {
        var s = new ClientSession(new FakeClientChannel()) { State = EnterState.InGame, IsMain = true, CharId = 1 };
        s.Char = new Character { CharId = 1, DbLoaded = true };
        return s;
    }

    private static Item Itm(long dlId, byte slot = 0, ushort tmpl = 200, byte count = 1)
        => new() { DlId = dlId, ItemSlot = slot, TemplateId = tmpl, Count = count };

    [Fact]
    public void EnqueueItemSave_StampsDlId_AndQueuesUpsert()
    {
        var svc = Armed(1000);
        var s = MainSession();
        var it = Itm(dlId: 0, slot: 3, tmpl: 200, count: 5);

        svc.EnqueueItemSave(s, invenId: 0xFF, it);

        Assert.NotEqual(0L, it.DlId);                       // a fresh id was minted (from the seed)
        Assert.Equal(1001L, it.DlId);                       // seed 1000 → ++ → 1001
        Assert.True(svc.PendingItemUpserts.ContainsKey(it.DlId));
        var (charId, data) = svc.PendingItemUpserts[it.DlId];
        Assert.Equal(1u, charId);
        Assert.Equal(0xFFu, data.StorageId);                // its container
        Assert.Equal((byte)3, data.ItemSlot);
        Assert.Equal((ushort)200, data.TemplateId);
        Assert.Equal((byte)5, data.Count);
        Assert.Empty(svc.PendingItemDeletes);
    }

    [Fact]
    public void EnqueueItemSave_PreservesExistingDlId()
    {
        var svc = Armed();
        svc.EnqueueItemSave(MainSession(), 0xFF, Itm(dlId: 555));
        Assert.True(svc.PendingItemUpserts.ContainsKey(555L)); // an already-persisted item keeps its PK
    }

    [Fact]
    public void EnqueueItemDelete_QueuesByDlId()
    {
        var svc = Armed();
        svc.EnqueueItemDelete(MainSession(), Itm(dlId: 555));
        Assert.Contains(555L, svc.PendingItemDeletes);
        Assert.Empty(svc.PendingItemUpserts);
    }

    [Fact]
    public void EnqueueItemDelete_NeverPersisted_NoOp()
    {
        var svc = Armed();
        svc.EnqueueItemDelete(MainSession(), Itm(dlId: 0));   // no DB row ever existed
        Assert.Empty(svc.PendingItemDeletes);
    }

    [Fact]
    public void SaveThenDelete_SameRow_NetsDelete()
    {
        var svc = Armed();
        var s = MainSession();
        var it = Itm(dlId: 555);
        svc.EnqueueItemSave(s, 0xFF, it);
        svc.EnqueueItemDelete(s, it);
        Assert.Empty(svc.PendingItemUpserts);                // the upsert was cancelled
        Assert.Contains(555L, svc.PendingItemDeletes);
    }

    [Fact]
    public void DeleteThenSave_SameRow_NetsUpsert()
    {
        var svc = Armed();
        var s = MainSession();
        var it = Itm(dlId: 555);
        svc.EnqueueItemDelete(s, it);
        svc.EnqueueItemSave(s, 0xFF, it);
        Assert.True(svc.PendingItemUpserts.ContainsKey(555L));
        Assert.Empty(svc.PendingItemDeletes);                // the delete was cancelled (re-added same tick)
    }

    [Fact]
    public void RepeatedSave_CollapsesByDlId_LatestWins()
    {
        var svc = Armed();
        var s = MainSession();
        var it = Itm(dlId: 555, count: 5);
        svc.EnqueueItemSave(s, 0xFF, it);
        it.Count = 2;                                        // e.g. two consumed
        svc.EnqueueItemSave(s, 0xFF, it);
        Assert.Single(svc.PendingItemUpserts);               // one row, not two writes
        Assert.Equal((byte)2, svc.PendingItemUpserts[555L].data.Count);
    }

    [Fact]
    public void Gate_NotSeeded_NoOp()
    {
        var svc = new MapTestHarness().Service;              // NOT armed — _itemIdReady false
        var s = MainSession();
        var it = Itm(dlId: 0);
        svc.EnqueueItemSave(s, 0xFF, it);
        svc.EnqueueItemDelete(s, Itm(dlId: 555));
        Assert.Equal(0L, it.DlId);                           // no id minted (would risk PK collision)
        Assert.Empty(svc.PendingItemUpserts);
        Assert.Empty(svc.PendingItemDeletes);
    }

    [Fact]
    public void Gate_NotMain_NoOp()
    {
        var svc = Armed();
        var s = MainSession();
        s.IsMain = false;
        svc.EnqueueItemSave(s, 0xFF, Itm(dlId: 555));
        Assert.Empty(svc.PendingItemUpserts);
    }

    [Fact]
    public void Gate_SynthChar_NoOp()
    {
        var svc = Armed();
        var s = MainSession();
        s.Char!.DbLoaded = false;                            // a synthesized (DB-free) char is never saved
        svc.EnqueueItemSave(s, 0xFF, Itm(dlId: 555));
        Assert.Empty(svc.PendingItemUpserts);
    }

    [Fact]
    public void FlushItemDirect_ClearsQueues_DbFree()
    {
        var svc = Armed();
        var s = MainSession();
        svc.EnqueueItemSave(s, 0xFF, Itm(dlId: 555));
        svc.EnqueueItemDelete(s, Itm(dlId: 777));

        svc.FlushItemDirect();                               // DB-free: drains without writing, never throws

        Assert.Empty(svc.PendingItemUpserts);
        Assert.Empty(svc.PendingItemDeletes);
    }

    [Fact]
    public async Task Drop_ThroughHandler_QueuesDelete()
    {
        var harness = new MapTestHarness();
        harness.Service.ApplyItemIdSeed(1000);
        var ch = new Character { CharId = 1, DbLoaded = true, Level = 10 };
        var bag = new Inven { InvenId = 0xFF };
        bag.Items.Add(new Item { DlId = 777, ItemSlot = 0, TemplateId = 200, Count = 1 });
        ch.Invens.Add(bag);

        var (session, _) = await harness.EnterAsync(1, 100, 42, preSeeded: ch);
        // full-stack drop: src bag 0xFF slot 0 → INVEN_NULL (0xFC)
        await harness.Service.DispatchClientAsync(session, MapTestHarness.MoveItemReq(0xFF, 0, 0xFC, 0, 1));

        Assert.Contains(777L, harness.Service.PendingItemDeletes);
    }
}
