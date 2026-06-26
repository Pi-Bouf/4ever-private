using TWorld.Data;
using TWorld.Protocol;
using TWorld.Server.Net;

namespace TWorld.Server.World;

/// <summary>
/// CT control-plane handlers that hit the database. In the C++ server these forwarded to the separate DM
/// (DB-job) worker thread (<c>OnCT_* → SendDM_*_REQ → OnDM_*_REQ → SayToBATCH(*_ACK) → OnDM_*_ACK</c>);
/// this port follows the established Phase-2 simplification and folds that whole round trip into a single
/// inline best-effort call on the batch task. Covers the cash-mall gift catalog feed + GM gift
/// (CT_CMGIFTCHARTUPDATE / CT_CMGIFT) and the item-admin queries (CT_ITEMFIND / CT_ITEMSTATE).
/// </summary>
public sealed partial class WorldService
{
    private async Task<bool> DispatchControlDbAsync(ServerSession session, PacketReader r, byte[] packet)
    {
        switch (r.Id)
        {
            case Msg.CT_CMGIFT_REQ: await OnCT_CMGIFT_REQ(r); return true;
            case Msg.CT_CMGIFTCHARTUPDATE_REQ: await OnCT_CMGIFTCHARTUPDATE_REQ(r); return true;
            case Msg.CT_ITEMFIND_REQ: await OnCT_ITEMFIND_REQ(r); return true;
            case Msg.CT_ITEMSTATE_REQ: await OnCT_ITEMSTATE_REQ(r); return true;
            case Msg.CT_EVENTQUARTERLIST_REQ: await OnCT_EVENTQUARTERLIST_REQ(r); return true;
            case Msg.CT_EVENTQUARTERUPDATE_REQ: await OnCT_EVENTQUARTERUPDATE_REQ(r); return true;
        }
        return false;
    }

    /// <summary>GM tool sends a cash-mall gift to a named char; a gift with TakeType!=0 runs the DB take-check
    /// inline. C++ OnCT_CMGIFT_REQ → (DB take-check) → OnDM_CMGIFT_ACK.</summary>
    private async Task OnCT_CMGIFT_REQ(PacketReader r)
    {
        string target = r.ReadString();
        ushort giftId = r.ReadUInt16();
        uint managerId = r.ReadUInt32();

        if (_state.CmGifts.TryGetValue(giftId, out var gift))
        {
            byte ret = gift.TakeType != 0 ? await CmGiftTakeCheckAsync(target, giftId) : (byte)CmGiftResult.Success;
            RouteCmGift(ret, target, giftId, tool: 1, managerId);
            return;
        }
        RouteCmGift((byte)CmGiftResult.Id, target, giftId, tool: 1, managerId);
    }

    /// <summary>The control server feeds a batch of gift-catalog edits (add/update/del). Each op is persisted
    /// (the DB assigns the id on add) and applied to the live catalog; the updated catalog is then returned to
    /// the control server via CT_CMGIFTLIST_ACK. Collapses C++ OnCT_CMGIFTCHARTUPDATE_REQ → DM round trip →
    /// OnDM_CMGIFTCHARTUPDATE_ACK.</summary>
    private async Task OnCT_CMGIFTCHARTUPDATE_REQ(PacketReader r)
    {
        ushort count = r.ReadUInt16();
        for (ushort i = 0; i < count; i++)
        {
            var type = (CmGiftUpdate)r.ReadByte();
            switch (type)
            {
                case CmGiftUpdate.Add:
                {
                    _ = r.ReadUInt16(); // wGiftID placeholder (the DB assigns the real id)
                    byte giftType = r.ReadByte();
                    uint value = r.ReadUInt32();
                    byte cnt = r.ReadByte();
                    byte takeType = r.ReadByte();
                    byte maxTake = r.ReadByte();
                    byte toolOnly = r.ReadByte();
                    ushort errGiftId = r.ReadUInt16();
                    string title = r.ReadString();
                    string msg = r.ReadString();

                    ushort newId;
                    if (_gameDb is not null)
                    {
                        try { newId = await _gameDb.CmGiftAddAsync(giftType, value, cnt, takeType, maxTake, toolOnly, errGiftId, title, msg); }
                        catch (Exception ex) { _log.LogWarning(ex, "TCMGiftAdd failed."); break; }
                    }
                    else newId = ++_state.CmGiftSeq;

                    _state.CmGifts[newId] = new CmGift
                    {
                        GiftId = newId, GiftType = giftType, Value = value, Count = cnt, TakeType = takeType,
                        MaxTakeCount = maxTake, ToolOnly = toolOnly, ErrGiftId = errGiftId, Title = title, Msg = msg,
                    };
                    break;
                }
                case CmGiftUpdate.Update:
                {
                    ushort giftId = r.ReadUInt16();
                    byte giftType = r.ReadByte();
                    uint value = r.ReadUInt32();
                    byte cnt = r.ReadByte();
                    byte takeType = r.ReadByte();
                    byte maxTake = r.ReadByte();
                    byte toolOnly = r.ReadByte();
                    ushort errGiftId = r.ReadUInt16();
                    string title = r.ReadString();
                    string msg = r.ReadString();

                    if (_gameDb is not null)
                    {
                        try { await _gameDb.CmGiftSetAsync(giftId, giftType, value, cnt, takeType, maxTake, toolOnly, errGiftId, title, msg); }
                        catch (Exception ex) { _log.LogWarning(ex, "TCMGiftSet failed."); break; }
                    }

                    _state.CmGifts[giftId] = new CmGift
                    {
                        GiftId = giftId, GiftType = giftType, Value = value, Count = cnt, TakeType = takeType,
                        MaxTakeCount = maxTake, ToolOnly = toolOnly, ErrGiftId = errGiftId, Title = title, Msg = msg,
                    };
                    break;
                }
                case CmGiftUpdate.Del:
                {
                    ushort giftId = r.ReadUInt16();
                    if (_gameDb is not null)
                    {
                        try { await _gameDb.CmGiftDelAsync(giftId); }
                        catch (Exception ex) { _log.LogWarning(ex, "TCMGiftDel failed."); break; }
                    }
                    _state.CmGifts.Remove(giftId);
                    break;
                }
            }
        }
        uint managerId = r.ReadUInt32();
        SendCmGiftList(managerId);
    }

    /// <summary>GM item search: return TITEMCHART rows matching a name/id to the control server. Collapses C++
    /// OnCT_ITEMFIND_REQ → DM_ITEMFIND_REQ → OnDM_ITEMFIND_REQ (CTBLItemFind) → CT_ITEMFIND_ACK.</summary>
    private async Task OnCT_ITEMFIND_REQ(PacketReader r)
    {
        uint manager = r.ReadUInt32();
        ushort itemId = r.ReadUInt16();
        string name = r.ReadString();

        List<ItemFindRow> rows = new();
        if (_gameDb is not null)
        {
            try { rows = await _gameDb.ItemFindAsync(itemId, name); }
            catch (Exception ex) { _log.LogWarning(ex, "CTBLItemFind failed."); }
        }

        if (_state.ControlServer is not { } ctrl) return;
        var w = new PacketWriter(Msg.CT_ITEMFIND_ACK);
        w.WriteUInt16((ushort)rows.Count);
        w.WriteUInt32(manager);
        foreach (var it in rows) { w.WriteUInt16(it.ItemId); w.WriteByte(it.InitState); w.WriteString(it.Name); }
        ctrl.Send(w.ToArray());
    }

    /// <summary>GM item-state change: apply each (itemId, initState) via TItemStateChange, stopping at the first
    /// failure (mirrors the C++ break), then fan the applied set to every map (MW_ITEMSTATE_REQ) and back to the
    /// control server (CT_ITEMSTATE_ACK). Collapses C++ OnCT_ITEMSTATE_REQ → DM round trip → OnDM_ITEMSTATE_ACK.</summary>
    private async Task OnCT_ITEMSTATE_REQ(PacketReader r)
    {
        uint id = r.ReadUInt32();
        ushort count = r.ReadUInt16();
        var applied = new List<(ushort Id, byte State)>();
        for (ushort i = 0; i < count; i++)
        {
            ushort itemId = r.ReadUInt16();
            byte initState = r.ReadByte();
            if (_gameDb is not null)
            {
                int ret;
                try { ret = await _gameDb.ItemStateChangeAsync(itemId, initState); }
                catch (Exception ex) { _log.LogWarning(ex, "TItemStateChange failed."); break; }
                if (ret != 0) break;
            }
            applied.Add((itemId, initState));
        }

        // Body shared by both packets (C++ copies the DM_ITEMSTATE_ACK body and only re-IDs it): id, count, entries.
        byte[] Body(ushort msgId)
        {
            var w = new PacketWriter(msgId);
            w.WriteUInt32(id);
            w.WriteUInt16((ushort)applied.Count);
            foreach (var (itemId, state) in applied) { w.WriteUInt16(itemId); w.WriteByte(state); }
            return w.ToArray();
        }

        BroadcastServers(Body(Msg.MW_ITEMSTATE_REQ));
        _state.ControlServer?.Send(Body(Msg.CT_ITEMSTATE_ACK));
    }

    /// <summary>Serialize the current gift catalog to the control server (CT_CMGIFTLIST_ACK).</summary>
    private void SendCmGiftList(uint managerId)
    {
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
}
