using TWorld.Protocol;

namespace TWorld.Server.World;

/// <summary>
/// One scheduled battle-field window — the ported C++ <c>TBATTLETIME</c>. All times are seconds-of-day.
/// The state machine that drives these lives in <c>WorldService.Battle.cs</c>.
/// </summary>
public sealed class BattleTime
{
    public byte Type { get; set; }
    public BattleStatus Status { get; set; } = BattleStatus.Normal;
    public uint BattleDur { get; set; }
    public uint BattleStart { get; set; }
    public uint AlarmStart { get; set; }
    public uint AlarmEnd { get; set; }
    public uint PeaceDur { get; set; }
    public byte Day { get; set; }       // castle/skygarden day-of-week (MFC: 1=Sun..7=Sat), 0 = unused
    public byte Week { get; set; }
    public uint LeftTime { get; set; }
    public bool Run { get; set; }
}

/// <summary>
/// The full battle-time schedule: one <see cref="BattleTime"/> per <see cref="BattleType"/> plus the
/// mission custom-time list (m_battletime[] + m_vCUSTOMTIMES). Lives on <see cref="WorldState"/>.
/// </summary>
public sealed class BattleSchedule
{
    public BattleTime[] Times { get; }
    public List<uint> CustomTimes { get; } = new();   // mission schedule (seconds-of-day)

    public BattleSchedule()
    {
        Times = new BattleTime[(int)BattleType.Count];
        for (int i = 0; i < Times.Length; i++) Times[i] = new BattleTime { Type = (byte)i };
    }

    public BattleTime this[BattleType t] => Times[(int)t];
}
