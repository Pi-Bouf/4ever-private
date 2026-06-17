using Microsoft.Extensions.Logging;
using TWorld.Protocol;

namespace TWorld.Server.World;

/// <summary>
/// Phase 4 — Battle of the Warlords, ported from <c>BowSystem.cpp</c> (CTBowSystem) plus the module
/// helpers in <c>TWorldSvr.cpp</c> (BOWNotify/TeleportBOWPlayer/NotifyBOWNonQueuedPlayers) and the
/// handlers in <c>SSHandler.cpp</c>. The C++ class held both state and behaviour; here the state lives in
/// <see cref="BowState"/> and the behaviour runs as batch-task methods, matching the rest of the world.
/// </summary>
public sealed partial class WorldService
{
    private const byte CDef = (byte)Contry.Defugel;
    private const byte CCra = (byte)Contry.Craxion;

    private async Task<bool> DispatchBowAsync(ServerSession session, PacketReader r)
    {
        if (_state.Bow is null) return false;
        switch (r.Id)
        {
            case Msg.MW_ADDTOBOWQUEUE_REQ: await OnAddToBowQueue(r); return true;
            case Msg.MW_CANCELBOWQUEUE_REQ: OnCancelBowQueue(r); return true;
            case Msg.MW_BOWPOINTSUPDATE_REQ: OnBowPointsUpdate(r); return true;
            case Msg.MW_LEAVEBATTLEFIELD_REQ: await OnLeaveBattlefield(r); return true;
            default: return false;
        }
    }

    private static long NowMs() => Environment.TickCount64;

    /// <summary>Test/diagnostic seam: drive the BoW phase machine for a given second-of-day, bypassing the
    /// wall clock (the production path is <see cref="OnTimerAsync"/>). No-op if BoW is not configured.</summary>
    public Task BowTickAsync(uint secondsOfDay)
        => _state.Bow is null ? Task.CompletedTask : BowOnTimerAsync(secondsOfDay);

    // ===== handlers =====

    private async Task OnAddToBowQueue(PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        var ch = _state.FindChar(charId, key);
        var bow = _state.Bow!;
        if (ch is null) return;
        var map = MapOf(ch);
        if (map is null) return;

        byte country = ch.Country > CCra ? ch.AidCountry : ch.Country;
        uint guildId = _state.FindTacticsGuild(charId)?.Id ?? ch.Guild?.Id ?? 0;
        byte result = await BowAddToQueue(charId, key, country, guildId);
        map.Send(BuildAddToBowQueueAck(result, charId, key, bow.Tick));
    }

    private void OnCancelBowQueue(PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        var ch = _state.FindChar(charId, key);
        var bow = _state.Bow!;
        if (ch is null) return;
        byte result = BowDeleteFromQueue(charId, key);
        // The client uses one cancel for both queues; fall back to BR when the BoW queue had nothing.
        if (result == (byte)BowReg.Fail && _state.Br is not null) result = BrErasePlayerFromQueue(charId, key);
        uint tick = Math.Max(bow.Tick, _state.Br?.Tick ?? 0);
        MapOf(ch)?.Send(BuildCancelBowQueueAck(result, charId, key, tick));
    }

    private void OnBowPointsUpdate(PacketReader r)
    {
        byte country = r.ReadByte();
        BowUpdatePoints(country);
    }

    private async Task OnLeaveBattlefield(PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        var ch = _state.FindChar(charId, key);
        if (ch is null) return;
        if (ch.Channel == Proto.BrServerId && _state.Br is not null) await BrReleaseSinglePlayer(charId, key);
        else if (ch.MapId == Proto.BowMapId) await BowReleaseSinglePlayer(charId, key);
    }

    // ===== queue (AddPlayerToQueue / DeletePlayerFromQueue) =====

    private async Task<byte> BowAddToQueue(uint charId, uint key, byte country, uint guildId, bool admin = false)
    {
        var bow = _state.Bow!;
        if (country > CCra) return (byte)BowReg.Country;

        if (bow.Status == BattleStatus.Peace || admin)
        {
            if (bow.Players.Count == 0 || bow.Players.ContainsKey(charId)) return (byte)BowReg.Fail;
            if (country > CCra) return (byte)BowReg.Fail;

            BowDeleteFromQueue(charId, key);

            var player = new BowPlayer { CharId = charId, Key = key };
            var count = new int[2];
            foreach (var p in bow.Players.Values) count[p.Country]++;
            player.Country = count[CCra] < count[CDef] ? CCra : CDef;
            bow.Players[charId] = player;

            await BowTeleportInAsync(new Dictionary<uint, BowPlayer> { [charId] = player });
            return (byte)BowReg.JoinedLate;   // BOWREG_FAIL + 1
        }

        if (bow.Status != BattleStatus.Alarm) return (byte)BowReg.Fail;

        if (guildId != 0)
        {
            if (bow.Reg.ContainsKey(charId)) return (byte)BowReg.Fail;
            if (!bow.GuildMembers.TryGetValue(guildId, out var members))
            {
                bow.GuildMembers[guildId] = new List<BowTempPlayer> { new() { CharId = charId, Key = key, Country = country } };
            }
            else
            {
                if (members.Any(m => m.CharId == charId)) return (byte)BowReg.Fail;
                members.Add(new BowTempPlayer { CharId = charId, Key = key, Country = country });
            }
            return (byte)BowReg.Success;
        }

        if (!bow.Reg.ContainsKey(charId))
        {
            bow.Reg[charId] = new BowTempPlayer { CharId = charId, Key = key, Country = country };
            return (byte)BowReg.Success;
        }
        return (byte)BowReg.AlreadyInQueue;
    }

    private byte BowDeleteFromQueue(uint charId, uint key)
    {
        var bow = _state.Bow!;
        if (bow.Status != BattleStatus.Alarm) return (byte)BowReg.Fail;

        foreach (var members in bow.GuildMembers.Values)
        {
            int i = members.FindIndex(m => m.CharId == charId && m.Key == key);
            if (i >= 0) { members.RemoveAt(i); return (byte)BowReg.Success; }
        }
        if (bow.Reg.TryGetValue(charId, out var t) && t.Key == key) { bow.Reg.Remove(charId); return (byte)BowReg.Success; }
        return (byte)BowReg.Fail;
    }

    // ===== match build (CreateMatch) =====

    private async Task<byte> BowCreateMatch()
    {
        var bow = _state.Bow!;
        bow.Winner = (byte)Contry.None;

        var first = new Dictionary<uint, BowTempPlayer>();   // Defugel side
        var second = new Dictionary<uint, BowTempPlayer>();  // Craxion side

        // Guild members go to the side of their guild's first registrant's country.
        foreach (var members in bow.GuildMembers.Values)
        {
            if (members.Count == 0) continue;
            byte guildCountry = members[0].Country;
            foreach (var m in members)
            {
                var p = new BowTempPlayer { CharId = m.CharId, Key = m.Key, Country = m.Country };
                (guildCountry == CDef ? first : second)[m.CharId] = p;
            }
        }

        // If the guild split is badly skewed, move the guild that best balances it to the smaller side.
        if (Math.Abs(first.Count - second.Count) > bow.Reg.Count + bow.MaxNationDifference)
        {
            var smaller = first.Count < second.Count ? first : second;
            var bigger = ReferenceEquals(smaller, first) ? second : first;
            if (smaller.Count > 0)
            {
                byte smallerCountry = smaller.Values.First().Country;
                int best = int.MaxValue;
                List<BowTempPlayer>? moveGuild = null;
                foreach (var members in bow.GuildMembers.Values)
                {
                    if (members.Count == 0 || members[0].Country == smallerCountry) continue;
                    int diff = Math.Abs((smaller.Count + members.Count) - (bigger.Count - members.Count));
                    if (diff < best) { best = diff; moveGuild = members; }
                }
                if (moveGuild is not null)
                    foreach (var m in moveGuild)
                        if (bigger.Remove(m.CharId, out var moved)) smaller[m.CharId] = moved;
            }
        }

        // Solo registrants fill the currently-smaller side.
        foreach (var t in bow.Reg.Values)
        {
            var p = new BowTempPlayer { CharId = t.CharId, Key = t.Key, Country = t.Country };
            (first.Count < second.Count ? first : second)[t.CharId] = p;
        }

        // Final balance pass: move up to diff/2 from the bigger side.
        int bDiff = Math.Abs(first.Count - second.Count);
        if (bDiff > bow.MaxNationDifference)
        {
            var from = first.Count > second.Count ? first : second;
            var to = ReferenceEquals(from, first) ? second : first;
            foreach (var id in from.Keys.Take(bDiff / 2).ToList())
            {
                if (from.Remove(id, out var moved)) to[id] = moved;
            }
        }

        foreach (var t in first.Values) bow.Players[t.CharId] = new BowPlayer { CharId = t.CharId, Key = t.Key, Country = CDef };
        foreach (var t in second.Values) bow.Players[t.CharId] = new BowPlayer { CharId = t.CharId, Key = t.Key, Country = CCra };

        bow.ResetPoints();
        await BowTeleportInAsync(bow.Players);
        _log.LogInformation("BoW match created: {N} participants ({D} Defugel / {C} Craxion).", bow.Players.Count, first.Count, second.Count);
        return (byte)BowMatch.Success;
    }

    // ===== phase machine (SetStatus) =====

    private async Task BowSetStatus(BattleStatus status, uint second)
    {
        var bow = _state.Bow!;
        if (bow.BowServer is null || !bow.Running) return;

        if (status == BattleStatus.BodPeace && bow.Status != BattleStatus.BodPeace)
            bow.PeaceTick = NowMs();

        if (bow.PeaceTick != 0)
        {
            if (!bow.SentEnd)
            {
                bow.Winner = (byte)BowWinner.Tie;
                if (bow.Points[CDef] > bow.Points[CCra]) bow.Winner = (byte)BowWinner.Defugel;
                else if (bow.Points[CCra] > bow.Points[CDef]) bow.Winner = (byte)BowWinner.Craxion;
                bow.BowServer.Send(BuildBowCommand((byte)BowCommand.End));
                bow.SentEnd = true;
            }

            status = bow.Status = BattleStatus.BodPeace;
            long left = NowMs() - bow.PeaceTick;
            second = (uint)Math.Max(0, 12 - left / 1000);
            if (left >= 12000) { await BowEndBattle(); bow.SentEnd = false; }

            BowNotify(status, second, bow.Points[CDef], bow.Points[CCra]);
            return;
        }

        BowNotify(status, second, bow.Points[CDef], bow.Points[CCra]);

        switch (status)
        {
            case BattleStatus.Battle:
                if (bow.Status != status)
                {
                    bow.BowServer.Send(BuildBowCommand((byte)BowCommand.Start));
                    NotifyBowNonQueuedPlayers();
                }
                break;
            case BattleStatus.Peace:
                if (bow.Players.Count != 0 || status == bow.Status) return;
                byte result = await BowCreateMatch();
                foreach (var _ in bow.Reg) { } // (C++ frees temp regs here)
                bow.Reg.Clear();
                bow.GuildMembers.Clear();
                if (result != (byte)BowMatch.Success)
                {
                    BowSetNextTime();
                    BowRelease();
                    BowNotify(BattleStatus.Normal, 0, bow.Points[CDef], bow.Points[CCra]);
                    return;
                }
                break;
        }

        bow.Tick = second;
        bow.Status = status;
    }

    private void BowUpdatePoints(byte country)
    {
        var bow = _state.Bow!;
        if (country == CDef)
        {
            bow.Points[CDef]++;
            if (bow.Points[CCra] == 1) { bow.Points[CCra] = 0; bow.PeaceTick = NowMs(); }
            else if ((int)bow.Points[CCra] - 1 > 0) bow.Points[CCra]--;
        }
        else if (country == CCra)
        {
            bow.Points[CCra]++;
            if (bow.Points[CDef] == 1) { bow.Points[CDef] = 0; bow.PeaceTick = NowMs(); }
            else if ((int)bow.Points[CDef] - 1 > 0) bow.Points[CDef]--;
        }
        BowNotify(bow.Status, bow.Tick, bow.Points[CDef], bow.Points[CCra]);
    }

    private async Task BowEndBattle()
    {
        var bow = _state.Bow!;
        if (bow.BowServer is null) return;
        await BowTeleportOutAsync(bow.Players, bow.Winner);
        BowSetNextTime();
        BowRelease();
    }

    private void BowSetNextTime()
    {
        var bow = _state.Bow!;
        if (bow.Times.Count == 0) return;
        uint next = bow.Times.FirstOrDefault(t => t > bow.Start, bow.Times[0]);
        bow.Start = next;
        _log.LogInformation("BoW next match time set to {T}.", bow.Start);
    }

    private async Task BowReleaseSinglePlayer(uint charId, uint key)
    {
        var bow = _state.Bow!;
        var p = bow.FindPlayer(charId);
        if (p is not null && p.Key == key && bow.Winner != (byte)Contry.None)
            await BowTeleportSingleAsync(charId, key, bow.Winner);
    }

    private void BowRelease()
    {
        var bow = _state.Bow!;
        bow.ResetPoints();
        bow.Running = false;
        bow.Status = BattleStatus.Normal;
        bow.Tick = 0;
        bow.PeaceTick = 0;
        bow.Reg.Clear();
        bow.Players.Clear();
        bow.GuildMembers.Clear();
    }

    // ===== OnTimer driver (the BoW slice of OnTimer) =====

    private async Task BowOnTimerAsync(uint dwCLT)
    {
        var bow = _state.Bow!;
        if (bow.BowServer is null) return;

        uint total = bow.TotalDur;
        if (dwCLT > bow.Start && dwCLT < bow.Start + total + 15)
        {
            uint elapsed = dwCLT - bow.Start;
            if (elapsed <= bow.AlarmDur)
                await BowSetStatus(BattleStatus.Alarm, bow.AlarmDur - elapsed);
            else if (elapsed <= bow.AlarmDur + bow.BuyTimeDur)
                await BowSetStatus(BattleStatus.Peace, bow.AlarmDur + bow.BuyTimeDur - elapsed);
            else if (elapsed <= bow.AlarmDur + bow.BuyTimeDur + bow.BattleDur)
                await BowSetStatus(BattleStatus.Battle, bow.AlarmDur + bow.BuyTimeDur + bow.BattleDur - elapsed);
            else
                await BowSetStatus(BattleStatus.BodPeace, bow.AlarmDur + bow.BuyTimeDur + bow.BattleDur + 15 - elapsed);

            bow.Running = true;
        }
    }

    // ===== module helpers (BOWNotify / TeleportBOWPlayer / NotifyBOWNonQueuedPlayers) =====

    private void BowNotify(BattleStatus status, uint second, byte dPoints, byte cPoints)
    {
        var packet = BuildBowTimeUpdate((byte)status, second, dPoints, cPoints);
        foreach (var s in _state.Servers.Values) s.Send(packet);
    }

    private async Task BowTeleportInAsync(IReadOnlyDictionary<uint, BowPlayer> roster)
    {
        var bow = _state.Bow!;
        bow.BowServer?.Send(BuildAddBowPlayers(roster));
        foreach (var p in roster.Values)
        {
            var ch = _state.FindChar(p.CharId, p.Key);
            if (ch is null) continue;
            if (ch.Country != p.Country) ch.Country = p.Country;
            MapOf(ch)?.Send(BuildPrepareForBow(p.CharId, p.Key, p.Country));
            await BowPersist(() => _bowDb!.AddPlayerAsync(ch.CharId, ch.UserId), "TAddBOWPlayer");
        }
    }

    private async Task BowTeleportOutAsync(IReadOnlyDictionary<uint, BowPlayer> roster, byte winner)
    {
        var bow = _state.Bow!;
        bow.BowServer?.Send(BuildEndBowWar(roster, winner));
        await BowPersist(() => _bowDb!.ClearPlayersAsync(), "TClearBOWPlayers");
    }

    private async Task BowTeleportSingleAsync(uint charId, uint key, byte winner)
    {
        var bow = _state.Bow!;
        var ch = _state.FindChar(charId, key);
        if (ch is null || !bow.Players.ContainsKey(charId)) return;
        bow.BowServer?.Send(BuildReleaseSingleBowPlayer(charId, key, winner));
        bow.Players.Remove(charId);
        await BowPersist(() => _bowDb!.DeleteSinglePlayerAsync(ch.UserId), "TDeleteSingleBOWPlayer");
    }

    private void NotifyBowNonQueuedPlayers()
    {
        var bow = _state.Bow!;
        foreach (var ch in _state.Characters.Values)
            if (!bow.Players.ContainsKey(ch.CharId))
                MapOf(ch)?.Send(BuildNotifyNonQueued(ch.CharId, ch.Key));
    }

    private async Task BowPersist(Func<Task> op, string what)
    {
        if (_bowDb is null) return;
        try { await op(); } catch (Exception ex) { _log.LogWarning(ex, "{Proc} failed.", what); }
    }

    // ===== senders =====

    private static byte[] BuildAddToBowQueueAck(byte result, uint charId, uint key, uint tick)
    { var w = new PacketWriter(Msg.MW_ADDTOBOWQUEUE_ACK); w.WriteByte(result); w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteUInt32(tick); return w.ToArray(); }

    private static byte[] BuildCancelBowQueueAck(byte result, uint charId, uint key, uint tick)
    { var w = new PacketWriter(Msg.MW_CANCELBOWQUEUE_ACK); w.WriteByte(result); w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteUInt32(tick); return w.ToArray(); }

    private static byte[] BuildBowTimeUpdate(byte status, uint second, byte dPoints, byte cPoints)
    { var w = new PacketWriter(Msg.MW_BOWTIMEUPDATE_ACK); w.WriteByte(status); w.WriteUInt32(second); w.WriteByte(dPoints); w.WriteByte(cPoints); return w.ToArray(); }

    private static byte[] BuildPrepareForBow(uint charId, uint key, byte team)
    { var w = new PacketWriter(Msg.MW_PREPAREFORBOW_REQ); w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteByte(team); return w.ToArray(); }

    private static byte[] BuildBowCommand(byte command)
    { var w = new PacketWriter(Msg.MW_BOWCOMMANDEXEC_REQ); w.WriteByte(command); return w.ToArray(); }

    private static byte[] BuildEndBowWar(IReadOnlyDictionary<uint, BowPlayer> roster, byte winner)
    {
        var w = new PacketWriter(Msg.MW_ENDBOWWAR_REQ);
        w.WriteByte(winner);
        w.WriteByte((byte)roster.Count);
        // C++ m_mapBOWPLAYERS is std::map<DWORD> — ascending charId order for a byte-identical body.
        foreach (var p in roster.Values.OrderBy(p => p.CharId)) { w.WriteUInt32(p.CharId); w.WriteUInt32(p.Key); }
        return w.ToArray();
    }

    private static byte[] BuildAddBowPlayers(IReadOnlyDictionary<uint, BowPlayer> roster)
    {
        var w = new PacketWriter(Msg.MW_ADDBOWPLAYERS_ACK);
        w.WriteByte((byte)roster.Count);
        foreach (var p in roster.Values.OrderBy(p => p.CharId)) { w.WriteUInt32(p.CharId); w.WriteUInt32(p.Key); w.WriteByte(p.Country); }
        return w.ToArray();
    }

    private static byte[] BuildReleaseSingleBowPlayer(uint charId, uint key, byte winner)
    { var w = new PacketWriter(Msg.MW_RELEASESINGLEBOWPLAYER_REQ); w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteByte(winner); return w.ToArray(); }

    private static byte[] BuildNotifyNonQueued(uint charId, uint key)
    { var w = new PacketWriter(Msg.MW_NOTIFYNONQUEUEDPLAYER_ACK); w.WriteUInt32(charId); w.WriteUInt32(key); return w.ToArray(); }
}
