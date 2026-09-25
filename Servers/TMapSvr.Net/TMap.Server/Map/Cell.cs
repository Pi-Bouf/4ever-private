namespace TMap.Server.Map;

/// <summary>
/// One <c>CELL_SIZE</c>×<c>CELL_SIZE</c> (64×64 world-unit) square of the map grid — the C# port of the
/// C++ <c>CTCell</c> (<c>TCell.h</c>). A player occupies exactly one cell, keyed by its character id
/// (matching the C++ <c>m_mapPLAYER</c> keyed by <c>m_dwID</c>).
///
/// Holds players (Phase 3) and field monsters (Phase 11), each in its own map keyed by object id — the C++
/// cell's <c>m_mapPLAYER</c> and <c>m_mapMONSTER</c>. The C++ cell also carries recall-mons, self-objs,
/// companions, switches and gates, plus the per-channel border-cell flags (<c>m_vEnable</c>/<c>m_vExtCell</c>/
/// <c>m_vServerID[8][51]</c>) that drive cross-server/cross-channel visibility. Those are single-server-inert
/// and deferred (see PORT_STATUS.md).
/// </summary>
internal sealed class Cell
{
    public Cell(uint id) => Id = id;

    /// <summary>The packed cell key, <c>MAKELONG(cellX, cellZ)</c> — X in the low word, Z in the high word.</summary>
    public uint Id { get; }

    /// <summary>Players in this cell, keyed by character id (C++ <c>m_mapPLAYER</c>).</summary>
    public Dictionary<uint, ClientSession> Players { get; } = new();

    /// <summary>Field monsters in this cell, keyed by monster instance id (C++ <c>m_mapMONSTER</c>).</summary>
    public Dictionary<uint, Monster> Monsters { get; } = new();

    /// <summary>Summons in this cell, keyed by recall id (C++ <c>CTCell::m_mapRECALLMON</c>).</summary>
    public Dictionary<uint, RecallMon> Recalls { get; } = new();

    /// <summary>Map switches in this cell, keyed by switch id (C++ <c>CTCell::m_mapSWITCH</c>).</summary>
    public Dictionary<uint, MapSwitch> Switches { get; } = new();

    /// <summary>Switch-driven gates in this cell, keyed by gate id (C++ <c>CTCell::m_mapGATE</c>).</summary>
    public Dictionary<uint, MapGate> Gates { get; } = new();
}
