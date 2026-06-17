using System.Data;
using Microsoft.Data.SqlClient;

namespace TWorld.Data;

/// <summary>
/// PvP-ranking startup loads + monthly-rollover persistence against the game DB. Column / parameter
/// orders are transcribed from <c>DBAccess.h</c> (CTBLPvPointTable, CTBLMonthPvPointTable/Char,
/// CTBLFirstGradeGroup, CSPInitMonthRank/SaveMonthRank/InitMonthPvPoint). These tables are fed by the
/// PvP/BoW/BR plane (a later phase), so on the clean baseline they are typically empty — every call is
/// best-effort at the call site.
/// </summary>
public sealed class RankDatabase
{
    private readonly string _cs;
    public RankDatabase(string connectionString) => _cs = connectionString;

    private async Task<SqlConnection> OpenAsync(CancellationToken ct)
    {
        var c = new SqlConnection(_cs);
        await c.OpenAsync(ct).ConfigureAwait(false);
        return c;
    }

    private const string WarlordSql = @"SELECT TOP 1
        P.dwCharID, C.szName, P.dwTotalPoint, C.bLevel, C.bClass, C.bRace, C.bSex, C.bHair, C.bFace,
        R.dwWarrior_win + R.dwRanger_win + R.dwArcher_win + R.dwWizard_win + R.dwPriest_win + R.dwSorcerer_win,
        R.dwWarrior_lose + R.dwRanger_lose + R.dwArcher_lose + R.dwWizard_lose + R.dwPriest_lose + R.dwSorcerer_lose,
        U.szName
        FROM TPVPOINTTABLE AS P
        INNER JOIN TCHARTABLE AS C ON C.dwCharID = P.dwCharID
        INNER JOIN TPVPRECORDTABLE AS R ON P.dwCharID = R.dwCharID
        LEFT JOIN TGUILDMEMBERTABLE AS G ON P.dwCharID = G.dwCharID
        LEFT JOIN TGUILDTABLE AS U ON G.dwGuildID = U.dwID
        WHERE C.bCountry = @country ORDER BY P.dwTotalPoint DESC, P.dwCharID ASC";

    /// <summary>The all-time top PvP earner for a country (the warlord seed, monthly fields zeroed).</summary>
    public async Task<MonthRankRow?> LoadWarlordAsync(byte country, CancellationToken ct = default)
    {
        await using var c = await OpenAsync(ct);
        await using var cmd = new SqlCommand(WarlordSql, c);
        cmd.Parameters.AddWithValue("@country", (int)country);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return new MonthRankRow(0, r.GetUIntSafe(0), r.GetStringSafe(1), r.GetUIntSafe(2), 0, 0, 0,
            r.GetUIntSafe(9), r.GetUIntSafe(10), country, r.GetByteSafe(3), r.GetByteSafe(4), r.GetByteSafe(5),
            r.GetByteSafe(6), r.GetByteSafe(7), r.GetByteSafe(8), "", r.GetStringSafe(11));
    }

    /// <summary>This-month point/win/lose/say for a single char (used to flesh out the warlord row).</summary>
    public async Task<(uint MonthPoint, ushort Win, ushort Lose, string Say)?> LoadMonthCharAsync(uint charId, CancellationToken ct = default)
    {
        await using var c = await OpenAsync(ct);
        await using var cmd = new SqlCommand("SELECT dwPoint, wWin, wLose, szSay FROM TMONTHPVPOINTTABLE WHERE dwCharID = @c", c);
        cmd.Parameters.AddWithValue("@c", unchecked((int)charId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return (r.GetUIntSafe(0), (ushort)r.GetUIntSafe(1), (ushort)r.GetUIntSafe(2), r.GetStringSafe(3));
    }

    private const string LadderSql = @"SELECT TOP 16
        M.dwCharID, C.szName, P.dwTotalPoint, M.dwPoint, M.wWin, M.wLose,
        R.dwWarrior_win + R.dwRanger_win + R.dwArcher_win + R.dwWizard_win + R.dwPriest_win + R.dwSorcerer_win,
        R.dwWarrior_lose + R.dwRanger_lose + R.dwArcher_lose + R.dwWizard_lose + R.dwPriest_lose + R.dwSorcerer_lose,
        C.bLevel, C.bClass, C.bRace, C.bSex, C.bHair, C.bFace, M.szSay, U.szName
        FROM TMONTHPVPOINTTABLE AS M
        INNER JOIN TCHARTABLE AS C ON C.dwCharID = M.dwCharID
        INNER JOIN TPVPOINTTABLE AS P ON P.dwCharID = M.dwCharID
        INNER JOIN TPVPRECORDTABLE AS R ON R.dwCharID = M.dwCharID
        LEFT JOIN TGUILDMEMBERTABLE AS G ON M.dwCharID = G.dwCharID
        LEFT JOIN TGUILDTABLE AS U ON G.dwGuildID = U.dwID
        WHERE C.bCountry = @country AND M.dwPoint > 0
        ORDER BY dwPoint DESC, wWin DESC, wLose DESC, C.dwCharID ASC";

    /// <summary>The monthly ladder (ranks 1..16) for a country.</summary>
    public async Task<List<MonthRankRow>> LoadMonthLadderAsync(byte country, CancellationToken ct = default)
    {
        await using var c = await OpenAsync(ct);
        await using var cmd = new SqlCommand(LadderSql, c);
        cmd.Parameters.AddWithValue("@country", (int)country);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var list = new List<MonthRankRow>();
        while (await r.ReadAsync(ct))
            list.Add(new MonthRankRow(0, r.GetUIntSafe(0), r.GetStringSafe(1), r.GetUIntSafe(2), r.GetUIntSafe(3),
                (ushort)r.GetUIntSafe(4), (ushort)r.GetUIntSafe(5), r.GetUIntSafe(6), r.GetUIntSafe(7), country,
                r.GetByteSafe(8), r.GetByteSafe(9), r.GetByteSafe(10), r.GetByteSafe(11), r.GetByteSafe(12), r.GetByteSafe(13),
                r.GetStringSafe(14), r.GetStringSafe(15)));
        return list;
    }

    /// <summary>Last month's frozen first-grade ladder (TMONTHRANKTABLE for a month).</summary>
    public async Task<List<MonthRankRow>> LoadFirstGradeGroupAsync(byte month, CancellationToken ct = default)
    {
        await using var c = await OpenAsync(ct);
        await using var cmd = new SqlCommand(
            @"SELECT bRank, dwCharID, szName, dwTotalPoint, dwMonthPoint, wMonthWin, wMonthLose, dwTotalWin, dwTotalLose,
                     bCountry, bLevel, bClass, bRace, bSex, bHair, bFace, szSay, szGuild
              FROM TMONTHRANKTABLE WHERE bMonth = @m ORDER BY bCountry, bRank", c);
        cmd.Parameters.AddWithValue("@m", (int)month);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var list = new List<MonthRankRow>();
        while (await r.ReadAsync(ct))
            list.Add(new MonthRankRow(r.GetByteSafe(0), r.GetUIntSafe(1), r.GetStringSafe(2), r.GetUIntSafe(3), r.GetUIntSafe(4),
                (ushort)r.GetUIntSafe(5), (ushort)r.GetUIntSafe(6), r.GetUIntSafe(7), r.GetUIntSafe(8), r.GetByteSafe(9),
                r.GetByteSafe(10), r.GetByteSafe(11), r.GetByteSafe(12), r.GetByteSafe(13), r.GetByteSafe(14), r.GetByteSafe(15),
                r.GetStringSafe(16), r.GetStringSafe(17)));
        return list;
    }

    // ----- monthly rollover persistence -----

    public Task InitMonthRankAsync(byte month, CancellationToken ct = default)
        => ExecNoRet("TInitMonthRank", ct, SqlProc.In("@m", SqlDbType.TinyInt, month));

    /// <summary>TSaveMonthRank — persists one ladder slot (20 positional params, order from DBAccess.h).</summary>
    public Task SaveMonthRankAsync(byte month, byte rank, byte monthRank, MonthRankRow row, CancellationToken ct = default)
        => ExecNoRet("TSaveMonthRank", ct,
            SqlProc.In("@month", SqlDbType.TinyInt, month),
            SqlProc.In("@rank", SqlDbType.TinyInt, rank),
            SqlProc.In("@monthRank", SqlDbType.TinyInt, monthRank),
            SqlProc.In("@charId", SqlDbType.Int, unchecked((int)row.CharId)),
            SqlProc.In("@name", SqlDbType.VarChar, row.Name, 64),
            SqlProc.In("@totalPoint", SqlDbType.Int, unchecked((int)row.TotalPoint)),
            SqlProc.In("@monthPoint", SqlDbType.Int, unchecked((int)row.MonthPoint)),
            SqlProc.In("@monthWin", SqlDbType.SmallInt, unchecked((short)row.MonthWin)),
            SqlProc.In("@monthLose", SqlDbType.SmallInt, unchecked((short)row.MonthLose)),
            SqlProc.In("@totalWin", SqlDbType.Int, unchecked((int)row.TotalWin)),
            SqlProc.In("@totalLose", SqlDbType.Int, unchecked((int)row.TotalLose)),
            SqlProc.In("@country", SqlDbType.TinyInt, row.Country),
            SqlProc.In("@level", SqlDbType.TinyInt, row.Level),
            SqlProc.In("@class", SqlDbType.TinyInt, row.Class),
            SqlProc.In("@race", SqlDbType.TinyInt, row.Race),
            SqlProc.In("@sex", SqlDbType.TinyInt, row.Sex),
            SqlProc.In("@hair", SqlDbType.TinyInt, row.Hair),
            SqlProc.In("@face", SqlDbType.TinyInt, row.Face),
            SqlProc.In("@say", SqlDbType.VarChar, row.Say, 128),
            SqlProc.In("@guild", SqlDbType.VarChar, row.Guild, 64));

    private async Task ExecNoRet(string proc, CancellationToken ct, params SqlParameter[] args)
    {
        await using var c = await OpenAsync(ct);
        await SqlProc.ExecAsync(c, proc, null, args, ct);
    }
}
