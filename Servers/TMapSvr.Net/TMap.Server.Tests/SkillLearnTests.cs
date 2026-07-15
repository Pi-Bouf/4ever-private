using TMap.Data;
using TMap.Protocol;
using TMap.Server.Map;
using Xunit;

namespace TMap.Server.Tests;

/// <summary>
/// Phase 36 — the skill-<b>learn</b> path (<c>CTObjBase::UpdateSkill</c>) driving the quest <b>GiveSkill</b>
/// subtype (<c>CQuestGiveSkill</c>). Learning is add-only: a runnable GiveSkill quest whose <c>QTT_SKILLID</c>
/// term names a chart skill grants it (level = term count, floored at 1) and pushes <c>CS_SKILLBUY_ACK</c>,
/// gated by the skill's class mask; an already-known skill is a no-op that blocks the child chain. All DB-free.
/// </summary>
public class SkillLearnTests
{
    private const byte QttSkillId = 4, TtTalkNpc = 3, TtExecQuest = 1;
    private const ushort Giver = 500, SkillId = 900;
    private const uint AllClasses = 0xFFFFFFFF;

    // A bare learnable skill template with a given class mask (BITSHIFTID: class N ⇒ bit 1<<N).
    private static SkillTemplate Sk(ushort id, uint classId)
        => new(id, Kind: 0, UseMp: 0, UseMpType: 0, UseHp: 0, UseHpType: 0, StartLevel: 1, MaxLevel: 10, NextLevel: 1,
            ReuseDelay: 0, ReuseDelayInc: 0, LoopDelay: 0, KindDelay: 0, SpeedApply: 0, Positive: 1, MapId: 0,
            ClassId: classId);

    private static TemplateStore StoreWith(SkillTemplate skill)
    {
        var t = new TemplateStore();
        t.Skills[skill.Id] = skill;
        return t;
    }

    // A runnable GiveSkill quest (TT_TALKNPC) whose QTT_SKILLID term names `skillId` at `level`.
    private static void AddGiveSkillQuest(TemplateStore t, uint questId, ushort skillId, byte level)
    {
        var q = new QuestTemplate { QuestId = questId, Type = (byte)QuestType.GiveSkill, TriggerType = TtTalkNpc, TriggerId = Giver };
        q.Terms.Add(new QuestTerm(skillId, QttSkillId, level));
        t.Quests[questId] = q;
    }

    // A child ChapterMsg quest fired under TT_EXECQUEST by `parentId` — observable via CS_CHAPTERMSG_ACK.
    private static void AddChildChapterMsg(TemplateStore t, uint childId, uint parentId)
        => t.Quests[childId] = new QuestTemplate
        { QuestId = childId, Type = (byte)QuestType.ChapterMsg, TriggerType = TtExecQuest, TriggerId = parentId };

    private static Character Hero() => new()
    { CharId = 1, Name = "Hero", Class = 0, Country = 1, Level = 5, MaxHp = 100, Hp = 100,
      Gold = 5, Silver = 6, Cooper = 7, Invens = { new Inven { InvenId = 0xFF } } };

    private static async Task<(MapTestHarness h, ClientSession s, FakeClientChannel c, Character ch)> Enter(
        TemplateStore store, Character ch)
    {
        var h = new MapTestHarness(store);
        var (s, c) = await h.EnterAsync(1, 1, 1, x: 100, z: 100, preSeeded: ch);
        s.Char!.Class = 0; s.Char.Country = 1; s.Char.Level = 5; s.Char.MapId = 0;
        h.Service.AddNpc(new Npc { Id = Giver, Type = 2, Country = 3, MapId = 0 });
        h.Service.InitQuests();
        c.Clear();
        return (h, s, c, s.Char);
    }

    // ==================== learn ====================

    [Fact]
    public async Task GiveSkill_LearnsTheSkill_AndAcks()
    {
        var store = StoreWith(Sk(SkillId, AllClasses));
        AddGiveSkillQuest(store, 8400, SkillId, level: 1);
        var (h, s, c, ch) = await Enter(store, Hero());

        await h.Service.DispatchClientAsync(s, MapTestHarness.QuestExecReq(8400));

        Assert.Contains(ch.Skills, k => k.SkillId == SkillId && k.Level == 1);
        Assert.True(c.Has(Msg.CS_SKILLBUY_ACK));
    }

    [Fact]
    public async Task GiveSkill_LevelComesFromTermCount()
    {
        var store = StoreWith(Sk(SkillId, AllClasses));
        AddGiveSkillQuest(store, 8400, SkillId, level: 3);
        var (h, s, _, ch) = await Enter(store, Hero());

        await h.Service.DispatchClientAsync(s, MapTestHarness.QuestExecReq(8400));

        Assert.Equal(3, ch.Skills.Single(k => k.SkillId == SkillId).Level);
    }

    [Fact]
    public async Task GiveSkill_ZeroCount_FlooredToLevelOne()
    {
        var store = StoreWith(Sk(SkillId, AllClasses));
        AddGiveSkillQuest(store, 8400, SkillId, level: 0);          // C++ max(bLevel, 1)
        var (h, s, _, ch) = await Enter(store, Hero());

        await h.Service.DispatchClientAsync(s, MapTestHarness.QuestExecReq(8400));

        Assert.Equal(1, ch.Skills.Single(k => k.SkillId == SkillId).Level);
    }

    // ==================== gates ====================

    [Fact]
    public async Task GiveSkill_ClassMismatch_DoesNotLearn()
    {
        var store = StoreWith(Sk(SkillId, classId: 2));            // bit 1 only ⇒ class 1, not class 0
        AddGiveSkillQuest(store, 8400, SkillId, level: 1);
        var (h, s, c, ch) = await Enter(store, Hero());            // Hero is class 0

        await h.Service.DispatchClientAsync(s, MapTestHarness.QuestExecReq(8400));

        Assert.DoesNotContain(ch.Skills, k => k.SkillId == SkillId);
        Assert.False(c.Has(Msg.CS_SKILLBUY_ACK));
    }

    [Fact]
    public async Task GiveSkill_UnknownSkill_DoesNotLearn()
    {
        var store = StoreWith(Sk(SkillId, AllClasses));
        AddGiveSkillQuest(store, 8400, skillId: 4321, level: 1);   // 4321 not in the chart
        var (h, s, c, ch) = await Enter(store, Hero());

        await h.Service.DispatchClientAsync(s, MapTestHarness.QuestExecReq(8400));

        Assert.Empty(ch.Skills);
        Assert.False(c.Has(Msg.CS_SKILLBUY_ACK));
    }

    // ==================== add-only + child recursion ====================

    [Fact]
    public async Task GiveSkill_NewlyLearned_RecursesChildren()
    {
        var store = StoreWith(Sk(SkillId, AllClasses));
        AddGiveSkillQuest(store, 8400, SkillId, level: 1);
        AddChildChapterMsg(store, childId: 8401, parentId: 8400);
        var (h, s, c, ch) = await Enter(store, Hero());

        await h.Service.DispatchClientAsync(s, MapTestHarness.QuestExecReq(8400));

        Assert.Contains(ch.Skills, k => k.SkillId == SkillId);
        Assert.True(c.Has(Msg.CS_CHAPTERMSG_ACK));                 // child ran (UpdateSkill returned TRUE)
    }

    [Fact]
    public async Task GiveSkill_AlreadyKnown_NoOp_AndBlocksChildChain()
    {
        var skill = Sk(SkillId, AllClasses);
        var store = StoreWith(skill);
        AddGiveSkillQuest(store, 8400, SkillId, level: 1);
        AddChildChapterMsg(store, childId: 8401, parentId: 8400);
        var ch = Hero();
        ch.Skills.Add(new Skill { SkillId = SkillId, Level = 4, Template = skill });   // already known (level 4)
        var (h, s, c, _) = await Enter(store, ch);

        await h.Service.DispatchClientAsync(s, MapTestHarness.QuestExecReq(8400));

        Assert.Single(ch.Skills, k => k.SkillId == SkillId);        // not duplicated
        Assert.Equal(4, ch.Skills.Single(k => k.SkillId == SkillId).Level);  // level untouched (no level-up)
        Assert.False(c.Has(Msg.CS_SKILLBUY_ACK));                   // add-only ⇒ no ack
        Assert.False(c.Has(Msg.CS_CHAPTERMSG_ACK));                 // child chain blocked (UpdateSkill returned FALSE)
    }

    // ==================== ack wire layout ====================

    [Fact]
    public async Task SkillBuyAck_WireLayout_IsByteExact()
    {
        var store = StoreWith(Sk(SkillId, AllClasses));
        AddGiveSkillQuest(store, 8400, SkillId, level: 2);
        var (h, s, c, _) = await Enter(store, Hero());

        await h.Service.DispatchClientAsync(s, MapTestHarness.QuestExecReq(8400));

        var r = new PacketReader(c.Last(Msg.CS_SKILLBUY_ACK)!);
        Assert.Equal(0, r.ReadByte());              // bRet = SKILL_SUCCESS
        Assert.Equal(SkillId, r.ReadUInt16());      // wSkillID
        Assert.Equal(2, r.ReadByte());              // bLevel
        Assert.Equal(0u, r.ReadUInt32());           // Tick (fresh grant)
        Assert.Equal(5u, r.ReadUInt32());           // dwGold
        Assert.Equal(6u, r.ReadUInt32());           // dwSilver
        Assert.Equal(7u, r.ReadUInt32());           // dwCooper
        Assert.Equal(0, r.ReadUInt16());            // wSkillPoint (SP unmodelled)
        Assert.Equal(0, r.ReadUInt16());            // kind[0]
        Assert.Equal(0, r.ReadUInt16());            // kind[1]
        Assert.Equal(0, r.ReadUInt16());            // kind[2]
        Assert.Equal(0, r.ReadUInt16());            // kind[3]
    }
}
