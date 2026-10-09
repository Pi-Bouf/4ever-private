using System.Data;
using Microsoft.Data.SqlClient;

namespace TMap.Data;

/// <summary>A guild skill held (C++ <c>GUILDSKILL</c>): its level and when it runs out (seconds since 1970, 0 = never bought
/// time).</summary>
public readonly record struct GuildSkillRow(ushort SkillId, byte Level, long EndTime);

/// <summary>
/// The guild skills in the database (migration 016): a member's skills (<c>TGUILDMEMBERSKILLTABLE</c>, by character) and the
/// guild's officer skills (<c>TGUILDMASTERSKILLTABLE</c>, by guild) — C++ <c>CTBLGuildSkillMember</c> / <c>CTBLGuildSkillDuty</c>
/// and <c>CSPSaveGuildSkill</c>. Behind an interface so the map logic can be tested against an in-memory fake.
/// </summary>
public interface IGuildSkillStore
{
    /// <summary>The character's member skills, and — when <paramref name="guildId"/> is not 0 — the guild's officer skills.</summary>
    Task<(List<GuildSkillRow> Member, List<GuildSkillRow> Guild)> LoadGuildSkillsAsync(uint charId, uint guildId);

    /// <summary><c>TSaveGuildSkill</c>: a member skill (type 0) under the character, an officer skill under the guild.</summary>
    Task SaveGuildSkillAsync(uint charId, uint guildId, byte type, GuildSkillRow skill);
}

public sealed partial class GameDatabase : IGuildSkillStore
{
    public async Task<(List<GuildSkillRow> Member, List<GuildSkillRow> Guild)> LoadGuildSkillsAsync(uint charId, uint guildId)
    {
        await using var c = await OpenAsync(default);
        await using var cmd = new SqlCommand(@"SELECT wSkillID, bLevel, tEndTime FROM TGUILDMEMBERSKILLTABLE WHERE dwCharID = @c;
SELECT wSkillID, bLevel, tEndTime FROM TGUILDMASTERSKILLTABLE WHERE dwGuildID = @g AND @g <> 0", c);
        cmd.Parameters.Add(SqlProc.In("@c", SqlDbType.Int, unchecked((int)charId)));
        cmd.Parameters.Add(SqlProc.In("@g", SqlDbType.Int, unchecked((int)guildId)));
        await using var r = await cmd.ExecuteReaderAsync();
        var member = new List<GuildSkillRow>();
        while (await r.ReadAsync()) member.Add(new GuildSkillRow(r.GetUShortSafe(0), r.GetByteSafe(1), ToTime64(r, 2)));
        var guild = new List<GuildSkillRow>();
        if (await r.NextResultAsync())
            while (await r.ReadAsync()) guild.Add(new GuildSkillRow(r.GetUShortSafe(0), r.GetByteSafe(1), ToTime64(r, 2)));
        return (member, guild);
    }

    public async Task SaveGuildSkillAsync(uint charId, uint guildId, byte type, GuildSkillRow skill)
    {
        await using var c = await OpenAsync(default);
        await SqlProc.ExecAsync(c, "TSaveGuildSkill", null, new[]
        {
            SqlProc.In("@p0", SqlDbType.Int, unchecked((int)charId)),
            SqlProc.In("@p1", SqlDbType.Int, unchecked((int)guildId)),
            SqlProc.In("@p2", SqlDbType.TinyInt, type),
            SqlProc.In("@p3", SqlDbType.SmallInt, unchecked((short)skill.SkillId)),
            SqlProc.In("@p4", SqlDbType.TinyInt, skill.Level),
            SqlProc.In("@p5", SqlDbType.SmallDateTime, FromTime64(skill.EndTime)),
        }, default);
    }
}
