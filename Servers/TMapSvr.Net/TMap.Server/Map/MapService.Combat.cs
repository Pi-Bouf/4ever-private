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
    // The rest of the CalcDamage exec dispatch (Phase 42): MTYPE_MDAMAGE = MP damage; MTYPE_HP/MTYPE_MP = direct
    // HP/MP heal (nInc≥0) or drain (nInc<0); MTYPE_HI/MTYPE_MI = post-damage HP/MP lifedrain (→ MW_GETBLOOD_ACK).
    private const byte MtypeMdamage = 88, MtypeHp = 14, MtypeMp = 22, MtypeHi = 38, MtypeMi = 40;
    // SKILL_DATA_TYPE SDT_ABILITY + SKILL_DATA_ATTR (SATT_*) — the per-data-row type + attack-attr the loop reads.
    private const byte SdtAbility = 1;
    private const byte SattNone = 0, SattLong = 2, SattMagicNo = 3;   // magic attrs = SATT_MAGICNO..SATT_MAGICIR (3..9)

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
        uint mgMin = r.ReadUInt32(), mgMax = r.ReadUInt32();   // dwMgMinPower, dwMgMaxPower (echoed for a monster hit)
        ushort transHp = r.ReadUInt16(), transMp = r.ReadUInt16();  // wTransHP/wTransMP — the SCT_HPTRANS/MPTRANS source
        r.ReadByte();                         // bCurseProb
        r.ReadByte();                         // bEquipSpecial
        byte canSelect = r.ReadByte();        // bCanSelect
        r.ReadByte();                         // bAttackCountry  (client value ignored — C++ re-derives from the attacker)
        byte aidCountry = r.ReadByte();       // bAttackAidCountry (re-derived for a player; echoed for a monster hit)
        r.ReadUInt16();                       // wAttackLevel     (re-derived server-side, CSHandler.cpp:1587)
        r.ReadByte();                         // bCP (crit prob — deferred)
        ushort skillId = r.ReadUInt16();      // wSkillID
        byte skillLevel = r.ReadByte();       // bSkillLevel
        float atkX = r.ReadFloat(), atkY = r.ReadFloat(), atkZ = r.ReadFloat();
        float defX = r.ReadFloat(), defY = r.ReadFloat(), defZ = r.ReadFloat();
        r.ReadUInt32();                       // dwRemainTick

        if (s.State != EnterState.InGame || s.Char is not { } ch) return;
        if (attackType == Monster.OtMon)
        {
            OnMonsterHitReport(s, ch, attackId, targetId, targetType, skillId, actId, aniId,
                atkX, atkY, atkZ, defX, defY, defZ, new MonsterHitEcho(canSelect, mgMin, mgMax, aidCountry));
            return;
        }
        if (attackType != OtPc) return;

        PlayerHitsTarget(s, ch, hostId, attackId, attackType, targetId, targetType, actId, aniId, attackerLevel,
            transHp, transMp, canSelect, skillId, skillLevel, atkX, atkY, atkZ, defX, defY, defZ);
    }

    /// <summary>C++ <c>OnCS_DEFEND_REQ</c>, monster attacker (CSHandler.cpp:1522-1587). A host client reports the
    /// moment one of its monsters' swings lands; this is where monster damage is dealt. The reporter becomes
    /// the host; the attacking monster must own the skill (<c>pATTACK-&gt;FindTSkill</c>), whose level is the
    /// monster's own — always 1 (TAICmdRegen.cpp:94); power, crit and attack level are re-derived from the
    /// monster, so nothing the client sends about strength is trusted.
    /// <para>Not in the C++, kept as a guard: a monster already dead does not land a hit. Monster → monster /
    /// summon targets are not ported.</para></summary>
    private void OnMonsterHitReport(ClientSession s, Character reporter, uint monId, uint targetId, byte targetType,
        ushort skillId, uint actId, uint aniId, float atkX, float atkY, float atkZ, float defX, float defY, float defZ,
        MonsterHitEcho echo)
    {
        if (!_templates.Skills.TryGetValue(skillId, out var tpl)) return;                     // FindTSkill
        if (tpl.MapId != 0xFFFF && tpl.MapId != reporter.MapId) return;                      // INVALID_MAPID = any map
        if (_state.FindMonster(monId) is not { Hp: > 0 } mon) return;
        if (!_templates.MonsterTemplates.TryGetValue(mon.ChartId, out var chart) || !chart.Skills.Contains(skillId))
            return;                                                                          // the monster's own skill
        if (targetType != OtPc) return;

        // FindTarget(pPlayer, OT_PC, id): a player on the reporter's map; a dead one cannot be hit.
        if (_state.FindByChar(targetId) is not { State: EnterState.InGame, Char: { Hp: > 0 } target } ts
            || ts.Channel != s.Channel || target.MapId != reporter.MapId)
            return;

        MonsterHitsPlayer(mon, target, s.CharId, skillId, skillLevel: 1, actId, aniId,
            atkX, atkY, atkZ, defX, defY, defZ, _tickSeconds * 1000L, echo);
    }

    /// <summary>
    /// One player hit on one target — the body of C++ <c>CTObjBase::Defend</c> as the port drives it. Shared by
    /// the two front doors that reach it: the classic <c>CS_DEFEND_REQ</c> (which this build's client uses only
    /// for monster attackers and a short skill-exceptions list) and <c>CS_FINISHSKILL_ACK</c> (the packet that
    /// carries every ordinary player attack in this build). Attack power, crit rate and attack level are always
    /// re-derived from the attacker server-side; the caller supplies only what the wire actually determines.
    /// </summary>
    private void PlayerHitsTarget(ClientSession s, Character ch, uint hostId, uint attackId, byte attackType,
        uint targetId, byte targetType, uint actId, uint aniId, byte attackerLevel, ushort transHp, ushort transMp,
        byte canSelect, ushort skillId, byte skillLevel,
        float atkX, float atkY, float atkZ, float defX, float defY, float defZ)
    {
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
                    ApplyPlayerCure(s, ch, targetId, tpl, lvl, attackId, transHp, transMp, atkX, atkY, atkZ);
                if (tpl.HasVitalsStatus())
                    ApplyPlayerStatus(s, ch, targetId, tpl, lvl, attackId, atkX, atkY, atkZ);
            }
            return;
        }

        // Otherwise this phase covers only a player attacking a live field monster.
        if (targetType != Monster.OtMon) return;
        if (_state.FindMonster(targetId) is not { Hp: > 0 } mon) return;

        var atkTpl = atkSkill?.Template;
        // The DISPLAY attack profile (by the skill's overall attack type) — fills CS_DEFEND_ACK and drives the
        // single GetAtkHitType roll. Each damage COMPONENT below re-picks the physical/magic band from its own
        // data-row attr (a skill can carry both — C++ CalcDamage switches attr per row).
        bool isMagic = atkTpl?.GetAttackType() == SkillTemplate.SatMagic;
        bool isLong = atkTpl?.IsLongAttack() ?? false;    // long/bow ⇒ the FTYPE_LAP ability set
        uint apMin, apMax; byte critRate; ushort attackLevel;
        if (isMagic)
        {
            apMin = StatEngine.MinMagicAp(ch, _templates); apMax = StatEngine.MaxMagicAp(ch, _templates);
            critRate = StatEngine.CriticalMagicProb(ch, _templates); attackLevel = StatEngine.MagicAtkLevel(ch, _templates);
        }
        else
        {
            apMin = StatEngine.MinAp(ch, arrow: isLong, _templates); apMax = StatEngine.MaxAp(ch, arrow: isLong, _templates);
            critRate = StatEngine.CriticalPysProb(ch, _templates); attackLevel = StatEngine.AttackLevel(ch, _templates);
        }

        // GetAtkHitType (on the defender = the monster): a level-scaled accuracy roll then a crit roll. Decided
        // ONCE per hit (C++ attacker-side, pre-Defend); every damage component then applies the same result.
        byte hitType = HitTypeVsMonster(CombatRng, _templates.Formula(isMagic ? FtypeMar : FtypePar),
            attackerLevel, mon.Level, isMagic ? mon.MagicDefLevel : mon.DefendLevel, critRate, attackLevel);
        byte ackSkillLevel = atkSkill?.Level ?? skillLevel;

        // ---- CalcDamage — the exec-aware per-data-row dispatch (HP/MP damage + heal/drain), or the basic
        // fallback when the skill has no damage rows (a basic attack / a pure buff / DB-free). ----
        var dmg = CalcMonsterDamage(ch, mon, atkTpl, ackSkillLevel, hitType, isMagic, isLong);

        // Combat entry (C++ ChgMode(MT_BATTLE)) + aggro happen on any swing, hit or miss. The attacker enters
        // battle here; the monster's mode/target are driven by the hate table (C++ Defend's opening SetAggro,
        // Phase 44): Aggravate accumulates the attacker's hate and, if it wins the 10% sticky-target rule, flips
        // the monster into battle onto them (ApplyRetarget → EnterBattle + CS_MONHOST_ACK).
        ch.EnterBattle(NowMs, RecoverInit);
        Aggravate(mon, ch, atkTpl, ackSkillLevel, canSelect, hostId, attackId);
        uint hpBefore = mon.Hp;
        ApplyMonsterDamage(mon, dmg);   // C++ OnDamage: HP/MP damage floors at 0, heal clamps to max
        // Loot/exp owner = the HP actually removed this swing (party bucket if partied — Phase 17/38; 0 on a miss/heal).
        mon.AddDamage(ch.CharId, ch.GetPartyId(), hpBefore - mon.Hp);

        // bAtkHit carries the hit result (HT_MISS/NORMAL/CRITICAL), overridden to HT_LASTHIT on the killing blow.
        byte atkHit = hitType == HtMiss ? HtMiss : (mon.Hp == 0 ? HtLastHit : hitType);

        // ---- apply a debuff maintain to the monster (C++ Defend: MaintainSkill + PushMaintainSkill on a landed
        // hit) — a skill can both damage and debuff; the ACK's bIsMaintain/dwMaintainTick announce it (Phase 31). ----
        byte isMaintain = 0; uint maintainTick = 0;
        if (hitType != HtMiss && atkTpl is { } dbt && dbt.IsMaintainType())
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

        // ---- lifedrain (C++ PerformSkill SDT_ABILITY MTYPE_HI/MI) — a fraction of the HP/MP damage dealt is
        // sent to the world as MW_GETBLOOD_ACK, which grants it to the attacker (post-damage). ----
        if (hitType != HtMiss && atkTpl is { } lt) SendLifeDrain(lt, ackSkillLevel, attackId, attackType, hostId, dmg);

        // ---- broadcast the hit (with the full per-exec damage map) + the new HP/MP bar to nearby players ----
        // Byte-audit (Phase 28): bHit carries the attacker's crit prob; bPerform is FALSE on a miss; the damage
        // map is keyed by each component's m_bExec (MTYPE_DAMAGE 30 / MTYPE_MDAMAGE 88 / MTYPE_HP 14 / MTYPE_MP 22).
        var defendAck = BuildCS_DEFEND_ACK(attackId, hostId, mon, attackType, actId, aniId,
            attackLevel, attackerLevel, apMin, apMax, isMagic, critRate, canSelect, ch.Country, ch.AidCountry,
            skillId, ackSkillLevel, atkHit, hitType != HtMiss, atkX, atkY, atkZ, defX, defY, defZ, dmg.Map,
            isMaintain, maintainTick);
        foreach (var p in _state.PlayersAround(mon))
        {
            p.Send(defendAck);
            SendMonsterHpMp(p, mon);
        }

        // ---- death → despawn → respawn re-arm ----
        if (hitType != HtMiss && mon.Hp == 0) OnMonsterDeath(mon);
    }

    /// <summary>The net HP/MP change a hit inflicts on the defender plus the per-exec damage map that fills
    /// <c>CS_DEFEND_ACK</c> — the C++ <c>CalcDamage</c> out-params <c>nDamageHP</c>/<c>nDamageMP</c>/<c>mapDamage</c>.
    /// HP/MP are signed (positive = damage, negative = heal); the map value is the C++ <c>(WORD)dwValue</c>.</summary>
    private readonly record struct DamageResult(int DamageHp, int DamageMp, List<(byte Exec, uint Value)> Map);

    /// <summary>The MTYPE_* execs <c>CalcDamage</c> resolves (any other exec is handled later by <c>PerformSkill</c>):
    /// <c>MTYPE_DAMAGE</c>/<c>MDAMAGE</c> (HP/MP damage) and <c>MTYPE_HP</c>/<c>MP</c> (direct heal/drain).</summary>
    private static bool IsDamageExec(byte exec) => exec is MtypeDamage or MtypeMdamage or MtypeHp or MtypeMp;

    /// <summary>C++ <c>CTObjBase::CalcDamage</c> (TObjBase.cpp:344) against a monster defender: iterate the
    /// attacking skill's <c>SDT_ABILITY</c> damage rows, dispatch on the row's attr (physical/magic → which AP
    /// band + DP) then its exec, and accumulate the net HP/MP change + the per-exec damage map. When the skill
    /// has no damage rows (a basic attack / a pure buff / DB-free) the C++ basic weapon-skill roll is used — one
    /// physical-or-magic HP-damage component keyed <c>MTYPE_DAMAGE</c> (the Phase-13 behavior, byte-preserved).
    ///
    /// <para><b>Deferred (documented — PORT_STATUS.md):</b> the buff-layer amplification of the roll
    /// (<c>CalcAbilityValue</c> maintain/remain/cure terms + <c>ApplyEffectionBuff</c> — only the instance-skill
    /// <c>CalcValue</c> term is applied here), <c>DistributeSkill</c> (pet damage share), the AUTOAI-recall ×3
    /// (pets), the shield-block roll (a monster has no equipped-item model → shield DP 0), and the custom
    /// Araz ≥3000 hard-coded skills.</para></summary>
    private DamageResult CalcMonsterDamage(Character ch, Monster mon, SkillTemplate? tpl, byte level,
        byte hitType, bool isMagic, bool isLong)
    {
        var map = new List<(byte Exec, uint Value)>();
        if (hitType == HtMiss) return new DamageResult(0, 0, map);   // C++ Defend skips CalcDamage on a miss

        // The skill's damage rows (SDT_ABILITY + a damage exec). No such rows ⇒ the basic-attack fallback.
        var rows = tpl?.Data.Where(d => d.Type == SdtAbility && IsDamageExec(d.Exec)).ToList();
        if (rows is not { Count: > 0 })
        {
            uint dmg = BasicRoll(ch, mon, tpl, level, hitType, isMagic, isLong);
            if (dmg != 0) map.Add((MtypeDamage, (ushort)dmg));   // (WORD)dwValue
            return new DamageResult((int)dmg, 0, map);
        }

        int nDamageHp = 0, nDamageMp = 0;
        int nHp = (int)mon.Hp, nMp = (int)mon.Mp;   // C++ working copies — only MTYPE_HP/MP read/write these
        foreach (var d in rows)
        {
            bool magicAttr = d.Attr >= SattMagicNo && d.Attr <= 9;   // SATT_MAGICNO..SATT_MAGICIR ⇒ magic; PHYSIC/LONG ⇒ physical
            switch (d.Attr)
            {
                case SattNone: continue;   // C++ switch(m_bAttr) { case SATT_NONE: break; } — no effect
            }
            switch (d.Exec)
            {
                case MtypeDamage:   // HP damage — crit off the MAX band (physical) / MIN band (magic)
                {
                    uint v = RollExec(ch, mon, tpl!, d, level, hitType, magicAttr, mpPool: false);
                    if (v != 0) { nDamageHp += (int)v; MapInsert(map, MtypeDamage, v); }
                    break;
                }
                case MtypeMdamage:  // MP damage — crit off the MIN band
                {
                    uint v = RollExec(ch, mon, tpl!, d, level, hitType, magicAttr, mpPool: true);
                    if (v != 0) { nDamageMp += (int)v; MapInsert(map, MtypeMdamage, v); }
                    break;
                }
                case MtypeHp:   // direct HP heal/drain: nInc = the skill's MTYPE_HP delta off MaxHP (sign = heal/drain)
                {
                    int nInc = tpl!.CalcValue(level, SdtAbility, MtypeHp, mon.MaxHp);
                    if (nInc < 0)   // drain (dwValue clamped to the working HP, then applied + reported)
                    {
                        uint dealt = nHp > -nInc ? (uint)(-nInc) : (uint)nHp;
                        nHp -= (int)dealt; nDamageHp += (int)dealt; MapInsert(map, MtypeHp, dealt);
                    }
                    else            // heal (negative nDamageHP ⇒ OnDamage restores; not shown in the map)
                    {
                        if (nHp + nInc > (int)mon.MaxHp) { nDamageHp -= (int)mon.MaxHp - nHp; nHp = (int)mon.MaxHp; }
                        else { nDamageHp -= nInc; nHp += nInc; }
                    }
                    break;
                }
                case MtypeMp:   // direct MP heal/drain
                {
                    int nInc = tpl!.CalcValue(level, SdtAbility, MtypeMp, mon.MaxMp);
                    if (nInc < 0)
                    {
                        uint dealt = nMp > -nInc ? (uint)(-nInc) : (uint)nMp;
                        nMp -= (int)dealt; nDamageMp += (int)dealt; MapInsert(map, MtypeMp, dealt);
                    }
                    else
                    {
                        if (nMp + nInc > (int)mon.MaxMp) { nDamageMp -= (int)mon.MaxMp - nMp; nMp = (int)mon.MaxMp; }
                        else { nDamageMp -= nInc; nMp += nInc; }
                    }
                    break;
                }
            }
        }
        return new DamageResult(nDamageHp, nDamageMp, map);
    }

    /// <summary>The basic weapon-attack roll (no damage-row skill): the physical-or-magic AP−DP band, crit per
    /// the FTYPE_PCD/MCD formula (physical off max, magic off min), then the instance-skill MTYPE_DAMAGE scaling
    /// (identity when the skill has no such row) — the Phase-13/15 behavior, preserved byte-for-byte.</summary>
    private uint BasicRoll(Character ch, Monster mon, SkillTemplate? tpl, byte level, byte hitType, bool isMagic, bool isLong)
    {
        uint apMin = isMagic ? StatEngine.MinMagicAp(ch, _templates) : StatEngine.MinAp(ch, arrow: isLong, _templates);
        uint apMax = isMagic ? StatEngine.MaxMagicAp(ch, _templates) : StatEngine.MaxAp(ch, arrow: isLong, _templates);
        uint dp = isMagic ? mon.MagicDefPower : mon.DefendPower;   // + GetShieldDP()/GetShieldMDP() == 0 (a monster has no shield)
        int a = Math.Max((int)(apMin - dp), 5), b = Math.Max((int)(apMax - dp), 7);
        uint roll = hitType == HtCritical
            ? CritDamage(CombatRng, _templates.Formula(isMagic ? FtypeMcd : FtypePcd), (uint)(isMagic ? a : b))
            : (uint)(a + CombatRng.Next(Math.Max(b - a, 1)));
        return tpl is { } st ? st.ScaleDamage(level, roll) : roll;
    }

    /// <summary>One MTYPE_DAMAGE / MTYPE_MDAMAGE component: the AP−DP roll (band by the row's attr — physical
    /// uses <c>GetDefendPower</c>, magic uses <c>GetMagicDefPower</c>; a monster's shield DP is 0), the crit
    /// band (HP-damage physical crits off <c>dwB</c>, everything else off <c>dwA</c>) via FTYPE_PCD/MCD, then
    /// the instance-skill scaling <c>CalcValue(SDT_ABILITY, exec, roll)</c> (C++ <c>CalcAbilityValue</c> instance
    /// term), 0-floored. The buff/remain/cure amplification layers are deferred.</summary>
    private uint RollExec(Character ch, Monster mon, SkillTemplate tpl, SkillDataRow d, byte level,
        byte hitType, bool magicAttr, bool mpPool)
    {
        bool arrow = d.Attr == SattLong || tpl.IsLongAttack();
        uint apMin = magicAttr ? StatEngine.MinMagicAp(ch, _templates) : StatEngine.MinAp(ch, arrow, _templates);
        uint apMax = magicAttr ? StatEngine.MaxMagicAp(ch, _templates) : StatEngine.MaxAp(ch, arrow, _templates);
        uint dp = magicAttr ? mon.MagicDefPower : mon.DefendPower;
        int a = Math.Max((int)(apMin - dp), 5), b = Math.Max((int)(apMax - dp), 7);
        uint roll = hitType == HtCritical
            ? CritDamage(CombatRng, _templates.Formula(magicAttr ? FtypeMcd : FtypePcd), (uint)(!magicAttr && !mpPool ? b : a))
            : (uint)(a + CombatRng.Next(Math.Max(b - a, 1)));
        return (uint)Math.Max(0, (int)roll + tpl.CalcValue(level, SdtAbility, d.Exec, roll));
    }

    /// <summary>The C++ <c>mapDamage</c> quirk (TObjBase.cpp:537): find by <c>m_bAttr</c> (never hits an exec
    /// key, since attrs 0–12 and damage execs 14/22/30/88 never coincide) then <c>std::map::insert</c> by
    /// <c>m_bExec</c> — i.e. add the entry only if the exec key is absent. The value is the C++ <c>(WORD)dwValue</c>.</summary>
    private static void MapInsert(List<(byte Exec, uint Value)> map, byte exec, uint value)
    {
        foreach (var e in map) if (e.Exec == exec) return;   // std::map::insert doesn't overwrite an existing key
        map.Add((exec, (ushort)value));
    }

    /// <summary>C++ <c>CTObjBase::OnDamage</c> (TObjBase.cpp:723): apply the net HP/MP change to the monster —
    /// positive damage floors at 0, negative heal clamps to max.</summary>
    private static void ApplyMonsterDamage(Monster mon, in DamageResult r)
    {
        if (r.DamageHp > 0) mon.Hp = mon.Hp > (uint)r.DamageHp ? mon.Hp - (uint)r.DamageHp : 0;
        else if (r.DamageHp < 0) { uint heal = (uint)(-r.DamageHp); mon.Hp = mon.Hp + heal > mon.MaxHp ? mon.MaxHp : mon.Hp + heal; }
        if (r.DamageMp > 0) mon.Mp = mon.Mp > (uint)r.DamageMp ? mon.Mp - (uint)r.DamageMp : 0;
        else if (r.DamageMp < 0) { uint heal = (uint)(-r.DamageMp); mon.Mp = mon.Mp + heal > mon.MaxMp ? mon.MaxMp : mon.Mp + heal; }
    }

    /// <summary>C++ <c>PerformSkill</c>'s <c>SDT_ABILITY</c> <c>MTYPE_HI</c>/<c>MTYPE_MI</c> lifedrain
    /// (TObjBase.cpp:3113): a fraction of the HP/MP damage dealt (<c>Calculate</c> off the damage, 0-floored)
    /// is sent to the world as <c>MW_GETBLOOD_ACK</c>, which credits it to the attacker. Fire-and-forget.</summary>
    private void SendLifeDrain(SkillTemplate tpl, byte level, uint attackId, byte attackType, uint hostId, in DamageResult r)
    {
        for (int i = 0; i < tpl.Data.Count; i++)
        {
            var d = tpl.Data[i];
            if (d.Type != SdtAbility) continue;
            if (d.Exec == MtypeHi && r.DamageHp > 0)
            {
                uint blood = (uint)Math.Max(tpl.Calculate(level, i, (uint)r.DamageHp), 0);
                if (blood != 0) SendMW_GETBLOOD_ACK(attackId, attackType, hostId, MtypeHi, blood);
            }
            else if (d.Exec == MtypeMi && r.DamageMp > 0)
            {
                uint blood = (uint)Math.Max(tpl.Calculate(level, i, (uint)r.DamageMp), 0);
                if (blood != 0) SendMW_GETBLOOD_ACK(attackId, attackType, hostId, MtypeMi, blood);
            }
        }
    }

    /// <summary>C++ <c>SendMW_GETBLOOD_ACK</c> (SSSender.cpp:4080) — byte-exact:
    /// <c>dwAtkID · bAtkType · dwHostID · bBloodType · dwBlood</c>.</summary>
    private void SendMW_GETBLOOD_ACK(uint attackId, byte attackType, uint hostId, byte bloodType, uint blood)
    {
        var w = new PacketWriter(Msg.MW_GETBLOOD_ACK, capacity: 16);
        w.WriteUInt32(attackId);
        w.WriteByte(attackType);
        w.WriteUInt32(hostId);
        w.WriteByte(bloodType);
        w.WriteUInt32(blood);
        _world.Send(w);
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
    /// trailing damage map is keyed by each component's <c>m_bExec</c> (<c>MTYPE_DAMAGE</c> 30 / <c>MTYPE_MDAMAGE</c>
    /// 88 / <c>MTYPE_HP</c> 14 / <c>MTYPE_MP</c> 22) and is empty on a miss. The power band goes to the magic or
    /// physical fields per <paramref name="isMagic"/>.</summary>
    private static byte[] BuildCS_DEFEND_ACK(uint attackId, uint hostId, Monster mon, byte attackType, uint actId, uint aniId,
        ushort attackLevel, byte attackerLevel, uint apMin, uint apMax, bool isMagic, byte critProb, byte canSelect,
        byte attackCountry, byte attackAid, ushort skillId, byte skillLevel, byte atkHit, bool landed,
        float atkX, float atkY, float atkZ, float defX, float defY, float defZ, IReadOnlyList<(byte Exec, uint Value)> map,
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
        w.WriteByte((byte)map.Count);     // damage-map entry count (empty on a miss)
        foreach (var (exec, value) in map)
        {
            w.WriteByte(exec);            // key = the component's m_bExec (MTYPE_DAMAGE/MDAMAGE/HP/MP)
            w.WriteUInt32(value);         // (WORD)dwValue, already masked at map-insert
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
