using TMap.Data;
using TMap.Protocol;
using TMap.Server.Map;
using Xunit;

namespace TMap.Server.Tests;

/// <summary>C++ OnCS_LOOPSKILL_REQ: a channelled / repeating skill's repeat — checks, the loop delay, the cost, the broadcast,
/// and the multi-attack missile spread. Plus the world's event prize mail (MW_WORLDPOSTSEND_REQ WPT_LOTITEM).</summary>
public class LoopSkillTests
{
    private const ushort Loop = 1200, Missiles = 324, Prize = 500;
    private const uint T1 = 0x60001, T2 = 0x60002, Bystander = 0x60003;

    private static SkillTemplate Tpl(ushort id, uint loopDelay = 3_000, byte targetHit = 1, uint useMp = 0)
        => new(id, 0, useMp, useMp == 0 ? (byte)0 : (byte)1, 0, 0, 1, 1, 1, 0, 0, loopDelay, 0, 0, 0, 0xFFFF, Rate1stX: 100f, TargetHit: targetHit);

    private static async Task<(MapTestHarness h, ClientSession a, FakeClientChannel ca, FakeClientChannel cb, Character ch)> Setup(
        SkillTemplate tpl, byte level = 1)
    {
        var t = new TemplateStore { Rate1st = 100f };
        t.Classes[0] = new StatSeed(1, 1, 1, 1, 1, 1);
        t.Races[0] = new StatSeed(1, 1, 1, 1, 1, 1);
        t.Skills[tpl.Id] = tpl;
        var ch = new Character { CharId = 1, Name = "Ann", MaxHp = 100, Hp = 100, MaxMp = 100, Mp = 100 };
        ch.Skills.Add(new Skill { SkillId = tpl.Id, Level = level, Template = tpl });
        var h = new MapTestHarness(t);
        var (a, ca) = await h.EnterAsync(1, 1, 1, x: 100, z: 100, name: "Ann", preSeeded: ch);
        var (_, cb) = await h.EnterAsync(2, 2, 2, x: 105, z: 100, name: "Bob");
        h.Service.CombatRng = new FixedRandom(0);
        h.Service.NowMs = 10_000;
        ca.Clear(); cb.Clear();
        return (h, a, ca, cb, ch);
    }

    private static byte[] LoopReq(ushort skill, params (uint Id, bool IsTarget)[] listed)
    {
        var w = new PacketWriter(Msg.CS_LOOPSKILL_REQ);
        w.WriteUInt32(1); w.WriteByte(1); w.WriteByte(1); w.WriteUInt16(0); w.WriteUInt16(skill);
        w.WriteFloat(100); w.WriteFloat(0); w.WriteFloat(100);
        w.WriteByte((byte)listed.Length);
        foreach (var (id, isTarget) in listed) { w.WriteUInt32(id); w.WriteByte(2); w.WriteByte((byte)(isTarget ? 1 : 0)); }
        return w.ToArray();
    }

    private static (SkillUseResult Result, uint[] Targets) Ack(FakeClientChannel c)
    {
        var r = new PacketReader(c.Last(Msg.CS_LOOPSKILL_ACK)!);
        var result = (SkillUseResult)r.ReadByte();
        r.ReadUInt32(); r.ReadByte(); r.ReadUInt16();
        if (result != SkillUseResult.Success) return (result, System.Array.Empty<uint>());
        r.ReadByte(); r.ReadUInt16(); r.ReadByte();
        for (int i = 0; i < 4; i++) r.ReadUInt32();
        r.ReadByte(); r.ReadByte(); r.ReadByte(); r.ReadByte();
        r.ReadFloat(); r.ReadFloat(); r.ReadFloat();
        var ids = new uint[r.ReadByte()];
        for (int i = 0; i < ids.Length; i++) { ids[i] = r.ReadUInt32(); r.ReadByte(); }
        return (result, ids);
    }

    [Fact]
    public async Task ALoop_IsShownAround_WithItsTargetsOnly_AndCosts()
    {
        var (h, a, ca, cb, ch) = await Setup(Tpl(Loop, useMp: 10));

        await h.Service.DispatchClientAsync(a, LoopReq(Loop, (T1, true), (Bystander, false)));

        var (result, targets) = Ack(cb);
        Assert.Equal(SkillUseResult.Success, result);
        Assert.Equal(new[] { T1 }, targets);
        Assert.True(ch.Mp < 100);
        Assert.True(cb.Has(Msg.CS_HPMP_ACK));
    }

    [Fact]
    public async Task TheNextLoop_WaitsForTheLoopDelay()
    {
        var (h, a, ca, _, _) = await Setup(Tpl(Loop, loopDelay: 3_000));
        await h.Service.DispatchClientAsync(a, LoopReq(Loop, (T1, true)));

        h.Service.NowMs += 1_000;
        await h.Service.DispatchClientAsync(a, LoopReq(Loop, (T1, true)));
        Assert.Equal(SkillUseResult.SpeedyUse, Ack(ca).Result);

        h.Service.NowMs += 5_000;
        await h.Service.DispatchClientAsync(a, LoopReq(Loop, (T1, true)));
        Assert.Equal(SkillUseResult.Success, Ack(ca).Result);
    }

    [Fact]
    public async Task AnUnlearnedSkill_IsNotFound()
    {
        var (h, a, ca, _, _) = await Setup(Tpl(Loop), level: 0);

        await h.Service.DispatchClientAsync(a, LoopReq(Loop, (T1, true)));

        Assert.Equal(SkillUseResult.NotFound, Ack(ca).Result);
    }

    [Fact]
    public async Task NotEnoughMp_IsRefused()
    {
        var (h, a, ca, _, ch) = await Setup(Tpl(Loop, useMp: 10));
        ch.Mp = 0;

        await h.Service.DispatchClientAsync(a, LoopReq(Loop, (T1, true)));

        Assert.Equal(SkillUseResult.NeedMp, Ack(ca).Result);
    }

    private static SkillTemplate MissileSkill()
    {
        var t = Tpl(Missiles, targetHit: 3);
        t.Data.Add(new SkillDataRow(0 /* SA_ONCE */, 1 /* SDT_ABILITY */, 2, SkillTemplate.MtypeEfc, 1, 3, 1, 1));   // Mana Arrows' row
        return t;
    }

    [Fact]
    public async Task Missiles_OneEachThenTheRestOnTheFirst()
    {
        var tpl = MissileSkill();
        int n = tpl.CalcAbilityValue(1, 0, SkillTemplate.MtypeEfc, 1);
        Assert.True(n >= 3);
        var (h, a, ca, _, _) = await Setup(tpl);

        await h.Service.DispatchClientAsync(a, LoopReq(Missiles, (T1, true), (T2, true)));

        // FixedRandom(0): no extra missile per target, so one each, then the rest on the first.
        var expected = new List<uint> { T1, T2 };
        for (int i = 2; i < n; i++) expected.Add(T1);
        Assert.Equal(expected.ToArray(), Ack(ca).Targets);
    }

    [Fact]
    public async Task Missiles_ARollPutsMoreOnATarget()
    {
        var tpl = MissileSkill();
        int n = tpl.CalcAbilityValue(1, 0, SkillTemplate.MtypeEfc, 1);
        var (h, a, ca, _, _) = await Setup(tpl);
        h.Service.CombatRng = new FixedRandom(2);                // up to 2 missiles on each target

        await h.Service.DispatchClientAsync(a, LoopReq(Missiles, (T1, true), (T2, true)));

        var hits = Ack(ca).Targets;
        Assert.Equal(n, hits.Length);
        Assert.Equal(new[] { T1, T1 }, hits.Take(2).ToArray());
    }

    // ================================ the world's prize mail ================================

    [Fact]
    public async Task AnEventPrize_IsMailedByTheOperator_AndTheWinnerTold()
    {
        var t = new TemplateStore();
        t.Items[Prize] = new ItemTemplate(Prize, 0, new float[4], DuraMax: 50);
        var h = new MapTestHarness(t);
        var db = new FakePostStore();
        h.Service.PostStore = db;
        h.Service.UnixNow = () => 1_000_000;
        var (_, c) = await h.EnterAsync(1, 1, 1, x: 100, z: 100, name: "Ann");
        c.Clear();

        var w = new PacketWriter(Msg.MW_WORLDPOSTSEND_REQ);
        w.WriteByte(4 /* WPT_LOTITEM */); w.WriteUInt32(1); w.WriteString("Ann"); w.WriteString("You won"); w.WriteString("Congrats");
        w.WriteUInt16(Prize); w.WriteByte(2); w.WriteUInt16(24);
        await h.Service.DispatchWorldAsync(w.ToArray());

        var mail = Assert.Single(db.Mails);
        Assert.Equal(("Operator", "You won", (byte)1 /* POST_PACKAGE */), (mail.Sender, mail.Title, mail.Type));
        var item = Assert.Single(mail.Items);
        Assert.Equal((Prize, (byte)2, 1_000_000 + 24 * 3600L), (item.TemplateId, item.Count, item.EndTime));
        Assert.True(c.Has(Msg.CS_POSTRECV_ACK));
    }
}
