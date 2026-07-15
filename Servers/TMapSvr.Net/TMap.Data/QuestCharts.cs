namespace TMap.Data;

/// <summary>One objective of a quest (C++ <c>tagQUESTTERM</c>): a <see cref="TermType"/> (QTT_*), the id it
/// keys on (an item id, monster chart id, npc id, prereq quest id, position…), and the <see cref="Count"/>
/// needed. Immutable — the per-character running progress is tracked separately in <c>RunningTerm</c>.</summary>
public sealed record QuestTerm(uint TermId, byte TermType, byte Count);

/// <summary>One payoff of a quest (C++ <c>tagQUESTREWARD</c>): <see cref="RewardType"/> (RT_*), the id/amount
/// (<see cref="RewardId"/>), the grant method (<see cref="TakeMethod"/> RM_*) and its probability/weight
/// (<see cref="TakeData"/>), and the stack <see cref="Count"/>.</summary>
public sealed record QuestReward(uint RewardId, byte RewardType, byte TakeMethod, byte TakeData, byte Count);

/// <summary>One eligibility gate of a quest (C++ <c>tagQUESTCONDITION</c>): a <see cref="ConditionType"/>
/// (QCT_*), its id/value, and a count.</summary>
public sealed record QuestCondition(uint ConditionId, byte ConditionType, byte Count);

/// <summary>
/// A quest template (C++ <c>tagQUESTTEMP</c>, keyed by <see cref="QuestId"/>): the type (QT_*), the trigger
/// that offers/advances it (<see cref="TriggerType"/> TT_* + <see cref="TriggerId"/>), the parent quest
/// (chains), the repeat cap (<see cref="CountMax"/>), the AND/OR condition mode (<see cref="ConditionCheck"/>),
/// plus its conditions / terms / rewards. Child quests are found by the trigger index under
/// <c>TT_EXECQUEST</c> keyed by the parent's id (that's how <c>CQuest::ExecQuest</c> recurses).
/// </summary>
public sealed class QuestTemplate
{
    public uint QuestId { get; init; }
    public byte Type { get; init; }             // QUEST_TYPE (QT_*)
    public byte TriggerType { get; init; }      // TRIGGER_TYPE (TT_*)
    public uint TriggerId { get; init; }
    public uint ParentId { get; init; }
    public byte CountMax { get; init; }
    public byte ConditionCheck { get; init; }   // 0 = ALL conditions must pass, 1 = ANY passing condition suffices
    public byte ForceRun { get; init; }

    public List<QuestCondition> Conditions { get; } = new();
    public List<QuestReward> Rewards { get; } = new();
    public List<QuestTerm> Terms { get; } = new();
}
