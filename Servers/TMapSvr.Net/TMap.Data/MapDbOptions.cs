namespace TMap.Data;

/// <summary>
/// Connection strings for the map server. The C++ TMapSvr opens a single ODBC DSN (<c>TGAME_GSP</c>,
/// see <c>Configurations/TMapSvr.ini → GameDSN</c>) and reaches TGlobal via cross-database references;
/// here we provide both as explicit connection strings. Either may be empty, in which case the map runs
/// DB-free (all persistence degrades to best-effort no-ops, matching the sibling ports).
/// </summary>
public sealed class MapDbOptions
{
    /// <summary>Game database (TGame_gsp) — the primary DSN the C++ server uses.</summary>
    public string GameConnectionString { get; set; } = "";

    /// <summary>Global database (TGlobal_gsp) — accounts, sessions, topology.</summary>
    public string GlobalConnectionString { get; set; } = "";
}
