using Microsoft.Extensions.Logging;
using TWorld.Protocol;

namespace TWorld.Server.World;

/// <summary>
/// Phase 4d (gameplay) — the tournament <b>match results + betting</b>, ported from
/// <c>OnMW_TOURNAMENTRESULT_ACK</c>/<c>OnMW_TOURNAMENTENTERGATE_ACK</c> + the <c>TournamentEvent*</c>
/// (betting) functions + <c>TNMTEnterGate</c>/<c>JoinBatting</c>/<c>GetBattingAmount</c> in
/// <c>TWorldSvr.cpp</c>. A reported match outcome marks the round result for the winner/loser (and their
/// party), is fanned out to every map, and on the final pays each backer of the champion. The result is
/// persisted via TTournamentResult (and the unseeded fee-back/unapply via TTournamentPayback/TTournamentApply
/// in the bracket build), best-effort.
/// </summary>
public sealed partial class WorldService
{
    // ===== gate + result =====

    private void OnTournamentEnterGate(PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        uint money = r.ReadUInt32();
        byte enter = r.ReadByte();
        var ch = _state.FindChar(charId, key);
        if (ch is null) return;
        TnmtEnterGate(ch, money, enter != 0);
    }

    /// <summary>Mirrors TNMTEnterGate: clear any standing bets, then grant betting tickets from the money
    /// brought to the arena (only while a tournament is past the ENTER step).</summary>
    private void TnmtEnterGate(Character ch, uint money, bool enter)
    {
        var t = _state.Tournament!;
        foreach (var target in ch.Batting.Values.ToList()) ResetBatting(target, ch);
        ch.Ticket = 0;
        ch.Batting.Clear();

        uint ticket = money / (uint)Proto.TournamentBasePrize;
        if (enter && ticket != 0 && t.Id != 0 && t.Step >= (byte)TnmtStep.Enter)
        {
            t.Sum += ticket;
            ch.Ticket = ticket;
        }
    }

    private void OnTournamentResult(PacketReader r)
    {
        var t = _state.Tournament!;
        byte step = r.ReadByte();
        byte ret = r.ReadByte();
        uint win = r.ReadUInt32();
        uint lose = r.ReadUInt32();
        uint blueHide = r.ReadUInt32();
        uint redHide = r.ReadUInt32();

        if (step != (byte)TnmtStep.QFinal && step != (byte)TnmtStep.SFinal && step != (byte)TnmtStep.Final) return;
        int bid = step == (byte)TnmtStep.Final ? 2 : step == (byte)TnmtStep.SFinal ? 1 : 0;
        _ = PersistGame(() => _gameDb!.TournamentResultAsync(step, ret, win, lose), "TTournamentResult"); // C++ SendDM_TOURNAMENTRESULT_REQ

        var pWin = FindTnmtPlayer(win);
        var pLose = FindTnmtPlayer(lose);
        var affected = new List<uint>();

        if (pWin is not null)
        {
            byte rv = ret != 0 ? (byte)TnmtWin.Win : (byte)TnmtWin.Lose;
            foreach (var m in pWin.Party.Values) { m.Result[bid] = rv; affected.Add(m.CharId); }
            pWin.Result[bid] = rv;
        }
        if (pLose is not null)
        {
            foreach (var m in pLose.Party.Values) { m.Result[bid] = (byte)TnmtWin.Lose; affected.Add(m.CharId); }
            pLose.Result[bid] = (byte)TnmtWin.Lose;
        }

        var packet = BuildTournamentResultReq(t.Id, step, ret, win, lose, blueHide, redHide, affected);
        foreach (var s in _state.Servers.Values) s.Send(packet);

        // The champion's backers are paid out at the final.
        if (step == (byte)TnmtStep.Final && pWin is not null && ret != 0)
        {
            foreach (var (bettorId, _) in pWin.Batting)
            {
                if (!_state.Characters.TryGetValue(bettorId, out var bettor)) continue;
                GetBattingAmount(pWin, bettorId, out _, out uint amount);
                if (amount != 0) MapOf(bettor)?.Send(BuildTournamentBatPoint(bettorId, bettor.Name, amount));
            }
        }
        // SendDM_TOURNAMENTRESULT_REQ persistence is a documented best-effort gap.
    }

    // ===== betting helpers (FindBatter / Join / Reset / GetBattingAmount) =====

    private static TnmtPlayer? FindBatter(byte entryId, Character ch)
        => ch.Batting.Values.FirstOrDefault(p => p.EntryId == entryId);

    private void JoinBatting(TnmtPlayer target, Character ch)
    {
        ch.Batting[target.CharId] = target;
        target.Batting[ch.CharId] = ch.Ticket;
        target.Sum += ch.Ticket;
    }

    private void ResetBatting(TnmtPlayer target, Character ch)
    {
        if (target.Batting.TryGetValue(ch.CharId, out var amt))
        {
            target.Sum -= amt;
            target.Batting.Remove(ch.CharId);
        }
    }

    /// <summary>Mirrors GetBattingAmount: the C++ does integer odds (<c>FLOAT(sum / targetSum)</c>) then
    /// <c>base * bet * rate</c>.</summary>
    private void GetBattingAmount(TnmtPlayer target, uint charId, out float rate, out uint amount)
    {
        rate = 0; amount = 0;
        if (target.Batting.TryGetValue(charId, out var bet))
        {
            var t = _state.Tournament!;
            rate = target.Sum != 0 ? t.Sum / target.Sum : 0;   // integer division, as in C++
            amount = (uint)(t.Base * bet * rate);
        }
    }

    // ===== betting views (TournamentEventList / Info / Join) =====

    private void TournamentEventList(ServerSession session, Character ch)
    {
        var t = _state.Tournament!;
        if (t.Base == 0) return;

        var w = new PacketWriter(Msg.MW_TOURNAMENT_REQ);
        w.WriteUInt32(ch.CharId); w.WriteUInt32(ch.Key); w.WriteUInt16(Msg.MW_TOURNAMENTEVENTLIST_REQ);
        w.WriteByte(t.Base); w.WriteUInt32(t.Sum); w.WriteByte(TournamentEntryCount());

        foreach (var e in t.Entries.Values.OrderBy(e => e.EntryId))
        {
            if (e.Group != t.Group) continue;
            var bet = FindBatter(e.EntryId, ch);
            GetBattingAmount(bet ?? new TnmtPlayer(), ch.CharId, out float rate, out uint amount);
            w.WriteByte(e.EntryId);
            w.WriteString(e.Name);
            w.WriteByte(e.Type);
            w.WriteString(bet?.Name ?? "");
            w.WriteByte(bet?.Country ?? (byte)Contry.None);
            w.WriteFloat(rate);
            w.WriteUInt32(amount);
        }
        session.Send(w.ToArray());
    }

    private void TournamentEventInfo(ServerSession session, Character ch, byte entryId)
    {
        var t = _state.Tournament!;
        var entry = t.Entry(entryId);
        if (entry is null || ch.Ticket == 0) return;
        if (!CanDoTournament((byte)TnmtStep.Enter, entry.Group)) return;

        var w = new PacketWriter(Msg.MW_TOURNAMENT_REQ);
        w.WriteUInt32(ch.CharId); w.WriteUInt32(ch.Key); w.WriteUInt16(Msg.MW_TOURNAMENTEVENTINFO_REQ);
        w.WriteByte(entryId); w.WriteByte(t.Base); w.WriteUInt32(t.Sum);
        w.WriteByte((byte)entry.Player.Count);

        foreach (var p in entry.Player.Values.OrderBy(p => p.CharId))
        {
            w.WriteUInt32(p.CharId);
            w.WriteByte(p.Country);
            w.WriteString(p.GuildName);
            w.WriteString(p.Name);
            w.WriteByte(p.Level);
            w.WriteByte(p.Class);
            w.WriteUInt32(p.Rank);
            w.WriteUInt32(p.MonthRank);
            w.WriteFloat(p.Sum != 0 ? t.Sum / p.Sum : 0);
            w.WriteByte((byte)p.Party.Count);
            foreach (var m in p.Party.Values.OrderBy(m => m.CharId))
            {
                w.WriteUInt32(m.CharId);
                w.WriteByte(m.Country);
                w.WriteString(m.GuildName);
                w.WriteString(m.Name);
                w.WriteByte(m.Level);
                w.WriteByte(m.Class);
                w.WriteUInt32(m.Rank);
                w.WriteUInt32(m.MonthRank);
            }
        }
        session.Send(w.ToArray());
    }

    private void TournamentEventJoin(ServerSession session, Character ch, byte entryId, uint targetId)
    {
        var t = _state.Tournament!;
        var target = FindTnmtPlayer(targetId);
        if (target is null) return;
        var entry = t.Entry(entryId);
        if (entry is null || !CanDoTournament((byte)TnmtStep.Enter, entry.Group) || entry.EntryId != target.EntryId) return;

        var old = FindBatter(entryId, ch);
        if (old is not null) { ResetBatting(old, ch); ch.Batting.Remove(old.CharId); }
        JoinBatting(target, ch);

        session.Send(BuildTournamentResult(ch.CharId, ch.Key, Msg.MW_TOURNAMENTEVENTJOIN_REQ, (byte)TournamentResult.Success));
    }

    // ===== senders =====

    private static byte[] BuildTournamentResultReq(ushort id, byte step, byte ret, uint win, uint lose, uint blueHide, uint redHide, List<uint> players)
    {
        var w = new PacketWriter(Msg.MW_TOURNAMENTRESULT_REQ);
        w.WriteUInt16(id); w.WriteByte(step); w.WriteByte(ret); w.WriteUInt32(win); w.WriteUInt32(lose);
        w.WriteUInt32(blueHide); w.WriteUInt32(redHide);
        w.WriteByte((byte)players.Count);
        foreach (var p in players) w.WriteUInt32(p);
        return w.ToArray();
    }

    private static byte[] BuildTournamentBatPoint(uint charId, string name, uint amount)
    {
        var w = new PacketWriter(Msg.MW_TOURNAMENTBATPOINT_REQ);
        w.WriteUInt32(charId); w.WriteString(name); w.WriteUInt32(amount);
        return w.ToArray();
    }
}
