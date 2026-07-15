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
    public uint Region { get; set; }

    // Placement.
    public byte Channel { get; set; }
    public ushort MapId { get; set; }

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

    /// <summary>Phase 19: the aggro target's char id (C++ the top of <c>m_mapAggro</c>) — the player this
    /// monster is chasing while <c>MT_BATTLE</c>. 0 = no target.</summary>
    public uint TargetId { get; set; }

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
}
