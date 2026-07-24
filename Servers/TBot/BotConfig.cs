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

    public float MoveRadius { get; set; } = 40f;
    public float MoveSpeed { get; set; } = 3.0f;
    public float MoveStep { get; set; } = 2.0f;
    public int MoveTickMs { get; set; } = 100;
    public int MoveDurationSec { get; set; } = 30;
}
