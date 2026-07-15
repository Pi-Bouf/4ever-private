using TControl.Data;
using TControl.Protocol;

namespace TControl.Server.Control;

/// <summary>
/// The scheduled-event engine — C++ <c>CheckEvent</c> + the event admin handlers (<c>OnCT_EVENTCHANGE_REQ</c>,
/// <c>OnCT_EVENTLIST_REQ</c>, <c>OnCT_CASHITEMLIST_REQ</c>) + the internal fan helpers the scheduler drives
/// (event message / value update / cash-sale / cash-shop-stop / delete) + the <c>szValue</c> column
/// pack/unpack (<c>ParseStrValue</c>/<c>MakeStrValue</c>).
/// </summary>
public sealed partial class ControlService
{
    private static readonly TimeZoneInfo LocalTz = TimeZoneInfo.Local;
    private static DateTime FromUnix(long s) => DateTimeOffset.FromUnixTimeSeconds(s).LocalDateTime;
    private static long ToUnix(DateTime dt) => new DateTimeOffset(dt, LocalTz.GetUtcOffset(dt)).ToUnixTimeSeconds();

    // ===== manager-facing event admin =====

    /// <summary>Add / update / delete a scheduled event with overlap arbitration + TEventUpdate persist.
    /// C++ OnCT_EVENTCHANGE_REQ.</summary>
    private async Task OnCT_EVENTCHANGE_REQ(ManagerSession mgr, PacketReader r)
    {
        if (!CheckAuthority(mgr, ManagerClass.GmLevel3)) return;

        byte type = r.ReadByte();
        var ev = EventInfo.Read(r);

        bool exists = _state.Events.ContainsKey(ev.Index);
        if (type == (byte)EventKind.Del && !exists)
        {
            mgr.Send(BuildEventChangeAck((byte)EventResult.Success, type, ev));
            return;
        }

        if (type != (byte)EventKind.Del)
        {
            long nowSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (ev.EndDate <= nowSec || ev.EndDate <= ev.StartDate)
            {
                mgr.Send(BuildEventChangeAck((byte)EventResult.InvalidTime, type, ev));
                return;
            }

            // Overlap arbitration against same-id events (C++ time-window + daily-window checks).
            var tStart = FromUnix(ev.StartDate); var tEnd = FromUnix(ev.EndDate);
            int startMin = tStart.Hour * 60 + tStart.Minute, endMin = tEnd.Hour * 60 + tEnd.Minute;
            foreach (var old in _state.Events.Values)
            {
                if (type == (byte)EventKind.Update && old.Index == ev.Index) continue;
                if (old.Id != ev.Id) continue;
                bool dateOverlap =
                    (old.StartDate <= ev.StartDate && old.EndDate >= ev.StartDate) ||
                    (old.StartDate <= ev.EndDate && old.EndDate >= ev.EndDate) ||
                    (ev.StartDate <= old.StartDate && ev.EndDate >= old.StartDate) ||
                    (ev.StartDate <= old.EndDate && ev.EndDate >= old.EndDate);
                if (!dateOverlap) continue;
                if (old.PartTime == 0 && ev.PartTime == 0)
                {
                    var os = FromUnix(old.StartDate); var oe = FromUnix(old.EndDate);
                    int oStart = os.Hour * 60 + os.Minute, oEnd = oe.Hour * 60 + oe.Minute;
                    bool timeOverlap =
                        (oStart <= startMin && oEnd >= startMin) ||
                        (oStart <= endMin && oEnd >= endMin) ||
                        (startMin <= oStart && endMin >= oStart) ||
                        (startMin <= oEnd && endMin >= oEnd);
                    if (timeOverlap) { mgr.Send(BuildEventChangeAck((byte)EventResult.InvalidTime, type, ev)); return; }
                }
                else { mgr.Send(BuildEventChangeAck((byte)EventResult.InvalidTime, type, ev)); return; }
            }
        }

        if (ev.Index == 0 && type == (byte)EventKind.Add) ev.Index = ++_state.EventIndex;

        byte ret = (byte)EventResult.Fail;
        if (_db is not null)
        {
            try
            {
                int rc = await _db.EventUpdateAsync(ev.Index, ev.Id, type, ev.Title, ev.GroupId, ev.SvrType, ev.SvrId,
                    FromUnix(ev.StartDate), FromUnix(ev.EndDate), ev.Value, ev.MapId, ev.StartAlarm, ev.EndAlarm,
                    ev.PartTime, ev.StartMsg, "", ev.EndMsg, ev.MakeStrValue(), CancellationToken.None);
                ret = (byte)rc;
            }
            catch (Exception ex) { _log.LogWarning(ex, "TEventUpdate failed."); }
        }
        else ret = (byte)EventResult.Success; // DB-less: accept in-memory.

        mgr.Send(BuildEventChangeAck(ret, type, ev));
        if (ret != 0) return; // non-zero result = failure; leave state unchanged.

        // If the (existing) event was running and is not a one-shot, turn it off on the affected servers.
        if (exists && _state.Events.TryGetValue(ev.Index, out var running) && running.State != 0
            && ev.Id != (byte)EventType.Lottery && ev.Id != (byte)EventType.GiftTime)
        {
            foreach (var svc in MatchingServers(ev))
            {
                if (ev.Id == (byte)EventType.CashSale) svc.Conn!.Send(BuildCashItemSale(ev, 0));
                else svc.Conn!.Send(BuildEventUpdate(ev, 0));
            }
        }

        _state.Events.Remove(ev.Index);
        if (type != (byte)EventKind.Del) _state.Events[ev.Index] = ev;
    }

    /// <summary>Send the full event list to the manager. C++ OnCT_EVENTLIST_REQ.</summary>
    private void OnCT_EVENTLIST_REQ(ManagerSession mgr)
    {
        if (!CheckAuthority(mgr, ManagerClass.User)) return;
        var w = new PacketWriter(Msg.CT_EVENTLIST_ACK).WriteUInt16((ushort)_state.Events.Count);
        foreach (var e in _state.Events.Values) e.Write(w);
        mgr.Send(w.ToArray());
    }

    /// <summary>Send the sellable cash-item list (loaded at startup). C++ OnCT_CASHITEMLIST_REQ.</summary>
    private void OnCT_CASHITEMLIST_REQ(ManagerSession mgr)
    {
        var w = new PacketWriter(Msg.CT_CASHITEMLIST_ACK).WriteUInt16((ushort)_state.CashItems.Count);
        foreach (var c in _state.CashItems) { w.WriteUInt16(c.Id); w.WriteString(c.Name); }
        mgr.Send(w.ToArray());
    }

    // ===== scheduler =====

    /// <summary>Push all currently-running events to a freshly-connected server. C++ SendEventToNewConnect.</summary>
    private void SendEventToNewConnect(ServiceInstance svc)
    {
        byte t = svc.SvrType.Type;
        if (t != Proto.SVRGRP_LOGINSVR && t != Proto.SVRGRP_MAPSVR && t != Proto.SVRGRP_WORLDSVR) return;
        foreach (var e in _state.Events.Values)
        {
            if (e.State == 0) continue;
            if (e.SvrType != t) continue;
            if (e.GroupId != 0 && svc.Group.GroupId != e.GroupId) continue;
            if (e.SvrId != 0 && svc.ServerId != e.SvrId) continue;
            svc.Conn!.Send(e.Id == (byte)EventType.CashSale ? BuildCashItemSale(e, e.Value) : BuildEventUpdate(e, e.Value));
        }
    }

    /// <summary>C++ CheckEvent — the 1-second event-lifecycle machine (start/end alarms + value push + expiry).</summary>
    private async Task CheckEventAsync()
    {
        DateTime now = DateTime.Now;
        long nowSec = DateTimeOffset.Now.ToUnixTimeSeconds();
        int curMinOfDay = now.Hour * 60 + now.Minute + 1440;

        foreach (var e in _state.Events.Values.ToList())
        {
            if (e.EndDate < nowSec && e.State == 0) { await DeleteEventAsync(e.Index); continue; }

            byte bTmp = 0;
            ushort wValue = 0;
            var tStart = FromUnix(e.StartDate);
            var tEnd = FromUnix(e.EndDate);
            var tEventStart = tStart.AddMinutes(-(double)e.StartAlarm);
            if (nowSec < ToUnix(tEventStart)) continue;

            if (e.PartTime == 0) // daily
            {
                int dwStartTime = tStart.Hour * 60 + tStart.Minute + 1440;
                int dwEndTime = tEnd.Hour * 60 + tEnd.Minute + 1440;
                int dwStartAlarmTime = dwStartTime - (int)e.StartAlarm;
                int dwEndAlarmTime = dwEndTime - (int)e.EndAlarm;
                if (curMinOfDay < dwStartAlarmTime) continue;

                if (e.State == 0)
                {
                    if (!e.StartAlarmFired && dwStartAlarmTime <= curMinOfDay && curMinOfDay < dwEndTime) { bTmp = 1; e.StartAlarmFired = true; }
                    else if (dwStartTime <= curMinOfDay && curMinOfDay < dwEndTime) { bTmp = 3; wValue = e.Value; e.State = 1; }
                }
                else if (e.State == 1)
                {
                    if (!e.EndAlarmFired && dwEndAlarmTime <= curMinOfDay && curMinOfDay <= dwEndTime) { bTmp = 2; e.EndAlarmFired = true; }
                    else if (dwEndTime <= curMinOfDay) { bTmp = 4; wValue = 0; e.State = 0; e.StartAlarmFired = false; e.EndAlarmFired = false; }
                }
            }
            else // term
            {
                if (e.State == 0)
                {
                    if (!e.StartAlarmFired && ToUnix(tEventStart) <= nowSec && nowSec < e.EndDate) { bTmp = 1; e.StartAlarmFired = true; }
                    else if (e.StartDate <= nowSec && nowSec < e.EndDate) { bTmp = 3; wValue = e.Value; e.State = 1; }
                }
                else if (e.State == 1)
                {
                    var tEventEnd = tEnd.AddMinutes(-(double)e.EndAlarm);
                    if (!e.EndAlarmFired && ToUnix(tEventEnd) <= nowSec && nowSec <= e.EndDate) { bTmp = 2; e.EndAlarmFired = true; }
                    else if (e.EndDate <= nowSec) { bTmp = 4; wValue = 0; e.State = 0; e.StartAlarmFired = false; e.EndAlarmFired = false; }
                }
            }

            switch (bTmp)
            {
                case 1:
                case 2:
                    SendEventMsg(e, (byte)(bTmp - 1));
                    if (e.Id == (byte)EventType.CashSale) SendCashShopStop(e);
                    break;
                case 3:
                case 4:
                    if (e.Id == (byte)EventType.CashSale)
                        foreach (var svc in MatchingServers(e)) svc.Conn!.Send(BuildCashItemSale(e, wValue));
                    else
                        foreach (var svc in MatchingServers(e)) svc.Conn!.Send(BuildEventUpdate(e, wValue));
                    if (e.Id == (byte)EventType.Lottery || e.Id == (byte)EventType.GiftTime)
                        await DeleteEventAsync(e.Index);
                    break;
            }
        }
    }

    // ===== fan helpers (internal, C++ OnCT_EVENTMSG_REQ / CASHSHOPSTOP / EVENTUPDATE / CASHITEMSALE / EVENTDEL) =====

    /// <summary>Servers matching an event's (type, group, serverId) filter. 0 group/serverId = all.</summary>
    private IEnumerable<ServiceInstance> MatchingServers(EventInfo e) =>
        _state.Services.Values.Where(s => s.Conn is not null && s.SvrType.Type == e.SvrType
            && (e.GroupId == 0 || s.Group.GroupId == e.GroupId)
            && (e.SvrId == 0 || s.ServerId == e.SvrId));

    /// <summary>C++ OnCT_EVENTMSG_REQ — event start/end message (relay-preferred, then map/world per group).</summary>
    private void SendEventMsg(EventInfo e, byte msgType)
    {
        string msg = msgType == 0 ? e.StartMsg : e.EndMsg;
        byte[] Build() => new PacketWriter(Msg.CT_EVENTMSG_REQ).WriteByte(e.Id).WriteByte(msgType).WriteString(msg).ToArray();

        var groups = new List<byte>();
        if (e.GroupId == 0 && e.SvrType == Proto.SVRGRP_WORLDSVR && e.SvrId == 0)
        {
            foreach (var relay in _state.Services.Values.Where(s => s.IsRelay))
                if (relay.Conn is { } c) c.Send(Build()); else groups.Add(relay.Group.GroupId);
        }
        else groups.Add(e.GroupId);

        foreach (byte g in groups)
            foreach (var svc in _state.Services.Values.Where(s => s.Conn is not null && s.SvrType.Type == e.SvrType && s.Group.GroupId == g))
                svc.Conn!.Send(Build());
    }

    /// <summary>C++ OnCT_CASHSHOPSTOP_REQ — always sends type=1 (stop) to matching servers.</summary>
    private void SendCashShopStop(EventInfo e)
    {
        foreach (var svc in MatchingServers(e))
            svc.Conn!.Send(new PacketWriter(Msg.CT_CASHSHOPSTOP_REQ).WriteByte(1).ToArray());
    }

    /// <summary>C++ SendCT_CASHITEMSALE_REQ body.</summary>
    private static byte[] BuildCashItemSale(EventInfo e, ushort value)
    {
        ushort v = Math.Min(value, (ushort)100);
        var w = new PacketWriter(Msg.CT_CASHITEMSALE_REQ).WriteUInt32(e.Index).WriteUInt16(v).WriteUInt16((ushort)e.CashItems.Count);
        foreach (var c in e.CashItems) { w.WriteUInt16(c.Id); w.WriteByte(c.SaleValue); }
        return w.ToArray();
    }

    /// <summary>C++ SendCT_EVENTUPDATE_REQ body: bEventID, wValue, then the full EventInfo (WrapPacketIn).</summary>
    private static byte[] BuildEventUpdate(EventInfo e, ushort value)
    {
        var w = new PacketWriter(Msg.CT_EVENTUPDATE_REQ).WriteByte(e.Id).WriteUInt16(value);
        e.Write(w);
        return w.ToArray();
    }

    /// <summary>C++ OnCT_EVENTDEL_REQ — persist EK_DEL, then drop from the live map on success.</summary>
    private async Task DeleteEventAsync(uint index)
    {
        if (!_state.Events.TryGetValue(index, out var e)) return;
        byte ret = 0;
        if (_db is not null)
        {
            try
            {
                ret = (byte)await _db.EventUpdateAsync(index, e.Id, (byte)EventKind.Del, e.Title, e.GroupId, e.SvrType, e.SvrId,
                    FromUnix(e.StartDate), FromUnix(e.EndDate), e.Value, e.MapId, e.StartAlarm, e.EndAlarm,
                    e.PartTime, e.StartMsg, "", e.EndMsg, "", CancellationToken.None);
            }
            catch (Exception ex) { _log.LogWarning(ex, "TEventUpdate(EK_DEL) failed."); ret = 1; }
        }
        if (ret == 0) _state.Events.Remove(index);
    }

    // ===== builders =====

    private static byte[] BuildEventChangeAck(byte ret, byte type, EventInfo e)
    {
        var w = new PacketWriter(Msg.CT_EVENTCHANGE_ACK).WriteByte(ret).WriteByte(type);
        e.Write(w);
        return w.ToArray();
    }
}
