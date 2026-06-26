using TWorld.Protocol;
using TWorld.Server.Net;

namespace TWorld.Server.World;

/// <summary>
/// The GM event-tournament admin plane (<c>CT_TOURNAMENTEVENT</c> + <c>SM_TOURNAMENTEVENT</c>), ported from
/// <c>SSHandler.cpp</c> + the <c>TWorldSvr.cpp</c> scheduler helpers. The control server's GM tool manages a
/// set of named event-tournaments (<c>m_mapTournament</c>: id → entries), their schedules
/// (<c>m_mapTournamentSchedule</c> / <c>m_mapTournamentTime</c>), and player registration; the earliest
/// schedule becomes the running tournament.
/// <list type="bullet">
/// <item><b>TET_ENTRYADD / TET_ENTRYDEL</b> — build/replace and delete an event-tournament's entry set.</item>
/// <item><b>TET_SCHEDULEADD / TET_SCHEDULEDEL</b> — register/compute (or drop) an event-tournament schedule
/// (<c>SetTournamentTime</c> date-math + overlap arbitration); the earliest active schedule takes over the
/// running tournament (<c>TournamentUpdate</c>).</item>
/// <item><b>TET_PLAYERADD</b> — look the char up by name (<c>CTBLGetCharInfo</c>) and register it into the
/// running tournament's 1st-grade roster.</item>
/// <item><b>TET_PLAYERDEL</b> — drop a registrant; <b>TET_PLAYEREND</b> — seed+match the bracket.</item>
/// <item><b>TET_LIST</b> — serialize the schedules + entries + 1st-grade rosters back to control.</item>
/// </list>
/// There is no live driver (the GM client) in this deployment; the wire layout is faithful to the C++ senders.
/// </summary>
public sealed partial class WorldService
{
    private const byte TclassCount = 6; // TCLASS_COUNT

    private async Task<bool> DispatchTournamentEventAsync(ServerSession session, PacketReader r)
    {
        switch (r.Id)
        {
            case Msg.CT_TOURNAMENTEVENT_REQ: await OnCT_TOURNAMENTEVENT_REQ(r); return true;
            // The CT handler does the work inline; the SM legs (timer/batch round trip) are recognized no-ops.
            case Msg.SM_TOURNAMENTEVENT_REQ:
            case Msg.SM_TOURNAMENTEVENT_ACK:
                return true;
        }
        return false;
    }

    private async Task OnCT_TOURNAMENTEVENT_REQ(PacketReader r)
    {
        uint mgId = r.ReadUInt32();
        var type = (TournamentEventCmd)r.ReadByte();
        switch (type)
        {
            case TournamentEventCmd.List:
                TournamentEventList(mgId);
                break;

            case TournamentEventCmd.ScheduleAdd:
                await TournamentEventScheduleAdd(mgId, r);
                break;

            case TournamentEventCmd.ScheduleDel:
            {
                ushort tid = r.ReadUInt16();
                DelTournamentScheduleEvent(tid);
                AckTournamentEvent(mgId, type, w => w.WriteUInt16(tid));
                break;
            }

            case TournamentEventCmd.EntryAdd:
                await TournamentEventEntryAdd(r);
                break;

            case TournamentEventCmd.EntryDel:
            {
                ushort tid = r.ReadUInt16();
                byte entryId = r.ReadByte();
                TnmtEntryDelete(tid, entryId);
                break;
            }

            case TournamentEventCmd.PlayerAdd:
                await TournamentEventPlayerAdd(mgId, r);
                break;

            case TournamentEventCmd.PlayerDel:
                TournamentEventPlayerDel(mgId, r);
                break;

            case TournamentEventCmd.PlayerEnd:
                TournamentEventPlayerEnd(mgId);
                break;
        }
    }

    // ===== entries (TET_ENTRYADD / TET_ENTRYDEL) =====

    private async Task TournamentEventEntryAdd(PacketReader r)
    {
        ushort tid = r.ReadUInt16();
        byte count = r.ReadByte();
        var entries = new Dictionary<byte, TournamentEntry>();
        for (byte i = 0; i < count; i++)
        {
            var e = new TournamentEntry
            {
                EntryId = r.ReadByte(),
                Name = r.ReadString(),
                Type = r.ReadByte(),
                Class = r.ReadUInt32(),
                Fee = r.ReadUInt32(),
                FeeBack = r.ReadUInt32(),
                PermitItemId = r.ReadUInt16(),
                PermitCount = r.ReadByte(),
                MinLevel = r.ReadByte(),
                MaxLevel = r.ReadByte(),
            };
            byte rewardCount = r.ReadByte();
            for (byte j = 0; j < rewardCount; j++)
                e.Rewards.Add(new TournamentReward
                {
                    ChartType = r.ReadByte(), ItemId = r.ReadUInt16(), Count = r.ReadByte(),
                    Class = r.ReadUInt32(), CheckShield = r.ReadByte(),
                });
            entries[e.EntryId] = e;
        }
        _state.EventTournaments[tid] = entries;
        _log.LogInformation("Event-tournament {Tid} entries set: {N}.", tid, entries.Count);

        // C++ OnDM_TNMTEVENTENTRYADD: a clear sentinel (entryId 0, empty name) then each entry + its rewards, in
        // order — so this must be sequential, not fire-and-forget.
        await PersistGame(async () =>
        {
            await _gameDb!.TnmtEventEntryAsync(tid, 0, "", 0, 0, 0, 0, 0, 0, 0, 0);
            foreach (var e in entries.Values)
            {
                await _gameDb!.TnmtEventEntryAsync(tid, e.EntryId, e.Name, e.Type, e.Class, e.Fee, e.FeeBack, e.PermitItemId, e.PermitCount, e.MinLevel, e.MaxLevel);
                foreach (var rw in e.Rewards)
                    await _gameDb!.TnmtEventRewardAsync(tid, e.EntryId, rw.ChartType, rw.ItemId, rw.Count, rw.Class, rw.CheckShield);
            }
        }, "TTnmtEventEntry/Reward");
    }

    private void TnmtEntryDelete(ushort tid, byte entryId)
    {
        if (!_state.EventTournaments.TryGetValue(tid, out var entries)) return;
        entries.Remove(entryId);
        if (entries.Count == 0) _state.EventTournaments.Remove(tid);
    }

    // ===== schedule machinery (TET_SCHEDULEADD / TET_SCHEDULEDEL) =====

    /// <summary>Register/replace an event-tournament schedule, compute its step times, and (if it survives the
    /// overlap check) re-evaluate which schedule runs. C++ OnSM_TOURNAMENTEVENT_REQ TET_SCHEDULEADD +
    /// SetTournamentTime.</summary>
    private async Task TournamentEventScheduleAdd(uint mgId, PacketReader r)
    {
        ushort tid = r.ReadUInt16();
        byte week = r.ReadByte();
        byte day = r.ReadByte();
        uint battleStart = r.ReadUInt32();
        byte count = r.ReadByte();

        var sched = new EventTournamentSchedule { Id = tid, Enable = false, Week = week, Day = day, BattleStart = battleStart };
        for (byte i = 0; i < count; i++)
            sched.Steps.Add(new TournamentStep { StepId = r.ReadByte(), Period = r.ReadUInt32() });

        ushort newId = SetTournamentTimeEvent(sched);
        if (newId != 0 && tid != 0) TournamentUpdateEvent(newId);

        AckTournamentEvent(mgId, TournamentEventCmd.ScheduleAdd, w => w.WriteUInt16(newId));

        // C++ SendDM_TNMTEVENTSCHEDULEADD_REQ (with the allocated id): window then each step, in order.
        if (newId != 0)
            await PersistGame(async () =>
            {
                await _gameDb!.TnmtEventTimeAsync(newId, week, day, battleStart);
                foreach (var st in sched.Steps) await _gameDb!.TnmtEventScheduleAsync(newId, st.StepId, st.Period);
            }, "TTnmtEventTime/Schedule");
    }

    /// <summary>C++ SetTournamentTime — compute each step's absolute start/end from the schedule's Nth-weekday
    /// window (this month then next), reject if all slots are in the past or overlap another schedule, and
    /// register the schedule (allocating an id when 0). Returns the schedule id, or 0 on rejection.</summary>
    private ushort SetTournamentTimeEvent(EventTournamentSchedule sched)
    {
        if (sched.Week == 0 || sched.Steps.Count == 0) return 0;

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var ordered = sched.Steps.OrderBy(s => StepKey(s.StepId, s.Group)).ToList();
        var cur = DateTime.UtcNow;
        long dStart = 0;
        for (int m = 0; m <= 1; m++)
        {
            int year = cur.Year, month = cur.Month + m;
            if (month > 12) { year++; month = 1; }
            if (FindNthWeekday(year, month, sched.Day, sched.Week) is not { } start) continue;
            dStart = new DateTimeOffset(start, TimeSpan.Zero).ToUnixTimeSeconds() + sched.BattleStart;
            foreach (var step in ordered) { step.Start = dStart; step.End = dStart + step.Period; dStart = step.End; }
            if (dStart > now) break;
        }
        if (dStart <= now) return 0; // every slot is in the past

        // Overlap check against the other registered schedules (C++ rejects an overlapping window).
        long ns = ordered[0].Start, ne = ordered[^1].End;
        foreach (var other in _state.EventSchedules.Values)
        {
            if (other.Id == sched.Id || other.Steps.Count == 0) continue;
            var oo = other.Steps.OrderBy(s => StepKey(s.StepId, s.Group)).ToList();
            long cs = oo[0].Start, ce = oo[^1].End;
            if ((ns >= cs && ns <= ce) || (ne >= cs && ne <= ce)) return 0;
        }

        if (sched.Id == 0) sched.Id = ++_state.EventTournamentIdSeq;
        else if (sched.Id > _state.EventTournamentIdSeq) _state.EventTournamentIdSeq = sched.Id;
        _state.EventSchedules[sched.Id] = sched;
        return sched.Id;
    }

    /// <summary>C++ DelTournamentSchedule — drop a schedule (+ its entries) and re-evaluate the running one.</summary>
    private void DelTournamentScheduleEvent(ushort tid)
    {
        _state.EventSchedules.Remove(tid);
        if (_state.EventTournaments.TryGetValue(tid, out var entries)) { entries.Clear(); _state.EventTournaments.Remove(tid); }
        TournamentUpdateEvent(tid);
        _ = PersistGame(() => _gameDb!.TnmtEventDelAsync(tid), "TTnmtEventDel"); // C++ SendDM_TNMTEVENTSCHEDULEDEL_REQ
    }

    /// <summary>C++ TournamentUpdate — pick the earliest-starting registered schedule and make it the running
    /// tournament (copying its steps + entries into <see cref="WorldState.Tournament"/>). Leaves the running
    /// tournament untouched when no event schedule is registered (so the config-loaded base keeps running).</summary>
    private void TournamentUpdateEvent(ushort changedId)
    {
        EventTournamentSchedule? earliest = null;
        long best = long.MaxValue;
        foreach (var s in _state.EventSchedules.Values)
        {
            if (s.Steps.Count == 0) continue;
            long st = s.Steps.Min(x => x.Start);
            if (st < best) { best = st; earliest = s; }
        }
        if (earliest is null) return; // no event schedule -> base tournament keeps running

        var t = _state.Tournament ??= new TournamentState();
        if (changedId != earliest.Id && t.Id == earliest.Id) return; // already running the earliest

        t.Id = earliest.Id;
        earliest.Enable = true;
        t.Steps.Clear();
        foreach (var s in earliest.Steps)
            t.Steps.Add(new TournamentStep { Group = s.Group, StepId = s.StepId, Period = s.Period, Start = s.Start, End = s.End });
        t.ScheduleActive = true;
        t.Group = 0; t.Step = (byte)TnmtStep.Ready; t.Selected = false; t.Sum = 0; t.Base = 0;
        t.Players.Clear();

        if (_state.EventTournaments.TryGetValue(earliest.Id, out var entries))
        {
            t.Entries.Clear();
            foreach (var (id, e) in entries) t.Entries[id] = e;
        }
        foreach (var e in t.Entries.Values) { e.First.Clear(); e.Normal.Clear(); e.Player.Clear(); }

        TournamentInfoBroadcast();
        _log.LogInformation("Event-tournament {Tid} is now the running tournament (starts {Start}).", earliest.Id, best);
    }

    // ===== player admin (TET_PLAYERADD / TET_PLAYERDEL / TET_PLAYEREND) =====

    /// <summary>TET_PLAYERADD — validate, look the char up by name (CTBLGetCharInfo), and register it into the
    /// running tournament's 1st-grade roster. C++ OnCT_TOURNAMENTEVENT TET_PLAYERADD →
    /// DM_TOURNAMENTEVENTCHARINFO → OnSM_TOURNAMENTEVENT_ACK TET_PLAYERADD, folded inline.</summary>
    private async Task TournamentEventPlayerAdd(uint mgId, PacketReader r)
    {
        ushort tourId = r.ReadUInt16();
        byte entryId = r.ReadByte();
        string target = r.ReadString();

        var t = _state.Tournament;
        bool error = t is null || tourId != t.Id || t.Step >= (byte)TournamentStepId.Enter || t.Entry(entryId) is null;
        if (!error && _gameDb is not null)
        {
            try
            {
                var rows = await _gameDb.GetCharInfoByNameAsync(target);
                if (rows.Count == 1) // exactly one match, as in the C++ (>1 is ambiguous -> failure)
                {
                    var row = rows[0];
                    if (t!.Entry(entryId) is { } entry && t.FindPlayer(row.CharId) is null)
                    {
                        var player = new TnmtPlayer
                        {
                            EntryId = entryId, CharId = row.CharId, Name = row.Name, Level = row.Level,
                            Class = row.Class, Country = row.Country, ChiefId = row.CharId,
                            GuildName = _state.FindGuildByChar(row.CharId)?.Name ?? "",
                        };
                        GetRanking(row.CharId, out var rk, out var mrk); player.Rank = rk; player.MonthRank = mrk;
                        AddTnmtPlayer(entry, player, (byte)TnmtStep.First, player);
                        _ = PersistGame(() => _gameDb!.TournamentApplyAsync(1, row.CharId, entryId, row.CharId, "", 0), "TTournamentApply(add)");
                        AckPlayerAdd(mgId, entryId, row.CharId, row.Name, row.Level, row.Class, row.Country);
                        return;
                    }
                }
            }
            catch (Exception ex) { _log.LogWarning(ex, "CTBLGetCharInfo failed for tournament PLAYERADD."); }
        }

        // invalid request, no DB, ambiguous/missing name, duplicate, or entry gone -> failure ack
        AckPlayerAdd(mgId, entryId, 0, "", 0, TclassCount, (byte)Contry.None);
    }

    private void AckPlayerAdd(uint mgId, byte entryId, uint charId, string name, byte level, byte cls, byte country)
    {
        var w = new PacketWriter(Msg.CT_TOURNAMENTEVENT_ACK);
        w.WriteUInt32(mgId); w.WriteByte((byte)TournamentEventCmd.PlayerAdd); w.WriteByte(entryId);
        w.WriteUInt32(charId); w.WriteString(name); w.WriteByte(level); w.WriteByte(cls); w.WriteByte(country);
        _state.ControlServer?.Send(w.ToArray());
    }

    private void TournamentEventPlayerDel(uint mgId, PacketReader r)
    {
        ushort tid = r.ReadUInt16();
        byte entryId = r.ReadByte();
        string target = r.ReadString();

        uint charId = 0;
        var t = _state.Tournament;
        if (t is not null && tid == t.Id && t.Entry(entryId) is { } entry)
        {
            var player = t.Players.Values.FirstOrDefault(p => p.Name == target);
            if (player is not null)
            {
                charId = player.CharId;
                DelTnmtPlayer(entry, player);
                _ = PersistGame(() => _gameDb!.TournamentApplyAsync(0, player.CharId, 0, 0, "", 0), "TTournamentApply(del)");
            }
        }

        var w = new PacketWriter(Msg.CT_TOURNAMENTEVENT_ACK);
        w.WriteUInt32(mgId); w.WriteByte((byte)TournamentEventCmd.PlayerDel); w.WriteByte(entryId); w.WriteUInt32(charId);
        _state.ControlServer?.Send(w.ToArray());
    }

    private void TournamentEventPlayerEnd(uint mgId)
    {
        var t = _state.Tournament;
        if (t is not null)
        {
            if (t.Step >= (byte)TournamentStepId.Party) TournamentSelectPlayer();
            if (t.Step >= (byte)TournamentStepId.Match) TournamentMatchBroadcast();
        }
        AckTournamentEvent(mgId, TournamentEventCmd.PlayerEnd, null);
    }

    // ===== TET_LIST + helpers =====

    /// <summary>TET_LIST — schedules (id + window + steps) then the event-tournament entries + 1st-grade
    /// rosters. C++ OnSM_TOURNAMENTEVENT_REQ/_ACK TET_LIST.</summary>
    private void TournamentEventList(uint mgId)
    {
        if (_state.ControlServer is not { } ctrl) return;
        var w = new PacketWriter(Msg.CT_TOURNAMENTEVENT_ACK);
        w.WriteUInt32(mgId); w.WriteByte((byte)TournamentEventCmd.List);

        // schedule block
        w.WriteByte((byte)_state.EventSchedules.Count);
        foreach (var s in _state.EventSchedules.Values.OrderBy(x => x.Id))
        {
            w.WriteUInt16(s.Id); w.WriteByte(s.Week); w.WriteByte(s.Day); w.WriteUInt32(s.BattleStart);
            var steps = s.Steps.OrderBy(x => StepKey(x.StepId, x.Group)).ToList();
            w.WriteByte((byte)steps.Count);
            foreach (var st in steps) { w.WriteByte(st.StepId); w.WriteUInt32(st.Period); w.WriteInt64(st.Start); }
        }

        // entry block
        w.WriteByte((byte)_state.EventTournaments.Count);
        foreach (var (tid, entries) in _state.EventTournaments.OrderBy(kv => kv.Key))
        {
            w.WriteUInt16(tid);
            w.WriteByte((byte)entries.Count);
            foreach (var e in entries.Values.OrderBy(x => x.EntryId))
            {
                w.WriteByte(e.EntryId); w.WriteString(e.Name); w.WriteByte(e.Type); w.WriteUInt32(e.Class);
                w.WriteUInt32(e.Fee); w.WriteUInt32(e.FeeBack); w.WriteUInt16(e.PermitItemId); w.WriteByte(e.PermitCount);
                w.WriteByte(e.MinLevel); w.WriteByte(e.MaxLevel);
                w.WriteByte((byte)e.Rewards.Count);
                foreach (var rw in e.Rewards)
                { w.WriteByte(rw.ChartType); w.WriteUInt16(rw.ItemId); w.WriteByte(rw.Count); w.WriteUInt32(rw.Class); w.WriteByte(rw.CheckShield); }
                w.WriteByte((byte)e.First.Count);
                foreach (var p in e.First.Values.OrderBy(x => x.CharId))
                { w.WriteUInt32(p.CharId); w.WriteString(p.Name); w.WriteByte(p.Level); w.WriteByte(p.Class); w.WriteByte(p.Country); }
            }
        }
        ctrl.Send(w.ToArray());
    }

    private void AckTournamentEvent(uint mgId, TournamentEventCmd type, Action<PacketWriter>? extra)
    {
        if (_state.ControlServer is not { } ctrl) return;
        var w = new PacketWriter(Msg.CT_TOURNAMENTEVENT_ACK);
        w.WriteUInt32(mgId); w.WriteByte((byte)type);
        extra?.Invoke(w);
        ctrl.Send(w.ToArray());
    }
}
