using TMap.Data;
using TMap.Protocol;
using TMap.Server.Map;
using Xunit;

namespace TMap.Server.Tests;

/// <summary>
/// The data-driven AI engine (<c>TAICHART</c>). These drive the interpreter directly: build a
/// script the way the chart loader would, attach it to a monster, fire a trigger, and assert the C++
/// semantics — the state gates, the <c>AN_*</c> conditions, the <c>AT_AICOMPLETE</c> chain, the delay
/// scheduler with its <c>m_dwHostKEY</c> epoch, and the <c>bLoop</c> re-arm.
/// </summary>
public class AiEngineTests
{
    // ---- chart builders (what GameDatabase.LoadTemplatesAsync produces from the three tables) ----

    private static AiCommandTemplate Cmd(uint id, AiCommandKind kind, params AiCondition[] conds)
    {
        var t = new AiCommandTemplate(id, kind);
        t.Conditions.AddRange(conds);
        return t;
    }

    private static AiScript Script(byte aiType, params (AiTrigger Trigger, uint TriggerId, AiCommandTemplate Cmd, uint Delay, bool Loop)[] rows)
    {
        var s = new AiScript(aiType);
        foreach (var r in rows) s.Bind((byte)r.Trigger, r.TriggerId, new AiBinding(r.Cmd, r.Delay, r.Loop));
        return s;
    }

    private static Monster Mob(uint id, float x, float z, AiScript? ai = null) => new()
    {
        Id = id, ChartId = 500, Level = 5, MaxHp = 100, Hp = 100, MaxMp = 50, Mp = 50,
        DefendPower = 100, PosX = x, PosZ = z, StartX = x, StartY = 0, StartZ = z,
        Mode = 0, Status = 1 /* OS_WAKEUP */, Country = 3 /* TCONTRY_N */, Area = 10,
        Region = 7, Channel = 1, MapId = 0, RoamNextMs = 0, Ai = ai,
    };

    // ================================ the Aggressive gate ================================

    [Fact]
    public void IsAggressive_KeysOffEnterLb_NotSetHostUnderEnter()
    {
        // The shipped chart's two scripts BOTH bind SetHost under AT_ENTER, so that test (the earlier
        // assumption, made while the chart was unloaded) marks every monster aggressive. AT_ENTER → SetHost
        // only wakes the monster and gives it a host to roam under; engagement is AT_ENTERLB → ChgHost/ChgMode.
        var live1 = Script(1,                                    // the real script 1 shape (3192 monsters)
            (AiTrigger.Enter, 0, Cmd(3, AiCommandKind.SetHost), 0, false),
            (AiTrigger.AiComplete, 3, Cmd(14, AiCommandKind.Roam), 3000, true),
            (AiTrigger.EnterLb, 0, Cmd(6, AiCommandKind.ChgHost), 0, false));
        var live2 = Script(2,                                    // the real script 2 shape (344 monsters)
            (AiTrigger.Enter, 0, Cmd(3, AiCommandKind.SetHost), 0, false),
            (AiTrigger.AiComplete, 3, Cmd(14, AiCommandKind.Roam), 3000, true));

        Assert.True(live1.IsAggressive);
        Assert.False(live2.IsAggressive);

        // …and the discarded heuristic would not have separated them.
        Assert.True(live1.Binds(AiTrigger.Enter, AiCommandKind.SetHost));
        Assert.True(live2.Binds(AiTrigger.Enter, AiCommandKind.SetHost));
    }

    [Fact]
    public async Task EnterLb_EngagesViaTheChart_TheRealAggroOnSightPath()
    {
        var h = new MapTestHarness();
        var (s, _) = await h.EnterAsync(1, 1, 1, x: 100, z: 100);
        s.Char!.CanHost = true;

        // The live script-1 engagement chain: AT_ENTERLB → ChgHost(cmd6, Mode==NORMAL) → ChgMode(cmd7) → Follow.
        var chgHost = Cmd(6, AiCommandKind.ChgHost, new AiCondition(AiConditionKind.Mode, 0));
        var chgMode = Cmd(7, AiCommandKind.ChgMode, new AiCondition(AiConditionKind.Mode, 0));
        var mob = Mob(0x60101, 100, 100, Script(1,
            (AiTrigger.EnterLb, 0, chgHost, 0, false),
            (AiTrigger.AiComplete, 6, chgMode, 0, false),
            (AiTrigger.AiComplete, 7, Cmd(11, AiCommandKind.Follow), 0, false)));
        // Derived, not hardcoded: ChgHost refuses a same-country host, and the harness player is country 1.
        mob.Country = (byte)(s.Char!.Country + 1);
        h.Service.SpawnMonster(mob);

        await h.Service.DispatchClientAsync(s, MapTestHarness.EnterLbReq(1, 1, 1, mob.Id));

        Assert.Equal(1u, mob.HostId);
        Assert.Equal(1u, mob.TargetId);
        Assert.Equal((byte)1, mob.Mode);       // MT_BATTLE — engaged from the look bound
    }

    [Fact]
    public async Task EnterLb_OnAPassiveScript_DoesNothing()
    {
        var h = new MapTestHarness();
        var (s, _) = await h.EnterAsync(1, 1, 1, x: 100, z: 100);

        // Script 2: no AT_ENTERLB row at all — the monster ignores the report.
        var mob = Mob(0x60102, 100, 100, Script(2,
            (AiTrigger.Enter, 0, Cmd(3, AiCommandKind.SetHost), 0, false)));
        mob.Country = (byte)(s.Char!.Country + 1);
        h.Service.SpawnMonster(mob);

        await h.Service.DispatchClientAsync(s, MapTestHarness.EnterLbReq(1, 1, 1, mob.Id));

        Assert.Equal((byte)0, mob.Mode);
        Assert.Equal(0u, mob.TargetId);
    }

    [Fact]
    public async Task AggroBoundReq_ForAnUnknownMonster_IsASilentNoOp()
    {
        var h = new MapTestHarness();
        var (s, c) = await h.EnterAsync(1, 1, 1, x: 100, z: 100);
        c.Clear();

        await h.Service.DispatchClientAsync(s, MapTestHarness.EnterLbReq(1, 1, 1, 0xDEADBEEF));

        Assert.Empty(c.Sent);   // no reply, no crash — the C++ FindMonster miss
    }

    [Fact]
    public void MissingScript_FallsBackToDefaultAiType()
    {
        var store = new TemplateStore();
        var dflt = Script(TemplateStore.DefaultAiType, (AiTrigger.Enter, 0, Cmd(1, AiCommandKind.Roam), 0, false));
        store.AiScripts[TemplateStore.DefaultAiType] = dflt;

        Assert.Same(dflt, store.AiScriptFor(99));          // unknown type → DEFAULT_AI (C++ FindTMonsterAI)
        Assert.Null(new TemplateStore().AiScriptFor(0));   // nothing loaded at all → null (DB-free)
    }

    // ================================ the event machine ================================

    [Fact]
    public async Task Enter_RunsSetHost_WakesAndAssignsHost_WithoutEnteringBattle()
    {
        var h = new MapTestHarness();
        var (s, c) = await h.EnterAsync(1, 1, 1, x: 100, z: 100);
        s.Char!.CanHost = true; s.Char!.LastMoveMs = 0;

        var mob = Mob(0x60001, 100, 100, Script(1, (AiTrigger.Enter, 0, Cmd(10, AiCommandKind.SetHost), 0, false)));
        h.Service.SpawnMonster(mob);
        c.Clear();

        h.Service.OnAiEvent(mob, AiTrigger.Enter, 0, 1, 1, 1);

        // SetHost only wakes + assigns the host. The target / BATTLE transition belong to the scripted
        // ChgHost / ChgMode successors — the whole point of not folding the chain.
        Assert.Equal(1u, mob.HostId);
        Assert.Equal((byte)1, mob.Status);        // OS_WAKEUP
        Assert.Equal(0u, mob.TargetId);
        Assert.Equal((byte)0, mob.Mode);          // still MT_NORMAL
        Assert.NotNull(c.Last(Msg.CS_MONHOST_ACK));
    }

    [Fact]
    public async Task AiComplete_ChainsSetHost_ToChgHost_ToChgMode()
    {
        var h = new MapTestHarness();
        var (s, _) = await h.EnterAsync(1, 1, 1, x: 100, z: 100);
        s.Char!.CanHost = true; s.Char!.LastMoveMs = 0;

        var setHost = Cmd(10, AiCommandKind.SetHost);
        var chgHost = Cmd(11, AiCommandKind.ChgHost);
        var chgMode = Cmd(12, AiCommandKind.ChgMode);
        // AT_ENTER → SetHost; SetHost completes → ChgHost; ChgHost completes → ChgMode. Each AT_AICOMPLETE
        // row is keyed on the *previous* command's id — the C++ chaining convention.
        var ai = Script(1,
            (AiTrigger.Enter, 0, setHost, 0, false),
            (AiTrigger.AiComplete, 10, chgHost, 0, false),
            (AiTrigger.AiComplete, 11, chgMode, 0, false));

        var mob = Mob(0x60002, 100, 100, ai);
        h.Service.SpawnMonster(mob);

        h.Service.OnAiEvent(mob, AiTrigger.Enter, 0, 1, 1, 1);

        Assert.Equal(1u, mob.HostId);
        Assert.Equal(1u, mob.TargetId);           // ChgHost locked the target
        Assert.Equal((byte)1, mob.Mode);          // ChgMode advanced MT_NORMAL → MT_BATTLE
    }

    [Fact]
    public void ChgMode_WalksTheModeRing_NormalBattleGohomeNormal()
    {
        var h = new MapTestHarness();
        var chgMode = Cmd(20, AiCommandKind.ChgMode);
        var mob = Mob(0x60003, 0, 0, Script(1, (AiTrigger.Defend, 0, chgMode, 0, false)));
        h.Service.SpawnMonster(mob);
        mob.HostId = 7;                            // ChgMode requires HostId == eventHost

        h.Service.OnAiEvent(mob, AiTrigger.Defend, 0, 7);
        Assert.Equal((byte)1, mob.Mode);           // MT_BATTLE
        mob.TargetId = 42;

        h.Service.OnAiEvent(mob, AiTrigger.Defend, 0, 7);
        Assert.Equal((byte)2, mob.Mode);           // MT_GOHOME, target cleared
        Assert.Equal(0u, mob.TargetId);

        h.Service.OnAiEvent(mob, AiTrigger.Defend, 0, 7);
        Assert.Equal((byte)0, mob.Mode);           // back to MT_NORMAL
    }

    [Fact]
    public void UnboundTriggerOrScriptlessMonster_IsASilentNoOp()
    {
        var h = new MapTestHarness();
        var scripted = Mob(0x60004, 0, 0, Script(1, (AiTrigger.Enter, 0, Cmd(1, AiCommandKind.ChgMode), 0, false)));
        var bare = Mob(0x60005, 0, 0);
        h.Service.SpawnMonster(scripted);
        h.Service.SpawnMonster(bare);

        h.Service.OnAiEvent(scripted, AiTrigger.Dead);      // bound trigger, but not this one
        h.Service.OnAiEvent(bare, AiTrigger.Enter);         // no script at all
        h.Service.OnAiEvent(scripted, AiTrigger.Enter, 99); // right trigger, unbound trigger id

        Assert.Equal((byte)0, scripted.Mode);
        Assert.Equal((byte)0, bare.Mode);
    }

    // ================================ conditions ================================

    [Fact]
    public void ProbCondition_GatesTheCommand()
    {
        var h = new MapTestHarness();
        var never = Cmd(30, AiCommandKind.ChgMode, new AiCondition(AiConditionKind.Prob, 0));
        var always = Cmd(31, AiCommandKind.ChgMode, new AiCondition(AiConditionKind.Prob, 100));

        var a = Mob(0x60006, 0, 0, Script(1, (AiTrigger.Defend, 0, never, 0, false)));
        var b = Mob(0x60007, 0, 0, Script(2, (AiTrigger.Defend, 0, always, 0, false)));
        h.Service.SpawnMonster(a); h.Service.SpawnMonster(b);
        a.HostId = b.HostId = 7;

        h.Service.OnAiEvent(a, AiTrigger.Defend, 0, 7);
        h.Service.OnAiEvent(b, AiTrigger.Defend, 0, 7);

        Assert.Equal((byte)0, a.Mode);   // rand()%100 < 0 is never true
        Assert.Equal((byte)1, b.Mode);   // rand()%100 < 100 is always true
    }

    [Fact]
    public void ModeCondition_MatchesTheMonstersCurrentMode()
    {
        var h = new MapTestHarness();
        var onlyInBattle = Cmd(32, AiCommandKind.Refill, new AiCondition(AiConditionKind.Mode, 1 /* MT_BATTLE */));
        var mob = Mob(0x60008, 0, 0, Script(1, (AiTrigger.Help, 0, onlyInBattle, 0, false)));
        h.Service.SpawnMonster(mob);
        mob.HostId = 7;
        mob.Hp = 10;

        h.Service.OnAiEvent(mob, AiTrigger.Help, 0, 7);
        Assert.Equal(10u, mob.Hp);       // MT_NORMAL ≠ 1 ⇒ the condition rejects, no refill

        mob.Mode = 1;
        h.Service.OnAiEvent(mob, AiTrigger.Help, 0, 7);
        Assert.Equal(mob.MaxHp, mob.Hp); // now it runs
    }

    [Fact]
    public void ChgHostCondition_AlwaysPasses_ReproducingTheOriginalMissingReturn()
    {
        // C++ TAICommand.cpp:60 evaluates the AN_CHGHOST arm but never returns it, so control falls through to
        // `return TRUE`. This asserts the port keeps that behaviour: even when the "should reject" shape holds
        // (the event's target IS the current target), the command still runs.
        var h = new MapTestHarness();
        var cmd = Cmd(33, AiCommandKind.ChgMode, new AiCondition(AiConditionKind.ChgHost, 0));
        var mob = Mob(0x60009, 0, 0, Script(1, (AiTrigger.Defend, 0, cmd, 0, false)));
        h.Service.SpawnMonster(mob);
        mob.HostId = 7;
        mob.TargetId = 55; mob.TargetType = 1;

        h.Service.OnAiEvent(mob, AiTrigger.Defend, 0, 7, 55, 1);   // rhId/rhType == the current target

        Assert.Equal((byte)1, mob.Mode);   // ran anyway — the condition is inert, exactly as shipped
    }

    // ================================ the delay scheduler ================================

    [Fact]
    public void CommandsWithAHardZeroGetDelay_IgnoreTheChartDelay()
    {
        // CTAICmdChgMode/SetHost/ChgHost/BeginAtk/Follow/Gohome/Refill/Remove all override GetDelay to 0, so a
        // dwDelay in the chart row is inert for them — they always run inline within their chain.
        var h = new MapTestHarness();
        var mob = Mob(0x6000A, 0, 0, Script(1, (AiTrigger.Defend, 0, Cmd(40, AiCommandKind.ChgMode), 500, false)));
        h.Service.SpawnMonster(mob);
        mob.HostId = 7;

        h.Service.OnAiEvent(mob, AiTrigger.Defend, 0, 7);

        Assert.Equal((byte)1, mob.Mode);           // ran immediately despite the 500 ms row
        Assert.Equal(0, h.Service.PendingAiCount);
    }

    [Fact]
    public void DelayedCommand_WaitsForItsDueTime()
    {
        // The base CTAICommand::GetDelay returns the row's dwDelay, so a command with no override is the one
        // that actually schedules. Its ChgMode successor makes the firing observable.
        var h = new MapTestHarness();
        var delayed = Cmd(40, (AiCommandKind)200);   // no override ⇒ base GetDelay ⇒ the row's 500 ms
        var mob = Mob(0x6000A, 0, 0, Script(1,
            (AiTrigger.Defend, 0, delayed, 500, false),
            (AiTrigger.AiComplete, 40, Cmd(41, AiCommandKind.ChgMode), 0, false)));
        h.Service.SpawnMonster(mob);
        mob.HostId = 7;

        h.Service.OnAiEvent(mob, AiTrigger.Defend, 0, 7);
        Assert.Equal((byte)0, mob.Mode);           // queued, not run
        Assert.Equal(1, h.Service.PendingAiCount);

        h.Service.RunScheduledAi(499);
        Assert.Equal((byte)0, mob.Mode);           // not due yet
        Assert.Equal(1, h.Service.PendingAiCount);

        h.Service.RunScheduledAi(500);
        Assert.Equal((byte)1, mob.Mode);           // fired, and its successor ran
        Assert.Equal(0, h.Service.PendingAiCount);
    }

    [Fact]
    public void StaleEpoch_DiscardsAScheduledCommand()
    {
        var h = new MapTestHarness();
        var delayed = Cmd(42, (AiCommandKind)200);
        var mob = Mob(0x6000B, 0, 0, Script(1,
            (AiTrigger.Defend, 0, delayed, 500, false),
            (AiTrigger.AiComplete, 42, Cmd(43, AiCommandKind.ChgMode), 0, false)));
        h.Service.SpawnMonster(mob);
        mob.HostId = 7;

        h.Service.OnAiEvent(mob, AiTrigger.Defend, 0, 7);
        mob.HostKey++;                             // something retargeted / re-moded the monster meanwhile

        h.Service.RunScheduledAi(500);

        Assert.Equal((byte)0, mob.Mode);           // dropped — the C++ m_dwHostKEY guard in OnSM_AICMD_ACK
        Assert.Equal(0, h.Service.PendingAiCount);
    }

    [Fact]
    public async Task LoopingCommand_ReArmsItself()
    {
        var h = new MapTestHarness();
        await h.EnterAsync(1, 1, 1, x: 100, z: 100);   // a viewer, so Roam has a host and does not fire AT_LEAVE

        var mob = Mob(0x6000C, 100, 100, Script(1, (AiTrigger.Delete, 0, Cmd(42, AiCommandKind.Roam), 100, true)));
        h.Service.SpawnMonster(mob);

        h.Service.OnAiEvent(mob, AiTrigger.Delete);
        Assert.Equal(1, h.Service.PendingAiCount);

        h.Service.RunScheduledAi(10_000);            // fire it — a loop command re-queues itself
        Assert.Equal(1, h.Service.PendingAiCount);
    }

    [Fact]
    public async Task NonLoopingCommand_DoesNotReArm()
    {
        var h = new MapTestHarness();
        await h.EnterAsync(1, 1, 1, x: 100, z: 100);

        var mob = Mob(0x6000D, 100, 100, Script(1, (AiTrigger.Delete, 0, Cmd(43, AiCommandKind.Roam), 100, false)));
        h.Service.SpawnMonster(mob);

        h.Service.OnAiEvent(mob, AiTrigger.Delete);
        h.Service.RunScheduledAi(10_000);

        Assert.Equal(0, h.Service.PendingAiCount);
    }

    // ================================ integration with the legacy sweep ================================

    [Fact]
    public async Task ScriptedMonster_IsSkippedByTheLegacySweep()
    {
        var h = new MapTestHarness();
        var (_, c) = await h.EnterAsync(1, 1, 1, x: 100, z: 100);

        // A script that binds nothing at all: the monster must go completely quiet, proving the sweep skipped
        // it rather than falling back to the hard-coded roam.
        var mob = Mob(0x6000E, 100, 100, new AiScript(9));
        h.Service.SpawnMonster(mob);
        c.Clear();

        h.Service.RunMonsterAI(10_000);

        Assert.Null(c.Last(Msg.CS_MONACTION_ACK));
    }

    [Fact]
    public async Task ScriptlessMonster_StillRoams_NoRegression()
    {
        var h = new MapTestHarness();
        var (_, c) = await h.EnterAsync(1, 1, 1, x: 100, z: 100);
        var mob = Mob(0x6000F, 100, 100);          // no script → the sweep
        h.Service.SpawnMonster(mob);
        c.Clear();

        h.Service.RunMonsterAI(10_000);

        Assert.NotNull(c.Last(Msg.CS_MONACTION_ACK));
    }

    [Fact]
    public void UnknownOpcode_StillChains_LikeTheCppDefaultCommand()
    {
        // C++ CreateCMD's `default:` builds a plain CTAICommand — an empty body that still fires
        // AT_AICOMPLETE, so a chart referencing an opcode this build doesn't know keeps its chain intact.
        var h = new MapTestHarness();
        var unknown = Cmd(50, (AiCommandKind)200);
        var chgMode = Cmd(51, AiCommandKind.ChgMode);
        var mob = Mob(0x60010, 0, 0, Script(1,
            (AiTrigger.Defend, 0, unknown, 0, false),
            (AiTrigger.AiComplete, 50, chgMode, 0, false)));
        h.Service.SpawnMonster(mob);
        mob.HostId = 7;

        h.Service.OnAiEvent(mob, AiTrigger.Defend, 0, 7);

        Assert.Equal((byte)1, mob.Mode);   // the successor ran
    }
}
