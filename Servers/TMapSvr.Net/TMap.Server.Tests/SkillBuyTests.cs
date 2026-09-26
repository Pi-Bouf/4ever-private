using TMap.Data;
using TMap.Protocol;
using TMap.Server.Map;
using Xunit;

namespace TMap.Server.Tests;

/// <summary>
/// Learning skills (C++ OnCS_SKILLBUY_REQ, CSHandler.cpp:2170 — identical in Source 3.3, OLD SOURCES and 5.0), the
/// skill trainer's list (SendCS_NPCITEMLIST_ACK), reset scrolls (OnCS_SKILLINIT_REQ / InitializeSkill /
/// OnCS_SKILLINITPOSSIBLE_REQ) and the skill save. The fixture is a sorcerer at the skill window's virtual trainer
/// (TDEF_SKILL_NPC 22047) with a Dark-Ritual-like skill it holds at level 0 (a TSTARTSKILL row).
/// </summary>
public class SkillBuyTests
{
    private const ushort Trainer = 22047;                 // TDEF_SKILL_NPC
    private const ushort Ritual = 623, Missile = 600, Storm = 608, Child = 650, Warrior = 1, Given = 34;
    private const byte Sorcerer = 5, OtPc = 1;
    private const ushort OneReset = 900, AllReset = 901;

    private static SkillTemplate Skill(ushort id, byte kind, byte start, byte next, byte max, uint classMask,
        ushort parent = 0, params (byte level, byte sp, byte group, byte parentLevel, uint payback)[] points)
    {
        var t = new SkillTemplate(id, kind, 0, 0, 0, 0, start, max, next, 0, 0, 0, 0, 0, 1, 0xFFFF,
            ClassId: classMask, Price: 2f, ParentSkillId: parent);
        foreach (var p in points) t.Points[p.level] = new SkillPointRow(p.sp, p.group, p.parentLevel, p.payback);
        return t;
    }

    private static TemplateStore Store()
    {
        var t = new TemplateStore();
        const uint sorc = 1u << Sorcerer;
        // A class skill held at 0: learned at 24, then +4 levels per rank; 4 points a level, the 2nd needs 4 spent in its tab.
        t.Skills[Ritual] = Skill(Ritual, 17, 24, 4, 3, sorc, 0, (1, 4, 0, 1, 1000), (2, 4, 4, 1, 5000), (3, 4, 8, 1, 9000));
        t.Skills[Missile] = Skill(Missile, 16, 1, 5, 9, sorc, 0, (1, 1, 0, 1, 100), (2, 1, 0, 1, 200));
        t.Skills[Storm] = Skill(Storm, 16, 26, 18, 5, sorc, 0, (1, 1, 6, 1, 700));      // needs 6 already spent in tab 1
        t.Skills[Child] = Skill(Child, 17, 20, 5, 3, sorc, Ritual, (1, 2, 0, 2, 300));   // needs the Ritual at 2
        t.Skills[Warrior] = Skill(Warrior, 1, 1, 1, 5, 1u << 0, 0, (1, 1, 0, 1, 0));
        t.Skills[Given] = Skill(Given, 16, 0, 1, 5, sorc, 0, (1, 0, 0, 1, 0), (2, 1, 0, 1, 50));  // start level 0: given
        foreach (var lvl in new[] { 1, 6, 24, 26, 28, 32 }) t.LevelMoney[lvl] = 1000;   // price = 1000 × 2.0
        t.Items[OneReset] = new ItemTemplate(OneReset, 0, new[] { 0f, 0f, 0f, 0f }, Type: 7, Kind: 36, UseValue: 60, Stack: 5);
        t.Items[AllReset] = new ItemTemplate(AllReset, 0, new[] { 0f, 0f, 0f, 0f }, Type: 7, Kind: 37, Stack: 5);
        return t;
    }

    private static TemplateStore _t = Store();

    private static async Task<(MapTestHarness h, ClientSession s, FakeClientChannel c, Character ch)> Setup(
        byte level = 29, uint points = 20, long money = 1_000_000, byte ritualLevel = 0)
    {
        var t = _t = Store();
        var h = new MapTestHarness(t);
        var ch = new Character { CharId = 1, Name = "Evocatoooor", MaxHp = 100, Hp = 100 };
        ch.Invens.Add(new Inven { InvenId = 0xFF });
        ch.Skills.Add(new Map.Skill { SkillId = Ritual, Level = ritualLevel, Template = t.Skills[Ritual] });
        ch.Skills.Add(new Map.Skill { SkillId = Given, Level = 1, Template = t.Skills[Given] });
        var (s, c) = await h.EnterAsync(1, 1, 1, preSeeded: ch);
        ch.Level = level; ch.Class = Sorcerer; ch.Country = 1; ch.AidCountry = 3; ch.SkillPoint = points;
        ch.SetMoneyTotal(money);
        var npc = new Npc { Id = Trainer, Type = 1, Country = 3 };                          // TNPC_SKILL_MASTER, neutral
        foreach (var id in new[] { Ritual, Missile, Storm, Child, Warrior }) npc.Skills[id] = t.Skills[id];
        h.Service.AddNpc(npc);
        c.Clear();
        return (h, s, c, ch);
    }

    private static byte[] Buy(ushort skillId, ushort npcId = Trainer)
    {
        var w = new PacketWriter(Msg.CS_SKILLBUY_REQ); w.WriteUInt16(npcId); w.WriteUInt16(skillId);
        return w.ToArray();
    }

    private static (SkillUseResult Ret, ushort Skill, byte Level, uint Points, ushort[] Kinds) ReadBuy(byte[] raw)
    {
        var r = new PacketReader(raw);
        var ret = (SkillUseResult)r.ReadByte(); ushort id = r.ReadUInt16(); byte lvl = r.ReadByte();
        r.ReadUInt32(); r.ReadUInt32(); r.ReadUInt32(); r.ReadUInt32();                  // Tick, gold, silver, copper
        uint sp = r.ReadUInt16();
        var kinds = new[] { r.ReadUInt16(), r.ReadUInt16(), r.ReadUInt16(), r.ReadUInt16() };
        return (ret, id, lvl, sp, kinds);
    }

    private static Map.Skill Held(Character ch, ushort id) => ch.Skills.Single(k => k.SkillId == id);

    // ================================ raising a held skill ================================

    [Fact]
    public async Task ALevelZeroSkill_IsLearnedToLevel1_ForItsPointsAndGold()
    {
        var (h, s, c, ch) = await Setup();

        await h.Service.DispatchClientAsync(s, Buy(Ritual));

        Assert.Equal((byte)1, Held(ch, Ritual).Level);
        Assert.Equal(16u, ch.SkillPoint);                                   // 20 − 4
        Assert.Equal(1_000_000 - 2000, ch.MoneyTotal);                      // level 24's 1000 × fPrice 2.0
        var ack = ReadBuy(c.Last(Msg.CS_SKILLBUY_ACK)!);
        Assert.Equal((SkillUseResult.Success, Ritual, (byte)1, 16u), (ack.Ret, ack.Skill, ack.Level, ack.Points));
        Assert.Equal((ushort)4, ack.Kinds[2]);                              // kind 17 − the sorcerer's 15 = tab 2
    }

    [Fact]
    public async Task TheNextLevel_WaitsForTheCharacterLevel()
    {
        var (h, s, c, ch) = await Setup(level: 27, ritualLevel: 1);         // level 2 needs 24 + 1·4 = 28

        await h.Service.DispatchClientAsync(s, Buy(Ritual));

        Assert.Equal((SkillUseResult.NeedLevelUp, (byte)1), (ReadBuy(c.Last(Msg.CS_SKILLBUY_ACK)!).Ret, Held(ch, Ritual).Level));
    }

    [Fact]
    public async Task ASkillAtItsMaximum_IsAlreadyLearned()
    {
        var (h, s, c, ch) = await Setup(level: 60, ritualLevel: 3);

        await h.Service.DispatchClientAsync(s, Buy(Ritual));

        Assert.Equal(SkillUseResult.Already, ReadBuy(c.Last(Msg.CS_SKILLBUY_ACK)!).Ret);
    }

    [Fact]
    public async Task WithoutTheGold_NothingIsSpent()
    {
        var (h, s, c, ch) = await Setup(money: 1999);

        await h.Service.DispatchClientAsync(s, Buy(Ritual));

        Assert.Equal(SkillUseResult.NeedMoney, ReadBuy(c.Last(Msg.CS_SKILLBUY_ACK)!).Ret);
        Assert.Equal((byte)0, Held(ch, Ritual).Level);
        Assert.Equal((20u, 1999L), (ch.SkillPoint, ch.MoneyTotal));
    }

    [Fact]
    public async Task WithoutTheSkillPoints_NothingIsSpent()
    {
        var (h, s, c, ch) = await Setup(points: 3);

        await h.Service.DispatchClientAsync(s, Buy(Ritual));

        Assert.Equal(SkillUseResult.NeedSkillPoint, ReadBuy(c.Last(Msg.CS_SKILLBUY_ACK)!).Ret);
        Assert.Equal(1_000_000L, ch.MoneyTotal);
    }

    [Fact]
    public async Task ALevel_CanNeedPointsAlreadySpentInItsTab()
    {
        var (h, s, c, ch) = await Setup(level: 28, ritualLevel: 1);         // tab 2 has 4 spent; level 2 needs 4
        await h.Service.DispatchClientAsync(s, Buy(Ritual));
        Assert.Equal((byte)2, Held(ch, Ritual).Level);

        ch.Skills.Add(new Map.Skill { SkillId = Storm, Level = 0, Template = _t.Skills[Storm] });
        await h.Service.DispatchClientAsync(s, Buy(Storm));                 // the storm needs 6 spent in tab 1: 0 are
        Assert.Equal(SkillUseResult.NeedSkillPoint, ReadBuy(c.Last(Msg.CS_SKILLBUY_ACK)!).Ret);
    }

    // ================================ learning a new skill ================================

    [Fact]
    public async Task ANewSkill_IsLearnedFromTheTrainerAtLevel1()
    {
        var (h, s, c, ch) = await Setup();

        await h.Service.DispatchClientAsync(s, Buy(Missile));

        Assert.Equal((byte)1, Held(ch, Missile).Level);
        Assert.Equal(19u, ch.SkillPoint);
        Assert.Equal(1_000_000 - 2000, ch.MoneyTotal);                      // level 1's money (its start level)
        Assert.Equal((SkillUseResult.Success, (byte)1), (ReadBuy(c.Last(Msg.CS_SKILLBUY_ACK)!).Ret, ReadBuy(c.Last(Msg.CS_SKILLBUY_ACK)!).Level));
    }

    [Fact]
    public async Task AnotherClassesSkill_CannotBeLearned()
    {
        var (h, s, c, ch) = await Setup();
        await h.Service.DispatchClientAsync(s, Buy(Warrior));
        Assert.Equal(SkillUseResult.MatchClass, ReadBuy(c.Last(Msg.CS_SKILLBUY_ACK)!).Ret);
        Assert.DoesNotContain(ch.Skills, k => k.SkillId == Warrior);
    }

    [Fact]
    public async Task ASkillNeedsItsParentHighEnough()
    {
        var (h, s, c, ch) = await Setup(ritualLevel: 1);                     // the child needs the Ritual at 2

        await h.Service.DispatchClientAsync(s, Buy(Child));
        Assert.Equal(SkillUseResult.NeedParent, ReadBuy(c.Last(Msg.CS_SKILLBUY_ACK)!).Ret);

        Held(ch, Ritual).Level = 2;
        await h.Service.DispatchClientAsync(s, Buy(Child));
        Assert.Equal(SkillUseResult.Success, ReadBuy(c.Last(Msg.CS_SKILLBUY_ACK)!).Ret);
    }

    [Fact]
    public async Task ASkillTheTrainerDoesNotTeach_IsNotFound()
    {
        var (h, s, c, ch) = await Setup();
        await h.Service.DispatchClientAsync(s, Buy(777));                   // neither held nor taught
        Assert.Equal(SkillUseResult.NotFound, ReadBuy(c.Last(Msg.CS_SKILLBUY_ACK)!).Ret);
    }

    [Fact]
    public async Task NoLearningDuringATrade()
    {
        var (h, s, c, ch) = await Setup();
        s.Deal.Status = (byte)DealStatus.Start;

        await h.Service.DispatchClientAsync(s, Buy(Ritual));

        Assert.Equal(SkillUseResult.ActionLock, ReadBuy(c.Last(Msg.CS_SKILLBUY_ACK)!).Ret);
        Assert.Equal((byte)0, Held(ch, Ritual).Level);
    }

    [Fact]
    public async Task AnUnknownTrainer_TeachesNothing()
    {
        var (h, s, c, ch) = await Setup();
        await h.Service.DispatchClientAsync(s, Buy(Ritual, npcId: 1234));
        Assert.Equal(SkillUseResult.NotFound, ReadBuy(c.Last(Msg.CS_SKILLBUY_ACK)!).Ret);
    }

    // ================================ the trainer's list ================================

    [Fact]
    public async Task TheTrainersList_HasWhatCanBeLearnedNow_WithPrices()
    {
        var (h, s, c, ch) = await Setup();
        var w = new PacketWriter(Msg.CS_NPCITEMLIST_REQ); w.WriteUInt16(Trainer);

        await h.Service.DispatchClientAsync(s, w.ToArray());

        var r = new PacketReader(c.Last(Msg.CS_NPCITEMLIST_ACK)!);
        Assert.Equal(Trainer, r.ReadUInt16()); Assert.Equal((byte)1, r.ReadByte()); r.ReadByte();
        int n = r.ReadByte();
        var list = Enumerable.Range(0, n).Select(_ => (r.ReadUInt16(), r.ReadUInt32())).ToList();
        // The Ritual (held at 0, level 24 reached) and the Missile (new); not the Storm (no points spent in its tab) nor
        // the warrior skill.
        Assert.Contains((Ritual, 2000u), list);
        Assert.Contains((Missile, 2000u), list);
        Assert.DoesNotContain(list, x => x.Item1 == Warrior);
        Assert.DoesNotContain(list, x => x.Item1 == Storm);
    }

    // ================================ reset scrolls ================================

    private static byte[] Reset(ushort skillId, byte slot)
    {
        var w = new PacketWriter(Msg.CS_SKILLINIT_REQ); w.WriteUInt16(skillId); w.WriteByte(0xFF); w.WriteByte(slot);
        return w.ToArray();
    }

    private static void GiveScroll(MapTestHarness h, Character ch, ushort id, byte slot)
        => ch.FindInven(0xFF)!.Items.Add(new Item { ItemSlot = slot, TemplateId = id, Count = 1, Template = _t.Items[id] });

    [Fact]
    public async Task AOneSkillScroll_TakesALevelOff_AndGivesItsPointsBack()
    {
        var (h, s, c, ch) = await Setup(ritualLevel: 2, points: 0);
        GiveScroll(h, ch, OneReset, 3);

        await h.Service.DispatchClientAsync(s, Reset(Ritual, 3));

        Assert.Equal((byte)1, Held(ch, Ritual).Level);
        Assert.Equal(4u, ch.SkillPoint);
        Assert.Null(ch.FindInven(0xFF)!.FindItem(3));                        // the scroll is used up
        Assert.Equal((byte)SkillUseResult.Success, new PacketReader(c.Last(Msg.CS_SKILLINIT_ACK)!).ReadByte());
        Assert.True(c.Has(Msg.CS_SKILLLIST_ACK));
    }

    [Fact]
    public async Task ASkillBroughtToZero_LeavesTheCharacter_AndItsHotkeys()
    {
        var (h, s, c, ch) = await Setup(ritualLevel: 1);
        var page = new HotkeyPage { InvenKey = 0 };
        page.Slots[5] = new HotkeySlot(1, Ritual);
        ch.HotkeyPages.Add(page);
        GiveScroll(h, ch, OneReset, 3);

        await h.Service.DispatchClientAsync(s, Reset(Ritual, 3));

        Assert.DoesNotContain(ch.Skills, k => k.SkillId == Ritual);          // C++ InitializeSkill: m_mapTSKILL.erase
        Assert.Equal(new HotkeySlot(0, 0), page.Slots[5]);
    }

    [Fact]
    public async Task AGivenSkill_CannotBeReset()
    {
        var (h, s, c, ch) = await Setup();
        GiveScroll(h, ch, OneReset, 3);
        await h.Service.DispatchClientAsync(s, Reset(Given, 3));
        Assert.Equal((byte)SkillUseResult.NotInit, new PacketReader(c.Last(Msg.CS_SKILLINIT_ACK)!).ReadByte());
    }

    [Fact]
    public async Task AFullReset_RefundsPointsAndPayback_ButKeepsGivenSkills()
    {
        var (h, s, c, ch) = await Setup(ritualLevel: 2, points: 0, money: 0);
        Held(ch, Given).Level = 2;
        GiveScroll(h, ch, AllReset, 4);

        await h.Service.DispatchClientAsync(s, Reset(0, 4));

        Assert.DoesNotContain(ch.Skills, k => k.SkillId == Ritual);
        Assert.Equal((byte)1, Held(ch, Given).Level);                         // start level 0: stays at 1
        Assert.Equal(4u + 4u + 1u, ch.SkillPoint);                            // ritual 2+1, given 2
        Assert.Equal(5000L + 50L, ch.MoneyTotal);                             // each one's current level's dwPayback
    }

    [Fact]
    public async Task AScroll_ListsTheSkillsItCanReset()
    {
        var (h, s, c, ch) = await Setup(ritualLevel: 2);
        GiveScroll(h, ch, OneReset, 3);
        var w = new PacketWriter(Msg.CS_SKILLINITPOSSIBLE_REQ); w.WriteByte(0xFF); w.WriteByte(3);

        await h.Service.DispatchClientAsync(s, w.ToArray());

        var r = new PacketReader(c.Last(Msg.CS_SKILLINITPOSSIBLE_ACK)!);
        int n = r.ReadByte();
        var ids = Enumerable.Range(0, n).Select(_ => r.ReadUInt16()).ToList();
        Assert.Equal(new[] { Ritual }, ids);                                  // not the given skill (start 0, level 1)
    }

    // ================================ saving ================================

    [Fact]
    public void TheSave_WritesEverySkill_LevelZeroIncluded()
    {
        var ch = new Character { CharId = 1 };
        ch.Skills.Add(new Map.Skill { SkillId = Storm, Level = 0 });
        ch.Skills.Add(new Map.Skill { SkillId = Ritual, Level = 2 });

        var rows = MapService.BuildSkillSaves(ch, 0);

        Assert.Equal(new[] { new SkillSaveRow(Storm, 0, 0), new SkillSaveRow(Ritual, 2, 0) }, rows);   // by id, as the C++ map
    }
}
