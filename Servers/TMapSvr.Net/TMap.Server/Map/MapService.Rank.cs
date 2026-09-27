using TMap.Protocol;

namespace TMap.Server.Map;

/// <summary>
/// One ranked character (C++ <c>tagMONTHRANKER</c>, TMapType.h:2819), in its wire order (<c>WrapPacketIn</c>/<c>Out</c>).
/// </summary>
public sealed class MonthRanker
{
    public uint TotalRank, MonthRank, CharId, TotalPoint, MonthPoint, TotalWin, TotalLose;
    public string Name = "", Say = "", Guild = "";
    public ushort MonthWin, MonthLose;
    public byte Country, Level, Class, Race, Sex, Hair, Face;

    public void Read(PacketReader r)
    {
        TotalRank = r.ReadUInt32(); MonthRank = r.ReadUInt32(); CharId = r.ReadUInt32(); Name = r.ReadString();
        TotalPoint = r.ReadUInt32(); MonthPoint = r.ReadUInt32(); MonthWin = r.ReadUInt16(); MonthLose = r.ReadUInt16();
        TotalWin = r.ReadUInt32(); TotalLose = r.ReadUInt32();
        Country = r.ReadByte(); Level = r.ReadByte(); Class = r.ReadByte(); Race = r.ReadByte(); Sex = r.ReadByte();
        Hair = r.ReadByte(); Face = r.ReadByte(); Say = r.ReadString(); Guild = r.ReadString();
    }

    public void Write(PacketWriter w)
    {
        w.WriteUInt32(TotalRank); w.WriteUInt32(MonthRank); w.WriteUInt32(CharId); w.WriteString(Name);
        w.WriteUInt32(TotalPoint); w.WriteUInt32(MonthPoint); w.WriteUInt16(MonthWin); w.WriteUInt16(MonthLose);
        w.WriteUInt32(TotalWin); w.WriteUInt32(TotalLose);
        w.WriteByte(Country); w.WriteByte(Level); w.WriteByte(Class); w.WriteByte(Race); w.WriteByte(Sex);
        w.WriteByte(Hair); w.WriteByte(Face); w.WriteString(Say); w.WriteString(Guild);
    }
}

/// <summary>
/// PvP ranking on the map — the C++ copy of the world's ladder and its client windows. The world owns the ladder
/// (TWorldSvr.Net WorldService.Rank.cs) and sends it here: the whole board when the map connects
/// (<c>MW_MONTHRANKLIST_REQ</c>), each change (<c>MW_MONTHRANKUPDATE_REQ</c>), last month's top of every country on the
/// month change (<c>MW_FIRSTGRADEGROUP_REQ</c>), the heroes of the finished month (<c>MW_MONTHRANKRESET_REQ</c> — every
/// player's month is reset then), fame-rank entries (<c>MW_FAMERANKUPDATE_REQ</c>) and a warlord's message
/// (<c>MW_WARLORDSAY_REQ</c>).
/// <para>A player whose points move reports itself (<see cref="CheckMonthRank"/>, <c>MW_MONTHRANKUPDATE_ACK</c>) when it
/// could enter the ladder: its country's slot 0 is the all-time top (the warlord), slots 1..32 the month.</para>
/// <para>The client asks for the month ladder (<c>CS_MONTHRANKLIST_REQ</c>, its first 17 of each country), last month's
/// top (<c>CS_FIRSTGRADEGROUP_REQ</c>), a fame list (<c>CS_FAMERANKLIST_REQ</c>) and its own record
/// (<c>CS_PVPRECORD_REQ</c>). <b>Not ported:</b> writing a warlord's message (<c>CS_WARLORDSAY_REQ</c> — secure code),
/// the duel record window's content (duel scores are not kept: it shows zeros), and the month-point wipe for a player
/// not on a map at the month change (<c>DM_MONTHPVPOINTRESET_REQ</c> — the world resets the table itself).</para>
/// </summary>
public sealed partial class MapService
{
    private const int CountryCount = 3, MonthRankCount = 33, MonthCount = 13, FameRankCount = 9, FirstGradeGroupCount = 17;
    private const int FrtHero = 0, FrtGod = 1, FrtGoddess = 2, FrtCount = 4;       // FAMERANK_TYPE

    private byte _rankMonth;
    private readonly MonthRanker[,] _monthRank = NewBoard(CountryCount, MonthRankCount);
    private readonly MonthRanker[,] _firstGrade = NewBoard(CountryCount, FirstGradeGroupCount);
    private readonly MonthRanker[,,] _fameRank = NewFame();
    private const byte FirstGroupCount = FirstGradeGroupCount;    // C++ m_bFirstGroupCount, never changed

    private static MonthRanker[,] NewBoard(int a, int b)
    {
        var x = new MonthRanker[a, b];
        for (int i = 0; i < a; i++) for (int j = 0; j < b; j++) x[i, j] = new MonthRanker();
        return x;
    }

    private static MonthRanker[,,] NewFame()
    {
        var x = new MonthRanker[FrtCount, MonthCount, FameRankCount];
        for (int f = 0; f < FrtCount; f++) for (int m = 0; m < MonthCount; m++) for (int i = 0; i < FameRankCount; i++)
            x[f, m, i] = new MonthRanker();
        return x;
    }

    /// <summary>The month the rank titles and the fame list look at: the one before the ladder's (C++ <c>m_bRankMonth - 1</c>,
    /// 0 read as 12).</summary>
    private byte LastRankMonth => _rankMonth <= 1 ? (byte)12 : (byte)(_rankMonth - 1);

    // ================================ from the world ================================

    private void OnMW_MONTHRANKLIST_REQ(PacketReader r)
    {
        _rankMonth = r.ReadByte();
        int count = r.ReadByte();
        if (count > MonthRankCount) return;
        for (int i = 0; i < CountryCount; i++)
            for (int j = 0; j < count; j++) { _monthRank[i, j].Read(r); _monthRank[i, j].MonthRank = (uint)j; }
    }

    private void OnMW_MONTHRANKUPDATE_REQ(PacketReader r)
    {
        byte month = r.ReadByte(), country = r.ReadByte(), start = r.ReadByte(), end = r.ReadByte();
        if (start > end || end >= MonthRankCount || month >= MonthCount || country >= CountryCount) return;
        if (start != 0 && end != 0)
            for (int i = start; i <= end; i++) { _monthRank[country, i].Read(r); _monthRank[country, i].MonthRank = (uint)i; }
        if (r.ReadByte() != 0) { _monthRank[country, 0].Read(r); _monthRank[country, 0].MonthRank = 0; }   // a new warlord
        _rankMonth = month;
    }

    private void OnMW_FIRSTGRADEGROUP_REQ(PacketReader r)
    {
        r.ReadByte();                                                                    // bRankMonth
        int count = r.ReadByte();
        if (count > FirstGradeGroupCount) return;
        for (int i = 0; i < CountryCount; i++)
            for (int j = 0; j < count; j++) { _firstGrade[i, j].Read(r); _firstGrade[i, j].MonthRank = (uint)j; }
    }

    /// <summary>C++ <c>OnMW_MONTHRANKRESET_REQ</c>: the finished month's heroes arrive; every player's month starts over
    /// and is shown the new fame list, the heroes get their month title, and the ladder empties (warlords stay).</summary>
    private void OnMW_MONTHRANKRESET_REQ(PacketReader r)
    {
        byte month = r.ReadByte();
        if (month == 0) month = 12;
        int count = r.ReadByte();
        if (count > FameRankCount) return;
        for (int i = 0; i < count; i++) { _fameRank[FrtHero, month, i].Read(r); _fameRank[FrtHero, month, i].MonthRank = (uint)i; }

        foreach (var s in _state.AllInGame().ToList())
        {
            if (s.Char is not { } ch) continue;
            MonthRankReset(s, ch);
            SendCS_UPDATEFAMERANKLIST_ACK(s, month);
            var w = new PacketWriter(Msg.CS_PVPPOINT_ACK, capacity: 13);                 // (sic) the month shown as 0
            w.WriteUInt32(ch.PvpTotalPoint); w.WriteUInt32(ch.PvpUseablePoint); w.WriteByte(0); w.WriteUInt32(0);
            s.Send(w);
        }
        for (int i = 0; i < count; i++)
            if (_fameRank[FrtHero, month, i].CharId is not 0 and var id && _state.FindByChar(id) is { Char: { } hero } hs)
                GetTitle(hs, hero, HonourMonthTitle, month, start: true);

        _rankMonth = (byte)(month + 1 > 12 ? month + 1 - 12 : month + 1);
        for (int c = 0; c < CountryCount; c++)
            for (int n = 1; n < MonthRankCount; n++) _monthRank[c, n] = new MonthRanker();
    }

    private void OnMW_MONTHRANKRESETCHAR_REQ(PacketReader r)
    {
        if (_state.FindByChar(r.ReadUInt32()) is { Char: { } ch } s) MonthRankReset(s, ch);
    }

    private void OnMW_FAMERANKUPDATE_REQ(PacketReader r)
    {
        byte type = r.ReadByte(), month = r.ReadByte(), order = r.ReadByte();
        if (type >= FrtCount || month >= MonthCount || order >= FameRankCount) return;
        var entry = _fameRank[type, month, order];
        entry.Read(r);
        if (_state.FindByChar(entry.CharId) is { Char: { } ch } cs) GetTitle(cs, ch, TournamentTitle, month, start: true);
        foreach (var s in _state.AllInGame().ToList()) SendCS_UPDATEFAMERANKLIST_ACK(s, month);
    }

    private void OnMW_WARLORDSAY_REQ(PacketReader r)
    {
        byte type = r.ReadByte(), month = r.ReadByte();
        r.ReadUInt32();                                                                   // dwCharID
        string say = r.ReadString();
        if (type < FrtCount && month < MonthCount) _fameRank[type, month, 0].Say = say;
    }

    // ================================ to the world ================================

    /// <summary>C++ <c>CTMapSvrModule::CheckMonthRank</c> (TMapSvr.cpp:11051): tell the world about this player when it is
    /// already on its country's ladder, beats someone on it, or beats the warlord's total.</summary>
    private void CheckMonthRank(Character ch, byte country, uint monthPoint, uint totalPoint)
    {
        if (country > TcontryB) return;
        bool update = false;
        for (int i = 0; i < MonthRankCount; i++)
            if (_monthRank[country, i].CharId == ch.CharId || _monthRank[country, i].MonthPoint < monthPoint) { update = true; break; }
        if (_monthRank[country, 0].TotalPoint < totalPoint) update = true;
        if (!update) return;

        var me = new MonthRanker
        {
            CharId = ch.CharId, Name = ch.Name, TotalPoint = totalPoint, MonthPoint = monthPoint,
            MonthWin = ch.MonthWin, MonthLose = ch.MonthLose, TotalWin = ch.TotalWin, TotalLose = ch.TotalLose,
            Country = country, Level = ch.Level, Class = ch.Class, Race = ch.Race, Sex = ch.Sex, Hair = ch.Hair, Face = ch.Face,
            Guild = ch.GuildName ?? "",
        };
        var w = new PacketWriter(Msg.MW_MONTHRANKUPDATE_ACK);
        w.WriteByte(_rankMonth); w.WriteByte(country);
        me.Write(w);
        _world.Send(w);
    }

    // ================================ a player's month ================================

    /// <summary>C++ <c>CTPlayer::MonthRankRest</c>: the month's points, rank, wins and losses start over, and the month's
    /// titles go (<c>CS_TITLERESET_ACK</c>).</summary>
    private void MonthRankReset(ClientSession s, Character ch)
    {
        ch.MonthPvPoint = 0; ch.MonthRankOrder = 0; ch.MonthRankPercent = 0; ch.MonthWin = 0; ch.MonthLose = 0; ch.MonthSay = "";
        foreach (var t in _templates.Titles.Values)
            if (t.Kind is VictoryMonthTitle or DefeatsMonthTitle or DeathMonthTitle or TournamentTitle or HonourMonthTitle)
                ch.Titles.Remove(t.Id);
        s.Send(new PacketWriter(Msg.CS_TITLERESET_ACK));
    }

    // ================================ client windows ================================

    private void OnCS_MONTHRANKLIST_REQ(ClientSession s)
    {
        if (s.State != EnterState.InGame || !s.IsMain) return;
        var w = new PacketWriter(Msg.CS_MONTHRANKLIST_ACK);
        w.WriteByte(_rankMonth); w.WriteByte(FirstGroupCount); w.WriteByte(FirstGradeGroupCount);
        for (int i = 0; i < CountryCount; i++)
        {
            w.WriteByte((byte)i);
            for (int j = 0; j < FirstGradeGroupCount; j++) _monthRank[i, j].Write(w);
        }
        s.Send(w);
    }

    private void OnCS_FIRSTGRADEGROUP_REQ(ClientSession s)
    {
        if (s.State != EnterState.InGame || !s.IsMain || _rankMonth >= MonthCount) return;
        var w = new PacketWriter(Msg.CS_FIRSTGRADEGROUP_ACK);
        w.WriteByte(_rankMonth); w.WriteByte(FirstGroupCount);
        for (int i = 0; i < CountryCount; i++)
        {
            w.WriteByte((byte)i);
            for (int j = 0; j < FirstGroupCount; j++) _firstGrade[i, j].Write(w);
        }
        s.Send(w);
    }

    private void OnCS_FAMERANKLIST_REQ(ClientSession s, PacketReader r)
    {
        byte type = r.ReadByte(), month = r.ReadByte();
        if (s.State != EnterState.InGame || !s.IsMain || month >= MonthCount || type >= FrtCount) return;
        var now = DateTime.Now;
        var w = new PacketWriter(Msg.CS_FAMERANKLIST_ACK);
        w.WriteByte(type); w.WriteUInt16((ushort)(month >= now.Month ? now.Year - 1 : now.Year)); w.WriteByte(month);
        w.WriteByte(FameRankCount);
        for (int i = 0; i < FameRankCount; i++) _fameRank[type, month, i].Write(w);
        s.Send(w);
    }

    /// <summary>C++ <c>SendCS_UPDATEFAMERANKLIST_ACK</c> (CSSender.cpp:5741) — every fame list of a month.</summary>
    private void SendCS_UPDATEFAMERANKLIST_ACK(ClientSession s, byte month)
    {
        var now = DateTime.Now;
        var w = new PacketWriter(Msg.CS_UPDATEFAMERANKLIST_ACK);
        w.WriteUInt16((ushort)(month > now.Month ? now.Year - 1 : now.Year)); w.WriteByte(month); w.WriteByte(FrtCount);
        for (int f = 0; f < FrtCount; f++)
        {
            w.WriteByte((byte)f); w.WriteByte(FameRankCount);
            for (int i = 0; i < FameRankCount; i++) _fameRank[f, month, i].Write(w);
        }
        s.Send(w);
    }

    /// <summary>C++ <c>SendCS_PVPRECORD_ACK</c> (CSSender.cpp:5268): the PvP record (type 0) — all-time rank, per-class wins
    /// and losses, the last kills and deaths, the month's rank and tally — or the duel record (duel scores are not kept).</summary>
    private void OnCS_PVPRECORD_REQ(ClientSession s, PacketReader r)
    {
        byte type = r.ReadByte();
        if (s.State != EnterState.InGame || !s.IsMain || s.Char is not { } ch) return;
        var w = new PacketWriter(Msg.CS_PVPRECORD_ACK);
        w.WriteByte(type);
        if (type != 0)
        {
            w.WriteUInt32(0); w.WriteByte(0);
            for (int i = 0; i < 6; i++) { w.WriteUInt32(0); w.WriteUInt32(0); }
            w.WriteByte(0);
        }
        else
        {
            w.WriteUInt32(ch.PvpRankOrder); w.WriteByte(ch.PvpRankPercent);
            for (int i = 0; i < 6; i++) { w.WriteUInt32(ch.PvpRecord[i * 2 + 1]); w.WriteUInt32(ch.PvpRecord[i * 2]); }   // win, lose
            int count = Math.Min(ch.PvpRecent.Count, PvpRecentCount);
            w.WriteByte((byte)count);
            foreach (var rec in ch.PvpRecent.Skip(ch.PvpRecent.Count - count))
            {
                w.WriteString(rec.Name); w.WriteByte((byte)(rec.Win ? 1 : 0)); w.WriteByte(rec.Class); w.WriteByte(rec.Level);
                w.WriteUInt32(rec.Point); w.WriteInt64(rec.UnixTime);
            }
            w.WriteUInt32(ch.MonthRankOrder); w.WriteByte(ch.MonthRankPercent); w.WriteUInt16(ch.MonthWin); w.WriteUInt16(ch.MonthLose);
        }
        s.Send(w);
    }
}
