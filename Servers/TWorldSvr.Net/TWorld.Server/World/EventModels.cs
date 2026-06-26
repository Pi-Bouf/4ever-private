using TWorld.Protocol;

namespace TWorld.Server.World;

/// <summary>
/// An in-progress server event (EVENTINFO → m_mapEVENT). <see cref="ReadFrom"/> / <see cref="WriteTo"/> are
/// transcribed field-for-field from the C++ <c>EVENTINFO::WrapPacketOut</c> / <c>WrapPacketIn</c> so the wire
/// layout is byte-exact. Dates are <c>__time64_t</c> (int64). Mirrors the nested cash-item / mon-spawn /
/// mon-regen / lottery vectors.
/// </summary>
public sealed class EventInfo
{
    public uint Index;
    public byte Id;
    public byte State;
    public byte GroupId;
    public byte SvrType;
    public byte SvrId;
    public long StartDate;
    public long EndDate;
    public ushort Value;
    public ushort MapId = 0xFF;
    public uint StartAlarm;
    public uint EndAlarm;
    public string StartMsg = "";
    public string EndMsg = "";
    public string Title = "";
    public byte PartTime;
    public string LotMsg = "";
    public readonly List<(ushort Id, byte SaleValue)> CashItems = new();
    public byte MonStartAction;
    public byte MonEndAction;
    public readonly List<ushort> SpawnIds = new();
    public readonly List<(ushort MonId, uint Delay, ushort MapId, float X, float Y, float Z)> MonRegen = new();
    public readonly List<Lottery> Lotteries = new();

    public sealed class Lottery { public ushort ItemId; public byte Num; public ushort Winner; }

    /// <summary>Read the body that follows <c>bEventID, wValue</c> (C++ EVENTINFO::WrapPacketOut).</summary>
    public void ReadFrom(PacketReader r)
    {
        Index = r.ReadUInt32();
        Id = r.ReadByte();
        State = r.ReadByte();
        GroupId = r.ReadByte();
        SvrType = r.ReadByte();
        SvrId = r.ReadByte();
        StartDate = r.ReadInt64();
        EndDate = r.ReadInt64();
        Value = r.ReadUInt16();
        MapId = r.ReadUInt16();
        StartAlarm = r.ReadUInt32();
        EndAlarm = r.ReadUInt32();
        StartMsg = r.ReadString();
        EndMsg = r.ReadString();
        Title = r.ReadString();
        PartTime = r.ReadByte();
        LotMsg = r.ReadString();

        ushort n = r.ReadUInt16();
        for (int i = 0; i < n; i++) CashItems.Add((r.ReadUInt16(), r.ReadByte()));

        MonStartAction = r.ReadByte();
        MonEndAction = r.ReadByte();
        n = r.ReadUInt16();
        for (int i = 0; i < n; i++) SpawnIds.Add(r.ReadUInt16());

        n = r.ReadUInt16();
        for (int i = 0; i < n; i++)
            MonRegen.Add((r.ReadUInt16(), r.ReadUInt32(), r.ReadUInt16(), r.ReadFloat(), r.ReadFloat(), r.ReadFloat()));

        n = r.ReadUInt16();
        for (int i = 0; i < n; i++)
            Lotteries.Add(new Lottery { ItemId = r.ReadUInt16(), Num = r.ReadByte(), Winner = r.ReadUInt16() });
    }

    /// <summary>Write the EVENTINFO body (C++ EVENTINFO::WrapPacketIn).</summary>
    public void WriteTo(PacketWriter w)
    {
        w.WriteUInt32(Index);
        w.WriteByte(Id);
        w.WriteByte(State);
        w.WriteByte(GroupId);
        w.WriteByte(SvrType);
        w.WriteByte(SvrId);
        w.WriteInt64(StartDate);
        w.WriteInt64(EndDate);
        w.WriteUInt16(Value);
        w.WriteUInt16(MapId);
        w.WriteUInt32(StartAlarm);
        w.WriteUInt32(EndAlarm);
        w.WriteString(StartMsg);
        w.WriteString(EndMsg);
        w.WriteString(Title);
        w.WriteByte(PartTime);
        w.WriteString(LotMsg);

        w.WriteUInt16((ushort)CashItems.Count);
        foreach (var (id, sale) in CashItems) { w.WriteUInt16(id); w.WriteByte(sale); }

        w.WriteByte(MonStartAction);
        w.WriteByte(MonEndAction);
        w.WriteUInt16((ushort)SpawnIds.Count);
        foreach (var id in SpawnIds) w.WriteUInt16(id);

        w.WriteUInt16((ushort)MonRegen.Count);
        foreach (var m in MonRegen)
        { w.WriteUInt16(m.MonId); w.WriteUInt32(m.Delay); w.WriteUInt16(m.MapId); w.WriteFloat(m.X); w.WriteFloat(m.Y); w.WriteFloat(m.Z); }

        w.WriteUInt16((ushort)Lotteries.Count);
        foreach (var l in Lotteries) { w.WriteUInt16(l.ItemId); w.WriteByte(l.Num); w.WriteUInt16(l.Winner); }
    }
}

/// <summary>A lucky-event (scheduled quarter) chart row (LUCKYEVENT). <see cref="WriteTo"/> mirrors
/// <c>LUCKYEVENT::WrapPacketIn</c> (the CT_EVENTQUARTERLIST entry layout).</summary>
public sealed class LuckyEvent
{
    public ushort Id;
    public byte Day, Hour, Min, Count;
    public ushort ItemId1, ItemId2, ItemId3, ItemId4, ItemId5;
    public string Present = "", Announce = "", Title = "", Message = "";
    public string Item1 = "", Item2 = "", Item3 = "", Item4 = "", Item5 = "";

    public void WriteTo(PacketWriter w)
    {
        w.WriteUInt16(Id); w.WriteByte(Day); w.WriteByte(Hour); w.WriteByte(Min);
        w.WriteUInt16(ItemId1); w.WriteUInt16(ItemId2); w.WriteUInt16(ItemId3); w.WriteUInt16(ItemId4); w.WriteUInt16(ItemId5);
        w.WriteByte(Count);
        w.WriteString(Present); w.WriteString(Announce); w.WriteString(Title); w.WriteString(Message);
        w.WriteString(Item1); w.WriteString(Item2); w.WriteString(Item3); w.WriteString(Item4); w.WriteString(Item5);
    }

    /// <summary>Read a LUCKYEVENT body (C++ LUCKYEVENT::WrapPacketOut) — the CT_EVENTQUARTERUPDATE payload.</summary>
    public static LuckyEvent ReadFrom(PacketReader r)
    {
        var e = new LuckyEvent
        {
            Id = r.ReadUInt16(), Day = r.ReadByte(), Hour = r.ReadByte(), Min = r.ReadByte(),
            ItemId1 = r.ReadUInt16(), ItemId2 = r.ReadUInt16(), ItemId3 = r.ReadUInt16(), ItemId4 = r.ReadUInt16(), ItemId5 = r.ReadUInt16(),
            Count = r.ReadByte(),
            Present = r.ReadString(), Announce = r.ReadString(), Title = r.ReadString(), Message = r.ReadString(),
            Item1 = r.ReadString(), Item2 = r.ReadString(), Item3 = r.ReadString(), Item4 = r.ReadString(), Item5 = r.ReadString(),
        };
        return e;
    }
}

/// <summary>A live scheduled quarter (TEVENTQUARTER → m_mapEVQT): when it next fires + its present/announce.</summary>
public sealed class EventQuarter
{
    public ushort Id;
    public byte Day, Hour, Minute;
    public bool Noticed;
    public long Time;             // next-fire unix seconds (GetNextEventTime)
    public string Present = "";
    public string Announce = "";
}

/// <summary>A timed-expiry entry (TEXPIREDBUF → m_vExpired), kept sorted by <see cref="TimeExpired"/>.</summary>
public readonly record struct ExpiredBuf(byte Type, long TimeExpired, uint Value1, uint Value2);
