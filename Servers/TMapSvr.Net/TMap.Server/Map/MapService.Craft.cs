using TMap.Data;
using TMap.Protocol;

namespace TMap.Server.Map;

/// <summary>
/// Crafting — <c>OnCS_ITEMUPGRADE_REQ</c> (CSHandler.cpp:7177), <c>OnCS_REFINE_REQ</c> (:14124) and
/// <c>OnCS_ITEMCHANGE_REQ</c> (:15208), with their helpers (<c>CalcProb</c>, <c>MakeSpecialItem</c>,
/// <c>SetMagicOpt</c>, <c>MakeItemMagicValue</c>, TMapSvr.cpp / TObjBase.cpp).
///
/// <para><b>Upgrade</b> uses a scroll on an item; the scroll's kind picks the branch: upgrade, purify
/// (downgrade), gem, appearance transfer ("mogg", from another piece of gear), magic / rare options, clear
/// options, wrap, ELD, clear refine, change / remove / dye effect. The scroll is always spent. <b>Refine</b>
/// feeds 1–3 other items (or refine catalysts) into one for durability and a chance to carry their options
/// over; the materials are always spent. <b>Item change</b> opens a gamble box.</para>
///
/// <para><b>Faithful, flagged:</b> this build's upgrade success sets the item straight to level 24 (a custom
/// change in these sources); refining without a valid NPC id always fails (C++ <c>CalcProb</c> returns 0); a
/// purify scroll whose grade exceeds the level wraps the byte; the accessory scroll's value is
/// <c>rand() % Max</c> (the C++ formula cancels its minimum). The NPC id only has to exist — any NPC type, at
/// any distance. <b>Changed:</b> an accessory scroll with an empty option table returns quietly (the C++ does
/// the same) and its option search is capped instead of looping forever; <c>bMinLevel</c> is taken as 0 (the
/// C++ never loads it); refining with no material is refused (the C++ divides by zero).</para>
///
/// <para><b>Not ported:</b> secure codes, the event bonuses (<c>m_wEventValue</c> = 0), PC-bang, and the
/// occupation / castle NPC discounts (<c>GetDiscountRate</c> = 0, no <c>m_bAddProb</c>).</para>
/// </summary>
public sealed partial class MapService
{
    // ITEMUPGRADE_RESULT (NetCode.h:570)
    private const byte UpgSuccess = 0, UpgNoItem = 1, UpgNoGrade = 2, UpgFail = 3, UpgMoney = 4, UpgNpcCallError = 5,
        UpgInvalidPos = 6, UpgGemSuccess = 7, UpgGemFail = 8, UpgMagicFail = 10, UpgMagicClear = 11, UpgWrap = 12,
        UpgEld = 13, UpgWrapping = 14, UpgMaxEld = 15, UpgNoRefine = 16, UpgNoGradeEffect = 17, UpgClearRefine = 18,
        UpgChangeEffect = 19, UpgSameColor = 20, UpgColor = 21, UpgDowngrade = 22, UpgMoggSuccess = 23,
        UpgMoggWrongKind = 24, UpgMoggSameItem = 25, UpgCannotColor = 26, UpgUndye = 27;
    // ITEMCHANGE_RESULT (NetCode.h:805)
    private const byte ChgSuccess = 0, ChgFail = 1, ChgInvalid = 2, ChgStatus = 3, ChgFull = 4;
    // TITEM_TYPE / TITEM_KIND
    private const byte ItWeapon = 1, ItDefensive = 2, ItLong = 4, ItShield = 6, ItUse = 7, ItGrade = 9, ItRefine = 14, ItCostume = 17;
    private const byte IkMultiVajra = 11, IkShieldKind = 12, Ik1Hand = 1, Ik2Hand = 3, IkAx = 5, IkMBar = 8, IkBack = 19,
        IkUpgrade = 29, IkDowngrade = 30, IkMagicGrade = 31, IkRareGrade = 32, IkClearMagic = 77,
        IkChange = 78, IkWrap = 79, IkEld = 80, IkClearRefine = 82, IkChgGradeEffect = 83, IkColor = 85,
        IkGem = 102, IkDelEffect = 104, IkCloakEffect = 105, IkAccessoryScroll = 205;
    private const byte EsPrmWeapon = 0, EsSndWeapon = 1, EsLongWeapon = 2, EsHead = 3, EsBody = 5, EsPants = 6,
        EsFoot = 7, EsHand = 8;
    private const int IeCount = 40, MinGradeEffectLevel = 17, TmaxItemLevelDown = 9, TmagicMax = 6, ItemLevelMax = 24,
        GemMax = 5, ItemMagicBaseLevel = 70;
    private const ushort EffectChangeId = 24501, GemKeepItem = 18191, GemGuaranteed = 1337;
    // PROB_TYPE and the SDT_STATUS buffs that raise them
    private enum Prob { Magic, Refine, Trans, Upgrade, ItemGuard, RareMagic }
    private const byte SdtStatusItemUpgrade = 42, SdtStatusRefineProb = 58, SdtStatusTransProb = 59,
        SdtStatusMagicProb = 60, SdtStatusItemGuard = 64;
    private const byte MipMagic = 0, MipRare = 1;

    /// <summary>The crafting dice (C++ <c>rand()</c>). Replaceable in tests.</summary>
    public Random CraftRng { get; set; } = new();
    private int Rand() => CraftRng.Next(32768);   // MSVC rand(): 0..RAND_MAX (0x7FFF)

    // ============================== upgrade ==============================

    private void OnCS_ITEMUPGRADE_REQ(ClientSession s, PacketReader r)
    {
        if (s.State != EnterState.InGame || !s.IsMain || s.Char is not { } ch) return;
        if (s.Deal.Status != (byte)DealStatus.Ready || s.Store.IsOpen) return;

        byte tInv = r.ReadByte(), tSlot = r.ReadByte(), gInv = r.ReadByte(), gSlot = r.ReadByte();
        ushort npcId = r.ReadUInt16();
        byte nInv = r.ReadByte(), nSlot = r.ReadByte();
        ushort color = r.ReadUInt16();

        var inven = ch.FindInven(tInv);
        var gradeInven = ch.FindInven(gInv);
        if (inven is null || gradeInven is null) return;
        var item = inven.Items.FirstOrDefault(i => i.ItemSlot == tSlot);
        var grade = gradeInven.Items.FirstOrDefault(i => i.ItemSlot == gSlot);
        if (item is null || grade is null || item.GLevel != 0 || item.Count == 0 || grade.Count == 0
            || item.Template is not { } it || grade.Template is not { } gt)
        { SendUpgradeFail(s, UpgNoItem); return; }
        if (gt.Type is not (ItGrade or ItWeapon or ItDefensive or ItLong or ItShield)) { SendUpgradeFail(s, UpgNoItem); return; }
        if (!item.CanUse || !grade.CanUse) { SendUpgradeFail(s, UpgNoItem); return; }
        if (_state.FindNpc(npcId) is not { } npc) { SendUpgradeFail(s, UpgNpcCallError); return; }

        long money = 0;
        bool hasOptionKind = item.Magic.Any(m => m.Template?.OptionKind != 0 && m.Template is not null);
        switch (gt.Kind)
        {
            case IkGem:
                if (item.Gem == GemMax || it.CanGrade == 0 || hasOptionKind) { SendUpgradeFail(s, UpgNoGrade); return; }
                break;
            case >= 1 and <= 9 or >= 12 and <= 19:                        // mogg: 1HAND..MSTICK, SHIELD..BACK
                if (grade.Level != 0) { SendUpgradeFail(s, UpgMoggSameItem); return; }
                if (IsMoggPair(it, gt) && gt.SubSlot == it.SubSlot)
                {
                    if (grade.TemplateId == item.TemplateId) { SendUpgradeFail(s, UpgMoggSameItem); return; }
                }
                else
                {
                    if (((gt.Kind != it.Kind || gt.SlotId != it.SlotId)
                         && (gt.SlotId == EsPrmWeapon || gt.SlotId == EsSndWeapon || gt.SlotId == EsLongWeapon))
                        || gt.SlotId != it.SlotId)
                    { SendUpgradeFail(s, UpgMoggWrongKind); return; }
                    if (grade.TemplateId == item.TemplateId) { SendUpgradeFail(s, UpgMoggSameItem); return; }
                }
                break;
            case IkUpgrade:
            case IkDowngrade:
                if (_templates.ItemGradeProb[Math.Min(item.Level, (byte)49)] == 0 || it.CanGrade == 0 || hasOptionKind)
                { SendUpgradeFail(s, UpgNoGrade); return; }
                if (gt.Kind == IkUpgrade)
                {
                    money = _templates.ItemGradeMoney[Math.Min(item.Level, (byte)49)];   // GetDiscountRate: 0
                    if (!ch.UseMoney(money, commit: false)) { SendUpgradeFail(s, UpgMoney); return; }
                }
                break;
            case IkMagicGrade:
                if (it.CanMagic == 0 || item.Level != 0 || item.Magic.Count != 0) { SendUpgradeFail(s, UpgNoGrade); return; }
                break;
            case IkRareGrade:
                if (it.CanRare == 0 || item.Level != 0 || item.Magic.Count == 0 || item.Magic.Count > 2)
                { SendUpgradeFail(s, UpgNoGrade); return; }
                break;
            case IkClearMagic:
                if (item.Magic.Count == 0) { SendUpgradeFail(s, UpgNoGrade); return; }
                break;
            case IkWrap:
                if (it.CanWrap == 0 || item.Ext[Item.IevWrap] != 0) { SendUpgradeFail(s, UpgWrapping); return; }
                break;
            case IkEld:
                if (item.Ext[Item.IevEld] >= TmaxItemLevelDown || item.EquipLevel == 1) { SendUpgradeFail(s, UpgMaxEld); return; }
                break;
            case IkClearRefine:
                if (item.RefineCur == 0) { SendUpgradeFail(s, UpgNoRefine); return; }
                break;
            case IkChgGradeEffect:
                if (it.Kind is 90 or IkBack) { SendUpgradeFail(s, UpgCannotColor); return; }
                if ((item.Level < MinGradeEffectLevel && it.Type != ItCostume) || it.Kind == IkBack)
                { SendUpgradeFail(s, UpgNoGradeEffect); return; }
                break;
            case IkColor:
                if (it.CanColor == 0 || item.Ext[Item.IevColor] == color) { SendUpgradeFail(s, UpgSameColor); return; }
                break;
            case IkDelEffect:
                if (item.GradeEffect == 0) { SendUpgradeFail(s, UpgSameColor); return; }
                break;
            case IkCloakEffect:
                if (it.Kind != IkBack) { SendUpgradeFail(s, UpgSameColor); return; }
                break;
            case IkAccessoryScroll:
                if (it.Type != ItCostume || item.Magic.Count >= 4) { SendUpgradeFail(s, UpgNoGrade); return; }
                break;
            default:
                SendUpgradeFail(s, UpgNoGrade);
                return;
        }

        if (!UseNpcCallItem(s, ch, nInv, nSlot, () => SendUpgradeFail(s, UpgInvalidPos), () => SendUpgradeFail(s, UpgNpcCallError)))
            return;
        if (money != 0) ch.UseMoney(money, commit: true);

        byte scrollKind = gt.Kind, scrollGrade = gt.Grade;
        ushort scrollUse = gt.UseValue, scrollId = grade.TemplateId;
        UseItemStack(s, gInv, gradeInven, grade, 1);

        switch (scrollKind)
        {
            case IkUpgrade: UpgradeLevel(s, ch, npc, tInv, inven, item); break;
            case IkDowngrade:
                if (item.Level != 0)
                {
                    item.Level = unchecked((byte)(item.Level - scrollGrade));
                    if (item.Level < MinGradeEffectLevel) item.GradeEffect = 0;
                    LinkItemAttr(item);
                    SendUpgradeResult(s, UpgSuccess, tInv, item);
                }
                else SendUpgradeFail(s, UpgFail);
                break;
            case IkGem: GemUpgrade(s, tInv, item, scrollUse, scrollId); break;
            case >= 1 and <= 9 or >= 12 and <= 19:
                item.MoggItemId = scrollId;
                LinkItemAttr(item);
                SendUpgradeResult(s, UpgMoggSuccess, tInv, item);
                break;
            case IkMagicGrade:
            case IkRareGrade:
            {
                ushort magicBuff = ItemProbBuff(ch, SdtStatusMagicProb);
                byte p = CalcProb(s, ch, npc, scrollKind == IkMagicGrade ? Prob.Magic : Prob.RareMagic, scrollGrade);
                bool ok = (byte)(Rand() % 100) < p && MakeSpecialItem(ch, item, scrollKind, magicBuff);
                if (ok) SendItemMagicGrade(s, UpgSuccess, tInv, item);
                else
                {
                    if (scrollKind == IkRareGrade)
                    {
                        inven.Items.Remove(item);
                        SendCS_DELITEM_ACK(s, tInv, item);
                    }
                    SendItemMagicGrade(s, UpgMagicFail, 0, null);
                }
                break;
            }
            case IkClearMagic:
                item.Magic.Clear();
                SendItemMagicGrade(s, UpgMagicClear, tInv, item);
                break;
            case IkWrap:
                item.Ext[Item.IevWrap] = 1;
                SendUpgradeResult(s, UpgWrap, tInv, item, level: (byte)item.Ext[Item.IevWrap]);
                break;
            case IkEld:
            {
                byte eld = (byte)(Rand() % Math.Max(scrollUse, (ushort)1));
                item.Ext[Item.IevEld] += Math.Max(eld, (byte)1);
                if (item.Ext[Item.IevEld] > TmaxItemLevelDown) item.Ext[Item.IevEld] = TmaxItemLevelDown;
                if (it.DefaultLevel <= item.Ext[Item.IevEld]) item.Ext[Item.IevEld] = (uint)(it.DefaultLevel - 1);
                SendUpgradeResult(s, UpgEld, tInv, item, level: (byte)item.Ext[Item.IevEld]);
                break;
            }
            case IkClearRefine:
                item.DuraMax = it.DuraMax;
                item.DuraCur = it.DuraMax;
                item.RefineCur = 0;
                item.Magic.Clear();
                SendCS_CHANGEITEMATTR_ACK(s, tInv, item);
                SendUpgradeResult(s, UpgClearRefine, tInv, item);
                break;
            case IkChgGradeEffect:
                if (scrollId >= EffectChangeId) item.GradeEffect = (byte)(scrollId - EffectChangeId + 1);
                else item.GradeEffect = NextEffect(item.GradeEffect);
                SendUpgradeResult(s, UpgChangeEffect, tInv, item);
                break;
            case IkColor:
                item.Ext[Item.IevColor] = color;
                item.GradeEffect = 0;
                SendUpgradeResult(s, UpgColor, tInv, item, color: color);
                break;
            case IkDelEffect:
                item.GradeEffect = 0;
                SendUpgradeResult(s, UpgUndye, tInv, item, color: color);
                break;
            case IkCloakEffect:
                item.GradeEffect = NextEffect(item.GradeEffect);
                item.Ext[Item.IevColor] = 0;
                SendUpgradeResult(s, UpgColor, tInv, item, color: color);
                break;
            case IkAccessoryScroll:
                if (!AccessoryScroll(item)) return;
                SendItemMagicGrade(s, UpgSuccess, tInv, item);
                break;
        }
        if (inven.Items.Contains(item)) EnqueueItemSave(s, tInv, item);   // the target changed in place
    }

    private static bool IsMoggPair(ItemTemplate it, ItemTemplate gt)
        => (it.SubSlot == EsHead && gt.SubSlot == EsHead) || (it.SubSlot == EsBody && gt.SubSlot == EsBody)
           || (it.SubSlot == EsPants && gt.SubSlot == EsPants) || (it.SubSlot == EsFoot && gt.SubSlot == EsFoot)
           || (it.SubSlot == EsHand && gt.SubSlot == EsHand)
           || (it.Kind == IkMultiVajra && gt.Kind == IkShieldKind) || (it.Kind == IkMBar && gt.Kind == Ik1Hand)
           || (it.Kind == IkAx && gt.Kind == Ik2Hand) || (it.Kind == Ik2Hand && gt.Kind == IkAx);

    /// <summary>A random effect 1..39, never the current one (C++ <c>(cur + 1) % 39 + 1</c> on a repeat).</summary>
    private byte NextEffect(byte current)
    {
        byte e = (byte)(Rand() % (IeCount - 1) + 1);
        return e == current ? (byte)((current + 1) % (IeCount - 1) + 1) : e;
    }

    private void UpgradeLevel(ClientSession s, Character ch, Npc npc, byte tInv, Inven inven, Item item)
    {
        byte roll = (byte)(Rand() % 100);
        byte p = CalcProb(s, ch, npc, Prob.Upgrade, _templates.ItemGradeProb[Math.Min(item.Level, (byte)49)]);
        if (roll < p)
        {
            item.Level = ItemLevelMax;                                  // this build: one success = max level
            if (item.Level >= MinGradeEffectLevel && item.GradeEffect == 0 && item.Template!.Kind != IkBack)
                item.GradeEffect = (byte)(Rand() % (IeCount - 1) + 1);
            LinkItemAttr(item);
            SendUpgradeResult(s, UpgSuccess, tInv, item);
            return;
        }

        if (IsTutorial(ch) || CalcProb(s, ch, npc, Prob.ItemGuard, 0) != 0)
        {
            const byte guard = 11;
            byte downRoll = item.Level <= guard ? (byte)0 : (byte)(Rand() % 100);
            byte down = downRoll < 20 ? (byte)0 : downRoll < 55 ? (byte)1 : (byte)2;
            if (item.Level > guard && item.Level - down < guard) down = (byte)(item.Level - guard);
            if (down != 0)
            {
                item.Level = item.Level > down ? (byte)(item.Level - down) : (byte)0;
                LinkItemAttr(item);
                if (item.Level < MinGradeEffectLevel) item.GradeEffect = 0;
            }
            SendUpgradeResult(s, UpgDowngrade, tInv, item);
            return;
        }

        inven.Items.Remove(item);                                       // the item is destroyed
        SendCS_DELITEM_ACK(s, tInv, item);
        SendUpgradeFail(s, UpgFail);
    }

    /// <summary>C++ <c>ProtectTutorial</c> — <c>IsTutorial() || m_bStartAct == 2</c>. Tutorial maps are not
    /// modelled, so only the start-act half applies.</summary>
    private static bool IsTutorial(Character ch) => ch.StartAct == 2;

    private void GemUpgrade(ClientSession s, byte tInv, Item item, ushort gemProb, ushort scrollId)
    {
        byte roll = (byte)(Rand() % 100 + 1);
        byte real = _templates.GemProbs[Math.Min(item.Gem, (byte)5)];
        byte rate = unchecked((byte)(gemProb * 2 / 100));
        bool success = item.Gem == 2
            ? (roll <= real * rate && rate != 1) || gemProb == GemGuaranteed
            : roll <= real * rate || gemProb == GemGuaranteed;
        if (item.Gem > 4) return;                                       // GEM5: no branch in the C++

        if (success)
        {
            item.Gem++;
            LinkItemAttr(item);
            SendUpgradeResult(s, UpgGemSuccess, tInv, item);
            return;
        }
        if (scrollId != GemKeepItem) item.Gem = 0;
        else if (item.Gem >= 3) item.Gem--;                            // the keep-scroll only steps back from 3 and 4
        LinkItemAttr(item);
        SendUpgradeResult(s, UpgGemFail, tInv, item);
    }

    private bool AccessoryScroll(Item item)
    {
        if (_templates.AccessoryMagic.Count == 0) return false;        // C++ returns before the loop
        for (int attempt = 0; attempt < 256; attempt++)
        {
            var a = _templates.AccessoryMagic[Rand() % _templates.AccessoryMagic.Count];
            if (_templates.Magic(a.MagicId) is not { } pick) continue;
            if (item.Magic.Any(m => m.Id == pick.MagicId)) continue;
            bool retry = false;
            foreach (var cur in item.Magic)
            {
                if (cur.Template is not { } ct) continue;
                if (ct.ExclIndex == pick.ExclIndex && ct.MagicId is not (50 or 51) && pick.MagicId is not (50 or 51))
                { retry = true; break; }
                if ((ct.MagicId == 50 && pick.MagicId == 51) || (ct.MagicId == 51 && pick.MagicId == 50))
                {
                    pick = _templates.Magic(ct.MagicId == 50 ? (byte)51 : (byte)50) ?? pick;
                    break;
                }
            }
            if (retry) continue;

            ushort value = unchecked((ushort)(a.MaxValue == 0 ? 0 : Rand() % a.MaxValue));   // C++: (rand()%Max − Min) + Min
            item.RefineCur++;
            LinkItemAttr(item);
            item.Magic.Add(new MagicOption(pick.MagicId, value, pick));
            return true;
        }
        return false;
    }

    // ============================== options ==============================

    /// <summary>C++ <c>MakeSpecialItem</c> (TMapSvr.cpp:6991) — roll magic (1–2) or rare (up to 5) options.</summary>
    private bool MakeSpecialItem(Character ch, Item item, byte kind, ushort magicBuff)
    {
        if (item.Level != 0) return false;
        if (kind == IkMagicGrade)
        {
            if (item.Magic.Count != 0) return false;
            byte cnt = (byte)Math.Min(Math.Max((byte)(ch.Level * 0.1), (byte)1), (byte)2);
            cnt = (byte)Math.Max(Rand() % cnt + 1, 1);
            for (int i = 0; i < cnt; i++) SetMagicOpt(ch, item, MipMagic);
        }
        else if (kind == IkRareGrade)
        {
            const byte maxMagic = 5;
            int had = item.Magic.Count;
            if (had == 0 || had > 2) return false;
            byte cnt = (byte)Math.Min(Math.Max((byte)(ch.Level * 0.1), (byte)3), maxMagic);
            cnt = (byte)Math.Max(Rand() % cnt + 1, 3);
            cnt = magicBuff != 0 ? (byte)(maxMagic - had) : cnt > had ? (byte)(cnt - had) : (byte)0;
            var added = new List<byte>();
            for (int i = 0; i < cnt; i++)
                if (SetMagicOpt(ch, item, MipRare) is var id and not 0) added.Add(id);
            if (item.Magic.Count == had)
            {
                item.Magic.RemoveAll(m => added.Contains(m.Id));
                return false;
            }
        }
        return item.Magic.Count != 0;
    }

    /// <summary>C++ <c>SetMagicOpt</c> (TMapSvr.cpp:7054) with <c>IMT_SCROLL</c> — pick one eligible option not
    /// already on the item and not sharing an exclusion group, and roll its value.</summary>
    private byte SetMagicOpt(Character ch, Item item, byte optType)
    {
        var t = item.Template!;
        if (t.CanMagic == 0 || !_templates.MagicsByKind.TryGetValue(t.Kind, out var pool) || pool.Count == 0) return 0;
        if (optType == MipRare && t.CanRare == 0) return 0;

        var candidates = pool.Where(m => item.Magic.All(o => o.Id != m.MagicId)
                                          && (optType != MipMagic || m.IsMagic != 0)
                                          && (optType != MipRare || m.IsRare != 0)).ToList();
        MagicTemplate? chosen = null;
        while (candidates.Count > 0)
        {
            int sel = Rand() % candidates.Count;
            var c = candidates[sel];
            if (c.ExclIndex != 0 && item.Magic.Any(o => o.Template?.ExclIndex == c.ExclIndex))
            {
                candidates.RemoveAt(sel);
                continue;
            }
            chosen = c;
            break;
        }
        if (chosen is null) return 0;

        int baseLevel = Math.Min(ItemMagicBaseLevel, (int)ch.Level)
                        - Math.Max(0, 34 - Math.Max(item.EquipLevel, item.GetPowerLevel(_templates)));
        ushort value = MakeItemMagicValue(baseLevel, chosen.RareBound);
        if (value == 0) return 0;
        item.Magic.Add(new MagicOption(chosen.MagicId, value, chosen));
        return chosen.MagicId;
    }

    /// <summary>C++ <c>CTObjBase::MakeItemMagicValue</c> (TObjBase.cpp:4911).</summary>
    private ushort MakeItemMagicValue(int nBase, ushort bound)
    {
        if (bound == 0) return 0;
        int r1 = Rand(), r2 = Rand(), r3 = Rand();
        double rateX = _templates.Rate1st;
        int opLv = Math.Min(80, Math.Max(1, nBase - r1 % 10) + Math.Max(0, r2 % 1000 - 989));
        ushort span = unchecked((ushort)(bound * ((100 - 3) * Math.Pow(rateX, opLv) / Math.Pow(rateX, 80) + 3) / 100));
        return span == 0 ? (ushort)1 : (ushort)(1 + r3 % span);
    }

    // ============================== probability ==============================

    /// <summary>C++ <c>CalcProb</c> (TMapSvr.cpp:9744) — no NPC ⇒ 0; the event bonuses and NPC hero bonuses are 0
    /// here, the item-probability buffs are honoured (and spent).</summary>
    private byte CalcProb(ClientSession s, Character ch, Npc? npc, Prob type, byte baseProb)
    {
        if (npc is null) return 0;
        int w = baseProb;
        switch (type)
        {
            case Prob.Magic:
            case Prob.RareMagic:
                if (ItemProbBuff(ch, SdtStatusMagicProb) is var mb and not 0) { w += baseProb * mb / 100; EraseItemProbBuff(s, ch, SdtStatusMagicProb); }
                break;
            case Prob.Refine:
                if (ItemProbBuff(ch, SdtStatusRefineProb) != 0) { w = 100; EraseItemProbBuff(s, ch, SdtStatusRefineProb); }
                break;
            case Prob.Trans:
                if (ItemProbBuff(ch, SdtStatusTransProb) is var tb and not 0) { w += baseProb * tb / 100; EraseItemProbBuff(s, ch, SdtStatusTransProb); }
                break;
            case Prob.Upgrade:
                if (ItemProbBuff(ch, SdtStatusItemUpgrade) is var ub and not 0) { w += baseProb * ub / 100; EraseItemProbBuff(s, ch, SdtStatusItemUpgrade); }
                break;
            case Prob.ItemGuard:
                w = ItemProbBuff(ch, SdtStatusItemGuard);
                EraseItemProbBuff(s, ch, SdtStatusItemGuard);
                break;
        }
        return (byte)Math.Min(w, 100);
    }

    /// <summary>C++ <c>HaveItemProbBuff</c> (TObjBase.cpp:4789) — the value of the first maintained buff carrying an
    /// <c>SDT_STATUS</c> row of this exec.</summary>
    private ushort ItemProbBuff(Character ch, byte exec)
    {
        foreach (var m in ch.MaintainSkills)
            if (_templates.Skills.TryGetValue(m.SkillId, out var t))
                foreach (var d in t.Data)
                    if (d.Type == SkillTemplate.SdtStatus && d.Exec == exec && d.Value != 0) return d.Value;
        return 0;
    }

    private void EraseItemProbBuff(ClientSession s, Character ch, byte exec)
    {
        for (int i = 0; i < ch.MaintainSkills.Count; i++)
            if (_templates.Skills.TryGetValue(ch.MaintainSkills[i].SkillId, out var t)
                && t.Data.Any(d => d.Type == SkillTemplate.SdtStatus && d.Exec == exec))
            {
                EraseMaintainPlayer(s, ch, i);
                return;
            }
    }

    // ============================== refine ==============================

    private void OnCS_REFINE_REQ(ClientSession s, PacketReader r)
    {
        if (s.State != EnterState.InGame || !s.IsMain || s.Char is not { } ch) return;
        byte needCost = r.ReadByte(), inv = r.ReadByte(), slot = r.ReadByte(), addCount = r.ReadByte();
        ushort npcId = r.ReadUInt16();
        byte nInv = r.ReadByte(), nSlot = r.ReadByte();

        var inven = ch.FindInven(inv);
        if (inven is null || addCount > 3 || addCount == 0 || inv == Proto.InvenEquip)
        { SendRefineAck(s, (byte)ItemRepairResult.NotFound, inv, null); return; }
        var item = inven.Items.FirstOrDefault(i => i.ItemSlot == slot);
        if (item is null || item.Count == 0 || item.Template is not { } it) { SendRefineAck(s, (byte)ItemRepairResult.NotFound, inv, null); return; }
        if (it.CanRepair == 0 || item.GLevel != 0) { SendRefineAck(s, (byte)ItemRepairResult.Disallow, inv, null); return; }
        if (it.RefineMax <= item.RefineCur) { SendRefineAck(s, (byte)ItemRepairResult.MaxRefine, inv, null); return; }

        static byte Bucket(byte g) => g < 34 ? (byte)((g - 1) / 3 * 3 + 1) : (byte)(g / 2 * 2);
        byte gradeLv = Bucket(item.GetPowerLevel(_templates));
        byte sum = 0, refineCount = 0;
        uint sumDura = 0;
        var materials = new List<(byte inv, Inven bag, Item item)>();
        for (int i = 0; i < addCount; i++)
        {
            byte aInv = r.ReadByte(), aSlot = r.ReadByte();
            if (aInv == inv && aSlot == slot) { SendRefineAck(s, (byte)ItemRepairResult.NotFound, aInv, null); return; }
            var bag = ch.FindInven(aInv);
            if (bag is null || aInv == Proto.InvenEquip) { SendRefineAck(s, (byte)ItemRepairResult.NotFound, aInv, null); return; }
            var add = bag.Items.FirstOrDefault(x => x.ItemSlot == aSlot);
            if (add is null || add.Count == 0 || add.Template is not { } at) { SendRefineAck(s, (byte)ItemRepairResult.NotFound, aInv, null); return; }

            byte addLv;
            if (at.Type == ItRefine) { refineCount++; addLv = gradeLv; }
            else if (at.RefineMax == 0) { SendRefineAck(s, (byte)ItemRepairResult.Disallow, inv, null); return; }
            else addLv = Bucket(add.GetPowerLevel(_templates));

            byte gap = Math.Min(gradeLv, addLv);
            if (gradeLv - gap > 25) { SendRefineAck(s, (byte)ItemRepairResult.LevelDiff, aInv, null); return; }
            byte addGrade = unchecked((byte)(20 + gap - gradeLv));
            if (materials.Any(m => m.inv == aInv && m.item.ItemSlot == aSlot)) { SendRefineAck(s, (byte)ItemRepairResult.NotFound, aInv, null); return; }
            sum = unchecked((byte)(sum + addGrade));
            sumDura += it.DuraMax * (uint)(8 + Rand() % 8) / 100;
            materials.Add((aInv, bag, add));
        }

        if (!_templates.RefineCostByLevel.TryGetValue(gradeLv, out uint levelCost))
        { SendRefineAck(s, (byte)ItemRepairResult.NotFound, inv, null); return; }
        uint cost = (uint)Math.Max(1f, levelCost * it.Price);
        var npc = _state.FindNpc(npcId);
        const byte discount = 0;                                        // GetDiscountRate: occupation discounts unported
        uint discounted = cost - cost * discount / 100;
        if (needCost != 0)
        {
            var w = new PacketWriter(Msg.CS_REFINECOST_ACK);
            w.WriteUInt32(cost); w.WriteByte(discount);
            s.Send(w);
            return;
        }
        if (!ch.UseMoney(discounted, commit: false)) { SendRefineAck(s, (byte)ItemRepairResult.NeedMoney, inv, null); return; }
        if (!UseNpcCallItem(s, ch, nInv, nSlot, () => SendRefineAck(s, (byte)ItemRepairResult.InvalidPos, nInv, null),
                () => SendRefineAck(s, (byte)ItemRepairResult.NpcCallError, nInv, null)))
            return;
        ch.UseMoney(discounted, commit: true);
        SendCS_MONEY_ACK(s, ch);

        var carried = new List<MagicOption>();
        foreach (var (mInv, bag, m) in materials)
        {
            foreach (var o in m.Magic)
                if (it.CanMagic != 0 && o.Template is { } ot && ((1u << (it.Kind - 1)) & ot.Kind) != 0
                    && (item.Level == 0 || ot.OptionKind == 0))
                    carried.Add(o);
            bag.Items.Remove(m);
            SendCS_DELITEM_ACK(s, mInv, m);
        }

        byte failRoll = (byte)(Rand() % 100);
        if (failRoll >= CalcProb(s, ch, npc, Prob.Refine, (byte)(20 + sum * 3 / addCount)))
        {
            SendRefineAck(s, (byte)ItemRepairResult.Fail, inv, null);
            return;
        }

        item.DuraMax += sumDura;
        item.DuraCur = item.DuraMax;
        item.RefineCur++;
        byte trans = carried.Count != 0 ? CalcProb(s, ch, npc, Prob.Trans, (byte)(8 + 6 * refineCount / addCount)) : (byte)0;
        for (int k = carried.Count - 1; k >= 0; k--)
        {
            var o = carried[k];
            if (item.Magic.Count >= TmagicMax) continue;
            bool clash = o.Template!.ExclIndex != 0
                         && item.Magic.Any(x => x.Id != o.Id && x.Template?.ExclIndex == o.Template.ExclIndex);
            byte roll = (byte)(Rand() % 100);
            if (clash || roll >= trans) continue;
            int at = item.Magic.FindIndex(x => x.Id == o.Id);
            if (at >= 0) { if (item.Magic[at].Value < o.Value) item.Magic[at] = item.Magic[at] with { Value = o.Value }; }
            else if (o.Value != 0) item.Magic.Add(o);
        }
        SendRefineAck(s, (byte)ItemRepairResult.Success, inv, item);
        EnqueueItemSave(s, inv, item);
    }

    // ============================== item change (gamble box) ==============================

    private void OnCS_ITEMCHANGE_REQ(ClientSession s, PacketReader r)
    {
        if (s.State != EnterState.InGame || !s.IsMain || s.Char is not { } ch) return;
        byte inv = r.ReadByte(), slot = r.ReadByte();
        if (s.Deal.InProgress || s.Store.IsOpen) { SendItemChangeAck(s, ChgStatus, 0, 0); return; }

        var inven = ch.FindInven(inv);
        var item = inven?.Items.FirstOrDefault(i => i.ItemSlot == slot);
        if (inven is null || item is null || item.Count == 0 || item.Template is not { } t) { SendItemChangeAck(s, ChgInvalid, 0, 0); return; }
        if (t.Type != ItUse || t.Kind != IkChange || !item.CanUse) { SendItemChangeAck(s, ChgInvalid, 0, 0); return; }
        if (!_templates.CashGamble.TryGetValue(t.UseValue, out var prizes)
            || _templates.CashGambleTotal.GetValueOrDefault(t.UseValue) is not (> 0 and var total))
        { SendItemChangeAck(s, ChgInvalid, 0, 0); return; }

        uint roll = TRand(total);
        CashGambleRow? won = null;
        uint acc = 0;
        foreach (var p in prizes) { acc += p.Prob; if (acc > roll) { won = p; break; } }

        byte result = ChgSuccess;
        ushort newId = 0; byte newCount = 0;
        if (won is null || won.Item.Count == 0) result = ChgFail;
        else if (ItemFromRow(won.Item) is not { } prize) result = ChgFail;
        else
        {
            prize.DlId = 0;                                             // Copy(…, genId = TRUE)
            prize.Count = (byte)(Rand() % won.Item.Count + 1);
            if (won.UseTime != 0) prize.EndTime = UnixNow() + won.UseTime * 86400L;
            var give = new List<Item> { prize };
            if (!HaveInvenBlank(ch) || !CanPush(ch, give)) result = ChgFull;
            else
            {
                newId = prize.TemplateId; newCount = prize.Count;
                PushTItem(s, give);
            }
        }
        if (result != ChgFull) UseItemStack(s, inv, inven, item, 1);
        SendItemChangeAck(s, result, newId, newCount);
    }

    /// <summary>C++ <c>TRand</c> (TNetDef.h:77) — a uniform draw below <paramref name="max"/> wider than RAND_MAX.</summary>
    private uint TRand(uint max) => max <= 1 ? 0u : (uint)CraftRng.NextInt64(max);

    /// <summary>C++ <c>HaveInvenBlank</c> — a free slot in any bag other than the equipment.</summary>
    private static bool HaveInvenBlank(Character ch)
        => ch.Invens.Any(b => b.InvenId != Proto.InvenEquip && b.GetBlankPos() != Proto.InvalidSlot);

    // ============================== shared ==============================

    /// <summary>The NPC-call item (C++ upgrade / refine tail): an optional <c>IK_NPCCALL</c> item that stands in
    /// for the NPC, usable only on maps 0 and 8, spent here. Returns false after sending the error.</summary>
    private bool UseNpcCallItem(ClientSession s, Character ch, byte inv, byte slot, Action invalidPos, Action callError)
    {
        if (inv == InvenNull || slot == Proto.InvalidSlot) return true;
        if (ch.MapId != 0 && ch.MapId != 8) { invalidPos(); return false; }
        var bag = ch.FindInven(inv);
        var call = bag?.Items.FirstOrDefault(i => i.ItemSlot == slot);
        if (bag is null || call is null || call.Template?.Kind != IkNpcCall || call.Count == 0) { callError(); return false; }
        UseItemStack(s, inv, bag, call, 1);
        return true;
    }

    /// <summary>C++ <c>UseItem(player, inven, item, n)</c> (TMapSvr.cpp:9649) — spend n from one stack.</summary>
    private void UseItemStack(ClientSession s, byte inv, Inven bag, Item item, byte n)
    {
        item.Count = (byte)Math.Max(0, item.Count - n);
        if (item.Count == 0)
        {
            bag.Items.Remove(item);
            SendCS_DELITEM_ACK(s, inv, item);
        }
        else SendCS_UPDATEITEM_ACK(s, inv, item);
    }

    private static void SendUpgradeFail(ClientSession s, byte result) => SendUpgradeAck(s, result, 0, 0, 0, 0, 0, 0, 0);

    private static void SendUpgradeResult(ClientSession s, byte result, byte inv, Item item, byte? level = null, ushort color = 0)
        => SendUpgradeAck(s, result, inv, item.ItemSlot, level ?? item.Level, item.Gem, item.GradeEffect, color, item.MoggItemId);

    /// <summary>C++ <c>SendCS_ITEMUPGRADE_ACK</c> (CSSender.cpp:3349).</summary>
    private static void SendUpgradeAck(ClientSession s, byte result, byte inv, byte slot, byte level, byte gem,
        byte effect, ushort color, ushort mogg)
    {
        var w = new PacketWriter(Msg.CS_ITEMUPGRADE_ACK);
        w.WriteByte(result); w.WriteByte(inv); w.WriteByte(slot); w.WriteByte(level); w.WriteByte(gem);
        w.WriteByte(effect); w.WriteUInt16(color); w.WriteUInt16(mogg);
        s.Send(w);
    }

    /// <summary>C++ <c>SendCS_ITEMMAGICGRADE_ACK</c> (CSSender.cpp:3367) — the options in id order, each with its
    /// displayed value.</summary>
    private static void SendItemMagicGrade(ClientSession s, byte result, byte inv, Item? item)
    {
        var w = new PacketWriter(Msg.CS_ITEMMAGICGRADE_ACK);
        w.WriteByte(result); w.WriteByte(inv); w.WriteByte(item?.ItemSlot ?? 0); w.WriteByte((byte)(item?.Magic.Count ?? 0));
        if (item is not null)
            foreach (var m in item.Magic.OrderBy(x => x.Id)) { w.WriteByte(m.Id); w.WriteUInt16(item.DisplayValue(m)); }
        s.Send(w);
    }

    private static void SendCS_CHANGEITEMATTR_ACK(ClientSession s, byte inv, Item item)
    {
        var w = new PacketWriter(Msg.CS_CHANGEITEMATTR_ACK);
        w.WriteByte(inv);
        item.WrapPacketClient(w, s.CharId);
        s.Send(w);
    }

    private static void SendRefineAck(ClientSession s, byte result, byte inv, Item? item)
    {
        var w = new PacketWriter(Msg.CS_REFINE_ACK);
        w.WriteByte(result); w.WriteByte(inv);
        item?.WrapPacketClient(w, s.CharId);
        s.Send(w);
    }

    private static void SendItemChangeAck(ClientSession s, byte result, ushort newId, byte newCount)
    {
        var w = new PacketWriter(Msg.CS_ITEMCHANGE_ACK);
        w.WriteByte(result); w.WriteUInt16(newId); w.WriteByte(newCount);
        s.Send(w);
    }
}
