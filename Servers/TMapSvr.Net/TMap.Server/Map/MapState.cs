namespace TMap.Server.Map;

/// <summary>
/// The whole in-memory world of this map server. Only ever touched on the batch thread, so it needs no
/// locks (replaces the C++ batch-thread + global lock). It holds the player registry (<see cref="ByChar"/>)
/// and one <see cref="MapGrid"/> per (channel, map) — the C# counterpart of the C++
/// <c>CTChannel</c>→<c>CTMap</c>→<c>CTCell</c> hierarchy. Visibility is the grid's fixed 3×3 cell block
/// (C++ <c>GetNeighbor</c>), replacing the Phase-1 whole-map broadcast. Monster spawns, NPCs and world
/// objects are deferred — see PORT_STATUS.md.
/// </summary>
public sealed class MapState
{
    /// <summary>Live/entering players indexed by character id (populated once CS_CONNECT_REQ resolves the char).</summary>
    public Dictionary<uint, ClientSession> ByChar { get; } = new();

    /// <summary>One spatial grid per (channel, map), created on demand — one C++ <c>CTMap</c> per pair.</summary>
    private readonly Dictionary<(byte Channel, ushort MapId), MapGrid> _grids = new();

    /// <summary>Live field monsters indexed by instance id (C++ per-map <c>m_mapTMONSTER</c>, flattened).</summary>
    private readonly Dictionary<uint, Monster> _monsters = new();

    /// <summary>The NPC registry indexed by NPC id (C++ module <c>m_mapTNpc</c>). Static (loaded once at
    /// bring-up); NPCs are not view objects, so they live here rather than in a grid cell.</summary>
    private readonly Dictionary<ushort, Npc> _npcs = new();

    /// <summary>Registers a session under its character id (call once the char id is known, on the batch thread).</summary>
    public void Register(ClientSession s)
    {
        if (s.CharId != 0) ByChar[s.CharId] = s;
    }

    /// <summary>Removes a session from the registry and its grid (batch thread). No-op if never registered.</summary>
    public void Remove(ClientSession s)
    {
        LeaveWorld(s);
        if (s.CharId != 0 && ByChar.TryGetValue(s.CharId, out var cur) && ReferenceEquals(cur, s))
            ByChar.Remove(s.CharId);
    }

    public ClientSession? FindByChar(uint charId) =>
        ByChar.TryGetValue(charId, out var s) ? s : null;

    // ---- spatial grid (C++ CTMap per channel+map) ----

    private MapGrid GridFor(byte channel, ushort mapId)
    {
        var key = (channel, mapId);
        if (!_grids.TryGetValue(key, out var g))
        {
            g = new MapGrid(channel, mapId);
            _grids[key] = g;
        }
        return g;
    }

    /// <summary>
    /// Places a live player into its (channel, map) grid at its current position (C++ <c>CTMap::EnterMAP</c>
    /// cell insertion). Call once the player goes in-game (CS_CONREADY_REQ), before broadcasting its entry.
    /// </summary>
    public void EnterWorld(ClientSession s)
    {
        if (s.Char is null) return;
        var g = GridFor(s.Channel, s.Char.MapId);
        s.Grid = g;
        g.Add(s);
    }

    /// <summary>Removes a player from its grid (C++ <c>CTMap::LeaveMAP</c> cell removal). No-op if not placed.</summary>
    public void LeaveWorld(ClientSession s)
    {
        s.Grid?.Remove(s);
        s.Grid = null;
    }

    /// <summary>
    /// Re-buckets a moved player and returns the visibility diff (C++ <c>CTMap::OnMove</c>). The caller must
    /// have already written the new position onto the character. Returns <see cref="CellDiff.None"/> if the
    /// player is not currently placed in a grid.
    /// </summary>
    public CellDiff Relocate(ClientSession s, float newX, float newZ) =>
        s.Grid?.Relocate(s, newX, newZ) ?? CellDiff.None;

    /// <summary>
    /// Other players that can see <paramref name="self"/> — the occupants of its 3×3 cell view block,
    /// excluding self (C++ <c>CTMap::GetNeighbor</c>). Empty if the player is not placed in a grid.
    /// </summary>
    public IEnumerable<ClientSession> Neighbors(ClientSession self) =>
        self.Grid?.Neighbors(self) ?? Enumerable.Empty<ClientSession>();

    /// <summary>The full 3×3 view block including self (C++ <c>GetNeighbor</c>) — for the block broadcast.</summary>
    public IEnumerable<ClientSession> InView(ClientSession self) =>
        self.Grid?.InView(self) ?? Enumerable.Empty<ClientSession>();

    /// <summary>The 3×3 block within <c>CELL_SIZE</c> of self, including self (C++ <c>GetNeerPlayer</c>) — for the jump broadcast.</summary>
    public IEnumerable<ClientSession> NearView(ClientSession self) =>
        self.Grid?.NearView(self) ?? Enumerable.Empty<ClientSession>();

    /// <summary>Every in-game player on the server (used for by-name lookups, not for visibility).</summary>
    public IEnumerable<ClientSession> AllInGame()
    {
        foreach (var s in ByChar.Values)
            if (s.State == EnterState.InGame && s.Char is not null)
                yield return s;
    }

    // ---- monsters (C++ CTMap monster occupancy) ----

    /// <summary>Places a monster into its (channel, map) grid cell and the registry (C++ <c>CTMap::EnterMAP</c>
    /// cell insertion). The caller announces it to nearby players.</summary>
    public void AddMonster(Monster m)
    {
        GridFor(m.Channel, m.MapId).AddMonster(m);
        _monsters[m.Id] = m;
    }

    /// <summary>Removes a monster from its grid cell and the registry (C++ <c>CTMap::LeaveMAP</c>).</summary>
    public void RemoveMonster(Monster m)
    {
        if (_grids.TryGetValue((m.Channel, m.MapId), out var g)) g.RemoveMonster(m);
        // Only if the id still maps to this object — a stale remove must not take its respawn with it.
        if (_monsters.TryGetValue(m.Id, out var cur) && ReferenceEquals(cur, m)) _monsters.Remove(m.Id);
    }

    /// <summary>Moves a monster to a new position, re-bucketing its grid cell and reporting the player
    /// visibility diff (C++ <c>CTMap::OnMove(CTMonster*)</c>). No-op diff when it stays in the same cell.</summary>
    public CellDiff MoveMonster(Monster m, float x, float z) =>
        _grids.TryGetValue((m.Channel, m.MapId), out var g) ? g.MoveMonster(m, x, z) : CellDiff.None;

    public Monster? FindMonster(uint id) => _monsters.GetValueOrDefault(id);

    /// <summary>Every live monster (registry order) — used by tests / the deferred tick.</summary>
    public IEnumerable<Monster> AllMonsters() => _monsters.Values;

    /// <summary>Monsters in <paramref name="s"/>'s 3×3 view block (C++ <c>CTCell::EnterPlayer</c> monster loop).
    /// Empty if the player is not placed in a grid.</summary>
    public IEnumerable<Monster> MonstersInView(ClientSession s) =>
        s.Grid?.MonstersInView(s.CellKey) ?? Enumerable.Empty<Monster>();

    /// <summary>Players in a monster's 3×3 view block (C++ <c>CTCell::EnterMonster</c> player loop).</summary>
    public IEnumerable<ClientSession> PlayersAround(Monster m) =>
        _grids.TryGetValue((m.Channel, m.MapId), out var g)
            ? g.PlayersAround(m.CellKey) : Enumerable.Empty<ClientSession>();

    // ---- NPCs (C++ module m_mapTNpc — a static registry, not a grid occupant) ----

    /// <summary>Registers an NPC by id (C++ <c>m_mapTNpc.insert</c>). Called at bring-up / by tests.</summary>
    public void AddNpc(Npc n) => _npcs[n.Id] = n;

    /// <summary>C++ <c>CTMapSvrModule::FindTNpc</c> — the NPC with this id, or null.</summary>
    public Npc? FindNpc(ushort id) => _npcs.GetValueOrDefault(id);

    /// <summary>Every registered NPC (used by the bring-up rebuild / tests).</summary>
    public IEnumerable<Npc> AllNpcs() => _npcs.Values;

    // ---- switches / gates (Phase 32) — per-(channel, map) static view objects, like monsters ----

    /// <summary>Places a switch into its (channel, map) grid (cell + id registry).</summary>
    public void AddSwitch(MapSwitch sw) => GridFor(sw.Channel, sw.MapId).AddSwitch(sw);

    /// <summary>Places a gate into its (channel, map) grid.</summary>
    public void AddGate(MapGate g) => GridFor(g.Channel, g.MapId).AddGate(g);

    /// <summary>C++ <c>CTMap::FindSwitch</c> — the switch with this id on the given (channel, map), or null.</summary>
    public MapSwitch? FindSwitch(byte channel, ushort mapId, uint id) =>
        _grids.TryGetValue((channel, mapId), out var g) ? g.FindSwitch(id) : null;

    /// <summary>The gate with this id on the given (channel, map), or null (used to accumulate multi-switch gates).</summary>
    public MapGate? FindGate(byte channel, ushort mapId, uint id) =>
        _grids.TryGetValue((channel, mapId), out var g) ? g.FindGate(id) : null;

    /// <summary>Switches / gates in <paramref name="s"/>'s 3×3 view block (empty if not placed in a grid).</summary>
    public IEnumerable<MapSwitch> SwitchesInView(ClientSession s) =>
        s.Grid?.SwitchesInView(s.CellKey) ?? Enumerable.Empty<MapSwitch>();
    public IEnumerable<MapGate> GatesInView(ClientSession s) =>
        s.Grid?.GatesInView(s.CellKey) ?? Enumerable.Empty<MapGate>();

    /// <summary>Players in the 3×3 block around a switch/gate cell on (channel, map) — the state-change broadcast set.</summary>
    public IEnumerable<ClientSession> PlayersAroundCell(byte channel, ushort mapId, uint cellKey) =>
        _grids.TryGetValue((channel, mapId), out var g) ? g.PlayersAroundCell(cellKey) : Enumerable.Empty<ClientSession>();

    /// <summary>Every live switch across all (channel, map) grids — used by the auto-revert sweep.</summary>
    public IEnumerable<MapSwitch> AllSwitches()
    {
        foreach (var g in _grids.Values)
            foreach (var sw in g.AllSwitches())
                yield return sw;
    }
}
