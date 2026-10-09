using TMap.Data;
using TMap.Protocol;
using TMap.Server.Map;
using Xunit;

namespace TMap.Server.Tests;

/// <summary>Guilds, batch G2: the guild cabinet, contribution, fame, the board and recruiting — the map's checks, what goes to
/// the world, and the world's answers applied.</summary>
public class GuildBoardTests
{
    private const ushort Gem = 600, Junk = 601;
    private const uint Guild = 77;

    private static TemplateStore Store()
    {
        var t = new TemplateStore();
        t.Items[Gem] = new ItemTemplate(Gem, 0, new[] { 0f, 0f, 0f, 0f }, Stack: 50, IsSell: 4 | 1);   // ITEMTRADE_CABINET, can deal
        t.Items[Junk] = new ItemTemplate(Junk, 0, new[] { 0f, 0f, 0f, 0f }, Stack: 50, IsSell: 1);
        return t;
    }

    private static async Task<(MapTestHarness h, ClientSession a, FakeClientChannel ca, ClientSession b, FakeClientChannel cb)> Setup()
    {
        var store = Store();
        var h = new MapTestHarness(store);
        var (a, ca) = await h.EnterAsync(1, 1, 1, x: 100, z: 100, name: "Ann", preSeeded: new Character { CharId = 1, Name = "Ann" });
        var (b, cb) = await h.EnterAsync(2, 2, 2, x: 101, z: 100, name: "Bob", preSeeded: new Character { CharId = 2, Name = "Bob" });
        (a.Char!.GuildId, a.Char.GuildDuty, a.Char.Level) = (Guild, 2, 30);
        var bag = new Inven { InvenId = 0 };
        bag.Items.Add(new Item { ItemSlot = 0, TemplateId = Gem, Count = 10, DlId = 900, Template = store.Item(Gem) });
        bag.Items.Add(new Item { ItemSlot = 1, TemplateId = Junk, Count = 1, DlId = 901, Template = store.Item(Junk) });
        a.Char.Invens.Add(bag);
        ca.Clear(); cb.Clear(); h.World.Clear();
        return (h, a, ca, b, cb);
    }

    private static byte[] Cs(ushort id, Action<PacketWriter>? body = null) { var w = new PacketWriter(id); body?.Invoke(w); return w.ToArray(); }
    private static byte[] World(ushort id, ClientSession s, Action<PacketWriter> body)
    {
        var w = new PacketWriter(id);
        w.WriteUInt32(s.CharId); w.WriteUInt32(s.Key);
        body(w);
        return w.ToArray();
    }

    private static byte Result(FakeClientChannel c, ushort id) => new PacketReader(c.Last(id)!).ReadByte();

    [Fact]
    public async Task PuttingPartOfAStackIn_TakesItFromTheBag_AndTellsTheWorld()
    {
        var (h, a, ca, _, _) = await Setup();

        await h.Service.DispatchClientAsync(a, Cs(Msg.CS_GUILDCABINETPUTIN_REQ, w => { w.WriteByte(0); w.WriteByte(0); w.WriteByte(4); }));

        Assert.Equal((byte)6, a.Char!.FindInven(0)!.FindItem(0)!.Count);
        Assert.True(ca.Has(Msg.CS_UPDATEITEM_ACK));
        Assert.Equal((byte)0, Result(ca, Msg.CS_GUILDCABINETPUTIN_ACK));
        var r = new PacketReader(h.World.Last(Msg.MW_GUILDCABINETPUTIN_ACK)!);
        Assert.Equal((a.CharId, a.Key, 1u), (r.ReadUInt32(), r.ReadUInt32(), r.ReadUInt32()));
        long dl = r.ReadInt64(); r.ReadByte();
        Assert.NotEqual(900L, dl);                                          // part of a stack: a new row
        Assert.Equal(Gem, r.ReadUInt16());
        r.ReadByte(); r.ReadByte(); r.ReadUInt16();
        Assert.Equal((byte)4, r.ReadByte());
        Assert.Null(a.Char.GuildItem);
    }

    [Fact]
    public async Task OnlyCabinetItems_AndOnlyAViceChief()
    {
        var (h, a, ca, _, _) = await Setup();

        await h.Service.DispatchClientAsync(a, Cs(Msg.CS_GUILDCABINETPUTIN_REQ, w => { w.WriteByte(0); w.WriteByte(1); w.WriteByte(1); }));
        Assert.Equal((byte)1 /* GUILD_CABINET_FAIL */, Result(ca, Msg.CS_GUILDCABINETPUTIN_ACK));

        a.Char!.GuildDuty = 0;
        await h.Service.DispatchClientAsync(a, Cs(Msg.CS_GUILDCABINETPUTIN_REQ, w => { w.WriteByte(0); w.WriteByte(0); w.WriteByte(1); }));
        Assert.Equal((byte)6 /* GUILD_CABINET_NOTDUTY */, Result(ca, Msg.CS_GUILDCABINETPUTIN_ACK));
        Assert.False(h.World.Has(Msg.MW_GUILDCABINETPUTIN_ACK));
        Assert.Equal((byte)10, a.Char.FindInven(0)!.FindItem(0)!.Count);
    }

    [Fact]
    public async Task TheWorldsCabinet_ReachesTheClient_LastSlotFirst()
    {
        var (h, a, ca, _, _) = await Setup();

        await h.Service.DispatchWorldAsync(World(Msg.MW_GUILDCABINETLIST_REQ, a, w =>
        {
            w.WriteByte(30); w.WriteByte(2);
            foreach (var (slot, item) in new[] { (1u, Gem), (2u, Junk) })
            {
                w.WriteUInt32(slot);
                w.WriteInt64(5000 + slot); w.WriteByte(0); w.WriteUInt16(item); w.WriteByte(0); w.WriteByte(0); w.WriteUInt16(0);
                w.WriteByte(3); w.WriteByte(0); w.WriteUInt32(0); w.WriteUInt32(0); w.WriteByte(0); w.WriteInt64(0); w.WriteByte(0);
                for (int e = 0; e < 4; e++) w.WriteUInt32(0);
                w.WriteByte(0);
            }
        }));

        var r = new PacketReader(ca.Last(Msg.CS_GUILDCABINETLIST_ACK)!);
        Assert.Equal(((byte)30, (byte)2, 2u), (r.ReadByte(), r.ReadByte(), r.ReadUInt32()));
    }

    [Fact]
    public async Task AContribution_IsCheckedHere_AndTakenWhenTheWorldSaysYes()
    {
        var (h, a, ca, _, _) = await Setup();
        a.Char!.Gold = 0; a.Char.Silver = 0; a.Char.Cooper = 500;

        await h.Service.DispatchClientAsync(a, Cs(Msg.CS_GUILDCONTRIBUTION_REQ, w => { w.WriteUInt32(0); w.WriteUInt32(0); w.WriteUInt32(1); w.WriteUInt32(0); w.WriteUInt32(0); }));
        Assert.Equal((byte)1 /* NOTENOUGH */, Result(ca, Msg.CS_GUILDCONTRIBUTION_ACK));

        await h.Service.DispatchClientAsync(a, Cs(Msg.CS_GUILDCONTRIBUTION_REQ, w => { w.WriteUInt32(0); w.WriteUInt32(0); w.WriteUInt32(0); w.WriteUInt32(300); w.WriteUInt32(0); }));
        Assert.True(h.World.Has(Msg.MW_GUILDCONTRIBUTION_ACK));
        Assert.Equal(500u, a.Char.Cooper);                                   // not yet

        await h.Service.DispatchWorldAsync(World(Msg.MW_GUILDCONTRIBUTION_REQ, a, w =>
        {
            w.WriteByte(0); w.WriteUInt32(0); w.WriteUInt32(0); w.WriteUInt32(0); w.WriteUInt32(300); w.WriteUInt32(0);
        }));
        Assert.Equal(200u, a.Char.Cooper);
        Assert.Equal((byte)0, Result(ca, Msg.CS_GUILDCONTRIBUTION_ACK));
    }

    [Fact]
    public async Task TheFame_IsTheChiefs_AndShowsAroundWhenSet()
    {
        var (h, a, ca, _, cb) = await Setup();
        a.Char!.GuildDuty = 1;

        await h.Service.DispatchClientAsync(a, Cs(Msg.CS_GUILDFAME_REQ, w => { w.WriteUInt32(5); w.WriteUInt32(6); }));
        Assert.Equal((byte)16, Result(ca, Msg.CS_GUILDFAME_ACK));

        await h.Service.DispatchWorldAsync(World(Msg.MW_GUILDFAME_REQ, a, w => { w.WriteByte(0); w.WriteUInt32(9); w.WriteUInt32(5); w.WriteUInt32(6); }));
        Assert.Equal((5u, 6u), (a.Char.Fame, a.Char.FameColor));
        Assert.True(cb.Has(Msg.CS_GUILDATTR_ACK));
        Assert.False(ca.WithId(Msg.CS_GUILDFAME_ACK).Count() > 1);         // only the one who set it hears the result
    }

    [Fact]
    public async Task TheBoard_AndRecruiting_AreRelayed()
    {
        var (h, a, ca, b, cb) = await Setup();

        await h.Service.DispatchClientAsync(a, Cs(Msg.CS_GUILDARTICLEADD_REQ, w => { w.WriteString("Hi"); w.WriteString("All"); }));
        await h.Service.DispatchClientAsync(a, Cs(Msg.CS_GUILDWANTEDADD_REQ, w => { w.WriteUInt32(0); w.WriteString("Join"); w.WriteString("us"); w.WriteByte(1); w.WriteByte(99); }));
        await h.Service.DispatchClientAsync(b, Cs(Msg.CS_GUILDVOLUNTEERING_REQ, w => w.WriteUInt32(Guild)));
        Assert.True(h.World.Has(Msg.MW_GUILDARTICLEADD_ACK));
        Assert.True(h.World.Has(Msg.MW_GUILDWANTEDADD_ACK));
        Assert.True(h.World.Has(Msg.MW_GUILDVOLUNTEERING_ACK));

        await h.Service.DispatchWorldAsync(World(Msg.MW_GUILDARTICLEADD_REQ, a, w => w.WriteByte(0)));
        await h.Service.DispatchWorldAsync(World(Msg.MW_GUILDWANTEDLIST_REQ, b, w => { w.WriteUInt32(1); w.WriteUInt32(Guild); }));
        Assert.Equal((byte)0, Result(ca, Msg.CS_GUILDARTICLEADD_ACK));
        var r = new PacketReader(cb.Last(Msg.CS_GUILDWANTEDLIST_ACK)!);
        Assert.Equal((1u, Guild), (r.ReadUInt32(), r.ReadUInt32()));

        b.Char!.GuildId = 5;
        await h.Service.DispatchClientAsync(b, Cs(Msg.CS_GUILDVOLUNTEERING_REQ, w => w.WriteUInt32(Guild)));
        Assert.Equal((byte)8 /* GUILD_HAVEGUILD */, Result(cb, Msg.CS_GUILDVOLUNTEERING_ACK));
    }
}
