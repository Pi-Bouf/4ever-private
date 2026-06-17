using System.Data;
using Microsoft.Data.SqlClient;

namespace TWorld.Data;

/// <summary>
/// Friend + soulmate persistence and per-character loads against the game DB. SELECT column orders and
/// proc parameter orders are transcribed verbatim from <c>Servers/TWorldSvr/DBAccess.h</c>
/// (CTBLFriend*, CTBLSoulmate, CSPFriend*, CSPSoulmate*). Procs are called positionally (see SqlProc).
/// </summary>
public sealed class SocialDatabase
{
    private readonly string _cs;
    public SocialDatabase(string connectionString) => _cs = connectionString;

    private async Task<SqlConnection> OpenAsync(CancellationToken ct)
    {
        var c = new SqlConnection(_cs);
        await c.OpenAsync(ct).ConfigureAwait(false);
        return c;
    }

    // ----- friend list load (CTBLFriendGroupTable / CTBLFriend / CTBLFriendTarget) -----

    public async Task<FriendLoad> LoadFriendsAsync(uint charId, CancellationToken ct = default)
    {
        await using var c = await OpenAsync(ct);
        int cid = unchecked((int)charId);

        var groups = new List<FriendGroupRow>();
        await using (var cmd = new SqlCommand("SELECT bGroup, szName FROM TFRIENDGROUPTABLE WHERE dwCharID = @c", c))
        {
            cmd.Parameters.AddWithValue("@c", cid);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) groups.Add(new FriendGroupRow(r.GetByteSafe(0), r.GetStringSafe(1)));
        }

        var friends = new List<FriendRow>();
        await using (var cmd = new SqlCommand(
            @"SELECT F.dwFriendID, C.szName, F.bGroup, C.bClass, C.bLevel
              FROM TFRIENDTABLE AS F INNER JOIN TCHARTABLE AS C ON F.dwFriendID = C.dwCharID
              WHERE F.dwCharID = @c AND C.bDelete = 0", c))
        {
            cmd.Parameters.AddWithValue("@c", cid);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                friends.Add(new FriendRow(r.GetUIntSafe(0), r.GetStringSafe(1), r.GetByteSafe(2), r.GetByteSafe(3), r.GetByteSafe(4)));
        }

        var targets = new List<FriendTargetRow>();
        await using (var cmd = new SqlCommand(
            @"SELECT F.dwCharID, C.szName
              FROM TFRIENDTABLE AS F INNER JOIN TCHARTABLE AS C ON F.dwCharID = C.dwCharID
              WHERE F.dwFriendID = @c AND C.bDelete = 0", c))
        {
            cmd.Parameters.AddWithValue("@c", cid);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) targets.Add(new FriendTargetRow(r.GetUIntSafe(0), r.GetStringSafe(1)));
        }

        return new FriendLoad(groups, friends, targets);
    }

    // ----- soulmate list load (CTBLSoulmate ∪ CTBLMySoulmate over TVIEW_SOULMATE) -----

    public async Task<List<SoulmateRow>> LoadSoulmatesAsync(uint charId, CancellationToken ct = default)
    {
        await using var c = await OpenAsync(ct);
        int cid = unchecked((int)charId);
        var list = new List<SoulmateRow>();

        async Task Read(string where)
        {
            await using var cmd = new SqlCommand(
                "SELECT dwCharID, dwTarget, szNAME, bLevel, bClass, dwTime FROM TVIEW_SOULMATE WHERE " + where, c);
            cmd.Parameters.AddWithValue("@c", cid);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                list.Add(new SoulmateRow(r.GetUIntSafe(0), r.GetUIntSafe(1), r.GetStringSafe(2), r.GetByteSafe(3), r.GetByteSafe(4), r.GetUIntSafe(5)));
        }

        await Read("dwTarget = @c");   // inbound: people who chose me
        await Read("dwCharID = @c");   // outbound: my own link
        return list;
    }

    // ----- persistence -----

    private static SqlParameter Dw(string n, uint v) => SqlProc.In(n, SqlDbType.Int, unchecked((int)v));
    private static SqlParameter Tb(string n, byte v) => SqlProc.In(n, SqlDbType.TinyInt, v);
    private static SqlParameter Str(string n, string v) => SqlProc.In(n, SqlDbType.VarChar, v, 64);

    private async Task ExecNoRet(string proc, CancellationToken ct, params SqlParameter[] args)
    {
        await using var c = await OpenAsync(ct);
        await SqlProc.ExecAsync(c, proc, null, args, ct);
    }

    public Task FriendInsertAsync(uint charId, uint friendId, CancellationToken ct = default)
        => ExecNoRet("TFriendInsert", ct, Dw("@c", charId), Dw("@f", friendId));
    public Task FriendEraseAsync(uint charId, uint friendId, CancellationToken ct = default)
        => ExecNoRet("TFriendDelete", ct, Dw("@c", charId), Dw("@f", friendId));
    public Task FriendGroupMakeAsync(uint charId, byte group, string name, CancellationToken ct = default)
        => ExecNoRet("TFriendGroupMake", ct, Dw("@c", charId), Tb("@g", group), Str("@n", name));
    public Task FriendGroupDeleteAsync(uint charId, byte group, CancellationToken ct = default)
        => ExecNoRet("TFriendGroupDelete", ct, Dw("@c", charId), Tb("@g", group));
    public Task FriendGroupChangeAsync(uint charId, byte group, uint friendId, CancellationToken ct = default)
        => ExecNoRet("TFriendGroupChange", ct, Dw("@c", charId), Tb("@g", group), Dw("@f", friendId));
    public Task FriendGroupNameAsync(uint charId, byte group, string name, CancellationToken ct = default)
        => ExecNoRet("TFriendGroupName", ct, Dw("@c", charId), Tb("@g", group), Str("@n", name));

    public Task SoulmateRegAsync(uint charId, uint target, CancellationToken ct = default)
        => ExecNoRet("TSoulmateReg", ct, Dw("@c", charId), Dw("@t", target));
    public Task SoulmateEndAsync(uint charId, uint time, CancellationToken ct = default)
        => ExecNoRet("TSoulmateEnd", ct, Dw("@c", charId), Dw("@t", time));
    public Task SoulmateDelAsync(uint charId, uint soul, CancellationToken ct = default)
        => ExecNoRet("TSoulmateDel", ct, Dw("@c", charId), Dw("@s", soul));
}
