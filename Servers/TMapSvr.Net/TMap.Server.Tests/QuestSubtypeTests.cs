using TMap.Data;
using TMap.Protocol;
using TMap.Server.Map;
using Xunit;

namespace TMap.Server.Tests;

/// <summary>Phase 29 — the additional quest subtypes ported on top of the Phase-24 core: DeleteItem, DropQuest,
/// ChapterMsg, Routing, and (same-map) Teleport. Each is driven through the real CS_QUESTEXEC handler against
/// an injected template (the DB template load is exercised live). The blocked/infra-heavy subtypes
/// (Switch/SpawnMon/Regen/DieMon/DropItem/Craft/GiveSkill/DefendSkill/SendPost) are documented in
/// PORT_STATUS.md.</summary>
public class QuestSubtypeTests
{
    private const byte QttCompQuest = 1, QttGetItem = 2, QttItemId = 5, QttMapId = 8, QttLeft = 9, QttTop = 10,
        QttHeight = 14;
    private const byte Mission = (byte)QuestType.Mission;
    private const ushort CollectItem = 200, FetchItem = 201, Giver = 500;

    private static QuestTemplate Q(uint id, QuestType type, uint trigId, uint parent = 0,
        (uint id, byte type, byte count)[]? terms = null)
    {
        var q = new QuestTemplate
        {
            QuestId = id, Type = (byte)type, TriggerType = 3 /*TT_TALKNPC*/, TriggerId = trigId, ParentId = parent,
        };
        if (terms is not null) foreach (var t in terms) q.Terms.Add(new QuestTerm(t.id, t.type, t.count));
        return q;
    }

    private static TemplateStore Store()
    {
        var t = new TemplateStore();
        t.Items[CollectItem] = new ItemTemplate(CollectItem, 0, new[] { 0f, 0f, 0f, 0f }, DefaultLevel: 5, Stack: 10);
        t.Items[FetchItem] = new ItemTemplate(FetchItem, 0, new[] { 0f, 0f, 0f, 0f }, DefaultLevel: 5, Stack: 10);
        return t;
    }

    private static Character Hero()
    {
        var ch = new Character { CharId = 1, Name = "Hero", Class = 0, Country = 1, Level = 5, MaxHp = 100, Hp = 100 };
        ch.Invens.Add(new Inven { InvenId = 0xFF });
        return ch;
    }

    private async Task<(MapTestHarness h, ClientSession s, FakeClientChannel c)> Enter(TemplateStore store, Character? ch = null)
    {
        var h = new MapTestHarness(store);
        var (s, c) = await h.EnterAsync(1, 1, 1, x: 100, z: 100, preSeeded: ch ?? Hero());
        s.Char!.Country = 1; s.Char.Class = 0; s.Char.Level = 5; s.Char.MapId = 0;
        h.Service.AddNpc(new Npc { Id = Giver, Type = 2, Country = 3, MapId = 0 });
        h.Service.InitQuests();
        c.Clear();
        return (h, s, c);
    }

    [Fact]
    public async Task DeleteItem_RemovesEveryStackFromBags()
    {
        var store = Store();
        store.Quests[6000] = Q(6000, QuestType.DeleteItem, Giver, terms: new[] { ((uint)CollectItem, QttItemId, (byte)0) });
        var ch = Hero();
        ch.FindInven(0xFF)!.Items.Add(new Item { ItemSlot = 0, TemplateId = CollectItem, Count = 3, Template = store.Item(CollectItem) });
        var (h, s, c) = await Enter(store, ch);

        await h.Service.DispatchClientAsync(s, MapTestHarness.QuestExecReq(6000));

        Assert.DoesNotContain(s.Char!.FindInven(0xFF)!.Items, i => i.TemplateId == CollectItem);
        Assert.True(c.Has(Msg.CS_DELITEM_ACK));
    }

    [Fact]
    public async Task DropQuest_AbandonsReferencedQuest()
    {
        var store = Store();
        store.Quests[1000] = Q(1000, QuestType.Mission, Giver, terms: new[] { ((uint)CollectItem, QttGetItem, (byte)2) });
        store.Quests[6100] = Q(6100, QuestType.DropQuest, Giver, terms: new[] { (1000u, QttCompQuest, (byte)0) });
        var (h, s, c) = await Enter(store);
        await h.Service.DispatchClientAsync(s, MapTestHarness.QuestExecReq(1000));
        Assert.True(s.Char!.IsRunningQuest(1000));
        c.Clear();

        await h.Service.DispatchClientAsync(s, MapTestHarness.QuestExecReq(6100));

        Assert.Equal((byte)QuestResult.Drop, new PacketReader(c.Last(Msg.CS_QUESTCOMPLETE_ACK)!).ReadByte());
        Assert.False(s.Char.IsRunningQuest(1000));
    }

    [Fact]
    public async Task ChapterMsg_SendsQuestId()
    {
        var store = Store();
        store.Quests[6200] = Q(6200, QuestType.ChapterMsg, Giver);
        var (h, s, c) = await Enter(store);

        await h.Service.DispatchClientAsync(s, MapTestHarness.QuestExecReq(6200));

        Assert.Equal(6200u, new PacketReader(c.Last(Msg.CS_CHAPTERMSG_ACK)!).ReadUInt32());
    }

    [Fact]
    public async Task Routing_SendsNpcItemList()
    {
        var store = Store();
        store.Quests[6300] = Q(6300, QuestType.Routing, Giver,
            terms: new[] { ((uint)CollectItem, QttItemId, (byte)0), ((uint)FetchItem, QttItemId, (byte)0) });
        var (h, s, c) = await Enter(store);

        await h.Service.DispatchClientAsync(s, MapTestHarness.QuestExecReq(6300));

        var r = new PacketReader(c.Last(Msg.CS_NPCITEMLIST_ACK)!);
        Assert.Equal(Giver, r.ReadUInt16());          // wNpcID = the quest's trigger id
        Assert.Equal((byte)7, r.ReadByte());          // TNPC_BOX
        r.ReadByte();                                  // discount rate (0)
        Assert.Equal((byte)2, r.ReadByte());          // two items
        Assert.Equal(CollectItem, r.ReadUInt16());
        Assert.Equal(FetchItem, r.ReadUInt16());
    }

    [Fact]
    public async Task Teleport_SameMap_FarAway_GoesThroughTheWorld()
    {
        // More than a cell away, so not a local hop: the C++ starts the world round trip (TMapSvr.cpp:7258) and the
        // position only changes when the world answers MW_STARTTELEPORT_REQ.
        var store = Store();
        store.Quests[6400] = Q(6400, QuestType.Teleport, Giver, terms: new[]
        {
            (0u, QttMapId, (byte)0),      // same map (0)
            (250u, QttLeft, (byte)0),     // x
            (0u, QttHeight, (byte)0),     // y
            (400u, QttTop, (byte)0),      // z
        });
        var (h, s, c) = await Enter(store);   // hero starts at (100, ·, 100) on map 0

        await h.Service.DispatchClientAsync(s, MapTestHarness.QuestExecReq(6400));

        Assert.True(h.World.Has(Msg.MW_BEGINTELEPORT_ACK));
        Assert.True(c.Has(Msg.CS_BEGINTELEPORT_ACK));
        Assert.Equal(100f, s.Char!.PosX);     // not yet

        var w = new PacketWriter(Msg.MW_STARTTELEPORT_REQ);
        w.WriteUInt32(s.CharId); w.WriteUInt32(s.Key); w.WriteByte(s.Channel); w.WriteUInt16(0);
        w.WriteFloat(250); w.WriteFloat(0); w.WriteFloat(400);
        await h.Service.DispatchWorldAsync(w.ToArray());

        Assert.Equal(250f, s.Char.PosX);
        Assert.Equal(400f, s.Char.PosZ);
    }

    [Fact]
    public async Task Teleport_CrossMap_DoesNotReposition()
    {
        var store = Store();
        store.Quests[6500] = Q(6500, QuestType.Teleport, Giver, terms: new[]
        {
            (9u, QttMapId, (byte)0),      // a DIFFERENT map ⇒ the world round trip: no reposition yet
            (250u, QttLeft, (byte)0),
            (400u, QttTop, (byte)0),
        });
        var (h, s, c) = await Enter(store);

        await h.Service.DispatchClientAsync(s, MapTestHarness.QuestExecReq(6500));

        Assert.Equal(100f, s.Char!.PosX);   // unchanged — cross-map teleport is deferred
        Assert.Equal(100f, s.Char.PosZ);
    }
}
