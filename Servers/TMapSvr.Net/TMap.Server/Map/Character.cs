using TMap.Protocol;

namespace TMap.Server.Map;

/// <summary>
/// The server-side character state the map needs to drive the client-enter handshake, the view
/// (CS_ENTER/LEAVE) broadcast and movement. A faithful-but-minimal port of the relevant <c>CTPlayer</c> /
/// <c>CTObjBase</c> fields — the deep combat/stat/skill/inventory model is deferred (see PORT_STATUS.md).
/// </summary>
public sealed class Character
{
    public uint CharId { get; set; }
    public string Name { get; set; } = "";

    // Appearance / identity
    public byte Class { get; set; }
    public byte Race { get; set; }
    public byte Country { get; set; }
    public byte AidCountry { get; set; }
    public byte Sex { get; set; }
    public byte Hair { get; set; }
    public byte Face { get; set; }
    public byte Body { get; set; }
    public byte Pants { get; set; }
    public byte Hand { get; set; }
    public byte Foot { get; set; }
    public byte HelmetHide { get; set; }
    public byte Level { get; set; } = 1;

    // Party / guild / tactics
    public ushort PartyId { get; set; }
    public uint PartyChiefId { get; set; }
    public ushort CommanderId { get; set; }
    /// <summary>C++ <c>m_bPartyType</c> (PARTY_TYPE / loot mode) — replicated from the world (enter + MW pushes).
    /// <c>PT_SOLO</c> opts the member out of all party sharing (the accessors below then report no party). Phase 38.</summary>
    public byte PartyType { get; set; }
    public uint GuildId { get; set; }

    // ---- PT_SOLO-masked party accessors (C++ CTObjBase::GetPartyID / CTPlayer::GetPartyChiefID / GetCommanderID) ----
    private const byte PtSolo = 1;   // PARTY_TYPE PT_SOLO (NetCode.h:1960)
    /// <summary>C++ <c>GetPartyID</c> — the effective party id, or 0 when solo (opts out of exp/loot sharing).</summary>
    public ushort GetPartyId() => PartyType == PtSolo ? (ushort)0 : PartyId;
    /// <summary>C++ <c>GetPartyChiefID</c> — the party chief id, masked to 0 when solo.</summary>
    public uint GetPartyChiefId() => PartyType == PtSolo ? 0u : PartyChiefId;
    /// <summary>C++ <c>GetCommanderID</c> — the squad/corps commander id, masked to 0 when solo.</summary>
    public ushort GetCommanderId() => PartyType == PtSolo ? (ushort)0 : CommanderId;
    public uint Fame { get; set; }
    public uint FameColor { get; set; }
    public byte GuildDuty { get; set; }
    public byte GuildPeer { get; set; }
    public string GuildName { get; set; } = "";
    public uint TacticsId { get; set; }
    public string TacticsName { get; set; } = "";

    // Money / exp / vitals — money is three DWORD tiers combined via CalcMoney (base 1000).
    public uint Gold { get; set; }
    public uint Silver { get; set; }
    public uint Cooper { get; set; }
    public uint PrevExp { get; set; }
    public uint NextExp { get; set; }
    public uint Exp { get; set; }
    public uint MaxHp { get; set; } = 1;
    public uint Hp { get; set; } = 1;
    public uint MaxMp { get; set; }
    public uint Mp { get; set; }

    // Position / motion
    public uint RegionId { get; set; }
    public ushort MapId { get; set; }
    public float PosX { get; set; }
    public float PosY { get; set; }
    public float PosZ { get; set; }
    public ushort Dir { get; set; }
    public ushort Pitch { get; set; }
    public byte Action { get; set; }
    public byte Block { get; set; }
    public byte Mode { get; set; }
    public byte MouseDir { get; set; } = Monster.TkdirN;   // C++ CTObjBase: TKDIR_N, not 0 (TKDIR_LF)
    public byte KeyDir { get; set; } = Monster.TkdirN;

    // ---- Phase 45: monster host-acquisition inputs (C++ CTPlayer, read by CTAICmdSetHost). ----
    /// <summary>C++ <c>m_bCanHost</c> (TPlayer.h:109) — whether this player is eligible to become a monster's
    /// host/target. Sticky: set the first time the player moves (C++ CSHandler.cpp:555; here on any MOVE — the
    /// C++ "not alone in a cell" guard is a harmless simplification since acquisition only reads it when a
    /// monster is already nearby). <c>CanHost</c>-the-method's ghost branch is deferred (the port doesn't model
    /// ghost); a dead player is excluded by the acquisition scan's HP guard instead.</summary>
    public bool CanHost { get; set; }
    /// <summary>C++ <c>m_dwMoveTick</c> (TPlayer.h:121) — the map-clock (ms) of this player's last MOVE packet
    /// (C++ CSHandler.cpp:517). Feeds <c>SetHost</c>'s "moved within the last 3000 ms" recency filter.</summary>
    public uint LastMoveMs { get; set; }

    // PvP points
    public uint PvpTotalPoint { get; set; }
    public uint PvpUseablePoint { get; set; }

    /// <summary>Unspent skill points (C++ <c>m_wSkillPoint</c>), granted on level-up. Not yet consumed
    /// (skill-buy is unported) — bookkeeping only.</summary>
    public uint SkillPoint { get; set; }

    // ---- Phase 16: HP/MP regen (Recover) state. Ticks are absolute deadlines vs the map ms clock. ----
    /// <summary>C++ <c>m_dwRecoverHPTick</c> — the anchor the next HP regen is measured from.</summary>
    public uint RecoverHpTick { get; set; }
    /// <summary>C++ <c>m_dwRecoverMPTick</c>.</summary>
    public uint RecoverMpTick { get; set; }
    /// <summary>C++ <c>m_dwLastAtkTick</c> — set on every combat action; drives the battle→normal timeout.</summary>
    public uint LastAtkTick { get; set; }

    /// <summary>C++ <c>ChgMode(MT_BATTLE)</c> + <c>m_dwLastAtkTick = now</c> combat entry (TObjBase.cpp:310):
    /// entering battle pushes both recover anchors to <c>now + RECOVER_INIT</c> (only on the NORMAL→BATTLE
    /// transition), suppressing HP regen; every hit refreshes <see cref="LastAtkTick"/>.</summary>
    public void EnterBattle(uint now, uint recoverInit)
    {
        if (Mode != 1) // MT_BATTLE
        {
            Mode = 1;
            RecoverHpTick = now + recoverInit;
            RecoverMpTick = now + recoverInit;
        }
        LastAtkTick = now;
    }

    /// <summary>0 = no intro cinematic needed, 1 = play intro (CS_CHARINFO_ACK bStartAct).</summary>
    public byte StartAct { get; set; }

    // ---- Phase 25: persistence ----

    /// <summary>True only when this character was loaded from a real <c>TCHARTABLE</c> row (not synthesized
    /// DB-free). The save path saves only DB-loaded chars — a synthesized char is never written back.</summary>
    public bool DbLoaded { get; set; }

    /// <summary>The map-clock ms of the last save (C++ <c>m_dwSaveTick</c>) — drives the periodic-save throttle.</summary>
    public uint LastSaveMs { get; set; }

    /// <summary>The <c>TSaveChar</c> value columns the map doesn't otherwise model but must round-trip so the
    /// full-row save doesn't zero them (loaded from <c>TCHARTABLE</c>, written back unchanged).</summary>
    public CharPersistExtras Persist { get; } = new();

    // ---- the death penalty (C++ m_aftermath, TMapType.h:1296) ----
    // The step (0..100) is Persist.Aftermath — it is what the DB stores. The rest derives from it.

    /// <summary>C++ <c>m_aftermath.m_dwTick</c> — when the next one-step recovery is due (map ms clock).</summary>
    public uint AftermathTick { get; set; }

    // ---- mail (C++ m_pPost / m_dwPostID / m_wPostTotal / m_wPostRead) ----

    /// <summary>The mail being read — the one take-item, delete and return act on.</summary>
    public Post? OpenPost { get; set; }

    /// <summary>A mail read still in flight (C++ <c>m_dwPostID</c>); 0 when none.</summary>
    public uint PostPending { get; set; }

    public ushort PostTotal { get; set; }
    public ushort PostNotRead { get; set; }

    /// <summary>C++ <c>CalcMoney(gold, silver, cooper)</c> for an arbitrary amount.</summary>
    public long MoneyOf(uint gold, uint silver, uint cooper)
        => cooper + (long)silver * MoneyMultiply + (long)gold * MoneyMultiply * MoneyMultiply;

    /// <summary>C++ <c>m_aftermath.m_fStatDec</c> — the percentage every primary stat loses (<c>step · 0.3</c>).</summary>
    public float AftermathStatDec => (float)(Persist.Aftermath * 0.3);

    // ---- Phase-2: loaded inventory / gear / skills / hotkeys ----

    /// <summary>Inventory containers keyed by container id (0xFF backpack, 0xFE equipped, timed bags…).</summary>
    public List<Inven> Invens { get; } = new();

    /// <summary>Learned skills (CS_CHARINFO_ACK skill sub-loop).</summary>
    public List<Skill> Skills { get; } = new();

    /// <summary>Active maintained/buff skills (CS_CHARINFO_ACK / CS_ENTER_ACK maintain sub-loop).</summary>
    public List<MaintainSkill> MaintainSkills { get; } = new();

    /// <summary>Hotkey pages (CS_CHARINFO_ACK hotkey sub-loop).</summary>
    public List<HotkeyPage> HotkeyPages { get; } = new();

    // ---- Phase 24: quests (C++ CTPlayer m_mapQUEST / m_mapLevelQuest) ----

    /// <summary>Active/accepted quests keyed by quest id (C++ <c>m_mapQUEST</c>). In-memory only — quest
    /// progress is not persisted (the <c>DM_*</c> save procs are deferred).</summary>
    public Dictionary<uint, QuestProgress> Quests { get; } = new();

    /// <summary>Level-gated quest bookkeeping (C++ <c>m_mapLevelQuest</c>, keyed by the quest's SAMELEVEL
    /// condition value → quest id) — one level-quest at a time.</summary>
    public Dictionary<uint, uint> LevelQuest { get; } = new();

    /// <summary>C++ <c>CTPlayer::FindQuest</c> — the quest progress for an id, or null.</summary>
    public QuestProgress? FindQuest(uint questId) => Quests.GetValueOrDefault(questId);

    /// <summary>C++ <c>CTPlayer::IsRunningQuest</c> — accepted and not yet completed this cycle.</summary>
    public bool IsRunningQuest(uint questId) => FindQuest(questId) is { IsRunning: true };

    /// <summary>The equipped-gear container (id 0xFE), or null if none loaded — used for the CS_ENTER_ACK block.</summary>
    public Inven? Equipped => Invens.FirstOrDefault(i => i.InvenId == 0xFE);

    /// <summary>Finds an inventory container by its id (C++ <c>CTObjBase::FindTInven</c>), or null.</summary>
    public Inven? FindInven(byte invenId) => Invens.FirstOrDefault(i => i.InvenId == invenId);

    /// <summary>The player's cabinets (item warehouse) — C++ <c>CTPlayer::m_mapCabinet</c>. Created lazily on
    /// open (or on the enter-time cabinet load). Phase 37.</summary>
    public List<Cabinet> Cabinets { get; } = new();

    /// <summary>C++ <c>CTPlayer::GetCabinet</c> — the cabinet with this id, or null.</summary>
    public Cabinet? FindCabinet(byte cabinetId) => Cabinets.FirstOrDefault(c => c.CabinetId == cabinetId);

    /// <summary>C++ <c>CTPlayer::PutinCabinet</c> — the cabinet with this id, created (added to
    /// <see cref="Cabinets"/>) if absent. Used by open and the enter-time load.</summary>
    public Cabinet GetOrCreateCabinet(byte cabinetId)
    {
        if (FindCabinet(cabinetId) is { } c) return c;
        var made = new Cabinet { CabinetId = cabinetId };
        Cabinets.Add(made);
        return made;
    }

    // ---- Money (C++ CalcMoney / CTPlayer::UseMoney / EarnMoney) ----

    /// <summary>C++ <c>MONEY_MULTIPLY</c> — the gold/silver/copper tier base.</summary>
    public const uint MoneyMultiply = 1000;

    /// <summary>The combined balance (C++ <c>CalcMoney(gold, silver, cooper)</c>):
    /// <c>cooper + silver·1000 + gold·1000²</c>.</summary>
    public long MoneyTotal => Cooper + (long)Silver * MoneyMultiply + (long)Gold * MoneyMultiply * MoneyMultiply;

    /// <summary>Splits a combined balance back into the three tiers (C++ <c>CalcMoney(total, &amp;gold, &amp;silver, &amp;cooper)</c>).</summary>
    public void SetMoneyTotal(long total)
    {
        Cooper = (uint)(total % MoneyMultiply);
        Silver = (uint)(total / MoneyMultiply % MoneyMultiply);
        Gold = (uint)(total / MoneyMultiply / MoneyMultiply);
    }

    /// <summary>C++ <c>CTPlayer::UseMoney</c> — with <paramref name="commit"/> false it only checks
    /// affordability (returns false if the balance is short); true also deducts. A zero cost always passes.
    /// (The C++ GOLD_TITLE side effect is deferred — titles unported.)</summary>
    public bool UseMoney(long cost, bool commit)
    {
        if (cost == 0) return true;
        long mine = MoneyTotal;
        if (mine < cost) return false;
        if (commit) SetMoneyTotal(mine - cost);
        return true;
    }

    /// <summary>C++ <c>CTPlayer::EarnMoney</c> — adds to the balance (returns false for a zero amount, as the
    /// C++ does).</summary>
    public bool EarnMoney(long amount)
    {
        if (amount == 0) return false;
        SetMoneyTotal(amount + MoneyTotal);
        return true;
    }

    /// <summary>The push-target containers in the C++ search order used by <c>CanPush</c>/<c>PushTItem</c>:
    /// <c>INVEN_DEFAULT</c> first, then the other non-equip bags (id-ordered). The equipped container is
    /// never a push target.</summary>
    public List<Inven> PushBags()
    {
        var bags = new List<Inven>();
        if (FindInven(Proto.InvenDefault) is { } def) bags.Add(def);
        foreach (var i in Invens.Where(i => i.InvenId != Proto.InvenDefault && i.InvenId != Proto.InvenEquip)
                                 .OrderBy(i => i.InvenId))
            bags.Add(i);
        return bags;
    }
}

/// <summary>The <c>TSaveChar</c> value columns the map doesn't otherwise use but must persist unchanged
/// (loaded from <c>TCHARTABLE</c>, written straight back) so the full-row save doesn't overwrite them with 0.
/// These back unported subsystems (guild-leave, spawn-return tracking, the stat-EFP level, last-destination).</summary>
public sealed class CharPersistExtras
{
    public byte GuildLeave { get; set; }
    public uint GuildLeaveTime { get; set; }
    public ushort SpawnId { get; set; }
    public ushort LastSpawnId { get; set; }
    public uint LastDestination { get; set; }
    public ushort TemptedMon { get; set; }
    public byte Aftermath { get; set; }
    public byte StatLevel { get; set; }
    public byte StatPoint { get; set; }
    public uint StatExp { get; set; }
}
