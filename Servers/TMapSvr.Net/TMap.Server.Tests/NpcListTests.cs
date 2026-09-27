using TMap.Data;
using TMap.Protocol;
using TMap.Server.Map;
using Xunit;

namespace TMap.Server.Tests;

/// <summary>
/// What an NPC offers — C++ OnCS_NPCITEMLIST_REQ / SendCS_NPCITEMLIST_ACK(CTNpc*): a merchant's and a PvP shop's stock
/// (class + country filter, gold or PvP-point price) and a teleporter's destinations (portal conditions). The client
/// opens the NPC window only on this answer.
/// </summary>
public class NpcListTests
{
    private const byte Warrior = 0, Archer = 2, CountryD = 0, CountryC = 1, TcontryN = 3;
    private const ushort Shop = 500, PvpShop = 501, Porter = 502, Monsters = 503;
    private const ushort Sword = 200, Bow = 201, Potion = 202, DOnly = 203, COnly = 204;
    private const ushort Here = 1, Open = 2, Toll = 3, Low = 4, High = 5, Band = 6, Guild = 7, Allied = 8;
    private const ushort TollItem = 900;

    private static ItemTemplate Item(ushort id, uint classMask, byte country = TcontryN, byte level = 5, float price = 2f,
        float pv = 0f)
        => new(id, 0, new[] { 0f, 0f, 0f, 0f }, DefaultLevel: level, ClassId: classMask, Price: price, ItemCountry: country,
            PvPrice: pv, Stack: 10);

    private static TemplateStore Store()
    {
        var t = new TemplateStore();
        t.Items[Sword] = Item(Sword, 1u << Warrior);
        t.Items[Bow] = Item(Bow, 1u << Archer);
        t.Items[Potion] = Item(Potion, 0x3F, level: 7, price: 1.5f, pv: 3f);
        t.Items[DOnly] = Item(DOnly, 0x3F, country: CountryD);
        t.Items[COnly] = Item(COnly, 0x3F, country: CountryC);
        t.Items[TollItem] = Item(TollItem, 0x3F);
        t.LevelMoney[5] = 100; t.LevelMoney[7] = 40;
        t.LevelPvPMoney[5] = 10; t.LevelPvPMoney[7] = 25;

        var none = new PortalCondition(0, 0);
        PortalDestination Dest(ushort id, uint price, PortalCondition c) => new(id, price, 1, new[] { c, none, none });
        var here = new PortalRow(Here, TcontryN, 0, 10, 0);
        t.Portals[Here] = here;
        foreach (var id in new[] { Open, Toll, Low, High, Band, Guild })
            t.Portals[id] = new PortalRow(id, TcontryN, 0, 10, 0);
        t.Portals[Allied] = new PortalRow(Allied, CountryC, 0, 10, 0);
        here.Destinations[Open] = Dest(Open, 0, none);
        here.Destinations[Toll] = Dest(Toll, 346, new PortalCondition(2 /* PCT_HAVEITEM */, TollItem));
        here.Destinations[Low] = Dest(Low, 10, new PortalCondition(5 /* PCT_DOWNLEVEL: level ≥ */, 20));
        here.Destinations[High] = Dest(High, 20, new PortalCondition(6 /* PCT_UPLEVEL: level ≤ */, 5));
        here.Destinations[Band] = Dest(Band, 30, new PortalCondition(7 /* PCT_UPDOWNLEVEL */, 10 | (15u << 16)));
        here.Destinations[Guild] = Dest(Guild, 40, new PortalCondition(8 /* PCT_GUILD — occupation unported */, 0));
        here.Destinations[Allied] = Dest(Allied, 50, new PortalCondition(1 /* PCT_COUNTRY of the target */, 0));
        here.Destinations[99] = Dest(99, 0, none);                                  // no such portal: dropped
        return t;
    }

    private static async Task<(MapTestHarness h, ClientSession s, FakeClientChannel c, Character ch, TemplateStore t)> Setup(
        byte cls = Warrior, byte country = CountryC, byte level = 12, byte dcc = 0)
    {
        var t = Store();
        var h = new MapTestHarness(t);
        var ch = new Character { CharId = 1, Name = "Buyer", MaxHp = 100, Hp = 100, Class = cls };
        ch.Invens.Add(new Inven { InvenId = 0xFF });
        var (s, c) = await h.EnterAsync(1, 1, 1, preSeeded: ch);
        ch.Country = country; ch.AidCountry = TcontryN; ch.Level = level;

        var shop = new Npc { Id = Shop, Type = 2, Country = TcontryN, DiscountCondition = dcc };
        var pvp = new Npc { Id = PvpShop, Type = 21, Country = TcontryN };
        foreach (var id in new[] { Sword, Bow, Potion, DOnly, COnly }) shop.Items[id] = t.Items[id];
        pvp.Items[Potion] = t.Items[Potion];
        h.Service.AddNpc(shop); h.Service.AddNpc(pvp);
        h.Service.AddNpc(new Npc { Id = Porter, Type = 8, Country = TcontryN, PortalId = Here });
        h.Service.AddNpc(new Npc { Id = Monsters, Type = 22, Country = TcontryN });
        c.Clear();
        return (h, s, c, ch, t);
    }

    private static byte[] ListReq(ushort npcId) => new PacketWriter(Msg.CS_NPCITEMLIST_REQ).WriteUInt16(npcId).ToArray();

    private static (ushort Npc, byte Type, byte Discount, List<(ushort Id, uint Price)> Rows) Read(byte[] p)
    {
        var r = new PacketReader(p);
        ushort npc = r.ReadUInt16(); byte type = r.ReadByte(), discount = r.ReadByte(), n = r.ReadByte();
        var rows = new List<(ushort, uint)>();
        for (int i = 0; i < n; i++) rows.Add((r.ReadUInt16(), r.ReadUInt32()));
        Assert.Equal(0, r.Remaining);
        return (npc, type, discount, rows);
    }

    // ================================ merchants ================================

    [Fact]
    public async Task AMerchant_ListsItsStockForTheClassAndCountry_AtTheGoldPrice()
    {
        var (h, s, c, _, _) = await Setup();

        await h.Service.DispatchClientAsync(s, ListReq(Shop));

        var a = Read(c.Last(Msg.CS_NPCITEMLIST_ACK)!);
        Assert.Equal((Shop, (byte)2, (byte)0), (a.Npc, a.Type, a.Discount));
        // No bow (archer only), no country-D item; prices = money[grade]·fPrice + 0.99, by item id.
        Assert.Equal(new List<(ushort, uint)> { (Sword, 200), (Potion, 60), (COnly, 200) }, a.Rows);
    }

    [Fact]
    public async Task AnAllCountryMerchant_SellsEveryCountrysStock()
    {
        var (h, s, c, _, _) = await Setup(dcc: 4 /* DCC_ALLCOUNTRY */);

        await h.Service.DispatchClientAsync(s, ListReq(Shop));

        Assert.Contains(Read(c.Last(Msg.CS_NPCITEMLIST_ACK)!).Rows, row => row.Id == DOnly);
    }

    [Fact]
    public async Task AnotherClass_SeesItsOwnStock()
    {
        var (h, s, c, _, _) = await Setup(cls: Archer, country: CountryD);

        await h.Service.DispatchClientAsync(s, ListReq(Shop));

        Assert.Equal(new ushort[] { Bow, Potion, DOnly }, Read(c.Last(Msg.CS_NPCITEMLIST_ACK)!).Rows.Select(x => x.Id));
    }

    [Fact]
    public async Task APvpShop_PricesInPvpPoints()
    {
        var (h, s, c, _, _) = await Setup();

        await h.Service.DispatchClientAsync(s, ListReq(PvpShop));

        var a = Read(c.Last(Msg.CS_NPCITEMLIST_ACK)!);
        Assert.Equal((byte)21, a.Type);
        Assert.Equal(new List<(ushort, uint)> { (Potion, 75) }, a.Rows);            // pvpMoney[7] 25 × 3.0
    }

    [Fact]
    public async Task BuyingFromAPvpShop_WithNoPvpPoints_NeedsMoney_AndTakesNoGold()
    {
        var (h, s, c, ch, _) = await Setup();
        ch.Gold = 100;

        await h.Service.DispatchClientAsync(s, MapTestHarness.ItemBuyReq(PvpShop, Potion, 1));

        Assert.Equal((byte)ItemBuyResult.NeedMoney, c.Last(Msg.CS_ITEMBUY_ACK)![PacketHeader.Size]);
        Assert.Equal(100u, ch.Gold);
        Assert.Empty(ch.Invens[0].Items);
    }

    // ================================ teleporters ================================

    [Fact]
    public async Task ATeleporter_ListsTheDestinationsWhoseConditionsAreMet()
    {
        var (h, s, c, _, _) = await Setup(level: 12);

        await h.Service.DispatchClientAsync(s, ListReq(Porter));

        var a = Read(c.Last(Msg.CS_NPCITEMLIST_ACK)!);
        Assert.Equal((Porter, (byte)8), (a.Npc, a.Type));
        // Open (none), Band (10..15), Allied (target portal is country C). Not: Toll (no item), Low (≥ 20),
        // High (≤ 5), Guild (occupation unported), 99 (no such portal).
        Assert.Equal(new List<(ushort, uint)> { (Open, 0), (Band, 30), (Allied, 50) }, a.Rows);
    }

    [Fact]
    public async Task ATeleporter_OpensTheLevelAndItemGatedOnes_WhenMet()
    {
        var (h, s, c, ch, _) = await Setup(level: 25, country: CountryD);
        ch.Invens[0].Items.Add(new Item { TemplateId = TollItem, Count = 1, ItemSlot = 0 });

        await h.Service.DispatchClientAsync(s, ListReq(Porter));

        Assert.Equal(new ushort[] { Open, Toll, Low }, Read(c.Last(Msg.CS_NPCITEMLIST_ACK)!).Rows.Select(x => x.Id));
    }

    [Fact]
    public async Task ATeleporterWhosePortalRefusesTheCountry_ListsNothing()
    {
        var (h, s, c, _, store) = await Setup(country: CountryD);
        h.Service.AddNpc(new Npc { Id = 600, Type = 8, Country = TcontryN, PortalId = Allied });
        var t = store.Portals[Allied];
        store.Portals[Allied] = t with { Condition = 1 /* PCT_COUNTRY */ };
        store.Portals[Allied].Destinations[Open] = new PortalDestination(Open, 0, 1,
            new[] { new PortalCondition(0, 0), new PortalCondition(0, 0), new PortalCondition(0, 0) });

        await h.Service.DispatchClientAsync(s, ListReq(600));

        Assert.Empty(Read(c.Last(Msg.CS_NPCITEMLIST_ACK)!).Rows);
    }

    // ================================ others ================================

    [Fact]
    public async Task AMonsterShop_GetsNoAnswer()
    {
        var (h, s, c, _, _) = await Setup();

        await h.Service.DispatchClientAsync(s, ListReq(Monsters));

        Assert.False(c.Has(Msg.CS_NPCITEMLIST_ACK));
    }

    [Fact]
    public async Task ANpcOfAnotherCountry_GetsNoAnswer()
    {
        var (h, s, c, _, _) = await Setup(country: CountryD);
        h.Service.AddNpc(new Npc { Id = 700, Type = 2, Country = CountryC });

        await h.Service.DispatchClientAsync(s, ListReq(700));

        Assert.False(c.Has(Msg.CS_NPCITEMLIST_ACK));
    }
}
