using TMap.Data;
using TMap.Protocol;

namespace TMap.Server.Map;

/// <summary>
/// Titles — C++ <c>CTPlayer::GetTitle</c> (TPlayer.cpp:6421), <c>OnCS_TITLELIST_REQ</c> / <c>OnCS_CHANGETITLE_REQ</c> and the
/// <c>IK_TITLE</c> case of <c>ChangeCharBase</c> / <c>OnMW_CHANGECHARBASE_REQ</c>. <c>TTITLECHART</c> lists every title with
/// its kind and what it takes; a character's owned ones (and the one shown) are kept in <c>TTITLETABLE</c>, rewritten at
/// each save.
/// <para>A title of a kind is earned when the kind's value passes its requirement (<see cref="GetTitle"/>): the total PvP
/// points (honour), gold, this month's kills, deaths and win rate, a completed quest, a place in the month's heroes or
/// a tournament. The shown title is chosen from the owned ones (<c>CS_CHANGETITLE_REQ</c>), goes through the world to
/// every map, and the players around see it (<c>CS_CHANGECHARBASE_ACK</c>).</para>
/// <para><b>Not ported:</b> play-time titles (<c>TIME_TITLE</c> — the chart has none, so play time is not tracked) and custom ones (nothing grants
/// them).</para>
/// </summary>
public sealed partial class MapService
{
    // TITLE_KIND (NetCode.h:2650)
    private const byte HonourMonthTitle = 1, TournamentTitle = 2, VictoryMonthTitle = 3, DefeatsMonthTitle = 4,
        DeathMonthTitle = 5, HonourTitle = 7, GoldTitle = 8, QuestTitle = 15;
    private const byte IkTitle = 103, CcbSuccess = 0;

    /// <summary>C++ <c>CTPlayer::GetTitle(bType, dwValue, bStart)</c>. The titles of <paramref name="kind"/> that
    /// <paramref name="value"/> earns replace the ones of that kind already owned — unless one of them is already owned,
    /// which changes nothing. <paramref name="start"/> false (at login) also takes away the kind's titles first, so a title
    /// no longer earned goes. Each new title is announced (<c>CS_TITLEGAIN_ACK</c>) and stays shown if one of its kind was.</summary>
    private void GetTitle(ClientSession s, Character ch, byte kind, uint value, bool start)
    {
        var earned = new List<ushort>();
        var chart = _templates.Titles.Values;
        switch (kind)
        {
            case HonourMonthTitle:
            {
                bool found = false;
                if (value < MonthCount)
                    for (int i = 0; i <= 9 && i < FameRankCount; i++)
                    {
                        var e = _fameRank[FrtHero, value, i];
                        if (e.CharId != ch.CharId) continue;
                        found = true;
                        earned.Add(e.MonthRank switch { 0 => (ushort)1, 1 => 4, 2 or 3 => 3, _ => 2 });
                    }
                if (!found && ch.TitleId is 1 or 2 or 3 or 4) ch.TitleId = 0;
                break;
            }
            case TournamentTitle:
            {
                bool found = false;
                if (value < MonthCount)
                {
                    for (int i = 0; i <= 7 && i < FameRankCount; i++)
                        if (_fameRank[FrtGod, value, i].CharId == ch.CharId) { found = true; if (i <= 6) earned.Add((ushort)(6 + i)); }
                    for (int i = 0; i <= 7 && i < FameRankCount; i++)
                        if (_fameRank[FrtGoddess, value, i].CharId == ch.CharId) { found = true; earned.Add(5); }
                }
                if (!found && ch.TitleId is >= 5 and <= 12) ch.TitleId = 0;
                break;
            }
            case VictoryMonthTitle:
            {
                if (ch.MonthWin <= 100) break;
                value = (uint)Math.Floor(100.0f / (ch.MonthWin + ch.MonthLose) * ch.MonthWin);   // the win rate, %
                RankedTitles(ch, chart, kind, value, earned);
                break;
            }
            case DefeatsMonthTitle: case DeathMonthTitle: case HonourTitle: case GoldTitle:
                RankedTitles(ch, chart, kind, value, earned);
                break;
            case QuestTitle:
                foreach (var t in chart) if (t.Kind == QuestTitle && t.Requirement == value) earned.Add(t.Id);
                break;
            default: return;                                                             // TIME / CUSTOM: not ported
        }

        if (!start)
            foreach (var t in chart) if (t.Kind == kind) ch.Titles.Remove(t.Id);
        if (earned.Any(ch.Titles.ContainsKey)) return;

        bool selected = false;
        foreach (var t in chart)
            if (t.Kind == kind)
            {
                if (ch.TitleId == t.Id) selected = true;
                ch.Titles.Remove(t.Id);
            }
        foreach (var id in earned)
        {
            ch.Titles[id] = selected;
            SendCS_TITLEGAIN_ACK(s, ch, id, start);
        }
    }

    /// <summary>The kinds ranked by a count: every title of the kind whose requirement the value passes (strictly). When the
    /// shown title was the next-to-best, the best becomes the shown one.</summary>
    private static void RankedTitles(Character ch, IEnumerable<TitleRow> chart, byte kind, uint value, List<ushort> earned)
    {
        foreach (var t in chart) if (t.Kind == kind && t.Requirement < value) earned.Add(t.Id);
        if (earned.Count > 1 && earned[^2] == ch.TitleId) ch.TitleId = earned[^1];
    }

    /// <summary>The login checks (C++ OnMW_CHARINFO_REQ, SSHandler.cpp:2146): every countable kind, the month's heroes and
    /// tournament of last month, and each completed quest.</summary>
    private void LoginTitles(ClientSession s, Character ch)
    {
        GetTitle(s, ch, GoldTitle, ch.Gold, start: false);
        GetTitle(s, ch, VictoryMonthTitle, ch.MonthRankPercent, start: false);
        GetTitle(s, ch, DefeatsMonthTitle, ch.MonthWin, start: false);
        GetTitle(s, ch, DeathMonthTitle, ch.MonthLose, start: false);
        GetTitle(s, ch, HonourTitle, ch.PvpTotalPoint, start: false);
        GetTitle(s, ch, HonourMonthTitle, LastRankMonth, start: false);
        GetTitle(s, ch, TournamentTitle, LastRankMonth, start: false);
        foreach (var q in ch.Quests.Values.ToList())
            if (q.CompleteCount == 1) GetTitle(s, ch, QuestTitle, q.Template.QuestId, start: false);
    }

    private void OnCS_TITLELIST_REQ(ClientSession s)
    {
        if (s.State != EnterState.InGame || !s.IsMain || s.Char is not { } ch || IsTutorial(ch)) return;
        var w = new PacketWriter(Msg.CS_TITLELIST_ACK);
        WriteTitles(w, ch);
        s.Send(w);
    }

    /// <summary>C++ <c>OnCS_CHANGETITLE_REQ</c> → <c>ChangeCharBase(IK_TITLE)</c>: an owned title becomes the shown one, through
    /// the world (<c>MW_CHANGECHARBASE_ACK</c>), which tells every map.</summary>
    private void OnCS_CHANGETITLE_REQ(ClientSession s, PacketReader r)
    {
        ushort id = r.ReadUInt16();
        if (s.State != EnterState.InGame || !s.IsMain || s.Char is not { } ch) return;
        if (ch.TitleId == id || !ch.Titles.ContainsKey(id)) return;
        var w = new PacketWriter(Msg.MW_CHANGECHARBASE_ACK);
        w.WriteUInt32(ch.CharId); w.WriteUInt32(s.Key); w.WriteByte(IkTitle); w.WriteByte(0); w.WriteUInt16(id); w.WriteString(ch.Name);
        _world.Send(w);
    }

    /// <summary>C++ <c>OnMW_CHANGECHARBASE_REQ</c> (SSHandler.cpp:15129): a title, a look (MapService.ItemSkill.cs), a name or a
    /// country (MapService.CharBase.cs) — the change is the player's, and, on its main map, the players around are told.</summary>
    private void OnMW_CHANGECHARBASE_REQ(PacketReader r)
    {
        uint charId = r.ReadUInt32(), key = r.ReadUInt32();
        byte type = r.ReadByte(), value = r.ReadByte();
        ushort titleId = r.ReadUInt16();
        string name = r.ReadString();
        if (type != IkTitle && !IsLookKind(type) && !IsCharBaseKind(type)) return;
        if (_state.FindByChar(charId) is not { Char: { } ch } s || s.Key != key) return;

        uint second = 0;
        if (type == IkTitle)
        {
            ch.TitleId = titleId;
            foreach (var id in ch.Titles.Keys.ToList()) ch.Titles[id] = id == titleId;
        }
        else if (IsCharBaseKind(type)) second = ApplyCharBase(ch, type, value, name);
        else ApplyLook(s, ch, type, value);
        if (!s.IsMain) return;
        var ack = BuildCS_CHANGECHARBASE_ACK(CcbSuccess, charId, type, value, name, titleId, second);
        foreach (var p in _state.InView(s)) p.Send(ack);
    }

    private static void SendCS_TITLEGAIN_ACK(ClientSession s, Character ch, ushort id, bool start)
    {
        var w = new PacketWriter(Msg.CS_TITLEGAIN_ACK);
        WriteTitles(w, ch);
        w.WriteUInt16(id); w.WriteByte((byte)(start ? 1 : 0));
        s.Send(w);
    }

    private static void WriteTitles(PacketWriter w, Character ch)
    {
        w.WriteByte((byte)ch.Titles.Count);
        foreach (var (id, selected) in ch.Titles) { w.WriteUInt16(id); w.WriteByte((byte)(selected ? 1 : 0)); }
    }
}
