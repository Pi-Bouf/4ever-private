using TWorld.Protocol;

namespace TWorld.Server.World;

/// <summary>A matched BoW participant (C++ <c>BOWPLAYER</c>).</summary>
public sealed class BowPlayer
{
    public uint CharId { get; set; }
    public uint Key { get; set; }
    public byte Country { get; set; }
}

/// <summary>A queued/registered BoW player before the match is built (C++ <c>BOWTEMPPLAYER</c>).</summary>
public sealed class BowTempPlayer
{
    public uint CharId { get; set; }
    public uint Key { get; set; }
    public byte Country { get; set; }
}

/// <summary>
/// All Battle-of-the-Warlords state — the data half of the C++ <c>CTBowSystem</c>. The behaviour
/// (queue/match/phase machine) lives in <c>WorldService.Bow.cs</c> so it runs on the single batch task,
/// matching the rest of the world logic. Two constants come straight from <c>BowSystem.cpp</c>.
/// </summary>
public sealed class BowState
{
    public const int TeamCount = 2;     // BOW_TEAM_COUNT
    public const int MaxPoints = 10;    // BOW_MAX_POINTS

    public BowState(ushort mapId, byte minPlayersCount, byte maxNationDifference)
    {
        MapId = mapId;
        MinPlayersCount = minPlayersCount;
        MaxNationDifference = maxNationDifference;
        Status = BattleStatus.Peace;
        Winner = (byte)Contry.None;
        ResetPoints();
    }

    public ushort MapId { get; }
    public byte MinPlayersCount { get; }
    public byte MaxNationDifference { get; }

    public ServerSession? BowServer { get; set; }   // the dedicated BoW map (server id 30)

    public bool Running { get; set; }
    public BattleStatus Status { get; set; }
    public byte Winner { get; set; }

    public uint AlarmDur { get; set; }
    public uint BuyTimeDur { get; set; }
    public uint BattleDur { get; set; }

    public uint Start { get; set; }       // seconds-of-day the next match begins
    public uint Tick { get; set; }        // last broadcast countdown seconds
    public long PeaceTick { get; set; }   // monotonic ms when BODPEACE began (0 = not started)
    public bool SentEnd { get; set; }     // BOW_END already sent for this BODPEACE (C++ static SentEnd)

    public List<uint> Times { get; } = new();   // sorted seconds-of-day schedule
    public byte[] Points { get; } = new byte[TeamCount];

    public Dictionary<uint, BowPlayer> Players { get; } = new();           // matched roster
    public Dictionary<uint, BowTempPlayer> Reg { get; } = new();           // solo registrants
    public Dictionary<uint, List<BowTempPlayer>> GuildMembers { get; } = new(); // guildId -> registrants

    public void ResetPoints()
    {
        for (int i = 0; i < TeamCount; i++) Points[i] = MaxPoints / TeamCount;
    }

    public BowPlayer? FindPlayer(uint charId) => Players.TryGetValue(charId, out var p) ? p : null;

    public uint TotalDur => AlarmDur + BuyTimeDur + BattleDur;
}
