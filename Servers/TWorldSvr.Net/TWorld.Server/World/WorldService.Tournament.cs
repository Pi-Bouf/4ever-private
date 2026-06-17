using Microsoft.Extensions.Logging;
using TWorld.Protocol;

namespace TWorld.Server.World;

/// <summary>
/// Phase 4d — the tournament, ported from <c>TournamentInfo</c>/<c>Tournament*</c> in <c>TWorldSvr.cpp</c>
/// and the <c>OnMW_TOURNAMENT_ACK</c> dispatcher in <c>SSHandler.cpp</c>. This slice covers the config
/// announce <b>and the player-facing registration gameplay</b>: apply (with the 1st-grade-group seeding
/// gate + dup HWID/IP check), apply-info, join-list, party add/del/list, and match-list. The date-math
/// scheduler (<c>SetTournamentTime</c>), the rank-seeded bracket build (<c>TournamentSelectPlayer</c>),
/// the result handler, events, and betting are a documented later slice.
/// </summary>
public sealed partial class WorldService
{
    private bool DispatchTournament(ServerSession session, PacketReader r)
    {
        if (_state.Tournament is null) return false;
        switch (r.Id)
        {
            case Msg.MW_TOURNAMENT_ACK: OnTournament(session, r); return true;
            case Msg.MW_TOURNAMENTENTERGATE_ACK: OnTournamentEnterGate(r); return true;
            case Msg.MW_TOURNAMENTRESULT_ACK: OnTournamentResult(r); return true;
            default: return false;
        }
    }

    private void OnTournament(ServerSession session, PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        ushort protocol = r.ReadUInt16();
        var ch = _state.FindChar(charId, key);
        if (ch is null) return;

        switch (protocol)
        {
            case Msg.MW_TOURNAMENTSCHEDULE_ACK:
                session.Send(BuildTournamentInfo());
                break;
            case Msg.MW_TOURNAMENTAPPLYINFO_ACK:
                TournamentApplyInfo(session, ch);
                break;
            case Msg.MW_TOURNAMENTAPPLY_ACK:
            {
                byte entryId = r.ReadByte();
                string hwid = r.ReadString();
                uint ip = r.ReadUInt32();
                TournamentApply(session, ch, entryId, hwid, ip);
                break;
            }
            case Msg.MW_TOURNAMENTJOINLIST_ACK:
                TournamentJoinList(session, ch);
                break;
            case Msg.MW_TOURNAMENTPARTYLIST_ACK:
                TournamentPartyList(session, ch, r.ReadUInt32());
                break;
            case Msg.MW_TOURNAMENTPARTYADD_ACK:
            {
                string target = r.ReadString();
                if (_state.CharactersByName.TryGetValue(target, out var t))
                    TournamentPartyAdd(session, ch, t.CharId, t.Country, t.Name, t.Level, t.Class);
                else
                    session.Send(BuildTournamentResult(charId, key, Msg.MW_TOURNAMENTPARTYADD_REQ, (byte)TournamentResult.NotFound));
                break;
            }
            case Msg.MW_TOURNAMENTPARTYDEL_ACK:
                TournamentPartyDel(session, ch, r.ReadUInt32());
                break;
            case Msg.MW_TOURNAMENTMATCHLIST_ACK:
                TournamentMatchList(session, ch);
                break;
            case Msg.MW_TOURNAMENTEVENTLIST_ACK:
                TournamentEventList(session, ch);
                break;
            case Msg.MW_TOURNAMENTEVENTINFO_ACK:
                TournamentEventInfo(session, ch, r.ReadByte());
                break;
            case Msg.MW_TOURNAMENTEVENTJOIN_ACK:
            {
                byte entryId = r.ReadByte();
                uint targetId = r.ReadUInt32();
                TournamentEventJoin(session, ch, entryId, targetId);
                break;
            }
            default:
                _log.LogDebug("Tournament sub-command 0x{P:X4} from char {C} not yet implemented.", protocol, charId);
                break;
        }
    }

    // ===== helpers (CanDoTournament / Add-Del-Find player / ranking) =====

    private bool CanDoTournament(byte step, byte group = 0)
    {
        var t = _state.Tournament;
        return t is not null && t.Step == step && (group == 0 || t.Group == group);
    }

    private TnmtPlayer? FindTnmtPlayer(uint charId) => _state.Tournament!.FindPlayer(charId);

    private bool FindTnmtPlayerApply(string hwid, uint ip)
        => _state.Tournament!.Players.Values.Any(p => p.Hwid == hwid || p.IpAddr == ip);

    private void AddTnmtPlayer(TournamentEntry entry, TnmtPlayer player, byte step, TnmtPlayer chief)
    {
        player.EntryId = entry.EntryId;
        player.ChiefId = chief.CharId;
        player.SlotId = chief.SlotId;
        switch ((TnmtStep)step)
        {
            case TnmtStep.First: entry.First[player.CharId] = player; break;
            case TnmtStep.Normal: entry.Normal[player.CharId] = player; break;
            case TnmtStep.Party: chief.Party[player.CharId] = player; break;
            case TnmtStep.Match: entry.Player[player.CharId] = player; break;
            default: return;
        }
        _state.Tournament!.Players[player.CharId] = player;
    }

    private void DelTnmtPlayer(TournamentEntry? entry, TnmtPlayer player)
    {
        if (entry is not null)
        {
            entry.First.Remove(player.CharId);
            entry.Normal.Remove(player.CharId);
            entry.Player.Remove(player.CharId);
            if (entry.Type == (byte)TournamentEntryType.Party && FindTnmtPlayer(player.ChiefId) is { } chief)
                chief.Party.Remove(player.CharId);
        }
        _state.Tournament!.Players.Remove(player.CharId);
    }

    /// <summary>Per-char total/month rank (C++ GetRanking via m_mapRank/m_mapMonthRank). Those per-char
    /// indices aren't loaded in this server, so rank is reported as 0 — cosmetic for registration; the
    /// rank-seeded bracket build is the deferred slice.</summary>
    private static void GetRanking(uint charId, out uint rank, out uint monthRank) { rank = 0; monthRank = 0; }

    private bool IsInFirstGradeGroup(byte country, uint charId)
    {
        if (country >= Proto.CountryCount) return false;
        var t = _state.Tournament!;
        for (int j = 1; j < t.FirstGroupCount && j < Proto.FirstGradeGroupCount; j++)
            if (_state.FirstGradeGroup[country][j].CharId == charId) return true;
        return false;
    }

    // ===== registration =====

    private void TournamentApply(ServerSession session, Character ch, byte entryId, string hwid, uint ip)
    {
        var t = _state.Tournament!;
        void Fail(byte result) => session.Send(BuildTournamentResult(ch.CharId, ch.Key, Msg.MW_TOURNAMENTAPPLY_REQ, result));

        if (ch.Country > (byte)Contry.Broa) { Fail((byte)TournamentResult.Fail); return; }
        var entry = t.Entry(entryId);
        if (entry is null) { Fail((byte)TournamentResult.Fail); return; }

        byte result = (byte)TournamentResult.Disqualify;
        byte step = (byte)TnmtStep.Normal;
        if (CanDoTournament((byte)TnmtStep.First))
        {
            if (IsInFirstGradeGroup(ch.Country, ch.CharId)) { result = (byte)TournamentResult.Success; step = (byte)TnmtStep.First; }
        }
        else if (!CanDoTournament((byte)TnmtStep.Normal)) result = (byte)TournamentResult.Timeout;
        else result = (byte)TournamentResult.Success;

        if (result != (byte)TournamentResult.Success) { Fail(result); return; }
        if (FindTnmtPlayer(ch.CharId) is not null) { Fail(result); return; }
        if (FindTnmtPlayerApply(hwid, ip)) { Fail((byte)TournamentResult.Fail); return; }
        if (entry.First.Count >= Proto.TournamentSlot) { Fail((byte)TournamentResult.Full); return; }

        var player = new TnmtPlayer
        {
            Class = ch.Class, Country = ch.Country, Level = ch.Level, CharId = ch.CharId, Name = ch.Name,
            SlotId = (byte)Proto.TournamentSlot, Hwid = hwid, IpAddr = ip, GuildName = ch.Guild?.Name ?? "",
        };
        GetRanking(ch.CharId, out var rk, out var mrk); player.Rank = rk; player.MonthRank = mrk;
        AddTnmtPlayer(entry, player, step, player);

        var w = new PacketWriter(Msg.MW_TOURNAMENT_REQ);
        w.WriteUInt32(ch.CharId); w.WriteUInt32(ch.Key); w.WriteUInt16(Msg.MW_TOURNAMENTAPPLY_REQ);
        w.WriteByte(result); w.WriteByte(entryId);
        session.Send(w.ToArray());

        TournamentApplyInfo(session, ch);
    }

    private void TournamentApplyInfo(ServerSession session, Character ch)
    {
        var t = _state.Tournament!;
        if (t.Step > (byte)TnmtStep.Normal) return;

        var w = new PacketWriter(Msg.MW_TOURNAMENT_REQ);
        w.WriteUInt32(ch.CharId); w.WriteUInt32(ch.Key); w.WriteUInt16(Msg.MW_TOURNAMENTAPPLYINFO_REQ);
        w.WriteByte((byte)t.Entries.Count);

        byte myEntry = FindTnmtPlayer(ch.CharId)?.EntryId ?? 0;
        foreach (var e in t.Entries.Values.OrderBy(e => e.EntryId))
        {
            w.WriteByte(e.Group);
            w.WriteByte(e.EntryId);
            w.WriteString(e.Name);
            w.WriteByte(e.Type);
            w.WriteUInt32(e.Class);
            w.WriteBool(e.EntryId == myEntry);
            w.WriteUInt32(e.Fee);
            w.WriteUInt32(e.FeeBack);
            w.WriteByte(e.PermitCount);
            w.WriteByte(e.MinLevel);
            w.WriteByte(e.MaxLevel == 0xFF ? t.MaxLevel : e.MaxLevel);
            w.WriteByte((byte)(Proto.TournamentSlot - e.First.Count));
            w.WriteUInt16((ushort)e.Normal.Count);
            WriteRewards(w, e, ch.Class);
            w.WriteByte((byte)e.First.Count);
            foreach (var p in e.First.Values.OrderBy(p => p.CharId)) WritePlayer7(w, p);
        }
        session.Send(w.ToArray());
    }

    private void TournamentJoinList(ServerSession session, Character ch)
    {
        var t = _state.Tournament!;
        if (!CanDoTournament((byte)TnmtStep.Party)) return;

        var w = new PacketWriter(Msg.MW_TOURNAMENT_REQ);
        w.WriteUInt32(ch.CharId); w.WriteUInt32(ch.Key); w.WriteUInt16(Msg.MW_TOURNAMENTJOINLIST_REQ);
        w.WriteByte((byte)t.Entries.Count);

        byte myEntry = FindTnmtPlayer(ch.CharId)?.EntryId ?? 0;
        foreach (var e in t.Entries.Values.OrderBy(e => e.EntryId))
        {
            w.WriteByte(e.Group);
            w.WriteByte(e.EntryId);
            w.WriteString(e.Name);
            w.WriteByte(e.Type);
            w.WriteUInt32(e.Class);
            w.WriteBool(e.EntryId == myEntry);
            WriteRewards(w, e, ch.Class);
            w.WriteByte((byte)e.Player.Count);
            foreach (var p in e.Player.Values.OrderBy(p => p.CharId)) WritePlayer7(w, p);
        }
        session.Send(w.ToArray());
    }

    private void TournamentPartyAdd(ServerSession session, Character ch, uint targetId, byte country, string targetName, byte level, byte cls)
    {
        var t = _state.Tournament!;
        if (!CanDoTournament((byte)TnmtStep.Party)) return;

        var entry = t.Entries.Values.FirstOrDefault(e => e.Type == (byte)TournamentEntryType.Party);
        if (entry is null) return;

        void Fail(byte result) => session.Send(BuildTournamentResult(ch.CharId, ch.Key, Msg.MW_TOURNAMENTPARTYADD_REQ, result));
        if (targetId == 0 || ch.Country != country) { Fail((byte)TournamentResult.NotFound); return; }
        if (FindTnmtPlayer(targetId) is not null) { Fail((byte)TournamentResult.AlreadyReg); return; }
        if (entry.MaxLevel < level || entry.MinLevel > level)
        {
            var lw = new PacketWriter(Msg.MW_TOURNAMENT_REQ);
            lw.WriteUInt32(ch.CharId); lw.WriteUInt32(ch.Key); lw.WriteUInt16(Msg.MW_TOURNAMENTPARTYADD_REQ);
            lw.WriteByte((byte)TournamentResult.Level); lw.WriteString(targetName);
            session.Send(lw.ToArray());
            return;
        }
        var chief = FindTnmtPlayer(ch.CharId);
        if (chief is null) return;
        if (chief.Party.Count >= 6) { Fail((byte)TournamentResult.Full); return; }

        var tgt = new TnmtPlayer { Class = cls, Country = country, Level = level, CharId = targetId, Name = targetName };
        if (_state.CharGuild.TryGetValue(targetId, out var gid) && _state.FindGuild(gid) is { } g) tgt.GuildName = g.Name;
        GetRanking(targetId, out var rk, out var mrk); tgt.Rank = rk; tgt.MonthRank = mrk;
        AddTnmtPlayer(entry, tgt, (byte)TnmtStep.Party, chief);

        var w = new PacketWriter(Msg.MW_TOURNAMENT_REQ);
        w.WriteUInt32(ch.CharId); w.WriteUInt32(ch.Key); w.WriteUInt16(Msg.MW_TOURNAMENTPARTYADD_REQ);
        w.WriteByte((byte)TournamentResult.Success); w.WriteString(targetName); w.WriteUInt32(targetId);
        session.Send(w.ToArray());

        TournamentPartyList(session, ch, ch.CharId);
    }

    private void TournamentPartyDel(ServerSession session, Character ch, uint targetId)
    {
        if (!CanDoTournament((byte)TnmtStep.Party)) return;
        var player = FindTnmtPlayer(targetId);
        if (player is null) return;
        if (player.ChiefId == targetId || (player.ChiefId != ch.CharId && targetId != ch.CharId)) return;

        uint chief = player.ChiefId;
        DelTnmtPlayer(_state.Tournament!.Entry(player.EntryId), player);
        TournamentPartyList(session, ch, chief);
    }

    private void TournamentPartyList(ServerSession session, Character ch, uint chiefId)
    {
        var chief = FindTnmtPlayer(chiefId);
        if (chief is null) return;

        var w = new PacketWriter(Msg.MW_TOURNAMENT_REQ);
        w.WriteUInt32(ch.CharId); w.WriteUInt32(ch.Key); w.WriteUInt16(Msg.MW_TOURNAMENTPARTYLIST_REQ);
        w.WriteUInt32(chiefId);
        w.WriteByte((byte)chief.Party.Count);
        foreach (var p in chief.Party.Values.OrderBy(p => p.CharId)) WritePlayer7(w, p);
        session.Send(w.ToArray());
    }

    private void TournamentMatchList(ServerSession session, Character ch)
    {
        var t = _state.Tournament!;
        var w = new PacketWriter(Msg.MW_TOURNAMENT_REQ);
        w.WriteUInt32(ch.CharId); w.WriteUInt32(ch.Key); w.WriteUInt16(Msg.MW_TOURNAMENTMATCHLIST_REQ);
        w.WriteByte((byte)t.Entries.Count);

        var mine = FindTnmtPlayer(ch.CharId);
        foreach (var e in t.Entries.Values.OrderBy(e => e.EntryId))
        {
            w.WriteByte(e.Group);
            w.WriteByte(e.EntryId);
            w.WriteString(e.Name);
            w.WriteByte(e.Type);
            w.WriteUInt32(e.Class);
            w.WriteBool(mine is not null && mine.EntryId == e.EntryId);
            WriteRewards(w, e, ch.Class);
            w.WriteByte((byte)e.Player.Count);
            foreach (var p in e.Player.Values.OrderBy(p => p.CharId))
            {
                w.WriteByte(p.SlotId);
                WritePlayer7(w, p);
                w.WriteByte(p.Result[0]); w.WriteByte(p.Result[1]); w.WriteByte(p.Result[2]);
            }
        }
        session.Send(w.ToArray());
    }

    // ===== sender fragments =====

    /// <summary>The standard 7-field TNMTPLAYER block shared by the list senders.</summary>
    private static void WritePlayer7(PacketWriter w, TnmtPlayer p)
    {
        w.WriteUInt32(p.CharId);
        w.WriteByte(p.Country);
        w.WriteString(p.Name);
        w.WriteByte(p.Level);
        w.WriteByte(p.Class);
        w.WriteUInt32(p.Rank);
        w.WriteUInt32(p.MonthRank);
    }

    /// <summary>Rewards filtered by the requester's class bit-mask (C++ <c>dwClass &amp; (1 &lt;&lt; class)</c>).</summary>
    private static void WriteRewards(PacketWriter w, TournamentEntry e, byte charClass)
    {
        uint mask = 1u << charClass;
        var matching = e.Rewards.Where(rw => (rw.Class & mask) != 0).ToList();
        w.WriteByte((byte)matching.Count);
        foreach (var rw in matching)
        {
            w.WriteByte(rw.CheckShield);
            w.WriteByte(rw.ChartType);
            w.WriteUInt16(rw.ItemId);
            w.WriteByte(rw.Count);
        }
    }

    private static byte[] BuildTournamentResult(uint charId, uint key, ushort protocol, byte result)
    {
        var w = new PacketWriter(Msg.MW_TOURNAMENT_REQ);
        w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteUInt16(protocol); w.WriteByte(result);
        return w.ToArray();
    }

    /// <summary>TournamentInfo: broadcast the bracket/entry config to every map (or one on connect).</summary>
    private void TournamentInfoBroadcast(ServerSession? only = null)
    {
        var packet = BuildTournamentInfo();
        if (only is not null) { only.Send(packet); return; }
        foreach (var s in _state.Servers.Values) s.Send(packet);
    }

    // ===== sender (SendMW_TOURNAMENTINFO_REQ) =====

    private byte[] BuildTournamentInfo()
    {
        var t = _state.Tournament!;
        var w = new PacketWriter(Msg.MW_TOURNAMENTINFO_REQ);
        w.WriteByte(t.FirstGroupCount);
        w.WriteByte(t.Group);
        w.WriteByte(t.Step);
        w.WriteByte((byte)t.Entries.Count);
        // C++ MAPTOURNAMENTENTRY is std::map<BYTE> — emit in ascending entryId order for a byte-identical body.
        foreach (var e in t.Entries.Values.OrderBy(e => e.EntryId))
        {
            w.WriteByte(e.Group);
            w.WriteByte(e.EntryId);
            w.WriteString(e.Name);
            w.WriteByte(e.Type);
            w.WriteUInt32(e.Class);
            w.WriteUInt32(e.Fee);
            w.WriteUInt32(e.FeeBack);
            w.WriteUInt16(e.PermitItemId);
            w.WriteByte(e.PermitCount);
            w.WriteByte(e.MinLevel);
            w.WriteByte(e.MaxLevel);
            w.WriteByte((byte)e.Rewards.Count);
            foreach (var rw in e.Rewards)
            {
                w.WriteByte(rw.ChartType);
                w.WriteUInt16(rw.ItemId);
                w.WriteByte(rw.Count);
            }
        }
        return w.ToArray();
    }
}
