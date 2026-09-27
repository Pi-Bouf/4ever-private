using TMap.Data;
using TMap.Protocol;

namespace TMap.Server.Map;

/// <summary>
/// Player against player — C++ <c>CTPlayer::Defend</c> → <c>CTObjBase::Defend</c> with a PC defender, the death it can
/// cause (<c>CTPlayer::OnDie</c>) and the kill's PvP points (<c>CTMapSvrModule::PvPEvent</c>, TMapSvr.cpp:10190).
/// <para>The server does not decide who may be attacked: like the C++, it takes the client's word for the target (the
/// client only lets a player attack an enemy) and checks only what the C++ checks here — a live target on the same
/// map, the attacker is not the target, and a player in a duel is only open to its opponent (MapService.Duel.cs). The C++
/// peace-zone and battle-zone gates concern systems that are not ported (territories, the lounge, tournaments), and always
/// pass.</para>
/// <para>The hit is the one a monster takes (<see cref="CalcDamage"/>): the defender's defence, its shield roll (a
/// successful one reports <c>HT_BLOCK</c>), its buffs on the damage and its immunity statuses. A kill costs the victim
/// total points and pays the killer — or, in a party, pays the killer the same and each partner nearby 12 — see
/// <see cref="PvPKill"/>. Each kill and death is recorded (<see cref="RecordPvP"/>). <b>Not ported:</b> debuffs a hit
/// leaves on a player (<c>MaintainSkill</c> for a PC target), <c>DistributeSkill</c> (a summon sharing its owner's
/// damage) and the local / battle-zone tallies (<c>LocalRecord</c>).</para>
/// </summary>
public sealed partial class MapService
{
    private const byte PvpsNormal = 1;               // PVP_STATUS PVPS_NORMAL (no local battle, no tournament)
    private const byte PvpeKillE = 2;
    private const int PvpLevelGap = 5, PvpPartnerPoint = 12;
    private const long PvpSameVictimWindowMs = 600_000;   // 600 s — three wins on one name within it pay nothing
    private const int PvpRecentCount = 10;           // PVP_RECENTRECORDCOUNT

    /// <summary>One negative hit from <paramref name="ch"/> on another player, <paramref name="target"/>.</summary>
    private void HitPlayer(ClientSession s, Character ch, uint hostId, uint attackId, byte attackType, SkillTemplate? tpl,
        byte level, ushort skillId, byte canSelect, ClientSession ts, Character target, uint actId, uint aniId,
        float atkX, float atkY, float atkZ, float defX, float defY, float defZ)
    {
        if (target.Hp == 0 && tpl?.CanDefendAtDie() != true) return;                     // OS_DEAD && !CanDefendAtDie

        var p = AttackerPower.Of(ch, _templates, tpl) with { Level = ch.Level };
        bool isMagic = p.IsMagic, isLong = p.IsLong;
        ch.EnterBattle(NowMs, RecoverInit);
        target.EnterBattle(NowMs, RecoverInit);                                          // CTPlayer::Defend ChgMode(MT_BATTLE)

        byte hitType = HitTypeVsPlayer(CombatRng, p.Crit, p.AttackLevel);
        uint maxHp = MaxHpFor(target), maxMp = MaxMpFor(target);
        var def = DamageTarget.Of(target, self: false, maxHp, maxMp, CombatRng, _templates);
        var dmg = CalcDamage(p, def, tpl, level, hitType, isMagic, isLong);
        uint hpBefore = target.Hp, mpBefore = target.Mp;
        ApplyPlayerDamage(target, dmg, maxHp, maxMp);

        byte atkHit = hitType == HtMiss ? HtMiss : target.Hp == 0 ? HtLastHit : dmg.Blocked ? HtBlock : hitType;
        if (hitType != HtMiss && tpl is { } lt) SendLifeDrain(lt, level, attackId, attackType, hostId, dmg);

        var ack = BuildCS_DEFEND_ACK(attackId, hostId, target.CharId, OtPc, attackType, actId, aniId,
            p.AttackLevel, p.Level, p.ApMin(isMagic, isLong), p.ApMax(isMagic, isLong), isMagic, p.Crit, canSelect,
            p.Country, p.AidCountry, skillId, level, atkHit, hitType != HtMiss, atkX, atkY, atkZ, defX, defY, defZ, dmg.Map);
        bool hpmp = target.Hp != hpBefore || target.Mp != mpBefore;
        var viewers = _state.InView(ts).ToList();
        foreach (var v in viewers)
        {
            v.Send(ack);
            if (hpmp) SendSelfHpMp(v, target.CharId, maxHp, target.Hp, maxMp, target.Hp != 0 ? target.Mp : 0);
        }

        if (hitType == HtMiss || target.Hp != 0 || hpBefore == 0) return;
        if (target.DuelId != 0 && DuelLose(ts, target, hostId)) return;              // a duel's loser does not die
        PlayerDied(target, viewers);
        PvPKill(ts, target, s, ch);
    }

    /// <summary>C++ <c>CTObjBase::OnDamage</c> for a player: damage floors at 0, a heal is capped at the maximum.</summary>
    private static void ApplyPlayerDamage(Character ch, in DamageResult r, uint maxHp, uint maxMp)
    {
        if (r.DamageHp > 0) ch.Hp = ch.Hp > (uint)r.DamageHp ? ch.Hp - (uint)r.DamageHp : 0;
        else if (r.DamageHp < 0) ch.Hp = (uint)Math.Min((long)maxHp, (long)ch.Hp - r.DamageHp);
        if (r.DamageMp > 0) ch.Mp = ch.Mp > (uint)r.DamageMp ? ch.Mp - (uint)r.DamageMp : 0;
        else if (r.DamageMp < 0) ch.Mp = (uint)Math.Min((long)maxMp, (long)ch.Mp - r.DamageMp);
    }

    /// <summary>C++ <c>PvPEvent(PVPE_KILL_E, victim, killer)</c>. The event is the level gap between the two sides, each
    /// side's level being its highest party member in view: more than 5 above the victim's side is <c>PVPE_KILL_L</c>, more
    /// than 5 below <c>PVPE_KILL_H</c>, else <c>PVPE_KILL_E</c>. The kill chart row for it gives two percentages of the
    /// victim level's <c>wPvPoint</c>: the victim loses the first from its total, the killer gains the second (cut by
    /// half the victim's death-penalty step, in %) in total and useable. A killer in a party instead shares with every
    /// partner in view, who gets 12.</summary>
    private void PvPKill(ClientSession vs, Character victim, ClientSession ks, Character killer)
    {
        var atkParty = killer.GetPartyId() != 0
            ? _state.InView(ks).Where(x => x.Char is { } c && c.GetPartyId() == killer.GetPartyId()).ToList()
            : new List<ClientSession>();
        int atkMax = killer.Level, defMax = victim.Level;
        foreach (var x in atkParty) atkMax = Math.Max(atkMax, x.Char!.Level);
        if (victim.GetPartyId() != 0)
            foreach (var x in _state.InView(vs))
                if (x.Char is { } c && c.GetPartyId() == victim.GetPartyId()) defMax = Math.Max(defMax, c.Level);

        byte evt = atkMax > defMax + PvpLevelGap ? PvpeKillL : atkMax + PvpLevelGap < defMax ? PvpeKillH : PvpeKillE;
        if (!_templates.PvPointKill.TryGetValue((PvpsNormal, evt), out var row)) return;
        if (!_templates.LevelPvPoint.TryGetValue(victim.Level, out var worth)) return;

        uint dec = worth * row.Dec / 100, inc = worth * row.Inc / 100;
        inc = inc * (uint)(100 - victim.Persist.Aftermath / 2) / 100;                     // no local battle

        UsePvPoint(vs, victim, dec, evt, PvpTotal, killer);
        if (atkParty.Count == 0)
        {
            GainPvPoint(ks, killer, inc, evt, PvpTotal | PvpUseable, victim);
            return;
        }
        for (int i = atkParty.Count - 1; i >= 0; i--)                                    // vParty.back() first
        {
            var x = atkParty[i];
            if (x.Char!.CharId == killer.CharId) GainPvPoint(x, x.Char, inc, evt, PvpTotal | PvpUseable, victim);
            else if (x.IsMain) GainPvPoint(x, x.Char, PvpPartnerPoint, evt, PvpTotal | PvpUseable, victim);
        }
    }

    /// <summary>The kill record part of C++ <c>GainPvPoint</c> / <c>UsePvPoint</c> (the <c>pRec</c> branch) and
    /// <c>CTPlayer::RecordPvP</c> (TPlayer.cpp:5385): a win on a name already beaten 3 times in the last 600 s pays nothing;
    /// the result joins the recent list, the class record and the month's and all-time tallies (with their titles), and a
    /// win is logged (<c>TSaveCharKill</c>). Returns the points to apply.</summary>
    private uint RecordPvP(ClientSession s, Character ch, Character other, bool win, uint point)
    {
        long now = NowMs;
        if (win && ch.PvpRecent.Count(r => now - r.TimeMs < PvpSameVictimWindowMs && r.Win && r.Name == other.Name) >= 3)
            point = 0;
        ch.PvpRecent.Add(new PvpRecord(other.Name, win, point, now, other.Class, other.Level, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        if (win && _gameDb is { } db) { uint k = ch.CharId, t = other.CharId; _ = EnqueueDbWrite(() => db.SaveCharKillAsync(k, t)); }
        // Past PVP_RECENTRECORDCOUNT, the records older than PVP_SAVETIME go.
        for (int i = 0; i < ch.PvpRecent.Count && ch.PvpRecent.Count > PvpRecentCount;)
            if (now - ch.PvpRecent[i].TimeMs > PvpSameVictimWindowMs) ch.PvpRecent.RemoveAt(i);
            else i++;

        if (other.Class < 6) ch.PvpRecord[other.Class * 2 + (win ? 1 : 0)]++;
        if (win)
        {
            ch.MonthWin++; ch.TotalWin++;
            GetTitle(s, ch, DefeatsMonthTitle, ch.MonthWin, start: true);
        }
        else
        {
            ch.MonthLose++; ch.TotalLose++;
            GetTitle(s, ch, DeathMonthTitle, ch.MonthLose, start: true);
        }
        GetTitle(s, ch, VictoryMonthTitle, ch.MonthRankPercent, start: true);
        return point;
    }
}
