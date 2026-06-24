namespace TWorld.Server.World;

/// <summary>One map-server connection for a character — the ported subset of C++ <c>TCHARCON</c>.</summary>
public sealed class CharConnection
{
    public byte ServerId { get; init; }
    public uint IpAddr { get; set; }
    public ushort Port { get; set; }
    public bool Ready { get; set; }
    public bool Valid { get; set; } = true;
}

/// <summary>
/// In-memory character session — the ported Phase-1 subset of C++ <c>TCHARACTER</c>. Guild/party/
/// tactics/soulmate references are deferred (later phases) and treated as absent (0 / empty) when
/// building MW_ENTERCHAR_REQ.
/// </summary>
public sealed class Character
{
    public uint CharId { get; init; }
    public uint Key { get; set; }
    public uint UserId { get; set; }

    public byte MainId { get; set; }      // LOBYTE of the primary map server's wID
    public string Name { get; set; } = "";
    public ushort MapId { get; set; }
    public byte Channel { get; set; }     // current channel; == BR_SERVER_ID while in a Battle-Royale match
    public float PosX { get; set; }
    public float PosY { get; set; }
    public float PosZ { get; set; }

    public byte StartAct { get; set; }
    public byte Level { get; set; }
    public byte Country { get; set; }
    public byte Mode { get; set; }
    public byte HelmetHide { get; set; }
    public byte AidCountry { get; set; } = 3;   // TCONTRY_N — "no aid country" until ENTERSVR_ACK sets it
    public byte Class { get; set; }
    public uint Riding { get; set; }
    public long ChatBanTime { get; set; }
    public ushort TitleId { get; set; }
    public uint RankPoint { get; set; }

    public uint MaxHP { get; set; }
    public uint HP { get; set; }
    public uint MaxMP { get; set; }
    public uint MP { get; set; }

    public bool Logout { get; set; }
    public bool Save { get; set; }

    // Appearance/region — not carried by MW_CHARDATA_ACK in this build, so 0 until a later phase
    // populates them; included so the party/guild senders match the C++ field layout.
    public byte Race { get; set; }
    public byte Sex { get; set; }
    public byte RealSex { get; set; }   // true sex (Sex may be disguised); used by soulmate matching
    public byte Face { get; set; }
    public byte Hair { get; set; }
    public uint Region { get; set; }

    /// <summary>True while a party invite to this character is pending (m_bPartyWaiter).</summary>
    public bool PartyWaiter { get; set; }

    /// <summary>Live guild membership (null if none) — set on enter from the guild roster.</summary>
    public Guild? Guild { get; set; }

    /// <summary>Live party (null if none) — in-memory, dissolves on logout.</summary>
    public Party? Party { get; set; }

    // --- Phase 3: friends + soulmate (loaded from DB on enter, mirrors C++ TCHARACTER maps) ---

    /// <summary>friendId → friend entry (m_mapTFRIEND). Includes one-way "target" entries.</summary>
    public Dictionary<uint, Friend> Friends { get; } = new();

    /// <summary>group id → group name (m_mapFRIENDGROUP).</summary>
    public Dictionary<byte, string> FriendGroups { get; } = new();

    /// <summary>charId → soulmate entry (m_mapTSOULMATE): the entry keyed by this char's own id is "my"
    /// soulmate link; others are inbound links from people who chose this char.</summary>
    public Dictionary<uint, Soulmate> Soulmates { get; } = new();

    /// <summary>Unix time of a pending soulmate-end silence window (m_dwSoulSilence), else 0.</summary>
    public uint SoulSilence { get; set; }

    /// <summary>True once the friend list has begun loading (m_bDBLoading) — gates cross-char online sync.</summary>
    public bool DbLoading { get; set; }

    /// <summary>True once the friend + soulmate lists have been loaded from DB (load happens once, on enter).</summary>
    public bool SocialLoaded { get; set; }

    /// <summary>Tournament betting ticket count (m_dwTicket), set on entering the arena gate.</summary>
    public uint Ticket { get; set; }

    /// <summary>The tournament players this char has bet on (m_mapBatting): targetCharId → target.</summary>
    public Dictionary<uint, TnmtPlayer> Batting { get; } = new();

    /// <summary>serverId → connection (a char can be connected to several map servers).</summary>
    public Dictionary<byte, CharConnection> Connections { get; } = new();

    /// <summary>Ids of the TMS conversations this char belongs to (m_mapTMS).</summary>
    public HashSet<uint> TmsIds { get; } = new();

    // --- Phase 5a: cross-map movement / teleport ---

    /// <summary>Connections marked for closing (m_vTDEADCON) — flushed by ClearDeadCON on the next CHECKMAIN.</summary>
    public List<byte> DeadCons { get; } = new();

    /// <summary>The new main-server id being adopted during a main-server hand-off (m_bCHGMainID), else 0.</summary>
    public byte ChgMainId { get; set; }

    /// <summary>Per-char serialization queue for the multi-message teleport/connect cycles (m_qConCess):
    /// CHECKCONNECT_ACK and cross-channel BEGINTELEPORT_ACK are processed one cycle at a time. Stores the
    /// sender's map-server id plus the raw packet bytes so the deferred item can be re-dispatched.</summary>
    public Queue<(byte ServerId, byte[] Packet)> ConCess { get; } = new();
}
