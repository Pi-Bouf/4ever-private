namespace TMap.Server.Map;

/// <summary>
/// A live map switch — the C# port of the C++ <c>tagSWITCH</c> (TMapType.h:1254). One per (channel, map)
/// instance, rebuilt from the chart at bring-up (never persisted). Players toggle it (via
/// <c>CS_SWITCHCHANGE_REQ</c> or a quest); it drives its linked <see cref="MapGate"/>s and fires the
/// <c>TT_RUNSWITCH</c> quest trigger. <see cref="Duration"/> is both an auto-revert delay and a re-flip
/// cooldown, measured from <see cref="StartTime"/>.
/// </summary>
public sealed class MapSwitch
{
    public uint SwitchId { get; init; }
    public ushort MapId { get; init; }
    public byte Channel { get; init; }
    public ushort PosX { get; init; }
    public ushort PosY { get; init; }
    public ushort PosZ { get; init; }
    public byte LockOnOpen { get; init; }
    public byte LockOnClose { get; init; }
    public uint Duration { get; init; }

    /// <summary>C++ <c>m_dwStartTime</c> — the map-clock tick of the last toggle (0 = never). Drives the
    /// duration cooldown gate and the auto-revert timer.</summary>
    public uint StartTime { get; set; }

    /// <summary>C++ <c>m_bOpened</c> — the current open state.</summary>
    public bool Opened { get; set; }

    /// <summary>The grid cell this switch occupies (for the 3×3 visibility broadcast).</summary>
    public uint CellKey { get; set; }

    /// <summary>C++ <c>m_vGate</c> — the gates this switch drives.</summary>
    public List<MapGate> Gates { get; } = new();
}

/// <summary>
/// A live switch-driven gate — the C# port of the C++ <c>tagGATE</c> (TMapType.h:1241). It carries no state
/// of its own beyond <see cref="Opened"/> (seeded from its switch at build); its open state is a synced
/// <b>visual door</b> — it does not block movement server-side (the C++ handles door collision client-side).
/// A <c>GT_MULTISWITCH</c> (<see cref="Type"/> 2) gate flips only when all its <see cref="Switches"/> share the
/// new state; other types flip on any linked switch change.
/// </summary>
public sealed class MapGate
{
    public uint GateId { get; init; }
    public uint SwitchId { get; init; }
    public ushort MapId { get; init; }
    public byte Channel { get; init; }
    public ushort PosX { get; init; }
    public ushort PosY { get; init; }
    public ushort PosZ { get; init; }

    /// <summary>C++ <c>m_bType</c> — a <c>GATE_TYPE</c> (0 GT_ONESWITCH / 1 GT_SELECTSWITCH / 2 GT_MULTISWITCH).</summary>
    public byte Type { get; init; }

    public bool Opened { get; set; }
    public uint CellKey { get; set; }

    /// <summary>C++ <c>m_vSwitch</c> — every switch wired to this gate (accumulated across chart rows).</summary>
    public List<MapSwitch> Switches { get; } = new();

    /// <summary>GATE_TYPE GT_MULTISWITCH — the gate opens only when all linked switches match the new state.</summary>
    public const byte GtMultiSwitch = 2;
}
