using TMap.Data;
using TMap.Protocol;

namespace TMap.Server.Map;

/// <summary>
/// Client-authoritative monster movement — the port of <c>OnCS_MONMOVE_REQ</c> (CSHandler.cpp:919).
///
/// <para>This engine does not walk a hosted monster on the server. When a monster takes a host, the AI
/// script's <c>Follow</c>/<c>Roam</c> only announces <i>intent</i> to that host client
/// (<c>CS_MONACTION_ACK</c>, see <c>CTAICmdFollow::ExecAI</c>); the client then moves the monster and
/// reports every step back through this packet. The server is the validator, not the mover.</para>
///
/// <para>So without this handler a monster with a host is frozen server-side: it is told to chase, the
/// client chases, and the server throws every position away. It never closes on the player, so the client
/// never reaches a state where it can swing — which is why an unported <c>CS_MONMOVE_REQ</c> presents as
/// "movement works but combat does nothing", with no attack packets on the wire at all.</para>
///
/// <para>Three rules from the C++ worth stating: the sender must be the monster's <b>host</b> (anyone else
/// is silently skipped — this is the anti-cheat boundary); the batch is <b>per-monster</b>, so one bad entry
/// skips only itself; and the relay goes to the nearby players <b>excluding the sender</b>, the mirror of
/// <c>CS_ACTION</c> — the host already knows where it put them.</para>
///
/// <para><b>Deferred:</b> the <c>OT_RECALL</c>/<c>OT_SELF</c>/<c>OT_COMPANION</c> owner kinds (summons
/// unported) resolve to no monster and are skipped, as the C++ does for an unknown id.</para>
/// </summary>
public sealed partial class MapService
{
    /// <summary>C++ <c>MAX_ROAMRANGE</c> — a non-battle monster dragged this far from its anchor dies.</summary>
    private const float MaxRoamRange = 3000f;

    // TACTION_TYPE (NetCode.h:962). TaWalk/TaRun/TaFollow already live in MapService.AI.cs / .AiEngine.cs.
    private const byte TaStand = 0, TaDead = 7;

    private void OnCS_MONMOVE_REQ(ClientSession s, PacketReader r)
    {
        // C++ gate: if(!pPlayer->m_pMAP) return;
        if (s.State != EnterState.InGame || s.Char is null) return;

        ushort count = r.ReadUInt16();              // wMonCount
        var moved = new List<Monster>(count);

        for (int i = 0; i < count; i++)
        {
            uint monId = r.ReadUInt32();            // dwMonID
            byte objType = r.ReadByte();            // bObjType
            r.ReadByte();                           // bChannel
            r.ReadUInt16();                         // wMapID
            float posX = r.ReadFloat();
            float posY = r.ReadFloat();
            float posZ = r.ReadFloat();
            ushort pitch = r.ReadUInt16();          // wPitch
            ushort dir = r.ReadUInt16();            // wDIR
            byte mouseDir = r.ReadByte();           // bMouseDIR
            byte keyDir = r.ReadByte();             // bKeyDIR
            byte action = r.ReadByte();             // bAction

            // Only OT_MON resolves here; summons are unported (C++ FindRecallMon/FindSelfObj/FindCompanion).
            if (objType != Monster.OtMon) continue;

            // The authority check: only the monster's own host may move it.
            if (_state.FindMonster(monId) is not { } mon)
            { _log.LogDebug("[mon] MONMOVE from char {Char}: unknown monster {Mon}.", s.CharId, monId); continue; }
            if (mon.HostId != s.CharId)
            { _log.LogDebug("[mon] MONMOVE from char {Char}: mon {Mon} is hosted by {Host}; rejected.", s.CharId, monId, mon.HostId); continue; }

            // A corpse stops steering; a monster already in the dead animation is skipped outright.
            if (mon.Hp == 0) { mouseDir = Monster.TkdirN; keyDir = Monster.TkdirN; }
            if (mon.Action == TaDead) continue;
            if (posX <= 0 || posZ <= 0) continue;            // C++ rejects non-positive coordinates

            byte prevAction = mon.Action;
            mon.MouseDir = mouseDir; mon.KeyDir = keyDir; mon.Action = action;
            mon.Pitch = pitch; mon.Dir = dir; mon.PosY = posY;

            _log.LogDebug("[mon] MONMOVE mon {Mon}: ({FromX:F1},{FromZ:F1}) -> ({ToX:F1},{ToZ:F1}) action {Action} mode {Mode}.",
                mon.Id, mon.PosX, mon.PosZ, posX, posZ, action, mon.Mode);
            ApplyMonsterMove(mon, posX, posZ);

            // Coming to a stand re-arms the MP-regen anchor (C++ m_dwRecoverMPTick = tick + RECOVER_INIT).
            if (prevAction != TaStand && action == TaStand) mon.RecoverMpTick = NowMs + RecoverInit;

            if (mon.Mode == MtBattle)
            {
                // Target gone, or dragged past the leash, drops aggro (C++ LeaveAggro, both arms).
                if (mon.TargetId == 0 || _state.FindByChar(mon.TargetId) is not { State: EnterState.InGame })
                    Disengage(mon, mon.TargetId, mon.TargetType, NowMs);
                else if (Distance(mon.StartX, mon.StartZ, mon.PosX, mon.PosZ) > mon.ChaseRange)
                    Disengage(mon, mon.TargetId, mon.TargetType, NowMs);
            }
            else if (mon.Mode == MtGohome)
            {
                if (Distance(mon.StartX, mon.StartZ, mon.PosX, mon.PosZ) <= ZoneHomeSize)
                    OnAiEvent(mon, AiTrigger.AtHome, 0, mon.HostId, mon.TargetId, mon.TargetType);
            }
            else if (Distance(mon.StartX, mon.StartZ, mon.PosX, mon.PosZ) > MaxRoamRange)
            {
                OnMonsterDeath(mon);                 // C++ OnDie(0, OT_NONE, 0) - pulled too far from home
            }

            if (mon.Status != OsDead) moved.Add(mon);
        }

        if (moved.Count == 0) return;

        // Relay to the neighbours EXCLUDING the sender (C++ if(pChar->m_dwID != pPlayer->m_dwID)).
        var ack = BuildCS_MONMOVE_ACK(moved);
        foreach (var other in _state.Neighbors(s)) other.Send(ack);
    }

    /// <summary>Applies a validated position: re-buckets the grid cell and exchanges the monster's
    /// appearance with players whose view it entered/left (C++ <c>CTMap::OnMove(CTMonster*)</c>).</summary>
    private void ApplyMonsterMove(Monster mon, float x, float z)
    {
        var diff = _state.MoveMonster(mon, x, z);
        if (!diff.CellChanged) return;
        foreach (var p in diff.Left) SendCS_DELMON_ACK(p, mon.Id, exitMap: false);
        foreach (var p in diff.Entered) SendCS_ADDMON_ACK(p, mon, newMember: false);
    }

    private static float Distance(float x1, float z1, float x2, float z2)
    {
        float dx = x1 - x2, dz = z1 - z2;
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>C++ <c>CTPlayer::SendCS_MONMOVE_ACK</c> (CSSender.cpp:1142) — <c>WORD(count)</c> then one
    /// 25-byte block per monster.</summary>
    private static byte[] BuildCS_MONMOVE_ACK(IReadOnlyList<Monster> mons)
    {
        var w = new PacketWriter(Msg.CS_MONMOVE_ACK);
        w.WriteUInt16((ushort)mons.Count);
        foreach (var m in mons)
        {
            w.WriteUInt32(m.Id);
            w.WriteByte(Monster.OtMon);
            w.WriteFloat(m.PosX); w.WriteFloat(m.PosY); w.WriteFloat(m.PosZ);
            w.WriteUInt16(m.Pitch); w.WriteUInt16(m.Dir);
            w.WriteByte(m.MouseDir); w.WriteByte(m.KeyDir); w.WriteByte(m.Action);
        }
        return w.ToArray();
    }
}
