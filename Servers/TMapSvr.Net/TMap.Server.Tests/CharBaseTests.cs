using TMap.Data;
using TMap.Protocol;
using TMap.Server.Map;
using Xunit;

namespace TMap.Server.Tests;

/// <summary>Name and country changes: C++ OnCS_CHANGENAME_REQ / OnDM_CHECKCHANGENAME_ACK, OnCS_CHANGECOUNTRY_REQ and the
/// name / country / aid-country cases of OnMW_CHANGECHARBASE_REQ.</summary>
public class CharBaseTests
{
    private const byte Backpack = 0xFF, ItUse = 7, IkName = 48, IkCountry = 96, IkAidCountry = 97;
    private const byte D = 0, C = 1, B = 2, N = 3, Peace = 4;
    private const byte CcbDuplicate = 1, CcbNoItem = 2, CcbTime = 3, CcbParty = 4, CcbGuild = 6, CcbLevel = 8, CcbFail = 9;
    private const ushort NameItem = 500, CountryItem = 501;

    private long _now = 1_800_000_000;

    private async Task<(MapTestHarness h, ClientSession s, FakeClientChannel c, Character ch)> Setup(byte country = C, byte level = 140,
        ushort item = NameItem, byte kind = IkName)
    {
        var t = new TemplateStore { Rate1st = 1.0f };
        t.Classes[0] = new StatSeed(1, 1, 1, 1, 1, 1);
        t.Races[0] = new StatSeed(1, 1, 1, 1, 1, 1);
        t.Portals[15006] = new PortalRow(15006, B, 0, 77, 0);
        t.Portals[15001] = new PortalRow(15001, D, 0, 55, 0);
        var tpl = new ItemTemplate(item, 0, new[] { 1f, 0f, 0f, 0f }, Type: ItUse, Kind: kind, Stack: 5);
        var h = new MapTestHarness(t);
        h.Service.UnixNow = () => _now;
        var ch = new Character { CharId = 1, Name = "Ann", Level = level, Country = country, AidCountry = N, MaxHp = 100, Hp = 100 };
        var bag = new Inven { InvenId = Backpack };
        bag.Items.Add(new Item { ItemSlot = 0, TemplateId = item, Count = 2, Template = tpl });
        ch.Invens.Add(bag);
        var (s, c) = await h.EnterAsync(1, 1, 1, x: 100, z: 100, name: "Ann", preSeeded: ch);
        ch.Country = country; ch.Level = level; ch.AidCountry = N;            // the enter packet set them
        c.Clear(); h.World.Clear();
        return (h, s, c, ch);
    }

    private static byte[] NameReq(string name) => Req(Msg.CS_CHANGENAME_REQ, w => { w.WriteByte(Backpack); w.WriteByte(0); w.WriteString(name); });
    private static byte[] CountryReq(byte type, byte country) => Req(Msg.CS_CHANGECOUNTRY_REQ, w =>
    {
        w.WriteByte(type); w.WriteByte(country); w.WriteByte(Backpack); w.WriteByte(0);
    });
    private static byte[] Req(ushort id, Action<PacketWriter> body) { var w = new PacketWriter(id); body(w); return w.ToArray(); }

    private static (byte Result, byte Kind, byte Value, string Name, uint Second) Ack(FakeClientChannel c)
    {
        var r = new PacketReader(c.Last(Msg.CS_CHANGECHARBASE_ACK)!);
        byte res = r.ReadByte(); r.ReadUInt32(); byte kind = r.ReadByte(), value = r.ReadByte(); string name = r.ReadString(); r.ReadUInt16();
        return (res, kind, value, name, r.ReadUInt32());
    }

    private static (byte Kind, byte Value, string Name) ToWorld(MapTestHarness h)
    {
        var r = new PacketReader(h.World.Last(Msg.MW_CHANGECHARBASE_ACK)!);
        r.ReadUInt32(); r.ReadUInt32();
        byte kind = r.ReadByte(), value = r.ReadByte(); r.ReadUInt16();
        return (kind, value, r.ReadString());
    }

    // ================================ name ================================

    [Theory]
    [InlineData("Bad_Name")]
    [InlineData("Dot.")]
    [InlineData("")]
    public async Task ABadName_IsRefused(string name)
    {
        var (h, s, c, ch) = await Setup();

        await h.Service.DispatchClientAsync(s, NameReq(name));

        Assert.Equal(CcbFail, Ack(c).Result);
        Assert.False(h.World.Has(Msg.MW_CHANGECHARBASE_ACK));
        Assert.Equal(2, ch.FindInven(Backpack)!.FindItem(0)!.Count);
    }

    [Fact]
    public void TheNameRule_TakesUmlautsAndSpaces() => Assert.True(MapService.CheckCharName("Jörg Mäß 2"));

    [Fact]
    public async Task AFreeName_GoesToTheWorld_AndTheItemIsUsed()
    {
        var (h, s, c, ch) = await Setup();

        await h.Service.DispatchClientAsync(s, NameReq("Annabel"));

        Assert.Equal((IkName, (byte)0, "Annabel"), ToWorld(h));
        Assert.Equal(1, ch.FindInven(Backpack)!.FindItem(0)!.Count);
        Assert.True(c.Has(Msg.CS_MOVEITEM_ACK));
        Assert.Equal("Ann", ch.Name);                                         // not until the world sends it back
    }

    [Fact]
    public async Task ATakenName_IsADuplicate()
    {
        var (h, s, c, _) = await Setup();
        await h.EnterAsync(2, 2, 2, x: 300, z: 300, name: "Bob");

        await h.Service.DispatchClientAsync(s, NameReq("Bob"));

        Assert.Equal(CcbDuplicate, Ack(c).Result);
    }

    [Fact]
    public async Task WithoutTheItem_NothingHappens()
    {
        var (h, s, c, _) = await Setup(kind: IkCountry);

        await h.Service.DispatchClientAsync(s, NameReq("Annabel"));

        Assert.False(c.Has(Msg.CS_CHANGECHARBASE_ACK));
        Assert.False(h.World.Has(Msg.MW_CHANGECHARBASE_ACK));
    }

    [Fact]
    public async Task TheWorldsName_IsThePlayers_AndShownAround()
    {
        var (h, s, c, ch) = await Setup();

        await h.Service.DispatchWorldAsync(WorldReq(s, IkName, 0, "Annabel"));

        Assert.Equal("Annabel", ch.Name);
        Assert.Equal((byte)0, Ack(c).Result);
    }

    private static byte[] WorldReq(ClientSession s, byte kind, byte value, string name = "")
    {
        var w = new PacketWriter(Msg.MW_CHANGECHARBASE_REQ);
        w.WriteUInt32(s.CharId); w.WriteUInt32(s.Key); w.WriteByte(kind); w.WriteByte(value); w.WriteUInt16(0); w.WriteString(name);
        return w.ToArray();
    }

    // ================================ country ================================

    [Fact]
    public async Task InAParty_NoNewCountry()
    {
        var (h, s, c, ch) = await Setup();
        ch.PartyId = 3;

        await h.Service.DispatchClientAsync(s, CountryReq(IkCountry, B));

        Assert.Equal(CcbParty, Ack(c).Result);
    }

    [Fact]
    public async Task InAGuild_NoNewCountry()
    {
        var (h, s, c, ch) = await Setup();
        ch.GuildId = 3;

        await h.Service.DispatchClientAsync(s, CountryReq(IkCountry, B));

        Assert.Equal(CcbGuild, Ack(c).Result);
    }

    [Fact]
    public async Task BelowLevel130_NoBroa()
    {
        var (h, s, c, _) = await Setup(level: 129);

        await h.Service.DispatchClientAsync(s, CountryReq(IkCountry, B));

        Assert.Equal(CcbLevel, Ack(c).Result);
    }

    [Theory]
    [InlineData(140, D, false)]    // Craxion can only go to Broa
    [InlineData(180, B, false)]    // too high
    [InlineData(140, B, true)]
    public async Task CraxionToBroa_From130To179(byte level, byte to, bool goes)
    {
        var (h, s, _, ch) = await Setup(level: level);

        await h.Service.DispatchClientAsync(s, CountryReq(IkCountry, to));

        Assert.Equal(goes, h.World.Has(Msg.MW_CHANGECHARBASE_ACK));
        if (goes)
        {
            Assert.Equal((IkCountry, B, ""), ToWorld(h));
            Assert.Equal((ushort)77, ch.Persist.SpawnId);                   // Broa's start point
        }
    }

    [Fact]
    public async Task FromPeace_Level9_ToDefugel()
    {
        var (h, s, c, ch) = await Setup(country: Peace, level: 8);
        await h.Service.DispatchClientAsync(s, CountryReq(IkCountry, D));
        Assert.Equal(CcbLevel, Ack(c).Result);

        ch.Level = 9;
        await h.Service.DispatchClientAsync(s, CountryReq(IkCountry, B));
        Assert.False(h.World.Has(Msg.MW_CHANGECHARBASE_ACK));               // not Broa

        await h.Service.DispatchClientAsync(s, CountryReq(IkCountry, D));
        Assert.Equal((IkCountry, D, ""), ToWorld(h));
        Assert.Equal((ushort)55, ch.Persist.SpawnId);
    }

    [Fact]
    public async Task BroaGoesBack_ToItsOriginalCountry_WithTheItem_DroppingTheAid()
    {
        var (h, s, c, ch) = await Setup(country: B, kind: IkName);
        await h.Service.DispatchClientAsync(s, CountryReq(IkCountry, C));
        Assert.Equal(CcbNoItem, Ack(c).Result);                              // a name item is not a country item

        var (h2, s2, c2, ch2) = await Setup(country: B, item: CountryItem, kind: IkCountry);
        ch2.OriCountry = D; ch2.AidCountry = C;
        await h2.Service.DispatchClientAsync(s2, CountryReq(IkCountry, C));

        var sent = h2.World.WithId(Msg.MW_CHANGECHARBASE_ACK).Select(p =>
        {
            var r = new PacketReader(p); r.ReadUInt32(); r.ReadUInt32(); return (r.ReadByte(), r.ReadByte());
        }).ToArray();
        Assert.Equal(new[] { (IkAidCountry, N), (IkCountry, D) }, sent);   // asked for Craxion, back to Defugel
        Assert.Equal(1, ch2.FindInven(Backpack)!.FindItem(0)!.Count);
    }

    [Fact]
    public async Task TheWorldsCountry_IsThePlayers()
    {
        var (h, s, _, ch) = await Setup();

        await h.Service.DispatchWorldAsync(WorldReq(s, IkCountry, B));

        Assert.Equal(B, ch.Country);
    }

    // ================================ aid country ================================

    [Fact]
    public async Task OnlyBroa_ChoosesAnAidCountry()
    {
        var (h, s, c, _) = await Setup(country: C);

        await h.Service.DispatchClientAsync(s, CountryReq(IkAidCountry, D));

        Assert.Equal(CcbFail, Ack(c).Result);
    }

    [Fact]
    public async Task TheAidCountry_OnceADay()
    {
        var (h, s, c, ch) = await Setup(country: B);
        await h.Service.DispatchClientAsync(s, CountryReq(IkAidCountry, D));
        Assert.Equal((IkAidCountry, D, ""), ToWorld(h));

        await h.Service.DispatchWorldAsync(WorldReq(s, IkAidCountry, D));
        Assert.Equal((D, _now), (ch.AidCountry, ch.AidDate));
        Assert.Equal(86400u, Ack(c).Second);

        _now += 3600;
        await h.Service.DispatchClientAsync(s, CountryReq(IkAidCountry, C));
        Assert.Equal((CcbTime, 82800u), (Ack(c).Result, Ack(c).Second));
    }
}
