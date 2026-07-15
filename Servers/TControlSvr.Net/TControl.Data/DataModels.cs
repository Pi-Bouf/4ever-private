namespace TControl.Data;

/// <summary>A raw TEVENTCHART row. <c>SzValue</c> is the packed sub-vector column the server layer
/// parses (C++ <c>ParseStrValue</c>) into cash-sale / mon-spawn / mon-regen / lottery entries.</summary>
public sealed class EventChartRow
{
    public uint Index { get; set; }
    public byte Id { get; set; }
    public string Title { get; set; } = "";
    public byte GroupId { get; set; }
    public byte SvrType { get; set; }
    public byte SvrId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public ushort Value { get; set; }
    public ushort MapId { get; set; }
    public uint StartAlarm { get; set; }
    public uint EndAlarm { get; set; }
    public byte PartTime { get; set; }
    public string StartMsg { get; set; } = "";
    public string MidMsg { get; set; } = "";
    public string EndMsg { get; set; } = "";
    public string SzValue { get; set; } = "";
}

/// <summary>A resolved service endpoint from <c>TLoadService</c>.</summary>
public readonly record struct ServiceEndpoint(string Ip, ushort Port);
