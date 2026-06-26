using Microsoft.Extensions.Logging;
using TWorld.Protocol;

namespace TWorld.Server.World;

/// <summary>
/// Phase 4d (gameplay) — the tournament <b>scheduler + rank-seeded bracket build</b>, ported from
/// <c>SetTournamentTime</c> + the OnTimer schedule walker + <c>OnSM_TOURNAMENT_REQ</c> +
/// <c>TournamentSelectPlayer</c>/<c>TNMTMatch</c>/<c>TournamentUpdate</c> in <c>TWorldSvr.cpp</c>. The
/// schedule advances the step the registration window keys off, then seeds the 8-slot match bracket.
/// The C++'s multi-schedule overlap arbitration + crash-recovery step resume (event tournaments) are
/// collapsed to the single base tournament; events, the result handler, and betting remain deferred.
/// </summary>
public sealed partial class WorldService
{
    private static uint TRand(uint max) => max <= 1 ? 0u : (uint)Random.Shared.NextInt64(max);

    /// <summary>Test/diagnostic seam: drive the tournament schedule for an absolute unix time.</summary>
    public void TournamentTick(long now)
    {
        if (_state.Tournament is not null) TournamentOnTimer(now);
    }

    // ===== SetTournamentTime (date-math) =====

    /// <summary>Compute each step's absolute start/end from the BT_TOURNAMENT window (the Nth weekday of the
    /// month at the window's battle-start second), chaining steps back-to-back. Returns false (schedule
    /// off) when the window has no week set or every computed slot is already in the past.</summary>
    private bool SetTournamentTime(BattleTime window, long now, bool monthBase)
    {
        var t = _state.Tournament!;
        if (window.Week == 0 || t.Steps.Count == 0) { t.ScheduleActive = false; return false; }

        var ordered = t.Steps.OrderBy(s => StepKey(s.StepId, s.Group)).ToList();
        var cur = DateTimeOffset.FromUnixTimeSeconds(now).UtcDateTime;
        long dStart = 0;

        for (int m = monthBase ? 1 : 0; m <= 1; m++)
        {
            int year = cur.Year, month = cur.Month + m;
            if (month > 12) { year++; month = 1; }

            DateTime? start = FindNthWeekday(year, month, window.Day, window.Week);
            if (start is not { } s0) continue;

            dStart = new DateTimeOffset(s0, TimeSpan.Zero).ToUnixTimeSeconds() + window.BattleStart;
            foreach (var step in ordered) { step.Start = dStart; step.End = dStart + step.Period; dStart = step.End; }
            if (dStart > now) break;
        }

        if (dStart <= now) { t.ScheduleActive = false; return false; }
        t.ScheduleActive = true;
        return true;
    }

    /// <summary>The <paramref name="week"/>-th occurrence of MFC day-of-week <paramref name="mfcDay"/>
    /// (1=Sun..7=Sat) in the month at midnight UTC, or null if it doesn't occur that many times.</summary>
    private static DateTime? FindNthWeekday(int year, int month, byte mfcDay, byte week)
    {
        int days = DateTime.DaysInMonth(year, month);
        int seen = 0;
        for (int i = 1; i <= days; i++)
        {
            var d = new DateTime(year, month, i, 0, 0, 0, DateTimeKind.Utc);
            if ((byte)((int)d.DayOfWeek + 1) != mfcDay) continue;
            if (++seen == week) return d;
        }
        return null;
    }

    private static int StepKey(byte step, byte group) => step | (group << 8);

    // ===== OnTimer schedule walker =====

    private void TournamentOnTimer(long now)
    {
        var t = _state.Tournament!;
        if (!t.ScheduleActive || t.Steps.Count == 0) return;

        var ordered = t.Steps.OrderBy(s => StepKey(s.StepId, s.Group)).ToList();
        byte lastGroup = ordered[^1].Group;

        foreach (var step in ordered)
        {
            if (step.Period == 0) continue;
            if (step.Start > now) break;
            if (step.Start != 0 && step.Start <= now)
            {
                step.Start = 0;
                TournamentStepFire(step.Group, step.StepId, step.Period);
                break;
            }
            if (step.StepId == (byte)TnmtStep.End && step.End != 0 && step.End <= now && lastGroup == step.Group)
            {
                step.End = 0;
                if (_state.Battles is not null) SetTournamentTime(_state.Battles[BattleType.Tournament], now, monthBase: true);
                TournamentUpdate();
                break;
            }
        }
    }

    // ===== step machine (OnSM_TOURNAMENT_REQ folded inline) =====

    private void TournamentStepFire(byte group, byte step, uint period)
    {
        var t = _state.Tournament!;
        if (t.Entries.Count == 0) return;

        if (t.Group != group)
        {
            t.Group = group;
            t.Sum = 0;
            byte ec = TournamentEntryCount();
            t.Base = ec != 0 ? (byte)(Proto.TournamentBasePrize / ec) : (byte)0;
            t.Selected = false;
        }
        t.Step = step;
        _ = PersistGame(() => _gameDb!.TournamentStatusAsync(t.Id, group, step), "TTournamentStatus"); // C++ SendDM_TOURNAMENTSTATUS_REQ

        long nextStart = t.Steps.FirstOrDefault(s => s.Group == group && s.StepId == step + 1)?.Start ?? 0;
        var enable = BuildTournamentEnable(group, step, period, nextStart);
        foreach (var s in _state.Servers.Values) s.Send(enable);

        if (!t.Selected && (step == (byte)TnmtStep.Party || step == (byte)TnmtStep.Match))
            TournamentSelectPlayer();

        if (step == (byte)TnmtStep.Match)
            TournamentMatchBroadcast();
    }

    private byte TournamentEntryCount()
    {
        var t = _state.Tournament!;
        return (byte)t.Entries.Values.Count(e => e.Group == t.Group);
    }

    /// <summary>Re-evaluate after an END: clear the finished bracket, recompute the base, re-announce.</summary>
    private void TournamentUpdate()
    {
        var t = _state.Tournament!;
        TournamentClear();
        byte ec = TournamentEntryCount();
        t.Base = ec != 0 ? (byte)(Proto.TournamentBasePrize / ec) : (byte)0;
        TournamentInfoBroadcast();
    }

    private void TournamentClear()
    {
        var t = _state.Tournament!;
        _ = PersistGame(() => _gameDb!.TournamentClearAsync(), "TTournamentClear"); // C++ SendDM_TOURNAMENTCLEAR_REQ
        t.Group = 0;
        t.Step = (byte)TnmtStep.Ready;
        t.Selected = false;
        t.Sum = 0;
        t.Base = 0;
        t.Players.Clear();
        foreach (var e in t.Entries.Values) { e.First.Clear(); e.Normal.Clear(); e.Player.Clear(); }
    }

    // ===== bracket build (TournamentSelectPlayer + TNMTMatch) =====

    /// <summary>Seed each entry's 8-slot match bracket from its registrants — keep the 1st-grade seeds,
    /// then fill remaining slots by a level-weighted random draw that balances countries (C++
    /// <c>TournamentSelectPlayer</c>), and order the slots by month-rank (C++ <c>TNMTMatch</c>).</summary>
    private void TournamentSelectPlayer()
    {
        var t = _state.Tournament!;
        t.Selected = true;

        // remaining pool = everyone currently registered (used to fee-back the unseeded later)
        var pool = new Dictionary<uint, TnmtPlayer>(t.Players);

        foreach (var entry in t.Entries.Values.OrderBy(e => e.EntryId))
        {
            entry.Player.Clear();

            var first = new List<TnmtPlayer>[Proto.CountryCount];
            var normal = new List<TnmtPlayer>[Proto.CountryCount];
            var byLevel = new List<(uint level, TnmtPlayer p)>[Proto.CountryCount];
            for (int c = 0; c < Proto.CountryCount; c++) { first[c] = new(); normal[c] = new(); byLevel[c] = new(); }

            foreach (var p in entry.First.Values)
                if (p.Country < Proto.CountryCount) { first[p.Country].Add(p); pool.Remove(p.CharId); }
            entry.First.Clear();

            var levelSum = new uint[Proto.CountryCount];
            foreach (var p in entry.Normal.Values)
                if (p.Country < Proto.CountryCount) { levelSum[p.Country] += p.Level; byLevel[p.Country].Add((levelSum[p.Country], p)); }
            entry.Normal.Clear();

            int sum = first.Sum(l => l.Count);
            var seededByCountry = new int[Proto.CountryCount];

            for (int slot = 0; slot < Proto.TournamentSlot; slot++)
            {
                if (sum >= Proto.TournamentSlot) break;
                if (sum > slot) continue;

                // pick the country with the fewest seeded so far that still has candidates
                int minC = -1;
                for (int c = 0; c < Proto.CountryCount; c++)
                {
                    if (byLevel[c].Count == 0) continue;
                    if (minC < 0 || first[c].Count + seededByCountry[c] < first[minC].Count + seededByCountry[minC]) minC = c;
                    else if (first[c].Count + seededByCountry[c] == first[minC].Count + seededByCountry[minC]) minC = TRand(2) != 0 ? c : minC;
                }
                if (minC < 0) break;

                // level-weighted random draw within that country
                uint roll = TRand(levelSum[minC]);
                var rebuilt = new List<(uint, TnmtPlayer)>();
                uint acc = 0;
                bool picked = false;
                foreach (var (cum, p) in byLevel[minC])
                {
                    if (cum > roll && !picked) { normal[minC].Add(p); pool.Remove(p.CharId); picked = true; }
                    else { acc += p.Level; rebuilt.Add((acc, p)); }
                }
                levelSum[minC] = acc;
                byLevel[minC] = rebuilt;
                seededByCountry[minC]++;
                sum++;
            }

            TnmtSeedBracket(entry, first, normal);
        }

        // unseeded leftovers are dropped from the active set; C++ mails each their entry fee back
        // (SendDM_TOURNAMENTPAYBACK_REQ) and unregisters them (SendDM_TOURNAMENTAPPLY_REQ(FALSE)).
        foreach (var p in pool.Values)
        {
            t.Players.Remove(p.CharId);
            uint feeBack = t.Entry(p.EntryId)?.FeeBack ?? 0;
            if (feeBack != 0) _ = PersistGame(() => _gameDb!.TournamentPaybackAsync(p.CharId, feeBack), "TTournamentPayback");
            _ = PersistGame(() => _gameDb!.TournamentApplyAsync(0, p.CharId, 0, 0, "", 0), "TTournamentApply(unseed)");
        }
        foreach (var charId in t.Players.Keys.Where(id => t.Players[id].SlotId == Proto.TournamentSlot).ToList())
            t.Players.Remove(charId);
    }

    /// <summary>Place seeds into the 8-slot bracket in the canonical order, preferring the lowest month-rank
    /// and, on odd slots, an opponent from a different country (C++ <c>TNMTMatch</c>).</summary>
    private void TnmtSeedBracket(TournamentEntry entry, List<TnmtPlayer>[] first, List<TnmtPlayer>[] normal)
    {
        byte[] order = { 0, 6, 4, 2, 3, 5, 7, 1 };
        var slotPlayer = new TnmtPlayer?[Proto.TournamentSlot];

        foreach (byte slot in order)
        {
            var pick = PickSeed(first, slot, slotPlayer) ?? PickSeed(normal, slot, slotPlayer);
            if (pick is null) continue;
            pick.SlotId = slot;
            AddTnmtPlayer(entry, pick, (byte)TnmtStep.Match, pick);
            (first[pick.Country].Contains(pick) ? first : normal)[pick.Country].Remove(pick);
            slotPlayer[slot] = pick;
        }
    }

    private static TnmtPlayer? PickSeed(List<TnmtPlayer>[] pools, byte slot, TnmtPlayer?[] slotPlayer)
    {
        TnmtPlayer? best = null, countryBest = null;
        for (int c = 0; c < Proto.CountryCount; c++)
            foreach (var p in pools[c])
            {
                if (best is null || best.MonthRank == 0 || (p.MonthRank != 0 && best.MonthRank > p.MonthRank)) best = p;
                if ((slot % 2) == 1 && slotPlayer[slot - 1] is { } prev && prev.Country != p.Country &&
                    (countryBest is null || countryBest.MonthRank == 0 || (p.MonthRank != 0 && countryBest.MonthRank > p.MonthRank)))
                    countryBest = p;
            }
        return countryBest ?? best;
    }

    private void TournamentMatchBroadcast()
    {
        var packet = BuildTournamentMatch();
        foreach (var s in _state.Servers.Values) s.Send(packet);
    }

    // ===== senders =====

    private static byte[] BuildTournamentEnable(byte group, byte step, uint period, long nextStart)
    {
        var w = new PacketWriter(Msg.MW_TOURNAMENTENABLE_REQ);
        w.WriteByte(group); w.WriteByte(step); w.WriteUInt32(period); w.WriteInt64(nextStart);
        return w.ToArray();
    }

    /// <summary>SendMW_TOURNAMENTMATCH_REQ: the whole seeded bracket (all entries' match players).</summary>
    private byte[] BuildTournamentMatch()
    {
        var t = _state.Tournament!;
        var players = t.Players.Values.Where(p => p.SlotId < Proto.TournamentSlot)
                                      .OrderBy(p => p.EntryId).ThenBy(p => p.SlotId).ToList();
        var w = new PacketWriter(Msg.MW_TOURNAMENTMATCH_REQ);
        w.WriteByte((byte)players.Count);
        foreach (var p in players)
        {
            w.WriteByte(p.EntryId);
            w.WriteByte(p.SlotId);
            w.WriteUInt32(p.CharId);
            w.WriteByte(p.Country);
            w.WriteString(p.Name);
            w.WriteByte(p.Level);
            w.WriteByte(p.Class);
            w.WriteUInt32(p.ChiefId);
            w.WriteByte(p.Result[0]);
            w.WriteByte(p.Result[1]);
            w.WriteByte(p.Result[2]);
        }
        return w.ToArray();
    }
}
