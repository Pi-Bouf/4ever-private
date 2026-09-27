using TMap.Data;
using TMap.Protocol;

namespace TMap.Server.Map;

/// <summary>
/// Duels — C++ <c>OnCS_DUELINVITE_REQ</c> / <c>OnCS_DUELINVITEREPLY_REQ</c> / <c>OnCS_DUELEND_REQ</c> (CSHandler.cpp:12216),
/// the <c>SM_DUEL*</c> round trip through the map's own AI thread (SSHandler.cpp:937, the timer at TMapSvr.cpp:6272) and
/// <c>CTPlayer::CanDuel</c> / <c>DuelLose</c> (TPlayer.cpp:4277). Everything stays on this map server.
/// <list type="number">
/// <item>A player on map 0 invites another; the target is asked (<c>CS_DUELINVITE_ACK</c>).</item>
/// <item>A yes, with both within 60 of their midpoint, makes a duel: both get <c>CS_DUELSTANDBY_ACK</c> (the midpoint is
/// the arena), an inviter's pet goes home, and 10 s later both get <c>CS_DUELSTART_ACK(DUEL_SUCCESS)</c>.</item>
/// <item>It ends after 5 minutes with no loser, when one gives up (<c>CS_DUELEND_REQ</c>), teleports or logs out — that
/// one loses, except on logout — or when one falls to 0 HP to the other: the loser does not die, both are healed to
/// full. Both get <c>CS_DUELEND_ACK(loser)</c>, lose their death-status and negative buffs, and the winner's view hears
/// <c>SM_DUAL_WIN</c>. The next second the duel is cleared.</item>
/// </list>
/// <para>While a player is in a duel, someone of its own side may only touch it as its opponent, and not once the duel
/// has ended; an enemy's attack ends the duel instead (<see cref="CanDuel"/>). <b>Faithful quirks:</b> the player's duel
/// state is never set to standby or started (only to ended), so the two may already fight during the 10 s countdown;
/// <c>DuelLose</c> against someone other than the opponent sends an end for a duel id that is really a character id, a
/// no-op. <b>Not ported:</b> the duel record and per-class score (<c>RecordDuel</c> — the C++ never saves it) and the
/// protected list (<c>PROTECTED_DUEL</c> — no block list is kept, so nobody refuses).</para>
/// </summary>
public sealed partial class MapService
{
    private const byte DuelSuccess = 0, DuelBusy = 2, DuelFail = 3;           // DUEL_RESULT
    private const byte DuelStandby = 1, DuelStart = 2, DuelEnd = 3;           // DUEL_TYPE
    private const uint DuelStandbyTime = 10, DuelTime = 5 * 60;               // seconds
    private const float DuelAreaRange = 60f;
    private const byte TcontryPeace = 4, TrecallPet = 7, SmDualWin = 8, SdtStatusDie = 49;

    private sealed class Duel
    {
        public uint Id, Inviter, Target, Tick;
        public byte Type;
        public string InviterName = "", TargetName = "";
    }

    private readonly Dictionary<uint, Duel> _duels = new();
    private uint _lastDuelId;

    private void OnCS_DUELINVITE_REQ(ClientSession s, PacketReader r)
    {
        if (s.State != EnterState.InGame || !s.IsMain || s.Char is not { } ch || IsTutorial(ch)) return;
        uint targetId = r.ReadUInt32();

        if (ch.MapId != 0 || ch.DuelId != 0 || IsActionBlock(ch) || HaveDieBuff(ch)
            || _state.FindByChar(targetId) is not { Char: { } target } ts)
        {
            SendCS_DUELSTART_ACK(s, DuelFail, ch.CharId, targetId);
            return;
        }
        if (ts.IsMain && target.DuelId == 0 && !IsActionBlock(target) && !HaveDieBuff(target))
        {
            var w = new PacketWriter(Msg.CS_DUELINVITE_ACK, capacity: 4);
            w.WriteUInt32(ch.CharId);
            ts.Send(w);
            return;
        }
        SendCS_DUELSTART_ACK(s, DuelBusy, ch.CharId, targetId);
    }

    private void OnCS_DUELINVITEREPLY_REQ(ClientSession s, PacketReader r)
    {
        if (s.State != EnterState.InGame || !s.IsMain || s.Char is not { } ch || IsTutorial(ch)) return;
        byte result = r.ReadByte();
        uint inviterId = r.ReadUInt32();

        if (_state.FindByChar(inviterId) is not { Char: { } inviter } si)
        {
            SendCS_DUELSTART_ACK(s, DuelFail, inviterId, ch.CharId);
            return;
        }
        if (!si.IsMain || IsActionBlock(inviter))
        {
            SendCS_DUELSTART_ACK(s, DuelFail, inviterId, ch.CharId);
            SendCS_DUELSTART_ACK(si, DuelFail, inviterId, ch.CharId);
            return;
        }
        if (IsActionBlock(ch))
        {
            SendCS_DUELSTART_ACK(s, DuelBusy, inviterId, ch.CharId);
            SendCS_DUELSTART_ACK(si, DuelBusy, inviterId, ch.CharId);
            return;
        }
        if (inviter.DuelId != 0 || ch.DuelId != 0) result = DuelFail;

        if (result != AskYes)
        {
            SendCS_DUELSTART_ACK(si, result, inviterId, ch.CharId);
            SendCS_DUELSTART_ACK(s, result, inviterId, ch.CharId);
            return;
        }

        float x = (inviter.PosX + ch.PosX) / 2f, z = (inviter.PosZ + ch.PosZ) / 2f;
        if (Distance(x, z, inviter.PosX, inviter.PosZ) >= DuelAreaRange || Distance(x, z, ch.PosX, ch.PosZ) >= DuelAreaRange)
        {
            SendCS_DUELSTART_ACK(s, DuelFail, inviterId, ch.CharId);
            SendCS_DUELSTART_ACK(si, DuelFail, inviterId, ch.CharId);
            return;
        }

        uint id = ++_lastDuelId;
        inviter.DuelId = ch.DuelId = id;
        inviter.DuelTarget = ch.CharId;
        ch.DuelTarget = inviter.CharId;
        foreach (var (who, ws) in new[] { (inviter, si), (ch, s) })                  // PET Clear
            foreach (var pet in who.Recalls.Values.Where(m => m.RecallType == TrecallPet).Take(1))
                SendMW_RECALLMONDEL_ACK(who.CharId, ws.Key, pet.Id);

        _duels[id] = new Duel
        {
            Id = id, Type = DuelStandby, Tick = _tickSeconds + DuelStandbyTime,
            Inviter = inviterId, InviterName = inviter.Name, Target = ch.CharId, TargetName = ch.Name,
        };
        SendCS_DUELSTANDBY_ACK(si, inviterId, ch.CharId, x, z);
        SendCS_DUELSTANDBY_ACK(s, inviterId, ch.CharId, x, z);
    }

    private void OnCS_DUELEND_REQ(ClientSession s, PacketReader r)
    {
        if (s.State != EnterState.InGame || !s.IsMain || s.Char is not { } ch || IsTutorial(ch)) return;
        if (ch.DuelId != 0) EndDuel(ch.DuelId, ch.CharId);                        // giving up: the one asking loses
    }

    /// <summary>The per-second duel sweep (C++ timer, TMapSvr.cpp:6272): a standby duel starts, a started one runs out
    /// with no loser, an ended one is cleared.</summary>
    private void RunDuels()
    {
        foreach (var d in _duels.Values.Where(d => d.Tick <= _tickSeconds).ToList())
        {
            if (d.Type == DuelStandby)
            {
                d.Type = DuelStart;
                d.Tick = _tickSeconds + DuelTime;
                if (_state.FindByChar(d.Inviter) is { } si && _state.FindByChar(d.Target) is { } st)
                {
                    SendCS_DUELSTART_ACK(si, DuelSuccess, d.Inviter, d.Target);
                    SendCS_DUELSTART_ACK(st, DuelSuccess, d.Inviter, d.Target);
                }
                else EndDuel(d.Id, 0);
            }
            else if (d.Type == DuelStart) EndDuel(d.Id, 0);
            else
            {
                _duels.Remove(d.Id);
                foreach (var id in new[] { d.Inviter, d.Target })
                    if (_state.FindByChar(id)?.Char is { } c) ClearDuel(c);
            }
        }
    }

    /// <summary>C++ <c>OnSM_DUELEND_REQ</c> then <c>OnSM_DUELEND_ACK</c>. A loser who is not in the duel counts as none.</summary>
    private void EndDuel(uint duelId, uint loser)
    {
        if (!_duels.TryGetValue(duelId, out var d) || d.Type == DuelEnd) return;
        string loserName = d.Inviter == loser ? d.InviterName : d.Target == loser ? d.TargetName : "";
        d.Type = DuelEnd;
        d.Tick = _tickSeconds;

        ClientSession? winner = null;
        string winnerName = "";
        foreach (var id in new[] { d.Inviter, d.Target })
        {
            if (_state.FindByChar(id) is not { Char: { } c } cs) continue;
            if (loser != 0 && c.DuelTarget == loser) { winner = cs; winnerName = c.Name; }
            c.DuelType = DuelEnd;
            EraseDieSkillBuff(cs, c);
            StripMaintains(cs, c, positive: false);                                    // DeleteNegativeMaintainSkill
            SendCS_DUELEND_ACK(cs, loser);
        }
        if (winner is null) return;
        var w = new PacketWriter(Msg.CS_SYSTEMMSG_ACK);
        w.WriteByte(SmDualWin); w.WriteString(winnerName); w.WriteString(loserName);
        var msg = w.ToArray();
        foreach (var p in _state.InView(winner)) p.Send(msg);
    }

    /// <summary>C++ <c>CTPlayer::CanDuel(pAttacker)</c> — whether <paramref name="attacker"/> may touch
    /// <paramref name="target"/>, which may be in a duel.</summary>
    private bool CanDuel(Character target, Character attacker)
    {
        if (target.DuelId == 0 || target.CharId == attacker.CharId) return true;
        if (WarCountryOf(target) == WarCountryOf(attacker) || target.Country == TcontryPeace || attacker.Country == TcontryPeace)
            return target.DuelTarget == attacker.CharId && target.DuelType is not (DuelEnd or DuelStandby);
        EndDuel(target.DuelId, 0);                                                       // an enemy breaks the duel up
        return true;
    }

    /// <summary>C++ <c>CTPlayer::DuelLose(dwAttackerID)</c> — the duel loser at 0 HP. True when it was its opponent's
    /// blow: no death, both healed to full, the duel ends with it as the loser.</summary>
    private bool DuelLose(ClientSession s, Character ch, uint attackerId)
    {
        if (ch.DuelTarget != attackerId) { EndDuel(ch.CharId, 0); return false; }       // (sic) a character id as the duel id

        EraseDieSkillBuff(s, ch);
        StripMaintains(s, ch, positive: false);
        foreach (var m in ch.Recalls.Values.ToList()) SendMW_RECALLMONDEL_ACK(ch.CharId, s.Key, m.Id);   // DeleteAllRecallMon
        foreach (var m in ch.SelfObjs.Values.ToList()) DeleteSelfObj(ch, m.Id);                         // DeleteAllSelfMon
        ch.DuelType = DuelEnd;
        ch.Hp = MaxHpFor(ch);
        ch.Mp = MaxMpFor(ch);
        SendCS_DUELEND_ACK(s, ch.CharId);

        var view = _state.InView(s).ToList();
        foreach (var p in view)
        {
            if (p.Char is not { } c || (c.CharId != attackerId && c.CharId != ch.CharId)) continue;
            c.Hp = MaxHpFor(c);
            c.Mp = MaxMpFor(c);
            SendSelfHpMp(p, c.CharId, c.Hp, c.Hp, c.Mp, c.Mp);
            foreach (var v in view) SendSelfHpMp(v, c.CharId, c.Hp, c.Hp, c.Mp, c.Mp);
        }
        EndDuel(ch.DuelId, ch.CharId);
        return true;
    }

    /// <summary>Leaving (teleport: it loses; logout: no loser) ends a duel, C++ <c>Teleport</c> / <c>ClearDuel</c> paths.</summary>
    private void LeaveDuel(Character ch, bool loses)
    {
        if (ch.DuelId != 0) EndDuel(ch.DuelId, loses ? ch.CharId : 0);
    }

    private static void ClearDuel(Character ch)
    {
        ch.DuelId = 0;
        ch.DuelType = 0;
        ch.DuelTarget = 0;
    }

    /// <summary>C++ <c>HaveDieBuff</c> — a buff carrying the <c>SDT_STATUS_DIE</c> status.</summary>
    private static bool HaveDieBuff(Character ch) => ch.MaintainSkills.Any(m => IsDieBuff(m));

    private static bool IsDieBuff(MaintainSkill m)
        => m.Template?.Data.Any(d => d.Type == SkillTemplate.SdtStatus && d.Exec == SdtStatusDie) == true;

    /// <summary>C++ <c>EraseDieSkillBuff</c> — every death-status buff ends.</summary>
    private void EraseDieSkillBuff(ClientSession s, Character ch)
    {
        for (int i = 0; i < ch.MaintainSkills.Count;)
            if (IsDieBuff(ch.MaintainSkills[i])) EraseMaintainPlayer(s, ch, i);
            else i++;
    }

    private static void SendCS_DUELSTART_ACK(ClientSession s, byte result, uint inviter, uint target)
    {
        var w = new PacketWriter(Msg.CS_DUELSTART_ACK, capacity: 9);
        w.WriteByte(result); w.WriteUInt32(inviter); w.WriteUInt32(target);
        s.Send(w);
    }

    private static void SendCS_DUELSTANDBY_ACK(ClientSession s, uint inviter, uint target, float x, float z)
    {
        var w = new PacketWriter(Msg.CS_DUELSTANDBY_ACK, capacity: 16);
        w.WriteUInt32(inviter); w.WriteUInt32(target); w.WriteFloat(x); w.WriteFloat(z);
        s.Send(w);
    }

    private static void SendCS_DUELEND_ACK(ClientSession s, uint loser)
    {
        var w = new PacketWriter(Msg.CS_DUELEND_ACK, capacity: 4);
        w.WriteUInt32(loser);
        s.Send(w);
    }
}
