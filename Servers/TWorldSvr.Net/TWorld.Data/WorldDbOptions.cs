namespace TWorld.Data;

/// <summary>
/// Connection strings for the world server. The C++ TWorld opens a single ODBC DSN (TGAME_GSP) and
/// reaches TGlobal via cross-database references; here we provide both as explicit connection strings.
/// </summary>
public sealed class WorldDbOptions
{
    /// <summary>Game database (TGame_gsp) — the primary DSN the C++ server uses.</summary>
    public string GameConnectionString { get; set; } = "";

    /// <summary>Global database (TGlobal_gsp) — accounts, sessions, topology.</summary>
    public string GlobalConnectionString { get; set; } = "";
}
