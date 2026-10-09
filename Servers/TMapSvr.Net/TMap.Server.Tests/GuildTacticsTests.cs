using TMap.Data;
using TMap.Protocol;
using TMap.Server.Map;
using Xunit;

namespace TMap.Server.Tests;

/// <summary>Guilds, batch G3: mercenaries (tactics), PvP point rewards and the castle guards' shop — the map's checks, what goes
/// to the world, and the world's answers applied.</summary>
public class GuildTacticsTests
{
    private const uint Guild = 77, OtherGuild = 88;
    private const ushort ShopNpc = 23100, GuardSpawn = 29940, TowerSpawn = 29950, MonId = 500, AttrId = 900, MapId = 0;

    private static TemplateStore Store()
    {
        var t = new TemplateStore { Rate1st = 1.0f };
        t.MonsterTemplates[MonId] = new MonsterTemplate(MonId, 5, AttrId);
        t.MonAttrs[TemplateStore.MonAttrKey(AttrId, 5)] = new MonAttrRow(AttrId, 5, 10, 50, 0);
        foreach (var id in new[] { GuardSpawn, TowerSpawn })
            t.MonsterSpawns.Add(new MonsterSpawnDef(new MonSpawnRow(id, MapId, 300, 0, 300, 0, 3, 1, 0, 100, 0, 30_000, 1),
                new List<MapMonRow> { new(id, MonId, 0, 0, 100) }));
        t.SvrMsgs[8] = "[%s] guild has provided [%d] points to you.";
        t.SvrMsgs[30] = "Welcome!";
        t.SvrMsgs[31] = "You have been hired as a mercenary of [%s] guild.";
        t.SvrMsgs[32] = "You are discharged from Mercenary.";
        t.SvrMsgs[33] = "[%d] Merit points are given.";
        return t;
    }

    private static async Task<(MapTestHarness h, ClientSession a, FakeClientChannel ca, ClientSession b, FakeClientChannel cb, FakePostStore db)> Setup()
    {
        var h = new MapTestHarness(Store());
        var db = new FakePostStore();
        h.Service.PostStore = db;
        var (a, ca) = await h.EnterAsync(1, 1, 1, x: 100, z: 100, name: "Ann", preSeeded: new Character { CharId = 1, Name = "Ann" });
        var (b, cb) = await h.EnterAsync(2, 2, 2, x: 101, z: 100, name: "Bob", preSeeded: new Character { CharId = 2, Name = "Bob" });
        (a.Char!.GuildId, a.Char.GuildName, a.Char.GuildDuty, a.Char.Level) = (Guild, "Blue", 2, 30);
        (b.Char!.GuildId, b.Char.GuildName, b.Char.Level) = (OtherGuild, "Red", 30);
        var npc = new Npc { Id = ShopNpc, Type = 22, Country = 3, MapId = MapId, PosX = 100, PosZ = 100 };
        npc.Monsters[1] = new MonsterShopRow(1, ShopNpc, GuardSpawn, 500_000, 0);
        npc.Monsters[2] = new MonsterShopRow(2, ShopNpc, TowerSpawn, 100_000, 3);
        h.Service.AddNpc(npc);
        ca.Clear(); cb.Clear(); h.World.Clear();
        return (h, a, ca, b, cb, db);
    }

    private static byte[] Cs(ushort id, Action<PacketWriter>? body = null) { var w = new PacketWriter(id); body?.Invoke(w); return w.ToArray(); }
    private static byte[] World(ushort id, Action<PacketWriter> body) { var w = new PacketWriter(id); body(w); return w.ToArray(); }
    private static byte[] Join(ushort id, ClientSession to, byte result, uint memberId, string memberName) => World(id, w =>
    {
        w.WriteUInt32(to.CharId); w.WriteUInt32(to.Key); w.WriteByte(result);
        w.WriteUInt32(Guild); w.WriteString("Blue"); w.WriteUInt32(memberId); w.WriteString(memberName);
        w.WriteUInt32(0); w.WriteUInt32(5); w.WriteUInt32(0);
    });

    [Fact]
    public async Task AnInvite_IsAnOfficers_AndTheAnswerGoesToTheWorld()
    {
        var (h, a, _, b, _, _) = await Setup();
        a.Char!.GuildDuty = 0;
        await h.Service.DispatchClientAsync(a, Cs(Msg.CS_GUILDTACTICSINVITE_REQ, w => { w.WriteString("Bob"); w.WriteByte(7); for (int i = 0; i < 4; i++) w.WriteUInt32(1); }));
        Assert.False(h.World.Has(Msg.MW_GUILDTACTICSINVITE_ACK));

        a.Char.GuildDuty = 1;
        await h.Service.DispatchClientAsync(a, Cs(Msg.CS_GUILDTACTICSINVITE_REQ, w => { w.WriteString("Bob"); w.WriteByte(7); for (int i = 0; i < 4; i++) w.WriteUInt32(1); }));
        var r = new PacketReader(h.World.Last(Msg.MW_GUILDTACTICSINVITE_ACK)!);
        Assert.Equal((a.CharId, a.Key, "Bob", (byte)7), (r.ReadUInt32(), r.ReadUInt32(), r.ReadString(), r.ReadByte()));

        await h.Service.DispatchClientAsync(b, Cs(Msg.CS_GUILDTACTICSANSWER_REQ, w => { w.WriteByte(0); w.WriteString("Ann"); w.WriteByte(7); for (int i = 0; i < 4; i++) w.WriteUInt32(1); }));
        r = new PacketReader(h.World.Last(Msg.MW_GUILDTACTICSANSWER_ACK)!);
        Assert.Equal((b.CharId, b.Key, (byte)0, "Ann"), (r.ReadUInt32(), r.ReadUInt32(), r.ReadByte(), r.ReadString()));
    }

    [Fact]
    public async Task Hired_TheMercenaryShowsItsNewGuild_AndTheOneWhoHiredMailsAWelcome()
    {
        var (h, a, ca, b, cb, db) = await Setup();

        await h.Service.DispatchWorldAsync(Join(Msg.MW_GUILDTACTICSANSWER_REQ, b, 0, b.CharId, "Bob"));
        await h.Service.DispatchWorldAsync(Join(Msg.MW_GUILDTACTICSREPLY_REQ, a, 0, b.CharId, "Bob"));

        Assert.Equal((Guild, "Blue"), (b.Char!.TacticsId, b.Char.TacticsName));
        Assert.True(ca.Has(Msg.CS_GUILDATTR_ACK));                          // the ones around see it
        var r = new PacketReader(cb.Last(Msg.CS_GUILDTACTICSANSWER_ACK)!);
        Assert.Equal(((byte)0, b.CharId), (r.ReadByte(), r.ReadUInt32()));
        Assert.True(ca.Has(Msg.CS_GUILDTACTICSREPLY_ACK));
        var mail = Assert.Single(db.Mails);
        Assert.Equal(("Ann", 2u, "Welcome!", "You have been hired as a mercenary of [Blue] guild."), (mail.Sender, mail.CharId, mail.Title, mail.Message));
        Assert.Equal((byte)0, a.Char!.TacticsId == 0 ? (byte)0 : (byte)1);   // the hirer stays as it was
    }

    [Fact]
    public async Task Fired_TheMercenaryLosesItsTacticsGuild_AndIsMailedItsPay()
    {
        var (h, a, ca, b, cb, db) = await Setup();
        (b.Char!.TacticsId, b.Char.TacticsName) = (Guild, "Blue");

        await h.Service.DispatchClientAsync(b, Cs(Msg.CS_GUILDTACTICSKICKOUT_REQ, w => w.WriteUInt32(a.CharId)));   // not an officer
        Assert.True(cb.Has(Msg.CS_GUILDTACTICSKICKOUT_ACK));
        Assert.False(h.World.Has(Msg.MW_GUILDTACTICSKICKOUT_ACK));
        await h.Service.DispatchClientAsync(b, Cs(Msg.CS_GUILDTACTICSKICKOUT_REQ, w => w.WriteUInt32(b.CharId)));   // leaving: anyone
        Assert.True(h.World.Has(Msg.MW_GUILDTACTICSKICKOUT_ACK));

        await h.Service.DispatchWorldAsync(World(Msg.MW_GUILDTACTICSKICKOUT_REQ, w =>
        { w.WriteUInt32(b.CharId); w.WriteUInt32(b.Key); w.WriteByte(0); w.WriteUInt32(b.CharId); w.WriteByte(1); }));
        await h.Service.DispatchWorldAsync(World(Msg.MW_WORLDPOSTSEND_REQ, w =>
        {
            w.WriteByte(1 /* WPT_TACTICSKICK */); w.WriteUInt32(300); w.WriteUInt32(a.CharId); w.WriteString("Ann");
            w.WriteUInt32(b.CharId); w.WriteString("Bob"); w.WriteInt64(2_000_005);
        }));
        await Task.Delay(50);

        Assert.Equal((0u, ""), (b.Char.TacticsId, b.Char.TacticsName));
        Assert.True(ca.Has(Msg.CS_GUILDATTR_ACK));
        var mail = Assert.Single(db.Mails);
        Assert.Equal(("You are discharged from Mercenary.", "[300] Merit points are given.", 2u, 0u, 5u), (mail.Title, mail.Message, mail.Gold, mail.Silver, mail.Cooper));
    }

    [Fact]
    public async Task ApplyingWhileAMercenary_OrToOnesOwnGuild_IsRefusedHere()
    {
        var (h, _, _, b, cb, _) = await Setup();

        await h.Service.DispatchClientAsync(b, Cs(Msg.CS_GUILDTACTICSVOLUNTEERING_REQ, w => { w.WriteUInt32(OtherGuild); w.WriteUInt32(1); }));
        Assert.Equal((byte)19 /* GUILD_SAMEGUILDTACTICS */, new PacketReader(cb.Last(Msg.CS_GUILDTACTICSVOLUNTEERING_ACK)!).ReadByte());
        b.Char!.TacticsId = 5;
        await h.Service.DispatchClientAsync(b, Cs(Msg.CS_GUILDTACTICSVOLUNTEERING_REQ, w => { w.WriteUInt32(Guild); w.WriteUInt32(1); }));
        Assert.Equal((byte)8 /* GUILD_HAVEGUILD */, new PacketReader(cb.Last(Msg.CS_GUILDTACTICSVOLUNTEERING_ACK)!).ReadByte());
        Assert.False(h.World.Has(Msg.MW_GUILDTACTICSVOLUNTEERING_ACK));

        b.Char.TacticsId = 0;
        await h.Service.DispatchClientAsync(b, Cs(Msg.CS_GUILDTACTICSVOLUNTEERING_REQ, w => { w.WriteUInt32(Guild); w.WriteUInt32(1); }));
        Assert.True(h.World.Has(Msg.MW_GUILDTACTICSVOLUNTEERING_ACK));
        await h.Service.DispatchWorldAsync(World(Msg.MW_GUILDTACTICSWANTEDLIST_REQ, w => { w.WriteUInt32(b.CharId); w.WriteUInt32(b.Key); w.WriteUInt32(0); }));
        Assert.Equal(0u, new PacketReader(cb.Last(Msg.CS_GUILDTACTICSWANTEDLIST_ACK)!).ReadUInt32());
    }

    [Fact]
    public async Task APointReward_IsTheChiefs_AndTheMemberIsMailed()
    {
        var (h, a, ca, _, _, db) = await Setup();
        a.Char!.GuildDuty = 1;
        await h.Service.DispatchClientAsync(a, Cs(Msg.CS_GUILDPOINTREWARD_REQ, w => { w.WriteString("Bob"); w.WriteUInt32(50); w.WriteString("gg"); }));
        Assert.False(h.World.Has(Msg.MW_GUILDPOINTREWARD_ACK));

        a.Char.GuildDuty = 2;
        await h.Service.DispatchClientAsync(a, Cs(Msg.CS_GUILDPOINTREWARD_REQ, w => { w.WriteString(""); w.WriteUInt32(50); w.WriteString("gg"); }));
        Assert.Equal((byte)2 /* GPR_NOMEMBER */, new PacketReader(ca.Last(Msg.CS_GUILDPOINTREWARD_ACK)!).ReadByte());
        await h.Service.DispatchClientAsync(a, Cs(Msg.CS_GUILDPOINTREWARD_REQ, w => { w.WriteString("Bob"); w.WriteUInt32(50); w.WriteString("gg"); }));
        Assert.True(h.World.Has(Msg.MW_GUILDPOINTREWARD_ACK));

        await h.Service.DispatchWorldAsync(World(Msg.MW_GUILDPOINTREWARD_REQ, w =>
        {
            w.WriteByte(0); w.WriteUInt32(a.CharId); w.WriteUInt32(a.Key); w.WriteUInt32(950);
            w.WriteUInt32(50); w.WriteUInt32(2); w.WriteString("Bob"); w.WriteString("gg");
        }));
        await Task.Delay(50);
        var r = new PacketReader(ca.Last(Msg.CS_GUILDPOINTREWARD_ACK)!);
        Assert.Equal(((byte)0, 950u), (r.ReadByte(), r.ReadUInt32()));
        var mail = Assert.Single(db.Mails);
        Assert.Equal(("[Blue] guild has provided [50] points to you.", "gg", 2u), (mail.Title, mail.Message, mail.CharId));
    }

    [Fact]
    public async Task TheGuardShop_ListsItsPosts_AndAnOfficerBuysWithTheGuildsMoney()
    {
        var (h, a, ca, b, cb, _) = await Setup();

        await h.Service.DispatchClientAsync(a, Cs(Msg.CS_NPCITEMLIST_REQ, w => w.WriteUInt16(ShopNpc)));
        var r = new PacketReader(ca.Last(Msg.CS_NPCITEMLIST_ACK)!);
        Assert.Equal((ShopNpc, (byte)22), (r.ReadUInt16(), r.ReadByte()));
        r.ReadByte();
        Assert.Equal(((byte)2, (ushort)1, 500_000u), (r.ReadByte(), r.ReadUInt16(), r.ReadUInt32()));

        await h.Service.DispatchClientAsync(b, Cs(Msg.CS_MONSTERBUY_REQ, w => { w.WriteUInt16(ShopNpc); w.WriteUInt16(1); }));
        Assert.Equal((byte)5 /* MSB_AUTHORITY */, new PacketReader(cb.Last(Msg.CS_MONSTERBUY_ACK)!).ReadByte());
        await h.Service.DispatchClientAsync(a, Cs(Msg.CS_MONSTERBUY_REQ, w => { w.WriteUInt16(ShopNpc); w.WriteUInt16(2); }));
        Assert.Equal((byte)4 /* MSB_CAMPMISMATCH: nobody's ball on that tower */, new PacketReader(ca.Last(Msg.CS_MONSTERBUY_ACK)!).ReadByte());

        await h.Service.DispatchClientAsync(a, Cs(Msg.CS_MONSTERBUY_REQ, w => { w.WriteUInt16(ShopNpc); w.WriteUInt16(1); }));
        r = new PacketReader(h.World.Last(Msg.MW_MONSTERBUY_ACK)!);
        Assert.Equal((a.CharId, a.Key, ShopNpc, (ushort)1, 500_000u), (r.ReadUInt32(), r.ReadUInt32(), r.ReadUInt16(), r.ReadUInt16(), r.ReadUInt32()));

        byte[] Paid(ClientSession to) => World(Msg.MW_MONSTERBUY_REQ, w =>
        { w.WriteByte(0); w.WriteUInt32(to.CharId); w.WriteUInt32(to.Key); w.WriteUInt32(Guild); w.WriteUInt16(ShopNpc); w.WriteUInt16(1); w.WriteUInt32(500_000); });
        await h.Service.DispatchWorldAsync(Paid(a));
        Assert.Equal((byte)0, new PacketReader(ca.Last(Msg.CS_MONSTERBUY_ACK)!).ReadByte());
        var guard = h.State.FindMonster(Monster.MakeId(GuardSpawn, 1, 0));
        Assert.NotNull(guard);
        Assert.Equal(a.Char!.Country, guard!.Country);                      // it fights for the buyer's country

        h.World.Clear();
        await h.Service.DispatchClientAsync(a, Cs(Msg.CS_MONSTERBUY_REQ, w => { w.WriteUInt16(ShopNpc); w.WriteUInt16(1); }));
        Assert.Equal((byte)6 /* MSB_ALREADY */, new PacketReader(ca.Last(Msg.CS_MONSTERBUY_ACK)!).ReadByte());
        await h.Service.DispatchWorldAsync(Paid(a));                         // paid twice: the money goes back
        r = new PacketReader(h.World.Last(Msg.MW_GUILDMONEYRECOVER_ACK)!);
        Assert.Equal((Guild, 500_000u), (r.ReadUInt32(), r.ReadUInt32()));
    }
}
