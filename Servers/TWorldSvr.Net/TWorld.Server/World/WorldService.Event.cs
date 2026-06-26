using TWorld.Protocol;
using TWorld.Server.Net;

namespace TWorld.Server.World;

/// <summary>
/// The event / lucky-event subsystem, ported from <c>SSHandler.cpp</c> + the <c>TWorldSvr.cpp</c> helpers.
/// The control server pushes events (<c>CT_EVENTUPDATE</c>) and lucky-event chart edits
/// (<c>CT_EVENTQUARTERLIST/UPDATE</c>); the world keeps the live event map + the time-keyed quarter schedule
/// and, on the 1-second tick, fires each quarter's pre-announce (<c>SM_EVENTQUARTERNOTIFY</c> → world chat)
/// and draw (<c>SM_EVENTQUARTER</c> → fan to maps), plus the timed-expiry queue (<c>SM_EVENTEXPIRED</c>) that
/// auto-removes expired guild-wanted / tactics-wanted ads. Lottery / gift-time events deliver via system mail
/// (relayed as <c>MW_WORLDPOSTSEND_REQ</c> to a map, as in the C++ <c>SendPost</c>).
///
/// The C++ <c>SayToBATCH</c> self-posts for the timer-driven SM messages are folded into direct calls here.
/// There is no live driver (the TControlSvr GM client) in this deployment, so these run only against the
/// simulated harness; the wire layout is faithful to the C++ senders.
/// </summary>
public sealed partial class WorldService
{
    private readonly Random _eventRand = new();

    private bool DispatchEvent(ServerSession session, PacketReader r)
    {
        if (r.Id == Msg.CT_EVENTUPDATE_REQ) { OnCT_EVENTUPDATE_REQ(r); return true; }
        return false;
    }

    // ===== CT_EVENTUPDATE (event map + lottery/gift) =====

    private void OnCT_EVENTUPDATE_REQ(PacketReader r)
    {
        byte eventId = r.ReadByte();
        ushort value = r.ReadUInt16();
        var ev = new EventInfo();
        ev.ReadFrom(r);
        ev.Value = value;

        if (eventId > (byte)EventType.Count) return;

        if (ev.Id == (byte)EventType.Lottery) { if (ev.State != 0) LotteryItem(ev); return; }
        if (ev.Id == (byte)EventType.GiftTime) { if (ev.State != 0) GiftTime(ev); return; }

        _state.EventInfos.Remove(ev.Index);
        if (value != 0) _state.EventInfos[ev.Index] = ev;

        var w = new PacketWriter(Msg.MW_EVENTUPDATE_REQ);
        w.WriteByte(eventId); w.WriteUInt16(value); ev.WriteTo(w);
        BroadcastServers(w.ToArray());
    }

    /// <summary>C++ LotteryItem — raffle each lottery item to random online chars on the event's map, mail the
    /// winners, then broadcast the winners list. Runs only for a non-zero (selected) map (mirrors the C++
    /// <c>if(bMap)</c>); 0xFF means all maps.</summary>
    private void LotteryItem(EventInfo ev)
    {
        byte map = (byte)ev.MapId;
        if (map == 0) return;
        var (title, message) = SplitLotMsg(ev.LotMsg);

        var pool = _state.Characters.Values.Where(c => map == 0xFF || (byte)c.MapId == map).ToList();
        var packets = new List<(EventInfo.Lottery Lot, List<Character> Winners)>();
        foreach (var lot in ev.Lotteries)
        {
            var winners = new List<Character>();
            for (int j = 0; j < lot.Winner; j++)
            {
                if (pool.Count == 0) break;
                int idx = _eventRand.Next(pool.Count);
                var c = pool[idx];
                SendPostLotItem(c.CharId, c.Name, title, message, lot.ItemId, lot.Num, 0);
                winners.Add(c);
                pool.RemoveAt(idx);
            }
            if (winners.Count > 0) packets.Add((lot, winners));
        }
        if (packets.Count == 0) return;

        var w = new PacketWriter(Msg.MW_EVENTMSGLOTTERY_REQ);
        w.WriteString(ev.Title);
        w.WriteUInt16((ushort)packets.Count);
        foreach (var (lot, winners) in packets)
        {
            w.WriteUInt16(lot.ItemId); w.WriteByte(lot.Num); w.WriteUInt16((ushort)winners.Count);
            foreach (var c in winners) w.WriteString(c.Name);
        }
        BroadcastServers(w.ToArray());
    }

    /// <summary>C++ GiftTime — mail the first lottery item to every online char inside the event's level range
    /// (HIBYTE..LOBYTE of wValue).</summary>
    private void GiftTime(EventInfo ev)
    {
        if (ev.Lotteries.Count == 0) return;
        var (title, message) = SplitLotMsg(ev.LotMsg);
        var lot = ev.Lotteries[0];
        byte minLevel = (byte)(ev.Value >> 8);
        byte maxLevel = (byte)(ev.Value & 0xFF);
        foreach (var ch in _state.Characters.Values)
            if (minLevel <= ch.Level && maxLevel >= ch.Level)
                SendPostLotItem(ch.CharId, ch.Name, title, message, lot.ItemId, lot.Num, lot.Winner);
    }

    /// <summary>Deliver a lucky-item system post via the first connected map (C++ SendPost(WPT_LOTITEM)).</summary>
    private void SendPostLotItem(uint recvId, string recver, string title, string message, ushort itemId, byte itemNum, ushort useTime)
    {
        var map = _state.Servers.Values.FirstOrDefault();
        if (map is null) return;
        var w = new PacketWriter(Msg.MW_WORLDPOSTSEND_REQ);
        w.WriteByte(4 /* WPT_LOTITEM */); w.WriteUInt32(recvId); w.WriteString(recver);
        w.WriteString(title); w.WriteString(message); w.WriteUInt16(itemId); w.WriteByte(itemNum); w.WriteUInt16(useTime);
        map.Send(w.ToArray());
    }

    /// <summary>C++ splits the lot message on the first '|' into (title, body).</summary>
    private static (string Title, string Message) SplitLotMsg(string s)
    {
        int i = s.IndexOf('|');
        return i < 0 ? (s, "") : (s[..i], s[(i + 1)..]);
    }

    // ===== CT_EVENTQUARTERLIST / CT_EVENTQUARTERUPDATE (DB-backed; wired in DispatchControlDbAsync) =====

    private async Task OnCT_EVENTQUARTERLIST_REQ(PacketReader r)
    {
        uint manager = r.ReadUInt32();
        byte day = r.ReadByte();

        var events = new List<LuckyEvent>();
        if (_gameDb is not null)
        {
            try
            {
                foreach (var row in await _gameDb.EventQuarterListAsync(day))
                {
                    var e = new LuckyEvent
                    {
                        Id = row.Id, Day = row.Day, Hour = row.Hour, Min = row.Min, Count = row.Count,
                        ItemId1 = row.ItemId1, ItemId2 = row.ItemId2, ItemId3 = row.ItemId3, ItemId4 = row.ItemId4, ItemId5 = row.ItemId5,
                        Present = row.Present, Announce = row.Announce, Title = row.Title, Message = row.Message,
                    };
                    var names = await _gameDb.GetItemNamesAsync(new[] { row.ItemId1, row.ItemId2, row.ItemId3, row.ItemId4, row.ItemId5 });
                    e.Item1 = names[0]; e.Item2 = names[1]; e.Item3 = names[2]; e.Item4 = names[3]; e.Item5 = names[4];
                    events.Add(e);
                }
            }
            catch (Exception ex) { _log.LogWarning(ex, "CTBLEventQuarterList failed."); }
        }

        if (_state.ControlServer is not { } ctrl) return;
        var w = new PacketWriter(Msg.CT_EVENTQUARTERLIST_ACK);
        w.WriteUInt32(manager); w.WriteUInt16((ushort)events.Count);
        foreach (var e in events) e.WriteTo(w);
        ctrl.Send(w.ToArray());
    }

    private async Task OnCT_EVENTQUARTERUPDATE_REQ(PacketReader r)
    {
        uint manager = r.ReadUInt32();
        byte type = r.ReadByte();
        var lk = LuckyEvent.ReadFrom(r);

        int ret = 0;
        if (_gameDb is not null)
        {
            try
            {
                var res = await _gameDb.EventQuarterUpdateAsync(type, lk.Id, lk.Day, lk.Hour, lk.Min,
                    new[] { lk.ItemId1, lk.ItemId2, lk.ItemId3, lk.ItemId4, lk.ItemId5 }, lk.Count, lk.Present, lk.Announce, lk.Title, lk.Message);
                ret = res.Ret;
                if (type == (byte)EventQuarterEdit.Add) lk.Id = res.OutId;
                if (res.Names.Length == 5)
                { lk.Item1 = res.Names[0]; lk.Item2 = res.Names[1]; lk.Item3 = res.Names[2]; lk.Item4 = res.Names[3]; lk.Item5 = res.Names[4]; }
            }
            catch (Exception ex) { _log.LogWarning(ex, "CSPEventQuarterUpdate failed."); ret = 1; }
        }

        // ret == 0 => success: apply the live-schedule edit (C++ OnDM_EVENTQUARTERUPDATE_ACK).
        if (ret == 0) ApplyEventQuarterEdit(type, lk);

        if (_state.ControlServer is { } ctrl)
        {
            var w = new PacketWriter(Msg.CT_EVENTQUARTERUPDATE_ACK);
            w.WriteByte((byte)ret); w.WriteUInt32(manager); w.WriteByte(type);
            lk.WriteTo(w);
            ctrl.Send(w.ToArray());
        }
    }

    private void ApplyEventQuarterEdit(byte type, LuckyEvent lk)
    {
        switch ((EventQuarterEdit)type)
        {
            case EventQuarterEdit.Del:
                _state.EventQuarters.Remove(lk.Id);
                break;
            case EventQuarterEdit.Add:
                _state.EventQuarters[lk.Id] = new EventQuarter
                {
                    Id = lk.Id, Day = lk.Day, Hour = lk.Hour, Minute = lk.Min, Noticed = false,
                    Present = lk.Present, Announce = lk.Announce, Time = GetNextEventTime(lk.Day, lk.Hour, lk.Min),
                };
                break;
            case EventQuarterEdit.Update:
                if (_state.EventQuarters.TryGetValue(lk.Id, out var q))
                {
                    q.Day = lk.Day; q.Hour = lk.Hour; q.Minute = lk.Min;
                    q.Present = lk.Present; q.Announce = lk.Announce;
                    q.Time = GetNextEventTime(lk.Day, lk.Hour, lk.Min);
                }
                break;
        }
    }

    // ===== SM event handlers + the 1-second timer drivers =====

    private void OnSM_EVENTQUARTER_REQ(PacketReader r)
    {
        byte day = r.ReadByte(); byte hour = r.ReadByte(); byte minute = r.ReadByte();
        string present = r.ReadString();
        EventQuarterDraw(day, hour, minute, present);
    }

    private void OnSM_EVENTQUARTERNOTIFY_REQ(PacketReader r) => EventQuarterNotify(r.ReadString());

    /// <summary>Pick a 0..99 selector and fan the quarter draw to every map (C++ OnSM_EVENTQUARTER_REQ).</summary>
    private void EventQuarterDraw(byte day, byte hour, byte minute, string present)
    {
        byte select = (byte)_eventRand.Next(100);
        var w = new PacketWriter(Msg.MW_EVENTQUARTER_REQ);
        w.WriteByte(day); w.WriteByte(hour); w.WriteByte(minute); w.WriteByte(select); w.WriteString(present);
        BroadcastServers(w.ToArray());
    }

    /// <summary>Broadcast the quarter pre-announce as a world chat from the operator name (C++
    /// OnSM_EVENTQUARTERNOTIFY_REQ).</summary>
    private void EventQuarterNotify(string announce)
    {
        var w = new PacketWriter(Msg.MW_CHAT_REQ);
        w.WriteUInt32(0); w.WriteUInt32(0); w.WriteByte(0); w.WriteUInt32(0);
        w.WriteString(_state.GetSvrMsg((uint)SvrMsg.NameOperator));
        w.WriteByte((byte)Contry.None); w.WriteByte((byte)Contry.None);
        w.WriteByte(3 /* CHAT_WORLD */); w.WriteByte(3 /* CHAT_WORLD */);
        w.WriteUInt32(0); w.WriteString(announce);
        BroadcastServers(w.ToArray());
    }

    /// <summary>C++ CheckEventQuarter — fire the earliest quarter's 5-minute pre-announce and, at its time, the
    /// draw, then reschedule it to the next occurrence.</summary>
    private void CheckEventQuarter()
    {
        if (_state.EventQuarters.Count == 0) return;
        var q = _state.EventQuarters.Values.OrderBy(x => x.Time).First();
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        long noticeTime = string.IsNullOrEmpty(q.Present) ? q.Time : q.Time - 5 * 60; // 5 min before, unless announce-only
        if (!q.Noticed && now >= noticeTime && !string.IsNullOrEmpty(q.Announce))
        {
            EventQuarterNotify(q.Announce);
            q.Noticed = true;
        }

        if (now >= q.Time)
        {
            if (!string.IsNullOrEmpty(q.Present))
                EventQuarterDraw(q.Day, q.Hour, q.Minute, q.Present);
            q.Noticed = false;
            q.Time = GetNextEventTime(q.Day, q.Hour, q.Minute);
        }
    }

    /// <summary>C++ GetNextEventTime — the next unix-second occurrence of (weekday 1..7, hour, minute). Uses UTC
    /// to stay consistent with the rest of the port. Returns -1 on an invalid spec.</summary>
    private static long GetNextEventTime(byte week, byte hour, byte min)
    {
        if (week == 0 || week > 7 || hour > 24 || min > 60) return -1;
        var now = DateTime.UtcNow;
        var evt = new DateTime(now.Year, now.Month, now.Day, hour % 24, min % 60, 0, DateTimeKind.Utc);
        int curWeek = (int)now.DayOfWeek + 1; // CTime: 1=Sun..7=Sat
        int w = week;
        if (curWeek > w) w += 7;
        int nDay = w - curWeek;
        if (nDay != 0) evt = evt.AddDays(nDay);
        if (evt < now) evt = evt.AddDays(7);
        return new DateTimeOffset(evt).ToUnixTimeSeconds();
    }

    // ===== timed-expiry queue (SM_EVENTEXPIRED) =====

    /// <summary>Insert or remove a timed-expiry entry, keeping the queue sorted ascending by fire time. C++
    /// OnSM_EVENTEXPIRED_REQ.</summary>
    private void OnSM_EVENTEXPIRED_REQ(PacketReader r)
    {
        bool insert = r.ReadByte() != 0;
        var buf = new ExpiredBuf(r.ReadByte(), r.ReadInt64(), r.ReadUInt32(), r.ReadUInt32());

        for (int i = 0; i < _state.Expired.Count; i++)
        {
            var e = _state.Expired[i];
            if (!insert && buf.TimeExpired == e.TimeExpired && buf.Type == e.Type && buf.Value1 == e.Value1 && buf.Value2 == e.Value2)
            {
                _state.Expired.RemoveAt(i);
                return;
            }
            if (buf.TimeExpired < e.TimeExpired) { _state.Expired.Insert(i, buf); return; }
        }
        _state.Expired.Add(buf);
    }

    /// <summary>An expiry fired on a peer: delete its target. C++ OnSM_EVENTEXPIRED_ACK.</summary>
    private void OnSM_EVENTEXPIRED_ACK(PacketReader r)
        => ExpiryFire(r.ReadByte(), r.ReadInt64(), r.ReadUInt32(), r.ReadUInt32());

    /// <summary>C++ CheckEventExpired — pop every entry whose time has passed and delete its target.</summary>
    private void CheckEventExpired()
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        while (_state.Expired.Count > 0 && _state.Expired[0].TimeExpired <= now)
        {
            var e = _state.Expired[0];
            _state.Expired.RemoveAt(0);
            ExpiryFire(e.Type, e.TimeExpired, e.Value1, e.Value2);
        }
    }

    /// <summary>Apply an expiry: drop the guild-wanted ad / tactics-wanted ad / tactics membership. The map
    /// broadcasts ride the existing *_DEL handlers; here we apply the data effect (C++ OnSM_EVENTEXPIRED_ACK).</summary>
    private void ExpiryFire(byte type, long _, uint value1, uint value2)
    {
        switch ((ExpiredType)type)
        {
            case ExpiredType.GuildWanted:
                if (_state.GuildWanted.Remove(value1))
                    _log.LogInformation("Guild-wanted ad for guild {Id} auto-expired.", value1);
                break;
            case ExpiredType.GuildTacticsWanted:
                if (_state.TacticsWanted.Remove(value2))
                    _log.LogInformation("Tactics-wanted ad {Id} auto-expired.", value2);
                break;
            case ExpiredType.GuildTactics:
                if (_state.FindGuild(value1) is { } g && g.Tactics.Remove(value2))
                {
                    _state.CharTactics.Remove(value2);
                    _log.LogInformation("Tactics contract (char {Char} in guild {Guild}) auto-expired.", value2, value1);
                }
                break;
        }
    }
}
