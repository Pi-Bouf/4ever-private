using System.Data;
using Microsoft.Data.SqlClient;
using TMap.Protocol;

namespace TMap.Data;

/// <summary>A character's persistent row from <c>TCHARTABLE</c> (the columns the map's <c>CTBLChar</c> load
/// reads). <c>Hp</c>/<c>Mp</c> are the persisted <b>current</b> HP/MP (<c>dwHP</c>/<c>dwMP</c>) — max HP/MP
/// are computed, not stored. The trailing block (<c>Exp</c>…<c>StatExp</c>) was added for Phase 25 so the
/// char save (<c>TSaveChar</c>, which rewrites all 28 value columns) round-trips them instead of zeroing the
/// ones the map doesn't otherwise use.</summary>
public readonly record struct CharLoadRow(
    string Name, byte StartAct, byte Class, byte Race, byte Country, byte Sex, byte Hair, byte Face,
    byte Body, byte Pants, byte Hand, byte Foot, byte Level, uint Region, byte HelmetHide, uint Hp, uint Mp,
    uint Gold, uint Silver, uint Cooper,
    uint Exp, ushort SkillPoint, byte GuildLeave, uint GuildLeaveTime, ushort SpawnId, ushort LastSpawnId,
    uint LastDestination, ushort TemptedMon, byte Aftermath, byte StatLevel, byte StatPoint, uint StatExp);

/// <summary>The full <c>TSaveChar</c> value set (C++ <c>CSPSaveChar</c>, param order preserved). A snapshot
/// built on the batch thread and handed to <see cref="GameDatabase.SaveCharAsync"/> for the off-thread write.
/// The two pc-bang columns are 0 (that subsystem is unported and not loaded).</summary>
public readonly record struct CharSaveData(
    uint CharId, byte StartAct, byte Level, byte HelmetHide, uint Gold, uint Silver, uint Cooper,
    byte GuildLeave, uint GuildLeaveTime, uint Exp, uint Hp, uint Mp, ushort SkillPoint, uint Region,
    ushort MapId, ushort SpawnId, ushort LastSpawnId, ushort TemptedMon, byte Aftermath,
    float PosX, float PosY, float PosZ, ushort Dir, uint PcBangTime, byte PcBangItemCnt,
    uint LastDestination, byte StatLevel, byte StatPoint, uint StatExp);

/// <summary>One quest's persisted progress (C++ <c>TSaveQuest</c> + its <c>TSaveQuestTerm</c> rows).</summary>
public readonly record struct QuestSaveRow(
    uint QuestId, uint Tick, byte CompleteCount, byte TriggerCount, IReadOnlyList<QuestTermSaveRow> Terms);

/// <summary>One quest term's persisted count (C++ <c>TSaveQuestTerm</c>).</summary>
public readonly record struct QuestTermSaveRow(uint TermId, byte TermType, byte Count);

/// <summary>One item instance from <c>TITEMTABLE</c> (subset the map needs for the equipped-gear view).</summary>
public readonly record struct ItemLoadRow(
    byte ItemId, ushort TemplateId, byte Level, byte GradeEffect, ushort Color, byte RegGuild,
    ushort CustomTex, ushort MoggItemId);

/// <summary>An inventory container row from <c>TINVENTABLE</c> (CTBLInven).</summary>
public readonly record struct InvenLoadRow(byte InvenId, ushort TemplateId, long EndTime, byte Eld);

/// <summary>A full item instance from <c>TITEMTABLE</c> (CTBLItem; the 32 columns the map needs — the
/// C++ 35-column read additionally selects dlID/bOwnerType/dwOwnerID, which this explicit-column read omits).</summary>
public readonly record struct FullItemRow(
    byte StorageType, uint StorageId, byte ItemSlot, ushort TemplateId, byte Level, byte Count,
    byte GLevel, uint DuraMax, uint DuraCur, byte RefineCur, long EndTime, byte GradeEffect,
    byte[] Magic, ushort[] Value, uint[] Ext, byte Gem, ushort MoggItemId, long DlId);

/// <summary>One inventory container to persist (C++ <c>TSaveInven</c>). <c>EndTime</c> is __time64_t seconds
/// (converted to a datetime by the writer); <c>Eld</c> is the bag-enhancement level.</summary>
public readonly record struct InvenSaveData(byte InvenId, ushort TemplateId, long EndTime, byte Eld);

/// <summary>One item to persist (C++ <c>TSaveItem</c> — the full 35-value set). <c>DlId</c> is the item's
/// unique row id (preserved from load, or freshly generated for an in-session item); <c>StorageType</c> +
/// <c>StorageId</c> + <c>ItemSlot</c> locate it — a bag item is <c>STORAGE_INVEN</c>/invenId/slot; a cabinet
/// item (Phase 37) is <c>STORAGE_CABINET</c>/<c>dwStItemID</c>/cabinetId.</summary>
public readonly record struct ItemSaveData(
    long DlId, byte StorageType, uint StorageId, byte ItemSlot, ushort TemplateId, byte Level, byte Count,
    byte GLevel, uint DuraMax, uint DuraCur, byte RefineCur, long EndTime, byte GradeEffect,
    byte[] Magic, ushort[] Value, uint[] Ext, byte Gem, ushort MoggItemId);

/// <summary>A learned skill row from <c>TSKILLTABLE</c> (CTBLSkill).</summary>
public readonly record struct SkillLoadRow(ushort SkillId, byte Level, uint RemainTick);

/// <summary>A maintained/buff skill row from <c>TSKILLMAINTAINTABLE</c> (CTBLSkillMaintain — the 8 persisted fields).</summary>
public readonly record struct MaintainLoadRow(
    ushort SkillId, byte Level, uint RemainTick, byte AttackType, uint AttackId, byte HostType,
    uint HostId, byte AttackCountry);

/// <summary>A hotkey page from <c>THOTKEYTABLE</c> (CTBLHotKey): the page key + 12 (type,id) slots.</summary>
public readonly record struct HotkeyLoadRow(byte InvenKey, (byte Type, ushort Id)[] Slots);

/// <summary>A cabinet open-state header from <c>TCABINETTABLE</c> (C++ <c>CTBLCabinetTable</c>, DBAccess.h:2845):
/// which of the character's cabinets exist and whether each is opened (<c>bUse</c>). Its items are ordinary
/// <c>TITEMTABLE</c> rows with <c>bStorageType=STORAGE_CABINET</c> (loaded by <see cref="GameDatabase.LoadItemsAsync"/>).</summary>
public readonly record struct CabinetHeaderRow(byte CabinetId, bool Use);

/// <summary>A saved quest's header from <c>TQUESTTABLE</c> (CTBLQuestTable): the quest id, the remaining timer
/// tick, and the complete/trigger counts. The counterpart of the Phase-25 <c>TSaveQuest</c> write.</summary>
public readonly record struct QuestLoadRow(uint QuestId, uint Tick, byte CompleteCount, byte TriggerCount);

/// <summary>A saved quest term's running counter from <c>TQUESTTERMTABLE</c> (CTBLQuestTermTable).</summary>
public readonly record struct QuestTermLoadRow(uint QuestId, uint TermId, byte TermType, byte Count);

/// <summary>
/// Access to the game database (DSN <c>TGAME_GSP</c> → <c>TGame_gsp</c>). The C++ <c>TMapSvr</c> embeds the
/// DB-manager role and loads a character on its own <c>m_db</c> connection during the enter handshake
/// (<c>OnDM_LOADCHAR_REQ</c>, from <c>CTBLChar</c>/<c>CTBLItem</c>/…). Here we do the same directly.
///
/// This is a Phase-1 subset: it loads the character's persistent appearance and equipped gear. The full
/// char blob (inventory containers, skills, quests, hotkeys, pets/recall-mons, cabinet, post, auction,
/// PvP/duel, medals, companions, etc.) and every save proc are deferred — see PORT_STATUS.md.
/// Every method degrades gracefully (throws a <see cref="SqlException"/> the caller catches) so the map
/// runs DB-free.
/// </summary>
public sealed class GameDatabase
{
    private readonly string _cs;
    public GameDatabase(string connectionString) => _cs = connectionString;

    private async Task<SqlConnection> OpenAsync(CancellationToken ct)
    {
        var c = new SqlConnection(_cs);
        await c.OpenAsync(ct).ConfigureAwait(false);
        return c;
    }

    public async Task PingAsync(CancellationToken ct = default)
    {
        await using var c = new SqlConnection(_cs);
        await c.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand("SELECT 1", c);
        await cmd.ExecuteScalarAsync(ct);
    }

    // ---- Phase 4: item + magic template charts (loaded once at startup) ----

    // Subset of the 50-column TITEMCHART (CTBLItemChart) the client wire needs: the refine-max cap and the
    // 4 revision columns → m_fRevision[0..3] (RV_PHYSIC/RV_MAGIC/RV_PHYSICPROB/RV_MAGICPROB).
    private const string ItemChartSql =
        @"SELECT wItemID, bRefineMax, fRevision, fMRevision, fAtRate, fMAtRate, bType, wAttrID, dwSpeedInc,
                 bLevel, dwClassID, dwSlotID, bPrmSlotID, bSubSlotID, bStack, bKind, wUseValue, dwDelay,
                 fPrice, bCanRepair, wDelayGroupID, bConsumable, bIsSell FROM TITEMCHART";
    // Phase 10: the per-level repair-cost coefficient (CTBLLevelChart.m_dwRepairCost).
    // Phase 17 adds dwEXP (the level-up threshold) + bSkillPoint (granted per level).
    // Phase 23 adds dwMoney (the base price the item buy/sell math scales by m_fPrice).
    private const string LevelChartSql = @"SELECT bLevel, dwRepairCost, dwEXP, bSkillPoint, dwMoney FROM TLEVELCHART";
    // Phase 23: the NPC registry + per-NPC shop stock (CTBLNpc → m_mapTNpc, CTBLNpcItemAll → m_mapItem).
    private const string NpcChartSql =
        @"SELECT wID, bType, bCountryID, wLocalID, bCondition, bDiscountRate, bAddProb, wItemID, wMapID,
                 fPosX, fPosY, fPosZ FROM TNPCCHART";
    private const string NpcItemChartSql = @"SELECT wNpcID, dwItemID FROM TNPCITEMCHART ORDER BY wNpcID";
    // Phase 32: the map switch/gate charts (C++ CTBLSwitchChart DBAccess.h:3104 / CTBLGateChart :3092). Column
    // order is the value-exact contract. Gate columns are gateId, switchId, bType, mapId, pos (SELECT order).
    private const string SwitchChartSql =
        @"SELECT dwSwitchID, wMapID, wPosX, wPosY, wPosZ, bStart, bLockOnOpen, bLockOnClose, dwDuration FROM TSWITCHCHART";
    private const string GateChartSql =
        @"SELECT dwGateID, dwSwitchID, bType, wMapID, wPosX, wPosY, wPosZ FROM TGATECHART";
    // Phase 12: monster spawn charts. The C++ CTBLMonSpawn SELECT filters by a TSVRCHART server/unit-id join
    // (multi-machine topology); this single-server port loads all rows and buckets them by (channel, map).
    private const string MonsterChartSql =
        @"SELECT wID, bLevel, wMonAttr, wExp, bMoneyProb, dwMinMoney, dwMaxMoney, bItemProb, bDropCount FROM TMONSTERCHART";
    // Phase 22: the per-monster item drop table (CTBLMonItemAll → m_vMONITEM). Bulk-loaded, bucketed by wMonID
    // (like the skill-data rows). We read the fixed-item subset; the ranged / magic-option columns are deferred.
    private const string MonItemChartSql =
        @"SELECT wMonID, bChartType, wItemID, wWeight, bItemProb_N1, bItemProb_N2, bItemProb_N3, bItemProb_N4
                 FROM TMONITEMCHART ORDER BY wMonID";
    private const string MonAttrChartSql =
        @"SELECT wID, bLevel, dwMaxHP, dwMaxMP, wDP, wAP, wMinWAP, wMaxWAP, dwAtkSpeed, wMDP, wDL, wMDL, bCriticalPP, wAL, wWDP FROM TMONATTRCHART";
    private const string MonSpawnChartSql =
        @"SELECT wID, wMapID, fPosX, fPosY, fPosZ, wDir, bCountry, bCount, bRange, bProb, dwRegion, dwDelay, bEvent FROM TMONSPAWNCHART";
    private const string MapMonChartSql = @"SELECT wSpawnID, wMonID, bLeader, bEssential, bProb FROM TMAPMONCHART ORDER BY wSpawnID";
    private const string MagicChartSql =
        @"SELECT bMagic, bRvType, wMaxValue FROM TITEMMAGICCHART";
    // Stat / HP-MP charts (FORMULA_TYPE FTYPE_1ST = 34 is the per-level stat-growth factor).
    private const string FormulaChartSql = @"SELECT bID, dwInit, fRateX, fRateY FROM TFORMULACHART";
    private const string ClassChartSql = @"SELECT bClassID, wSTR, wDEX, wCON, wINT, wWIS, wMEN FROM TCLASSCHART";
    private const string RaceChartSql = @"SELECT bRaceID, wSTR, wDEX, wCON, wINT, wWIS, wMEN FROM TRACECHART";
    // Item-attribute (AP/DP) charts.
    private const string ItemAttrChartSql =
        @"SELECT wID, bKind, bGrade, wMinAP, wMaxAP, wDP, wMinMAP, wMaxMAP, wMDP, bBlockProb FROM TITEMATTRCHART";
    private const string ItemGradeChartSql = @"SELECT bLevel, bGrade FROM TITEMGRADECHART";
    // Phase 14: the skill chart (CTBLSkillChart → CTSkillTemp). Of the 56-column C++ SELECT we read the
    // subset the CS_SKILLUSE caster spine uses. Note bLevel → m_bStartLevel (the C++ remaps it, TMapSvr.cpp:2630).
    private const string SkillChartSql =
        @"SELECT wID, bKind, dwUseMP, bUseMPType, dwUseHP, bUseHPType, bLevel, bMaxLevel, bNextLevel,
                 dwReuseDelay, nReuseDelayInc, dwLoopDelay, dwKindDelay, bSpeedApply, bPositive, wMapID,
                 dwDuration, dwDurationInc, bMaintainType, bPriority, bStatic, dwClassID FROM TSKILLCHART";
    // Phase 15: the skill-effect rows (CTBLSkillData → CTSkillTemp::m_vData). C++ runs one query per skill;
    // this bulk load buckets by wSkillID (natural table order per skill matches the per-skill fetch order).
    private const string SkillDataChartSql =
        @"SELECT wSkillID, bAction, bType, bAttr, bExec, bInc, wValue, wValueInc, bCalc FROM TSKILLDATA";
    private const byte FType1st = 34;

    // Phase 29: the quest template graph (C++ CTMapSvrModule::LoadQuestTemp, the 4-table bulk load
    // DBAccess.h:2288-2408). The ORDER BYs are load-bearing: TQUESTCHART's `bMain DESC` fixes the order children
    // of a parent register in the trigger index (so ExecChildren attempts the main branch first); conditions are
    // `bConditionType DESC`; terms are `dwID`. The rows are assembled by dwQuestID into QuestTemplate.
    private const string QuestChartSql =
        @"SELECT dwParentID, dwQuestID, dwTriggerID, bTriggerType, bForceRun, bCountMax, bType, bConditionCheck
                 FROM TQUESTCHART ORDER BY dwParentID, bMain DESC";
    private const string QuestCondChartSql =
        @"SELECT dwQuestID, dwConditionID, bConditionType, bCount FROM TQCONDITIONCHART ORDER BY dwQuestID, bConditionType DESC";
    private const string QuestRewardChartSql =
        @"SELECT dwQuestID, dwRewardID, bRewardType, bTakeMethod, bTakeData, bCount FROM TQREWARDCHART ORDER BY dwQuestID";
    private const string QuestTermChartSql =
        @"SELECT dwQuestID, dwTermID, bTermType, bCount FROM TQUESTTERMCHART ORDER BY dwQuestID, dwID";

    /// <summary>
    /// Loads the item + magic template charts (C++ <c>CTBLItemChart</c> → <c>m_mapTITEM</c>,
    /// <c>CTBLItemMagicChart</c> → <c>m_mapTItemMagic</c>) once at startup. Drives the client-facing
    /// derived item values: template <c>RefineMax</c> and the computed <c>GetMagicValue</c>.
    /// </summary>
    public async Task<TemplateStore> LoadTemplatesAsync(CancellationToken ct = default)
    {
        var store = new TemplateStore();
        await using var c = await OpenAsync(ct);

        await using (var cmd = new SqlCommand(ItemChartSql, c))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
            {
                ushort id = r.GetUShortSafe(0);
                store.Items[id] = new ItemTemplate(id, r.GetByteSafe(1),
                    new[] { r.GetFloatSafe(2), r.GetFloatSafe(3), r.GetFloatSafe(4), r.GetFloatSafe(5) },
                    Type: r.GetByteSafe(6), AttrId: r.GetUShortSafe(7), SpeedInc: r.GetUIntSafe(8),
                    DefaultLevel: r.GetByteSafe(9), ClassId: r.GetUIntSafe(10), SlotId: r.GetUIntSafe(11),
                    PrmSlot: r.GetByteSafe(12), SubSlot: r.GetByteSafe(13), Stack: r.GetByteSafe(14),
                    Kind: r.GetByteSafe(15), UseValue: r.GetUShortSafe(16), Delay: r.GetUIntSafe(17),
                    Price: r.GetFloatSafe(18), CanRepair: r.GetByteSafe(19),
                    DelayGroup: r.GetUShortSafe(20), Consumable: r.GetByteSafe(21), IsSell: r.GetByteSafe(22));
            }

        await using (var cmd = new SqlCommand(MagicChartSql, c))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
            {
                byte id = r.GetByteSafe(0);
                store.Magics[id] = new MagicTemplate(id, r.GetByteSafe(1), r.GetUShortSafe(2));
            }

        await using (var cmd = new SqlCommand(FormulaChartSql, c))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
                store.Formulas[r.GetByteSafe(0)] = new FormulaRow(r.GetUIntSafe(1), r.GetFloatSafe(2), r.GetFloatSafe(3));
        store.Rate1st = store.Formulas.TryGetValue(FType1st, out var f1) ? f1.RateX : 1f;

        await using (var cmd = new SqlCommand(ClassChartSql, c))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
                store.Classes[r.GetByteSafe(0)] = new StatSeed(r.GetUShortSafe(1), r.GetUShortSafe(2),
                    r.GetUShortSafe(3), r.GetUShortSafe(4), r.GetUShortSafe(5), r.GetUShortSafe(6));

        await using (var cmd = new SqlCommand(RaceChartSql, c))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
                store.Races[r.GetByteSafe(0)] = new StatSeed(r.GetUShortSafe(1), r.GetUShortSafe(2),
                    r.GetUShortSafe(3), r.GetUShortSafe(4), r.GetUShortSafe(5), r.GetUShortSafe(6));

        await using (var cmd = new SqlCommand(ItemAttrChartSql, c))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
            {
                ushort id = r.GetUShortSafe(0);
                store.ItemAttrs[id] = new ItemAttr(id, r.GetByteSafe(1), r.GetByteSafe(2),
                    r.GetUShortSafe(3), r.GetUShortSafe(4), r.GetUShortSafe(5),
                    r.GetUShortSafe(6), r.GetUShortSafe(7), r.GetUShortSafe(8), r.GetByteSafe(9));
            }
        // SetItemAttr's fallback is std::map::begin() = the lowest-wID row.
        store.DefaultAttr = store.ItemAttrs.Count == 0 ? null
            : store.ItemAttrs[store.ItemAttrs.Keys.Min()];

        await using (var cmd = new SqlCommand(ItemGradeChartSql, c))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
            {
                byte level = r.GetByteSafe(0);
                if (level < store.ItemGrades.Length) store.ItemGrades[level] = r.GetByteSafe(1);
            }

        await using (var cmd = new SqlCommand(LevelChartSql, c))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
            {
                byte level = r.GetByteSafe(0);
                store.RepairCostByLevel[level] = r.GetUIntSafe(1);
                store.LevelExp[level] = r.GetUIntSafe(2);
                store.LevelSkillPoint[level] = r.GetByteSafe(3);
                store.LevelMoney[level] = r.GetUIntSafe(4);
            }

        // Phase 14: skill templates. Each is stamped with the global f1stRateX (= store.Rate1st, the
        // FTYPE_1ST growth base) exactly as the C++ loader does (pSkill->m_f1stRateX = f1stRateX).
        await using (var cmd = new SqlCommand(SkillChartSql, c))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
            {
                ushort id = r.GetUShortSafe(0);
                store.Skills[id] = new SkillTemplate(id, r.GetByteSafe(1),
                    UseMp: r.GetUIntSafe(2), UseMpType: r.GetByteSafe(3),
                    UseHp: r.GetUIntSafe(4), UseHpType: r.GetByteSafe(5),
                    StartLevel: r.GetByteSafe(6), MaxLevel: r.GetByteSafe(7), NextLevel: r.GetByteSafe(8),
                    ReuseDelay: r.GetUIntSafe(9), ReuseDelayInc: r.GetIntSafe(10),
                    LoopDelay: r.GetUIntSafe(11), KindDelay: r.GetUIntSafe(12),
                    SpeedApply: r.GetByteSafe(13), Positive: r.GetByteSafe(14), MapId: r.GetUShortSafe(15),
                    Duration: r.GetUIntSafe(16), DurationInc: r.GetUIntSafe(17), MaintainKind: r.GetByteSafe(18),
                    Priority: r.GetByteSafe(19), StaticFlag: r.GetByteSafe(20), ClassId: r.GetUIntSafe(21),
                    Rate1stX: store.Rate1st);
            }

        await using (var cmd = new SqlCommand(SkillDataChartSql, c))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
            {
                ushort skillId = r.GetUShortSafe(0);
                if (store.Skills.TryGetValue(skillId, out var sk))
                    sk.Data.Add(new SkillDataRow(r.GetByteSafe(1), r.GetByteSafe(2), r.GetByteSafe(3),
                        r.GetByteSafe(4), r.GetByteSafe(5), r.GetUShortSafe(6), r.GetUShortSafe(7), r.GetByteSafe(8)));
            }

        // Phase 12: monster templates, attrs, and spawn points (+ their monster-type tables).
        await using (var cmd = new SqlCommand(MonsterChartSql, c))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
            {
                ushort id = r.GetUShortSafe(0);
                store.MonsterTemplates[id] = new MonsterTemplate(id, r.GetByteSafe(1), r.GetUShortSafe(2),
                    Exp: r.GetUIntSafe(3), MoneyProb: r.GetByteSafe(4),
                    MinMoney: r.GetUIntSafe(5), MaxMoney: r.GetUIntSafe(6),
                    ItemProb: r.GetByteSafe(7), DropCount: r.GetByteSafe(8));
            }

        await using (var cmd = new SqlCommand(MonAttrChartSql, c))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
            {
                ushort id = r.GetUShortSafe(0); byte level = r.GetByteSafe(1);
                uint ap = r.GetUShortSafe(5), minWap = r.GetUShortSafe(6), maxWap = r.GetUShortSafe(7);
                // C++ GetDefendPower = m_wDP + m_wWDP and GetMagicDefPower = m_wMDP + m_wWDP (the weapon-DP term,
                // added when !HaveDisDefend — always true for a fresh monster, TMonster.cpp:1642/1736). Baked in
                // here to mirror the weapon-AP band (wAP + wMin/MaxWAP) we already fold into the attack.
                uint wWdp = r.GetUShortSafe(14);
                store.MonAttrs[TemplateStore.MonAttrKey(id, level)] =
                    new MonAttrRow(id, level, r.GetUIntSafe(2), r.GetUIntSafe(3), r.GetUShortSafe(4) + wWdp,
                        AtkMin: ap + minWap, AtkMax: ap + maxWap, AtkSpeed: r.GetUIntSafe(8), // C++ GetMin/MaxAP
                        MagicDefPower: r.GetUShortSafe(9) + wWdp, DefendLevel: r.GetUShortSafe(10),
                        MagicDefLevel: r.GetUShortSafe(11), CritProb: r.GetByteSafe(12), AttackLevel: r.GetUShortSafe(13));
            }

        // Phase 22: the per-monster item drop rows, bucketed onto their monster template by wMonID.
        await using (var cmd = new SqlCommand(MonItemChartSql, c))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
            {
                ushort monId = r.GetUShortSafe(0);
                if (store.MonsterTemplates.TryGetValue(monId, out var mt))
                    mt.DropRows.Add(new MonItemRow(r.GetUShortSafe(2), r.GetUShortSafe(3), r.GetByteSafe(1),
                        r.GetByteSafe(4), r.GetByteSafe(5), r.GetByteSafe(6), r.GetByteSafe(7)));
            }

        var spawnRows = new Dictionary<ushort, MonSpawnRow>();
        await using (var cmd = new SqlCommand(MonSpawnChartSql, c))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
            {
                ushort id = r.GetUShortSafe(0);
                spawnRows[id] = new MonSpawnRow(id, r.GetUShortSafe(1),
                    r.GetFloatSafe(2), r.GetFloatSafe(3), r.GetFloatSafe(4), r.GetUShortSafe(5), r.GetByteSafe(6),
                    r.GetByteSafe(7), r.GetByteSafe(8), r.GetByteSafe(9), r.GetUIntSafe(10), r.GetUIntSafe(11),
                    r.GetByteSafe(12));
            }

        var typesBySpawn = new Dictionary<ushort, List<MapMonRow>>();
        await using (var cmd = new SqlCommand(MapMonChartSql, c))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
            {
                ushort spawnId = r.GetUShortSafe(0);
                if (!typesBySpawn.TryGetValue(spawnId, out var list)) typesBySpawn[spawnId] = list = new();
                list.Add(new MapMonRow(spawnId, r.GetUShortSafe(1), r.GetByteSafe(2), r.GetByteSafe(3), r.GetByteSafe(4)));
            }

        foreach (var (id, spawn) in spawnRows)
            store.MonsterSpawns.Add(new MonsterSpawnDef(spawn,
                typesBySpawn.TryGetValue(id, out var types) ? types : new List<MapMonRow>()));

        // Phase 23: the NPC registry (TNPCCHART) + the per-NPC shop stock (TNPCITEMCHART, bulk-loaded like the
        // C++ CTBLNpcItemAll and bucketed by wNpcID). dwItemID is a DWORD but item ids are WORD (C++ (WORD)cast).
        await using (var cmd = new SqlCommand(NpcChartSql, c))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
            {
                ushort id = r.GetUShortSafe(0);
                store.Npcs[id] = new NpcDef(id, r.GetByteSafe(1), r.GetByteSafe(2), r.GetUShortSafe(3),
                    r.GetByteSafe(4), r.GetByteSafe(5), r.GetByteSafe(6), r.GetUShortSafe(7), r.GetUShortSafe(8),
                    r.GetFloatSafe(9), r.GetFloatSafe(10), r.GetFloatSafe(11));
            }

        await using (var cmd = new SqlCommand(NpcItemChartSql, c))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
            {
                ushort npcId = r.GetUShortSafe(0);
                if (store.Npcs.TryGetValue(npcId, out var npc)) npc.ItemIds.Add((ushort)r.GetUIntSafe(1));
            }

        // Phase 32: map switches + their gates (read-only definitions; runtime state is per-map-instance, rebuilt
        // from these at bring-up and never persisted, matching the C++ CTMap load).
        await using (var cmd = new SqlCommand(SwitchChartSql, c))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
                store.Switches.Add(new SwitchDef(r.GetUIntSafe(0), r.GetUShortSafe(1),
                    r.GetUShortSafe(2), r.GetUShortSafe(3), r.GetUShortSafe(4),
                    r.GetByteSafe(5), r.GetByteSafe(6), r.GetByteSafe(7), r.GetUIntSafe(8)));

        await using (var cmd = new SqlCommand(GateChartSql, c))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
                store.Gates.Add(new GateDef(r.GetUIntSafe(0), r.GetUIntSafe(1), r.GetByteSafe(2),
                    r.GetUShortSafe(3), r.GetUShortSafe(4), r.GetUShortSafe(5), r.GetUShortSafe(6)));

        // Phase 29: the quest template graph. Build every QuestTemplate from TQUESTCHART (inserted in the
        // bMain-DESC query order — the dict's insertion order is what InitQuests indexes, matching the C++
        // trigger-vector order), then attach conditions / rewards / terms by dwQuestID.
        await using (var cmd = new SqlCommand(QuestChartSql, c))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
            {
                uint questId = r.GetUIntSafe(1);
                store.Quests[questId] = new QuestTemplate
                {
                    QuestId = questId, ParentId = r.GetUIntSafe(0), TriggerId = r.GetUIntSafe(2),
                    TriggerType = r.GetByteSafe(3), ForceRun = r.GetByteSafe(4), CountMax = r.GetByteSafe(5),
                    Type = r.GetByteSafe(6), ConditionCheck = r.GetByteSafe(7),
                };
            }

        await using (var cmd = new SqlCommand(QuestCondChartSql, c))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
                if (store.Quests.TryGetValue(r.GetUIntSafe(0), out var q))
                    q.Conditions.Add(new QuestCondition(r.GetUIntSafe(1), r.GetByteSafe(2), r.GetByteSafe(3)));

        await using (var cmd = new SqlCommand(QuestRewardChartSql, c))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
                if (store.Quests.TryGetValue(r.GetUIntSafe(0), out var q))
                    q.Rewards.Add(new QuestReward(r.GetUIntSafe(1), r.GetByteSafe(2), r.GetByteSafe(3),
                        r.GetByteSafe(4), r.GetByteSafe(5)));

        await using (var cmd = new SqlCommand(QuestTermChartSql, c))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
                if (store.Quests.TryGetValue(r.GetUIntSafe(0), out var q))
                    q.Terms.Add(new QuestTerm(r.GetUIntSafe(1), r.GetByteSafe(2), r.GetByteSafe(3)));

        return store;
    }

    // The TCHARTABLE columns the map's CTBLChar (DBAccess.h) reads. The first 20 are the Phase-1 subset; the
    // trailing block is the rest of the TSaveChar value set (loaded so the save round-trips, not zeroes, them).
    private const string CharSql = @"
SELECT szNAME, bStartAct, bClass, bRace, bCountry, bSex, bHair, bFace, bBody, bPants, bHand, bFoot,
       bLevel, dwRegion, bHelmetHide, dwHP, dwMP, dwGold, dwSilver, dwCooper,
       dwEXP, wSkillPoint, bGuildLeave, dwGuildLeaveTime, wSpawnID, wLastSpawnID, dwLastDestination,
       wTemptedMon, bAftermath, bStatLevel, bStatPoint, dwStatExp
FROM TCHARTABLE WHERE dwCharID = @dwCharID AND bDelete = 0";

    /// <summary>Loads a character's persistent row, or null if absent. CTBLChar.</summary>
    public async Task<CharLoadRow?> LoadCharAsync(uint charId, CancellationToken ct = default)
    {
        await using var c = await OpenAsync(ct);
        await using var cmd = new SqlCommand(CharSql, c);
        cmd.Parameters.Add(SqlProc.In("@dwCharID", SqlDbType.Int, unchecked((int)charId)));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return new CharLoadRow(
            r.GetStringSafe(0), r.GetByteSafe(1), r.GetByteSafe(2), r.GetByteSafe(3), r.GetByteSafe(4),
            r.GetByteSafe(5), r.GetByteSafe(6), r.GetByteSafe(7), r.GetByteSafe(8), r.GetByteSafe(9),
            r.GetByteSafe(10), r.GetByteSafe(11), r.GetByteSafe(12), r.GetUIntSafe(13), r.GetByteSafe(14),
            r.GetUIntSafe(15), r.GetUIntSafe(16), r.GetUIntSafe(17), r.GetUIntSafe(18), r.GetUIntSafe(19),
            r.GetUIntSafe(20), r.GetUShortSafe(21), r.GetByteSafe(22), r.GetUIntSafe(23), r.GetUShortSafe(24),
            r.GetUShortSafe(25), r.GetUIntSafe(26), r.GetUShortSafe(27), r.GetByteSafe(28), r.GetByteSafe(29),
            r.GetByteSafe(30), r.GetUIntSafe(31));
    }

    // ================= Phase 25: character + quest SAVE (proc calls; bodies live in the .bak baseline) =================

    /// <summary>C++ <c>TSaveChar</c> (CSPSaveChar) — the single-row char UPDATE by <c>dwCharID</c>. Params are
    /// bound positionally in the exact <c>DBAccess.h</c> order (28 value columns; the two pc-bang columns are 0
    /// — that subsystem is unported). A missing proc (2812, not in the baseline) is tolerated silently.</summary>
    public async Task SaveCharAsync(CharSaveData d, CancellationToken ct = default)
    {
        try
        {
            await using var c = await OpenAsync(ct);
            var args = new[]
            {
                SqlProc.In("@p0", SqlDbType.Int, unchecked((int)d.CharId)),
                SqlProc.In("@p1", SqlDbType.TinyInt, d.StartAct),
                SqlProc.In("@p2", SqlDbType.TinyInt, d.Level),
                SqlProc.In("@p3", SqlDbType.TinyInt, d.HelmetHide),
                SqlProc.In("@p4", SqlDbType.Int, unchecked((int)d.Gold)),
                SqlProc.In("@p5", SqlDbType.Int, unchecked((int)d.Silver)),
                SqlProc.In("@p6", SqlDbType.Int, unchecked((int)d.Cooper)),
                SqlProc.In("@p7", SqlDbType.TinyInt, d.GuildLeave),
                SqlProc.In("@p8", SqlDbType.Int, unchecked((int)d.GuildLeaveTime)),
                SqlProc.In("@p9", SqlDbType.Int, unchecked((int)d.Exp)),
                SqlProc.In("@p10", SqlDbType.Int, unchecked((int)d.Hp)),
                SqlProc.In("@p11", SqlDbType.Int, unchecked((int)d.Mp)),
                SqlProc.In("@p12", SqlDbType.SmallInt, unchecked((short)d.SkillPoint)),
                SqlProc.In("@p13", SqlDbType.Int, unchecked((int)d.Region)),
                SqlProc.In("@p14", SqlDbType.SmallInt, unchecked((short)d.MapId)),
                SqlProc.In("@p15", SqlDbType.SmallInt, unchecked((short)d.SpawnId)),
                SqlProc.In("@p16", SqlDbType.SmallInt, unchecked((short)d.LastSpawnId)),
                SqlProc.In("@p17", SqlDbType.SmallInt, unchecked((short)d.TemptedMon)),
                SqlProc.In("@p18", SqlDbType.TinyInt, d.Aftermath),
                SqlProc.In("@p19", SqlDbType.Real, d.PosX),
                SqlProc.In("@p20", SqlDbType.Real, d.PosY),
                SqlProc.In("@p21", SqlDbType.Real, d.PosZ),
                SqlProc.In("@p22", SqlDbType.SmallInt, unchecked((short)d.Dir)),
                SqlProc.In("@p23", SqlDbType.Int, unchecked((int)d.PcBangTime)),
                SqlProc.In("@p24", SqlDbType.TinyInt, d.PcBangItemCnt),
                SqlProc.In("@p25", SqlDbType.Int, unchecked((int)d.LastDestination)),
                SqlProc.In("@p26", SqlDbType.TinyInt, d.StatLevel),
                SqlProc.In("@p27", SqlDbType.TinyInt, d.StatPoint),
                SqlProc.In("@p28", SqlDbType.Int, unchecked((int)d.StatExp)),
            };
            await SqlProc.ExecAsync(c, "TSaveChar", SqlProc.Ret(), args, ct);
        }
        catch (SqlException ex) when (ex.Number == 2812) { /* proc absent in this baseline — tolerate */ }
    }

    /// <summary>C++ <c>TSaveQuest</c> + <c>TSaveQuestTerm</c> (CSPSaveQuest/CSPSaveQuestTerm) — the per-quest
    /// upsert keyed on <c>(dwCharID, dwQuestID)</c> (+ <c>dwTermID</c> for terms). Only the caller's dirty
    /// (<c>Save</c>-flagged) quests are passed. All rows share one connection. Missing procs are tolerated.</summary>
    public async Task SaveQuestsAsync(uint charId, IReadOnlyList<QuestSaveRow> quests, CancellationToken ct = default)
    {
        if (quests.Count == 0) return;
        try
        {
            await using var c = await OpenAsync(ct);
            int cid = unchecked((int)charId);
            foreach (var q in quests)
            {
                await SqlProc.ExecAsync(c, "TSaveQuest", SqlProc.Ret(), new[]
                {
                    SqlProc.In("@p0", SqlDbType.Int, cid),
                    SqlProc.In("@p1", SqlDbType.Int, unchecked((int)q.QuestId)),
                    SqlProc.In("@p2", SqlDbType.Int, unchecked((int)q.Tick)),
                    SqlProc.In("@p3", SqlDbType.TinyInt, q.CompleteCount),
                    SqlProc.In("@p4", SqlDbType.TinyInt, q.TriggerCount),
                }, ct);

                foreach (var t in q.Terms)
                    await SqlProc.ExecAsync(c, "TSaveQuestTerm", SqlProc.Ret(), new[]
                    {
                        SqlProc.In("@p0", SqlDbType.Int, cid),
                        SqlProc.In("@p1", SqlDbType.Int, unchecked((int)q.QuestId)),
                        SqlProc.In("@p2", SqlDbType.Int, unchecked((int)t.TermId)),
                        SqlProc.In("@p3", SqlDbType.TinyInt, t.TermType),
                        SqlProc.In("@p4", SqlDbType.TinyInt, t.Count),
                    }, ct);
            }
        }
        catch (SqlException ex) when (ex.Number == 2812) { /* proc absent — tolerate */ }
    }

    /// <summary>C++ <c>TInitGenItemID(@out, @serverId)</c> (CSPInitGenItemID) — the per-server base for the
    /// unique item-id counter (so multiple map servers carve disjoint <c>dlID</c> ranges). Returns 0 when the
    /// DB / proc is unavailable (⇒ the caller won't rewrite inventory, avoiding unsafe ids).</summary>
    public async Task<long> InitGenItemIdAsync(byte serverId, CancellationToken ct = default)
    {
        try
        {
            await using var c = await OpenAsync(ct);
            var outId = SqlProc.Out("@p0", SqlDbType.BigInt);
            await SqlProc.ExecAsync(c, "TInitGenItemID", null,
                new[] { outId, SqlProc.In("@p1", SqlDbType.TinyInt, serverId) }, ct);
            return outId.Value is null or DBNull ? 0L : Convert.ToInt64(outId.Value);
        }
        catch (SqlException ex) when (ex.Number == 2812) { return 0L; }
    }

    /// <summary>C++ char-item SAVE (<c>SSHandler.cpp OnDM_SAVEITEM_REQ</c>, CSPSaveItemData*) — the
    /// delete-then-reinsert rewrite bracketed by <c>TSaveItemDataStart</c>/<c>TSaveItemDataEnd</c>: Start clears
    /// the char's <b>staging</b> tables (<c>TTEMPINVENTABLE</c>/<c>TTEMPITEMTABLE</c>), each container
    /// (<c>TSaveInven</c>) and item (<c>TSaveItem</c>, 35 values in exact param order) is inserted into staging,
    /// then End atomically promotes staging → live (<c>TINVENTABLE</c> and <c>TITEMTABLE</c>, filtered
    /// <c>bOwnerType=0 AND bStorageType=0</c> — matching the <c>TOWNER_CHAR</c>/<c>STORAGE_INVEN</c> we write).
    /// All on one connection so each proc's inner transaction sees the staged rows.
    ///
    /// <para><b>Do not use the <c>TSaveCharData*</c> bracket here:</b> in this baseline its item promotion
    /// (<c>INSERT TITEMTABLE SELECT … TTEMPITEMTABLE</c>) is <i>commented out</i>, so items would stage but never
    /// reach live — verified against the live proc bodies. <c>TSaveCharData*</c> is the separate cabinet/skill/
    /// used-item save.</para>
    /// A missing proc is tolerated.</summary>
    public async Task SaveInventoryAsync(uint charId, IReadOnlyList<InvenSaveData> invens,
        IReadOnlyList<ItemSaveData> items, CancellationToken ct = default)
    {
        try
        {
            await using var c = await OpenAsync(ct);
            int cid = unchecked((int)charId);

            await SqlProc.ExecAsync(c, "TSaveItemDataStart", null, new[] { SqlProc.In("@p0", SqlDbType.Int, cid) }, ct);

            foreach (var iv in invens)
                await SqlProc.ExecAsync(c, "TSaveInven", SqlProc.Ret(), new[]
                {
                    SqlProc.In("@p0", SqlDbType.Int, cid),
                    SqlProc.In("@p1", SqlDbType.TinyInt, iv.InvenId),
                    SqlProc.In("@p2", SqlDbType.SmallInt, unchecked((short)iv.TemplateId)),
                    SqlProc.In("@p3", SqlDbType.DateTime, FromTime64(iv.EndTime)),
                    SqlProc.In("@p4", SqlDbType.TinyInt, iv.Eld),
                }, ct);

            foreach (var it in items)
                await SqlProc.ExecAsync(c, "TSaveItem", SqlProc.Ret(), ItemArgs(cid, it), ct);

            await SqlProc.ExecAsync(c, "TSaveItemDataEnd", null, new[] { SqlProc.In("@p0", SqlDbType.Int, cid) }, ct);
        }
        catch (SqlException ex) when (ex.Number == 2812) { /* proc absent — tolerate */ }
    }

    /// <summary>The <c>TSaveItem</c>/<c>TSaveItemDirect</c> 35-value positional arg set (identical param list,
    /// exact <c>DBAccess.h</c> order). <c>bOwnerType</c> is always <c>TOWNER_CHAR</c> and <c>dwOwnerID</c> the
    /// char; the item's storage type/slot/attributes fill the rest. Shared by the full inventory rewrite and the
    /// incremental direct save.</summary>
    private static SqlParameter[] ItemArgs(int cid, in ItemSaveData it) => new[]
    {
        SqlProc.In("@p0", SqlDbType.BigInt, it.DlId),
        SqlProc.In("@p1", SqlDbType.TinyInt, it.StorageType),
        SqlProc.In("@p2", SqlDbType.Int, unchecked((int)it.StorageId)),
        SqlProc.In("@p3", SqlDbType.TinyInt, Proto.OwnerChar),
        SqlProc.In("@p4", SqlDbType.Int, cid),
        SqlProc.In("@p5", SqlDbType.TinyInt, it.ItemSlot),
        SqlProc.In("@p6", SqlDbType.SmallInt, unchecked((short)it.TemplateId)),
        SqlProc.In("@p7", SqlDbType.TinyInt, it.Level),
        SqlProc.In("@p8", SqlDbType.TinyInt, it.Count),
        SqlProc.In("@p9", SqlDbType.TinyInt, it.GLevel),
        SqlProc.In("@p10", SqlDbType.Int, unchecked((int)it.DuraMax)),
        SqlProc.In("@p11", SqlDbType.Int, unchecked((int)it.DuraCur)),
        SqlProc.In("@p12", SqlDbType.TinyInt, it.RefineCur),
        SqlProc.In("@p13", SqlDbType.DateTime, FromTime64(it.EndTime)),
        SqlProc.In("@p14", SqlDbType.TinyInt, it.GradeEffect),
        SqlProc.In("@p15", SqlDbType.TinyInt, it.Magic[0]),
        SqlProc.In("@p16", SqlDbType.TinyInt, it.Magic[1]),
        SqlProc.In("@p17", SqlDbType.TinyInt, it.Magic[2]),
        SqlProc.In("@p18", SqlDbType.TinyInt, it.Magic[3]),
        SqlProc.In("@p19", SqlDbType.TinyInt, it.Magic[4]),
        SqlProc.In("@p20", SqlDbType.TinyInt, it.Magic[5]),
        SqlProc.In("@p21", SqlDbType.SmallInt, unchecked((short)it.Value[0])),
        SqlProc.In("@p22", SqlDbType.SmallInt, unchecked((short)it.Value[1])),
        SqlProc.In("@p23", SqlDbType.SmallInt, unchecked((short)it.Value[2])),
        SqlProc.In("@p24", SqlDbType.SmallInt, unchecked((short)it.Value[3])),
        SqlProc.In("@p25", SqlDbType.SmallInt, unchecked((short)it.Value[4])),
        SqlProc.In("@p26", SqlDbType.SmallInt, unchecked((short)it.Value[5])),
        SqlProc.In("@p27", SqlDbType.Int, unchecked((int)it.Ext[0])),
        SqlProc.In("@p28", SqlDbType.Int, unchecked((int)it.Ext[1])),
        SqlProc.In("@p29", SqlDbType.Int, unchecked((int)it.Ext[2])),
        SqlProc.In("@p30", SqlDbType.Int, unchecked((int)it.Ext[3])),
        SqlProc.In("@p31", SqlDbType.Int, unchecked((int)it.Ext[4])),
        SqlProc.In("@p32", SqlDbType.Int, unchecked((int)it.Ext[5])),
        SqlProc.In("@p33", SqlDbType.TinyInt, it.Gem),
        SqlProc.In("@p34", SqlDbType.SmallInt, unchecked((short)it.MoggItemId)),
    };

    /// <summary>C++ <c>TSaveItemDirect</c> (CSPSaveItemDirect) — a single-item upsert straight to the live
    /// <c>TITEMTABLE</c> (delete-by-<c>dlID</c>-then-insert), no staging bracket. The incremental fast-path: one
    /// row per changed item so an item change survives a crash before the next full rewrite. Same 35-value param
    /// set as <c>TSaveItem</c>. Missing proc (2812) tolerated.</summary>
    public async Task SaveItemDirectAsync(uint charId, ItemSaveData item, CancellationToken ct = default)
    {
        try
        {
            await using var c = await OpenAsync(ct);
            await SqlProc.ExecAsync(c, "TSaveItemDirect", SqlProc.Ret(), ItemArgs(unchecked((int)charId), item), ct);
        }
        catch (SqlException ex) when (ex.Number == 2812) { /* proc absent — tolerate */ }
    }

    /// <summary>Removes one item row from the live <c>TITEMTABLE</c> by its primary key — the incremental
    /// counterpart of <see cref="SaveItemDirectAsync"/> for a consumed/dropped/sold item. A single-row delete by
    /// <c>dlID</c> (exactly what <c>TSaveItemDirect</c> itself does before re-inserting); it never touches a
    /// shared table. There is no dedicated delete proc in the baseline, so this is a parameterized statement.</summary>
    public async Task DeleteItemDirectAsync(long dlId, CancellationToken ct = default)
    {
        await using var c = await OpenAsync(ct);
        await using var cmd = new SqlCommand("DELETE FROM TITEMTABLE WHERE dlID = @dlID", c);
        cmd.Parameters.Add(SqlProc.In("@dlID", SqlDbType.BigInt, dlId));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>The inverse of <see cref="ToTime64"/> — a __time64_t (seconds since 1970) back to a SQL
    /// <c>smalldatetime</c>. Mirrors the C++ <c>__TIMETODB</c> (TNetDef.h): <b>0 (no expiry) → 1900-01-01</b>,
    /// the sentinel the game reads back as 0 (<c>__DBTOTIME</c>: <c>year &lt; 2000 ⇒ 0</c>). It is <b>never
    /// <see cref="DBNull"/></b> — the <c>dEndTime</c> columns (incl. the staging <c>TTEMPINVENTABLE</c>) are
    /// <c>NOT NULL</c>, so a NULL would fail the insert.</summary>
    private static object FromTime64(long secs) => secs <= 0
        ? new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        : new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(secs);

    private const string ItemSql = @"
SELECT bItemID, wItemID, bLevel, bGradeEffect, dwTime3, dwTime4, dwTime6, wMoggItemID
FROM TITEMTABLE
WHERE dwOwnerID = @dwOwnerID AND bOwnerType = @bOwnerType AND bStorageType = @bStorageType AND dwStorageID = @dwStorageID";

    /// <summary>Items in a storage slot. CTBLItem (subset; dwTime3/4/6 alias color/regGuild/customTex per DBAccess.h).</summary>
    public async Task<List<ItemLoadRow>> ItemListAsync(uint ownerId, byte ownerType, byte storageType, uint storageId, CancellationToken ct = default)
    {
        await using var c = await OpenAsync(ct);
        await using var cmd = new SqlCommand(ItemSql, c);
        cmd.Parameters.Add(SqlProc.In("@dwOwnerID", SqlDbType.Int, unchecked((int)ownerId)));
        cmd.Parameters.Add(SqlProc.In("@bOwnerType", SqlDbType.TinyInt, ownerType));
        cmd.Parameters.Add(SqlProc.In("@bStorageType", SqlDbType.TinyInt, storageType));
        cmd.Parameters.Add(SqlProc.In("@dwStorageID", SqlDbType.Int, unchecked((int)storageId)));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var list = new List<ItemLoadRow>();
        while (await r.ReadAsync(ct))
            list.Add(new ItemLoadRow(
                r.GetByteSafe(0), r.GetUShortSafe(1), r.GetByteSafe(2), r.GetByteSafe(3),
                r.GetUShortSafe(4), (byte)r.GetUIntSafe(5), r.GetUShortSafe(6), r.GetUShortSafe(7)));
        return list;
    }

    // ================= Phase-2: full inventory / gear / skills / hotkeys load =================

    private static long ToTime64(SqlDataReader r, int i)
    {
        // TITEMTABLE/TINVENTABLE dEndTime is a smalldatetime; the wire wants __time64_t (seconds since 1970).
        // Mirrors C++ __DBTOTIME (TNetDef.h): a pre-2000 date — chiefly the 1900-01-01 no-expiry sentinel that
        // FromTime64 writes for 0 — reads back as 0.
        if (r.IsDBNull(i)) return 0;
        var dt = Convert.ToDateTime(r.GetValue(i));
        if (dt.Year < 2000) return 0;
        var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        long secs = (long)(DateTime.SpecifyKind(dt, DateTimeKind.Utc) - epoch).TotalSeconds;
        return secs < 0 ? 0 : secs;
    }

    private const string InvenSql =
        "SELECT bInvenID, wItemID, dEndTime, bELD FROM TINVENTABLE WHERE dwCharID = @dwCharID";

    /// <summary>CTBLInven — the character's inventory containers (bags), excluding the implicit backpack/equip.</summary>
    public async Task<List<InvenLoadRow>> LoadInvensAsync(uint charId, CancellationToken ct = default)
    {
        await using var c = await OpenAsync(ct);
        await using var cmd = new SqlCommand(InvenSql, c);
        cmd.Parameters.Add(SqlProc.In("@dwCharID", SqlDbType.Int, unchecked((int)charId)));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var list = new List<InvenLoadRow>();
        while (await r.ReadAsync(ct))
            list.Add(new InvenLoadRow(r.GetByteSafe(0), r.GetUShortSafe(1), ToTime64(r, 2), r.GetByteSafe(3)));
        return list;
    }

    // CTBLItem — the 32 columns the map needs (of the C++ 35-column CTBLItem read in DBAccess.h; dlID/
    // bOwnerType/dwOwnerID are omitted). dwTime1..6 are the m_dwExtValue[] array (not timers).
    private const string FullItemSql = @"
SELECT bStorageType, dwStorageID, bItemID, wItemID, bLevel, bCount, bGLevel, dwDuraMax, dwDuraCur,
       bRefineCur, dEndTime, bGradeEffect,
       bMagic1, bMagic2, bMagic3, bMagic4, bMagic5, bMagic6,
       wValue1, wValue2, wValue3, wValue4, wValue5, wValue6,
       dwTime1, dwTime2, dwTime3, dwTime4, dwTime5, dwTime6,
       bGem, wMoggItemID, dlID
FROM TITEMTABLE WHERE dwOwnerID = @dwOwnerID AND bOwnerType = @bOwnerType AND bStorageType <> 2";

    /// <summary>CTBLItem — every item the character owns (all storages except type 2), with full attributes.</summary>
    public async Task<List<FullItemRow>> LoadItemsAsync(uint charId, CancellationToken ct = default)
    {
        await using var c = await OpenAsync(ct);
        await using var cmd = new SqlCommand(FullItemSql, c);
        cmd.Parameters.Add(SqlProc.In("@dwOwnerID", SqlDbType.Int, unchecked((int)charId)));
        cmd.Parameters.Add(SqlProc.In("@bOwnerType", SqlDbType.TinyInt, Proto.OwnerChar));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var list = new List<FullItemRow>();
        while (await r.ReadAsync(ct))
        {
            var magic = new byte[6];
            var value = new ushort[6];
            var ext = new uint[6];
            for (int i = 0; i < 6; i++) magic[i] = r.GetByteSafe(12 + i);
            for (int i = 0; i < 6; i++) value[i] = r.GetUShortSafe(18 + i);
            for (int i = 0; i < 6; i++) ext[i] = r.GetUIntSafe(24 + i);
            list.Add(new FullItemRow(
                r.GetByteSafe(0), r.GetUIntSafe(1), r.GetByteSafe(2), r.GetUShortSafe(3), r.GetByteSafe(4),
                r.GetByteSafe(5), r.GetByteSafe(6), r.GetUIntSafe(7), r.GetUIntSafe(8), r.GetByteSafe(9),
                ToTime64(r, 10), r.GetByteSafe(11), magic, value, ext, r.GetByteSafe(30), r.GetUShortSafe(31),
                r.GetInt64Safe(32)));
        }
        return list;
    }

    private const string SkillSql =
        "SELECT wSkillID, bLevel, dwRemainTick FROM TSKILLTABLE WHERE dwCharID = @dwCharID";

    /// <summary>CTBLSkill — the character's learned skills.</summary>
    public async Task<List<SkillLoadRow>> LoadSkillsAsync(uint charId, CancellationToken ct = default)
    {
        await using var c = await OpenAsync(ct);
        await using var cmd = new SqlCommand(SkillSql, c);
        cmd.Parameters.Add(SqlProc.In("@dwCharID", SqlDbType.Int, unchecked((int)charId)));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var list = new List<SkillLoadRow>();
        while (await r.ReadAsync(ct))
            list.Add(new SkillLoadRow(r.GetUShortSafe(0), r.GetByteSafe(1), r.GetUIntSafe(2)));
        return list;
    }

    private const string MaintainSql = @"
SELECT wSkillID, bLevel, dwRemainTick, bAttackType, dwAttackID, bHostType, dwHostID, bAttackCountry
FROM TSKILLMAINTAINTABLE WHERE dwCharID = @dwCharID";

    /// <summary>CTBLSkillMaintain — the character's active maintained/buff skills (8 persisted fields).</summary>
    public async Task<List<MaintainLoadRow>> LoadMaintainAsync(uint charId, CancellationToken ct = default)
    {
        await using var c = await OpenAsync(ct);
        await using var cmd = new SqlCommand(MaintainSql, c);
        cmd.Parameters.Add(SqlProc.In("@dwCharID", SqlDbType.Int, unchecked((int)charId)));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var list = new List<MaintainLoadRow>();
        while (await r.ReadAsync(ct))
            list.Add(new MaintainLoadRow(
                r.GetUShortSafe(0), r.GetByteSafe(1), r.GetUIntSafe(2), r.GetByteSafe(3),
                r.GetUIntSafe(4), r.GetByteSafe(5), r.GetUIntSafe(6), r.GetByteSafe(7)));
        return list;
    }

    private const string QuestTableSql =
        @"SELECT dwQuestID, dwTick, bCompleteCount, bTriggerCount FROM TQUESTTABLE WHERE dwCharID = @dwCharID";
    private const string QuestTermTableSql =
        @"SELECT dwQuestID, dwTermID, bTermType, bCount FROM TQUESTTERMTABLE WHERE dwCharID = @dwCharID";

    /// <summary>C++ <c>CTBLQuestTable</c> + <c>CTBLQuestTermTable</c> (DBAccess.h:2410-2468) — the character's
    /// saved quest progress (headers + running term counters), keyed by <c>dwCharID</c>. The read-back
    /// counterpart of the Phase-25 <c>TSaveQuest</c>/<c>TSaveQuestTerm</c> write.</summary>
    public async Task<(List<QuestLoadRow> quests, List<QuestTermLoadRow> terms)> LoadQuestsAsync(
        uint charId, CancellationToken ct = default)
    {
        await using var c = await OpenAsync(ct);
        var quests = new List<QuestLoadRow>();
        var terms = new List<QuestTermLoadRow>();
        int cid = unchecked((int)charId);

        await using (var cmd = new SqlCommand(QuestTableSql, c))
        {
            cmd.Parameters.Add(SqlProc.In("@dwCharID", SqlDbType.Int, cid));
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                quests.Add(new QuestLoadRow(r.GetUIntSafe(0), r.GetUIntSafe(1), r.GetByteSafe(2), r.GetByteSafe(3)));
        }
        await using (var cmd = new SqlCommand(QuestTermTableSql, c))
        {
            cmd.Parameters.Add(SqlProc.In("@dwCharID", SqlDbType.Int, cid));
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                terms.Add(new QuestTermLoadRow(r.GetUIntSafe(0), r.GetUIntSafe(1), r.GetByteSafe(2), r.GetByteSafe(3)));
        }
        return (quests, terms);
    }

    private const int HotkeyPosCount = 12; // MAX_HOTKEY_POS

    /// <summary>
    /// CTBLHotKey — hotkey pages. The C++ query is <c>SELECT *</c> bound positionally
    /// (dwCharID, bInven, then 12 × {bType, wHotID}). We read positionally too and tolerate a row whose
    /// shape doesn't match (skips hotkeys) so an unexpected DDL never breaks login.
    /// </summary>
    public async Task<List<HotkeyLoadRow>> LoadHotkeysAsync(uint charId, CancellationToken ct = default)
    {
        await using var c = await OpenAsync(ct);
        await using var cmd = new SqlCommand("SELECT * FROM THOTKEYTABLE WHERE dwCharID = @dwCharID", c);
        cmd.Parameters.Add(SqlProc.In("@dwCharID", SqlDbType.Int, unchecked((int)charId)));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var list = new List<HotkeyLoadRow>();
        const int expected = 2 + HotkeyPosCount * 2; // dwCharID, bInven, 12×{type,id}
        while (await r.ReadAsync(ct))
        {
            if (r.FieldCount < expected) break; // unexpected shape → skip hotkeys
            byte invenKey = r.GetByteSafe(1);
            var slots = new (byte, ushort)[HotkeyPosCount];
            for (int i = 0; i < HotkeyPosCount; i++)
                slots[i] = (r.GetByteSafe(2 + i * 2), r.GetUShortSafe(3 + i * 2));
            list.Add(new HotkeyLoadRow(invenKey, slots));
        }
        return list;
    }

    private const string CabinetSql =
        "SELECT bCabinetID, bUse FROM TCABINETTABLE WHERE dwCharID = @dwCharID ORDER BY bCabinetID";

    /// <summary>C++ <c>CTBLCabinetTable</c> (DBAccess.h:2845) — the character's cabinet open-state headers
    /// (which cabinets exist + their <c>bUse</c> flag). The cabinet <b>items</b> ride in the normal item load
    /// (<see cref="LoadItemsAsync"/>, <c>bStorageType=STORAGE_CABINET</c>). Phase 37.</summary>
    public async Task<List<CabinetHeaderRow>> LoadCabinetsAsync(uint charId, CancellationToken ct = default)
    {
        await using var c = await OpenAsync(ct);
        await using var cmd = new SqlCommand(CabinetSql, c);
        cmd.Parameters.Add(SqlProc.In("@dwCharID", SqlDbType.Int, unchecked((int)charId)));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var list = new List<CabinetHeaderRow>();
        while (await r.ReadAsync(ct))
            list.Add(new CabinetHeaderRow(r.GetByteSafe(0), r.GetByteSafe(1) != 0));
        return list;
    }

    /// <summary>C++ <c>TSaveCabinet</c> (CSPSaveCabinet, DBAccess.h:5688) — persists one cabinet's open-state
    /// (<c>bUse</c>). The cabinet's items persist separately via the incremental item save
    /// (<see cref="SaveItemDirectAsync"/> with <c>STORAGE_CABINET</c>). Missing proc (2812) tolerated.</summary>
    public async Task SaveCabinetAsync(uint charId, byte cabinetId, bool use, CancellationToken ct = default)
    {
        try
        {
            await using var c = await OpenAsync(ct);
            await SqlProc.ExecAsync(c, "TSaveCabinet", SqlProc.Ret(), new[]
            {
                SqlProc.In("@p0", SqlDbType.Int, unchecked((int)charId)),
                SqlProc.In("@p1", SqlDbType.TinyInt, cabinetId),
                SqlProc.In("@p2", SqlDbType.TinyInt, (byte)(use ? 1 : 0)),
            }, ct);
        }
        catch (SqlException ex) when (ex.Number == 2812) { /* proc absent — tolerate */ }
    }
}
