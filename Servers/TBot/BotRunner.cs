using TLogin.Protocol;

namespace TBot;

/// <summary>
/// Drives one bot through the full flow: (optional account insert) → login server (login, group/channel/
/// char list, create char if needed, start) → map server (connect, conready, parse spawn) → walk around.
/// </summary>
public sealed class BotRunner
{
    private readonly BotConfig _cfg;
    public BotRunner(BotConfig cfg) => _cfg = cfg;

    private static readonly TimeSpan Step = TimeSpan.FromSeconds(5);

    public async Task RunAsync(CancellationToken ct)
    {
        if (_cfg.CreateAccount)
        {
            var prov = new AccountProvisioner(_cfg.GlobalConnectionString);
            bool created = await prov.EnsureAccountAsync(_cfg.Account, _cfg.Password, ct);
            Log(created ? $"account '{_cfg.Account}' created" : $"account '{_cfg.Account}' already exists");
        }

        if (_cfg.AccountOnly)
        {
            Log($"account-only mode: '{_cfg.Account}' is ready to log into. done.");
            return;
        }

        var login = RunLogin();
        RunGameAndWalk(login, ct);
        Log("done.");
    }

    // ---- login server -------------------------------------------------------------------------------

    private sealed record LoginResultData(uint UserId, uint CharId, uint Key, string WorldHost, ushort WorldPort);

    private LoginResultData RunLogin()
    {
        Log($"connecting to login {_cfg.LoginHost}:{_cfg.LoginPort} ...");
        using var conn = new BotConnection(_cfg.LoginHost, _cfg.LoginPort, !_cfg.NoCrypt);

        // CS_LOGIN_REQ — order from CTClientWnd::SendCS_LOGIN_REQ (zombies are empty strings).
        ushort version = _cfg.OverrideVersion != 0 ? _cfg.OverrideVersion : Proto.ClientVersion;
        var loginReq = new PacketWriter(Msg.CS_LOGIN_REQ);
        loginReq.WriteUInt16(version);
        loginReq.WriteString("");                                  // Zombie3
        loginReq.WriteString(Sha1Util.UpperHex(_cfg.Password));    // password (uppercase SHA1 hex)
        loginReq.WriteString("");                                  // Zombie1
        loginReq.WriteString("");                                  // Zombie2
        loginReq.WriteString(_cfg.Account);
        loginReq.WriteInt64(0);                                    // dlCheck (exe checksum; ignored)
        loginReq.WriteInt64(Proto.ComputeLoginChecksum(Proto.ClientVersion));
        conn.Send(loginReq);

        var ack = Expect(conn, Msg.CS_LOGIN_ACK, "CS_LOGIN_ACK");
        byte result = ack.ReadByte();
        uint userId = ack.ReadUInt32();
        uint charId = ack.ReadUInt32();
        uint key = ack.ReadUInt32();
        var lr = (LoginResult)result;
        Log($"login result = {lr} (userId={userId}, charId={charId})");

        if (lr == LoginResult.NeedAgreement)
        {
            conn.Send(new PacketWriter(Msg.CS_AGREEMENT_REQ));
            Log("sent CS_AGREEMENT_REQ");
        }
        else if (lr != LoginResult.Success)
        {
            throw new InvalidOperationException($"login failed: {lr}");
        }

        // Mirror the client: group list then channel list (the server gates char/start on agreement).
        conn.Send(new PacketWriter(Msg.CS_GROUPLIST_REQ));
        Drain(conn, Msg.CS_GROUPLIST_ACK, "CS_GROUPLIST_ACK");

        var chReq = new PacketWriter(Msg.CS_CHANNELLIST_REQ);
        chReq.WriteByte(_cfg.GroupId);
        conn.Send(chReq);
        Drain(conn, Msg.CS_CHANNELLIST_ACK, "CS_CHANNELLIST_ACK");

        // Char list — capture the first character (charId + slot) if any.
        var clReq = new PacketWriter(Msg.CS_CHARLIST_REQ);
        clReq.WriteByte(_cfg.GroupId);
        conn.Send(clReq);
        var clAck = Expect(conn, Msg.CS_CHARLIST_ACK, "CS_CHARLIST_ACK");
        clAck.ReadUInt32();                 // GetCheckFilePoint
        byte count = clAck.ReadByte();
        uint firstCharId = 0;
        if (count > 0)
        {
            firstCharId = clAck.ReadUInt32();   // dwID (first char)
            string name = clAck.ReadString();
            clAck.ReadByte();                   // bStartAct
            byte slot = clAck.ReadByte();       // bSlotID
            Log($"existing characters: {count}; using '{name}' (charId={firstCharId}, slot={slot})");
        }
        else
        {
            Log("no characters on this account");
        }

        if (firstCharId == 0)
            firstCharId = CreateCharacter(conn);

        // Optionally relocate the bot char to another char's region/spot (e.g. next to Pittt) before START,
        // so it spawns in the main-world region instead of the country-4 newbie zone. Same login session, so
        // no duplicate. Done via the bot's own DB connection (the account-creation channel) — not a GM teleport.
        if (_cfg.MatchPositionOfCharId > 0 && _cfg.GameConnectionString.Length > 0)
        {
            var spot = new GameDb(_cfg.GameConnectionString)
                .MatchPositionAsync(_cfg.MatchPositionOfCharId, firstCharId).GetAwaiter().GetResult();
            Log($"placed char {firstCharId} at char {_cfg.MatchPositionOfCharId}'s spot: " +
                $"map {spot.MapId} region {spot.Region} pos ({spot.X:0.0}, {spot.Y:0.0}, {spot.Z:0.0})");
        }

        // CS_START_REQ → CS_START_ACK gives the map/world server address to connect to.
        var startReq = new PacketWriter(Msg.CS_START_REQ);
        startReq.WriteByte(_cfg.GroupId);
        startReq.WriteByte(_cfg.Channel);
        startReq.WriteUInt32(firstCharId);
        conn.Send(startReq);

        var startAck = Expect(conn, Msg.CS_START_ACK, "CS_START_ACK");
        byte sr = startAck.ReadByte();
        uint mapIp = startAck.ReadUInt32();
        ushort port = startAck.ReadUInt16();
        byte serverId = startAck.ReadByte();
        var startResult = (StartResult)sr;
        if (startResult != StartResult.Success)
            throw new InvalidOperationException($"start failed: {startResult}");

        string worldHost = UIntToIp(mapIp);
        Log($"start ok → game server {worldHost}:{port} (serverId={serverId})");
        return new LoginResultData(userId, firstCharId, key, worldHost, port);
    }

    private uint CreateCharacter(BotConnection conn)
    {
        Log($"creating character '{_cfg.CharName}' in slot {_cfg.CharSlot} ...");
        var w = new PacketWriter(Msg.CS_CREATECHAR_REQ);
        w.WriteByte(_cfg.GroupId);
        w.WriteString(_cfg.CharName);
        w.WriteByte(_cfg.CharSlot);
        w.WriteByte(_cfg.Class);
        w.WriteByte(_cfg.Race);
        w.WriteByte(_cfg.Country);
        w.WriteByte(_cfg.Sex);
        w.WriteByte(_cfg.Hair);
        w.WriteByte(_cfg.Face);
        w.WriteByte(_cfg.Body);
        w.WriteByte(_cfg.Pants);
        w.WriteByte(_cfg.Hand);
        w.WriteByte(_cfg.Foot);
        w.WriteByte(_cfg.LevelOption);
        conn.Send(w);

        // Happy path: a CS_CREATECHAR_ACK with the new id.
        var ack = conn.TryReceive(Step);
        if (ack is { } a && a.Id == Msg.CS_CREATECHAR_ACK)
        {
            byte result = a.ReadByte();
            uint charId = a.ReadUInt32();
            var cr = (CreateResult)result;
            if (cr == CreateResult.Success) { Log($"created character (charId={charId})"); return charId; }
            throw new InvalidOperationException($"create char failed: {cr}");
        }

        // Fallback: the .NET login persists the char then throws (e.g. a secondary-insert 2627) and sends
        // no ack. The row exists, so re-list and take it.
        Log("no CS_CREATECHAR_ACK; re-listing characters ...");
        var clReq = new PacketWriter(Msg.CS_CHARLIST_REQ);
        clReq.WriteByte(_cfg.GroupId);
        conn.Send(clReq);
        var cl = Expect(conn, Msg.CS_CHARLIST_ACK, "CS_CHARLIST_ACK");
        cl.ReadUInt32();              // GetCheckFilePoint
        if (cl.ReadByte() == 0)       // count
            throw new InvalidOperationException("character creation failed (none present after create)");
        uint id = cl.ReadUInt32();
        Log($"character present after create (charId={id})");
        return id;
    }

    // ---- map server: connect, enter the world, then walk (one connection) ---------------------------

    private void RunGameAndWalk(LoginResultData login, CancellationToken ct)
    {
        Log($"connecting to game server {login.WorldHost}:{login.WorldPort} (crypt={(!_cfg.MapNoCrypt)}) ...");
        using var conn = new BotConnection(login.WorldHost, login.WorldPort, !_cfg.MapNoCrypt);

        CharSpawn spawn = ConnectAndEnter(conn, login);
        Walk(conn, spawn, ct);
        // `using` disposes the socket here → clean disconnect from the map server.
        Log("disconnected from game server");
    }

    /// <summary>
    /// CS_CONNECT_REQ → wait for CS_CONNECT_ACK (the map only sends it after the map↔world
    /// ENTERSVR→…→CHECKMAIN→CONRESULT handshake) → CS_CONREADY_REQ → wait for CS_CHARINFO_ACK.
    /// The player is locked server-side until CONNECT_ACK, so CONREADY must NOT be sent before it.
    /// </summary>
    private CharSpawn ConnectAndEnter(BotConnection conn, LoginResultData login)
    {
        uint clientIp = Proto.IpToUInt(login.WorldHost); // mirrors the client passing its target address
        conn.Send(GamePackets.BuildConnect(Proto.ClientVersion, _cfg.Channel, login.UserId, login.CharId,
            login.Key, clientIp, login.WorldPort));
        Log("sent CS_CONNECT_REQ; waiting for CS_CONNECT_ACK / CS_CHARINFO_ACK ...");

        // CS_CONNECT_ACK (after the map↔world handshake) and CS_CHARINFO_ACK can arrive in either order,
        // so collect both in one loop: send CONREADY when CONNECT_ACK lands, capture spawn from CHARINFO.
        bool readied = false;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            var r = conn.TryReceive(Step);
            if (r is null) continue;

            if (r.Id == GameMsg.CS_CONNECT_ACK)
            {
                byte result = r.ReadByte();
                if (result != 0) // CN_SUCCESS == 0
                    throw new InvalidOperationException($"map rejected connect (CS_CONNECT_ACK result={result})");
                byte count = r.ReadByte();
                for (int i = 0; i < count; i++) r.ReadByte(); // bSvrID list (routing only; one socket here)
                conn.Send(GamePackets.BuildConReady());
                readied = true;
                Log($"connect accepted ({count} server(s)); sent CS_CONREADY_REQ");
            }
            else if (r.Id == GameMsg.CS_CHARINFO_ACK)
            {
                var spawn = GamePackets.ParseCharInfo(conn.LastPacket!);
                if (!readied) conn.Send(GamePackets.BuildConReady()); // be safe if CHARINFO led
                Log($"in world: '{spawn.Name}' lvl {spawn.Level} @ map {spawn.MapId} " +
                    $"pos ({spawn.X:0.0}, {spawn.Y:0.0}, {spawn.Z:0.0}) dir {spawn.Dir}");
                return spawn;
            }
            // ignore add-connect / enter / nearby-entity / chat / etc.
        }
        throw new TimeoutException("never received CS_CHARINFO_ACK");
    }

    // ---- movement -----------------------------------------------------------------------------------

    private void Walk(BotConnection conn, CharSpawn spawn, CancellationToken ct)
    {
        Log($"walking a {_cfg.MoveRadius:0}-unit loop for {_cfg.MoveDurationSec}s (speed {_cfg.MoveSpeed}) ...");
        float x = spawn.X, z = spawn.Z;

        float y = spawn.Y;
        float r = _cfg.MoveRadius;
        // Four corners of a square around the spawn point.
        var waypoints = new (float X, float Z)[]
        {
            (spawn.X + r, spawn.Z),
            (spawn.X + r, spawn.Z + r),
            (spawn.X - r, spawn.Z + r),
            (spawn.X - r, spawn.Z - r),
        };
        int wp = 0;

        var end = DateTime.UtcNow + TimeSpan.FromSeconds(_cfg.MoveDurationSec);
        float speed = Math.Min(_cfg.MoveSpeed, 3.4f); // hard cap: anti-cheat closes the session above 3.40
        int sent = 0;

        while (DateTime.UtcNow < end && !ct.IsCancellationRequested)
        {
            var (tx, tz) = waypoints[wp];
            float dx = tx - x, dz = tz - z;
            float dist = MathF.Sqrt(dx * dx + dz * dz);
            if (dist <= _cfg.MoveStep)
            {
                x = tx; z = tz;
                wp = (wp + 1) % waypoints.Length;
            }
            else
            {
                x += dx / dist * _cfg.MoveStep;
                z += dz / dist * _cfg.MoveStep;
            }

            ushort dir = (ushort)(((int)(MathF.Atan2(dz, dx) * (1800f / MathF.PI)) % 3600 + 3600) % 3600);
            conn.Send(GamePackets.BuildMove(spawn.MapId, x, y, z, 0, dir, 4, 4, 0, 0, speed));
            sent++;

            // Drain anything the server pushes back (move acks, nearby entities); detect a kick.
            try
            {
                while (conn.TryReceive(TimeSpan.FromMilliseconds(1)) is { }) { }
            }
            catch (IOException)
            {
                Log($"connection closed by server after {sent} moves (kicked?)");
                return;
            }

            Thread.Sleep(_cfg.MoveTickMs);
        }
        Log($"sent {sent} movement packets without being kicked");
    }

    // ---- helpers ------------------------------------------------------------------------------------

    private static PacketReader Expect(BotConnection conn, ushort id, string name)
    {
        var r = conn.Receive(Step);
        if (r.Id != id)
            throw new InvalidOperationException($"expected {name} (0x{id:X4}) but got 0x{r.Id:X4}");
        return r;
    }

    // Read until we see the wanted id (tolerates incidental packets before it); logs and returns.
    private static void Drain(BotConnection conn, ushort id, string name)
    {
        var deadline = DateTime.UtcNow + Step;
        while (DateTime.UtcNow < deadline)
        {
            var r = conn.TryReceive(Step);
            if (r is null) break;
            if (r.Id == id) { return; }
        }
    }

    private static string UIntToIp(uint v) => $"{v & 0xFF}.{(v >> 8) & 0xFF}.{(v >> 16) & 0xFF}.{(v >> 24) & 0xFF}";

    private static void Log(string msg) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {msg}");
}
