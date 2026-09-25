namespace TMap.Data;

/// <summary>A named spawn point (C++ <c>TSPAWNPOS</c> from <c>TSPAWNPOSCHART</c>): where a teleport, a return or a
/// revival puts a player. Keyed by <see cref="Id"/> in <see cref="TemplateStore.SpawnPositions"/>.</summary>
public sealed record SpawnPosRow(ushort Id, ushort MapId, float PosX, float PosY, float PosZ, byte Type);

/// <summary>One condition slot of a portal destination (C++ <c>m_bCondition[i]</c> / <c>m_dwConditionID[i]</c>).</summary>
public readonly record struct PortalCondition(byte Type, uint Id);

/// <summary>A destination offered by a portal (C++ <c>TDESTINATION</c> from <c>TDESTINATIONCHART</c>).
/// <see cref="Conditions"/> always holds <c>PORTALCONDITION_COUNT</c> (3) slots.</summary>
public sealed record PortalDestination(ushort DestId, uint Price, byte Enable, PortalCondition[] Conditions);

/// <summary>A portal (C++ <c>TPORTAL</c> from <c>TPORTALCHART</c>): the spawn point it lands on, and the other
/// portals it can send you to. A destination is kept only when its target portal exists (TMapSvr.cpp:3806).</summary>
public sealed record PortalRow(ushort PortalId, byte Country, ushort LocalId, ushort SpawnId, byte Condition)
{
    public Dictionary<ushort, PortalDestination> Destinations { get; } = new();
}
