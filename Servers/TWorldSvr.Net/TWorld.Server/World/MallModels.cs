namespace TWorld.Server.World;

/// <summary>
/// A row of the rock-paper-scissors chart (TRPSGAMECHART, loaded into m_mapRPSGame keyed by
/// MAKEWORD(type, winCount)). <see cref="WinDates"/> holds the timestamps of recent global wins at this
/// (type, winCount); the win-keep rule caps how many wins may stand within a rolling <see cref="WinPeriod"/>.
/// </summary>
public sealed class RpsGame
{
    public byte Type { get; init; }
    public byte WinCount { get; init; }
    public ushort WinKeep { get; set; }       // max concurrent wins allowed in the period (0 = unrestricted)
    public ushort WinPeriod { get; set; }     // window length in days
    public byte[] Prob { get; } = new byte[3]; // win/draw/lose probabilities (server-side, informational)

    /// <summary>Unix-second timestamps of standing wins (m_vWinDate), pruned as they age out.</summary>
    public List<long> WinDates { get; } = new();
}

/// <summary>
/// A cash-mall gift definition (TCMGIFT, loaded into m_mapCMGift keyed by gift id). The catalog is fed by
/// the control server (CT_CMGIFTCHARTUPDATE), which isn't ported yet, so this map is empty at runtime; the
/// MW gift handlers route against it faithfully regardless.
/// </summary>
public sealed class CmGift
{
    public ushort GiftId { get; init; }
    public byte GiftType { get; init; }
    public uint Value { get; init; }
    public byte Count { get; init; }
    public byte TakeType { get; init; }       // 0 = deliver immediately, !=0 = needs a DB take-check
    public byte MaxTakeCount { get; init; }
    public byte ToolOnly { get; init; }        // gift may only be sent by the GM tool
    public ushort ErrGiftId { get; init; }     // fallback gift on a duplicate
    public string Title { get; init; } = "";
    public string Msg { get; init; } = "";
}

/// <summary>One item's sale state inside a cash-item sale event (TCASHITEMSALE: id + sale value/percent).</summary>
public sealed class CashItemSale
{
    public ushort Id { get; set; }
    public byte SaleValue { get; set; }
}

/// <summary>A cash-item sale event (TCASHITEMSALEEVENT), pushed by the control server and kept in
/// m_mapTCashItemSale keyed by index. The world fans it to every map and, once all maps confirm, persists
/// it (the DB write is deferred). <see cref="Value"/> 0 means the sale is being cleared.</summary>
public sealed class CashItemSaleEvent
{
    public uint Index { get; init; }
    public ushort Value { get; set; }
    public List<CashItemSale> Items { get; } = new();
}
