using TMap.Protocol;

namespace TMap.Server.Map;

/// <summary>
/// Phase 32 — the map switch/gate subsystem (C++ <c>CTPlayer::ChangeSwitch</c> / <c>CTMapSvrModule::ChangeSwitch</c>
/// + the <c>TSWITCH</c>/<c>TGATE</c> graph). A player activates a switch (<c>CS_SWITCHCHANGE_REQ</c>) or a quest
/// flips it (<see cref="ExecSwitch"/>); the switch toggles (subject to its lock flags + duration cooldown), fires
/// the <c>TT_RUNSWITCH</c> quest trigger, broadcasts <c>CS_SWITCHCHANGE_ACK</c>, and drives its linked gates
/// (each broadcasting <c>CS_GATECHANGE_ACK</c> + firing <c>TT_RUNGATE</c>). Switches/gates are static view
/// objects placed in the grid (add/del on enter/move like monsters); a gate is a synced <b>visual door</b> and
/// does not block movement server-side.
///
/// <para><b>Deviations (documented — PORT_STATUS.md):</b> the <c>dwDuration</c> auto-revert is a local one-shot
/// queue (<see cref="_switchReverts"/>) ticked in <c>OnTimerAsync</c>, collapsing the C++ AI-server round-trip
/// (<c>SM_SWITCHSTART_REQ</c>→<c>SM_SWITCHCHANGE_REQ</c>). Deferred: the <c>GT_SELECTSWITCH</c> random-switch pick
/// (a template-clone nuance for instanced dungeons), and the battle-zone <c>m_bGateOpened</c>/gatekeeper-boss
/// gating (a separate siege subsystem). Party/dungeon map instances are single per (channel, map).</para>
/// </summary>
public sealed partial class MapService
{
    // SWITCH_CONTROL (TMapType.h:244) + SWITCH_RESULT (NetCode.h:669).
    private const byte SwcToggle = 0, SwcOpen = 1, SwcClose = 2;
    private const byte SwitchSuccess = 0;

    /// <summary>Pending one-shot auto-reverts (C++ module <c>m_vTSWITCHOBJ</c>): a switch toggled with a
    /// <c>Duration</c> flips back once, <c>Duration</c> ms after the toggle tick.</summary>
    private readonly List<SwitchRevert> _switchReverts = new();
    private readonly record struct SwitchRevert(uint EnqueueTick, uint Duration, byte Channel, ushort MapId, uint SwitchId);

    /// <summary>Builds the per-(channel, map) runtime switches + gates from the charts (C++ <c>CTMap</c> load).
    /// Idempotent-ish (call once at bring-up). Links each gate to every switch its chart rows reference — the
    /// <c>GT_SELECTSWITCH</c> random pick is a template-clone nuance (deferred). No-op DB-free (empty charts).</summary>
    public void InitSwitches()
    {
        var channels = _opt.Channels is { Length: > 0 } ? _opt.Channels : new byte[] { 1 };
        foreach (var channel in channels)
        {
            foreach (var def in _templates.Switches)
                _state.AddSwitch(new MapSwitch
                {
                    SwitchId = def.SwitchId, MapId = def.MapId, Channel = channel,
                    PosX = def.PosX, PosY = def.PosY, PosZ = def.PosZ,
                    LockOnOpen = def.LockOnOpen, LockOnClose = def.LockOnClose, Duration = def.Duration,
                    Opened = def.Start != 0, StartTime = 0,
                });

            foreach (var def in _templates.Gates)
            {
                var sw = _state.FindSwitch(channel, def.MapId, def.SwitchId);
                if (sw is null) continue;   // C++ skips a gate row whose switch is missing (TMapSvr.cpp:2539)
                var gate = _state.FindGate(channel, def.MapId, def.GateId);
                if (gate is null)
                {
                    gate = new MapGate
                    {
                        GateId = def.GateId, SwitchId = def.SwitchId, MapId = def.MapId, Channel = channel,
                        PosX = def.PosX, PosY = def.PosY, PosZ = def.PosZ, Type = def.Type,
                        Opened = sw.Opened,   // C++ gate initial state = its switch's state (TMapSvr.cpp:2551)
                    };
                    _state.AddGate(gate);
                }
                gate.Switches.Add(sw);   // multi-row → multi-switch accumulation
                sw.Gates.Add(gate);
            }
        }
        if (_templates.Switches.Count > 0)
            _log.LogInformation("Built {Switches} switches / {Gates} gates per channel.",
                _templates.Switches.Count, _templates.Gates.Count);
    }

    private void OnCS_SWITCHCHANGE_REQ(ClientSession s, PacketReader r)
    {
        uint switchId = r.ReadUInt32();
        // C++ guard (CSHandler.cpp:10238): must be on a map (in-game) and the main character. The return value
        // of ChangeSwitch is ignored — a rejected flip (locked / cooling) is silent.
        if (s.State != EnterState.InGame || s.Char is not { } ch || !s.IsMain) return;
        ChangeSwitchPlayer(s, ch, switchId);
    }

    /// <summary>C++ <c>CTPlayer::ChangeSwitch</c> (TPlayer.cpp:3838) — the player-triggered toggle: enforces the
    /// lock flags + the duration cooldown, toggles, fires <c>TT_RUNSWITCH</c>, broadcasts, and drives gates
    /// (toggling each + firing <c>TT_RUNGATE</c>). Returns whether the switch flipped.</summary>
    public bool ChangeSwitchPlayer(ClientSession s, Character ch, uint switchId)
    {
        if (_state.FindSwitch(s.Channel, ch.MapId, switchId) is not { } sw) return false;
        if (sw.LockOnClose != 0 && !sw.Opened) return false;   // locked closed
        if (sw.LockOnOpen != 0 && sw.Opened) return false;     // locked open
        // Duration cooldown (C++ m_dwTick - m_dwStartTime < m_dwDuration). StartTime 0 = never toggled ⇒ no
        // cooldown (the C++ tick is large at load; the port's map clock starts at 0, so guard the sentinel).
        if (sw.StartTime != 0 && unchecked(NowMs - sw.StartTime) < sw.Duration) return false;

        if (sw.Duration != 0)   // schedule the one-shot auto-revert (C++ SendSM_SWITCHSTART_REQ, collapsed local)
            _switchReverts.Add(new SwitchRevert(NowMs, sw.Duration, s.Channel, ch.MapId, switchId));

        sw.StartTime = NowMs;
        sw.Opened = !sw.Opened;   // unconditional toggle

        CheckQuest(s, 0, ch.PosX, ch.PosY, ch.PosZ, switchId, 0, TtRunSwitch, 1);

        var sAck = BuildSwitchChange(SwitchSuccess, switchId, sw.Opened);
        foreach (var p in _state.PlayersAroundCell(s.Channel, ch.MapId, sw.CellKey)) p.Send(sAck);

        foreach (var gate in sw.Gates)
        {
            if (gate.Type == MapGate.GtMultiSwitch && gate.Switches.Any(g => g.Opened != sw.Opened)) continue;
            gate.Opened = !gate.Opened;   // player path TOGGLES the gate (C++ TPlayer.cpp:3920)
            CheckQuest(s, 0, ch.PosX, ch.PosY, ch.PosZ, gate.GateId, 0, TtRunGate, 1);
            var gAck = BuildGateChange(gate.GateId, gate.Opened);
            foreach (var p in _state.PlayersAroundCell(s.Channel, gate.MapId, gate.CellKey)) p.Send(gAck);
        }
        return true;
    }

    /// <summary>C++ <c>CTMapSvrModule::ChangeSwitch</c> (TMapSvr.cpp:7774) — the server/system toggle: ignores
    /// the lock/duration gates, applies an explicit <c>SWC_*</c> action, and <b>assigns</b> the switch state to
    /// each driven gate (no quest hook). Used by the auto-revert (SWC_TOGGLE). Returns whether it flipped.</summary>
    public bool ChangeSwitchModule(byte channel, ushort mapId, uint switchId, byte swc)
    {
        if (_state.FindSwitch(channel, mapId, switchId) is not { } sw) return false;
        switch (swc)
        {
            case SwcToggle: sw.Opened = !sw.Opened; break;
            case SwcOpen: if (sw.Opened) return false; sw.Opened = true; break;
            case SwcClose: if (!sw.Opened) return false; sw.Opened = false; break;
            default: return false;
        }
        var sAck = BuildSwitchChange(SwitchSuccess, switchId, sw.Opened);
        foreach (var p in _state.PlayersAroundCell(channel, mapId, sw.CellKey)) p.Send(sAck);

        foreach (var gate in sw.Gates)
        {
            if (gate.Type == MapGate.GtMultiSwitch && gate.Switches.Any(g => g.Opened != sw.Opened)) continue;
            gate.Opened = sw.Opened;   // module path ASSIGNS the switch state (C++ TMapSvr.cpp:7842)
            var gAck = BuildGateChange(gate.GateId, gate.Opened);
            foreach (var p in _state.PlayersAroundCell(channel, gate.MapId, gate.CellKey)) p.Send(gAck);
        }
        return true;
    }

    /// <summary>The auto-revert sweep (C++ module <c>OnTimer</c> over <c>m_vTSWITCHOBJ</c>, TMapSvr.cpp:6237): a
    /// switch toggled with a duration flips back once, <c>Duration</c> ms later, via the module <c>SWC_TOGGLE</c>
    /// path. Public so tests can drive it at a chosen <paramref name="now"/>.</summary>
    public void RunSwitchReverts(uint now)
    {
        for (int i = _switchReverts.Count - 1; i >= 0; i--)
        {
            var rv = _switchReverts[i];
            if (unchecked(now - rv.EnqueueTick) < rv.Duration) continue;
            _switchReverts.RemoveAt(i);
            ChangeSwitchModule(rv.Channel, rv.MapId, rv.SwitchId, SwcToggle);
        }
    }

    /// <summary>Sends the switches + gates in a player's 3×3 view (C++ <c>CTCell::EnterPlayer</c> switch/gate
    /// loops — switches first, then gates). Called when the player goes live (CS_CONREADY_REQ).</summary>
    private void SendSwitchesAndGatesInView(ClientSession s)
    {
        foreach (var w in _state.SwitchesInView(s)) s.Send(BuildSwitchAdd(w.SwitchId, w.Opened));
        foreach (var g in _state.GatesInView(s)) s.Send(BuildGateAdd(g.GateId, g.Opened));
    }

    /// <summary>Applies a move's switch/gate visibility diff to the moving player (C++ cell enter/leave). Called
    /// from <c>RelocateAndExchangeView</c> alongside the player/monster diffs.</summary>
    private static void ApplySwitchGateDiff(ClientSession s, CellDiff diff)
    {
        foreach (var w in diff.LeftSwitches) s.Send(BuildSwitchDel(w.SwitchId));
        foreach (var w in diff.EnteredSwitches) s.Send(BuildSwitchAdd(w.SwitchId, w.Opened));
        foreach (var g in diff.LeftGates) s.Send(BuildGateDel(g.GateId));
        foreach (var g in diff.EnteredGates) s.Send(BuildGateAdd(g.GateId, g.Opened));
    }

    // ---- senders (byte-exact, CSSender.cpp:3934-4006) ----

    private static byte[] BuildSwitchAdd(uint switchId, bool opened)
        => new PacketWriter(Msg.CS_SWITCHADD_ACK).WriteUInt32(switchId).WriteByte((byte)(opened ? 1 : 0)).ToArray();
    private static byte[] BuildSwitchDel(uint switchId)
        => new PacketWriter(Msg.CS_SWITCHDEL_ACK).WriteUInt32(switchId).ToArray();
    private static byte[] BuildSwitchChange(byte result, uint switchId, bool opened)
        => new PacketWriter(Msg.CS_SWITCHCHANGE_ACK).WriteByte(result).WriteUInt32(switchId).WriteByte((byte)(opened ? 1 : 0)).ToArray();

    private static byte[] BuildGateAdd(uint gateId, bool opened)
        => new PacketWriter(Msg.CS_GATEADD_ACK).WriteUInt32(gateId).WriteByte((byte)(opened ? 1 : 0)).ToArray();
    private static byte[] BuildGateDel(uint gateId)
        => new PacketWriter(Msg.CS_GATEDEL_ACK).WriteUInt32(gateId).ToArray();
    private static byte[] BuildGateChange(uint gateId, bool opened)
        => new PacketWriter(Msg.CS_GATECHANGE_ACK).WriteUInt32(gateId).WriteByte((byte)(opened ? 1 : 0)).ToArray();
}
