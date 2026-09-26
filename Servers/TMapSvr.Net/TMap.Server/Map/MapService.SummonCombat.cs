using TMap.Data;
using TMap.Protocol;

namespace TMap.Server.Map;

/// <summary>
/// Summon combat — a summon or placed object attacking (C++ <c>OnCS_SKILLUSE_REQ</c> / <c>OnCS_FINISHSKILL_ACK</c>
/// with an <c>OT_RECALL</c> / <c>OT_SELF</c> attacker, CSHandler.cpp:2496-2528, 20266-20420). The owner's client drives
/// its summons: it announces their swings (<c>CS_SKILLUSE_REQ</c>) and reports what they hit
/// (<c>CS_FINISHSKILL_ACK</c>); only the owner is accepted for either.
///
/// <para>A summon casts its own chart skills, paying MP/HP from its own pool, with its own cooldowns. Its hits use its
/// stats row plus 55% of its owner's gear (C++ <c>CTRecallMon::GetMinAP</c>… <c>RECALL_ITEMABILITY_RATE</c>) — except a
/// "skill" placed object (Rain of Arrows, Ice Rain…), which hits with its owner's live figures. An auto-AI summon deals
/// ×3 damage; a doppelganger's fake hit always misses. The owner gets the credit: exp, loot and the monster's hate
/// (on the summon itself when its template is selectable, else on the owner).</para>
///
/// <para>Faithful, flagged: a summon's magic crit takes no gear bonus (the C++ reads the summon's own gear, which is
/// always empty). <b>Not ported:</b> summons hitting players (PvP), summon buffs, the owner's passive skills, and the
/// fireball mine's self-destruct (only on the classic <c>CS_DEFEND_REQ</c> path in the C++, which this client does not
/// use for summons).</para>
/// </summary>
public sealed partial class MapService
{
    private const float RecallItemAbilityRate = 0.55f;     // RECALL_ITEMABILITY_RATE

    /// <summary>The owner's summon or placed object with this id (C++ <c>FindRecallMon</c> / <c>FindSelfObj</c> on the
    /// sender), or null. A moving summon also needs the owner to be the main copy.</summary>
    private static RecallMon? OwnSummon(ClientSession s, Character ch, byte type, uint id) => type switch
    {
        RecallMon.OtRecall when s.IsMain => ch.Recalls.GetValueOrDefault(id),
        RecallMon.OtSelf => ch.SelfObjs.GetValueOrDefault(id),
        _ => null,
    };

    /// <summary>C++ <c>CTRecallMon::GetMinAP/GetMaxAP/GetMinMagicAP/…</c> (TRecallMon.cpp:178-367): the stats row, plus
    /// the owner's gear — AP/DP at 55% of the better of the physical and magic figure, levels and physical crit in full.</summary>
    private AttackerPower SummonPower(RecallMon m, SkillTemplate? tpl, Character? owner)
    {
        var a = m.Attr ?? new MonAttrRow(0, 0, 0, 0, 0);
        int G(StatEngine.Ab x) => owner is null ? 0 : StatEngine.SumGetter(owner, x, _templates);
        uint Share(StatEngine.Ab x, StatEngine.Ab y) => (uint)(Math.Max(G(x), G(y)) * RecallItemAbilityRate);
        int M(byte mtype) => owner is null ? 0 : StatEngine.SumMagic(owner, mtype, _templates);

        bool magic = tpl?.GetAttackType() == SkillTemplate.SatMagic;
        bool isLong = tpl?.IsLongAttack() ?? false;
        return new AttackerPower(
            ShortMin: (uint)(a.Ap + a.MinWap) + Share(StatEngine.Ab.MinAp, StatEngine.Ab.MinMap),
            ShortMax: (uint)(a.Ap + a.MaxWap) + Share(StatEngine.Ab.MaxAp, StatEngine.Ab.MaxMap),
            LongMin: (uint)(a.LongAp + a.MinWap) + Share(StatEngine.Ab.MinLap, StatEngine.Ab.MinMap),
            LongMax: (uint)(a.LongAp + a.MaxWap) + Share(StatEngine.Ab.MaxLap, StatEngine.Ab.MaxMap),
            MgMin: (uint)(a.MagicAp + a.MinWap) + Share(StatEngine.Ab.MinMap, StatEngine.Ab.MinAp),
            MgMax: (uint)(a.MagicAp + a.MaxWap) + Share(StatEngine.Ab.MaxMap, StatEngine.Ab.MaxAp),
            AttackLevel: magic ? (ushort)(a.MagicAtkLevel + M(86)) : (ushort)(a.AttackLevel + M(11)),   // MTYPE_MAL / MTYPE_AL
            Crit: magic ? a.CritMagicProb : (byte)(a.CritProb + M(13)),                                // MTYPE_CR (magic: none)
            IsMagic: magic, IsLong: isLong, Level: m.Level, Country: m.Country, AidCountry: m.AidCountry,
            Class: m.Template?.Class ?? 0);
    }

    /// <summary>The power a summon's hit uses: a "skill" placed object hits with its owner's live figures
    /// (CSHandler.cpp:20400), everything else with its own.</summary>
    private AttackerPower HitPower(RecallMon m, SkillTemplate? tpl, Character owner)
        => m.IsSelf && m.RecallType == TrecallSkill ? AttackerPower.Of(owner, _templates, tpl) with { Level = m.Level }
           : SummonPower(m, tpl, owner);

    // ================================ skill use ================================

    /// <summary>The summon branch of <c>OnCS_SKILLUSE_REQ</c>: the owner's client announces its summon's skill.</summary>
    private void SummonSkillUse(ClientSession s, Character ch, byte type, uint id, ushort skillId, byte actionId, uint actId,
        uint aniId, float x, float y, float z, (uint id, byte type)[] targets)
    {
        if (OwnSummon(s, ch, type, id) is not { InMap: true } m) return;
        if (m.Skills.FirstOrDefault(k => k.SkillId == skillId) is not { } skill)
        { SendSkillUseFail(s, SkillUseResult.NotFound, id, type, skillId, actionId, actId, aniId); return; }

        uint needMp = skill.GetRequiredMp(m.MaxMp);
        if (m.Mp < needMp) { SendSkillUseFail(s, SkillUseResult.NeedMp, id, type, skillId, actionId, actId, aniId); return; }
        uint needHp = skill.GetRequiredHp(m.MaxHp);
        if (m.Hp <= needHp) { SendSkillUseFail(s, SkillUseResult.NeedHp, id, type, skillId, actionId, actId, aniId); return; }
        if (!skill.CanUse(NowMs)) { SendSkillUseFail(s, SkillUseResult.SpeedyUse, id, type, skillId, actionId, actId, aniId); return; }

        skill.UseSkill(NowMs, m.Attr?.AtkSpeed ?? 0, 100);             // CTMonster::GetAtkSpeed = attr dwAtkSpeed
        if (needHp != 0 || needMp != 0) { m.Hp -= needHp; m.Mp -= needMp; }

        var p = SummonPower(m, skill.Template, ch);
        byte canSelect = type == RecallMon.OtRecall ? (m.Template?.CanSelect ?? 1) : (byte)1;
        var ack = BuildCS_SKILLUSE_ACK(SkillUseResult.Success, id, type, skillId, actionId, actId, aniId,
            skill.Level, backSkill: 0, p.AttackLevel, p.Level, p.PysMin, p.PysMax, p.MgMin, p.MgMax,
            transHp: 0, transMp: 0, curseProb: 0, equipSpecial: 0, canSelect, p.Country, p.AidCountry, p.Crit, x, y, z, targets);
        foreach (var viewer in _state.PlayersAround(m))
        {
            viewer.Send(ack);
            if (needHp != 0 || needMp != 0) SendSummonHpMp(viewer, m);
        }
    }

    // ================================ skill finish ================================

    /// <summary>The summon branch of <c>OnCS_FINISHSKILL_ACK</c>: each monster its owner's client says the summon hit
    /// takes the hit, credited to the owner.</summary>
    private void SummonFinishSkill(ClientSession s, Character ch, byte type, uint id, SkillTemplate tpl, bool fake,
        float x, float y, float z, (uint Id, byte Type)[] targets)
    {
        if (OwnSummon(s, ch, type, id) is not { InMap: true } m) return;
        var skill = m.Skills.FirstOrDefault(k => k.SkillId == tpl.Id);   // FindTSkill(m_wTriggerID)
        byte level = skill?.Level ?? 0;
        var power = HitPower(m, tpl, ch);
        byte canSelect = type == RecallMon.OtRecall ? (m.Template?.CanSelect ?? 1) : (byte)1;
        bool triple = type == RecallMon.OtRecall && m.RecallType == TrecallAutoAi;
        byte attackCountry = GetAttackCountry(m.Country, m.AidCountry);

        foreach (var (targetId, targetType) in targets)
        {
            if (targetType != Monster.OtMon) continue;                     // summons hitting players / summons: not ported
            if (_state.FindMonster(targetId) is not { Hp: > 0 } mon) continue;
            if (tpl.IsNegative && mon.Country != TcontryN && (attackCountry == TcontryB || attackCountry == mon.Country)) continue;
            HitMonster(ch, power, id, type, ch.CharId, tpl, level, canSelect, mon, 0, 0, tpl.Id,
                forceMiss: fake, triple, x, y, z, mon.PosX, mon.PosY, mon.PosZ);
        }
    }

    /// <summary>C++ <c>SendCS_HPMP_ACK</c> for a summon (its own id and type).</summary>
    private static void SendSummonHpMp(ClientSession p, RecallMon m)
    {
        var w = new PacketWriter(Msg.CS_HPMP_ACK, capacity: 24);
        w.WriteUInt32(m.Id); w.WriteByte(m.ObjType);
        w.WriteUInt32(m.MaxHp); w.WriteUInt32(m.Hp); w.WriteUInt32(m.MaxMp); w.WriteUInt32(m.Mp);
        p.Send(w);
    }
}
