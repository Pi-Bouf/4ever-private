using TMap.Data;
using TMap.Protocol;

namespace TMap.Server.Map;

/// <summary>
/// Skills — the CS_SKILLUSE caster-side attack announce (<c>OnCS_SKILLUSE_REQ</c>, CSHandler.cpp:2429). The
/// client sends this <b>first</b> when a player swings a skill/attack; the server validates, deducts the
/// <b>caster's own</b> MP/HP (never a target's — that's <c>CS_DEFEND</c>, Phase 13), arms the reuse
/// cooldown, and broadcasts <c>CS_SKILLUSE_ACK</c> (the attack-power payload the follow-up <c>CS_DEFEND</c>
/// reads back) + <c>CS_HPMP_ACK</c> (the cost) to the near players. Closes the two-packet attack.
///
/// <para><b>Phase-14 slice (documented — PORT_STATUS.md).</b> Caster = <c>OT_PC</c> only (OT_MON/OT_RECALL/
/// OT_SELF summon/recall objects unported). Guards ported: skill-known (<c>SKILL_NOTFOUND</c>), MP
/// (<c>&lt;</c> ⇒ <c>SKILL_NEEDMP</c>), HP (<c>&lt;=</c> ⇒ <c>SKILL_NEEDHP</c>), cooldown
/// (<c>SKILL_SPEEDYUSE</c>) — in the C++ order (skill → MP → HP → cooldown). Deferred guards (each gates an
/// unported subsystem): toggleable-recast, wrong-region, peace-zone, tournament/arena, premium-skill
/// medals, <c>CheckAttack</c> stun/hold buffs, <c>CheckPrevAct</c>, and <c>UseSkillItem</c> weapon/consumable.
/// The power payload uses the <b>physical-melee</b> path (<c>GetAttackType()</c>/<c>IsLongAttack()</c>,
/// derived from the skill-data rows <c>m_vData</c>, are deferred ⇒ every skill is treated as SAT_PHYSIC
/// short): <c>wAttackLevel = GetAttackLevel()</c>, phys AP from the melee set, <c>bCP = CriticalPysProb</c>;
/// magic AP is still populated. <c>wBackSkill</c> (weapon-durability <c>DurationDec</c>), <c>wTransHP/MP</c>
/// (SCT_HPTRANS/MPTRANS transform-cost), <c>bEquipSpecial</c> and aggro are 0/deferred. Target validation is
/// deferred — the requested target list is echoed verbatim.</para>
/// </summary>
public sealed partial class MapService
{
    // ---- TATTACK_DELAY (NetCode.h) — the m_bSpeedApply selector fallback when no template is linked. ----
    private const byte TadPhysical = 1;

    private void OnCS_SKILLUSE_REQ(ClientSession s, PacketReader r)
    {
        // Request layout (CSHandler.cpp:2459-2471) then bCount × {dwTarget, bTargetType, bIsTarget}.
        uint attackId = r.ReadUInt32();     // dwAttackID (the caster object)
        byte attackType = r.ReadByte();     // bAttackType
        r.ReadByte();                       // bChannel
        r.ReadUInt16();                     // wMapID
        ushort skillId = r.ReadUInt16();    // wSkillID
        byte actionId = r.ReadByte();       // bActionID
        uint actId = r.ReadUInt32();        // dwActID
        uint aniId = r.ReadUInt32();        // dwAniID
        float posX = r.ReadFloat(), posY = r.ReadFloat(), posZ = r.ReadFloat();
        byte count = r.ReadByte();          // bCount
        var targets = new (uint id, byte type)[count];
        for (int i = 0; i < count; i++)
        {
            uint tid = r.ReadUInt32();      // dwTarget
            byte ttype = r.ReadByte();      // bTargetType
            r.ReadByte();                   // bIsTarget
            targets[i] = (tid, ttype);
        }

        if (s.State != EnterState.InGame || s.Char is null) return;

        // This phase: only a player casting. Resolve the attacker player by id (C++ m_mapPLAYER lookup;
        // must be an in-game main char). A missing attacker ⇒ silent return, exactly as the C++.
        if (attackType != OtPc) return;
        if (_state.FindByChar(attackId) is not { State: EnterState.InGame, Char: { } ch } casterSession) return;

        // ---- skill known? (pATTACK->FindTSkill(wSkillID)) ----
        var skill = ch.Skills.FirstOrDefault(k => k.SkillId == skillId);
        if (skill is null) { SendSkillUseFail(s, SkillUseResult.NotFound, attackId, attackType, skillId, actionId, actId, aniId); return; }

        // ---- MP cost (GetRequiredMP vs GetPureMaxMP; strict <) ----
        uint needMp = skill.GetRequiredMp(StatEngine.PureMaxMp(ch, _templates));
        if (ch.Mp < needMp) { SendSkillUseFail(s, SkillUseResult.NeedMp, attackId, attackType, skillId, actionId, actId, aniId); return; }

        // ---- HP cost (GetRequiredHP vs GetPureMaxHP; <=, can't drop to/below 0) ----
        uint needHp = skill.GetRequiredHp(StatEngine.PureMaxHp(ch, _templates));
        if (ch.Hp <= needHp) { SendSkillUseFail(s, SkillUseResult.NeedHp, attackId, attackType, skillId, actionId, actId, aniId); return; }

        // ---- cooldown (non-mon caster: !CanUse ⇒ SPEEDYUSE) ----
        if (!skill.CanUse(NowMs)) { SendSkillUseFail(s, SkillUseResult.SpeedyUse, attackId, attackType, skillId, actionId, actId, aniId); return; }

        // ---- arm the cooldown (SkillUse), then deduct the caster's own HP/MP ----
        SkillUse(ch, skill, NowMs);
        if (needHp != 0 || needMp != 0) { ch.Hp -= needHp; ch.Mp -= needMp; }

        // Casting an offensive skill enters battle (C++ CSHandler.cpp:2918) — suppresses HP regen (Phase 16).
        if (skill.Template?.IsNegative ?? true) ch.EnterBattle(NowMs, RecoverInit);

        // ---- compute the attack-power payload (attack type / long from the skill data, Phase 15) ----
        bool isLong = skill.Template?.IsLongAttack() ?? false;
        bool isMagic = skill.Template?.GetAttackType() == SkillTemplate.SatMagic;
        ushort attackLevel = isMagic ? StatEngine.MagicAtkLevel(ch, _templates) : StatEngine.AttackLevel(ch, _templates);
        uint pysMin = StatEngine.MinAp(ch, arrow: isLong, _templates);
        uint pysMax = StatEngine.MaxAp(ch, arrow: isLong, _templates);
        uint mgMin = StatEngine.MinMagicAp(ch, _templates);
        uint mgMax = StatEngine.MaxMagicAp(ch, _templates);
        byte cp = isMagic ? StatEngine.CriticalMagicProb(ch, _templates) : StatEngine.CriticalPysProb(ch, _templates);

        var ack = BuildCS_SKILLUSE_ACK(SkillUseResult.Success, attackId, attackType, skillId, actionId, actId, aniId,
            skill.Level, backSkill: 0, attackLevel, ch.Level, pysMin, pysMax, mgMin, mgMax,
            transHp: 0, transMp: 0, curseProb: 0, equipSpecial: 0, canSelect: 1,
            ch.Country, ch.AidCountry, cp, posX, posY, posZ, targets);

        // ---- broadcast to the near players (GetNeerPlayer — includes the caster) ----
        foreach (var p in _state.NearView(casterSession))
        {
            p.Send(ack);
            if (needHp != 0 || needMp != 0) SendSkillCostHpMp(p, attackId, attackType, ch);
        }
    }

    /// <summary>C++ <c>CTObjBase::SkillUse</c> (TObjBase.cpp:4568): arm the standard cooldown with the
    /// caster's attack speed/rate, then (if the skill has a kind-delay) the shared same-kind group cooldown.</summary>
    private void SkillUse(Character ch, Skill skill, uint now)
    {
        byte speedApply = skill.Template?.SpeedApply ?? TadPhysical;
        uint rate = StatEngine.AtkSpeedRate(ch, speedApply, _templates);
        skill.UseSkill(now, StatEngine.AtkSpeed(ch, speedApply, _templates), rate);

        if (skill.Template is { KindDelay: > 0 and var kindDelay, Kind: var kind })
            foreach (var other in ch.Skills)
                if (other.Template?.Kind == kind) other.UseKind(now, kindDelay, rate);
    }

    // ---- senders ----

    /// <summary>A failure <c>CS_SKILLUSE_ACK</c> to the caster only (C++ 7-arg overload: result + echo, all
    /// power fields 0, an empty target list).</summary>
    private static void SendSkillUseFail(ClientSession s, SkillUseResult result, uint attackId, byte attackType,
        ushort skillId, byte actionId, uint actId, uint aniId)
        => s.Send(BuildCS_SKILLUSE_ACK(result, attackId, attackType, skillId, actionId, actId, aniId,
            skillLevel: 0, backSkill: 0, attackLevel: 0, attackerLevel: 0, pysMin: 0, pysMax: 0, mgMin: 0, mgMax: 0,
            transHp: 0, transMp: 0, curseProb: 0, equipSpecial: 0, canSelect: 0,
            attackCountry: 0, attackAid: 0, cp: 0, posX: 0, posY: 0, posZ: 0, targets: System.Array.Empty<(uint, byte)>()));

    /// <summary>C++ <c>SendCS_SKILLUSE_ACK</c> (CSSender.cpp:1552-1609) — byte-exact. Note <c>wBackSkill</c>
    /// sits right after <c>wSkillID</c> (the header comment is stale), and the trailing target list is
    /// <c>BYTE count</c> + count×{<c>DWORD id</c>, <c>BYTE type</c>}.</summary>
    private static byte[] BuildCS_SKILLUSE_ACK(SkillUseResult result, uint attackId, byte attackType, ushort skillId,
        byte actionId, uint actId, uint aniId, byte skillLevel, ushort backSkill, ushort attackLevel, byte attackerLevel,
        uint pysMin, uint pysMax, uint mgMin, uint mgMax, ushort transHp, ushort transMp, byte curseProb,
        byte equipSpecial, byte canSelect, byte attackCountry, byte attackAid, byte cp,
        float posX, float posY, float posZ, (uint id, byte type)[] targets)
    {
        var w = new PacketWriter(Msg.CS_SKILLUSE_ACK, capacity: 96);
        w.WriteByte((byte)result);    // bResult
        w.WriteUInt32(attackId);      // dwAttackID
        w.WriteByte(attackType);      // bAttackType
        w.WriteUInt16(skillId);       // wSkillID
        w.WriteUInt16(backSkill);     // wBackSkill  (right after wSkillID — the comment is stale)
        w.WriteByte(actionId);        // bActionID
        w.WriteUInt32(actId);         // dwActID
        w.WriteUInt32(aniId);         // dwAniID
        w.WriteByte(skillLevel);      // bSkillLevel
        w.WriteUInt16(attackLevel);   // wAttackLevel
        w.WriteByte(attackerLevel);   // bAttackerLevel
        w.WriteUInt32(pysMin);        // dwPysMinPower
        w.WriteUInt32(pysMax);        // dwPysMaxPower
        w.WriteUInt32(mgMin);         // dwMgMinPower
        w.WriteUInt32(mgMax);         // dwMgMaxPower
        w.WriteUInt16(transHp);       // wTransHP
        w.WriteUInt16(transMp);       // wTransMP
        w.WriteByte(curseProb);       // bCurseProb (always 0 in this build)
        w.WriteByte(equipSpecial);    // bEquipSpecial
        w.WriteByte(canSelect);       // bCanSelect
        w.WriteByte(attackCountry);   // bAttackCountry
        w.WriteByte(attackAid);       // bAttackAidCountry
        w.WriteByte(cp);              // bCP (crit prob)
        w.WriteFloat(posX); w.WriteFloat(posY); w.WriteFloat(posZ); // fGndPos
        w.WriteByte((byte)targets.Length); // bTargetCount
        foreach (var (id, type) in targets) { w.WriteUInt32(id); w.WriteByte(type); }
        return w.ToArray();
    }

    /// <summary>C++ <c>SendCS_HPMP_ACK</c> (CSSender.cpp:1315) for the skill's caster cost — id/type are the
    /// caster's <c>dwAttackID</c>/<c>bAttackType</c>, and the full (item+buff) MaxHP/MaxMP is reported.</summary>
    private void SendSkillCostHpMp(ClientSession p, uint attackId, byte attackType, Character ch)
    {
        var w = new PacketWriter(Msg.CS_HPMP_ACK, capacity: 24);
        w.WriteUInt32(attackId);
        w.WriteByte(attackType);
        w.WriteUInt32(MaxHpFor(ch));
        w.WriteUInt32(ch.Hp);
        w.WriteUInt32(MaxMpFor(ch));
        w.WriteUInt32(ch.Mp);
        p.Send(w);
    }
}
