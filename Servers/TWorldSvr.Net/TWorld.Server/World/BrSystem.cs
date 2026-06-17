using TWorld.Protocol;

namespace TWorld.Server.World;

/// <summary>A matched/premade BR participant (C++ <c>TBRPLAYERS</c>).</summary>
public sealed class BrPlayer
{
    public uint CharId { get; set; }
    public uint Key { get; set; }
    public string Name { get; set; } = "";
    public byte Class { get; set; }
    public bool Ready { get; set; }
}

/// <summary>A BR team — premade (keyed by chief) or matched (C++ <c>TBRTEAMS</c>).</summary>
public sealed class BrTeam
{
    public Dictionary<uint, BrPlayer> Players { get; } = new();
    public bool Ready { get; set; }
}

/// <summary>A queued BR registrant before teams are built (C++ <c>BRTEMPPLAYER</c>).</summary>
public sealed class BrTempPlayer
{
    public uint CharId { get; set; }
    public uint Key { get; set; }
    public string Name { get; set; } = "";
    public byte Class { get; set; }
}

/// <summary>A selectable BR map (C++ <c>BRMAP</c>).</summary>
public readonly record struct BrMap(ushort MapId, string Name);

/// <summary>
/// All Battle-Royale state — the data half of the C++ <c>CBRSystem</c>. Behaviour (queue / premade /
/// voting / match / phase machine) lives in <c>WorldService.Br.cs</c> so it runs on the single batch task.
/// </summary>
public sealed class BrState
{
    public const ushort DefaultMap = 710; // DEFAULT_BR_MAP (Blonea)

    public BrState(uint alarmDur, uint buyTimeDur, uint battleDur, byte minPlayerCount)
    {
        AlarmDur = alarmDur;
        BuyTimeDur = buyTimeDur;
        BattleDur = battleDur;
        MinPlayerCount = minPlayerCount;
        Status = BattleStatus.Normal;
        Type = (byte)BrType.Team;
        Mode = (byte)BrMode.ThreeV3;
        MapId = DefaultMap;
        Maps.Add(new BrMap(706, "Hod"));
        Maps.Add(new BrMap(710, "Blonea"));
        Maps.Add(new BrMap(703, "Colossus"));
        Maps.Add(new BrMap(708, "Tyconteroga"));
    }

    public ServerSession? BrServer { get; set; }

    public uint AlarmDur { get; set; }
    public uint BuyTimeDur { get; set; }
    public uint BattleDur { get; set; }
    public byte MinPlayerCount { get; set; }

    public bool Running { get; set; }
    public BattleStatus Status { get; set; }
    public byte Type { get; set; }    // BrType
    public byte Mode { get; set; }    // BrMode
    public ushort MapId { get; set; }

    public uint Start { get; set; }
    public uint Tick { get; set; }
    public long PeaceTick { get; set; }
    public bool SentEnd { get; set; }

    public List<uint> Times { get; } = new();
    public List<byte> BattleTypes { get; } = new();
    public List<BrMap> Maps { get; } = new();

    public Dictionary<uint, BrTeam> PremadeTeams { get; } = new();  // chiefId -> team
    public Dictionary<byte, BrTeam> Teams { get; } = new();         // index -> matched team
    public Dictionary<uint, BrTempPlayer> Reg { get; } = new();     // charId -> solo registrant
    public Dictionary<uint, byte> MapVote { get; } = new();         // userId -> map index
    public Dictionary<uint, byte> ModeVote { get; } = new();        // userId -> mode

    public uint TotalDur => AlarmDur + BuyTimeDur + BattleDur;
    public byte PartySize => (byte)(Mode == (byte)BrMode.ThreeV3 ? 3 : 2);

    public bool FindPlayerInPremade(uint charId)
        => PremadeTeams.Values.Any(t => t.Players.ContainsKey(charId));
    public bool FindPlayerInTeam(uint charId)
        => Teams.Values.Any(t => t.Players.ContainsKey(charId));
    public BrPlayer? FindPlayerInTeamPtr(uint charId)
        => Teams.Values.SelectMany(t => t.Players.Values).FirstOrDefault(p => p.CharId == charId);
    public byte PremadeCountByChief(uint chiefId)
        => PremadeTeams.TryGetValue(chiefId, out var t) ? (byte)t.Players.Count : (byte)0;
    public uint ChiefIdByMate(uint charId)
    {
        foreach (var (chief, team) in PremadeTeams)
            if (team.Players.ContainsKey(charId)) return chief;
        return 0;
    }
}
