using Microsoft.Extensions.Logging;
using TWorld.Data;
using TWorld.Protocol;
using TWorld.Server.Net;

namespace TWorld.Server.World;

/// <summary>
/// World logic (Phases 1–2), ported from <c>SSHandler.cpp</c>/<c>SSSender.cpp</c>. Phase 1: map-connect +
/// character enter/leave/chat. Phase 2: the guild lifecycle (DB-backed) and in-memory parties. All
/// methods run on the single batch task, so <see cref="WorldState"/> is touched without locks.
/// Deferred (Phase 2b): corps command relay, guild tactics/cabinet/articles/wanted/fame-gain/level-up.
/// </summary>
public sealed partial class WorldService
{
    private readonly WorldState _state;
    private readonly GuildDatabase? _guildDb;
    private readonly SocialDatabase? _socialDb;
    private readonly RankDatabase? _rankDb;
    private readonly BowDatabase? _bowDb;
    private readonly BrDatabase? _brDb;
    private readonly GameDatabase? _gameDb;
    private readonly GlobalDatabase? _globalDb;
    private readonly ILogger<WorldService> _log;
    private uint _fakeGuildSeq; // used only when no DB is configured (tests)

    public WorldService(WorldState state, GuildDatabase? guildDb, ILogger<WorldService> log,
        SocialDatabase? socialDb = null, RankDatabase? rankDb = null, BowDatabase? bowDb = null,
        BrDatabase? brDb = null, GameDatabase? gameDb = null, GlobalDatabase? globalDb = null)
    {
        _state = state;
        _guildDb = guildDb;
        _socialDb = socialDb;
        _rankDb = rankDb;
        _bowDb = bowDb;
        _brDb = brDb;
        _gameDb = gameDb;
        _globalDb = globalDb;
        _log = log;
    }

    public WorldState State => _state;

    public ValueTask OnConnectedAsync(PacketConnection conn)
    {
        conn.State = new ServerSession(conn);
        return ValueTask.CompletedTask;
    }

    public async Task DispatchAsync(PacketConnection conn, byte[] packet)
    {
        var session = (ServerSession)conn.State!;
        var r = new PacketReader(packet);
        try
        {
            switch (r.Id)
            {
                // --- Phase 1: map connect + character session ---
                case Msg.MW_CONNECT_ACK: OnMW_CONNECT_ACK(session, r); break;
                case Msg.MW_ADDCHAR_ACK: OnMW_ADDCHAR_ACK(session, r); break;
                case Msg.MW_ENTERSVR_ACK: OnMW_ENTERSVR_ACK(session, r); break;
                case Msg.MW_ROUTE_ACK: OnMW_ROUTE_ACK(session, r); break;
                case Msg.MW_CHARDATA_ACK: OnMW_CHARDATA_ACK(session, r); break;
                case Msg.MW_ENTERCHAR_ACK: await OnMW_ENTERCHAR_ACK(session, r); break;
                case Msg.MW_CHECKMAIN_ACK: OnMW_CHECKMAIN_ACK(session, r); break;
                case Msg.MW_CLOSECHAR_ACK: OnMW_CLOSECHAR_ACK(session, r); break;
                case Msg.MW_CHECKCONNECT_ACK: OnMW_CHECKCONNECT_ACK(session, packet); break;
                case Msg.MW_CHAT_ACK: OnMW_CHAT_ACK(session, packet); break;
                case Msg.CT_CTRLSVR_REQ: _state.ControlServer = session; _log.LogInformation("Control server registered."); break;
                case Msg.RW_RELAYSVR_REQ: OnRW_RELAYSVR_REQ(session, r); break;

                // --- Phase 2: guild ---
                case Msg.MW_GUILDESTABLISH_ACK: await OnGuildEstablish(r); break;
                case Msg.MW_GUILDDISORGANIZATION_ACK: await OnGuildDisorg(r); break;
                case Msg.MW_GUILDINVITE_ACK: OnGuildInvite(r); break;
                case Msg.MW_GUILDINVITEANSWER_ACK: await OnGuildInviteAnswer(r); break;
                case Msg.MW_GUILDLEAVE_ACK: await OnGuildLeave(r); break;
                case Msg.MW_GUILDKICKOUT_ACK: await OnGuildKickout(r); break;
                case Msg.MW_GUILDDUTY_ACK: await OnGuildDuty(r); break;
                case Msg.MW_GUILDPEER_ACK: await OnGuildPeer(r); break;
                case Msg.MW_GUILDINFO_ACK: OnGuildInfo(r); break;
                case Msg.MW_GUILDMEMBERLIST_ACK: OnGuildMemberList(r); break;

                // --- Phase 2: party (in-memory) ---
                case Msg.MW_PARTYADD_ACK: OnPartyAdd(r); break;
                case Msg.MW_PARTYJOIN_ACK: OnPartyJoin(r); break;
                case Msg.MW_PARTYDEL_ACK: OnPartyDel(r); break;
                case Msg.MW_CHGPARTYCHIEF_ACK: OnChgPartyChief(r); break;
                case Msg.MW_CHGPARTYTYPE_ACK: OnChgPartyType(r); break;
                case Msg.MW_PARTYMANSTAT_ACK: OnPartyManStat(r); break;

                default:
                    if (DispatchMovement(session, r, packet)) break;
                    if (DispatchCombat(session, r, packet)) break;
                    if (DispatchPvp(session, r, packet)) break;
                    if (DispatchPet(session, r, packet)) break;
                    if (DispatchCharInfo(session, r, packet)) break;
                    if (DispatchTms(session, r)) break;
                    if (DispatchMisc(session, r)) break;
                    if (DispatchMinigame(session, r)) break;
                    if (await DispatchMallAsync(session, r)) break;
                    if (DispatchControl(session, r, packet)) break;
                    if (await DispatchControlDbAsync(session, r, packet)) break;
                    if (DispatchRelay(session, r)) break;
                    if (DispatchSm(session, r)) break;
                    if (DispatchEvent(session, r)) break;
                    if (await DispatchTournamentEventAsync(session, r)) break;
                    if (DispatchNation(session, r)) break;
                    if (await DispatchCastleAsync(session, r, packet)) break;
                    if (await DispatchGuild2bAsync(session, r, packet)) break;
                    if (await DispatchCorpsAsync(session, r, packet)) break;
                    if (await DispatchFriendAsync(session, r)) break;
                    if (await DispatchSoulmateAsync(session, r)) break;
                    if (DispatchRank(session, r)) break;
                    if (await DispatchBowAsync(session, r)) break;
                    if (await DispatchBrAsync(session, r)) break;
                    if (DispatchTournament(session, r)) break;
                    _log.LogDebug("Unhandled message 0x{Id:X4} from server {Sid}", r.Id, session.WId);
                    break;
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Handler for 0x{Id:X4} failed.", r.Id);
        }
    }

    public void OnDisconnect(PacketConnection conn)
    {
        if (conn.State is not ServerSession session) return;
        if (_state.ControlServer == session) _state.ControlServer = null;
        if (_state.RelayServer == session) _state.RelayServer = null;
        if (_state.Bow?.BowServer == session) _state.Bow.BowServer = null;
        if (_state.Br?.BrServer == session) _state.Br.BrServer = null;
        if (session.WId != 0 && _state.Servers.TryGetValue(session.WId, out var s) && s == session)
        {
            _state.Servers.Remove(session.WId);
            foreach (var ch in _state.Characters.Values.Where(c => c.MainId == session.ServerId).ToList())
                CloseChar(ch);
            _log.LogInformation("Map server {Sid} unregistered.", session.ServerId);
        }
    }

    // ===== Phase 1 handlers =====

    private void OnMW_CONNECT_ACK(ServerSession session, PacketReader r)
    {
        ushort wServerId = r.ReadUInt16();
        byte count = r.ReadByte();
        if (_state.Servers.ContainsKey(wServerId))
        {
            _log.LogWarning("Duplicate server id 0x{Id:X4}; rejecting.", wServerId);
            return;
        }
        session.WId = wServerId;
        for (byte i = 0; i < count; i++) session.Channels.Add(r.ReadByte());
        _state.Servers[wServerId] = session;
        _log.LogInformation("Map server registered: id={Sid} type={Type} channels=[{Ch}]",
            session.ServerId, session.ServerType, string.Join(",", session.Channels));

        // Phase 4: the dedicated BoW/BR map servers register themselves as the battle hosts.
        if (session.ServerId == Proto.BowServerId && _state.Bow is not null)
        {
            _state.Bow.BowServer = session;
            _log.LogInformation("BoW battle server connected.");
        }
        if (session.ServerId == Proto.BrServerId && _state.Br is not null)
        {
            _state.Br.BrServer = session;
            _log.LogInformation("BR battle server connected.");
        }

        // Phase 3: hand the newly-connected map the current monthly PvP ladder (C++ sends this from the
        // map's first LOCALENABLE; here the registration ack is the equivalent "map is ready" point).
        // Only once the rank system is initialized (RankMonth set at startup) — skipped in DB-free tests.
        if (_state.RankMonth != 0) session.Send(BuildMonthRankList());

        // Phase 4d: hand the newly-connected map the current tournament bracket config (TournamentInfo).
        if (_state.Tournament is not null) TournamentInfoBroadcast(session);
    }

    private void OnMW_ADDCHAR_ACK(ServerSession session, PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        uint ip = r.ReadUInt32();
        ushort port = r.ReadUInt16();
        uint userId = r.ReadUInt32();
        _state.ActiveUsers.Add(userId);

        if (!_state.Characters.TryGetValue(charId, out var ch))
        {
            ch = new Character { CharId = charId, Key = key, UserId = userId, MainId = session.ServerId };
            ch.Connections[session.ServerId] = new CharConnection { ServerId = session.ServerId, IpAddr = ip, Port = port, Valid = true };
            _state.Characters[charId] = ch;
            SendEnterSvrReq(session, true, charId, key);
            _log.LogInformation("AddChar {Char} (user {User}) on map {Sid} -> ENTERSVR_REQ", charId, userId, session.ServerId);
            return;
        }

        var main = _state.FindMapSvr(ch.MainId);
        ch.Connections.TryGetValue(session.ServerId, out var con);
        if (main is null || ch.Key != key || con is null || con.Valid || ip != con.IpAddr || port != con.Port)
        {
            session.Send(BuildInvalidChar(charId, key, false));
            CloseChar(ch);
            return;
        }
        con.Ready = false; con.Valid = true;
        if (ch.Connections.Values.Any(c => !c.Valid)) return;
        SendCharDataReq(main, charId, key);
    }

    /// <summary>The map's reply to ENTERSVR_REQ: it loaded the character from DB and returns the full
    /// record. We populate the in-memory char, link guild/tactics, then drive the rest of the handshake
    /// with CHARINFO_REQ + ROUTE_REQ (the map answers ROUTE_REQ with ROUTE_ACK). Ported from C++
    /// <c>OnMW_ENTERSVR_ACK</c> — the step the Phase-1 flow was missing, which stalled login after
    /// ENTERSVR_REQ.</summary>
    private void OnMW_ENTERSVR_ACK(ServerSession session, PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        string name = r.ReadString();
        byte level = r.ReadByte();
        byte realSex = r.ReadByte();
        byte cls = r.ReadByte();
        byte race = r.ReadByte();
        byte sex = r.ReadByte();
        byte face = r.ReadByte();
        byte hair = r.ReadByte();
        byte helmetHide = r.ReadByte();
        byte country = r.ReadByte();
        byte aidCountry = r.ReadByte();
        uint region = r.ReadUInt32();
        byte channel = r.ReadByte();
        ushort mapId = r.ReadUInt16();
        float posX = r.ReadFloat();
        float posY = r.ReadFloat();
        float posZ = r.ReadFloat();
        byte logout = r.ReadByte();
        byte save = r.ReadByte();
        byte result = r.ReadByte();
        ushort titleId = r.ReadUInt16();
        uint rankPoint = r.ReadUInt32();
        _ = r.ReadUInt32(); // userIP (anti-cheat / logging only)

        var ch = _state.FindChar(charId, key);
        if (ch is null) { SendDelChar(session, charId, key, true, false); return; }
        var main = _state.FindMapSvr(ch.MainId);
        if (main is null || main != session || !ch.Connections.ContainsKey(session.ServerId))
        { session.Send(BuildInvalidChar(charId, key, false)); return; }

        ch.Name = name; ch.Level = level; ch.RealSex = realSex; ch.Class = cls; ch.Race = race;
        ch.Sex = sex; ch.Face = face; ch.Hair = hair; ch.HelmetHide = helmetHide;
        ch.Country = country; ch.AidCountry = aidCountry; ch.Region = region;
        ch.Channel = channel; ch.MapId = mapId; ch.PosX = posX; ch.PosY = posY; ch.PosZ = posZ;
        ch.Logout = logout != 0; ch.Save = save != 0; ch.TitleId = titleId; ch.RankPoint = rankPoint;

        if (result != 0) { session.Send(BuildInvalidChar(charId, key, false)); CloseChar(ch); return; }

        // Main-server hand-off completion (a regular cross-map-server switch, not a BoW/BR transition): the
        // char was re-loaded on the new main, so reconcile its connection set via MAPSVRLIST rather than
        // running the first-login CHARINFO/ROUTE path. C++ OnMW_ENTERSVR_ACK m_bCHGMainID branch.
        if (ch.ChgMainId != 0 && ch.ChgMainId != Proto.BowServerId && ch.ChgMainId != Proto.BrServerId)
        {
            ch.ChgMainId = 0;
            main.Send(BuildPosReq(Msg.MW_MAPSVRLIST_REQ, ch));
            return;
        }

        if (!string.IsNullOrEmpty(name)) _state.CharactersByName[name] = ch;

        byte duty = 0, peer = 0, camp = 0; ushort castle = 0;
        var guild = _state.FindGuildByChar(charId);
        if (guild is not null)
        {
            ch.Guild = guild;
            var mem = guild.FindMember(charId);
            if (mem is not null) { mem.OnlineChar = ch; mem.Level = level; duty = mem.Duty; peer = mem.Peer; castle = mem.Castle; camp = mem.Camp; }
        }
        var tactics = _state.FindTacticsGuild(charId);

        main.Send(BuildCharInfoReq(ch, guild, tactics, duty, peer, castle, camp));
        main.Send(BuildRouteReq(charId, key, channel, mapId, posX, posY, posZ));
        _log.LogInformation("EnterSvrAck {Char} '{Name}' map={Map} -> CHARINFO_REQ + ROUTE_REQ", charId, name, mapId);
    }

    /// <summary>The map's reply to ENTERSVR_REQ: routing is done. With no extra channel connections we ask
    /// for the character data (→ MW_CHARDATA_REQ); otherwise we open the extra connections first. Ported
    /// from C++ <c>OnMW_ROUTE_ACK</c> — this is the step the Phase-1 flow was missing, which left a real
    /// map stalled right after ENTERSVR_REQ.</summary>
    private void OnMW_ROUTE_ACK(ServerSession session, PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        byte count = r.ReadByte();

        var ch = _state.FindChar(charId, key);
        if (ch is null) { SendDelChar(session, charId, key, true, false); return; }

        if (count == 0) { SendCharDataReq(session, charId, key); return; }

        // Extra channel connections: echo them back as ADDCONNECT_REQ and register each (not yet valid).
        var w = new PacketWriter(Msg.MW_ADDCONNECT_REQ);
        w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteByte(count);
        for (byte i = 0; i < count; i++)
        {
            uint ip = r.ReadUInt32();
            ushort port = r.ReadUInt16();
            byte serverId = r.ReadByte();
            w.WriteUInt32(ip); w.WriteUInt16(port); w.WriteByte(serverId);

            bool prevValid = ch.Connections.TryGetValue(serverId, out var existing) && existing.Valid;
            ch.Connections[serverId] = new CharConnection { ServerId = serverId, IpAddr = ip, Port = port, Valid = prevValid, Ready = false };
        }
        session.Send(w.ToArray());
    }

    private void OnMW_CHARDATA_ACK(ServerSession session, PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        byte startAct = r.ReadByte();
        byte level = r.ReadByte();
        uint maxHp = r.ReadUInt32();
        uint hp = r.ReadUInt32();
        uint maxMp = r.ReadUInt32();
        uint mp = r.ReadUInt32();
        byte country = r.ReadByte();
        byte mode = r.ReadByte();
        byte[] recallBlob = r.ReadRemaining().ToArray();

        var ch = _state.FindChar(charId, key);
        if (ch is null) { SendDelChar(session, charId, key, true, false); return; }

        ch.StartAct = startAct; ch.Level = level; ch.Country = country; ch.Mode = mode;
        ch.MaxHP = maxHp; ch.HP = hp; ch.MaxMP = maxMp; ch.MP = mp;

        // Phase 2: attach guild membership (if any) so ENTERCHAR_REQ carries real guild data.
        var guild = _state.FindGuildByChar(charId);
        if (guild is not null)
        {
            ch.Guild = guild;
            var mem = guild.FindMember(charId);
            if (mem is not null)
            {
                mem.OnlineChar = ch;
                mem.Level = level;
                if (!string.IsNullOrEmpty(mem.Name)) ch.Name = mem.Name; // world learns the name from the roster
            }
        }

        var main = _state.FindMapSvr(ch.MainId);
        if (main is null) { session.Send(BuildInvalidChar(charId, key, false)); return; }

        foreach (var (serverId, con) in ch.Connections.ToList())
        {
            if (con.Ready) continue;
            var map = _state.FindMapSvr(serverId);
            if (map is null) { ch.Connections.Remove(serverId); continue; }
            map.Send(BuildEnterCharReq(ch, startAct, level, country, mode, recallBlob));
        }
        _log.LogInformation("CharData {Char}: lvl={Lvl} guild={G} -> ENTERCHAR_REQ", charId, level, guild?.Name ?? "-");
    }

    private async Task OnMW_ENTERCHAR_ACK(ServerSession session, PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        var ch = _state.FindChar(charId, key);
        if (ch is null) { SendDelChar(session, charId, key, true, false); return; }
        if (!ch.Connections.TryGetValue(session.ServerId, out var con)) { session.Send(BuildInvalidChar(charId, key, false)); return; }

        // Mark this connection loaded; wait until every connection has entered before confirming main.
        con.Ready = true;
        if (ch.Connections.Values.Any(c => !c.Ready)) return;

        // All map connections are ready -> confirm the main server (C++ CheckMainCON), which asks each map
        // to verify it as main; the map replies CHECKMAIN_ACK and we then grant the connection (CONRESULT_REQ).
        CheckMainCON(ch);

        if (!string.IsNullOrEmpty(ch.Name)) _state.CharactersByName[ch.Name] = ch;
        _log.LogInformation("Char {Char} entered world on map {Sid} -> CHECKMAIN_REQ.", charId, session.ServerId);

        // Phase 3: once the char is live on its main map, load + sync friends and soulmate (mirrors the
        // C++ CHARINFO step that fires SendDM_FRIENDLIST_REQ / SendDM_SOULMATELIST_REQ). Done once.
        if (!ch.SocialLoaded && session.ServerId == ch.MainId)
        {
            ch.SocialLoaded = true;
            WarCountryEnter(ch);   // bucket the now-online char for nation balance (C++ does this via SetCharLevel on enter)
            await LoadSoulmatesAsync(ch);
            await LoadFriendsAsync(ch);
        }
    }

    /// <summary>C++ <c>CheckMainCON</c> — once all of a char's map connections have entered, ask each one to
    /// confirm the world's view of the main server (MW_CHECKMAIN_REQ, carrying channel/map/pos).</summary>
    private void CheckMainCON(Character ch)
    {
        foreach (var serverId in ch.Connections.Keys.OrderBy(k => k).ToList())
        {
            var map = _state.FindMapSvr(serverId);
            if (map is null) continue;
            var w = new PacketWriter(Msg.MW_CHECKMAIN_REQ);
            w.WriteUInt32(ch.CharId); w.WriteUInt32(ch.Key); w.WriteByte(ch.Channel); w.WriteUInt16(ch.MapId);
            w.WriteFloat(ch.PosX); w.WriteFloat(ch.PosY); w.WriteFloat(ch.PosZ);
            map.Send(w.ToArray());
        }
    }

    /// <summary>C++ <c>OnMW_CHECKMAIN_ACK</c> — the map confirms the main check. When the responding server is
    /// the char's main, grant the connection with CONRESULT_REQ(CN_SUCCESS): this is the signal that finally
    /// drops the player into the game. Otherwise it's a main-server hand-off (RELEASEMAIN_REQ).</summary>
    private void OnMW_CHECKMAIN_ACK(ServerSession session, PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        var ch = _state.FindChar(charId, key);
        if (ch is null) { SendDelChar(session, charId, key, true, false); return; }
        var main = _state.FindMapSvr(ch.MainId);
        if (main is null) { session.Send(BuildInvalidChar(charId, key, false)); return; }

        if (main == session)
        {
            // Close any connections this teleport/connect cycle made redundant, grant the connection, then
            // advance the per-char ConCess queue so a deferred teleport/connect cycle can run.
            ClearDeadCON(ch);
            session.Send(BuildConResultReq(ch, Proto.CnSuccess));
            _log.LogInformation("Char {Char} connection granted on map {Sid} (CONRESULT CN_SUCCESS).", charId, session.ServerId);
            PopConCess(ch);
        }
        else
        {
            // Main-server hand-off: tell the old main to release, then adopt the responding server as main.
            var w = new PacketWriter(Msg.MW_RELEASEMAIN_REQ);
            w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteByte(ch.Channel); w.WriteUInt16(ch.MapId);
            w.WriteFloat(ch.PosX); w.WriteFloat(ch.PosY); w.WriteFloat(ch.PosZ);
            main.Send(w.ToArray());
            ch.MainId = session.ServerId;
            ch.Save = false;
        }
    }

    /// <summary>MW_CONRESULT_REQ — charId, key, result, then the ascending list of connected map server ids.</summary>
    private static byte[] BuildConResultReq(Character ch, byte result)
    {
        var w = new PacketWriter(Msg.MW_CONRESULT_REQ);
        w.WriteUInt32(ch.CharId); w.WriteUInt32(ch.Key); w.WriteByte(result);
        var ids = ch.Connections.Keys.OrderBy(k => k).ToList();
        w.WriteByte((byte)ids.Count);
        foreach (var id in ids) w.WriteByte(id);
        return w.ToArray();
    }

    private void OnMW_CLOSECHAR_ACK(ServerSession session, PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        var ch = _state.FindChar(charId, key);
        if (ch is null) { SendDelChar(session, charId, key, true, false); return; }
        CloseChar(ch);
    }

    private void OnMW_CHAT_ACK(ServerSession session, byte[] packet)
    {
        foreach (var s in _state.Servers.Values) s.Conn.Send(packet);
    }

    private void OnRW_RELAYSVR_REQ(ServerSession session, PacketReader r)
    {
        session.WId = r.ReadUInt16();
        _state.RelayServer = session;
        _log.LogInformation("Relay server registered (id {Id}).", session.WId);
        // Reply with nation + operators + server messages, then tell every map to connect to the relay.
        SendRelaySvrAck();
    }

    // ===== Phase 2: guild handlers =====

    private async Task OnGuildEstablish(PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        string name = r.ReadString();

        var ch = _state.FindChar(charId, key);
        if (ch is null) return;
        if (ch.Guild is not null) { SendToChar(ch, BuildGuildEstablishReq(charId, key, (byte)GuildResult.HaveGuild, 0, name)); return; }

        int ret = 0;
        uint guildId;
        if (_guildDb is not null)
        {
            try
            {
                var row = await _guildDb.EstablishAsync(name, charId, DateTime.UtcNow);
                ret = row.Ret; guildId = row.GuildId;
            }
            catch (Exception ex) { _log.LogWarning(ex, "TGuildEstablish failed."); SendToChar(ch, BuildGuildEstablishReq(charId, key, (byte)GuildResult.EstablishErr, 0, name)); return; }
        }
        else
        {
            guildId = ++_fakeGuildSeq; // no DB (tests)
        }

        if (ret != 0 || guildId == 0)
        {
            SendToChar(ch, BuildGuildEstablishReq(charId, key, (byte)GuildResult.EstablishErr, 0, name));
            return;
        }

        var guild = new Guild
        {
            Id = guildId, Name = name, Chief = charId, ChiefName = ch.Name, Level = 1,
            Country = ch.Country, TimeEstablish = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            LevelChart = _state.GuildLevelOf(1),
        };
        guild.Members[charId] = new GuildMember
        {
            CharId = charId, Name = ch.Name, Level = ch.Level, Class = ch.Class,
            Duty = (byte)GuildDuty.Chief, OnlineChar = ch,
        };
        _state.Guilds[guildId] = guild;
        _state.CharGuild[charId] = guildId;
        ch.Guild = guild;

        SendToChar(ch, BuildGuildEstablishReq(charId, key, (byte)GuildResult.Success, guildId, name));
        RelayGuildAdd(charId, guildId, charId); // forward to the relay visibility index (no-op without a relay peer)
        _log.LogInformation("Guild '{Name}' (id {Id}) established by char {Char}.", name, guildId, charId);
    }

    private async Task OnGuildDisorg(PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        byte disorg = r.ReadByte();

        var guild = _state.FindGuildByChar(charId);
        if (guild is null || !guild.IsChief(charId)) return;
        guild.Disorg = disorg;
        guild.Time = disorg != 0 ? (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds() : 0;
        if (_guildDb is not null) { try { await _guildDb.DisorgAsync(guild.Id, disorg, guild.Time); } catch (Exception ex) { _log.LogWarning(ex, "TGuildDisorg failed."); } }
        BroadcastToGuild(guild, () => BuildGuildDisorgReq(charId, key, disorg));
    }

    private void OnGuildInvite(PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        string target = r.ReadString();

        var guild = _state.FindGuildByChar(charId);
        if (guild is null) return;
        if (_state.CharactersByName.TryGetValue(target, out var tgt))
            SendToChar(tgt, BuildGuildInviteReq(tgt.CharId, tgt.Key, guild.Name, charId, _state.Characters.TryGetValue(charId, out var inv) ? inv.Name : ""));
    }

    private async Task OnGuildInviteAnswer(PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        byte answer = r.ReadByte();
        uint inviter = r.ReadUInt32();

        var ch = _state.FindChar(charId, key);
        if (ch is null) return;
        var guild = _state.FindGuildByChar(inviter);
        if (answer != Ask.Yes || guild is null || ch.Guild is not null || guild.Members.Count >= guild.MaxMembers)
        {
            SendToChar(ch, BuildGuildJoinReq(charId, key, (byte)GuildResult.JoinDeny, 0, 0, 0, "", 0, "", 0));
            return;
        }

        guild.Members[charId] = new GuildMember
        {
            CharId = charId, Name = ch.Name, Level = ch.Level, Class = ch.Class,
            Duty = (byte)GuildDuty.None, OnlineChar = ch,
        };
        _state.CharGuild[charId] = guild.Id;
        ch.Guild = guild;
        if (_guildDb is not null) { try { await _guildDb.MemberAddAsync(guild.Id, charId, ch.Level, (byte)GuildDuty.None); } catch (Exception ex) { _log.LogWarning(ex, "TGuildMemberAdd failed."); } }

        byte[] join = BuildGuildJoinReq(charId, key, (byte)GuildResult.JoinSuccess, guild.Id, guild.Fame, guild.FameColor, guild.Name, charId, ch.Name, (byte)guild.MaxMembers);
        SendToChar(ch, join);
        var chief = guild.FindMember(guild.Chief)?.OnlineChar;
        if (chief is not null) SendToChar(chief, join);
        _log.LogInformation("Char {Char} joined guild {Id}.", charId, guild.Id);
    }

    private async Task OnGuildLeave(PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        var ch = _state.FindChar(charId, key);
        var guild = _state.FindGuildByChar(charId);
        if (ch is null || guild is null) return;

        var mem = guild.FindMember(charId);
        string name = mem?.Name ?? ch.Name;
        guild.Members.Remove(charId);
        _state.CharGuild.Remove(charId);
        ch.Guild = null;
        if (_guildDb is not null) { try { await _guildDb.LeaveAsync(guild.Id, charId, (byte)GuildResult.LeaveSelf, (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds()); } catch (Exception ex) { _log.LogWarning(ex, "TGuildLeave failed."); } }
        SendToChar(ch, BuildGuildLeaveReq(charId, key, name, (byte)GuildResult.LeaveSelf, 0));
        BroadcastToGuild(guild, () => BuildGuildLeaveReq(charId, key, name, (byte)GuildResult.LeaveSelf, 0));
    }

    private async Task OnGuildKickout(PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        string target = r.ReadString();

        var guild = _state.FindGuildByChar(charId);
        if (guild is null) return;
        var mem = guild.FindMember(target);
        if (mem is null) return;

        uint targetId = mem.CharId;
        var targetChar = mem.OnlineChar;
        guild.Members.Remove(targetId);
        _state.CharGuild.Remove(targetId);
        if (targetChar is not null) targetChar.Guild = null;
        if (_guildDb is not null) { try { await _guildDb.KickoutAsync(guild.Id, targetId); } catch (Exception ex) { _log.LogWarning(ex, "TGuildKickout failed."); } }
        if (targetChar is not null) SendToChar(targetChar, BuildGuildLeaveReq(targetId, targetChar.Key, target, (byte)GuildResult.LeaveKick, 0));
        BroadcastToGuild(guild, () => BuildGuildLeaveReq(targetId, key, target, (byte)GuildResult.LeaveKick, 0));
    }

    private async Task OnGuildDuty(PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        string target = r.ReadString();
        byte duty = r.ReadByte();

        var guild = _state.FindGuildByChar(charId);
        if (guild is null || !guild.IsChief(charId)) return;
        var mem = guild.FindMember(target);
        if (mem is null || mem.CharId == charId || mem.Duty == duty) return;

        if (duty == (byte)GuildDuty.Chief)
        {
            var oldChief = guild.FindMember(guild.Chief);
            if (oldChief is not null)
            {
                oldChief.Duty = (byte)GuildDuty.None;
                if (_guildDb is not null) { try { await _guildDb.DutyAsync(oldChief.CharId, guild.Id, (byte)GuildDuty.None); } catch (Exception ex) { _log.LogWarning(ex, "TGuildDuty(old chief) failed."); } }
            }
            guild.Chief = mem.CharId;
            guild.ChiefName = mem.Name;
            RelayGuildChgMaster(guild.Id, mem.CharId); // relay visibility index
        }
        mem.Duty = duty;
        if (_guildDb is not null) { try { await _guildDb.DutyAsync(mem.CharId, guild.Id, duty); } catch (Exception ex) { _log.LogWarning(ex, "TGuildDuty failed."); } }
        BroadcastToGuild(guild, () => BuildGuildDutyReq(mem.CharId, key, target, duty));
    }

    private async Task OnGuildPeer(PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        string target = r.ReadString();
        byte peer = r.ReadByte();

        var guild = _state.FindGuildByChar(charId);
        if (guild is null || !guild.IsChief(charId)) return;
        var mem = guild.FindMember(target);
        if (mem is null) { SendToCharId(charId, key, BuildGuildPeerReq(charId, key, (byte)GuildResult.Fail, target, peer, 0)); return; }
        byte old = mem.Peer;
        mem.Peer = peer;
        if (_guildDb is not null) { try { await _guildDb.PeerAsync(mem.CharId, guild.Id, peer); } catch (Exception ex) { _log.LogWarning(ex, "TGuildPeer failed."); } }
        BroadcastToGuild(guild, () => BuildGuildPeerReq(charId, key, (byte)GuildResult.Success, target, peer, old));
    }

    private void OnGuildInfo(PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        var guild = _state.FindGuildByChar(charId);
        if (guild is null) { SendToCharId(charId, key, BuildGuildInfoNotFound(charId, key)); return; }
        SendToCharId(charId, key, BuildGuildInfoReq(charId, key, guild));
    }

    private void OnGuildMemberList(PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        var guild = _state.FindGuildByChar(charId);
        if (guild is null) { SendToCharId(charId, key, BuildGuildMemberListNotFound(charId, key)); return; }
        SendToCharId(charId, key, BuildGuildMemberListReq(charId, key, guild));
    }

    // ===== Phase 2: party handlers (in-memory) =====

    private void OnPartyAdd(PacketReader r)
    {
        string request = r.ReadString();
        string target = r.ReadString();
        byte obtainType = r.ReadByte();
        _ = r.ReadUInt32(); _ = r.ReadUInt32(); _ = r.ReadUInt32(); _ = r.ReadUInt32(); // requester hp/mp

        if (!_state.CharactersByName.TryGetValue(request, out var req) ||
            !_state.CharactersByName.TryGetValue(target, out var tgt)) return;
        if (tgt.PartyWaiter || tgt.Party is not null || req.Country != tgt.Country) return;
        if (req.Party is not null && (!req.Party.IsChief(req.CharId) || req.Party.IsFull)) return;

        tgt.PartyWaiter = true;
        SendToChar(tgt, BuildPartyAddReq(tgt.CharId, tgt.Key, request, target, obtainType, Ask.Yes, req.CharId));
    }

    private void OnPartyJoin(PacketReader r)
    {
        string origin = r.ReadString();
        string target = r.ReadString();
        byte obtainType = r.ReadByte();
        byte response = r.ReadByte();
        _ = r.ReadUInt32(); _ = r.ReadUInt32(); _ = r.ReadUInt32(); _ = r.ReadUInt32(); // target hp/mp

        if (!_state.CharactersByName.TryGetValue(origin, out var org) ||
            !_state.CharactersByName.TryGetValue(target, out var tgt)) return;
        tgt.PartyWaiter = false;
        if (response != Ask.Yes || tgt.Party is not null || org.Country != tgt.Country) return;

        Party party;
        if (org.Party is not null)
        {
            if (!org.Party.IsChief(org.CharId) || org.Party.IsFull) return;
            party = org.Party;
        }
        else
        {
            party = new Party { Id = _state.PartyIds.Alloc(), ChiefId = org.CharId, ObtainType = obtainType };
            _state.Parties[party.Id] = party;
            party.AddMember(org);
            SendPartyJoinReq(party, org);   // seed origin into its own new party
            RelayPartyAdd(org.CharId, party.Id, party.ChiefId); // relay visibility index
        }
        party.AddMember(tgt);
        SendPartyJoinReq(party, tgt);
        RelayPartyAdd(tgt.CharId, party.Id, party.ChiefId); // relay visibility index
        _log.LogInformation("Char {T} joined party {P} (chief {C}).", tgt.CharId, party.Id, party.ChiefId);
    }

    private void OnPartyDel(PacketReader r)
    {
        ushort partyId = r.ReadUInt16();
        uint charId = r.ReadUInt32();
        byte kick = r.ReadByte();

        var party = _state.FindParty(partyId);
        if (party is null) return;
        LeaveParty(party, charId, kick);
    }

    /// <summary>Remove a member from a party: pick a new chief if needed, broadcast PARTYDEL to the remaining
    /// members and the leaver, and dissolve the party if it drops to one. Shared by OnPartyDel and arena split.</summary>
    private void LeaveParty(Party party, uint charId, byte kick)
    {
        var leaving = party.FindMember(charId);
        if (leaving is null) return;

        party.NextChiefAfter(charId);
        party.DelMember(charId);
        byte[] del = BuildPartyDelReq(charId, leaving.Key, charId, party.ChiefId, party.CorpsId, party.Id, kick);
        foreach (var m in party.Members) SendToChar(m, del);
        SendToChar(leaving, del);
        RelayPartyDel(charId, party.Id, party.ChiefId); // relay visibility index

        if (party.Size <= 1)
        {
            foreach (var m in party.Members.ToList()) { party.DelMember(m.CharId); }
            _state.Parties.Remove(party.Id);
            _state.PartyIds.Free(party.Id);
        }
    }

    private void OnChgPartyChief(PacketReader r)
    {
        uint chiefId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        uint targetId = r.ReadUInt32();

        var ch = _state.Characters.TryGetValue(chiefId, out var c) ? c : null;
        var party = ch?.Party;
        if (party is null || !party.IsChief(chiefId) || !party.IsMember(targetId))
        {
            if (ch is not null) SendToChar(ch, BuildChgPartyChiefReq(chiefId, key, (byte)GuildResult.Fail));
            return;
        }
        party.ChiefId = targetId;
        foreach (var m in party.Members) SendToChar(m, BuildChgPartyChiefReq(chiefId, key, (byte)GuildResult.Success));
        RelayPartyChgChief(party.Id, party.ChiefId); // relay visibility index
    }

    private void OnChgPartyType(PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        byte type = r.ReadByte();
        var ch = _state.Characters.TryGetValue(charId, out var c) ? c : null;
        var party = ch?.Party;
        if (party is null || !party.IsChief(charId)) return;
        party.ObtainType = type;
        foreach (var m in party.Members) SendToChar(m, BuildChgPartyTypeReq(charId, key, (byte)GuildResult.Success, type));
    }

    private void OnPartyManStat(PacketReader r)
    {
        ushort partyId = r.ReadUInt16();
        uint id = r.ReadUInt32();
        byte type = r.ReadByte();
        byte level = r.ReadByte();
        uint maxHp = r.ReadUInt32();
        uint curHp = r.ReadUInt32();
        uint maxMp = r.ReadUInt32();
        uint curMp = r.ReadUInt32();

        var party = _state.FindParty(partyId);
        var mem = party?.FindMember(id);
        if (party is null || mem is null) return;
        mem.MaxHP = maxHp; mem.HP = curHp; mem.MaxMP = maxMp; mem.MP = curMp; mem.Level = level;
        foreach (var m in party.Members)
            SendToChar(m, BuildPartyManStatReq(m.CharId, m.Key, id, type, level, maxHp, curHp, maxMp, curMp));
    }

    // ===== helpers =====

    private void CloseChar(Character ch)
    {
        if (_state.Bow is not null) BowDeleteFromQueue(ch.CharId, ch.Key);
        if (_state.Br is not null)
        {
            if (_state.Br.FindPlayerInPremade(ch.CharId)) BrErasePlayerFromPremade(ch.CharId);
            BrErasePlayerFromQueue(ch.CharId, ch.Key);
        }
        if (ch.Friends.Count > 0) LeaveFriend(ch);
        if (ch.Soulmates.Count > 0) LeaveSoulmate(ch);
        if (ch.Guild is not null)
        {
            var mem = ch.Guild.FindMember(ch.CharId);
            if (mem is not null) mem.OnlineChar = null; // keep roster, drop online link
            ch.Guild = null;
        }
        if (ch.Party is not null)
        {
            var party = ch.Party;
            party.NextChiefAfter(ch.CharId);
            party.DelMember(ch.CharId);
            if (party.Size <= 1)
            {
                foreach (var m in party.Members.ToList()) party.DelMember(m.CharId);
                _state.Parties.Remove(party.Id);
                _state.PartyIds.Free(party.Id);
            }
        }
        WarCountryLeave(ch);
        if (ch.TmsIds.Count > 0) TmsLeaveAll(ch);
        _state.Characters.Remove(ch.CharId);
        if (!string.IsNullOrEmpty(ch.Name)) _state.CharactersByName.Remove(ch.Name);
        _log.LogInformation("Char {Char} closed.", ch.CharId);

        // Release the account's login lock so a return-to-character-select reconnect isn't rejected as a
        // duplicate login (TLogin's TCURRENTUSER check). The C++ TLogout did this; the .NET port omitted it.
        if (_globalDb is not null) _ = ReleaseLoginLockAsync(ch.UserId);
    }

    private async Task ReleaseLoginLockAsync(uint userId)
    {
        try { await _globalDb!.ReleaseCurrentUserAsync(userId); }
        catch (Exception ex) { _log.LogWarning(ex, "Release login lock (TCURRENTUSER) failed for user {User}.", userId); }
    }

    private ServerSession? MapOf(Character ch) => _state.FindMapSvr(ch.MainId);
    private void SendToChar(Character ch, byte[] packet) => MapOf(ch)?.Send(packet);
    private void SendToCharId(uint charId, uint key, byte[] packet)
    {
        if (_state.FindChar(charId, key) is { } ch) SendToChar(ch, packet);
    }

    /// <summary>Send a freshly-built packet to every online guild member's map server.</summary>
    private void BroadcastToGuild(Guild guild, Func<byte[]> build)
    {
        foreach (var mem in guild.Members.Values)
            if (mem.OnlineChar is { } ch) SendToChar(ch, build());
    }

    private void SendPartyJoinReq(Party party, Character newMember)
    {
        var w = new PacketWriter(Msg.MW_PARTYJOIN_REQ);
        w.WriteUInt32(newMember.CharId);
        w.WriteUInt32(newMember.Key);
        w.WriteUInt16(party.Id);
        w.WriteString(newMember.Name);
        w.WriteUInt32(newMember.CharId);
        w.WriteUInt32(party.ChiefId);
        w.WriteUInt16(party.CorpsId);
        w.WriteString(newMember.Guild?.Name ?? "");
        w.WriteByte(newMember.Level);
        w.WriteUInt32(newMember.MaxHP);
        w.WriteUInt32(newMember.HP);
        w.WriteUInt32(newMember.MaxMP);
        w.WriteUInt32(newMember.MP);
        w.WriteByte(newMember.Race);
        w.WriteByte(newMember.Sex);
        w.WriteByte(newMember.Face);
        w.WriteByte(newMember.Hair);
        w.WriteByte(party.ObtainType);
        w.WriteByte(newMember.Class);
        foreach (var m in party.Members) SendToChar(m, w.ToArray());
    }

    // ----- senders (Phase 1) -----
    private static void SendEnterSvrReq(ServerSession s, bool dbLoad, uint charId, uint key)
    { var w = new PacketWriter(Msg.MW_ENTERSVR_REQ); w.WriteBool(dbLoad); w.WriteUInt32(charId); w.WriteUInt32(key); s.Send(w); }
    private static void SendCharDataReq(ServerSession s, uint charId, uint key)
    { var w = new PacketWriter(Msg.MW_CHARDATA_REQ); w.WriteUInt32(charId); w.WriteUInt32(key); s.Send(w); }
    private static void SendDelChar(ServerSession s, uint charId, uint key, bool logout, bool save)
    { var w = new PacketWriter(Msg.MW_DELCHAR_REQ); w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteBool(logout); w.WriteBool(save); s.Send(w); }
    private static byte[] BuildInvalidChar(uint charId, uint key, bool releaseMain)
    { var w = new PacketWriter(Msg.MW_INVALIDCHAR_REQ); w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteBool(releaseMain); return w.ToArray(); }

    private static byte[] BuildRouteReq(uint charId, uint key, byte channel, ushort mapId, float x, float y, float z)
    {
        var w = new PacketWriter(Msg.MW_ROUTE_REQ);
        w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteByte(channel); w.WriteUInt16(mapId);
        w.WriteFloat(x); w.WriteFloat(y); w.WriteFloat(z);
        return w.ToArray();
    }

    /// <summary>MW_CHARINFO_REQ — guild/tactics/party/title block the client renders (SSSender.cpp).</summary>
    private byte[] BuildCharInfoReq(Character ch, Guild? guild, Guild? tactics, byte duty, byte peer, ushort castle, byte camp)
    {
        var p = ch.Party;
        var w = new PacketWriter(Msg.MW_CHARINFO_REQ);
        w.WriteUInt32(ch.CharId);
        w.WriteUInt32(ch.Key);
        if (guild is not null)
        { w.WriteUInt32(guild.Id); w.WriteByte(guild.Country); w.WriteString(guild.Name); w.WriteUInt32(guild.Fame); w.WriteUInt32(guild.FameColor); }
        else
        { w.WriteUInt32(0); w.WriteByte((byte)Contry.None); w.WriteString(""); w.WriteUInt32(0); w.WriteUInt32(0); }
        if (tactics is not null) { w.WriteUInt32(tactics.Id); w.WriteString(tactics.Name); }
        else { w.WriteUInt32(0); w.WriteString(""); }
        w.WriteByte(duty);
        w.WriteByte(peer);
        w.WriteUInt16(castle);
        w.WriteByte(camp);
        w.WriteUInt16(p?.Id ?? 0);
        w.WriteByte(p?.ObtainType ?? 0);
        w.WriteUInt32(p?.ChiefId ?? 0);
        w.WriteUInt16(ch.TitleId);
        w.WriteUInt32(ch.RankPoint);
        w.WriteBool(false);   // BOWRelease
        return w.ToArray();
    }

    private byte[] BuildEnterCharReq(Character ch, byte startAct, byte level, byte country, byte mode, byte[] recallBlob)
    {
        var g = ch.Guild;
        var gm = g?.FindMember(ch.CharId);
        var p = ch.Party;
        // corps commander (via the party's corps) + tactics (sub-guild) membership
        ushort commander = 0;
        if (p is not null && p.CorpsId != 0 && _state.FindCorps(p.CorpsId) is { } corps) commander = corps.Commander;
        var tacticsGuild = _state.FindTacticsGuild(ch.CharId);
        var w = new PacketWriter(Msg.MW_ENTERCHAR_REQ);
        w.WriteUInt32(ch.CharId);
        w.WriteUInt32(ch.Key);
        w.WriteByte(startAct);
        w.WriteString(ch.Name);
        w.WriteUInt16(ch.MapId);
        w.WriteFloat(ch.PosX);
        w.WriteFloat(ch.PosY);
        w.WriteFloat(ch.PosZ);
        w.WriteUInt32(g?.Id ?? 0);
        w.WriteUInt32(g?.Fame ?? 0);
        w.WriteUInt32(g?.FameColor ?? 0);
        w.WriteString(g?.Name ?? "");
        w.WriteByte(gm?.Duty ?? 0);
        w.WriteByte(gm?.Peer ?? 0);
        w.WriteUInt16(gm?.Castle ?? 0);
        w.WriteByte(gm?.Camp ?? 0);
        w.WriteUInt32(tacticsGuild?.Id ?? 0);   // tactics (sub-guild) id
        w.WriteString(tacticsGuild?.Name ?? ""); // tactics name
        w.WriteUInt16(p?.Id ?? 0);
        w.WriteByte(p?.ObtainType ?? 0);
        w.WriteUInt32(p?.ChiefId ?? 0);
        w.WriteUInt16(commander);                // corps commander party id
        w.WriteByte(level);
        w.WriteByte(ch.HelmetHide);
        w.WriteByte(country);
        w.WriteByte(ch.AidCountry);
        w.WriteByte(mode);
        w.WriteUInt32(ch.Riding);
        w.WriteInt64(ch.ChatBanTime);
        w.WriteUInt32(0); // soulmate (deferred)
        w.WriteUInt32(0); // soul silence (deferred)
        w.WriteString(""); // soulmate name (deferred)
        w.WriteByte(ch.Class);
        w.WriteRaw(recallBlob);
        return w.ToArray();
    }

    // ----- senders (guild) -----
    private static byte[] BuildGuildEstablishReq(uint charId, uint key, byte ret, uint guildId, string name)
    { var w = new PacketWriter(Msg.MW_GUILDESTABLISH_REQ); w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteByte(ret); w.WriteUInt32(guildId); w.WriteString(name); w.WriteByte(1); return w.ToArray(); }

    private static byte[] BuildGuildDisorgReq(uint charId, uint key, byte disorg)
    { var w = new PacketWriter(Msg.MW_GUILDDISORGANIZATION_REQ); w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteByte(disorg); return w.ToArray(); }

    private static byte[] BuildGuildInviteReq(uint charId, uint key, string guildName, uint inviter, string inviterName)
    { var w = new PacketWriter(Msg.MW_GUILDINVITE_REQ); w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteString(guildName); w.WriteUInt32(inviter); w.WriteString(inviterName); return w.ToArray(); }

    private static byte[] BuildGuildJoinReq(uint charId, uint key, byte ret, uint guildId, uint fame, uint fameColor, string guildName, uint memberId, string memberName, byte maxMember)
    { var w = new PacketWriter(Msg.MW_GUILDJOIN_REQ); w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteByte(ret); w.WriteUInt32(guildId); w.WriteUInt32(fame); w.WriteUInt32(fameColor); w.WriteString(guildName); w.WriteUInt32(memberId); w.WriteString(memberName); w.WriteByte(maxMember); return w.ToArray(); }

    private static byte[] BuildGuildLeaveReq(uint charId, uint key, string target, byte reason, uint time)
    { var w = new PacketWriter(Msg.MW_GUILDLEAVE_REQ); w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteString(target); w.WriteByte(reason); w.WriteUInt32(time); return w.ToArray(); }

    private static byte[] BuildGuildDutyReq(uint charId, uint key, string target, byte duty)
    { var w = new PacketWriter(Msg.MW_GUILDDUTY_REQ); w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteString(target); w.WriteByte(duty); return w.ToArray(); }

    private static byte[] BuildGuildPeerReq(uint charId, uint key, byte result, string target, byte peer, byte oldPeer)
    { var w = new PacketWriter(Msg.MW_GUILDPEER_REQ); w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteByte(result); w.WriteString(target); w.WriteByte(peer); w.WriteByte(oldPeer); return w.ToArray(); }

    private static byte[] BuildGuildInfoNotFound(uint charId, uint key)
    { var w = new PacketWriter(Msg.MW_GUILDINFO_REQ); w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteByte((byte)GuildResult.NotFound); return w.ToArray(); }

    private byte[] BuildGuildInfoReq(uint charId, uint key, Guild g)
    {
        var (vice1, vice2) = g.ViceChiefNames();
        var w = new PacketWriter(Msg.MW_GUILDINFO_REQ);
        w.WriteUInt32(charId);
        w.WriteUInt32(key);
        w.WriteByte((byte)GuildResult.Success);
        w.WriteUInt32(g.Id);
        w.WriteString(g.Name);
        w.WriteInt64(g.TimeEstablish);
        w.WriteUInt16((ushort)g.Members.Count);
        w.WriteUInt16(g.MaxMembers);
        w.WriteString(g.ChiefName);
        w.WriteByte(g.ChiefPeer());
        w.WriteString(vice1);
        w.WriteString(vice2);
        w.WriteByte(g.Level);
        w.WriteUInt32(g.Fame);
        w.WriteUInt32(g.FameColor);
        w.WriteUInt32(g.GI);
        w.WriteUInt32(g.Exp);
        w.WriteUInt32(g.NextLevelExp);
        w.WriteByte(g.GPoint);
        w.WriteByte(g.Status);
        w.WriteUInt32(g.Gold);
        w.WriteUInt32(g.Silver);
        w.WriteUInt32(g.Cooper);
        w.WriteByte(g.FindDuty(charId));
        w.WriteByte(g.FindPeer(charId));
        w.WriteString(g.ArticleTitle);
        w.WriteUInt32(g.PvPTotalPoint);
        w.WriteUInt32(g.PvPUseablePoint);
        w.WriteUInt32(g.PvPMonthPoint);
        w.WriteUInt32(g.RankTotal);
        w.WriteUInt32(g.RankMonth);
        w.WriteByte(g.StatLevel);
        w.WriteByte(g.StatPoint);
        w.WriteUInt32(g.StatExp);
        return w.ToArray();
    }

    private static byte[] BuildGuildMemberListNotFound(uint charId, uint key)
    { var w = new PacketWriter(Msg.MW_GUILDMEMBERLIST_REQ); w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteByte((byte)GuildResult.NotFound); return w.ToArray(); }

    private static byte[] BuildGuildMemberListReq(uint charId, uint key, Guild g)
    {
        var w = new PacketWriter(Msg.MW_GUILDMEMBERLIST_REQ);
        w.WriteUInt32(charId);
        w.WriteUInt32(key);
        w.WriteByte((byte)GuildResult.Success);
        w.WriteUInt32(g.Id);
        w.WriteString(g.Name);
        w.WriteUInt16((ushort)g.Members.Count);
        foreach (var m in g.Members.Values)
        {
            w.WriteUInt32(m.CharId);
            w.WriteString(m.Name);
            w.WriteByte(m.OnlineChar?.Level ?? m.Level);
            w.WriteByte(m.Class);
            w.WriteByte(m.Duty);
            w.WriteByte(m.Peer);
            w.WriteBool(m.Online);
            w.WriteUInt32(m.OnlineChar?.Region ?? 0);
            w.WriteUInt16(m.Castle);
            w.WriteByte(m.Camp);
            w.WriteUInt32(m.Tactics);
            w.WriteByte(m.WarCountry);
            w.WriteInt64(m.ConnectedDate);
        }
        return w.ToArray();
    }

    // ----- senders (party) -----
    private static byte[] BuildPartyAddReq(uint charId, uint key, string request, string target, byte obtainType, byte result, uint requestId)
    { var w = new PacketWriter(Msg.MW_PARTYADD_REQ); w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteString(request); w.WriteString(target); w.WriteByte(obtainType); w.WriteByte(result); w.WriteUInt32(requestId); return w.ToArray(); }

    private static byte[] BuildPartyDelReq(uint charId, uint key, uint targetId, uint chief, ushort commander, ushort partyId, byte kick)
    { var w = new PacketWriter(Msg.MW_PARTYDEL_REQ); w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteUInt32(targetId); w.WriteUInt32(chief); w.WriteUInt16(commander); w.WriteUInt16(partyId); w.WriteByte(kick); return w.ToArray(); }

    private static byte[] BuildChgPartyChiefReq(uint charId, uint key, byte ret)
    { var w = new PacketWriter(Msg.MW_CHGPARTYCHIEF_REQ); w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteByte(ret); return w.ToArray(); }

    private static byte[] BuildChgPartyTypeReq(uint charId, uint key, byte ret, byte partyType)
    { var w = new PacketWriter(Msg.MW_CHGPARTYTYPE_REQ); w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteByte(ret); w.WriteByte(partyType); return w.ToArray(); }

    private static byte[] BuildPartyManStatReq(uint charId, uint key, uint id, byte type, byte level, uint maxHp, uint curHp, uint maxMp, uint curMp)
    { var w = new PacketWriter(Msg.MW_PARTYMANSTAT_REQ); w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteUInt32(id); w.WriteByte(type); w.WriteByte(level); w.WriteUInt32(maxHp); w.WriteUInt32(curHp); w.WriteUInt32(maxMp); w.WriteUInt32(curMp); return w.ToArray(); }
}
