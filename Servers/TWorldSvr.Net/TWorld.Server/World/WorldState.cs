using TWorld.Protocol;

namespace TWorld.Server.World;

/// <summary>
/// All live world state. Mirrors the C++ module's in-memory maps. <b>Accessed only from the single
/// batch task</b> (the C++ used one big batch lock), so plain dictionaries are safe here — there is no
/// concurrent access. Phase 1 holds servers + characters; guild/party/corps/ranking maps arrive later.
/// </summary>
public sealed class WorldState
{
    public Dictionary<ushort, ServerSession> Servers { get; } = new();   // wID -> peer
    public Dictionary<uint, Character> Characters { get; } = new();      // charId -> character
    public Dictionary<string, Character> CharactersByName { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<uint> ActiveUsers { get; } = new();

    public ServerSession? ControlServer { get; set; }
    public ServerSession? RelayServer { get; set; }

    public byte Nation { get; set; }

    // --- Phase 2: guilds (DB-backed) + parties/corps (in-memory) ---
    public Dictionary<byte, GuildLevel> GuildLevels { get; } = new();
    public Dictionary<uint, Guild> Guilds { get; } = new();         // guildId -> guild
    public Dictionary<uint, uint> CharGuild { get; } = new();       // charId -> guildId (membership index)
    public Dictionary<ushort, Party> Parties { get; } = new();      // partyId -> party
    public Dictionary<ushort, Corps> CorpsMap { get; } = new();     // corpsId -> corps
    public PartyIdPool PartyIds { get; } = new();

    public Guild? FindGuild(uint guildId) => Guilds.TryGetValue(guildId, out var g) ? g : null;
    public Guild? FindGuildByChar(uint charId)
        => CharGuild.TryGetValue(charId, out var gid) ? FindGuild(gid) : null;
    public Party? FindParty(ushort partyId) => Parties.TryGetValue(partyId, out var p) ? p : null;
    public Corps? FindCorps(ushort corpsId) => CorpsMap.TryGetValue(corpsId, out var c) ? c : null;

    // --- Phase 2b: recruitment boards + tactics membership index ---
    public Dictionary<uint, GuildWanted> GuildWanted { get; } = new();           // guildId -> wanted ad
    public Dictionary<uint, GuildTacticsWanted> TacticsWanted { get; } = new();  // wantedId -> tactics ad
    public uint TacticsWantedSeq { get; set; }
    public Dictionary<uint, uint> CharTactics { get; } = new();                  // charId -> guildId (tactics membership)

    /// <summary>The guild a char belongs to as a tactics (mercenary) member, if any.</summary>
    public Guild? FindTacticsGuild(uint charId)
        => CharTactics.TryGetValue(charId, out var gid) ? FindGuild(gid) : null;

    /// <summary>Current guild for display: tactics guild takes priority over the regular guild (GetCurGuild).</summary>
    public Guild? GetCurGuild(uint charId) => FindTacticsGuild(charId) ?? FindGuildByChar(charId);

    public GuildLevel? GuildLevelOf(byte level) => GuildLevels.TryGetValue(level, out var l) ? l : null;

    // --- Phase 3: PvP rankings + monthly rollover ---

    /// <summary>m_arMonthRank[country][rank]: index 0 is the country warlord, 1..N the monthly ladder.</summary>
    public MonthRanker[][] MonthRank { get; } = NewGrid(Proto.CountryCount, Proto.MonthRankCount);
    /// <summary>m_arFirstGradeGroup[country][rank]: last month's frozen first-grade ladder.</summary>
    public MonthRanker[][] FirstGradeGroup { get; } = NewGrid(Proto.CountryCount, Proto.FirstGradeGroupCount);
    /// <summary>m_arLastFameRank: the cross-country fame podium snapshot.</summary>
    public MonthRanker[] LastFameRank { get; } = NewRow(Proto.FameRankCount);

    /// <summary>Calendar month (1-12) the loaded rank tables belong to (m_bRankMonth).</summary>
    public byte RankMonth { get; set; }
    /// <summary>Set while a monthly rollover save is in flight (m_bFameRankSave).</summary>
    public bool FameRankSave { get; set; }

    // --- Phase 4: Battle of the Warlords / Battle Royale (null until the config row loads at startup). ---
    public BowState? Bow { get; set; }
    public BrState? Br { get; set; }

    // --- Phase 4c: scheduled battle-field windows (local / castle / mission / sky-garden). ---
    public BattleSchedule? Battles { get; set; }

    // --- Phase 4d: tournament config + announce state (null until config loads at startup). ---
    public TournamentState? Tournament { get; set; }

    private static MonthRanker[] NewRow(int n)
    {
        var a = new MonthRanker[n];
        for (int i = 0; i < n; i++) a[i] = new MonthRanker();
        return a;
    }

    private static MonthRanker[][] NewGrid(int rows, int cols)
    {
        var g = new MonthRanker[rows][];
        for (int i = 0; i < rows; i++) g[i] = NewRow(cols);
        return g;
    }

    /// <summary>Find a character by id, validating the session key (mirrors C++ <c>FindTChar</c>).</summary>
    public Character? FindChar(uint charId, uint key)
        => Characters.TryGetValue(charId, out var c) && c.Key == key ? c : null;

    /// <summary>Find a connected map server by its LOBYTE server id (mirrors C++ <c>FindMapSvr</c>).</summary>
    public ServerSession? FindMapSvr(byte serverId)
    {
        foreach (var s in Servers.Values)
            if (s.ServerId == serverId) return s;
        return null;
    }
}
