namespace TMap.Data;

/// <summary>
/// C++ <c>AI_TRIGGER</c> (TMapType.h:287) — the event that fires a monster's AI script. Used as the index
/// into <see cref="AiScript.ByTrigger"/> (the C++ <c>m_mapVCOMMAND[AT_COUNT]</c> array), so the numeric
/// values are wire/DB-significant: they are the <c>TAICHART.bTriggerType</c> column.
/// </summary>
public enum AiTrigger : byte
{
    /// <summary>A command finished; the trigger id is the completed command's id. This is how the C++
    /// scripts chain (base <c>CTAICommand::ExecAI</c> fires it unconditionally).</summary>
    AiComplete = 0,
    EnterLb,        // a player entered the "look" bound
    LeaveLb,        // …left it
    EnterAb,        // a player entered the "attack" bound
    LeaveAb,        // …left it
    Defend,         // took/observed an attack — the retarget event
    Delete,         // corpse removal scheduled
    Enter,          // a player came into view (the aggro-on-sight hook)
    Leave,          // the last viewer left
    Dead,
    Timeout,
    AtHome,         // arrived back at the spawn anchor
    Help,           // call-for-help / low HP
    Lottery,        // a corpse item was rolled for
    Count,
}

/// <summary>
/// C++ <c>AI_COMMAND</c> (TMapType.h:306) — the command opcodes, i.e. the <c>TAICMDCHART.bCmdType</c>
/// column and the switch in <c>CTAICommand::CreateCMD</c>.
/// </summary>
public enum AiCommandKind : byte
{
    Regen = 0,
    Leave,
    SetHost,
    ChkHost,
    ChgHost,
    ChgMode,
    BeginAtk,
    Attack,
    Follow,
    Getaway,
    Refill,
    Roam,
    Remove,
    Gohome,
    Lottery,
    Count,
}

/// <summary>
/// C++ <c>AI_CONDITION</c> (TMapType.h:326) — the guard kinds evaluated by
/// <c>CTAICommand::CheckCondition</c>; the <c>TAICONCHART.bConditionType</c> column.
/// </summary>
public enum AiConditionKind : byte
{
    /// <summary>Roll <c>rand()%100 &lt; Value</c>.</summary>
    Prob = 0,
    /// <summary>The monster's <c>m_bMode</c> must equal <c>Value</c> (a <c>TMODE_TYPE</c>).</summary>
    Mode,
    /// <summary>"the real host differs from the current target". <b>Inert in the original</b> — see
    /// <see cref="AiCondition"/>.</summary>
    ChgHost,
    Count,
}

/// <summary>
/// One guard on a command (C++ <c>tagTAICONDITION</c>: <c>m_bType</c> + <c>m_dwID</c>), from
/// <c>TAICONCHART</c>. All of a command's conditions must pass for it to run
/// (<c>CTAICommand::CanRun</c> is an AND-fold).
///
/// <para><b>Ported bug, deliberately preserved.</b> The C++ <c>AN_CHGHOST</c> arm
/// (TAICommand.cpp:60) computes its result but has no <c>return</c>:
/// <code>case AN_CHGHOST : pMON-&gt;m_dwTargetID == dwRHId &amp;&amp; pMON-&gt;m_bTargetType == bRHType ? FALSE : TRUE;</code>
/// so control falls out of the switch to the trailing <c>return TRUE</c> and the condition
/// <b>always passes</b>. Live monster behaviour depends on that, so
/// <c>AiCommand.CheckCondition</c> reproduces it rather than "fixing" it.</para>
/// </summary>
public readonly record struct AiCondition(AiConditionKind Kind, uint Value);

/// <summary>
/// A command template from <c>TAICMDCHART</c> (C++ <c>tagTAICOMMAND</c>, held in <c>m_mapTCMDTEMP</c>):
/// an opcode plus the guards loaded from <c>TAICONCHART</c>. Shared by every binding that references the
/// command id — the per-binding delay/loop live on <see cref="AiBinding"/>, exactly as in C++ where
/// <c>m_pCOMMAND</c> is shared and <c>m_dwDelay</c>/<c>m_bLoop</c> sit on the <c>CTAICommand</c> instance.
/// </summary>
public sealed class AiCommandTemplate(uint id, AiCommandKind kind)
{
    public uint Id { get; } = id;
    public AiCommandKind Kind { get; } = kind;
    public List<AiCondition> Conditions { get; } = new();
}

/// <summary>
/// One <c>TAICHART</c> row: under some (script, trigger, trigger-id), run this command after
/// <paramref name="Delay"/> ms, and if <paramref name="Loop"/> re-arm it when it reports work done.
/// The C++ equivalent is a <c>CTAICommand</c> instance in the trigger's vector.
/// </summary>
public sealed record AiBinding(AiCommandTemplate Command, uint Delay, bool Loop);

/// <summary>
/// One AI script (C++ <c>CTMonsterAI</c>, keyed by <c>bAIType</c> in <c>m_mapTMONAI</c>): for each of the
/// 14 triggers, a map from trigger-id to the ordered list of commands to run.
/// <c>TMONSTERCHART.bAIType</c> selects it.
/// </summary>
public sealed class AiScript(byte aiType)
{
    public byte AiType { get; } = aiType;

    /// <summary>C++ <c>m_mapVCOMMAND[AT_COUNT]</c> — indexed by <see cref="AiTrigger"/>, then by trigger id.
    /// Insertion order within a list is the DB row order, which is the execution order.</summary>
    public Dictionary<uint, List<AiBinding>>[] ByTrigger { get; } =
        CreateTriggerTable();

    private static Dictionary<uint, List<AiBinding>>[] CreateTriggerTable()
    {
        var t = new Dictionary<uint, List<AiBinding>>[(int)AiTrigger.Count];
        for (var i = 0; i < t.Length; i++) t[i] = new Dictionary<uint, List<AiBinding>>();
        return t;
    }

    /// <summary>The commands bound to (trigger, triggerId), or null when the script says nothing about it —
    /// the C++ <c>m_mapVCOMMAND[nEvent].find(dwTriggerID)</c> miss, which is a silent no-op.</summary>
    public List<AiBinding>? Lookup(AiTrigger trigger, uint triggerId) =>
        (uint)trigger < (uint)ByTrigger.Length && ByTrigger[(int)trigger].TryGetValue(triggerId, out var v)
            ? v
            : null;

    /// <summary>Add a binding, creating the trigger-id bucket on first use (C++ load loop,
    /// TMapSvr.cpp:2354-2372). Rows with an out-of-range trigger type are dropped rather than crashing —
    /// the C++ would index past <c>m_mapVCOMMAND</c>.</summary>
    public bool Bind(byte triggerType, uint triggerId, AiBinding binding)
    {
        if (triggerType >= (byte)AiTrigger.Count) return false;
        var bucket = ByTrigger[triggerType];
        if (!bucket.TryGetValue(triggerId, out var list)) bucket[triggerId] = list = new List<AiBinding>();
        list.Add(binding);
        return true;
    }

    /// <summary>
    /// Whether this script binds <paramref name="kind"/> under <paramref name="trigger"/> at all (any
    /// trigger id). See <see cref="IsAggressive"/> for the aggro-on-sight test built on it.
    /// </summary>
    public bool Binds(AiTrigger trigger, AiCommandKind kind)
    {
        if ((uint)trigger >= (uint)ByTrigger.Length) return false;
        foreach (var (_, list) in ByTrigger[(int)trigger])
            foreach (var b in list)
                if (b.Command.Kind == kind) return true;
        return false;
    }

    /// <summary>
    /// Whether a monster running this script <b>engages a player on sight</b>, rather than only fighting back
    /// once hit.
    ///
    /// <para><b>This is not "binds <c>AC_SETHOST</c> under <c>AT_ENTER</c>"</b>, which is what the port
    /// assumed while the chart was unloaded. Reading the shipped <c>TAICHART</c> settles it: both live scripts
    /// bind exactly that, so the old test marks <b>every</b> monster aggressive. What <c>AT_ENTER</c> →
    /// <c>SetHost</c> actually does is <i>wake</i> the monster and give it a host to drive its roam
    /// broadcasts — its chain runs <c>SetHost</c> → <c>Roam</c>(loop) → <c>SetHost</c> and never reaches
    /// <c>ChgMode</c>, so it cannot enter battle.</para>
    ///
    /// <para>Engagement comes from the <b>client-reported look bound</b>: <c>AT_ENTERLB</c> →
    /// <c>ChgHost</c> → <c>ChgMode</c> (→ <c>MT_BATTLE</c>) → <c>Follow</c>. That is the real discriminator,
    /// and it splits the live data the way an MMO should — script 1 (3192 monsters) binds it and is
    /// aggressive; script 2 (344 monsters) has no <c>AT_ENTERLB</c> row at all and is passive.</para>
    /// </summary>
    public bool IsAggressive =>
        Binds(AiTrigger.EnterLb, AiCommandKind.ChgHost) || Binds(AiTrigger.EnterLb, AiCommandKind.ChgMode);

    /// <summary>Total bindings across every trigger — for the load-summary log.</summary>
    public int BindingCount
    {
        get
        {
            var n = 0;
            foreach (var bucket in ByTrigger)
                foreach (var (_, list) in bucket) n += list.Count;
            return n;
        }
    }
}
