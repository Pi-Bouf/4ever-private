using Microsoft.Extensions.Logging;
using TWorld.Protocol;
using TWorld.Server.Net;

namespace TWorld.Server.World;

/// <summary>
/// Phase 5a — cross-map movement, teleport and connection routing. Ported from <c>SSHandler.cpp</c>
/// (OnMW_TELEPORT/CONLIST/MAPSVRLIST/RELEASEMAIN/REGION/BEGINTELEPORT/ENTER+LEAVESOLOMAP, OnCheckConnect,
/// OnBeginTeleport) and <c>TWorldSvr.cpp</c> (CheckMainCON/ClearDeadCON/Push+PopConCess). A character can
/// hold connections to several map servers at once (<see cref="Character.Connections"/>); a teleport
/// reconciles that set against the destination's requirements, drops the now-unneeded ones (DeadCons),
/// opens any new ones, then re-confirms the main server. The CHECKCONNECT/BEGINTELEPORT cycles are
/// serialized per character through the ConCess queue, exactly as the C++ batch thread does.
/// </summary>
public sealed partial class WorldService
{
    private bool DispatchMovement(ServerSession session, PacketReader r, byte[] packet)
    {
        switch (r.Id)
        {
            case Msg.MW_TELEPORT_ACK: OnMW_TELEPORT_ACK(session, r); return true;
            case Msg.MW_CONLIST_ACK: OnMW_CONLIST_ACK(session, r); return true;
            case Msg.MW_MAPSVRLIST_ACK: OnMW_MAPSVRLIST_ACK(session, r); return true;
            case Msg.MW_RELEASEMAIN_ACK: OnMW_RELEASEMAIN_ACK(session, r); return true;
            case Msg.MW_REGION_ACK: OnMW_REGION_ACK(r); return true;
            case Msg.MW_BEGINTELEPORT_ACK: OnMW_BEGINTELEPORT_ACK(session, packet); return true;
            case Msg.MW_ENTERSOLOMAP_ACK: OnMW_ENTERSOLOMAP_ACK(r); return true;
            case Msg.MW_LEAVESOLOMAP_ACK: OnMW_LEAVESOLOMAP_ACK(r); return true;
        }
        return false;
    }

    /// <summary>The map asks to teleport a char to <c>destServerId</c>: ack the client with TELEPORT_REQ and
    /// ask the destination map which connections the new location needs (CONLIST_REQ). C++ OnMW_TELEPORT_ACK.</summary>
    private void OnMW_TELEPORT_ACK(ServerSession session, PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        byte destServerId = r.ReadByte();

        var ch = _state.FindChar(charId, key);
        if (ch is null) { SendDelChar(session, charId, key, true, false); return; }

        var dest = _state.FindMapSvr(destServerId);
        if (dest is null)
        {
            // No such destination server -> tell the client the teleport failed, then drop the char.
            session.Send(BuildTeleportReq(ch, TprResult.NoDestination));
            CloseChar(ch);
            return;
        }

        ch.PartyWaiter = false;
        session.Send(BuildTeleportReq(ch, TprResult.Success));
        dest.Send(BuildPosReq(Msg.MW_CONLIST_REQ, ch));
    }

    private void OnMW_CONLIST_ACK(ServerSession session, PacketReader r) => ReconcileFromList(session, r, addSelf: true);
    private void OnMW_MAPSVRLIST_ACK(ServerSession session, PacketReader r) => ReconcileFromList(session, r, addSelf: true);

    /// <summary>Shared body of OnMW_CONLIST_ACK / OnMW_MAPSVRLIST_ACK: read the required server-id list and
    /// reconcile the char's live connections against it (adding the responding server itself).</summary>
    private void ReconcileFromList(ServerSession session, PacketReader r, bool addSelf)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        byte count = r.ReadByte();

        var ch = _state.FindChar(charId, key);
        if (ch is null) { SendDelChar(session, charId, key, true, false); return; }
        var main = _state.FindMapSvr(ch.MainId);
        if (main is null) { session.Send(BuildInvalidChar(charId, key, false)); return; }

        var required = new List<byte>(count);
        for (byte i = 0; i < count; i++) required.Add(r.ReadByte());
        ReconcileConnections(ch, session, main, required, addSelf);
    }

    /// <summary>The reconcile primitive shared by CONLIST/MAPSVRLIST/CHECKCONNECT: given the set of map
    /// servers the char now needs, drop the connections no longer wanted (queued into DeadCons), and for any
    /// genuinely new server ask the main to open it (ROUTELIST_REQ, answered with MW_ROUTE_ACK). If nothing
    /// new is needed, re-confirm the main server. Mirrors the identical C++ block in those three handlers.</summary>
    private void ReconcileConnections(Character ch, ServerSession session, ServerSession main, List<byte> required, bool addSelf)
    {
        var want = new HashSet<byte>(required);
        if (addSelf) want.Add(session.ServerId);

        foreach (var sid in ch.Connections.Keys.ToList())
            if (!want.Contains(sid)) { ch.DeadCons.Add(sid); ch.Connections.Remove(sid); }

        var fresh = want.Where(sid => !ch.Connections.ContainsKey(sid)).OrderBy(x => x).ToList();
        if (fresh.Count > 0)
        {
            var w = new PacketWriter(Msg.MW_ROUTELIST_REQ);
            w.WriteUInt32(ch.CharId); w.WriteUInt32(ch.Key); w.WriteByte((byte)fresh.Count);
            foreach (var sid in fresh) w.WriteByte(sid);
            main.Send(w.ToArray());
        }
        else
        {
            CheckMainCON(ch);
        }
    }

    /// <summary>The old main server released the char during a hand-off: forward the re-load (ENTERSVR_REQ)
    /// to the new main and remember which server we're switching to. C++ OnMW_RELEASEMAIN_ACK.</summary>
    private void OnMW_RELEASEMAIN_ACK(ServerSession session, PacketReader r)
    {
        byte dbLoad = r.ReadByte();          // note: bDBLoad is first in this packet
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();

        var ch = _state.FindChar(charId, key);
        if (ch is null) { SendDelChar(session, charId, key, true, false); return; }
        var main = _state.FindMapSvr(ch.MainId);
        if (main is null) { session.Send(BuildInvalidChar(charId, key, true)); return; }

        SendEnterSvrReq(main, dbLoad != 0, charId, key);
        ch.ChgMainId = session.ServerId;
    }

    /// <summary>A char changed region: record it and propagate the new region into the soulmate/friend views
    /// other online characters hold of this one. Pure in-memory. C++ OnMW_REGION_ACK.</summary>
    private void OnMW_REGION_ACK(PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        uint region = r.ReadUInt32();

        var ch = _state.FindChar(charId, key);
        if (ch is null) return;
        ch.Region = region;

        foreach (var soul in ch.Soulmates.Values)
        {
            if (soul.CharId == ch.CharId) continue;
            if (_state.Characters.TryGetValue(soul.CharId, out var partner)
                && partner.Soulmates.TryGetValue(partner.CharId, out var ps))
            { ps.Connected = true; ps.Region = region; }
        }

        foreach (var fr in ch.Friends.Values)
        {
            if (fr.Type == FriendType.Friend) continue;     // only "target" links carry the reverse view
            if (!fr.Connected) continue;
            if (_state.CharactersByName.TryGetValue(fr.Name, out var target)
                && target.Friends.TryGetValue(ch.CharId, out var tf))
                tf.Region = region;
        }
    }

    // ===== CHECKCONNECT / BEGINTELEPORT — serialized per char through the ConCess queue =====

    /// <summary>A map reports its current connection state for the char (sent during/after movement). It is
    /// queued so only one connect/teleport cycle runs at a time. C++ OnMW_CHECKCONNECT_ACK.</summary>
    private void OnMW_CHECKCONNECT_ACK(ServerSession session, byte[] packet)
    {
        var r = new PacketReader(packet);
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        var ch = _state.FindChar(charId, key);
        if (ch is null) { SendDelChar(session, charId, key, true, false); return; }

        if (PushConCess(ch, session, packet)) return;   // a cycle is already running -> deferred
        OnCheckConnect(ch, session, packet);
    }

    /// <summary>Map's begin-teleport notice. A same-channel move just updates the channel; a cross-channel
    /// move enters the serialized teleport cycle. C++ OnMW_BEGINTELEPORT_ACK.</summary>
    private void OnMW_BEGINTELEPORT_ACK(ServerSession session, byte[] packet)
    {
        var r = new PacketReader(packet);
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        byte sameChannel = r.ReadByte();
        byte channel = r.ReadByte();

        var ch = _state.FindChar(charId, key);
        if (ch is null) { SendDelChar(session, charId, key, true, false); return; }

        if (sameChannel != 0) { ch.Channel = channel; return; }

        if (PushConCess(ch, session, packet)) return;
        OnBeginTeleport(ch, session, packet);
    }

    /// <summary>C++ OnCheckConnect: only the main server's report drives state; others just advance the queue.
    /// Updates position, drops/opens connections to match the reported set, then re-confirms the main.</summary>
    private void OnCheckConnect(Character ch, ServerSession session, byte[] packet)
    {
        var main = _state.FindMapSvr(ch.MainId);
        if (main is null) { CloseChar(ch); return; }
        if (session != main) { PopConCess(ch); return; }

        var r = new PacketReader(packet);
        _ = r.ReadUInt32(); _ = r.ReadUInt32();              // charId, key
        ch.Channel = r.ReadByte();
        ch.MapId = r.ReadUInt16();
        ch.PosX = r.ReadFloat(); ch.PosY = r.ReadFloat(); ch.PosZ = r.ReadFloat();
        byte count = r.ReadByte();

        if (count == 0) { CheckMainCON(ch); return; }

        var required = new List<byte>(count);
        for (byte i = 0; i < count; i++) required.Add(r.ReadByte());
        ReconcileConnections(ch, session, main, required, addSelf: false);
    }

    /// <summary>C++ OnBeginTeleport: only the main drives it; tell every valid connection to start the
    /// teleport (STARTTELEPORT_REQ). The cycle is popped later by the resulting CHECKMAIN_ACK.</summary>
    private void OnBeginTeleport(Character ch, ServerSession session, byte[] packet)
    {
        var main = _state.FindMapSvr(ch.MainId);
        if (main is null) { CloseChar(ch); return; }
        if (session != main) { PopConCess(ch); return; }

        var r = new PacketReader(packet);
        _ = r.ReadUInt32(); _ = r.ReadUInt32(); _ = r.ReadByte();   // charId, key, sameChannel
        ch.Channel = r.ReadByte();
        ch.MapId = r.ReadUInt16();
        ch.PosX = r.ReadFloat(); ch.PosY = r.ReadFloat(); ch.PosZ = r.ReadFloat();

        foreach (var (sid, con) in ch.Connections.ToList())
        {
            if (!con.Valid) continue;
            _state.FindMapSvr(sid)?.Send(BuildPosReq(Msg.MW_STARTTELEPORT_REQ, ch));
        }
    }

    /// <summary>m_qConCess push: enqueue this cycle; return true if another is already in front (so the caller
    /// should defer and not process now).</summary>
    private static bool PushConCess(Character ch, ServerSession session, byte[] packet)
    {
        ch.ConCess.Enqueue((session.ServerId, packet));
        return ch.ConCess.Count > 1;
    }

    /// <summary>m_qConCess pop: drop the finished front, then re-dispatch the next queued cycle, if any.</summary>
    private void PopConCess(Character ch)
    {
        if (ch.ConCess.Count == 0) return;
        ch.ConCess.Dequeue();
        if (ch.ConCess.Count == 0) return;

        var (sid, packet) = ch.ConCess.Peek();
        var session = _state.FindMapSvr(sid);
        if (session is null) return;
        switch (new PacketReader(packet).Id)
        {
            case Msg.MW_BEGINTELEPORT_ACK: OnBeginTeleport(ch, session, packet); break;
            case Msg.MW_CHECKCONNECT_ACK: OnCheckConnect(ch, session, packet); break;
        }
    }

    /// <summary>C++ ClearDeadCON: send CLOSECHAR_REQ to every map server the char no longer needs, then clear
    /// the dead-connection list. Called from CHECKMAIN once the new connection set is confirmed.</summary>
    private void ClearDeadCON(Character ch)
    {
        foreach (var sid in ch.DeadCons)
        {
            var map = _state.FindMapSvr(sid);
            if (map is null) continue;
            var w = new PacketWriter(Msg.MW_CLOSECHAR_REQ);
            w.WriteUInt32(ch.CharId); w.WriteUInt32(ch.Key);
            map.Send(w.ToArray());
        }
        ch.DeadCons.Clear();
    }

    // ===== solo / instance maps =====

    /// <summary>Map confirms the char entered a solo (instance) map: create a one-member solo party if the
    /// char has none, then tell every connection to enter the solo map. C++ OnMW_ENTERSOLOMAP_ACK.</summary>
    private void OnMW_ENTERSOLOMAP_ACK(PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        var ch = _state.FindChar(charId, key);
        if (ch is null) return;
        if (_state.FindMapSvr(ch.MainId) is null) return;

        if (ch.Party is null)
        {
            var party = new Party { Id = _state.PartyIds.Alloc(), ObtainType = PtSolo, ChiefId = charId };
            party.AddMember(ch);
            _state.Parties[party.Id] = party;
        }

        var p = ch.Party!;
        foreach (var (sid, con) in ch.Connections.ToList())
        {
            if (!con.Valid) continue;
            _state.FindMapSvr(sid)?.Send(BuildEnterSoloMapReq(charId, key, p.Id, p.ObtainType, p.ChiefId));
        }
    }

    /// <summary>Map reports the char left a solo map: dissolve the solo party. C++ OnMW_LEAVESOLOMAP_ACK.</summary>
    private void OnMW_LEAVESOLOMAP_ACK(PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        var ch = _state.FindChar(charId, key);
        if (ch is null) return;

        if (ch.Party is { ObtainType: PtSolo } party)
        {
            ushort id = party.Id;
            foreach (var m in party.Members.ToList()) party.DelMember(m.CharId);
            _state.Parties.Remove(id);
            _state.PartyIds.Free(id);
        }
    }

    // ===== senders =====

    private const byte PtSolo = 1;   // PARTY_TYPE.PT_SOLO

    private static byte[] BuildTeleportReq(Character ch, TprResult result)
    {
        var w = new PacketWriter(Msg.MW_TELEPORT_REQ);
        w.WriteUInt32(ch.CharId); w.WriteUInt32(ch.Key); w.WriteByte(ch.Channel); w.WriteUInt16(ch.MapId);
        w.WriteFloat(ch.PosX); w.WriteFloat(ch.PosY); w.WriteFloat(ch.PosZ);
        w.WriteByte((byte)result);
        return w.ToArray();
    }

    /// <summary>charId, key, channel, mapId, pos — the common layout of CONLIST_REQ / MAPSVRLIST_REQ /
    /// STARTTELEPORT_REQ (identical to ROUTE_REQ).</summary>
    private static byte[] BuildPosReq(ushort id, Character ch)
    {
        var w = new PacketWriter(id);
        w.WriteUInt32(ch.CharId); w.WriteUInt32(ch.Key); w.WriteByte(ch.Channel); w.WriteUInt16(ch.MapId);
        w.WriteFloat(ch.PosX); w.WriteFloat(ch.PosY); w.WriteFloat(ch.PosZ);
        return w.ToArray();
    }

    private static byte[] BuildEnterSoloMapReq(uint charId, uint key, ushort partyId, byte partyType, uint chiefId)
    {
        var w = new PacketWriter(Msg.MW_ENTERSOLOMAP_REQ);
        w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteUInt16(partyId); w.WriteByte(partyType); w.WriteUInt32(chiefId);
        return w.ToArray();
    }
}
