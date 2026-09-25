using TMap.Data;
using TMap.Protocol;
using TMap.Server.Map;
using Xunit;

namespace TMap.Server.Tests;

/// <summary>Extra bags (C++ OnCS_INVENADD/INVENDEL/INVENMOVE_REQ, CSHandler.cpp:9886-10111) and the return point
/// (OnCS_SETRETURNPOS_REQ, CSHandler.cpp:10743).</summary>
public class BagTests
{
    private const ushort BagItem = 3001, Potion = 4001;

    private static TemplateStore Store()
    {
        var t = new TemplateStore();
        t.Items[BagItem] = new ItemTemplate(BagItem, 0, new float[4], Type: 11 /* IT_INVEN */, DefaultLevel: 5);
        t.Items[Potion] = new ItemTemplate(Potion, 0, new float[4], Type: 7 /* IT_USE */);
        return t;
    }

    private static async Task<(MapTestHarness h, ClientSession s, FakeClientChannel c, Character ch)> Setup(byte level = 10)
    {
        var t = Store();
        var h = new MapTestHarness(t);
        var ch = new Character { CharId = 1, Name = "Hero", MaxHp = 100, Hp = 100 };
        var backpack = new Inven { InvenId = 0 };
        backpack.Items.Add(new Item { ItemSlot = 4, TemplateId = BagItem, Template = t.Items[BagItem], Count = 1, EndTime = 777 });
        backpack.Items.Add(new Item { ItemSlot = 5, TemplateId = Potion, Template = t.Items[Potion], Count = 3 });
        ch.Invens.Add(backpack);
        var (s, c) = await h.EnterAsync(1, 1, 1, preSeeded: ch);
        ch.Level = level;
        c.Clear();
        return (h, s, c, ch);
    }

    private static byte[] Req(ushort id, params byte[] b)
    {
        var w = new PacketWriter(id);
        foreach (var x in b) w.WriteByte(x);
        return w.ToArray();
    }

    private static byte Result(FakeClientChannel c, ushort ack) => new PacketReader(c.Last(ack)!).ReadByte();

    [Fact]
    public async Task EquippingABag_TurnsTheItemIntoAContainer()
    {
        var (h, s, c, ch) = await Setup();

        await h.Service.DispatchClientAsync(s, Req(Msg.CS_INVENADD_REQ, 1, 0, 4));   // into bag slot 1, from backpack slot 4

        var bag = ch.FindInven(1);
        Assert.NotNull(bag);
        Assert.Equal(BagItem, bag!.TemplateId);
        Assert.Equal(777, bag.EndTime);                       // the expiry carries over
        Assert.DoesNotContain(ch.FindInven(0)!.Items, i => i.ItemSlot == 4);
        Assert.True(c.Has(Msg.CS_DELITEM_ACK));
        var r = new PacketReader(c.Last(Msg.CS_INVENADD_ACK)!);
        Assert.Equal(0, r.ReadByte());                        // INVEN_SUCCESS
        Assert.Equal(1, r.ReadByte());
        Assert.Equal(BagItem, r.ReadUInt16());
        Assert.Equal(777, r.ReadInt64());
    }

    [Fact]
    public async Task OnlyABagItem_CanBeEquippedAsABag()
    {
        var (h, s, c, ch) = await Setup();

        await h.Service.DispatchClientAsync(s, Req(Msg.CS_INVENADD_REQ, 1, 0, 5));   // the potion

        Assert.Null(ch.FindInven(1));
        Assert.Equal(5, Result(c, Msg.CS_INVENADD_ACK));      // INVEN_FAIL
    }

    [Fact]
    public async Task ABagAboveYourLevel_IsRefused()
    {
        var (h, s, c, ch) = await Setup(level: 3);             // the bag needs level 5

        await h.Service.DispatchClientAsync(s, Req(Msg.CS_INVENADD_REQ, 1, 0, 4));

        Assert.Null(ch.FindInven(1));
        Assert.Equal(4, Result(c, Msg.CS_INVENADD_ACK));      // INVEN_LEVEL
    }

    [Fact]
    public async Task AnOccupiedBagSlot_IsRefused()
    {
        var (h, s, c, ch) = await Setup();
        ch.Invens.Add(new Inven { InvenId = 1, TemplateId = BagItem });

        await h.Service.DispatchClientAsync(s, Req(Msg.CS_INVENADD_REQ, 1, 0, 4));

        Assert.Equal(1, Result(c, Msg.CS_INVENADD_ACK));      // INVEN_EXIST
        Assert.Contains(ch.FindInven(0)!.Items, i => i.ItemSlot == 4);
    }

    [Fact]
    public async Task UnequippingAnEmptyBag_GivesTheItemBack()
    {
        var (h, s, c, ch) = await Setup();
        ch.Invens.Add(new Inven { InvenId = 1, TemplateId = BagItem, EndTime = 777, Eld = 2 });

        await h.Service.DispatchClientAsync(s, Req(Msg.CS_INVENDEL_REQ, 1, 0, Proto.InvalidSlot));

        Assert.Null(ch.FindInven(1));
        var back = ch.FindInven(0)!.Items.Single(i => i.TemplateId == BagItem && i.ItemSlot != 4);   // not the one already there
        Assert.Equal(777, back.EndTime);
        Assert.Equal(2u, back.Ext[Item.IevEld]);
        Assert.Equal(0, Result(c, Msg.CS_INVENDEL_ACK));
        Assert.True(c.Has(Msg.CS_ADDITEM_ACK));
    }

    [Fact]
    public async Task ABagWithItemsInside_CannotBeRemoved()
    {
        var (h, s, c, ch) = await Setup();
        var bag = new Inven { InvenId = 1, TemplateId = BagItem };
        bag.Items.Add(new Item { ItemSlot = 0, TemplateId = Potion, Count = 1 });
        ch.Invens.Add(bag);

        await h.Service.DispatchClientAsync(s, Req(Msg.CS_INVENDEL_REQ, 1, 0, Proto.InvalidSlot));

        Assert.NotNull(ch.FindInven(1));
        Assert.Equal(2, Result(c, Msg.CS_INVENDEL_ACK));      // INVEN_NOTEMPTY
    }

    [Fact]
    public async Task MovingBags_SwapsTheirSlots()
    {
        var (h, s, c, ch) = await Setup();
        var a = new Inven { InvenId = 1, TemplateId = BagItem };
        var b = new Inven { InvenId = 2, TemplateId = BagItem };
        ch.Invens.AddRange(new[] { a, b });

        await h.Service.DispatchClientAsync(s, Req(Msg.CS_INVENMOVE_REQ, 1, 2));

        Assert.Equal(2, a.InvenId);
        Assert.Equal(1, b.InvenId);
        Assert.Equal(0, Result(c, Msg.CS_INVENMOVE_ACK));
    }

    // ================= return point =================

    [Fact]
    public async Task AReturnNpc_SetsTheReturnPoint()
    {
        var (h, s, c, ch) = await Setup();
        h.Service.AddNpc(new Npc { Id = 50, Type = 14 /* TNPC_RETURN */, Country = 3 } .WithSpawn(12));

        var w = new PacketWriter(Msg.CS_SETRETURNPOS_REQ);
        w.WriteUInt16(50);
        await h.Service.DispatchClientAsync(s, w.ToArray());

        Assert.Equal(12, ch.Persist.SpawnId);
        Assert.Equal(1, Result(c, Msg.CS_SETRETURNPOS_ACK));
    }

    [Fact]
    public async Task AnyOtherNpc_Refuses()
    {
        var (h, s, c, ch) = await Setup();
        h.Service.AddNpc(new Npc { Id = 51, Type = 2 /* TNPC_ITEM */, Country = 3 }.WithSpawn(12));

        var w = new PacketWriter(Msg.CS_SETRETURNPOS_REQ);
        w.WriteUInt16(51);
        await h.Service.DispatchClientAsync(s, w.ToArray());

        Assert.Equal(0, ch.Persist.SpawnId);
        Assert.Equal(0, Result(c, Msg.CS_SETRETURNPOS_ACK));
    }
}

internal static class NpcTestExtensions
{
    public static Npc WithSpawn(this Npc n, ushort spawnPos) { n.SpawnPosId = spawnPos; return n; }
}
