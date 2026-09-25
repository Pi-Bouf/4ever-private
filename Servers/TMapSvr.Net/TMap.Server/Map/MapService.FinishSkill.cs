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
/// random-trans / random-buff skills, guild skills, the peace-zone and local-battle gates, the
/// <c>OT_RECALL</c>/<c>OT_SELF</c> attackers and targets (summons), the <c>SDT_STATUS_LINK</c> self-maintain tail,
/// the skill-229/128/3604 special cases and <c>CheckEquipSkill</c>.</para>
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
        r.ReadUInt32();                     // IsFake (only meaningful for an OT_RECALL fake — unported)
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

        // The attacker: a player acting as itself (see the hardening note). Summon attackers are unported.
        if (attackType != OtPc || attackId != s.CharId || objId != s.CharId)
        { _log.LogDebug("FINISHSKILL from char {Char}: attacker {Attack}/{Obj} type {Type} is not the sender; dropped.", s.CharId, attackId, objId, attackType); return; }

        // Skill level from the attacker's own copy (C++ FindTSkill(m_wTriggerID); triggerID == wSkillID here).
        byte skillLevel = ch.Skills.FirstOrDefault(k => k.SkillId == skillId)?.Level ?? 0;
        ushort trans = TransHpMp(tpl);
        byte attackCountry = GetAttackCountry(ch.Country, ch.AidCountry);

        _log.LogDebug("FINISHSKILL from char {Char}: skill {Skill} lvl {Level}, {Count} target(s).", s.CharId, skillId, skillLevel, targets.Length);
        foreach (var (targetId, targetType) in targets)
        {
            float defX, defY, defZ;
            if (targetType == OtPc)
            {
                // C++ CanDuel only rejects a target that is mid-duel; duels are unported, so it always passes.
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
            else
            {
                continue;   // OT_RECALL / OT_SELF targets — summons unported
            }

            // dwActID/dwAniID are 0 here: the C++ passes literal zeros to Defend for this path.
            PlayerHitsTarget(s, ch, hostId: attackId, attackId, attackType, targetId, targetType,
                actId: 0, aniId: 0, attackerLevel: ch.Level, transHp: trans, transMp: trans,
                canSelect: 1, skillId, skillLevel, posX, posY, posZ, defX, defY, defZ);
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
