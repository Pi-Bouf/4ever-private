namespace TBot;

/// <summary>
/// TBot run configuration (bound from the "Bot" section of appsettings.json, overridable on the
/// command line, e.g. <c>--Bot:Account=test2</c>).
/// </summary>
public sealed class BotConfig
{
    public string LoginHost { get; set; } = "127.0.0.1";
    public int LoginPort { get; set; } = 4816;

    public string Account { get; set; } = "test";
    public string Password { get; set; } = "test123";

    public bool CreateAccount { get; set; }

    /// <summary>When true, only ensure the account exists (no login/char/enter) and exit.</summary>
    public bool AccountOnly { get; set; }

    public string GlobalConnectionString { get; set; } = "";

    /// <summary>Game DB connection (TGame_gsp), needed only for MatchPositionOfCharId.</summary>
    public string GameConnectionString { get; set; } = "";

    /// <summary>If &gt;0, spawn the bot's char at this character's region/position (e.g. 2 = Pittt) so it
    /// appears in the main-world region with other players instead of the country-4 newbie zone.</summary>
    public uint MatchPositionOfCharId { get; set; }

    public byte GroupId { get; set; } = 1;
    public byte Channel { get; set; }

    /// <summary>Diagnostic only: when non-zero, sent as the CS_LOGIN_REQ version instead of TVERSION.</summary>
    public ushort OverrideVersion { get; set; }

    /// <summary>When true, the LOGIN connection runs without the session cipher (this deployment's C++ login is crypt-off).</summary>
    public bool NoCrypt { get; set; }

    /// <summary>When true, the MAP connection runs without the session cipher. The C++ map may differ from login.</summary>
    public bool MapNoCrypt { get; set; }

    public string CharName { get; set; } = "TBot01";
    public byte CharSlot { get; set; }
    public byte Class { get; set; } = 1;
    public byte Race { get; set; }
    public byte Country { get; set; } = 1;
    public byte Sex { get; set; }
    public byte Hair { get; set; }
    public byte Face { get; set; }
    public byte Body { get; set; }
    public byte Pants { get; set; }
    public byte Hand { get; set; }
    public byte Foot { get; set; }
    public byte LevelOption { get; set; }

    /// <summary>Prefix for this bot's log lines (set per bot when a scenario runs several).</summary>
    public string LogTag { get; set; } = "";

    /// <summary>When set, runs a scripted multi-bot scenario instead of the walk: <c>features</c> drives party,
    /// mail, hotkeys, bags and teleport between <see cref="Account"/> and <see cref="Account2"/>.</summary>
    public string Scenario { get; set; } = "";

    /// <summary>The second bot's account for a scenario (same password, first character).</summary>
    public string Account2 { get; set; } = "";

    public float MoveRadius { get; set; } = 40f;
    public float MoveSpeed { get; set; } = 3.0f;
    public float MoveStep { get; set; } = 2.0f;
    public int MoveTickMs { get; set; } = 100;
    public int MoveDurationSec { get; set; } = 30;

    /// <summary>Suppresses the per-bot log lines (the stress scenario prints its own status instead).</summary>
    public bool Quiet { get; set; }

    // ---- stress scenario (--Bot:Scenario=stress) ----

    /// <summary>How many bots to run at once, each on its own account (<see cref="StressPrefix"/> + number).</summary>
    public int StressCount { get; set; } = 100;
    /// <summary>Account prefix; use a fresh one per run (a used account logs in as "Duplicate" until servers restart).</summary>
    public string StressPrefix { get; set; } = "st";
    /// <summary>Ramp: how many bots start per second.</summary>
    public double StressRampPerSec { get; set; } = 10;
    /// <summary>Each bot walks a square of a different radius (20..this) so the crowd spreads over several cells;
    /// set to 20 to keep everyone in the same few cells (worst-case broadcast).</summary>
    public float StressMaxRadius { get; set; } = 300f;
    /// <summary>Docker container of the map server to sample (RSS + CPU) in the status line; empty = don't sample.</summary>
    public string StressDockerContainer { get; set; } = "araz-mapsvr";

    /// <summary>A shallow copy (every setting is a value or an immutable string).</summary>
    public BotConfig Copy() => (BotConfig)MemberwiseClone();
}
