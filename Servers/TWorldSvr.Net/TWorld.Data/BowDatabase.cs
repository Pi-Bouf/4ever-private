using System.Data;
using Microsoft.Data.SqlClient;

namespace TWorld.Data;

/// <summary>BoW config row (TBOWSETTINGSCHART).</summary>
public readonly record struct BowSettingsRow(ushort MapId, byte MinPlayersCount, byte MaxNationDifference, uint AlarmDur, uint BuyTimeDur, uint BattleDur);

/// <summary>A custom-time schedule row (TCUSTOMTIMECHART): battle type + seconds-of-day.</summary>
public readonly record struct CustomTimeRow(byte Type, uint Time);

/// <summary>
/// BoW config loads + the live-roster persistence (TAddBOWPlayer / TClearBOWPlayers /
/// TDeleteSingleBOWPlayer), transcribed from <c>DBAccess.h</c>. Persistence is best-effort at the call site.
/// </summary>
public sealed class BowDatabase
{
    private readonly string _cs;
    public BowDatabase(string connectionString) => _cs = connectionString;

    private async Task<SqlConnection> OpenAsync(CancellationToken ct)
    {
        var c = new SqlConnection(_cs);
        await c.OpenAsync(ct).ConfigureAwait(false);
        return c;
    }

    public async Task<BowSettingsRow?> LoadSettingsAsync(CancellationToken ct = default)
    {
        await using var c = await OpenAsync(ct);
        await using var cmd = new SqlCommand(
            "SELECT wMapID, bMinPlayersCount, bMaxNationDifference, dwAlarmDur, dwBuyTimeDur, dwBattleDur FROM TBOWSETTINGSCHART", c);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return new BowSettingsRow((ushort)r.GetUIntSafe(0), r.GetByteSafe(1), r.GetByteSafe(2), r.GetUIntSafe(3), r.GetUIntSafe(4), r.GetUIntSafe(5));
    }

    public async Task<List<CustomTimeRow>> LoadCustomTimesAsync(CancellationToken ct = default)
    {
        await using var c = await OpenAsync(ct);
        await using var cmd = new SqlCommand("SELECT bType, dwTime FROM TCUSTOMTIMECHART", c);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var list = new List<CustomTimeRow>();
        while (await r.ReadAsync(ct)) list.Add(new CustomTimeRow(r.GetByteSafe(0), r.GetUIntSafe(1)));
        return list;
    }

    public Task AddPlayerAsync(uint charId, uint userId, CancellationToken ct = default)
        => ExecNoRet("TAddBOWPlayer", ct, SqlProc.In("@c", SqlDbType.Int, unchecked((int)charId)), SqlProc.In("@u", SqlDbType.Int, unchecked((int)userId)));
    public Task ClearPlayersAsync(CancellationToken ct = default)
        => ExecNoRet("TClearBOWPlayers", ct);
    public Task DeleteSinglePlayerAsync(uint userId, CancellationToken ct = default)
        => ExecNoRet("TDeleteSingleBOWPlayer", ct, SqlProc.In("@u", SqlDbType.Int, unchecked((int)userId)));

    private async Task ExecNoRet(string proc, CancellationToken ct, params SqlParameter[] args)
    {
        await using var c = await OpenAsync(ct);
        await SqlProc.ExecAsync(c, proc, null, args, ct);
    }
}
