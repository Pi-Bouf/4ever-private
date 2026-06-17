using Microsoft.Data.SqlClient;

namespace TWorld.Data;

/// <summary>A battle-time chart row (TBATTLETIMECHART): one scheduled window per battle type.</summary>
public readonly record struct BattleTimeRow(
    byte Type, uint BattleDur, uint BattleStart, uint AlarmStart, uint AlarmEnd, uint PeaceDur, byte Day, byte Week);

/// <summary>Loads the scheduled-battle config (TBATTLETIMECHART), transcribed from <c>DBAccess.h</c>.
/// The mission schedule shares <see cref="BowDatabase.LoadCustomTimesAsync"/> (TCUSTOMTIMECHART).</summary>
public sealed class BattleDatabase
{
    private readonly string _cs;
    public BattleDatabase(string connectionString) => _cs = connectionString;

    public async Task<List<BattleTimeRow>> LoadBattleTimesAsync(CancellationToken ct = default)
    {
        var list = new List<BattleTimeRow>();
        await using var c = new SqlConnection(_cs);
        await c.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(
            "SELECT bType, dwBattleDur, dwBattleStart, dwAlarmStart, dwAlarmEnd, dwPeaceDur, bDay, bWeek FROM TBATTLETIMECHART", c);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new BattleTimeRow(r.GetByteSafe(0), r.GetUIntSafe(1), r.GetUIntSafe(2), r.GetUIntSafe(3),
                r.GetUIntSafe(4), r.GetUIntSafe(5), r.GetByteSafe(6), r.GetByteSafe(7)));
        return list;
    }
}
