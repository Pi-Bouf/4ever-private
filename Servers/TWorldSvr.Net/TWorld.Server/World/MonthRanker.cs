using TWorld.Protocol;

namespace TWorld.Server.World;

/// <summary>
/// One PvP ranking row — the ported C++ <c>MONTHRANKER</c>. The wire order in <see cref="WrapIn"/>/
/// <see cref="WrapOut"/> is transcribed verbatim from <c>TWorldType.h</c> <c>WrapPacketIn/Out</c>.
/// </summary>
public sealed class MonthRanker
{
    public uint CharId { get; set; }
    public string Name { get; set; } = "";
    public uint TotalRank { get; set; }
    public uint MonthRank { get; set; }
    public uint TotalPoint { get; set; }
    public uint MonthPoint { get; set; }
    public ushort MonthWin { get; set; }
    public ushort MonthLose { get; set; }
    public uint TotalWin { get; set; }
    public uint TotalLose { get; set; }
    public byte Country { get; set; }
    public byte Level { get; set; }
    public byte Class { get; set; }
    public byte Race { get; set; }
    public byte Sex { get; set; }
    public byte Hair { get; set; }
    public byte Face { get; set; }
    public string Say { get; set; } = "";
    public string Guild { get; set; } = "";

    public void Reset()
    {
        CharId = 0; Name = ""; TotalRank = 0; MonthRank = 0; TotalPoint = 0; MonthPoint = 0;
        MonthWin = 0; MonthLose = 0; TotalWin = 0; TotalLose = 0; Country = 0; Level = 0;
        Class = 0; Race = 0; Sex = 0; Hair = 0; Face = 0; Say = ""; Guild = "";
    }

    public MonthRanker Clone() => (MonthRanker)MemberwiseClone();

    public void WrapIn(PacketWriter w)
    {
        w.WriteUInt32(TotalRank);
        w.WriteUInt32(MonthRank);
        w.WriteUInt32(CharId);
        w.WriteString(Name);
        w.WriteUInt32(TotalPoint);
        w.WriteUInt32(MonthPoint);
        w.WriteUInt16(MonthWin);
        w.WriteUInt16(MonthLose);
        w.WriteUInt32(TotalWin);
        w.WriteUInt32(TotalLose);
        w.WriteByte(Country);
        w.WriteByte(Level);
        w.WriteByte(Class);
        w.WriteByte(Race);
        w.WriteByte(Sex);
        w.WriteByte(Hair);
        w.WriteByte(Face);
        w.WriteString(Say);
        w.WriteString(Guild);
    }

    public void WrapOut(PacketReader r)
    {
        TotalRank = r.ReadUInt32();
        MonthRank = r.ReadUInt32();
        CharId = r.ReadUInt32();
        Name = r.ReadString();
        TotalPoint = r.ReadUInt32();
        MonthPoint = r.ReadUInt32();
        MonthWin = r.ReadUInt16();
        MonthLose = r.ReadUInt16();
        TotalWin = r.ReadUInt32();
        TotalLose = r.ReadUInt32();
        Country = r.ReadByte();
        Level = r.ReadByte();
        Class = r.ReadByte();
        Race = r.ReadByte();
        Sex = r.ReadByte();
        Hair = r.ReadByte();
        Face = r.ReadByte();
        Say = r.ReadString();
        Guild = r.ReadString();
    }
}
