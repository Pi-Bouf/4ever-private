using System.Data;
using Microsoft.Data.SqlClient;

namespace TWorld.Data;

/// <summary>BR config row (TBRSETTINGSCHART).</summary>
public readonly record struct BrSettingsRow(byte MinPlayerCount, uint AlarmDur, uint BuyTimeDur, uint BattleDur);

/// <summary>
/// BR config load + live-roster persistence (TAddBRPlayer / TClearBRPlayers / TDeleteSingleBRPlayer),
/// transcribed from <c>DBAccess.h</c>. Schedule times share <see cref="BowDatabase.LoadCustomTimesAsync"/>
/// (TCUSTOMTIMECHART, filtered by battle type). Persistence is best-effort at the call site.
/// </summary>
public sealed class BrDatabase
{
    private readonly string _cs;
    public BrDatabase(string connectionString) => _cs = connectionString;

    private async Task<SqlConnection> OpenAsync(CancellationToken ct)
    {
        var c = new SqlConnection(_cs);
        await c.OpenAsync(ct).ConfigureAwait(false);
        return c;
    }

    public async Task<BrSettingsRow?> LoadSettingsAsync(CancellationToken ct = default)
    {
        await using var c = await OpenAsync(ct);
        await using var cmd = new SqlCommand(
            "SELECT bMinPlayerCount, dwAlarmDur, dwBuyTimeDur, dwBattleDur FROM TBRSETTINGSCHART", c);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return new BrSettingsRow(r.GetByteSafe(0), r.GetUIntSafe(1), r.GetUIntSafe(2), r.GetUIntSafe(3));
    }

    public Task AddPlayerAsync(uint charId, uint userId, CancellationToken ct = default)
        => ExecNoRet("TAddBRPlayer", ct, SqlProc.In("@c", SqlDbType.Int, unchecked((int)charId)), SqlProc.In("@u", SqlDbType.Int, unchecked((int)userId)));
    public Task ClearPlayersAsync(CancellationToken ct = default)
        => ExecNoRet("TClearBRPlayers", ct);
    public Task DeleteSinglePlayerAsync(uint userId, CancellationToken ct = default)
        => ExecNoRet("TDeleteSingleBRPlayer", ct, SqlProc.In("@u", SqlDbType.Int, unchecked((int)userId)));

    private async Task ExecNoRet(string proc, CancellationToken ct, params SqlParameter[] args)
    {
        await using var c = await OpenAsync(ct);
        await SqlProc.ExecAsync(c, proc, null, args, ct);
    }
}
