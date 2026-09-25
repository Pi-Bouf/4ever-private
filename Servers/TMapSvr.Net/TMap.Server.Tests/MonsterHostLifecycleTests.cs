using TMap.Data;
using TMap.Protocol;
using TMap.Server.Map;
using Xunit;

namespace TMap.Server.Tests;

/// <summary>
/// Who drives a monster, and when that changes. Found with the real client: a respawned monster spun in place,
/// and a monster the player had walked away from set off in a straight line until it left the map and crashed
/// the client (CTachyonHUGEMAP::GetRegionID, from CalcMONSTER). Two faults combined:
/// <list type="bullet">
/// <item><b>Direction 0 is not "none".</b> <c>TKDIR_N</c> is 4; 0 is <c>TKDIR_LF</c>. The port defaulted both
/// direction bytes to 0, so every fresh monster was announced as walking left-forward.</item>
/// <item><b>Missing host events.</b> The C++ fires <c>AT_ENTER</c> when a monster enters the map and when a
/// player comes into view, and <c>LeaveAggro</c> + <c>AT_LEAVE</c> when a player leaves it. The port fired
/// only the login <c>AT_ENTER</c>, so a respawn never got a host and a monster kept a host that could no
/// longer see it.</item>
/// </list>
/// </summary>
public class MonsterHostLifecycleTests
{
    private const byte OtPc = 1;

    private static AiScript Script(params (AiTrigger trigger, AiCommandKind kind)[] rows)
    {
        var s = new AiScript(1);
        uint id = 100;
        foreach (var (trigger, kind) in rows)
            s.Bind((byte)trigger, 0, new AiBinding(new AiCommandTemplate(id++, kind), 0, false));
        return s;
    }

    /// <summary>Script 1's host rows: AT_ENTER → SetHost, AT_LEAVE → ChkHost.</summary>
    private static AiScript HostScript() => Script((AiTrigger.Enter, AiCommandKind.SetHost), (AiTrigger.Leave, AiCommandKind.ChkHost));

    private static Monster Mob(AiScript? ai = null, uint id = 0x50001) => new()
    {
        Id = id, ChartId = 500, Level = 5, MaxHp = 100, Hp = 100, PosX = 100, PosZ = 100, StartX = 100, StartZ = 100,
        Channel = 1, MapId = 0, Region = 7, Country = 3, Ai = ai,
    };

    private static byte[] Run(float x, float z) => MapTestHarness.MoveReq(0, x, 0, z, dir: 0, speed: 1.0f, action: 4);

    private static byte MonHostFlag(byte[] p) { var r = new PacketReader(p); r.ReadUInt32(); return r.ReadByte(); }

    // ================= direction =================

    [Fact]
    public async Task AFreshMonster_IsAnnouncedWithNoDirectionKey()
    {
        var h = new MapTestHarness();
        var (_, c) = await h.EnterAsync(1, 1, 1, x: 100, z: 100);
        c.Clear();

        h.Service.SpawnMonster(Mob());

        // CS_ADDMON_ACK: id(4) chart(2) level(1) maxHp hp maxMp mp(16) pos(12) pitch(2) dir(2) · mouseDir keyDir
        var r = new PacketReader(c.Last(Msg.CS_ADDMON_ACK)!);
        r.ReadUInt32(); r.ReadUInt16(); r.ReadByte();
        r.ReadUInt32(); r.ReadUInt32(); r.ReadUInt32(); r.ReadUInt32();
        r.ReadFloat(); r.ReadFloat(); r.ReadFloat(); r.ReadUInt16(); r.ReadUInt16();
        Assert.Equal(Monster.TkdirN, r.ReadByte());
        Assert.Equal(Monster.TkdirN, r.ReadByte());
    }

    [Fact]
    public void ANewPlayer_HoldsNoDirectionKey()
    {
        var ch = new Character();
        Assert.Equal(Monster.TkdirN, ch.MouseDir);
        Assert.Equal(Monster.TkdirN, ch.KeyDir);
    }

    // ================= AT_ENTER =================

    [Fact]
    public async Task ARespawn_FindsAHost_AmongThePlayersAlreadyThere()
    {
        var h = new MapTestHarness();
        var (s, c) = await h.EnterAsync(1, 1, 1, x: 100, z: 100);
        s.Char!.CanHost = true;
        c.Clear();

        var mon = Mob(HostScript());
        h.Service.SpawnMonster(mon);

        Assert.Equal(1u, mon.HostId);
        Assert.Equal(1, MonHostFlag(c.Last(Msg.CS_MONHOST_ACK)!));
    }

    [Fact]
    public async Task ComingBackIntoView_HostsTheMonsterAgain()
    {
        var h = new MapTestHarness();
        var (s, _) = await h.EnterAsync(1, 1, 1, x: 100, z: 100);
        s.Char!.CanHost = true;
        var mon = Mob(HostScript());
        h.Service.SpawnMonster(mon);

        await h.Service.DispatchClientAsync(s, Run(400, 100));   // out of its 3×3
        Assert.Equal(0u, mon.HostId);

        await h.Service.DispatchClientAsync(s, Run(100, 100));   // and back
        Assert.Equal(1u, mon.HostId);
    }

    [Fact]
    public async Task TheFirstRealMove_WakesTheMonstersAround()
    {
        var h = new MapTestHarness();
        var (s, _) = await h.EnterAsync(1, 1, 1, x: 100, z: 100);
        var mon = Mob();
        h.Service.SpawnMonster(mon);
        mon.Ai = HostScript();                 // armed after its own spawn AT_ENTER
        s.Char!.CanHost = false;
        mon.HostId = 0;

        await h.Service.DispatchClientAsync(s, MapTestHarness.MoveReq(0, 101, 0, 100, dir: 0, speed: 1.0f));  // a stand
        Assert.Equal(0u, mon.HostId);

        await h.Service.DispatchClientAsync(s, Run(102, 100));
        Assert.True(s.Char!.CanHost);
        Assert.Equal(1u, mon.HostId);
    }

    // ================= AT_LEAVE =================

    [Fact]
    public async Task WalkingAway_ReleasesTheMonster_AndTellsTheOldHost()
    {
        var h = new MapTestHarness();
        var (s, c) = await h.EnterAsync(1, 1, 1, x: 100, z: 100);
        s.Char!.CanHost = true;
        var mon = Mob(HostScript());
        h.Service.SpawnMonster(mon);
        c.Clear();

        await h.Service.DispatchClientAsync(s, Run(400, 100));

        Assert.Equal(0u, mon.HostId);
        Assert.Equal(0, MonHostFlag(c.Last(Msg.CS_MONHOST_ACK)!));   // even though it is out of view now
    }

    [Fact]
    public async Task WalkingAway_HandsTheMonster_ToAPlayerStillThere()
    {
        var h = new MapTestHarness();
        var (s1, _) = await h.EnterAsync(1, 1, 1, x: 100, z: 100);
        var (s2, _) = await h.EnterAsync(2, 2, 2, name: "Other", x: 110, z: 100);
        s1.Char!.CanHost = true; s2.Char!.CanHost = true;
        var mon = Mob(HostScript());
        h.Service.SpawnMonster(mon);
        mon.HostId = 1;

        await h.Service.DispatchClientAsync(s1, Run(400, 100));

        Assert.Equal(2u, mon.HostId);
    }

    [Fact]
    public async Task TheHostLoggingOut_HandsTheMonsterOn()
    {
        var h = new MapTestHarness();
        var (s1, _) = await h.EnterAsync(1, 1, 1, x: 100, z: 100);
        var (s2, _) = await h.EnterAsync(2, 2, 2, name: "Other", x: 110, z: 100);
        s1.Char!.CanHost = true; s2.Char!.CanHost = true;
        var mon = Mob(HostScript());
        h.Service.SpawnMonster(mon);
        mon.HostId = 1;

        h.Service.OnClientDisconnect(s1);

        Assert.Equal(2u, mon.HostId);
    }

    [Fact]
    public async Task AMonsterGoingHome_IgnoresAPlayerLeaving()
    {
        // C++ LeaveAggro: in MT_GOHOME only the hate entry is erased — no AT_LEAVELB, so the walk home is kept.
        var h = new MapTestHarness();
        var (s, _) = await h.EnterAsync(1, 1, 1, x: 100, z: 100);
        var mon = Mob();
        h.Service.SpawnMonster(mon);
        mon.Ai = Script((AiTrigger.LeaveLb, AiCommandKind.ChgMode));
        mon.Mode = 2; mon.HostId = 1;          // MT_GOHOME, target already cleared

        await h.Service.DispatchClientAsync(s, Run(400, 100));

        Assert.Equal(2, mon.Mode);
    }

    // ================= ResetHost =================

    private static TemplateStore OneSpawn()
    {
        var t = new TemplateStore();
        t.MonsterTemplates[500] = new MonsterTemplate(500, 5, 900);
        t.MonAttrs[TemplateStore.MonAttrKey(900, 5)] = new MonAttrRow(900, 5, 200, 50, 0);
        t.MonsterSpawns.Add(new MonsterSpawnDef(
            new MonSpawnRow(Id: 7, MapId: 0, PosX: 100, PosY: 0, PosZ: 100, Dir: 0, Country: 0,
                Count: 1, Range: 0, Prob: 100, Region: 7, Delay: 0, Event: 0, Area: 3),
            new List<MapMonRow> { new(SpawnId: 7, MonId: 500, Leader: 0, Essential: 0, Prob: 100) }));
        return t;
    }

    [Fact]
    public async Task AnAbandonedMonster_FarFromHome_IsSentBackToItsSpawnPoint_OnTheNextTick()
    {
        var h = new MapTestHarness(OneSpawn());
        var (s, _) = await h.EnterAsync(1, 1, 1, x: 140, z: 100);
        h.Service.InitMonsterSpawns();
        h.Service.RunMonsterRegen(0);
        var mon = Assert.Single(h.State.AllMonsters());
        mon.Ai = HostScript();
        mon.PosX = 140;                        // dragged 40 from home — more than half a cell
        mon.HostId = 1;

        h.Service.OnClientDisconnect(s);       // nobody left to host it ⇒ ResetHost
        Assert.Equal(0u, mon.HostId);
        Assert.Equal(140f, mon.PosX);          // the C++ round-trips SM_RESETHOST: not in the same chain

        await h.Service.OnTimerAsync();

        Assert.Equal(100f, mon.PosX);
        Assert.Equal(100f, mon.PosZ);
        Assert.Equal(0, mon.Action);           // TA_STAND
    }

    // ================= a respawn reuses its slot's id =================

    [Fact]
    public async Task ACommandLeftFromTheCorpse_DoesNotRemoveTheRespawn()
    {
        // Seen live: kill, respawn, pull — and the respawn vanished for good. The corpse's AT_DEAD → Leave was
        // still queued; the respawn has the same id, so the old Leave ran and despawned it.
        var h = new MapTestHarness();
        var (_, c) = await h.EnterAsync(1, 1, 1, x: 100, z: 100);
        var corpse = Mob();
        h.Service.SpawnMonster(corpse);
        corpse.Ai = new AiScript(1);
        corpse.Ai.Bind((byte)AiTrigger.Dead, 0, new AiBinding(new AiCommandTemplate(1, AiCommandKind.Leave), 1000, false));
        corpse.Status = 3; corpse.Dead = true; corpse.Hp = 0;   // OS_DEAD
        h.Service.OnAiEvent(corpse, AiTrigger.Dead);            // Leave queued for +1000 ms

        h.Service.DespawnMonster(corpse);                       // removed by another path first
        var respawn = Mob();
        h.Service.SpawnMonster(respawn);
        c.Clear();

        h.Service.RunScheduledAi(10_000);

        Assert.Same(respawn, h.State.FindMonster(respawn.Id));
        Assert.False(c.Has(Msg.CS_DELMON_ACK));
    }

    [Fact]
    public void RemovingAStaleObject_KeepsTheMonsterNowHoldingItsId()
    {
        var h = new MapTestHarness();
        var old = Mob();
        h.State.AddMonster(old);
        var respawn = Mob();
        h.State.AddMonster(respawn);            // same id

        h.State.RemoveMonster(old);

        Assert.Same(respawn, h.State.FindMonster(respawn.Id));
    }

    // ================= ChgMode =================

    [Fact]
    public async Task AModeChange_IsAnnouncedToViewers()
    {
        var h = new MapTestHarness();
        var (_, c) = await h.EnterAsync(1, 1, 1, x: 100, z: 100);
        var mon = Mob();
        h.Service.SpawnMonster(mon);
        mon.Ai = Script((AiTrigger.Defend, AiCommandKind.ChgMode));
        mon.HostId = 1;
        c.Clear();

        h.Service.OnAiEvent(mon, AiTrigger.Defend, 0, 1, 1, OtPc);

        // CS_CHGMODE_ACK: dwID · bType · bMode — the client clears its follow state on it.
        var r = new PacketReader(c.Last(Msg.CS_CHGMODE_ACK)!);
        Assert.Equal(mon.Id, r.ReadUInt32());
        Assert.Equal(Monster.OtMon, r.ReadByte());
        Assert.Equal(1, r.ReadByte());         // MT_BATTLE
    }
}
