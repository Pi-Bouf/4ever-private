using TWorld.Protocol;
using TWorld.Server.Net;

namespace TWorld.Server.World;

/// <summary>
/// RW relay plane (TRelaySvr ↔ world), ported from <c>RWHandler.cpp</c> / <c>RWSender.cpp</c>. The relay
/// keeps a cross-map-instance visibility index: the world answers its <c>RW_ENTERCHAR_REQ</c> char query and
/// <c>RW_RELAYCONNECT_REQ</c>, and <b>forwards</b> every guild/party/tactics/corps/name/chat-ban/map-change
/// state transition to the relay so its index stays current. All forwarders are guarded on a live relay peer.
///
/// In this deployment there is no TRelaySvr, so <see cref="WorldState.RelayServer"/> is normally null and the
/// forwarders are no-ops; the wire layer is nonetheless complete and faithful to the C++ senders. The GM
/// operator list (<c>m_vTOPERATOR</c>) isn't modelled by this port, so the registration ack emits zero
/// operators (the empty-list path is exact).
/// </summary>
public sealed partial class WorldService
{
    private bool DispatchRelay(ServerSession session, PacketReader r)
    {
        switch (r.Id)
        {
            case Msg.RW_ENTERCHAR_REQ: OnRW_ENTERCHAR_REQ(session, r); return true;
            case Msg.RW_RELAYCONNECT_REQ: OnRW_RELAYCONNECT_REQ(r); return true;
        }
        return false;
    }

    /// <summary>The map-unit id a char sits on: MAKEWORD(posX/UNIT_SIZE, posZ/UNIT_SIZE) (UNIT_SIZE=1024).</summary>
    private static ushort MakeUnit(float posX, float posZ)
        => (ushort)(((byte)((int)posX / 1024)) | (((byte)((int)posZ / 1024)) << 8));

    /// <summary>Reply to relay registration with nation + GM operators + the server-message table, then tell
    /// every map to (re)connect to the relay. C++ OnRW_RELAYSVR_REQ.</summary>
    private void SendRelaySvrAck()
    {
        if (_state.RelayServer is not { } relay) return;
        var w = new PacketWriter(Msg.RW_RELAYSVR_ACK);
        w.WriteByte(_state.Nation);
        w.WriteUInt16(0); // operator count (GM operator list not modelled in this port)
        w.WriteUInt16((ushort)_state.ServerMessages.Count);
        foreach (var (id, text) in _state.ServerMessages.OrderBy(kv => kv.Key))
        {
            w.WriteUInt32(id);
            w.WriteString(text);
        }
        relay.Send(w.ToArray());

        var connect = new PacketWriter(Msg.MW_RELAYCONNECT_REQ);
        connect.WriteUInt32(0);
        BroadcastServers(connect.ToArray());
    }

    /// <summary>The relay asks whether a char is online; reply with its full guild/tactics/party/corps state
    /// (or result=false). C++ OnRW_ENTERCHAR_REQ.</summary>
    private void OnRW_ENTERCHAR_REQ(ServerSession session, PacketReader r)
    {
        uint charId = r.ReadUInt32();
        string name = r.ReadString();

        Character? ch = _state.CharactersByName.TryGetValue(name, out var c) ? c : null;
        if (ch is null || ch.CharId != charId)
        {
            var fail = new PacketWriter(Msg.RW_ENTERCHAR_ACK);
            fail.WriteUInt32(charId); fail.WriteString(name); fail.WriteBool(false);
            session.Send(fail.ToArray());
            return;
        }

        uint guildId = 0, guildChief = 0, tacticsId = 0, tacticsChief = 0;
        byte duty = 0;
        var guild = _state.FindGuildByChar(charId);
        if (guild?.FindMember(charId) is { } gm) { guildId = guild.Id; guildChief = guild.Chief; duty = gm.Duty; }
        var tactics = _state.FindTacticsGuild(charId);
        if (tactics is not null) { tacticsId = tactics.Id; tacticsChief = tactics.Chief; }

        ushort partyId = 0, corpsId = 0; uint chiefId = 0, generalId = 0;
        if (ch.Party is { } p)
        {
            partyId = p.Id; chiefId = p.ChiefId;
            if (p.CorpsId != 0 && _state.FindCorps(p.CorpsId) is { } corps) { corpsId = corps.Id; generalId = corps.GeneralId; }
        }

        var w = new PacketWriter(Msg.RW_ENTERCHAR_ACK);
        w.WriteUInt32(charId); w.WriteString(name); w.WriteBool(true);
        w.WriteByte(ch.Country); w.WriteByte(ch.AidCountry);
        w.WriteUInt32(guildId); w.WriteUInt32(guildChief); w.WriteByte(duty);
        w.WriteUInt16(partyId); w.WriteUInt32(chiefId); w.WriteUInt16(corpsId); w.WriteUInt32(generalId);
        w.WriteUInt32(tacticsId); w.WriteUInt32(tacticsChief);
        w.WriteUInt16(ch.MapId); w.WriteUInt16(MakeUnit(ch.PosX, ch.PosZ));
        session.Send(w.ToArray());
    }

    /// <summary>The relay asks the world to open a char's relay connection; forward to the char's main map.
    /// C++ OnRW_RELAYCONNECT_REQ.</summary>
    private void OnRW_RELAYCONNECT_REQ(PacketReader r)
    {
        uint charId = r.ReadUInt32();
        if (!_state.Characters.TryGetValue(charId, out var ch)) return;
        if (_state.FindMapSvr(ch.MainId) is not { } map) return;
        var w = new PacketWriter(Msg.MW_RELAYCONNECT_REQ);
        w.WriteUInt32(charId);
        map.Send(w.ToArray());
    }

    // ===== forwarders (world -> relay), each a no-op without a live relay peer =====

    private void RelayEnterCharAck(Character ch, byte result)
    {
        if (_state.RelayServer is not { } relay) return;
        uint guildId = 0, guildChief = 0, tacticsId = 0, tacticsChief = 0;
        byte duty = 0;
        var guild = _state.FindGuildByChar(ch.CharId);
        if (guild?.FindMember(ch.CharId) is { } gm) { guildId = guild.Id; guildChief = guild.Chief; duty = gm.Duty; }
        var tactics = _state.FindTacticsGuild(ch.CharId);
        if (tactics is not null) { tacticsId = tactics.Id; tacticsChief = tactics.Chief; }
        ushort partyId = 0, corpsId = 0; uint chiefId = 0, generalId = 0;
        if (ch.Party is { } p)
        {
            partyId = p.Id; chiefId = p.ChiefId;
            if (p.CorpsId != 0 && _state.FindCorps(p.CorpsId) is { } corps) { corpsId = corps.Id; generalId = corps.GeneralId; }
        }
        var w = new PacketWriter(Msg.RW_ENTERCHAR_ACK);
        w.WriteUInt32(ch.CharId); w.WriteString(ch.Name); w.WriteBool(result != 0);
        w.WriteByte(ch.Country); w.WriteByte(ch.AidCountry);
        w.WriteUInt32(guildId); w.WriteUInt32(guildChief); w.WriteByte(duty);
        w.WriteUInt16(partyId); w.WriteUInt32(chiefId); w.WriteUInt16(corpsId); w.WriteUInt32(generalId);
        w.WriteUInt32(tacticsId); w.WriteUInt32(tacticsChief);
        w.WriteUInt16(ch.MapId); w.WriteUInt16(MakeUnit(ch.PosX, ch.PosZ));
        relay.Send(w.ToArray());
    }

    private void RelayPartyAdd(uint charId, ushort partyId, uint chiefId)
        => RelaySend(Msg.RW_PARTYADD_ACK, w => { w.WriteUInt32(charId); w.WriteUInt16(partyId); w.WriteUInt32(chiefId); });

    private void RelayPartyDel(uint charId, ushort partyId, uint chiefId)
        => RelaySend(Msg.RW_PARTYDEL_ACK, w => { w.WriteUInt32(charId); w.WriteUInt16(partyId); w.WriteUInt32(chiefId); });

    private void RelayPartyChgChief(ushort partyId, uint chiefId)
        => RelaySend(Msg.RW_PARTYCHGCHIEF_ACK, w => { w.WriteUInt16(partyId); w.WriteUInt32(chiefId); });

    private void RelayGuildAdd(uint charId, uint guildId, uint masterId)
        => RelaySend(Msg.RW_GUILDADD_ACK, w => { w.WriteUInt32(charId); w.WriteUInt32(guildId); w.WriteUInt32(masterId); });

    private void RelayGuildDel(uint charId, uint guildId)
        => RelaySend(Msg.RW_GUILDDEL_ACK, w => { w.WriteUInt32(charId); w.WriteUInt32(guildId); });

    private void RelayGuildChgMaster(uint guildId, uint masterId)
        => RelaySend(Msg.RW_GUILDCHGMASTER_ACK, w => { w.WriteUInt32(guildId); w.WriteUInt32(masterId); });

    private void RelayCorpsJoin(ushort partyId, ushort corpsId, ushort commander)
        => RelaySend(Msg.RW_CORPSJOIN_ACK, w => { w.WriteUInt16(partyId); w.WriteUInt16(corpsId); w.WriteUInt16(commander); });

    private void RelayChangeName(uint charId, byte type, byte value, string name)
        => RelaySend(Msg.RW_CHANGENAME_ACK, w => { w.WriteUInt32(charId); w.WriteByte(type); w.WriteByte(value); w.WriteString(name); });

    private void RelayTacticsAdd(uint charId, uint guildId, uint guildMaster)
        => RelaySend(Msg.RW_TACTICSADD_ACK, w => { w.WriteUInt32(charId); w.WriteUInt32(guildId); w.WriteUInt32(guildMaster); });

    private void RelayTacticsDel(uint charId, uint guildId)
        => RelaySend(Msg.RW_TACTICSDEL_ACK, w => { w.WriteUInt32(charId); w.WriteUInt32(guildId); });

    private void RelayChatBan(string name, long chatBanTime)
        => RelaySend(Msg.RW_CHATBAN_ACK, w => { w.WriteString(name); w.WriteInt64(chatBanTime); });

    private void RelayChangeMap(uint charId, ushort mapId, ushort unitId)
        => RelaySend(Msg.RW_CHANGEMAP_ACK, w => { w.WriteUInt32(charId); w.WriteUInt16(mapId); w.WriteUInt16(unitId); });

    private void RelaySend(ushort id, Action<PacketWriter> build)
    {
        if (_state.RelayServer is not { } relay) return;
        var w = new PacketWriter(id);
        build(w);
        relay.Send(w.ToArray());
    }
}
