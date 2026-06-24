using Microsoft.Extensions.Logging;
using TWorld.Protocol;
using TWorld.Server.Net;

namespace TWorld.Server.World;

/// <summary>
/// Phase 5h — character-info / mail / misc forwards, ported from <c>SSHandler.cpp</c>. Stat-inspection and
/// incoming mail are routed to the owning char's main; a hero selection is announced to every map; a base
/// change (appearance/name/title) updates world state and fans the change to the char's connections (or all
/// maps for name/title). The country-change branch (which retunes nation-balance buckets and runs party/
/// tactics fix-ups) and the rename's DB/relay clean-up are larger dependent flows, deferred and noted.
/// </summary>
public sealed partial class WorldService
{
    private const byte IkFace = 45, IkHair = 46, IkRace = 47, IkName = 48, IkSex = 49,
                       IkCountry = 96, IkAidCountry = 97, IkTitle = 103;

    private bool DispatchCharInfo(ServerSession session, PacketReader r, byte[] packet)
    {
        switch (r.Id)
        {
            case Msg.MW_CHARSTATINFO_ACK: OnMW_CHARSTATINFO_ACK(r); return true;
            case Msg.MW_CHARSTATINFOANS_ACK: OnMW_CHARSTATINFOANS_ACK(r, packet); return true;
            case Msg.MW_POSTRECV_ACK: OnMW_POSTRECV_ACK(r, packet); return true;
            case Msg.MW_CHANGECHARBASE_ACK: OnMW_CHANGECHARBASE_ACK(r); return true;
            case Msg.MW_HEROSELECT_ACK: OnMW_HEROSELECT_ACK(r); return true;
        }
        return false;
    }

    /// <summary>Someone inspects a char's stats: deliver the request to the target's main. C++ OnMW_CHARSTATINFO_ACK.</summary>
    private void OnMW_CHARSTATINFO_ACK(PacketReader r)
    {
        uint reqCharId = r.ReadUInt32();
        uint charId = r.ReadUInt32();
        if (!_state.Characters.TryGetValue(charId, out var ch)) return;
        var w = new PacketWriter(Msg.MW_CHARSTATINFOANS_REQ);
        w.WriteUInt32(reqCharId); w.WriteUInt32(charId);
        _state.FindMapSvr(ch.MainId)?.Send(w.ToArray());
    }

    /// <summary>The inspected char answers: deliver the stat block to the requester's main (verbatim). C++ OnMW_CHARSTATINFOANS_ACK.</summary>
    private void OnMW_CHARSTATINFOANS_ACK(PacketReader r, byte[] packet)
    {
        uint reqCharId = r.ReadUInt32();
        if (!_state.Characters.TryGetValue(reqCharId, out var ch)) return;
        _state.FindMapSvr(ch.MainId)?.Send(Reframe(Msg.MW_CHARSTATINFO_REQ, packet));
    }

    /// <summary>Mail arrived for a (possibly elsewhere) char: route it to the recipient's main. C++ OnMW_POSTRECV_ACK.</summary>
    private void OnMW_POSTRECV_ACK(PacketReader r, byte[] packet)
    {
        _ = r.ReadUInt32();              // postId
        _ = r.ReadString();              // sender
        string target = r.ReadString();
        if (!_state.CharactersByName.TryGetValue(target, out var ch)) return;
        _state.FindMapSvr(ch.MainId)?.Send(Reframe(Msg.MW_POSTRECV_REQ, packet));
    }

    /// <summary>A battle-zone hero was selected: announce to every map. C++ OnMW_HEROSELECT_ACK.</summary>
    private void OnMW_HEROSELECT_ACK(PacketReader r)
    {
        ushort battleZoneId = r.ReadUInt16();
        string heroName = r.ReadString();
        long timeHero = r.ReadInt64();
        var w = new PacketWriter(Msg.MW_HEROSELECT_REQ);
        w.WriteUInt16(battleZoneId); w.WriteString(heroName); w.WriteInt64(timeHero);
        BroadcastServers(w.ToArray());
    }

    /// <summary>A character changed an appearance/name/title attribute: apply it to world state and fan the
    /// change out — appearance to the char's connections, name/title to every map. C++ OnMW_CHANGECHARBASE_ACK.</summary>
    private void OnMW_CHANGECHARBASE_ACK(PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        byte type = r.ReadByte();
        byte value = r.ReadByte();
        ushort titleId = r.ReadUInt16();
        string name = r.ReadString();

        var ch = _state.FindChar(charId, key);
        if (ch is null) return;

        switch (type)
        {
            case IkFace: ch.Face = value; break;
            case IkHair: ch.Hair = value; break;
            case IkRace: ch.Race = value; break;
            case IkSex: ch.Sex = value; break;
            case IkCountry:
            case IkAidCountry:
                ChangeCountry(ch, type, value);   // re-buckets nation balance + party/guild/tactics/social fix-ups + broadcasts
                return;
            case IkName:
                // Friend/soulmate links break on rename (the peer's stored id no longer resolves) — erase in DB.
                foreach (var fr in ch.Friends.Values) _ = Persist(() => _socialDb!.FriendEraseAsync(fr.Id, charId), "TFriendDelete(rename)");
                foreach (var sm in ch.Soulmates.Values)
                    if (sm.CharId != charId) _ = Persist(() => _socialDb!.SoulmateDelAsync(sm.CharId, sm.Target), "TSoulmateDel(rename)");
                RenameInWantedApps(charId, name);
                if (FindTournamentPlayer(charId) is { } tp) tp.Name = name;
                if (!string.IsNullOrEmpty(ch.Name)) _state.CharactersByName.Remove(ch.Name);
                ch.Name = name;
                if (!string.IsNullOrEmpty(name)) _state.CharactersByName[name] = ch;
                if (ch.Guild?.FindMember(charId) is { } gm) gm.Name = name;
                if (_state.FindTacticsGuild(charId)?.FindTactics(charId) is { } tm) tm.Name = name;
                // Relay RW_CHANGENAME is skipped — the relay plane isn't ported (cross-map rename visibility).
                break;
            case IkTitle: ch.TitleId = titleId; break;
            default: break;
        }

        byte[] req = BuildChangeCharBaseReq(charId, key, type, value, titleId, name);
        if (type != IkName && type != IkTitle)
        {
            foreach (var sid in ch.Connections.Keys.ToList())
                if (ch.Connections[sid].Valid) _state.FindMapSvr(sid)?.Send(req);
        }
        else
        {
            BroadcastServers(req);
        }
    }

    private static byte[] BuildChangeCharBaseReq(uint charId, uint key, byte type, byte value, ushort titleId, string name)
    {
        var w = new PacketWriter(Msg.MW_CHANGECHARBASE_REQ);
        w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteByte(type); w.WriteByte(value); w.WriteUInt16(titleId); w.WriteString(name);
        return w.ToArray();
    }

    /// <summary>A character's country (or aid-country) changed. Leaves the party, drops tactics-wanted apps and
    /// any mercenary membership, re-buckets nation balance, and — for a primary country change — leaves the
    /// guild and erases friends/soulmates (those bonds are country-scoped). Then fans the change to every map
    /// the char is on. C++ CTWorldSvrModule::ChangeCountry. (Relay RW_CHANGENAME + RecalcCountryBalance are
    /// skipped: the relay plane isn't ported and balance is recomputed on demand.)</summary>
    private void ChangeCountry(Character ch, byte type, byte value)
    {
        if (ch.Party is { } party) LeaveParty(party, ch.CharId, 0);

        DelGuildTacticsWantedApp(ch.CharId);
        if (_state.FindTacticsGuild(ch.CharId) is { } tacGuild)
        {
            tacGuild.Tactics.Remove(ch.CharId);
            _state.CharTactics.Remove(ch.CharId);
        }

        // Re-bucket: erase from the old war-country bucket, insert into the new one (by the changed value,
        // matching the C++ which buckets on the field being set).
        byte gap = GetWarCountryGap(ch.Level);
        if (gap < Proto.WarCountryMaxGap)
        {
            byte oldCc = GetWarCountry(ch);
            if (oldCc < (byte)Contry.Broa) _state.WarCountry[oldCc][gap].Remove(ch.CharId);
            if (value < (byte)Contry.Broa) _state.WarCountry[value][gap].Add(ch.CharId);
        }

        if (ch.Guild?.FindMember(ch.CharId) is { } gm) gm.WarCountry = value;

        if (type == IkCountry)
        {
            ch.Country = value;
            DelGuildWantedApp(ch.CharId);

            if (ch.Guild is { } guild && guild.FindMember(ch.CharId) is not null)
            {
                string name = guild.FindMember(ch.CharId)!.Name;
                guild.Members.Remove(ch.CharId);
                _state.CharGuild.Remove(ch.CharId);
                ch.Guild = null;
                if (_guildDb is not null)
                    _ = Persist(async () => await _guildDb.LeaveAsync(guild.Id, ch.CharId, (byte)GuildResult.LeaveSelf, (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds()), "TGuildLeave(country)");
                _state.FindMapSvr(ch.MainId)?.Send(BuildGuildLeaveReq(ch.CharId, ch.Key, name, (byte)GuildResult.LeaveSelf, (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
            }

            foreach (var fr in ch.Friends.Values)
            {
                _ = Persist(() => _socialDb!.FriendEraseAsync(fr.Id, ch.CharId), "TFriendDelete(country)");
                _ = Persist(() => _socialDb!.FriendEraseAsync(ch.CharId, fr.Id), "TFriendDelete(country)");
            }
            foreach (var sm in ch.Soulmates.Values)
                _ = Persist(() => _socialDb!.SoulmateDelAsync(sm.CharId, sm.Target), "TSoulmateDel(country)");
        }
        else
        {
            ch.AidCountry = value;
        }

        byte[] req = BuildChangeCharBaseReq(ch.CharId, ch.Key, type, value, ch.TitleId, "");
        foreach (var sid in ch.Connections.Keys.ToList())
            if (ch.Connections[sid].Valid) _state.FindMapSvr(sid)?.Send(req);
    }

    /// <summary>Update a char's name in any guild/tactics wanted application it has open. C++ rename branch.</summary>
    private void RenameInWantedApps(uint charId, string name)
    {
        foreach (var w in _state.GuildWanted.Values)
            if (w.Apps.TryGetValue(charId, out var app)) app.Name = name;
    }

    /// <summary>Remove a char's guild-wanted application, if any. C++ DelGuildWantedApp.</summary>
    private void DelGuildWantedApp(uint charId)
    {
        foreach (var w in _state.GuildWanted.Values) w.Apps.Remove(charId);
    }

    /// <summary>Remove a char's guild-tactics-wanted application, if any. C++ DelGuildTacticsWantedApp.</summary>
    private void DelGuildTacticsWantedApp(uint charId)
    {
        foreach (var w in _state.TacticsWanted.Values) w.Apps.Remove(charId);
    }

    /// <summary>Find a char's tournament player record. C++ FindTNMTPlayer.</summary>
    private TnmtPlayer? FindTournamentPlayer(uint charId) => _state.Tournament?.FindPlayer(charId);
}
