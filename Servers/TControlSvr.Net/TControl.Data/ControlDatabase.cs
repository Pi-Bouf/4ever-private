using System.Data;
using Microsoft.Data.SqlClient;
using TControl.Protocol;

namespace TControl.Data;

/// <summary>
/// SQL Server access for the control server (global DB / DSN <c>TGLOBAL_GSP</c>). Topology <c>SELECT</c>s
/// + the stored procs from the C++ <c>DBAccess.h</c>, invoked positionally to match the ODBC binding.
/// A fresh <see cref="SqlConnection"/> is opened per call (connection pooling handles reuse).
/// </summary>
public sealed class ControlDatabase
{
    private readonly string _cs;

    public ControlDatabase(string connectionString) => _cs = connectionString;

    private async Task<SqlConnection> OpenAsync(CancellationToken ct)
    {
        var c = new SqlConnection(_cs);
        await c.OpenAsync(ct).ConfigureAwait(false);
        return c;
    }

    public async Task PingAsync(CancellationToken ct)
    {
        await using var c = await OpenAsync(ct);
        await using var cmd = new SqlCommand("SELECT 1", c);
        await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
    }

    // ===== Topology (LoadData) =====

    /// <summary>TMACHINE + TNETWORK + TIPADDR(bActive=1). Returns machines keyed by id, ip/pri/network filled.</summary>
    public async Task<List<TMachine>> LoadMachinesAsync(CancellationToken ct)
    {
        var machines = new Dictionary<byte, TMachine>();
        await using var c = await OpenAsync(ct);

        await using (var cmd = new SqlCommand("SELECT bMachineID, szName, bRouteID FROM TMACHINE", c))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
            {
                var m = new TMachine { MachineId = r.GetByteSafe(0), Name = r.GetStringSafe(1), RouteId = r.GetByteSafe(2) };
                machines[m.MachineId] = m;
            }

        await using (var cmd = new SqlCommand("SELECT bMachineID, szNetwork FROM TNETWORK", c))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
                if (machines.TryGetValue(r.GetByteSafe(0), out var m)) m.Network = r.GetStringSafe(1);

        foreach (var m in machines.Values)
        {
            await using var cmd = new SqlCommand("SELECT szIPAddr, szPriAddr FROM TIPADDR WHERE bMachineID = @m AND bActive = 1", c);
            cmd.Parameters.AddWithValue("@m", m.MachineId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                string ip = r.GetStringSafe(0), pri = r.GetStringSafe(1);
                if (!string.IsNullOrEmpty(ip)) m.IpAddr.Add(ip);
                if (!string.IsNullOrEmpty(pri)) m.PriAddr.Add(pri);
            }
        }

        return machines.Values.ToList();
    }

    public async Task<List<TGroup>> LoadGroupsAsync(CancellationToken ct)
    {
        var list = new List<TGroup>();
        await using var c = await OpenAsync(ct);
        await using var cmd = new SqlCommand("SELECT bGroupID, szName FROM TGROUP", c);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(new TGroup { GroupId = r.GetByteSafe(0), Name = r.GetStringSafe(1) });
        return list;
    }

    public async Task<List<TSvrType>> LoadSvrTypesAsync(CancellationToken ct)
    {
        var list = new List<TSvrType>();
        await using var c = await OpenAsync(ct);
        await using var cmd = new SqlCommand("SELECT bType, szName FROM TSVRTYPE WHERE bControl = 1", c);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(new TSvrType { Type = r.GetByteSafe(0), Name = r.GetStringSafe(1) });
        return list;
    }

    public async Task<List<ServerRow>> LoadServersAsync(CancellationToken ct)
    {
        var list = new List<ServerRow>();
        await using var c = await OpenAsync(ct);
        await using var cmd = new SqlCommand("SELECT bGroupID, bServerID, bType, bMachineID, wPort, szName FROM TSERVER WHERE bType <> 6", c);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new ServerRow
            {
                GroupId = r.GetByteSafe(0), ServerId = r.GetByteSafe(1), Type = r.GetByteSafe(2),
                MachineId = r.GetByteSafe(3), Port = r.GetUShortSafe(4), Name = r.GetStringSafe(5),
            });
        return list;
    }

    public async Task<List<EventChartRow>> LoadEventsAsync(CancellationToken ct)
    {
        var list = new List<EventChartRow>();
        await using var c = await OpenAsync(ct);
        await using var cmd = new SqlCommand(
            "SELECT dwIndex, bID, szTitle, bGroupID, bSvrType, bSvrID, dStartDate, dEndDate, wValue, wMapID, " +
            "dwStartAlarm, dwEndAlarm, bPartTime, szStartMsg, szMidMsg, szEndMsg, szValue FROM TEVENTCHART", c);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new EventChartRow
            {
                Index = r.GetUIntSafe(0), Id = r.GetByteSafe(1), Title = r.GetStringSafe(2),
                GroupId = r.GetByteSafe(3), SvrType = r.GetByteSafe(4), SvrId = r.GetByteSafe(5),
                StartDate = r.GetDateTimeSafe(6), EndDate = r.GetDateTimeSafe(7),
                Value = r.GetUShortSafe(8), MapId = r.GetUShortSafe(9),
                StartAlarm = r.GetUIntSafe(10), EndAlarm = r.GetUIntSafe(11), PartTime = r.GetByteSafe(12),
                StartMsg = r.GetStringSafe(13), MidMsg = r.GetStringSafe(14), EndMsg = r.GetStringSafe(15),
                SzValue = r.GetStringSafe(16),
            });
        return list;
    }

    public async Task<List<TCashItem>> LoadCashShopItemsAsync(CancellationToken ct)
    {
        var list = new List<TCashItem>();
        await using var c = await OpenAsync(ct);
        await using var cmd = new SqlCommand("SELECT wID, szName FROM TCASHSHOPITEMCHART WHERE bCanSell = 1", c);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(new TCashItem { Id = r.GetUShortSafe(0), Name = r.GetStringSafe(1) });
        return list;
    }

    public async Task<List<PatchFile>> LoadPreVersionAsync(CancellationToken ct)
    {
        var list = new List<PatchFile>();
        await using var c = await OpenAsync(ct);
        await using var cmd = new SqlCommand("SELECT dwBetaVer, szPath, szName, dwSize FROM TPREVERSION", c);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new PatchFile { BetaVer = r.GetUIntSafe(0), Path = r.GetStringSafe(1), Name = r.GetStringSafe(2), Size = r.GetUIntSafe(3) });
        return list;
    }

    // ===== Stored procedures =====

    /// <summary>TOPLogin(id, pw) → authority level (0 = fail). C++ CSPOPLogin.</summary>
    public async Task<int> OpLoginAsync(string id, string pw, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct);
        var ret = SqlProc.Ret();
        await SqlProc.ExecAsync(c, "TOPLogin", ret, new[]
        {
            SqlProc.In("@szID", SqlDbType.VarChar, id, 50),
            SqlProc.In("@szPW", SqlDbType.VarChar, pw, 50),
        }, ct);
        return ret.AsInt();
    }

    /// <summary>TLoadService(world, serviceGroup, OUT ip, OUT port). C++ CSPLoadService.</summary>
    public async Task<ServiceEndpoint> LoadServiceAsync(byte world, byte serviceGroup, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct);
        var ip = SqlProc.Out("@szIP", SqlDbType.VarChar, 50);
        var port = SqlProc.Out("@wPort", SqlDbType.SmallInt);
        await SqlProc.ExecAsync(c, "TLoadService", null, new[]
        {
            SqlProc.In("@bWorld", SqlDbType.TinyInt, world),
            SqlProc.In("@bServiceGroup", SqlDbType.TinyInt, serviceGroup),
            ip, port,
        }, ct);
        return new ServiceEndpoint(ip.AsString(), port.AsUShort());
    }

    /// <summary>TEventUpdate(...) → result code (EVENT_RESULT). C++ CSPEventUpdate, 18 inputs.</summary>
    public async Task<int> EventUpdateAsync(
        uint index, byte id, byte type, string title, byte groupId, byte svrType, byte svrId,
        DateTime startDate, DateTime endDate, ushort value, ushort mapId, uint startAlarm, uint endAlarm,
        byte partTime, string startMsg, string midMsg, string endMsg, string szValue, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct);
        var ret = SqlProc.Ret();
        await SqlProc.ExecAsync(c, "TEventUpdate", ret, new[]
        {
            SqlProc.In("@dwIndex", SqlDbType.Int, (int)index),
            SqlProc.In("@bID", SqlDbType.TinyInt, id),
            SqlProc.In("@bType", SqlDbType.TinyInt, type),
            SqlProc.In("@szTitle", SqlDbType.VarChar, title, 50),
            SqlProc.In("@bGroupID", SqlDbType.TinyInt, groupId),
            SqlProc.In("@bSvrType", SqlDbType.TinyInt, svrType),
            SqlProc.In("@bSvrID", SqlDbType.TinyInt, svrId),
            SqlProc.In("@dStartDate", SqlDbType.DateTime, startDate),
            SqlProc.In("@dEndDate", SqlDbType.DateTime, endDate),
            SqlProc.In("@wValue", SqlDbType.SmallInt, (short)value),
            SqlProc.In("@wMapID", SqlDbType.SmallInt, (short)mapId),
            SqlProc.In("@dwStartAlarm", SqlDbType.Int, (int)startAlarm),
            SqlProc.In("@dwEndAlarm", SqlDbType.Int, (int)endAlarm),
            SqlProc.In("@bPartTime", SqlDbType.TinyInt, partTime),
            SqlProc.In("@szStartMsg", SqlDbType.VarChar, startMsg, 512),
            SqlProc.In("@szMidMsg", SqlDbType.VarChar, midMsg, 512),
            SqlProc.In("@szEndMsg", SqlDbType.VarChar, endMsg, 512),
            SqlProc.In("@szValue", SqlDbType.VarChar, szValue, 512),
        }, ct);
        return ret.AsInt();
    }

    /// <summary>TUserProtectedAdd(userId, duration, reason, permanent, operator) → result. C++ CSPUserProtectedAdd.</summary>
    public async Task<int> UserProtectedAddAsync(string userId, uint duration, string reason, byte permanent, string op, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct);
        var ret = SqlProc.Ret();
        await SqlProc.ExecAsync(c, "TUserProtectedAdd", ret, new[]
        {
            SqlProc.In("@szUserID", SqlDbType.VarChar, userId, 50),
            SqlProc.In("@dwDuration", SqlDbType.Int, (int)duration),
            SqlProc.In("@szReason", SqlDbType.VarChar, reason, 50),
            SqlProc.In("@bPermanent", SqlDbType.TinyInt, permanent),
            SqlProc.In("@szOperator", SqlDbType.VarChar, op, 50),
        }, ct);
        return ret.AsInt();
    }

    /// <summary>OPTool_SMSEmergency(svrType, svrId, status). C++ CSPSvrStatusSMS.</summary>
    public async Task SvrStatusSmsAsync(byte svrType, uint svrId, byte status, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct);
        await SqlProc.ExecAsync(c, "OPTool_SMSEmergency", null, new[]
        {
            SqlProc.In("@bSvrType", SqlDbType.TinyInt, svrType),
            SqlProc.In("@dwSvrID", SqlDbType.TinyInt, (byte)svrId),
            SqlProc.In("@bSvrStatus", SqlDbType.TinyInt, status),
        }, ct);
    }

    /// <summary>TUpdateVersion(path, name, size, betaVer) → result. C++ CSPUpdatePatch.</summary>
    public async Task<int> UpdatePatchAsync(string path, string name, uint size, uint betaVer, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct);
        var ret = SqlProc.Ret();
        await SqlProc.ExecAsync(c, "TUpdateVersion", ret, new[]
        {
            SqlProc.In("@szPath", SqlDbType.VarChar, path, 100),
            SqlProc.In("@szName", SqlDbType.VarChar, name, 50),
            SqlProc.In("@dwSize", SqlDbType.Int, (int)size),
            SqlProc.In("@dwBetaVer", SqlDbType.Int, (int)betaVer),
        }, ct);
        return ret.AsInt();
    }

    /// <summary>TUpdatePreVersion(path, name, size) → result. C++ CSPUpdatePrePatch.</summary>
    public async Task<int> UpdatePrePatchAsync(string path, string name, uint size, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct);
        var ret = SqlProc.Ret();
        await SqlProc.ExecAsync(c, "TUpdatePreVersion", ret, new[]
        {
            SqlProc.In("@szPath", SqlDbType.VarChar, path, 100),
            SqlProc.In("@szName", SqlDbType.VarChar, name, 50),
            SqlProc.In("@dwSize", SqlDbType.Int, (int)size),
        }, ct);
        return ret.AsInt();
    }

    /// <summary>TBetaToVersion(betaVer) → result. C++ CSPBetaToVer.</summary>
    public async Task<int> BetaToVersionAsync(uint betaVer, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct);
        var ret = SqlProc.Ret();
        await SqlProc.ExecAsync(c, "TBetaToVersion", ret, new[] { SqlProc.In("@dwBetaVer", SqlDbType.Int, (int)betaVer) }, ct);
        return ret.AsInt();
    }

    /// <summary>TDeletePreVersion(betaVer) → result. C++ CSPDeletePreVersion.</summary>
    public async Task<int> DeletePreVersionAsync(uint betaVer, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct);
        var ret = SqlProc.Ret();
        await SqlProc.ExecAsync(c, "TDeletePreVersion", ret, new[] { SqlProc.In("@dwBetaVer", SqlDbType.Int, (int)betaVer) }, ct);
        return ret.AsInt();
    }
}
