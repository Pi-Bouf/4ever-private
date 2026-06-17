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
}
