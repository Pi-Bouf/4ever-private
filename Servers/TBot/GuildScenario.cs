using TLogin.Protocol;

namespace TBot;

/// <summary>
/// <c>--Bot:Scenario=guild</c>: a guild's life through the map's guild relay, end to end with the live world. A (made level 20)
/// founds "TbotGuild", invites B, B says yes; A opens the guild window, makes B a vice-chief and gives a peerage, then puts B out
/// and disbands. A disbanded guild only goes after 7 days in the world, so the scenario takes it out of the database and
/// restarts the world (<c>docker compose restart worldsvr</c>) to forget it; the bots' level and guild-leave marks are put back.
/// </summary>
public static partial class Scenarios
{
    private const string TestGuild = "TbotGuild";
    private const ushort CS_GUILDESTABLISH_REQ = M + 0x0060, CS_GUILDESTABLISH_ACK = M + 0x0061, CS_GUILDDISORGANIZATION_REQ = M + 0x0062,
        CS_GUILDDISORGANIZATION_ACK = M + 0x0063, CS_GUILDINVITE_REQ = M + 0x0064, CS_GUILDINVITE_ACK = M + 0x0065,
        CS_GUILDINVITEANSWER_REQ = M + 0x0066, CS_GUILDJOIN_ACK = M + 0x0067, CS_GUILDKICKOUT_REQ = M + 0x0069,
        CS_GUILDLEAVE_ACK = M + 0x006A, CS_GUILDDUTY_REQ = M + 0x006B, CS_GUILDDUTY_ACK = M + 0x006C, CS_GUILDMEMBERLIST_REQ = M + 0x006D,
        CS_GUILDMEMBERLIST_ACK = M + 0x006E, CS_GUILDATTR_ACK = M + 0x006F, CS_GUILDPEER_REQ = M + 0x0070, CS_GUILDPEER_ACK = M + 0x0071,
        CS_GUILDINFO_REQ = M + 0x0072, CS_GUILDINFO_ACK = M + 0x0073;

    private static async Task<int> RunGuildAsync(BotConfig cfg)
    {
        var db = new GameDb(cfg.GameConnectionString);
        var cfgA = Clone(cfg, cfg.Account, "[A]");
        var cfgB = Clone(cfg, cfg.Account2, "[B]");
        uint idA = await FirstChar(db, cfg.Account), idB = await FirstChar(db, cfg.Account2);
        const string Cols = "SELECT bLevel, bGuildLeave, dwGuildLeaveTime FROM TCHARTABLE WHERE dwCharID=@p0";
        var savedA = await db.RowAsync(Cols, (int)idA);
        var savedB = await db.RowAsync(Cols, (int)idB);
        try
        {
            await ClearTestGuild(db);
            await db.ExecAsync("UPDATE TCHARTABLE SET bLevel=20 WHERE dwCharID=@p0", (int)idA);
            await GuildLife(cfgA, cfgB);
        }
        finally
        {
            await Task.Delay(2500);                                              // the logout saves land first
            await ClearTestGuild(db);
            foreach (var (id, row) in new[] { (idA, savedA), (idB, savedB) })
                if (row is not null)
                    await db.ExecAsync("UPDATE TCHARTABLE SET bLevel=@p1, bGuildLeave=@p2, dwGuildLeaveTime=@p3 WHERE dwCharID=@p0",
                        (int)id, row[0], row[1], row[2]);
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] fixtures restored; restarting the world ...");
            await Docker("compose restart worldsvr");
        }
        return PrintResults();
    }

    private static int PrintResults()
    {
        Console.WriteLine();
        Console.WriteLine("==== results ====");
        foreach (var (name, ok, detail) in Results)
            Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}{(detail.Length > 0 ? "  — " + detail : "")}");
        int failed = Results.Count(r => !r.Ok);
        Console.WriteLine($"{Results.Count - failed}/{Results.Count} passed");
        return failed == 0 ? 0 : 1;
    }

    /// <summary>Takes the test guild out of every table that has a guild id.</summary>
    private static Task ClearTestGuild(GameDb db) => db.ExecAsync($@"DECLARE @ids TABLE (id int);
INSERT INTO @ids SELECT dwID FROM TGUILDTABLE WHERE szName = '{TestGuild}';
DECLARE @s nvarchar(max) = N'';
SELECT @s = @s + N'DELETE FROM ' + QUOTENAME(OBJECT_NAME(object_id)) + N' WHERE dwGuildID IN (SELECT dwID FROM TGUILDTABLE WHERE szName = ''{TestGuild}'');'
  FROM sys.columns WHERE name = 'dwGuildID' AND OBJECTPROPERTY(object_id, 'IsUserTable') = 1;
EXEC (@s);
DELETE FROM TGUILDTABLE WHERE szName = '{TestGuild}'");

    private static async Task GuildLife(BotConfig cfgA, BotConfig cfgB)
    {
        using var a = new Bot(cfgA);
        using var b = new Bot(cfgB);
        a.Enter(); b.Enter();
        Thread.Sleep(1500);
        string nameA = a.Spawn.Name, nameB = b.Spawn.Name;

        // Founding.
        a.Send(Req(CS_GUILDESTABLISH_REQ, w => w.WriteString(TestGuild)));
        var est = a.TryWait(CS_GUILDESTABLISH_ACK, timeoutMs: 5000);
        var (res, guildId, gname) = est is null ? default : Read(est, r => (r.ReadByte(), r.ReadUInt32(), r.ReadString()));
        Check("guild: A founds a guild (CS_GUILDESTABLISH_ACK success)", est is not null && res == 0 && guildId != 0 && gname == TestGuild, Describe(est));
        var attr = b.TryWait(CS_GUILDATTR_ACK, r => r.ReadUInt32() == a.CharId, 5000);
        Check("guild: … B, nearby, sees A's new guild (CS_GUILDATTR_ACK)", attr is not null
            && Read(attr, r => { r.ReadUInt32(); uint g = r.ReadUInt32(); r.ReadUInt32(); r.ReadUInt32(); string n = r.ReadString(); r.ReadByte(); r.ReadUInt32(); r.ReadString(); return (g, n); })
               == (guildId, TestGuild), Describe(attr));

        // Invite, answer, join.
        a.Send(Req(CS_GUILDINVITE_REQ, w => w.WriteString(nameB)));
        var inv = b.TryWait(CS_GUILDINVITE_ACK, timeoutMs: 5000);
        Check("guild: A invites B — B is asked (CS_GUILDINVITE_ACK)", inv is not null
            && Read(inv, r => (r.ReadByte(), r.ReadString(), r.ReadUInt32(), r.ReadString())) == (0, TestGuild, a.CharId, nameA), Describe(inv));
        b.Send(Req(CS_GUILDINVITEANSWER_REQ, w => { w.WriteByte(0); w.WriteUInt32(a.CharId); }));     // ASK_YES
        (byte, uint, string, uint, string, byte)? Join(Bot bot) => bot.TryWait(CS_GUILDJOIN_ACK, timeoutMs: 5000) is { } j
            ? Read(j, r => (r.ReadByte(), r.ReadUInt32(), r.ReadString(), r.ReadUInt32(), r.ReadString(), r.ReadByte())) : null;
        var joinB = Join(b);
        var joinA = Join(a);
        Check("guild: B says yes — both are told B joined (CS_GUILDJOIN_ACK)", joinB is { } jb && jb.Item2 == guildId && jb.Item4 == b.CharId
            && joinA is { } ja && ja.Item4 == b.CharId, $"B={joinB} A={joinA}");
        var attrB = a.TryWait(CS_GUILDATTR_ACK, r => r.ReadUInt32() == b.CharId, 5000);
        Check("guild: … and A sees B wear the guild (CS_GUILDATTR_ACK)", attrB is not null
            && Read(attrB, r => { r.ReadUInt32(); uint g = r.ReadUInt32(); r.ReadUInt32(); r.ReadUInt32(); r.ReadString(); r.ReadByte(); r.ReadUInt32(); r.ReadString(); return g; }) == guildId,
            Describe(attrB));

        // The guild window.
        a.Send(Req(CS_GUILDINFO_REQ, _ => { }));
        Check("guild: the guild window's info comes (CS_GUILDINFO_ACK)", a.TryWait(CS_GUILDINFO_ACK, timeoutMs: 5000) is not null, "no reply");
        a.Send(Req(CS_GUILDMEMBERLIST_REQ, _ => { }));
        Check("guild: … and its member list (CS_GUILDMEMBERLIST_ACK)", a.TryWait(CS_GUILDMEMBERLIST_ACK, timeoutMs: 5000) is not null, "no reply");

        // Duty and peerage.
        a.Send(Req(CS_GUILDDUTY_REQ, w => { w.WriteString(nameB); w.WriteByte(1); }));
        var duty = b.TryWait(CS_GUILDDUTY_ACK, timeoutMs: 5000);
        Check("guild: A makes B a vice-chief (CS_GUILDDUTY_ACK)", duty is not null
            && Read(duty, r => (r.ReadByte(), r.ReadString(), r.ReadByte())) == (0, nameB, 1), Describe(duty));
        a.Send(Req(CS_GUILDPEER_REQ, w => { w.WriteString(nameB); w.WriteByte(1); }));
        var peer = b.TryWait(CS_GUILDPEER_ACK, timeoutMs: 5000);
        Check("guild: A gives B a peerage (CS_GUILDPEER_ACK)", peer is not null
            && Read(peer, r => (r.ReadByte(), r.ReadString(), r.ReadByte(), r.ReadByte())) is var pr && pr.Item1 == 0 && pr.Item2 == nameB && pr.Item3 == 1,
            Describe(peer));

        // Put out, then disband.
        b.Discard(CS_GUILDATTR_ACK);
        a.Send(Req(CS_GUILDKICKOUT_REQ, w => w.WriteString(nameB)));
        var left = b.TryWait(CS_GUILDLEAVE_ACK, timeoutMs: 5000);
        Check("guild: A puts B out — B is told (CS_GUILDLEAVE_ACK, GUILD_LEAVE_KICK)", left is not null
            && Read(left, r => (r.ReadByte(), r.ReadString(), r.ReadByte())) == (0, nameB, 13), Describe(left));
        var bare = b.TryWait(CS_GUILDATTR_ACK, r => r.ReadUInt32() == b.CharId, 5000);
        Check("guild: … and B no longer wears it (CS_GUILDATTR_ACK, no guild)", bare is not null
            && Read(bare, r => { r.ReadUInt32(); uint g = r.ReadUInt32(); r.ReadUInt32(); r.ReadUInt32(); r.ReadString(); r.ReadByte(); r.ReadUInt32(); r.ReadString(); return g; }) == 0,
            Describe(bare));
        a.Send(Req(CS_GUILDDISORGANIZATION_REQ, w => w.WriteByte(1)));
        var dis = a.TryWait(CS_GUILDDISORGANIZATION_ACK, timeoutMs: 5000);
        Check("guild: A disbands it (CS_GUILDDISORGANIZATION_ACK — gone in 7 days)", dis is not null && Read(dis, r => r.ReadByte()) == 1, Describe(dis));
    }
}
