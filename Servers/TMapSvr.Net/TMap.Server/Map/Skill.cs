using TMap.Data;

namespace TMap.Server.Map;

/// <summary>A learned skill on the character (the CS_CHARINFO_ACK skill sub-loop). Port of a
/// <c>CTSkill</c> entry. Phase 14 adds the CS_SKILLUSE caster spine: the linked chart template
/// (<see cref="Template"/>), the MP/HP cost (<c>GetRequiredMP</c>/<c>GetRequiredHP</c>) and the
/// reuse-cooldown state + math (<c>Use</c>/<c>CanUse</c>/<c>GetReuseRemainTick</c>/<c>GetReuseDelay</c>).
/// The charge/cure/maintain math is still deferred (PORT_STATUS.md).</summary>
public sealed class Skill
{
    public ushort SkillId { get; set; }
    public byte Level { get; set; }
    public uint ReuseRemainTick { get; set; } // DWORD reuse-remaining tick sent to the client (loaded from DB)

    /// <summary>The linked chart template (C++ <c>CTSkill::m_pTSKILL</c>), or null when the skill chart
    /// isn't loaded (DB-free) — cost then computes 0 and there is no cooldown.</summary>
    public SkillTemplate? Template { get; set; }

    // ---- runtime cooldown state (C++ CTSkill::m_dwUseTick / m_dwDelayTick) ----
    private uint _useTick;   // tick at which the skill was last cast (0 == never used)
    private uint _delayTick; // the cooldown length currently in force

    // ---- SDELAY_TYPE (TMapType.h:200-205) ----
    private const byte SdelaySkill = 0, SdelayLoop = 1, SdelayKind = 2;

    /// <summary>C++ <c>CTSkill::GetRequiredMP</c> (TSkill.cpp:202-217): type 1 = flat <c>m_dwUseMP</c> scaled by
    /// the per-level rate, type 2 = a percentage of <paramref name="pureMaxMp"/>, else 0. Null template ⇒ 0.</summary>
    public uint GetRequiredMp(uint pureMaxMp)
    {
        if (Template is not { } t) return 0;
        return t.UseMpType switch
        {
            1 => (uint)((float)t.UseMp * CostRate(t)),
            2 => unchecked(pureMaxMp * t.UseMp / 100u),
            _ => 0,
        };
    }

    /// <summary>C++ <c>CTSkill::GetRequiredHP</c> (TSkill.cpp:185-200) — as <see cref="GetRequiredMp"/>.</summary>
    public uint GetRequiredHp(uint pureMaxHp)
    {
        if (Template is not { } t) return 0;
        return t.UseHpType switch
        {
            1 => (uint)((float)t.UseHp * CostRate(t)),
            2 => unchecked(pureMaxHp * t.UseHp / 100u),
            _ => 0,
        };
    }

    /// <summary>The per-level cost scale <c>pow(m_f1stRateX, exp)/100</c> where
    /// <c>exp = level==0 ? 0 : m_bStartLevel + (level-1)·m_bNextLevel</c> (TSkill.cpp:188/205). Computed in
    /// double then narrowed to float once, exactly as the C++ (FLOAT fRate = pow(...)/100).</summary>
    private float CostRate(SkillTemplate t)
    {
        int exp = Level == 0 ? 0 : t.StartLevel + (Level - 1) * t.NextLevel;
        return (float)(Math.Pow(t.Rate1stX, exp) / 100.0);
    }

    /// <summary>C++ <c>CTSkill::GetReuseRemainTick</c> (TSkill.cpp:108-114) — remaining cooldown at
    /// <paramref name="now"/>, with DWORD wraparound. 0 when never used or elapsed.</summary>
    public uint GetReuseRemainTick(uint now)
    {
        if (_useTick == 0 || unchecked(now - _useTick) >= _delayTick) return 0;
        return unchecked(_delayTick - (now - _useTick));
    }

    /// <summary>C++ <c>CTSkill::CanUse</c> (TSkill.cpp:224-227) — <c>!GetReuseRemainTick</c>.</summary>
    public bool CanUse(uint now) => GetReuseRemainTick(now) == 0;

    /// <summary>C++ <c>CTSkill::GetReuseDelay</c> (TSkill.cpp:80-98): SDELAY_SKILL base =
    /// <c>m_dwReuseDelay + (level-1)·m_nReuseDelayInc</c>, SDELAY_LOOP = <c>m_dwLoopDelay</c>, SDELAY_KIND
    /// returns <paramref name="atkSpeed"/> directly; SKILL/LOOP return <c>(base + atkSpeed)·rate/100</c>.</summary>
    private uint GetReuseDelay(byte type, uint atkSpeed, uint rate)
    {
        if (Template is not { } t) return 0;
        uint baseDelay;
        switch (type)
        {
            case SdelaySkill: baseDelay = unchecked(t.ReuseDelay + (uint)((Level - 1) * t.ReuseDelayInc)); break;
            case SdelayLoop: baseDelay = t.LoopDelay; break;
            case SdelayKind: return atkSpeed;
            default: baseDelay = 0; break;
        }
        return unchecked((baseDelay + atkSpeed) * rate / 100);
    }

    /// <summary>C++ <c>CTSkill::Use</c> (TSkill.cpp:70-78) — arm the cooldown at <paramref name="now"/> only if
    /// the newly-computed delay exceeds what's already remaining.</summary>
    public void Use(byte type, uint now, uint atkSpeed, uint rate)
    {
        uint delay = GetReuseDelay(type, atkSpeed, rate);
        if (GetReuseRemainTick(now) < delay) { _useTick = now; _delayTick = delay; }
    }

    /// <summary>Arm the standard skill cooldown (C++ <c>CTObjBase::SkillUse</c> calls
    /// <c>Use(SDELAY_SKILL, tick, GetAtkSpeed, GetAtkSpeedRate)</c>).</summary>
    public void UseSkill(uint now, uint atkSpeed, uint rate) => Use(SdelaySkill, now, atkSpeed, rate);

    /// <summary>Arm the shared same-kind group cooldown (C++ <c>CTObjBase::SkillUse</c> KindDelay loop):
    /// <c>Use(SDELAY_KIND, tick, m_dwKindDelay, rate)</c> — SDELAY_KIND uses the kind-delay as the delay
    /// directly (rate-independent).</summary>
    public void UseKind(uint now, uint kindDelay, uint rate) => Use(SdelayKind, now, kindDelay, rate);
}

/// <summary>
/// A maintained (buff / debuff) skill currently active on a character or monster — one C++ <c>CTSkill</c>
/// held in <c>m_vMaintainSkill</c>. The 19 fields below are the CS_CHARINFO_ACK / CS_ENTER_ACK / CS_ADDMON_ACK
/// maintain sub-loop block (serialized by <c>MapService.WriteMaintainSkill</c>); the timing state
/// (<see cref="StartTick"/>/<see cref="MaintainTick"/>/<see cref="ChargeTick"/>) + the linked
/// <see cref="Template"/> drive the Phase-31 buff engine (stat modification via
/// <see cref="StatEngine.CalcAbilityValue"/>, expiry, and removal).
/// </summary>
public sealed class MaintainSkill
{
    public ushort SkillId { get; set; }
    public byte Level { get; set; } = 1;              // C++ CTSkill ctor default m_bLevel = 1
    public uint RemainTick { get; set; }
    public uint AttackId { get; set; }
    public byte AttackType { get; set; }              // C++ ctor default OT_PC (0 here; set by SetMaintain)
    public uint HostId { get; set; }
    public byte HostType { get; set; }
    public byte Hit { get; set; }
    public ushort AttackLevel { get; set; }
    public byte AttackerLevel { get; set; }
    public uint PysMinPower { get; set; }
    public uint PysMaxPower { get; set; }
    public uint MgMinPower { get; set; }
    public uint MgMaxPower { get; set; }
    public byte CanSelect { get; set; } = 1;          // C++ ctor default m_bCanSelect = TRUE
    public byte AttackCountry { get; set; }
    public float PosX { get; set; }
    public float PosY { get; set; }
    public float PosZ { get; set; }

    /// <summary>The linked skill-chart template (C++ <c>CTSkill::m_pTSKILL</c>) — supplies the buff's ability
    /// rows, duration, priority and static flag. Null ⇒ the chart isn't loaded (DB-free); such a buff neither
    /// modifies stats nor expires.</summary>
    public SkillTemplate? Template { get; set; }

    // ---- timing (C++ CTSkill m_dwStartTick / m_dwMaintainTick / m_dwChargeTick), DWORD tick-ms ----
    /// <summary>C++ <c>m_dwStartTick</c> — the map-clock tick the buff started at; 0 ⇒ permanent (never expires).</summary>
    public uint StartTick { get; set; }
    /// <summary>C++ <c>m_dwMaintainTick</c> — the buff's total duration in ms.</summary>
    public uint MaintainTick { get; set; }
    /// <summary>C++ <c>m_dwChargeTick</c> — the tick the buff was (re)applied at.</summary>
    public uint ChargeTick { get; set; }

    /// <summary>C++ <c>CTSkill::IsEnd</c> (TSkill.cpp:233): expired iff started and elapsed ≥ duration
    /// (wrap-tolerant unsigned subtraction). A permanent buff (<see cref="StartTick"/> 0) never ends.</summary>
    public bool IsEnd(uint tick) => StartTick != 0 && unchecked(tick - StartTick) >= MaintainTick;

    /// <summary>C++ <c>CTSkill::GetRemainTick</c> (TSkill.cpp:100): remaining ms, 0 when permanent or elapsed.</summary>
    public uint GetRemainTick(uint tick)
        => (StartTick == 0 || unchecked(tick - StartTick) >= MaintainTick) ? 0u : unchecked(MaintainTick - (tick - StartTick));

    /// <summary>C++ <c>CTSkill::SetEndTick</c> (TSkill.cpp:133): duration from the template; a 0-duration buff
    /// is permanent (<see cref="StartTick"/> stays 0).</summary>
    public void SetEndTick(uint tick)
    {
        uint dur = Template?.GetMaintainTick(Level) ?? 0;
        StartTick = dur != 0 ? Math.Max(tick, 1u) : 0u;
        MaintainTick = dur;
        ChargeTick = tick;
    }

    /// <summary>C++ <c>CTSkill::SetLoopEndTick</c> (TSkill.cpp:140): an explicit remaining duration (loop /
    /// reload from DB); always time-limited.</summary>
    public void SetLoopEndTick(uint tick, uint remain)
    {
        StartTick = Math.Max(tick, 1u);
        MaintainTick = remain;
        ChargeTick = tick;
    }

    /// <summary>C++ <c>m_pTSKILL->m_bStatic</c> — a permanent buff not dropped on death/logout.</summary>
    public bool IsStatic => Template?.IsStatic ?? false;
    /// <summary>C++ <c>IsNegative</c> — an even <c>m_bPositive</c> (a debuff). Unknown template ⇒ treated positive.</summary>
    public bool IsNegative => Template?.IsNegative ?? false;
    /// <summary>C++ <c>IsPositive</c> — <c>m_bPositive == SPT_POSITIVE</c> (a buff).</summary>
    public bool IsPositive => Template?.IsPositive ?? false;
}
