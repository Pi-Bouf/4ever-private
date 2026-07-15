namespace TControl.Data;

/// <summary>
/// Connection strings for the control server. The C++ TControlSvr opens a single ODBC DSN (TGLOBAL_GSP);
/// here it is an explicit SQL Server connection string. Empty ⇒ DB-less mode (wire smoke test).
/// </summary>
public sealed class ControlDbOptions
{
    /// <summary>Global database (TGlobal_gsp / DSN TGLOBAL_GSP) — topology, operators, events, patch.</summary>
    public string GlobalConnectionString { get; set; } = "";
}
