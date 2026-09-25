using TMap.Data;
using TMap.Protocol;
using TMap.Server.Map;
using Xunit;

namespace TMap.Server.Tests;

/// <summary>
/// CS_ACTION — the action/animation gate the client blocks on before a swing or cast proceeds to
/// CS_SKILLUSE / CS_DEFEND. The two rules that make or break it: the ACK reaches the <b>actor itself</b>
/// (no self-exclusion, unlike CS_MOVE), and a failed precondition still broadcasts an ACK carrying the
/// reason rather than going silent.
/// </summary>
public class ActionTests
{
    private const ushort Sid = 100;

    private static TemplateStore VitalStore()
    {
        var t = new TemplateStore { Rate1st = 1.0f };
        t.Formulas[8] = new FormulaRow(200, 0f, 0f);   // FTYPE_HP
        t.Formulas[19] = new FormulaRow(100, 0f, 0f);  // FTYPE_MP
        t.Classes[1] = new StatSeed(1, 1, 1, 1, 1, 1);
        t.Races[1] = new StatSeed(1, 1, 1, 1, 1, 1);
        return t;
    }

    // UseMpType/UseHpType 2 = a PERCENTAGE of pure max HP/MP. Type 1 is a flat value scaled by
    // pow(Rate1stX, exp)/100, which at Rate1stX=1 truncates any small cost to 0 — useless for a cost test.
    private static SkillTemplate Tmpl(uint useMp = 0, uint useHp = 0, uint reuseDelay = 0,
                                      byte useMpType = 2, byte useHpType = 2)
        => new(Sid, Kind: 0, UseMp: useMp, UseMpType: useMpType, UseHp: useHp, UseHpType: useHpType,
               StartLevel: 1, MaxLevel: 100, NextLevel: 0, ReuseDelay: reuseDelay, ReuseDelayInc: 0,
               LoopDelay: 0, KindDelay: 0, SpeedApply: 1, Positive: 0, MapId: 0, Rate1stX: 1f);

    /// <summary>CS_ACTION_REQ as the client sends it (CSHandler.cpp:1248-1256).</summary>
    private static byte[] ActionReq(uint objId, byte objType, byte actionId, uint actId, uint aniId,
                                    byte channel, ushort mapId, ushort skillId)
    {
        var w = new PacketWriter(Msg.CS_ACTION_REQ);
        w.WriteUInt32(objId); w.WriteByte(objType); w.WriteByte(actionId);
        w.WriteUInt32(actId); w.WriteUInt32(aniId);
        w.WriteByte(channel); w.WriteUInt16(mapId); w.WriteUInt16(skillId);
        return w.ToArray();
    }

    private static (byte result, uint objId, byte objType, byte actionId, uint actId, uint aniId, ushort skillId)
        ParseAck(byte[] p)
    {
        var r = new PacketReader(p);
        return (r.ReadByte(), r.ReadUInt32(), r.ReadByte(), r.ReadByte(), r.ReadUInt32(), r.ReadUInt32(), r.ReadUInt16());
    }

    private static Character Caster(uint hp = 200, uint mp = 100)
        => new() { CharId = 1, Name = "Hero", Class = 1, Race = 1, Hp = hp, Mp = mp };

    // ---- the rule that unblocks the client ----

    [Fact]
    public async Task ActionAck_ReachesTheActorItself()
    {
        var h = new MapTestHarness(VitalStore());
        var (s, client) = await h.EnterAsync(1, 1, 1, preSeeded: Caster());
        client.Clear();

        await h.Service.DispatchClientAsync(s, ActionReq(1, 1, 3, 7, 9, 1, 0, 0));

        // No self-exclusion here (unlike CS_MOVE) — without its own copy the caster never animates.
        Assert.True(client.Has(Msg.CS_ACTION_ACK), "the actor must receive its own CS_ACTION_ACK");
    }

    [Fact]
    public async Task ActionAck_IsByteExact()
    {
        var h = new MapTestHarness(VitalStore());
        var (s, client) = await h.EnterAsync(1, 1, 1, preSeeded: Caster());
        client.Clear();

        await h.Service.DispatchClientAsync(s, ActionReq(objId: 1, objType: 1, actionId: 3,
            actId: 0x11223344, aniId: 0x55667788, channel: 1, mapId: 0, skillId: 0));

        var ack = client.Last(Msg.CS_ACTION_ACK);
        Assert.NotNull(ack);
        // bResult(1) dwObjID(4) bObjType(1) bActionID(1) dwActID(4) dwAniID(4) wSkillID(2) = 17-byte body.
        Assert.Equal(PacketHeader.Size + 17, ack!.Length);
        var (result, objId, objType, actionId, actId, aniId, skillId) = ParseAck(ack);
        Assert.Equal((byte)SkillUseResult.Success, result);
        Assert.Equal(1u, objId);
        Assert.Equal(1, objType);
        Assert.Equal(3, actionId);
        Assert.Equal(0x11223344u, actId);
        Assert.Equal(0x55667788u, aniId);
        Assert.Equal(0, skillId);
    }

    [Fact]
    public async Task ActionAck_ReachesNearbyPlayers()
    {
        var h = new MapTestHarness(VitalStore());
        var (s, _) = await h.EnterAsync(1, 1, 1, preSeeded: Caster());
        var (_, other) = await h.EnterAsync(2, 2, 2, name: "Other", preSeeded: new Character { CharId = 2, Name = "Other", Class = 1, Race = 1, Hp = 200, Mp = 100 });
        other.Clear();

        await h.Service.DispatchClientAsync(s, ActionReq(1, 1, 3, 7, 9, 1, 0, 0));

        Assert.True(other.Has(Msg.CS_ACTION_ACK), "players in view must see the action");
    }

    // ---- preconditions: reported in the ACK, never silently dropped ----

    [Fact]
    public async Task UnknownSkill_ReportsNotFound_ButStillBroadcasts()
    {
        var h = new MapTestHarness(VitalStore());
        var (s, client) = await h.EnterAsync(1, 1, 1, preSeeded: Caster());
        client.Clear();

        await h.Service.DispatchClientAsync(s, ActionReq(1, 1, 3, 7, 9, 1, 0, Sid));

        var ack = client.Last(Msg.CS_ACTION_ACK);
        Assert.NotNull(ack);
        Assert.Equal((byte)SkillUseResult.NotFound, ParseAck(ack!).result);
    }

    [Fact]
    public async Task NotEnoughMp_ReportsNeedMp_ButStillBroadcasts()
    {
        var store = VitalStore();
        store.Skills[Sid] = Tmpl(useMp: 50);           // 50% of PureMaxMP(100) = 50
        var h = new MapTestHarness(store);
        var ch = Caster(mp: 10);
        ch.Skills.Add(new Skill { SkillId = Sid, Level = 1, Template = store.Skills[Sid] });
        var (s, client) = await h.EnterAsync(1, 1, 1, preSeeded: ch);
        client.Clear();

        await h.Service.DispatchClientAsync(s, ActionReq(1, 1, 3, 7, 9, 1, 0, Sid));

        var ack = client.Last(Msg.CS_ACTION_ACK);
        Assert.NotNull(ack);
        Assert.Equal((byte)SkillUseResult.NeedMp, ParseAck(ack!).result);
    }

    [Fact]
    public async Task KnownAffordableSkill_Succeeds_AndDeductsNothing()
    {
        var store = VitalStore();
        store.Skills[Sid] = Tmpl(useMp: 10);   // 10% of PureMaxMP(100) = 10
        var h = new MapTestHarness(store);
        var ch = Caster(mp: 100);
        ch.Skills.Add(new Skill { SkillId = Sid, Level = 1, Template = store.Skills[Sid] });
        var (s, client) = await h.EnterAsync(1, 1, 1, preSeeded: ch);
        client.Clear();

        await h.Service.DispatchClientAsync(s, ActionReq(1, 1, 3, 7, 9, 1, 0, Sid));

        var ack = client.Last(Msg.CS_ACTION_ACK);
        Assert.NotNull(ack);
        Assert.Equal((byte)SkillUseResult.Success, ParseAck(ack!).result);
        // CS_ACTION is a pure gate: the C++ deducts cost and arms the cooldown in CS_SKILLUSE, not here.
        Assert.Equal(100u, ch.Mp);
    }

    [Fact]
    public async Task PlainMelee_NoSkill_Succeeds()
    {
        var h = new MapTestHarness(VitalStore());
        var (s, client) = await h.EnterAsync(1, 1, 1, preSeeded: Caster());
        client.Clear();

        await h.Service.DispatchClientAsync(s, ActionReq(1, 1, 3, 7, 9, 1, 0, skillId: 0));

        var ack = client.Last(Msg.CS_ACTION_ACK);
        Assert.NotNull(ack);
        Assert.Equal((byte)SkillUseResult.Success, ParseAck(ack!).result);
    }

    [Fact]
    public async Task UnportedActorKind_IsIgnored()
    {
        var h = new MapTestHarness(VitalStore());
        var (s, client) = await h.EnterAsync(1, 1, 1, preSeeded: Caster());
        client.Clear();

        // OT_COMPANION (7) — summons/pets unported, so no object resolves and nothing is broadcast.
        await h.Service.DispatchClientAsync(s, ActionReq(1, 7, 3, 7, 9, 1, 0, 0));

        Assert.False(client.Has(Msg.CS_ACTION_ACK));
    }
}
