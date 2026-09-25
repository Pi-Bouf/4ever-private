using TMap.Data;
using TMap.Protocol;
using TMap.Server.Map;
using Xunit;

namespace TMap.Server.Tests;

/// <summary>
/// Regression for the first real-client combat crash: the map announced monster attacks with wSkillID 0. The
/// client resolves that id through CTChart::FindTSKILLTEMP and dereferences the result <b>before</b> null-checking
/// it — in OnCS_MONATTACK_ACK (a null read at +0x94, taken from the crash dump) and again in OnCS_DEFEND_ACK — so
/// an id missing from the skill chart kills the client on the monster's first counterattack. Every monster swing
/// must therefore carry a skill that exists, and a monster without one must not swing at all (as in the C++,
/// where BeginAtk/Attack require m_pNextSkill).
/// </summary>
public class MonsterSkillIdTests
{
    private static Character Victim() => new() { CharId = 1, Name = "Victim", MaxHp = 100, Hp = 100, MaxMp = 50, Mp = 50 };

    private static Monster Attacker(ushort chartId = 500)
        => new() { Id = 0x50001, ChartId = chartId, Level = 5, MaxHp = 100, Hp = 100, MaxMp = 50, Mp = 50,
            DefendPower = 10, PosX = 100, PosZ = 100, StartX = 100, StartY = 0, StartZ = 100,
            Mode = 1, TargetId = 1, AtkMin = 20, AtkMax = 20, AtkSpeed = 2000, AtkNextMs = 0,
            AttackLevel = 10, CritProb = 0, Region = 7, Channel = 1, MapId = 0 };

    private static SkillTemplate Skill(ushort id) => new(id, Kind: 0, UseMp: 0, UseMpType: 0, UseHp: 0, UseHpType: 0,
        StartLevel: 1, MaxLevel: 1, NextLevel: 0, ReuseDelay: 0, ReuseDelayInc: 0, LoopDelay: 0, KindDelay: 0,
        SpeedApply: 0, Positive: 0, MapId: 0xFFFF);

    private static async Task<(MapTestHarness h, FakeClientChannel c, Character ch)> Setup(TemplateStore store)
    {
        var h = new MapTestHarness(store);
        var ch = Victim();
        var (_, c) = await h.EnterAsync(1, 1, 1, x: 120, z: 100, preSeeded: ch);
        h.Service.SpawnMonster(Attacker());
        h.Service.CombatRng = new Random(1);
        c.Clear();
        return (h, c, ch);
    }

    // CS_MONATTACK_ACK: dwAttackID(4) dwTargetID(4) bAttackType(1) bTargetType(1) wSkillID(2).
    private static ushort MonAttackSkill(byte[] p) { var r = new PacketReader(p); r.ReadUInt32(); r.ReadUInt32(); r.ReadByte(); r.ReadByte(); return r.ReadUInt16(); }

    // CS_DEFEND_ACK: walk to wSkillID (same layout CombatCoreTests.DamageMap walks).
    private static ushort DefendSkill(byte[] p)
    {
        var r = new PacketReader(p);
        r.ReadUInt32(); r.ReadUInt32(); r.ReadByte(); r.ReadByte(); r.ReadUInt32(); r.ReadByte();
        r.ReadUInt32(); r.ReadUInt32(); r.ReadByte(); r.ReadUInt32(); r.ReadByte(); r.ReadByte();
        r.ReadUInt16(); r.ReadByte(); r.ReadUInt32(); r.ReadUInt32(); r.ReadUInt32(); r.ReadUInt32();
        r.ReadByte(); r.ReadByte(); r.ReadByte(); r.ReadByte();
        return r.ReadUInt16();
    }

    [Fact]
    public async Task SwingAnnounce_CarriesTheChartSkill_NeverZero()
    {
        var (h, c, _) = await Setup(MapTestHarness.WithMonsterMelee());
        await h.MonsterTurnAsync(1_000);

        var ack = c.Last(Msg.CS_MONATTACK_ACK);
        Assert.NotNull(ack);
        Assert.Equal(MapTestHarness.MonsterMelee, MonAttackSkill(ack!));
    }

    [Fact]
    public async Task HitResult_CarriesTheSameSkill_NeverZero()
    {
        var (h, c, _) = await Setup(MapTestHarness.WithMonsterMelee());
        await h.MonsterTurnAsync(1_000);

        var ack = c.Last(Msg.CS_DEFEND_ACK);
        Assert.NotNull(ack);
        Assert.Equal(MapTestHarness.MonsterMelee, DefendSkill(ack!));
    }

    [Fact]
    public async Task MonsterWithNoChartSkill_DoesNotSwing()
    {
        var store = new TemplateStore();
        store.MonsterTemplates[500] = new MonsterTemplate(500, 5, 0);   // like the 8 live skill-less monsters
        var (h, c, ch) = await Setup(store);

        await h.MonsterTurnAsync(1_000);

        Assert.False(c.Has(Msg.CS_MONATTACK_ACK));
        Assert.Equal(100u, ch.Hp);
    }

    [Fact]
    public async Task SkillMissingFromTheChart_IsSkipped_NotSent()
    {
        // Like the one live monster whose wSkill1 is absent from TSKILLCHART: sending it would crash the client.
        var store = new TemplateStore();
        store.MonsterTemplates[500] = new MonsterTemplate(500, 5, 0, Skill1: 4242);
        var (h, c, ch) = await Setup(store);

        await h.MonsterTurnAsync(1_000);

        Assert.False(c.Has(Msg.CS_MONATTACK_ACK));
        Assert.Equal(100u, ch.Hp);
    }

    [Fact]
    public async Task FallsThroughToTheNextValidSlot()
    {
        var store = new TemplateStore();
        store.MonsterTemplates[500] = new MonsterTemplate(500, 5, 0, Skill1: 4242, Skill2: 702);
        store.Skills[702] = Skill(702);
        var (h, c, _) = await Setup(store);

        await h.MonsterTurnAsync(1_000);

        var ack = c.Last(Msg.CS_MONATTACK_ACK);
        Assert.NotNull(ack);
        Assert.Equal(702, MonAttackSkill(ack!));
    }
}
