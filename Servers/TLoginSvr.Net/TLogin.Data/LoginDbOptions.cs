namespace TLogin.Data;

/// <summary>
/// Connection-string configuration. The C++ server opened ODBC DSNs by name; here we map each
/// DSN name (as stored in <c>TGROUP.szDSN</c>) to a SQL Server connection string.
/// </summary>
public sealed class LoginDbOptions
{
    /// <summary>Connection string for the global database (TGlobal_gsp / DSN TGLOBAL_GSP).</summary>
    public string GlobalConnectionString { get; set; } = "";

    /// <summary>
    /// Per-group game-database connection strings, keyed by the DSN name a group declares in TGROUP.
    /// </summary>
    public Dictionary<string, string> GameConnectionStringsByDsn { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves the connection string for a group. Falls back to the single configured game
    /// connection string when only one is present and the DSN is not explicitly mapped.
    /// </summary>
    public string GameConnectionFor(GroupConfig group)
    {
        if (!string.IsNullOrWhiteSpace(group.Dsn) &&
            GameConnectionStringsByDsn.TryGetValue(group.Dsn, out var cs))
            return cs;

        if (GameConnectionStringsByDsn.Count == 1)
            return GameConnectionStringsByDsn.Values.First();

        throw new InvalidOperationException(
            $"No game connection string configured for group {group.GroupId} (DSN '{group.Dsn}').");
    }
}
