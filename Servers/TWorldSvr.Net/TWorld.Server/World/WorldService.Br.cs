using Microsoft.Extensions.Logging;
using TWorld.Protocol;

namespace TWorld.Server.World;

/// <summary>
/// Phase 4b — Battle Royale, ported from <c>BRSystem.cpp</c> (CBRSystem) plus the module helpers in
/// <c>TWorldSvr.cpp</c> (BRNotify/UpdateBRTeam/RemoveFromBRTeam/TeleportBRTeam/TeleportBRPlayer/
/// IsSomeoneAliveInTeam) and the <c>SSHandler.cpp</c> handlers. State lives in <see cref="BrState"/>; the
/// behaviour runs on the single batch task. The C++ <c>CreateMatch</c> contained a large dead/commented
/// block — only the live path is ported.
/// </summary>
public sealed partial class WorldService
{
    private readonly Random _brRng = new();

    private async Task<bool> DispatchBrAsync(ServerSession session, PacketReader r)
    {
        if (_state.Br is null) return false;
        switch (r.Id)
        {
            case Msg.MW_ADDTOBRQUEUE_REQ: OnAddToBrQueue(r); return true;
            case Msg.MW_BRTEAMMATEADD_REQ: OnBrTeammateAdd(r); return true;
            case Msg.MW_BRTEAMMATEDEL_REQ: OnBrTeammateDel(r); return true;
            case Msg.MW_BRTEAMMATEADDRESULT_ACK: OnBrTeammateAddResult(r); return true;
            case Msg.MW_VOTEFORBRMAP_REQ: OnVoteForBrMap(r); return true;
            case Msg.MW_CMTELEPORTBATTLEMODE_REQ: await OnCmTeleportBattleMode(r); return true;
            default: return false;
        }
    }

    // ===== handlers =====

    private void OnAddToBrQueue(PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        byte onlyReady = r.ReadByte();
        var ch = _state.FindChar(charId, key);
        var br = _state.Br!;
        if (ch is null) return;
        var map = MapOf(ch);
        if (map is null) return;

        if (onlyReady != 0)
        {
            bool ok = br.PremadeCountByChief(charId) > 0 ? BrFlagTeamReady(charId) : BrFlagPlayerReady(charId, key);
            if (ok) UpdateBrTeam(br.ChiefIdByMate(charId));
            return;
        }

        byte result = BrAddToQueue(charId, key, ch.Class, ch.Name);
        map.Send(BuildAddToBrQueueAck(result, charId, key, br.Tick));
    }

    private void OnBrTeammateAdd(PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        string name = r.ReadString();
        var ch = _state.FindChar(charId, key);
        if (ch is null) return;
        if (!_state.CharactersByName.TryGetValue(name, out var mate) || ReferenceEquals(ch, mate))
        {
            MapOf(ch)?.Send(BuildBrTeammateAdd(charId, key, (byte)TeamAdd.NotFound, ""));
            return;
        }
        MapOf(mate)?.Send(BuildBrTeammateAdd(mate.CharId, mate.Key, (byte)TeamAdd.Success, ch.Name));
    }

    private void OnBrTeammateDel(PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        string name = r.ReadString();
        var ch = _state.FindChar(charId, key);
        var br = _state.Br!;
        if (ch is null || !_state.CharactersByName.TryGetValue(name, out var erase)) return;
        if (br.PremadeCountByChief(charId) > 0 || ReferenceEquals(ch, erase))
            BrErasePlayerFromPremade(erase.CharId);
    }

    private void OnBrTeammateAddResult(PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        byte result = r.ReadByte();
        string name = r.ReadString();
        var br = _state.Br!;

        if (result == (byte)TeamAdd.Success)
        {
            var mate = _state.FindChar(charId, key);
            _state.CharactersByName.TryGetValue(name, out var chief);
            if (mate is null || chief is null) return;
            if (br.FindPlayerInPremade(mate.CharId) || br.PremadeCountByChief(chief.CharId) + 1 > 3)
            {
                MapOf(chief)?.Send(BuildBrTeammateAdd(chief.CharId, chief.Key, (byte)TeamAdd.AlreadyInTeam, ""));
                return;
            }
            BrJoinPremadeTeam(chief, mate);
        }
        else
        {
            if (_state.CharactersByName.TryGetValue(name, out var ch))
                MapOf(ch)?.Send(BuildBrTeammateAdd(ch.CharId, ch.Key, result, ""));
        }
    }

    private void OnVoteForBrMap(PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        string map = r.ReadString();
        byte mode = r.ReadByte();
        var ch = _state.FindChar(charId, key);
        var br = _state.Br!;
        if (ch is null) return;
        if (!string.IsNullOrEmpty(map)) BrVoteForMap(ch.UserId, map);
        else if (mode != 0xFF) { if (!br.ModeVote.ContainsKey(ch.UserId)) br.ModeVote[ch.UserId] = mode; }
    }

    private async Task OnCmTeleportBattleMode(PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        byte type = r.ReadByte();
        var ch = _state.FindChar(charId, key);
        if (ch is null) return;
        // SYSTEM_BOW = 0 -> admin-add to the BoW queue (the C++ SYSTEM_BR branch is empty).
        if (type == 0 && _state.Bow is not null)
            await BowAddToQueue(charId, key, CCra, ch.Guild?.Id ?? 0, admin: true);
    }

    // ===== queue / premade =====

    private byte BrAddToQueue(uint charId, uint key, byte cls, string name)
    {
        var br = _state.Br!;
        if (br.Status != BattleStatus.Alarm) return (byte)BowReg.Fail;
        if (br.Reg.ContainsKey(charId) || br.FindPlayerInPremade(charId)) return (byte)BowReg.AlreadyInQueue;

        br.Reg[charId] = new BrTempPlayer { CharId = charId, Key = key, Class = cls, Name = name };
        BrErasePlayerFromPremade(charId);
        return (byte)BowReg.Success;
    }

    private byte BrErasePlayerFromQueue(uint charId, uint key)
    {
        var br = _state.Br!;
        if (br.Status != BattleStatus.Alarm) return (byte)BowReg.Fail;
        if (br.Reg.TryGetValue(charId, out var t) && t.Key == key) { br.Reg.Remove(charId); return (byte)BowReg.Success; }
        return (byte)BowReg.Fail;
    }

    private void BrJoinPremadeTeam(Character chief, Character mate)
    {
        var br = _state.Br!;
        if (br.Type != (byte)BrType.Team) return;

        if (!br.PremadeTeams.TryGetValue(chief.CharId, out var team))
        {
            if (br.FindPlayerInPremade(chief.CharId) || br.FindPlayerInPremade(mate.CharId)) return;
            team = new BrTeam();
            team.Players[chief.CharId] = new BrPlayer { CharId = chief.CharId, Key = chief.Key, Name = chief.Name, Class = chief.Class, Ready = true };
            team.Players[mate.CharId] = new BrPlayer { CharId = mate.CharId, Key = mate.Key, Name = mate.Name, Class = mate.Class, Ready = false };
            br.PremadeTeams[chief.CharId] = team;
        }
        else
        {
            if (br.FindPlayerInPremade(mate.CharId) || team.Players.Count >= 3) return;
            team.Players[mate.CharId] = new BrPlayer { CharId = mate.CharId, Key = mate.Key, Name = mate.Name, Class = mate.Class, Ready = false };
        }

        BrErasePlayerFromQueue(chief.CharId, chief.Key);
        BrErasePlayerFromQueue(mate.CharId, mate.Key);
        UpdateBrTeam(chief.CharId);
    }

    /// <summary>Remove a player from their premade; promote a new chief / dissolve as the C++ does.</summary>
    private void BrErasePlayerFromPremade(uint charId)
    {
        var br = _state.Br!;
        if (!br.FindPlayerInPremade(charId) || br.Type != (byte)BrType.Team) return;

        // Case 1: the char is a chief (key match).
        if (br.PremadeTeams.TryGetValue(charId, out var owned))
        {
            if (owned.Players.Count < 3)
            {
                foreach (var p in owned.Players.Values) RemoveFromBrTeam(p.CharId);
                br.PremadeTeams.Remove(charId);
                return;
            }
            owned.Players.Remove(charId);
            RemoveFromBrTeam(charId);
            uint newChief = owned.Players.Keys.FirstOrDefault();
            if (newChief == 0) { br.PremadeTeams.Remove(charId); return; }
            br.PremadeTeams.Remove(charId);
            br.PremadeTeams[newChief] = owned;
            UpdateBrTeam(newChief);
            return;
        }

        // Case 2: the char is a member of someone else's premade.
        foreach (var (chief, team) in br.PremadeTeams.ToList())
        {
            if (!team.Players.ContainsKey(charId)) continue;
            if (team.Players.Count < 3)
            {
                foreach (var p in team.Players.Values) RemoveFromBrTeam(p.CharId);
                br.PremadeTeams.Remove(chief);
            }
            else
            {
                team.Players.Remove(charId);
                RemoveFromBrTeam(charId);
                UpdateBrTeam(chief);
            }
            return;
        }
    }

    private bool BrFlagPlayerReady(uint charId, uint key)
    {
        foreach (var team in _state.Br!.PremadeTeams.Values)
            if (team.Players.TryGetValue(charId, out var p)) { p.Ready = true; return true; }
        return false;
    }

    private bool BrFlagTeamReady(uint chiefId)
    {
        var br = _state.Br!;
        if (!br.PremadeTeams.TryGetValue(chiefId, out var team)) return false;
        foreach (var p in team.Players.Values)
            if (p.CharId != chiefId && !p.Ready) return false;
        team.Ready = true;
        return true;
    }

    private void BrVoteForMap(uint userId, string mapName)
    {
        var br = _state.Br!;
        for (byte i = 0; i < br.Maps.Count; i++)
            if (br.Maps[i].Name == mapName)
            {
                if (!br.MapVote.ContainsKey(userId)) br.MapVote[userId] = i;
                return;
            }
    }

    // ===== match build (CreateMatch — live path only) =====

    private async Task<bool> BrCreateMatch()
    {
        var br = _state.Br!;
        int playerCount = br.Reg.Count;
        foreach (var team in br.PremadeTeams.Values) { team.Ready = true; playerCount += team.Players.Count; }
        if (playerCount < br.MinPlayerCount) return false;

        // Mode: team mode is decided by votes; solo mode is random.
        if (br.Type == (byte)BrType.Team)
        {
            int v2 = br.ModeVote.Values.Count(m => m == (byte)BrMode.TwoV2);
            int v3 = br.ModeVote.Values.Count(m => m == (byte)BrMode.ThreeV3);
            br.Mode = (byte)(v2 > v3 ? BrMode.TwoV2 : BrMode.ThreeV3);
        }
        else br.Mode = (byte)_brRng.Next(0, 2);

        // Map: highest-voted, or random for solo; small turnouts fall back to the default map.
        int mapIdx = 0, best = 0;
        var votes = new int[br.Maps.Count];
        foreach (var v in br.MapVote.Values) if (v < votes.Length) votes[v]++;
        for (int i = 0; i < votes.Length; i++) if (votes[i] > best) { best = votes[i]; mapIdx = i; }
        if (mapIdx < br.Maps.Count) br.MapId = br.Maps[mapIdx].MapId;
        if (br.Type == (byte)BrType.Solo && br.Maps.Count > 0) br.MapId = br.Maps[_brRng.Next(br.Maps.Count)].MapId;
        if (playerCount < 30) br.MapId = BrState.DefaultMap;

        int size = br.PartySize;

        // Team mode: rebalance premades toward the chosen party size (split 2v2 overflow / fill shortfalls).
        if (br.Type == (byte)BrType.Team)
        {
            foreach (var team in br.PremadeTeams.Values)
            {
                if (team.Players.Count == size) continue;
                if (br.Mode == (byte)BrMode.TwoV2 && team.Players.Count > size)
                {
                    var last = team.Players.Values.Last();
                    team.Players.Remove(last.CharId);
                    br.Reg[last.CharId] = new BrTempPlayer { CharId = last.CharId, Key = last.Key, Name = last.Name, Class = last.Class };
                }
                else if (team.Players.Count == size - 1 && br.Reg.Count > 0)
                {
                    var fill = br.Reg.Values.First();
                    team.Players[fill.CharId] = new BrPlayer { CharId = fill.CharId, Key = fill.Key, Name = fill.Name, Class = fill.Class };
                    br.Reg.Remove(fill.CharId);
                }
            }
        }

        // Bucket the solo queue by role, form fair teams, then recombine leftovers.
        BuildRoleTeams(br, size);

        // Team mode: append any premades that now have a full party.
        if (br.Type == (byte)BrType.Team)
        {
            foreach (var (chief, team) in br.PremadeTeams.ToList())
            {
                if (team.Players.Count != size) continue;
                var matched = new BrTeam();
                foreach (var p in team.Players.Values)
                    matched.Players[p.CharId] = new BrPlayer { CharId = p.CharId, Key = p.Key, Name = p.Name, Class = p.Class };
                br.Teams[(byte)br.Teams.Count] = matched;
                br.PremadeTeams.Remove(chief);
            }
        }

        br.Reg.Clear();
        await BrTeleportTeamsAsync(br.Teams, teleportIn: true);
        _log.LogInformation("BR match created: mode={Mode} teams={N} map={Map}.", br.Mode, br.Teams.Count, br.MapId);
        return true;
    }

    private void BuildRoleTeams(BrState br, int size)
    {
        List<BrPlayer> Pop(Func<byte, bool> pred)
        {
            var list = new List<BrPlayer>();
            foreach (var t in br.Reg.Values.Where(t => pred(t.Class)))
                list.Add(new BrPlayer { CharId = t.CharId, Key = t.Key, Name = t.Name, Class = t.Class });
            Shuffle(list);
            return list;
        }

        List<List<BrPlayer>> roles;
        if (br.Mode == (byte)BrMode.ThreeV3)
        {
            var eyes = Pop(c => c is (byte)TClass.Warrior or (byte)TClass.Ranger);
            var attacks = Pop(c => c is (byte)TClass.Archer or (byte)TClass.Wizard);
            var supports = Pop(c => c is (byte)TClass.Priest or (byte)TClass.Sorcerer);
            roles = new() { eyes, attacks, supports };
        }
        else
        {
            var attacks = Pop(c => c is (byte)TClass.Archer or (byte)TClass.Wizard or (byte)TClass.Ranger);
            var supports = Pop(c => c is (byte)TClass.Warrior or (byte)TClass.Priest or (byte)TClass.Sorcerer);
            roles = new() { attacks, supports };
        }

        // Fair teams: one of each role until the smallest bucket runs out.
        int fair = roles.Min(b => b.Count);
        for (int i = 0; i < fair; i++)
        {
            var team = new BrTeam();
            foreach (var bucket in roles) { var p = bucket[0]; bucket.RemoveAt(0); team.Players[p.CharId] = p; br.Reg.Remove(p.CharId); }
            if (team.Players.Count == size) br.Teams[(byte)br.Teams.Count] = team;
        }

        // Leftovers (whatever remains in any bucket), shuffled and chunked into full parties.
        var leftover = roles.SelectMany(b => b).ToList();
        Shuffle(leftover);
        foreach (var p in leftover) br.Reg.Remove(p.CharId);
        for (int i = 0; i + size <= leftover.Count; i += size)
        {
            var team = new BrTeam();
            for (int j = 0; j < size; j++) team.Players[leftover[i + j].CharId] = leftover[i + j];
            br.Teams[(byte)br.Teams.Count] = team;
        }
    }

    private void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = _brRng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    // ===== phase machine (SetStatus) =====

    private async Task BrSetStatus(BattleStatus status, uint second)
    {
        var br = _state.Br!;
        if (br.BrServer is null) return;

        if (status == BattleStatus.BodPeace && br.Status != BattleStatus.BodPeace) br.PeaceTick = NowMs();

        if (br.PeaceTick != 0)
        {
            if (!br.SentEnd) { br.BrServer.Send(BuildBrCommand((byte)BrCommand.End)); br.SentEnd = true; }
            status = br.Status = BattleStatus.BodPeace;
            long left = NowMs() - br.PeaceTick;
            second = (uint)Math.Max(0, 12 - left / 1000);
            if (left >= 12000) { await BrOnEnd(); br.SentEnd = false; }
            BrNotify(status, second, br.Type);
            return;
        }

        BrNotify(status, second, br.Type);

        switch (status)
        {
            case BattleStatus.Battle:
                if (br.Status != status)
                {
                    br.Reg.Clear();
                    br.BrServer.Send(BuildBrCommand((byte)BrCommand.Start));
                }
                break;
            case BattleStatus.Peace:
                if (br.Teams.Count != 0 || status == br.Status) return;
                if (!await BrCreateMatch())
                {
                    BrSetNextTime();
                    BrRelease();
                    BrNotify(status, second, br.Type);
                    return;
                }
                break;
        }

        br.Tick = second;
        br.Status = status;
    }

    private async Task BrOnEnd()
    {
        var br = _state.Br!;
        if (br.BrServer is null) return;
        await BrTeleportTeamsAsync(br.Teams, teleportIn: false);
        BrSetNextTime();
        BrRelease();
    }

    private void BrSetNextTime()
    {
        var br = _state.Br!;
        if (br.Times.Count == 0) return;
        uint next = br.Times.FirstOrDefault(t => t > br.Start, br.Times[0]);
        br.Start = next;
        int idx = br.Times.IndexOf(next);
        br.Type = idx >= 0 && idx < br.BattleTypes.Count ? br.BattleTypes[idx] : (byte)BrType.Team;
    }

    private void BrRelease()
    {
        var br = _state.Br!;
        br.Running = false;
        br.Status = BattleStatus.Normal;
        br.Tick = 0;
        br.PeaceTick = 0;
        br.Mode = (byte)BrMode.ThreeV3;
        br.Teams.Clear();
        br.PremadeTeams.Clear();
        br.Reg.Clear();
        br.MapVote.Clear();
        br.ModeVote.Clear();
    }

    private async Task BrReleaseSinglePlayer(uint charId, uint key)
    {
        var br = _state.Br!;
        var p = br.FindPlayerInTeamPtr(charId);
        if (p is not null && p.Key == key) await BrTeleportPlayerAsync(charId, key);
    }

    // ===== OnTimer driver =====

    private async Task BrOnTimerAsync(uint dwCLT)
    {
        var br = _state.Br!;
        uint total = br.TotalDur;
        if (dwCLT > br.Start && dwCLT < br.Start + total + 15)
        {
            uint elapsed = dwCLT - br.Start;
            if (elapsed <= br.AlarmDur)
                await BrSetStatus(BattleStatus.Alarm, br.AlarmDur - elapsed);
            else if (elapsed <= br.AlarmDur + br.BuyTimeDur)
                await BrSetStatus(BattleStatus.Peace, br.AlarmDur + br.BuyTimeDur - elapsed);
            else if (elapsed <= br.AlarmDur + br.BuyTimeDur + br.BattleDur)
                await BrSetStatus(BattleStatus.Battle, br.AlarmDur + br.BuyTimeDur + br.BattleDur - elapsed);
            else
                await BrSetStatus(BattleStatus.BodPeace, br.AlarmDur + br.BuyTimeDur + br.BattleDur + 15 - elapsed);

            br.Running = true;

            // Early end: if only one team still has a live member, finish immediately.
            if (br.Status == BattleStatus.Battle)
            {
                int alive = br.Teams.Values.Count(IsSomeoneAliveInTeam);
                if (alive == 1) { br.BrServer?.Send(BuildBrCommand((byte)BrCommand.End)); await BrOnEnd(); }
            }
        }
    }

    private bool IsSomeoneAliveInTeam(BrTeam team)
        => team.Players.Values.Any(p => _state.FindChar(p.CharId, p.Key) is { Channel: var ch } && ch == Proto.BrServerId);

    /// <summary>Test/diagnostic seam: drive the BR phase machine for a given second-of-day.</summary>
    public Task BrTickAsync(uint secondsOfDay) => _state.Br is null ? Task.CompletedTask : BrOnTimerAsync(secondsOfDay);

    // ===== module helpers =====

    private void BrNotify(BattleStatus status, uint second, byte type)
    {
        var packet = BuildBrTimeUpdate((byte)status, second, type);
        foreach (var s in _state.Servers.Values) s.Send(packet);
    }

    private void UpdateBrTeam(uint chiefId)
    {
        var br = _state.Br!;
        if (!br.PremadeTeams.TryGetValue(chiefId, out var team)) return;
        if (!team.Players.TryGetValue(chiefId, out var chief)) return;
        foreach (var p in team.Players.Values)
            if (_state.Characters.TryGetValue(p.CharId, out var ch))
                MapOf(ch)?.Send(BuildUpdateBrTeam(ch.CharId, ch.Key, chief.Name, team, team.Ready));
    }

    private void RemoveFromBrTeam(uint charId)
    {
        if (_state.Characters.TryGetValue(charId, out var ch))
            MapOf(ch)?.Send(BuildUpdateBrTeam(ch.CharId, ch.Key, "", new BrTeam(), false));
    }

    private async Task BrTeleportTeamsAsync(IReadOnlyDictionary<byte, BrTeam> teams, bool teleportIn)
    {
        var br = _state.Br!;
        if (teleportIn)
        {
            br.BrServer?.Send(BuildAddBrTeams(br.MapId, br.Mode, teams));
            foreach (var team in teams.Values)
                foreach (var p in team.Players.Values)
                {
                    var ch = _state.FindChar(p.CharId, p.Key);
                    if (ch is null) continue;
                    MapOf(ch)?.Send(BuildPrepareForBr(p.CharId, p.Key, br.MapId));
                    await BrPersist(() => _brDb!.AddPlayerAsync(ch.CharId, ch.UserId), "TAddBRPlayer");
                }
        }
        else
        {
            br.BrServer?.Send(BuildEndBrWar(teams));
            await BrPersist(() => _brDb!.ClearPlayersAsync(), "TClearBRPlayers");
        }
    }

    private async Task BrTeleportPlayerAsync(uint charId, uint key)
    {
        var br = _state.Br!;
        var ch = _state.FindChar(charId, key);
        if (ch is null || !br.FindPlayerInTeam(charId)) return;
        br.BrServer?.Send(BuildReleaseSingleBrPlayer(charId, key));
        await BrPersist(() => _brDb!.DeleteSinglePlayerAsync(ch.UserId), "TDeleteSingleBRPlayer");
    }

    private async Task BrPersist(Func<Task> op, string what)
    {
        if (_brDb is null) return;
        try { await op(); } catch (Exception ex) { _log.LogWarning(ex, "{Proc} failed.", what); }
    }

    // ===== senders =====

    private static byte[] BuildAddToBrQueueAck(byte result, uint charId, uint key, uint tick)
    { var w = new PacketWriter(Msg.MW_ADDTOBRQUEUE_ACK); w.WriteByte(result); w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteUInt32(tick); return w.ToArray(); }

    private static byte[] BuildBrTimeUpdate(byte status, uint second, byte type)
    { var w = new PacketWriter(Msg.MW_BRTIMEUPDATE_ACK); w.WriteByte(status); w.WriteUInt32(second); w.WriteByte(type); return w.ToArray(); }

    private static byte[] BuildBrTeammateAdd(uint charId, uint key, byte result, string requester)
    { var w = new PacketWriter(Msg.MW_BRTEAMMATEADD_ACK); w.WriteByte(result); w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteString(requester); return w.ToArray(); }

    private static byte[] BuildUpdateBrTeam(uint charId, uint key, string chiefName, BrTeam team, bool teamReady)
    {
        var w = new PacketWriter(Msg.MW_UPDATEBRTEAM_ACK);
        w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteString(chiefName); w.WriteBool(teamReady);
        w.WriteByte((byte)team.Players.Count);
        // C++ MAPBRPLAYERS is std::map<DWORD> — iterate in ascending charId order for a byte-identical body.
        foreach (var p in team.Players.Values.OrderBy(p => p.CharId)) { w.WriteUInt32(p.CharId); w.WriteString(p.Name); w.WriteBool(p.Ready); }
        return w.ToArray();
    }

    private static byte[] BuildAddBrTeams(ushort mapId, byte mode, IReadOnlyDictionary<byte, BrTeam> teams)
    {
        var w = new PacketWriter(Msg.MW_ADDBRTEAMS_ACK);
        w.WriteUInt16(mapId); w.WriteByte(mode); w.WriteByte((byte)teams.Count);
        // Both containers are std::map in C++ — emit teams by ascending key, players by ascending charId.
        foreach (var (key, team) in teams.OrderBy(kv => kv.Key))
        {
            w.WriteUInt32(key);
            foreach (var p in team.Players.Values.OrderBy(p => p.CharId)) { w.WriteUInt32(p.CharId); w.WriteUInt32(p.Key); w.WriteByte(p.Class); w.WriteString(p.Name); }
        }
        return w.ToArray();
    }

    private static byte[] BuildPrepareForBr(uint charId, uint key, ushort mapId)
    { var w = new PacketWriter(Msg.MW_PREPAREFORBR_REQ); w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteUInt16(mapId); return w.ToArray(); }

    private static byte[] BuildBrCommand(byte command)
    { var w = new PacketWriter(Msg.MW_BRCOMMANDEXEC_REQ); w.WriteByte(command); return w.ToArray(); }

    private static byte[] BuildReleaseSingleBrPlayer(uint charId, uint key)
    { var w = new PacketWriter(Msg.MW_RELEASESINGLEBRPLAYER_REQ); w.WriteUInt32(charId); w.WriteUInt32(key); return w.ToArray(); }

    private static byte[] BuildEndBrWar(IReadOnlyDictionary<byte, BrTeam> teams)
    {
        var w = new PacketWriter(Msg.MW_ENDBRWAR_REQ);
        w.WriteByte((byte)teams.Count);
        foreach (var (_, team) in teams.OrderBy(kv => kv.Key))
        {
            w.WriteByte((byte)team.Players.Count);
            foreach (var p in team.Players.Values.OrderBy(p => p.CharId)) { w.WriteUInt32(p.CharId); w.WriteUInt32(p.Key); }
        }
        return w.ToArray();
    }
}
