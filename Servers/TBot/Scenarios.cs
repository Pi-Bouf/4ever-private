using TLogin.Protocol;

namespace TBot;

/// <summary>
/// Scripted multi-bot checks against the live cluster. <c>features</c> logs two characters in side by side and
/// drives the party, mail, hotkey, bag and teleport flows end to end, checking each reply against what the real
/// client reads (TClient CSHandler.cpp) — every reply must be consumed to its last byte — and, after logout,
/// what the map server saved. The characters' money, HP, position and test items are put back at the end.
/// </summary>
public static class Scenarios
{
    // CS_MAP offsets (CSProtocol.h), mirrored from TMap.Protocol/NetCode.cs.
    private const ushort M = Msg.CS_MAP;
    private const ushort CS_LEAVE_ACK = M + 0x0007, CS_HPMP_ACK = M + 0x0022, CS_ADDITEM_ACK = M + 0x002B,
        CS_DELITEM_ACK = M + 0x002C, CS_MONEY_ACK = M + 0x0049,
        CS_PARTYADD_REQ = M + 0x0040, CS_PARTYADD_ACK = M + 0x0041, CS_PARTYJOINASK_ACK = M + 0x0042,
        CS_PARTYJOIN_REQ = M + 0x0043, CS_PARTYJOIN_ACK = M + 0x0044, CS_PARTYDEL_REQ = M + 0x0045,
        CS_PARTYDEL_ACK = M + 0x0046, CS_PARTYMANSTAT_ACK = M + 0x0047, CS_PARTYATTR_ACK = M + 0x0048,
        CS_CHGPARTYCHIEF_REQ = M + 0x0101, CS_CHGPARTYCHIEF_ACK = M + 0x0102,
        CS_CHGPARTYTYPE_REQ = M + 0x0140, CS_CHGPARTYTYPE_ACK = M + 0x0141,
        CS_HOTKEYADD_REQ = M + 0x009F, CS_HOTKEYDEL_REQ = M + 0x00A1, CS_HOTKEYCHANGE_ACK = M + 0x00A2,
        CS_TELEPORT_REQ = M + 0x00D7, CS_TELEPORT_ACK = M + 0x00D8, CS_BEGINTELEPORT_ACK = M + 0x0139,
        CS_INVENADD_REQ = M + 0x00E3, CS_INVENADD_ACK = M + 0x00E4, CS_INVENDEL_REQ = M + 0x00E5,
        CS_INVENDEL_ACK = M + 0x00E6, CS_INVENMOVE_REQ = M + 0x00E7, CS_INVENMOVE_ACK = M + 0x00E8,
        CS_POSTSEND_REQ = M + 0x0122, CS_POSTSEND_ACK = M + 0x0123, CS_POSTRECV_ACK = M + 0x0124,
        CS_POSTLIST_ACK = M + 0x0125, CS_POSTVIEW_REQ = M + 0x0126, CS_POSTVIEW_ACK = M + 0x0127,
        CS_POSTDEL_REQ = M + 0x0128, CS_POSTDEL_ACK = M + 0x0129, CS_POSTGETITEM_REQ = M + 0x012A,
        CS_POSTGETITEM_ACK = M + 0x012B, CS_POSTLIST_REQ = M + 0x0225,
        CS_PETMAKE_REQ = M + 0x012E, CS_PETMAKE_ACK = M + 0x012F, CS_PETLIST_ACK = M + 0x0132,
        CS_PETRECALL_REQ = M + 0x0133, CS_PETRECALL_ACK = M + 0x0134, CS_PETRIDING_REQ = M + 0x0135,
        CS_PETRIDING_ACK = M + 0x0136, CS_PETCANCEL_REQ = M + 0x01FB, CS_SENDSADDLE_REQ = M + 0x0320,
        CS_ADDRECALLMON_ACK = M + 0x00D9, CS_DELRECALLMON_ACK = M + 0x00DA,
        CS_COMPANIONLIST_ACK = M + 0x0329, CS_COMPANIONRECALL_REQ = M + 0x032E, CS_CREATECOMPANION_REQ = M + 0x033E,
        CS_UPDATESPAWNEDCOMPANION_REQ = M + 0x0340, CS_CREATECOMPANION_ACK = M + 0x0341, CS_ADDSPOLECNIKMON_ACK = M + 0x0362,
        CS_DELSPOLECNIKMON_ACK = M + 0x0364,
        CS_SKILLUSE_REQ = M + 0x0034, CS_SKILLUSE_ACK = M + 0x0035, CS_DEFEND_ACK = M + 0x0021, CS_ADDMON_ACK = M + 0x0011,
        CS_FINISHSKILL_ACK = M + 0x0377, CS_DELRECALLMON_REQ = M + 0x00E1, CS_ADDSELFOBJ_ACK = M + 0x00F4,
        CS_DELSELFOBJ_ACK = M + 0x00F5,
        CS_DUELINVITE_REQ = M + 0x0147, CS_DUELINVITE_ACK = M + 0x0148, CS_DUELINVITEREPLY_REQ = M + 0x0149,
        CS_DUELSTART_ACK = M + 0x014A, CS_DUELEND_ACK = M + 0x014C, CS_DUELSTANDBY_ACK = M + 0x014D, CS_SYSTEMMSG_ACK = M + 0x01D1,
        CS_PVPRECORD_REQ = M + 0x01E9, CS_PVPRECORD_ACK = M + 0x01EA, CS_MONTHRANKLIST_REQ = M + 0x021D, CS_MONTHRANKLIST_ACK = M + 0x021E,
        CS_TITLEGAIN_ACK = M + 0x0277, CS_CHARSTATINFO_ACK = M + 0x00A4,
        CS_ITEMUSE_REQ = M + 0x004A, CS_ITEMUSE_ACK = M + 0x004B, CS_CHANGECHARBASE_ACK = M + 0x01CA,
        CS_OPENMONEY_ACK = M + 0x01D3, CS_RESETPCBANG_ACK = M + 0x01B9, CS_EXP_ACK = M + 0x0024,
        CS_CHANGENAME_REQ = M + 0x01C9, CS_CHANGECOUNTRY_REQ = M + 0x024B,
        CS_CHGMODE_REQ = M + 0x001C, CS_CHGMODE_ACK = M + 0x001D, CS_CANCELACTION_REQ = M + 0x00A5, CS_CANCELACTION_ACK = M + 0x00A6,
        CS_GETTARGET_REQ = M + 0x014E, CS_GETTARGET_ACK = M + 0x014F, CS_GETTARGETANS_REQ = M + 0x0150, CS_GETTARGETANS_ACK = M + 0x0151,
        CS_HELMETHIDE_REQ = M + 0x018E, CS_HELMETHIDE_ACK = M + 0x018F, CS_COMMENT_REQ = M + 0x01E0, CS_COMMENT_ACK = M + 0x01E1,
        CS_LOOPSKILL_REQ = M + 0x00F2, CS_LOOPSKILL_ACK = M + 0x00F3,
        CS_CANCELSKILL_REQ = M + 0x0146, CS_QUESTUPDATE_ACK = M + 0x0053, CS_QUESTENDTIMER_REQ = M + 0x0058,
        CS_QUESTPOSEXEC_REQ = M + 0x0354, CS_MONITEMTAKEALL_REQ = M + 0x0152, CS_MONITEMTAKE_ACK = M + 0x008B,
        CS_GUILDLOCALLIST_REQ = M + 0x0155, CS_GUILDLOCALLIST_ACK = M + 0x0156, CS_REGION_REQ = M + 0x00F1,
        CS_ITEMLEVELREVISION_ACK = M + 0x0245,
        CS_DIE_ACK = M + 0x0025, CS_REVIVAL_REQ = M + 0x0026, CS_REVIVAL_ACK = M + 0x0027, CS_PVPPOINT_ACK = M + 0x01E2,
        CS_MOVEITEM_REQ = M + 0x0028, CS_MOVEITEM_ACK = M + 0x0029, CS_SKILLEND_ACK = M + 0x0037,
        CS_SKILLBUY_REQ = M + 0x0032, CS_SKILLBUY_ACK = M + 0x0033, CS_NPCITEMLIST_REQ = M + 0x0082, CS_NPCITEMLIST_ACK = M + 0x0083;

    // Test fixtures.
    private const ushort FarPortal = 4500;           // map 0 (3726.9, 146.0): ~390 m from the start area
    private const ushort BagItem = 4;                // Cloth Bag (IT_INVEN, level 5)
    private const byte BagSlot = 40;                 // a free backpack slot for it
    private const long BagDlId = 900_000_001;        // well outside any id range a map server hands out
    private const byte HotkeyPage = 2, HotkeySkill = 3;
    private const uint TestCooper = 1000, MailCooper = 5, MailCost = 100;
    private const ushort HorseItem = 7549, HorsePet = 13, StarterPet = 2;   // White Horse (permanent mount 13)
    private const byte HorseSlot = 41;
    private const long HorseDlId = 900_000_002;
    private const string HorseName = "TBotHorse";
    private const ushort RuneItem = 19019, RuneSpecies = 31125;   // Suckling Rune → Suckling (TCOMPANIONRUNECHART)
    private const byte RuneSlot = 42;
    private const long RuneDlId = 900_000_003;
    private const string PalName = "TBotPal";
    private const ushort RitualSkill = 623, RitualMon = 21100, IceRainSkill = 424, IceRainMon = 20001, SummonAttack = 700;
    // Skill learning: the skill window's trainer (TDEF_SKILL_NPC) and a ranger skill the bot holds at level 0 (learnable at
    // 4, 1 point; level 2 needs character level 4 + 16 = 20, the bot is 19). Price = TLEVELCHART(4).dwMoney 211 × fPrice 1.0275.
    private const ushort SkillNpc = 22047, SandSkill = 209, SandPrice = 216, BotSkillPoints = 200;
    // Passives: a self-buff that needs a weapon of kind 1, 8 or 9 (dwWeaponID 385) — the bot wields a kind-1 weapon in
    // equip slot 0 — and a kind-13 item (all classes, equip slot 8) whose mastery a ranger does not have.
    private const ushort WeaponBuff = 427, MasteryItem = 10158;
    private const byte WeaponSlot = 0, SpareSlot = 43, MasteryItemSlot = 44, MasteryEquipSlot = 8;
    private const long MasteryDlId = 900_000_004;
    // PvP: the bot's basic melee attack; a level-19 victim is worth 14 (TLEVELCHART.wPvPoint) — an even kill pays 80% (11)
    // and costs the victim 20% (2) of its total. B starts with a total of 10.
    private const ushort BasicMelee = 31;
    // A debuff: skill 213 hits and lowers the target's defence for 9 s (level 1). Chan1 is given it for the run.
    private const ushort DefenceBreak = 213;
    // Use items: a Chocolate Rudolf (a random buff, one of 1302-1306) and a Potion of Gender Alteration (IK_SEX).
    private const ushort ChocolateItem = 18007, ChocolateFirst = 1302, ChocolateLast = 1306, GenderItem = 7623;
    private const byte ChocolateSlot = 45, GenderSlot = 46, IkSex = 49;
    private const long ChocolateDlId = 900_000_005, GenderDlId = 900_000_006;
    // More use items, in slots 47-52: a pirate box (1 gold), a level-5 reward box, two growth potions, an XP Plus premium and a
    // book of wisdom (+100 exp).
    private static readonly (long DlId, byte Slot, ushort Item)[] UseItems =
    {
        (900_000_007, 47, 7957), (900_000_008, 48, 18148), (900_000_009, 49, 7631), (900_000_010, 50, 7632),
        (900_000_011, 51, 7402), (900_000_012, 52, 8701), (900_000_013, 53, NameItem), (900_000_014, 54, NameItem),
    };
    private const ushort NameItem = 7624;
    private const string NewName = "TbotRenamed";
    private const ushort MoneyBox = 7957, RewardBox = 18148, Growth1h = 7631, Growth3h = 7632, XpPlus = 7402, Wisdom = 8701;
    private const ushort XpPlusSkill = 901, ExpBuffSkill = 903, ShootSkill = 32;
    // Magic Attack (1407): a 6-missile multi-attack loop skill with no MP cost. Two quests: 5756 has a timer term, 47 a hunt term.
    private const ushort MissileSkill = 1407;
    private const uint TimerQuest = 5756, TimerTerm = 300000, HuntQuest = 47, HuntTerm = 1191;
    private const uint DefenceBreakMs = 9000;
    // Combat leftovers, all on A (B only strikes — A's HP is refilled by the duel's knockout right after):
    // a hide (221: ends on attacking and on being hit), a self buff (218), B's dispel (326, strips buffs), and the
    // sorcerer's damage share (635) on A's Dark Ritual summon.
    private const ushort Hide = 221, SelfBuff = 218, Dispel = 326, DamageShare = 635;
    private const uint VictimTotal = 10, KillGain = 11, KillLoss = 2;
    // Ranking and titles: 11 total points pass the first honour title (TTITLECHART 56, more than 10). The bots' rank and
    // title rows are copied aside before the run and put back after (RankTables).
    private const ushort HonourTitle = 56;
    // Backed up before the run and put back after (by dwCharID); TSKILLMAINTAINTABLE because the map saves the bots' buffs.
    private static readonly string[] RankTables = { "TMONTHPVPOINTTABLE", "TPVPRECORDTABLE", "TTITLETABLE", "TSKILLMAINTAINTABLE" };
    private const byte InvenEquip = 0xFE, InvenBackpack = 0xFF;

    private static readonly List<(string Name, bool Ok, string Detail)> Results = new();

    public static async Task<int> RunAsync(BotConfig cfg, CancellationToken ct)
    {
        if (cfg.Scenario != "features") { Console.Error.WriteLine($"unknown scenario '{cfg.Scenario}'"); return 2; }
        if (cfg.Account2.Length == 0 || cfg.GameConnectionString.Length == 0)
        {
            Console.Error.WriteLine("the features scenario needs --Bot:Account2 and --Bot:GameConnectionString");
            return 2;
        }
        var db = new GameDb(cfg.GameConnectionString);
        var cfgA = Clone(cfg, cfg.Account, "[A]");
        var cfgB = Clone(cfg, cfg.Account2, "[B]");

        uint idA = await FirstChar(db, cfg.Account), idB = await FirstChar(db, cfg.Account2);
        var saved = await Snapshot(db, idA, idB);
        try
        {
            await Prepare(db, idA, idB);
            await RunFeatures(cfgA, cfgB, db, idA, idB);
        }
        finally
        {
            await Restore(db, saved, idA, idB);
        }

        Console.WriteLine();
        Console.WriteLine("==== results ====");
        foreach (var (name, ok, detail) in Results)
            Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}{(detail.Length > 0 ? "  — " + detail : "")}");
        int failed = Results.Count(r => !r.Ok);
        Console.WriteLine($"{Results.Count - failed}/{Results.Count} passed");
        return failed == 0 ? 0 : 1;
    }

    // ================================ the script ================================

    private static async Task RunFeatures(BotConfig cfgA, BotConfig cfgB, GameDb db, uint idA, uint idB)
    {
        using (var a = new Bot(cfgA))
        using (var b = new Bot(cfgB))
        {
            a.Enter();
            b.Enter();
            string nameA = a.Spawn.Name, nameB = b.Spawn.Name;
            Thread.Sleep(1000);

            LoginStatSheet(a);
            SmallRequests(a, b);
            Territory(a, b);
            Party(a, b, nameA, nameB);
            Mail(a, b, nameA, nameB);
            HotkeyAdd(a);
            Bags(b);
            Pets(a, b);
            Companions(a, b);
            Summons(a, b);
            Skills(a);
            Passives(a);
            CombatLeftovers(a, b);
            ItemSkills(a, b);
            MoreUseItems(a, b);
            await NameAndCountry(a, b, db, idA, nameA, nameB);
            Duel(a, b);
            PvP(a, b);
            Teleport(a, b);
            LeaveASummonOut(a);

            foreach (var bot in new[] { a, b })
                Check($"byte check: every packet to {bot.Tag} decodes with a valid checksum", bot.BadPackets == 0,
                    $"{bot.BadPackets} of {bot.Received.Count} corrupt");
            Report("byte check: " + a.Tag, ByteReport(a));
            Report("byte check: " + b.Tag, ByteReport(b));
        }

        // Both are out: the map server saves on logout.
        await Task.Delay(4000);
        await CheckSaved(db, idA, idB);

        // A second session removes the hotkey again, and that has to be saved too.
        using (var a = new Bot(cfgA))
        {
            a.Enter();
            var back = a.TryWait(CS_ADDRECALLMON_ACK, r => { r.ReadUInt32(); r.ReadUInt32(); return r.ReadUInt16() == RitualMon; }, 8000);
            Check("summons: the summon left out at logout comes back at login", back is not null, Describe(back));
            var pc = a.TryWait(CS_RESETPCBANG_ACK, r => r.ReadUInt32() == a.CharId, 8000);
            Check("use items: the premium comes back at login", pc is not null && Read(pc, r => { r.ReadUInt32(); return r.ReadByte(); }) == 2,
                Describe(pc));
            if (back is not null)
            {
                var mon = ParseAddRecall(back);
                a.Send(Req(CS_DELRECALLMON_REQ, w => { w.WriteUInt32(mon.MonId); w.WriteByte(7); }));
                a.TryWait(CS_DELRECALLMON_ACK, r => { r.ReadUInt32(); return r.ReadUInt32() == mon.MonId; }, 8000);
            }
            a.Send(Req(CS_HOTKEYDEL_REQ, w => { w.WriteByte(HotkeyPage); w.WriteByte(0); }));
            var comps = a.TryWait(CS_COMPANIONLIST_ACK);
            Check("companions: the new companion is listed after relog", comps is not null
                && ParseCompanionList(comps).Any(c => c.Name == PalName && c.MonId == RuneSpecies), Describe(comps));
            var list = a.TryWait(CS_PETLIST_ACK);
            Check("pets: the new pet is listed after relog", list is not null && ParsePetList(list).Any(p => p.Id == HorsePet && p.Name == HorseName),
                Describe(list));
            var r = a.TryWait(CS_HOTKEYCHANGE_ACK);
            Check("hotkey: delete answered", r is not null && ParseHotkey(r) is var h && h.Set == HotkeyPage
                && h.Slots.Count == 1 && h.Slots[0] == (0, 0, 0), Describe(r));
        }
        await Task.Delay(3000);
        var gone = await db.RowAsync("SELECT COUNT(*) FROM TRECALLMONTABLE WHERE dwOwnerID=@p0", (int)idA);
        Check("summons: dismissed, it is no longer saved", gone is not null && Convert.ToInt32(gone[0]) == 0, $"rows={gone?[0]}");
        var row = await db.RowAsync("SELECT COUNT(*) FROM THOTKEYTABLE WHERE dwCharID=@p0 AND bInvenID=@p1", (int)idA, (int)HotkeyPage);
        Check("hotkey: emptied page deleted from THOTKEYTABLE", row is not null && Convert.ToInt32(row[0]) == 0,
            $"rows={row?[0]}");
    }

    private static void Party(Bot a, Bot b, string nameA, string nameB)
    {
        a.Send(Req(CS_PARTYADD_REQ, w => { w.WriteString(nameB); w.WriteByte(0); }));

        var ask = b.TryWait(CS_PARTYJOINASK_ACK);
        if (!Check("party: invite reaches the target (JOINASK)", ask is not null && Read(ask, r => (r.ReadString(), r.ReadByte())) is var q
                && q.Item1 == nameA && q.Item2 == 0, Describe(ask)))
            return;

        b.Send(Req(CS_PARTYJOIN_REQ, w => { w.WriteString(nameA); w.WriteByte(0); w.WriteByte(0); })); // ASK_YES

        var joinA = a.TryWait(CS_PARTYJOIN_ACK);
        var joinB = b.TryWait(CS_PARTYJOIN_ACK);
        var ja = joinA is null ? null : ParseJoin(joinA);
        var jb = joinB is null ? null : ParseJoin(joinB);
        Check("party: chief is told the member joined", ja is not null && ja.Value.MemberId == b.CharId && ja.Value.PartyId != 0
            && ja.Value.ChiefId == a.CharId, ja?.ToString() ?? "no CS_PARTYJOIN_ACK");
        Check("party: member is told about the chief", jb is not null && jb.Value.MemberId == a.CharId
            && jb.Value.ChiefId == a.CharId, jb?.ToString() ?? "no CS_PARTYJOIN_ACK");
        if (ja is null) return;

        // The member was logged in at low HP: its regeneration ticks reach the chief as party HP bars.
        var stat = a.TryWait(CS_PARTYMANSTAT_ACK, r => r.ReadUInt32() == b.CharId, 20000);
        Check("party: member HP reaches the chief (MANSTAT)", stat is not null && Read(stat, r => (r.ReadUInt32(), r.ReadByte(),
            r.ReadByte(), r.ReadUInt32(), r.ReadUInt32(), r.ReadUInt32(), r.ReadUInt32())) is var st && st.Item6 > 0, Describe(stat));

        // PT_LOTTERY (3). Not PT_SOLO (1): that is the hidden one-player party, which masks every party field to 0.
        a.Send(Req(CS_CHGPARTYTYPE_REQ, w => w.WriteByte(3)));
        var type = a.TryWait(CS_CHGPARTYTYPE_ACK);
        var typeB = b.TryWait(CS_CHGPARTYTYPE_ACK);
        Check("party: loot type changed (chief)", type is not null && Read(type, r => (r.ReadByte(), r.ReadByte())) == (0, 3), Describe(type));
        Check("party: loot type changed (member told too)", typeB is not null && Read(typeB, r => (r.ReadByte(), r.ReadByte())) == (0, 3),
            Describe(typeB));

        a.Discard(CS_PARTYATTR_ACK);
        b.Discard(CS_PARTYATTR_ACK);
        a.Send(Req(CS_CHGPARTYCHIEF_REQ, w => w.WriteUInt32(b.CharId)));
        var attr = a.TryWait(CS_PARTYATTR_ACK, r => r.ReadUInt32() == a.CharId);
        Check("party: leadership handed over (ATTR chief = member)", attr is not null
            && Read(attr, r => (r.ReadUInt32(), r.ReadUInt16(), r.ReadUInt32(), r.ReadUInt16())) is var at && at.Item3 == b.CharId,
            Describe(attr));
        var chief = a.TryWait(CS_CHGPARTYCHIEF_ACK, timeoutMs: 1000);
        Check("party: old chief told the change worked (PARTY_CHGCHIEF)", chief is not null && Read(chief, r => r.ReadByte()) == 10,
            Describe(chief));

        // The new chief removes the old one.
        b.Send(Req(CS_PARTYDEL_REQ, w => w.WriteUInt32(a.CharId)));
        var delA = a.TryWait(CS_PARTYDEL_ACK);
        var delB = b.TryWait(CS_PARTYDEL_ACK);
        Check("party: kicked player told (kick=1)", delA is not null && Read(delA, r => (r.ReadUInt32(), r.ReadUInt32(),
            r.ReadUInt16(), r.ReadUInt16(), r.ReadByte())) is var da && da.Item1 == a.CharId && da.Item5 == 1, Describe(delA));
        Check("party: chief told of the kick", delB is not null && Read(delB, r => (r.ReadUInt32(), r.ReadUInt32(),
            r.ReadUInt16(), r.ReadUInt16(), r.ReadByte())).Item1 == a.CharId, Describe(delB));

        // A player not online: the world answers the inviter.
        a.Send(Req(CS_PARTYADD_REQ, w => { w.WriteString("NoSuchPlayerTBot"); w.WriteByte(0); }));
        var fail = a.TryWait(CS_PARTYADD_ACK);
        Check("party: invite to an unknown name refused", fail is not null && Read(fail, r => (r.ReadString(), r.ReadString(),
            r.ReadByte())) is var f && f.Item3 != 0, Describe(fail));
    }

    private static void Mail(Bot a, Bot b, string nameA, string nameB)
    {
        a.Discard(CS_MONEY_ACK);
        a.Send(PostSend("NoSuchPlayerTBot", "TBot", "x", 0));
        var bad = a.TryWait(CS_POSTSEND_ACK);
        Check("mail: unknown receiver refused (POST_NORECEIVER)", bad is not null && Read(bad, r => r.ReadByte()) == 1, Describe(bad));

        a.Send(PostSend(nameB, "TBot test", "hello from TBot", MailCooper));
        var sent = a.TryWait(CS_POSTSEND_ACK);
        Check("mail: sent", sent is not null && Read(sent, r => r.ReadByte()) == 0, Describe(sent));
        var money = a.TryWait(CS_MONEY_ACK);
        Check("mail: sender charged postage + money", money is not null && Read(money, r => (r.ReadUInt32(), r.ReadUInt32(),
            r.ReadUInt32())) is var m && m.Item3 == TestCooper - MailCost - MailCooper, Describe(money));

        var recv = b.TryWait(CS_POSTRECV_ACK);
        var rc = recv is null ? default : Read(recv, r => (r.ReadUInt32(), r.ReadByte(), r.ReadByte(), r.ReadString(),
            r.ReadString(), r.ReadInt64()));
        if (!Check("mail: receiver notified (POSTRECV)", recv is not null && rc.Item4 == nameA && rc.Item5 == "TBot test"
                && Math.Abs(rc.Item6 - DateTimeOffset.UtcNow.ToUnixTimeSeconds()) < 120, Describe(recv)))
            return;
        uint postId = rc.Item1;

        b.Send(Req(CS_POSTLIST_REQ, w => w.WriteUInt16(0)));
        var list = b.TryWait(CS_POSTLIST_ACK);
        bool listed = false;
        if (list is not null)
            Read(list, r =>
            {
                r.ReadUInt16(); r.ReadUInt16(); r.ReadUInt16();
                ushort n = r.ReadUInt16();
                for (int i = 0; i < n; i++)
                {
                    uint id = r.ReadUInt32(); r.ReadByte(); r.ReadByte(); string sender = r.ReadString();
                    r.ReadString(); r.ReadInt64(); r.ReadByte();
                    listed |= id == postId && sender == nameA;
                }
                return 0;
            });
        Check("mail: listed", listed, Describe(list));

        b.Send(Req(CS_POSTVIEW_REQ, w => w.WriteUInt32(postId)));
        var view = b.TryWait(CS_POSTVIEW_ACK);
        Check("mail: opened", view is not null && Read(view, r => (r.ReadUInt32(), r.ReadByte(), r.ReadString(), r.ReadUInt32(),
            r.ReadUInt32(), r.ReadUInt32(), r.ReadByte(), r.ReadByte())) is var v && v.Item1 == postId
            && v.Item3 == "hello from TBot" && v.Item6 == MailCooper && v.Item8 == 0, Describe(view));

        b.Discard(CS_MONEY_ACK);
        b.Send(Req(CS_POSTGETITEM_REQ, w => w.WriteUInt32(postId)));
        var got = b.TryWait(CS_POSTGETITEM_ACK);
        var bm = b.TryWait(CS_MONEY_ACK);
        Check("mail: money taken", got is not null && Read(got, r => r.ReadByte()) == 0 && bm is not null
            && Read(bm, r => (r.ReadUInt32(), r.ReadUInt32(), r.ReadUInt32())).Item3 == MailCooper, $"{Describe(got)} / {Describe(bm)}");

        b.Send(Req(CS_POSTDEL_REQ, w => w.WriteUInt32(postId)));
        var del = b.TryWait(CS_POSTDEL_ACK);
        Check("mail: deleted", del is not null && Read(del, r => r.ReadUInt32()) == postId, Describe(del));
    }

    private static void HotkeyAdd(Bot a)
    {
        a.Send(Req(CS_HOTKEYADD_REQ, w => { w.WriteByte(1); w.WriteUInt16(HotkeySkill); w.WriteByte(HotkeyPage); w.WriteByte(0); }));
        var r = a.TryWait(CS_HOTKEYCHANGE_ACK);
        Check("hotkey: added", r is not null && ParseHotkey(r) is var h && h.Set == HotkeyPage && h.Slots.Count == 1
            && h.Slots[0] == (0, 1, HotkeySkill), Describe(r));
    }

    private static void Bags(Bot b)
    {
        b.Send(Req(CS_INVENDEL_REQ, w => { w.WriteByte(3); w.WriteByte(0xFF); w.WriteByte(0xFF); }));
        var none = b.TryWait(CS_INVENDEL_ACK);
        Check("bags: removing a missing bag fails", none is not null && Read(none, r => (r.ReadByte(), r.ReadByte(), r.ReadByte())).Item1 == 5,
            Describe(none));

        b.Send(Req(CS_INVENADD_REQ, w => { w.WriteByte(0); w.WriteByte(0xFF); w.WriteByte(BagSlot); }));
        var gone = b.TryWait(CS_DELITEM_ACK);
        var add = b.TryWait(CS_INVENADD_ACK);
        Check("bags: bag equipped (item leaves the backpack)", gone is not null && Read(gone, r => (r.ReadByte(), r.ReadByte())) == (0xFF, BagSlot),
            Describe(gone));
        Check("bags: bag equipped (INVENADD)", add is not null && Read(add, r => (r.ReadByte(), r.ReadByte(), r.ReadUInt16(),
            r.ReadInt64(), r.ReadByte())) is var ad && ad.Item1 == 0 && ad.Item2 == 0 && ad.Item3 == BagItem && ad.Item5 == 1, Describe(add));

        b.Send(Req(CS_INVENMOVE_REQ, w => { w.WriteByte(0); w.WriteByte(1); }));
        var mv = b.TryWait(CS_INVENMOVE_ACK);
        Check("bags: bag moved to slot 1", mv is not null && Read(mv, r => (r.ReadByte(), r.ReadByte(), r.ReadByte())) == (0, 0, 1), Describe(mv));

        b.Send(Req(CS_INVENDEL_REQ, w => { w.WriteByte(1); w.WriteByte(0xFF); w.WriteByte(BagSlot); }));
        var back = b.TryWait(CS_ADDITEM_ACK);
        var del = b.TryWait(CS_INVENDEL_ACK);
        Check("bags: bag back in the backpack (ADDITEM)", back is not null && back.ReadByte() == 0xFF, Describe(back));
        Check("bags: bag removed (INVENDEL)", del is not null && Read(del, r => (r.ReadByte(), r.ReadByte(), r.ReadByte())) == (0, 1, 1), Describe(del));
    }

    private static void Teleport(Bot a, Bot b)
    {
        b.Send(Req(CS_TELEPORT_REQ, w => { w.WriteUInt16(0); w.WriteUInt16(FarPortal); }));
        var begin = b.TryWait(CS_BEGINTELEPORT_ACK);
        Check("teleport: loading screen (BEGINTELEPORT)", begin is not null && Read(begin, r => (r.ReadByte(), r.ReadUInt16())).Item2 == 0,
            Describe(begin));
        var tp = b.TryWait(CS_TELEPORT_ACK, timeoutMs: 8000);
        var t = tp is null ? default : Read(tp, r => (r.ReadByte(), r.ReadUInt32(), r.ReadByte(), r.ReadUInt32(), r.ReadUInt16(),
            r.ReadFloat(), r.ReadFloat(), r.ReadFloat()));
        Check("teleport: arrived at the portal (TELEPORT_ACK)", tp is not null && t.Item1 == 0 && t.Item2 == b.CharId && t.Item5 == 0
            && Math.Abs(t.Item6 - 3726.88f) < 1 && Math.Abs(t.Item8 - 146.04f) < 1, Describe(tp));

        var con = b.TryWait(GameMsg.CS_CONNECT_ACK, timeoutMs: 8000);
        Check("teleport: re-entry granted (CONNECT_ACK)", con is not null && con.ReadByte() == 0, Describe(con));
        if (con is null) return;
        b.Send(GamePackets.BuildConReady());

        var left = a.TryWait(CS_LEAVE_ACK, timeoutMs: 3000);
        Check("teleport: the other bot saw it leave (LEAVE_ACK)", left is not null, Describe(left));

        // Walk a few steps at the destination: a player not placed again would be dropped by the move guard.
        Thread.Sleep(500);
        for (int i = 1; i <= 5; i++)
        {
            b.Send(GamePackets.BuildMove(0, t.Item6 + i, t.Item7, t.Item8, 0, 0, 4, 4, 0, 0, 3f));
            Thread.Sleep(200);
        }
        Check("teleport: still connected after walking there", !b.Closed, "");
    }

    private static void Pets(Bot a, Bot b)
    {
        var list = a.TryWait(CS_PETLIST_ACK, timeoutMs: 1000);
        Check("pets: the account's pets are listed at login", list is not null && ParsePetList(list).Any(p => p.Id == StarterPet),
            Describe(list));
        Check("pets: the saddle state is sent at login", a.TryWait(CS_SENDSADDLE_REQ, timeoutMs: 1000) is { } sd
            && Read(sd, r => (r.ReadUInt32(), r.ReadByte(), r.ReadInt64(), r.ReadUInt32())).Item1 == 0, "");

        a.Send(Req(CS_PETMAKE_REQ, w => { w.WriteByte(0xFF); w.WriteByte(HorseSlot); w.WriteString(HorseName); }));
        var made = a.TryWait(CS_PETMAKE_ACK);
        Check("pets: a mount item makes a pet (permanent)", made is not null && Read(made, r => (r.ReadByte(), r.ReadUInt16(), r.ReadString(),
            r.ReadInt64())) == (0, HorsePet, HorseName, 0L), Describe(made));

        a.Send(Req(CS_PETRECALL_REQ, w => w.WriteUInt16(HorsePet)));
        var addA = a.TryWait(CS_ADDRECALLMON_ACK, timeoutMs: 8000);
        var addB = b.TryWait(CS_ADDRECALLMON_ACK, r => r.ReadUInt32() == a.CharId, 8000);
        var mount = addA is null ? default : ParseAddRecall(addA);
        Check("pets: calling the pet summons the mount (owner)", addA is not null && mount.Host == a.CharId && mount.PetId == HorsePet
            && mount.Name == HorseName && mount.RecallType == 7, addA is null ? Describe(a.TryWait(CS_PETRECALL_ACK, timeoutMs: 1)) : mount.ToString());
        Check("pets: the other player sees the mount", addB is not null && ParseAddRecall(addB).MonId == mount.MonId, Describe(addB));
        if (addA is null) return;

        a.Send(Req(CS_PETRIDING_REQ, w => { w.WriteUInt32(mount.MonId); w.WriteByte(1); }));
        var rideA = a.TryWait(CS_PETRIDING_ACK);
        var rideB = b.TryWait(CS_PETRIDING_ACK);
        Check("pets: mounting is shown to the rider", rideA is not null && Read(rideA, r => (r.ReadByte(), r.ReadUInt32(), r.ReadUInt32(),
            r.ReadByte())) == (0, a.CharId, mount.MonId, 1), Describe(rideA));
        Check("pets: mounting is shown to the other player", rideB is not null && Read(rideB, r => (r.ReadByte(), r.ReadUInt32(),
            r.ReadUInt32(), r.ReadByte())) == (0, a.CharId, mount.MonId, 1), Describe(rideB));

        a.Send(Req(CS_PETRIDING_REQ, w => { w.WriteUInt32(mount.MonId); w.WriteByte(2); }));
        var down = b.TryWait(CS_PETRIDING_ACK);
        Check("pets: dismounting is shown", down is not null && Read(down, r => (r.ReadByte(), r.ReadUInt32(), r.ReadUInt32(),
            r.ReadByte())).Item4 == 2, Describe(down));
        a.Discard(CS_PETRIDING_ACK);

        a.Send(Req(CS_PETCANCEL_REQ, _ => { }));
        var gone = b.TryWait(CS_DELRECALLMON_ACK, timeoutMs: 8000);
        var goneA = a.TryWait(CS_DELRECALLMON_ACK, timeoutMs: 8000);
        Check("pets: sending the mount away removes it for everyone", gone is not null && goneA is not null
            && Read(gone, r => (r.ReadUInt32(), r.ReadUInt32(), r.ReadByte(), r.ReadByte())).Item2 == mount.MonId
            && Read(goneA, r => (r.ReadUInt32(), r.ReadUInt32(), r.ReadByte(), r.ReadByte())).Item2 == mount.MonId,
            $"{Describe(gone)} / {Describe(goneA)}");
    }

    private static void Companions(Bot a, Bot b)
    {
        var none = a.TryWait(CS_COMPANIONLIST_ACK, timeoutMs: 1000);
        Check("companions: the (empty) list is sent at login", none is not null && ParseCompanionList(none).Count == 0, Describe(none));

        a.Send(Req(CS_CREATECOMPANION_REQ, w => { w.WriteByte(0xFF); w.WriteByte(RuneSlot); w.WriteString(PalName); }));
        var ack = a.TryWait(CS_CREATECOMPANION_ACK);
        Check("companions: a rune creates a companion (species stamped by the server)", ack is not null
            && Read(ack, r => (r.ReadByte(), r.ReadByte())) == (0, 0), Describe(ack));
        var list = a.TryWait(CS_COMPANIONLIST_ACK);
        var entry = list is null ? default : ParseCompanionList(list).FirstOrDefault();
        Check("companions: the list shows it", list is not null && entry.Name == PalName && entry.MonId == RuneSpecies && entry.Level == 1,
            Describe(list));
        var spawned = a.TryWait(CS_UPDATESPAWNEDCOMPANION_REQ, r => r.ReadByte() == 0, 8000);
        Check("companions: it is marked summoned", spawned is not null && Read(spawned, r => r.ReadByte()) == 0, Describe(spawned));

        var addA = a.TryWait(CS_ADDSPOLECNIKMON_ACK, timeoutMs: 8000);
        var addB = b.TryWait(CS_ADDSPOLECNIKMON_ACK, r => r.ReadUInt32() == a.CharId, 8000);
        var pal = addA is null ? default : ParseAddCompanion(addA);
        Check("companions: it appears for its owner", addA is not null && pal.Host == a.CharId && pal.TempId == RuneSpecies, pal.ToString());
        Check("companions: and for the other player", addB is not null && ParseAddCompanion(addB).MonId == pal.MonId, Describe(addB));
        if (addA is null) return;

        a.Send(Req(CS_COMPANIONRECALL_REQ, w => { w.WriteUInt32(RuneSpecies); w.WriteByte(0); }));   // same slot: dismiss
        var gone = b.TryWait(CS_DELSPOLECNIKMON_ACK, timeoutMs: 8000);
        Check("companions: summoning it again sends it away", gone is not null
            && Read(gone, r => (r.ReadUInt32(), r.ReadUInt32(), r.ReadByte())).Item2 == pal.MonId, Describe(gone));
        a.Discard(CS_DELSPOLECNIKMON_ACK);
    }

    private static PacketWriter FinishSkill(Bot a, uint objId, byte type, ushort skill, float x, float z, params (uint Id, byte Type)[] targets)
        => Req(CS_FINISHSKILL_ACK, w =>
        {
            w.WriteUInt32(a.CharId); w.WriteUInt32(objId); w.WriteByte(type);
            w.WriteFloat(x); w.WriteFloat(a.Spawn.Y); w.WriteFloat(z);
            w.WriteUInt16(skill); w.WriteUInt32(0); w.WriteUInt32(0); w.WriteUInt16(0);
            w.WriteByte((byte)targets.Length);
            foreach (var (id, t) in targets) { w.WriteUInt32(id); w.WriteByte(t); }
        });

    private static (byte Ret, byte Level, uint Points) ReadSkillBuy(PacketReader p) => Read(p, r =>
    {
        byte ret = r.ReadByte(); r.ReadUInt16(); byte lvl = r.ReadByte();
        r.ReadUInt32(); r.ReadUInt32(); r.ReadUInt32(); r.ReadUInt32();                  // Tick, gold, silver, copper
        uint points = r.ReadUInt16();
        for (int i = 0; i < 4; i++) r.ReadUInt16();                                      // the four tabs' spent points
        return (ret, lvl, points);
    });

    private static void Skills(Bot a)
    {
        // The skill window's "learn" button: CS_SKILLBUY_REQ to the virtual trainer.
        a.Send(Req(CS_SKILLBUY_REQ, w => { w.WriteUInt16(SkillNpc); w.WriteUInt16(SandSkill); }));
        var ack = a.TryWait(CS_SKILLBUY_ACK, r => { r.ReadByte(); return r.ReadUInt16() == SandSkill; });
        Check("skills: Throw Sand is learned from the skill window (level 0 → 1, one point)",
            ack is not null && ReadSkillBuy(ack) == (0, 1, BotSkillPoints - 1u), Describe(ack));

        a.Send(Req(CS_SKILLBUY_REQ, w => { w.WriteUInt16(SkillNpc); w.WriteUInt16(SandSkill); }));
        var again = a.TryWait(CS_SKILLBUY_ACK, r => { r.ReadByte(); return r.ReadUInt16() == SandSkill; });
        Check("skills: its level 2 waits for character level 20", again is not null && ReadSkillBuy(again) is { Ret: 5, Level: 1 },
            Describe(again));                                                            // SKILL_NEEDLEVELUP

        a.Send(Req(CS_NPCITEMLIST_REQ, w => w.WriteUInt16(SkillNpc)));
        var list = a.TryWait(CS_NPCITEMLIST_ACK, r => r.ReadUInt16() == SkillNpc);
        var ids = list is null ? new List<ushort>() : Read(list, r =>
        {
            r.ReadUInt16(); r.ReadByte(); r.ReadByte(); int n = r.ReadByte();
            var l = new List<ushort>(); for (int i = 0; i < n; i++) { l.Add(r.ReadUInt16()); r.ReadUInt32(); } return l;
        });
        Check("skills: the trainer lists what can be learned now (not Throw Sand's level 2)",
            list is not null && ids.Count > 0 && !ids.Contains(SandSkill), list is null ? "no reply" : $"{ids.Count} skills");
    }

    private static void Summons(Bot a, Bot b)
    {
        // A main summon (Dark Ritual): created through the world, seen by both.
        a.Send(FinishSkill(a, a.CharId, 1, RitualSkill, a.Spawn.X, a.Spawn.Z, (a.CharId, 1)));
        var addA = a.TryWait(CS_ADDRECALLMON_ACK, r => { r.ReadUInt32(); r.ReadUInt32(); return r.ReadUInt16() == RitualMon; }, 8000);
        var addB = b.TryWait(CS_ADDRECALLMON_ACK, r => { r.ReadUInt32(); r.ReadUInt32(); return r.ReadUInt16() == RitualMon; }, 8000);
        var ritual = addA is null ? default : ParseAddRecall(addA);
        Check("summons: Dark Ritual summons its creature (owner)", addA is not null && ritual.Host == a.CharId && ritual.RecallType == 1,
            addA is null ? "no reply" : ritual.ToString());
        Check("summons: the other player sees it", addB is not null && ParseAddRecall(addB).MonId == ritual.MonId, Describe(addB));
        if (addA is null) return;

        // Its owner's client makes it swing.
        a.Send(Req(CS_SKILLUSE_REQ, w =>
        {
            w.WriteUInt32(ritual.MonId); w.WriteByte(7); w.WriteByte(1); w.WriteUInt16(0); w.WriteUInt16(SummonAttack);
            w.WriteByte(0); w.WriteUInt32(0); w.WriteUInt32(0); w.WriteFloat(0); w.WriteFloat(0); w.WriteFloat(0); w.WriteByte(0);
        }));
        var use = a.TryWait(CS_SKILLUSE_ACK, r => { r.ReadByte(); return r.ReadUInt32() == ritual.MonId; });
        Check("summons: the summon's skill is announced", use is not null && Read(use, r =>
        {
            byte res = r.ReadByte(); r.ReadUInt32(); byte type = r.ReadByte(); r.ReadUInt16(); r.ReadUInt16(); r.ReadByte();
            r.ReadUInt32(); r.ReadUInt32(); r.ReadByte(); r.ReadUInt16(); r.ReadByte();
            for (int i = 0; i < 4; i++) r.ReadUInt32();
            r.ReadUInt16(); r.ReadUInt16(); for (int i = 0; i < 6; i++) r.ReadByte();
            r.ReadFloat(); r.ReadFloat(); r.ReadFloat(); byte n = r.ReadByte();
            for (int i = 0; i < n; i++) { r.ReadUInt32(); r.ReadByte(); }
            return (res, type);
        }) == (0, 7), Describe(use));

        // Its repeating attack (a loop skill on a summon), once the attack just announced is ready again (1.5 s).
        Thread.Sleep(1700);
        a.Discard(CS_LOOPSKILL_ACK);
        a.Send(Req(CS_LOOPSKILL_REQ, w =>
        {
            w.WriteUInt32(ritual.MonId); w.WriteByte(7); w.WriteByte(1); w.WriteUInt16(0); w.WriteUInt16(SummonAttack);
            w.WriteFloat(0); w.WriteFloat(0); w.WriteFloat(0); w.WriteByte(0);
        }));
        var sloop = a.TryWait(CS_LOOPSKILL_ACK, r => { r.ReadByte(); return r.ReadUInt32() == ritual.MonId; });
        Check("summons: the summon's loop skill is shown", sloop is not null && Read(sloop, r =>
        {
            byte res = r.ReadByte(); r.ReadUInt32(); r.ReadByte(); r.ReadUInt16(); r.ReadByte(); r.ReadUInt16(); r.ReadByte();
            for (int i = 0; i < 4; i++) r.ReadUInt32();
            for (int i = 0; i < 4; i++) r.ReadByte();
            r.ReadFloat(); r.ReadFloat(); r.ReadFloat();
            int n = r.ReadByte();
            for (int i = 0; i < n; i++) { r.ReadUInt32(); r.ReadByte(); }
            return res;
        }) == 0, Describe(sloop));
        b.Discard(CS_LOOPSKILL_ACK);

        // … and hit a monster nearby (the first one the owner was shown).
        var monRaw = a.Received.FirstOrDefault(pk => PacketHeader.ReadId(pk) == CS_ADDMON_ACK);
        if (monRaw is not null)
        {
            uint monId = BitConverter.ToUInt32(monRaw, PacketHeader.Size);
            a.Send(FinishSkill(a, ritual.MonId, 7, SummonAttack, a.Spawn.X, a.Spawn.Z, (monId, 2)));
            var hit = a.TryWait(CS_DEFEND_ACK, r => r.ReadUInt32() == ritual.MonId);
            Check("summons: the summon's hit lands on a monster (owner is the host)", hit is not null && Read(hit, r =>
            {
                r.ReadUInt32(); uint target = r.ReadUInt32(); byte atkType = r.ReadByte(); r.ReadByte(); uint host = r.ReadUInt32();
                r.ReadByte(); r.ReadUInt32(); r.ReadUInt32(); r.ReadByte(); r.ReadUInt32(); r.ReadByte(); r.ReadByte(); r.ReadUInt16();
                r.ReadByte(); for (int i = 0; i < 4; i++) r.ReadUInt32(); r.ReadByte(); r.ReadByte(); r.ReadByte(); r.ReadByte();
                r.ReadUInt16(); r.ReadByte(); r.ReadUInt16(); r.ReadByte();
                for (int i = 0; i < 6; i++) r.ReadFloat();
                byte n = r.ReadByte(); for (int i = 0; i < n; i++) { r.ReadByte(); r.ReadUInt32(); }
                return (target, atkType, host);
            }) == (monId, 7, a.CharId), Describe(hit));
        }

        // PvP: the other player strikes the summon, and the summon strikes back.
        a.Discard(CS_DEFEND_ACK); a.Discard(CS_HPMP_ACK); b.Discard(CS_DEFEND_ACK);
        b.Send(FinishSkill(b, b.CharId, 1, BasicMelee, b.Spawn.X, b.Spawn.Z, (ritual.MonId, 7)));
        var struck = a.TryWait(CS_DEFEND_ACK, r => r.ReadUInt32() == b.CharId && r.ReadUInt32() == ritual.MonId);
        Check("summons: another player's strike lands on the summon", struck is not null, Describe(struck));
        var bar = a.TryWait(CS_HPMP_ACK, r => r.ReadUInt32() == ritual.MonId && r.ReadByte() == 7);
        Check("summons: … and takes its HP", bar is not null && Read(bar, r =>
        {
            r.ReadUInt32(); r.ReadByte(); uint max = r.ReadUInt32(), hp = r.ReadUInt32(); r.ReadUInt32(); r.ReadUInt32();
            return hp < max;
        }), Describe(bar));
        b.Discard(CS_DEFEND_ACK);
        a.Send(FinishSkill(a, ritual.MonId, 7, SummonAttack, a.Spawn.X, a.Spawn.Z, (b.CharId, 1)));
        var back = b.TryWait(CS_DEFEND_ACK, r => r.ReadUInt32() == ritual.MonId && r.ReadUInt32() == b.CharId);
        Check("summons: the summon's hit lands on a player (PvP)", back is not null, Describe(back));
        Thread.Sleep(300);
        a.Discard(CS_DEFEND_ACK); b.Discard(CS_DEFEND_ACK); a.Discard(CS_HPMP_ACK); b.Discard(CS_HPMP_ACK);

        a.Send(Req(CS_DELRECALLMON_REQ, w => { w.WriteUInt32(ritual.MonId); w.WriteByte(7); }));
        var gone = b.TryWait(CS_DELRECALLMON_ACK, r => { r.ReadUInt32(); return r.ReadUInt32() == ritual.MonId; }, 8000);
        Check("summons: dismissing it removes it for everyone", gone is not null, Describe(gone));

        // A placed object (Ice Rain): made right here, no world round trip.
        a.Send(FinishSkill(a, a.CharId, 1, IceRainSkill, a.Spawn.X + 3, a.Spawn.Z + 3, (a.CharId, 1)));
        var selfA = a.TryWait(CS_ADDSELFOBJ_ACK);
        var selfB = b.TryWait(CS_ADDSELFOBJ_ACK);
        var obj = selfA is null ? default : Read(selfA, r =>
        {
            uint host = r.ReadUInt32(), id = r.ReadUInt32(); ushort tpl = r.ReadUInt16();
            r.ReadByte(); r.ReadByte(); r.ReadByte(); r.ReadByte();
            for (int i = 0; i < 4; i++) r.ReadUInt32();
            r.ReadFloat(); r.ReadFloat(); r.ReadFloat(); r.ReadUInt16(); r.ReadUInt16();
            r.ReadByte(); r.ReadByte(); r.ReadByte(); r.ReadUInt32(); byte type = r.ReadByte(); r.ReadByte(); r.ReadByte();
            r.ReadUInt16(); r.ReadByte(); for (int i = 0; i < 5; i++) r.ReadUInt32();
            byte n = r.ReadByte();
            for (int i = 0; i < n; i++)
            {
                r.ReadUInt16(); r.ReadByte(); r.ReadUInt32(); r.ReadUInt32(); r.ReadByte(); r.ReadUInt32(); r.ReadByte(); r.ReadByte();
                r.ReadUInt16(); r.ReadByte(); for (int k = 0; k < 4; k++) r.ReadUInt32(); r.ReadByte(); r.ReadByte();
                r.ReadFloat(); r.ReadFloat(); r.ReadFloat();
            }
            return (host, id, tpl, type);
        });
        Check("summons: Ice Rain places its object (owner)", selfA is not null && obj.host == a.CharId && obj.tpl == IceRainMon && obj.type == 4,
            Describe(selfA));
        Check("summons: the other player sees the placed object", selfB is not null, Describe(selfB));
        if (selfA is null) return;

        a.Send(Req(CS_DELRECALLMON_REQ, w => { w.WriteUInt32(obj.id); w.WriteByte(11); }));
        var del = b.TryWait(CS_DELSELFOBJ_ACK);
        Check("summons: dismissing the placed object removes it", del is not null
            && Read(del, r => (r.ReadUInt32(), r.ReadByte())).Item1 == obj.id, Describe(del));
        a.Discard(CS_DELSELFOBJ_ACK);
    }

    private readonly record struct AddCompanion(uint Host, uint MonId, ushort TempId);

    // TClient OnCS_ADDSPOLECNIKMON_ACK (CSHandler.cpp:7129) — every field.
    private static AddCompanion ParseAddCompanion(PacketReader p) => Read(p, r =>
    {
        uint host = r.ReadUInt32(), mon = r.ReadUInt32(); ushort temp = r.ReadUInt16(); r.ReadUInt16(); r.ReadByte();
        r.ReadString();
        r.ReadByte(); r.ReadByte(); r.ReadByte(); r.ReadByte();
        for (int i = 0; i < 4; i++) r.ReadUInt32();
        r.ReadFloat(); r.ReadFloat(); r.ReadFloat(); r.ReadUInt16(); r.ReadUInt16();
        r.ReadByte(); r.ReadByte(); r.ReadByte(); r.ReadByte(); r.ReadByte();
        r.ReadUInt32(); r.ReadByte(); r.ReadByte(); r.ReadByte();
        r.ReadUInt16(); r.ReadByte();
        for (int i = 0; i < 4; i++) r.ReadUInt32();
        return new AddCompanion(host, mon, temp);
    });

    // TClient OnCS_COMPANIONLIST_ACK (CSHandler.cpp:12533).
    private static List<(byte Slot, uint MonId, string Name, byte Level)> ParseCompanionList(PacketReader p) => Read(p, r =>
    {
        byte n = r.ReadByte();
        var list = new List<(byte, uint, string, byte)>();
        for (int i = 0; i < n; i++)
        {
            byte slot = r.ReadByte(); uint mon = r.ReadUInt32(); string name = r.ReadString();
            r.ReadUInt32(); r.ReadUInt32(); byte level = r.ReadByte(); r.ReadUInt32(); r.ReadByte(); r.ReadByte();
            for (int k = 0; k < 6; k++) r.ReadByte();
            for (int k = 0; k < 2; k++) { r.ReadUInt16(); r.ReadInt64(); }
            r.ReadUInt32(); r.ReadByte(); r.ReadFloat();
            list.Add((slot, mon, name, level));
        }
        return list;
    });

    private readonly record struct AddRecall(uint Host, uint MonId, ushort TempId, ushort PetId, string Name, byte RecallType);

    // TClient OnCS_ADDRECALLMON_ACK (CSHandler.cpp:7351) — every field, to the last byte.
    private static AddRecall ParseAddRecall(PacketReader p) => Read(p, r =>
    {
        uint host = r.ReadUInt32(), mon = r.ReadUInt32(); ushort temp = r.ReadUInt16(), pet = r.ReadUInt16(); r.ReadByte();
        string name = r.ReadString();
        r.ReadByte(); r.ReadByte(); r.ReadByte(); r.ReadByte();                    // country, aid, colour, level
        for (int i = 0; i < 4; i++) r.ReadUInt32();                                  // max hp, hp, max mp, mp
        r.ReadFloat(); r.ReadFloat(); r.ReadFloat(); r.ReadUInt16(); r.ReadUInt16();
        r.ReadByte(); r.ReadByte(); r.ReadByte(); r.ReadByte(); r.ReadByte();        // mouse, key, action, mode, new
        r.ReadUInt32(); byte type = r.ReadByte(); r.ReadByte(); r.ReadByte();          // region, recall type, hit, skill lvl
        r.ReadUInt16(); r.ReadByte();                                                // AL, atk level
        for (int i = 0; i < 5; i++) r.ReadUInt32();                                  // 4 powers + life tick
        r.ReadUInt32(); r.ReadByte();                                                // target
        byte n = r.ReadByte();
        for (int i = 0; i < n; i++)
        {
            r.ReadUInt16(); r.ReadByte(); r.ReadUInt32(); r.ReadUInt32(); r.ReadByte(); r.ReadUInt32(); r.ReadByte(); r.ReadByte();
            r.ReadUInt16(); r.ReadByte(); for (int k = 0; k < 4; k++) r.ReadUInt32(); r.ReadByte(); r.ReadByte();
            r.ReadFloat(); r.ReadFloat(); r.ReadFloat();
        }
        return new AddRecall(host, mon, temp, pet, name, type);
    });

    // TClient OnCS_PETLIST_ACK (CSHandler.cpp:10138).
    private static List<(ushort Id, string Name, long End, byte Effect)> ParsePetList(PacketReader p) => Read(p, r =>
    {
        byte n = r.ReadByte();
        var list = new List<(ushort, string, long, byte)>();
        for (int i = 0; i < n; i++) list.Add((r.ReadUInt16(), r.ReadString(), r.ReadInt64(), r.ReadByte()));
        return list;
    });

    /// <summary>The stat sheet comes at login: the client's skill cooldowns scale by the attack delay rates it carries.</summary>
    private static void LoginStatSheet(Bot a)
    {
        var sheet = a.TryWait(CS_CHARSTATINFO_ACK, r => r.ReadUInt32() == a.CharId, 3000);
        Check("login: the stat sheet comes with non-zero attack delay rates (skill cooldowns)", sheet is not null && Read(sheet, r =>
        {
            r.ReadUInt32();
            for (int i = 0; i < 6; i++) r.ReadUInt16();
            for (int i = 0; i < 8; i++) r.ReadUInt32();
            bool rates = r.ReadUInt32() != 0 & r.ReadUInt32() != 0 & r.ReadUInt32() != 0;
            r.ReadUInt16(); r.ReadUInt16(); r.ReadByte();                 // attack / defend level, crit
            r.ReadUInt32(); r.ReadUInt32(); r.ReadUInt32();               // magic AP / DP
            r.ReadUInt16(); r.ReadUInt16(); r.ReadByte(); r.ReadByte(); r.ReadByte();
            r.ReadUInt16(); r.ReadByte();                                 // skill points, aftermath
            return rates;
        }), Describe(sheet));
    }

    private static void Passives(Bot a)
    {
        // A weapon mastery is missing: the kind-13 item cannot be worn.
        a.Discard(CS_MOVEITEM_ACK);
        a.Send(MoveItem(InvenBackpack, MasteryItemSlot, InvenEquip, MasteryEquipSlot));
        var noSkill = a.TryWait(CS_MOVEITEM_ACK);
        Check("passives: an item whose weapon mastery is missing cannot be worn (MI_NOSKILL)",
            noSkill is not null && Read(noSkill, r => r.ReadByte()) == 9, Describe(noSkill));

        // A self-buff that needs the equipped weapon…
        a.Send(FinishSkill(a, a.CharId, 1, WeaponBuff, a.Spawn.X, a.Spawn.Z, (a.CharId, 1)));
        var buff = a.TryWait(CS_DEFEND_ACK, r => r.ReadUInt32() == a.CharId && r.ReadUInt32() == a.CharId);
        Check("passives: the weapon buff lands on its caster", buff is not null && IsMaintain(buff), Describe(buff));

        // … ends when the weapon is taken off.
        a.Discard(CS_SKILLEND_ACK);
        a.Send(MoveItem(InvenEquip, WeaponSlot, InvenBackpack, SpareSlot));
        var end = a.TryWait(CS_SKILLEND_ACK);
        Check("passives: taking the weapon off ends the buff that needs it", end is not null
            && Read(end, r => (r.ReadUInt32(), r.ReadByte(), r.ReadUInt16())) == (a.CharId, 1, WeaponBuff), Describe(end));

        // The weapon goes back on, and a buff cast now stays through an unrelated move.
        a.Discard(CS_MOVEITEM_ACK);
        a.Send(MoveItem(InvenBackpack, SpareSlot, InvenEquip, WeaponSlot));
        var back = a.TryWait(CS_MOVEITEM_ACK);
        Check("passives: the weapon goes back on", back is not null && Read(back, r => r.ReadByte()) == 0, Describe(back));
        a.Send(FinishSkill(a, a.CharId, 1, WeaponBuff, a.Spawn.X, a.Spawn.Z, (a.CharId, 1)));
        a.TryWait(CS_DEFEND_ACK, r => r.ReadUInt32() == a.CharId && r.ReadUInt32() == a.CharId);
        a.Discard(CS_SKILLEND_ACK);
        a.Send(MoveItem(InvenBackpack, MasteryItemSlot, InvenBackpack, SpareSlot));
        var kept = a.TryWait(CS_SKILLEND_ACK, timeoutMs: 1500);
        Check("passives: with the weapon on, the buff stays", kept is null, Describe(kept));
        a.Discard(CS_MOVEITEM_ACK);
    }

    private static void CombatLeftovers(Bot a, Bot b)
    {
        bool SelfCast(ushort skill)
        {
            a.Discard(CS_DEFEND_ACK);
            a.Send(FinishSkill(a, a.CharId, 1, skill, a.Spawn.X, a.Spawn.Z, (a.CharId, 1)));
            var ack = a.TryWait(CS_DEFEND_ACK, r => r.ReadUInt32() == a.CharId && r.ReadUInt32() == a.CharId);
            return ack is not null && ReadMaintain(ack).IsMaintain == 1;
        }
        PacketReader? Ended(ushort skill, int ms = 3000)
            => a.TryWait(CS_SKILLEND_ACK, r => r.ReadUInt32() == a.CharId && r.ReadByte() == 1 && r.ReadUInt16() == skill, ms);
        void StrikeA(ushort skill) => b.Send(FinishSkill(b, b.CharId, 1, skill, b.Spawn.X, b.Spawn.Z, (a.CharId, 1)));

        // Being hit ends a hide.
        a.Discard(CS_SKILLEND_ACK);
        Check("leftovers: the hide is taken", SelfCast(Hide), "no maintain on the self-cast");
        StrikeA(BasicMelee);
        Check("leftovers: being hit ends the hide (EraseBuffByDefend)", Ended(Hide) is not null, "no CS_SKILLEND_ACK for the hide");

        // Attacking ends it too.
        SelfCast(Hide);
        a.Send(Req(CS_SKILLUSE_REQ, w =>
        {
            w.WriteUInt32(a.CharId); w.WriteByte(1); w.WriteByte(1); w.WriteUInt16(0); w.WriteUInt16(BasicMelee); w.WriteByte(0);
            w.WriteUInt32(0); w.WriteUInt32(0); w.WriteFloat(a.Spawn.X); w.WriteFloat(a.Spawn.Y); w.WriteFloat(a.Spawn.Z);
            w.WriteByte(1); w.WriteUInt32(b.CharId); w.WriteByte(1); w.WriteByte(1);
        }));
        Check("leftovers: attacking ends the hide (EraseBuffByAttack)", Ended(Hide) is not null, "no CS_SKILLEND_ACK for the hide");
        a.Discard(CS_SKILLUSE_ACK);

        // An enemy's dispel strips a buff.
        Check("leftovers: the self buff is taken", SelfCast(SelfBuff), "no maintain on the self-cast");
        StrikeA(Dispel);
        Check("leftovers: an enemy's dispel strips it (SCT_POSREMOVE)", Ended(SelfBuff) is not null, "no CS_SKILLEND_ACK for the buff");

        // The summon takes a share of its owner's damage.
        a.Send(FinishSkill(a, a.CharId, 1, RitualSkill, a.Spawn.X, a.Spawn.Z, (a.CharId, 1)));
        var add = a.TryWait(CS_ADDRECALLMON_ACK, r => { r.ReadUInt32(); r.ReadUInt32(); return r.ReadUInt16() == RitualMon; }, 8000);
        if (add is null) { Check("leftovers: the Dark Ritual summon comes", false, "no CS_ADDRECALLMON_ACK"); return; }
        var ritual = ParseAddRecall(add);
        a.Discard(CS_DEFEND_ACK);
        a.Send(FinishSkill(a, a.CharId, 1, DamageShare, a.Spawn.X, a.Spawn.Z, (ritual.MonId, 7)));
        var shared = a.TryWait(CS_DEFEND_ACK, r => r.ReadUInt32() == a.CharId && r.ReadUInt32() == ritual.MonId);
        Check("leftovers: the damage-share buff lands on one's own summon", shared is not null && ReadMaintain(shared).IsMaintain == 1,
            Describe(shared));
        a.Discard(CS_HPMP_ACK);
        StrikeA(BasicMelee);
        var bar = a.TryWait(CS_HPMP_ACK, r => r.ReadUInt32() == ritual.MonId && r.ReadByte() == 7);
        Check("leftovers: the owner is hit, the summon takes part of it (DistributeSkill)", bar is not null && Read(bar, r =>
        {
            r.ReadUInt32(); r.ReadByte(); uint max = r.ReadUInt32(), hp = r.ReadUInt32(); r.ReadUInt32(); r.ReadUInt32();
            return hp < max;
        }), Describe(bar));
        a.Send(Req(CS_DELRECALLMON_REQ, w => { w.WriteUInt32(ritual.MonId); w.WriteByte(7); }));
        a.TryWait(CS_DELRECALLMON_ACK, r => { r.ReadUInt32(); return r.ReadUInt32() == ritual.MonId; }, 8000);
        Thread.Sleep(500);
        a.Discard(CS_DEFEND_ACK); b.Discard(CS_DEFEND_ACK); a.Discard(CS_HPMP_ACK); b.Discard(CS_HPMP_ACK); a.Discard(CS_SKILLEND_ACK);
    }

    /// <summary>Use items: a random-buff chocolate, then a gender potion that goes round the world.</summary>
    private static void ItemSkills(Bot a, Bot b)
    {
        PacketWriter Use(ushort item, byte slot) => Req(CS_ITEMUSE_REQ, w =>
        {
            w.WriteUInt16(item); w.WriteByte(0xFF); w.WriteByte(slot); w.WriteUInt16(0); w.WriteByte(0);
        });
        byte? Result() => a.TryWait(CS_ITEMUSE_ACK) is { } ack ? Read(ack, r => { byte res = r.ReadByte(); r.ReadUInt16(); r.ReadByte(); r.ReadUInt32(); return res; }) : null;

        a.Discard(CS_DEFEND_ACK); a.Discard(CS_ITEMUSE_ACK);
        a.Send(Use(ChocolateItem, ChocolateSlot));
        Check("items: the chocolate is eaten", Result() == 0, "no IU_SUCCESS");
        var buff = a.TryWait(CS_DEFEND_ACK, r => r.ReadUInt32() == a.CharId && r.ReadUInt32() == a.CharId);
        Check("items: one of its random buffs lands on the eater", buff is not null && ReadMaintain(buff).IsMaintain == 1
            && ReadSkillId(buff) is >= ChocolateFirst and <= ChocolateLast, Describe(buff));
        ChocolateBuff = buff is null ? (ushort)0 : ReadSkillId(buff);

        a.Discard(CS_CHANGECHARBASE_ACK); b.Discard(CS_CHANGECHARBASE_ACK);
        a.Send(Use(GenderItem, GenderSlot));
        Check("items: the gender potion is drunk", Result() == 0, "no IU_SUCCESS");
        (byte Type, byte Value)? Look(Bot bot) => bot.TryWait(CS_CHANGECHARBASE_ACK, r => { r.ReadByte(); return r.ReadUInt32() == a.CharId; })
            is { } ack ? Read(ack, r => { r.ReadByte(); r.ReadUInt32(); var tv = (r.ReadByte(), r.ReadByte()); r.ReadString(); r.ReadUInt16(); r.ReadUInt32(); return tv; }) : null;
        var mine = Look(a);
        Check("items: through the world, the drinker's sex changes", mine is { Type: IkSex }, mine?.ToString() ?? "no CS_CHANGECHARBASE_ACK");
        var seen = Look(b);
        Check("items: … and the player nearby sees it", seen == mine, seen?.ToString() ?? "no CS_CHANGECHARBASE_ACK");
    }

    /// <summary>The small requests: battle mode, a cancelled action, "who is your target", the helmet (through the world), the
    /// comment, and a loop skill (Shoot) with its repeat delay.</summary>
    private static void SmallRequests(Bot a, Bot b)
    {
        b.Discard(CS_CHGMODE_ACK);
        a.Send(Req(CS_CHGMODE_REQ, w => w.WriteByte(1)));
        var mode = b.TryWait(CS_CHGMODE_ACK, r => r.ReadUInt32() == a.CharId);
        Check("small: the battle mode is shown around", mode is not null && Read(mode, r => { r.ReadUInt32(); r.ReadByte(); return r.ReadByte(); }) == 1,
            Describe(mode));
        a.Send(Req(CS_CHGMODE_REQ, w => w.WriteByte(0)));
        b.TryWait(CS_CHGMODE_ACK, r => r.ReadUInt32() == a.CharId, 2000);

        a.Send(Req(CS_CANCELACTION_REQ, w => { w.WriteUInt32(a.CharId); w.WriteByte(1); }));
        Check("small: a cancelled action is shown around", b.TryWait(CS_CANCELACTION_ACK, r => r.ReadUInt32() == a.CharId) is not null,
            "no CS_CANCELACTION_ACK");

        a.Send(Req(CS_GETTARGET_REQ, w => w.WriteUInt32(b.CharId)));
        var ask = b.TryWait(CS_GETTARGETANS_ACK);
        Check("small: \"who is your target?\" reaches the other player", ask is not null && Read(ask, r => r.ReadUInt32()) == a.CharId, Describe(ask));
        b.Send(Req(CS_GETTARGETANS_REQ, w => { w.WriteUInt32(a.CharId); w.WriteUInt32(a.CharId); w.WriteByte(1); }));
        var answer = a.TryWait(CS_GETTARGET_ACK);
        Check("small: … and the answer comes back", answer is not null && Read(answer, r => (r.ReadUInt32(), r.ReadByte())) == (a.CharId, 1),
            Describe(answer));

        foreach (byte hide in new byte[] { 1, 0 })
        {
            b.Discard(CS_HELMETHIDE_ACK);
            a.Send(Req(CS_HELMETHIDE_REQ, w => w.WriteByte(hide)));
            var helm = b.TryWait(CS_HELMETHIDE_ACK, r => r.ReadUInt32() == a.CharId);
            Check($"small: the helmet {(hide == 1 ? "hidden" : "shown again")}, through the world", helm is not null
                && Read(helm, r => { r.ReadUInt32(); return r.ReadByte(); }) == hide, Describe(helm));
        }

        a.Send(Req(CS_COMMENT_REQ, w => w.WriteString("tbot was here")));
        var comment = b.TryWait(CS_COMMENT_ACK, r => r.ReadUInt32() == a.CharId);
        Check("small: the comment reaches the player nearby of the same country", comment is not null
            && Read(comment, r => { r.ReadUInt32(); return r.ReadString(); }) == "tbot was here", Describe(comment));

        PacketWriter Loop() => Req(CS_LOOPSKILL_REQ, w =>
        {
            w.WriteUInt32(a.CharId); w.WriteByte(1); w.WriteByte(1); w.WriteUInt16(0); w.WriteUInt16(ShootSkill);
            w.WriteFloat(a.Spawn.X); w.WriteFloat(a.Spawn.Y); w.WriteFloat(a.Spawn.Z);
            w.WriteByte(1); w.WriteUInt32(b.CharId); w.WriteByte(1); w.WriteByte(1);
        });
        b.Discard(CS_LOOPSKILL_ACK); a.Discard(CS_LOOPSKILL_ACK);
        a.Send(Loop());
        var loop = b.TryWait(CS_LOOPSKILL_ACK, r => { r.ReadByte(); return r.ReadUInt32() == a.CharId; });
        // CS_LOOPSKILL_ACK: result, attacker, type, skill, then on success the attack figures, ground point and targets.
        static byte LoopResult(PacketReader r)
        {
            byte res = r.ReadByte(); r.ReadUInt32(); r.ReadByte(); r.ReadUInt16();
            r.ReadByte(); r.ReadUInt16(); r.ReadByte();
            for (int i = 0; i < 4; i++) r.ReadUInt32();
            for (int i = 0; i < 4; i++) r.ReadByte();
            r.ReadFloat(); r.ReadFloat(); r.ReadFloat();
            int n = r.ReadByte();
            for (int i = 0; i < n; i++) { r.ReadUInt32(); r.ReadByte(); }
            return res;
        }
        Check("small: a loop skill (Shoot) is shown around", loop is not null && Read(loop, LoopResult) == 0, Describe(loop));
        a.Discard(CS_LOOPSKILL_ACK);
        a.Send(Loop());
        var again = a.TryWait(CS_LOOPSKILL_ACK);
        Check("small: … and repeats only after its loop delay (SKILL_SPEEDYUSE)", again is not null && Read(again, LoopResult) == 6,
            Describe(again));
        // A multi-missile loop skill: its missiles all land on the one target.
        a.Discard(CS_LOOPSKILL_ACK);
        a.Send(Req(CS_LOOPSKILL_REQ, w =>
        {
            w.WriteUInt32(a.CharId); w.WriteByte(1); w.WriteByte(1); w.WriteUInt16(0); w.WriteUInt16(MissileSkill);
            w.WriteFloat(a.Spawn.X); w.WriteFloat(a.Spawn.Y); w.WriteFloat(a.Spawn.Z);
            w.WriteByte(1); w.WriteUInt32(b.CharId); w.WriteByte(1); w.WriteByte(1);
        }));
        var missiles = a.TryWait(CS_LOOPSKILL_ACK, r => { r.ReadByte(); return r.ReadUInt32() == a.CharId; });
        var hits = missiles is null ? new List<uint>() : Read(missiles, r =>
        {
            r.ReadByte(); r.ReadUInt32(); r.ReadByte(); r.ReadUInt16(); r.ReadByte(); r.ReadUInt16(); r.ReadByte();
            for (int i = 0; i < 4; i++) r.ReadUInt32();
            for (int i = 0; i < 4; i++) r.ReadByte();
            r.ReadFloat(); r.ReadFloat(); r.ReadFloat();
            var ids = new List<uint>();
            int n = r.ReadByte();
            for (int i = 0; i < n; i++) { ids.Add(r.ReadUInt32()); r.ReadByte(); }
            return ids;
        });
        Check("small: a multi-missile loop skill (Magic Attack) fires several missiles at its target", hits.Count > 1 && hits.All(id => id == b.CharId),
            $"{hits.Count} hit(s)");

        // Cancelling a skill resets its cooldown: Mean Kick twice is too fast, after a cancel it goes.
        byte? Kick()
        {
            a.Discard(CS_SKILLUSE_ACK);
            a.Send(Req(CS_SKILLUSE_REQ, w =>
            {
                w.WriteUInt32(a.CharId); w.WriteByte(1); w.WriteByte(1); w.WriteUInt16(0); w.WriteUInt16(DefenceBreak); w.WriteByte(0);
                w.WriteUInt32(0); w.WriteUInt32(0); w.WriteFloat(a.Spawn.X); w.WriteFloat(a.Spawn.Y); w.WriteFloat(a.Spawn.Z);
                w.WriteByte(1); w.WriteUInt32(b.CharId); w.WriteByte(1); w.WriteByte(1);
            }));
            return a.TryWait(CS_SKILLUSE_ACK, r => { r.ReadByte(); return r.ReadUInt32() == a.CharId; }) is { } ack
                ? new PacketReader(Bot.Raw.TryGetValue(ack, out var raw) ? raw : throw new InvalidOperationException()).ReadByte() : null;
        }
        Kick();
        Check("small: a skill used again at once is too fast (SKILL_SPEEDYUSE)", Kick() == 6, "not refused");
        a.Send(Req(CS_CANCELSKILL_REQ, w => { w.WriteByte(1); w.WriteUInt32(a.CharId); w.WriteUInt16(DefenceBreak); }));
        Check("small: … after CS_CANCELSKILL_REQ it can be used again", Kick() == 0, "still refused");
        b.Discard(CS_SKILLUSE_ACK); b.Discard(CS_HPMP_ACK);

        // Quests: the client's timer ran out ⇒ the timed quest fails; a hunt term reached by position is done.
        a.Discard(CS_QUESTUPDATE_ACK);
        a.Send(Req(CS_QUESTENDTIMER_REQ, w => w.WriteUInt32(TimerQuest)));
        (uint, uint, byte, byte, byte)? Upd() => a.TryWait(CS_QUESTUPDATE_ACK) is { } u
            ? Read(u, r => (r.ReadUInt32(), r.ReadUInt32(), r.ReadByte(), r.ReadByte(), r.ReadByte())) : null;
        var failed = Upd();
        Check("quests: the timed quest fails when the client's timer runs out", failed == (TimerQuest, TimerTerm, 6, 0, 2), failed?.ToString() ?? "no update");
        a.Send(Req(CS_QUESTPOSEXEC_REQ, w => { w.WriteUInt32(HuntQuest); w.WriteUInt32(HuntTerm); }));
        var done = Upd();
        Check("quests: a hunt term reached by position is done", done == (HuntQuest, HuntTerm, 3, 1, 1), done?.ToString() ?? "no update");

        // Take-all on a monster nearby (alive, nothing on it): answered, nothing taken.
        var monRaw = a.Received.FirstOrDefault(pk => PacketHeader.ReadId(pk) == CS_ADDMON_ACK);
        if (monRaw is not null)
        {
            a.Send(Req(CS_MONITEMTAKEALL_REQ, w => w.WriteUInt32(BitConverter.ToUInt32(monRaw, PacketHeader.Size))));
            var take = a.TryWait(CS_MONITEMTAKE_ACK);
            Check("loot: take-all is answered", take is not null && Read(take, r => r.ReadByte()) == 0, Describe(take));
        }
        Thread.Sleep(500);
        b.Discard(CS_LOOPSKILL_ACK);
    }

    // Territories (TBATTLEZONECHART): castle Chesed (4) and the first mission area (101, item cap 15).
    private const ushort ChesedCastle = 4, FirstMission = 101;
    private const byte SkillPeaceZone = 25;

    /// <summary>Territory battles, batch A: the war-info window, entering a territory (its item cap), a castle out of its war is a
    /// peace zone (for the caster and for a player standing in it), and the war phases forced on the world, with their news.</summary>
    private static void Territory(Bot a, Bot b)
    {
        // The war-info window: 4 castles (Chesed with 3 forts), then 8 missions and the sky garden.
        a.Send(Req(CS_GUILDLOCALLIST_REQ, _ => { }));
        var list = a.TryWait(CS_GUILDLOCALLIST_ACK);
        var shape = list is null ? default : Read(list, r =>
        {
            int castles = r.ReadUInt16(); int firstForts = -1;
            for (int c = 0; c < castles; c++)
            {
                r.ReadUInt16(); r.ReadString(); r.ReadByte(); r.ReadUInt32(); r.ReadString(); r.ReadByte(); r.ReadInt64();
                r.ReadString(); r.ReadString(); r.ReadString();
                r.ReadUInt16(); r.ReadUInt16(); r.ReadByte(); r.ReadUInt16(); r.ReadUInt16(); r.ReadByte(); r.ReadUInt16(); r.ReadByte();
                for (int side = 0; side < 2; side++) { int n = r.ReadByte(); for (int i = 0; i < n; i++) { r.ReadString(); r.ReadUInt16(); } }
                int forts = r.ReadUInt16();
                if (c == 0) firstForts = forts;
                for (int f = 0; f < forts; f++) { r.ReadUInt16(); r.ReadString(); r.ReadUInt32(); r.ReadString(); r.ReadByte(); r.ReadInt64(); r.ReadString(); r.ReadByte(); }
            }
            int missions = r.ReadByte();
            for (int m = 0; m < missions; m++) { r.ReadUInt16(); r.ReadString(); r.ReadByte(); r.ReadByte(); r.ReadInt64(); }
            int sky = r.ReadByte();
            for (int g = 0; g < sky; g++) { r.ReadUInt16(); r.ReadString(); r.ReadByte(); r.ReadByte(); r.ReadInt64(); }
            r.ReadByte(); r.ReadUInt32(); r.ReadByte(); r.ReadByte(); r.ReadUInt32(); r.ReadByte();
            return (castles, firstForts, missions, sky);
        });
        Check("territory: the war-info window lists 4 castles (Chesed with 3 forts), 8 missions and the sky garden",
            shape == (4, 3, 8, 1), list is null ? "no CS_GUILDLOCALLIST_ACK" : shape.ToString());

        PacketWriter Region(Bot bot, ushort local) => Req(CS_REGION_REQ, w =>
        {
            w.WriteUInt32(bot.CharId); w.WriteByte(1); w.WriteByte(1); w.WriteUInt16(0); w.WriteUInt32(0); w.WriteUInt16(local);
        });

        // A territory's item cap: in the first mission area, equipment counts as level 15 at most; out of it, as itself.
        a.Discard(CS_ITEMLEVELREVISION_ACK);
        a.Send(Region(a, FirstMission));
        var cap = a.TryWait(CS_ITEMLEVELREVISION_ACK);
        Check("territory: entering a mission area caps the equipment at its level (15)", cap is not null && Read(cap, r => r.ReadByte()) == 15, Describe(cap));
        a.Send(Region(a, 0));
        var uncap = a.TryWait(CS_ITEMLEVELREVISION_ACK);
        Check("territory: … and leaving it lifts the cap", uncap is not null && Read(uncap, r => r.ReadByte()) == 0, Describe(uncap));

        byte? Kick()
        {
            a.Discard(CS_SKILLUSE_ACK);
            a.Send(Req(CS_SKILLUSE_REQ, w =>
            {
                w.WriteUInt32(a.CharId); w.WriteByte(1); w.WriteByte(1); w.WriteUInt16(0); w.WriteUInt16(DefenceBreak); w.WriteByte(0);
                w.WriteUInt32(0); w.WriteUInt32(0); w.WriteFloat(a.Spawn.X); w.WriteFloat(a.Spawn.Y); w.WriteFloat(a.Spawn.Z);
                w.WriteByte(1); w.WriteUInt32(b.CharId); w.WriteByte(1); w.WriteByte(1);
            }));
            return a.TryWait(CS_SKILLUSE_ACK, r => { r.ReadByte(); return r.ReadUInt32() == a.CharId; }) is { } ack
                ? new PacketReader(Bot.Raw.TryGetValue(ack, out var raw) ? raw : throw new InvalidOperationException()).ReadByte() : null;
        }

        // A castle out of its war is a peace zone: no hostile skill from it…
        a.Send(Region(a, ChesedCastle));
        Thread.Sleep(300);
        Check("territory: in a castle out of its war, a hostile skill is refused (SKILL_PEACEZONE)", Kick() == SkillPeaceZone, "not refused");
        a.Send(Region(a, 0));

        // … nor on a player standing in it.
        b.Send(Region(b, ChesedCastle));
        Thread.Sleep(300);
        b.Discard(CS_DEFEND_ACK);
        a.Send(FinishSkill(a, a.CharId, 1, DefenceBreak, a.Spawn.X, a.Spawn.Z, (b.CharId, 1)));
        Check("territory: … nor does one land on a player standing in it", b.TryWait(CS_DEFEND_ACK, r => r.ReadUInt32() == a.CharId, 1500) is null,
            "a CS_DEFEND_ACK came");
        b.Send(Region(b, 0));

        // The war phases, forced on the world: the news reach the players.
        (byte Type, uint Second)? News(Bot bot, byte type) => bot.TryWait(CS_SYSTEMMSG_ACK, r => r.ReadByte() == type, 5000) is { } m
            ? Read(m, r => { byte t = r.ReadByte(); return (t, r.Remaining >= 4 ? r.ReadUInt32() : 0u); }) : null;
        a.Discard(CS_SYSTEMMSG_ACK); b.Discard(CS_SYSTEMMSG_ACK);
        WorldTool.BattleStatus(WorldTool.BtLocal, WorldTool.BsBattle, 0, 1800);
        Check("territory: the forts' war starts (SM_BATTLE_START), for both players", News(a, 2) is not null && News(b, 2) is not null, "no news");
        WorldTool.BattleStatus(WorldTool.BtLocal, WorldTool.BsPeace, 0, 180);
        var peace = News(a, 5);
        Check("territory: … and ends in 3 minutes of peace (SM_BATTLE_PEACE 180)", peace == (5, 180), peace?.ToString() ?? "no news");
        WorldTool.BattleStatus(WorldTool.BtLocal, WorldTool.BsNormal, 0, 0);
        Check("territory: … then back to normal (SM_BATTLE_NORMAL)", News(a, 1) is not null, "no news");

        // A castle war: no castle has both a defender and an attacker in this database, so (C++ EndWar(WIN_NOWAR)) it ends at
        // once and the castle stays a peace zone.
        Thread.Sleep(3200);                                                      // Mean Kick's cooldown
        WorldTool.BattleStatus(WorldTool.BtCastle, WorldTool.BsBattle, 0, 2700);
        Check("territory: the castle war starts (SM_CASTLE_START)", News(a, 12) is not null, "no news");
        a.Send(Region(a, ChesedCastle));
        Thread.Sleep(300);
        Check("territory: … with no defender and attacker it ends at once: still no hostile skill there", Kick() == SkillPeaceZone, "allowed");
        a.Send(Region(a, 0));
        WorldTool.BattleStatus(WorldTool.BtCastle, WorldTool.BsPeace, 0, 0);
        Check("territory: … the castle war ends (SM_CASTLE_PEACE)", News(a, 15) is not null, "no news");
        WorldTool.BattleStatus(WorldTool.BtCastle, WorldTool.BsNormal, 0, 0);
        Check("territory: … then back to normal (SM_CASTLE_NORMAL)", News(a, 11) is not null, "no news");

        // The mission news carry the start hour the map knew before (C++ static dwCurStart): the second alarm shows 20 h.
        WorldTool.BattleStatus(WorldTool.BtMission, WorldTool.BsNormal, 20 * 3600, 600);
        a.TryWait(CS_SYSTEMMSG_ACK, r => r.ReadByte() == 23, 5000);
        WorldTool.BattleStatus(WorldTool.BtMission, WorldTool.BsNormal, 20 * 3600, 300);
        var mission = a.TryWait(CS_SYSTEMMSG_ACK, r => r.ReadByte() == 23, 5000);
        Check("territory: the mission alarm carries its start hour (SM_MISSION_START_ALARM, 20 h, 300 s)", mission is not null
            && Read(mission, r => { r.ReadByte(); return (r.ReadUInt16(), r.ReadUInt32()); }) == (20, 300), Describe(mission));
        Thread.Sleep(300);
        a.Discard(CS_SYSTEMMSG_ACK); b.Discard(CS_SYSTEMMSG_ACK); b.Discard(CS_SKILLUSE_ACK); b.Discard(CS_HPMP_ACK);
    }

    /// <summary>Money pouch, reward box, exp book, exp boost (and a second one, refused) and an XP Plus premium.</summary>
    private static void MoreUseItems(Bot a, Bot b)
    {
        byte Slot(ushort item) => UseItems.First(u => u.Item == item).Slot;
        byte? Use(ushort item)
        {
            a.Discard(CS_ITEMUSE_ACK);
            a.Send(Req(CS_ITEMUSE_REQ, w => { w.WriteUInt16(item); w.WriteByte(0xFF); w.WriteByte(Slot(item)); w.WriteUInt16(0); w.WriteByte(0); }));
            return a.TryWait(CS_ITEMUSE_ACK) is { } ack ? Read(ack, r => { byte res = r.ReadByte(); r.ReadUInt16(); r.ReadByte(); r.ReadUInt32(); return res; }) : null;
        }

        a.Discard(CS_OPENMONEY_ACK); a.Discard(CS_MONEY_ACK);
        Check("use items: the pirate box is opened", Use(MoneyBox) == 0, "no IU_SUCCESS");
        var open = a.TryWait(CS_OPENMONEY_ACK);
        Check("use items: … and it holds at least 1 gold (1 000 000)", open is not null && Read(open, r => r.ReadUInt32()) >= 1_000_000,
            Describe(open));
        Check("use items: … which goes to the purse", a.TryWait(CS_MONEY_ACK) is not null, "no CS_MONEY_ACK");
        a.Discard(CS_TITLEGAIN_ACK);                                              // the gold may earn the gold title

        a.Discard(CS_DELITEM_ACK);
        Check("use items: the reward box is opened (nothing in it for a mage)", Use(RewardBox) == 0, "no IU_SUCCESS");
        Check("use items: … and used up", a.TryWait(CS_DELITEM_ACK, r => { r.ReadByte(); return r.ReadByte() == Slot(RewardBox); }) is not null,
            "no CS_DELITEM_ACK");

        a.Discard(CS_EXP_ACK);
        Check("use items: the book of wisdom is read", Use(Wisdom) == 0, "no IU_SUCCESS");
        Check("use items: … and gives exp", a.TryWait(CS_EXP_ACK) is not null, "no CS_EXP_ACK");

        a.Discard(CS_DEFEND_ACK);
        Check("use items: the growth potion is drunk", Use(Growth1h) == 0, "no IU_SUCCESS");
        var buff = a.TryWait(CS_DEFEND_ACK, r => r.ReadUInt32() == a.CharId && r.ReadUInt32() == a.CharId);
        Check("use items: … and shows as the exp buff, for its hour", buff is not null && ReadSkillId(buff) == ExpBuffSkill
            && ReadMaintain(buff) == (1, 3_600_000), Describe(buff));
        Check("use items: a second growth potion is refused (IU_OVERLAPEXPBONUS)", Use(Growth3h) == 13, "not refused");

        a.Discard(CS_DEFEND_ACK); b.Discard(CS_RESETPCBANG_ACK);
        Check("use items: the XP Plus premium is used", Use(XpPlus) == 0, "no IU_SUCCESS");
        var pc = b.TryWait(CS_RESETPCBANG_ACK, r => r.ReadUInt32() == a.CharId);
        Check("use items: … the player nearby sees the premium (CS_RESETPCBANG_ACK 2)", pc is not null && Read(pc, r => { r.ReadUInt32(); return r.ReadByte(); }) == 2,
            Describe(pc));
        var landed = new List<ushort>();
        for (int i = 0; i < 4 && !landed.Contains(XpPlusSkill); i++)
            if (a.TryWait(CS_DEFEND_ACK, r => r.ReadUInt32() == a.CharId && r.ReadUInt32() == a.CharId, 2000) is { } d) landed.Add(ReadSkillId(d));
        Check("use items: … and its own buff lands, with the exp buff", landed.Contains(XpPlusSkill) && landed.Contains(ExpBuffSkill),
            string.Join(",", landed));
    }

    /// <summary>A name change (refused when taken, then to a new name and back) and a country change refused below level 130.</summary>
    private static async Task NameAndCountry(Bot a, Bot b, GameDb db, uint idA, string nameA, string nameB)
    {
        (byte Result, byte Kind, string Name)? Ack(Bot bot, int timeout = 5000) => bot.TryWait(CS_CHANGECHARBASE_ACK, r => { r.ReadByte(); return r.ReadUInt32() == a.CharId; }, timeout)
            is { } p ? Read(p, r => { byte res = r.ReadByte(); r.ReadUInt32(); byte k = r.ReadByte(); r.ReadByte(); string n = r.ReadString(); r.ReadUInt16(); r.ReadUInt32(); return (res, k, n); }) : null;
        void Rename(byte slot, string name)
        {
            a.Discard(CS_CHANGECHARBASE_ACK); b.Discard(CS_CHANGECHARBASE_ACK);
            a.Send(Req(CS_CHANGENAME_REQ, w => { w.WriteByte(0xFF); w.WriteByte(slot); w.WriteString(name); }));
        }
        byte slot1 = UseItems.First(u => u.DlId == 900_000_013).Slot, slot2 = UseItems.First(u => u.DlId == 900_000_014).Slot;

        Rename(slot1, nameB);
        Check("name: someone else's name is refused (CCB_DUPLICATE)", Ack(a) is { Result: 1 }, "not refused");
        Rename(slot1, "Bad.Name");
        Check("name: a name with a dot is refused (CCB_FAIL)", Ack(a) is { Result: 9 }, "not refused");

        a.Discard(CS_DELITEM_ACK);
        Rename(slot1, NewName);
        var mine = Ack(a);
        Check("name: the new name comes back through the world", mine == (0, 48, NewName), mine?.ToString() ?? "no CS_CHANGECHARBASE_ACK");
        var seen = Ack(b);
        Check("name: … and the player nearby sees it", seen == (0, 48, NewName), seen?.ToString() ?? "no CS_CHANGECHARBASE_ACK");
        Check("name: … and the item is used up", a.TryWait(CS_DELITEM_ACK, r => { r.ReadByte(); return r.ReadByte() == slot1; }) is not null, "no CS_DELITEM_ACK");
        await Task.Delay(1500);
        var row = await db.RowAsync(@"SELECT c.szName, (SELECT COUNT(*) FROM TGLOBAL_GSP.dbo.TALLCHARTABLE WHERE szName=@p1) FROM TCHARTABLE c
            WHERE c.dwCharID=@p0", (int)idA, NewName);
        Check("name: saved in TCHARTABLE and TGlobal's TALLCHARTABLE", row is not null && (string)row[0] == NewName && Convert.ToInt32(row[1]) == 1,
            row is null ? "no row" : $"{row[0]} / {row[1]}");

        Rename(slot2, nameA);
        var back = Ack(a);
        Check("name: and back to the old one", back == (0, 48, nameA), back?.ToString() ?? "no CS_CHANGECHARBASE_ACK");

        a.Send(Req(CS_CHANGECOUNTRY_REQ, w => { w.WriteByte(96); w.WriteByte(2); w.WriteByte(0xFF); w.WriteByte(0xFF); }));
        var country = Ack(a);
        Check("country: going to Broa below level 130 is refused (CCB_LEVEL)", country is { Result: 8, Kind: 96 }, country?.ToString() ?? "no answer");
        await Task.Delay(1000);
    }

    /// <summary>A Dark Ritual summon left out: the logout saves it (TRECALLMONTABLE), and the next login brings it back.</summary>
    private static void LeaveASummonOut(Bot a)
    {
        a.Discard(CS_ADDRECALLMON_ACK);
        a.Send(FinishSkill(a, a.CharId, 1, RitualSkill, a.Spawn.X, a.Spawn.Z, (a.CharId, 1)));
        var add = a.TryWait(CS_ADDRECALLMON_ACK, r => { r.ReadUInt32(); r.ReadUInt32(); return r.ReadUInt16() == RitualMon; }, 8000);
        Check("summons: a summon is out at logout", add is not null, Describe(add));
    }

    private static void Duel(Bot a, Bot b)
    {
        a.Send(Req(CS_DUELINVITE_REQ, w => w.WriteUInt32(b.CharId)));
        var ask = b.TryWait(CS_DUELINVITE_ACK);
        Check("duel: the invite reaches the other player", ask is not null && Read(ask, r => r.ReadUInt32()) == a.CharId, Describe(ask));
        if (ask is null) return;

        b.Send(Req(CS_DUELINVITEREPLY_REQ, w => { w.WriteByte(0); w.WriteUInt32(a.CharId); }));   // ASK_YES
        var standA = a.TryWait(CS_DUELSTANDBY_ACK);
        var standB = b.TryWait(CS_DUELSTANDBY_ACK);
        Check("duel: both get the arena (standby)", standA is not null && standB is not null
            && Read(standA, r => (r.ReadUInt32(), r.ReadUInt32(), r.ReadFloat(), r.ReadFloat())).Item2 == b.CharId
            && Read(standB, r => (r.ReadUInt32(), r.ReadUInt32(), r.ReadFloat(), r.ReadFloat())).Item1 == a.CharId,
            $"{Describe(standA)} / {Describe(standB)}");
        var go = b.TryWait(CS_DUELSTART_ACK, timeoutMs: 14000);
        Check("duel: it starts about 10 s later", go is not null && Read(go, r => (r.ReadByte(), r.ReadUInt32(), r.ReadUInt32()))
            == (0, a.CharId, b.CharId), Describe(go));
        a.TryWait(CS_DUELSTART_ACK, timeoutMs: 1000);
        if (go is null) return;

        a.Discard(CS_PVPPOINT_ACK); b.Discard(CS_DIE_ACK); b.Discard(CS_HPMP_ACK);
        PacketReader? end = null;
        for (int i = 0; i < 60 && end is null; i++)
        {
            a.Send(FinishSkill(a, a.CharId, 1, BasicMelee, a.Spawn.X, a.Spawn.Z, (b.CharId, 1)));
            b.TryWait(CS_DEFEND_ACK, r => r.ReadUInt32() == a.CharId && r.ReadUInt32() == b.CharId, 3000);
            end = b.TryWait(CS_DUELEND_ACK, timeoutMs: 300);
        }
        Check("duel: the knockout ends it, the loser named", end is not null && Read(end, r => r.ReadUInt32()) == b.CharId, Describe(end));
        Check("duel: the loser does not die", b.TryWait(CS_DIE_ACK, r => r.ReadUInt32() == b.CharId, 500) is null, "CS_DIE_ACK sent");
        var hp = b.TryWait(CS_HPMP_ACK, r => r.ReadUInt32() == b.CharId && r.ReadByte() == 1 && r.ReadUInt32() == r.ReadUInt32());
        Check("duel: the loser is healed to full", hp is not null, Describe(hp));
        var win = a.TryWait(CS_SYSTEMMSG_ACK, r => r.ReadByte() == 8);
        Check("duel: the winner's view hears who won (SM_DUAL_WIN)", win is not null
            && Read(win, r => (r.ReadByte(), r.ReadString(), r.ReadString())) == (8, a.Spawn.Name, b.Spawn.Name), Describe(win));
        Check("duel: no PvP points for a duel", a.TryWait(CS_PVPPOINT_ACK, timeoutMs: 500) is null, "CS_PVPPOINT_ACK sent");
        a.TryWait(CS_DUELEND_ACK, timeoutMs: 500);
        Thread.Sleep(2000);                                            // the duel is cleared the next second
        a.Discard(CS_HPMP_ACK); b.Discard(CS_HPMP_ACK); a.Discard(CS_DEFEND_ACK); b.Discard(CS_DEFEND_ACK);
    }

    private static void PvP(Bot a, Bot b)
    {
        a.Discard(CS_PVPPOINT_ACK); b.Discard(CS_PVPPOINT_ACK); b.Discard(CS_DIE_ACK);
        PacketReader? die = null, first = null;
        for (int i = 0; i < 40 && die is null; i++)
        {
            a.Send(FinishSkill(a, a.CharId, 1, BasicMelee, a.Spawn.X, a.Spawn.Z, (b.CharId, 1)));
            var hit = b.TryWait(CS_DEFEND_ACK, r => r.ReadUInt32() == a.CharId && r.ReadUInt32() == b.CharId, 3000);
            first ??= hit;
            die = b.TryWait(CS_DIE_ACK, r => r.ReadUInt32() == b.CharId, 300);
        }
        Check("pvp: a hit on the other player reaches it", first is not null, Describe(first));
        Check("pvp: the other player falls", die is not null && Read(die, r => (r.ReadUInt32(), r.ReadByte())) == (b.CharId, 1), Describe(die));
        if (die is null) return;

        var gain = a.TryWait(CS_PVPPOINT_ACK);
        var loss = b.TryWait(CS_PVPPOINT_ACK);
        Check("pvp: the killer earns 11 total, useable and this month (an even kill)",
            gain is not null && ReadPvPoint(gain) == (KillGain, KillGain, 2, KillGain), Describe(gain));
        Check("pvp: the victim loses 2 from its total",
            loss is not null && ReadPvPoint(loss) == (VictimTotal - KillLoss, 0, 2, 0), Describe(loss));
        var title = a.TryWait(CS_TITLEGAIN_ACK);
        Check("titles: passing 10 total points earns the first honour title", title is not null && Read(title, r =>
        {
            int n = r.ReadByte(); var owned = new List<ushort>();
            for (int i = 0; i < n; i++) { owned.Add(r.ReadUInt16()); r.ReadByte(); }
            return (owned.Contains(HonourTitle), r.ReadUInt16(), r.ReadByte());
        }) == (true, HonourTitle, 1), Describe(title));

        a.Send(Req(CS_PVPRECORD_REQ, w => w.WriteByte(0)));
        var rec = a.TryWait(CS_PVPRECORD_ACK);
        Check("ranking: the record window counts the win against a ranger and lists the kill", rec is not null && Read(rec, r =>
        {
            r.ReadByte(); r.ReadUInt32(); r.ReadByte();
            var cls = Enumerable.Range(0, 6).Select(_ => (r.ReadUInt32(), r.ReadUInt32())).ToList();
            int n = r.ReadByte(); string last = "";
            for (int i = 0; i < n; i++) { last = r.ReadString(); r.ReadByte(); r.ReadByte(); r.ReadByte(); r.ReadUInt32(); r.ReadInt64(); }
            r.ReadUInt32(); r.ReadByte(); ushort mw = r.ReadUInt16(); r.ReadUInt16();
            return (cls[1].Item1, last, mw);
        }) == (1u, b.Spawn.Name, (ushort)1), Describe(rec));

        Thread.Sleep(1000);                                            // the world's ladder update comes back
        a.Send(Req(CS_MONTHRANKLIST_REQ, _ => { }));
        var board = a.TryWait(CS_MONTHRANKLIST_ACK);
        Check("ranking: the killer is on the month's ladder the world keeps", board is not null && Read(board, r =>
        {
            r.ReadByte(); r.ReadByte(); int per = r.ReadByte(); bool found = false;
            for (int c = 0; c < 3; c++)
            {
                r.ReadByte();
                for (int j = 0; j < per; j++)
                {
                    r.ReadUInt32(); r.ReadUInt32(); uint id = r.ReadUInt32(); r.ReadString(); r.ReadUInt32(); uint month = r.ReadUInt32();
                    r.ReadUInt16(); r.ReadUInt16(); r.ReadUInt32(); r.ReadUInt32();
                    for (int k = 0; k < 7; k++) r.ReadByte();
                    r.ReadString(); r.ReadString();
                    if (id == a.CharId && month == KillGain) found = true;
                }
            }
            return found;
        }), Describe(board));

        // B gets up where it fell (REVIVAL_GHOST).
        b.Send(Req(CS_REVIVAL_REQ, w => { w.WriteFloat(b.Spawn.X); w.WriteFloat(b.Spawn.Y); w.WriteFloat(b.Spawn.Z); w.WriteByte(1); }));
        var up = b.TryWait(CS_REVIVAL_ACK, r => r.ReadUInt32() == b.CharId);
        Check("pvp: the victim revives", up is not null && Read(up, r => (r.ReadUInt32(), r.ReadFloat(), r.ReadFloat(), r.ReadFloat())).Item1 == b.CharId,
            Describe(up));
        if (up is null) return;

        // A hostile skill leaves its debuff on the other player: the hit says so and gives its length.
        Thread.Sleep(500);
        b.Discard(CS_DEFEND_ACK);
        a.Send(FinishSkill(a, a.CharId, 1, DefenceBreak, a.Spawn.X, a.Spawn.Z, (b.CharId, 1)));
        var cursed = b.TryWait(CS_DEFEND_ACK, r => r.ReadUInt32() == a.CharId && r.ReadUInt32() == b.CharId);
        Check("pvp: a hostile skill leaves its debuff on the player (9 s)", cursed is not null && ReadMaintain(cursed) == (1, DefenceBreakMs),
            Describe(cursed));
        Check("pvp: the debuffed player gets its new stat sheet", b.TryWait(CS_CHARSTATINFO_ACK, timeoutMs: 2000) is not null, "no CS_CHARSTATINFO_ACK");
    }

    // CS_DEFEND_ACK up to dwMaintainTick: attacker, target, their types, host + type, act, ani, bIsMaintain, dwMaintainTick.
    /// <summary>The <c>wSkillID</c> of a <c>CS_DEFEND_ACK</c> (53 bytes in).</summary>
    private static ushort ReadSkillId(PacketReader p)
    {
        var raw = Bot.Raw.TryGetValue(p, out var r) ? r : throw new InvalidOperationException();
        return BitConverter.ToUInt16(raw, PacketHeader.Size + 53);
    }

    private static ushort ChocolateBuff;                                                 // which one the chocolate gave

    private static (byte IsMaintain, uint Tick) ReadMaintain(PacketReader p)
    {
        var r = new PacketReader(Bot.Raw.TryGetValue(p, out var raw) ? raw : throw new InvalidOperationException());
        r.ReadUInt32(); r.ReadUInt32(); r.ReadByte(); r.ReadByte(); r.ReadUInt32(); r.ReadByte(); r.ReadUInt32(); r.ReadUInt32();
        return (r.ReadByte(), r.ReadUInt32());
    }

    // TClient OnCS_PVPPOINT_ACK: dwTotal, dwUseable, bEvent, dwMonthPvPoint.
    private static (uint Total, uint Useable, byte Event, uint Month) ReadPvPoint(PacketReader p)
        => Read(p, r => (r.ReadUInt32(), r.ReadUInt32(), r.ReadByte(), r.ReadUInt32()));

    private static PacketWriter MoveItem(byte srcInven, byte srcSlot, byte dstInven, byte dstSlot) => Req(CS_MOVEITEM_REQ, w =>
    {
        w.WriteByte(srcInven); w.WriteByte(srcSlot); w.WriteByte(dstInven); w.WriteByte(dstSlot); w.WriteByte(1);
    });

    // CS_DEFEND_ACK up to bIsMaintain: attacker, target, their types, host + type, act, ani.
    private static bool IsMaintain(PacketReader p)
    {
        var r = new PacketReader(Bot.Raw.TryGetValue(p, out var raw) ? raw : throw new InvalidOperationException());
        r.ReadUInt32(); r.ReadUInt32(); r.ReadByte(); r.ReadByte(); r.ReadUInt32(); r.ReadByte(); r.ReadUInt32(); r.ReadUInt32();
        return r.ReadByte() == 1;
    }

    private static async Task CheckSaved(GameDb db, uint idA, uint idB)
    {
        var exp = await db.RowAsync("SELECT wItemID, bType, dwRemainTime FROM TEXPITEMTABLE WHERE dwCharID=@p0", (int)idA);
        Check("saved: the growth potion still running, in TEXPITEMTABLE", exp is not null && Convert.ToInt32(exp[0]) == Growth1h
            && Convert.ToInt32(exp[1]) == 1 && Convert.ToInt32(exp[2]) is > 3000 and <= 3600, exp is null ? "no row" : $"{exp[0]} / {exp[1]} / {exp[2]}");
        var dur = await db.RowAsync(@"SELECT wItemID, bType, dwRemainTime FROM TGLOBAL_GSP.dbo.TDURINGITEMTABLE
            WHERE dwUserID=(SELECT dwUserID FROM TCHARTABLE WHERE dwCharID=@p0)", (int)idA);
        Check("saved: the account's premium, in TGlobal's TDURINGITEMTABLE", dur is not null && Convert.ToInt32(dur[0]) == XpPlus
            && Convert.ToInt32(dur[1]) == 1 && Convert.ToInt32(dur[2]) is > 35000 and <= 36000, dur is null ? "no row" : $"{dur[0]} / {dur[1]} / {dur[2]}");
        var recall = await db.RowAsync("SELECT COUNT(*) FROM TRECALLMONTABLE WHERE dwOwnerID=@p0 AND wMonID=@p1", (int)idA, (int)RitualMon);
        Check("saved: the summon out at logout, in TRECALLMONTABLE", recall is not null && Convert.ToInt32(recall[0]) == 1, $"rows={recall?[0]}");
        // The player's own buffs are kept (TSaveSkillMaintain) — the chocolate's, unless it was the 3-second "Drunk".
        var choc = await db.RowAsync("SELECT COUNT(*) FROM TSKILLMAINTAINTABLE WHERE dwCharID=@p0 AND wSkillID=@p1", (int)idA, (int)ChocolateBuff);
        int expect = ChocolateBuff == ChocolateLast ? 0 : 1;
        Check($"saved: the chocolate's buff ({ChocolateBuff}) in TSKILLMAINTAINTABLE", choc is not null && Convert.ToInt32(choc[0]) == expect,
            $"rows={choc?[0]}, expected {expect}");
        var comp = await db.RowAsync("SELECT strName, dwMonID FROM TCOMPANIONTABLE WHERE dwCharID=@p0 AND bSlot=0", (int)idA);
        Check("saved: the companion in TCOMPANIONTABLE", comp is not null && (string)comp[0] == PalName && Convert.ToInt32(comp[1]) == RuneSpecies,
            comp is null ? "no row" : $"{comp[0]} / {comp[1]}");
        var pet = await db.RowAsync("SELECT szName FROM TPETTABLE WHERE dwUserID=(SELECT dwUserID FROM TCHARTABLE WHERE dwCharID=@p0) AND wPetID=@p1",
            (int)idA, (int)HorsePet);
        Check("saved: the new pet in TPETTABLE", pet is not null && (string)pet[0] == HorseName, pet is null ? "no row" : $"name={pet[0]}");

        var term = await db.RowAsync("SELECT bCount FROM TQUESTTERMTABLE WHERE dwCharID=@p0 AND dwQuestID=@p1 AND dwTermID=@p2", (int)idA,
            (int)HuntQuest, (int)HuntTerm);
        Check("saved: the hunt term done by position, in TQUESTTERMTABLE", term is not null && Convert.ToInt32(term[0]) == 1,
            term is null ? "no row" : $"count={term[0]}");
        var hk = await db.RowAsync("SELECT bType1, wID1 FROM THOTKEYTABLE WHERE dwCharID=@p0 AND bInvenID=@p1", (int)idA, (int)HotkeyPage);
        Check("saved: hotkey page in THOTKEYTABLE", hk is not null && Convert.ToInt32(hk[0]) == 1 && Convert.ToInt32(hk[1]) == HotkeySkill,
            hk is null ? "no row" : $"type={hk[0]} id={hk[1]}");

        var pos = await db.RowAsync("SELECT wMapID, fPosX, fPosZ FROM TCHARTABLE WHERE dwCharID=@p0", (int)idB);
        Check("saved: teleported position", pos is not null && Math.Abs(Convert.ToSingle(pos[1]) - 3726.88f) < 10
            && Math.Abs(Convert.ToSingle(pos[2]) - 146.04f) < 10, pos is null ? "" : $"map {pos[0]} ({pos[1]}, {pos[2]})");

        var ma = await db.RowAsync("SELECT dwCooper FROM TCHARTABLE WHERE dwCharID=@p0", (int)idA);
        Check("saved: sender's money", ma is not null && Convert.ToInt64(ma[0]) == TestCooper - MailCost - MailCooper - SandPrice, $"cooper={ma?[0]}");
        var sk = await db.RowAsync("SELECT bLevel, (SELECT wSkillPoint FROM TCHARTABLE WHERE dwCharID=@p0) FROM TSKILLTABLE WHERE dwCharID=@p0 AND wSkillID=@p1",
            (int)idA, (int)SandSkill);
        Check("saved: the learned skill in TSKILLTABLE and the points spent", sk is not null && Convert.ToInt32(sk[0]) == 1
            && Convert.ToInt32(sk[1]) == BotSkillPoints - 1, sk is null ? "no row" : $"level {sk[0]}, {sk[1]} points");
        var pa = await db.RowAsync("SELECT dwUseablePoint, dwTotalPoint FROM TPVPOINTTABLE WHERE dwCharID=@p0", (int)idA);
        var pb = await db.RowAsync("SELECT dwUseablePoint, dwTotalPoint FROM TPVPOINTTABLE WHERE dwCharID=@p0", (int)idB);
        Check("saved: the killer's PvP points in TPVPOINTTABLE", pa is not null && Convert.ToInt64(pa[0]) == KillGain
            && Convert.ToInt64(pa[1]) == KillGain, pa is null ? "no row" : $"useable {pa[0]}, total {pa[1]}");
        Check("saved: the victim's PvP points in TPVPOINTTABLE", pb is not null && Convert.ToInt64(pb[1]) == VictimTotal - KillLoss,
            pb is null ? "no row" : $"useable {pb[0]}, total {pb[1]}");
        var month = await db.RowAsync("SELECT dwPoint, wWin FROM TMONTHPVPOINTTABLE WHERE dwCharID=@p0", (int)idA);
        Check("saved: the killer's month in TMONTHPVPOINTTABLE", month is not null && Convert.ToInt64(month[0]) == KillGain
            && Convert.ToInt32(month[1]) == 1, month is null ? "no row" : $"{month[0]} points, {month[1]} wins");
        var cls = await db.RowAsync("SELECT dwRanger_win FROM TPVPRECORDTABLE WHERE dwCharID=@p0", (int)idA);
        Check("saved: the win against a ranger in TPVPRECORDTABLE", cls is not null && Convert.ToInt64(cls[0]) == 1, cls is null ? "no row" : $"{cls[0]}");
        var ttl = await db.RowAsync("SELECT COUNT(*) FROM TTITLETABLE WHERE dwCharID=@p0 AND wTitleID=@p1", (int)idA, (int)HonourTitle);
        Check("saved: the honour title in TTITLETABLE", ttl is not null && Convert.ToInt32(ttl[0]) == 1, $"{ttl?[0]} rows");
        var kill = await db.RowAsync("SELECT COUNT(*) FROM charkilling_log WHERE dwKillerID=@p0 AND dwTargetID=@p1", (int)idA, (int)idB);
        Check("saved: the kill in charkilling_log", kill is not null && Convert.ToInt32(kill[0]) == 1, $"{kill?[0]} rows");
        var mb = await db.RowAsync("SELECT dwCooper FROM TCHARTABLE WHERE dwCharID=@p0", (int)idB);
        Check("saved: receiver's money", mb is not null && Convert.ToInt64(mb[0]) == MailCooper, $"cooper={mb?[0]}");

        var bag = await db.RowAsync("SELECT COUNT(*) FROM TITEMTABLE WHERE dwOwnerID=@p0 AND bOwnerType=0 AND bStorageType=0 " +
            "AND dwStorageID=255 AND wItemID=@p1", (int)idB, (int)BagItem);
        var bagInven = await db.RowAsync("SELECT COUNT(*) FROM TINVENTABLE WHERE dwCharID=@p0 AND bInvenID IN (0,1)", (int)idB);
        Check("saved: bag back in the backpack, no bag slot left", bag is not null && Convert.ToInt32(bag[0]) == 1
            && bagInven is not null && Convert.ToInt32(bagInven[0]) == 0, $"backpack={bag?[0]} bagRows={bagInven?[0]}");
    }

    // ================================ fixtures ================================

    private sealed record Saved(object[] A, object[] B, object[]? PvpA, object[]? PvpB);

    /// <summary>Sets a character's TPVPOINTTABLE row, or removes it (<paramref name="keep"/> false: it had none).</summary>
    private static async Task SetPvPoint(GameDb db, uint id, long useable, long total, bool keep = true)
    {
        await db.ExecAsync("DELETE FROM TPVPOINTTABLE WHERE dwCharID=@p0", (int)id);
        if (keep)
            await db.ExecAsync("INSERT INTO TPVPOINTTABLE (dwCharID, dwUseablePoint, dwTotalPoint) VALUES (@p0, @p1, @p2)",
                (int)id, useable, total);
    }

    private static async Task<uint> FirstChar(GameDb db, string account)
    {
        var row = await db.RowAsync(@"SELECT TOP 1 c.dwCharID FROM TCHARTABLE c
            JOIN TGlobal_gsp.dbo.TACCOUNT_PW u ON u.dwUserID = c.dwUserID WHERE u.szUserID=@p0 AND c.bDelete=0
            ORDER BY c.bSlot", account);
        return row is null ? throw new InvalidOperationException($"account '{account}' has no character") : Convert.ToUInt32(row[0]);
    }

    private const string CharCols = "dwGold, dwSilver, dwCooper, dwHP, wMapID, dwRegion, fPosX, fPosY, fPosZ, bAftermath, bSex, dwEXP, bLevel, szName";

    private static async Task<Saved> Snapshot(GameDb db, uint idA, uint idB)
        => new((await db.RowAsync($"SELECT {CharCols} FROM TCHARTABLE WHERE dwCharID=@p0", (int)idA))!,
               (await db.RowAsync($"SELECT {CharCols} FROM TCHARTABLE WHERE dwCharID=@p0", (int)idB))!,
               await db.RowAsync("SELECT dwUseablePoint, dwTotalPoint FROM TPVPOINTTABLE WHERE dwCharID=@p0", (int)idA),
               await db.RowAsync("SELECT dwUseablePoint, dwTotalPoint FROM TPVPOINTTABLE WHERE dwCharID=@p0", (int)idB));

    private static async Task Prepare(GameDb db, uint idA, uint idB)
    {
        await db.ExecAsync("UPDATE TCHARTABLE SET dwGold=0, dwSilver=0, dwCooper=@p1 WHERE dwCharID=@p0", (int)idA, (int)TestCooper);
        await db.ExecAsync("UPDATE TCHARTABLE SET dwGold=0, dwSilver=0, dwCooper=0, dwHP=20 WHERE dwCharID=@p0", (int)idB);
        await db.ExecAsync("DELETE FROM THOTKEYTABLE WHERE dwCharID=@p0 AND bInvenID=@p1", (int)idA, (int)HotkeyPage);
        await db.ExecAsync("DELETE FROM TITEMTABLE WHERE dlID=@p0 OR (dwOwnerID=@p1 AND bOwnerType=0 AND bStorageType=0 AND dwStorageID=255 AND bItemID=@p2)",
            BagDlId, (int)idB, (int)BagSlot);
        await db.ExecAsync(@"INSERT INTO TITEMTABLE (dlID, bStorageType, dwStorageID, bOwnerType, dwOwnerID, bItemID, wItemID, bLevel,
            bCount, bGLevel, dwDuraMax, dwDuraCur, bRefineCur, dEndTime, bGradeEffect, bMagic1, bMagic2, bMagic3, bMagic4, bMagic5,
            bMagic6, wValue1, wValue2, wValue3, wValue4, wValue5, wValue6, dwTime1, dwTime2, dwTime3, dwTime4, dwTime5, dwTime6,
            bGem, wMoggItemID)
            VALUES (@p0, 0, 255, 0, @p1, @p2, @p3, 0, 1, 0, 0, 0, 0, '1900-01-01', 0, 0,0,0,0,0,0, 0,0,0,0,0,0, 0,0,0,0,0,0, 0, 0)",
            BagDlId, (int)idB, (int)BagSlot, (int)BagItem);
        await db.ExecAsync("DELETE FROM TPETTABLE WHERE dwUserID=(SELECT dwUserID FROM TCHARTABLE WHERE dwCharID=@p0) AND wPetID=@p1",
            (int)idA, (int)HorsePet);
        await db.ExecAsync("DELETE FROM TITEMTABLE WHERE dlID=@p0 OR (dwOwnerID=@p1 AND bOwnerType=0 AND bStorageType=0 AND dwStorageID=255 AND bItemID=@p2)",
            HorseDlId, (int)idA, (int)HorseSlot);
        await db.ExecAsync(@"INSERT INTO TITEMTABLE (dlID, bStorageType, dwStorageID, bOwnerType, dwOwnerID, bItemID, wItemID, bLevel,
            bCount, bGLevel, dwDuraMax, dwDuraCur, bRefineCur, dEndTime, bGradeEffect, bMagic1, bMagic2, bMagic3, bMagic4, bMagic5,
            bMagic6, wValue1, wValue2, wValue3, wValue4, wValue5, wValue6, dwTime1, dwTime2, dwTime3, dwTime4, dwTime5, dwTime6,
            bGem, wMoggItemID)
            VALUES (@p0, 0, 255, 0, @p1, @p2, @p3, 0, 1, 0, 0, 0, 0, '1900-01-01', 0, 0,0,0,0,0,0, 0,0,0,0,0,0, 0,0,0,0,0,0, 0, 0)",
            HorseDlId, (int)idA, (int)HorseSlot, (int)HorseItem);
        await ClearCompanions(db, idA);
        await ResetSand(db, idA);
        await db.ExecAsync("DELETE FROM TSKILLTABLE WHERE dwCharID=@p0 AND wSkillID IN (@p1, @p2, @p3)", (int)idA, (int)RitualSkill, (int)IceRainSkill,
            (int)DefenceBreak);
        await db.ExecAsync("INSERT INTO TSKILLTABLE (dwCharID, wSkillID, bLevel, dwRemainTick) VALUES (@p0, @p1, 1, 0), (@p0, @p2, 1, 0), (@p0, @p3, 1, 0)",
            (int)idA, (int)RitualSkill, (int)IceRainSkill, (int)DefenceBreak);
        await db.ExecAsync("DELETE FROM TSKILLMAINTAINTABLE WHERE dwCharID=@p0 AND wSkillID=@p1", (int)idB, (int)DefenceBreak);
        await ClearLeftovers(db, idA, idB);
        await ClearItemSkills(db, idA);
        foreach (var (dl, slot, item) in new[] { (ChocolateDlId, ChocolateSlot, ChocolateItem), (GenderDlId, GenderSlot, GenderItem) }.Concat(UseItems))
            await db.ExecAsync(@"INSERT INTO TITEMTABLE (dlID, bStorageType, dwStorageID, bOwnerType, dwOwnerID, bItemID, wItemID, bLevel,
            bCount, bGLevel, dwDuraMax, dwDuraCur, bRefineCur, dEndTime, bGradeEffect, bMagic1, bMagic2, bMagic3, bMagic4, bMagic5,
            bMagic6, wValue1, wValue2, wValue3, wValue4, wValue5, wValue6, dwTime1, dwTime2, dwTime3, dwTime4, dwTime5, dwTime6,
            bGem, wMoggItemID)
            VALUES (@p0, 0, 255, 0, @p1, @p2, @p3, 0, 1, 0, 0, 0, 0, '1900-01-01', 0, 0,0,0,0,0,0, 0,0,0,0,0,0, 0,0,0,0,0,0, 0, 0)", dl, (int)idA, (int)slot, (int)item);
        await db.ExecAsync("INSERT INTO TSKILLTABLE (dwCharID, wSkillID, bLevel, dwRemainTick) VALUES (@p0, @p2, 1, 0), (@p0, @p3, 1, 0), (@p0, @p4, 1, 0), (@p1, @p5, 1, 0)",
            (int)idA, (int)idB, (int)Hide, (int)SelfBuff, (int)DamageShare, (int)Dispel);
        await db.ExecAsync("DELETE FROM TITEMTABLE WHERE dlID=@p0 OR (dwOwnerID=@p1 AND bOwnerType=0 AND bStorageType=0 AND dwStorageID=255 AND bItemID=@p2)",
            RuneDlId, (int)idA, (int)RuneSlot);
        await db.ExecAsync(@"INSERT INTO TITEMTABLE (dlID, bStorageType, dwStorageID, bOwnerType, dwOwnerID, bItemID, wItemID, bLevel,
            bCount, bGLevel, dwDuraMax, dwDuraCur, bRefineCur, dEndTime, bGradeEffect, bMagic1, bMagic2, bMagic3, bMagic4, bMagic5,
            bMagic6, wValue1, wValue2, wValue3, wValue4, wValue5, wValue6, dwTime1, dwTime2, dwTime3, dwTime4, dwTime5, dwTime6,
            bGem, wMoggItemID)
            VALUES (@p0, 0, 255, 0, @p1, @p2, @p3, 0, 1, 0, 0, 0, 0, '1900-01-01', 0, 0,0,0,0,0,0, 0,0,0,0,0,0, 0,0,0,0,0,0, 0, 0)",
            RuneDlId, (int)idA, (int)RuneSlot, (int)RuneItem);   // dwTime5 = 0: no species until the server stamps it
        foreach (var t in RankTables)
        {
            await db.ExecAsync($"IF OBJECT_ID('TBOT_BAK_{t}') IS NOT NULL DROP TABLE TBOT_BAK_{t}");
            await db.ExecAsync($"SELECT * INTO TBOT_BAK_{t} FROM {t} WHERE dwCharID IN (@p0, @p1)", (int)idA, (int)idB);
            await db.ExecAsync($"DELETE FROM {t} WHERE dwCharID IN (@p0, @p1)", (int)idA, (int)idB);
        }
        await db.ExecAsync("DELETE FROM charkilling_log WHERE dwKillerID=@p0 AND dwTargetID=@p1", (int)idA, (int)idB);
        await SetPvPoint(db, idA, 0, 0);
        await SetPvPoint(db, idB, 0, VictimTotal);
        await db.ExecAsync("UPDATE TCHARTABLE SET bAftermath=0 WHERE dwCharID IN (@p0, @p1)", (int)idA, (int)idB);
        await ClearPassives(db, idA);
        await ClearSmallRequests(db, idA);
        await db.ExecAsync("INSERT INTO TSKILLTABLE (dwCharID, wSkillID, bLevel, dwRemainTick) VALUES (@p0, @p1, 1, 0)", (int)idA, (int)MissileSkill);
        await db.ExecAsync(@"INSERT INTO TQUESTTABLE (dwCharID, dwQuestID, dwTick, bCompleteCount, bTriggerCount) VALUES (@p0, @p1, 300000, 0, 1), (@p0, @p2, 0, 0, 1);
            INSERT INTO TQUESTTERMTABLE (dwCharID, dwQuestID, dwTermID, bTermType, bCount) VALUES (@p0, @p2, @p3, 3, 0)",
            (int)idA, (int)TimerQuest, (int)HuntQuest, (int)HuntTerm);
        await db.ExecAsync("INSERT INTO TSKILLTABLE (dwCharID, wSkillID, bLevel, dwRemainTick) VALUES (@p0, @p1, 1, 0)", (int)idA, (int)WeaponBuff);
        await db.ExecAsync(@"INSERT INTO TITEMTABLE (dlID, bStorageType, dwStorageID, bOwnerType, dwOwnerID, bItemID, wItemID, bLevel,
            bCount, bGLevel, dwDuraMax, dwDuraCur, bRefineCur, dEndTime, bGradeEffect, bMagic1, bMagic2, bMagic3, bMagic4, bMagic5,
            bMagic6, wValue1, wValue2, wValue3, wValue4, wValue5, wValue6, dwTime1, dwTime2, dwTime3, dwTime4, dwTime5, dwTime6,
            bGem, wMoggItemID)
            VALUES (@p0, 0, 255, 0, @p1, @p2, @p3, 0, 1, 0, 0, 0, 0, '1900-01-01', 0, 0,0,0,0,0,0, 0,0,0,0,0,0, 0,0,0,0,0,0, 0, 0)",
            MasteryDlId, (int)idA, (int)MasteryItemSlot, (int)MasteryItem);
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] fixtures: A={idA} has {TestCooper} copper, B={idB} at 20 HP with a bag in slot {BagSlot}");
    }

    /// <summary>Throw Sand back to its TSTARTSKILL level 0, and the bot's skill points back to 200.</summary>
    private static async Task ResetSand(GameDb db, uint id)
    {
        await db.ExecAsync("DELETE FROM TSKILLTABLE WHERE dwCharID=@p0 AND wSkillID=@p1", (int)id, (int)SandSkill);
        await db.ExecAsync("INSERT INTO TSKILLTABLE (dwCharID, wSkillID, bLevel, dwRemainTick) VALUES (@p0, @p1, 0, 0)", (int)id, (int)SandSkill);
        await db.ExecAsync("UPDATE TCHARTABLE SET wSkillPoint=@p1 WHERE dwCharID=@p0", (int)id, (int)BotSkillPoints);
    }

    /// <summary>Takes away the small-requests fixtures: the multi-missile skill and the two quests.</summary>
    private static async Task ClearSmallRequests(GameDb db, uint idA)
    {
        await db.ExecAsync("DELETE FROM TSKILLTABLE WHERE dwCharID=@p0 AND wSkillID=@p1", (int)idA, (int)MissileSkill);
        await db.ExecAsync("DELETE FROM TQUESTTABLE WHERE dwCharID=@p0 AND dwQuestID IN (@p1, @p2); DELETE FROM TQUESTTERMTABLE WHERE dwCharID=@p0 AND dwQuestID IN (@p1, @p2)",
            (int)idA, (int)TimerQuest, (int)HuntQuest);
    }

    /// <summary>Takes away the use items (left or used up), the chocolate's buff, and the saved summons.</summary>
    private static async Task ClearItemSkills(GameDb db, uint idA)
    {
        await db.ExecAsync("DELETE FROM TITEMTABLE WHERE dlID IN (@p0, @p1) OR (dwOwnerID=@p2 AND bOwnerType=0 AND bStorageType=0 AND dwStorageID=255 AND bItemID IN (@p3, @p4))",
            ChocolateDlId, GenderDlId, (int)idA, (int)ChocolateSlot, (int)GenderSlot);
        await db.ExecAsync("DELETE FROM TSKILLMAINTAINTABLE WHERE dwCharID=@p0 AND wSkillID BETWEEN @p1 AND @p2", (int)idA, (int)ChocolateFirst, (int)ChocolateLast);
        await db.ExecAsync("DELETE FROM TRECALLMONTABLE WHERE dwOwnerID=@p0; DELETE FROM TRECALLMAINTAINTABLE WHERE dwCharID=@p0", (int)idA);
        foreach (var (dl, slot, _) in UseItems)
            await db.ExecAsync("DELETE FROM TITEMTABLE WHERE dlID=@p0 OR (dwOwnerID=@p1 AND bOwnerType=0 AND bStorageType=0 AND dwStorageID=255 AND bItemID=@p2)",
                dl, (int)idA, (int)slot);
        await db.ExecAsync(@"DELETE FROM TEXPITEMTABLE WHERE dwCharID=@p0;
            DELETE FROM TGLOBAL_GSP.dbo.TDURINGITEMTABLE WHERE dwUserID=(SELECT dwUserID FROM TCHARTABLE WHERE dwCharID=@p0);
            DELETE FROM TGLOBAL_GSP.dbo.TPCBANGPLAYTABLE WHERE dwUserID=(SELECT dwUserID FROM TCHARTABLE WHERE dwCharID=@p0);
            DELETE FROM TSKILLMAINTAINTABLE WHERE dwCharID=@p0 AND wSkillID IN (@p1, @p2)", (int)idA, (int)XpPlusSkill, (int)ExpBuffSkill);
    }

    /// <summary>Takes away the combat-leftover skills (learned and running): A's hide, self buff and damage share, B's dispel.</summary>
    private static async Task ClearLeftovers(GameDb db, uint idA, uint idB)
    {
        foreach (var t in new[] { "TSKILLTABLE", "TSKILLMAINTAINTABLE" })
            await db.ExecAsync($"DELETE FROM {t} WHERE (dwCharID=@p0 AND wSkillID IN (@p2, @p3, @p4)) OR (dwCharID=@p1 AND wSkillID=@p5)",
                (int)idA, (int)idB, (int)Hide, (int)SelfBuff, (int)DamageShare, (int)Dispel);
    }

    /// <summary>Takes away the weapon buff (learned and running) and the kind-13 test item.</summary>
    private static async Task ClearPassives(GameDb db, uint id)
    {
        await db.ExecAsync("DELETE FROM TSKILLTABLE WHERE dwCharID=@p0 AND wSkillID=@p1", (int)id, (int)WeaponBuff);
        await db.ExecAsync("DELETE FROM TSKILLMAINTAINTABLE WHERE dwCharID=@p0 AND wSkillID=@p1", (int)id, (int)WeaponBuff);
        await db.ExecAsync("DELETE FROM TITEMTABLE WHERE dlID=@p0 OR (dwOwnerID=@p1 AND bOwnerType=0 AND bStorageType=0 AND wItemID=@p2)",
            MasteryDlId, (int)id, (int)MasteryItem);
    }

    private static async Task ClearCompanions(GameDb db, uint id)
    {
        foreach (var t in new[] { "TCOMPANIONTABLE", "TCOMPANIONITEMTABLE", "TLASTCOMPANIONTABLE", "TMEDALS" })
            await db.ExecAsync($"DELETE FROM {t} WHERE dwCharID=@p0", (int)id);
    }

    private static async Task Restore(GameDb db, Saved s, uint idA, uint idB)
    {
        foreach (var (id, v) in new[] { (idA, s.A), (idB, s.B) })
            await db.ExecAsync(@"UPDATE TCHARTABLE SET dwGold=@p1, dwSilver=@p2, dwCooper=@p3, dwHP=@p4, wMapID=@p5, dwRegion=@p6,
                fPosX=@p7, fPosY=@p8, fPosZ=@p9, bAftermath=@p10, bSex=@p11, dwEXP=@p12, bLevel=@p13, szName=@p14 WHERE dwCharID=@p0", (int)id, v[0], v[1], v[2],
                v[3], v[4], v[5], v[6], v[7], v[8], v[9], v[10], v[11], v[12], v[13]);
        // A rename left half-way (the name change test) is undone in TGlobal's character list too.
        await db.ExecAsync("UPDATE TGLOBAL_GSP.dbo.TALLCHARTABLE SET szName=@p0 WHERE szName=@p1", s.A[13], NewName);
        // The gender potion also went to TGlobal's character list (TSaveCharBase chains to TGLOBAL_GSP).
        await db.ExecAsync("UPDATE TGLOBAL_GSP.dbo.TALLCHARTABLE SET bSex=@p1 WHERE szName=(SELECT szName FROM TCHARTABLE WHERE dwCharID=@p0)",
            (int)idA, s.A[10]);
        foreach (var (id, row) in new[] { (idA, s.PvpA), (idB, s.PvpB) })
            await SetPvPoint(db, id, row is null ? 0 : Convert.ToInt64(row[0]), row is null ? 0 : Convert.ToInt64(row[1]), keep: row is not null);
        await db.ExecAsync("DELETE FROM TITEMTABLE WHERE dwOwnerID=@p0 AND bOwnerType=0 AND bStorageType=0 AND wItemID=@p1", (int)idB, (int)BagItem);
        await db.ExecAsync("DELETE FROM TINVENTABLE WHERE dwCharID=@p0 AND bInvenID IN (0,1)", (int)idB);
        await db.ExecAsync("DELETE FROM THOTKEYTABLE WHERE dwCharID=@p0 AND bInvenID=@p1", (int)idA, (int)HotkeyPage);
        await db.ExecAsync("DELETE FROM TPETTABLE WHERE dwUserID=(SELECT dwUserID FROM TCHARTABLE WHERE dwCharID=@p0) AND wPetID=@p1",
            (int)idA, (int)HorsePet);
        await db.ExecAsync("DELETE FROM TITEMTABLE WHERE dwOwnerID=@p0 AND bOwnerType=0 AND wItemID=@p1", (int)idA, (int)HorseItem);
        await db.ExecAsync("DELETE FROM TITEMTABLE WHERE dwOwnerID=@p0 AND bOwnerType=0 AND wItemID=@p1", (int)idA, (int)RuneItem);
        await ClearCompanions(db, idA);
        await ResetSand(db, idA);
        await db.ExecAsync("DELETE FROM TSKILLTABLE WHERE dwCharID=@p0 AND wSkillID IN (@p1, @p2, @p3)", (int)idA, (int)RitualSkill, (int)IceRainSkill,
            (int)DefenceBreak);
        await db.ExecAsync("DELETE FROM TSKILLMAINTAINTABLE WHERE dwCharID=@p0 AND wSkillID=@p1", (int)idB, (int)DefenceBreak);
        await ClearLeftovers(db, idA, idB);
        await ClearItemSkills(db, idA);
        await ClearPassives(db, idA);
        await ClearSmallRequests(db, idA);
        foreach (var t in RankTables)
        {
            await db.ExecAsync($"DELETE FROM {t} WHERE dwCharID IN (@p0, @p1)", (int)idA, (int)idB);
            await db.ExecAsync($"IF OBJECT_ID('TBOT_BAK_{t}') IS NOT NULL BEGIN INSERT INTO {t} SELECT * FROM TBOT_BAK_{t}; DROP TABLE TBOT_BAK_{t} END");
        }
        await db.ExecAsync("DELETE FROM charkilling_log WHERE dwKillerID=@p0 AND dwTargetID=@p1", (int)idA, (int)idB);
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] fixtures restored");
    }

    // ================================ packets ================================

    private static PacketWriter Req(ushort id, Action<PacketWriter> body)
    {
        var w = new PacketWriter(id);
        body(w);
        return w;
    }

    private static PacketWriter PostSend(string target, string title, string text, uint cooper) => Req(CS_POSTSEND_REQ, w =>
    {
        w.WriteString(target); w.WriteString(title); w.WriteString(text); w.WriteByte(0);   // POST_NORMAL
        w.WriteUInt32(0); w.WriteUInt32(0); w.WriteUInt32(cooper);
        w.WriteByte(0xFC); w.WriteByte(0);                                                // INVEN_NULL: no item
    });

    private readonly record struct Join(ushort PartyId, string Name, uint MemberId, uint ChiefId, ushort Commander, byte Level,
        uint MaxHp, uint Hp, byte PartyType, byte Class);

    // TClient OnCS_PARTYJOIN_ACK (CSHandler.cpp:4942-4958).
    private static Join? ParseJoin(PacketReader p) => Read(p, r =>
    {
        ushort party = r.ReadUInt16(); string name = r.ReadString(); uint member = r.ReadUInt32(); uint chief = r.ReadUInt32();
        ushort cmd = r.ReadUInt16(); r.ReadString(); byte level = r.ReadByte(); uint maxHp = r.ReadUInt32(), hp = r.ReadUInt32();
        r.ReadUInt32(); r.ReadUInt32(); r.ReadByte(); r.ReadByte(); r.ReadByte(); r.ReadByte();
        byte type = r.ReadByte(), cls = r.ReadByte();
        return (Join?)new Join(party, name, member, chief, cmd, level, maxHp, hp, type, cls);
    });

    // TClient OnCS_HOTKEYCHANGE_ACK (CSHandler.cpp:5880-5902).
    private static (byte Set, List<(byte Slot, byte Type, ushort Id)> Slots) ParseHotkey(PacketReader p) => Read(p, r =>
    {
        byte set = r.ReadByte(), n = r.ReadByte();
        var slots = new List<(byte, byte, ushort)>();
        for (int i = 0; i < n; i++) slots.Add((r.ReadByte(), r.ReadByte(), r.ReadUInt16()));
        return (set, slots);
    });

    /// <summary>Parses a reply the way the client does and records whether that used up every byte of it.</summary>
    private static T Read<T>(PacketReader r, Func<PacketReader, T> parse)
    {
        T v = parse(r);
        if (r.Remaining != 0) Check($"byte check: 0x{r.Id:X4} fully read", false, $"{r.Remaining} byte(s) left over");
        else Checked.Add(r.Id);
        return v;
    }

    private static readonly HashSet<ushort> Checked = new();

    private static string ByteReport(Bot bot)
    {
        var ids = bot.Received.GroupBy(PacketHeader.ReadId).OrderBy(g => g.Key)
            .Select(g => $"{(Checked.Contains(g.Key) ? "+" : "")}0x{g.Key:X4}*{g.Count()}");
        return string.Join(" ", ids);
    }

    private static string Describe(PacketReader? r)
    {
        if (r is null) return "no reply";
        return Bot.Raw.TryGetValue(r, out var raw)
            ? $"0x{r.Id:X4} body {Convert.ToHexString(raw, PacketHeader.Size, raw.Length - PacketHeader.Size)}"
            : $"0x{r.Id:X4}";
    }

    // ================================ reporting ================================

    private static bool Check(string name, bool ok, string detail)
    {
        Results.Add((name, ok, ok ? "" : detail));
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {(ok ? "PASS" : "FAIL")} {name}{(ok || detail.Length == 0 ? "" : " — " + detail)}");
        return ok;
    }

    private static void Report(string name, string detail) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {name}: {detail}");

    private static BotConfig Clone(BotConfig c, string account, string tag) => new()
    {
        LoginHost = c.LoginHost, LoginPort = c.LoginPort, Account = account, Password = c.Password, GroupId = c.GroupId,
        Channel = c.Channel, NoCrypt = c.NoCrypt, MapNoCrypt = c.MapNoCrypt, OverrideVersion = c.OverrideVersion, LogTag = tag,
    };
}
