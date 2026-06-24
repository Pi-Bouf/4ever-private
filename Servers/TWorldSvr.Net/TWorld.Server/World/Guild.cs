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

    /// <summary>Rolling 7-day aggregate of <see cref="Records"/> (m_weekrecord), recomputed on each war-end.</summary>
    public GuildWeekRecord WeekRecord { get; } = new();

    /// <summary>Recompute this member's rolling 7-day record as of <paramref name="date"/>, pruning entries
    /// older than a week. CTGuild::CalcWeekRecord(member, date).</summary>
    public void RecalcWeekRecord(uint date)
    {
        WeekRecord.KillCount = 0; WeekRecord.DieCount = 0; Array.Clear(WeekRecord.Point);
        for (int w = 0; w < Records.Count;)
        {
            if (Records[w].Date + 7 <= date) { Records.RemoveAt(w); continue; }
            var rec = Records[w];
            WeekRecord.KillCount = (ushort)(WeekRecord.KillCount + rec.KillCount);
            WeekRecord.DieCount = (ushort)(WeekRecord.DieCount + rec.DieCount);
            for (int e = 0; e < WeekRecord.Point.Length; e++) WeekRecord.Point[e] += rec.Point[e];
            w++;
        }
    }
}

/// <summary>A member's rolling 7-day PvP totals (the aggregate fields of TENTRYRECORD).</summary>
public sealed class GuildWeekRecord
{
    public ushort KillCount { get; set; }
    public ushort DieCount { get; set; }
    public uint[] Point { get; } = new uint[8]; // PVPE_COUNT
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

    /// <summary>Recompute every member's rolling 7-day record (CTGuild::CalcWeekRecord).</summary>
    public void CalcWeekRecord(uint date)
    {
        foreach (var m in Members.Values) m.RecalcWeekRecord(date);
    }

    /// <summary>Add PvP points by flag bits (PVP_TOTAL bumps total+month, PVP_USEABLE bumps useable).
    /// CTGuild::GainPvPoint (the bEvent argument is unused by the original). Persistence is the caller's job.</summary>
    public void GainPvPoint(uint point, byte type)
    {
        if (point == 0) return;
        if ((type & Proto.PvpTotal) != 0) { PvPTotalPoint += point; PvPMonthPoint += point; }
        if ((type & Proto.PvpUseable) != 0) PvPUseablePoint += point;
    }

    /// <summary>Spend PvP points (clamped at 0). CTGuild::UsePvPoint.</summary>
    public void UsePvPoint(uint point, byte type)
    {
        if (point == 0) return;
        if ((type & Proto.PvpTotal) != 0) PvPTotalPoint = PvPTotalPoint > point ? PvPTotalPoint - point : 0;
        if ((type & Proto.PvpUseable) != 0) PvPUseablePoint = PvPUseablePoint > point ? PvPUseablePoint - point : 0;
    }

    /// <summary>Treasury as a single copper-denominated amount (NetCode.h CalcMoney radix MONEY_MULTIPLY).</summary>
    private long TreasuryMoney => Cooper + (long)Silver * Proto.MoneyMultiply + (long)Gold * Proto.MoneyMultiply * Proto.MoneyMultiply;

    private void SetTreasury(long money)
    {
        Cooper = (uint)(money % Proto.MoneyMultiply);
        Silver = (uint)(money / Proto.MoneyMultiply % Proto.MoneyMultiply);
        Gold = (uint)(money / Proto.MoneyMultiply / Proto.MoneyMultiply);
    }

    /// <summary>Spend treasury money. Returns false (and changes nothing) if the guild can't afford it.
    /// CTGuild::UseMoney(INT64, bUse). The DB contribution save is the caller's job.</summary>
    public bool UseMoney(long money, bool use)
    {
        if (TreasuryMoney < money) return false;
        if (use && money != 0) SetTreasury(TreasuryMoney - money);
        return true;
    }

    /// <summary>Add treasury money (gold/silver/copper denominations). CTGuild::GainMoney.</summary>
    public void GainMoney(uint gold, uint silver, uint cooper)
        => SetTreasury(TreasuryMoney + (cooper + (long)silver * Proto.MoneyMultiply + (long)gold * Proto.MoneyMultiply * Proto.MoneyMultiply));

    /// <summary>Prepend a PvP-point reward to the rolling TOP-50 log. CTGuild::PointLog.</summary>
    public void PointLog(uint point, string target, long date)
    {
        PointRewards.Insert(0, new GuildPointReward { Name = target, Point = point, Date = date });
        if (PointRewards.Count > 50) PointRewards.RemoveAt(PointRewards.Count - 1);
    }
}
