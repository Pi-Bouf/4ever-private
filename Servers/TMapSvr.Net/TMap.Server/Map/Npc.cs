using TMap.Data;

namespace TMap.Server.Map;

/// <summary>
/// A runtime NPC — a focused port of <c>CTNpc</c> (the fields the talk/shop spine needs). NPCs are a static,
/// server-side registry keyed by id (C++ <c>m_mapTNpc</c>): the client already knows their positions from its
/// own map files, so the server never spawns/broadcasts them — it only validates an NPC by id + country and
/// holds the shop stock (<see cref="Items"/> = <c>m_mapItem</c>, item id → template). Quests, skill-rent
/// stock, portals, warehouses, auction/arena and the occupation-zone (<c>m_pLocal</c>) country/discount
/// overrides are deferred (PORT_STATUS.md).
/// </summary>
public sealed class Npc
{
    // TCONTRY_TYPE / SDT_TRANS_TYPE (NetCode.h) used by CanTalk.
    private const byte TcontryN = 3;              // TCONTRY_N (neutral — anyone may talk)
    private const byte SdtTransDisguiseD = 4;     // SDT_TRANS_DISGUISE_D (disguise value = country + 4)

    public ushort Id { get; init; }
    public byte Type { get; init; }               // TNPC_* — TNPC_ITEM=2, TNPC_PVPOINT=21, …
    public byte Country { get; init; }            // m_bCountry (TCONTRY_N ⇒ neutral)
    public byte DiscountCondition { get; init; }  // m_bDiscountCondition (DCC_*) — informational (discount deferred)
    public byte DiscountRate { get; init; }       // m_bDiscountRate
    public ushort MapId { get; init; }            // m_wMapID
    public float PosX { get; init; }
    public float PosY { get; init; }
    public float PosZ { get; init; }

    /// <summary>The shop stock (C++ <c>m_mapItem</c>): item id → its template. Populated at bring-up from the
    /// NPC's <c>TNPCITEMCHART</c> ids resolved against the loaded item chart.</summary>
    public Dictionary<ushort, ItemTemplate> Items { get; } = new();

    /// <summary>C++ <c>m_wSpawnPosID</c> — for a <c>TNPC_RETURN</c> NPC, the spawn point it sets as the player's
    /// return point (its <c>TNPCITEMCHART</c> id; the last row wins, as in the C++ load loop, TMapSvr.cpp:3869).</summary>
    public ushort SpawnPosId { get; set; }

    /// <summary>C++ <c>m_pPortal</c> — for a <c>TNPC_PORTAL</c> NPC, the portal it sends players through (its
    /// <c>TNPCITEMCHART</c> id; the last row wins).</summary>
    public ushort PortalId { get; set; }

    /// <summary>C++ <c>m_wItemID</c> (TNPCCHART) — an item the player must carry to use this NPC's portal.</summary>
    public ushort RequiredItemId { get; set; }

    /// <summary>C++ <c>CTNpc::GetItem(WORD)</c> — the stocked item template for an id, or null (not in stock).</summary>
    public ItemTemplate? GetItem(ushort itemId) => Items.GetValueOrDefault(itemId);

    /// <summary>
    /// C++ <c>CTNpc::CanTalk</c> (TNpc.cpp:80) — country gating. A country-bound NPC (<c>m_bCountry != TCONTRY_N</c>)
    /// serves only its own/allied country, and a disguise must match that country's disguise value. A neutral NPC
    /// serves everyone; a disguised player of a different country still passes if disguised as this country.
    /// The occupation-zone country override (<c>m_pLocal-&gt;m_bCountry</c>) is deferred, so the NPC's own country
    /// is used throughout.
    /// </summary>
    public bool CanTalk(byte country, byte aidCountry, byte disguise)
    {
        if (Country != TcontryN)
            return (country == Country || aidCountry == Country)
                && (disguise == 0 || disguise == Country + SdtTransDisguiseD);

        byte npcCountry = Country; // m_pLocal ? m_pLocal->m_bCountry : m_bCountry — occupation zones deferred
        return npcCountry == TcontryN
            || ((country == npcCountry || aidCountry == npcCountry)
                 && (disguise == 0 || disguise == npcCountry + SdtTransDisguiseD))
            || ((country != npcCountry && aidCountry != npcCountry)
                 && disguise == npcCountry + SdtTransDisguiseD);
    }
}
