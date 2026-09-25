namespace TMap.Data;

/// <summary>
/// An item template row from <c>TITEMCHART</c> (C++ <c>CTBLItemChart</c> → <c>tagTITEM</c>, keyed by
/// <c>m_wItemID</c>). This is the subset the client wire needs: the refine-max cap (<c>m_bRefineMax</c>)
/// and the 4-element magic-value revision table (<c>m_fRevision[RV_COUNT]</c>, i.e. the DB columns
/// <c>fRevision</c>/<c>fMRevision</c>/<c>fAtRate</c>/<c>fMAtRate</c>). The rest of the 50-column template
/// (attr id, price, slots, type/kind, durability, class mask…) and the linked <c>tagITEMATTR</c> base
/// AP/DP chart are deferred until the combat/stat engine needs them — see PORT_STATUS.md.
/// </summary>
public sealed record ItemTemplate(ushort ItemId, byte RefineMax, float[] Revision,
    byte Type = 0, ushort AttrId = 0, uint SpeedInc = 0,
    // Equip: required level, class mask, equip-slot mask, primary/sub equip slot, max stack.
    byte DefaultLevel = 0, uint ClassId = 0, uint SlotId = 0,
    byte PrmSlot = 0xFF, byte SubSlot = 0xFF, byte Stack = 1,
    // Use: item kind (IK_* — selects the use-effect), the use-effect value (potion heal amount),
    // and the reuse delay (echoed in CS_ITEMUSE_ACK; server-side cooldown enforcement deferred).
    byte Kind = 0, ushort UseValue = 0, uint Delay = 0,
    // Repair: the price ratio (m_fPrice) driving the repair-cost formula, and whether the item is
    // repairable at all (m_bCanRepair — 0 ⇒ ITEMREPAIR_DISALLOW).
    float Price = 0f, byte CanRepair = 0,
    // audit fold-in: the use delay-group (m_wDelayGroupID — the CS_ITEMUSE anti-tamper guard) and
    // whether using it consumes it (m_bConsumable — default 1 so DB-free/synth items still consume).
    ushort DelayGroup = 0, byte Consumable = 1,
    // NPC shop: the trade-permission mask (m_bIsSell — ITEMTRADE_SELL=2 lets it be sold to an NPC,
    // ITEMTRADE_DEAL=1 / ITEMTRADE_CABINET=4 are the other bits). Default 0 ⇒ not sellable.
    byte IsSell = 0,
    // Crafting: a scroll's own grade (m_bGrade — the downgrade step / magic-scroll probability), which crafts the
    // item accepts (bCanGrade/bCanMagic/bCanRare/bCanWrap/bCanColor), and the template durability (dwDuraMax).
    byte Grade = 0, byte CanGrade = 0, byte CanMagic = 0, byte CanRare = 0, byte CanWrap = 0, byte CanColor = 0,
    uint DuraMax = 0,
    // How long what the item grants lasts (m_wUseTime, in hours or days per m_bUseType's DURINGTYPE_TIME 0x01 /
    // DURINGTYPE_DAY 0x02 bit; neither ⇒ permanent) — a mount item's pet duration.
    ushort UseTime = 0, byte UseType = 0);

/// <summary>
/// An item-attribute row from <c>TITEMATTRCHART</c> (C++ <c>CTBLItemAttrChart</c> → <c>tagITEMATTR</c>,
/// keyed by <c>m_wID</c>) — the base attack/defence numbers an item contributes, selected per
/// (attr id + item-level grade + gem) by <c>SetItemAttr</c>.
/// </summary>
public sealed record ItemAttr(ushort Id, byte Kind, byte Grade, ushort MinAp, ushort MaxAp, ushort Dp,
    ushort MinMagicAp, ushort MaxMagicAp, ushort MagicDp, byte BlockProb);

/// <summary>
/// A magic-option template row from <c>TITEMMAGICCHART</c> (C++ <c>CTBLItemMagicChart</c> →
/// <c>tagITEMMAGIC</c>, keyed by <c>m_bMagic</c>). <see cref="RvType"/> selects the revision factor
/// (0 ⇒ 1.0, else the 1-based index into the item template's <see cref="ItemTemplate.Revision"/>);
/// <see cref="MaxValue"/> is the scalar used by <c>CTItem::GetMagicValue</c>.
/// </summary>
public sealed record MagicTemplate(byte MagicId, byte RvType, ushort MaxValue,
    // Crafting (C++ TITEMMAGIC): the item kinds it may appear on (dwKind bit kind-1), whether a magic / rare
    // scroll may roll it, the exclusion group two options on one item may not share, the option kind (non-zero
    // blocks upgrading), and the value bound a rolled option scales from.
    uint Kind = 0, byte IsMagic = 0, byte IsRare = 0, byte ExclIndex = 0, byte OptionKind = 0, ushort RareBound = 0);

/// <summary>One cash-gamble prize (C++ <c>TCASHGAMBLE</c> from <c>TVIEW_CASHGAMBLECHART</c>): its weight, how many
/// days the prize lasts (0 = forever) and the prize item itself.</summary>
public sealed record CashGambleRow(uint Id, uint Prob, ushort Group, ushort UseTime, FullItemRow Item);

/// <summary>One option an accessory scroll can add (C++ <c>ACCESSORYMAGIC</c> from <c>ACCESSORYMAGICTABLE</c>).</summary>
public sealed record AccessoryMagicRow(uint Id, byte MagicId, ushort MinValue, ushort MaxValue);

/// <summary>
/// A 2nd-ability formula row from <c>TFORMULACHART</c> (C++ <c>CTBLFormulaChart</c> → <c>m_mapTFORMULA</c>,
/// keyed by <c>FTYPE_*</c>). Each derived ability is <c>Init + f(stat)·RateX</c> (or a level-scaled
/// <c>pow(RateY, level)·RateX</c> for defence). The chart's <c>szName</c> column is not needed and not loaded.
/// </summary>
public sealed record FormulaRow(uint Init, float RateX, float RateY);

/// <summary>
/// The six primary-stat seeds from <c>TCLASSCHART</c> / <c>TRACECHART</c> (C++ <c>CTBLClassChart</c> /
/// <c>CTBLRaceChart</c> → <c>tagTSTAT</c>), keyed by class id / race id. The character's effective stats are
/// derived from these seeds + level; there is no per-character stat storage.
/// </summary>
public sealed record StatSeed(ushort Str, ushort Dex, ushort Con, ushort Int, ushort Wis, ushort Men);

/// <summary>
/// The in-memory template charts the map server loads once at startup — the C# counterpart of
/// <c>CTMapSvrModule</c>'s <c>m_mapTITEM</c> (items), <c>m_mapTItemMagic</c> (magic options),
/// <c>m_mapTFORMULA</c> (2nd-ability formulas), and <c>m_mapTCLASS</c>/<c>m_mapTRACE</c> (stat seeds). Keyed
/// identically. Loaded via <see cref="GameDatabase.LoadTemplatesAsync"/>; empty in DB-free mode (callers
/// then fall back to raw persisted / synthesized values).
/// </summary>
public sealed class TemplateStore
{
    public Dictionary<ushort, ItemTemplate> Items { get; } = new();
    public Dictionary<byte, MagicTemplate> Magics { get; } = new();

    // Stat / HP-MP charts.
    public Dictionary<byte, FormulaRow> Formulas { get; } = new(); // C++ m_mapTFORMULA, keyed by FTYPE_*
    public Dictionary<byte, StatSeed> Classes { get; } = new();    // C++ m_mapTCLASS, keyed by class id
    public Dictionary<byte, StatSeed> Races { get; } = new();      // C++ m_mapTRACE, keyed by race id

    // The per-level repair-cost coefficient (C++ CTBLLevelChart.m_dwRepairCost, keyed by bLevel;
    // looked up via FindTLevel(GetPowerLevel())). A missing level ⇒ GetRepairCost returns 0 (free repair).
    public Dictionary<int, uint> RepairCostByLevel { get; } = new();

    // The exp curve + per-level skill-point grant (C++ CTBLLevelChart m_dwEXP / m_bSkillPoint,
    // keyed by bLevel). LevelExp[L] = the total exp needed to advance FROM level L to L+1 (m_pTLEVEL->m_dwEXP).
    public Dictionary<int, uint> LevelExp { get; } = new();
    public Dictionary<int, byte> LevelSkillPoint { get; } = new();

    // The per-level base price (C++ CTBLLevelChart.m_dwMoney, keyed by bLevel). The item buy/sell
    // price is m_dwMoney[grade]·m_fPrice (grade = the item's attr grade or default level). A missing level ⇒
    // price 0 (DB-free / unknown grade).
    public Dictionary<int, uint> LevelMoney { get; } = new();

    /// <summary>The exp threshold to leave level <paramref name="level"/> (C++ <c>FindTLevel(level)-&gt;m_dwEXP</c>),
    /// or null if the level chart has no such row (DB-free / above the top ⇒ no more level-ups).</summary>
    public uint? LevelExpOf(int level) => LevelExp.TryGetValue(level, out var e) ? e : null;
    public byte SkillPointOf(int level) => LevelSkillPoint.TryGetValue(level, out var sp) ? sp : (byte)0;

    /// <summary>The base money for a level/grade (C++ <c>FindTLevel(grade)-&gt;m_dwMoney</c>), or null when the
    /// chart has no such row (DB-free / unknown grade ⇒ price 0).</summary>
    public uint? LevelMoneyOf(int level) => LevelMoney.TryGetValue(level, out var m) ? m : null;

    /// <summary>C++ <c>m_mapTNpc</c> — the NPC registry loaded from <c>TNPCCHART</c> (+ the per-NPC shop stock
    /// from <c>TNPCITEMCHART</c>), keyed by NPC id. The map server builds its runtime NPC objects from these.</summary>
    public Dictionary<ushort, NpcDef> Npcs { get; } = new();

    /// <summary>C++ <c>m_mapTSPAWNPOS</c> — named spawn points by <c>wID</c> (TSPAWNPOSCHART).</summary>
    public Dictionary<ushort, SpawnPosRow> SpawnPositions { get; } = new();

    /// <summary>C++ <c>m_mapTPortal</c> — portals by <c>wPortalID</c>, each with its destinations (TPORTALCHART +
    /// TDESTINATIONCHART).</summary>
    public Dictionary<ushort, PortalRow> Portals { get; } = new();

    /// <summary>C++ <c>m_mapQUESTTEMP</c> — quest templates keyed by quest id. The map server builds the
    /// trigger index (<c>m_mapTRIGGER</c>) from these at bring-up. Test-injectable; the DB load is deferred
    /// (the quest-table schema is unverified — see PORT_STATUS.md).</summary>
    public Dictionary<uint, QuestTemplate> Quests { get; } = new();

    /// <summary>The quest template for an id (C++ <c>FindQuestTemplate</c>), or null.</summary>
    public QuestTemplate? Quest(uint id) => Quests.GetValueOrDefault(id);

    /// <summary>C++ <c>TSWITCHCHART</c> rows (the map switch definitions), loaded once at bring-up. The map
    /// server builds a per-(channel, map) runtime switch from each (<see cref="Server"/> <c>InitSwitches</c>).</summary>
    public List<SwitchDef> Switches { get; } = new();

    /// <summary>C++ <c>TGATECHART</c> rows (the switch-driven gate definitions). Each links to a switch by id;
    /// a gate mirrors its switch(es)' open state.</summary>
    public List<GateDef> Gates { get; } = new();

    // The skill chart (C++ m_mapTSKILL of CTSkillTemp), keyed by m_wID. Drives the CS_SKILLUSE
    // caster cost + cooldown; a learned skill links to its template here (like items → ItemTemplate).
    public Dictionary<ushort, SkillTemplate> Skills { get; } = new();

    // The monster AI scripts (C++ m_mapTMONAI of CTMonsterAI), keyed by bAIType. A monster
    // template's AiType selects one; a missing type falls back to DefaultAiType, and a missing fallback means
    // the monster has no scripted behaviour at all (every OnEvent is a silent no-op) — which is what the
    // DB-free path gets, and why the engine must degrade cleanly on a null script.
    public Dictionary<byte, AiScript> AiScripts { get; } = new();

    /// <summary>C++ <c>DEFAULT_AI</c> — the script <c>CTMonster::OnEvent</c> falls back to when the monster's
    /// own template has none (<c>m_pMON-&gt;m_pAI ? : FindTMonsterAI(DEFAULT_AI)</c>, TMonster.cpp:456).</summary>
    public const byte DefaultAiType = 0;

    /// <summary>The script driving a monster with this <c>bAIType</c>, falling back to
    /// <see cref="DefaultAiType"/>, or null when neither is loaded.</summary>
    public AiScript? AiScriptFor(byte aiType) =>
        AiScripts.TryGetValue(aiType, out var s) ? s : AiScripts.GetValueOrDefault(DefaultAiType);

    // Monster spawn charts.
    /// <summary>C++ <c>m_mapTMONSTER</c> chart — monster templates keyed by <c>m_wID</c>.</summary>
    public Dictionary<ushort, MonsterTemplate> MonsterTemplates { get; } = new();
    /// <summary>C++ <c>m_mapTMONATTR</c> — level-scaled vitals keyed by <c>MAKELONG(m_wID, m_bLevel)</c>.</summary>
    public Dictionary<uint, MonAttrRow> MonAttrs { get; } = new();
    /// <summary>C++ <c>m_mapTMONSPAWN</c> — spawn points (each bundled with its monster-type table).</summary>
    public List<MonsterSpawnDef> MonsterSpawns { get; } = new();

    /// <summary>C++ <c>MAKELONG(wAttrId, bLevel)</c> — the monster-attr chart key.</summary>
    public static uint MonAttrKey(ushort attrId, byte level) => attrId | ((uint)level << 16);
    public MonAttrRow? MonAttr(ushort attrId, byte level) => MonAttrs.GetValueOrDefault(MonAttrKey(attrId, level));

    // Item-attribute (AP/DP) charts.
    public Dictionary<ushort, ItemAttr> ItemAttrs { get; } = new(); // C++ m_mapTItemAttr, keyed by wID
    /// <summary>Item-level → grade byte (C++ <c>m_itemgrade[level].m_bGrade</c>, ITEMLEVEL_COUNT = 50).</summary>
    public byte[] ItemGrades { get; } = new byte[50];

    /// <summary>C++ <c>m_mapTPET</c> — the mount templates (TMOUNTCHART) by mount id.</summary>
    public Dictionary<ushort, MountTemplate> Mounts { get; } = new();

    /// <summary>C++ <c>m_itemgrade[level].m_bProb</c> / <c>m_dwMoney</c> — the upgrade success chance and cost.</summary>
    public byte[] ItemGradeProb { get; } = new byte[50];
    public uint[] ItemGradeMoney { get; } = new uint[50];

    /// <summary>C++ <c>m_gemgrade[gem].m_bProb</c> (TGEMGRADECHART), gem 0..5.</summary>
    public byte[] GemProbs { get; } = new byte[6];

    /// <summary>C++ <c>m_vItemMagic[kind]</c> — the magic options that may roll on an item kind (1..25), in chart
    /// order. Built from each option's <see cref="MagicTemplate.Kind"/> mask.</summary>
    public Dictionary<byte, List<MagicTemplate>> MagicsByKind { get; } = new();

    /// <summary>C++ <c>TLEVELCHART.dwRefineCost</c> by level (keyed by the power level of the item refined).</summary>
    public Dictionary<int, uint> RefineCostByLevel { get; } = new();

    /// <summary>C++ <c>m_mapCashGameble</c> — the prizes of each gamble group, in chart order.</summary>
    public Dictionary<ushort, List<CashGambleRow>> CashGamble { get; } = new();

    /// <summary>C++ <c>m_mapMaxCashGambleProb</c> — each group's total weight.</summary>
    public Dictionary<ushort, uint> CashGambleTotal { get; } = new();

    /// <summary>C++ <c>m_vAccessoryMagic</c> (ACCESSORYMAGICTABLE) — empty in the live data.</summary>
    public List<AccessoryMagicRow> AccessoryMagic { get; } = new();
    /// <summary>The <c>SetItemAttr</c> fallback row — C++ <c>m_mapTItemAttr.begin()</c> = the lowest-<c>wID</c>
    /// entry (a <c>std::map</c>). Set once after the attr chart loads.</summary>
    public ItemAttr? DefaultAttr { get; set; }

    /// <summary>The per-level stat-growth base (<c>FormulaChart[FTYPE_1ST].RateX</c>), cached like the C++
    /// <c>f1stRateX</c>. 1.0 ⇒ no level scaling.</summary>
    public float Rate1st { get; set; } = 1f;

    /// <summary>True once the item chart has actually been loaded — the gate for the C++ "drop any item
    /// whose <c>wItemID</c> has no template" rule (which must not fire in DB-free mode).</summary>
    public bool HasItems => Items.Count > 0;

    /// <summary>True once the stat/HP-MP charts (formula + class + race) are loaded — the gate for computing
    /// derived stats/vitals (else callers fall back to the synthesized values).</summary>
    public bool HasStats => Formulas.Count > 0 && Classes.Count > 0 && Races.Count > 0;

    /// <summary>True once the item-attribute chart is loaded — the gate for linking <c>m_pTITEMATTR</c>.</summary>
    public bool HasItemAttrs => ItemAttrs.Count > 0;

    public ItemTemplate? Item(ushort itemId) => Items.GetValueOrDefault(itemId);
    public MagicTemplate? Magic(byte magicId) => Magics.GetValueOrDefault(magicId);
    /// <summary>The skill template for a skill id (C++ <c>CTMapSvrModule::FindTSkill(WORD)→CTSkillTemp*</c>),
    /// or null (DB-free / unknown skill).</summary>
    public SkillTemplate? Skill(ushort skillId) => Skills.GetValueOrDefault(skillId);
    public FormulaRow? Formula(byte ftype) => Formulas.GetValueOrDefault(ftype);
    public ItemAttr? Attr(ushort id) => ItemAttrs.GetValueOrDefault(id);

    /// <summary>The grade byte for an item level (C++ <c>m_itemgrade[level].m_bGrade</c>), clamped to the table.</summary>
    public byte GradeForLevel(byte level) => level < ItemGrades.Length ? ItemGrades[level] : ItemGrades[0];
}
