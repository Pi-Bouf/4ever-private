using Microsoft.Data.SqlClient;

namespace TBot;

/// <summary>
/// Small game-DB helper. Used (optionally) to place the bot's character at another character's location
/// so it spawns where players actually are, instead of the country-4 newbie/tutorial region. This sets the
/// character's own saved spawn — it is NOT an in-game GM teleport.
/// </summary>
public sealed class GameDb
{
    private readonly string _connectionString;
    public GameDb(string connectionString) => _connectionString = connectionString;

    /// <summary>Runs one statement; <c>@p0..@pN</c> bind to <paramref name="args"/>. Returns rows affected.</summary>
    public async Task<int> ExecAsync(string sql, params object[] args)
    {
        await using var c = new SqlConnection(_connectionString);
        await c.OpenAsync();
        await using var cmd = new SqlCommand(sql, c);
        for (int i = 0; i < args.Length; i++) cmd.Parameters.AddWithValue($"@p{i}", args[i]);
        return await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Reads the first row of a query as raw values (null when there is none).</summary>
    public async Task<object[]?> RowAsync(string sql, params object[] args)
    {
        await using var c = new SqlConnection(_connectionString);
        await c.OpenAsync();
        await using var cmd = new SqlCommand(sql, c);
        for (int i = 0; i < args.Length; i++) cmd.Parameters.AddWithValue($"@p{i}", args[i]);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        var row = new object[r.FieldCount];
        r.GetValues(row);
        return row;
    }

    /// <summary>Copies map/region/position/country from <paramref name="fromCharId"/> onto
    /// <paramref name="toCharId"/> so the bot char spawns in that char's region. Returns the target spot.</summary>
    public async Task<(ushort MapId, uint Region, float X, float Y, float Z)> MatchPositionAsync(
        uint fromCharId, uint toCharId, CancellationToken ct = default)
    {
        await using var c = new SqlConnection(_connectionString);
        await c.OpenAsync(ct);

        await using var upd = new SqlCommand(@"
UPDATE c SET c.wMapID = p.wMapID, c.dwRegion = p.dwRegion,
             c.fPosX = p.fPosX, c.fPosY = p.fPosY, c.fPosZ = p.fPosZ,
             c.bCountry = p.bCountry, c.wSpawnID = 0
FROM dbo.TCHARTABLE c, dbo.TCHARTABLE p
WHERE c.dwCharID = @to AND p.dwCharID = @from;", c);
        upd.Parameters.AddWithValue("@to", unchecked((int)toCharId));
        upd.Parameters.AddWithValue("@from", unchecked((int)fromCharId));
        await upd.ExecuteNonQueryAsync(ct);

        await using var sel = new SqlCommand(
            "SELECT wMapID, dwRegion, fPosX, fPosY, fPosZ FROM dbo.TCHARTABLE WHERE dwCharID=@to", c);
        sel.Parameters.AddWithValue("@to", unchecked((int)toCharId));
        await using var r = await sel.ExecuteReaderAsync(ct);
        await r.ReadAsync(ct);
        return ((ushort)Convert.ToInt32(r.GetValue(0)), (uint)Convert.ToInt32(r.GetValue(1)),
                Convert.ToSingle(r.GetValue(2)), Convert.ToSingle(r.GetValue(3)), Convert.ToSingle(r.GetValue(4)));
    }
}
