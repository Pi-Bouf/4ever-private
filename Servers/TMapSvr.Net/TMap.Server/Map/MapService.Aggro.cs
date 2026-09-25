using TMap.Data;
using TMap.Protocol;

namespace TMap.Server.Map;

/// <summary>
/// Monster aggro / retargeting — the map-service half of the C++ <c>m_mapAggro</c> hate table.
/// <see cref="Monster"/> owns the table and the pure arithmetic (<c>SetAggro</c>/<c>LeaveAggro</c> decide who to
/// hate + whether to switch); this file applies the resulting retarget (the C++ <c>CTAICmdChgHost</c> action):
/// set the monster's target/host, flip it into battle, seed the new target's minimal hate, and broadcast the
/// host-change (<c>CS_MONHOST_ACK</c>) to nearby players. Aggro is <b>skill-driven</b> (<c>max(1, GetAggro)</c>,
/// warrior ×1.5), not raw damage — so a monster stays on the highest-cumulative-aggro attacker with a 10%
/// sticky-target hysteresis, and on losing that target re-picks the next-highest in-view attacker before it
/// gives up and goes home.
///
/// <para><b>Deferred (documented — PORT_STATUS.md):</b> the data-driven AI-command table (<c>TMONSTERAI</c>) that
/// wires events→commands; the <c>MT_GOHOME</c> walk-back state (the port pins a monster to its spawn anchor, so
/// disengage collapses straight to <c>MT_NORMAL</c>); call-for-help / low-HP flee (gated by the unloaded
/// <c>m_bCall</c>/<c>m_wKind</c> chart flags); pack/leader aggro spread (<c>SetEventToFollower</c>); the
/// out-of-view previous-host <c>CS_MONHOST_ACK(FALSE)</c> (only in-view players are notified); and the
/// <c>dwAggro</c> magnitude column (live hate = 1/hit until loaded — the mechanics are exact regardless).</para>
/// </summary>
public sealed partial class MapService
{
    private const byte TcontryN = 3;   // TCONTRY_TYPE TCONTRY_N (neutral)

    /// <summary>C++ <c>GetAttackCountry</c> (TMapType.h:149) — the ally-faction override, else the base country.</summary>
    private static byte GetAttackCountry(byte country, byte aidCountry) => aidCountry != TcontryN ? aidCountry : country;

    /// <summary>C++ <c>GetWarCountry</c> for a player (TObjBase.cpp:4962).</summary>
    private static byte WarCountryOf(Character ch) => ch.AidCountry != TcontryN ? ch.AidCountry : ch.Country;

    /// <summary>C++ <c>CTObjBase::Defend</c>'s opening <c>SetAggro</c> (TMonster.cpp:935) — a hostile hit adds hate
    /// and may flip the monster's target. Only a hostile (offensive) skill aggros; a basic attack (no template) is
    /// treated as hostile with the floor aggro of 1 (<c>max(1, GetAggro)</c>).</summary>
    private void Aggravate(Monster mon, Character ch, SkillTemplate? atkTpl, byte level, byte canSelect,
        uint hostId, uint attackId)
    {
        if (atkTpl is { } t && !t.IsNegative) return;                         // a positive skill never aggros
        int aggro = (int)Math.Max(1u, atkTpl?.GetAggro(level) ?? 0u);         // max(1, GetAggro(level))
        byte atkCountry = GetAttackCountry(ch.Country, ch.AidCountry);
        uint objId = canSelect != 0 ? attackId : hostId;                      // bCanSelect ? attacker : host (TMonster.cpp:939)
        var dec = mon.SetAggro(hostId, objId, OtPc, atkCountry, ch.Class, 0, 0, aggro, active: true);
        if (dec is { } d) ApplyRetarget(mon, d);
    }

    /// <summary>C++ <c>CTAICmdChgHost::ExecAI</c> (TAICmdChgHost.cpp:25) — assign the monster's new host/target,
    /// enter battle on the first pull, seed the target's minimal hate (<c>AddAggro(...,1)</c>), and notify clients
    /// via <c>CS_MONHOST_ACK</c>.</summary>
    private void ApplyRetarget(Monster mon, Monster.AggroTarget dec)
    {
        // A scripted monster does not get the folded retarget. The hate table only *decides*; what
        // that decision means is the chart's business. Fire the C++ event
        // (OnEvent(AT_DEFEND, 0, host, obj, type) — TMonster.cpp:224/236) and let the script's ChgHost /
        // ChgMode commands carry it out.
        if (mon.Ai is not null)
        {
            OnAiEvent(mon, AiTrigger.Defend, 0, dec.HostId, dec.ObjId, dec.ObjType);
            return;
        }

        if (mon.Mode != MtBattle) mon.EnterBattle(NowMs, RecoverInit);        // ChgMode MT_NORMAL → MT_BATTLE
        mon.HostId = dec.HostId;
        mon.TargetId = dec.ObjId;
        mon.TargetType = dec.ObjType;
        byte tc = _state.FindByChar(dec.ObjId)?.Char is { } tch ? WarCountryOf(tch) : (byte)0;
        mon.AddAggro(dec.HostId, dec.ObjId, dec.ObjType, tc, 1);              // ChgHost AddAggro(...,1)
        NotifyHost(mon);
    }

    /// <summary>C++ <c>CTMonster::NotifyHost</c> (TMonster.cpp:2369) — tell nearby players who the monster now
    /// hosts: <c>TRUE</c> to the new host, <c>FALSE</c> to everyone else in view (the previous host, if still in
    /// view, is thereby cleared).</summary>
    private void NotifyHost(Monster mon)
    {
        _log.LogDebug("[mon] HOST mon {Mon} -> char {Host} (notifying {Count} viewer(s)).", mon.Id, mon.HostId, _state.PlayersAround(mon).Count());
        foreach (var p in _state.PlayersAround(mon))
            SendCS_MONHOST_ACK(p, mon.Id, p.Char is { CharId: var cid } && cid == mon.HostId ? (byte)1 : (byte)0);
    }

    /// <summary>C++ <c>SendCS_MONHOST_ACK</c> (CSSender.cpp:688) — <c>dwMonID · bSet</c>.</summary>
    private static void SendCS_MONHOST_ACK(ClientSession p, uint monId, byte bSet)
    {
        var w = new PacketWriter(Msg.CS_MONHOST_ACK, capacity: 8);
        w.WriteUInt32(monId);
        w.WriteByte(bSet);
        p.Send(w);
    }

    /// <summary>C++ <c>CTMonster::LeaveAggro</c> (TMonster.cpp:87) + the caller's neighbour test: the current
    /// target left (out of range / gone / fled past the leash), so drop its hate and switch to the highest
    /// remaining in-view hostile attacker; a top-aggro survivor that is no longer in view is dropped too (the C++
    /// recurse-drop of a non-neighbour). If nothing hostile remains in view, leave battle and head home
    /// (<see cref="DropAggro"/> → <c>MT_NORMAL</c>).</summary>
    private void Disengage(Monster mon, uint leaveId, byte leaveType, uint nowMs)
    {
        var survivor = mon.LeaveAggro(leaveId, leaveType);
        while (survivor is { } s)
        {
            if (_state.FindByChar(s.ObjId) is { State: EnterState.InGame, Char: { Hp: > 0 } }
                && _state.PlayersAround(mon).Any(p => p.Char is { CharId: var cid } && cid == s.ObjId))
            {
                ApplyRetarget(mon, s);
                return;
            }
            survivor = mon.LeaveAggro(s.ObjId, s.ObjType);   // non-viewable top-aggro → drop, try the next
        }

        // Nothing left to fight. The C++ LeaveAggro fires AT_LEAVELB here (TMonster.cpp:134) and lets the script
        // decide: script 1 runs ChgMode (BATTLE -> GOHOME, target cleared) then Gohome, so the monster runs back
        // to its anchor, still driven by its host client, until AT_ATHOME returns it to roaming. Hard-resetting
        // it instead left it standing mid-field with no host, rejecting the moves its client kept sending.
        if (mon.Ai is not null)
        {
            _log.LogDebug("[mon] LEAVE mon {Mon}: nothing left to fight, AT_LEAVELB (going home).", mon.Id);
            OnAiEvent(mon, AiTrigger.LeaveLb, 0, mon.HostId, leaveId, leaveType);
            return;
        }
        DropAggro(mon, nowMs);   // script-less monsters keep the built-in reset
    }

    /// <summary>C++ <c>CTAICmdSetHost::ExecAI</c> (TAICmdSetHost.cpp:27) — an idle <b>aggressive</b> monster
    /// acquires a host/target <b>on sight</b> (auto-aggro), without being hit. Gather the 3×3-view players, seed
    /// the first <c>CanHost</c> one (no recency check — the C++ quirk, line 40), then prefer the nearest that
    /// moved within the last <b>3000 ms</b>; a first player who was never host-eligible is lazily activated
    /// (C++ lines 57-64). On a pick the port <b>folds</b> the C++ wake→<c>ChgHost</c>→<c>ChgMode</c> chain
    /// (SetHost only wakes + assigns host + <c>SelectSkill</c>; the target/aggro/BATTLE transition come from the
    /// scripted <c>AT_AICOMPLETE</c> successors) into the <see cref="ApplyRetarget"/>. Returns whether a
    /// host was acquired (the monster is now in <c>MT_BATTLE</c>).
    ///
    /// <para><b>Deferred (documented):</b> the C++ trigger is event-driven (<c>AT_ENTER</c> on a player moving
    /// into view / entering the cell / the monster spawning); the port drives it from the per-tick
    /// <c>RunMonsterAI</c> sweep instead (consistent with its roam/chase), the 3000 ms recency window preserving
    /// the "recently moved" semantics. <c>CanHost</c>'s ghost branch (a dead player within <c>CELL_SIZE/2</c>)
    /// is replaced by the <c>Hp &gt; 0</c> filter (the port doesn't model ghost). The "aggressive" gate itself is
    /// <see cref="Monster.Aggressive"/> — sourced from <c>TAICHART</c> in C++, not yet loaded (defaults off).</para></summary>
    private bool TryAcquireHost(Monster mon, uint nowMs)
    {
        var players = _state.PlayersAround(mon)
            .Where(p => p.State == EnterState.InGame && p.Char is { Hp: > 0 })
            .ToList();
        if (players.Count == 0) return false;

        ClientSession? host = null;
        uint dist = 0xFFFFFFFF;

        if (players[0].Char!.CanHost) host = players[0];   // seed: first CanHost, no recency (TAICmdSetHost.cpp:40)

        foreach (var p in players)                          // nearest Manhattan among CanHost, moved <3000ms (44-55)
        {
            var pc = p.Char!;
            if (!pc.CanHost) continue;
            uint local = (uint)(MathF.Abs(mon.PosX - pc.PosX) + MathF.Abs(mon.PosZ - pc.PosZ));
            if (local < dist && nowMs - pc.LastMoveMs < 3000) { host = p; dist = local; }
        }

        if (host is null && !players[0].Char!.CanHost)      // lazy activate a never-eligible first player (57-64)
        {
            host = players[0];
            players[0].Char!.CanHost = true;
        }

        if (host?.Char is not { CharId: var hostId }) return false;
        ApplyRetarget(mon, new Monster.AggroTarget(hostId, hostId, OtPc));
        return true;
    }
}
