namespace TMap.Server.Map;

/// <summary>
/// The result of moving a player between cells — the set-difference of the old 3×3 cell block against the
/// new one (C++ <c>CTMap::OnMove</c>). <see cref="Entered"/> are players who became visible (in the new
/// block only), <see cref="Left"/> are players who became invisible (in the old block only); players in
/// both blocks are untouched (they get a plain move update, not a re-enter). When <see cref="CellChanged"/>
/// is false the move stayed inside one cell, so there is nothing to enter/leave.
/// </summary>
public sealed class CellDiff
{
    /// <summary>A same-cell move: no visibility change. Never mutated — guarded by <see cref="CellChanged"/>.</summary>
    public static readonly CellDiff None = new();

    public bool CellChanged { get; init; }
    public List<ClientSession> Entered { get; } = new();
    public List<ClientSession> Left { get; } = new();

    /// <summary>Monsters that became visible / invisible to the moving player (the new-block − old-block and
    /// old-block − new-block monster diffs). One-directional: only the player is notified (monsters have no
    /// client). Empty on a same-cell move.</summary>
    public List<Monster> EnteredMonsters { get; } = new();
    public List<Monster> LeftMonsters { get; } = new();

    /// <summary>Map switches / gates that became visible / invisible to the moving player (static objects, so
    /// the diff is purely which cells entered/left the 3×3 block). One-directional. Empty on a same-cell move.</summary>
    public List<MapSwitch> EnteredSwitches { get; } = new();
    public List<MapSwitch> LeftSwitches { get; } = new();
    public List<MapGate> EnteredGates { get; } = new();
    public List<MapGate> LeftGates { get; } = new();
}

/// <summary>
/// The per-(channel, map) spatial grid — the C# port of a single C++ <c>CTMap</c>'s cell map
/// (<c>m_mapTCELL</c>, <c>TMap.cpp</c>). It buckets each player into exactly one 64×64 cell and answers the
/// fixed 3×3 "neighbour" visibility query (<c>CTMap::GetNeighbor</c>), which replaces the Phase-1 whole-map
/// broadcast.
///
/// <para>Byte-exact rules from the C++ (<c>NetCode.h</c>, <c>TMap.cpp</c>):</para>
/// <list type="bullet">
/// <item><c>CELL_SIZE = 64</c>; the grid is indexed by world <b>X and Z</b> (never Y).</item>
/// <item>Cell index per axis = <c>(WORD)pos / CELL_SIZE</c> — the float is cast to <b>unsigned 16-bit
/// first</b>, then integer-divided (so it truncates toward zero and wraps at 65536; world coords are ≥ 0,
/// there is no origin offset — cell (0,0) is world [0,64)).</item>
/// <item>Cell key = <c>MAKELONG(cellX, cellZ)</c> — X low word, Z high word.</item>
/// <item>Visibility = the fixed 3×3 block (the centre cell + its 8 neighbours); the scan origin is
/// centre − 1 on each axis and negative indices are skipped. There is no configurable sight radius.</item>
/// </list>
///
/// Cells are created lazily: a single-server deployment has every cell enabled, so an absent cell is
/// indistinguishable from an empty one for player visibility. The border-cell / cross-server machinery
/// (<c>IsMainCell</c>/<c>IsEnable</c>/<c>m_vServerID</c>) is deferred.
/// </summary>
public sealed class MapGrid
{
    /// <summary>C++ <c>#define CELL_SIZE (64)</c> (NetCode.h).</summary>
    public const int CellSize = 64;

    public MapGrid(byte channel, ushort mapId)
    {
        Channel = channel;
        MapId = mapId;
    }

    public byte Channel { get; }
    public ushort MapId { get; }

    private readonly Dictionary<uint, Cell> _cells = new();

    // Per-map id registries (C++ CTMap::m_mapTSWITCH / m_mapTGATE) — the authoritative Find lookups; the cells
    // additionally hold them for the 3×3 visibility broadcast.
    private readonly Dictionary<uint, MapSwitch> _switchById = new();
    private readonly Dictionary<uint, MapGate> _gateById = new();

    // ---- coordinate math (byte-exact with the C++) ----

    /// <summary>Cell index for one axis: <c>(WORD)world / CELL_SIZE</c> — cast to unsigned 16-bit, then divide.</summary>
    public static int Coord(float world) => (ushort)world / CellSize;

    /// <summary>Packs a world position into its cell key, <c>MAKELONG(cellX, cellZ)</c> (X low, Z high).</summary>
    public static uint KeyOf(float x, float z) => Key(Coord(x), Coord(z));

    private static uint Key(int cellX, int cellZ) => ((uint)(cellZ & 0xFFFF) << 16) | (uint)(cellX & 0xFFFF);

    private static (int X, int Z) Split(uint key) => ((int)(key & 0xFFFF), (int)(key >> 16));

    // ---- membership ----

    /// <summary>Places a player into the cell for its current position (C++ <c>CTCell::AddPlayer</c>).</summary>
    public void Add(ClientSession s)
    {
        if (s.Char is null) return;
        uint key = KeyOf(s.Char.PosX, s.Char.PosZ);
        GetOrCreate(key).Players[s.CharId] = s;
        s.CellKey = key;
    }

    /// <summary>Removes a player from the cell it was last bucketed in (C++ <c>CTCell::DelPlayer</c>).</summary>
    public void Remove(ClientSession s)
    {
        if (_cells.TryGetValue(s.CellKey, out var cell))
            cell.Players.Remove(s.CharId);
    }

    /// <summary>
    /// Moves a player to <paramref name="newX"/>/<paramref name="newZ"/> and returns the visibility diff
    /// (C++ <c>CTMap::OnMove</c>). No-op (returns <see cref="CellDiff.None"/>) when the centre cell is
    /// unchanged. The caller is responsible for having already written the new position onto the character.
    /// </summary>
    public CellDiff Relocate(ClientSession s, float newX, float newZ)
    {
        uint oldKey = s.CellKey;
        uint newKey = KeyOf(newX, newZ);
        if (oldKey == newKey) return CellDiff.None;

        var (ocx, ocz) = Split(oldKey);
        var (ncx, ncz) = Split(newKey);

        // Snapshot the old 3×3 occupants before the player is re-bucketed.
        var old3 = NeighborsAt(ocx, ocz, s).ToList();

        if (_cells.TryGetValue(oldKey, out var oldCell)) oldCell.Players.Remove(s.CharId);
        GetOrCreate(newKey).Players[s.CharId] = s;
        s.CellKey = newKey;

        var new3 = NeighborsAt(ncx, ncz, s).ToList();

        var diff = new CellDiff { CellChanged = true };
        var oldSet = new HashSet<ClientSession>(old3);   // reference equality (ClientSession has no Equals override)
        var newSet = new HashSet<ClientSession>(new3);
        foreach (var p in new3) if (!oldSet.Contains(p)) diff.Entered.Add(p);
        foreach (var p in old3) if (!newSet.Contains(p)) diff.Left.Add(p);

        // Monsters don't move on a player move, so their visibility diff is just old-block vs new-block cells.
        var oldMon = MonstersAt(ocx, ocz).ToList();
        var newMon = MonstersAt(ncx, ncz).ToList();
        var oldMonSet = new HashSet<Monster>(oldMon);
        var newMonSet = new HashSet<Monster>(newMon);
        foreach (var m in newMon) if (!oldMonSet.Contains(m)) diff.EnteredMonsters.Add(m);
        foreach (var m in oldMon) if (!newMonSet.Contains(m)) diff.LeftMonsters.Add(m);

        // Switches / gates are static, so their diff is likewise just old-block vs new-block cells.
        var oldSw = SwitchesAt(ocx, ocz).ToList();
        var newSw = SwitchesAt(ncx, ncz).ToList();
        var oldSwSet = new HashSet<MapSwitch>(oldSw);
        var newSwSet = new HashSet<MapSwitch>(newSw);
        foreach (var w in newSw) if (!oldSwSet.Contains(w)) diff.EnteredSwitches.Add(w);
        foreach (var w in oldSw) if (!newSwSet.Contains(w)) diff.LeftSwitches.Add(w);

        var oldGt = GatesAt(ocx, ocz).ToList();
        var newGt = GatesAt(ncx, ncz).ToList();
        var oldGtSet = new HashSet<MapGate>(oldGt);
        var newGtSet = new HashSet<MapGate>(newGt);
        foreach (var g in newGt) if (!oldGtSet.Contains(g)) diff.EnteredGates.Add(g);
        foreach (var g in oldGt) if (!newGtSet.Contains(g)) diff.LeftGates.Add(g);
        return diff;
    }

    // ---- visibility ----

    /// <summary>Players in <paramref name="self"/>'s 3×3 view block, excluding self (C++ <c>GetNeighbor</c>
    /// as used by the move broadcast, which has an explicit self-id guard).</summary>
    public IEnumerable<ClientSession> Neighbors(ClientSession self)
    {
        var (cx, cz) = Split(self.CellKey);
        return NeighborsAt(cx, cz, self);
    }

    /// <summary>All players in <paramref name="center"/>'s 3×3 view block, <b>including</b> self — the raw
    /// C++ <c>GetNeighbor</c> player list (no self filter), used by the block broadcast which the C++
    /// echoes to the mover.</summary>
    public IEnumerable<ClientSession> InView(ClientSession center)
    {
        var (cx, cz) = Split(center.CellKey);
        return NeighborsAt(cx, cz, self: null);
    }

    /// <summary>Players in the 3×3 block within <c>CELL_SIZE</c> Euclidean (X/Z) of the centre's position,
    /// <b>including</b> self — the C++ <c>GetNeerPlayer</c> used by the jump broadcast.</summary>
    public IEnumerable<ClientSession> NearView(ClientSession center)
    {
        if (center.Char is not { } ch) yield break;
        float ox = ch.PosX, oz = ch.PosZ;
        var (cx, cz) = Split(center.CellKey);
        foreach (var p in NeighborsAt(cx, cz, self: null))
        {
            if (p.Char is not { } pc) continue;
            float dx = ox - pc.PosX, dz = oz - pc.PosZ;
            if (dx * dx + dz * dz <= (float)CellSize * CellSize) yield return p; // GetDistance(...) <= CELL_SIZE
        }
    }

    /// <summary>The 3×3 fan-out around a centre cell — outer loop over Z, inner over X, skipping negatives.</summary>
    private IEnumerable<ClientSession> NeighborsAt(int centreX, int centreZ, ClientSession? self)
    {
        for (int i = -1; i <= 1; i++)        // Z
        {
            int nz = centreZ + i;
            if (nz < 0) continue;
            for (int j = -1; j <= 1; j++)    // X
            {
                int nx = centreX + j;
                if (nx < 0) continue;
                if (!_cells.TryGetValue(Key(nx, nz), out var cell)) continue;
                foreach (var p in cell.Players.Values)
                    if (self is null || !ReferenceEquals(p, self))
                        yield return p;
            }
        }
    }

    // ---- monsters (Phase 11) ----

    /// <summary>Places a monster into the cell for its current position (C++ <c>CTCell::AddMonster</c>).</summary>
    public void AddMonster(Monster m)
    {
        uint key = KeyOf(m.PosX, m.PosZ);
        GetOrCreate(key).Monsters[m.Id] = m;
        m.CellKey = key;
    }

    /// <summary>
    /// Re-buckets a moving monster and reports the player-visibility diff (C++ <c>CTMap::OnMove(CTMonster*)</c>,
    /// TMap.cpp:1346). Within one cell it is a bare position write; across a cell boundary the monster leaves
    /// the cells that drop out of its 3x3 block and enters the ones that come in, so the players there get
    /// CS_DELMON_ACK / CS_ADDMON_ACK. The multi-channel main-cell/border arms of the C++ are the cross-server
    /// topology this port does not carry (single-server).
    /// </summary>
    public CellDiff MoveMonster(Monster m, float newX, float newZ)
    {
        uint oldKey = m.CellKey;
        uint newKey = KeyOf(newX, newZ);
        if (oldKey == newKey)
        {
            m.PosX = newX; m.PosZ = newZ;
            return CellDiff.None;
        }

        var (ocx, ocz) = Split(oldKey);
        var (ncx, ncz) = Split(newKey);

        // Snapshot who can see it from the old block before re-bucketing.
        var old3 = NeighborsAt(ocx, ocz, self: null).ToList();

        if (_cells.TryGetValue(oldKey, out var oldCell)) oldCell.Monsters.Remove(m.Id);
        m.PosX = newX; m.PosZ = newZ;
        GetOrCreate(newKey).Monsters[m.Id] = m;
        m.CellKey = newKey;

        var new3 = NeighborsAt(ncx, ncz, self: null).ToList();

        var diff = new CellDiff { CellChanged = true };
        var oldSet = new HashSet<ClientSession>(old3);
        var newSet = new HashSet<ClientSession>(new3);
        foreach (var pl in new3) if (!oldSet.Contains(pl)) diff.Entered.Add(pl);
        foreach (var pl in old3) if (!newSet.Contains(pl)) diff.Left.Add(pl);
        return diff;
    }

    /// <summary>Removes a monster from the cell it was last bucketed in (C++ <c>CTCell::DelMonster</c>).</summary>
    public void RemoveMonster(Monster m)
    {
        if (_cells.TryGetValue(m.CellKey, out var cell)) cell.Monsters.Remove(m.Id);
    }

    /// <summary>The monsters in the 3×3 view block around <paramref name="cellKey"/> — the C++
    /// <c>CTCell::EnterPlayer</c> <c>m_mapMONSTER</c> loop over the neighbour cells.</summary>
    public IEnumerable<Monster> MonstersInView(uint cellKey)
    {
        var (cx, cz) = Split(cellKey);
        return MonstersAt(cx, cz);
    }

    /// <summary>The players in the 3×3 view block around <paramref name="cellKey"/> (no self filter) — used to
    /// announce a monster's spawn/despawn to nearby players (C++ <c>CTCell::EnterMonster</c> player loop).</summary>
    public IEnumerable<ClientSession> PlayersAround(uint cellKey)
    {
        var (cx, cz) = Split(cellKey);
        return NeighborsAt(cx, cz, self: null);
    }

    /// <summary>The 3×3 monster fan-out around a centre cell — same scan as <see cref="NeighborsAt"/>.</summary>
    private IEnumerable<Monster> MonstersAt(int centreX, int centreZ)
    {
        for (int i = -1; i <= 1; i++)        // Z
        {
            int nz = centreZ + i;
            if (nz < 0) continue;
            for (int j = -1; j <= 1; j++)    // X
            {
                int nx = centreX + j;
                if (nx < 0) continue;
                if (!_cells.TryGetValue(Key(nx, nz), out var cell)) continue;
                foreach (var m in cell.Monsters.Values) yield return m;
            }
        }
    }

    // ---- switches / gates (Phase 32) — static view objects placed by position, like monsters ----

    /// <summary>Places a switch into its cell + the id registry (C++ <c>CTMap::EnterMAP(LPTSWITCH)</c>).</summary>
    public void AddSwitch(MapSwitch sw)
    {
        uint key = KeyOf(sw.PosX, sw.PosZ);
        GetOrCreate(key).Switches[sw.SwitchId] = sw;
        sw.CellKey = key;
        _switchById[sw.SwitchId] = sw;
    }

    /// <summary>Places a gate into its cell + the id registry (C++ <c>CTMap::EnterMAP(LPTGATE)</c>).</summary>
    public void AddGate(MapGate g)
    {
        uint key = KeyOf(g.PosX, g.PosZ);
        GetOrCreate(key).Gates[g.GateId] = g;
        g.CellKey = key;
        _gateById[g.GateId] = g;
    }

    /// <summary>C++ <c>CTMap::FindSwitch</c> — the switch with this id on this (channel, map), or null.</summary>
    public MapSwitch? FindSwitch(uint id) => _switchById.GetValueOrDefault(id);
    /// <summary>The gate with this id, or null (C++ walks <c>m_mapTGATE</c> — there is no <c>FindGate</c>).</summary>
    public MapGate? FindGate(uint id) => _gateById.GetValueOrDefault(id);

    /// <summary>Every switch on this (channel, map) — used by the auto-revert sweep.</summary>
    public IEnumerable<MapSwitch> AllSwitches() => _switchById.Values;

    /// <summary>Switches / gates in the 3×3 view block around <paramref name="cellKey"/> (C++ <c>CTCell::EnterPlayer</c>
    /// switch/gate loops).</summary>
    public IEnumerable<MapSwitch> SwitchesInView(uint cellKey) { var (cx, cz) = Split(cellKey); return SwitchesAt(cx, cz); }
    public IEnumerable<MapGate> GatesInView(uint cellKey) { var (cx, cz) = Split(cellKey); return GatesAt(cx, cz); }

    /// <summary>Players in the 3×3 block around <paramref name="cellKey"/> — the switch/gate state-change broadcast set.</summary>
    public IEnumerable<ClientSession> PlayersAroundCell(uint cellKey) { var (cx, cz) = Split(cellKey); return NeighborsAt(cx, cz, self: null); }

    private IEnumerable<MapSwitch> SwitchesAt(int centreX, int centreZ)
    {
        for (int i = -1; i <= 1; i++)
        {
            int nz = centreZ + i; if (nz < 0) continue;
            for (int j = -1; j <= 1; j++)
            {
                int nx = centreX + j; if (nx < 0) continue;
                if (!_cells.TryGetValue(Key(nx, nz), out var cell)) continue;
                foreach (var w in cell.Switches.Values) yield return w;
            }
        }
    }

    private IEnumerable<MapGate> GatesAt(int centreX, int centreZ)
    {
        for (int i = -1; i <= 1; i++)
        {
            int nz = centreZ + i; if (nz < 0) continue;
            for (int j = -1; j <= 1; j++)
            {
                int nx = centreX + j; if (nx < 0) continue;
                if (!_cells.TryGetValue(Key(nx, nz), out var cell)) continue;
                foreach (var g in cell.Gates.Values) yield return g;
            }
        }
    }

    private Cell GetOrCreate(uint key)
    {
        if (!_cells.TryGetValue(key, out var cell))
        {
            cell = new Cell(key);
            _cells[key] = cell;
        }
        return cell;
    }
}
