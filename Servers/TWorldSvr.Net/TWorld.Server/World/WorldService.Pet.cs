using TWorld.Protocol;
using TWorld.Server.Net;

namespace TWorld.Server.World;

/// <summary>
/// Phase 5g — pets / mounts / summons / taming, ported from <c>SSHandler.cpp</c>. These are coordination
/// relays: a tame attempt, blood draw, magic-mirror reflect or trade error is routed to the owning char's
/// <em>main</em> server; a mount change is fanned to the char's <em>other</em> connections; summon removals
/// and recall-monster data are fanned to all of a char's connections so every map it's visible on stays in
/// sync. (The summon <em>creation</em> handlers — CREATERECALLMON / CREATESPOLECNIKMON — allocate a recall
/// id and re-serialize the full creature record, so they're a separate slice.)
/// </summary>
public sealed partial class WorldService
{
    private bool DispatchPet(ServerSession session, PacketReader r, byte[] packet)
    {
        switch (r.Id)
        {
            case Msg.MW_MONTEMPT_ACK: OnMW_MONTEMPT_ACK(r); return true;
            case Msg.MW_MONTEMPTEVO_ACK: OnMW_MONTEMPTEVO_ACK(r); return true;
            case Msg.MW_GETBLOOD_ACK: OnMW_GETBLOOD_ACK(r); return true;
            case Msg.MW_DEALITEMERROR_ACK: OnMW_DEALITEMERROR_ACK(r); return true;
            case Msg.MW_MAGICMIRROR_ACK: OnMW_MAGICMIRROR_ACK(r, packet); return true;
            case Msg.MW_PETRIDING_ACK: OnMW_PETRIDING_ACK(session, r); return true;
            case Msg.MW_HELMETHIDE_ACK: OnMW_HELMETHIDE_ACK(session, r); return true;
            case Msg.MW_RECALLMONDEL_ACK: OnMW_RECALLMONDEL_ACK(r); return true;
            case Msg.MW_SPOLECNIKMONDEL_ACK: OnMW_SPOLECNIKMONDEL_ACK(r); return true;
            case Msg.MW_RECALLMONDATA_ACK: OnMW_RECALLMONDATA_ACK(r, packet); return true;
            case Msg.MW_CREATERECALLMON_ACK: OnCreateMon_ACK(r, Msg.MW_CREATERECALLMON_REQ); return true;
            case Msg.MW_CREATESPOLECNIKMON_ACK: OnCreateMon_ACK(r, Msg.MW_CREATESPOLECNIKMON_REQ); return true;
        }
        return false;
    }

    /// <summary>Spawn a recall (summon) or companion monster: if the map didn't supply an id, allocate the
    /// next one, then forward the full creature record to every map the owner is connected to. The record
    /// layout after the <c>(charId,key,monId)</c> head is byte-identical between the ACK we receive and the
    /// REQ we emit, so the tail is forwarded verbatim. C++ OnMW_CREATERECALLMON_ACK / OnMW_CREATESPOLECNIKMON_ACK.</summary>
    private void OnCreateMon_ACK(PacketReader r, ushort reqId)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        uint monId = r.ReadUInt32();
        byte[] tail = r.ReadRemaining().ToArray();   // wMon..skill list, identical in the REQ

        if (!_state.Characters.TryGetValue(charId, out var ch) || ch.Key != key)
        {
            _log.LogWarning("CreateMon 0x{Req:X4}: char {Char}/{Key:X} not found (stored key {Stored:X}).",
                reqId, charId, key, _state.Characters.TryGetValue(charId, out var c2) ? c2.Key : 0);
            return;
        }
        if (monId == 0) monId = _state.NextRecallId();

        int sent = 0;
        foreach (var sid in ch.Connections.Keys.ToList())
        {
            if (!ch.Connections[sid].Valid) continue;
            var w = new PacketWriter(reqId);
            w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteUInt32(monId); w.WriteRaw(tail);
            _state.FindMapSvr(sid)?.Send(w.ToArray());
            sent++;
        }
        _log.LogInformation("CreateMon 0x{Req:X4}: char {Char} monId {Mon} -> forwarded to {Sent} connection(s).",
            reqId, charId, monId, sent);
    }

    /// <summary>Monster tame attempt: route to the attacker's main. C++ OnMW_MONTEMPT_ACK.</summary>
    private void OnMW_MONTEMPT_ACK(PacketReader r)
    {
        uint atkId = r.ReadUInt32();
        ushort monId = r.ReadUInt16();
        if (!_state.Characters.TryGetValue(atkId, out var ch)) return;
        var w = new PacketWriter(Msg.MW_MONTEMPT_REQ);
        w.WriteUInt32(ch.CharId); w.WriteUInt32(ch.Key); w.WriteUInt16(monId);
        _state.FindMapSvr(ch.MainId)?.Send(w.ToArray());
    }

    /// <summary>Tame evolution: route to the attacker's main. C++ OnMW_MONTEMPTEVO_ACK.</summary>
    private void OnMW_MONTEMPTEVO_ACK(PacketReader r)
    {
        uint atkId = r.ReadUInt32();
        uint hostId = r.ReadUInt32();
        byte hostType = r.ReadByte();
        if (!_state.Characters.TryGetValue(atkId, out var ch)) return;
        var w = new PacketWriter(Msg.MW_MONTEMPTEVO_REQ);
        w.WriteUInt32(ch.CharId); w.WriteUInt32(ch.Key); w.WriteUInt32(hostId); w.WriteByte(hostType);
        _state.FindMapSvr(ch.MainId)?.Send(w.ToArray());
    }

    /// <summary>Blood drawn (vampire/pet feed): route to the owning char's main (the PC attacker, or the host
    /// if the attacker isn't a PC). C++ OnMW_GETBLOOD_ACK.</summary>
    private void OnMW_GETBLOOD_ACK(PacketReader r)
    {
        uint atkId = r.ReadUInt32();
        byte atkType = r.ReadByte();
        uint hostId = r.ReadUInt32();
        byte bloodType = r.ReadByte();
        uint blood = r.ReadUInt32();

        uint lookup = atkType == Proto.ObjTypePc ? atkId : hostId;
        if (!_state.Characters.TryGetValue(lookup, out var ch)) return;
        var w = new PacketWriter(Msg.MW_GETBLOOD_REQ);
        w.WriteUInt32(ch.CharId); w.WriteUInt32(ch.Key); w.WriteUInt32(atkId); w.WriteByte(atkType); w.WriteByte(bloodType); w.WriteUInt32(blood);
        _state.FindMapSvr(ch.MainId)?.Send(w.ToArray());
    }

    /// <summary>Trade-item error: route to the target char's main (looked up by name). C++ OnMW_DEALITEMERROR_ACK.</summary>
    private void OnMW_DEALITEMERROR_ACK(PacketReader r)
    {
        string target = r.ReadString();
        string errorChar = r.ReadString();
        byte error = r.ReadByte();
        if (!_state.CharactersByName.TryGetValue(target, out var ch)) return;
        var w = new PacketWriter(Msg.MW_DEALITEMERROR_REQ);
        w.WriteString(target); w.WriteString(errorChar); w.WriteByte(error);
        _state.FindMapSvr(ch.MainId)?.Send(w.ToArray());
    }

    /// <summary>Magic-mirror reflect: route verbatim to the attacker's main. C++ OnMW_MAGICMIRROR_ACK.</summary>
    private void OnMW_MAGICMIRROR_ACK(PacketReader r, byte[] packet)
    {
        _ = r.ReadUInt32();              // hostId
        uint attackId = r.ReadUInt32();
        if (!_state.Characters.TryGetValue(attackId, out var ch)) return;
        _state.FindMapSvr(ch.MainId)?.Send(Reframe(Msg.MW_MAGICMIRROR_REQ, packet));
    }

    /// <summary>Mount state changed: tell the char's <em>other</em> connections (the originating map already
    /// knows). C++ OnMW_PETRIDING_ACK.</summary>
    private void OnMW_PETRIDING_ACK(ServerSession session, PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        uint riding = r.ReadUInt32();
        var ch = _state.FindChar(charId, key);
        if (ch is null) return;
        ch.Riding = riding;

        foreach (var sid in ch.Connections.Keys.ToList())
        {
            var map = _state.FindMapSvr(sid);
            if (map is null || map == session) continue;   // skip the originating server
            var w = new PacketWriter(Msg.MW_PETRIDING_REQ);
            w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteUInt32(riding);
            map.Send(w.ToArray());
        }
    }

    /// <summary>Toggle helmet visibility: record it and echo back to the reporting server. C++ OnMW_HELMETHIDE_ACK.</summary>
    private void OnMW_HELMETHIDE_ACK(ServerSession session, PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        byte hide = r.ReadByte();
        var ch = _state.FindChar(charId, key);
        if (ch is null) return;
        ch.HelmetHide = hide;
        var w = new PacketWriter(Msg.MW_HELMETHIDE_REQ);
        w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteByte(hide);
        session.Send(w.ToArray());
    }

    private void OnMW_RECALLMONDEL_ACK(PacketReader r) => MonDelToConnections(r, Msg.MW_RECALLMONDEL_REQ);
    private void OnMW_SPOLECNIKMONDEL_ACK(PacketReader r) => MonDelToConnections(r, Msg.MW_SPOLECNIKMONDEL_REQ);

    /// <summary>Remove a recall/companion monster on every map the char is connected to (charId,key,monId,forever).
    /// C++ OnMW_RECALLMONDEL_ACK / OnMW_SPOLECNIKMONDEL_ACK.</summary>
    private void MonDelToConnections(PacketReader r, ushort reqId)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        uint monId = r.ReadUInt32();
        byte forever = r.ReadByte();
        if (!_state.Characters.TryGetValue(charId, out var ch)) return;

        foreach (var sid in ch.Connections.Keys.ToList())
        {
            if (!ch.Connections[sid].Valid) continue;
            var w = new PacketWriter(reqId);
            w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteUInt32(monId); w.WriteByte(forever);
            _state.FindMapSvr(sid)?.Send(w.ToArray());
        }
    }

    /// <summary>Recall-monster data update: forward verbatim to every valid connection. C++ OnMW_RECALLMONDATA_ACK.</summary>
    private void OnMW_RECALLMONDATA_ACK(PacketReader r, byte[] packet)
    {
        uint charId = r.ReadUInt32();
        _ = r.ReadUInt32();              // key
        if (!_state.Characters.TryGetValue(charId, out var ch)) return;
        foreach (var sid in ch.Connections.Keys.ToList())
        {
            if (!ch.Connections[sid].Valid) continue;
            _state.FindMapSvr(sid)?.Send(Reframe(Msg.MW_RECALLMONDATA_REQ, packet));
        }
    }
}
