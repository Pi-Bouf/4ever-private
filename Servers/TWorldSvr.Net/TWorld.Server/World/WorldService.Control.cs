using TWorld.Protocol;
using TWorld.Server.Net;

namespace TWorld.Server.World;

/// <summary>
/// CT control plane — the self-contained TControlSvr↔world admin/monitoring handlers, ported from
/// <c>SSHandler.cpp</c>. These are GM/operations commands: live-count monitoring, GM teleport/position
/// queries, chat bans, system messages, a castle owner override, server-wide broadcasts (event message,
/// cash-shop stop, help message), and RPS-config / gift-catalog read+change. Each either replies to the
/// control server, relays to the target char's map, or broadcasts to every map.
///
/// The control server registers via <c>CT_CTRLSVR_REQ</c> (handled in the main switch, sets
/// <see cref="WorldState.ControlServer"/>). Handlers that need the un-ported event/lottery system, the
/// cash-sale catalog, the DM/DB-job plane, or tournament-event persistence are deferred (see PORT_STATUS).
/// </summary>
public sealed partial class WorldService
{
    private bool DispatchControl(ServerSession session, PacketReader r, byte[] packet)
    {
        switch (r.Id)
        {
            case Msg.CT_SERVICEMONITOR_ACK: OnCT_SERVICEMONITOR_ACK(session, r); return true;
            case Msg.CT_USERMOVE_ACK: OnCT_USERMOVE_ACK(r); return true;
            case Msg.CT_USERPOSITION_ACK: OnCT_USERPOSITION_ACK(r); return true;
            case Msg.CT_CHATBAN_REQ: OnCT_CHATBAN_REQ(session, r); return true;
            case Msg.CT_CHARMSG_ACK: OnCT_CHARMSG_ACK(r); return true;
            case Msg.CT_SERVICEDATACLEAR_ACK: OnCT_SERVICEDATACLEAR_ACK(); return true;
            case Msg.CT_CASTLEGUILDCHG_REQ: OnCT_CASTLEGUILDCHG_REQ(session, r); return true;
            case Msg.CT_EVENTMSG_REQ: OnCT_EVENTMSG_REQ(r); return true;
            case Msg.CT_CASHSHOPSTOP_REQ: OnCT_CASHSHOPSTOP_REQ(r); return true;
            case Msg.CT_HELPMESSAGE_REQ: OnCT_HELPMESSAGE_REQ(r); return true;
            case Msg.CT_RPSGAMEDATA_REQ: OnCT_RPSGAMEDATA_REQ(session, r); return true;
            case Msg.CT_RPSGAMECHANGE_REQ: OnCT_RPSGAMECHANGE_REQ(session, r, packet); return true;
            case Msg.CT_CMGIFTLIST_REQ: OnCT_CMGIFTLIST_REQ(r); return true;
            case Msg.CT_CASHITEMSALE_REQ: OnCT_CASHITEMSALE_REQ(r); return true;
        }
        return false;
    }

    /// <summary>Reply with live session / character / active-user counts. C++ OnCT_SERVICEMONITOR_ACK.</summary>
    private void OnCT_SERVICEMONITOR_ACK(ServerSession session, PacketReader r)
    {
        uint tick = r.ReadUInt32();
        var w = new PacketWriter(Msg.CT_SERVICEMONITOR_REQ);
        w.WriteUInt32(tick);
        w.WriteUInt32((uint)_state.Servers.Count);
        w.WriteUInt32((uint)_state.Characters.Count);
        w.WriteUInt32((uint)_state.ActiveUsers.Count);
        session.Send(w.ToArray());
    }

    /// <summary>GM teleport: move a named user. Forwarded to the char's map as CT_USERMOVE_ACK. C++ OnCT_USERMOVE_ACK.</summary>
    private void OnCT_USERMOVE_ACK(PacketReader r)
    {
        string user = r.ReadString();
        byte channel = r.ReadByte();
        ushort mapId = r.ReadUInt16();
        float px = r.ReadFloat(), py = r.ReadFloat(), pz = r.ReadFloat();
        ushort partyId = r.ReadUInt16();

        if (!_state.CharactersByName.TryGetValue(user, out var ch)) return;
        if (_state.FindMapSvr(ch.MainId) is not { } map) return;
        var w = new PacketWriter(Msg.CT_USERMOVE_ACK);
        w.WriteUInt32(ch.CharId); w.WriteUInt32(ch.Key); w.WriteByte(channel); w.WriteUInt16(mapId);
        w.WriteFloat(px); w.WriteFloat(py); w.WriteFloat(pz); w.WriteUInt16(partyId);
        map.Send(w.ToArray());
    }

    /// <summary>GM position query: ask the target's map to report the target's position to the GM. C++ OnCT_USERPOSITION_ACK.</summary>
    private void OnCT_USERPOSITION_ACK(PacketReader r)
    {
        string targetName = r.ReadString();
        string gmName = r.ReadString();
        if (!_state.CharactersByName.TryGetValue(targetName, out var target)) return;
        if (!_state.CharactersByName.ContainsKey(gmName)) return;
        if (_state.FindMapSvr(target.MainId) is not { } map) return;
        var w = new PacketWriter(Msg.MW_USERPOSITION_REQ);
        w.WriteUInt32(target.CharId); w.WriteUInt32(target.Key); w.WriteString(gmName);
        map.Send(w.ToArray());
    }

    /// <summary>Chat-ban a char for a number of minutes; apply on the char's map and record the ban. C++ OnCT_CHATBAN_REQ.</summary>
    private void OnCT_CHATBAN_REQ(ServerSession session, PacketReader r)
    {
        string name = r.ReadString();
        ushort minutes = r.ReadUInt16();
        uint banSeq = r.ReadUInt32();
        uint managerId = r.ReadUInt32();

        var target = _state.CharactersByName.TryGetValue(name, out var t) ? t : null;
        if (target is null)
        {
            session.Send(BuildChatBanAck(false, banSeq, managerId));
            return;
        }

        long banUntil = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + minutes * 60;
        target.ChatBanTime = banUntil;

        if (_state.FindMapSvr(target.MainId) is { } map)
        {
            var w = new PacketWriter(Msg.MW_CHATBAN_REQ);
            w.WriteString(name); w.WriteInt64(banUntil); w.WriteByte(0 /* CHATBAN_SUCCESS */); w.WriteUInt32(0); w.WriteUInt32(0);
            map.Send(w.ToArray());
        }
        _state.ChatBans[name] = banUntil;
        session.Send(BuildChatBanAck(true, banSeq, managerId));
    }

    /// <summary>Send a system message to a named char (relayed to the char's map). C++ OnCT_CHARMSG_ACK.</summary>
    private void OnCT_CHARMSG_ACK(PacketReader r)
    {
        string name = r.ReadString();
        string msg = r.ReadString();
        if (!_state.CharactersByName.TryGetValue(name, out var ch)) return;
        if (msg.Length > 1024) msg = msg[..1024];
        if (_state.FindMapSvr(ch.MainId) is not { } map) return;
        var w = new PacketWriter(Msg.MW_CHARMSG_REQ);
        w.WriteString(name); w.WriteString(msg);
        map.Send(w.ToArray());
    }

    /// <summary>Rebuild the active-user set from the live character table. C++ OnCT_SERVICEDATACLEAR_ACK.</summary>
    private void OnCT_SERVICEDATACLEAR_ACK()
    {
        _state.ActiveUsers.Clear();
        foreach (var ch in _state.Characters.Values) _state.ActiveUsers.Add(ch.UserId);
    }

    /// <summary>Force a castle's defending/attacking guilds (GM override). Broadcast to every map. C++ OnCT_CASTLEGUILDCHG_REQ.</summary>
    private void OnCT_CASTLEGUILDCHG_REQ(ServerSession session, PacketReader r)
    {
        ushort castle = r.ReadUInt16();
        uint defGuildId = r.ReadUInt32();
        uint atkGuildId = r.ReadUInt32();
        uint managerId = r.ReadUInt32();
        long time = r.ReadInt64();

        var def = _state.FindGuild(defGuildId);
        var atk = _state.FindGuild(atkGuildId);
        if (def is null || atk is null || string.IsNullOrEmpty(def.Name) || string.IsNullOrEmpty(atk.Name))
        {
            session.Send(BuildCastleGuildChgAck(managerId, false, 0, 0, "", 0, "", 0));
            return;
        }

        var b = new PacketWriter(Msg.MW_CASTLEGUILDCHG_REQ);
        b.WriteUInt16(castle); b.WriteUInt32(defGuildId); b.WriteString(def.Name);
        b.WriteUInt32(atkGuildId); b.WriteString(atk.Name); b.WriteInt64(time);
        BroadcastServers(b.ToArray());

        session.Send(BuildCastleGuildChgAck(managerId, true, castle, defGuildId, def.Name, atkGuildId, atk.Name, time));
    }

    /// <summary>Broadcast an event message to every map. C++ OnCT_EVENTMSG_REQ.</summary>
    private void OnCT_EVENTMSG_REQ(PacketReader r)
    {
        byte eventId = r.ReadByte();
        byte msgType = r.ReadByte();
        string msg = r.ReadString();
        var w = new PacketWriter(Msg.MW_EVENTMSG_REQ);
        w.WriteByte(eventId); w.WriteByte(msgType); w.WriteString(msg);
        BroadcastServers(w.ToArray());
    }

    /// <summary>Stop/resume the cash shop on every map. C++ OnCT_CASHSHOPSTOP_REQ.</summary>
    private void OnCT_CASHSHOPSTOP_REQ(PacketReader r)
    {
        byte type = r.ReadByte();
        var w = new PacketWriter(Msg.MW_CASHSHOPSTOP_REQ);
        w.WriteByte(type); w.WriteByte(0 /* bSendPlayer default */);
        BroadcastServers(w.ToArray());
    }

    /// <summary>Broadcast a scheduled help message to every map. C++ OnCT_HELPMESSAGE_REQ (DB persist deferred).</summary>
    private void OnCT_HELPMESSAGE_REQ(PacketReader r)
    {
        byte id = r.ReadByte();
        long start = r.ReadInt64();
        long end = r.ReadInt64();
        string msg = r.ReadString();
        var w = new PacketWriter(Msg.MW_HELPMESSAGE_REQ);
        w.WriteByte(id); w.WriteInt64(start); w.WriteInt64(end); w.WriteString(msg);
        BroadcastServers(w.ToArray());
        _ = PersistGame(() => _gameDb!.HelpMessageAsync(id, start, end, msg), "THelpMessage"); // C++ SendDM_HELPMESSAGE_REQ
    }

    /// <summary>Read the current RPS config. C++ OnCT_RPSGAMEDATA_REQ.</summary>
    private void OnCT_RPSGAMEDATA_REQ(ServerSession session, PacketReader r)
    {
        byte group = r.ReadByte();
        session.Send(BuildRpsGameData(false, group));
    }

    /// <summary>Change RPS config (probabilities / win-keep / period), reply with the new config, and push it
    /// to every map. C++ OnCT_RPSGAMECHANGE_REQ.</summary>
    private void OnCT_RPSGAMECHANGE_REQ(ServerSession session, PacketReader r, byte[] packet)
    {
        byte group = r.ReadByte();
        ushort count = r.ReadUInt16();
        for (ushort i = 0; i < count; i++)
        {
            byte type = r.ReadByte();
            byte winCount = r.ReadByte();
            byte winProb = r.ReadByte(), drawProb = r.ReadByte(), loseProb = r.ReadByte();
            ushort winKeep = r.ReadUInt16();
            ushort winPeriod = r.ReadUInt16();

            if (_state.RpsGames.TryGetValue((ushort)(type | (winCount << 8)), out var rps))
            {
                rps.Prob[0] = winProb; rps.Prob[1] = drawProb; rps.Prob[2] = loseProb;
                rps.WinKeep = winKeep; rps.WinPeriod = winPeriod;
            }
        }

        if (count != 0)
        {
            session.Send(BuildRpsGameData(true, group));
            BroadcastServers(Reframe(Msg.MW_RPSGAMECHANGE_REQ, packet));
        }
    }

    /// <summary>Send the gift catalog to the control server. C++ OnCT_CMGIFTLIST_REQ.</summary>
    private void OnCT_CMGIFTLIST_REQ(PacketReader r)
    {
        uint managerId = r.ReadUInt32();
        if (_state.ControlServer is not { } ctrl) return;

        var w = new PacketWriter(Msg.CT_CMGIFTLIST_ACK);
        w.WriteUInt32(managerId);
        w.WriteUInt16((ushort)_state.CmGifts.Count);
        foreach (var g in _state.CmGifts.Values.OrderBy(g => g.GiftId))
        {
            w.WriteUInt16(g.GiftId); w.WriteByte(g.GiftType); w.WriteUInt32(g.Value); w.WriteByte(g.Count);
            w.WriteByte(g.TakeType); w.WriteByte(g.MaxTakeCount); w.WriteByte(g.ToolOnly); w.WriteUInt16(g.ErrGiftId);
            w.WriteString(g.Title); w.WriteString(g.Msg);
        }
        ctrl.Send(w.ToArray());
    }

    /// <summary>Push (or clear, when value==0) a cash-item sale. The event is recorded in the catalog, every
    /// map's confirm flag is reset, and the sale is fanned to all maps; the maps then confirm via
    /// <c>MW_CASHITEMSALE_ACK</c>, after which it persists (DB write deferred). C++ OnCT_CASHITEMSALE_REQ.</summary>
    private void OnCT_CASHITEMSALE_REQ(PacketReader r)
    {
        uint index = r.ReadUInt32();
        ushort value = r.ReadUInt16();
        ushort count = r.ReadUInt16();

        var ev = new CashItemSaleEvent { Index = index, Value = value };
        for (ushort i = 0; i < count; i++)
            ev.Items.Add(new CashItemSale { Id = r.ReadUInt16(), SaleValue = r.ReadByte() });

        List<CashItemSale>? saleItems;
        if (value == 0)
        {
            // Clearing the sale: zero the stored event's values and re-broadcast them.
            if (_state.CashItemSales.TryGetValue(index, out var existing))
            {
                existing.Value = 0;
                foreach (var it in existing.Items) it.SaleValue = 0;
                saleItems = existing.Items;
            }
            else saleItems = null;
        }
        else
        {
            _state.CashItemSales[index] = ev;
            saleItems = ev.Items;
        }

        if (saleItems is null)
        {
            _log.LogWarning("CT_CASHITEMSALE clear for unknown index {Index}.", index);
            return;
        }

        foreach (var s in _state.Servers.Values) s.CashSale = false;
        var pkt = BuildCashItemSaleReq(index, value, saleItems);
        BroadcastServers(pkt);
    }

    private static byte[] BuildCashItemSaleReq(uint index, ushort value, List<CashItemSale> items)
    {
        var w = new PacketWriter(Msg.MW_CASHITEMSALE_REQ);
        w.WriteUInt32(index); w.WriteUInt16(value); w.WriteUInt16((ushort)items.Count);
        foreach (var it in items) { w.WriteUInt16(it.Id); w.WriteByte(it.SaleValue); }
        return w.ToArray();
    }

    // ===== builders =====

    private static byte[] BuildChatBanAck(bool ret, uint banSeq, uint managerId)
    {
        var w = new PacketWriter(Msg.CT_CHATBAN_ACK);
        w.WriteBool(ret); w.WriteUInt32(banSeq); w.WriteUInt32(managerId);
        return w.ToArray();
    }

    private static byte[] BuildCastleGuildChgAck(uint managerId, bool ret, ushort castle,
        uint defGuildId, string defName, uint atkGuildId, string atkName, long time)
    {
        var w = new PacketWriter(Msg.CT_CASTLEGUILDCHG_ACK);
        w.WriteUInt32(managerId); w.WriteBool(ret); w.WriteUInt16(castle);
        w.WriteUInt32(defGuildId); w.WriteString(defName); w.WriteUInt32(atkGuildId); w.WriteString(atkName); w.WriteInt64(time);
        return w.ToArray();
    }

    /// <summary>Serialize the RPS chart in ascending MAKEWORD(type,winCount) order (std::map order). C++ SendCT_RPSGAMEDATA_ACK.</summary>
    private byte[] BuildRpsGameData(bool change, byte group)
    {
        var w = new PacketWriter(Msg.CT_RPSGAMEDATA_ACK);
        w.WriteBool(change); w.WriteByte(group); w.WriteUInt16((ushort)_state.RpsGames.Count);
        foreach (var rps in _state.RpsGames.Values.OrderBy(x => (ushort)(x.Type | (x.WinCount << 8))))
        {
            w.WriteByte(rps.Type); w.WriteByte(rps.WinCount);
            w.WriteByte(rps.Prob[0]); w.WriteByte(rps.Prob[1]); w.WriteByte(rps.Prob[2]);
            w.WriteUInt16(rps.WinKeep); w.WriteUInt16(rps.WinPeriod);
        }
        return w.ToArray();
    }
}
