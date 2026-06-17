using Microsoft.Data.SqlClient;

namespace TWorld.Data;

/// <summary>
/// Access to the game database (TGame_gsp). Phase 1 only validates connectivity at startup; the guild,
/// party, ranking, BoW/BR and tournament procs are added in later phases.
/// </summary>
public sealed class GameDatabase
{
    private readonly string _cs;
    public GameDatabase(string connectionString) => _cs = connectionString;

    /// <summary>Opens and closes a connection to validate configuration at boot.</summary>
    public async Task PingAsync(CancellationToken ct = default)
    {
        await using var c = new SqlConnection(_cs);
        await c.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand("SELECT 1", c);
        await cmd.ExecuteScalarAsync(ct);
    }
}
