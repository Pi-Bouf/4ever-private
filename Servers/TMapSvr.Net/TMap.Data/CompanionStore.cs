using System.Data;
using Microsoft.Data.SqlClient;

namespace TMap.Data;

/// <summary>A companion bonus (C++ <c>tagCOMPBONUS</c> from <c>TCOMPANIONBONUSCHART</c>): the owner bonus is
/// <c>Base + Multiplier·(level-1)</c>.</summary>
public readonly record struct CompanionBonus(byte BonusId, float Base, float Multiplier)
{
    public float At(byte level) => Base + Multiplier * (level - 1);
}

/// <summary>One owned companion as stored (C++ <c>TCOMPANIONTABLE</c> + its <c>TCOMPANIONITEMTABLE</c> row).</summary>
public sealed record CompanionRow(byte Slot, uint MonId, byte Level, string Name, uint Exp, uint Life, byte StatPoints,
    byte Effect, byte[] Stats, byte BonusId, ushort[] ItemIds, long[] EndTimes, uint Tick);

/// <summary>Everything the character load reads for companions (C++ <c>OnDM_LOADCHAR_REQ</c>, SSHandler.cpp:4283):
/// the records, the summoned slot (<c>TGetLastCompanion</c>, 0xFF none) and the medals (<c>TGetMedals</c>).</summary>
public sealed record CompanionLoad(List<CompanionRow> Companions, byte SummonedSlot, uint Medals);

/// <summary>
/// The companion database: <c>TCOMPANIONTABLE</c>/<c>TCOMPANIONITEMTABLE</c>, the last summoned slot and the medals.
/// One method per C++ query or procedure, so the map logic runs against a fake in tests.
/// </summary>
public interface ICompanionStore
{
    Task<CompanionLoad> LoadCompanionsAsync(uint charId);

    /// <summary><c>TSaveCompanion</c> — an upsert of the record and its items (20 parameters).</summary>
    Task SaveCompanionAsync(uint charId, CompanionRow c);

    /// <summary><c>TDeleteCompanion(dwCharID, bSlot)</c>.</summary>
    Task DeleteCompanionAsync(uint charId, byte slot);

    Task SaveLastCompanionAsync(uint charId, byte slot);
    Task SaveMedalsAsync(uint charId, uint medals);
}

public sealed partial class GameDatabase : ICompanionStore
{
    public async Task<CompanionLoad> LoadCompanionsAsync(uint charId)
    {
        await using var c = await OpenAsync(default);
        var list = new List<CompanionRow>();
        await using (var cmd = new SqlCommand(@"SELECT bSlot, dwMonID, bLevel, strName, dwExp, wLife, bStatusPoints, bEffect,
    wSTR, wDEX, wCON, wINT, wWIS, wMEN, wBonusID FROM TCOMPANIONTABLE WHERE dwCharID = @c", c))
        {
            cmd.Parameters.Add(SqlProc.In("@c", SqlDbType.Int, unchecked((int)charId)));
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var stats = new byte[6];
                for (int i = 0; i < 6; i++) stats[i] = r.GetByteSafe(8 + i);
                list.Add(new CompanionRow(r.GetByteSafe(0), r.GetUIntSafe(1), r.GetByteSafe(2), r.GetStringSafe(3),
                    r.GetUIntSafe(4), (uint)(ushort)r.GetIntSafe(5), r.GetByteSafe(6), r.GetByteSafe(7), stats,
                    r.GetByteSafe(14), new ushort[2], new long[2], 0));
            }
        }

        // Item rows only land on slots that exist (C++ OnDM_LOADCHAR_ACK overlays them).
        await using (var cmd = new SqlCommand(@"SELECT bSlot, dwTick, wFirstItemID, dFirstEndTime, wSecondItemID, dSecondEndTime
    FROM TCOMPANIONITEMTABLE WHERE dwCharID = @c", c))
        {
            cmd.Parameters.Add(SqlProc.In("@c", SqlDbType.Int, unchecked((int)charId)));
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                byte slot = r.GetByteSafe(0);
                int i = list.FindIndex(x => x.Slot == slot);
                if (i < 0) continue;
                list[i] = list[i] with
                {
                    Tick = r.GetUIntSafe(1),
                    ItemIds = new[] { r.GetUShortSafe(2), r.GetUShortSafe(4) },
                    EndTimes = new[] { ToTime64(r, 3), ToTime64(r, 5) },
                };
            }
        }

        var last = SqlProc.Out("@p1", SqlDbType.Int);
        await SqlProc.ExecAsync(c, "TGetLastCompanion", null, new[] { SqlProc.In("@p0", SqlDbType.Int, unchecked((int)charId)), last }, default);
        byte slotOut = last.Value is null or DBNull ? (byte)0xFF : (byte)last.AsInt();   // no row ⇒ none

        var medals = SqlProc.Out("@p1", SqlDbType.Int);
        await SqlProc.ExecAsync(c, "TGetMedals", null, new[] { SqlProc.In("@p0", SqlDbType.Int, unchecked((int)charId)), medals }, default);

        return new CompanionLoad(list, slotOut, medals.AsUInt());
    }

    public async Task SaveCompanionAsync(uint charId, CompanionRow x)
    {
        await using var c = await OpenAsync(default);
        var args = new List<SqlParameter>
        {
            SqlProc.In("@p0", SqlDbType.Int, unchecked((int)charId)),
            SqlProc.In("@p1", SqlDbType.TinyInt, x.Slot),
            SqlProc.In("@p2", SqlDbType.Int, unchecked((int)x.MonId)),
            SqlProc.In("@p3", SqlDbType.TinyInt, x.Level),
            SqlProc.In("@p4", SqlDbType.VarChar, x.Name, 55),
            SqlProc.In("@p5", SqlDbType.Int, unchecked((int)x.Exp)),
            // wLife is a SMALLINT while life runs to 300000 in game: a value past 32767 failed the whole C++ call.
            SqlProc.In("@p6", SqlDbType.SmallInt, (short)Math.Min(x.Life, 32767u)),
            SqlProc.In("@p7", SqlDbType.TinyInt, x.StatPoints),
            SqlProc.In("@p8", SqlDbType.TinyInt, x.Effect),
        };
        for (int i = 0; i < 6; i++) args.Add(SqlProc.In($"@p{9 + i}", SqlDbType.TinyInt, x.Stats[i]));
        args.Add(SqlProc.In("@p15", SqlDbType.TinyInt, x.BonusId));
        args.Add(SqlProc.In("@p16", SqlDbType.SmallInt, unchecked((short)x.ItemIds[0])));
        args.Add(SqlProc.In("@p17", SqlDbType.SmallInt, unchecked((short)x.ItemIds[1])));
        args.Add(SqlProc.In("@p18", SqlDbType.SmallDateTime, FromTime64(x.EndTimes[0])));
        args.Add(SqlProc.In("@p19", SqlDbType.SmallDateTime, FromTime64(x.EndTimes[1])));
        await SqlProc.ExecAsync(c, "TSaveCompanion", null, args, default);
    }

    public async Task DeleteCompanionAsync(uint charId, byte slot)
    {
        await using var c = await OpenAsync(default);
        await SqlProc.ExecAsync(c, "TDeleteCompanion", null, new[]
        {
            SqlProc.In("@p0", SqlDbType.Int, unchecked((int)charId)), SqlProc.In("@p1", SqlDbType.TinyInt, slot),
        }, default);
    }

    public async Task SaveLastCompanionAsync(uint charId, byte slot)
    {
        await using var c = await OpenAsync(default);
        await SqlProc.ExecAsync(c, "TSaveLastCompanion", null, new[]
        {
            SqlProc.In("@p0", SqlDbType.Int, unchecked((int)charId)), SqlProc.In("@p1", SqlDbType.TinyInt, slot),
        }, default);
    }

    public async Task SaveMedalsAsync(uint charId, uint medals)
    {
        await using var c = await OpenAsync(default);
        await SqlProc.ExecAsync(c, "TSaveMedals", null, new[]
        {
            SqlProc.In("@p0", SqlDbType.Int, unchecked((int)charId)), SqlProc.In("@p1", SqlDbType.Int, unchecked((int)medals)),
        }, default);
    }
}
