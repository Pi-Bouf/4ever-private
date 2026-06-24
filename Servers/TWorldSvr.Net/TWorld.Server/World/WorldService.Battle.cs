using Microsoft.Extensions.Logging;
using TWorld.Protocol;

namespace TWorld.Server.World;

/// <summary>
/// Phase 4c — the scheduled battle-field state machine, ported from the <c>OnTimer</c> battle-time block
/// in <c>TWorldSvr.cpp</c> and <c>OnSM_BATTLESTATUS_REQ</c> in <c>SSHandler.cpp</c> (folded inline). Each
/// second it picks the active window (castle-day / sky-garden-window / local, with a mission override),
/// runs the NORMAL→BATTLE→PEACE→NORMAL cycle, and broadcasts the matching enable packet to every map.
/// </summary>
public sealed partial class WorldService
{
    /// <summary>Test/diagnostic seam (the production caller is <see cref="OnTimerAsync"/>). dayOfWeek is the
    /// MFC convention: 1=Sunday .. 7=Saturday, matching the castle <c>m_bDay</c>.</summary>
    public void BattleTick(uint secondsOfDay, byte dayOfWeek, uint recentDay = 0)
    {
        if (_state.Battles is not null) BattleOnTimer(secondsOfDay, dayOfWeek, recentDay);
    }

    private void BattleOnTimer(uint dwCLT, byte dayOfWeek, uint recentDay)
    {
        var sched = _state.Battles!;
        var castle = sched[BattleType.Castle];
        var sky = sched[BattleType.SkyGarden];
        var local = sched[BattleType.Local];
        var mission = sched[BattleType.Mission];

        // Active-window selection (unsigned arithmetic wraps exactly as the C++ DWORD math does).
        BattleTime battle;
        if (castle.Day == dayOfWeek) battle = castle;
        else if (dwCLT >= sky.BattleStart - sky.AlarmStart - 60 &&
                 dwCLT <= sky.BattleStart + sky.BattleDur + sky.PeaceDur + 60) battle = sky;
        else battle = local;

        if (!battle.Run)
        {
            if (mission.Run) battle = mission;
            else if (mission.BattleStart != 0)
            {
                uint realStart = mission.BattleStart - mission.AlarmStart - 60;
                uint realEnd = mission.BattleStart + mission.BattleDur + mission.AlarmEnd + 60;
                if ((dwCLT >= realStart - Proto.DayOne && dwCLT <= realEnd - Proto.DayOne) ||
                    (dwCLT >= realStart && dwCLT <= realEnd))
                    battle = mission;
            }
        }

        if (battle.BattleStart == 0) return;

        var prevStatus = battle.Status;
        bool alarm = false;
        uint leftTime = 0;

        uint startLeft = dwCLT > battle.BattleStart ? Proto.DayOne - dwCLT + battle.BattleStart : battle.BattleStart - dwCLT;
        uint battleEnd = (battle.BattleStart + battle.BattleDur) % Proto.DayOne;
        uint endLeft = dwCLT > battleEnd ? Proto.DayOne - dwCLT + battleEnd : battleEnd - dwCLT;
        battle.LeftTime = endLeft;

        switch (battle.Status)
        {
            case BattleStatus.Normal:
                if (startLeft <= battle.AlarmStart)
                {
                    uint interval = startLeft > 600 ? 600u : (startLeft > 60 ? 120u : 10u);
                    if (startLeft != 0 && startLeft % interval == 0) { leftTime = startLeft; alarm = true; battle.Run = true; }
                }
                else if (endLeft <= battle.BattleDur)
                {
                    battle.Status = BattleStatus.Battle; leftTime = endLeft; battle.Run = true;
                }
                break;
            case BattleStatus.Battle:
                if (endLeft > battle.BattleDur) battle.Status = BattleStatus.Peace;
                else if (endLeft != 0 && endLeft <= battle.AlarmEnd)
                {
                    uint interval = endLeft > 60 ? 60u : 10u;
                    if (endLeft % interval == 0) { leftTime = endLeft; alarm = true; }
                }
                break;
            case BattleStatus.Peace:
            {
                uint peaceEnd = (battle.BattleStart + battle.BattleDur + battle.PeaceDur) % Proto.DayOne;
                uint peaceLeft = dwCLT > peaceEnd ? Proto.DayOne - dwCLT + peaceEnd : peaceEnd - dwCLT;
                if (peaceLeft > battle.PeaceDur)
                {
                    battle.Status = BattleStatus.Normal;
                    battle.Run = false;
                    if (battle.Type == (byte)BattleType.Mission) AdvanceMissionStart(battle);
                }
                break;
            }
        }

        if (alarm || battle.Status != prevStatus)
        {
            if (battle.Status == BattleStatus.Peace) leftTime = battle.PeaceDur;
            BroadcastBattleStatus(battle.Type, battle.Status, battle.BattleStart, leftTime);

            // War end (any non-mission field entering PEACE): recompute guild week records, and for a castle
            // war clear the scoreboard. C++ OnSM_BATTLESTATUS_REQ tail. (PEACE has no alarms, so this fires
            // exactly once, on the BATTLE->PEACE transition.)
            if (battle.Type != (byte)BattleType.Mission && battle.Status == BattleStatus.Peace)
                OnBattlePeace((BattleType)battle.Type, recentDay);
        }
    }

    /// <summary>The war-end side effects: refresh every guild's rolling 7-day record and, for a castle war,
    /// clear the aggregated scoreboard so the next war starts clean. C++ OnSM_BATTLESTATUS_REQ (PEACE branch).</summary>
    private void OnBattlePeace(BattleType type, uint recentDay)
    {
        _state.RecentRecordDate = recentDay;
        foreach (var g in _state.Guilds.Values) g.CalcWeekRecord(recentDay);
        if (type == BattleType.Castle) _state.CastleWarInfo.Clear();
        _log.LogInformation("War end ({Type}): guild week records recomputed{Castle}.", type,
            type == BattleType.Castle ? ", castle scoreboard cleared" : "");
    }

    /// <summary>On a mission PEACE→NORMAL, advance its start to the next custom time (C++ OnTimer tail).</summary>
    private void AdvanceMissionStart(BattleTime mission)
    {
        var ct = _state.Battles!.CustomTimes;
        for (int i = 0; i < ct.Count; i++)
        {
            if (ct[i] != mission.BattleStart && ct[i] != mission.BattleStart - Proto.DayOne) continue;
            i++;
            if (i >= ct.Count) mission.BattleStart = ct[0] + Proto.DayOne;
            else if (ct[i] != mission.BattleStart) mission.BattleStart = ct[i];
            else if (i + 1 < ct.Count) mission.BattleStart = ct[i + 1];
            return;
        }
    }

    /// <summary>OnSM_BATTLESTATUS_REQ, folded inline: fan the active window's status out to every map.</summary>
    private void BroadcastBattleStatus(byte type, BattleStatus status, uint start, uint second)
    {
        foreach (var s in _state.Servers.Values)
        {
            switch ((BattleType)type)
            {
                case BattleType.Local: s.Send(BuildLocalEnable((byte)status, second)); break;
                case BattleType.Castle: s.Send(BuildCastleEnable((byte)status, second)); break;
                case BattleType.Mission: s.Send(BuildMissionEnable((byte)status, start, second)); break;
                // BT_SKYGARDEN enable is compiled out in the shipped build (#ifdef SKYGARDEN) — not sent.
                default: break;
            }
        }
        // Non-mission PEACE in the C++ also recalcs guild week records + clears castle-war info; both are
        // deferred subsystems here (documented), so this is a no-op beyond the broadcast.
        if (type != (byte)BattleType.Mission && status == BattleStatus.Peace)
            _log.LogDebug("Battle {Type} entered PEACE.", (BattleType)type);
    }

    // ===== senders =====

    private static byte[] BuildLocalEnable(byte status, uint second)
    {
        var w = new PacketWriter(Msg.MW_LOCALENABLE_REQ);
        w.WriteByte(status); w.WriteUInt32(second);
        w.WriteUInt32(0); w.WriteByte(0); w.WriteUInt32(0);   // localStart, castleDay, castleStart (C++ sends zeros)
        return w.ToArray();
    }

    private static byte[] BuildCastleEnable(byte status, uint second)
    { var w = new PacketWriter(Msg.MW_CASTLEENABLE_REQ); w.WriteByte(status); w.WriteUInt32(second); return w.ToArray(); }

    private static byte[] BuildMissionEnable(byte status, uint start, uint second)
    { var w = new PacketWriter(Msg.MW_MISSIONENABLE_REQ); w.WriteByte(status); w.WriteUInt32(start); w.WriteUInt32(second); return w.ToArray(); }
}
