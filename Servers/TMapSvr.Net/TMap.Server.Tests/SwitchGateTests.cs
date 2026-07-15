using TMap.Data;
using TMap.Protocol;
using TMap.Server.Map;
using Xunit;

namespace TMap.Server.Tests;

/// <summary>
/// Phase 32 — the map switch/gate subsystem: chart-driven per-(channel, map) switches + switch-driven gates,
/// the player toggle (<c>CS_SWITCHCHANGE_REQ</c>) with lock/duration gating, gate propagation
/// (ONESWITCH vs MULTISWITCH), the duration auto-revert, add-on-enter visibility, the <c>TT_RUNSWITCH</c>/
/// <c>TT_RUNGATE</c> quest triggers, the <c>QCT_SWITCH</c> condition, and the quest <c>Switch</c> subtype.
/// All DB-free (switches/gates injected into the template store, built by <see cref="MapService.InitSwitches"/>).
/// </summary>
public class SwitchGateTests
{
    private const byte OtPc = 1;
    private const byte GtOneSwitch = 0, GtMultiSwitch = 2;

    private static SwitchDef Sw(uint id, byte start = 0, byte lockOpen = 0, byte lockClose = 0, uint duration = 0,
        ushort x = 100, ushort z = 100) => new(id, MapId: 0, x, 0, z, start, lockOpen, lockClose, duration);
    private static GateDef Gate(uint id, uint switchId, byte type = GtOneSwitch, ushort x = 100, ushort z = 100)
        => new(id, switchId, type, MapId: 0, x, 0, z);

    private static TemplateStore Store(SwitchDef[] switches, GateDef[]? gates = null)
    {
        var t = new TemplateStore();
        foreach (var s in switches) t.Switches.Add(s);
        if (gates is not null) foreach (var g in gates) t.Gates.Add(g);
        return t;
    }

    private static byte[] SwitchReq(uint switchId)
        => new PacketWriter(Msg.CS_SWITCHCHANGE_REQ).WriteUInt32(switchId).ToArray();

    /// <summary>Build the switches into the service, then enter a player at (100,100) (cell 1,1 — in view of a
    /// (100,100) switch). Switches are built before enter so the CONREADY add-on-enter fires.</summary>
    private static async Task<(MapTestHarness h, ClientSession s, FakeClientChannel c)> Enter(TemplateStore store)
    {
        var h = new MapTestHarness(store);
        h.Service.InitSwitches();
        var (s, c) = await h.EnterAsync(1, 1, 1, x: 100, z: 100);
        return (h, s, c);
    }

    // ==================== build + visibility ====================

    [Fact]
    public async Task InitSwitches_BuildsSwitchesAndLinksGates()
    {
        var (h, _, _) = await Enter(Store(
            new[] { Sw(500, start: 1) },
            new[] { Gate(700, switchId: 500) }));

        var sw = h.State.FindSwitch(channel: 1, mapId: 0, id: 500);
        Assert.NotNull(sw);
        Assert.True(sw!.Opened);                                  // bStart 1 ⇒ open
        var gate = h.State.FindGate(1, 0, 700);
        Assert.NotNull(gate);
        Assert.True(gate!.Opened);                                // gate seeded from its switch
        Assert.Contains(sw, gate.Switches);                       // two-way link
        Assert.Contains(gate, sw.Gates);
    }

    [Fact]
    public async Task Enter_SendsSwitchAndGateInView()
    {
        var (_, _, c) = await Enter(Store(
            new[] { Sw(500, start: 1) },
            new[] { Gate(700, switchId: 500) }));

        var sw = new PacketReader(c.Last(Msg.CS_SWITCHADD_ACK)!);
        Assert.Equal(500u, sw.ReadUInt32());
        Assert.Equal((byte)1, sw.ReadByte());       // opened
        var gt = new PacketReader(c.Last(Msg.CS_GATEADD_ACK)!);
        Assert.Equal(700u, gt.ReadUInt32());
        Assert.Equal((byte)1, gt.ReadByte());
    }

    // ==================== the player toggle ====================

    [Fact]
    public async Task SwitchChange_TogglesAndBroadcasts()
    {
        var (h, s, c) = await Enter(Store(new[] { Sw(500, start: 0) }));
        c.Clear();

        await h.Service.DispatchClientAsync(s, SwitchReq(500));

        Assert.True(h.State.FindSwitch(1, 0, 500)!.Opened);       // 0 → 1
        var r = new PacketReader(c.Last(Msg.CS_SWITCHCHANGE_ACK)!);
        Assert.Equal((byte)0, r.ReadByte());                      // bResult = SWITCH_SUCCESS
        Assert.Equal(500u, r.ReadUInt32());
        Assert.Equal((byte)1, r.ReadByte());                      // opened
    }

    [Fact]
    public async Task LockOnOpen_BlocksToggleWhileOpen()
    {
        var (h, s, c) = await Enter(Store(new[] { Sw(500, start: 1, lockOpen: 1) }));
        c.Clear();

        await h.Service.DispatchClientAsync(s, SwitchReq(500));

        Assert.True(h.State.FindSwitch(1, 0, 500)!.Opened);       // unchanged (locked open)
        Assert.False(c.Has(Msg.CS_SWITCHCHANGE_ACK));             // silent rejection
    }

    [Fact]
    public async Task LockOnClose_BlocksToggleWhileClosed()
    {
        var (h, s, c) = await Enter(Store(new[] { Sw(500, start: 0, lockClose: 1) }));
        c.Clear();
        await h.Service.DispatchClientAsync(s, SwitchReq(500));
        Assert.False(h.State.FindSwitch(1, 0, 500)!.Opened);      // unchanged (locked closed)
        Assert.False(c.Has(Msg.CS_SWITCHCHANGE_ACK));
    }

    [Fact]
    public async Task Duration_BlocksReToggleWithinCooldown()
    {
        var (h, s, c) = await Enter(Store(new[] { Sw(500, start: 0, duration: 10_000) }));
        h.Service.NowMs = 1_000;   // a nonzero clock so the toggle stamps a real StartTime
        c.Clear();

        await h.Service.DispatchClientAsync(s, SwitchReq(500));    // first flip: ok
        Assert.True(h.State.FindSwitch(1, 0, 500)!.Opened);
        c.Clear();

        await h.Service.DispatchClientAsync(s, SwitchReq(500));    // within the 10s window: rejected
        Assert.True(h.State.FindSwitch(1, 0, 500)!.Opened);        // still open
        Assert.False(c.Has(Msg.CS_SWITCHCHANGE_ACK));
    }

    [Fact]
    public async Task Duration_AutoRevertsAfterDelay()
    {
        var (h, s, c) = await Enter(Store(new[] { Sw(500, start: 0, duration: 5_000) }));
        h.Service.NowMs = 1_000;
        await h.Service.DispatchClientAsync(s, SwitchReq(500));    // 0 → 1 at tick 1000
        Assert.True(h.State.FindSwitch(1, 0, 500)!.Opened);
        c.Clear();

        h.Service.RunSwitchReverts(3_000);                        // before the delay elapses
        Assert.True(h.State.FindSwitch(1, 0, 500)!.Opened);

        h.Service.RunSwitchReverts(6_000);                        // 6000 − 1000 ≥ 5000 ⇒ revert
        Assert.False(h.State.FindSwitch(1, 0, 500)!.Opened);      // flipped back to closed
        Assert.True(c.Has(Msg.CS_SWITCHCHANGE_ACK));              // the revert broadcast
    }

    // ==================== gate propagation ====================

    [Fact]
    public async Task OneSwitchGate_FollowsSwitch()
    {
        var (h, s, c) = await Enter(Store(
            new[] { Sw(500, start: 0) },
            new[] { Gate(700, switchId: 500, type: GtOneSwitch) }));
        c.Clear();

        await h.Service.DispatchClientAsync(s, SwitchReq(500));

        Assert.True(h.State.FindGate(1, 0, 700)!.Opened);         // gate toggled with the switch
        var g = new PacketReader(c.Last(Msg.CS_GATECHANGE_ACK)!);
        Assert.Equal(700u, g.ReadUInt32());
        Assert.Equal((byte)1, g.ReadByte());
    }

    [Fact]
    public async Task MultiSwitchGate_OpensOnlyWhenAllSwitchesMatch()
    {
        // One gate driven by two switches, both initially closed (gate closed).
        var (h, s, c) = await Enter(Store(
            new[] { Sw(500, start: 0), Sw(501, start: 0) },
            new[] { Gate(700, switchId: 500, type: GtMultiSwitch), Gate(700, switchId: 501, type: GtMultiSwitch) }));
        c.Clear();

        await h.Service.DispatchClientAsync(s, SwitchReq(500));    // only switch 500 open ⇒ not all match
        Assert.False(h.State.FindGate(1, 0, 700)!.Opened);        // gate stays closed
        Assert.False(c.Has(Msg.CS_GATECHANGE_ACK));

        await h.Service.DispatchClientAsync(s, SwitchReq(501));    // now both open ⇒ gate flips
        Assert.True(h.State.FindGate(1, 0, 700)!.Opened);
        Assert.True(c.Has(Msg.CS_GATECHANGE_ACK));
    }

    // ==================== quests ====================

    [Fact]
    public async Task QuestSwitch_FlipsTheTermSwitch()
    {
        const ushort Giver = 500;
        var store = Store(new[] { Sw(600, start: 0) });
        var q = new QuestTemplate { QuestId = 7100, Type = (byte)QuestType.Switch, TriggerType = 3, TriggerId = Giver };
        q.Terms.Add(new QuestTerm(600, 15 /*QTT_SWITCH*/, 0));
        store.Quests[7100] = q;

        var ch = new Character { CharId = 1, Name = "Hero", Class = 0, Country = 1, Level = 5, MaxHp = 100, Hp = 100 };
        ch.Invens.Add(new Inven { InvenId = 0xFF });
        var h = new MapTestHarness(store);
        h.Service.InitSwitches();
        var (s, _) = await h.EnterAsync(1, 1, 1, x: 100, z: 100, preSeeded: ch);
        s.Char!.Country = 1; s.Char.Class = 0; s.Char.Level = 5; s.Char.MapId = 0;
        h.Service.AddNpc(new Npc { Id = Giver, Type = 2, Country = 3, MapId = 0 });
        h.Service.InitQuests();

        await h.Service.DispatchClientAsync(s, MapTestHarness.QuestExecReq(7100));

        Assert.True(h.State.FindSwitch(1, 0, 600)!.Opened);       // the quest toggled the switch
    }

    [Fact]
    public async Task QctSwitch_GatesQuestOnSwitchState()
    {
        // A Mission quest that is runnable only while switch 600 is OPEN (QCT_SWITCH, count 1).
        const ushort Giver = 500;
        var store = Store(new[] { Sw(600, start: 0) });
        var q = new QuestTemplate { QuestId = 7200, Type = (byte)QuestType.Mission, TriggerType = 3, TriggerId = Giver };
        q.Conditions.Add(new QuestCondition(600, 20 /*QCT_SWITCH*/, 1 /*expect open*/));
        store.Quests[7200] = q;

        var ch = new Character { CharId = 1, Name = "Hero", Class = 0, Country = 1, Level = 5, MaxHp = 100, Hp = 100 };
        ch.Invens.Add(new Inven { InvenId = 0xFF });
        var h = new MapTestHarness(store);
        h.Service.InitSwitches();
        var (s, c) = await h.EnterAsync(1, 1, 1, x: 100, z: 100, preSeeded: ch);
        s.Char!.Country = 1; s.Char.Class = 0; s.Char.Level = 5; s.Char.MapId = 0;
        h.Service.AddNpc(new Npc { Id = Giver, Type = 2, Country = 3, MapId = 0 });
        h.Service.InitQuests();
        c.Clear();

        await h.Service.DispatchClientAsync(s, MapTestHarness.QuestExecReq(7200));   // switch closed ⇒ condition fails
        Assert.False(c.Has(Msg.CS_QUESTADD_ACK));
        Assert.False(s.Char!.IsRunningQuest(7200));

        await h.Service.DispatchClientAsync(s, SwitchReq(600));                       // open the switch
        await h.Service.DispatchClientAsync(s, MapTestHarness.QuestExecReq(7200));    // now runnable ⇒ accepted
        Assert.True(c.Has(Msg.CS_QUESTADD_ACK));
        Assert.True(s.Char!.IsRunningQuest(7200));
    }
}
