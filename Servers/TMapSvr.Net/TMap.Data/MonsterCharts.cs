namespace TMap.Data;

/// <summary>
/// A monster template row from <c>TMONSTERCHART</c> (C++ <c>CTBLMonster</c> → <c>tagTMONSTER</c>, keyed by
/// <c>m_wID</c>). This is the subset the spawn + client-visibility path needs: the chart id (the client's
/// model/name key), the level, and the attr-chart key (<c>m_wMonAttr</c>) used to look up the level-scaled
/// vitals. The combat/AI/loot columns (class/race/roam/aggro/money/drop/skills…) are deferred — see
/// PORT_STATUS.md.
/// </summary>
public sealed record MonsterTemplate(ushort Id, byte Level, ushort MonAttr,
    // Phase 17 (exp/money): the kill reward (m_wExp) and the money-drop knobs (m_bMoneyProb / m_dwMinMoney /
    // m_dwMaxMoney). Phase 22 (item loot): the per-attempt item chance (m_bItemProb) and attempt count
    // (m_bDropCount); the drop table itself is the linked <see cref="DropRows"/> (from TMONITEMCHART).
    uint Exp = 0, byte MoneyProb = 0, uint MinMoney = 0, uint MaxMoney = 0,
    byte ItemProb = 0, byte DropCount = 0)
{
    /// <summary>The monster's item-drop rows (C++ <c>m_vMONITEM</c>, TMONITEMCHART) — loaded per monster.</summary>
    public List<MonItemRow> DropRows { get; } = new();

    /// <summary>C++ <c>m_dwMaxWeight</c> — Σ of the drop rows' weights; the roll's denominator. When 0 the
    /// monster drops nothing at all (money included — the C++ <c>AddItem</c> early-returns on it).</summary>
    public uint MaxWeight => (uint)DropRows.Sum(r => (int)r.Weight);
}

/// <summary>
/// One item-drop row from <c>TMONITEMCHART</c> (C++ <c>tagTMONITEM</c>, keyed by <c>wMonID</c>): a candidate
/// drop with its selection weight and the four "normal" probability gates that must all pass. This slice
/// handles fixed chart-type items (<see cref="ChartType"/> ≠ 0, <see cref="ItemId"/> set); the ranged
/// <c>MonChoiceItem</c> pick (ItemID 0 + min/max) and pre-built magic items (ChartType 0) are deferred.
/// </summary>
public sealed record MonItemRow(ushort ItemId, ushort Weight, byte ChartType,
    byte Prob1, byte Prob2, byte Prob3, byte Prob4);

/// <summary>
/// A monster-attribute row from <c>TMONATTRCHART</c> (C++ <c>CTBLMonAttr</c>, keyed by
/// <c>MAKELONG(m_wID, m_bLevel)</c>). Holds the level-scaled vitals; only MaxHP/MaxMP are needed for the
/// client appearance packet (the AP/DP/speed/crit combat columns are deferred).
/// </summary>
public sealed record MonAttrRow(ushort Id, byte Level, uint MaxHp, uint MaxMp, uint DefendPower,
    // Phase 20 (monster attack): the physical attack-power band (C++ CTMonster::GetMinAP/GetMaxAP =
    // m_wAP + m_wMin/MaxWAP) and the attack-speed cadence (m_dwAtkSpeed).
    uint AtkMin = 0, uint AtkMax = 0, uint AtkSpeed = 0,
    // Phase 28 (combat quality): as the DEFENDER — magic defence (wMDP) + the defend levels (wDL/wMDL) that
    // feed the attacker's hit-rate roll; as the ATTACKER — its own attack level (wAL) + crit prob (bCriticalPP).
    uint MagicDefPower = 0, ushort DefendLevel = 0, ushort MagicDefLevel = 0, byte CritProb = 0, ushort AttackLevel = 0);

/// <summary>
/// A spawn-point definition from <c>TMONSPAWNCHART</c> (C++ <c>CTBLMonSpawn</c> → <c>tagTMONSPAWN</c>). The
/// subset needed for a basic auto-spawn: the id (high word of each monster's instance id), the map/position/
/// facing anchor, the scatter radius (<c>Range</c>), the per-cycle regen probability (<c>Prob</c>), the
/// respawn <c>Delay</c>, the instance count, and the <c>Event</c> byte (<c>SE_DEFAULT = 0</c> ⇒ auto-spawn at
/// load). Group/link/area/roam/party/local-zone fields are deferred.
/// </summary>
public sealed record MonSpawnRow(
    ushort Id, ushort MapId, float PosX, float PosY, float PosZ, ushort Dir, byte Country,
    byte Count, byte Range, byte Prob, uint Region, uint Delay, byte Event);

/// <summary>
/// One entry of a spawn's monster-type table from <c>TMAPMONCHART</c> (C++ <c>CTBLMapMonAll</c> →
/// <c>tagTMAPMON</c>): a candidate monster type with its selection weight (<c>Prob</c>) and the
/// <c>Essential</c> flag (essential monsters are resolved directly and excluded from the weighted pick).
/// </summary>
public sealed record MapMonRow(ushort SpawnId, ushort MonId, byte Leader, byte Essential, byte Prob);

/// <summary>A spawn point bundled with its candidate monster-type table — the unit the spawn engine works on.</summary>
public sealed record MonsterSpawnDef(MonSpawnRow Spawn, List<MapMonRow> Types);
