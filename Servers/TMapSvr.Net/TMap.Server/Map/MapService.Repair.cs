using TMap.Protocol;

namespace TMap.Server.Map;

/// <summary>
/// Durability repair — <c>OnCS_DURATIONREP_REQ</c> (CSHandler.cpp:13883) — plus the money currency core it
/// needs (<see cref="Character.UseMoney"/>/<see cref="Character.EarnMoney"/> over the three-tier
/// gold/silver/copper balance). Restores worn items to full durability for a gold cost, in three modes:
/// <c>RPT_NORMAL</c> (one item), <c>RPT_EQUIP</c> (all equipped), <c>RPT_ALL</c> (every repairable item in
/// every bag, incl. equipped). A <c>bNeedCost</c> request just quotes the price. A portable-smith item
/// (<c>IK_NPCCALL</c>) is consumed when supplied.
///
/// <para>Deferred (documented): the NPC discount (<c>GetDiscountRate</c> — NPCs unported ⇒ rate 0, full
/// price) + the PC-bang bonus; the secure-code guard (unported ⇒ treated unlocked); the per-item
/// <c>MTYPE_REPCOST</c> cost magic; and the enchant-based weapon/shield power level (<c>GetPowerLevel</c>
/// uses the item's attr grade — the C++ fallback). No DB save (the C++ repair path saves nothing).</para>
/// </summary>
public sealed partial class MapService
{
    private const byte IkNpcCall = 55; // IK_NPCCALL — the portable-smith usage item

    private void OnCS_DURATIONREP_REQ(ClientSession s, PacketReader r)
    {
        if (s.State != EnterState.InGame || s.Char is not { } ch) return;

        byte needCost = r.ReadByte();
        byte type = r.ReadByte();
        byte invenId = r.ReadByte();
        byte itemSlot = r.ReadByte();
        r.ReadUInt16();               // wNpcID — only used for the (deferred) discount lookup
        byte npcInven = r.ReadByte();
        byte npcItem = r.ReadByte();
        // (secure-code guard, NPC discount + PC-bang bonus deferred; the C++ has no deal/store guard here.)

        var toRepair = new List<(byte inven, Item item)>();
        uint cost = 0;

        if (type == (byte)RepairType.Normal)
        {
            var inv = ch.FindInven(invenId);
            if (inv is null) { SendCS_DURATIONREP_ACK(s, ItemRepairResult.NotFound, toRepair); return; }
            var it = inv.FindItem(itemSlot);
            if (it is null || it.Count == 0) { SendCS_DURATIONREP_ACK(s, ItemRepairResult.NotFound, toRepair); return; }
            if (it.Template is { CanRepair: 0 }) { SendCS_DURATIONREP_ACK(s, ItemRepairResult.Disallow, toRepair); return; }
            if (it.DuraCur >= it.DuraMax) { SendCS_DURATIONREP_ACK(s, ItemRepairResult.NotFound, toRepair); return; }
            toRepair.Add((invenId, it));
            cost = GetRepairCost(it);
        }
        else if (type == (byte)RepairType.Equip)
        {
            var eq = ch.FindInven(Proto.InvenEquip);
            if (eq is not null)
                foreach (var it in eq.Items.OrderBy(i => i.ItemSlot))
                    if (Repairable(it)) { toRepair.Add((Proto.InvenEquip, it)); cost += GetRepairCost(it); }
        }
        else // RPT_ALL — every inventory (the C++ iterates m_mapTINVEN, which includes INVEN_EQUIP)
        {
            foreach (var inv in ch.Invens.OrderBy(i => i.InvenId))
                foreach (var it in inv.Items.OrderBy(i => i.ItemSlot))
                    if (Repairable(it)) { toRepair.Add((inv.InvenId, it)); cost += GetRepairCost(it); }
        }

        if (toRepair.Count == 0) { SendCS_DURATIONREP_ACK(s, ItemRepairResult.NotFound, toRepair); return; }

        uint discountCost = cost;   // discount rate deferred (0) ⇒ discounted == full

        if (needCost != 0) { SendCS_DURATIONREPCOST_ACK(s, cost, 0); return; }   // quote only, no repair

        if (!ch.UseMoney(discountCost, commit: false))
        {
            SendCS_DURATIONREP_ACK(s, ItemRepairResult.NeedMoney, toRepair);
            return;
        }

        // Portable-smith item (only when a slot is supplied): map must be 0 or 8; consume one IK_NPCCALL.
        if (npcInven != Proto.InvenNull && npcItem != Proto.InvalidSlot)
        {
            if (ch.MapId != 0 && ch.MapId != 8)
            {
                SendCS_DURATIONREP_ACK(s, ItemRepairResult.InvalidPos, toRepair);
                return;
            }
            var smithInv = ch.FindInven(npcInven);
            var smith = smithInv?.FindItem(npcItem);
            if (smith is null || smith.Count == 0 || smith.Template is not { Kind: IkNpcCall })
            {
                SendCS_DURATIONREP_ACK(s, ItemRepairResult.NpcCallError, toRepair);
                return;
            }
            smith.Count -= 1;   // C++ UseItem(...,1)
            if (smith.Count == 0) { smithInv!.Items.Remove(smith); SendCS_DELITEM_ACK(s, npcInven, smith); }
            else SendCS_UPDATEITEM_ACK(s, npcInven, smith);
        }

        ch.UseMoney(discountCost, commit: true);
        SendCS_MONEY_ACK(s, ch);

        // Repair restores durability via the bespoke DURATIONREP_ACK (not UPDATEITEM), so persist each mended
        // item explicitly through the fast-path.
        foreach (var (inven, it) in toRepair) { it.DuraCur = it.DuraMax; EnqueueItemSave(s, inven, it); }
        SendCS_DURATIONREP_ACK(s, ItemRepairResult.Success, toRepair);
    }

    /// <summary>The C++ per-item repair filter: worn (<c>DuraMax &amp;&amp; DuraCur &lt; DuraMax</c>) and the
    /// template permits repair (<c>m_bCanRepair</c>).</summary>
    private static bool Repairable(Item it) =>
        it.DuraMax != 0 && it.DuraCur < it.DuraMax && it.Template is { CanRepair: not 0 };

    /// <summary>C++ <c>CTItem::GetRepairCost</c> = <c>max(1, RepairCost[powerLevel]·(DuraMax−DuraCur)·fPrice/
    /// DuraMax)</c>. The power level is the item's attr grade (the C++ <c>GetPowerLevel</c> fallback — the
    /// enchant-based weapon/shield power level is deferred). A missing level row ⇒ 0 (free repair), matching
    /// the C++ <c>FindTLevel</c>-null path. The per-item <c>MTYPE_REPCOST</c> cost magic is deferred.</summary>
    private uint GetRepairCost(Item it)
    {
        int powerLevel = it.Attr?.Grade ?? 1;
        if (it.Template is not { } t || !_templates.RepairCostByLevel.TryGetValue(powerLevel, out uint coef))
            return 0;
        uint prod = coef * (it.DuraMax - it.DuraCur);
        float c = prod * t.Price / it.DuraMax;
        return (uint)MathF.Max(1f, c);
    }

    // ---- senders ----

    /// <summary>C++ <c>CTPlayer::SendCS_MONEY_ACK</c> — the three currency tiers.</summary>
    private static void SendCS_MONEY_ACK(ClientSession s, Character ch)
    {
        var w = new PacketWriter(Msg.CS_MONEY_ACK, capacity: 16);
        w.WriteUInt32(ch.Gold);
        w.WriteUInt32(ch.Silver);
        w.WriteUInt32(ch.Cooper);
        s.Send(w);
    }

    /// <summary>C++ <c>SendCS_DURATIONREPCOST_ACK</c> — the <c>bNeedCost</c> quote: the un-discounted cost and
    /// the discount rate (client applies the discount for display).</summary>
    private static void SendCS_DURATIONREPCOST_ACK(ClientSession s, uint cost, byte discountRate)
    {
        var w = new PacketWriter(Msg.CS_DURATIONREPCOST_ACK, capacity: 8);
        w.WriteUInt32(cost);
        w.WriteByte(discountRate);
        s.Send(w);
    }

    /// <summary>C++ <c>SendCS_DURATIONREP_ACK</c>: <c>BYTE result, BYTE count</c>, then each item
    /// <c>{ BYTE inven, BYTE slot, DWORD duraMax, DWORD duraCur }</c> — written in REVERSE of the repair list
    /// (the C++ pops from the back of the vector).</summary>
    private static void SendCS_DURATIONREP_ACK(ClientSession s, ItemRepairResult result, List<(byte inven, Item item)> items)
    {
        var w = new PacketWriter(Msg.CS_DURATIONREP_ACK, capacity: 16 + items.Count * 10);
        w.WriteByte((byte)result);
        w.WriteByte((byte)items.Count);
        for (int i = items.Count - 1; i >= 0; i--)
        {
            w.WriteByte(items[i].inven);
            w.WriteByte(items[i].item.ItemSlot);
            w.WriteUInt32(items[i].item.DuraMax);
            w.WriteUInt32(items[i].item.DuraCur);
        }
        s.Send(w);
    }
}
