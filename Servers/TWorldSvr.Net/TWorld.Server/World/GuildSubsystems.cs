namespace TWorld.Server.World;

// Phase 2b: the guild long-tail state (articles, cabinet, tactics/mercenary members, relations, PvP
// records/rewards, stats, wanted/volunteer boards). Ported from TWorldType.h structs.

/// <summary>A guild board post (TGUILDARTICLE).</summary>
public sealed class GuildArticle
{
    public uint Id { get; init; }
    public byte Duty { get; set; }
    public string Writer { get; set; } = "";
    public string Title { get; set; } = "";
    public string Article { get; set; } = "";
    public string Date { get; set; } = "";
}

/// <summary>A guild cabinet (treasury) item — the Phase-2b subset of TITEM the cabinet list needs.</summary>
public sealed class GuildItem
{
    public long ItemDbId { get; init; }   // m_dlID
    public uint StorageId { get; set; }    // m_dwItemID (slot)
    public ushort ItemId { get; set; }     // m_wItemID
    public byte Level { get; set; }
    public byte Count { get; set; }
    public byte GLevel { get; set; }
    public uint DuraMax { get; set; }
    public uint DuraCur { get; set; }
    public byte RefineCur { get; set; }
    public long EndTime { get; set; }
    public byte GradeEffect { get; set; }
    public byte[] Magic { get; } = new byte[6];
    public ushort[] Value { get; } = new ushort[6];
    public uint[] ExtValue { get; } = new uint[6];
}

/// <summary>A guild tactics (paid mercenary / sub-guild) member (TTACTICSMEMBER).</summary>
public sealed class TacticsMember
{
    public uint CharId { get; init; }
    public string Name { get; set; } = "";
    public byte Level { get; set; }
    public byte Class { get; set; }
    public uint RewardPoint { get; set; }
    public uint GainPoint { get; set; }
    public byte Day { get; set; }
    public long EndTime { get; set; }
    public ushort Castle { get; set; }
    public byte Camp { get; set; }
    public long RewardMoney { get; set; }
    public Character? OnlineChar { get; set; }
    public bool Online => OnlineChar is not null;
}

/// <summary>A guild PvP-point reward log entry (TGUILDPOINTREWARD).</summary>
public sealed class GuildPointReward
{
    public string Name { get; init; } = "";
    public uint Point { get; init; }
    public long Date { get; init; }
}

/// <summary>A per-member guild PvP record (TENTRYRECORD).</summary>
public sealed class GuildPvpRecord
{
    public uint CharId { get; init; }
    public uint Date { get; init; }
    public ushort KillCount { get; set; }
    public ushort DieCount { get; set; }
    public uint[] Point { get; } = new uint[8]; // PVPE_COUNT
}

/// <summary>A guild recruitment ad (TGUILDWANTED) with its applicants.</summary>
public sealed class GuildWanted
{
    public uint GuildId { get; init; }
    public byte Country { get; set; }
    public byte MinLevel { get; set; }
    public byte MaxLevel { get; set; }
    public long EndTime { get; set; }
    public string Name { get; set; } = "";
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public Dictionary<uint, GuildWantedApp> Apps { get; } = new();
}

/// <summary>A guild-tactics recruitment ad (TGUILDTACTICSWANTED) with its applicants.</summary>
public sealed class GuildTacticsWanted
{
    public uint Id { get; init; }
    public uint GuildId { get; set; }
    public uint Point { get; set; }
    public uint Gold { get; set; }
    public uint Silver { get; set; }
    public uint Cooper { get; set; }
    public byte Day { get; set; }
    public byte Country { get; set; }
    public byte MinLevel { get; set; }
    public byte MaxLevel { get; set; }
    public long EndTime { get; set; }
    public string Name { get; set; } = "";
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public Dictionary<uint, GuildTacticsWantedApp> Apps { get; } = new();
}

/// <summary>An applicant to a guild wanted ad (TGUILDWANTEDAPP).</summary>
public sealed class GuildWantedApp
{
    public uint CharId { get; init; }
    public uint WantedId { get; set; }
    public uint Region { get; set; }
    public byte Class { get; set; }
    public byte Level { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>An applicant to a guild-tactics wanted ad (TGUILDTACTICSWANTEDAPP).</summary>
public sealed class GuildTacticsWantedApp
{
    public uint CharId { get; init; }
    public uint WantedGuildId { get; set; }
    public uint WantedId { get; set; }
    public uint Region { get; set; }
    public uint Point { get; set; }
    public uint Gold { get; set; }
    public uint Silver { get; set; }
    public uint Cooper { get; set; }
    public byte Day { get; set; }
    public byte Class { get; set; }
    public byte Level { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>Guild diplomacy relation type (TGUILDRELATION.bType).</summary>
public enum GuildRelation : byte
{
    Alliance = 1,
    Enemy = 2,
}
