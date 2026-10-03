using TMap.Data;
using TMap.Protocol;

namespace TMap.Server.Map;

/// <summary>
/// <c>CS_FINISHSKILL_ACK</c> — the packet that carries every ordinary player attack in this build. The port of
/// <c>OnCS_FINISHSKILL_ACK</c> (CSHandler.cpp:20197).
///
/// <para>Despite the <c>_ACK</c> suffix it is <b>client → server</b>. It is a late addition to this release
/// (<c>CS_MAP + 0x377</c>, near the end of the table) that moves hit resolution server-side. The client decides
/// which path a finished skill takes (TClientGame.cpp:14381):</para>
/// <code>
/// if ((attacker != OT_PC &amp;&amp; attacker != OT_RECALL &amp;&amp; attacker != OT_SELF) || skill in exceptions)
///     Defend(...)       // monsters + a short exceptions list  → classic CS_DEFEND_REQ
/// else
///     FinishSkill(...)  // players                             → this packet
/// </code>
/// <para>So a player's swing never produces <c>CS_DEFEND_REQ</c>. Without this handler every player attack is
/// dropped, and the whole damage engine is unreachable from a real client. The wire carries only the attacker,
/// the ground point, the skill and the target list; attack power, crit rate, attack level and the hit roll are
/// all derived here. Each target is then resolved through the same per-target body the classic path uses
/// (<see cref="PlayerHitsTarget"/>) — both front doors end in C++ <c>CTObjBase::Defend</c>.</para>
///
/// <para><b>Preserved from the C++:</b></para>
/// <list type="bullet">
/// <item><c>GetTransHPMPFromType</c> ignores its argument and returns the first cure row that is <i>either</i>
/// HP-transfer or MP-transfer, so both transfer amounts come out the same (<see cref="TransHpMp"/>).</item>
/// <item>The repeat-count kick and the reuse "Cant use" check are guarded by
/// <c>m_wID &lt; 31 &amp;&amp; m_wID &gt; 34 &amp;&amp; …</c>, which is never true — both are dead code, so neither is
/// ported.</item>
/// </list>
///
/// <para><b>Deliberate hardening:</b> the C++ resolves an <c>OT_PC</c> attacker by <c>dwAttackID</c> from the
/// global player map, so any client could name any other player as the attacker. Here the attacker must be the
/// sender, the same rule the port's <c>CS_DEFEND_REQ</c> already applies.</para>
///
/// <para><b>Deferred</b> (subsystems or columns this port does not carry): the <c>m_vUsedSkill</c> anti-cheat
/// ledger (it is populated by <c>CS_SKILLUSE</c>, and for ordinary skills a miss only logs), the charge-time and
/// <c>m_bRunFromServer</c> / <c>m_bCheckAttacker</c> / <c>m_wTargetActiveID</c> checks (columns not loaded),
/// guild skills (the guild-skill subsystem is not ported), the peace-zone and local-battle gates, and the
/// <c>SDT_STATUS_LINK</c> self-maintain tail. Random skills and the 229/128/3604 special cases: see the loop below.</para>
/// </summary>
public sealed partial class MapService
{
    private const ushort InvalidMapId = 0xFFFF;   // C++ INVALID_MAPID (TMapType.h:24) — "usable on any map"
    private const byte TcontryB = 2;              // TCONTRY_TYPE TCONTRY_B (NetCode.h:1095)

    private void OnCS_FINISHSKILL_ACK(ClientSession s, PacketReader r)
    {
        // Request layout (client CSSender.cpp:2996 / server CSHandler.cpp:20229). IsLinked and IsFake are C++
        // BOOL, written through CPacket::operator<<(int) — 4 bytes each, not 1.
        uint attackId = r.ReadUInt32();     // dwAttackID (the host id)
        uint objId = r.ReadUInt32();        // dwID (the attacking object)
        byte attackType = r.ReadByte();     // bType
        float posX = r.ReadFloat(), posY = r.ReadFloat(), posZ = r.ReadFloat();   // the ground point
        ushort skillId = r.ReadUInt16();    // wSkillID
        r.ReadUInt32();                     // IsLinked
        bool fake = r.ReadUInt32() != 0;    // IsFake — a doppelganger's hit, which always misses
        r.ReadUInt16();                     // wAttackPartyID
        byte count = r.ReadByte();          // bDefendCount
        var targets = new (uint Id, byte Type)[count];
        for (int i = 0; i < count; i++) targets[i] = (r.ReadUInt32(), r.ReadByte());

        if (s.State != EnterState.InGame || s.Char is not { } ch) return;

        // The skill template must exist and be usable on this map.
        if (!_templates.Skills.TryGetValue(skillId, out var tpl))
        { _log.LogDebug("FINISHSKILL from char {Char}: unknown skill {Skill}; dropped.", s.CharId, skillId); return; }
        if (tpl.MapId != InvalidMapId && tpl.MapId != ch.MapId)
        { _log.LogDebug("FINISHSKILL from char {Char}: skill {Skill} restricted to map {SkillMap}, char on {Map}; dropped.", s.CharId, skillId, tpl.MapId, ch.MapId); return; }

        // A summon / placed object of the sender (C++ FindTarget(pPlayer, bType, dwID) — the sender's own).
        if (attackType is RecallMon.OtRecall or RecallMon.OtSelf && attackId == s.CharId)
        {
            SummonFinishSkill(s, ch, attackType, objId, tpl, fake, posX, posY, posZ, targets);
            return;
        }

        // The attacker: a player acting as itself (see the hardening note).
        if (attackType != OtPc || attackId != s.CharId || objId != s.CharId)
        { _log.LogDebug("FINISHSKILL from char {Char}: attacker {Attack}/{Obj} type {Type} is not the sender; dropped.", s.CharId, attackId, objId, attackType); return; }

        // Skill level from the attacker's own copy (C++ FindTSkill(m_wTriggerID); triggerID == wSkillID here).
        // One the character holds at level 0 is not learned (see LearnedSkill): the cast does nothing.
        var own = ch.Skills.FirstOrDefault(k => k.SkillId == skillId);
        if (own is not null && !IsLearned(own))
        { _log.LogDebug("FINISHSKILL from char {Char}: skill {Skill} is not learned (level 0); dropped.", s.CharId, skillId); return; }
        byte skillLevel = own?.Level ?? 0;
        ushort trans = TransHpMp(tpl);
        byte attackCountry = GetAttackCountry(ch.Country, ch.AidCountry);

        // C++ (CSHandler.cpp:20601): Deadly Poison's damage over time (3604) is not learned — it hits at the level of
        // Deadly Poison itself (3603), and not at all without it.
        Skill? fixedCast = null;
        if (skillId == DeadlyPoisonDot)
        {
            if (ch.Skills.FirstOrDefault(k => k.SkillId == DeadlyPoison) is not { } poison) return;
            skillLevel = poison.Level;
            fixedCast = new Skill { SkillId = skillId, Level = poison.Level, Template = tpl };
        }

        _log.LogDebug("FINISHSKILL from char {Char}: skill {Skill} lvl {Level}, {Count} target(s).", s.CharId, skillId, skillLevel, targets.Length);
        foreach (var (targetId, targetType) in targets)
        {
            float defX, defY, defZ;
            if (targetType == OtPc)
            {
                // C++ CanDuel (a target in a duel) is checked where the hit lands, PlayerHitsTarget.
                if (_state.FindByChar(targetId) is not { State: EnterState.InGame, Char: { } tch }) continue;
                defX = tch.PosX; defY = tch.PosY; defZ = tch.PosZ;
            }
            else if (targetType == Monster.OtMon)
            {
                if (_state.FindMonster(targetId) is not { } mon) continue;
                // A negative skill never lands on a monster of the attacker's own faction (or on anything
                // non-neutral when attacking as TCONTRY_B).
                if (tpl.IsNegative && mon.Country != TcontryN
                    && (attackCountry == TcontryB || attackCountry == mon.Country))
                    continue;
                defX = mon.PosX; defY = mon.PosY; defZ = mon.PosZ;
            }
            else if (targetType is RecallMon.OtRecall or RecallMon.OtSelf)
            {
                // Any summon or placed object in the attacker's cells (CSHandler.cpp:20464), one's own or another's.
                if (SummonInReach(ch, s, targetType, targetId) is not { } pet) continue;
                defX = pet.PosX; defY = pet.PosY; defZ = pet.PosZ;
            }
            else
            {
                continue;
            }

            // C++ RandTransSkill / RandBuffSkill: a random skill lands as one of its run, picked for each target.
            var cast = fixedCast;
            ushort castId = skillId;
            if (tpl.IsRandomTrans() || tpl.IsRandomBuff())
            {
                if (PickRandomSkill(tpl) is not { } picked) continue;
                castId = picked.Id;
                cast = new Skill { SkillId = picked.Id, Level = Math.Max(skillLevel, (byte)1), Template = picked };
            }

            // C++ (CSHandler.cpp:20590): Sixth Sense (128) and Inner Eye (229) first take away their own effect (129 / 230)
            // from the target, so a new cast starts it over.
            if (skillId is SixthSense or InnerEye) EraseMaintainOn(targetId, targetType, (ushort)(skillId + 1));

            // dwActID/dwAniID are 0 here: the C++ passes literal zeros to Defend for this path.
            PlayerHitsTarget(s, ch, hostId: attackId, attackId, attackType, targetId, targetType,
                actId: 0, aniId: 0, attackerLevel: ch.Level, transHp: trans, transMp: trans,
                canSelect: 1, castId, skillLevel, posX, posY, posZ, defX, defY, defZ, cast);
        }
        CheckEquipSkill(s, ch);
    }

    private const ushort SixthSense = 128, InnerEye = 229, DeadlyPoison = 3603, DeadlyPoisonDot = 3604;

    /// <summary>C++ <c>pDEFEND->EraseMaintainSkill(wSkillID)</c> on a player, a monster or a summon.</summary>
    private void EraseMaintainOn(uint targetId, byte targetType, ushort skillId)
    {
        if (targetType == OtPc && _state.FindByChar(targetId) is { Char: { } pc } ps)
        {
            int i = pc.MaintainSkills.FindIndex(m => m.SkillId == skillId);
            if (i >= 0) EraseMaintainPlayer(ps, pc, i);
        }
        else if (targetType == Monster.OtMon && _state.FindMonster(targetId) is { } mon)
        {
            int i = mon.MaintainSkills.FindIndex(m => m.SkillId == skillId);
            if (i >= 0) EraseMaintainMonster(mon, i);
        }
        else if (_state.FindRecall(targetType, targetId) is { } pet)
        {
            int i = pet.MaintainSkills.FindIndex(m => m.SkillId == skillId);
            if (i >= 0) EraseMaintainSummon(pet, i);
        }
    }

    /// <summary>C++ <c>CTSkillTemp::GetTransHPMPFromType</c> (TSkillTemp.cpp:387), bug included: the argument is
    /// ignored, and the first <c>SDT_CURE</c> row whose exec is HP-transfer <i>or</i> MP-transfer wins.</summary>
    private static ushort TransHpMp(SkillTemplate tpl)
    {
        foreach (var d in tpl.Data)
            if (d.Type == SkillTemplate.SdtCure && (d.Exec == SctHpTrans || d.Exec == SctMpTrans))
                return d.Value;
        return 0;
    }
}
