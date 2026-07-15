using TMap.Data;
using TMap.Protocol;

namespace TMap.Server.Map;

/// <summary>
/// Combat — the damage-application spine: <c>OnCS_DEFEND_REQ</c> (CSHandler.cpp:1438) →
/// <c>CalcDamage</c> → <c>OnDamage</c> → death → respawn. A reported hit lands on a target; the server
/// re-derives the attacker's power, computes the base physical AP−DP roll, reduces the monster's HP,
/// broadcasts the hit (<c>CS_DEFEND_ACK</c>) + new HP (<c>CS_HPMP_ACK</c>) to nearby players, and on a kill
/// sends <c>CS_DIE_ACK</c>, removes the corpse (<c>CS_DELMON_ACK</c>), and re-arms the spawn slot to respawn
/// after its delay — closing the Phase-12 spawn loop.
///
/// <para>This phase = a PLAYER's basic PHYSICAL melee hit on a field MONSTER. Deferred (documented —
/// PORT_STATUS.md): the announce/cost/power-broadcast half of an attack (<c>CS_SKILLUSE_REQ</c>, sent by the
/// client first) is now ported in Phase 14 — see <c>MapService.Skills.cs</c>; the server re-derives power
/// here regardless.
///
/// <para><b>Phase 28 (combat quality):</b> the hit is now resolved through <c>GetAtkHitType</c> — a level-scaled
/// accuracy roll (miss vs hit) then a crit roll on the attacker's crit rate — and the damage takes the physical
/// OR <b>magic</b> branch (magic AP vs the monster's magic DP), with the C++ crit-damage formulas
/// (<c>FTYPE_PCD</c>/<c>FTYPE_MCD</c>). A miss deals 0 and carries an empty damage map. The shield-block roll
/// (<c>GetShieldDP</c>) is ported in Phase 41 (<see cref="StatEngine.ShieldBlockDp"/>) and wired live into the
/// monster→player PC-defender path (<c>MapService.AI.cs</c>); here the defender is a monster, which has no
/// equipped-item model, so its shield DP is 0. Still deferred: the skill hit-test / premium / guild /
/// boss-special early-outs of <c>GetAtkHitType</c> (their skill flags aren't loaded — the level-based accuracy
/// path is used); PvP; buff/pet damage layers; loot regen.</para>
///
/// <para>RNG is .NET <see cref="System.Random"/> (<see cref="CombatRng"/>, seedable) — the hit/crit/roll LOGIC
/// is exact, the value is not (never wire-comparable to a C++ instance). The hit is broadcast to the monster's
/// 3×3 view rather than the tighter C++ <c>GetNeerPlayer</c> distance set.</para>
/// </summary>
public sealed partial class MapService
{
    private const byte HtMiss = 0;       // HIT_TYPE HT_MISS
    private const byte HtNormal = 1;     // HIT_TYPE HT_NORMAL
    private const byte HtCritical = 2;   // HIT_TYPE HT_CRITICAL
    private const byte HtBlock = 3;      // HIT_TYPE HT_BLOCK — set when the defender's shield roll succeeds (Phase 41)
    private const byte HtLastHit = 4;    // HIT_TYPE HT_LASTHIT — the kill signal carried in the bAtkHit field
    private const byte MtypeDamage = 30; // MAGIC_TYPE MTYPE_DAMAGE — the DEFEND_ACK damage-map key (m_bExec)

    // FORMULA_TYPE (NetCode.h): the level-scaled hit-rate formulas (physical/magic attack success) and the
    // crit-damage formulas (physical/magic critical damage).
    private const byte FtypePar = 30, FtypeMar = 31, FtypePcd = 32, FtypeMcd = 33;

    /// <summary>The combat RNG (the damage-spread roll). Seedable for tests; not C <c>rand()</c>.</summary>
    public Random CombatRng { get; set; } = new();

    private void OnCS_DEFEND_REQ(ClientSession s, PacketReader r)
    {
        // Read the full 33-field request (CSHandler.cpp:1485); this phase uses only the marked fields.
        uint hostId = r.ReadUInt32();         // dwHostID (kept for a PC attacker; C++ CSHandler.cpp:1523 only overwrites it for a monster host)
        uint attackId = r.ReadUInt32();       // dwAttackID
        uint targetId = r.ReadUInt32();       // dwTargetID  (the monster)
        byte attackType = r.ReadByte();       // bAttackType
        byte targetType = r.ReadByte();       // bTargetType
        r.ReadUInt16();                       // wAttackPartyID
        uint actId = r.ReadUInt32();          // dwActID
        uint aniId = r.ReadUInt32();          // dwAniID
        r.ReadByte();                         // bChannel
        r.ReadUInt16();                       // wMapID
        byte attackerLevel = r.ReadByte();    // bAttackerLevel
        r.ReadUInt32(); r.ReadUInt32();       // dwPysMinPower, dwPysMaxPower (server re-derives below)
        r.ReadUInt32(); r.ReadUInt32();       // dwMgMinPower, dwMgMaxPower
        r.ReadUInt16(); r.ReadUInt16();       // wTransHP, wTransMP
        r.ReadByte();                         // bCurseProb
        r.ReadByte();                         // bEquipSpecial
        byte canSelect = r.ReadByte();        // bCanSelect
        r.ReadByte();                         // bAttackCountry  (client value ignored — C++ re-derives from the attacker)
        r.ReadByte();                         // bAttackAidCountry (re-derived)
        r.ReadUInt16();                       // wAttackLevel     (re-derived server-side, CSHandler.cpp:1587)
        r.ReadByte();                         // bCP (crit prob — deferred)
        ushort skillId = r.ReadUInt16();      // wSkillID
        byte skillLevel = r.ReadByte();       // bSkillLevel
        float atkX = r.ReadFloat(), atkY = r.ReadFloat(), atkZ = r.ReadFloat();
        float defX = r.ReadFloat(), defY = r.ReadFloat(), defZ = r.ReadFloat();
        r.ReadUInt32();                       // dwRemainTick

        if (s.State != EnterState.InGame || s.Char is not { } ch) return;
        if (attackType != OtPc) return;

        // Resolve the attacking skill (C++ FindTSkill(m_wTriggerID); for a basic attack triggerID == wSkillID).
        var atkSkill = ch.Skills.FirstOrDefault(k => k.SkillId == skillId);

        // ---- PC→PC: a positive maintain-type skill applies a buff, and/or a cure skill dispels/heals, on
        // self/an ally (no PvP damage). C++ Defend runs MaintainSkill + PerformSkill(SDT_CURE) both. ----
        if (targetType == OtPc)
        {
            if (atkSkill?.Template is { } tpl && atkSkill is { Level: var lvl })
            {
                if (tpl.IsMaintainType() && tpl.IsPositive)
                    ApplyPlayerMaintain(s, ch, targetId, tpl, lvl, attackId, atkX, atkY, atkZ);
                if (tpl.HasCure())
                    ApplyPlayerCure(s, ch, targetId, tpl, lvl, attackId, atkX, atkY, atkZ);
            }
            return;
        }

        // Otherwise this phase covers only a player attacking a live field monster.
        if (targetType != Monster.OtMon) return;
        if (_state.FindMonster(targetId) is not { Hp: > 0 } mon) return;

        // ---- CalcDamage — physical or magic AP−DP roll, hit-type resolved, then the skill's MTYPE_DAMAGE scaling ----
        bool isMagic = atkSkill?.Template?.GetAttackType() == SkillTemplate.SatMagic;
        bool isLong = atkSkill?.Template?.IsLongAttack() ?? false;    // long/bow ⇒ the FTYPE_LAP ability set

        // The attacker's power band + the crit rate / attack level, and the defender's matching defence — all
        // by physical vs magic (C++ CalcDamage: SATT_PHYSIC/LONG ⇒ GetDefendPower; magic attr ⇒ GetMagicDefPower).
        uint apMin, apMax, dp;
        byte critRate;
        ushort attackLevel;
        if (isMagic)
        {
            apMin = StatEngine.MinMagicAp(ch, _templates);
            apMax = StatEngine.MaxMagicAp(ch, _templates);
            dp = mon.MagicDefPower;                   // + GetShieldMDP() == 0 (a monster has no shield)
            critRate = StatEngine.CriticalMagicProb(ch, _templates);
            attackLevel = StatEngine.MagicAtkLevel(ch, _templates);
        }
        else
        {
            apMin = StatEngine.MinAp(ch, arrow: isLong, _templates);
            apMax = StatEngine.MaxAp(ch, arrow: isLong, _templates);
            dp = mon.DefendPower;                     // + GetShieldDP() == 0
            critRate = StatEngine.CriticalPysProb(ch, _templates);
            attackLevel = StatEngine.AttackLevel(ch, _templates);
        }
        int a = Math.Max((int)(apMin - dp), 5);       // C++ max(int(dwMin - dwDefendPower), 5) — unsigned wrap → int
        int b = Math.Max((int)(apMax - dp), 7);

        // GetAtkHitType (on the defender = the monster): a level-scaled accuracy roll then a crit roll.
        byte hitType = HitTypeVsMonster(CombatRng, _templates.Formula(isMagic ? FtypeMar : FtypePar),
            attackerLevel, mon.Level, isMagic ? mon.MagicDefLevel : mon.DefendLevel, critRate, attackLevel);

        // Base damage by hit type: MISS ⇒ 0; NORMAL ⇒ the range roll; CRIT ⇒ the FTYPE_PCD/MCD formula
        // (physical off the max dwB, magic off the min dwA — C++ CalcDamage TObjBase.cpp:395-398 / 560-563).
        uint baseDmg = hitType switch
        {
            HtMiss => 0u,
            HtCritical => isMagic ? CritDamage(CombatRng, _templates.Formula(FtypeMcd), (uint)a)
                                  : CritDamage(CombatRng, _templates.Formula(FtypePcd), (uint)b),
            _ => (uint)(a + CombatRng.Next(Math.Max(b - a, 1))),  // dwA + rand() % max(dwB - dwA, 1)
        };
        // The instance-skill damage row scales a LANDED hit (SVI_INCREASE/MULTIPLY/PERCENT/…). A basic attack
        // (no skill / no MTYPE_DAMAGE row) leaves the raw roll unchanged. Maintain/remain buffs + cure +
        // ApplyEffectionBuff + DistributeSkill (pet share) are deferred (buffs/pets unported).
        uint scaled = hitType == HtMiss ? 0u
            : atkSkill?.Template is { } st ? st.ScaleDamage(atkSkill.Level, baseDmg) : baseDmg;
        uint dmg = (uint)Math.Min((int)scaled, (int)mon.Hp); // clamp to remaining HP

        // Combat entry (C++ ChgMode(MT_BATTLE) on both sides) + aggro happen on any swing, hit or miss:
        // suppresses HP regen + resets the recover anchors (Phase 16); the monster chases the attacker (Phase 19).
        ch.EnterBattle(NowMs, RecoverInit);
        mon.EnterBattle(NowMs, RecoverInit);
        mon.AddDamage(ch.CharId, ch.GetPartyId(), dmg); // track the loot/exp owner — party bucket if partied (Phase 17/38); a miss adds 0
        mon.TargetId = ch.CharId;

        if (hitType != HtMiss) mon.Hp -= dmg;

        // ---- broadcast the hit + the new HP bar to the players who can see the monster ----
        // bAtkHit carries the hit result (HT_MISS/NORMAL/CRITICAL), overridden to HT_LASTHIT on the killing blow.
        byte atkHit = hitType == HtMiss ? HtMiss : (mon.Hp == 0 ? HtLastHit : hitType);
        // Byte-audit fixes (Phase 28): bHit carries the attacker's crit prob (bCP, not 0); bPerform is FALSE on
        // a miss; bSkillLevel is the server skill level (not the client's); the damage map is empty on 0 damage.
        byte ackSkillLevel = atkSkill?.Level ?? skillLevel;

        // ---- apply a debuff maintain to the monster (C++ Defend: MaintainSkill + PushMaintainSkill on a landed
        // hit) — a skill can both damage and debuff; the ACK's bIsMaintain/dwMaintainTick announce it (Phase 31). ----
        byte isMaintain = 0; uint maintainTick = 0;
        if (hitType != HtMiss && atkSkill?.Template is { } dbt && dbt.IsMaintainType())
        {
            var snap = new MaintainSnapshot(ch.CharId, OtPc, hostId, OtPc, critRate, attackLevel, attackerLevel,
                isMagic ? 0 : apMin, isMagic ? 0 : apMax, isMagic ? apMin : 0, isMagic ? apMax : 0,
                canSelect, ch.Country, atkX, atkY, atkZ);
            if (ApplyMaintainToMonster(mon, dbt, ackSkillLevel, 0, snap, NowMs) is { } applied)
            {
                isMaintain = 1;
                maintainTick = applied.MaintainTick;   // C++ dwRemainTick ? … : GetMaintainTick(); remain 0 here
            }
        }

        var defendAck = BuildCS_DEFEND_ACK(attackId, hostId, mon, attackType, actId, aniId,
            attackLevel, attackerLevel, apMin, apMax, isMagic, critRate, canSelect, ch.Country, ch.AidCountry,
            skillId, ackSkillLevel, atkHit, hitType != HtMiss, atkX, atkY, atkZ, defX, defY, defZ, dmg,
            isMaintain, maintainTick);
        foreach (var p in _state.PlayersAround(mon))
        {
            p.Send(defendAck);
            SendMonsterHpMp(p, mon);
        }

        // ---- death → despawn → respawn re-arm ----
        if (hitType != HtMiss && mon.Hp == 0) OnMonsterDeath(mon);
    }

    // ---- hit-type resolution (C++ CTObjBase::GetAtkHitType, TObjBase.cpp:2266) ----

    /// <summary>Resolves the hit type against a MONSTER defender (the else-of-<c>OT_PC</c> branch of
    /// <c>GetAtkHitType</c>): a level-scaled accuracy roll <c>dwAtk</c> from the <c>FTYPE_PAR</c>/<c>MAR</c>
    /// formula vs the monster's defend-level and the attacker's attack level (clamped ≥ 20), then — on a hit —
    /// a crit roll on the attacker's crit rate. The monster's <c>GetAvoidProb()</c> is 0 (it holds no item
    /// enchants), so the negative-skill avoid term drops out. The skill hit-test / premium / guild / boss-special
    /// early-outs are deferred (those skill flags aren't loaded) — the accuracy path is always taken.</summary>
    public static byte HitTypeVsMonster(Random rng, FormulaRow? par, byte attackerLevel, byte monsterLevel,
        uint monsterDefLevel, byte critRate, ushort attackLevel)
    {
        if (critRate == 0xFF) return HtMiss;   // C++ bCR == 0xFF sentinel (also MT_GOHOME, which a monster never enters here)
        if (par is null) return HtNormal;      // no formula chart (DB-free) ⇒ always connects, matching the stat-sheet fallbacks
        int wAL = attackLevel == 0 ? 1 : attackLevel;
        int nL = monsterLevel - attackerLevel;
        double val = Math.Max(par.RateY - Math.Pow(par.RateX, nL) * monsterDefLevel / wAL, 0.0) * 100.0;
        uint dwAtk = (uint)Math.Min((double)par.Init, val);  // C++ DWORD(min(dwInit, max(...)*100))
        uint atk = dwAtk > 0 ? Math.Max(dwAtk, 20u) : 20u;   // dwAVP = 0 ⇒ (dwAtk>0 ? max(dwAtk,20) : 20)
        if (rng.Next(100) < atk)
            return rng.Next(100) < critRate ? HtCritical : HtNormal;
        return HtMiss;
    }

    /// <summary>Resolves the hit type against a PLAYER defender (the <c>OT_PC</c> branch of
    /// <c>GetAtkHitType</c>, TObjBase.cpp:2311): a PC is never dodged by the level formula — the swing connects
    /// unless the attacker's attack level is 0/1 — then a crit roll on the attacker's crit rate.</summary>
    public static byte HitTypeVsPlayer(Random rng, byte critRate, ushort attackLevel)
    {
        if (critRate == 0xFF) return HtMiss;
        if (attackLevel is 0 or 1) return HtMiss;   // C++ !wAL || wAL == 1
        return rng.Next(100) < critRate ? HtCritical : HtNormal;
    }

    /// <summary>The C++ crit-damage bonus (CalcDamage TObjBase.cpp:398/563): <c>base + (base · (fRateX +
    /// rand%max(dwInit,1)) / 100)</c> from the given crit formula (<c>FTYPE_PCD</c> physical off the max band,
    /// <c>FTYPE_MCD</c> magic off the min band). Float arithmetic + a single toward-zero truncation, matching
    /// the C++ <c>DWORD(...)</c>.</summary>
    public static uint CritDamage(Random rng, FormulaRow? f, uint baseVal)
    {
        float rateX = f?.RateX ?? 0f;
        uint init = f?.Init ?? 0;
        int r = rng.Next((int)Math.Max(init, 1u));   // rand() % max(dwInit, 1)
        return baseVal + (uint)((float)baseVal * (rateX + r) / 100f);
    }

    // ---- senders ----

    /// <summary>C++ <c>SendCS_DEFEND_ACK</c> (CSSender.cpp:1262, call site TObjBase.cpp:1160) — byte-exact. The
    /// wire field named <c>bHit</c> carries the attacker crit-prob (<c>bCP</c>); <c>bAtkHit</c> carries the hit
    /// result (<c>HT_MISS</c>/<c>NORMAL</c>/<c>CRITICAL</c>, or <c>HT_LASTHIT</c> on the killing blow);
    /// <c>bPerform</c> is <c>(bPerform==PERFORM_SUCCESS?TRUE:FALSE)</c> ⇒ 1 on a landed hit, 0 on a miss; the
    /// trailing damage map is keyed by <c>m_bExec</c> (<c>MTYPE_DAMAGE</c>) and is empty unless damage &gt; 0
    /// (C++ <c>if(dwValue)</c>). The power band goes to the magic or physical fields per <paramref name="isMagic"/>.</summary>
    private static byte[] BuildCS_DEFEND_ACK(uint attackId, uint hostId, Monster mon, byte attackType, uint actId, uint aniId,
        ushort attackLevel, byte attackerLevel, uint apMin, uint apMax, bool isMagic, byte critProb, byte canSelect,
        byte attackCountry, byte attackAid, ushort skillId, byte skillLevel, byte atkHit, bool landed,
        float atkX, float atkY, float atkZ, float defX, float defY, float defZ, uint dmg,
        byte isMaintain = 0, uint maintainTick = 0)
    {
        var w = new PacketWriter(Msg.CS_DEFEND_ACK, capacity: 96);
        w.WriteUInt32(attackId);      // dwAttackID
        w.WriteUInt32(mon.Id);        // dwTargetID
        w.WriteByte(attackType);      // bAttackType
        w.WriteByte(Monster.OtMon);   // bTargetType
        w.WriteUInt32(hostId);        // dwHostID (the client-sent host; not overwritten for a PC attacker)
        w.WriteByte(OtPc);            // bHostType
        w.WriteUInt32(actId);         // dwActID
        w.WriteUInt32(aniId);         // dwAniID
        w.WriteByte(isMaintain);      // bIsMaintain — TRUE when this hit also applied a debuff (Phase 31)
        w.WriteUInt32(maintainTick);  // dwMaintainTick — the debuff's duration
        w.WriteByte(critProb);        // bHit == bCP (attacker crit prob)
        w.WriteByte(atkHit);          // bAtkHit (== the hit result / HT_LASTHIT on kill)
        w.WriteUInt16(attackLevel);   // wAttackLevel
        w.WriteByte(attackerLevel);   // bAttackerLevel
        w.WriteUInt32(isMagic ? 0 : apMin);   // dwPysMinPower
        w.WriteUInt32(isMagic ? 0 : apMax);   // dwPysMaxPower
        w.WriteUInt32(isMagic ? apMin : 0);   // dwMgMinPower
        w.WriteUInt32(isMagic ? apMax : 0);   // dwMgMaxPower
        w.WriteByte(canSelect);       // bCanSelect
        w.WriteByte(0);               // bCancelCharge
        w.WriteByte(attackCountry);   // bAttackCountry
        w.WriteByte(attackAid);       // bAttackAidCountry
        w.WriteUInt16(skillId);       // wSkillID
        w.WriteByte(skillLevel);      // bSkillLevel (server skill level)
        w.WriteUInt16(0);             // wBackSkillID
        w.WriteByte((byte)(landed ? 1 : 0)); // bPerform — 1 on a landed hit, 0 on a miss (PERFORM_MISS)
        w.WriteFloat(atkX); w.WriteFloat(atkY); w.WriteFloat(atkZ);
        w.WriteFloat(defX); w.WriteFloat(defY); w.WriteFloat(defZ);
        if (dmg > 0)
        {
            w.WriteByte(1);               // damage-map count (a single MTYPE_DAMAGE entry)
            w.WriteByte(MtypeDamage);     // key = MTYPE_DAMAGE
            w.WriteUInt32((ushort)dmg);   // damage — C++ stores (WORD)dwValue then widens, so it's masked to 16 bits
        }
        else
        {
            w.WriteByte(0);               // empty damage map on a miss (CalcDamage not called)
        }
        return w.ToArray();
    }

    private static void SendMonsterHpMp(ClientSession p, Monster mon)
    {
        var w = new PacketWriter(Msg.CS_HPMP_ACK, capacity: 24);
        w.WriteUInt32(mon.Id);
        w.WriteByte(Monster.OtMon);
        w.WriteUInt32(mon.MaxHp);
        w.WriteUInt32(mon.Hp);
        w.WriteUInt32(mon.MaxMp);
        w.WriteUInt32(mon.Hp == 0 ? 0 : mon.Mp);  // C++ sends (m_dwHP ? m_dwMP : 0)
        p.Send(w);
    }

    private static void SendCS_DIE_ACK(ClientSession p, uint id, byte type)
    {
        var w = new PacketWriter(Msg.CS_DIE_ACK, capacity: 8);
        w.WriteUInt32(id);
        w.WriteByte(type);
        p.Send(w);
    }
}
