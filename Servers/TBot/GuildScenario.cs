using TLogin.Protocol;

namespace TBot;

/// <summary>
/// <c>--Bot:Scenario=guild</c>: a guild's life through the map's guild relay, end to end with the live world. A (made level 20)
/// founds "TbotGuild", invites B, B says yes; A opens the guild window, makes B a vice-chief and gives a peerage, then puts B out
/// and disbands. A disbanded guild only goes after 7 days in the world, so the scenario takes it out of the database and
/// restarts the world (<c>docker compose restart worldsvr</c>) to forget it; the bots' level and guild-leave marks are put back.
/// Then the mercenaries, PvP point rewards and the guard shop on a fixture guild (GuildTacticsScenario.cs).
/// </summary>
public static partial class Scenarios
{
    private const string TestGuild = "TbotGuild";
    private const ushort CS_GUILDESTABLISH_REQ = M + 0x0060, CS_GUILDESTABLISH_ACK = M + 0x0061, CS_GUILDDISORGANIZATION_REQ = M + 0x0062,
        CS_GUILDDISORGANIZATION_ACK = M + 0x0063, CS_GUILDINVITE_REQ = M + 0x0064, CS_GUILDINVITE_ACK = M + 0x0065,
        CS_GUILDINVITEANSWER_REQ = M + 0x0066, CS_GUILDJOIN_ACK = M + 0x0067, CS_GUILDKICKOUT_REQ = M + 0x0069,
        CS_GUILDLEAVE_ACK = M + 0x006A, CS_GUILDDUTY_REQ = M + 0x006B, CS_GUILDDUTY_ACK = M + 0x006C, CS_GUILDMEMBERLIST_REQ = M + 0x006D,
        CS_GUILDMEMBERLIST_ACK = M + 0x006E, CS_GUILDATTR_ACK = M + 0x006F, CS_GUILDPEER_REQ = M + 0x0070, CS_GUILDPEER_ACK = M + 0x0071,
        CS_GUILDINFO_REQ = M + 0x0072, CS_GUILDINFO_ACK = M + 0x0073, CS_UPDATEITEM_ACK = M + 0x002A,
        CS_GUILDCABINETLIST_REQ = M + 0x0159, CS_GUILDCABINETLIST_ACK = M + 0x015A, CS_GUILDCABINETPUTIN_REQ = M + 0x015B,
        CS_GUILDCABINETPUTIN_ACK = M + 0x015C, CS_GUILDCABINETTAKEOUT_REQ = M + 0x015D, CS_GUILDCABINETTAKEOUT_ACK = M + 0x015E,
        CS_GUILDCONTRIBUTION_REQ = M + 0x015F, CS_GUILDCONTRIBUTION_ACK = M + 0x0160, CS_GUILDARTICLELIST_ACK = M + 0x0162,
        CS_GUILDARTICLEADD_REQ = M + 0x0163, CS_GUILDARTICLEADD_ACK = M + 0x0164, CS_GUILDARTICLEDEL_REQ = M + 0x0165,
        CS_GUILDARTICLEDEL_ACK = M + 0x0166, CS_GUILDFAME_REQ = M + 0x0167, CS_GUILDFAME_ACK = M + 0x0168,
        CS_GUILDARTICLEUPDATE_REQ = M + 0x0169, CS_GUILDARTICLEUPDATE_ACK = M + 0x016A, CS_GUILDWANTEDADD_REQ = M + 0x016B,
        CS_GUILDWANTEDADD_ACK = M + 0x016C, CS_GUILDWANTEDLIST_REQ = M + 0x016F, CS_GUILDWANTEDLIST_ACK = M + 0x0170,
        CS_GUILDVOLUNTEERING_REQ = M + 0x0171, CS_GUILDVOLUNTEERING_ACK = M + 0x0172, CS_GUILDVOLUNTEERLIST_REQ = M + 0x0175,
        CS_GUILDVOLUNTEERLIST_ACK = M + 0x0176, CS_GUILDVOLUNTEERREPLY_REQ = M + 0x0177, CS_GUILDPOINTLOG_REQ = M + 0x01E3,
        CS_GUILDPOINTLOG_ACK = M + 0x01E4, CS_GUILDPVPRECORD_REQ = M + 0x01E7, CS_GUILDPVPRECORD_ACK = M + 0x01E8,
        CS_GUILDSKILLACTION_REQ = M + 0x039A, CS_GUILDSKILLACTION_ACK = M + 0x039B, CS_GUILDSKILLUPDATE_ACK = M + 0x039C;
    // A stack the guild cabinet takes (TITEMCHART bIsSell has ITEMTRADE_CABINET), put in A's backpack for the run.
    private const ushort CabinetItem = 1573;
    private const byte CabinetItemSlot = 43;
    private const long CabinetItemDlId = 900_000_004;

    private static async Task<int> RunGuildAsync(BotConfig cfg)
    {
        var db = new GameDb(cfg.GameConnectionString);
        var cfgA = Clone(cfg, cfg.Account, "[A]");
        var cfgB = Clone(cfg, cfg.Account2, "[B]");
        uint idA = await FirstChar(db, cfg.Account), idB = await FirstChar(db, cfg.Account2);
        const string Cols = "SELECT bLevel, bGuildLeave, dwGuildLeaveTime, dwGold, dwSilver, dwCooper FROM TCHARTABLE WHERE dwCharID=@p0";
        var savedA = await db.RowAsync(Cols, (int)idA);
        var savedB = await db.RowAsync(Cols, (int)idB);
        try
        {
            await ClearTestGuild(db);
            await ClearMemberGuildSkills(db, idA, idB);
            await db.ExecAsync("UPDATE TCHARTABLE SET bLevel=20, dwGold=0, dwSilver=5, dwCooper=0 WHERE dwCharID=@p0", (int)idA);
            await ClearCabinetItem(db, idA);
            await db.ExecAsync(@"INSERT INTO TITEMTABLE (dlID, bStorageType, dwStorageID, bOwnerType, dwOwnerID, bItemID, wItemID, bLevel,
                bCount, bGLevel, dwDuraMax, dwDuraCur, bRefineCur, dEndTime, bGradeEffect, bMagic1, bMagic2, bMagic3, bMagic4, bMagic5,
                bMagic6, wValue1, wValue2, wValue3, wValue4, wValue5, wValue6, dwTime1, dwTime2, dwTime3, dwTime4, dwTime5, dwTime6,
                bGem, wMoggItemID)
                VALUES (@p0, 0, 255, 0, @p1, @p2, @p3, 0, 5, 0, 0, 0, 0, '1900-01-01', 0, 0,0,0,0,0,0, 0,0,0,0,0,0, 0,0,0,0,0,0, 0, 0)",
                CabinetItemDlId, (int)idA, (int)CabinetItemSlot, (int)CabinetItem);
            await GuildLife(cfgA, cfgB, db);
        }
        finally
        {
            await Task.Delay(2500);                                              // the logout saves land first
            await ClearTestGuild(db);
            await ClearMemberGuildSkills(db, idA, idB);
            await ClearCabinetItem(db, idA);
            foreach (var (id, row) in new[] { (idA, savedA), (idB, savedB) })
                if (row is not null)
                    await db.ExecAsync("UPDATE TCHARTABLE SET bLevel=@p1, bGuildLeave=@p2, dwGuildLeaveTime=@p3, dwGold=@p4, dwSilver=@p5, dwCooper=@p6 WHERE dwCharID=@p0",
                        (int)id, row[0], row[1], row[2], row[3], row[4], row[5]);
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] fixtures restored; restarting the world ...");
            await Docker("compose restart worldsvr");
        }
        await RunGuildTacticsAsync(cfg, db, idA, idB);              // G3: GuildTacticsScenario.cs
        return PrintResults();
    }

    /// <summary>The bots' own guild skills (a founder holds them all), from <c>TGUILDMEMBERSKILLTABLE</c>.</summary>
    private static Task ClearMemberGuildSkills(GameDb db, uint idA, uint idB)
        => db.ExecAsync("DELETE FROM TGUILDMEMBERSKILLTABLE WHERE dwCharID IN (@p0, @p1)", (int)idA, (int)idB);

    /// <summary>A CS_GUILDSKILLUPDATE_ACK read: (skill, level, end).</summary>
    private static List<(ushort Id, byte Level, long End)> GuildSkillList(PacketReader p) => Read(p, r =>
    {
        var list = new List<(ushort, byte, long)>();
        for (byte n = r.ReadByte(), i = 0; i < n; i++) list.Add((r.ReadUInt16(), r.ReadByte(), r.ReadInt64()));
        return list;
    });

    /// <summary>The run's cabinet stack, wherever it went (A's bags, or the guild's cabinet — that goes with the guild).</summary>
    private static Task ClearCabinetItem(GameDb db, uint idA)
        => db.ExecAsync("DELETE FROM TITEMTABLE WHERE wItemID=@p1 AND ((bOwnerType=0 AND dwOwnerID=@p0) OR dlID=@p2)", (int)idA, (int)CabinetItem, CabinetItemDlId);

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
DELETE FROM TITEMTABLE WHERE bOwnerType = 1 AND dwOwnerID IN (SELECT id FROM @ids);   -- its cabinet (TOWNER_GUILD)
DECLARE @s nvarchar(max) = N'';
SELECT @s = @s + N'DELETE FROM ' + QUOTENAME(OBJECT_NAME(object_id)) + N' WHERE dwGuildID IN (SELECT dwID FROM TGUILDTABLE WHERE szName = ''{TestGuild}'');'
  FROM sys.columns WHERE name = 'dwGuildID' AND OBJECTPROPERTY(object_id, 'IsUserTable') = 1;
EXEC (@s);
DELETE FROM TGUILDTABLE WHERE szName = '{TestGuild}'");

    private static async Task GuildLife(BotConfig cfgA, BotConfig cfgB, GameDb db)
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
        var founderSkills = a.TryWait(CS_GUILDSKILLUPDATE_ACK, r => r.ReadByte() > 0, 5000);
        var fs = founderSkills is null ? new() : GuildSkillList(founderSkills);
        Check("guild skills: the founder holds all 10 at level 1, not yet renewed (CS_GUILDSKILLUPDATE_ACK)",
            fs.Count == 10 && fs.All(x => x.Level == 1 && x.End == 0), Describe(founderSkills));
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

        // ---- G2: the board ----
        a.Send(Req(CS_GUILDARTICLEADD_REQ, w => { w.WriteString("Welcome"); w.WriteString("Be nice"); }));
        var added = a.TryWait(CS_GUILDARTICLEADD_ACK, timeoutMs: 5000);
        var board = a.TryWait(CS_GUILDARTICLELIST_ACK, timeoutMs: 5000);
        var post = board is null ? default : Read(board, r =>
        {
            byte n = r.ReadByte(); uint id = r.ReadUInt32(); r.ReadByte(); string writer = r.ReadString(); string title = r.ReadString();
            r.ReadString(); string date = r.ReadString();
            return (n, id, writer, title, date);
        });
        Check("guild board: A posts an article — the board shows it, written by A, dated", added is not null && Read(added, r => r.ReadByte()) == 0
            && post.n == 1 && post.writer == nameA && post.title == "Welcome" && post.date.Length == 10, $"{Describe(added)} {post}");
        a.Send(Req(CS_GUILDARTICLEUPDATE_REQ, w => { w.WriteUInt32(post.id); w.WriteString("Welcome!"); w.WriteString("Be very nice"); }));
        Check("guild board: … edits it (CS_GUILDARTICLEUPDATE_ACK)", a.TryWait(CS_GUILDARTICLEUPDATE_ACK, timeoutMs: 5000) is { } up && Read(up, r => r.ReadByte()) == 0, "no reply");
        a.Send(Req(CS_GUILDARTICLEDEL_REQ, w => w.WriteUInt32(post.id)));
        var del = a.TryWait(CS_GUILDARTICLEDEL_ACK, timeoutMs: 5000);
        var empty = a.TryWait(CS_GUILDARTICLELIST_ACK, r => r.ReadByte() == 0, 5000);
        Check("guild board: … and takes it down — the board is empty", del is not null && Read(del, r => r.ReadByte()) == 0 && empty is not null, Describe(del));

        // ---- contribution and fame ----
        a.Discard(CS_MONEY_ACK);
        a.Send(Req(CS_GUILDCONTRIBUTION_REQ, w => { w.WriteUInt32(0); w.WriteUInt32(0); w.WriteUInt32(2); w.WriteUInt32(0); w.WriteUInt32(0); }));
        var gave = a.TryWait(CS_GUILDCONTRIBUTION_ACK, timeoutMs: 5000);
        var purse = a.TryWait(CS_MONEY_ACK, timeoutMs: 3000);
        Check("guild: A gives the guild 2 silver — they leave A's purse (CS_GUILDCONTRIBUTION_ACK, CS_MONEY_ACK 3 silver)", gave is not null
            && Read(gave, r => r.ReadByte()) == 0 && purse is not null && Read(purse, r => { var m = (r.ReadUInt32(), r.ReadUInt32(), r.ReadUInt32()); while (r.Remaining > 0) r.ReadByte(); return m; }) == (0u, 3u, 0u),
            $"{Describe(gave)} {Describe(purse)}");
        a.Send(Req(CS_GUILDFAME_REQ, w => { w.WriteUInt32(1); w.WriteUInt32(1); }));
        var fame = a.TryWait(CS_GUILDFAME_ACK, timeoutMs: 5000);
        Check("guild: a fame mark costs the guild 30000 PvP points — refused for a new guild (GUILD_NOPOINT)", fame is not null
            && Read(fame, r => { byte f = r.ReadByte(); while (r.Remaining > 0) r.ReadByte(); return f; }) == 21, Describe(fame));

        // ---- the cabinet ----
        a.Send(Req(CS_GUILDCABINETPUTIN_REQ, w => { w.WriteByte(InvenBackpack); w.WriteByte(CabinetItemSlot); w.WriteByte(2); }));
        var closed = a.TryWait(CS_GUILDCABINETPUTIN_ACK, timeoutMs: 5000);
        var back = a.TryWait(CS_ADDITEM_ACK, timeoutMs: 3000) ?? a.TryWait(CS_UPDATEITEM_ACK, timeoutMs: 1000);
        Check("guild cabinet: a level-1 guild has none (GUILD_CABINET_LEVEL) — the items come back", closed is not null
            && Read(closed, r => r.ReadByte()) == 5 && back is not null, $"{Describe(closed)} back={back is not null}");
        await db.ExecAsync($"UPDATE TGUILDTABLE SET bMaxCabinet=10 WHERE szName='{TestGuild}'");
        a.Discard(CS_GUILDCABINETLIST_ACK);
        a.Send(Req(CS_GUILDCABINETPUTIN_REQ, w => { w.WriteByte(InvenBackpack); w.WriteByte(CabinetItemSlot); w.WriteByte(2); }));
        var put = a.TryWait(CS_GUILDCABINETPUTIN_ACK, timeoutMs: 5000);
        var shelf = a.TryWait(CS_GUILDCABINETLIST_ACK, timeoutMs: 5000);
        var stored = shelf is null ? default : Read(shelf, r => { r.ReadByte(); byte n = r.ReadByte(); uint st = n > 0 ? r.ReadUInt32() : 0; ushort item = n > 0 ? r.ReadUInt16() : (ushort)0; while (r.Remaining > 0) r.ReadByte(); return (n, st, item); });
        Check("guild cabinet: (a bigger guild) A puts 2 in — the cabinet lists them", put is not null && Read(put, r => r.ReadByte()) == 0
            && stored.n == 1 && stored.item == CabinetItem, $"{Describe(put)} {stored}");
        var row = await db.RowAsync($"SELECT bCount FROM TITEMTABLE WHERE bOwnerType=1 AND bStorageType=1 AND dwOwnerID=(SELECT dwID FROM TGUILDTABLE WHERE szName='{TestGuild}')");
        Check("saved: the cabinet holds 2 (TITEMTABLE, guild-owned)", row is not null && Convert.ToInt32(row[0]) == 2, row is null ? "no row" : $"count={row[0]}");
        a.Discard(CS_ADDITEM_ACK); a.Discard(CS_UPDATEITEM_ACK);
        a.Send(Req(CS_GUILDCABINETTAKEOUT_REQ, w => { w.WriteUInt32(stored.st); w.WriteByte(1); w.WriteByte(InvenBackpack); w.WriteByte(CabinetItemSlot); }));
        var took = a.TryWait(CS_GUILDCABINETTAKEOUT_ACK, timeoutMs: 5000);
        var inBag = a.TryWait(CS_UPDATEITEM_ACK, timeoutMs: 3000) ?? a.TryWait(CS_ADDITEM_ACK, timeoutMs: 1000);
        Check("guild cabinet: … takes 1 back out, into its bag", took is not null && Read(took, r => r.ReadByte()) == 0 && inBag is not null, Describe(took));
        await Task.Delay(500);
        row = await db.RowAsync($"SELECT bCount FROM TITEMTABLE WHERE bOwnerType=1 AND bStorageType=1 AND dwOwnerID=(SELECT dwID FROM TGUILDTABLE WHERE szName='{TestGuild}')");
        Check("saved: the cabinet holds 1 now", row is not null && Convert.ToInt32(row[0]) == 1, row is null ? "no row" : $"count={row[0]}");

        // ---- the records ----
        a.Send(Req(CS_GUILDPOINTLOG_REQ, _ => { }));
        Check("guild: the point log opens (CS_GUILDPOINTLOG_ACK)", a.TryWait(CS_GUILDPOINTLOG_ACK, timeoutMs: 5000) is not null, "no reply");
        a.Send(Req(CS_GUILDPVPRECORD_REQ, w => w.WriteByte(0)));
        Check("guild: … and the members' PvP record (CS_GUILDPVPRECORD_ACK)", a.TryWait(CS_GUILDPVPRECORD_ACK, timeoutMs: 5000) is not null, "no reply");

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
        // ---- G2: recruiting — a wanted post, B applies, A takes B back ----
        a.Send(Req(CS_GUILDWANTEDADD_REQ, w => { w.WriteUInt32(0); w.WriteString("Join us"); w.WriteString("All welcome"); w.WriteByte(1); w.WriteByte(99); }));
        var wanted = a.TryWait(CS_GUILDWANTEDADD_ACK, timeoutMs: 5000);
        Check("guild recruiting: A puts up a wanted post (CS_GUILDWANTEDADD_ACK)", wanted is not null && Read(wanted, r => r.ReadByte()) == 0, Describe(wanted));
        b.Send(Req(CS_GUILDWANTEDLIST_REQ, _ => { }));
        var posts = b.TryWait(CS_GUILDWANTEDLIST_ACK, timeoutMs: 5000);
        var seen = posts is null ? default : Read(posts, r =>
        {
            uint n = r.ReadUInt32();
            for (uint i = 0; i < n; i++)
            {
                uint g = r.ReadUInt32(); string gn = r.ReadString(); r.ReadString(); r.ReadString(); r.ReadByte(); r.ReadByte(); r.ReadInt64(); r.ReadByte();
                if (g == guildId) { while (r.Remaining > 0) r.ReadByte(); return (true, gn); }
            }
            return (false, "");
        });
        Check("guild recruiting: B, of the same country, sees it", seen == (true, TestGuild), Describe(posts));
        b.Send(Req(CS_GUILDVOLUNTEERING_REQ, w => w.WriteUInt32(guildId)));
        var applied = b.TryWait(CS_GUILDVOLUNTEERING_ACK, timeoutMs: 5000);
        Check("guild recruiting: B applies (CS_GUILDVOLUNTEERING_ACK)", applied is not null && Read(applied, r => r.ReadByte()) == 0, Describe(applied));
        a.Send(Req(CS_GUILDVOLUNTEERLIST_REQ, _ => { }));
        var apps = a.TryWait(CS_GUILDVOLUNTEERLIST_ACK, timeoutMs: 5000);
        Check("guild recruiting: A sees B among the applicants", apps is not null
            && Read(apps, r => { uint n = r.ReadUInt32(); uint id = n == 1 ? r.ReadUInt32() : 0u; while (r.Remaining > 0) r.ReadByte(); return id; }) == b.CharId, Describe(apps));
        b.Discard(CS_GUILDJOIN_ACK);
        a.Send(Req(CS_GUILDVOLUNTEERREPLY_REQ, w => { w.WriteUInt32(b.CharId); w.WriteByte(1); }));
        var back2 = b.TryWait(CS_GUILDJOIN_ACK, timeoutMs: 5000);
        Check("guild recruiting: A takes B — B is in the guild again (CS_GUILDJOIN_ACK)", back2 is not null
            && Read(back2, r => { r.ReadByte(); uint g = r.ReadUInt32(); while (r.Remaining > 0) r.ReadByte(); return g; }) == guildId, Describe(back2));

        a.Send(Req(CS_GUILDDISORGANIZATION_REQ, w => w.WriteByte(1)));
        var dis = a.TryWait(CS_GUILDDISORGANIZATION_ACK, timeoutMs: 5000);
        Check("guild: A disbands it (CS_GUILDDISORGANIZATION_ACK — gone in 7 days)", dis is not null && Read(dis, r => r.ReadByte()) == 1, Describe(dis));
    }
}
