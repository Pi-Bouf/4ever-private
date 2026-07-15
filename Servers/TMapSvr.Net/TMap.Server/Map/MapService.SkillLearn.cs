using TMap.Data;
using TMap.Protocol;

namespace TMap.Server.Map;

/// <summary>
/// The skill-<b>learn</b> path — the C# port of <c>CTObjBase::UpdateSkill</c> (TObjBase.cpp:2495), the
/// add-only skill grant used by the quest system. It mirrors the C++ exactly: if the character does not
/// already know the skill, a fresh learned <see cref="Skill"/> is created at the requested level, added to
/// the learned-skill map (keyed by the template id), and a <c>CS_SKILLBUY_ACK(SKILL_SUCCESS)</c> is pushed;
/// if the skill is already known (at <em>any</em> level), it is a no-op and returns <c>false</c> — there is
/// no level-up here. Level-up and the class/skill-point/money gates live on a separate client path.
///
/// <para><b>Deferred (documented in PORT_STATUS.md):</b> the NPC-purchase handler <c>OnCS_SKILLBUY_REQ</c>
/// (CSHandler.cpp:2170) — both its LEARN-new and LEVEL-UP branches — is blocked on subsystems this port does
/// not model: the <b>skill-point currency</b> (<c>m_wSkillPoint</c>/<c>IsEnoughSkillPoint</c>/
/// <c>GetNeedSkillPoint</c>), the <b>level-cost / price tables</b> (<c>FindTLevel(...).m_dwMoney</c> ×
/// <c>GetPrice</c>), the <b>NPC skill-teaching lists</b> (<c>pNpc-&gt;GetSkill</c>), <b>parent-skill
/// prerequisites</b> (<c>m_wParentSkillID</c>/<c>CheckParentSkill</c>), and the trade lock. Also deferred:
/// <c>RemainSkill</c> (the passive/"remain" registry <c>m_vRemainSkill</c> — a no-op for non-remain skills
/// anyway), <c>AutoEquipSkill</c> (auto-grant class skills on level-up), the skill-reset path
/// (<c>CS_SKILLINIT</c>), and the server-push full list (<c>CS_SKILLLIST_ACK</c> — the login list is already
/// carried inside CHARINFO). The <c>CS_SKILLBUY_ACK</c> emits <c>skillPoint</c> and the four kind-points as
/// <c>0</c> (the SP currency is unmodelled — CHARINFO also emits <c>0</c> there).</para>
/// </summary>
public sealed partial class MapService
{
    /// <summary>C++ <c>CTObjBase::UpdateSkill</c> (TObjBase.cpp:2495) — add-only skill grant. Returns
    /// <c>true</c> only when the skill was newly learned (C++ <c>FindTSkill</c> null ⇒ insert + ack + TRUE);
    /// an already-known skill (any level) is left untouched and returns <c>false</c>. The learned entry is
    /// keyed by the template id, its level stored verbatim (the caller floors it at 1). The class-mask gate is
    /// the caller's responsibility (see <see cref="ExecGiveSkill"/>), matching the C++.</summary>
    private bool UpdateSkill(ClientSession s, Character ch, SkillTemplate temp, ushort skillId, byte level)
    {
        if (ch.Skills.Any(k => k.SkillId == skillId)) return false;   // C++ if(!FindTSkill): already known ⇒ FALSE

        ch.Skills.Add(new Skill { SkillId = temp.Id, Level = level, Template = temp });  // key = pTemp->m_wID
        SendCS_SKILLBUY_ACK(s, ch, SkillUseResult.Success, temp.Id, level);
        // C++ then calls RemainSkill(pSkill, 0) — the m_vRemainSkill passive registry, a no-op for a
        // non-remain skill; the remain/passive layer is deferred (PORT_STATUS.md).
        return true;
    }

    /// <summary>C++ <c>CTPlayer::SendCS_SKILLBUY_ACK</c> (CSSender.cpp:1494) — a skill was learned/leveled:
    /// <c>bRet, wSkillID, bLevel, Tick, dwGold, dwSilver, dwCooper, wSkillPoint, kindPoint[4]</c>. The
    /// <c>DWORD Tick</c> field is present in the code though absent from the header comment (the code is
    /// authoritative). <paramref name="tick"/> defaults to 0 (a fresh grant has no reuse remaining, and the
    /// C++ <c>UpdateSkill</c> call uses the 3-arg overload → <c>Tick = 0</c>). The skill-point and kind-point
    /// tail is emitted as 0 (SP currency unmodelled — as in CHARINFO).</summary>
    private static void SendCS_SKILLBUY_ACK(ClientSession s, Character ch, SkillUseResult ret, ushort skillId,
        byte level, uint tick = 0)
    {
        var w = new PacketWriter(Msg.CS_SKILLBUY_ACK, capacity: 32);
        w.WriteByte((byte)ret);
        w.WriteUInt16(skillId);
        w.WriteByte(level);
        w.WriteUInt32(tick);
        w.WriteUInt32(ch.Gold);
        w.WriteUInt32(ch.Silver);
        w.WriteUInt32(ch.Cooper);
        w.WriteUInt16(0);                       // m_wSkillPoint (SP currency unported — CHARINFO also emits 0)
        w.WriteUInt16(0); w.WriteUInt16(0);     // arPoint[0..3] — the per-kind invested-SP summary (unported)
        w.WriteUInt16(0); w.WriteUInt16(0);
        s.Send(w);
    }
}
