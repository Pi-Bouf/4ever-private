namespace TControl.Protocol;

/// <summary>A cash-shop sale entry (tagTCASHITEMSALE): item id + sale percentage.</summary>
public sealed class CashItemSale
{
    public ushort Id { get; set; }
    public byte SaleValue { get; set; }
}

/// <summary>A monster-regen entry (tagMONREGEN).</summary>
public sealed class MonRegen
{
    public ushort MonId { get; set; }
    public uint Delay { get; set; }
    public ushort MapId { get; set; }
    public float PosX { get; set; }
    public float PosY { get; set; }
    public float PosZ { get; set; }
}

/// <summary>A lottery/gift entry (tagLOTTERY).</summary>
public sealed class Lottery
{
    public ushort ItemId { get; set; }
    public byte Num { get; set; }
    public ushort Winner { get; set; }
}

/// <summary>Monster-event actions (tagMONEVENT).</summary>
public sealed class MonEvent
{
    public byte StartAction { get; set; }
    public byte EndAction { get; set; }
    public List<ushort> SpawnIds { get; } = new();
}

/// <summary>
/// A scheduled event (tagEVENTINFO). <see cref="Write"/>/<see cref="Read"/> are byte-exact ports of the
/// C++ <c>WrapPacketIn</c>/<c>WrapPacketOut</c> — the wire layout used by <c>CT_EVENTUPDATE</c>,
/// <c>CT_EVENTCHANGE</c>, and <c>CT_EVENTLIST</c>. The scheduler-only fields (<see cref="StartAlarmFired"/>
/// / <see cref="EndAlarmFired"/> / <see cref="State"/>) are in-memory state, not part of the wire body
/// except <see cref="State"/> which IS serialized (matches C++ <c>m_bState</c>).
/// </summary>
public sealed class EventInfo
{
    public uint Index { get; set; }
    public byte Id { get; set; }        // EventType
    public byte State { get; set; }     // 0=idle, 1=running
    public byte GroupId { get; set; }
    public byte SvrType { get; set; }
    public byte SvrId { get; set; }     // 0 = ALL
    public long StartDate { get; set; } // __time64_t (unix seconds)
    public long EndDate { get; set; }
    public ushort Value { get; set; }
    public ushort MapId { get; set; }   // 0xFF = ALL
    public uint StartAlarm { get; set; }
    public uint EndAlarm { get; set; }
    public string StartMsg { get; set; } = "";
    public string EndMsg { get; set; } = "";
    public string Title { get; set; } = "";
    public byte PartTime { get; set; }  // 0 = daily, 1 = term
    public string LotMsg { get; set; } = "";
    public List<CashItemSale> CashItems { get; } = new();
    public MonEvent Mon { get; } = new();
    public List<MonRegen> MonRegens { get; } = new();
    public List<Lottery> Lotteries { get; } = new();

    // Scheduler-only (not on the wire):
    public bool StartAlarmFired { get; set; }
    public bool EndAlarmFired { get; set; }

    /// <summary>Serialize into the packet (C++ <c>WrapPacketIn</c>, using <c>&lt;&lt;</c>).</summary>
    public void Write(PacketWriter w)
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
        foreach (var c in CashItems) { w.WriteUInt16(c.Id); w.WriteByte(c.SaleValue); }

        w.WriteByte(Mon.StartAction);
        w.WriteByte(Mon.EndAction);
        w.WriteUInt16((ushort)Mon.SpawnIds.Count);
        foreach (var s in Mon.SpawnIds) w.WriteUInt16(s);

        w.WriteUInt16((ushort)MonRegens.Count);
        foreach (var m in MonRegens)
        {
            w.WriteUInt16(m.MonId); w.WriteUInt32(m.Delay); w.WriteUInt16(m.MapId);
            w.WriteFloat(m.PosX); w.WriteFloat(m.PosY); w.WriteFloat(m.PosZ);
        }

        w.WriteUInt16((ushort)Lotteries.Count);
        foreach (var l in Lotteries) { w.WriteUInt16(l.ItemId); w.WriteByte(l.Num); w.WriteUInt16(l.Winner); }
    }

    /// <summary>Deserialize from the packet (C++ <c>WrapPacketOut</c>, using <c>&gt;&gt;</c>).</summary>
    public static EventInfo Read(PacketReader r)
    {
        var e = new EventInfo
        {
            Index = r.ReadUInt32(),
            Id = r.ReadByte(),
            State = r.ReadByte(),
            GroupId = r.ReadByte(),
            SvrType = r.ReadByte(),
            SvrId = r.ReadByte(),
            StartDate = r.ReadInt64(),
            EndDate = r.ReadInt64(),
            Value = r.ReadUInt16(),
            MapId = r.ReadUInt16(),
            StartAlarm = r.ReadUInt32(),
            EndAlarm = r.ReadUInt32(),
            StartMsg = r.ReadString(),
            EndMsg = r.ReadString(),
            Title = r.ReadString(),
            PartTime = r.ReadByte(),
            LotMsg = r.ReadString(),
        };

        ushort n = r.ReadUInt16();
        for (int i = 0; i < n; i++)
            e.CashItems.Add(new CashItemSale { Id = r.ReadUInt16(), SaleValue = r.ReadByte() });

        e.Mon.StartAction = r.ReadByte();
        e.Mon.EndAction = r.ReadByte();
        n = r.ReadUInt16();
        for (int i = 0; i < n; i++) e.Mon.SpawnIds.Add(r.ReadUInt16());

        n = r.ReadUInt16();
        for (int i = 0; i < n; i++)
            e.MonRegens.Add(new MonRegen
            {
                MonId = r.ReadUInt16(), Delay = r.ReadUInt32(), MapId = r.ReadUInt16(),
                PosX = r.ReadFloat(), PosY = r.ReadFloat(), PosZ = r.ReadFloat(),
            });

        n = r.ReadUInt16();
        for (int i = 0; i < n; i++)
            e.Lotteries.Add(new Lottery { ItemId = r.ReadUInt16(), Num = r.ReadByte(), Winner = r.ReadUInt16() });

        return e;
    }

    /// <summary>Parse a TEVENTCHART.szValue column into this event's sub-vectors, keyed by <see cref="Id"/>
    /// (C++ <c>ParseStrValue</c>).</summary>
    public void ParseStrValue(string str)
    {
        switch ((EventType)Id)
        {
            case EventType.CashSale:
                foreach (var tok in str.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    int dash = tok.IndexOf('-');
                    if (dash < 0) continue;
                    CashItems.Add(new CashItemSale { Id = PU(tok[..dash]), SaleValue = PB(tok[(dash + 1)..]) });
                }
                break;

            case EventType.MonSpawn:
                foreach (var tok in str.Split(';', StringSplitOptions.RemoveEmptyEntries)) Mon.SpawnIds.Add(PU(tok));
                break;

            case EventType.MonRegen:
                foreach (var tok in str.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    var f = tok.Split('-');
                    if (f.Length < 6) continue;
                    MonRegens.Add(new MonRegen { MonId = PU(f[0]), Delay = PI(f[1]), MapId = PU(f[2]), PosX = PF(f[3]), PosY = PF(f[4]), PosZ = PF(f[5]) });
                }
                break;

            case EventType.Lottery:
            {
                int bar = str.IndexOf('|');
                string rewards = bar < 0 ? str : str[..bar];
                foreach (var tok in rewards.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    int x = tok.IndexOf('x'), dash = tok.IndexOf('-');
                    if (x < 0 || dash < 0) continue;
                    Lotteries.Add(new Lottery { ItemId = PU(tok[..x]), Num = PB(tok[(x + 1)..dash]), Winner = PU(tok[(dash + 1)..]) });
                }
                if (bar >= 0) LotMsg = str[(bar + 1)..];
                break;
            }

            case EventType.GiftTime:
                LotMsg = str;
                break;
        }
    }

    /// <summary>Serialize this event's sub-vectors back into a szValue column (C++ <c>MakeStrValue</c>).</summary>
    public string MakeStrValue() => (EventType)Id switch
    {
        EventType.CashSale => string.Concat(CashItems.Select(c => $"{c.Id}-{c.SaleValue};")),
        EventType.MonSpawn => string.Concat(Mon.SpawnIds.Select(s => $"{s};")),
        EventType.MonRegen => string.Concat(MonRegens.Select(m =>
            $"{m.MonId}-{m.Delay}-{m.MapId}-{m.PosX.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}-{m.PosY.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}-{m.PosZ.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)};")),
        EventType.Lottery => string.Concat(Lotteries.Select(l => $"{l.ItemId}x{l.Num}-{l.Winner};")) + "|" + LotMsg,
        EventType.GiftTime => LotMsg,
        _ => "",
    };

    private static ushort PU(string s) => ushort.TryParse(s.Trim(), out var v) ? v : (ushort)0;
    private static byte PB(string s) => byte.TryParse(s.Trim(), out var v) ? v : (byte)0;
    private static uint PI(string s) => uint.TryParse(s.Trim(), out var v) ? v : 0u;
    private static float PF(string s) =>
        float.TryParse(s.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0f;
}

/// <summary>Cash-mall gift catalog row (tagCMGIFT).</summary>
public sealed class CmGift
{
    public ushort GiftId { get; set; }
    public byte GiftType { get; set; }
    public uint Value { get; set; }
    public byte Count { get; set; }
    public byte TakeType { get; set; }
    public byte MaxTakeCount { get; set; }
    public byte ToolOnly { get; set; }
    public ushort ErrGiftId { get; set; }
    public string Title { get; set; } = "";
    public string Msg { get; set; } = "";
}

/// <summary>An active chat-ban record (tagBANINFO).</summary>
public sealed class BanInfo
{
    public string OpName { get; set; } = "";
    public string BanName { get; set; } = "";
    public string Reason { get; set; } = "";
    public uint Id { get; set; }
    public ushort Min { get; set; }
    public long Time { get; set; } // __time64_t
}

/// <summary>A patch-file record (tagPATCHFILE / TPREVERSION).</summary>
public sealed class PatchFile
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public uint Size { get; set; }
    public uint BetaVer { get; set; }
}

/// <summary>A machine (TMACHINE + TNETWORK + TIPADDR).</summary>
public sealed class TMachine
{
    public byte MachineId { get; set; }
    public string Name { get; set; } = "";
    public byte RouteId { get; set; }
    public string Network { get; set; } = "";
    public List<string> IpAddr { get; } = new();
    public List<string> PriAddr { get; } = new();
}

/// <summary>A server group (TGROUP).</summary>
public sealed class TGroup
{
    public byte GroupId { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>A server type (TSVRTYPE, bControl=1).</summary>
public sealed class TSvrType
{
    public byte Type { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>A sellable cash-shop item (TCASHSHOPITEMCHART, bCanSell=1).</summary>
public sealed class TCashItem
{
    public ushort Id { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>A raw TSERVER row (bType&lt;&gt;6). Assembled into the runtime service model by the server layer.</summary>
public sealed class ServerRow
{
    public byte GroupId { get; set; }
    public byte ServerId { get; set; }
    public byte Type { get; set; }
    public byte MachineId { get; set; }
    public ushort Port { get; set; }
    public string Name { get; set; } = "";
}
