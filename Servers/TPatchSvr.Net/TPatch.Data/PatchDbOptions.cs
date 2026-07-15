namespace TPatch.Data;

/// <summary>
/// Connection string for the patch server. The C++ TPatchSvr opens a single ODBC DSN and reads the patch
/// tables (TVERSION / TPREVERSION / TUSER_INTERFACE) plus the topology procs (TLoadService / TMinBetaVer /
/// TPreCompleteAdd) from it — here it is one explicit SQL Server connection string.
/// </summary>
public sealed class PatchDbOptions
{
    /// <summary>Patch/topology database (the single DSN the C++ server uses). Empty ⇒ DB-less mode.</summary>
    public string ConnectionString { get; set; } = "";
}
