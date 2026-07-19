using TMap.Protocol;

namespace TMap.Server.Map;

/// <summary>
/// Monster AI — the idle-roam slice (C++ <c>CTAICmdRoam::ExecAI</c>, TAICmdRoam.cpp:30). On its per-monster
/// roam timer, an idle (<c>MT_NORMAL</c>, alive) monster that at least one player can see picks a wander
/// destination within its spawn radius and broadcasts <c>CS_MONACTION_ACK</c> (the destination + action
/// verb) to the players in view — exactly the packet the C++ AI emits. A monster no one can see stays
/// dormant (C++ roam requires a host, else fires <c>AT_LEAVE</c>).
///
/// <para><b>Faithful-to-wire, with documented approximations (PORT_STATUS.md):</b> this ports the roam
/// <b>broadcast</b> 1:1; the roam parameters that live in charts / the AI-command table I haven't loaded
/// (<c>m_bArea</c>, <c>m_bRoamType</c>, <c>m_bRoamProb</c>, the per-command roam delay) are approximated by
/// the <b>spawn Range</b> (radius), a fixed <b>WALK</b> action, and a constant roam interval
/// (<c>base + rand()%(4·base)</c>, matching the C++ jitter shape). The server keeps the monster's
/// authoritative position at the anchor: the <b>client-authoritative</b> position echo (C++
/// <c>CS_MONMOVE_REQ</c> → <c>CS_MONMOVE_ACK</c>) that actually advances <c>m_fPos</c> is deferred, so the
/// client animates the wander while the server grid cell (visibility) is computed from the anchor.</para>
///
/// <para><b>Deferred (slice 2 — documented):</b> the whole BATTLE branch — aggro accrual (<c>SetAggro</c>/
/// <c>m_mapAggro</c>), host lifecycle (<c>SetHost</c>/<c>ChkHost</c>/<c>ChgHost</c>/<c>m_dwHostKEY</c>),
/// <c>ChgMode</c> state machine, chase (<c>CTAICmdFollow</c>) + <c>m_wChaseRange</c> leash, the monster
/// attack (<c>CTAICmdBeginAtk</c>/<c>CTAICmdAttack</c> → <c>CS_MONATTACK_ACK</c> + monster→player
/// <c>CS_DEFEND</c>), <c>MT_GOHOME</c> return, pack/leader/patrol movement, and call-for-help / flee.</para>
/// </summary>
public sealed partial class MapService
{
    /// <summary>Base roam-step interval (ms). Approximates the AI-command-table delay; the actual wait is
    /// <c>RoamDelayMs + rand()%(4·RoamDelayMs)</c> (C++ <c>m_dwDelay + rand()%(ROAM_DELAY_BOUND·m_dwDelay)</c>,
    /// ROAM_DELAY_BOUND = 4).</summary>
    private const int RoamDelayMs = 5000;
    private const int ChaseIntervalMs = 1000;  // how often a chasing monster re-broadcasts its follow target
    private const float ChaseRange = 800f;     // leash: max distance of the target from the spawn anchor (C++ m_wChaseRange, not loaded)
    private const float AttackRange = 50f;     // melee reach: within this of the anchor the monster attacks instead of chasing
    private const int DefaultAtkSpeedMs = 2000; // attack cadence when the attr has no m_dwAtkSpeed
    private const byte TaWalk = 3, TaFollow = 9; // TACTION_TYPE TA_WALK / TA_FOLLOW

    /// <summary>The per-tick monster AI sweep: a monster in <c>MT_BATTLE</c> chases its aggro target
    /// (<c>CTAICmdFollow</c>); an idle <c>MT_NORMAL</c> monster roams (<c>CTAICmdRoam</c>). Public so tests
    /// can drive it at a chosen <paramref name="nowMs"/>.</summary>
    public void RunMonsterAI(long nowMs)
    {
        foreach (var mon in _state.AllMonsters())
        {
            if (mon.Hp == 0) continue;                          // dead corpse — no AI
            if (mon.Mode == MtBattle) { ChaseTarget(mon, nowMs); continue; }
            if (mon.Mode != MtNormal) continue;
            // An aggressive monster looks for a host on sight (C++ AT_ENTER → CTAICmdSetHost); on a pull it enters
            // battle here and chases/attacks from the next tick. A passive monster (the default) just roams.
            if (mon.Aggressive && TryAcquireHost(mon, (uint)nowMs)) continue;
            if (mon.Area > 0) Roam(mon, nowMs);
        }
    }

    /// <summary>C++ <c>CTAICmdRoam::ExecAI</c> — an idle monster a player can see wanders within its radius.</summary>
    private void Roam(Monster mon, long nowMs)
    {
        if (nowMs < mon.RoamNextMs) return;

        var viewers = _state.PlayersAround(mon).ToList();
        if (viewers.Count == 0)                       // no host/observer ⇒ dormant (C++ AT_LEAVE)
        {
            mon.RoamNextMs = nowMs + RoamDelayMs;     // re-check later
            return;
        }

        var (nx, nz) = RoamDestination(mon);
        mon.NextX = nx;
        mon.NextZ = nz;
        foreach (var p in viewers) SendCS_MONACTION_ACK(p, mon.Id, TaWalk, nx, mon.StartY, nz);

        mon.RoamNextMs = nowMs + RoamDelayMs + SpawnRng.Next(4 * RoamDelayMs); // C++ delay + rand()%(4·delay)
    }

    /// <summary>C++ <c>CTAICmdChgMode</c>/<c>CTAICmdFollow</c> — a battle monster chases its aggro target,
    /// re-broadcasting <c>CS_MONACTION_ACK</c> (<c>TA_FOLLOW</c>) toward the target; it drops aggro (→ NORMAL,
    /// re-enabling HP regen) when the target is gone or has fled past the leash (distance from the anchor).
    /// The monster's authoritative position stays at the anchor (client-authoritative move echo deferred), so
    /// the leash naturally tightens as the target flees.</summary>
    private void ChaseTarget(Monster mon, long nowMs)
    {
        if (mon.TargetId == 0
            || _state.FindByChar(mon.TargetId) is not { State: EnterState.InGame, Char: { Hp: > 0 } target })
        {
            Disengage(mon, mon.TargetId, mon.TargetType != 0 ? mon.TargetType : OtPc, (uint)nowMs); // target gone / dead → re-pick or go home
            return;
        }

        float dx = target.PosX - mon.StartX, dz = target.PosZ - mon.StartZ;
        float dist2 = dx * dx + dz * dz;
        // fled past the leash (C++ m_wChaseRange < GetDistance(anchor,pos); the port measures the target's distance
        // from the anchor since the monster is pinned there): drop this target, re-pick the next in-view attacker.
        if (dist2 > ChaseRange * ChaseRange) { Disengage(mon, mon.TargetId, mon.TargetType != 0 ? mon.TargetType : OtPc, (uint)nowMs); return; }

        if (dist2 <= AttackRange * AttackRange) // in melee range ⇒ attack (Phase 20)
        {
            if (nowMs >= mon.AtkNextMs)
            {
                AttackPlayer(mon, target, nowMs);
                mon.AtkNextMs = nowMs + (mon.AtkSpeed != 0 ? mon.AtkSpeed : DefaultAtkSpeedMs);
            }
            return;
        }

        if (nowMs < mon.RoamNextMs) return; // chase cadence
        foreach (var p in _state.PlayersAround(mon))
            SendCS_MONACTION_ACK(p, mon.Id, TaFollow, target.PosX, target.PosY, target.PosZ, mon.TargetId, OtPc);
        mon.RoamNextMs = nowMs + ChaseIntervalMs;
    }

    /// <summary>The monster's melee hit on its target player (C++ <c>CTAICmdAttack</c> announce +
    /// the follow-up damage, which the C++ routes through the host client's <c>CS_DEFEND_REQ</c>; here it is
    /// applied server-side). The AP−DP roll uses the monster's <c>GetMinAP</c>/<c>GetMaxAP</c> band vs the
    /// player's <c>GetDefendPower</c>; the player enters battle (suppressing HP regen) and, at 0 HP, dies.</summary>
    private void AttackPlayer(Monster mon, Character target, long nowMs)
    {
        // C++ order: GetAtkHitType (attacker side) decides miss/normal/crit FIRST; then Defend→CalcDamage runs
        // the DEFENDER's shield-block roll (GetShieldDP), and only on a landed hit (a miss short-circuits before
        // CalcDamage, so it can never become a block). A successful roll returns the shield's defence power,
        // which is ADDED to normal defence (additive reduction — the 5/7 floor still holds) and flags HT_BLOCK.
        byte hitType = HitTypeVsPlayer(CombatRng, mon.CritProb, mon.AttackLevel);
        uint shieldDp = hitType == HtMiss ? 0u : StatEngine.ShieldBlockDp(target, CombatRng, _templates); // physical melee
        uint dp = StatEngine.DefendPower(target, _templates) + shieldDp;
        int a = Math.Max((int)(mon.AtkMin - dp), 5);          // C++ CalcDamage floors (min 5 / max 7)
        int b = Math.Max((int)(mon.AtkMax - dp), 7);

        uint baseDmg = hitType switch
        {
            HtMiss => 0u,
            HtCritical => CritDamage(CombatRng, _templates.Formula(FtypePcd), (uint)b), // physical crit off the max band
            _ => (uint)(a + CombatRng.Next(Math.Max(b - a, 1))),
        };
        uint dmg = (uint)Math.Min((int)baseDmg, (int)target.Hp); // clamp to remaining HP
        if (hitType != HtMiss) target.Hp -= dmg;
        target.EnterBattle((uint)nowMs, RecoverInit);         // player enters battle ⇒ HP regen suppressed (Phase 16)

        // The reported hit result: a kill downgrades to HT_LASTHIT (C++ Defend `m_dwHP ? bAtkHit : HT_LASTHIT`);
        // otherwise a successful shield roll flags HT_BLOCK (set last in CalcDamage, so it wins over a crit report).
        byte atkHit = hitType == HtMiss ? HtMiss
            : target.Hp == 0 ? HtLastHit
            : shieldDp != 0 ? HtBlock
            : hitType;
        var attackAck = BuildMonsterAttackAck(mon, target.CharId);
        var hitAck = BuildMonsterHitAck(mon, target, dmg, atkHit, landed: hitType != HtMiss);
        foreach (var p in _state.PlayersAround(mon))
        {
            p.Send(attackAck);
            p.Send(hitAck);
            SendSelfHpMp(p, target.CharId, MaxHpFor(target), target.Hp, MaxMpFor(target), target.Mp);
        }

        if (hitType != HtMiss && target.Hp == 0) // player death — CS_DIE_ACK; revival (CS_REVIVAL) is deferred
        {
            foreach (var p in _state.PlayersAround(mon)) SendCS_DIE_ACK(p, target.CharId, OtPc);
            // C++ OnDie → ReleaseMaintain(FALSE): silently drop all non-static buffs (Phase 31).
            if (_state.FindByChar(target.CharId) is { } ts) ReleaseMaintainPlayer(ts, target, notify: false);
            DropAggro(mon, nowMs); // the corpse isn't a target — leave battle + clear the hate table
        }
    }

    /// <summary>C++ <c>SendCS_MONATTACK_ACK</c> (CSSender.cpp:1208) — the monster's swing announce.</summary>
    private static byte[] BuildMonsterAttackAck(Monster mon, uint targetId)
    {
        var w = new PacketWriter(Msg.CS_MONATTACK_ACK, capacity: 16);
        w.WriteUInt32(mon.Id);       // dwAttackID
        w.WriteUInt32(targetId);     // dwTargetID
        w.WriteByte(Monster.OtMon);  // bAttackType
        w.WriteByte(OtPc);           // bTargetType
        w.WriteUInt16(0);            // wSkillID (basic attack)
        return w.ToArray();
    }

    /// <summary>The hit result for a monster→player attack — the same <c>CS_DEFEND_ACK</c> layout as the
    /// player→monster path (Phase 13), with the monster as attacker and the player as target.</summary>
    private static byte[] BuildMonsterHitAck(Monster mon, Character target, uint dmg, byte atkHit, bool landed)
    {
        var w = new PacketWriter(Msg.CS_DEFEND_ACK, capacity: 96);
        w.WriteUInt32(mon.Id);        // dwAttackID
        w.WriteUInt32(target.CharId); // dwTargetID
        w.WriteByte(Monster.OtMon);   // bAttackType
        w.WriteByte(OtPc);            // bTargetType
        w.WriteUInt32(mon.Id);        // dwHostID
        w.WriteByte(Monster.OtMon);   // bHostType
        w.WriteUInt32(0);             // dwActID
        w.WriteUInt32(0);             // dwAniID
        w.WriteByte(0);               // bIsMaintain
        w.WriteUInt32(0);             // dwMaintainTick
        w.WriteByte(mon.CritProb);    // bHit == bCP (the monster's crit prob)
        w.WriteByte(atkHit);          // bAtkHit (HT_MISS/NORMAL/CRITICAL, HT_LASTHIT on the killing blow)
        w.WriteUInt16(mon.AttackLevel); // wAttackLevel (m_wAL)
        w.WriteByte(mon.Level);       // bAttackerLevel
        w.WriteUInt32(mon.AtkMin);    // dwPysMinPower
        w.WriteUInt32(mon.AtkMax);    // dwPysMaxPower
        w.WriteUInt32(0);             // dwMgMinPower
        w.WriteUInt32(0);             // dwMgMaxPower
        w.WriteByte(1);               // bCanSelect
        w.WriteByte(0);               // bCancelCharge
        w.WriteByte(mon.Country);     // bAttackCountry
        w.WriteByte(0);               // bAttackAidCountry
        w.WriteUInt16(0);             // wSkillID
        w.WriteByte(0);               // bSkillLevel
        w.WriteUInt16(0);             // wBackSkillID
        w.WriteByte((byte)(landed ? 1 : 0)); // bPerform — 0 on a miss (PERFORM_MISS)
        w.WriteFloat(mon.PosX); w.WriteFloat(mon.PosY); w.WriteFloat(mon.PosZ);
        w.WriteFloat(target.PosX); w.WriteFloat(target.PosY); w.WriteFloat(target.PosZ);
        if (dmg > 0)
        {
            w.WriteByte(1);               // damage-map count
            w.WriteByte(MtypeDamage);     // key
            w.WriteUInt32((ushort)dmg);   // damage (WORD-truncated, matching the C++)
        }
        else
        {
            w.WriteByte(0);               // empty damage map on a miss
        }
        return w.ToArray();
    }

    /// <summary>Leave battle for good (C++ <c>ResetHost</c>-lite, TMonster.cpp:2392): clear the target/host and
    /// the whole hate table, drop to <c>MT_NORMAL</c> (HP regen resumes — Phase 16), and return to roaming. Used
    /// when no hostile attacker remains in view (the C++ <c>AT_LEAVELB</c> outcome, the port collapsing the
    /// <c>MT_GOHOME</c> walk-back since the monster is pinned at its anchor).</summary>
    private static void DropAggro(Monster mon, long nowMs)
    {
        mon.TargetId = 0;
        mon.TargetType = 0;
        mon.HostId = 0;
        mon.ClearAggro();
        mon.Mode = MtNormal;
        mon.RoamNextMs = nowMs + RoamDelayMs;
    }

    /// <summary>A random point on the circle of radius <see cref="Monster.Area"/> around the anchor (C++
    /// <c>MoveNext</c> in-bounds branch, TMonster.cpp:1897): <c>rad = (rand()%360)·π/180</c>.</summary>
    private (float x, float z) RoamDestination(Monster mon)
    {
        float rad = SpawnRng.Next(360) * (float)Math.PI / 180f;
        return (mon.StartX + mon.Area * MathF.Cos(rad), mon.StartZ + mon.Area * MathF.Sin(rad));
    }

    /// <summary>C++ <c>SendCS_MONACTION_ACK</c> (CSSender.cpp:1096) — the AI move/action command: monster id,
    /// action verb, ground destination, and an optional target (0 for a plain roam step).</summary>
    private static void SendCS_MONACTION_ACK(ClientSession p, uint monId, byte action, float x, float y, float z,
        uint targetId = 0, byte targetType = 0)
    {
        var w = new PacketWriter(Msg.CS_MONACTION_ACK, capacity: 24);
        w.WriteUInt32(monId);
        w.WriteByte(action);
        w.WriteFloat(x); w.WriteFloat(y); w.WriteFloat(z);
        w.WriteUInt32(targetId);
        w.WriteByte(targetType);
        p.Send(w);
    }
}
