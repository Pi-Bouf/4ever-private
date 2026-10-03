using TMap.Data;
using TMap.Protocol;

namespace TMap.Server.Map;

/// <summary>
/// The rest of C++ <c>OnCS_ITEMUSE_REQ</c> (CSHandler.cpp:9092): the money pouches, special boxes, exp books, the premium and
/// exp-boost items, and the action items.
/// <list type="bullet">
/// <item><b>A money pouch</b> (<c>IK_MONEY</c>) gives a random sum (<c>CTItem::RandMoney</c>) and shows it (<c>CS_OPENMONEY_ACK</c>).</item>
/// <item><b>A special box</b> (<c>IK_SPECIALBOX</c>) gives the items of its group (<c>TSPECIALBOXCHART</c>) meant for the player's
/// class, when they all fit.</item>
/// <item><b>An exp book</b> (<c>IK_GAINEXP</c>) gives its <c>dwSpeedInc</c> in exp.</item>
/// <item><b>Premium</b> (<c>IK_GOLDPREMIUM</c> / <c>2</c>, account-wide) and <b>exp boosts</b> (<c>IK_EXPBONUS</c>) are "during"
/// items (C++ <c>m_diPremium</c> / <c>m_diExp</c>): they run for their use time, count down while played, are saved at logout
/// and come back at login. The premium gives its own buff and +20% hunting exp for 4 hours of play a day (the pc-bang time);
/// an exp boost gives its <c>wUseValue</c>%. Both show as the exp buff (<c>TPCBANG_SKILL</c> 903). A gain-exp buff
/// (<c>SDT_STATUS_GAINEXP</c>) adds its value too.</item>
/// <item><b>An action item</b> (<c>IT_ACTITEMS</c>) plays its skill's animation around, and nothing else.</item>
/// </list>
/// <para><b>Not ported:</b> cash items (<c>IK_CASH</c> — the bonus goes to a cash DB this setup has no link to), the Korean premium
/// pet, a real pc-bang (<c>PCBANG_REAL</c>) and its magic-drop bonus.</para>
/// </summary>
public sealed partial class MapService
{
    // TITEM_KIND (NetCode.h:1182) / TITEM_TYPE
    private const byte IkAp = 28, IkGoldPremium = 40, IkMoney = 51, IkGoldPremium2 = 52, IkExpBonus = 56, IkGainExp = 81,
        IkSpecialBox = 115, ItActItems = 25;
    private const byte DuringTypeUse = 0x40, TclassCount = 6, SdtStatusGainExp = 69;
    private const ushort TpcbangSkill = 903;
    private const byte IkCash = 86, TtUseItem = 10;   // TRIGGER_TYPE TT_USEITEM
    private const uint PcbangTime = 4 * 60 * 60;
    private const byte PcbangReal = 1, PcbangPremium1 = 2, PcbangPremium2 = 4;

    // ================================ the use ================================

    /// <summary>The <c>IK_MONEY</c> case: C++ <c>CTItem::RandMoney</c> (TItem.cpp:648) — up to five rolls of
    /// <c>price·1000·r²</c>, going on only while r ≥ 0.95, and never less than <c>wUseValue·1000</c>.</summary>
    private void OpenMoneyPouch(ClientSession s, Character ch, ItemTemplate t)
    {
        uint money = 0;
        for (int i = 0; i < 5; i++)
        {
            float add = LootRng.Next(100) / 100.0f;
            money += (uint)(t.Price * 1000 * add * add);
            if (add < 0.95f) break;
        }
        money = Math.Max((uint)t.UseValue * 1000, money);
        ch.EarnMoney(money);
        SendCS_MONEY_ACK(s, ch);
        var w = new PacketWriter(Msg.CS_OPENMONEY_ACK);
        w.WriteUInt32(money);
        s.Send(w);
    }

    /// <summary>The <c>IK_SPECIALBOX</c> case: the group's items for the player's class (each its own copy, lasting its days), all
    /// pushed into the bags — or nothing when the group is unknown or they don't fit.</summary>
    private bool OpenSpecialBox(ClientSession s, Character ch, ItemTemplate t)
    {
        if (!_templates.SpecialBoxes.TryGetValue(t.UseValue, out var rows)) return false;
        var give = new List<Item>();
        foreach (var row in rows)
        {
            if (row.Class != ch.Class && row.Class != TclassCount) continue;
            if (ItemFromRow(row.Item) is not { } item) continue;
            item.DlId = 0;                                                    // Copy(…, genId = TRUE)
            if (row.UseTime != 0) item.EndTime = UnixNow() + row.UseTime * (long)DayOne;
            give.Add(item);
        }
        if (!HaveInvenBlank(ch) || !CanPush(ch, give)) return false;
        PushTItem(s, give);
        return true;
    }

    /// <summary>The <c>IT_ACTITEMS</c> type: its skill's action shown around (<c>CS_ACTION_ACK</c>), nothing used up, no answer.</summary>
    private void PlayActItem(ClientSession s, Character ch, ItemTemplate t)
    {
        if (t.UseValue == 0) return;
        var ack = BuildCS_ACTION_ACK(SkillUseResult.Success, ch.CharId, OtPc, 0xFF, 0xFFFFFFFF, 0xFFFFFFFF, t.UseValue);
        foreach (var p in _state.NearView(s)) p.Send(ack);
    }

    /// <summary>After a used item whose <c>bUseType</c> has <c>DURINGTYPE_USE</c>: it runs for its hours or days. A premium also
    /// becomes the pc-bang flag and hangs its buffs; anything else hangs the exp buff.</summary>
    private void StartDuringItem(ClientSession s, Character ch, ItemTemplate t)
    {
        if ((t.UseType & DuringTypeUse) == 0) return;
        byte type = DuringTypeTime;
        uint add = 0;
        if ((t.UseType & DuringTypeTime) != 0) add = t.UseTime * HourOne;
        else if ((t.UseType & DuringTypeDay) != 0) { type = DuringTypeDay; add = t.UseTime * DayOne; }
        SetDuringItem(ch, t.Kind, t, type, add);

        byte pcbang = t.Kind == IkGoldPremium ? PcbangPremium1 : t.Kind == IkGoldPremium2 ? PcbangPremium2 : (byte)0;
        if (pcbang != 0)
        {
            ResetPcBangData(s, ch, pcbang, add);
            HangPremiumItem(s, ch);
        }
        else HangExpBuff(s, ch);
    }

    // ================================ the state ================================

    /// <summary>C++ <c>CTPlayer::SetDuringItem</c>: no time ⇒ none.</summary>
    private void SetDuringItem(Character ch, byte kind, ItemTemplate? t, byte type, uint add)
    {
        var d = add == 0 || t is null ? null : new DuringItem { Item = t, Type = type, RemainTime = add, EndTime = UnixNow() + add };
        if (kind is IkGoldPremium or IkGoldPremium2) ch.Premium = d;
        else if (kind == IkExpBonus) ch.ExpItem = d;
    }

    /// <summary>C++ <c>CTPlayer::IsExpBenefit</c> (TPlayer.cpp:4395): the pc-bang / premium bonus while its daily 4 hours last
    /// (20%), else the exp boost's <c>wUseValue</c>%. Returns which (the item kind), or 0.</summary>
    public static byte IsExpBenefit(Character ch, out ushort bonus)
    {
        bonus = 0;
        if (ch.InPcBang != 0 && ch.PcBangTime < PcbangTime) { bonus = 20; return IkGoldPremium; }
        if (ch.ExpItem is { } e) { bonus = e.Item.UseValue; return IkExpBonus; }
        return 0;
    }

    /// <summary>C++ <c>CTObjBase::GetGainExpBuff</c>: the first <c>SDT_STATUS_GAINEXP</c> value among the buffs on.</summary>
    private static ushort GainExpBuff(Character ch)
    {
        foreach (var m in ch.MaintainSkills)
            if (m.Template is { } t)
                foreach (var d in t.Data)
                    if (d.Type == SkillTemplate.SdtStatus && d.Exec == SdtStatusGainExp) return d.Value;
        return 0;
    }

    /// <summary>C++ <c>CTMonster::OnDie</c>'s exp bonus (TMonster.cpp:663/738): the benefit plus the gain-exp buff, in percent.</summary>
    public static uint WithExpBonus(Character ch, uint exp)
    {
        IsExpBenefit(ch, out ushort bonus);
        bonus = (ushort)(bonus + GainExpBuff(ch));
        return bonus != 0 ? unchecked(exp + exp * bonus / 100) : exp;
    }

    /// <summary>C++ <c>CTPlayer::HangExpBuff</c> (TPlayer.cpp:376): the exp buff for as long as the benefit lasts.</summary>
    private void HangExpBuff(ClientSession s, Character ch)
    {
        byte exp = IsExpBenefit(ch, out _);
        if (exp == IkGoldPremium)
        {
            uint remain = 0xFFFFFFFF;
            if (ch.Premium is { } p) remain = Math.Min(p.RemainTime, DayOne * 31) * 1000;
            ForceMaintain(s, ch, TpcbangSkill, ch.CharId, OtPc, ch.CharId, OtPc, Math.Min(remain, (PcbangTime - ch.PcBangTime) * 1000));
        }
        else if (exp == IkExpBonus)
            ForceMaintain(s, ch, TpcbangSkill, ch.CharId, OtPc, ch.CharId, OtPc, ch.ExpItem!.RemainTime * 1000);
    }

    /// <summary>C++ <c>CTPlayer::HangPremiumItem</c> (TPlayer.cpp:252): the exp buff, then the premium's own buff.</summary>
    private bool HangPremiumItem(ClientSession s, Character ch)
    {
        HangExpBuff(s, ch);
        if ((ch.InPcBang & (PcbangPremium1 | PcbangPremium2)) == 0 || ch.Premium is not { } p) return false;
        ForceMaintain(s, ch, p.Item.UseValue, ch.CharId, OtPc, ch.CharId, OtPc, Math.Min(p.RemainTime, DayOne * 31) * 1000);
        return true;
    }

    /// <summary>C++ <c>CTPlayer::ResetPcBangData</c> (TPlayer.cpp:341): the new pc-bang flags (a real pc-bang kept); unless only
    /// that, the day's bonus time restarts so that <paramref name="remain"/> seconds of it are left (at most), and the exp buff
    /// comes off. Those around are told when the flags change.</summary>
    private void ResetPcBangData(ClientSession s, Character ch, byte inPcBang, uint remain)
    {
        byte prev = ch.InPcBang;
        ch.InPcBang = (byte)((ch.InPcBang & PcbangReal) | inPcBang);
        if (inPcBang != PcbangReal)
        {
            ch.PcBangTime = Math.Min(PcbangTime < remain ? 0 : PcbangTime - remain, ch.PcBangTime);
            ch.PcBangItemCnt = 0;
            EraseMaintainById(s, ch, TpcbangSkill);
        }
        if (ch.InPcBang != prev) BroadcastResetPcBang(s, ch);
    }

    private void BroadcastResetPcBang(ClientSession s, Character ch)
    {
        var w = new PacketWriter(Msg.CS_RESETPCBANG_ACK);
        w.WriteUInt32(ch.CharId);
        w.WriteByte(ch.InPcBang);
        var ack = w.ToArray();
        foreach (var p in _state.InView(s)) p.Send(ack);
    }

    /// <summary>C++ <c>CTObjBase::EraseMaintainSkill(WORD)</c> (TObjBase.cpp:2575): every buff of that id off; the exp buff going
    /// re-hangs whatever benefit is left.</summary>
    private void EraseMaintainById(ClientSession s, Character ch, ushort id)
    {
        if (id == 0) return;
        for (int i = 0; i < ch.MaintainSkills.Count;)
            if (ch.MaintainSkills[i].SkillId == id) EraseMaintainPlayer(s, ch, i);
            else i++;
        if (id == TpcbangSkill) HangExpBuff(s, ch);
    }

    // ================================ the clock ================================

    /// <summary>C++ <c>CTPlayer::OnTimer</c>'s pc-bang day (TPlayer.cpp:966) and <c>CheckDuringItem</c> (TPlayer.cpp:275), each
    /// second: a new day restarts the bonus time, else it counts the time played; the running items count down (by at most 10
    /// s a step) and end when their time is out.</summary>
    private void RunDuringItems()
    {
        long now = UnixNow();
        foreach (var s in _state.AllInGame())
        {
            if (!s.IsMain || s.Char is not { } ch) continue;
            long last = ch.DuringTick == 0 ? now : ch.DuringTick;
            ch.DuringTick = now;
            uint elapsed = (uint)Math.Max(0, now - last);
            if (ch.InPcBang != 0)
            {
                if (LocalDay(now) != LocalDay(last)) { ch.PcBangTime = 0; ch.PcBangItemCnt = 0; HangPremiumItem(s, ch); }
                else ch.PcBangTime += elapsed;
            }
            CheckDuringItem(s, ch, now, Math.Min(elapsed, 10u));
        }
    }

    private static int LocalDay(long unix) => int.Parse(DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime().ToString("yyyyMMdd"));

    private void CheckDuringItem(ClientSession s, Character ch, long now, uint pass)
    {
        if (ch.Premium is { } p)
        {
            if (p.IsOver(now))
            {
                ushort skill = p.Item.UseValue;
                ch.Premium = null;
                EraseMaintainById(s, ch, skill);
                ResetPcBangData(s, ch, (byte)(ch.InPcBang & PcbangReal), unchecked(PcbangTime < ch.PcBangTime ? 0 : ch.PcBangTime - PcbangTime));
                HangExpBuff(s, ch);
            }
            else p.RemainTime = p.RemainTime > pass ? p.RemainTime - pass : 0;
        }
        if (IsExpBenefit(ch, out _) == IkExpBonus && ch.ExpItem is { } e)
        {
            if (e.IsOver(now)) { ch.ExpItem = null; EraseMaintainById(s, ch, TpcbangSkill); }
            else e.RemainTime = e.RemainTime > pass ? e.RemainTime - pass : 0;
        }
    }

    // ================================ login / save ================================

    /// <summary>C++ <c>OnDM_LOADCHAR_ACK</c>'s during items (SSHandler.cpp:5180): each comes back with the time it had (a
    /// by-day one with the days to its end).</summary>
    public void LoadDuringItems(Character ch, DuringLoad load)
    {
        ch.InPcBang = load.InPcBang;
        ch.PcBangTime = load.PcBangTime;
        ch.PcBangItemCnt = load.PcBangItemCnt;
        long now = UnixNow();
        foreach (var row in load.Premium.Concat(load.Exp))
        {
            if (_templates.Item(row.ItemId) is not { } t) continue;
            byte type = DuringTypeTime;
            uint add = 0;
            if ((t.UseType & DuringTypeTime) != 0) add = row.Remain;
            else if ((t.UseType & DuringTypeDay) != 0) { type = DuringTypeDay; add = (uint)Math.Max(0, row.EndTime - now); }
            SetDuringItem(ch, t.Kind, t, type, add);
            if (t.Kind is IkGoldPremium or IkGoldPremium2 && ch.Premium is { } p) p.EndTime = row.EndTime;
            else if (t.Kind == IkExpBonus && ch.ExpItem is { } e) e.EndTime = row.EndTime;
        }
    }

    /// <summary>C++ <c>InitMap</c> (TMapSvr.cpp:8279): no benefit ⇒ the saved exp buff comes off; the premium hangs its buffs and
    /// is shown around.</summary>
    private void DuringItemsAtLogin(ClientSession s, Character ch)
    {
        if (!s.IsMain) return;
        ch.DuringTick = UnixNow();
        if (IsExpBenefit(ch, out _) == 0)
        {
            int i = ch.MaintainSkills.FindIndex(m => m.SkillId == TpcbangSkill);
            if (i >= 0) EraseMaintainPlayer(s, ch, i);
        }
        if (HangPremiumItem(s, ch)) BroadcastResetPcBang(s, ch);
    }

    /// <summary>C++ <c>SendDM_SAVECHAR_REQ</c>'s during items (SSSender.cpp:1157): those still running, and the pc-bang time.</summary>
    public CharSaveData WithDuringItems(CharSaveData d, Character ch, long now)
    {
        static DuringItemRow? Row(DuringItem? x, long now) => x is null || x.IsOver(now) ? null
            : new DuringItemRow(x.Item.ItemId, x.Type, x.RemainTime, x.EndTime);
        return d with
        {
            Premium = Row(ch.Premium, now), ExpItem = Row(ch.ExpItem, now), SaveDuring = true,
            PcBangTime = ch.PcBangTime, PcBangItemCnt = ch.PcBangItemCnt,
        };
    }
}

/// <summary>A running premium or exp-boost item (C++ <c>TDURINGITEM</c>).</summary>
public sealed class DuringItem
{
    public required ItemTemplate Item { get; init; }
    /// <summary>C++ <c>DURINGTYPE_TIME</c> (counts down while played) or <c>DURINGTYPE_DAY</c> (ends on a date).</summary>
    public byte Type { get; set; }
    public uint RemainTime { get; set; }
    public long EndTime { get; set; }

    public bool IsOver(long now) => (Type == 0x02 && EndTime <= now) || (Type == 0x01 && RemainTime == 0);
}
