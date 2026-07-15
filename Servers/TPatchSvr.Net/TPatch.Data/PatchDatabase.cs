using System.Data;
using Microsoft.Data.SqlClient;

namespace TPatch.Data;

/// <summary>
/// The live SQL implementation of <see cref="IPatchSource"/> plus startup helpers (ping + topology
/// lookup). Column / parameter orders are transcribed verbatim from <c>TPatchSvr/DBAccess.h</c>
/// (CTBLVersion, CTBLPreVersion, CTBLInterface, CSPMinBetaVer, CSPPreComplete, CSPLoadService).
/// Every call is best-effort at the call site — on the clean baseline these tables may be empty, in
/// which case the server simply reports "no files".
/// </summary>
public sealed class PatchDatabase : IPatchSource
{
    private readonly string _cs;
    public PatchDatabase(string connectionString) => _cs = connectionString;

    private async Task<SqlConnection> OpenAsync(CancellationToken ct)
    {
        var c = new SqlConnection(_cs);
        await c.OpenAsync(ct).ConfigureAwait(false);
        return c;
    }

    /// <summary>Connectivity probe used at startup (the C++ opens the DSN in InitDB before serving).</summary>
    public async Task PingAsync(CancellationToken ct = default)
    {
        await using var c = await OpenAsync(ct);
        await using var cmd = new SqlCommand("SELECT 1", c);
        await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
    }

    // CTBLVersion: SELECT dwVersion, szPath, szName, dwSize, dwBetaVer FROM TVERSION WHERE dwVersion > ? ORDER BY dwVersion
    public async Task<IReadOnlyList<PatchFile>> GetVersionsAsync(uint fromVersion, CancellationToken ct = default)
    {
        var list = new List<PatchFile>();
        await using var c = await OpenAsync(ct);
        await using var cmd = new SqlCommand(
            "SELECT dwVersion, szPath, szName, dwSize, dwBetaVer FROM TVERSION WHERE dwVersion > @v ORDER BY dwVersion", c);
        cmd.Parameters.AddWithValue("@v", unchecked((int)fromVersion));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new PatchFile(r.GetUIntSafe(0), r.GetStringSafe(1), r.GetStringSafe(2), r.GetUIntSafe(3), r.GetUIntSafe(4)));
        return list;
    }

    // CTBLPreVersion: SELECT dwBetaVer, szPath, szName, dwSize FROM TPREVERSION WHERE dwBetaVer > ? ORDER BY dwBetaVer
    public async Task<IReadOnlyList<PatchFile>> GetPreVersionsAsync(uint fromBetaVer, CancellationToken ct = default)
    {
        var list = new List<PatchFile>();
        await using var c = await OpenAsync(ct);
        await using var cmd = new SqlCommand(
            "SELECT dwBetaVer, szPath, szName, dwSize FROM TPREVERSION WHERE dwBetaVer > @v ORDER BY dwBetaVer", c);
        cmd.Parameters.AddWithValue("@v", unchecked((int)fromBetaVer));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new PatchFile(0, r.GetStringSafe(1), r.GetStringSafe(2), r.GetUIntSafe(3), r.GetUIntSafe(0)));
        return list;
    }

    // CTBLInterface: SELECT szName, dwSize, (SELECT MAX(dwVersion) FROM TVERSION) FROM TUSER_INTERFACE WHERE bOption = ?
    public async Task<IReadOnlyList<PatchFile>> GetInterfaceAsync(byte option, CancellationToken ct = default)
    {
        var list = new List<PatchFile>();
        await using var c = await OpenAsync(ct);
        await using var cmd = new SqlCommand(
            "SELECT szName, dwSize, (SELECT MAX(dwVersion) FROM TVERSION) AS dwVersion FROM TUSER_INTERFACE WHERE bOption = @o", c);
        cmd.Parameters.AddWithValue("@o", (int)option);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            // The C++ sets path = "" and betaVer = 0 for interface files; version = current max TVERSION.
            list.Add(new PatchFile(r.GetUIntSafe(2), "", r.GetStringSafe(0), r.GetUIntSafe(1), 0));
        return list;
    }

    // CSPMinBetaVer: {? = CALL TMinBetaVer} — the min beta version is the proc's return value.
    public async Task<uint> GetMinBetaVerAsync(CancellationToken ct = default)
    {
        await using var c = await OpenAsync(ct);
        var ret = SqlProc.Ret();
        await SqlProc.ExecAsync(c, "TMinBetaVer", ret, Array.Empty<SqlParameter>(), ct);
        return ret.AsUInt();
    }

    // CSPPreComplete: {CALL TPreCompleteAdd(?)}
    public async Task AddPreCompleteAsync(uint betaVer, CancellationToken ct = default)
    {
        await using var c = await OpenAsync(ct);
        await SqlProc.ExecAsync(c, "TPreCompleteAdd", null,
            new[] { SqlProc.In("@v", SqlDbType.Int, unchecked((int)betaVer)) }, ct);
    }

    // CSPLoadService: {CALL TLoadService(?,?,?,?)} — IN world, IN serviceGroup, OUT ip, OUT port.
    // Used at startup to resolve the login-server endpoint the client is handed in CT_NEWPATCH_ACK.
    public async Task<ServiceEndpoint?> LoadServiceAsync(byte world, byte serviceGroup, CancellationToken ct = default)
    {
        await using var c = await OpenAsync(ct);
        var ip = SqlProc.Out("@ip", SqlDbType.VarChar, 101);
        var port = SqlProc.Out("@port", SqlDbType.SmallInt);
        await SqlProc.ExecAsync(c, "TLoadService", null, new[]
        {
            SqlProc.In("@world", SqlDbType.TinyInt, world),
            SqlProc.In("@group", SqlDbType.TinyInt, serviceGroup),
            ip, port,
        }, ct);

        string sIp = ip.AsString();
        if (string.IsNullOrWhiteSpace(sIp)) return null;
        return new ServiceEndpoint(sIp, port.AsUShort());
    }
}
