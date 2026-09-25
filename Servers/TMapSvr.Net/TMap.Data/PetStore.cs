using System.Data;
using Microsoft.Data.SqlClient;

namespace TMap.Data;

/// <summary>A mount template (C++ <c>tagPETTEMP</c> from <c>TMOUNTCHART</c>, DBAccess.h:546): the mount id and the
/// monster it summons plain (<c>wDefMonID</c>) or saddled (<c>wUpgMonID</c>).</summary>
public sealed record MountTemplate(ushort Id, ushort NormalMonId, ushort SaddleMonId);

/// <summary>One owned pet (C++ <c>CTBLPetTable</c>, DBAccess.h:3144) — account-wide. <see cref="EndTime"/> is the
/// absolute expiry in unix seconds, 0 for a permanent pet (stored as 1900-01-01).</summary>
public readonly record struct PetRow(ushort PetId, string Name, long EndTime, byte Effect);

/// <summary>The account's saddle (C++ <c>TGetMountSaddle</c> outputs): the saddle item, its expiry (0 none) and
/// <c>bType</c> (1 = permanent). <see cref="ItemId"/> 0 means no saddle.</summary>
public readonly record struct SaddleRow(ushort ItemId, long EndTime, byte Type);

/// <summary>
/// The pet database: <c>TPETTABLE</c> (keyed by account) and the saddle procs. One method per C++ query, so the map
/// logic runs against an in-memory fake in tests.
/// </summary>
public interface IPetStore
{
    /// <summary><c>SELECT wPetID, szName, timeUse, bEffect FROM TPETTABLE WHERE dwUserID = ?</c>.</summary>
    Task<List<PetRow>> LoadPetsAsync(uint userId);

    /// <summary><c>TSavePet</c> — takes the character id and resolves the account itself.</summary>
    Task SavePetAsync(uint charId, PetRow pet);

    /// <summary><c>TPetDelete(dwUserID, wPetID)</c>.</summary>
    Task DeletePetAsync(uint userId, ushort petId);

    Task<SaddleRow> GetSaddleAsync(uint userId);
    Task SetSaddleAsync(uint userId, SaddleRow saddle);
    Task DeleteSaddleAsync(uint userId);
}

public sealed partial class GameDatabase : IPetStore
{
    public async Task<List<PetRow>> LoadPetsAsync(uint userId)
    {
        await using var c = await OpenAsync(default);
        await using var cmd = new SqlCommand("SELECT wPetID, szName, timeUse, bEffect FROM TPETTABLE WHERE dwUserID = @u", c);
        cmd.Parameters.Add(SqlProc.In("@u", SqlDbType.Int, unchecked((int)userId)));
        await using var r = await cmd.ExecuteReaderAsync();
        var list = new List<PetRow>();
        while (await r.ReadAsync())
            list.Add(new PetRow(r.GetUShortSafe(0), r.GetStringSafe(1), ToTime64(r, 2), r.GetByteSafe(3)));
        return list;
    }

    public async Task SavePetAsync(uint charId, PetRow pet)
    {
        await using var c = await OpenAsync(default);
        await SqlProc.ExecAsync(c, "TSavePet", null, new[]
        {
            SqlProc.In("@p0", SqlDbType.Int, unchecked((int)charId)),
            SqlProc.In("@p1", SqlDbType.SmallInt, unchecked((short)pet.PetId)),
            SqlProc.In("@p2", SqlDbType.VarChar, pet.Name, 50),
            SqlProc.In("@p3", SqlDbType.SmallDateTime, FromTime64(pet.EndTime)),
            SqlProc.In("@p4", SqlDbType.TinyInt, pet.Effect),
        }, default);
    }

    public async Task DeletePetAsync(uint userId, ushort petId)
    {
        await using var c = await OpenAsync(default);
        await SqlProc.ExecAsync(c, "TPetDelete", null, new[]
        {
            SqlProc.In("@p0", SqlDbType.Int, unchecked((int)userId)),
            SqlProc.In("@p1", SqlDbType.SmallInt, unchecked((short)petId)),
        }, default);
    }

    public async Task<SaddleRow> GetSaddleAsync(uint userId)
    {
        await using var c = await OpenAsync(default);
        var item = SqlProc.Out("@p1", SqlDbType.SmallInt);
        var end = SqlProc.Out("@p2", SqlDbType.SmallDateTime);
        var type = SqlProc.Out("@p3", SqlDbType.TinyInt);
        await SqlProc.ExecAsync(c, "TGetMountSaddle", null, new[]
        {
            SqlProc.In("@p0", SqlDbType.Int, unchecked((int)userId)), item, end, type,
        }, default);
        return new SaddleRow((ushort)item.AsInt(), TimeOf(end.Value), type.AsByte());
    }

    public async Task SetSaddleAsync(uint userId, SaddleRow saddle)
    {
        await using var c = await OpenAsync(default);
        await SqlProc.ExecAsync(c, "TSetMountSaddle", null, new[]
        {
            SqlProc.In("@p0", SqlDbType.Int, unchecked((int)userId)),
            SqlProc.In("@p1", SqlDbType.SmallInt, unchecked((short)saddle.ItemId)),
            SqlProc.In("@p2", SqlDbType.SmallDateTime, FromTime64(saddle.EndTime)),
            SqlProc.In("@p3", SqlDbType.Int, (int)saddle.Type),
        }, default);
    }

    public async Task DeleteSaddleAsync(uint userId)
    {
        await using var c = await OpenAsync(default);
        await SqlProc.ExecAsync(c, "TDelMountSaddle", null, new[]
        {
            SqlProc.In("@p0", SqlDbType.Int, unchecked((int)userId)),
        }, default);
    }

    /// <summary>A smalldatetime output back to unix seconds — C++ <c>__DBTOTIME</c>: pre-2000 (the 1900-01-01
    /// sentinel) and NULL read as 0.</summary>
    private static long TimeOf(object? value)
    {
        if (value is null or DBNull) return 0;
        var dt = Convert.ToDateTime(value);
        if (dt.Year < 2000) return 0;
        long secs = (long)(DateTime.SpecifyKind(dt, DateTimeKind.Utc) - DateTime.UnixEpoch).TotalSeconds;
        return secs < 0 ? 0 : secs;
    }
}
