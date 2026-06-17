using Microsoft.Data.SqlClient;

namespace TWorld.Data;

/// <summary>A tournament bracket-entry row (TTOURNAMENTCHART).</summary>
public readonly record struct TournamentEntryRow(
    byte Group, byte EntryId, string Name, byte Type, uint Class, uint Fee, uint FeeBack, ushort PermitItemId, byte PermitCount);

/// <summary>A tournament reward row (TTOURNAMENTREWARDCHART).</summary>
public readonly record struct TournamentRewardRow(byte EntryId, uint Class, byte CheckShield, byte ChartType, ushort ItemId, byte Count);

/// <summary>A tournament schedule-step row (TTOURNAMENTSCHEDULECHART).</summary>
public readonly record struct TournamentStepRow(byte Group, byte Step, uint Period);

/// <summary>Loads tournament bracket config (entries + rewards), transcribed from <c>DBAccess.h</c>
/// (CTBLTournament / CTBLTournamentReward). The date-math schedule + player tables are a later slice.</summary>
public sealed class TournamentDatabase
{
    private readonly string _cs;
    public TournamentDatabase(string connectionString) => _cs = connectionString;

    private async Task<SqlConnection> OpenAsync(CancellationToken ct)
    {
        var c = new SqlConnection(_cs);
        await c.OpenAsync(ct).ConfigureAwait(false);
        return c;
    }

    public async Task<List<TournamentEntryRow>> LoadEntriesAsync(CancellationToken ct = default)
    {
        await using var c = await OpenAsync(ct);
        // The baseline names the permit columns wItemID/bItemCount (the 5.0 source calls them
        // wPermitItemID/bPermitCount); they carry the same meaning.
        await using var cmd = new SqlCommand(
            "SELECT bGroup, bEntryID, szName, bType, dwClass, dwFee, dwFeeBack, wItemID, bItemCount FROM TTOURNAMENTCHART WHERE bEnable = 1", c);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var list = new List<TournamentEntryRow>();
        while (await r.ReadAsync(ct))
            list.Add(new TournamentEntryRow(r.GetByteSafe(0), r.GetByteSafe(1), r.GetStringSafe(2), r.GetByteSafe(3),
                r.GetUIntSafe(4), r.GetUIntSafe(5), r.GetUIntSafe(6), (ushort)r.GetUIntSafe(7), r.GetByteSafe(8)));
        return list;
    }

    public async Task<List<TournamentStepRow>> LoadScheduleAsync(CancellationToken ct = default)
    {
        await using var c = await OpenAsync(ct);
        await using var cmd = new SqlCommand(
            @"SELECT bGroup, bStep, dwPeriod FROM TTOURNAMENTSCHEDULECHART
              WHERE bGroup = 0 OR bGroup IN (SELECT bGroup FROM TTOURNAMENTCHART WHERE bEnable = 1)", c);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var list = new List<TournamentStepRow>();
        while (await r.ReadAsync(ct)) list.Add(new TournamentStepRow(r.GetByteSafe(0), r.GetByteSafe(1), r.GetUIntSafe(2)));
        return list;
    }

    public async Task<List<TournamentRewardRow>> LoadRewardsAsync(CancellationToken ct = default)
    {
        await using var c = await OpenAsync(ct);
        await using var cmd = new SqlCommand(
            "SELECT bEntryID, dwClass, bCheckShield, bChartType, wItemID, bItemCount FROM TTOURNAMENTREWARDCHART WHERE bItemCount > 0", c);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var list = new List<TournamentRewardRow>();
        while (await r.ReadAsync(ct))
            list.Add(new TournamentRewardRow(r.GetByteSafe(0), r.GetUIntSafe(1), r.GetByteSafe(2), r.GetByteSafe(3), (ushort)r.GetUIntSafe(4), r.GetByteSafe(5)));
        return list;
    }
}
