using TMap.Data;
using TMap.Protocol;
using TMap.Server.Map;
using Xunit;

namespace TMap.Server.Tests;

/// <summary>Phase 22 — item drop-loot: a killed monster with a drop table rolls chart-type items onto its
/// corpse; CS_MONITEMLIST lists them and CS_MONITEMTAKE moves one into the taker's bags (CS_GETITEM_ACK).
/// No drop table ⇒ no loot at all (money included).</summary>
public class ItemLootTests
{
    private const ushort DropId = 300;

    private static TemplateStore LootStore()
    {
        var t = new TemplateStore();
        t.Items[DropId] = new ItemTemplate(DropId, 0, new[] { 1f, 0f, 0f, 0f }); // a droppable item
        return t;
    }

    private static Character Looter()
    {
        var ch = new Character { CharId = 1, Name = "Looter", MaxHp = 100, Hp = 100 };
        ch.Invens.Add(new Inven { InvenId = 0xFF }); // backpack, to take loot into
        return ch;
    }

    // MaxHP-10 mob with a 100%-drop table of one fixed item (id 300). 2 naked hits kill it.
    private static Monster LootMob(byte itemProb = 100, byte dropCount = 1)
        => new() { Id = 0x60001, ChartId = 500, Level = 5, MaxHp = 10, Hp = 10, MaxMp = 50, Mp = 50,
            DefendPower = 100, PosX = 100, PosZ = 100, Region = 7, Channel = 1, MapId = 0,
            ItemProb = itemProb, DropCount = dropCount, MaxWeight = 1,
            DropRows = new[] { new MonItemRow(DropId, Weight: 1, ChartType: 1, 100, 100, 100, 100) } };

    private static async Task<(MapTestHarness h, ClientSession s, FakeClientChannel c, Monster mob)> KillMob(
        Monster mob, Character? ch = null)
    {
        var h = new MapTestHarness(LootStore());
        var (s, c) = await h.EnterAsync(1, 1, 1, x: 100, z: 100, preSeeded: ch ?? Looter());
        h.Service.SpawnMonster(mob);
        h.Service.CombatRng = new Random(1);
        h.Service.LootRng = new Random(1);
        c.Clear();
        await h.Service.DispatchClientAsync(s, MapTestHarness.DefendReq(1, mob.Id));
        await h.Service.DispatchClientAsync(s, MapTestHarness.DefendReq(1, mob.Id));
        return (h, s, c, mob);
    }

    [Fact]
    public async Task Kill_DropsItem_OntoPersistentCorpse()
    {
        var (h, s, c, mob) = await KillMob(LootMob());

        Assert.NotNull(h.State.FindMonster(mob.Id));         // corpse persists (has loot)
        Assert.Single(mob.CorpseInven.Items);
        Assert.Equal(DropId, mob.CorpseInven.Items[0].TemplateId);
        Assert.True(c.Has(Msg.CS_DIE_ACK));
        Assert.False(c.Has(Msg.CS_DELMON_ACK));
    }

    [Fact]
    public async Task MonItemList_ListsCorpseItem()
    {
        var (h, s, c, mob) = await KillMob(LootMob());
        c.Clear();

        await h.Service.DispatchClientAsync(s, MapTestHarness.MonItemListReq(mob.Id));

        var r = new PacketReader(c.Last(Msg.CS_MONITEMLIST_ACK)!);
        r.ReadByte(); r.ReadByte();              // bRet, bUpdate
        Assert.Equal(mob.Id, r.ReadUInt32());    // dwMonID
        r.ReadUInt32(); r.ReadUInt32(); r.ReadUInt32(); // gold/silver/cooper
        Assert.Equal((byte)1, r.ReadByte());     // item count
        r.ReadByte();                            // item slot (WrapPacketClient: bItemID)
        Assert.Equal(DropId, r.ReadUInt16());    // wItemID
    }

    [Fact]
    public async Task MonItemTake_MovesItemToBags()
    {
        var ch = Looter();
        var (h, s, c, mob) = await KillMob(LootMob(), ch);
        byte slot = mob.CorpseInven.Items[0].ItemSlot;
        c.Clear();

        await h.Service.DispatchClientAsync(s, MapTestHarness.MonItemTakeReq(mob.Id, slot));

        Assert.Empty(mob.CorpseInven.Items);                 // taken from the corpse
        var bag = ch.FindInven(0xFF)!;
        Assert.Single(bag.Items);                            // now in the taker's backpack
        Assert.Equal(DropId, bag.Items[0].TemplateId);
        Assert.True(c.Has(Msg.CS_GETITEM_ACK));
        Assert.Equal((byte)MonItemTakeResult.Success,
            new PacketReader(c.Last(Msg.CS_MONITEMTAKE_ACK)!).ReadByte());
    }

    [Fact]
    public async Task MonItemTake_BagsFull_FullInven()
    {
        var ch = Looter();
        ch.FindInven(0xFF)!.SlotCount = 0;                   // no room
        var (h, s, c, mob) = await KillMob(LootMob(), ch);
        byte slot = mob.CorpseInven.Items[0].ItemSlot;
        c.Clear();

        await h.Service.DispatchClientAsync(s, MapTestHarness.MonItemTakeReq(mob.Id, slot));

        Assert.Single(mob.CorpseInven.Items);                // still on the corpse
        Assert.Equal((byte)MonItemTakeResult.FullInven,
            new PacketReader(c.Last(Msg.CS_MONITEMTAKE_ACK)!).ReadByte());
    }

    [Fact]
    public async Task NoDropTable_NoLoot_CorpseDespawnsImmediately()
    {
        // MaxWeight 0 ⇒ C++ AddItem early-returns ⇒ no items and no money ⇒ nothing to loot.
        var mob = LootMob();
        mob.MaxWeight = 0; mob.DropRows = System.Array.Empty<MonItemRow>();
        var (h, s, c, _) = await KillMob(mob);

        Assert.Null(h.State.FindMonster(mob.Id));            // despawned + rearmed
        Assert.True(c.Has(Msg.CS_DELMON_ACK));
    }
}
