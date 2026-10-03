using TMap.Data;
using TMap.Protocol;
using TMap.Server.Map;
using Xunit;

namespace TMap.Server.Tests;

/// <summary>
/// Summons across logout (C++ SendDM_SAVECHAR_REQ / OnDM_LOADCHAR_ACK): what the save keeps, and how the saved summons
/// come back at login with their buffs.
/// </summary>
public class RecallSaveTests
{
    private const ushort Ritual = 21100, Attr = 2001, Shield = 1200, Mount = 31026;
    private const byte Level = 10;

    private static MaintainSkill Buff(ushort id, uint start, uint tick)
    {
        var t = new SkillTemplate(id, 0, 0, 0, 0, 0, 1, 1, 1, 0, 0, 0, 0, 0, Positive: 1, MapId: 0xFFFF, Duration: 60_000);
        return new MaintainSkill { SkillId = id, Level = 2, Template = t, StartTick = start, MaintainTick = tick, AttackId = 1, HostId = 1 };
    }

    private static RecallMon Summon(uint id, ushort chart, byte type = 1, long recallTick = 0, uint duration = 0) => new()
    {
        Id = id, ChartId = chart, RecallType = type, Level = Level, Hp = 300, Mp = 40, AtkSkillLevel = 3, PosX = 120, PosZ = 130,
        RecallTickMs = recallTick, DurationMs = duration, Attr = new MonAttrRow(Attr, Level, 400, 100, 0),
    };

    private static CharSaveData Save(Character ch, uint now) => MapService.WithRecalls(MapService.BuildCharSave(ch), ch, now);

    // ================================ the save ================================

    [Fact]
    public void TheSave_KeepsTheLiveSummons_WithTheirLifeAndBuffsLeft()
    {
        var ch = new Character { CharId = 1 };
        var m = Summon(5, Ritual, recallTick: 1_000, duration: 10_000);
        m.MaintainSkills.Add(Buff(Shield, start: 2_000, tick: 5_000));                // 2 000 left at 5 000
        m.MaintainSkills.Add(Buff(Shield + 1, start: 0, tick: 0));                    // for good
        m.MaintainSkills.Add(Buff(Shield + 2, start: 1_000, tick: 1_000));            // over
        ch.Recalls[5] = m;

        var d = Save(ch, now: 5_000);

        var row = Assert.Single(d.Recalls!);
        Assert.Equal((5u, Ritual, (uint)(Attr | (Level << 16)), 6_000u, (short)120), (row.Id, row.MonId, row.Attr, row.Time, row.X));
        Assert.Equal(new[] { (Shield, 2_000u), ((ushort)(Shield + 1), 0u) },
            d.RecallMaintains!.Select(b => (b.SkillId, b.Remain)).ToArray());
    }

    [Fact]
    public void TheSave_LeavesOutAnExpiredSummon_AndTheEnslavedMonster()
    {
        var ch = new Character { CharId = 1, Class = 5 /* TCLASS_SORCERER */ };
        ch.Persist.TemptedMon = 500;
        ch.Recalls[5] = Summon(5, Ritual, recallTick: 0, duration: 1_000);           // life over
        ch.Recalls[6] = Summon(6, 500);                                               // the enslaved monster

        Assert.Empty(Save(ch, now: 5_000).Recalls!);
    }

    [Fact]
    public void ThePlayersOwnBuffs_AreSavedToo_ButNotTheStoringOne()
    {
        var ch = new Character { CharId = 1 };
        ch.MaintainSkills.Add(Buff(Shield, start: 2_000, tick: 5_000));               // 2 000 left
        ch.MaintainSkills.Add(Buff(804, start: 0, tick: 0));                          // TSTORE_SKILL
        ch.MaintainSkills.Add(Buff(Shield + 2, start: 1_000, tick: 1_000));           // over

        var d = Save(ch, now: 5_000);

        Assert.Equal(new[] { (Shield, 2_000u) }, d.Maintains!.Select(m => (m.SkillId, m.RemainTick)).ToArray());
    }

    [Fact]
    public void ASummonNotBackYet_IsSavedAsItWas()
    {
        var ch = new Character { CharId = 1 };
        var row = new RecallSaveRow(9, Ritual, 0, Attr, Level, 1, 1, 1, 0, 0, 0, 7_000, 0);
        ch.PendingRecalls.Add((row, new List<RecallMaintainRow> { new(9, Shield, 1, 500, 1, 1, 1, 1, 0) }));

        var d = Save(ch, now: 1);

        Assert.Same(row, Assert.Single(d.Recalls!));
        Assert.Single(d.RecallMaintains!);
    }

    [Fact]
    public void TheLoad_SkipsTheBaselinesEmptyRow_AndPairsTheBuffs()
    {
        var ch = new Character { CharId = 1 };
        MapService.LoadSavedRecalls(ch, (
            new List<RecallSaveRow> { new(0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0), new(4, Ritual, 0, Attr, Level, 1, 1, 1, 0, 0, 0, 0, 0) },
            new List<RecallMaintainRow> { new(4, Shield, 1, 0, 1, 1, 1, 1, 0), new(8, Shield, 1, 0, 1, 1, 1, 1, 0) }));

        var p = Assert.Single(ch.PendingRecalls);
        Assert.Equal(Ritual, p.Row.MonId);
        Assert.Single(p.Buffs);
    }

    // ================================ the login ================================

    private static TemplateStore Store()
    {
        var t = new TemplateStore { Rate1st = 1.0f };
        t.Classes[0] = new StatSeed(1, 1, 1, 1, 1, 1);
        t.Races[0] = new StatSeed(1, 1, 1, 1, 1, 1);
        t.MonsterTemplates[Ritual] = new MonsterTemplate(Ritual, 1, 0, RecallType: 1, SummonAttr: Attr, CanSelect: 1);
        t.MonsterTemplates[Mount] = new MonsterTemplate(Mount, 1, 0, RecallType: RecallMon.TypePet, SummonAttr: Attr);
        t.MonAttrs[TemplateStore.MonAttrKey(Attr, Level)] = new MonAttrRow(Attr, Level, 400, 100, 0);
        t.Skills[Shield] = new SkillTemplate(Shield, 0, 0, 0, 0, 0, 1, 1, 1, 0, 0, 0, 0, 0, Positive: 1, MapId: 0xFFFF, Duration: 60_000);
        return t;
    }

    private static async Task<(MapTestHarness h, ClientSession s, Character ch)> Login(params (RecallSaveRow, List<RecallMaintainRow>)[] saved)
    {
        var h = new MapTestHarness(Store());
        var ch = new Character { CharId = 1, Name = "Ann", Level = 20, MaxHp = 100, Hp = 100 };
        foreach (var p in saved) ch.PendingRecalls.Add(p);
        var (s, _) = await h.EnterAsync(1, 1, 1, x: 100, z: 100, name: "Ann", preSeeded: ch);
        return (h, s, ch);
    }

    [Fact]
    public async Task AtLogin_ASavedSummonIsAskedOfTheWorld_WithItsLifeLeft()
    {
        var (h, _, _) = await Login((new RecallSaveRow(4, Ritual, 0, (uint)(Attr | (Level << 16)), Level, 1, 1, 3, 0, 0, 0, 7_000, 0), new()));

        var r = new PacketReader(h.World.Last(Msg.MW_CREATERECALLMON_ACK)!);
        Assert.Equal(1u, r.ReadUInt32()); r.ReadUInt32();
        Assert.Equal(0u, r.ReadUInt32());                                              // the world gives it a new id
        Assert.Equal(Ritual, r.ReadUInt16());
    }

    [Fact]
    public async Task WhenTheWorldsCopyArrives_ItsBuffsGoBackOn()
    {
        var buffs = new List<RecallMaintainRow> { new(4, Shield, 2, 3_000, 1, 1, 1, 1, 0) };
        var (h, _, ch) = await Login((new RecallSaveRow(4, Ritual, 0, (uint)(Attr | (Level << 16)), Level, 1, 1, 3, 0, 0, 0, 7_000, 0), buffs));
        var raw = (byte[])h.World.Last(Msg.MW_CREATERECALLMON_ACK)!.Clone();
        PacketHeader.WriteId(raw, Msg.MW_CREATERECALLMON_REQ);
        BitConverter.GetBytes(77u).CopyTo(raw, PacketHeader.Size + 8);

        await h.Service.DispatchWorldAsync(raw);

        var buff = Assert.Single(ch.Recalls[77].MaintainSkills);
        Assert.Equal((Shield, (byte)2, 3_000u), (buff.SkillId, buff.Level, buff.MaintainTick));
        Assert.Empty(ch.PendingRecalls);
    }

    [Fact]
    public async Task AMountWithoutItsLicence_DoesNotComeBack()
    {
        var (h, _, ch) = await Login((new RecallSaveRow(4, Mount, 2, (uint)(Attr | (Level << 16)), Level, 1, 1, 1, 0, 0, 0, 0, 0), new()));

        Assert.False(h.World.Has(Msg.MW_CREATERECALLMON_ACK));
        Assert.Empty(ch.PendingRecalls);
    }
}
