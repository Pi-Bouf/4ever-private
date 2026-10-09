using TLogin.Protocol;

namespace TBot;

/// <summary>
/// The guild scenario's second part (G3): a level-5 guild "TbotTactics" — A its chief, 1 gold and 1000 PvP points in hand — is put
/// in the database and the world restarted to load it. A posts a mercenary ad, B applies and is taken; B leaves its contract (the
/// guild is paid back); A invites B as a mercenary, B says yes, A fires B (B is mailed its pay and given its points). Then B
/// joins the guild, A rewards B with guild PvP points (B is mailed), and A buys a castle guard post from the guild's money.
/// Everything is taken out again and the world and the map restarted (the bought guards go with the map).
/// </summary>
public static partial class Scenarios
{
    private const uint TacticsGuild = 990011;
    private const string TacticsGuildName = "TbotTactics";
    private const ushort GuardShopNpc = 23100;
    private const ushort CS_GUILDTACTICSWANTEDADD_REQ = M + 0x0179, CS_GUILDTACTICSWANTEDADD_ACK = M + 0x017A,
        CS_GUILDTACTICSWANTEDLIST_REQ = M + 0x017D, CS_GUILDTACTICSWANTEDLIST_ACK = M + 0x017E,
        CS_GUILDTACTICSVOLUNTEERING_REQ = M + 0x017F, CS_GUILDTACTICSVOLUNTEERING_ACK = M + 0x0180,
        CS_GUILDTACTICSVOLUNTEERLIST_REQ = M + 0x0183, CS_GUILDTACTICSVOLUNTEERLIST_ACK = M + 0x0184,
        CS_GUILDTACTICSREPLY_REQ = M + 0x0185, CS_GUILDTACTICSREPLY_ACK = M + 0x0186, CS_GUILDTACTICSKICKOUT_REQ = M + 0x0187,
        CS_GUILDTACTICSKICKOUT_ACK = M + 0x0188, CS_GUILDPOINTREWARD_REQ = M + 0x01E5, CS_GUILDPOINTREWARD_ACK = M + 0x01E6,
        CS_MONSTERBUY_REQ = M + 0x01ED, CS_MONSTERBUY_ACK = M + 0x01EE, CS_GUILDTACTICSINVITE_REQ = M + 0x0211,
        CS_GUILDTACTICSINVITE_ACK = M + 0x0212, CS_GUILDTACTICSANSWER_REQ = M + 0x0213, CS_GUILDTACTICSANSWER_ACK = M + 0x0214,
        CS_GUILDTACTICSLIST_REQ = M + 0x0215, CS_GUILDTACTICSLIST_ACK = M + 0x0216, CS_NPCITEMLIST_REQ_G = M + 0x0082,
        CS_NPCITEMLIST_ACK_G = M + 0x0083;

    private static async Task RunGuildTacticsAsync(BotConfig cfg, GameDb db, uint idA, uint idB)
    {
        var cfgA = Clone(cfg, cfg.Account, "[A]");
        var cfgB = Clone(cfg, cfg.Account2, "[B]");
        var pvpA = await db.RowAsync("SELECT dwUseablePoint, dwTotalPoint FROM TPVPOINTTABLE WHERE dwCharID=@p0", (int)idA);
        var pvpB = await db.RowAsync("SELECT dwUseablePoint, dwTotalPoint FROM TPVPOINTTABLE WHERE dwCharID=@p0", (int)idB);
        var leaveB = await db.RowAsync("SELECT bGuildLeave, dwGuildLeaveTime FROM TCHARTABLE WHERE dwCharID=@p0", (int)idB);
        try
        {
            await ClearTacticsGuild(db, idA, idB);
            await db.ExecAsync($@"SET IDENTITY_INSERT TGUILDTABLE ON;
INSERT INTO TGUILDTABLE (dwID, szName, dwChief, bLevel, dwFame, dwFameColor, bMaxCabinet, dwGold, dwSilver, dwCooper, dwGI, dwExp,
    bGPoint, bStatus, bDisorg, dwTime, timeEstablish, dwPvPTotalPoint, dwPvPUseablePoint, dwPvPMonthPoint)
VALUES ({TacticsGuild}, '{TacticsGuildName}', @p0, 5, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, GETDATE(), 1000, 1000, 0);
SET IDENTITY_INSERT TGUILDTABLE OFF;
INSERT INTO TGUILDMEMBERTABLE (dwCharID, dwGuildID, bDuty, bPeer, dwService) VALUES (@p0, {TacticsGuild}, 2, 0, 0)", (int)idA);
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] tactics guild in; restarting the world ...");
            await Docker("compose restart worldsvr");
            await WaitForWorld();
            await Mercenaries(cfgA, cfgB, db, idA, idB);
        }
        finally
        {
            await Task.Delay(2500);                                              // the logout saves land first
            await ClearTacticsGuild(db, idA, idB);
            foreach (var (id, row) in new[] { (idA, pvpA), (idB, pvpB) })
                await db.ExecAsync(row is null ? "DELETE FROM TPVPOINTTABLE WHERE dwCharID=@p0"
                    : "UPDATE TPVPOINTTABLE SET dwUseablePoint=@p1, dwTotalPoint=@p2 WHERE dwCharID=@p0", (int)id, row?[0] ?? 0, row?[1] ?? 0);
            if (leaveB is not null)
                await db.ExecAsync("UPDATE TCHARTABLE SET bGuildLeave=@p1, dwGuildLeaveTime=@p2 WHERE dwCharID=@p0", (int)idB, leaveB[0], leaveB[1]);
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] tactics fixtures restored; restarting the world and the map ...");
            await Docker("compose restart worldsvr mapsvr");
        }
    }

    /// <summary>The tactics guild out of every table with a guild id, the bots' applications, and the run's mails to B.</summary>
    private static Task ClearTacticsGuild(GameDb db, uint idA, uint idB) => db.ExecAsync($@"DECLARE @s nvarchar(max) = N'';
SELECT @s = @s + N'DELETE FROM ' + QUOTENAME(OBJECT_NAME(object_id)) + N' WHERE dwGuildID = {TacticsGuild};'
  FROM sys.columns WHERE name = 'dwGuildID' AND OBJECTPROPERTY(object_id, 'IsUserTable') = 1;
EXEC (@s);
DELETE FROM TGUILDTABLE WHERE dwID = {TacticsGuild};
DELETE FROM TGUILDVOLUNTEERTABLE WHERE dwCharID IN (@p0, @p1);
DELETE FROM TGUILDTACTICSTABLE WHERE dwCharID IN (@p0, @p1);
DELETE FROM TPOSTTABLE WHERE dwCharID = @p1 AND dwSendID = @p0", (int)idA, (int)idB);

    /// <summary>CS_GUILDATTR_ACK of <paramref name="charId"/>: its tactics guild.</summary>
    private static uint? TacticsShown(Bot bot, uint charId)
        => bot.TryWait(CS_GUILDATTR_ACK, r => r.ReadUInt32() == charId, 5000) is { } p
            ? Read(p, r => { r.ReadUInt32(); r.ReadUInt32(); r.ReadUInt32(); r.ReadUInt32(); r.ReadString(); r.ReadByte(); uint t = r.ReadUInt32(); r.ReadString(); return t; })
            : null;

    private static async Task<object[]?> GuildRow(GameDb db)
        => await db.RowAsync($"SELECT dwGold, dwSilver, dwCooper, dwPvPUseablePoint FROM TGUILDTABLE WHERE dwID={TacticsGuild}");

    private static async Task Mercenaries(BotConfig cfgA, BotConfig cfgB, GameDb db, uint idA, uint idB)
    {
        using var a = new Bot(cfgA);
        using var b = new Bot(cfgB);
        a.Enter(); b.Enter();
        Thread.Sleep(1500);
        string nameA = a.Spawn.Name, nameB = b.Spawn.Name;
        (byte, uint) Res(PacketReader p) => Read(p, r => (r.ReadByte(), r.ReadUInt32()));

        // ---- a mercenary ad: posted, seen, applied to, taken ----
        a.Send(Req(CS_GUILDTACTICSWANTEDADD_REQ, w =>
        {
            w.WriteUInt32(0); w.WriteString("Mercs wanted"); w.WriteString("Paid well"); w.WriteByte(1); w.WriteByte(1); w.WriteByte(99);
            w.WriteUInt32(100); w.WriteUInt32(0); w.WriteUInt32(1); w.WriteUInt32(0);
        }));
        var posted = a.TryWait(CS_GUILDTACTICSWANTEDADD_ACK, timeoutMs: 5000);
        Check("mercenaries: A posts an ad (CS_GUILDTACTICSWANTEDADD_ACK)", posted is not null && Read(posted, r => r.ReadByte()) == 0, Describe(posted));
        b.Send(Req(CS_GUILDTACTICSWANTEDLIST_REQ, _ => { }));
        var ads = b.TryWait(CS_GUILDTACTICSWANTEDLIST_ACK, timeoutMs: 5000);
        var ad = ads is null ? default : Read(ads, r =>
        {
            uint n = r.ReadUInt32();
            for (uint i = 0; i < n; i++)
            {
                uint id = r.ReadUInt32(), g = r.ReadUInt32(); string gn = r.ReadString(); r.ReadString(); r.ReadString();
                r.ReadByte(); r.ReadByte(); r.ReadByte(); uint point = r.ReadUInt32(); r.ReadUInt32(); uint silver = r.ReadUInt32(); r.ReadUInt32();
                r.ReadInt64(); r.ReadByte();
                if (g == TacticsGuild) { while (r.Remaining > 0) r.ReadByte(); return (id, gn, point, silver); }
            }
            return (0u, "", 0u, 0u);
        });
        Check("mercenaries: B sees it — 100 points and 1 silver for a day", ad.Item1 != 0 && ad.Item2 == TacticsGuildName && ad.Item3 == 100 && ad.Item4 == 1, Describe(ads));
        b.Send(Req(CS_GUILDTACTICSVOLUNTEERING_REQ, w => { w.WriteUInt32(TacticsGuild); w.WriteUInt32(ad.Item1); }));
        var applied = b.TryWait(CS_GUILDTACTICSVOLUNTEERING_ACK, timeoutMs: 5000);
        Check("mercenaries: B applies (CS_GUILDTACTICSVOLUNTEERING_ACK)", applied is not null && Read(applied, r => r.ReadByte()) == 0, Describe(applied));
        a.Send(Req(CS_GUILDTACTICSVOLUNTEERLIST_REQ, _ => { }));
        var apps = a.TryWait(CS_GUILDTACTICSVOLUNTEERLIST_ACK, timeoutMs: 5000);
        Check("mercenaries: A sees B among the applicants", apps is not null
            && Read(apps, r => { uint n = r.ReadUInt32(); uint id = n == 1 ? r.ReadUInt32() : 0u; while (r.Remaining > 0) r.ReadByte(); return id; }) == b.CharId, Describe(apps));

        b.Discard(CS_GUILDATTR_ACK); b.Discard(CS_POSTRECV_ACK);
        a.Send(Req(CS_GUILDTACTICSREPLY_REQ, w => { w.WriteUInt32(b.CharId); w.WriteByte(1); }));
        var hired = b.TryWait(CS_GUILDTACTICSREPLY_ACK, timeoutMs: 5000);
        Check("mercenaries: A takes B — B is told (CS_GUILDTACTICSREPLY_ACK)", hired is not null && Res(hired) == (0, b.CharId), Describe(hired));
        Check("mercenaries: … B now fights for the guild (CS_GUILDATTR_ACK, tactics id)", TacticsShown(b, b.CharId) == TacticsGuild, "");
        var welcome = b.TryWait(CS_POSTRECV_ACK, timeoutMs: 5000);
        Check("mercenaries: … and gets a welcome letter from A (CS_POSTRECV_ACK)", welcome is not null
            && Read(welcome, r => { r.ReadUInt32(); r.ReadByte(); r.ReadByte(); string from = r.ReadString(); while (r.Remaining > 0) r.ReadByte(); return from; }) == nameA, Describe(welcome));
        await Task.Delay(500);
        var g = await GuildRow(db);
        var t = await db.RowAsync($"SELECT dwRewardPoint, dlRewardMoney FROM TGUILDTACTICSTABLE WHERE dwCharID=@p0 AND dwGuildID={TacticsGuild}", (int)idB);
        Check("saved: B's contract (TGUILDTACTICSTABLE), paid by the guild (999 silver, 900 points left)", t is not null && Convert.ToInt32(t[0]) == 100
            && g is not null && Convert.ToInt32(g[0]) == 0 && Convert.ToInt32(g[1]) == 999 && Convert.ToInt32(g[3]) == 900,
            $"contract={(t is null ? "none" : $"{t[0]}/{t[1]}")} guild={(g is null ? "none" : string.Join(",", g))}");
        a.Send(Req(CS_GUILDTACTICSLIST_REQ, _ => { }));
        var list = a.TryWait(CS_GUILDTACTICSLIST_ACK, timeoutMs: 5000);
        Check("mercenaries: A's mercenary list shows B", list is not null
            && Read(list, r => { uint n = r.ReadUInt32(); uint id = n == 1 ? r.ReadUInt32() : 0u; while (r.Remaining > 0) r.ReadByte(); return id; }) == b.CharId, Describe(list));

        // ---- B leaves its contract: the guild is paid back ----
        b.Discard(CS_GUILDATTR_ACK);
        b.Send(Req(CS_GUILDTACTICSKICKOUT_REQ, w => w.WriteUInt32(b.CharId)));
        var quit = b.TryWait(CS_GUILDTACTICSKICKOUT_ACK, timeoutMs: 5000);
        Check("mercenaries: B leaves its contract (CS_GUILDTACTICSKICKOUT_ACK) — no longer a mercenary", quit is not null && Res(quit) == (0, b.CharId)
            && TacticsShown(b, b.CharId) == 0, Describe(quit));
        await Task.Delay(500);
        g = await GuildRow(db);
        Check("saved: … the guild has its silver and points back", g is not null && Convert.ToInt32(g[0]) == 1 && Convert.ToInt32(g[1]) == 0 && Convert.ToInt32(g[3]) == 1000
            && await db.RowAsync("SELECT 1 FROM TGUILDTACTICSTABLE WHERE dwCharID=@p0", (int)idB) is null, g is null ? "none" : string.Join(",", g));

        // ---- an invite, a yes, then fired: B is mailed its pay and given its points ----
        a.Send(Req(CS_GUILDTACTICSINVITE_REQ, w => { w.WriteString(nameB); w.WriteByte(1); w.WriteUInt32(100); w.WriteUInt32(0); w.WriteUInt32(1); w.WriteUInt32(0); }));
        var offer = b.TryWait(CS_GUILDTACTICSINVITE_ACK, timeoutMs: 5000);
        Check("mercenaries: A invites B (CS_GUILDTACTICSINVITE_ACK: the guild, A, 1 day, 100 points)", offer is not null
            && Read(offer, r => { var o = (r.ReadString(), r.ReadString(), r.ReadByte(), r.ReadUInt32()); while (r.Remaining > 0) r.ReadByte(); return o; }) == (TacticsGuildName, nameA, 1, 100u), Describe(offer));
        b.Discard(CS_GUILDATTR_ACK);
        b.Send(Req(CS_GUILDTACTICSANSWER_REQ, w => { w.WriteByte(0); w.WriteString(nameA); w.WriteByte(1); w.WriteUInt32(100); w.WriteUInt32(0); w.WriteUInt32(1); w.WriteUInt32(0); }));
        var yesB = b.TryWait(CS_GUILDTACTICSANSWER_ACK, timeoutMs: 5000);
        var yesA = a.TryWait(CS_GUILDTACTICSANSWER_ACK, timeoutMs: 5000);
        Check("mercenaries: B says yes — both are told, B fights for the guild", yesB is not null && Res(yesB) == (0, b.CharId) && yesA is not null && Res(yesA) == (0, b.CharId)
            && TacticsShown(b, b.CharId) == TacticsGuild, $"B={Describe(yesB)} A={Describe(yesA)}");

        b.Discard(CS_POSTRECV_ACK); b.Discard(CS_PVPPOINT_ACK); b.Discard(CS_GUILDATTR_ACK);
        a.Send(Req(CS_GUILDTACTICSKICKOUT_REQ, w => w.WriteUInt32(b.CharId)));
        var fired = a.TryWait(CS_GUILDTACTICSKICKOUT_ACK, timeoutMs: 5000);
        var firedB = b.TryWait(CS_GUILDTACTICSKICKOUT_ACK, timeoutMs: 5000);
        Check("mercenaries: A fires B — both are told, B is no longer a mercenary", fired is not null && Res(fired) == (0, b.CharId) && firedB is not null
            && TacticsShown(b, b.CharId) == 0, $"A={Describe(fired)} B={Describe(firedB)}");
        var points = b.TryWait(CS_PVPPOINT_ACK, timeoutMs: 5000);
        Check("mercenaries: … B is given its 100 points (CS_PVPPOINT_ACK)", points is not null, "no reply");
        var pay = b.TryWait(CS_POSTRECV_ACK, timeoutMs: 5000);
        await Task.Delay(500);
        var mail = await db.RowAsync("SELECT szSender, dwSilver FROM TPOSTTABLE WHERE dwCharID=@p0 AND dwSendID=@p1 AND dwSilver=1", (int)idB, (int)idA);
        Check("mercenaries: … and mailed its pay — 1 silver, from A (TPOSTTABLE)", pay is not null && mail is not null && (string)mail[0] == nameA, mail is null ? "no mail" : $"{mail[0]} {mail[1]}");

        // ---- a PvP point reward to a member ----
        a.Send(Req(CS_GUILDINVITE_REQ, w => w.WriteString(nameB)));
        b.TryWait(CS_GUILDINVITE_ACK, timeoutMs: 5000);
        b.Send(Req(CS_GUILDINVITEANSWER_REQ, w => { w.WriteByte(0); w.WriteUInt32(a.CharId); }));
        var joined = b.TryWait(CS_GUILDJOIN_ACK, timeoutMs: 5000);
        Check("guild points: B joins the guild (CS_GUILDJOIN_ACK)", joined is not null, Describe(joined));
        b.Discard(CS_POSTRECV_ACK); b.Discard(CS_PVPPOINT_ACK);
        a.Send(Req(CS_GUILDPOINTREWARD_REQ, w => { w.WriteString(nameB); w.WriteUInt32(50); w.WriteString("Well fought"); }));
        var reward = a.TryWait(CS_GUILDPOINTREWARD_ACK, timeoutMs: 5000);
        Check("guild points: A gives B 50 of the guild's points — 850 left (CS_GUILDPOINTREWARD_ACK)", reward is not null && Res(reward) == (0, 850), Describe(reward));
        var gotPoints = b.TryWait(CS_PVPPOINT_ACK, timeoutMs: 5000);
        var note = b.TryWait(CS_POSTRECV_ACK, timeoutMs: 5000);
        Check("guild points: … B gets them and a letter (CS_PVPPOINT_ACK, CS_POSTRECV_ACK)", gotPoints is not null && note is not null, $"points={gotPoints is not null} mail={note is not null}");

        // ---- the castle guards' shop ----
        a.Send(Req(CS_NPCITEMLIST_REQ_G, w => w.WriteUInt16(GuardShopNpc)));
        var shop = a.TryWait(CS_NPCITEMLIST_ACK_G, r => r.ReadUInt16() == GuardShopNpc, 5000);
        var stock = shop is null ? default : Read(shop, r => { r.ReadUInt16(); byte type = r.ReadByte(); r.ReadByte(); byte n = r.ReadByte(); ushort first = r.ReadUInt16(); uint price = r.ReadUInt32(); while (r.Remaining > 0) r.ReadByte(); return (type, n, first, price); });
        Check("guard shop: the mercenary merchant lists its guard posts (TNPC_MONSTER)", stock.type == 22 && stock.n > 0 && stock.first == 1 && stock.price == 500_000, Describe(shop));
        b.Send(Req(CS_MONSTERBUY_REQ, w => { w.WriteUInt16(GuardShopNpc); w.WriteUInt16(1); }));
        var noRight = b.TryWait(CS_MONSTERBUY_ACK, timeoutMs: 5000);
        Check("guard shop: a plain member may not buy (MSB_AUTHORITY)", noRight is not null && Read(noRight, r => r.ReadByte()) == 5, Describe(noRight));
        a.Send(Req(CS_MONSTERBUY_REQ, w => { w.WriteUInt16(GuardShopNpc); w.WriteUInt16(1); }));
        var bought = a.TryWait(CS_MONSTERBUY_ACK, timeoutMs: 5000);
        Check("guard shop: A buys a guard post (CS_MONSTERBUY_ACK success)", bought is not null && Read(bought, r => r.ReadByte()) == 0, Describe(bought));
        await Task.Delay(500);
        g = await GuildRow(db);
        Check("saved: … paid from the guild's money — 499 silver left", g is not null && Convert.ToInt32(g[0]) == 0 && Convert.ToInt32(g[1]) == 499, g is null ? "none" : string.Join(",", g));
        a.Send(Req(CS_MONSTERBUY_REQ, w => { w.WriteUInt16(GuardShopNpc); w.WriteUInt16(1); }));
        var again = a.TryWait(CS_MONSTERBUY_ACK, timeoutMs: 5000);
        Check("guard shop: … its guards are out — not twice (MSB_ALREADY)", again is not null && Read(again, r => r.ReadByte()) == 6, Describe(again));
    }
}
