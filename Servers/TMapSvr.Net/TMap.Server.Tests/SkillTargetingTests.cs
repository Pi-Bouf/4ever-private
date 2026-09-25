using TMap.Data;
using TMap.Protocol;
using TMap.Server.Map;
using Xunit;

namespace TMap.Server.Tests;

/// <summary>
/// Two bugs found with the real client, both in CS_SKILLUSE:
/// <list type="number">
/// <item><b>Bystanders hit.</b> The client lists every object in the skill's area and flags only the real target
/// with bIsTarget; the rest are bystanders. The port dropped the flag and echoed them all back in the ACK, and the
/// client attacks exactly the ACK's list — so a single-target swing hit every monster near the target. (The test
/// harness had always written bIsTarget = 1, which is why no test caught it.)</item>
/// <item><b>No monster animation.</b> After CS_MONATTACK_ACK the host client sends CS_SKILLUSE_REQ on the monster's
/// behalf; its ACK is what animates the swing. The port dropped every non-player caster, and it also broadcast
/// CS_MONATTACK_ACK to all viewers instead of the host alone.</item>
/// </list>
/// </summary>
public class SkillTargetingTests
{
    private const byte OtPc = 1, OtMon = 2;
    private const ushort Sid = 100;

    private static TemplateStore Store()
    {
        var t = MapTestHarness.WithMonsterMelee();
        t.Rate1st = 1.0f;
        t.Formulas[8] = new FormulaRow(200, 0f, 0f);
        t.Formulas[19] = new FormulaRow(100, 0f, 0f);
        t.Classes[1] = new StatSeed(1, 1, 1, 1, 1, 1);
        t.Races[1] = new StatSeed(1, 1, 1, 1, 1, 1);
        t.Skills[Sid] = new SkillTemplate(Sid, Kind: 0, UseMp: 0, UseMpType: 0, UseHp: 0, UseHpType: 0,
            StartLevel: 1, MaxLevel: 10, NextLevel: 0, ReuseDelay: 0, ReuseDelayInc: 0, LoopDelay: 0, KindDelay: 0,
            SpeedApply: 1, Positive: 0, MapId: 0xFFFF);
        return t;
    }

    private static Character Hero(uint id, TemplateStore t)
    {
        var ch = new Character { CharId = id, Name = "Hero" + id, Class = 1, Race = 1, Hp = 200, Mp = 100 };
        ch.Skills.Add(new Skill { SkillId = Sid, Level = 1, Template = t.Skills[Sid] });
        return ch;
    }

    private static Monster Mob(uint id, uint hostId = 0) => new()
    {
        Id = id, ChartId = 500, Level = 5, MaxHp = 100, Hp = 100, PosX = 3663, PosZ = 557, StartX = 3663,
        StartZ = 557, Channel = 1, MapId = 0, HostId = hostId, AtkMin = 11, AtkMax = 22, AttackLevel = 7, CritProb = 3,
    };

    /// <summary>CS_SKILLUSE_REQ with an explicit bIsTarget per entry — the harness builder always writes 1.</summary>
    private static byte[] SkillUse(uint attackerId, byte attackType, ushort skillId,
        params (uint id, byte type, bool isTarget)[] targets)
    {
        var w = new PacketWriter(Msg.CS_SKILLUSE_REQ);
        w.WriteUInt32(attackerId); w.WriteByte(attackType); w.WriteByte(1); w.WriteUInt16(0);
        w.WriteUInt16(skillId); w.WriteByte(0); w.WriteUInt32(0); w.WriteUInt32(0);
        w.WriteFloat(0); w.WriteFloat(0); w.WriteFloat(0);
        w.WriteByte((byte)targets.Length);
        foreach (var (id, type, isTarget) in targets) { w.WriteUInt32(id); w.WriteByte(type); w.WriteByte(isTarget ? (byte)1 : (byte)0); }
        return w.ToArray();
    }

    private sealed record Ack(byte Result, uint AttackId, byte AttackType, uint PysMin, uint PysMax, ushort AttackLevel,
        byte Cp, List<(uint Id, byte Type)> Targets);

    private static Ack Parse(byte[] p)
    {
        var r = new PacketReader(p);
        byte result = r.ReadByte(); uint aid = r.ReadUInt32(); byte atype = r.ReadByte();
        r.ReadUInt16(); r.ReadUInt16(); r.ReadByte(); r.ReadUInt32(); r.ReadUInt32(); r.ReadByte();
        ushort al = r.ReadUInt16(); r.ReadByte();
        uint pmin = r.ReadUInt32(), pmax = r.ReadUInt32(); r.ReadUInt32(); r.ReadUInt32();
        r.ReadUInt16(); r.ReadUInt16(); r.ReadByte(); r.ReadByte(); r.ReadByte(); r.ReadByte(); r.ReadByte();
        byte cp = r.ReadByte();
        r.ReadFloat(); r.ReadFloat(); r.ReadFloat();
        byte n = r.ReadByte();
        var t = new List<(uint, byte)>();
        for (int i = 0; i < n; i++) t.Add((r.ReadUInt32(), r.ReadByte()));
        return new Ack(result, aid, atype, pmin, pmax, al, cp, t);
    }

    // ================= bystanders =================

    [Fact]
    public async Task OnlyFlaggedTargets_AreEchoed_BystandersAreNot()
    {
        var store = Store();
        var h = new MapTestHarness(store);
        var (s, c) = await h.EnterAsync(1, 1, 1, preSeeded: Hero(1, store));
        c.Clear();

        await h.Service.DispatchClientAsync(s, SkillUse(1, OtPc, Sid,
            (0x30001, OtMon, true), (0x30002, OtMon, false), (0x30003, OtMon, false)));

        var ack = Parse(c.Last(Msg.CS_SKILLUSE_ACK)!);
        Assert.Equal((byte)SkillUseResult.Success, ack.Result);
        Assert.Equal(new[] { (0x30001u, OtMon) }, ack.Targets);
    }

    [Fact]
    public async Task TargetList_IsCappedAtMaxTarget()
    {
        var store = Store();
        var h = new MapTestHarness(store);
        var (s, c) = await h.EnterAsync(1, 1, 1, preSeeded: Hero(1, store));
        c.Clear();

        var many = Enumerable.Range(0, 20).Select(i => ((uint)(0x30001 + i), OtMon, true)).ToArray();
        await h.Service.DispatchClientAsync(s, SkillUse(1, OtPc, Sid, many));

        Assert.Equal(16, Parse(c.Last(Msg.CS_SKILLUSE_ACK)!).Targets.Count);   // MAX_TARGET
    }

    // ================= monster caster =================

    [Fact]
    public async Task HostAnnouncingItsMonstersSkill_AnimatesForEveryone()
    {
        var store = Store();
        var h = new MapTestHarness(store);
        var (host, hostClient) = await h.EnterAsync(1, 1, 1, preSeeded: Hero(1, store));
        var (_, viewer) = await h.EnterAsync(2, 2, 2, name: "Viewer", preSeeded: Hero(2, store));
        h.Service.SpawnMonster(Mob(0x50001, hostId: 1));
        hostClient.Clear(); viewer.Clear();

        await h.Service.DispatchClientAsync(host, SkillUse(0x50001, OtMon, MapTestHarness.MonsterMelee, (1, OtPc, true)));

        foreach (var client in new[] { hostClient, viewer })
        {
            var ack = Parse(client.Last(Msg.CS_SKILLUSE_ACK)!);
            Assert.Equal((byte)SkillUseResult.Success, ack.Result);
            Assert.Equal(0x50001u, ack.AttackId);
            Assert.Equal(OtMon, ack.AttackType);
            Assert.Equal(11u, ack.PysMin);          // the monster's own attack payload
            Assert.Equal(22u, ack.PysMax);
            Assert.Equal(7, ack.AttackLevel);
            Assert.Equal(3, ack.Cp);
            Assert.Equal(new[] { (1u, OtPc) }, ack.Targets);
        }
    }

    [Fact]
    public async Task NonHostCannotDriveTheMonster()
    {
        var store = Store();
        var h = new MapTestHarness(store);
        var (s, c) = await h.EnterAsync(1, 1, 1, preSeeded: Hero(1, store));
        h.Service.SpawnMonster(Mob(0x50001, hostId: 99));
        c.Clear();

        await h.Service.DispatchClientAsync(s, SkillUse(0x50001, OtMon, MapTestHarness.MonsterMelee, (1, OtPc, true)));

        Assert.False(c.Has(Msg.CS_SKILLUSE_ACK));
    }

    [Fact]
    public async Task ASkillTheMonsterDoesNotHave_IsRefused()
    {
        var store = Store();
        var h = new MapTestHarness(store);
        var (s, c) = await h.EnterAsync(1, 1, 1, preSeeded: Hero(1, store));
        h.Service.SpawnMonster(Mob(0x50001, hostId: 1));
        c.Clear();

        // Sid exists in the skill chart, but is not one of chart 500's monster skills.
        await h.Service.DispatchClientAsync(s, SkillUse(0x50001, OtMon, Sid, (1, OtPc, true)));

        Assert.Equal((byte)SkillUseResult.NotFound, Parse(c.Last(Msg.CS_SKILLUSE_ACK)!).Result);
    }

    [Fact]
    public async Task MonsterAttackAnnounce_GoesToTheHostOnly()
    {
        var store = Store();
        var h = new MapTestHarness(store);
        var victim = new Character { CharId = 1, Name = "Victim", MaxHp = 100, Hp = 100, MaxMp = 50, Mp = 50 };
        var (_, hostClient) = await h.EnterAsync(1, 1, 1, x: 120, z: 100, preSeeded: victim);
        var (_, viewer) = await h.EnterAsync(2, 2, 2, name: "Viewer", x: 125, z: 100, preSeeded: Hero(2, store));
        h.Service.SpawnMonster(new Monster
        {
            Id = 0x50001, ChartId = 500, Level = 5, MaxHp = 100, Hp = 100, PosX = 100, PosZ = 100, StartX = 100,
            StartZ = 100, Mode = 1, TargetId = 1, HostId = 1, AtkMin = 20, AtkMax = 20, AtkSpeed = 2000,
            AttackLevel = 10, Channel = 1, MapId = 0, Region = 7,
        });
        h.Service.CombatRng = new Random(1);
        hostClient.Clear(); viewer.Clear();

        h.Service.RunMonsterAI(1_000);

        Assert.True(hostClient.Has(Msg.CS_MONATTACK_ACK));
        Assert.False(viewer.Has(Msg.CS_MONATTACK_ACK));   // else every viewer would re-announce the swing
        Assert.True(viewer.Has(Msg.CS_DEFEND_ACK));       // the hit itself is still seen by everyone near
    }
}
