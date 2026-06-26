using System.Data;
using Microsoft.Data.SqlClient;

namespace TWorld.Data;

/// <summary>
/// Access to the global database (TGlobal_gsp). Phase 1 only needs the server nation plus a
/// connectivity check at startup; guild/ranking/social loads are added in later phases.
/// </summary>
public sealed class GlobalDatabase
{
    private readonly string _cs;
    public GlobalDatabase(string connectionString) => _cs = connectionString;

    private async Task<SqlConnection> OpenAsync(CancellationToken ct)
    {
        var c = new SqlConnection(_cs);
        await c.OpenAsync(ct).ConfigureAwait(false);
        return c;
    }

    /// <summary>Opens and closes a connection to validate configuration at boot.</summary>
    public async Task PingAsync(CancellationToken ct = default)
    {
        await using var c = await OpenAsync(ct);
        await using var cmd = new SqlCommand("SELECT 1", c);
        await cmd.ExecuteScalarAsync(ct);
    }

    /// <summary>TGetNation — server nation/locale enum (CSPGetNation). Returns 0 if the proc is absent.</summary>
    public async Task<byte> GetNationAsync(CancellationToken ct = default)
    {
        await using var c = await OpenAsync(ct);
        var nation = SqlProc.Out("@bNation", SqlDbType.TinyInt);
        try
        {
            await SqlProc.ExecAsync(c, "TGetNation", null, new[] { nation }, ct);
        }
        catch (SqlException ex) when (ex.Number == 2812)
        {
            return 0; // proc absent in this baseline
        }
        return nation.AsByte();
    }

    /// <summary>Releases the login lock (deletes the TCURRENTUSER row) for a user who has left the game,
    /// so a return-to-character-select reconnect isn't rejected by TLogin as a duplicate login. The C++
    /// TLogout did this on session end; the .NET port previously only cleared TCURRENTUSER on shutdown,
    /// so every reconnect hit the duplicate check. Char-stat persistence is left to the map server, so we
    /// delete the lock row directly rather than calling TLogout (which would also rewrite level/exp).</summary>
    public async Task ReleaseCurrentUserAsync(uint userId, CancellationToken ct = default)
    {
        await using var c = await OpenAsync(ct);
        await using var cmd = new SqlCommand("DELETE FROM TCURRENTUSER WHERE dwUserID = @id", c);
        cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = unchecked((int)userId) });
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
