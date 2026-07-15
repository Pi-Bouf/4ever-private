using TMap.Data;
using TMap.Server.Map;
using Xunit;

namespace TMap.Server.Tests;

/// <summary>Phase 25 — character persistence: the DB-free-testable decision logic. The save GATE + 30-min
/// throttle (<see cref="MapService.IsSaveDue"/>), the char-record snapshot (<see cref="MapService.BuildCharSave"/>
/// — mutable fields + the round-trip-only columns), and the dirty-quest collection
/// (<see cref="MapService.BuildQuestSaves"/> — only <c>Save</c>-flagged quests, flag cleared after). The actual
/// SQL writes run only against a live DB (guarded by config) and are exercised by a live-DB smoke test.</summary>
public class PersistTests
{
    private static MapService Svc() => new MapTestHarness().Service; // gameDb null — IsSaveDue ignores it

    private static ClientSession Session(bool main = true, bool dbLoaded = true, uint lastSaveMs = 0,
        EnterState state = EnterState.InGame)
    {
        var s = new ClientSession(new FakeClientChannel()) { State = state, IsMain = main, CharId = 1 };
        s.Char = new Character { CharId = 1, DbLoaded = dbLoaded, LastSaveMs = lastSaveMs };
        return s;
    }

    [Fact]
    public void IsSaveDue_MainDbLoadedPastInterval_True()
    {
        var s = Session(lastSaveMs: 1000);
        Assert.True(Svc().IsSaveDue(s, 1000 + MapService.SaveIntervalMs));  // exactly one interval later
    }

    [Fact]
    public void IsSaveDue_WithinInterval_False()
    {
        var s = Session(lastSaveMs: 1000);
        Assert.False(Svc().IsSaveDue(s, 1000 + MapService.SaveIntervalMs - 1));
    }

    [Fact]
    public void IsSaveDue_SynthesizedChar_False()
        => Assert.False(Svc().IsSaveDue(Session(dbLoaded: false), MapService.SaveIntervalMs * 10));

    [Fact]
    public void IsSaveDue_NotMain_False()
        => Assert.False(Svc().IsSaveDue(Session(main: false), MapService.SaveIntervalMs * 10));

    [Fact]
    public void IsSaveDue_NotInGame_False()
        => Assert.False(Svc().IsSaveDue(Session(state: EnterState.Granted), MapService.SaveIntervalMs * 10));

    [Fact]
    public void BuildCharSave_CapturesMutable_AndRoundTripColumns()
    {
        var ch = new Character
        {
            CharId = 42, Level = 7, Gold = 1, Silver = 2, Cooper = 3, Exp = 12345, SkillPoint = 9,
            Hp = 80, Mp = 40, RegionId = 5, MapId = 3, PosX = 100f, PosY = 1f, PosZ = 200f, Dir = 90,
            StartAct = 1, HelmetHide = 1,
        };
        ch.Persist.SpawnId = 11; ch.Persist.LastSpawnId = 12; ch.Persist.LastDestination = 13;
        ch.Persist.StatLevel = 4; ch.Persist.StatPoint = 5; ch.Persist.StatExp = 999;
        ch.Persist.GuildLeave = 1; ch.Persist.GuildLeaveTime = 777; ch.Persist.TemptedMon = 8; ch.Persist.Aftermath = 2;

        var d = MapService.BuildCharSave(ch);

        Assert.Equal(42u, d.CharId);
        Assert.Equal((byte)7, d.Level);
        Assert.Equal(1u, d.Gold); Assert.Equal(2u, d.Silver); Assert.Equal(3u, d.Cooper);
        Assert.Equal(12345u, d.Exp); Assert.Equal((ushort)9, d.SkillPoint);
        Assert.Equal(80u, d.Hp); Assert.Equal(40u, d.Mp);
        Assert.Equal((ushort)3, d.MapId); Assert.Equal(100f, d.PosX); Assert.Equal(200f, d.PosZ);
        // round-trip-only columns preserved
        Assert.Equal((ushort)11, d.SpawnId); Assert.Equal(13u, d.LastDestination);
        Assert.Equal((byte)4, d.StatLevel); Assert.Equal(999u, d.StatExp);
        Assert.Equal((byte)1, d.GuildLeave); Assert.Equal(777u, d.GuildLeaveTime);
        // pc-bang columns zeroed (subsystem unported)
        Assert.Equal(0u, d.PcBangTime); Assert.Equal((byte)0, d.PcBangItemCnt);
    }

    private static QuestProgress Quest(uint id, bool save, params (uint termId, byte termType, byte count)[] terms)
    {
        var qp = new QuestProgress { Template = new QuestTemplate { QuestId = id, Type = 8 }, TriggerCount = 1, Save = save };
        foreach (var t in terms) qp.RunningTerms.Add(new RunningTerm { TermId = t.termId, TermType = t.termType, Count = t.count });
        return qp;
    }

    [Fact]
    public void BuildQuestSaves_CollectsFlagged_ClearsFlag_IncludesTerms()
    {
        var ch = new Character { CharId = 1 };
        ch.Quests[100] = Quest(100, save: true, (700, 3, 2));   // dirty — a HUNT term at 2
        ch.Quests[200] = Quest(200, save: false, (800, 3, 1));  // clean — must be skipped

        var rows = MapService.BuildQuestSaves(ch, nowMs: 0);

        Assert.Single(rows);
        var r = rows[0];
        Assert.Equal(100u, r.QuestId);
        Assert.Equal((byte)1, r.TriggerCount);
        Assert.Single(r.Terms);
        Assert.Equal(700u, r.Terms[0].TermId);
        Assert.Equal((byte)2, r.Terms[0].Count);
        Assert.False(ch.Quests[100].Save);   // flag cleared after packing (C++ resets m_bSave)
        Assert.False(ch.Quests[200].Save);
    }

    [Fact]
    public void BuildQuestSaves_NoneDirty_Empty()
    {
        var ch = new Character { CharId = 1 };
        ch.Quests[100] = Quest(100, save: false, (700, 3, 1));
        Assert.Empty(MapService.BuildQuestSaves(ch, nowMs: 0));
    }

    // ---- Phase 26: inventory snapshot ----

    [Fact]
    public void BuildInvenSaves_SnapshotsContainersAndItems()
    {
        var ch = new Character { CharId = 1 };
        var bag = new Inven { InvenId = 0xFF, Eld = 2 };
        bag.Items.Add(new Item { DlId = 555, ItemSlot = 3, TemplateId = 200, Count = 5, DuraMax = 100, DuraCur = 90 });
        ch.Invens.Add(bag);

        var (invens, items) = Svc().BuildInvenSaves(ch);

        Assert.Single(invens);
        Assert.Equal((byte)0xFF, invens[0].InvenId);
        Assert.Equal((byte)2, invens[0].Eld);
        Assert.Single(items);
        Assert.Equal(555L, items[0].DlId);            // loaded id preserved (delete-then-reinsert with same PK)
        Assert.Equal((byte)0, items[0].StorageType);  // STORAGE_INVEN
        Assert.Equal(0xFFu, items[0].StorageId);      // its container
        Assert.Equal((ushort)200, items[0].TemplateId);
        Assert.Equal((byte)5, items[0].Count);
        Assert.Equal(90u, items[0].DuraCur);
    }

    [Fact]
    public void BuildInvenSaves_StampsNewItemDlId()
    {
        var ch = new Character { CharId = 1 };
        var bag = new Inven { InvenId = 0xFF };
        var fresh = new Item { DlId = 0, ItemSlot = 0, TemplateId = 200, Count = 1 }; // in-session item, no id yet
        bag.Items.Add(fresh);
        ch.Invens.Add(bag);

        var (_, items) = Svc().BuildInvenSaves(ch);

        Assert.NotEqual(0L, items[0].DlId);       // a fresh unique id was minted
        Assert.Equal(fresh.DlId, items[0].DlId);  // …and written back onto the item so later saves reuse it
    }

    [Fact]
    public void BuildInvenSaves_PacksMagicSlots()
    {
        var ch = new Character { CharId = 1 };
        var bag = new Inven { InvenId = 0xFF };
        var item = new Item { DlId = 1, TemplateId = 200, Count = 1 };
        item.Magic.Add(new MagicOption(7, 50));
        item.Magic.Add(new MagicOption(8, 25));
        bag.Items.Add(item);
        ch.Invens.Add(bag);

        var (_, items) = Svc().BuildInvenSaves(ch);

        Assert.Equal((byte)7, items[0].Magic[0]); Assert.Equal((ushort)50, items[0].Value[0]);
        Assert.Equal((byte)8, items[0].Magic[1]); Assert.Equal((ushort)25, items[0].Value[1]);
        Assert.Equal((byte)0, items[0].Magic[2]); // unused slots zero
    }

    [Fact]
    public void GenItemId_Increments()
    {
        var svc = Svc();
        Assert.Equal(svc.GenItemId() + 1, svc.GenItemId()); // strictly increasing
    }
}
