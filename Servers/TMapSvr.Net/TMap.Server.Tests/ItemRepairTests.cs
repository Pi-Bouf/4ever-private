using TMap.Data;
using TMap.Protocol;
using TMap.Server.Map;
using Xunit;

namespace TMap.Server.Tests;

/// <summary>Phase 10 — CS_DURATIONREP (durability repair): the three modes, the cost formula + money
/// check/commit, the price quote, the guards, and the reverse-order ACK payload.</summary>
public class ItemRepairTests
{
    private const byte Backpack = 0xFF, Equip = 0xFE;
    private const byte Grade = 1;

    // A repair-cost chart where power-level (attr grade) 1 costs coefficient 10.
    private static TemplateStore RepairStore(uint coef = 10)
    {
        var t = new TemplateStore();
        t.RepairCostByLevel[Grade] = coef;
        return t;
    }

    // Worn item: dura 50/100, repairable, price 2.0, attr grade 1 ⇒ cost = max(1, 10·50·2.0/100) = 10.
    private static Item Worn(byte slot, uint duraCur = 50, uint duraMax = 100, byte canRepair = 1,
        float price = 2f, byte grade = Grade, byte kind = 0)
        => new() { ItemSlot = slot, TemplateId = 7000, Count = 1, DuraMax = duraMax, DuraCur = duraCur,
            Template = new ItemTemplate(7000, 0, new[] { 1f, 0f, 0f, 0f }, CanRepair: canRepair, Price: price, Kind: kind),
            Attr = new ItemAttr(1, 0, grade, 0, 0, 0, 0, 0, 0, 0) };

    private static Character MakeChar(uint cooper = 100)
    {
        var ch = new Character { CharId = 9, Name = "Fixer", Class = 2, Level = 10, Cooper = cooper };
        ch.Invens.Add(new Inven { InvenId = Backpack });
        ch.Invens.Add(new Inven { InvenId = Equip });
        return ch;
    }

    private static ItemRepairResult Result(FakeClientChannel c)
        => (ItemRepairResult)new PacketReader(c.Last(Msg.CS_DURATIONREP_ACK)!).ReadByte();

    private static async Task<(MapTestHarness h, ClientSession s, FakeClientChannel c)> Enter(Character ch, TemplateStore? store = null)
    {
        var h = new MapTestHarness(store ?? RepairStore());
        var (s, c) = await h.EnterAsync(9, 1, 1, name: "Fixer", preSeeded: ch);
        c.Clear();
        return (h, s, c);
    }

    [Fact]
    public async Task Repair_Normal_RestoresDura_AndChargesMoney()
    {
        var ch = MakeChar(cooper: 100);
        ch.FindInven(Backpack)!.Items.Add(Worn(0));
        var (h, s, c) = await Enter(ch);

        await h.Service.DispatchClientAsync(s, MapTestHarness.DurationRepReq(RepairType.Normal, Backpack, 0));

        Assert.Equal(ItemRepairResult.Success, Result(c));
        Assert.Equal(100u, ch.FindInven(Backpack)!.FindItem(0)!.DuraCur); // restored to max
        Assert.Equal(90L, ch.MoneyTotal);                                 // 100 - 10 cost
        Assert.True(c.Has(Msg.CS_MONEY_ACK));
    }

    [Fact]
    public async Task Repair_Normal_Ack_CarriesItemDura()
    {
        var ch = MakeChar(cooper: 100);
        ch.FindInven(Backpack)!.Items.Add(Worn(4));
        var (h, s, c) = await Enter(ch);

        await h.Service.DispatchClientAsync(s, MapTestHarness.DurationRepReq(RepairType.Normal, Backpack, 4));

        var r = new PacketReader(c.Last(Msg.CS_DURATIONREP_ACK)!);
        Assert.Equal((byte)ItemRepairResult.Success, r.ReadByte());
        Assert.Equal((byte)1, r.ReadByte());       // count
        Assert.Equal((byte)Backpack, r.ReadByte()); // inven
        Assert.Equal((byte)4, r.ReadByte());        // slot
        Assert.Equal(100u, r.ReadUInt32());         // duraMax
        Assert.Equal(100u, r.ReadUInt32());         // duraCur (now full)
    }

    [Fact]
    public async Task Repair_Insufficient_NeedMoney_NoChange()
    {
        var ch = MakeChar(cooper: 5); // cost is 10
        ch.FindInven(Backpack)!.Items.Add(Worn(0));
        var (h, s, c) = await Enter(ch);

        await h.Service.DispatchClientAsync(s, MapTestHarness.DurationRepReq(RepairType.Normal, Backpack, 0));

        Assert.Equal(ItemRepairResult.NeedMoney, Result(c));
        Assert.Equal(50u, ch.FindInven(Backpack)!.FindItem(0)!.DuraCur); // not repaired
        Assert.Equal(5L, ch.MoneyTotal);                                 // not charged
        Assert.False(c.Has(Msg.CS_MONEY_ACK));
    }

    [Fact]
    public async Task Repair_NeedCost_QuotesPrice_NoRepair()
    {
        var ch = MakeChar(cooper: 100);
        ch.FindInven(Backpack)!.Items.Add(Worn(0));
        var (h, s, c) = await Enter(ch);

        await h.Service.DispatchClientAsync(s, MapTestHarness.DurationRepReq(RepairType.Normal, Backpack, 0, needCost: 1));

        var q = c.Last(Msg.CS_DURATIONREPCOST_ACK);
        Assert.NotNull(q);
        var r = new PacketReader(q!);
        Assert.Equal(10u, r.ReadUInt32());  // quoted cost
        Assert.Equal((byte)0, r.ReadByte()); // discount rate (deferred ⇒ 0)
        Assert.Equal(50u, ch.FindInven(Backpack)!.FindItem(0)!.DuraCur); // not repaired
        Assert.Equal(100L, ch.MoneyTotal);                               // not charged
        Assert.False(c.Has(Msg.CS_DURATIONREP_ACK));
    }

    [Fact]
    public async Task Repair_Unrepairable_Disallow()
    {
        var ch = MakeChar();
        ch.FindInven(Backpack)!.Items.Add(Worn(0, canRepair: 0));
        var (h, s, c) = await Enter(ch);

        await h.Service.DispatchClientAsync(s, MapTestHarness.DurationRepReq(RepairType.Normal, Backpack, 0));
        Assert.Equal(ItemRepairResult.Disallow, Result(c));
    }

    [Fact]
    public async Task Repair_AlreadyFull_NotFound()
    {
        var ch = MakeChar();
        ch.FindInven(Backpack)!.Items.Add(Worn(0, duraCur: 100, duraMax: 100));
        var (h, s, c) = await Enter(ch);

        await h.Service.DispatchClientAsync(s, MapTestHarness.DurationRepReq(RepairType.Normal, Backpack, 0));
        Assert.Equal(ItemRepairResult.NotFound, Result(c));
    }

    [Fact]
    public async Task Repair_Equip_RepairsAllWorn_SkipsFullAndUnrepairable()
    {
        var ch = MakeChar(cooper: 1000);
        var eq = ch.FindInven(Equip)!;
        eq.Items.Add(Worn(0, duraCur: 40));               // worn ⇒ repaired (cost = max(1,10·60·2/100)=12)
        eq.Items.Add(Worn(1, duraCur: 100, duraMax: 100)); // full ⇒ skipped
        eq.Items.Add(Worn(2, duraCur: 10, canRepair: 0));  // unrepairable ⇒ skipped
        var (h, s, c) = await Enter(ch);

        await h.Service.DispatchClientAsync(s, MapTestHarness.DurationRepReq(RepairType.Equip, Equip, 0));

        Assert.Equal(ItemRepairResult.Success, Result(c));
        Assert.Equal(100u, eq.FindItem(0)!.DuraCur); // repaired
        Assert.Equal(10u, eq.FindItem(2)!.DuraCur);  // untouched (unrepairable)
        var r = new PacketReader(c.Last(Msg.CS_DURATIONREP_ACK)!);
        r.ReadByte();
        Assert.Equal((byte)1, r.ReadByte()); // only the one worn+repairable item
    }

    [Fact]
    public async Task Repair_NoLevelChart_IsFree()
    {
        var ch = MakeChar(cooper: 0);                 // no money at all
        ch.FindInven(Backpack)!.Items.Add(Worn(0));
        var (h, s, c) = await Enter(ch, store: new TemplateStore()); // empty ⇒ GetRepairCost 0 (C++ FindTLevel-null)

        await h.Service.DispatchClientAsync(s, MapTestHarness.DurationRepReq(RepairType.Normal, Backpack, 0));

        Assert.Equal(ItemRepairResult.Success, Result(c)); // free repair still succeeds
        Assert.Equal(100u, ch.FindInven(Backpack)!.FindItem(0)!.DuraCur);
        Assert.Equal(0L, ch.MoneyTotal);
    }

    [Fact]
    public async Task Repair_EmptySlot_NotFound()
    {
        var (h, s, c) = await Enter(MakeChar());
        await h.Service.DispatchClientAsync(s, MapTestHarness.DurationRepReq(RepairType.Normal, Backpack, 0));
        Assert.Equal(ItemRepairResult.NotFound, Result(c));
    }
}
