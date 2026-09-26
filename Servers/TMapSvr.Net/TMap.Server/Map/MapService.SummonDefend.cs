using TMap.Data;
using TMap.Protocol;

namespace TMap.Server.Map;

/// <summary>
/// Summons as targets. A monster's hate can land on a summon (when its template is selectable); it then chases and
/// swings at the summon, and — its host being the summon's owner (the hate entry's host) — the owner's client
/// reports the hit with <c>CS_DEFEND_REQ</c> (target <c>OT_RECALL</c>/<c>OT_SELF</c>/<c>OT_COMPANION</c>). The C++
/// resolves that target among the reporter's own objects only (<c>FindTarget(pPlayer, …)</c>, TMapSvr.cpp:6441).
///
/// <para>The hit is the monster-vs-non-player roll (<c>GetAtkHitType</c>'s monster-defender branch, on the summon's
/// defend level) against the summon's defence — its stats row plus 55% of the owner's gear
/// (<c>CTRecallMon::GetDefendPower</c>). The summon goes into battle (<c>CTRecallMon::OnDamage</c>); at 0 HP it dies —
/// a summon through the world, a placed object here — and the monsters that hated it turn on its owner.</para>
/// </summary>
public sealed partial class MapService
{
    /// <summary>What a monster is after: a player, or one of a player's summons.</summary>
    private readonly record struct MonTarget(uint Id, byte Type, float X, float Y, float Z, Character? Player, RecallMon? Summon)
    {
        public uint OwnerId => Player?.CharId ?? Summon!.OwnerId;
    }

    /// <summary>The monster's live target, or null when it is gone or dead.</summary>
    private MonTarget? ResolveMonTarget(Monster mon)
    {
        if (mon.TargetId == 0) return null;
        byte type = mon.TargetType != 0 ? mon.TargetType : OtPc;
        if (type == OtPc)
            return _state.FindByChar(mon.TargetId) is { State: EnterState.InGame, Char: { Hp: > 0 } p }
                ? new MonTarget(p.CharId, OtPc, p.PosX, p.PosY, p.PosZ, p, null) : null;
        return _state.FindRecall(type, mon.TargetId) is { InMap: true, Hp: > 0 } m
               && _state.FindByChar(m.OwnerId) is { State: EnterState.InGame }
            ? new MonTarget(m.Id, m.ObjType, m.PosX, m.PosY, m.PosZ, null, m) : null;
    }

    /// <summary>A reported monster hit on one of the reporter's own summons (C++ <c>OnCS_DEFEND_REQ</c>, target a
    /// summon → <c>CTRecallMon::OnDamage</c>).</summary>
    private void MonsterHitsSummon(ClientSession reporter, Character owner, Monster mon, RecallMon target, ushort skillId,
        uint actId, uint aniId, float atkX, float atkY, float atkZ, float defX, float defY, float defZ, MonsterHitEcho echo)
    {
        var a = target.Attr ?? new MonAttrRow(0, 0, 0, 0, 0);
        int G(StatEngine.Ab x) => StatEngine.SumGetter(owner, x, _templates);
        uint dp = (uint)(a.RawDp + a.Wdp) + (uint)(Math.Max(G(StatEngine.Ab.Pdp), G(StatEngine.Ab.Mdp)) * RecallItemAbilityRate);
        uint defLevel = (uint)(a.DefendLevel + StatEngine.SumMagic(owner, 12, _templates));        // + ABILITY_DL

        byte hitType = HitTypeVsMonster(CombatRng, _templates.Formula(FtypePar), mon.Level, target.Level, defLevel,
            mon.CritProb, mon.AttackLevel);
        int lo = Math.Max((int)(mon.AtkMin - dp), 5), hi = Math.Max((int)(mon.AtkMax - dp), 7);
        uint roll = hitType switch
        {
            HtMiss => 0u,
            HtCritical => CritDamage(CombatRng, _templates.Formula(FtypePcd), (uint)hi),
            _ => (uint)(lo + CombatRng.Next(Math.Max(hi - lo, 1))),
        };
        uint dmg = Math.Min(roll, target.Hp);
        uint hpBefore = target.Hp;
        if (hitType != HtMiss) target.Hp -= dmg;

        if (target.Mode != MtBattle)                                    // CTRecallMon::OnDamage → ChgMode(MT_BATTLE)
        {
            target.Mode = MtBattle;
            var mode = new PacketWriter(Msg.CS_CHGMODE_ACK, capacity: 8);
            mode.WriteUInt32(target.Id); mode.WriteByte(target.ObjType); mode.WriteByte(MtBattle);
            var modeAck = mode.ToArray();
            foreach (var p in _state.PlayersAround(target)) p.Send(modeAck);
        }

        byte atkHit = hitType == HtMiss ? HtMiss : target.Hp == 0 ? HtLastHit : hitType;
        var hitAck = BuildMonsterHitAck(mon, target.Id, target.ObjType, dmg, atkHit, landed: hitType != HtMiss, skillId, 1,
            reporter.CharId, actId, aniId, atkX, atkY, atkZ, defX, defY, defZ, echo);
        foreach (var p in _state.PlayersAround(mon))
        {
            p.Send(hitAck);
            if (target.Hp != hpBefore) SendSummonHpMp(p, target);
        }
        if (target.Hp != 0) return;

        foreach (var p in _state.PlayersAround(target)) SendCS_DIE_ACK(p, target.Id, target.ObjType);
        switch (target.ObjType)
        {
            case RecallMon.OtSelf: DeleteSelfObj(owner, target.Id); break;
            case RecallMon.OtCompanion: SendMW_SPOLECNIKMONDEL_ACK(owner.CharId, reporter.Key, target.Id); break;
            default: SendMW_RECALLMONDEL_ACK(owner.CharId, reporter.Key, target.Id); break;
        }
    }
}
