using Microsoft.Data.SqlClient;

namespace TBot;

/// <summary>
/// Optional DB-insert account helper. There is no account-create packet in the protocol, so to make a
/// fresh account TBot inserts directly into <c>TGlobal_gsp.dbo.TACCOUNT_PW(szUserID, szPasswd)</c> —
/// the same table the seed migration writes and that <c>TLogin</c>/<c>CSPLogin</c> reads
/// (Database/migrations/001_seed_test_account.sql). The DB hashes the password so the stored form is
/// byte-identical to what the client sends.
/// </summary>
public sealed class AccountProvisioner
{
    private readonly string _connectionString;

    public AccountProvisioner(string connectionString) => _connectionString = connectionString;

    /// <summary>Inserts the account if absent. Returns true if a row was created, false if it already existed.</summary>
    public async Task<bool> EnsureAccountAsync(string userId, string password, CancellationToken ct = default)
    {
        await using var c = new SqlConnection(_connectionString);
        await c.OpenAsync(ct);

        await using var exists = new SqlCommand(
            "SELECT 1 FROM dbo.TACCOUNT_PW WHERE szUserID = @u", c);
        exists.Parameters.AddWithValue("@u", userId);
        if (await exists.ExecuteScalarAsync(ct) is not null)
            return false;

        // Hash in C# (uppercase SHA-1 hex over ASCII bytes) so the stored value is byte-identical to what
        // the bot sends at login. Do NOT hash via HASHBYTES on a parameter: AddWithValue infers NVarChar,
        // so HASHBYTES would hash UTF-16 bytes and never match the client's ASCII SHA-1.
        await using var insert = new SqlCommand(
            "INSERT INTO dbo.TACCOUNT_PW (szUserID, szPasswd) VALUES (@u, @p)", c);
        insert.Parameters.AddWithValue("@u", userId);
        insert.Parameters.AddWithValue("@p", Sha1Util.UpperHex(password));
        await insert.ExecuteNonQueryAsync(ct);
        return true;
    }
}
