using TMap.Data;
using TMap.Protocol;
using TMap.Server.Map;
using Xunit;

namespace TMap.Server.Tests;

/// <summary>
/// PvP points (C++ GainPvPoint / UsePvPoint): the PvP shop spends the useable balance, a credits item adds to it, every
/// change is announced with CS_PVPPOINT_ACK, and the balances go into the character save.
/// </summary>
public class PvPointTests
{
    private const ushort PvpShop = 501, Potion = 202, Credits = 7680;

    private static TemplateStore Store()
    {
        var t = new TemplateStore();
        t.Items[Potion] = new ItemTemplate(Potion, 0, new[] { 0f, 0f, 0f, 0f }, DefaultLevel: 7, ClassId: 0x3F, PvPrice: 3f, Stack: 10);
        t.Items[Credits] = new ItemTemplate(Credits, 0, new[] { 0f, 0f, 0f, 0f }, Kind: 87 /* IK_CREDITS */, UseValue: 10, Stack: 10);
        t.LevelPvPMoney[7] = 25;                                                    // the potion costs 25 × 3 = 75 points
        return t;
    }

    private static async Task<(MapTestHarness h, ClientSession s, FakeClientChannel c, Character ch)> Setup(uint useable, uint total = 500)
    {
        var t = Store();
        var h = new MapTestHarness(t);
        var ch = new Character { CharId = 1, Name = "Duelist", MaxHp = 100, Hp = 100 };
        ch.Invens.Add(new Inven { InvenId = 0xFF });
        var (s, c) = await h.EnterAsync(1, 1, 1, preSeeded: ch);
        ch.Country = 1; ch.AidCountry = 3; ch.Level = 12; ch.PvpUseablePoint = useable; ch.PvpTotalPoint = total;
        var shop = new Npc { Id = PvpShop, Type = 21, Country = 3 };
        shop.Items[Potion] = t.Items[Potion];
        h.Service.AddNpc(shop);
        c.Clear();
        return (h, s, c, ch);
    }

    private static (uint Total, uint Useable, byte Event, uint Month) ReadAck(byte[] p)
    {
        var r = new PacketReader(p);
        var v = (r.ReadUInt32(), r.ReadUInt32(), r.ReadByte(), r.ReadUInt32());
        Assert.Equal(0, r.Remaining);
        return v;
    }

    [Fact]
    public async Task BuyingFromAPvpShop_SpendsUseablePoints_AndSaysSo()
    {
        var (h, s, c, ch) = await Setup(useable: 100);

        await h.Service.DispatchClientAsync(s, MapTestHarness.ItemBuyReq(PvpShop, Potion, 1));

        Assert.Equal((byte)ItemBuyResult.Success, c.Last(Msg.CS_ITEMBUY_ACK)![PacketHeader.Size]);
        Assert.Equal(Potion, ch.Invens[0].Items.Single().TemplateId);
        Assert.Equal((500u, 25u, MapService.PvpeBuyItem, 0u), ReadAck(c.Last(Msg.CS_PVPPOINT_ACK)!));   // the total is kept
        Assert.Equal((25u, 500u), (ch.PvpUseablePoint, ch.PvpTotalPoint));
    }

    [Fact]
    public async Task TooFewPoints_NeedMoney_AndNothingIsTaken()
    {
        var (h, s, c, ch) = await Setup(useable: 74);

        await h.Service.DispatchClientAsync(s, MapTestHarness.ItemBuyReq(PvpShop, Potion, 1));

        Assert.Equal((byte)ItemBuyResult.NeedMoney, c.Last(Msg.CS_ITEMBUY_ACK)![PacketHeader.Size]);
        Assert.Equal(74u, ch.PvpUseablePoint);
        Assert.False(c.Has(Msg.CS_PVPPOINT_ACK));
    }

    [Fact]
    public async Task ACreditsItem_AddsItsValueToTheUseablePoints()
    {
        var (h, s, c, ch) = await Setup(useable: 5);
        ch.Invens[0].Items.Add(new Item { TemplateId = Credits, Count = 2, ItemSlot = 0, Template = Store().Items[Credits] });

        await h.Service.DispatchClientAsync(s, MapTestHarness.ItemUseReq(Credits, 0xFF, 0));

        Assert.Equal((500u, 15u, MapService.PvpeBuyItem, 0u), ReadAck(c.Last(Msg.CS_PVPPOINT_ACK)!));
        Assert.Equal(1, ch.Invens[0].Items.Single().Count);                          // one used up
    }

    [Fact]
    public void TheSave_CarriesBothBalances()
    {
        var ch = new Character { CharId = 7, PvpUseablePoint = 40, PvpTotalPoint = 900 };

        var d = MapService.BuildCharSave(ch);

        Assert.Equal((40u, 900u), (d.PvpUseablePoint, d.PvpTotalPoint));
    }
}
