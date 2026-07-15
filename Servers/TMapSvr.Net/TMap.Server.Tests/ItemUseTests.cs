using TMap.Data;
using TMap.Protocol;
using TMap.Server.Map;
using Xunit;

namespace TMap.Server.Tests;

/// <summary>Phase 8 — CS_ITEMUSE (HP/MP potions): heal, clamp, IU_FULL, consume, broadcast, guards.</summary>
public class ItemUseTests
{
    private const byte Backpack = 0xFF;
    private const byte IkHp = 26, IkMp = 27, IkMaxHp = 42;
    private const ushort Tid = 300;

    // Flat vitals: MaxHP = 200, MaxMP = 100 (RateX 0 → stat-independent, simple).
    private static TemplateStore VitalStore()
    {
        var t = new TemplateStore { Rate1st = 1.0f };
        t.Formulas[8] = new FormulaRow(200, 0f, 0f);
        t.Formulas[19] = new FormulaRow(100, 0f, 0f);
        t.Classes[1] = new StatSeed(1, 1, 1, 1, 1, 1);
        t.Races[1] = new StatSeed(1, 1, 1, 1, 1, 1);
        return t;
    }

    private static Character PotionChar(uint hp, uint mp)
    {
        var ch = new Character { CharId = 9, Name = "Drinker", Class = 1, Race = 1, Hp = hp, Mp = mp };
        ch.Invens.Add(new Inven { InvenId = Backpack });
        return ch;
    }

    private static Item Potion(byte kind, ushort useValue, byte count = 5, byte level = 1, uint delay = 0,
        ushort delayGroup = 0, byte consumable = 1)
        => new() { ItemSlot = 0, TemplateId = Tid, Count = count,
                   Template = new ItemTemplate(Tid, 0, new[] { 1f, 0f, 0f, 0f },
                       Kind: kind, UseValue: useValue, DefaultLevel: level, Delay: delay,
                       DelayGroup: delayGroup, Consumable: consumable) };

    private static byte Result(FakeClientChannel c)
        => new PacketReader(c.Last(Msg.CS_ITEMUSE_ACK)!).ReadByte();

    private static async Task<(MapTestHarness h, ClientSession s, FakeClientChannel c)> Use(Character ch)
    {
        var h = new MapTestHarness(VitalStore());
        var (s, c) = await h.EnterAsync(9, 1, 1, name: "Drinker", preSeeded: ch);
        c.Clear();
        return (h, s, c);
    }

    [Fact]
    public async Task HpPotion_Heals_AndConsumesOne()
    {
        var ch = PotionChar(hp: 50, mp: 100);
        ch.FindInven(Backpack)!.Items.Add(Potion(IkHp, useValue: 30));
        var (h, s, c) = await Use(ch);

        await h.Service.DispatchClientAsync(s, MapTestHarness.ItemUseReq(Tid, Backpack, 0));

        var r = new PacketReader(c.Last(Msg.CS_ITEMUSE_ACK)!);
        Assert.Equal((byte)ItemUseResult.Success, r.ReadByte());
        r.ReadUInt16();                                 // delayGroup
        Assert.Equal((byte)IkHp, r.ReadByte());         // kind echoed
        Assert.Equal(80u, ch.Hp);                       // 50 + 30
        Assert.Equal(4, ch.FindInven(Backpack)!.FindItem(0)!.Count);
        Assert.True(c.Has(Msg.CS_HPMP_ACK));
    }

    [Fact]
    public async Task HpPotion_OverheHeal_ClampsToMax()
    {
        var ch = PotionChar(hp: 190, mp: 100);
        ch.FindInven(Backpack)!.Items.Add(Potion(IkHp, useValue: 30)); // 190 + 30 > 200
        var (h, s, c) = await Use(ch);

        await h.Service.DispatchClientAsync(s, MapTestHarness.ItemUseReq(Tid, Backpack, 0));
        Assert.Equal(200u, ch.Hp); // clamped to MaxHP
    }

    [Fact]
    public async Task Potion_AtFullHp_ReturnsFull_NoConsume()
    {
        var ch = PotionChar(hp: 200, mp: 100); // already at MaxHP 200
        ch.FindInven(Backpack)!.Items.Add(Potion(IkHp, useValue: 30));
        var (h, s, c) = await Use(ch);

        await h.Service.DispatchClientAsync(s, MapTestHarness.ItemUseReq(Tid, Backpack, 0));
        Assert.Equal((byte)ItemUseResult.Full, Result(c));
        Assert.Equal(5, ch.FindInven(Backpack)!.FindItem(0)!.Count); // not consumed
        Assert.False(c.Has(Msg.CS_HPMP_ACK));
    }

    [Fact]
    public async Task MaxHpPotion_FullyRestores()
    {
        var ch = PotionChar(hp: 10, mp: 100);
        ch.FindInven(Backpack)!.Items.Add(Potion(IkMaxHp, useValue: 0, count: 1));
        var (h, s, c) = await Use(ch);

        await h.Service.DispatchClientAsync(s, MapTestHarness.ItemUseReq(Tid, Backpack, 0));
        Assert.Equal(200u, ch.Hp);
        Assert.Null(ch.FindInven(Backpack)!.FindItem(0)); // last one consumed → DELITEM
        Assert.True(c.Has(Msg.CS_DELITEM_ACK));
    }

    [Fact]
    public async Task MpPotion_Heals()
    {
        var ch = PotionChar(hp: 200, mp: 20);
        ch.FindInven(Backpack)!.Items.Add(Potion(IkMp, useValue: 15));
        var (h, s, c) = await Use(ch);

        await h.Service.DispatchClientAsync(s, MapTestHarness.ItemUseReq(Tid, Backpack, 0));
        Assert.Equal((byte)ItemUseResult.Success, Result(c));
        Assert.Equal(35u, ch.Mp);
    }

    [Fact]
    public async Task Potion_TooLowLevel_NeedLevel()
    {
        var ch = PotionChar(hp: 50, mp: 100);
        ch.FindInven(Backpack)!.Items.Add(Potion(IkHp, useValue: 30, level: 50)); // char level is 10
        var (h, s, c) = await Use(ch);

        await h.Service.DispatchClientAsync(s, MapTestHarness.ItemUseReq(Tid, Backpack, 0));
        Assert.Equal((byte)ItemUseResult.NeedLevel, Result(c));
        Assert.Equal(50u, ch.Hp); // unchanged
    }

    [Fact]
    public async Task NonConsumable_Heals_ButNotConsumed()
    {
        // Audit fold-in: C++ consumes only if m_bConsumable — a non-consumable HP/MP item heals but stays.
        var ch = PotionChar(hp: 50, mp: 100);
        ch.FindInven(Backpack)!.Items.Add(Potion(IkHp, useValue: 30, consumable: 0));
        var (h, s, c) = await Use(ch);

        await h.Service.DispatchClientAsync(s, MapTestHarness.ItemUseReq(Tid, Backpack, 0));

        Assert.Equal((byte)ItemUseResult.Success, Result(c));
        Assert.Equal(80u, ch.Hp);                                     // healed
        Assert.Equal(5, ch.FindInven(Backpack)!.FindItem(0)!.Count);  // but NOT consumed
    }

    [Fact]
    public async Task DelayGroupMismatch_NotFound()
    {
        // Audit fold-in: C++ anti-tamper — a request whose delay-group differs from the item's is rejected.
        var ch = PotionChar(hp: 50, mp: 100);
        ch.FindInven(Backpack)!.Items.Add(Potion(IkHp, useValue: 30, delayGroup: 7));
        var (h, s, c) = await Use(ch);

        await h.Service.DispatchClientAsync(s, MapTestHarness.ItemUseReq(Tid, Backpack, 0, delayGroup: 0));

        Assert.Equal((byte)ItemUseResult.NotFound, Result(c));
        Assert.Equal(50u, ch.Hp);   // not healed
    }

    [Fact]
    public async Task Use_EmptySlot_NotFound()
    {
        var (h, s, c) = await Use(PotionChar(hp: 50, mp: 100));
        await h.Service.DispatchClientAsync(s, MapTestHarness.ItemUseReq(Tid, Backpack, 0));
        Assert.Equal((byte)ItemUseResult.NotFound, Result(c));
    }

    [Fact]
    public async Task HpPotion_BroadcastsBarToNeighbour_AndEchoesDelay()
    {
        var ch = PotionChar(hp: 50, mp: 100);
        ch.FindInven(Backpack)!.Items.Add(Potion(IkHp, useValue: 30, delay: 1000));
        var h = new MapTestHarness(VitalStore());
        var (sn, cn) = await h.EnterAsync(1, 1, 1, name: "Watcher");     // same spawn cell → in view
        var (s, c) = await h.EnterAsync(9, 9, 9, name: "Drinker", preSeeded: ch);
        c.Clear(); cn.Clear();

        await h.Service.DispatchClientAsync(s, MapTestHarness.ItemUseReq(Tid, Backpack, 0));

        var hpmp = cn.Last(Msg.CS_HPMP_ACK);
        Assert.NotNull(hpmp);
        var r = new PacketReader(hpmp!);
        Assert.Equal(9u, r.ReadUInt32());   // actor charId
        Assert.Equal((byte)1, r.ReadByte()); // OT_PC
        Assert.Equal(200u, r.ReadUInt32()); // maxHp
        Assert.Equal(80u, r.ReadUInt32());  // hp

        var ack = new PacketReader(c.Last(Msg.CS_ITEMUSE_ACK)!);
        ack.ReadByte(); ack.ReadUInt16(); ack.ReadByte(); // result, delayGroup, kind
        Assert.Equal(1000u, ack.ReadUInt32());            // delay echoed from the template
    }
}
