using TMap.Protocol;

namespace TMap.Server.Map;

/// <summary>
/// PvP points — C++ <c>CTPlayer::GainPvPoint</c> / <c>UsePvPoint</c> (TPlayer.cpp:5267/5357). A character has a total
/// (rank, honour) and a useable balance (the PvP shop's currency), loaded from and saved to <c>TPVPOINTTABLE</c> with the
/// character. Every change is announced to the owner with <c>CS_PVPPOINT_ACK</c>.
/// <para>A gain with a companion out is raised by its PvP bonus (id 88) and feeds the companion that much exp, unless it
/// comes from buying or the guild. The total also counts in this month's points, is checked for honour titles, and a
/// change is reported to the ladder (<see cref="CheckMonthRank"/>). Kills: MapService.PvP.cs. <b>Not ported:</b> the UDP
/// log.</para>
/// </summary>
public sealed partial class MapService
{
    // PVP_EVENT (NetCode.h:2371) and the PvP-point kinds.
    public const byte PvpeGuild = 0, PvpeKillH = 1, PvpeKillL = 3, PvpeBuyItem = 9;
    public const byte PvpTotal = 1, PvpUseable = 2;
    private const byte CompanionPvpBonus = 88;

    /// <summary>C++ <c>CTPlayer::GainPvPoint</c>. A kill names its victim (<paramref name="victim"/>, the C++ <c>pRec</c>):
    /// it is recorded, and pays nothing on a name beaten too often (<see cref="RecordPvP"/>).</summary>
    private void GainPvPoint(ClientSession s, Character ch, uint point, byte evt, byte type, Character? victim = null)
    {
        if (victim is not null) point = RecordPvP(s, ch, victim, win: true, point);
        bool fromPlay = evt != PvpeBuyItem && evt != PvpeGuild;
        float bonus = CompanionBonusValue(ch, CompanionPvpBonus, _templates);
        if (bonus != 0 && fromPlay) point = (uint)(point * (1 + bonus / 100));   // DWORD *= FLOAT
        if (point == 0) return;
        if (evt is >= PvpeKillH and <= PvpeKillL && point > 100) return;       // a kill is never worth more than 100

        if ((type & PvpTotal) != 0)
        {
            ch.PvpTotalPoint += point;
            ch.MonthPvPoint += point;
            GetTitle(s, ch, HonourTitle, ch.PvpTotalPoint, start: true);
        }

        if (ch.CompanionSlot != NoCompanion && fromPlay)
        {
            // The C++ gives up here, points not added, when the slot names no companion.
            if (ch.SummonedCompanion is not { } c) return;
            byte gain = (byte)point;                                               // BYTE bExpIncrease = (BYTE)dwPoint
            if (c.Level <= 19) c.Exp = Math.Min(c.Exp + gain, c.NextExp);
            var w = new PacketWriter(Msg.CS_UPDATECOMPANIONBYSYSTEM_REQ);
            w.WriteByte(ch.CompanionSlot); w.WriteUInt32(c.Life); w.WriteUInt32(c.Exp);
            s.Send(w);
        }

        if ((type & PvpUseable) != 0) ch.PvpUseablePoint += point;
        SendCS_PVPPOINT_ACK(s, ch, evt);
        if ((type & PvpTotal) != 0) CheckMonthRank(ch, ch.Country, ch.MonthPvPoint, ch.PvpTotalPoint);
    }

    /// <summary>C++ <c>CTPlayer::UsePvPoint</c> — the balances floor at 0. A death names its killer, and is recorded.</summary>
    private void UsePvPoint(ClientSession s, Character ch, uint point, byte evt, byte type, Character? killer = null)
    {
        if (killer is not null) RecordPvP(s, ch, killer, win: false, point);
        if (point == 0) return;
        if ((type & PvpTotal) != 0)
        {
            ch.PvpTotalPoint = ch.PvpTotalPoint > point ? ch.PvpTotalPoint - point : 0;
            ch.MonthPvPoint = ch.MonthPvPoint > point ? ch.MonthPvPoint - point : 0;
        }
        if ((type & PvpUseable) != 0) ch.PvpUseablePoint = ch.PvpUseablePoint > point ? ch.PvpUseablePoint - point : 0;
        SendCS_PVPPOINT_ACK(s, ch, evt);
        if ((type & PvpTotal) != 0) CheckMonthRank(ch, ch.Country, ch.MonthPvPoint, ch.PvpTotalPoint);
    }

    /// <summary>C++ <c>CTPlayer::SendCS_PVPPOINT_ACK</c> (CSSender.cpp:5219).</summary>
    private static void SendCS_PVPPOINT_ACK(ClientSession s, Character ch, byte evt)
    {
        var w = new PacketWriter(Msg.CS_PVPPOINT_ACK, capacity: 13);
        w.WriteUInt32(ch.PvpTotalPoint); w.WriteUInt32(ch.PvpUseablePoint); w.WriteByte(evt);
        w.WriteUInt32(ch.MonthPvPoint);
        s.Send(w);
    }
}
