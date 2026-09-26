using TMap.Data;
using TMap.Protocol;

namespace TMap.Server.Map;

/// <summary>
/// Learning skills with skill points and gold, and unlearning them with a reset scroll — the C# port of
/// <c>OnCS_SKILLBUY_REQ</c> (CSHandler.cpp:2170), the skill-master branch of <c>OnCS_NPCITEMLIST_REQ</c>,
/// <c>OnCS_SKILLINIT_REQ</c> / <c>OnCS_SKILLINITPOSSIBLE_REQ</c> and <c>CTPlayer::InitializeSkill</c>. The logic is the
/// same in all three sources (Source 3.3, OLD SOURCES and the 5.0 C++).
///
/// <para>The skill window's "learn" button asks the virtual trainer <c>TDEF_SKILL_NPC</c> (22047, the "Technology
/// tradesman", a <c>TNPC_SKILL_MASTER</c> placed nowhere) whose <c>TNPCITEMCHART</c> lists the skills it teaches.
/// A skill the character already holds — at any level, including the level-0 rows <c>TSTARTSKILL</c> gives every class
/// skill — goes up one level; one it does not hold is learned at level 1 if the trainer teaches it. Either way it costs
/// the level chart's money for the level it needs (× the skill's <c>fPrice</c>) and <c>TSKILLPOINTCHART</c>'s skill
/// points, and needs the character level, enough points already spent in the skill's tab and the parent skill.</para>
///
/// <para><b>Not ported:</b> the NPC discount (<c>GetDiscountRate</c>: guild / local-hero / castle-hero conditions —
/// territories are not modelled, so 0), the 5.0 secure-code lock, the passive "remain" registry (<c>RemainSkill</c>)
/// and the skill log (<c>SendDM_LOGSKILL_REQ</c>). The C++ reads <c>pNextLevel-&gt;m_dwMoney</c> before checking it for
/// null; a missing level row is answered <c>SKILL_ALREADY</c> here instead of crashing.</para>
/// </summary>
public sealed partial class MapService
{
    private const byte TnpcSkillMaster = 1, TnpcSkillRent = 19;   // TNPC_TYPE
    private const byte NormalEquip = 0;                            // SKILL kind NORMAL_EQUIP (equipment skills)
    private const byte IkSkillOneInit = 36, IkSkillAllInit = 37;    // TITEM_KIND reset scrolls

    // ================================ learning ================================

    /// <summary>C++ <c>OnCS_SKILLBUY_REQ</c> — <c>wNpcID · wSkillID</c>.</summary>
    private void OnCS_SKILLBUY_REQ(ClientSession s, PacketReader r)
    {
        ushort npcId = r.ReadUInt16();
        ushort skillId = r.ReadUInt16();
        if (s.State != EnterState.InGame || !s.IsMain || s.Char is not { } ch) return;
        if (IsTutorial(ch)) return;                                                     // ProtectTutorial

        if (s.Deal.Status != (byte)DealStatus.Ready) { SendCS_SKILLBUY_ACK(s, ch, SkillUseResult.ActionLock, skillId, 0); return; }
        if (_state.FindNpc(npcId) is not { } npc || !npc.CanTalk(ch.Country, ch.AidCountry, 0))
        { SendCS_SKILLBUY_ACK(s, ch, SkillUseResult.NotFound, skillId, 0); return; }
        const byte discount = 0;                                                        // GetDiscountRate — see the class remarks

        // A skill the character holds goes up one level.
        if (ch.Skills.FirstOrDefault(k => k.SkillId == skillId) is { Template: { } st } skill)
        {
            byte next = NextLearnLevel(skill);
            if (_templates.LevelMoneyOf(next) is not { } money || st.MaxLevel == skill.Level)
            { SendCS_SKILLBUY_ACK(s, ch, SkillUseResult.Already, skillId, skill.Level); return; }
            uint price = Discounted(st.GetPrice(money), discount);

            if (ch.Level < next) { SendCS_SKILLBUY_ACK(s, ch, SkillUseResult.NeedLevelUp, skillId, skill.Level); return; }
            if (!ch.UseMoney(price, false)) { SendCS_SKILLBUY_ACK(s, ch, SkillUseResult.NeedMoney, skillId, skill.Level); return; }
            if (!IsEnoughSkillPoint(ch, st)) { SendCS_SKILLBUY_ACK(s, ch, SkillUseResult.NeedSkillPoint, skillId, 0); return; }
            if (st.ParentSkillId != 0 && (ch.Skills.FirstOrDefault(k => k.SkillId == st.ParentSkillId) is not { } parent
                                          || !st.CheckParentSkill((byte)(skill.Level + 1), parent.Level)))
            { SendCS_SKILLBUY_ACK(s, ch, SkillUseResult.NeedParent, skillId, 0); return; }

            ch.UseMoney(price, true);
            ch.SkillPoint -= st.GetNeedSkillPoint((byte)(skill.Level + 1));              // GetNextSkillPoint
            skill.Level++;
            SendCS_SKILLBUY_ACK(s, ch, SkillUseResult.Success, st.Id, skill.Level);
            return;
        }

        // One it does not hold is learned from the trainer at level 1.
        if (!npc.Skills.TryGetValue(skillId, out var temp))
        { SendCS_SKILLBUY_ACK(s, ch, SkillUseResult.NotFound, skillId, 0); return; }
        uint cost = Discounted(temp.GetPrice(_templates.LevelMoneyOf(temp.StartLevel) ?? 0), discount);

        if (ch.Level < temp.StartLevel) { SendCS_SKILLBUY_ACK(s, ch, SkillUseResult.NeedLevelUp, skillId, 0); return; }
        if (temp.ParentSkillId != 0 && (ch.Skills.FirstOrDefault(k => k.SkillId == temp.ParentSkillId) is not { } p
                                        || !temp.CheckParentSkill(1, p.Level)))
        { SendCS_SKILLBUY_ACK(s, ch, SkillUseResult.NeedParent, skillId, 0); return; }
        if (!temp.IsClassMatch(ch.Class)) { SendCS_SKILLBUY_ACK(s, ch, SkillUseResult.MatchClass, skillId, 0); return; }
        if (!IsEnoughSkillPoint(ch, temp)) { SendCS_SKILLBUY_ACK(s, ch, SkillUseResult.NeedSkillPoint, skillId, 0); return; }
        if (!ch.UseMoney(cost, false)) { SendCS_SKILLBUY_ACK(s, ch, SkillUseResult.NeedMoney, skillId, 0); return; }

        var learned = new Skill { SkillId = skillId, Level = 1, Template = temp };      // CTSkill's level starts at 1
        ch.SkillPoint -= temp.GetNeedSkillPoint(1);
        ch.UseMoney(cost, true);
        ch.Skills.Add(learned);
        SendCS_SKILLBUY_ACK(s, ch, SkillUseResult.Success, skillId, learned.Level);
    }

    private static uint Discounted(uint price, byte rate) => price - price * rate / 100;

    /// <summary>C++ <c>CTSkill::GetNextLevel</c> (TSkill.cpp:228) — the character level the next skill level needs:
    /// <c>m_bStartLevel + m_bLevel · m_bNextLevel</c> (BYTE arithmetic).</summary>
    private static byte NextLearnLevel(Skill k) => unchecked((byte)(k.Template!.StartLevel + k.Level * k.Template.NextLevel));

    /// <summary>C++ <c>CTSkill::GetUsedSkillPoint</c> (TSkill.cpp:268) — the points spent on this skill's levels.</summary>
    private static ushort UsedSkillPoint(Skill k)
    {
        ushort total = 0;
        for (int i = 0; i < k.Level; i++) total += k.Template?.GetNeedSkillPoint((byte)(i + 1)) ?? 0;
        return total;
    }

    /// <summary>C++ <c>CTPlayer::IsEnoughSkillPoint</c> (TPlayer.cpp:2297) — the character has the points the next level
    /// costs, and has already spent enough in the skill's kind (its skill-window tab).</summary>
    private static bool IsEnoughSkillPoint(Character ch, SkillTemplate t)
    {
        var mine = ch.Skills.FirstOrDefault(k => k.SkillId == t.Id);
        byte next = mine is null ? (byte)1 : (byte)(mine.Level + 1);
        if (ch.SkillPoint < t.GetNeedSkillPoint(next)) return false;
        int spent = ch.Skills.Where(k => k.Template?.Kind == t.Kind).Sum(k => UsedSkillPoint(k));
        return spent >= t.GetNeedKindPoint(next);
    }

    /// <summary>C++ <c>CTPlayer::GetSkillKindPoint</c> (TPlayer.cpp:2321) — the points spent in each of the four skill
    /// tabs: equipment skills by their kind, class skills by their kind minus the class's offset (BYTE arithmetic — a
    /// kind below the offset wraps past 4 and is not counted).</summary>
    private static ushort[] SkillKindPoints(Character ch)
    {
        var points = new ushort[4];
        foreach (var k in ch.Skills)
        {
            if (k.Template is not { } t) continue;
            if (t.Kind == NormalEquip) { points[t.Kind] += UsedSkillPoint(k); continue; }
            byte kind = unchecked((byte)(t.Kind - (ch.Class <= 5 ? ch.Class * 3 : 0)));   // TCLASS_WARRIOR..SORCERER: 0,3,..,15
            if (ch.Class <= 5 && kind < 4) points[kind] += UsedSkillPoint(k);
        }
        return points;
    }

    // ================================ the trainer's list ================================

    /// <summary>C++ <c>OnCS_NPCITEMLIST_REQ</c> — <c>wNpcID</c>. Only the skill trainers' list is ported here.</summary>
    private void OnCS_NPCITEMLIST_REQ(ClientSession s, PacketReader r)
    {
        ushort npcId = r.ReadUInt16();
        if (s.State != EnterState.InGame || !s.IsMain || s.Char is not { } ch) return;
        if (_state.FindNpc(npcId) is not { } npc || !npc.CanTalk(ch.Country, ch.AidCountry, 0)) return;
        if (npc.Type is not (TnpcSkillMaster or TnpcSkillRent)) return;              // item shops: not ported
        SendCS_NPCSKILLLIST_ACK(s, ch, npc);
    }

    /// <summary>The skill branch of C++ <c>SendCS_NPCITEMLIST_ACK(CTNpc*)</c> (CSSender.cpp) —
    /// <c>wNpcID · bType · bDiscountRate · bCount · {wSkillID · dwPrice}</c>: what the character can learn right now —
    /// a new skill of its class whose points it has and whose parent it knows, or a held one below its maximum whose
    /// next level it has reached. The price is before discount (the client applies the rate).</summary>
    private void SendCS_NPCSKILLLIST_ACK(ClientSession s, Character ch, Npc npc)
    {
        var list = new SortedDictionary<ushort, uint>();
        foreach (var t in npc.Skills.Values)
        {
            var mine = ch.Skills.FirstOrDefault(k => k.SkillId == t.Id);
            if (mine is null)
            {
                if (t.IsClassMatch(ch.Class) && IsEnoughSkillPoint(ch, t)
                    && (t.ParentSkillId == 0 || ch.Skills.Any(k => k.SkillId == t.ParentSkillId)))
                    list[t.Id] = t.GetPrice(_templates.LevelMoneyOf(t.StartLevel) ?? 0);
            }
            else if (t.MaxLevel > mine.Level && ch.Level >= NextLearnLevel(mine) && IsEnoughSkillPoint(ch, t))
                list[t.Id] = t.GetPrice(_templates.LevelMoneyOf(NextLearnLevel(mine)) ?? 0);
        }

        var w = new PacketWriter(Msg.CS_NPCITEMLIST_ACK, capacity: 8 + list.Count * 6);
        w.WriteUInt16(npc.Id); w.WriteByte(npc.Type); w.WriteByte(0);                 // bDiscountRate — see the class remarks
        w.WriteByte((byte)list.Count);
        foreach (var (id, price) in list) { w.WriteUInt16(id); w.WriteUInt32(price); }
        s.Send(w);
    }

    // ================================ unlearning ================================

    /// <summary>C++ <c>OnCS_SKILLINIT_REQ</c> — <c>wSkillID · bInvenID · bItemID</c>: a reset scroll takes one skill
    /// down a level (<c>IK_SKILLONEINIT</c>, a skill named) or resets every skill (<c>IK_SKILLALLINIT</c>, none named).</summary>
    private void OnCS_SKILLINIT_REQ(ClientSession s, PacketReader r)
    {
        ushort skillId = r.ReadUInt16();
        byte invenId = r.ReadByte(), itemId = r.ReadByte();
        if (s.State != EnterState.InGame || !s.IsMain || s.Char is not { } ch) return;
        if (IsTutorial(ch)) return;
        if (s.Store.IsOpen || s.Deal.Status >= (byte)DealStatus.Start) return;

        if (ch.FindInven(invenId) is not { } bag || bag.FindItem(itemId) is not { Count: > 0 } item
            || item.Template is not { Type: ItUse } it)
        { SendCS_SKILLINIT_ACK(s, SkillUseResult.NeedItem, 0); return; }
        if (skillId != 0 && it.Kind == IkSkillAllInit) { SendCS_SKILLINIT_ACK(s, SkillUseResult.NeedItem, 0); return; }
        if (skillId == 0 && it.Kind == IkSkillOneInit) { SendCS_SKILLINIT_ACK(s, SkillUseResult.NotFound, 0); return; }

        Skill? skill = null;
        if (skillId != 0)
        {
            skill = ch.Skills.FirstOrDefault(k => k.SkillId == skillId);
            if (skill?.Template is not { } st) { SendCS_SKILLINIT_ACK(s, SkillUseResult.NotFound, 0); return; }
            if (st.Kind == NormalEquip) { SendCS_SKILLINIT_ACK(s, SkillUseResult.NotInit, 0); return; }
            if (st.StartLevel == 0 && skill.Level == 1) { SendCS_SKILLINIT_ACK(s, SkillUseResult.NotInit, 0); return; }   // a given skill
            if (FindChildSkill(ch, skillId) is not null) { SendCS_SKILLINIT_ACK(s, SkillUseResult.HaveChild, 0); return; }
        }
        if (it.Kind is not (IkSkillOneInit or IkSkillAllInit)) { SendCS_SKILLINIT_ACK(s, SkillUseResult.NeedItem, 0); return; }

        InitializeSkill(s, ch, skill);
        UseItemStack(s, invenId, bag, item, 1);
        SendCS_SKILLINIT_ACK(s, SkillUseResult.Success, skillId);
        SendCS_SKILLLIST_ACK(s, ch);
    }

    /// <summary>C++ <c>OnCS_SKILLINITPOSSIBLE_REQ</c> — <c>bInvenID · bItemID</c>: which skills a one-skill reset scroll
    /// (its <c>wUseValue</c> is the highest level it can reset) may take down.</summary>
    private void OnCS_SKILLINITPOSSIBLE_REQ(ClientSession s, PacketReader r)
    {
        byte invenId = r.ReadByte(), itemId = r.ReadByte();
        if (s.State != EnterState.InGame || !s.IsMain || s.Char is not { } ch) return;
        if (IsTutorial(ch)) return;
        if (ch.FindInven(invenId)?.FindItem(itemId) is not { Count: > 0, Template: { Type: ItUse, Kind: IkSkillOneInit } it }) return;

        byte level = (byte)it.UseValue;
        var list = new List<ushort>();
        foreach (var k in ch.Skills)                                                     // CTPlayer::InitSkillPossible
        {
            if (k.Template is not { } t || t.Kind == NormalEquip) continue;
            if (FindChildSkill(ch, t.Id) is not null) continue;
            if (t.StartLevel == 0 && k.Level == 1) continue;
            if (t.StartLevel + k.Level > level) continue;
            list.Add(t.Id);
        }
        var w = new PacketWriter(Msg.CS_SKILLINITPOSSIBLE_ACK, capacity: 4 + list.Count * 2);
        w.WriteByte((byte)list.Count);
        foreach (var id in list) w.WriteUInt16(id);
        s.Send(w);
    }

    /// <summary>C++ <c>CTPlayer::FindTChildSkill</c> (TPlayer.cpp:3404) — a held skill whose parent is this one.</summary>
    private static Skill? FindChildSkill(Character ch, ushort parentId)
        => ch.Skills.FirstOrDefault(k => k.Template?.ParentSkillId == parentId);

    /// <summary>C++ <c>CTPlayer::InitializeSkill</c> (TPlayer.cpp:3415). With a skill: one level down, its points back,
    /// and a skill that reaches 0 is dropped (with its hotkeys). With none: every non-equipment skill goes back to 0 —
    /// except a given skill (start level 0) kept at level 1 — refunding its points and its current level's
    /// <c>dwPayback</c> in gold.</summary>
    private void InitializeSkill(ClientSession s, Character ch, Skill? skill)
    {
        uint points = 0;
        if (skill is { Template: { } st })
        {
            points = st.GetNeedSkillPoint(skill.Level);
            skill.Level--;
            if (skill.Level == 0) DropSkill(ch, skill);
        }
        else
        {
            long payback = 0;
            var dropped = new List<Skill>();
            foreach (var k in ch.Skills)
            {
                if (k.Template is not { } t || t.Kind == NormalEquip) continue;
                if (t.Points.TryGetValue(k.Level, out var row)) payback += row.Payback;
                while (k.Level != 0)
                {
                    if (t.StartLevel == 0 && k.Level == 1) break;                       // a given skill stays
                    points += t.GetNeedSkillPoint(k.Level);
                    k.Level--;
                }
                if (k.Level == 0) dropped.Add(k);
            }
            foreach (var k in dropped) DropSkill(ch, k);
            if (payback != 0 && ch.EarnMoney(payback)) SendCS_MONEY_ACK(s, ch);
        }
        ch.SkillPoint += points;
    }

    /// <summary>A skill brought to 0 leaves the character (C++ <c>m_mapTSKILL.erase</c>) and every hotkey page's first
    /// slot holding its id is cleared (C++ <c>CTPlayer::EraseHotkey</c> — which matches the id whatever the slot's type).</summary>
    private static void DropSkill(Character ch, Skill k)
    {
        ch.Skills.Remove(k);
        foreach (var page in ch.HotkeyPages)
            for (int i = 0; i < HotkeyPage.SlotCount; i++)
                if (page.Slots[i].Id == k.SkillId)
                {
                    page.Slots[i] = new HotkeySlot(HotkeyNone, 0);
                    page.Save |= HotkeyPage.SaveUpdate;
                    break;
                }
    }

    // ================================ senders ================================

    /// <summary>C++ <c>SendCS_SKILLINIT_ACK</c> (CSSender.cpp:4397) — <c>bResult · wSkillID</c>.</summary>
    private static void SendCS_SKILLINIT_ACK(ClientSession s, SkillUseResult result, ushort skillId)
    {
        var w = new PacketWriter(Msg.CS_SKILLINIT_ACK, capacity: 4);
        w.WriteByte((byte)result); w.WriteUInt16(skillId);
        s.Send(w);
    }

    /// <summary>C++ <c>SendCS_SKILLLIST_ACK</c> (CSSender.cpp:4407) — <c>wSkillPoint · kind[4] · bCount ·
    /// {wSkillID · bLevel · dwReuseRemain}</c>, skills ascending by id (the C++ map order).</summary>
    private void SendCS_SKILLLIST_ACK(ClientSession s, Character ch)
    {
        var kinds = SkillKindPoints(ch);
        var w = new PacketWriter(Msg.CS_SKILLLIST_ACK, capacity: 16 + ch.Skills.Count * 7);
        w.WriteUInt16((ushort)ch.SkillPoint);
        foreach (var k in kinds) w.WriteUInt16(k);
        w.WriteByte((byte)ch.Skills.Count);
        foreach (var k in ch.Skills.OrderBy(x => x.SkillId))
        {
            w.WriteUInt16(k.SkillId); w.WriteByte(k.Level); w.WriteUInt32(k.GetReuseRemainTick(NowMs));
        }
        s.Send(w);
    }
}
