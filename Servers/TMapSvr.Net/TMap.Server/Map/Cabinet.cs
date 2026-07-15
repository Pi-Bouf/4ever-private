namespace TMap.Server.Map;

/// <summary>
/// One player cabinet (item warehouse) — a port of C++ <c>tagCABINET</c> (TMapType.h:1999), held in
/// <c>CTPlayer::m_mapCabinet</c> (<c>map&lt;BYTE cabinetID, TCABINET*&gt;</c>). A character has up to
/// <see cref="MapService.CabinetCount"/> (3) cabinets, ids 0/1/2; each holds up to
/// <see cref="MapService.CabinetStorageMax"/> (16) items keyed by their per-cabinet
/// <see cref="Item.StItemId"/>. <see cref="Use"/> is the "opened/unlocked" flag (paid for on open).
/// </summary>
public sealed class Cabinet
{
    public byte CabinetId { get; set; }
    public bool Use { get; set; }

    /// <summary>The stored items (C++ <c>m_mapCabinetItem</c>, a <c>map&lt;dwStItemID, CTItem*&gt;</c>). Kept as a
    /// list; the wire order (ascending <see cref="Item.StItemId"/>) is applied at serialization.</summary>
    public List<Item> Items { get; } = new();

    /// <summary>C++ <c>CTPlayer::GetCabinetItem</c> — the stored item with this key, or null.</summary>
    public Item? FindItem(uint stItemId) => Items.FirstOrDefault(i => i.StItemId == stItemId);

    /// <summary>C++ <c>CTPlayer::GetCabinetItemIndex</c> (TPlayer.cpp:1477) — the next stored-item key:
    /// <c>1</c> when empty, else <c>(highest existing key) + 1</c>. NOT a monotonic counter — a removed top
    /// id is reused and a removed middle id leaves a permanent gap (byte-exact with the C++ map iteration).</summary>
    public uint NextStItemId() => Items.Count == 0 ? 1u : Items.Max(i => i.StItemId) + 1u;
}
