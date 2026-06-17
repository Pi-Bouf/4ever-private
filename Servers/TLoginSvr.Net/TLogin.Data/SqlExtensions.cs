using System.Data;
using System.Text;
using Microsoft.Data.SqlClient;

namespace TLogin.Data;

/// <summary>
/// Stored-procedure invocation helpers. The original server bound parameters by ODBC position
/// (<c>{ ? = CALL Proc(?,?) }</c>). To stay faithful to that — and robust against any parameter-name
/// drift in the database baseline — we invoke procs as positional <c>EXEC dbo.[Proc] @p0, @p1 OUTPUT</c>
/// text, where SQL Server binds the supplied variables to the proc's parameters by position rather than
/// by name. Parameter ORDER (taken verbatim from <c>DBAccess.h</c>) is therefore what matters.
/// </summary>
internal static class SqlProc
{
    public static SqlParameter In(string name, SqlDbType type, object? value, int size = 0)
    {
        var p = new SqlParameter(name, type) { Direction = ParameterDirection.Input, Value = value ?? DBNull.Value };
        if (size > 0) p.Size = size;
        return p;
    }

    public static SqlParameter Out(string name, SqlDbType type, int size = 0)
    {
        var p = new SqlParameter(name, type) { Direction = ParameterDirection.Output };
        if (size > 0) p.Size = size;
        return p;
    }

    /// <summary>The output variable that catches the procedure's RETURN code (the leading <c>?=</c>).</summary>
    public static SqlParameter Ret() => new("@__ret", SqlDbType.Int) { Direction = ParameterDirection.Output };

    /// <summary>Executes <c>{ [? =] CALL proc(args...) }</c> positionally and returns when complete.</summary>
    public static async Task ExecAsync(SqlConnection c, string proc, SqlParameter? ret, IReadOnlyList<SqlParameter> args, CancellationToken ct)
    {
        var sb = new StringBuilder("EXEC ");
        if (ret is not null) sb.Append("@__ret = ");
        sb.Append("dbo.[").Append(proc).Append(']');
        for (int i = 0; i < args.Count; i++)
        {
            sb.Append(i == 0 ? " " : ", ");
            sb.Append(args[i].ParameterName);
            if (args[i].Direction is ParameterDirection.Output or ParameterDirection.InputOutput)
                sb.Append(" OUTPUT");
        }

        await using var cmd = new SqlCommand(sb.ToString(), c) { CommandType = CommandType.Text };
        if (ret is not null) cmd.Parameters.Add(ret);
        foreach (var p in args) cmd.Parameters.Add(p);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}

internal static class SqlExtensions
{
    public static int AsInt(this SqlParameter p) => p.Value is null or DBNull ? 0 : Convert.ToInt32(p.Value);
    public static uint AsUInt(this SqlParameter p) => p.Value is null or DBNull ? 0u : unchecked((uint)Convert.ToInt64(p.Value));
    public static byte AsByte(this SqlParameter p) => p.Value is null or DBNull ? (byte)0 : Convert.ToByte(p.Value);
    public static ushort AsUShort(this SqlParameter p) => p.Value is null or DBNull ? (ushort)0 : unchecked((ushort)Convert.ToInt32(p.Value));
    public static string AsString(this SqlParameter p) => p.Value is null or DBNull ? "" : Convert.ToString(p.Value) ?? "";

    public static byte GetByteSafe(this SqlDataReader r, int i) => r.IsDBNull(i) ? (byte)0 : Convert.ToByte(r.GetValue(i));
    public static ushort GetUShortSafe(this SqlDataReader r, int i) => r.IsDBNull(i) ? (ushort)0 : unchecked((ushort)Convert.ToInt32(r.GetValue(i)));
    public static uint GetUIntSafe(this SqlDataReader r, int i) => r.IsDBNull(i) ? 0u : unchecked((uint)Convert.ToInt64(r.GetValue(i)));
    public static int GetIntSafe(this SqlDataReader r, int i) => r.IsDBNull(i) ? 0 : Convert.ToInt32(r.GetValue(i));
    public static string GetStringSafe(this SqlDataReader r, int i) => r.IsDBNull(i) ? "" : (r.GetValue(i)?.ToString() ?? "");
}
