using TMap.Data;
using TMap.Protocol;
using TMap.Server.Map;
using Xunit;

namespace TMap.Server.Tests;

/// <summary>
/// The action bar (C++ <c>CTPlayer::AddHotKey</c>/<c>EraseHotKey</c>, TPlayer.cpp:822-907) and its save
/// (<c>SendDM_SAVECHAR_REQ</c> → <c>TSaveHotkey</c>). Before this the bar was load-only: every change the player
/// made was dropped, so it reset on relog.
/// </summary>
public class HotkeyTests
{
    private const byte Skill = 1, Item = 2;

    private static byte[] Add(byte type, ushort id, byte inven, byte pos)
    {
        var w = new PacketWriter(Msg.CS_HOTKEYADD_REQ);
        w.WriteByte(type); w.WriteUInt16(id); w.WriteByte(inven); w.WriteByte(pos);
        return w.ToArray();
    }

    private static byte[] Del(byte inven, byte pos)
    {
        var w = new PacketWriter(Msg.CS_HOTKEYDEL_REQ);
        w.WriteByte(inven); w.WriteByte(pos);
        return w.ToArray();
    }

    private static List<(byte inven, List<(byte pos, byte type, ushort id)> keys)> Changes(FakeClientChannel c)
        => c.WithId(Msg.CS_HOTKEYCHANGE_ACK).Select(p =>
        {
            var r = new PacketReader(p);
            byte inven = r.ReadByte(), n = r.ReadByte();
            var keys = new List<(byte, byte, ushort)>();
            for (int i = 0; i < n; i++) keys.Add((r.ReadByte(), r.ReadByte(), r.ReadUInt16()));
            return (inven, keys);
        }).ToList();

    private static async Task<(MapTestHarness h, ClientSession s, FakeClientChannel c, Character ch)> Setup(TemplateStore? t = null)
    {
        var h = new MapTestHarness(t ?? new TemplateStore());
        var ch = new Character { CharId = 1, Name = "Hero", MaxHp = 100, Hp = 100 };
        var (s, c) = await h.EnterAsync(1, 1, 1, preSeeded: ch);
        c.Clear();
        return (h, s, c, ch);
    }

    [Fact]
    public async Task AddingToANewPage_CreatesIt_AndConfirmsTheKey()
    {
        var (h, s, c, ch) = await Setup();

        await h.Service.DispatchClientAsync(s, Add(Item, 501, inven: 0, pos: 3));

        var page = Assert.Single(ch.HotkeyPages);
        Assert.Equal(new HotkeySlot(Item, 501), page.Slots[3]);
        Assert.Equal(HotkeyPage.SaveInsert, page.Save);
        var ack = Assert.Single(Changes(c));
        Assert.Equal((byte)0, ack.inven);
        Assert.Equal(new[] { ((byte)3, Item, (ushort)501) }, ack.keys);
    }

    [Fact]
    public async Task MovingAKey_ClearsItsOldSlot_AndTheTarget()
    {
        var (h, s, c, ch) = await Setup();
        await h.Service.DispatchClientAsync(s, Add(Item, 501, 0, 1));
        await h.Service.DispatchClientAsync(s, Add(Item, 777, 0, 5));
        c.Clear();

        await h.Service.DispatchClientAsync(s, Add(Item, 501, 0, 5));   // drop 501 onto 777's slot

        var page = ch.HotkeyPages[0];
        Assert.Equal(new HotkeySlot(0, 0), page.Slots[1]);             // its old slot is cleared
        Assert.Equal(new HotkeySlot(Item, 501), page.Slots[5]);        // 777 is gone, 501 is there
        // One single-entry ACK per clear (slot 1, then slot 5), then the add — the C++ order.
        Assert.Equal(new[] { (byte)1, (byte)5, (byte)5 }, Changes(c).Select(x => x.keys.Single().pos));
    }

    [Fact]
    public async Task Deleting_EmptiesTheSlot()
    {
        var (h, s, c, ch) = await Setup();
        await h.Service.DispatchClientAsync(s, Add(Item, 501, 0, 2));
        c.Clear();

        await h.Service.DispatchClientAsync(s, Del(0, 2));

        Assert.Equal(new HotkeySlot(0, 0), ch.HotkeyPages[0].Slots[2]);
        Assert.Equal(new[] { ((byte)2, (byte)0, (ushort)0) }, Assert.Single(Changes(c)).keys);
    }

    [Fact]
    public async Task APositionPastTheBar_IsIgnored()
    {
        var (h, s, c, ch) = await Setup();

        await h.Service.DispatchClientAsync(s, Add(Item, 501, 0, 12));   // MAX_HOTKEY_POS = 12

        Assert.Empty(ch.HotkeyPages);
        Assert.False(c.Has(Msg.CS_HOTKEYCHANGE_ACK));
    }

    // ================= global skills =================

    private static TemplateStore WithGlobalSkill(ushort id)
    {
        var t = new TemplateStore();
        t.Skills[id] = new SkillTemplate(id, Kind: 0, UseMp: 0, UseMpType: 0, UseHp: 0, UseHpType: 0,
            StartLevel: 1, MaxLevel: 1, NextLevel: 0, ReuseDelay: 0, ReuseDelayInc: 0, LoopDelay: 0, KindDelay: 0,
            SpeedApply: 0, Positive: 1, MapId: 0xFFFF, Global: true);
        return t;
    }

    [Fact]
    public async Task PlacingAGlobalSkill_GrantsIt_AndClearingItRevokesIt()
    {
        var (h, s, c, ch) = await Setup(WithGlobalSkill(900));

        await h.Service.DispatchClientAsync(s, Add(Skill, 900, 0, 0));
        Assert.Contains(ch.Skills, k => k.SkillId == 900);
        Assert.True(c.Has(Msg.CS_SKILLBUY_ACK));

        await h.Service.DispatchClientAsync(s, Del(0, 0));
        Assert.DoesNotContain(ch.Skills, k => k.SkillId == 900);
    }

    [Fact]
    public async Task AnOrdinarySkill_IsNotGrantedByTheBar()
    {
        var t = WithGlobalSkill(900);
        t.Skills[900] = t.Skills[900] with { Global = false };
        var (h, s, _, ch) = await Setup(t);

        await h.Service.DispatchClientAsync(s, Add(Skill, 900, 0, 0));

        Assert.DoesNotContain(ch.Skills, k => k.SkillId == 900);
    }

    // ================= save verbs =================

    [Fact]
    public void TheSaveVerbs_MatchTheProc_AndResetTheFlag()
    {
        var ch = new Character();
        var fresh = new HotkeyPage { InvenKey = 0, Save = HotkeyPage.SaveInsert };
        fresh.Slots[0] = new HotkeySlot(Item, 1);
        var edited = new HotkeyPage { InvenKey = 1, Save = HotkeyPage.SaveLoad | HotkeyPage.SaveUpdate };
        edited.Slots[0] = new HotkeySlot(Item, 2);
        var untouched = new HotkeyPage { InvenKey = 2 };
        untouched.Slots[0] = new HotkeySlot(Item, 3);
        var emptied = new HotkeyPage { InvenKey = 3, Save = HotkeyPage.SaveUpdate };
        ch.HotkeyPages.AddRange(new[] { fresh, edited, untouched, emptied });

        var rows = MapService.BuildHotkeySaves(ch);

        // TSaveHotkey: 2 = INSERT, 3 = UPDATE, anything else = DELETE. Untouched pages are not sent.
        Assert.Equal(new[] { ((byte)0, (byte)2), ((byte)1, (byte)3), ((byte)3, (byte)1) },
            rows.Select(r => (r.InvenKey, r.Verb)));
        Assert.All(new[] { fresh, edited, emptied }, p => Assert.Equal(HotkeyPage.SaveLoad, p.Save));
        // Saved once ⇒ nothing new to write; an empty page is re-deleted every time, exactly as the C++ does.
        Assert.DoesNotContain(MapService.BuildHotkeySaves(ch), r => r.Verb != 1);
    }
}
