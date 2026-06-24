using System.Data;
using Microsoft.Data.SqlClient;

namespace TWorld.Data;

/// <summary>A server-message row (TSVRMSGCHART): id → localized system-message text.</summary>
public readonly record struct ServerMessageRow(uint Id, string Message);

/// <summary>A rock-paper-scissors chart row (TRPSGAMECHART).</summary>
public readonly record struct RpsChartRow(byte Type, byte WinCount, byte ProbWin, byte ProbDraw, byte ProbLose, ushort WinKeep, ushort WinPeriod);

/// <summary>A standing RPS win record (TRPSGAMERECORDTABLE): when a win at (type,winCount) was scored.</summary>
public readonly record struct RpsRecordRow(byte Type, byte WinCount, long WinDate);

/// <summary>
/// Access to the game database (TGame_gsp). Phase 1 only validates connectivity at startup; the guild,
/// party, ranking, BoW/BR and tournament procs are added in later phases.
/// </summary>
public sealed class GameDatabase
{
    private readonly string _cs;
    public GameDatabase(string connectionString) => _cs = connectionString;

    private async Task<SqlConnection> OpenAsync(CancellationToken ct)
    {
        var c = new SqlConnection(_cs);
        await c.OpenAsync(ct).ConfigureAwait(false);
        return c;
    }

    /// <summary>Opens and closes a connection to validate configuration at boot.</summary>
    public async Task PingAsync(CancellationToken ct = default)
    {
        await using var c = new SqlConnection(_cs);
        await c.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand("SELECT 1", c);
        await cmd.ExecuteScalarAsync(ct);
    }

    /// <summary>TGetRecallID — the persisted recall/companion-monster id high-water mark (CSPGetRecallID).
    /// The world resumes id allocation from here so fresh summon ids never collide with saved ones. Returns
    /// 0 if the proc is absent.</summary>
    public async Task<uint> GetRecallIdAsync(CancellationToken ct = default)
    {
        await using var c = new SqlConnection(_cs);
        await c.OpenAsync(ct).ConfigureAwait(false);
        var genId = SqlProc.Out("@dwRecallID", SqlDbType.Int);
        try
        {
            await SqlProc.ExecAsync(c, "TGetRecallID", null, new[] { genId }, ct);
        }
        catch (SqlException ex) when (ex.Number == 2812)
        {
            return 0; // proc absent in this baseline
        }
        return genId.AsUInt();
    }

    /// <summary>TSVRMSGCHART — localized system-message text keyed by id (CTBLSvrMsg → m_mapTSvrMsg).</summary>
    public async Task<List<ServerMessageRow>> LoadServerMessagesAsync(CancellationToken ct = default)
    {
        await using var c = await OpenAsync(ct);
        await using var cmd = new SqlCommand("SELECT dwID, szMessage FROM TSVRMSGCHART", c);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var list = new List<ServerMessageRow>();
        while (await r.ReadAsync(ct)) list.Add(new ServerMessageRow(r.GetUIntSafe(0), r.GetStringSafe(1)));
        return list;
    }

    /// <summary>TRPSGAMECHART — the rock-paper-scissors config rows (CTBLRPSGame).</summary>
    public async Task<List<RpsChartRow>> LoadRpsChartAsync(CancellationToken ct = default)
    {
        await using var c = await OpenAsync(ct);
        await using var cmd = new SqlCommand(
            "SELECT bType, bWinCount, bProb_Win, bProb_Draw, bProb_Lose, wWinKeep, wWinPeriod FROM TRPSGAMECHART", c);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var list = new List<RpsChartRow>();
        while (await r.ReadAsync(ct))
            list.Add(new RpsChartRow(r.GetByteSafe(0), r.GetByteSafe(1), r.GetByteSafe(2), r.GetByteSafe(3), r.GetByteSafe(4),
                (ushort)r.GetUIntSafe(5), (ushort)r.GetUIntSafe(6)));
        return list;
    }

    /// <summary>TRPSGAMERECORDTABLE — standing win timestamps per (type,winCount) (CTBLRPSGameRecord).</summary>
    public async Task<List<RpsRecordRow>> LoadRpsRecordsAsync(CancellationToken ct = default)
    {
        await using var c = await OpenAsync(ct);
        await using var cmd = new SqlCommand("SELECT bType, bWinCount, dWinDate FROM TRPSGAMERECORDTABLE", c);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var list = new List<RpsRecordRow>();
        while (await r.ReadAsync(ct))
        {
            long when = r.IsDBNull(2) ? 0 : new DateTimeOffset(DateTime.SpecifyKind(r.GetDateTime(2), DateTimeKind.Utc)).ToUnixTimeSeconds();
            list.Add(new RpsRecordRow(r.GetByteSafe(0), r.GetByteSafe(1), when));
        }
        return list;
    }

    /// <summary>TSaveCastleApplicant — persist a char's castle-war application so it survives a restart
    /// (CSPSaveCastleApplicant). camp 0 clears the application.</summary>
    public async Task SaveCastleApplicantAsync(ushort castle, uint charId, byte camp, CancellationToken ct = default)
    {
        await using var c = await OpenAsync(ct);
        await SqlProc.ExecAsync(c, "TSaveCastleApplicant", null, new[]
        {
            SqlProc.In("@wCastle", SqlDbType.SmallInt, unchecked((short)castle)),
            SqlProc.In("@dwCharID", SqlDbType.Int, unchecked((int)charId)),
            SqlProc.In("@bCamp", SqlDbType.TinyInt, camp),
        }, ct);
    }

    /// <summary>TRPSGameRecord — insert (record=true) or delete (record=false) a standing RPS win record
    /// (CSPRPSGameRecord). <paramref name="winDate"/> is unix seconds.</summary>
    public async Task SaveRpsRecordAsync(bool record, uint charId, byte type, byte winCount, long winDate, CancellationToken ct = default)
    {
        await using var c = await OpenAsync(ct);
        await SqlProc.ExecAsync(c, "TRPSGameRecord", null, new[]
        {
            SqlProc.In("@bRecord", SqlDbType.TinyInt, record ? (byte)1 : (byte)0),
            SqlProc.In("@dwCharID", SqlDbType.Int, unchecked((int)charId)),
            SqlProc.In("@bType", SqlDbType.TinyInt, type),
            SqlProc.In("@bWinCount", SqlDbType.TinyInt, winCount),
            SqlProc.In("@dWinDate", SqlDbType.SmallDateTime, DateTimeOffset.FromUnixTimeSeconds(winDate).UtcDateTime),
        }, ct);
    }
}
