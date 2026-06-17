namespace TWorld.Data;

/// <summary>A row of TGUILDCHART (guild-level chart).</summary>
public sealed record GuildLevelRow(
    byte Level, uint Exp, byte MaxCnt, byte MinCnt, byte CabinetCnt, byte TacticsCnt,
    byte BattleSetCnt, byte GuardCnt, byte RoyalGuardCnt, byte TurretCnt,
    byte Peer1, byte Peer2, byte Peer3, byte Peer4, byte Peer5);

/// <summary>A row of TGUILDTABLE (guild header).</summary>
public sealed record GuildRow(
    uint Id, string Name, uint Chief, byte Level, uint Fame, uint FameColor, byte MaxCabinet,
    uint GI, uint Exp, byte GPoint, byte Status, uint Gold, uint Silver, uint Cooper,
    byte Disorg, uint Time, long TimeEstablish, uint PvPTotalPoint, uint PvPUseablePoint, uint PvPMonthPoint);

/// <summary>A row of TGUILDMEMBER (with its owning guild id).</summary>
public sealed record GuildMemberRow(
    uint GuildId, uint CharId, string Name, byte Country, byte WarCountry,
    byte Level, byte Class, byte Duty, byte Peer, long ConnectedDate);

/// <summary>Result of TGuildEstablish.</summary>
public sealed record GuildEstablishRow(int Ret, uint GuildId);

// ----- Phase 2b load rows -----
public sealed record GuildArticleRow(uint GuildId, uint Id, byte Duty, string Writer, string Title, string Article, uint Time);
public sealed record GuildTacticsRow(uint GuildId, uint CharId, string Name, byte Level, byte Class, uint RewardPoint, long RewardMoney, uint GainPoint, byte Day, long EndTime);
public sealed record GuildRelationRow(byte Type, uint GuildOne, uint GuildTwo);
public sealed record GuildPointRewardRow(uint GuildId, string Name, uint Point, long Date);
public sealed record GuildPvpRecordRow(uint GuildId, uint CharId, uint Date, ushort KillCount, ushort DieCount, uint[] Point);
public sealed record GuildStatsRow(uint GuildId, byte SkillPoint, byte Level, uint Exp);
public sealed record GuildCabinetRow(uint OwnerId, long ItemDbId, uint StorageId, ushort ItemId, byte Level, byte Count, byte GLevel, uint DuraMax, uint DuraCur, byte RefineCur, long EndTime, byte GradeEffect, byte[] Magic, ushort[] Value, uint[] ExtValue);
public sealed record GuildWantedRow(uint GuildId, byte MinLevel, byte MaxLevel, long EndTime, string Title, string Text);
public sealed record GuildTacticsWantedRow(uint Id, uint GuildId, byte MinLevel, byte MaxLevel, long EndTime, string Title, string Text, byte Day, uint Gold, uint Silver, uint Cooper, uint PvPoint);
public sealed record GuildVolunteerRow(byte Type, uint Id, uint CharId, string Name, byte Level, byte Class);
