using TMap.Data;
using TMap.Protocol;

namespace TMap.Server.Map;

/// <summary>
/// A summon's skill reported through <c>CS_DEFEND_REQ</c> (C++ <c>OnCS_DEFEND_REQ</c>, summon attacker —
/// CSHandler.cpp:1535-1760). The client uses this path for what it applies on its own side: the Protection Crystal's
/// aura. Its owner's client has the crystal buff itself (<c>CheckAutoSKILL</c> → <c>Defend(crystal, crystal)</c>),
/// and then every client in range of that buff reports it on its own player (<c>CheckMaintainOBJ</c>), for as long as
/// the crystal's buff lasts (<c>dwRemainTick</c>); walking out of range ends it (<c>CS_SKILLEND_REQ</c>).
/// <list type="bullet">
/// <item>The attacker is found through its owner, <c>dwHostID</c> — who need not be the reporter (a party member
/// standing in your crystal's aura reports your crystal).</item>
/// <item>The target is the reporter or one of the reporter's own summons (C++ <c>FindTarget(pPlayer, …)</c>), or a
/// monster.</item>
/// <item>The level is the attacker's own copy of the skill; a buff lasts <c>dwRemainTick</c>, else its duration.</item>
/// </list>
/// <para><b>Not ported:</b> an attacker the server cannot find (the C++ still lets a non-monster target take it when
/// <c>m_bCheckAttacker</c> is 0 — the column is not loaded), PvP skills on players, the riding / arena / guild-skill /
/// magic-mirror / random-skill branches.</para>
/// </summary>
public sealed partial class MapService
{
    private void OnSummonDefendReport(ClientSession s, Character reporter, uint hostId, uint attackId, byte attackType,
        uint targetId, byte targetType, byte canSelect, ushort skillId, uint remainTick,
        float atkX, float atkY, float atkZ, float defX, float defY, float defZ)
    {
        _log.LogDebug("[summon] DEFEND {AtkType}:{Atk} (host {Host}) skill {Skill} -> {TgtType}:{Tgt}, remain {Remain} ms.",
            attackType, attackId, hostId, skillId, targetType, targetId, remainTick);
        if (!_templates.Skills.TryGetValue(skillId, out var tpl)) return;
        if (tpl.MapId != InvalidMapId && tpl.MapId != reporter.MapId) return;
        if (_state.FindByChar(hostId) is not { State: EnterState.InGame, Char: { } host }) return;   // pAtkHost
        IReadOnlyDictionary<uint, RecallMon> own = attackType == RecallMon.OtSelf ? host.SelfObjs : host.Recalls;
        if (!own.TryGetValue(attackId, out var atk) || !atk.InMap) return;                         // FindTarget(pAtkHost, …)
        if (atk.Skills.FirstOrDefault(k => k.SkillId == skillId) is not { } atkSkill) return;      // FindTSkill(m_wTriggerID)
        byte level = atkSkill.Level;

        switch (targetType)
        {
            case OtPc:
                if (targetId != reporter.CharId) return;                  // FindTarget(pPlayer, OT_PC, id): the reporter
                if (tpl.IsPositive && tpl.IsMaintainType())
                {
                    var snap = SummonSnapshot(atk, attackType, host, tpl, canSelect, atkX, atkY, atkZ);
                    if (ApplyMaintainToPlayer(s, reporter, tpl, level, remainTick, snap, NowMs) is { } m)
                    {
                        var ack = BuildDefendAckMaintain(attackId, attackType, reporter.CharId, OtPc, tpl.Id, m.Level,
                            isMaintain: 1, m.MaintainTick, atkX, atkY, atkZ);
                        foreach (var p in _state.NearView(s)) p.Send(ack);
                    }
                }
                break;

            case RecallMon.OtSelf or RecallMon.OtRecall:
            {
                IReadOnlyDictionary<uint, RecallMon> mine = targetType == RecallMon.OtSelf ? reporter.SelfObjs : reporter.Recalls;
                if (!mine.TryGetValue(targetId, out var target) || !target.InMap) return;
                if (tpl.IsPositive && tpl.IsMaintainType())
                {
                    var snap = SummonSnapshot(atk, attackType, host, tpl, canSelect, atkX, atkY, atkZ);
                    if (ApplyMaintainToSummon(target, tpl, level, remainTick, snap) is { } m)
                    {
                        var ack = BuildDefendAckMaintain(attackId, attackType, target.Id, target.ObjType, tpl.Id, m.Level,
                            isMaintain: 1, m.MaintainTick, atkX, atkY, atkZ);
                        foreach (var p in _state.PlayersAround(target)) p.Send(ack);
                    }
                }
                break;
            }

            case Monster.OtMon:
            {
                if (_state.FindMonster(targetId) is not { Hp: > 0 } mon) return;
                byte attackCountry = GetAttackCountry(atk.Country, atk.AidCountry);
                if (tpl.IsNegative && mon.Country != TcontryN && (attackCountry == TcontryB || attackCountry == mon.Country)) return;
                bool triple = attackType == RecallMon.OtRecall && atk.RecallType == TrecallAutoAi;
                HitMonster(host, HitPower(atk, tpl, host), attackId, attackType, host.CharId, tpl, level, canSelect, mon,
                    0, 0, tpl.Id, forceMiss: false, triple, atkX, atkY, atkZ, defX, defY, defZ);
                break;
            }
        }
    }

    /// <summary>The attacker's figures stored on the buff (C++ <c>SetMaintain</c>).</summary>
    private MaintainSnapshot SummonSnapshot(RecallMon atk, byte attackType, Character host, SkillTemplate tpl, byte canSelect,
        float x, float y, float z)
    {
        var p = HitPower(atk, tpl, host);
        return new MaintainSnapshot(atk.Id, attackType, host.CharId, OtPc, p.Crit, p.AttackLevel, p.Level,
            p.PysMin, p.PysMax, p.MgMin, p.MgMax, canSelect, p.Country, x, y, z);
    }

    /// <summary>C++ <c>MaintainSkill</c> on a summon: stack-resolve and push (its vitals are chart-fixed).</summary>
    private MaintainSkill? ApplyMaintainToSummon(RecallMon m, SkillTemplate tpl, byte level, uint remainTick,
        in MaintainSnapshot snap)
    {
        var neu = BuildMaintain(tpl, level, remainTick, snap, NowMs);
        if (!UpdateBuffSkill(m.MaintainSkills, neu, m.Id, m.ObjType, i => EraseMaintainSummon(m, i))) return null;
        if (!PushMaintain(m.MaintainSkills, m.Hp == 0, neu)) return null;
        return neu;
    }

    private void EraseMaintainSummon(RecallMon m, int index)
    {
        var skill = m.MaintainSkills[index];
        m.MaintainSkills.RemoveAt(index);
        var end = BuildSkillEndAck(m.Id, m.ObjType, skill.SkillId);
        foreach (var p in _state.PlayersAround(m)) p.Send(end);
    }

    /// <summary>C++ <c>CheckMaintainSkill</c> for every summon (run from its owner's timer).</summary>
    private void CheckMaintainSummons(Character ch, uint now)
    {
        foreach (var own in new IReadOnlyDictionary<uint, RecallMon>[] { ch.Recalls, ch.SelfObjs, ch.CompanionObjs })
            foreach (var m in own.Values)
                for (int i = 0; i < m.MaintainSkills.Count;)
                {
                    if (m.MaintainSkills[i].IsEnd(now)) EraseMaintainSummon(m, i);
                    else i++;
                }
    }
}
