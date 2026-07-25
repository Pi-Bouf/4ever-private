using TMap.Data;
using TMap.Protocol;

namespace TMap.Server.Map;

/// <summary>
/// A live field-monster instance — a focused port of <c>CTMonster</c> (which extends <c>CTObjBase</c>): the
/// minimal identity / appearance / vitals / position needed to place it on the spatial grid and serialize it
/// to a client via <c>CS_ADDMON_ACK</c>. The deep combat/AI/aggro/loot state (<c>m_pATTR</c> beyond MaxHP/MP,
/// host tracking, skills, drop tables) is deferred — see PORT_STATUS.md.
///
/// <para>There is no name on the server: the client resolves the model and display name from its own
/// monster chart keyed by <see cref="ChartId"/>.</para>
/// </summary>
public sealed class Monster
{
    /// <summary>OBJ_TYPE OT_MON (NetCode.h) — a field monster.</summary>
    public const byte OtMon = 2;

    /// <summary>The instance id (C++ <c>m_dwID</c>), composed deterministically from the spawn, not a
    /// counter: <c>(spawnId &lt;&lt; 16) | (channel &lt;&lt; 8) | slot</c> (C++ <c>MAKELONG(MAKEWORD(slot, channel),
    /// spawnId)</c>, TMap.cpp:565).</summary>
    public uint Id { get; set; }

    /// <summary>The monster template / chart id (C++ <c>m_pMON->m_wID</c>) — the client's model+name key.</summary>
    public ushort ChartId { get; set; }
    public byte Level { get; set; }              // m_pMON->m_bLevel

    // Vitals — MaxHP/MaxMP come from the monster attr chart (m_pATTR->m_dwMaxHP/m_dwMaxMP); buff deltas deferred.
    public uint MaxHp { get; set; }
    public uint Hp { get; set; }
    public uint MaxMp { get; set; }
    public uint Mp { get; set; }

    /// <summary>The defence power the attacker's damage is reduced by (C++ <c>GetDefendPower</c> — the attr
    /// chart <c>m_wDP</c>; shield DP and buff deltas are deferred/0 for a monster).</summary>
    public uint DefendPower { get; set; }

    // ---- Phase 28 (combat quality). As the DEFENDER: magic defence (wMDP) + the defend levels (wDL/wMDL)
    // that feed the attacker's GetAtkHitType hit-rate roll. As the ATTACKER: its attack level (wAL) + crit
    // prob (bCriticalPP), used for the miss/crit roll on the player it hits. ----
    public uint MagicDefPower { get; set; }
    public ushort DefendLevel { get; set; }
    public ushort MagicDefLevel { get; set; }
    public byte CritProb { get; set; }
    public ushort AttackLevel { get; set; }

    // Position / facing (CTObjBase).
    public float PosX { get; set; }
    public float PosY { get; set; }
    public float PosZ { get; set; }
    public ushort Pitch { get; set; }
    public ushort Dir { get; set; }
    public byte MouseDir { get; set; }
    public byte KeyDir { get; set; }
    public byte Action { get; set; }
    public byte Mode { get; set; }               // TMODE_TYPE — MT_NORMAL = 0 at spawn
    public byte Country { get; set; }
    /// <summary>C++ <c>m_bAidCountry</c> — the war-alliance country (an ally-faction override). Defaults to
    /// <c>TCONTRY_N</c> (neutral) so a field monster's <see cref="WarCountry"/> is just its <see cref="Country"/>.</summary>
    public byte AidCountry { get; set; } = TcontryN;
    public uint Region { get; set; }

    // Placement.
    public byte Channel { get; set; }
    public ushort MapId { get; set; }

    /// <summary>Phase 45/46 — whether this monster acquires a host/target on sight (auto-aggro), vs staying
    /// passive until hit. In C++ this is not a monster-chart flag: a monster is aggressive iff its <c>bAIType</c>
    /// script binds <c>AC_SETHOST</c> under the <c>AT_ENTER</c> trigger in the DB <c>TAICHART</c> table.
    /// <b>Phase 46</b> loads that table (<see cref="TemplateStore.AggressiveAiTypes"/>) and stamps this at spawn;
    /// it still defaults <c>false</c> DB-free / when the AI charts are absent (the hit-driven aggro of Phase 44 is
    /// unaffected either way). Tests set it directly. See PORT_STATUS.md.</summary>
    public bool Aggressive { get; set; }

    /// <summary>The grid cell this monster is currently bucketed in (C++ cell membership), set by the grid.</summary>
    public uint CellKey { get; set; }

    // ---- Phase 18: roam AI. The wander anchor (C++ m_fStartX/Y/Z, set at spawn), the roam radius (the
    // spawn's area), the last picked destination (m_fNextX/Z), and the per-monster roam-step timer. ----
    public float StartX { get; set; }
    public float StartY { get; set; }
    public float StartZ { get; set; }
    public float Area { get; set; }        // roam radius (spawn Range); 0 ⇒ the monster stays put
    public float NextX { get; set; }
    public float NextZ { get; set; }
    public long RoamNextMs { get; set; }   // earliest map-clock tick (ms) for the next AI step (roam or chase)

    /// <summary>Phase 44: the resolved aggro target's object id (C++ <c>m_dwTargetID</c>) — the entity this
    /// monster is chasing while <c>MT_BATTLE</c>, picked from <see cref="AggroTable"/> by the retarget rule.
    /// 0 = no target. Written by the map-service <c>ApplyRetarget</c> (the C++ <c>ChgHost</c> action).</summary>
    public uint TargetId { get; set; }
    /// <summary>C++ <c>m_bTargetType</c> — the target's OBJ_TYPE (always <c>OT_PC</c> in the current port; pets/
    /// summons are deferred). Paired with <see cref="TargetId"/> to form the aggro key of the current target.</summary>
    public byte TargetType { get; set; }

    /// <summary>C++ <c>m_dwHostID</c> — the char id of the player currently "hosting" (driving) this monster's AI
    /// (the controller of the aggressor it targets). Sent as the <c>TRUE</c> recipient of <c>CS_MONHOST_ACK</c>.
    /// 0 = no host.</summary>
    public uint HostId { get; set; }

    // ---- Phase 20: monster attack. AtkMin/Max = the physical AP band (C++ GetMinAP/GetMaxAP), AtkSpeed the
    // attack cadence (m_dwAtkSpeed), AtkNextMs the next-attack deadline vs the map clock. ----
    public uint AtkMin { get; set; }
    public uint AtkMax { get; set; }
    public uint AtkSpeed { get; set; }
    public long AtkNextMs { get; set; }

    // ---- Phase 16: HP/MP regen (Recover) state (C++ m_dwRecoverHPTick / m_dwRecoverMPTick / m_dwLastAtkTick). ----
    public uint RecoverHpTick { get; set; }
    public uint RecoverMpTick { get; set; }
    public uint LastAtkTick { get; set; }

    // ---- Phase 17: exp/loot. Exp + money knobs come from the monster chart (TMONSTERCHART). ----
    public uint Exp { get; set; }          // m_pMON->m_wExp — the kill exp reward
    public byte MoneyProb { get; set; }    // m_bMoneyProb
    public uint MinMoney { get; set; }     // m_dwMinMoney
    public uint MaxMoney { get; set; }     // m_dwMaxMoney

    // ---- Phase 22: item loot. The drop table (from the monster chart) + the corpse's item inventory. ----
    public byte ItemProb { get; set; }           // m_bItemProb — per-attempt drop chance
    public byte DropCount { get; set; }          // m_bDropCount — number of drop attempts
    public uint MaxWeight { get; set; }          // m_dwMaxWeight — Σ drop-row weights (0 ⇒ no loot at all)
    public IReadOnlyList<MonItemRow> DropRows { get; set; } = System.Array.Empty<MonItemRow>();

    /// <summary>The dead monster's loot inventory (C++ <c>INVEN_DEFAULT</c> on the corpse) — dropped items
    /// wait here to be taken.</summary>
    public Inven CorpseInven { get; } = new() { InvenId = Proto.InvenDefault };

    /// <summary>The money rolled onto the corpse on death (C++ <c>m_dwMoney</c>), waiting to be taken.</summary>
    public uint CorpseMoney { get; set; }

    /// <summary>True while the corpse still holds loot (money or items) — keeps it around to be looted.</summary>
    public bool HasLoot => CorpseMoney > 0 || CorpseInven.Items.Count > 0;
    /// <summary>True once dead (C++ <c>OS_DEAD</c>): a lootable corpse — not attackable, does not regen.</summary>
    public bool Dead { get; set; }
    /// <summary>The map-clock (ms) at which the corpse despawns and its spawn slot re-arms.</summary>
    public long CorpseExpireMs { get; set; }

    /// <summary>Per-bucket cumulative damage (C++ <c>m_mapDamage</c>) — decides the loot/exp owner. The bucket
    /// key is the attacker's party (when partied) or char id (C++ <c>nKey = wPartyID ? MAKEINT64(wPartyID,0) :
    /// dwHostID</c>), so a party's hits accumulate together.</summary>
    private readonly Dictionary<long, uint> _damage = new();
    /// <summary>The loot/exp owner (C++ <c>m_dwKeeperID</c>) — the first bucket to cross the <c>MONKEEP_PER</c>
    /// (10% of MaxHP) cumulative-damage threshold. For <see cref="KeeperType"/> Private it's a char id; for
    /// Party it's a party id. 0 = none yet.</summary>
    public uint KeeperId { get; private set; }
    /// <summary>C++ <c>m_bKeeperType</c> (OWNER_PRIVATE / OWNER_PARTY) — whether <see cref="KeeperId"/> is a char
    /// or a party. 0 (OWNER_NONE) until a bucket crosses the threshold. Phase 38.</summary>
    public byte KeeperType { get; private set; }

    // OWNER_TYPE (NetCode.h:1972)
    private const byte OwnerPrivate = 1, OwnerParty = 2;

    /// <summary>C++ <c>CTMonster::OnDamage</c>/<c>SetKeeper</c> (TMonster.cpp:381,996): accumulate the attacker's
    /// damage into its party bucket (or its own, if unpartied) and, once that bucket crosses 10% of MaxHP,
    /// stamp the keeper — a party keeper (<paramref name="partyId"/> non-zero) or a private one.</summary>
    public void AddDamage(uint charId, ushort partyId, uint dmg)
    {
        long key = partyId != 0 ? (long)partyId << 32 : charId;   // C++ MAKEINT64(partyId,0) vs dwHostID
        _damage[key] = _damage.TryGetValue(key, out var d) ? d + dmg : dmg;
        if (KeeperType == 0 && _damage[key] >= MaxHp * 10 / 100)   // MONKEEP_PER = 10
        {
            KeeperId = partyId != 0 ? partyId : charId;
            KeeperType = partyId != 0 ? OwnerParty : OwnerPrivate;
        }
    }

    /// <summary>Enter battle mode on being hit (C++ <c>ChgMode(MT_BATTLE)</c>): pushes the recover anchors to
    /// <c>now + RECOVER_INIT</c> on the transition (suppressing monster HP regen while <c>MT_BATTLE</c>), and
    /// refreshes <see cref="LastAtkTick"/> on every hit.</summary>
    public void EnterBattle(uint now, uint recoverInit)
    {
        if (Mode != 1) // MT_BATTLE
        {
            Mode = 1;
            RecoverHpTick = now + recoverInit;
            RecoverMpTick = now + recoverInit;
        }
        LastAtkTick = now;
    }

    /// <summary>Active maintained (debuff) skills on this monster (C++ <c>CTObjBase::m_vMaintainSkill</c>) —
    /// landed by a player's buff-type <c>CS_DEFEND</c>. Serialized into <c>CS_ADDMON_ACK</c> and expired by the
    /// per-tick <c>CheckMaintainSkill</c> sweep (Phase 31).</summary>
    public List<MaintainSkill> MaintainSkills { get; } = new();

    /// <summary>C++ <c>m_dwID = MAKELONG(MAKEWORD(slot, channel), spawnId)</c> (TMap.cpp:565).</summary>
    public static uint MakeId(ushort spawnId, byte channel, byte slot) =>
        ((uint)spawnId << 16) | ((uint)channel << 8) | slot;

    // ============================ Phase 44 — the aggro / hate table (m_mapAggro) ============================
    // C++ CTMonster::m_mapAggro (map<__int64,TAGGRO>, TMonster.h:44). The monster's victim is chosen from this
    // table, NOT by last-hitter: highest cumulative aggro with a 10% "sticky-target" hysteresis. Aggro is
    // SKILL-driven (max(1, skill.GetAggro), warrior ×1.5), never raw damage. This type owns the table + the pure
    // arithmetic (SetAggro/LeaveAggro decisions); the map service applies the retarget (ChgHost) + broadcast.

    private const byte MtBattle = 1, MtGohome = 2;   // TMODE_TYPE (MT_NORMAL = 0)
    private const byte TclassWarrior = 0;            // TCLASS_TYPE TCLASS_WARRIOR (NetCode.h:1104)
    private const byte TcontryN = 3;                 // TCONTRY_TYPE TCONTRY_N (neutral, NetCode.h:1096)
    private const byte OtPc = 1;                     // OBJ_TYPE OT_PC

    /// <summary>One <c>TAGGRO</c> entry (TMapType.h:1271): the hated entity, its controlling host + war-country,
    /// and the accumulated hate.</summary>
    public sealed class AggroEntry
    {
        public byte ObjType;
        public uint ObjId;
        public uint HostId;
        public uint Aggro;
        public byte Country;
    }

    /// <summary>A retarget decision (the args of the C++ <c>OnEvent(AT_DEFEND, 0, host, obj, type)</c>) — who the
    /// monster should switch its target/host to. Returned by <see cref="SetAggro"/>/<see cref="LeaveAggro"/> for
    /// the map service to apply (set <see cref="TargetId"/>/<see cref="TargetType"/> + broadcast).</summary>
    public readonly record struct AggroTarget(uint HostId, uint ObjId, byte ObjType);

    /// <summary>C++ <c>GetWarCountry</c> (TObjBase.cpp:4962): the ally-faction override, else the base country.</summary>
    public byte WarCountry => AidCountry != TcontryN ? AidCountry : Country;

    private readonly Dictionary<long, AggroEntry> _aggro = new();

    /// <summary>C++ <c>MAKEINT64(objID, objType)</c> (TMapType.h:20) — the aggro-table key.</summary>
    private static long Key(uint objId, byte objType) => ((long)objId << 32) | objType;

    /// <summary>Read-only view of the hate table (for tests / the retarget scan).</summary>
    public IReadOnlyDictionary<long, AggroEntry> AggroTable => _aggro;

    /// <summary>C++ <c>FindAggro</c> (TMonster.cpp:244) — the accumulated hate an entity holds (0 if none).</summary>
    public uint FindAggro(uint id, byte type) => _aggro.TryGetValue(Key(id, type), out var e) ? e.Aggro : 0;

    /// <summary>C++ <c>ResetHost</c>'s <c>m_mapAggro.clear()</c> (TMonster.cpp:2412).</summary>
    public void ClearAggro() => _aggro.Clear();

    /// <summary>C++ <c>CTMonster::SetAggro</c> (TMonster.cpp:141) — add/accumulate hate for an attacker and decide
    /// whether that flips the monster's target. Returns the retarget decision (the AT_DEFEND args) or <c>null</c>
    /// if the target stays. <paramref name="active"/> mirrors <c>bActive</c>: TRUE (a direct hit) may create a
    /// fresh entry and re-aggro even a going-home monster; FALSE (splash) only tops up known entries.</summary>
    public AggroTarget? SetAggro(uint hostId, uint attackId, byte attackType, byte attackCountry,
        byte attackClass, uint target, byte targetType, int nAggro, bool active)
    {
        if (nAggro == 0 || attackType == OtMon || attackId == 0) return null;   // TMonster.cpp:151
        if (Mode == MtGohome && !active) return null;                            // TMonster.cpp:156
        if (attackCountry == WarCountry) return null;                            // no same-faction aggro
        if (attackClass == TclassWarrior) nAggro = nAggro * 3 / 2;               // warrior ×3/2 (int)

        uint dwOld = _aggro.TryGetValue(Key(TargetId, TargetType), out var cur) ? cur.Aggro : 0;
        uint dwNew = 0;

        long ak = Key(attackId, attackType);
        if (_aggro.TryGetValue(ak, out var e))
        {
            long sum = (long)e.Aggro + nAggro;                                   // floor at 0 (TMonster.cpp:174)
            e.Aggro = sum < 0 ? 0u : (uint)sum;
            dwNew = e.Aggro;
        }
        else if (active || _aggro.ContainsKey(Key(target, targetType)))          // create iff active, or the passed target exists
        {
            _aggro[ak] = new AggroEntry
            {
                ObjType = attackType, ObjId = attackId, HostId = hostId,
                Aggro = (uint)nAggro, Country = attackCountry,
            };
            dwNew = (uint)nAggro;
        }

        // ---- retarget decision (TMonster.cpp:205-241) ----
        if (nAggro < 0 && Mode == MtBattle)
        {
            // aggro decrease: rescan the whole table for the highest hostile-country entry
            var top = HighestSurvivor();
            if (top is { } t && (t.ObjType != TargetType || t.ObjId != TargetId)
                && dwOld + dwOld * 0.1 < FindAggro(t.ObjId, t.ObjType))
                return t;
            return null;
        }

        // aggro increase: pull into battle, or steal the target only past the 10% sticky threshold
        if ((dwNew != 0 && Mode != MtBattle)
            || ((TargetType != attackType || TargetId != attackId) && dwOld + dwOld * 0.1 < dwNew))
            return new AggroTarget(hostId, attackId, attackType);
        return null;
    }

    /// <summary>C++ <c>AddAggro</c> (TMonster.cpp:323) — unconditional accumulation (no scaling, no retarget). Used
    /// to seed a minimal hate entry on a freshly-chosen target (the C++ <c>ChgHost</c> <c>AddAggro(...,1)</c>).</summary>
    public void AddAggro(uint hostId, uint target, byte targetType, byte country, uint aggro)
    {
        long k = Key(target, targetType);
        if (_aggro.TryGetValue(k, out var e)) e.Aggro += aggro;
        else _aggro[k] = new AggroEntry { ObjType = targetType, ObjId = target, HostId = hostId, Aggro = aggro, Country = country };
    }

    /// <summary>C++ <c>DelAggro</c> (TMonster.cpp:340) — drop one entry. Returns whether it was present.</summary>
    public bool DelAggro(uint target, byte targetType) => _aggro.Remove(Key(target, targetType));

    /// <summary>The highest-hate entry whose country differs from the monster's (C++ the survivor scan in
    /// <c>LeaveAggro</c>/the decrease branch, TMonster.cpp:99-105). Strict <c>&lt;</c> with ascending-key iteration
    /// so ties go to the lowest object-id (matching <c>std::map</c> order). <c>null</c> if the table has no
    /// hostile-country entry.</summary>
    public AggroTarget? HighestSurvivor()
    {
        uint best = 0; AggroEntry? pick = null;
        foreach (var kv in _aggro.OrderBy(k => k.Key))
            if (best < kv.Value.Aggro && kv.Value.Country != Country) { best = kv.Value.Aggro; pick = kv.Value; }
        return pick is null ? null : new AggroTarget(pick.HostId, pick.ObjId, pick.ObjType);
    }

    /// <summary>C++ <c>LeaveAggro</c> (TMonster.cpp:87) minus the spatial neighbour test: erase the leaving
    /// entity's hate and return the highest-hate survivor to switch to (<c>null</c> = leave battle / go home). The
    /// map service checks the survivor is still a live, in-view player; a non-viewable survivor is dropped by
    /// calling this again on it (the C++ recurse-drop of a non-neighbour top-aggro).</summary>
    public AggroTarget? LeaveAggro(uint rhId, byte rhType)
    {
        _aggro.Remove(Key(rhId, rhType));
        return HighestSurvivor();
    }
}
