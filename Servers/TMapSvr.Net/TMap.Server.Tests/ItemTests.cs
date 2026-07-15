using TMap.Data;
using TMap.Protocol;
using TMap.Server.Map;
using Xunit;

namespace TMap.Server.Tests;

public class ItemTests
{
    // ---- a character with gear/skills/hotkeys to inject (DB-free) ----
    private static Character GearedChar(uint charId) => Configure(charId);

    private static Character Configure(uint charId)
    {
        var ch = new Character { CharId = charId, Name = "Geared", Level = 20, Class = 2, Race = 1 };

        var backpack = new Inven { InvenId = 0xFF };
        var potion = new Item
        {
            ItemSlot = 0, TemplateId = 1234, Level = 1, Count = 5, DuraMax = 100, DuraCur = 90,
            RefineCur = 3, GLevel = 2, GradeEffect = 1, Gem = 4, MoggItemId = 77,
        };
        potion.Ext[Item.IevColor] = 7;
        potion.Ext[Item.IevCompanion] = 55;
        potion.Magic.Add(new MagicOption(3, 100));
        potion.Magic.Add(new MagicOption(9, 250));
        backpack.Items.Add(potion);

        var equip = new Inven { InvenId = 0xFE };
        equip.Items.Add(new Item { ItemSlot = 1, TemplateId = 5678, Level = 10, RefineCur = 2, Count = 1 });
        ch.Invens.Add(backpack);
        ch.Invens.Add(equip);

        ch.Skills.Add(new Skill { SkillId = 100, Level = 3, ReuseRemainTick = 0 });
        ch.Skills.Add(new Skill { SkillId = 200, Level = 1, ReuseRemainTick = 500 });

        var page = new HotkeyPage { InvenKey = 0 };
        page.Slots[0] = new HotkeySlot(1, 100);
        page.Slots[5] = new HotkeySlot(2, 1234);
        ch.HotkeyPages.Add(page);
        return ch;
    }

    [Fact]
    public void WrapPacketClient_Roundtrips()
    {
        var it = new Item
        {
            ItemSlot = 3, TemplateId = 4444, Level = 7, Gem = 2, MoggItemId = 88, Count = 12,
            DuraMax = 300, DuraCur = 275, RefineMax = 6, RefineCur = 4, GLevel = 5, EndTime = 1_700_000_000,
            GradeEffect = 1,
        };
        it.Ext[Item.IevEld] = 1;
        it.Ext[Item.IevWrap] = 1;
        it.Ext[Item.IevColor] = 9;
        it.Ext[Item.IevCustomTex] = 321;
        it.Ext[Item.IevCompanion] = 654;
        it.Ext[Item.IevGuild] = 42;              // owner charId below → regGuild flag set
        it.Magic.Add(new MagicOption(11, 1000));

        var w = new PacketWriter(0x1234);
        it.WrapPacketClient(w, ownerCharId: 42);
        var r = new PacketReader(w.ToArray());

        var p = Wire.ParseItem(r);
        Assert.Equal(3, p.Slot);
        Assert.Equal(4444, p.TemplateId);
        Assert.Equal(7, p.Level);
        Assert.Equal(2, p.Gem);
        Assert.Equal(88, p.MoggItemId);
        Assert.Equal(654, p.Companion);
        Assert.Equal(12, p.Count);
        Assert.Equal(300u, p.DuraMax);
        Assert.Equal(275u, p.DuraCur);
        Assert.Equal(6, p.RefineMax);
        Assert.Equal(4, p.RefineCur);
        Assert.Equal(5, p.GLevel);
        Assert.Equal(1_700_000_000L, p.EndTime);
        Assert.Equal(1, p.GradeEffect);
        Assert.Equal(1, p.Eld);
        Assert.Equal(1, p.Wrap);
        Assert.Equal(9, p.Color);
        Assert.Equal(321, p.CustomTex);
        Assert.Equal(1, p.RegGuild);             // guild ext == owner id → flag set
        Assert.Equal((11, (ushort)1000), Assert.Single(p.Magic));
    }

    [Fact]
    public void RegGuild_ClearedWhenGuildExtDiffersFromOwner()
    {
        var it = new Item { ItemSlot = 0, TemplateId = 1 };
        it.Ext[Item.IevGuild] = 99;
        var w = new PacketWriter(0x1);
        it.WrapPacketClient(w, ownerCharId: 42); // 99 != 42
        Assert.Equal(0, Wire.ParseItem(new PacketReader(w.ToArray())).RegGuild);
    }

    [Fact]
    public void WrapPacketClient_UsesTemplateRefineMax_AndComputesMagicValue()
    {
        // Item template: refine cap 8, RV_PHYSIC revision = 2.0. Magic id 5: RvType 1 (→ Revision[0]=2.0), max 30.
        var itemT = new ItemTemplate(4444, RefineMax: 8, Revision: new[] { 2.0f, 0f, 0f, 0f });
        var magT = new MagicTemplate(5, RvType: 1, MaxValue: 30);

        var it = new Item { ItemSlot = 0, TemplateId = 4444, RefineCur = 4, RefineMax = 99, Template = itemT };
        it.Magic.Add(new MagicOption(5, 100, magT)); // stored value 100

        var w = new PacketWriter(0x1);
        it.WrapPacketClient(w, ownerCharId: 1);
        var p = Wire.ParseItem(new PacketReader(w.ToArray()));

        Assert.Equal(8, p.RefineMax); // from the template, NOT the per-instance 99
        // GetMagicValue = max( (ushort)(int)(2.0 * 100 * 30) / 100, 1 ) = max(6000/100, 1) = 60
        Assert.Equal((5, (ushort)60), Assert.Single(p.Magic));
    }

    [Fact]
    public void GetMagicValue_FlooredToMinimumOne_AndRvTypeZeroUsesFactorOne()
    {
        var itemT = new ItemTemplate(1, RefineMax: 0, Revision: new[] { 5.0f, 0f, 0f, 0f });
        var magT = new MagicTemplate(1, RvType: 0, MaxValue: 1); // RvType 0 → revision factor forced to 1.0
        var it = new Item { TemplateId = 1, Template = itemT };
        it.Magic.Add(new MagicOption(1, 1, magT)); // 1.0 * 1 * 1 / 100 = 0 → floored to 1

        var w = new PacketWriter(0x1);
        it.WrapPacketClient(w, ownerCharId: 1);
        var p = Wire.ParseItem(new PacketReader(w.ToArray()));

        Assert.Equal((1, (ushort)1), Assert.Single(p.Magic));
    }

    [Fact]
    public void WrapPacketClient_NoTemplate_EmitsRawValuesAndPerInstanceRefineMax()
    {
        var it = new Item { TemplateId = 1, RefineMax = 6 };
        it.Magic.Add(new MagicOption(3, 250)); // no template → raw stored value

        var w = new PacketWriter(0x1);
        it.WrapPacketClient(w, ownerCharId: 1);
        var p = Wire.ParseItem(new PacketReader(w.ToArray()));

        Assert.Equal(6, p.RefineMax);                           // per-instance fallback (DB-free)
        Assert.Equal((3, (ushort)250), Assert.Single(p.Magic)); // raw stored value
    }

    [Fact]
    public void AddPersistedMagic_AppliesCppCreateItemFilter()
    {
        var t = new TemplateStore();
        t.Magics[3] = new MagicTemplate(3, 0, 1);
        t.Magics[9] = new MagicTemplate(9, 0, 1);
        // (3,100) keep · (0,50) skip id0 · (5,0) skip value0 · (7,10) skip unknown id (chart loaded) · (3,200) dup→last wins.
        var ids = new byte[] { 3, 0, 5, 7, 3, 0 };
        var vals = new ushort[] { 100, 50, 0, 10, 200, 0 };
        var item = new Item { TemplateId = 1, Template = new ItemTemplate(1, 0, new[] { 1f, 0f, 0f, 0f }) };

        Item.AddPersistedMagic(item, ids, vals, t);

        var m = Assert.Single(item.Magic);
        Assert.Equal((byte)3, m.Id);
        Assert.Equal((ushort)200, m.Value); // last slot with id 3 wins
    }

    [Fact]
    public void AddPersistedMagic_KeepsUnknownId_Raw_WhenChartNotLoaded()
    {
        var t = new TemplateStore(); // no magic chart → DB-free: keep with raw value / null template
        var item = new Item { TemplateId = 1 };
        Item.AddPersistedMagic(item, new byte[] { 7 }, new ushort[] { 10 }, t);
        var m = Assert.Single(item.Magic);
        Assert.Equal((byte)7, m.Id);
        Assert.Null(m.Template);
        Assert.Equal((ushort)10, m.Value);
    }

    [Fact]
    public async Task CharInfoAck_CarriesInventorySkillsAndHotkeys()
    {
        var h = new MapTestHarness();
        var (s, client) = await h.EnterAsync(9, 1, 1, name: "Geared", preSeeded: GearedChar(9));

        var pkt = client.Last(Msg.CS_CHARINFO_ACK);
        Assert.NotNull(pkt);
        var info = Wire.ParseCharInfo(pkt!);

        Assert.Equal(2, info.Invens.Count); // backpack + equip
        var backpack = info.Invens.Single(i => i.InvenId == 0xFF);
        var item = Assert.Single(backpack.Items);
        Assert.Equal(1234, item.TemplateId);
        Assert.Equal(5, item.Count);
        Assert.Equal(7, item.Color);
        Assert.Equal(55, item.Companion);
        Assert.Equal(2, item.Magic.Count);
        Assert.Equal((3, (ushort)100), item.Magic[0]);

        var equip = info.Invens.Single(i => i.InvenId == 0xFE);
        Assert.Equal(5678, Assert.Single(equip.Items).TemplateId);

        Assert.Equal(2, info.Skills.Count);
        Assert.Contains(info.Skills, sk => sk.Id == 100 && sk.Level == 3);
        Assert.Contains(info.Skills, sk => sk.Id == 200 && sk.Reuse == 500);

        var pg = Assert.Single(info.Hotkeys);
        Assert.Equal((byte)1, pg.Slots[0].Type);
        Assert.Equal((ushort)100, pg.Slots[0].Id);
        Assert.Equal((ushort)1234, pg.Slots[5].Id);
    }

    [Fact]
    public async Task CharInfoAck_EmitsItemsAndMagicInAscendingKeyOrder()
    {
        // C++ holds items in map<BYTE slot> and magic in map<BYTE id>, so the wire is key-sorted.
        // Inject them OUT of order and assert the serializer sorts to match.
        var ch = new Character { CharId = 3, Name = "Sorted", Level = 1 };
        var bag = new Inven { InvenId = 0xFF };
        foreach (var slot in new byte[] { 3, 1, 2 })
            bag.Items.Add(new Item { ItemSlot = slot, TemplateId = (ushort)(1000 + slot), Count = 1 });
        bag.Items[0].Magic.Add(new MagicOption(9, 90));  // on the slot-3 item, ids out of order
        bag.Items[0].Magic.Add(new MagicOption(3, 30));
        ch.Invens.Add(bag);

        var h = new MapTestHarness();
        var (_, client) = await h.EnterAsync(3, 1, 1, name: "Sorted", preSeeded: ch);
        var info = Wire.ParseCharInfo(client.Last(Msg.CS_CHARINFO_ACK)!);

        var backpack = info.Invens.Single(i => i.InvenId == 0xFF);
        Assert.Equal(new byte[] { 1, 2, 3 }, backpack.Items.Select(i => i.Slot).ToArray()); // ascending by slot
        var slot3 = backpack.Items.Single(i => i.Slot == 3);
        Assert.Equal(new[] { ((byte)3, (ushort)30), ((byte)9, (ushort)90) }, slot3.Magic.ToArray()); // ascending by id
    }

    [Fact]
    public async Task EnterAck_CarriesEquippedGear()
    {
        var h = new MapTestHarness();
        var (sa, ca) = await h.EnterAsync(1, 1, 1, name: "Alice", mapId: 0);
        ca.Clear();
        // Bob enters with gear; Alice (a neighbour) receives Bob's CS_ENTER_ACK with his equipped items.
        var (sb, cb) = await h.EnterAsync(2, 2, 2, name: "Bob", mapId: 0, preSeeded: GearedChar(2));

        var enter = ca.Last(Msg.CS_ENTER_ACK);
        Assert.NotNull(enter);
        var equip = Wire.ParseEnterEquip(enter!);
        Assert.Equal(5678, Assert.Single(equip).TemplateId);
    }
}
