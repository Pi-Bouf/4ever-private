using TMap.Data;
using TMap.Protocol;
using TMap.Server.Map;
using Xunit;

namespace TMap.Server.Tests;

/// <summary>
/// Phase-5 stat engine: the primary stats and computed MaxHP/MaxMP (C++ CTObjBase). Value-exact unit tests
/// with hand-built charts, plus an integration test that the computed vitals reach CS_CHARINFO_ACK.
/// </summary>
public class StatTests
{
    // FTYPE_HP=8, FTYPE_MP=19; class/race seeds → CON base 16, MEN base 11, STR base 9 (incl. BASE_STAT 1).
    private static TemplateStore StatStore(float rate1st = 1.0f)
    {
        var t = new TemplateStore { Rate1st = rate1st };
        t.Formulas[8] = new FormulaRow(50, 5.0f, 0f);   // MaxHP = 50 + CON*5
        t.Formulas[19] = new FormulaRow(20, 3.0f, 0f);  // MaxMP = 20 + MEN*3
        t.Classes[1] = new StatSeed(3, 0, 5, 0, 0, 2);  // STR DEX CON INT WIS MEN
        t.Races[1] = new StatSeed(5, 0, 10, 0, 0, 8);
        return t;
    }

    [Fact]
    public void MaxHp_MaxMp_AndStats_AreValueExact()
    {
        var t = StatStore();
        var ch = new Character { Class = 1, Race = 1, Level = 1 };

        Assert.Equal((ushort)9, StatEngine.Ability(ch, StatEngine.MtypeStr, t));  // 1 + 5 + 3
        Assert.Equal((ushort)16, StatEngine.Ability(ch, StatEngine.MtypeCon, t)); // 1 + 10 + 5
        Assert.Equal((ushort)11, StatEngine.Ability(ch, StatEngine.MtypeMen, t)); // 1 + 8 + 2
        Assert.Equal(130u, StatEngine.MaxHp(ch, t)); // 50 + (16 * 5.0)
        Assert.Equal(53u, StatEngine.MaxMp(ch, t));  // 20 + (11 * 3.0)
    }

    [Fact]
    public void BaseStat_ScalesByPowRate1stPerLevel()
    {
        var t = StatStore(rate1st: 1.1f);
        var ch = new Character { Class = 1, Race = 1, Level = 11 }; // pow(1.1, 10)
        float con = (float)(16 * Math.Pow(1.1f, 10));               // C++ double pow, narrowed once ≈ 41.5

        Assert.Equal((ushort)con, StatEngine.Ability(ch, StatEngine.MtypeCon, t));
        Assert.Equal(50u + (uint)(con * 5.0f), StatEngine.MaxHp(ch, t));
    }

    [Fact]
    public void MaxHp_IncludesEquippedConEnchant()
    {
        var t = StatStore();
        var ch = new Character { Class = 1, Race = 1, Level = 1 };

        // A ring with a CON enchant computing to +10 (rev 1.0 via RvType 0, value 1000, max 1 → 1000/100).
        var equip = new Inven { InvenId = 0xFE };
        var ring = new Item { TemplateId = 500, Template = new ItemTemplate(500, 0, new[] { 1f, 0f, 0f, 0f }) };
        ring.Magic.Add(new MagicOption(StatEngine.MtypeCon, 1000, new MagicTemplate(StatEngine.MtypeCon, 0, 1)));
        equip.Items.Add(ring);
        ch.Invens.Add(equip);

        Assert.Equal((ushort)26, StatEngine.Ability(ch, StatEngine.MtypeCon, t)); // 16 base + 10 enchant
        Assert.Equal(180u, StatEngine.MaxHp(ch, t));                              // 50 + (26 * 5.0)
    }

    [Fact]
    public void MaxHp_MaxMp_FallBackToSynthesized_WhenChartsAbsent()
    {
        var ch = new Character { Class = 1, Race = 1, Level = 5, MaxHp = 100, MaxMp = 77 };
        var empty = new TemplateStore();
        Assert.Equal(100u, StatEngine.MaxHp(ch, empty));
        Assert.Equal(77u, StatEngine.MaxMp(ch, empty));
    }

    [Fact]
    public async Task CharInfoAck_CarriesComputedMaxHp_AndClampsCurrentDown()
    {
        var h = new MapTestHarness(StatStore()); // rate1st 1.0 → level-independent
        var ch = new Character { CharId = 7, Name = "Vit", Class = 1, Race = 1, Hp = 999, Mp = 999 };
        var (_, client) = await h.EnterAsync(7, 1, 1, name: "Vit", preSeeded: ch);

        var info = Wire.ParseCharInfo(client.Last(Msg.CS_CHARINFO_ACK)!);
        Assert.Equal(130u, info.MaxHp);
        Assert.Equal(53u, info.MaxMp);
        Assert.Equal(130u, info.Hp); // current 999 clamped down to computed max
        Assert.Equal(53u, info.Mp);
    }

    // ===== Phase 6: AP/DP + CS_CHARSTATINFO =====

    // StatStore + combat formulas. Base stats (rate1st 1.0): STR 9, DEX 1, CON 16, INT 1, WIS 1, MEN 11.
    private static TemplateStore CombatStore()
    {
        var t = StatStore();
        t.Formulas[1] = new FormulaRow(10, 2.0f, 0f);   // FTYPE_PAP: melee AP = 10 + STR·2
        t.Formulas[3] = new FormulaRow(0, 1.0f, 0f);    // FTYPE_LAP: ranged AP = DEX·1
        t.Formulas[12] = new FormulaRow(5, 3.0f, 1.0f); // FTYPE_PDP: 5 + pow(1.0,level)·3
        t.Formulas[13] = new FormulaRow(0, 1.0f, 0f);   // FTYPE_MAP: magic AP = INT·1
        t.Formulas[23] = new FormulaRow(2, 4.0f, 1.0f); // FTYPE_MDP: 2 + pow(1.0,level)·4
        return t;
    }

    [Fact]
    public void Ap_Dp_ValueExact_ForNakedChar()
    {
        var t = CombatStore();
        var ch = new Character { Class = 1, Race = 1, Level = 1 };
        Assert.Equal(28u, StatEngine.MaxAp(ch, arrow: false, t)); // 10 + STR9·2
        Assert.Equal(1u, StatEngine.MaxAp(ch, arrow: true, t));   // 0 + DEX1·1
        Assert.Equal(1u, StatEngine.MaxMagicAp(ch, t));           // 0 + INT1·1
        Assert.Equal(8u, StatEngine.DefendPower(ch, t));          // 5 + pow(1,1)·3
        Assert.Equal(6u, StatEngine.MagicDefPower(ch, t));        // 2 + pow(1,1)·4
    }

    [Fact]
    public void EquippedWeapon_AddsAttrApThroughGetter()
    {
        var t = CombatStore();
        var ch = new Character { Class = 1, Race = 1, Level = 1 };
        var equip = new Inven { InvenId = 0xFE };
        equip.Items.Add(new Item
        {
            ItemSlot = 0, TemplateId = 900, DuraMax = 100, DuraCur = 100, // ES_PRMWEAPON, unbroken
            Template = new ItemTemplate(900, 0, new[] { 1f, 0f, 0f, 0f }, Type: 1), // IT_WEAPON
            Attr = new ItemAttr(1, 0, 0, MinAp: 30, MaxAp: 50, Dp: 0, MinMagicAp: 0, MaxMagicAp: 0, MagicDp: 0, BlockProb: 0),
        });
        ch.Invens.Add(equip);

        Assert.Equal(78u, StatEngine.MaxAp(ch, arrow: false, t)); // base 28 + weapon MaxAP 50
        Assert.Equal(58u, StatEngine.MinAp(ch, arrow: false, t)); // base 28 + weapon MinAP 30 (≤ max 78)
    }

    [Fact]
    public void BrokenWeapon_ContributesNothing()
    {
        var t = CombatStore();
        var ch = new Character { Class = 1, Race = 1, Level = 1 };
        var equip = new Inven { InvenId = 0xFE };
        equip.Items.Add(new Item
        {
            ItemSlot = 0, TemplateId = 900, DuraMax = 100, DuraCur = 0, // broken → HasPower() false
            Template = new ItemTemplate(900, 0, new[] { 1f, 0f, 0f, 0f }, Type: 1),
            Attr = new ItemAttr(1, 0, 0, 30, 50, 0, 0, 0, 0, 0),
        });
        ch.Invens.Add(equip);

        Assert.Equal(28u, StatEngine.MaxAp(ch, arrow: false, t)); // broken weapon skipped → base only
    }

    [Fact]
    public async Task CharStatInfo_Ack_CarriesTheFull87ByteSheet()
    {
        var h = new MapTestHarness(CombatStore());
        var ch = new Character { CharId = 5, Name = "Fighter", Class = 1, Race = 1 };
        var (session, client) = await h.EnterAsync(5, 1, 1, name: "Fighter", preSeeded: ch);
        client.Clear();

        await h.Service.DispatchClientAsync(session, MapTestHarness.CharStatInfoReq(5));

        var pkt = client.Last(Msg.CS_CHARSTATINFO_ACK);
        Assert.NotNull(pkt);
        var r = new PacketReader(pkt!);
        Assert.Equal(5u, r.ReadUInt32());          // 1  charId
        Assert.Equal((ushort)9, r.ReadUInt16());   // 2  STR
        Assert.Equal((ushort)1, r.ReadUInt16());   // 3  DEX
        Assert.Equal((ushort)16, r.ReadUInt16());  // 4  CON
        Assert.Equal((ushort)1, r.ReadUInt16());   // 5  INT
        Assert.Equal((ushort)1, r.ReadUInt16());   // 6  WIS
        Assert.Equal((ushort)11, r.ReadUInt16());  // 7  MEN
        Assert.Equal(28u, r.ReadUInt32());         // 8  min melee AP (naked)
        Assert.Equal(28u, r.ReadUInt32());         // 9  max melee AP
        Assert.Equal(8u, r.ReadUInt32());          // 10 physical DP
        r.ReadUInt32(); r.ReadUInt32();            // 11,12 ranged min/max
        r.ReadUInt32(); r.ReadUInt32(); r.ReadUInt32(); // 13-15 atk speed
        Assert.Equal(100u, r.ReadUInt32());        // 16 atk-speed-rate phys (no enchants → 100)
        Assert.Equal(100u, r.ReadUInt32());        // 17 long
        Assert.Equal(100u, r.ReadUInt32());        // 18 magic
        r.ReadUInt16(); r.ReadUInt16();            // 19,20 atk/def level
        r.ReadByte();                              // 21 crit phys
        Assert.Equal(1u, r.ReadUInt32());          // 22 min magic AP
        Assert.Equal(1u, r.ReadUInt32());          // 23 max magic AP
        Assert.Equal(6u, r.ReadUInt32());          // 24 magic DP
        r.ReadUInt16(); r.ReadUInt16();            // 25,26 magic atk/def level
        r.ReadByte(); r.ReadByte(); r.ReadByte();  // 27,28,29 charge speed/prob, magic crit
        Assert.Equal((ushort)0, r.ReadUInt16());   // 30 skill point
        Assert.Equal((byte)0, r.ReadByte());       // 31 aftermath step
    }
}
