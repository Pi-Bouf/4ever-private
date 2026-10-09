using System.Diagnostics;
using System.Net.Sockets;
using TLogin.Protocol;

namespace TBot;

/// <summary>
/// <c>--Bot:Scenario=castle</c>: a real castle war on Chesed (castle 4, map 801), end to end. The database has no guilds, so the
/// scenario makes two — A the chief of the defenders "TBotBlue", B the chief of the attackers "TBotRed" — and restarts the world
/// (<c>docker compose restart worldsvr</c>) so it loads them. It then names them Chesed's two sides (the control server's
/// <c>CT_CASTLEGUILDCHG_REQ</c>), the chiefs sign themselves up, the war is forced on, and the bots play it: they come in with
/// their camps and see the god balls and towers, A mounts a defence ball (the balance tips), B knocks it off and mounts an attack
/// ball, and the war ends on time — the stronger side wins, the castle is saved under its guild, everyone hears it, and at normal
/// everyone is sent out. Everything is put back after, and the world and map are restarted to forget the guilds.
/// <para>Needs the docker stack (it restarts containers) and a fort war not due in the next 3 minutes (signing up for a castle
/// closes once its forts' war comes before the castle's).</para>
/// </summary>
public static partial class Scenarios
{
    private const uint BlueGuild = 990001, RedGuild = 990002;
    private const ushort Chesed = 4, CastleMap = 801, CastleTower1 = 1, CastleTower2 = 2, DefBall = 1, AtkBall = 5;
    private const ushort CS_CASTLEAPPLY_REQ = M + 0x00D3, CS_CASTLEAPPLY_ACK = M + 0x00D4, CS_ADDGODTOWER_ACK = M + 0x01A8,
        CS_ADDGODBALL_ACK = M + 0x01AA, CS_TAKEGODBALL_REQ = M + 0x01AC, CS_TAKEGODBALL_ACK = M + 0x01AD,
        CS_MOUNTGODBALL_REQ = M + 0x01B0, CS_MOUNTGODBALL_ACK = M + 0x01B1, CS_DEMOUNTGODBALL_REQ = M + 0x01B2,
        CS_DEMOUNTGODBALL_ACK = M + 0x01B3, CS_BALANCEOFPOWER_ACK = M + 0x01B4, CS_ENDWAR_ACK = M + 0x01EB;
    private static readonly string[] CastleTables = { "TCASTLETABLE", "TLOCALOCCUPYTABLE", "TCASTLEAPPLICANTTABLE" };

    private static async Task<int> RunCastleAsync(BotConfig cfg)
    {
        var db = new GameDb(cfg.GameConnectionString);
        var cfgA = Clone(cfg, cfg.Account, "[A]");
        var cfgB = Clone(cfg, cfg.Account2, "[B]");
        uint idA = await FirstChar(db, cfg.Account), idB = await FirstChar(db, cfg.Account2);
        var posA = await db.RowAsync("SELECT wMapID, fPosX, fPosY, fPosZ, wLastSpawnID FROM TCHARTABLE WHERE dwCharID=@p0", (int)idA);
        var posB = await db.RowAsync("SELECT wMapID, fPosX, fPosY, fPosZ, wLastSpawnID FROM TCHARTABLE WHERE dwCharID=@p0", (int)idB);
        try
        {
            foreach (var t in CastleTables)
                await db.ExecAsync($"IF OBJECT_ID('TBOT_BAK_{t}') IS NULL SELECT * INTO TBOT_BAK_{t} FROM {t}");
            await ClearGuilds(db);
            await db.ExecAsync($@"SET IDENTITY_INSERT TGUILDTABLE ON;
INSERT INTO TGUILDTABLE (dwID, szName, dwChief, bLevel, dwFame, dwFameColor, bMaxCabinet, dwGold, dwSilver, dwCooper, dwGI, dwExp,
    bGPoint, bStatus, bDisorg, dwTime, timeEstablish, dwPvPTotalPoint, dwPvPUseablePoint, dwPvPMonthPoint)
VALUES ({BlueGuild}, 'TBotBlue', @p0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, GETDATE(), 0, 0, 0),
       ({RedGuild}, 'TBotRed', @p1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, GETDATE(), 0, 0, 0);
SET IDENTITY_INSERT TGUILDTABLE OFF;
INSERT INTO TGUILDMEMBERTABLE (dwCharID, dwGuildID, bDuty, bPeer, dwService) VALUES (@p0, {BlueGuild}, 2, 0, 0), (@p1, {RedGuild}, 2, 0, 0)",
                (int)idA, (int)idB);
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] guild fixtures in; restarting the world ...");
            await Docker("compose restart worldsvr");
            await WaitForWorld();

            long warTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 180;
            WorldTool.CastleGuildChg(Chesed, BlueGuild, RedGuild, warTime);
            Thread.Sleep(500);
            await CastleWar(cfgA, cfgB, db, idA, idB);
        }
        finally
        {
            WorldTool.BattleStatus(WorldTool.BtCastle, WorldTool.BsNormal, 0, 0);
            await Task.Delay(2500);                                          // the logout saves land first
            await ClearGuilds(db);
            await db.ExecAsync(@"DELETE FROM TITEMTABLE WHERE bStorageType=2 AND dwStorageID IN
                (SELECT dwPostID FROM TPOSTTABLE WHERE dwCharID IN (@p0, @p1) AND szTitle='Castle reward');
                DELETE FROM TPOSTITEMTABLE WHERE dwPostID IN (SELECT dwPostID FROM TPOSTTABLE WHERE dwCharID IN (@p0, @p1) AND szTitle='Castle reward');
                DELETE FROM TPOSTTABLE WHERE dwCharID IN (@p0, @p1) AND szTitle='Castle reward'", (int)idA, (int)idB);
            foreach (var t in CastleTables)
                await db.ExecAsync($"IF OBJECT_ID('TBOT_BAK_{t}') IS NOT NULL BEGIN DELETE FROM {t}; INSERT INTO {t} SELECT * FROM TBOT_BAK_{t}; DROP TABLE TBOT_BAK_{t} END");
            foreach (var (id, p) in new[] { (idA, posA), (idB, posB) })
                if (p is not null)
                    await db.ExecAsync("UPDATE TCHARTABLE SET wMapID=@p1, fPosX=@p2, fPosY=@p3, fPosZ=@p4, wLastSpawnID=@p5 WHERE dwCharID=@p0",
                        (int)id, p[0], p[1], p[2], p[3], p[4]);
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] fixtures restored; restarting the world and the map ...");
            await Docker("compose restart worldsvr mapsvr");
        }

        Console.WriteLine();
        Console.WriteLine("==== results ====");
        foreach (var (name, ok, detail) in Results)
            Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}{(detail.Length > 0 ? "  — " + detail : "")}");
        int failed = Results.Count(r => !r.Ok);
        Console.WriteLine($"{Results.Count - failed}/{Results.Count} passed");
        return failed == 0 ? 0 : 1;
    }

    /// <summary>Takes the test guilds out of every table that has a guild id.</summary>
    private static Task ClearGuilds(GameDb db) => db.ExecAsync($@"DECLARE @s nvarchar(max) = N'';
SELECT @s = @s + N'DELETE FROM ' + QUOTENAME(OBJECT_NAME(object_id)) + N' WHERE dwGuildID IN ({BlueGuild}, {RedGuild});'
  FROM sys.columns WHERE name = 'dwGuildID' AND OBJECTPROPERTY(object_id, 'IsUserTable') = 1;
EXEC (@s);
DELETE FROM TGUILDTABLE WHERE dwID IN ({BlueGuild}, {RedGuild})");

    private static async Task CastleWar(BotConfig cfgA, BotConfig cfgB, GameDb db, uint idA, uint idB)
    {
        // 1. The chiefs sign themselves up, wherever they are.
        using (var a = new Bot(cfgA))
        using (var b = new Bot(cfgB))
        {
            a.Enter(); b.Enter();
            Thread.Sleep(1000);
            foreach (var (bot, camp) in new[] { (a, (byte)1), (b, (byte)2) })
            {
                bot.Send(Req(CS_CASTLEAPPLY_REQ, w => { w.WriteUInt16(Chesed); w.WriteUInt32(bot.CharId); }));
                var ack = bot.TryWait(CS_CASTLEAPPLY_ACK, timeoutMs: 5000);
                Check($"castle war: the {(camp == 1 ? "defenders'" : "attackers'")} chief signs up for Chesed (CS_CASTLEAPPLY_ACK)",
                    ack is not null && Read(ack, r => (r.ReadByte(), r.ReadUInt16(), r.ReadUInt32(), r.ReadByte())) == (0, Chesed, bot.CharId, camp),
                    Describe(ack));
            }
        }
        await Task.Delay(2500);

        // 2. The war, with both of them in the castle: A by the defenders' ball spots, B by the attackers'.
        await db.ExecAsync("UPDATE TCHARTABLE SET wMapID=@p1, fPosX=225.0, fPosY=171.5, fPosZ=265.0, wLastSpawnID=@p2 WHERE dwCharID=@p0",
            (int)idA, (int)CastleMap, (int)BindSpawn);
        await db.ExecAsync("UPDATE TCHARTABLE SET wMapID=@p1, fPosX=585.0, fPosY=116.0, fPosZ=546.0, wLastSpawnID=@p2 WHERE dwCharID=@p0",
            (int)idB, (int)CastleMap, (int)BindSpawn);
        WorldTool.BattleStatus(WorldTool.BtCastle, WorldTool.BsBattle, 0, 600);
        Thread.Sleep(500);
        using (var a = new Bot(cfgA))
        using (var b = new Bot(cfgB))
        {
            a.Enter(); b.Enter();
            foreach (var (bot, camp) in new[] { (a, (byte)1), (b, (byte)2) })
            {
                var enter = bot.TryWait(CS_ENTERCASTLE_ACK, timeoutMs: 8000);
                Check($"castle war: a signed-up {(camp == 1 ? "defender" : "attacker")} comes in with its camp (CS_ENTERCASTLE_ACK)",
                    enter is not null && Read(enter, r => (r.ReadUInt16(), r.ReadByte(), r.ReadString(), r.ReadString())) == (Chesed, camp, "TBotRed", "TBotBlue"),
                    Describe(enter));
            }
            Thread.Sleep(1000);
            int balls = a.Received.Count(p => PacketHeader.ReadId(p) == CS_ADDGODBALL_ACK);
            int towers = a.Received.Count(p => PacketHeader.ReadId(p) == CS_ADDGODTOWER_ACK);
            Check("castle war: … and sees the two sides' god balls and the four towers", balls >= 4 && towers == 4, $"balls={balls} towers={towers}");

            // 3. A carries a defence ball to a tower: the balance tips to the defenders.
            a.Send(Req(CS_TAKEGODBALL_REQ, w => w.WriteUInt16(DefBall)));
            var taken = b.TryWait(CS_TAKEGODBALL_ACK, timeoutMs: 5000);
            Check("castle war: A picks up a defence god ball — B sees it (CS_TAKEGODBALL_ACK)",
                taken is not null && Read(taken, r => (r.ReadUInt32(), r.ReadUInt16())) == (a.CharId, DefBall), Describe(taken));
            a.Send(Req(CS_MOUNTGODBALL_REQ, w => w.WriteUInt16(CastleTower1)));
            var mounted = b.TryWait(CS_MOUNTGODBALL_ACK, timeoutMs: 5000);
            Check("castle war: … and mounts it on a tower (CS_MOUNTGODBALL_ACK)", mounted is not null
                && Read(mounted, r => (r.ReadUInt16(), r.ReadUInt16(), r.ReadByte(), r.ReadUInt32())) == (CastleTower1, DefBall, 1, a.CharId), Describe(mounted));
            Thread.Sleep(3500);
            b.Discard(CS_BALANCEOFPOWER_ACK);
            var balance = b.TryWait(CS_BALANCEOFPOWER_ACK, timeoutMs: 3000);
            float share = balance is null ? 0 : Read(balance, r =>
            {
                float s = r.ReadFloat(); r.ReadUInt32(); r.ReadUInt16(); r.ReadUInt16();
                for (int i = 0; i < 4; i++) r.ReadString();
                for (int i = 0; i < 4; i++) r.ReadUInt16();
                return s;
            });
            Check("castle war: every second the balance of power tips to the defenders (CS_BALANCEOFPOWER_ACK)", share > 50f, $"defence {share}%");

            // 4. B takes an attack ball, knocks the defence ball off, and mounts its own on another tower.
            b.Send(Req(CS_TAKEGODBALL_REQ, w => w.WriteUInt16(AtkBall)));
            b.TryWait(CS_TAKEGODBALL_ACK, timeoutMs: 3000);
            b.Send(Req(CS_DEMOUNTGODBALL_REQ, w => w.WriteUInt16(CastleTower1)));
            var off = a.TryWait(CS_DEMOUNTGODBALL_ACK, timeoutMs: 5000);
            Check("castle war: B, carrying an attack ball, knocks the defence ball off the tower (CS_DEMOUNTGODBALL_ACK)",
                off is not null && Read(off, r => (r.ReadUInt16(), r.ReadUInt32())) == (CastleTower1, b.CharId), Describe(off));
            b.Send(Req(CS_MOUNTGODBALL_REQ, w => w.WriteUInt16(CastleTower2)));
            var own = a.TryWait(CS_MOUNTGODBALL_ACK, r => r.ReadUInt16() == CastleTower2, 5000);
            Check("castle war: … and mounts its own on another tower", own is not null
                && Read(own, r => (r.ReadUInt16(), r.ReadUInt16(), r.ReadByte(), r.ReadUInt32())) == (CastleTower2, AtkBall, 2, b.CharId), Describe(own));
            Thread.Sleep(2000);

            // 5. Time is up: the stronger side wins, the castle is saved under its guild, everyone hears it.
            a.Discard(CS_SYSTEMMSG_ACK); a.Discard(CS_LEAVECASTLE_ACK); b.Discard(CS_LEAVECASTLE_ACK);
            WorldTool.BattleStatus(WorldTool.BtCastle, WorldTool.BsPeace, 0, 300);
            var end = a.TryWait(CS_ENDWAR_ACK, timeoutMs: 5000);
            var result = end is null ? default : Read(end, r =>
            {
                var head = (Type: r.ReadByte(), Win: r.ReadUInt32(), Def: r.ReadUInt32(), Atk: r.ReadUInt32());
                r.ReadString(); r.ReadUInt32(); r.ReadUInt16(); r.ReadUInt16();     // the defenders' name, power, fort bonus, kills
                r.ReadString(); r.ReadUInt32(); r.ReadUInt16(); r.ReadUInt16();     // the attackers'
                return head;
            });
            uint expected = result.Atk > result.Def ? RedGuild : BlueGuild;
            Check("castle war: time is up (WIN_TIME): the stronger side wins (CS_ENDWAR_ACK)", end is not null && result.Type == 1
                && result.Win == expected, end is null ? "no CS_ENDWAR_ACK" : $"win={result.Win} def={result.Def} atk={result.Atk}");
            var news = a.TryWait(CS_SYSTEMMSG_ACK, r => r.ReadByte() == 16, 8000);
            string winner = expected == RedGuild ? "TBotRed" : "TBotBlue";
            Check("castle war: … everyone hears who took the castle (SM_CASTLE_END)", news is not null
                && Read(news, r => { r.ReadByte(); return (r.ReadString(), r.ReadString()); }) == ("Chesed Castle", winner), Describe(news));
            await Task.Delay(1000);
            var saved = await db.RowAsync("SELECT dwGuildID, bCountry FROM TCASTLETABLE WHERE wCastle=@p0", (int)Chesed);
            Check("saved: Chesed belongs to the winning guild now, with its chief's country (TCASTLETABLE)", saved is not null
                && Convert.ToUInt32(saved[0]) == expected && Convert.ToInt32(saved[1]) == 1, saved is null ? "no row" : $"guild={saved[0]} country={saved[1]}");
            var mail = await db.RowAsync("SELECT COUNT(*) FROM TPOSTTABLE WHERE dwCharID=@p0 AND szTitle='Castle reward'", (int)(expected == RedGuild ? idB : idA));
            Check("castle war: the winners' chief in the castle gets the castle's reward by mail", mail is not null && Convert.ToInt32(mail[0]) == 1,
                $"posts={mail?[0]}");

            // 6. The capture ends both guilds' sign-ups (the world's ResetCastleApply): both are sent out of the castle.
            Check("castle war: the capture ends both guilds' sign-ups: both are sent out of the castle (CS_LEAVECASTLE_ACK)",
                a.TryWait(CS_LEAVECASTLE_ACK, timeoutMs: 8000) is not null && b.TryWait(CS_LEAVECASTLE_ACK, timeoutMs: 8000) is not null, "not sent out");
            WorldTool.BattleStatus(WorldTool.BtCastle, WorldTool.BsNormal, 0, 0);
        }
    }

    private static async Task Docker(string args)
    {
        string root = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(root, "docker-compose.yml")) && Directory.GetParent(root) is { } up) root = up.FullName;
        using var p = Process.Start(new ProcessStartInfo("docker", args) { WorkingDirectory = root, UseShellExecute = false })!;
        await p.WaitForExitAsync();
    }

    /// <summary>Waits for the world to listen again, then for the maps to reconnect to it.</summary>
    private static async Task WaitForWorld()
    {
        for (int i = 0; i < 60; i++)
        {
            try { using var tcp = new TcpClient(WorldTool.Host, WorldTool.Port); break; }
            catch (SocketException) { await Task.Delay(1000); }
        }
        await Task.Delay(10000);
    }
}
