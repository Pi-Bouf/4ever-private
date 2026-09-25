namespace TMap.Data;

/// <summary>
/// One <c>TSKILLDATA</c> row (C++ <c>tagTSKILLDATA</c>, TMapType.h:1969) — a skill's effect descriptor.
/// <see cref="Attr"/> classifies the attack (SATT_PHYSIC/LONG/MAGIC*), <see cref="Type"/> the effect kind
/// (SDT_ABILITY/CURE/STATUS…), <see cref="Exec"/> the sub-selector (MTYPE_DAMAGE…), and
/// <see cref="Inc"/>/<see cref="Value"/>/<see cref="ValueInc"/>/<see cref="Calc"/> the value formula.
/// </summary>
public sealed record SkillDataRow(byte Action, byte Type, byte Attr, byte Exec, byte Inc,
    ushort Value, ushort ValueInc, byte Calc);

/// <summary>
/// A skill-chart template row from <c>TSKILLCHART</c> (C++ <c>CTBLSkillChart</c> → <c>CTSkillTemp</c>,
/// keyed by <c>m_wID</c>). This is the subset the <c>CS_SKILLUSE</c> caster-side spine needs: the MP/HP
/// cost fields (<c>m_dwUseMP</c>/<c>m_bUseMPType</c>/<c>m_dwUseHP</c>/<c>m_bUseHPType</c>), the per-level
/// cost scale (<c>m_bStartLevel</c>/<c>m_bNextLevel</c> + the config-injected <c>m_f1stRateX</c>), the
/// reuse-cooldown fields (<c>m_dwReuseDelay</c>/<c>m_nReuseDelayInc</c>/<c>m_dwLoopDelay</c>/
/// <c>m_dwKindDelay</c>/<c>m_bKind</c>), the attack-speed selector (<c>m_bSpeedApply</c>, a TATTACK_DELAY),
/// the offensive flag (<c>m_bPositive</c>), and the bound map (<c>m_wMapID</c>). The rest of the 56-column
/// chart + the skill-data rows (<c>m_vData</c>, which drive damage scaling / cure / attack-type
/// classification) are deferred — see PORT_STATUS.md.
/// </summary>
/// <remarks><b>Value-exact note:</b> the DB column <c>bLevel</c> maps to <see cref="StartLevel"/> (not a
/// "level" field), and <see cref="Rate1stX"/> is <b>not</b> a chart column — the C++ stamps every template
/// with the global <c>f1stRateX = TFORMULACHART[FTYPE_1ST].fRateX</c> at load (TMapSvr.cpp:2596-2655),
/// which is exactly the value the stat engine caches as <c>TemplateStore.Rate1st</c>.</remarks>
public sealed record SkillTemplate(
    ushort Id, byte Kind,
    uint UseMp, byte UseMpType, uint UseHp, byte UseHpType,
    byte StartLevel, byte MaxLevel, byte NextLevel,
    uint ReuseDelay, int ReuseDelayInc, uint LoopDelay, uint KindDelay,
    byte SpeedApply, byte Positive, ushort MapId,
    uint Duration = 0, uint DurationInc = 0, byte MaintainKind = 0, byte Priority = 0, byte StaticFlag = 0,
    uint ClassId = 0, float Rate1stX = 1f, uint Aggro = 0,
    // C++ m_bGlobal: a skill every character may use without learning it. Placing it on the hotkey bar grants it
    // (CTPlayer::AddHotKey, TPlayer.cpp:860) and clearing that slot takes it away again. 5 of 765 live skills.
    bool Global = false,
    // Riding (C++ m_bIsRide / m_bIsHideSkill / m_bIsDismount / m_bEraseAct): a buff that blocks calling or mounting a
    // pet (unless it is a hide skill), a hit that throws the target off its mount, and the actions that end a buff
    // (BUFFERASEACTION_TYPE — EraseBuffByRide tests m_bEraseAct & BEA_RIDE, BEA_RIDE = 3).
    bool IsRide = false, bool IsHideSkill = false, bool IsDismount = false, byte EraseAct = 0)
{
    /// <summary>C++ <c>CTSkillTemp::GetAggro</c> (TSkillTemp.cpp:448) — the hate a hostile cast adds to a
    /// monster at <paramref name="level"/>: <c>m_dwAggro·pow(m_f1stRateX, exp)/100</c> (<c>exp = 0</c> at
    /// level 0, else <c>m_bStartLevel + (level-1)·m_bNextLevel</c>), truncated to DWORD <b>before</b> the
    /// integer /100. Zero when <see cref="Aggro"/> is 0. The caller floors it at 1 (<c>max(1, GetAggro())</c>,
    /// TMonster.cpp:944), so any hostile hit adds ≥1 aggro.
    /// <para><b>Deferred:</b> the <c>dwAggro</c> chart column isn't loaded (see PORT_STATUS.md), so live
    /// magnitude collapses to <c>max(1,0)=1</c> per hit until it is — the retarget <i>mechanics</i> are exact;
    /// tests set <see cref="Aggro"/> directly to exercise the magnitude formula.</para></summary>
    public uint GetAggro(byte level)
    {
        if (Aggro == 0) return 0;
        double exp = level == 0 ? 0 : StartLevel + (level - 1) * NextLevel;
        return (uint)(Aggro * System.Math.Pow(Rate1stX, exp)) / 100u;
    }

    /// <summary>C++ class gate <c>m_dwClassID &amp; BITSHIFTID(m_bClass)</c> (QuestGiveSkill.cpp:43,
    /// OnCS_SKILLBUY_REQ CSHandler.cpp:2359) — the skill's allowed-class bitmask (<c>m_dwClassID</c>) has the
    /// bit for class index <paramref name="class"/> set (<c>BITSHIFTID(a) = 1u &lt;&lt; a</c>, TMapType.h:33).</summary>
    public bool IsClassMatch(byte @class) => (ClassId & (1u << @class)) != 0;

    /// <summary>C++ <c>CTSkillTemp::IsNegative</c> (TSkillTemp.cpp:31) — <c>!(m_bPositive % 2)</c>: an even
    /// <c>m_bPositive</c> is an offensive skill (the peace-zone / mode / CheckAttack gate).</summary>
    public bool IsNegative => (Positive % 2) == 0;

    /// <summary>C++ <c>CTSkillTemp::IsPositive</c> (TSkillTemp.cpp:26) — <c>m_bPositive == SPT_POSITIVE</c> (1).</summary>
    public bool IsPositive => Positive == SptPositive;

    /// <summary>C++ <c>m_bStatic</c> — a permanent buff that survives death/logout (never auto-released).</summary>
    public bool IsStatic => StaticFlag != 0;

    private const byte SptPositive = 1;              // SPT_* (NetCode.h:2263)
    public const byte SaBuff = 3;                    // SKILL_ACTION SA_BUFF (NetCode.h:1533)
    /// <summary>SKILL_DATA_TYPE SDT_CURE (NetCode.h:1540) — a cure/dispel data row.</summary>
    public const byte SdtCure = 5;

    /// <summary>The skill's effect rows (C++ <c>m_vData</c>), loaded from <c>TSKILLDATA</c> — drives the
    /// damage-scaling engine, attack-type classification, and (deferred) cure/status effects.</summary>
    public List<SkillDataRow> Data { get; } = new();

    // ---- SKILL_DATA_TYPE (bType), SKILL_DATA_INC (bInc), SKILL_DATA_ATTR (bAttr), SKILL_ATTACK_TYPE,
    // and the MTYPE_* / MAGIC_TYPE selectors this engine uses (NetCode.h / TMapType.h). ----
    public const byte SdtAbility = 1;                                        // SDT_ABILITY
    private const byte SviIncrease = 1, SviDecrease = 2, SviMultiply = 3, SviDivide = 4, SviPercent = 5;
    private const byte SattPhysic = 1, SattLong = 2, SattMagicNo = 3;        // SATT_* (magic attrs are 3..9)
    /// <summary>SKILL_ATTACK_TYPE (TMapType.h:356).</summary>
    public const byte SatNone = 0, SatPhysic = 1, SatLong = 2, SatMagic = 3;
    public const byte MtypeLap = 9, MtypeDamage = 30;                        // MAGIC_TYPE selectors

    /// <summary>C++ <c>CTSkillTemp::GetValue</c> (TSkillTemp.cpp:62) — the per-row value at
    /// <paramref name="level"/>, by <c>m_bCalc</c>: 0 flat, 1 level-linear, 2 <c>pow</c>-scaled (double
    /// division by 100), 3 level-linear-decrease (pure int).</summary>
    public int DataValue(SkillDataRow d, byte level) => d.Calc switch
    {
        0 => d.Value,
        1 => d.Value + (level - 1) * d.ValueInc,
        2 => (int)((double)d.Value * System.Math.Pow(Rate1stX, level == 0 ? 0 : StartLevel + (level - 1) * NextLevel) / 100.0),
        3 => (int)d.Value - ((int)level - 1) * (int)d.ValueInc,
        _ => 0,
    };

    /// <summary>C++ <c>CTSkillTemp::Calculate</c> (TSkillTemp.cpp:76) — the <c>m_bInc</c> operator applied to
    /// <paramref name="value"/>, returning the <b>delta</b> the caller adds (MULTIPLY/DIVIDE/PERCENT return
    /// <c>op(value) − value</c>). Unsigned DWORD arithmetic reproduced (wrap on a negative per-row value).</summary>
    public int Calculate(byte level, int index, uint value)
    {
        int calc = DataValue(Data[index], level);
        return Data[index].Inc switch
        {
            SviIncrease => calc,
            SviDecrease => -calc,
            SviMultiply => unchecked((int)(value * (uint)calc - value)),
            SviDivide => unchecked((int)(value / (uint)(calc <= 0 ? 1 : calc) - value)),
            SviPercent => unchecked((int)((int)((double)value * calc / 100.0) - value)),
            _ => 0,
        };
    }

    /// <summary>C++ <c>CTSkillTemp::CalcValue</c> (TSkillTemp.cpp:49) — Σ <see cref="Calculate"/> over every
    /// row matching (<paramref name="type"/>, <paramref name="exec"/>). This is the skill's own scaling of a
    /// base value (e.g. the AP−DP damage roll).</summary>
    public int CalcValue(byte level, byte type, byte exec, uint value)
    {
        int inc = 0;
        for (int i = 0; i < Data.Count; i++)
            if (Data[i].Type == type && Data[i].Exec == exec) inc += Calculate(level, i, value);
        return inc;
    }

    /// <summary>C++ <c>CTSkillTemp::GetAttackType</c> (TSkillTemp.cpp:172) — physical/long attr ⇒ SAT_PHYSIC
    /// (early), MAGICNO ⇒ SAT_MAGIC (early), other magic attrs set SAT_MAGIC but keep scanning.</summary>
    public byte GetAttackType()
    {
        byte type = SatNone;
        foreach (var d in Data)
        {
            if (d.Attr is SattPhysic or SattLong) return SatPhysic;
            if (d.Attr == SattMagicNo) return SatMagic;
            if (d.Attr > SattMagicNo && d.Attr <= 9) type = SatMagic; // SATT_MAGICSR..SATT_MAGICIR
        }
        return type;
    }

    /// <summary>C++ <c>CTSkillTemp::IsLongAttack</c> (TSkillTemp.cpp:196) — any ability row whose exec is
    /// <c>MTYPE_LAP</c> or whose attr is <c>SATT_LONG</c>.</summary>
    public bool IsLongAttack()
    {
        foreach (var d in Data)
            if ((d.Type == SdtAbility && d.Exec == MtypeLap) || d.Attr == SattLong) return true;
        return false;
    }

    /// <summary>The instance-skill damage scaling applied to a base roll (the only live term of C++
    /// <c>CalcAbilityValue(dwValue, MTYPE_DAMAGE)</c> — maintain/remain buffs + cure + effection are
    /// deferred): <c>max(0, roll + CalcValue(level, SDT_ABILITY, MTYPE_DAMAGE, roll))</c>.</summary>
    public uint ScaleDamage(byte level, uint roll)
        => (uint)System.Math.Max(0, (int)roll + CalcValue(level, SdtAbility, MtypeDamage, roll));

    /// <summary>C++ <c>CTSkillTemp::IsMaintainType</c> (TSkillTemp.cpp:226) — the skill has at least one
    /// <c>SA_BUFF</c> data row, i.e. casting it applies an active maintained buff/debuff.</summary>
    public bool IsMaintainType()
    {
        foreach (var d in Data)
            if (d.Action == SaBuff) return true;
        return false;
    }

    /// <summary>The skill carries at least one <c>SDT_CURE</c> data row (a cure/dispel skill — C++
    /// <c>PerformSkill</c> runs a cure effect per such row).</summary>
    public bool HasCure()
    {
        foreach (var d in Data)
            if (d.Type == SdtCure) return true;
        return false;
    }

    /// <summary>SKILL_DATA_TYPE SDT_STATUS (NetCode.h:1552) — a status-effect data row.</summary>
    public const byte SdtStatus = 6;
    /// <summary>SDT_STATUS_TYPE HP↔MP execs (NetCode.h:1737) — swap HP↔MP, and sacrifice half HP into MP; the
    /// only vitals-mutating status effects the map server applies inline (the rest are movement / flags).</summary>
    public const byte SdtStatusHpMpChange = 50, SdtStatusHpToMp = 51;

    /// <summary>The skill carries at least one HP↔MP status row (<c>SDT_STATUS</c> +
    /// <c>SDT_STATUS_HPMPCHANGE</c>/<c>HPTOMP</c>) — the vitals-mutating status effects handled on the DEFEND path.</summary>
    public bool HasVitalsStatus()
    {
        foreach (var d in Data)
            if (d.Type == SdtStatus && (d.Exec == SdtStatusHpMpChange || d.Exec == SdtStatusHpToMp)) return true;
        return false;
    }

    /// <summary>C++ <c>CTSkill::GetMaintainTick</c> (TSkill.cpp:147) — the buff's total duration in ms at
    /// <paramref name="level"/>: <c>m_dwDuration + m_dwDurationInc·(level-1)</c>.</summary>
    public uint GetMaintainTick(byte level)
        => Duration + DurationInc * (uint)(level < 1 ? 0 : level - 1);

    /// <summary>C++ <c>CTSkill::CalcAbilityValue</c> (TSkill.cpp:151) — the per-skill buff delta for a target
    /// ability: Σ <see cref="Calculate"/> over the rows matching (<paramref name="action"/>, <c>SDT_ABILITY</c>,
    /// <c>m_bExec == exec</c>). <paramref name="action"/> is a <c>SA_*</c> (the maintain sum uses <c>SA_BUFF</c>).</summary>
    public int CalcAbilityValue(byte level, byte action, byte exec, uint value)
    {
        int inc = 0;
        for (int i = 0; i < Data.Count; i++)
            if (Data[i].Action == action && Data[i].Type == SdtAbility && Data[i].Exec == exec)
                inc += Calculate(level, i, value);
        return inc;
    }

    /// <summary>C++ <c>CTSkillTemp::HaveSkillData</c> (TSkillTemp.cpp:36) — this template has a data row matching
    /// another's (<paramref name="type"/>, <paramref name="attr"/>, non-zero <paramref name="exec"/>). Used by
    /// the buff-stack resolver to detect two buffs contending over the same effect.</summary>
    public bool HaveSkillData(byte type, byte attr, byte exec)
    {
        foreach (var d in Data)
            if (d.Type == type && d.Attr == attr && d.Exec != 0 && d.Exec == exec) return true;
        return false;
    }

    /// <summary>The first <c>SA_BUFF</c> <c>SDT_ABILITY</c> row this and <paramref name="other"/> both carry
    /// (the exec the stack resolver compares on), or <c>false</c> if none is shared.</summary>
    public bool SharedBuffAbility(SkillTemplate other, out byte exec)
    {
        foreach (var d in Data)
            if (d.Action == SaBuff && d.Type == SdtAbility && other.HaveSkillData(d.Type, d.Attr, d.Exec))
            {
                exec = d.Exec;
                return true;
            }
        exec = 0;
        return false;
    }
}
