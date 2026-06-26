using TWorld.Protocol;

namespace TWorld.Server.World;

/// <summary>One scheduled tournament step (C++ <c>TOURNAMENTSTEP</c>).</summary>
public sealed class TournamentStep
{
    public byte Group { get; set; }
    public byte StepId { get; set; }
    public uint Period { get; set; }
    public long Start { get; set; }   // absolute unix seconds (0 = fired/unset)
    public long End { get; set; }
}

/// <summary>A registered GM event-tournament schedule (C++ <c>TOURNAMENTSCHEDULE</c> + its
/// <c>m_mapTournamentTime</c> battle-window). Each carries the Nth-weekday window the steps key off and the
/// computed step list; the earliest-starting schedule becomes the running tournament (C++ TournamentUpdate).</summary>
public sealed class EventTournamentSchedule
{
    public ushort Id { get; set; }
    public bool Enable { get; set; }
    public byte Week { get; set; }        // window: Nth occurrence
    public byte Day { get; set; }         // window: MFC day-of-week (1=Sun..7=Sat)
    public uint BattleStart { get; set; } // window: seconds-into-day offset
    public List<TournamentStep> Steps { get; } = new();
}

/// <summary>A tournament reward row (C++ <c>TNMTREWARD</c>).</summary>
public sealed class TournamentReward
{
    public byte ChartType { get; set; }
    public ushort ItemId { get; set; }
    public byte Count { get; set; }
    public uint Class { get; set; }
    public byte CheckShield { get; set; }
}

/// <summary>A registered tournament participant (C++ <c>TNMTPLAYER</c>, the runtime subset).</summary>
public sealed class TnmtPlayer
{
    public uint CharId { get; set; }
    public byte Country { get; set; }
    public string Name { get; set; } = "";
    public byte Level { get; set; }
    public byte Class { get; set; }
    public uint Rank { get; set; }
    public uint MonthRank { get; set; }
    public byte EntryId { get; set; }
    public uint ChiefId { get; set; }
    public string Hwid { get; set; } = "";
    public uint IpAddr { get; set; }
    public string GuildName { get; set; } = "";
    public byte SlotId { get; set; } = 8;          // TOURNAMENT_SLOT = unseeded
    public byte[] Result { get; } = new byte[3];   // [QFINAL, SFINAL, FINAL]
    public Dictionary<uint, TnmtPlayer> Party { get; } = new();

    // betting: who bet on this player and the pool total (m_mapBatting + m_dwSum)
    public Dictionary<uint, uint> Batting { get; } = new();   // bettorCharId → ticket amount
    public uint Sum { get; set; }
}

/// <summary>A tournament bracket entry / division (C++ <c>TOURNAMENTENTRY</c>).</summary>
public sealed class TournamentEntry
{
    public byte Group { get; set; }
    public byte EntryId { get; set; }
    public string Name { get; set; } = "";
    public byte Type { get; set; }
    public uint Class { get; set; }
    public uint Fee { get; set; }
    public uint FeeBack { get; set; }
    public ushort PermitItemId { get; set; }
    public byte PermitCount { get; set; }
    public byte MinLevel { get; set; }
    public byte MaxLevel { get; set; } = 0xFF;
    public List<TournamentReward> Rewards { get; } = new();

    // Registration rosters (C++ m_map1st / m_mapNormal / m_mapPlayer).
    public Dictionary<uint, TnmtPlayer> First { get; } = new();   // 1st-grade seeds
    public Dictionary<uint, TnmtPlayer> Normal { get; } = new();  // open registrants
    public Dictionary<uint, TnmtPlayer> Player { get; } = new();  // the seeded match bracket
}

/// <summary>
/// Tournament configuration + current announce state — the ported C++ <c>m_mapTournament</c> entry set
/// plus the <c>m_tournament</c> status fields used by <c>TournamentInfo</c>. The full date-math scheduler
/// (<c>SetTournamentTime</c>), bracket progression, parties, events, and betting are a later slice.
/// </summary>
public sealed class TournamentState
{
    /// <summary>entryId → bracket entry (C++ MAPTOURNAMENTENTRY, std::map by entryId).</summary>
    public Dictionary<byte, TournamentEntry> Entries { get; } = new();

    public ushort Id { get; set; }            // m_tournament.m_wID (0 = none active)
    public byte Group { get; set; }           // m_tournament.m_bGroup
    public byte Step { get; set; }            // m_tournament.m_bStep
    public byte FirstGroupCount { get; set; } // m_bFirstGroupCount
    public byte MaxLevel { get; set; }        // m_bMaxLevel

    // --- scheduler state (m_tournament + m_TournamentSchedule) ---
    public List<TournamentStep> Steps { get; } = new();   // the active schedule's steps
    public bool ScheduleActive { get; set; }              // m_TournamentSchedule.m_wID != 0
    public bool ScheduleInitialized { get; set; }         // one-shot SetTournamentTime done at first tick
    public bool Selected { get; set; }                    // m_bSelected (bracket already seeded this group)
    public byte Base { get; set; }                        // m_bBase (prize base per entry)
    public uint Sum { get; set; }                         // m_dwSum (betting pool)

    /// <summary>charId → registered participant (C++ m_mapTNMTPlayer).</summary>
    public Dictionary<uint, TnmtPlayer> Players { get; } = new();

    public int EntryCount => Entries.Count;
    public TnmtPlayer? FindPlayer(uint charId) => Players.TryGetValue(charId, out var p) ? p : null;
    public TournamentEntry? Entry(byte entryId) => Entries.TryGetValue(entryId, out var e) ? e : null;
}
