using TMap.Data;
using TMap.Protocol;
using TMap.Server.Map;
using Xunit;

namespace TMap.Server.Tests;

/// <summary>The small leftover requests: mode switch, skill cancel, cancelled action, "who is your target", take-all loot, the
/// quest timer / position requests, the helmet and the comment, the world's level, and inspecting across map servers.</summary>
public class SmallRequestsTests
{
    private const ushort Drop = 300, Sword = 1000, Kick = 1001, Other = 1002;

    private static byte[] Req(ushort id, Action<PacketWriter> body) { var w = new PacketWriter(id); body(w); return w.ToArray(); }

    private static async Task<(MapTestHarness h, ClientSession a, FakeClientChannel ca, ClientSession b, FakeClientChannel cb)> Two(
        TemplateStore? t = null, Character? a = null)
    {
        var h = new MapTestHarness(t ?? new TemplateStore());
        var (sa, ca) = await h.EnterAsync(1, 1, 1, x: 100, z: 100, name: "Ann", preSeeded: a);
        var (sb, cb) = await h.EnterAsync(2, 2, 2, x: 105, z: 100, name: "Bob");
        ca.Clear(); cb.Clear(); h.World.Clear();
        return (h, sa, ca, sb, cb);
    }

    [Fact]
    public async Task TheModeSwitch_IsShownAround_AndHoldsBackRegen()
    {
        var (h, a, _, _, cb) = await Two();
        h.Service.NowMs = 1000;

        await h.Service.DispatchClientAsync(a, Req(Msg.CS_CHGMODE_REQ, w => w.WriteByte(1)));

        var r = new PacketReader(cb.Last(Msg.CS_CHGMODE_ACK)!);
        Assert.Equal((1u, (byte)1, (byte)1), (r.ReadUInt32(), r.ReadByte(), r.ReadByte()));
        Assert.Equal((byte)1, a.Char!.Mode);
        Assert.True(a.Char.RecoverHpTick > 1000);
    }

    [Fact]
    public async Task CancellingASkill_ResetsItAndItsKind()
    {
        var t = new TemplateStore();
        SkillTemplate Tpl(ushort id, byte kind) => new(id, kind, 0, 0, 0, 0, 1, 1, 1, 10_000, 0, 0, 5_000, 0, Positive: 1, MapId: 0xFFFF);
        var ch = new Character { CharId = 1, Name = "Ann", MaxHp = 100, Hp = 100 };
        foreach (var (id, kind) in new[] { (Sword, (byte)3), (Kick, (byte)3), (Other, (byte)4) })
        {
            var k = new Skill { SkillId = id, Level = 1, Template = t.Skills[id] = Tpl(id, kind) };
            k.UseSkill(100, 0, 100);
            ch.Skills.Add(k);
        }
        var (h, a, _, _, _) = await Two(t, ch);
        h.Service.NowMs = 6_000;                                  // 4 100 left on each: within Sword's 5 000 kind delay

        await h.Service.DispatchClientAsync(a, Req(Msg.CS_CANCELSKILL_REQ, w => { w.WriteByte(1); w.WriteUInt32(1); w.WriteUInt16(Sword); }));

        Assert.Equal(new uint[] { 0, 0, 4_100 }, ch.Skills.Select(k => k.GetReuseRemainTick(6_000)).ToArray());
    }

    [Fact]
    public async Task ACancelledAction_IsShownAround()
    {
        var (h, a, _, _, cb) = await Two();

        await h.Service.DispatchClientAsync(a, Req(Msg.CS_CANCELACTION_REQ, w => { w.WriteUInt32(1); w.WriteByte(1); }));

        Assert.True(cb.Has(Msg.CS_CANCELACTION_ACK));
    }

    [Fact]
    public async Task WhoIsYourTarget_GoesAndComesBack()
    {
        var (h, a, ca, b, cb) = await Two();

        await h.Service.DispatchClientAsync(a, Req(Msg.CS_GETTARGET_REQ, w => w.WriteUInt32(2)));
        Assert.Equal(1u, new PacketReader(cb.Last(Msg.CS_GETTARGETANS_ACK)!).ReadUInt32());

        await h.Service.DispatchClientAsync(b, Req(Msg.CS_GETTARGETANS_REQ, w => { w.WriteUInt32(1); w.WriteUInt32(0x60001); w.WriteByte(2); }));
        var r = new PacketReader(ca.Last(Msg.CS_GETTARGET_ACK)!);
        Assert.Equal((0x60001u, (byte)2), (r.ReadUInt32(), r.ReadByte()));
    }

    [Fact]
    public async Task TakeAll_TakesTheMoneyAndEveryItemThatFits()
    {
        var t = new TemplateStore();
        var tpl = t.Items[Drop] = new ItemTemplate(Drop, 0, new[] { 1f, 0f, 0f, 0f });
        var ch = new Character { CharId = 1, Name = "Ann", MaxHp = 100, Hp = 100 };
        ch.Invens.Add(new Inven { InvenId = 0xFF });
        var (h, a, ca, _, _) = await Two(t, ch);
        var mon = new Monster { Id = 0x60001, ChartId = 500, MaxHp = 10, Hp = 0, PosX = 100, PosZ = 100, Channel = 1, CorpseMoney = 1234 };
        h.Service.SpawnMonster(mon);
        for (byte i = 0; i < 3; i++) mon.CorpseInven.Items.Add(new Item { ItemSlot = i, TemplateId = Drop, Count = 1, Template = tpl });
        mon.CorpseInven.Items[2].OwnerId = 9;                     // someone else's quest drop

        await h.Service.DispatchClientAsync(a, Req(Msg.CS_MONITEMTAKEALL_REQ, w => w.WriteUInt32(mon.Id)));

        Assert.Equal((1u, 234u), (ch.Silver, ch.Cooper));                 // 1 234 copper
        Assert.Equal(2, ch.FindInven(0xFF)!.Items.Count);
        Assert.Single(mon.CorpseInven.Items);
        Assert.Equal((byte)MonItemTakeResult.Success, new PacketReader(ca.Last(Msg.CS_MONITEMTAKE_ACK)!).ReadByte());
    }

    // ================================ quests ================================

    private static (TemplateStore t, Character ch, QuestProgress qp) WithQuest(byte termType)
    {
        var t = new TemplateStore();
        var q = new QuestTemplate { QuestId = 77 };
        q.Terms.Add(new QuestTerm(5, termType, 1));
        var ch = new Character { CharId = 1, Name = "Ann", MaxHp = 100, Hp = 100 };
        var qp = new QuestProgress { Template = q, TriggerCount = 1, BeginTick = 500 };
        qp.RunningTerms.Add(new RunningTerm { TermId = 5, TermType = termType, Count = 0 });
        ch.Quests[77] = qp;
        return (t, ch, qp);
    }

    private static (uint Quest, uint Term, byte Type, byte Count, byte Status) Update(FakeClientChannel c)
    {
        var r = new PacketReader(c.Last(Msg.CS_QUESTUPDATE_ACK)!);
        return (r.ReadUInt32(), r.ReadUInt32(), r.ReadByte(), r.ReadByte(), r.ReadByte());
    }

    [Fact]
    public async Task TheClientsTimerRanOut_TheQuestFails()
    {
        var (t, ch, qp) = WithQuest(6 /* QTT_TIMER */);
        var (h, a, ca, _, _) = await Two(t, ch);

        await h.Service.DispatchClientAsync(a, Req(Msg.CS_QUESTENDTIMER_REQ, w => w.WriteUInt32(77)));

        Assert.Equal((77u, 5u, (byte)6, (byte)0, (byte)QuestTermStatus.Failed), Update(ca));
        Assert.Equal(0u, qp.BeginTick);
    }

    [Fact]
    public async Task AHuntTermReachedByPosition_IsDone()
    {
        var (t, ch, qp) = WithQuest(3 /* QTT_HUNT */);
        var (h, a, ca, _, _) = await Two(t, ch);

        await h.Service.DispatchClientAsync(a, Req(Msg.CS_QUESTPOSEXEC_REQ, w => { w.WriteUInt32(77); w.WriteUInt32(5); }));

        Assert.Equal((77u, 5u, (byte)3, (byte)1, (byte)QuestTermStatus.Success), Update(ca));
        Assert.Equal(1, qp.RunningTerms[0].Count);
    }

    [Fact]
    public async Task OnlyAHuntTerm_IsDoneByPosition()
    {
        var (t, ch, _) = WithQuest(6);
        var (h, a, ca, _, _) = await Two(t, ch);

        await h.Service.DispatchClientAsync(a, Req(Msg.CS_QUESTPOSEXEC_REQ, w => { w.WriteUInt32(77); w.WriteUInt32(5); }));

        Assert.False(ca.Has(Msg.CS_QUESTUPDATE_ACK));
    }

    // ================================ character ================================

    [Fact]
    public async Task TheHelmet_GoesThroughTheWorld_AndIsShownAround()
    {
        var (h, a, _, _, cb) = await Two();

        await h.Service.DispatchClientAsync(a, Req(Msg.CS_HELMETHIDE_REQ, w => w.WriteByte(1)));
        var raw = (byte[])h.World.Last(Msg.MW_HELMETHIDE_ACK)!.Clone();
        PacketHeader.WriteId(raw, Msg.MW_HELMETHIDE_REQ);
        await h.Service.DispatchWorldAsync(raw);

        Assert.Equal((byte)1, a.Char!.HelmetHide);
        var r = new PacketReader(cb.Last(Msg.CS_HELMETHIDE_ACK)!);
        Assert.Equal((1u, (byte)1), (r.ReadUInt32(), r.ReadByte()));
    }

    [Fact]
    public async Task TheComment_IsShownToOnesSide_NotToEnemies()
    {
        var (h, a, _, b, cb) = await Two();
        a.Char!.Country = 0; a.Char.AidCountry = 3;
        b.Char!.Country = 0; b.Char.AidCountry = 3;

        await h.Service.DispatchClientAsync(a, Req(Msg.CS_COMMENT_REQ, w => w.WriteString("hello")));
        Assert.Equal("hello", a.Char.Comment);
        Assert.True(cb.Has(Msg.CS_COMMENT_ACK));

        cb.Clear(); b.Char.Country = 1;
        await h.Service.DispatchClientAsync(a, Req(Msg.CS_COMMENT_REQ, w => w.WriteString("bye")));
        Assert.False(cb.Has(Msg.CS_COMMENT_ACK));
    }

    [Fact]
    public async Task TheWorld_SetsTheLevel()
    {
        var (h, a, _, _, _) = await Two();

        await h.Service.DispatchWorldAsync(Req(Msg.MW_LEVELUP_REQ, w => { w.WriteUInt32(1); w.WriteUInt32(a.Key); w.WriteByte(42); }));

        Assert.Equal((byte)42, a.Char!.Level);
    }

    [Fact]
    public async Task InspectingSomeoneNotHere_GoesThroughTheWorld_AndTheSheetComesBack()
    {
        var (h, a, ca, _, _) = await Two();

        await h.Service.DispatchClientAsync(a, Req(Msg.CS_CHARSTATINFO_REQ, w => w.WriteUInt32(99)));
        var ask = new PacketReader(h.World.Last(Msg.MW_CHARSTATINFO_ACK)!);
        Assert.Equal((1u, 99u), (ask.ReadUInt32(), ask.ReadUInt32()));

        // the world asks this server for player 2's sheet on behalf of player 7…
        await h.Service.DispatchWorldAsync(Req(Msg.MW_CHARSTATINFOANS_REQ, w => { w.WriteUInt32(7); w.WriteUInt32(2); }));
        var ans = (byte[])h.World.Last(Msg.MW_CHARSTATINFOANS_ACK)!.Clone();
        Assert.Equal(7u, new PacketReader(ans).ReadUInt32());

        // …and the answer, sent back as MW_CHARSTATINFO_REQ for player 1, reaches it as its CS_CHARSTATINFO_ACK
        BitConverter.GetBytes(1u).CopyTo(ans, PacketHeader.Size);
        PacketHeader.WriteId(ans, Msg.MW_CHARSTATINFO_REQ);
        await h.Service.DispatchWorldAsync(ans);
        Assert.Equal(2u, new PacketReader(ca.Last(Msg.CS_CHARSTATINFO_ACK)!).ReadUInt32());
    }
}
