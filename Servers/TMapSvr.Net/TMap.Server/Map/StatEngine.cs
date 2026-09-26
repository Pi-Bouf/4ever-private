using TMap.Data;

namespace TMap.Server.Map;

/// <summary>
/// The C# port of the <c>CTObjBase</c> stat / combat-attribute derivation (Phases 5–6). Ported value-exact
/// from <c>TObjBase.cpp</c>: the six primary stats (<c>GetSTR…</c>), the 2nd-ability engine
/// (<c>Calc2ndAbility</c> + <c>CalcItemAbility</c>), the vitals (<c>GetMaxHP</c>/<c>GetMaxMP</c>) and the
/// attack/defence sheet (<c>GetMaxAP</c>/<c>GetDefendPower</c>/attack-speed/crit/charge) that fills
/// <c>CS_CHARSTATINFO_ACK</c>.
///
/// <para>Stats are fully derived (race + class seed × per-level growth) + equipped-gear enchants + the
/// maintained-buff delta (<c>CalcAbilityValue</c>, Phase 31 — the third layer of every getter); there is no
/// per-character stat storage. Deferred (stubbed to 0, their subsystems aren't ported): pet bonuses
/// (<c>CalcPetATTR</c>/<c>CalcPetBonus</c>), the death-penalty stat reduction (<c>CalcAfterMath</c>), the guild
/// <c>StatLevel</c> level bonus, and — within the buff layer — the cure/effection/remain-skill terms.</para>
///
/// <para><b>Value-exact notes:</b> everything accumulates in <c>float</c>; each <c>DWORD(...)</c> cast is a
/// toward-zero truncation at the same point the C++ truncates. Min-AP is clamped to max after both are
/// computed.</para>
/// </summary>
public static class StatEngine
{
    // ---- FORMULA_TYPE (NetCode.h) ----
    private const byte FtypePap = 1, FtypeLap = 3, FtypeNas = 4, FtypeAl = 5, FtypeDl = 6, FtypePcr = 7,
        FtypeHp = 8, FtypeHpr = 9, FtypePdp = 12, FtypeMap = 13, FtypeMnas = 16, FtypeMsp = 17, FtypeMcr = 18,
        FtypeMp = 19, FtypeMpr = 20, FtypeMdp = 23, FtypeMcs = 24, FtypeMal = 28, FtypeMdl = 29;

    // ---- MAGIC_TYPE (NetCode.h) — enchant ids summed from equipped gear + the buff-layer target ids ----
    public const byte MtypeStr = 1, MtypeDex = 2, MtypeCon = 3, MtypeInt = 4, MtypeWis = 5, MtypeMen = 6;
    private const byte MtypePap = 7, MtypePdp = 8, MtypeLap = 9, MtypeMdp = 16, MtypeMap = 17,
        MtypeAl = 11, MtypeDl = 12, MtypeCr = 13, MtypeMcs = 19, MtypeCmp = 20, MtypeMcr = 21,
        MtypeHpr = 32, MtypeMpr = 33,
        MtypeMhp = 50, MtypeMmp = 51, MtypePas = 54, MtypeLas = 55, MtypeMpas = 56, MtypeMal = 86, MtypeMdl = 87;
    // Shield-block enchant ids (C++ GetShieldDP/GetShieldMDP): SDR/SMDR = block RATE, SPDPOW/SMDPOW = block POWER.
    private const byte MtypeSdr = 34, MtypeSpdpow = 58, MtypeSmdpow = 59, MtypeSmdr = 60;

    // ---- TITEM_TYPE / TITEM_KIND (NetCode.h) — the equipped item that carries the base block prob / DP ----
    private const byte ItShield = 6, IkMultiVajra = 11, IkShield = 12;

    // ---- SKILL_ACTION (NetCode.h:1533) — the maintained-buff data-row action the stat layer sums ----
    private const byte SaBuff = 3;

    /// <summary>C++ <c>CTObjBase::CalcAbilityValue</c> (TObjBase.cpp:1409) — the buff/debuff delta for a target
    /// ability <paramref name="mtype"/> (an <c>MTYPE_*</c>): Σ each active maintained skill's <c>SA_BUFF</c>
    /// <c>SDT_ABILITY</c> rows for that ability, scaled off <paramref name="value"/> (needed by the
    /// MULTIPLY/DIVIDE/PERCENT operators). Returns the signed delta the caller adds; the final value is 0-clamped
    /// by <see cref="Buffed"/>. Buffs whose chart template isn't linked (DB-free) contribute nothing.
    ///
    /// <para><b>Deferred (documented — PORT_STATUS.md):</b> the sign-guarded <b>stat-layer</b> cure delta
    /// (<c>CalcCure</c> — it needs the mid-cast <c>m_pInstanceSkill</c> threading the port doesn't model; the
    /// <i>instant</i> cure/dispel is done in Phase 35), the <c>ApplyEffectionBuff</c> percent amplifier
    /// (<c>SDT_STATUS_MAGIC</c>), and the remain/passive layer (<c>m_vRemainSkill</c>,
    /// <c>SA_CONTINUE</c>/<c>SA_PASSIVE</c>) — this ports the maintained <c>SA_BUFF</c> term only.</para></summary>
    public static int CalcAbilityValue(Character ch, uint value, byte mtype)
    {
        int inc = 0;
        foreach (var m in ch.MaintainSkills)
            if (m.Template is { } tpl) inc += tpl.CalcAbilityValue(m.Level, SaBuff, mtype, value);
        return inc;
    }

    /// <summary>Adds the maintained-buff delta for <paramref name="mtype"/> onto a base+item subtotal and clamps
    /// to ≥ 0 (C++ folds this as the third layer of every stat getter: base → items → buffs).</summary>
    private static uint Buffed(Character ch, uint sub, byte mtype)
        => (uint)Math.Max(0, (int)sub + CalcAbilityValue(ch, sub, mtype));

    // ---- TATTACK_DELAY (NetCode.h) ----
    public const byte TadPhysical = 1, TadLong = 2, TadMagic = 3;

    // ---- EQUIP_SLOT (NetCode.h) — weapon slots for the atk-speed SpeedInc term ----
    private const byte EsPrmWeapon = 0, EsSndWeapon = 1, EsLongWeapon = 2;

    private const int BaseSeed = 1;     // C++ #define BASE_STAT (1)
    private const uint SpeedRate = 100; // C++ #define TMAGIC_OPT_SPEED_RATE 100

    // ==================== primary stats ====================

    /// <summary>The pure base stat: <c>max(baseMin, BASE_STAT + race + class) · pow(rate1st, level-1)</c>
    /// (C++ <c>CTObjBase::GetBaseStatValue</c>). Returns 0 if the class/race seed is missing.</summary>
    public static float BaseStat(Character ch, byte mtype, TemplateStore t, ushort baseMin = 0)
    {
        if (!t.Classes.TryGetValue(ch.Class, out var cls) || !t.Races.TryGetValue(ch.Race, out var race))
            return 0f;
        int seed = Math.Max((int)baseMin, BaseSeed + StatOf(race, mtype) + StatOf(cls, mtype));
        // C++ `pow()` is double: the FLOAT rate + int exponent promote to double and the whole product
        // narrows to FLOAT exactly ONCE. Using MathF.Pow + a float multiply would round twice and, after
        // the downstream toward-zero truncation, drift by an off-by-one for a non-unit growth rate.
        return (float)(seed * Math.Pow(t.Rate1st, Level(ch) - 1));
    }

    /// <summary>Effective primary stat = base less the death penalty, + equipped-gear enchants + the
    /// maintained-buff delta (C++ <c>GetSTR…</c>: <c>CalcAfterMath(fSTR)</c> on the base, then
    /// <c>fSTR += CalcItemAbility</c>, then <c>fSTR += CalcAbilityValue((DWORD)fSTR, MTYPE_STR)</c> — the int delta
    /// added to the FLOAT, off the truncated value). The pet layer stays stubbed. FLOAT.</summary>
    public static float Stat(Character ch, byte mtype, TemplateStore t, ushort baseMin = 0)
    {
        float b = BaseStat(ch, mtype, t, baseMin);
        b -= b * ch.AftermathStatDec / 100f;                  // CTObjBase::CalcAfterMath (TObjBase.cpp:4609)
        float v = b + SumMagic(ch, mtype, t);
        v += CalcAbilityValue(ch, (uint)v, mtype);         // C++ passes dwSTR = (DWORD)fSTR by value
        return v + MapService.CompanionStat(ch, mtype);    // CTPlayer::GetSTR… + CalcPetATTR
    }

    /// <summary>Effective stat truncated toward zero (C++ <c>GetAbility</c> → <c>DWORD(GetSTR())</c>).</summary>
    public static ushort Ability(Character ch, byte mtype, TemplateStore t) => (ushort)Stat(ch, mtype, t);

    // ==================== vitals (Phase 5) ====================

    public static uint MaxHp(Character ch, TemplateStore t)
    {
        if (!t.HasStats || t.Formula(FtypeHp) is not { } f) return ch.MaxHp;
        return Buffed(ch, f.Init + (uint)(Stat(ch, MtypeCon, t) * f.RateX) + (uint)SumMagic(ch, MtypeMhp, t), MtypeMhp);
    }

    public static uint MaxMp(Character ch, TemplateStore t)
    {
        if (!t.HasStats || t.Formula(FtypeMp) is not { } f) return ch.MaxMp;
        return Buffed(ch, f.Init + (uint)(Stat(ch, MtypeMen, t) * f.RateX) + (uint)SumMagic(ch, MtypeMmp, t), MtypeMmp)
               + Pet(ch, MtypeMmp, t);
    }

    /// <summary>C++ <c>(DWORD)CalcPetBonus(id)</c>, added after the buff layer in GetMaxMP / the level and crit getters.</summary>
    private static uint Pet(Character ch, byte bonusId, TemplateStore t) => (uint)MapService.CompanionBonusValue(ch, bonusId, t);

    /// <summary>C++ <c>GetPureMaxHP</c> (TObjBase.cpp:1991) — the base formula only: <c>init + BASE-CON·rateX</c>,
    /// with no item ability, buff or pet bonus. Used for the skill HP-cost percentage (<c>GetRequiredHP</c>).</summary>
    public static uint PureMaxHp(Character ch, TemplateStore t)
    {
        if (!t.HasStats || t.Formula(FtypeHp) is not { } f) return ch.MaxHp;
        return f.Init + (uint)(BaseStat(ch, MtypeCon, t) * f.RateX);
    }

    /// <summary>C++ <c>GetPureMaxMP</c> (TObjBase.cpp:2001) — base formula only (<c>init + BASE-MEN·rateX</c>).</summary>
    public static uint PureMaxMp(Character ch, TemplateStore t)
    {
        if (!t.HasStats || t.Formula(FtypeMp) is not { } f) return ch.MaxMp;
        return f.Init + (uint)(BaseStat(ch, MtypeMen, t) * f.RateX);
    }

    /// <summary>C++ <c>GetHPR</c> (TObjBase.cpp:2226) — the per-tick HP recovery amount: the CON-driven
    /// <c>FTYPE_HPR</c> formula + equipped <c>MTYPE_HPR</c> enchants + the maintained-buff <c>MTYPE_HPR</c>
    /// delta (Phase 31).</summary>
    public static uint HpRecover(Character ch, TemplateStore t)
        => Buffed(ch, Calc2ndAbility(ch, FtypeHpr, t) + (uint)SumMagic(ch, MtypeHpr, t), MtypeHpr);

    /// <summary>C++ <c>GetMPR</c> (TObjBase.cpp:2233) — the MEN-driven <c>FTYPE_MPR</c> formula + equipped
    /// <c>MTYPE_MPR</c> enchants + the maintained-buff <c>MTYPE_MPR</c> delta.</summary>
    public static uint MpRecover(Character ch, TemplateStore t)
        => Buffed(ch, Calc2ndAbility(ch, FtypeMpr, t) + (uint)SumMagic(ch, MtypeMpr, t), MtypeMpr);

    // ==================== attack / defence sheet (Phase 6) ====================

    public static uint MaxAp(Character ch, bool arrow, TemplateStore t) => arrow
        ? Buffed(ch, Calc2ndAbility(ch, FtypeLap, t) + (uint)SumGetter(ch, Ab.MaxLap, t), MtypeLap)
        : Buffed(ch, Calc2ndAbility(ch, FtypePap, t) + (uint)SumGetter(ch, Ab.MaxAp, t), MtypePap);

    public static uint MinAp(Character ch, bool arrow, TemplateStore t)
    {
        uint min = arrow
            ? Buffed(ch, Calc2ndAbility(ch, FtypeLap, t) + (uint)SumGetter(ch, Ab.MinLap, t), MtypeLap)
            : Buffed(ch, Calc2ndAbility(ch, FtypePap, t) + (uint)SumGetter(ch, Ab.MinAp, t), MtypePap);
        uint max = MaxAp(ch, arrow, t);
        return min > max ? max : min;
    }

    public static uint MaxMagicAp(Character ch, TemplateStore t)
        => Buffed(ch, Calc2ndAbility(ch, FtypeMap, t) + (uint)SumGetter(ch, Ab.MaxMap, t), MtypeMap);

    public static uint MinMagicAp(Character ch, TemplateStore t)
    {
        uint min = Buffed(ch, Calc2ndAbility(ch, FtypeMap, t) + (uint)SumGetter(ch, Ab.MinMap, t), MtypeMap);
        uint max = MaxMagicAp(ch, t);
        return min > max ? max : min;
    }

    public static uint DefendPower(Character ch, TemplateStore t)
        => Buffed(ch, Calc2ndAbility(ch, FtypePdp, t) + (uint)SumGetter(ch, Ab.Pdp, t), MtypePdp);

    public static uint MagicDefPower(Character ch, TemplateStore t)
        => Buffed(ch, Calc2ndAbility(ch, FtypeMdp, t) + (uint)SumGetter(ch, Ab.Mdp, t), MtypeMdp);

    // ==================== shield block (Phase 41) ====================

    /// <summary>C++ <c>CTObjBase::GetShieldDP</c> (TObjBase.cpp:2171) — the PHYSICAL shield-block roll on a
    /// defender, run per damage component inside <c>CalcDamage</c>. The block rate <c>dwR = ABILITY_SDR</c>
    /// (Σ equipped <c>MTYPE_SDR</c> enchants + an equipped physical shield's base <c>bBlockProb</c>) folded
    /// through the <c>MTYPE_SDR</c> buff layer; on a hit (<c>dwR &gt; rand()%100</c> — <b>strictly</b> greater,
    /// so a rate of 0 never blocks and 100 always does) it returns <c>ABILITY_SDP</c> (Σ <c>MTYPE_SPDPOW</c>
    /// enchants + the shield's base <c>DP</c>) folded through the <c>MTYPE_SPDPOW</c> buff layer, else 0. The
    /// returned power is <b>added to the defender's normal defence</b> (additive reduction — never a fixed
    /// %/full negation; the 5/7 min-damage floor still holds), and a nonzero return flags the reported hit
    /// <c>HT_BLOCK</c>. RNG is .NET <see cref="System.Random"/> — the roll LOGIC is exact, the value is not.
    ///
    /// <para><b>Deferred (documented — PORT_STATUS.md):</b> the disguise gates of <c>CalcItemAbility</c>
    /// (<c>HaveDisguiseBuff</c>/<c>HaveDisWeapon</c>/<c>HaveDisDefend</c> — the transform/disguise buffs aren't
    /// modelled; with no disguise buff those gates are no-ops, so the item sum is exact).</para></summary>
    public static uint ShieldBlockDp(Character defender, Random rng, TemplateStore t)
        => ShieldBlock(defender, rng, t, MtypeSdr, MtypeSpdpow, IkShield, magicPower: false);

    /// <summary>C++ <c>CTObjBase::GetShieldMDP</c> (TObjBase.cpp:2184) — the MAGIC shield-block roll: the
    /// <c>MTYPE_SMDR</c> rate / <c>MTYPE_SMDPOW</c> power enchant ids and an equipped <c>IK_MULTIVAJRA</c>
    /// off-hand's base block prob + magic DP (<c>m_wMDP</c>). Structure identical to <see cref="ShieldBlockDp"/>.</summary>
    public static uint ShieldBlockMdp(Character defender, Random rng, TemplateStore t)
        => ShieldBlock(defender, rng, t, MtypeSmdr, MtypeSmdpow, IkMultiVajra, magicPower: true);

    private static uint ShieldBlock(Character ch, Random rng, TemplateStore t, byte rateMtype, byte powMtype,
        byte shieldKind, bool magicPower)
    {
        uint rate = Buffed(ch, (uint)Math.Max(0, SumMagic(ch, rateMtype, t) + ShieldBase(ch, shieldKind, rate: true, magicPower)), rateMtype);
        if (rate <= (uint)rng.Next(100)) return 0;   // C++ if(dwR > rand()%100)
        return Buffed(ch, (uint)Math.Max(0, SumMagic(ch, powMtype, t) + ShieldBase(ch, shieldKind, rate: false, magicPower)), powMtype);
    }

    /// <summary>The base-shield contribution to <c>ABILITY_SDR</c>/<c>ABILITY_SDP</c> (the shield cases of
    /// C++ <c>CalcItemAbility</c>): an equipped, unbroken <c>IT_SHIELD</c> of <paramref name="shieldKind"/>
    /// adds its attr <c>bBlockProb</c> (the rate) or its <c>DP</c>/<c>MDP</c> (the power). Broken items are
    /// skipped, matching the C++ <c>HavePower</c> gate at the head of the <c>CalcItemAbility</c> loop.</summary>
    private static int ShieldBase(Character ch, byte shieldKind, bool rate, bool magicPower)
    {
        var equip = ch.Equipped;
        if (equip is null) return 0;
        int sum = 0;
        foreach (var it in equip.Items)
        {
            if (!it.HasPower() || it.Template is not { Type: ItShield } tpl || tpl.Kind != shieldKind || it.Attr is not { } attr)
                continue;
            sum += rate ? attr.BlockProb : (magicPower ? attr.MagicDp : attr.Dp);
        }
        return sum;
    }

    // Level accessors — WORD. Base + item enchant + maintained-buff delta; guild StatLevel + pet bonuses stubbed.
    public static ushort AttackLevel(Character ch, TemplateStore t)
        => (ushort)(Buffed(ch, Calc2ndAbility(ch, FtypeAl, t) + (uint)SumMagic(ch, MtypeAl, t), MtypeAl) + Pet(ch, MtypeAl, t));
    public static ushort DefendLevel(Character ch, TemplateStore t)
        => (ushort)(Buffed(ch, Calc2ndAbility(ch, FtypeDl, t) + (uint)SumMagic(ch, MtypeDl, t), MtypeDl) + Pet(ch, MtypeDl, t));
    public static ushort MagicAtkLevel(Character ch, TemplateStore t)
        => (ushort)(Buffed(ch, Calc2ndAbility(ch, FtypeMal, t) + (uint)SumMagic(ch, MtypeMal, t), MtypeMal) + Pet(ch, MtypeMal, t));
    public static ushort MagicDefLevel(Character ch, TemplateStore t)
        => (ushort)(Buffed(ch, Calc2ndAbility(ch, FtypeMdl, t) + (uint)SumMagic(ch, MtypeMdl, t), MtypeMdl) + Pet(ch, MtypeMdl, t));

    // Crit / charge — BYTE. Base + item enchant + maintained-buff delta; pet bonuses stubbed.
    public static byte CriticalPysProb(Character ch, TemplateStore t)
        => (byte)(Buffed(ch, Calc2ndAbility(ch, FtypePcr, t) + (uint)SumMagic(ch, MtypeCr, t), MtypeCr) + Pet(ch, MtypeCr, t));
    public static byte CriticalMagicProb(Character ch, TemplateStore t)
        => (byte)(Buffed(ch, Calc2ndAbility(ch, FtypeMcr, t) + (uint)SumMagic(ch, MtypeMcr, t), MtypeMcr) + (byte)Pet(ch, MtypeMcr, t));
    public static byte ChargeProb(Character ch, TemplateStore t)   // field 28 (cast-maintenance prob)
        => (byte)(Buffed(ch, Calc2ndAbility(ch, FtypeMsp, t) + (uint)SumMagic(ch, MtypeCmp, t), MtypeCmp) + (byte)Pet(ch, MtypeCmp, t));
    public static byte ChargeSpeed(Character ch, TemplateStore t)  // field 27 (magic cast speed)
        => (byte)Buffed(ch, Calc2ndAbility(ch, FtypeMcs, t) + (uint)SumMagic(ch, MtypeMcs, t), MtypeMcs);

    /// <summary>Attack delay (C++ <c>GetAtkSpeed</c>): base <c>FTYPE_NAS</c>/<c>FTYPE_MNAS</c> + the equipped
    /// weapon's <c>SpeedInc</c> (magic uses the melee weapon's, matching the C++ quirk).</summary>
    public static uint AtkSpeed(Character ch, byte tad, TemplateStore t)
    {
        int nas = tad switch
        {
            TadPhysical => (int)Calc2ndAbility(ch, FtypeNas, t) + SumGetter(ch, Ab.AtkSpeedS, t),
            TadLong => (int)Calc2ndAbility(ch, FtypeNas, t) + SumGetter(ch, Ab.AtkSpeedL, t),
            TadMagic => (int)Calc2ndAbility(ch, FtypeMnas, t) + SumGetter(ch, Ab.AtkSpeedS, t),
            _ => 0,
        };
        return (uint)Math.Max(nas, 0);
    }

    /// <summary>Attack-speed rate % (C++ <c>GetAtkSpeedRate</c>): 100, reduced by item speed-rate enchants
    /// as <c>rate·(100-ias)/100</c>, <c>ias = min(Σ enchant, 100)</c>. (CalcAbilityValue buff scale stubbed.)</summary>
    public static uint AtkSpeedRate(Character ch, byte tad, TemplateStore t)
    {
        byte mt = tad switch { TadPhysical => MtypePas, TadLong => MtypeLas, TadMagic => MtypeMpas, _ => (byte)0 };
        uint rate = SpeedRate;
        if (mt == 0) return rate;
        uint ias = (uint)Math.Min(SumMagic(ch, mt, t), 100);
        if (ias != 0) rate = rate * (100 - ias) / 100;
        return rate;
    }

    // ==================== the 2nd-ability engine ====================

    /// <summary>C++ <c>CTObjBase::Calc2ndAbility</c> — the per-<c>FTYPE</c> switch driving every derived
    /// ability off the primary stats and the formula chart. Returns 0 if the class/race chart is missing.</summary>
    public static uint Calc2ndAbility(Character ch, byte ftype, TemplateStore t)
    {
        if (!t.Classes.TryGetValue(ch.Class, out var cls) || !t.Races.TryGetValue(ch.Race, out var race))
            return 0;
        var f = t.Formula(ftype);
        uint init = f?.Init ?? 0;
        float rateX = f?.RateX ?? 0f;
        float rateY = f?.RateY ?? 0f;
        int level = Level(ch);
        switch (ftype)
        {
            case FtypePap: return init + (uint)(Stat(ch, MtypeStr, t) * rateX);
            case FtypeLap:
            case FtypeAl: return init + (uint)(Stat(ch, MtypeDex, t) * rateX);       // FTYPE_AL shares FTYPE_LAP
            case FtypeMap: return init + (uint)(Stat(ch, MtypeInt, t) * rateX);
            case FtypeMal: return init + (uint)(Stat(ch, MtypeWis, t) * rateX);
            case FtypeDl: return (uint)(Stat(ch, MtypeDex, t, (ushort)rateY) * rateX);  // no init; rateY = DEX floor
            case FtypeMdl: return (uint)(Stat(ch, MtypeWis, t, (ushort)rateY) * rateX);
            case FtypePdp:
            // C++ DWORD(pow(fRateY,m_bLevel)*fRateX): pow + product computed in DOUBLE, narrowed once by the
            // (uint) cast — as in BaseStat. MathF.Pow (single) would round twice and off-by-one after truncation.
            case FtypeMdp: return init + (uint)(Math.Pow(rateY, level) * rateX);         // level-scaled, no stat
            case FtypeHp:
            case FtypeHpr: return init + (uint)(Stat(ch, MtypeCon, t) * rateX);   // FTYPE_HPR shares the CON formula
            case FtypeMp:
            case FtypeMpr: return init + (uint)(Stat(ch, MtypeMen, t) * rateX);   // FTYPE_MPR shares the MEN formula
            case FtypeNas:  // min(init, max(rateY - DEXseed·rateX, 0)) — all in float, then truncate
                return (uint)MathF.Min(init, MathF.Max(rateY - SeedRaw(MtypeDex, race, cls) * rateX, 0f));
            case FtypeMnas:
                return (uint)MathF.Min(init, MathF.Max(rateY - SeedRaw(MtypeWis, race, cls) * rateX, 0f));
            case FtypePcr: return init + (uint)(SeedRaw(MtypeDex, race, cls) * rateX);  // raw seed, not leveled
            case FtypeMcr: return init + (uint)(SeedRaw(MtypeWis, race, cls) * rateX);
            case FtypeMsp: return unchecked((uint)(int)(SeedRaw(MtypeMen, race, cls) * rateX - rateY)); // no init/clamp
            case FtypeMcs: return init;
            default: return 0;
        }
    }

    // ==================== equipped-gear ability sums (CalcItemAbility) ====================

    public enum Ab { MaxAp, MinAp, MaxLap, MinLap, MaxMap, MinMap, Pdp, Mdp, AtkSpeedS, AtkSpeedL }

    /// <summary>Σ over the equipped (0xFE) container of each item's enchant magic of the given type
    /// (the <c>GetMagicValue(MTYPE_*)</c> branch of C++ <c>CalcItemAbility</c>); broken items skipped.</summary>
    public static int SumMagic(Character ch, byte mtype, TemplateStore t)
    {
        var equip = ch.Equipped;
        if (equip is null) return 0;
        int sum = 0;
        foreach (var it in equip.Items)
            if (it.HasPower()) sum += it.GetMagicValue(mtype);
        return sum;
    }

    /// <summary>Σ over the equipped container of each item's AP/DP getter (or weapon <c>SpeedInc</c>) — the
    /// item-getter branch of C++ <c>CalcItemAbility</c>; broken items skipped.</summary>
    public static int SumGetter(Character ch, Ab ab, TemplateStore t)
    {
        var equip = ch.Equipped;
        if (equip is null) return 0;
        int sum = 0;
        foreach (var it in equip.Items)
        {
            if (!it.HasPower()) continue;
            switch (ab)
            {
                case Ab.MaxAp: sum += (int)it.GetMaxAP(); break;
                case Ab.MinAp: sum += (int)it.GetMinAP(); break;
                case Ab.MaxLap: sum += (int)it.GetMaxLAP(); break;
                case Ab.MinLap: sum += (int)it.GetMinLAP(); break;
                case Ab.MaxMap: sum += (int)it.GetMaxMagicAP(); break;
                case Ab.MinMap: sum += (int)it.GetMinMagicAP(); break;
                case Ab.Pdp: sum += (int)it.GetDefendPower(); break;
                case Ab.Mdp: sum += (int)it.GetMagicDefPower(); break;
                case Ab.AtkSpeedS:
                    if (it.ItemSlot is EsPrmWeapon or EsSndWeapon) sum += (int)(it.Template?.SpeedInc ?? 0);
                    break;
                case Ab.AtkSpeedL:
                    if (it.ItemSlot == EsLongWeapon) sum += (int)(it.Template?.SpeedInc ?? 0);
                    break;
            }
        }
        return sum;
    }

    private static int SeedRaw(byte mtype, StatSeed race, StatSeed cls)
        => BaseSeed + StatOf(race, mtype) + StatOf(cls, mtype);

    private static int Level(Character ch) => ch.Level < 1 ? 1 : ch.Level;

    private static int StatOf(StatSeed s, byte mtype) => mtype switch
    {
        MtypeStr => s.Str, MtypeDex => s.Dex, MtypeCon => s.Con,
        MtypeInt => s.Int, MtypeWis => s.Wis, MtypeMen => s.Men, _ => 0,
    };
}
