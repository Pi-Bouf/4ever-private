using TMap.Data;
using TMap.Protocol;
using TMap.Server.Map;
using Xunit;

namespace TMap.Server.Tests;

/// <summary>Phase 30 — quest persistence load-on-enter + the enter-time running-quest list. Phase 25 saved
/// quest progress (TSaveQuest/TSaveQuestTerm) but nothing reloaded it; this rebuilds <c>m_mapQUEST</c> from the
/// saved rows on enter and sends <c>CS_QUESTLIST_ACK</c> so the client shows its quest log on login. The
/// rebuild (<see cref="MapService.LoadQuestProgress"/>) + the list build are DB-free-testable; the actual
/// <c>TQUESTTABLE</c>/<c>TQUESTTERMTABLE</c> reads run against a live DB.</summary>
public class QuestLoadTests
{
    private const byte QttHunt = 3;
    private const ushort MonChart = 700;

    private static MapService Svc(params QuestTemplate[] quests)
    {
        var store = new TemplateStore();
        foreach (var q in quests) store.Quests[q.QuestId] = q;
        return new MapTestHarness(store).Service;
    }

    private static QuestTemplate Quest(uint id, params (uint id, byte type, byte count)[] terms)
    {
        var q = new QuestTemplate { QuestId = id, Type = (byte)QuestType.Mission, TriggerType = 3, TriggerId = 1 };
        foreach (var t in terms) q.Terms.Add(new QuestTerm(t.id, t.type, t.count));
        return q;
    }

    [Fact]
    public void LoadQuestProgress_RebuildsRunningQuestAndTerms()
    {
        var svc = Svc(Quest(1000, ((uint)MonChart, QttHunt, (byte)3)));
        var ch = new Character { CharId = 1 };

        svc.LoadQuestProgress(ch,
            new[] { new QuestLoadRow(1000, 0, 0, 1) },
            new[] { new QuestTermLoadRow(1000, MonChart, QttHunt, 2) });

        var qp = ch.FindQuest(1000);
        Assert.NotNull(qp);
        Assert.True(qp!.IsRunning);                 // complete 0 < trigger 1
        Assert.Equal((byte)1, qp.TriggerCount);
        var rt = Assert.Single(qp.RunningTerms);
        Assert.Equal((uint)MonChart, rt.TermId);
        Assert.Equal((byte)2, rt.Count);            // 2 of 3 killed, restored
    }

    [Fact]
    public void LoadQuestProgress_SkipsUnknownTemplate()
    {
        var svc = Svc();  // empty store
        var ch = new Character { CharId = 1 };
        svc.LoadQuestProgress(ch, new[] { new QuestLoadRow(9999, 0, 0, 1) }, System.Array.Empty<QuestTermLoadRow>());
        Assert.Empty(ch.Quests);
    }

    [Fact]
    public void LoadQuestProgress_SkipsType0Template()   // C++ `if(pQuestTemp && m_bType)` drops QT_NONE
    {
        var store = new TemplateStore();
        store.Quests[500] = new QuestTemplate { QuestId = 500, Type = 0 /*QT_NONE*/, TriggerType = 3, TriggerId = 1 };
        var svc = new MapTestHarness(store).Service;
        var ch = new Character { CharId = 1 };
        svc.LoadQuestProgress(ch, new[] { new QuestLoadRow(500, 0, 0, 1) }, System.Array.Empty<QuestTermLoadRow>());
        Assert.Empty(ch.Quests);
    }

    [Fact]
    public void LoadQuestProgress_TimerQuest_IsFlaggedDirty()   // C++ re-sets m_bSave when the timer is active
    {
        var svc = Svc(Quest(1000));
        var ch = new Character { CharId = 1 };
        svc.LoadQuestProgress(ch, new[] { new QuestLoadRow(1000, 5000, 0, 1) }, System.Array.Empty<QuestTermLoadRow>());
        Assert.True(ch.FindQuest(1000)!.Save);
    }

    [Fact]
    public void LoadQuestProgress_RestoresRemainingTimer()
    {
        var svc = Svc(Quest(1000));
        svc.NowMs = 10_000;
        var ch = new Character { CharId = 1 };
        svc.LoadQuestProgress(ch, new[] { new QuestLoadRow(1000, 5000, 0, 1) }, System.Array.Empty<QuestTermLoadRow>());
        var qp = ch.FindQuest(1000)!;
        Assert.Equal(5000u, qp.TimerTick);          // remaining tick restored
        Assert.Equal(10_000u, qp.BeginTick);        // timer resumes from now
    }

    [Fact]
    public async Task Enter_SendsRunningQuestList()
    {
        var store = new TemplateStore();
        var tmpl = Quest(1000, ((uint)MonChart, QttHunt, (byte)3));
        store.Quests[1000] = tmpl;
        var h = new MapTestHarness(store);
        var ch = new Character { CharId = 1, Name = "Hero", Level = 5, MaxHp = 100, Hp = 100 };
        ch.Invens.Add(new Inven { InvenId = 0xFF });
        var qp = new QuestProgress { Template = tmpl, TriggerCount = 1, CompleteCount = 0 };
        qp.RunningTerms.Add(new RunningTerm { TermId = MonChart, TermType = QttHunt, Count = 1 });
        ch.Quests[1000] = qp;

        var (_, c) = await h.EnterAsync(1, 1, 1, preSeeded: ch);   // QUESTLIST is sent during MW_CHARINFO

        var r = new PacketReader(c.Last(Msg.CS_QUESTLIST_ACK)!);
        Assert.Equal((byte)1, r.ReadByte());            // one running quest
        Assert.Equal(1000u, r.ReadUInt32());            // dwQuestID
        r.ReadByte();                                    // bType
        r.ReadByte();                                    // bCountMax
        Assert.Equal((byte)1, r.ReadByte());            // one term
        Assert.Equal((uint)MonChart, r.ReadUInt32());   // dwTermID
        Assert.Equal(QttHunt, r.ReadByte());            // bTermType
        Assert.Equal((byte)3, r.ReadByte());            // bNeedCount
        Assert.Equal((byte)1, r.ReadByte());            // bCurrentCount (the restored running count)
        r.ReadByte();                                    // bStatus
    }
}
