using TMap.Data;

namespace TMap.Server.Map;

/// <summary>
/// A summon (C++ <c>CTRecallMon</c>, <c>OT_RECALL</c>): a monster-like object owned by a player — a mount called with
/// a pet, or a summoner's creature. Its id comes from the world (<c>GenRecallID</c>), so it lives in its own registry
/// and cell bucket, apart from the field monsters. The owner's client drives it (movement, mode, attacks); the map
/// keeps its state and shows it to the players around with <c>CS_ADDRECALLMON_ACK</c>.
/// </summary>
public sealed class RecallMon
{
    public const byte OtRecall = 7;                  // OBJ_TYPE OT_RECALL
    public const byte TypePet = 7;                   // TRECALL_TYPE TRECALLTYPE_PET

    public uint Id { get; set; }
    public uint OwnerId { get; set; }                // m_dwHostID — the owning player's char id
    public ushort ChartId { get; set; }              // m_pMON->m_wID
    public MonsterTemplate? Template { get; set; }
    public MonAttrRow? Attr { get; set; }            // m_pATTR — TMONATTRCHART at (wSummonAttr, level)

    public ushort PetId { get; set; }                // m_wPetID — non-zero for a mount
    public byte Effect { get; set; }                 // m_bEffect — the mount's colour/aura
    public string Name { get; set; } = "";           // m_strName — the pet's name
    public byte RecallType { get; set; }             // m_bRecallType

    public byte Level { get; set; }
    public byte AtkLevel { get; set; }               // m_bAtkLevel — the owner's level at summoning
    public byte AtkSkillLevel { get; set; }          // m_bAtkSkillLevel
    public byte Hit { get; set; }                    // m_bHit
    public uint MaxHp { get; set; }
    public uint Hp { get; set; }
    public uint MaxMp { get; set; }
    public uint Mp { get; set; }

    public byte Country { get; set; }
    public byte AidCountry { get; set; }
    public uint Region { get; set; }
    public byte Channel { get; set; }
    public ushort MapId { get; set; }
    public float PosX { get; set; }
    public float PosY { get; set; }
    public float PosZ { get; set; }
    public ushort Pitch { get; set; }
    public ushort Dir { get; set; }
    public byte MouseDir { get; set; } = Monster.TkdirN;
    public byte KeyDir { get; set; } = Monster.TkdirN;
    public byte Action { get; set; }
    public byte Mode { get; set; }
    public byte Status { get; set; } = 1;            // OS_WAKEUP

    public uint TargetId { get; set; }
    public byte TargetType { get; set; }

    /// <summary>C++ <c>m_dwRecallTick</c> / <c>m_dwDurationTick</c> — summoned at, and how long it lasts (ms). A 0
    /// duration never expires.</summary>
    public long RecallTickMs { get; set; }
    public uint DurationMs { get; set; }

    public List<ushort> Skills { get; } = new();
    public List<MaintainSkill> MaintainSkills { get; } = new();

    public uint CellKey { get; set; }
    public bool InMap { get; set; }

    /// <summary>The expiry removal was already asked of the world (sent once, not every tick).</summary>
    public bool DeleteAsked { get; set; }

    /// <summary>C++ <c>CTRecallMon::GetLifeTick</c> — what is left of its life, in ms (0 for an immortal one).</summary>
    public uint LifeLeft(long nowMs)
    {
        if (DurationMs == 0) return 0;
        long left = RecallTickMs + DurationMs - nowMs;
        return left <= 0 ? 0 : (uint)left;
    }

    public bool Expired(long nowMs) => DurationMs != 0 && LifeLeft(nowMs) == 0;
}

/// <summary>An owned pet (C++ <c>tagPET</c>): account-wide mount licence. <see cref="EndTime"/> is the expiry in unix
/// seconds, 0 for permanent.</summary>
public sealed class Pet
{
    public ushort PetId { get; set; }
    public string Name { get; set; } = "";
    public long EndTime { get; set; }
    public byte Effect { get; set; }
    public MountTemplate? Template { get; set; }
}
