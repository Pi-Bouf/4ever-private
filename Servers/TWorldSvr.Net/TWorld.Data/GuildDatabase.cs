using System.Data;
using Microsoft.Data.SqlClient;

namespace TWorld.Data;

/// <summary>
/// Guild persistence + startup loads against the game DB (TGame_gsp). SELECT column orders and proc
/// parameter orders are transcribed verbatim from <c>Servers/TWorldSvr/DBAccess.h</c>. Procs are called
/// positionally (see <see cref="SqlProc"/>).
/// </summary>
public sealed class GuildDatabase
{
    private readonly string _cs;
    public GuildDatabase(string connectionString) => _cs = connectionString;

    private async Task<SqlConnection> OpenAsync(CancellationToken ct)
    {
        var c = new SqlConnection(_cs);
        await c.OpenAsync(ct).ConfigureAwait(false);
        return c;
    }

    private static long ToUnix(SqlDataReader r, int i)
        => r.IsDBNull(i) ? 0 : ((DateTimeOffset)DateTime.SpecifyKind(Convert.ToDateTime(r.GetValue(i)), DateTimeKind.Utc)).ToUnixTimeSeconds();

    // ----- startup loads -----

    private const string ChartSql = @"
SELECT bLevel, dwEXP, bMaxCnt, bMinCnt, bCabinetCnt, bTacticsCnt, bBattleSetCnt, bGuardCnt,
       bRoyalGuardCnt, bTurretCnt, bPeer1, bPeer2, bPeer3, bPeer4, bPeer5 FROM TGUILDCHART";

    public async Task<List<GuildLevelRow>> LoadGuildChartAsync(CancellationToken ct = default)
    {
        await using var c = await OpenAsync(ct);
        await using var cmd = new SqlCommand(ChartSql, c);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var list = new List<GuildLevelRow>();
        while (await r.ReadAsync(ct))
            list.Add(new GuildLevelRow(r.GetByteSafe(0), r.GetUIntSafe(1), r.GetByteSafe(2), r.GetByteSafe(3),
                r.GetByteSafe(4), r.GetByteSafe(5), r.GetByteSafe(6), r.GetByteSafe(7), r.GetByteSafe(8),
                r.GetByteSafe(9), r.GetByteSafe(10), r.GetByteSafe(11), r.GetByteSafe(12), r.GetByteSafe(13), r.GetByteSafe(14)));
        return list;
    }

    private const string GuildSql = @"
SELECT dwID, szName, dwChief, bLevel, dwFame, dwFameColor, bMaxCabinet, dwGI, dwExp, bGPoint, bStatus,
       dwGold, dwSilver, dwCooper, bDisorg, dwTime, timeEstablish, dwPvPTotalPoint, dwPvPUseablePoint,
       dwPvPMonthPoint FROM TGUILDTABLE";

    public async Task<List<GuildRow>> LoadGuildsAsync(CancellationToken ct = default)
    {
        await using var c = await OpenAsync(ct);
        await using var cmd = new SqlCommand(GuildSql, c);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var list = new List<GuildRow>();
        while (await r.ReadAsync(ct))
            list.Add(new GuildRow(
                r.GetUIntSafe(0), r.GetStringSafe(1), r.GetUIntSafe(2), r.GetByteSafe(3), r.GetUIntSafe(4),
                r.GetUIntSafe(5), r.GetByteSafe(6), r.GetUIntSafe(7), r.GetUIntSafe(8), r.GetByteSafe(9),
                r.GetByteSafe(10), r.GetUIntSafe(11), r.GetUIntSafe(12), r.GetUIntSafe(13), r.GetByteSafe(14),
                r.GetUIntSafe(15), ToUnix(r, 16), r.GetUIntSafe(17), r.GetUIntSafe(18), r.GetUIntSafe(19)));
        return list;
    }

    private const string MemberSql = @"
SELECT dwGuildID, dwCharID, szName, bCountry, bWarCountry, bLevel, bClass, bDuty, bPeer, dLogoutDate
FROM TGUILDMEMBER";

    public async Task<List<GuildMemberRow>> LoadGuildMembersAsync(CancellationToken ct = default)
    {
        await using var c = await OpenAsync(ct);
        await using var cmd = new SqlCommand(MemberSql, c);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var list = new List<GuildMemberRow>();
        while (await r.ReadAsync(ct))
            list.Add(new GuildMemberRow(
                r.GetUIntSafe(0), r.GetUIntSafe(1), r.GetStringSafe(2), r.GetByteSafe(3), r.GetByteSafe(4),
                r.GetByteSafe(5), r.GetByteSafe(6), r.GetByteSafe(7), r.GetByteSafe(8), ToUnix(r, 9)));
        return list;
    }

    // ----- Phase 2b loads (each best-effort; a missing table/view yields an empty list) -----

    private async Task<List<T>> QueryAsync<T>(string sql, Func<SqlDataReader, T> map, CancellationToken ct)
    {
        var list = new List<T>();
        await using var c = await OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, c);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(map(r));
        return list;
    }

    public Task<List<GuildArticleRow>> LoadArticlesAsync(CancellationToken ct = default) => QueryAsync(
        "SELECT dwGuildID, dwID, bDuty, szWritter, szTitle, szArticle, dwTime FROM TGUILDARTICLETABLE",
        r => new GuildArticleRow(r.GetUIntSafe(0), r.GetUIntSafe(1), r.GetByteSafe(2), r.GetStringSafe(3), r.GetStringSafe(4), r.GetStringSafe(5), r.GetUIntSafe(6)), ct);

    public Task<List<GuildTacticsRow>> LoadTacticsAsync(CancellationToken ct = default) => QueryAsync(
        "SELECT dwGuildID, dwCharID, szName, bLevel, bClass, dwRewardPoint, dlRewardMoney, dwGainPoint, bDay, dEndTime FROM TVIEW_GUILDTACTICSTABLE",
        r => new GuildTacticsRow(r.GetUIntSafe(0), r.GetUIntSafe(1), r.GetStringSafe(2), r.GetByteSafe(3), r.GetByteSafe(4), r.GetUIntSafe(5), r.GetInt64Safe(6), r.GetUIntSafe(7), r.GetByteSafe(8), ToUnix(r, 9)), ct);

    public Task<List<GuildRelationRow>> LoadRelationsAsync(CancellationToken ct = default) => QueryAsync(
        "SELECT bType, dwGuildOne, dwGuildTwo FROM TGUILDRELATION",
        r => new GuildRelationRow(r.GetByteSafe(0), r.GetUIntSafe(1), r.GetUIntSafe(2)), ct);

    public Task<List<GuildPointRewardRow>> LoadPointRewardsAsync(CancellationToken ct = default) => QueryAsync(
        "SELECT dwGuildID, szName, dwPoint, dlDate FROM TGUILDPVPOINTREWARDTABLE ORDER BY dlDate DESC",
        r => new GuildPointRewardRow(r.GetUIntSafe(0), r.GetStringSafe(1), r.GetUIntSafe(2), ToUnix(r, 3)), ct);

    public Task<List<GuildPvpRecordRow>> LoadPvpRecordsAsync(CancellationToken ct = default) => QueryAsync(
        "SELECT dwGuildID, dwCharID, dwDate, wKillCount, wDieCount, dwPoint_1, dwPoint_2, dwPoint_3, dwPoint_4, dwPoint_5, dwPoint_6, dwPoint_7, dwPoint_8 FROM TGUILDPVPRECORDTABLE",
        r => new GuildPvpRecordRow(r.GetUIntSafe(0), r.GetUIntSafe(1), r.GetUIntSafe(2), (ushort)r.GetUIntSafe(3), (ushort)r.GetUIntSafe(4),
            new[] { r.GetUIntSafe(5), r.GetUIntSafe(6), r.GetUIntSafe(7), r.GetUIntSafe(8), r.GetUIntSafe(9), r.GetUIntSafe(10), r.GetUIntSafe(11), r.GetUIntSafe(12) }), ct);

    public Task<List<GuildStatsRow>> LoadStatsAsync(CancellationToken ct = default) => QueryAsync(
        "SELECT dwGuildID, bSkillPoint, bLevel, dwExp FROM TGUILDSTATSTABLE",
        r => new GuildStatsRow(r.GetUIntSafe(0), r.GetByteSafe(1), r.GetByteSafe(2), r.GetUIntSafe(3)), ct);

    public Task<List<GuildCabinetRow>> LoadCabinetAsync(CancellationToken ct = default) => QueryAsync(
        @"SELECT dwOwnerID, dlID, dwStorageID, wItemID, bLevel, bCount, bGLevel, dwDuraMax, dwDuraCur, bRefineCur, dEndTime, bGradeEffect,
                 bMagic1, bMagic2, bMagic3, bMagic4, bMagic5, bMagic6, wValue1, wValue2, wValue3, wValue4, wValue5, wValue6,
                 dwTime1, dwTime2, dwTime3, dwTime4, dwTime5, dwTime6
          FROM TITEMTABLE WHERE bOwnerType = 1 AND bStorageType = 1", // TOWNER_GUILD / STORAGE_CABINET
        r => new GuildCabinetRow(r.GetUIntSafe(0), r.GetInt64Safe(1), r.GetUIntSafe(2), (ushort)r.GetUIntSafe(3), r.GetByteSafe(4), r.GetByteSafe(5),
            r.GetByteSafe(6), r.GetUIntSafe(7), r.GetUIntSafe(8), r.GetByteSafe(9), ToUnix(r, 10), r.GetByteSafe(11),
            new[] { r.GetByteSafe(12), r.GetByteSafe(13), r.GetByteSafe(14), r.GetByteSafe(15), r.GetByteSafe(16), r.GetByteSafe(17) },
            new[] { (ushort)r.GetUIntSafe(18), (ushort)r.GetUIntSafe(19), (ushort)r.GetUIntSafe(20), (ushort)r.GetUIntSafe(21), (ushort)r.GetUIntSafe(22), (ushort)r.GetUIntSafe(23) },
            new[] { r.GetUIntSafe(24), r.GetUIntSafe(25), r.GetUIntSafe(26), r.GetUIntSafe(27), r.GetUIntSafe(28), r.GetUIntSafe(29) }), ct);

    public Task<List<GuildWantedRow>> LoadWantedAsync(CancellationToken ct = default) => QueryAsync(
        "SELECT dwGuildID, bMinLevel, bMaxLevel, dEndTime, szTitle, szText FROM TGUILDWANTEDTABLE",
        r => new GuildWantedRow(r.GetUIntSafe(0), r.GetByteSafe(1), r.GetByteSafe(2), ToUnix(r, 3), r.GetStringSafe(4), r.GetStringSafe(5)), ct);

    public Task<List<GuildTacticsWantedRow>> LoadTacticsWantedAsync(CancellationToken ct = default) => QueryAsync(
        "SELECT dwID, dwGuildID, bMinLevel, bMaxLevel, dEndTime, szTitle, szText, bDay, dwGold, dwSilver, dwCooper, dwPvPoint FROM TGUILDTACTICSWANTEDTABLE ORDER BY dwID",
        r => new GuildTacticsWantedRow(r.GetUIntSafe(0), r.GetUIntSafe(1), r.GetByteSafe(2), r.GetByteSafe(3), ToUnix(r, 4), r.GetStringSafe(5), r.GetStringSafe(6), r.GetByteSafe(7), r.GetUIntSafe(8), r.GetUIntSafe(9), r.GetUIntSafe(10), r.GetUIntSafe(11)), ct);

    public Task<List<GuildVolunteerRow>> LoadVolunteersAsync(CancellationToken ct = default) => QueryAsync(
        "SELECT bType, dwID, dwCharID, szName, bLevel, bClass FROM TVIEW_GUILDVOLUNTEERTABLE",
        r => new GuildVolunteerRow(r.GetByteSafe(0), r.GetUIntSafe(1), r.GetUIntSafe(2), r.GetStringSafe(3), r.GetByteSafe(4), r.GetByteSafe(5)), ct);

    // ----- persistence (CSPGuild* param orders from DBAccess.h) -----

    /// <summary>TGuildEstablish: { ? = CALL TGuildEstablish(@dwGuildID OUT, @name, @chiefId, @time) }.</summary>
    public async Task<GuildEstablishRow> EstablishAsync(string name, uint chiefId, DateTime time, CancellationToken ct = default)
    {
        await using var c = await OpenAsync(ct);
        var ret = SqlProc.Ret();
        var guildId = SqlProc.Out("@dwGuildID", SqlDbType.Int);
        await SqlProc.ExecAsync(c, "TGuildEstablish", ret, new[]
        {
            guildId,
            SqlProc.In("@szName", SqlDbType.VarChar, name, 50),
            SqlProc.In("@dwChiefID", SqlDbType.Int, unchecked((int)chiefId)),
            SqlProc.In("@timeEstablish", SqlDbType.DateTime, time),
        }, ct);
        return new GuildEstablishRow(ret.AsInt(), guildId.AsUInt());
    }

    public Task DisorgAsync(uint guildId, byte disorg, uint time, CancellationToken ct = default)
        => ExecNoRet("TGuildDisorg", ct,
            SqlProc.In("@dwGuildID", SqlDbType.Int, unchecked((int)guildId)),
            SqlProc.In("@bDisorg", SqlDbType.TinyInt, disorg),
            SqlProc.In("@dwTime", SqlDbType.Int, unchecked((int)time)));

    public Task MemberAddAsync(uint guildId, uint charId, byte level, byte duty, CancellationToken ct = default)
        => ExecNoRet("TGuildMemberAdd", ct,
            SqlProc.In("@dwGuildID", SqlDbType.Int, unchecked((int)guildId)),
            SqlProc.In("@dwCharID", SqlDbType.Int, unchecked((int)charId)),
            SqlProc.In("@bLevel", SqlDbType.TinyInt, level),
            SqlProc.In("@bDuty", SqlDbType.TinyInt, duty));

    public Task LeaveAsync(uint guildId, uint charId, byte leave, uint leaveTime, CancellationToken ct = default)
        => ExecNoRet("TGuildLeave", ct,
            SqlProc.In("@dwGuildID", SqlDbType.Int, unchecked((int)guildId)),
            SqlProc.In("@dwCharID", SqlDbType.Int, unchecked((int)charId)),
            SqlProc.In("@bLeave", SqlDbType.TinyInt, leave),
            SqlProc.In("@dwLeaveTime", SqlDbType.Int, unchecked((int)leaveTime)));

    public Task DutyAsync(uint charId, uint guildId, byte duty, CancellationToken ct = default)
        => ExecNoRet("TGuildDuty", ct,
            SqlProc.In("@dwCharID", SqlDbType.Int, unchecked((int)charId)),
            SqlProc.In("@dwGuildID", SqlDbType.Int, unchecked((int)guildId)),
            SqlProc.In("@bDuty", SqlDbType.TinyInt, duty));

    public Task PeerAsync(uint charId, uint guildId, byte peer, CancellationToken ct = default)
        => ExecNoRet("TGuildPeer", ct,
            SqlProc.In("@dwCharID", SqlDbType.Int, unchecked((int)charId)),
            SqlProc.In("@dwGuildID", SqlDbType.Int, unchecked((int)guildId)),
            SqlProc.In("@bPeer", SqlDbType.TinyInt, peer));

    public Task KickoutAsync(uint guildId, uint charId, CancellationToken ct = default)
        => ExecNoRet("TGuildKickout", ct,
            SqlProc.In("@dwGuildID", SqlDbType.Int, unchecked((int)guildId)),
            SqlProc.In("@dwCharID", SqlDbType.Int, unchecked((int)charId)));

    /// <summary>TGuildDelete — fully delete a guild (CSPGuildDelete); returns the proc result (0 = success).
    /// Used by the guild auto-extinction timer after the disband grace period.</summary>
    public async Task<int> DeleteAsync(uint guildId, CancellationToken ct = default)
    {
        await using var c = await OpenAsync(ct);
        var ret = SqlProc.Ret();
        await SqlProc.ExecAsync(c, "TGuildDelete", ret, new[] { SqlProc.In("@dwGuildID", SqlDbType.Int, unchecked((int)guildId)) }, ct);
        return ret.AsInt();
    }

    // ----- Phase 2b persistence procs (param orders verbatim from DBAccess.h) -----

    private static DateTime FromUnix(long s) => s <= 0 ? new DateTime(1900, 1, 1) : DateTimeOffset.FromUnixTimeSeconds(s).UtcDateTime;
    private static SqlParameter Dw(string n, uint v) => SqlProc.In(n, SqlDbType.Int, unchecked((int)v));
    private static SqlParameter Tb(string n, byte v) => SqlProc.In(n, SqlDbType.TinyInt, v);
    private static SqlParameter Str(string n, string v) => SqlProc.In(n, SqlDbType.VarChar, v, 2048);
    private static SqlParameter Dt(string n, long unix) => SqlProc.In(n, SqlDbType.DateTime, FromUnix(unix));

    public Task ArticleAddAsync(uint guildId, uint id, byte duty, string writer, string title, string article, uint time, CancellationToken ct = default)
        => ExecNoRet("TGuildArticleAdd", ct, Dw("@g", guildId), Dw("@id", id), Tb("@duty", duty), Str("@w", writer), Str("@t", title), Str("@a", article), Dw("@time", time));
    public Task ArticleDelAsync(uint guildId, uint id, CancellationToken ct = default)
        => ExecNoRet("TGuildArticleDel", ct, Dw("@g", guildId), Dw("@id", id));
    public Task ArticleUpdateAsync(uint guildId, uint id, string title, string article, CancellationToken ct = default)
        => ExecNoRet("TGuildArticleUpdate", ct, Dw("@g", guildId), Dw("@id", id), Str("@t", title), Str("@a", article));
    public Task FameAsync(uint guildId, uint fame, uint fameColor, CancellationToken ct = default)
        => ExecNoRet("TGuildFame", ct, Dw("@g", guildId), Dw("@f", fame), Dw("@fc", fameColor));
    public Task LevelAsync(uint guildId, byte level, CancellationToken ct = default)
        => ExecNoRet("TGuildLevel", ct, Dw("@g", guildId), Tb("@l", level));
    public Task MaxCabinetAsync(uint guildId, byte maxCabinet, CancellationToken ct = default)
        => ExecNoRet("TGuildMaxCabinet", ct, Dw("@g", guildId), Tb("@m", maxCabinet));
    public Task ContributionAsync(uint guildId, uint charId, uint exp, uint gold, uint silver, uint cooper, CancellationToken ct = default)
        => ExecNoRet("TGuildContribution", ct, Dw("@g", guildId), Dw("@c", charId), Dw("@e", exp), Dw("@gold", gold), Dw("@s", silver), Dw("@co", cooper));
    public Task SaveGuildPvPointAsync(uint guildId, uint total, uint useable, uint month, CancellationToken ct = default)
        => ExecNoRet("TSaveGuildPvPoint", ct, Dw("@g", guildId), Dw("@t", total), Dw("@u", useable), Dw("@m", month));
    public Task SaveGuildPointRewardAsync(uint guildId, uint point, string name, uint total, uint useable, CancellationToken ct = default)
        => ExecNoRet("TSaveGuildPointReward", ct, Dw("@g", guildId), Dw("@p", point), Str("@n", name), Dw("@t", total), Dw("@u", useable));
    public Task SaveGuildStatsAsync(uint guildId, byte skillPoint, byte level, uint exp, CancellationToken ct = default)
        => ExecNoRet("TSaveGuildStats", ct, Dw("@g", guildId), Tb("@sp", skillPoint), Tb("@l", level), Dw("@e", exp));
    public Task SaveGuildPvPRecordAsync(uint guildId, uint memberId, uint date, ushort kill, ushort die, uint[] point, CancellationToken ct = default)
        => ExecNoRet("TSaveGuildPvPRecord", ct, Dw("@g", guildId), Dw("@m", memberId), Dw("@d", date),
            SqlProc.In("@k", SqlDbType.SmallInt, unchecked((short)kill)), SqlProc.In("@di", SqlDbType.SmallInt, unchecked((short)die)),
            Dw("@p0", point[0]), Dw("@p1", point[1]), Dw("@p2", point[2]), Dw("@p3", point[3]), Dw("@p4", point[4]), Dw("@p5", point[5]), Dw("@p6", point[6]), Dw("@p7", point[7]));
    public Task WantedAddAsync(uint guildId, byte minLevel, byte maxLevel, long endTime, string title, string text, CancellationToken ct = default)
        => ExecNoRet("TGuildWantedAdd", ct, Dw("@g", guildId), Tb("@mi", minLevel), Tb("@ma", maxLevel), Dt("@e", endTime), Str("@t", title), Str("@x", text));
    public Task WantedDelAsync(uint guildId, CancellationToken ct = default)
        => ExecNoRet("TGuildWantedDel", ct, Dw("@g", guildId));
    public Task VolunteerAddAsync(uint guildId, uint charId, string time, byte type, uint gold, uint silver, uint cooper, CancellationToken ct = default)
        => ExecNoRet("TGuildVolunteerAdd", ct, Dw("@g", guildId), Dw("@c", charId), Str("@t", time), Tb("@ty", type), Dw("@gold", gold), Dw("@s", silver), Dw("@co", cooper));
    public Task VolunteerDelAsync(uint guildId, uint id, CancellationToken ct = default)
        => ExecNoRet("TGuildVolunteerDel", ct, Dw("@g", guildId), Dw("@id", id));
    public Task VolunteeringAsync(byte type, uint charId, uint id, CancellationToken ct = default)
        => ExecNoRet("TGuildVolunteering", ct, Tb("@ty", type), Dw("@c", charId), Dw("@id", id));
    public Task VolunteeringDelAsync(byte type, uint charId, CancellationToken ct = default)
        => ExecNoRet("TGuildVolunteeringDel", ct, Tb("@ty", type), Dw("@c", charId));
    public Task TacticsWantedAddAsync(uint id, uint guildId, uint point, uint gold, uint silver, uint cooper, byte day, byte minLevel, byte maxLevel, long endTime, string title, string text, CancellationToken ct = default)
        => ExecNoRet("TGuildTacticsWantedAdd", ct, Dw("@id", id), Dw("@g", guildId), Dw("@p", point), Dw("@gold", gold), Dw("@s", silver), Dw("@co", cooper), Tb("@day", day), Tb("@mi", minLevel), Tb("@ma", maxLevel), Dt("@e", endTime), Str("@t", title), Str("@x", text));
    public Task TacticsWantedDelAsync(uint id, CancellationToken ct = default)
        => ExecNoRet("TGuildTacticsWantedDel", ct, Dw("@id", id));
    public Task SaveTacticsGainPointAsync(uint charId, uint gainPoint, CancellationToken ct = default)
        => ExecNoRet("TSaveTacticsGainPoint", ct, Dw("@c", charId), Dw("@gp", gainPoint));

    /// <summary>TGuildTacticsAdd — registers a mercenary; returns the char's prior guild id (output).</summary>
    public async Task<uint> TacticsAddAsync(uint guildId, uint charId, uint point, long money, byte day, long endTime, CancellationToken ct = default)
    {
        await using var c = await OpenAsync(ct);
        var outGuild = SqlProc.Out("@charGuildId", SqlDbType.Int);
        await SqlProc.ExecAsync(c, "TGuildTacticsAdd", null, new[]
        {
            Dw("@g", guildId), Dw("@c", charId), Dw("@p", point),
            SqlProc.In("@money", SqlDbType.BigInt, money), Tb("@day", day), Dt("@e", endTime), outGuild,
        }, ct);
        return outGuild.AsUInt();
    }

    /// <summary>TGuildTacticsDel — removes a mercenary; returns the char's prior guild id (output).</summary>
    public async Task<uint> TacticsDelAsync(uint charId, CancellationToken ct = default)
    {
        await using var c = await OpenAsync(ct);
        var outGuild = SqlProc.Out("@charGuildId", SqlDbType.Int);
        await SqlProc.ExecAsync(c, "TGuildTacticsDel", null, new[] { Dw("@c", charId), outGuild }, ct);
        return outGuild.AsUInt();
    }

    private async Task ExecNoRet(string proc, CancellationToken ct, params SqlParameter[] args)
    {
        await using var c = await OpenAsync(ct);
        await SqlProc.ExecAsync(c, proc, null, args, ct);
    }
}
