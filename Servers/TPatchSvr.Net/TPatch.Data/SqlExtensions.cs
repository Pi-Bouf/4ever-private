using System.Data;
using System.Text;
using Microsoft.Data.SqlClient;

namespace TPatch.Data;

/// <summary>
/// Stored-procedure invocation helpers (same approach as TWorld.Data): procs are invoked as positional
/// <c>EXEC @__ret = dbo.[Proc] @p0, @p1 OUTPUT</c> text so SQL Server binds by position — robust to
/// parameter-name drift. Parameter ORDER (from <c>DBAccess.h</c>) is what matters.
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

    public static SqlParameter Ret() => new("@__ret", SqlDbType.Int) { Direction = ParameterDirection.Output };

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
    public static uint AsUInt(this SqlParameter p) => p.Value is null or DBNull ? 0u : unchecked((uint)Convert.ToInt64(p.Value));
    public static ushort AsUShort(this SqlParameter p) => p.Value is null or DBNull ? (ushort)0 : unchecked((ushort)Convert.ToInt64(p.Value));
    public static string AsString(this SqlParameter p) => p.Value is null or DBNull ? "" : Convert.ToString(p.Value) ?? "";

    public static uint GetUIntSafe(this SqlDataReader r, int i) => r.IsDBNull(i) ? 0u : unchecked((uint)Convert.ToInt64(r.GetValue(i)));
    public static string GetStringSafe(this SqlDataReader r, int i) => r.IsDBNull(i) ? "" : (r.GetValue(i)?.ToString() ?? "");
}
