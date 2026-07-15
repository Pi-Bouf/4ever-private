namespace TMap.Data;

/// <summary>
/// An NPC registry row from <c>TNPCCHART</c> (C++ <c>CTBLNpc</c> → <c>CTNpc</c>, keyed by <c>m_wID</c>) plus
/// its shop stock (the <c>TNPCITEMCHART</c> item-id list gathered into <see cref="ItemIds"/>). NPCs are a
/// static, server-side registry — the client already knows their positions from its own map files, so the
/// map server never broadcasts them; it only validates an NPC by id + country and holds the shop/quest data.
///
/// <para>The <c>m_wLocalID</c> occupation-zone link (guild/hero discount conditions, neutral-country
/// override) is loaded but not resolved — occupation zones are unported, so <see cref="LocalId"/> is
/// informational and the discount is treated as 0 (see the shop handlers).</para>
/// </summary>
public sealed record NpcDef(ushort Id, byte Type, byte Country, ushort LocalId,
    byte DiscountCondition, byte DiscountRate, byte AddProb, ushort ItemId, ushort MapId,
    float PosX, float PosY, float PosZ)
{
    /// <summary>The shop-stock item ids for this NPC (C++ <c>TNPCITEMCHART.dwItemID</c> rows). For a
    /// <c>TNPC_ITEM</c>/<c>TNPC_PVPOINT</c> NPC the map server resolves each to an item template into the
    /// runtime NPC's <c>m_mapItem</c>; other NPC types repurpose the list (skills, portals…) — deferred.</summary>
    public List<ushort> ItemIds { get; } = new();
}
