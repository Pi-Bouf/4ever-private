using TWorld.Protocol;
using TWorld.Server.Net;

namespace TWorld.Server.World;

/// <summary>
/// Phase 5b — combat / progression / loot, ported from <c>SSHandler.cpp</c>/<c>SSSender.cpp</c>
/// (OnMW_LEVELUP/MONSTERDIE/TAKEMONMONEY/ADDITEM/ADDITEMRESULT/PARTYORDERTAKEITEM). Most of these route
/// an event reported by some map to the character's <em>authoritative main</em> server (which owns the
/// inventory/money) by re-emitting the same packet under its <c>_REQ</c> id. LEVELUP also fans the new
/// level out to the char's other connections and refreshes soulmate state; PARTYORDERTAKEITEM walks the
/// party's round-robin loot rotation and hands the drop to the next eligible member's main.
/// </summary>
public sealed partial class WorldService
{
    private bool DispatchCombat(ServerSession session, PacketReader r, byte[] packet)
    {
        switch (r.Id)
        {
            case Msg.MW_LEVELUP_ACK: OnMW_LEVELUP_ACK(r); return true;
            case Msg.MW_MONSTERDIE_ACK: ForwardToMain(r, Msg.MW_MONSTERDIE_REQ, packet); return true;
            case Msg.MW_TAKEMONMONEY_ACK: ForwardToMain(r, Msg.MW_TAKEMONMONEY_REQ, packet); return true;
            case Msg.MW_ADDITEM_ACK: OnMW_ADDITEM_ACK(session, r, packet); return true;
            case Msg.MW_ADDITEMRESULT_ACK: OnMW_ADDITEMRESULT_ACK(r); return true;
            case Msg.MW_PARTYORDERTAKEITEM_ACK: OnMW_PARTYORDERTAKEITEM_ACK(session, r); return true;
        }
        return false;
    }

    /// <summary>A char leveled up: record it, push the new level to the char's other connections, and refresh
    /// every soulmate link's level (then re-test the level-gap break). C++ OnMW_LEVELUP_ACK.</summary>
    private void OnMW_LEVELUP_ACK(PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        byte level = r.ReadByte();

        var ch = _state.FindChar(charId, key);
        if (ch is null) return;
        SetCharLevel(ch, level);

        foreach (var (sid, con) in ch.Connections.ToList())
        {
            if (!con.Valid || sid == ch.MainId) continue;
            var w = new PacketWriter(Msg.MW_LEVELUP_REQ);
            w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteByte(level);
            _state.FindMapSvr(sid)?.Send(w.ToArray());
        }

        foreach (var soul in ch.Soulmates.Values)
        {
            Character? sc = null; Soulmate? ss = null;
            if (soul.CharId == ch.CharId) { sc = ch; ss = soul; }
            else if (_state.Characters.TryGetValue(soul.CharId, out var partner))
            {
                sc = partner;
                if (partner.Soulmates.TryGetValue(partner.CharId, out var ps)) { ss = ps; ps.Level = level; }
            }
            CheckSoulmateEnd(sc, ss);
        }
    }

    /// <summary>Add a looted item to the char: route to the authoritative main, or tell the reporting server
    /// the char is gone (ADDITEMRESULT NOTFOUND). C++ OnMW_ADDITEM_ACK.</summary>
    private void OnMW_ADDITEM_ACK(ServerSession session, PacketReader r, byte[] packet)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        _ = r.ReadByte();                 // serverId
        byte channel = r.ReadByte();
        ushort mapId = r.ReadUInt16();
        uint monId = r.ReadUInt32();
        _ = r.ReadByte();                 // inven
        _ = r.ReadByte();                 // slot
        byte itemId = r.ReadByte();

        var ch = _state.FindChar(charId, key);
        if (ch is null)
        {
            session.Send(BuildAddItemResult(charId, key, channel, mapId, monId, itemId, MonItemTake.NotFound));
            return;
        }
        _state.FindMapSvr(ch.MainId)?.Send(Reframe(Msg.MW_ADDITEM_REQ, packet));
    }

    /// <summary>Relay a loot result back to the server that originally reported the kill. C++ OnMW_ADDITEMRESULT_ACK.</summary>
    private void OnMW_ADDITEMRESULT_ACK(PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        byte mapSvrId = r.ReadByte();
        byte channel = r.ReadByte();
        ushort mapId = r.ReadUInt16();
        uint monId = r.ReadUInt32();
        byte itemId = r.ReadByte();
        byte result = r.ReadByte();

        _state.FindMapSvr(mapSvrId)?.Send(BuildAddItemResult(charId, key, channel, mapId, monId, itemId, (MonItemTake)result));
    }

    /// <summary>Ordered-loot drop: pick the next member in the party's round-robin rotation and forward the
    /// item (carried verbatim) to that member's main. C++ OnMW_PARTYORDERTAKEITEM_ACK.</summary>
    private void OnMW_PARTYORDERTAKEITEM_ACK(ServerSession session, PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        ushort partyId = r.ReadUInt16();
        byte serverId = r.ReadByte();
        byte channel = r.ReadByte();
        ushort mapId = r.ReadUInt16();
        uint monId = r.ReadUInt32();
        ushort tempMonId = r.ReadUInt16();
        byte cnt = r.ReadByte();

        var eligible = new List<uint>(cnt);
        for (byte i = 0; i < cnt; i++) eligible.Add(r.ReadUInt32());
        // The remainder of the packet is the wrapped item; we forward it verbatim (no item-format port).
        byte[] item = r.ReadRemaining().ToArray();

        var party = _state.FindParty(partyId);
        if (party is null)
        {
            // No party to distribute to -> NOTFOUND back to the reporting server (item id unknown without parsing).
            session.Send(BuildAddItemResult(charId, key, channel, mapId, monId, 0, MonItemTake.NotFound));
            return;
        }

        var next = party.GetNextOrder(eligible);
        if (next is null) return;
        var main = _state.FindMapSvr(next.MainId);
        if (main is null) return;

        var w = new PacketWriter(Msg.MW_PARTYORDERTAKEITEM_REQ);
        w.WriteUInt32(next.CharId); w.WriteUInt32(next.Key);
        w.WriteByte(serverId); w.WriteByte(channel); w.WriteUInt16(mapId); w.WriteUInt32(monId); w.WriteUInt16(tempMonId);
        w.WriteRaw(item);
        main.Send(w.ToArray());
    }

    // ===== helpers =====

    /// <summary>Route an event packet to the char's authoritative main server, re-emitted under <paramref name="reqId"/>.</summary>
    private void ForwardToMain(PacketReader r, ushort reqId, byte[] packet)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        var ch = _state.FindChar(charId, key);
        if (ch is null) return;
        _state.FindMapSvr(ch.MainId)?.Send(Reframe(reqId, packet));
    }

    /// <summary>Re-emit a received packet's body verbatim under a new message id (C++ <c>CPacket::Copy + SetID</c>).</summary>
    private static byte[] Reframe(ushort newId, byte[] packet)
        => new PacketWriter(newId).WriteRaw(packet.AsSpan(PacketHeader.Size)).ToArray();

    private static byte[] BuildAddItemResult(uint charId, uint key, byte channel, ushort mapId, uint monId, byte itemId, MonItemTake result)
    {
        var w = new PacketWriter(Msg.MW_ADDITEMRESULT_REQ);
        w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteByte(channel); w.WriteUInt16(mapId);
        w.WriteUInt32(monId); w.WriteByte(itemId); w.WriteByte((byte)result);
        return w.ToArray();
    }
}
