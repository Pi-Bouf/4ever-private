using TWorld.Protocol;

namespace TWorld.Server.World;

/// <summary>A row of the guild-level chart (TGUILDCHART → TGUILDLEVEL). Caps member/peer counts per level.</summary>
public sealed class GuildLevel
{
    public byte Level { get; init; }
    public uint Exp { get; init; }          // exp needed for the NEXT level
    public byte MaxCnt { get; init; }
    public byte MinCnt { get; init; }
    public byte CabinetCnt { get; init; }
    public byte TacticsCnt { get; init; }
    public byte BattleSetCnt { get; init; }
    public byte GuardCnt { get; init; }
    public byte RoyalGuardCnt { get; init; }
    public byte TurretCnt { get; init; }
    public byte[] Peer { get; } = new byte[Proto.MaxGuildPeer]; // peer-rank caps
}

/// <summary>A guild member (TGUILDMEMBER). Online members carry a live <see cref="OnlineChar"/> link.</summary>
public sealed class GuildMember
{
    public uint CharId { get; init; }
    public string Name { get; set; } = "";
    public byte Level { get; set; }
    public byte Class { get; set; }
    public byte Duty { get; set; }          // GuildDuty
    public byte Peer { get; set; }          // 0..5
    public byte Country { get; set; }
    public ushort Castle { get; set; }
    public byte Camp { get; set; }
    public uint Tactics { get; set; }
    public byte WarCountry { get; set; }
    public long ConnectedDate { get; set; }

    public Character? OnlineChar { get; set; }
    public bool Online => OnlineChar is not null;

    /// <summary>Per-member PvP records (TENTRYRECORD list).</summary>
    public List<GuildPvpRecord> Records { get; } = new();
}

/// <summary>
/// In-memory guild (CTGuild) — the Phase-2 subset. Holds the member roster and the treasury/fame/level
/// header from TGUILDTABLE. Tactics/cabinet/articles/PvP-logs are deferred (Phase 2b).
/// </summary>
public sealed class Guild
{
    public uint Id { get; init; }
    public string Name { get; set; } = "";
    public uint Chief { get; set; }
    public string ChiefName { get; set; } = "";
    public byte Level { get; set; } = 1;
    public uint Fame { get; set; }
    public uint FameColor { get; set; }
    public byte MaxCabinet { get; set; }
    public uint GI { get; set; }
    public uint Exp { get; set; }
    public byte GPoint { get; set; }
    public byte Status { get; set; }
    public uint Gold { get; set; }
    public uint Silver { get; set; }
    public uint Cooper { get; set; }
    public byte Disorg { get; set; }
    public uint Time { get; set; }
    public byte Country { get; set; }
    public long TimeEstablish { get; set; }
    public string ArticleTitle { get; set; } = "";
    public uint PvPTotalPoint { get; set; }
    public uint PvPUseablePoint { get; set; }
    public uint PvPMonthPoint { get; set; }
    public uint RankTotal { get; set; }
    public uint RankMonth { get; set; }
    public byte StatLevel { get; set; }
    public byte StatPoint { get; set; }
    public uint StatExp { get; set; }

    public GuildLevel? LevelChart { get; set; }

    /// <summary>charId → member.</summary>
    public Dictionary<uint, GuildMember> Members { get; } = new();

    // --- Phase 2b long-tail ---
    public Dictionary<uint, TacticsMember> Tactics { get; } = new();   // charId → tactics (mercenary) member
    public Dictionary<uint, GuildArticle> Articles { get; } = new();   // articleId → board post
    public uint ArticleSeq { get; set; }                               // next article id
    public List<GuildItem> Cabinet { get; } = new();                   // treasury items
    public List<uint> Allies { get; } = new();
    public List<uint> Enemies { get; } = new();
    public List<GuildPointReward> PointRewards { get; } = new();       // TOP 50 PvP point reward log
    public byte StatLevel2 { get; set; }                               // guild-skill stat (alias of StatLevel)

    public TacticsMember? FindTactics(uint charId) => Tactics.TryGetValue(charId, out var t) ? t : null;
    public TacticsMember? FindTactics(string name)
        => Tactics.Values.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

    public GuildMember? FindMember(uint charId) => Members.TryGetValue(charId, out var m) ? m : null;
    public GuildMember? FindMember(string name)
        => Members.Values.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));

    public byte FindDuty(uint charId) => FindMember(charId)?.Duty ?? 0;
    public byte FindPeer(uint charId) => FindMember(charId)?.Peer ?? 0;

    /// <summary>Max members allowed at the current level (falls back to a sane default).</summary>
    public ushort MaxMembers => LevelChart?.MaxCnt ?? 20;
    public uint NextLevelExp => LevelChart?.Exp ?? 0;

    public bool IsChief(uint charId) => FindMember(charId)?.Duty == (byte)GuildDuty.Chief;

    /// <summary>The two vice-chiefs' names (for guild info), or empty.</summary>
    public (string, string) ViceChiefNames()
    {
        var vices = Members.Values.Where(m => m.Duty == (byte)GuildDuty.ViceChief).Select(m => m.Name).ToList();
        return (vices.Count > 0 ? vices[0] : "", vices.Count > 1 ? vices[1] : "");
    }

    public byte ChiefPeer()
    {
        var chief = FindMember(Chief);
        return chief?.Peer ?? 0;
    }

    /// <summary>True while the guild has fewer than 49 members+tactics applied to <paramref name="castle"/>
    /// (CTGuild::CanApplyWar — the per-guild castle-war slot cap).</summary>
    public bool CanApplyWar(ushort castle)
    {
        int count = Members.Values.Count(m => m.Castle == castle) + Tactics.Values.Count(t => t.Castle == castle);
        return count < 49;
    }

    /// <summary>Number of members+tactics applied to <paramref name="castle"/>, plus the last camp seen
    /// (CTGuild::GetCastleApplicantCount → MAKEWORD(count, camp)).</summary>
    public (byte Count, byte Camp) GetCastleApplicantCount(ushort castle)
    {
        byte count = 0, camp = 0;
        foreach (var m in Members.Values) if (m.Castle == castle) { count++; camp = m.Camp; }
        foreach (var t in Tactics.Values) if (t.Castle == castle) { count++; camp = t.Camp; }
        return (count, camp);
    }
}
