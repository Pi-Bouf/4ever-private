namespace TMap.Data;

/// <summary>
/// One <c>TSWITCHCHART</c> row (C++ <c>CTBLSwitchChart</c>, DBAccess.h:3104) — a map switch definition.
/// <see cref="Start"/> (SQL <c>bStart</c>) is the initial open state; <see cref="LockOnOpen"/>/
/// <see cref="LockOnClose"/> freeze the switch once open/closed; <see cref="Duration"/> (ms) is an
/// auto-revert delay (0 = none, and also the re-flip cooldown window).
/// </summary>
public sealed record SwitchDef(uint SwitchId, ushort MapId, ushort PosX, ushort PosY, ushort PosZ,
    byte Start, byte LockOnOpen, byte LockOnClose, uint Duration);

/// <summary>
/// One <c>TGATECHART</c> row (C++ <c>CTBLGateChart</c>, DBAccess.h:3092) — a switch-driven gate. <see cref="Type"/>
/// is a <c>GATE_TYPE</c> (0 GT_ONESWITCH / 1 GT_SELECTSWITCH / 2 GT_MULTISWITCH). A gate carries no state of its
/// own: it mirrors the open state of its linked switch(es); a <c>GT_MULTISWITCH</c> gate opens only when all of
/// its switches share the new state. Multiple chart rows with the same <see cref="GateId"/> accumulate several
/// switches onto one gate.
/// </summary>
public sealed record GateDef(uint GateId, uint SwitchId, byte Type, ushort MapId, ushort PosX, ushort PosY, ushort PosZ);
