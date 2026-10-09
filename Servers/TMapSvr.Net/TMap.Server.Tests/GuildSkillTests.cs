using TMap.Data;
using TMap.Protocol;
using TMap.Server.Map;
using Xunit;

namespace TMap.Server.Tests;

/// <summary>An in-memory <see cref="IGuildSkillStore"/>: member skills by (character, skill), officer skills by (guild, skill).</summary>
internal sealed class FakeGuildSkillStore : IGuildSkillStore
{
    public readonly Dictionary<(uint, ushort), GuildSkillRow> Member = new();
    public readonly Dictionary<(uint, ushort), GuildSkillRow> Guild = new();

    public Task<(List<GuildSkillRow> Member, List<GuildSkillRow> Guild)> LoadGuildSkillsAsync(uint charId, uint guildId)
        => Task.FromResult((Member.Where(p => p.Key.Item1 == charId).Select(p => p.Value).ToList(),
            Guild.Where(p => guildId != 0 && p.Key.Item1 == guildId).Select(p => p.Value).ToList()));

    public Task SaveGuildSkillAsync(uint charId, uint guildId, byte type, GuildSkillRow skill)
    {
        if (type == 0) Member[(charId, skill.SkillId)] = skill; else Guild[(guildId, skill.SkillId)] = skill;
        return Task.CompletedTask;
    }
}

/// <summary>Guilds, batch G4: the guild skills — given to the founder, renewed, levelled, cast with the guild's cooldown.</summary>
public class GuildSkillTests
{
    private const ushort ChiefSkill = 3000, ViceSkill = 3001, MemberSkill = 3600;
    private const uint Guild = 77;

    private static SkillTemplate Tmpl(ushort id, byte max) => new(id, Kind: 24, UseMp: 0, UseMpType: 0, UseHp: 0, UseHpType: 0,
        StartLevel: 0, MaxLevel: max, NextLevel: 0, ReuseDelay: 180_000, ReuseDelayInc: 0, LoopDelay: 0, KindDelay: 0,
        SpeedApply: 0, Positive: 1, MapId: 0);

    private static async Task<(MapTestHarness h, ClientSession a, FakeClientChannel ca, FakeGuildSkillStore db)> Setup(byte duty = 2)
    {
        var t = new TemplateStore();
        t.Skills[ChiefSkill] = Tmpl(ChiefSkill, 10);
        t.Skills[ViceSkill] = Tmpl(ViceSkill, 7);
        t.Skills[MemberSkill] = Tmpl(MemberSkill, 7);
        t.GuildSkillTypes[ChiefSkill] = 2; t.GuildSkillTypes[ViceSkill] = 1; t.GuildSkillTypes[MemberSkill] = 0;
        var h = new MapTestHarness(t);
        var db = new FakeGuildSkillStore();
        h.Service.GuildSkillStore = db;
        h.Service.NowMs = 10_000;
        var (a, ca) = await h.EnterAsync(1, 1, 1, x: 100, z: 100, name: "Ann", preSeeded: new Character { CharId = 1, Name = "Ann" });
        (a.Char!.GuildId, a.Char.GuildDuty) = (Guild, duty);
        ca.Clear(); h.World.Clear();
        return (h, a, ca, db);
    }

    private static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    private static byte[] Cs(ushort id, Action<PacketWriter> body) { var w = new PacketWriter(id); body(w); return w.ToArray(); }
    private static byte[] Action(byte action, params ushort[] p) => Cs(Msg.CS_GUILDSKILLACTION_REQ, w =>
    { w.WriteByte(action); w.WriteByte((byte)p.Length); foreach (var x in p) w.WriteUInt16(x); });
    private static byte[] WorldAck(ClientSession s, byte action, params ushort[] p) => Cs(Msg.MW_GUILDSKILLACTION_ACK, w =>
    { w.WriteUInt32(s.CharId); w.WriteUInt32(s.Key); w.WriteByte(action); w.WriteByte((byte)p.Length); foreach (var x in p) w.WriteUInt16(x); });

    private static List<(ushort Id, byte Level, long End)> Update(FakeClientChannel c)
    {
        var r = new PacketReader(c.Last(Msg.CS_GUILDSKILLUPDATE_ACK)!);
        var list = new List<(ushort, byte, long)>();
        for (byte n = r.ReadByte(), i = 0; i < n; i++) list.Add((r.ReadUInt16(), r.ReadByte(), r.ReadInt64()));
        return list;
    }

    [Fact]
    public async Task TheFounder_HoldsEveryGuildSkill_AtLevelOne_WithNoTimeYet()
    {
        var (h, a, ca, db) = await Setup();
        a.Char!.GuildId = 0;

        await h.Service.DispatchWorldAsync(Cs(Msg.MW_GUILDESTABLISH_REQ, w =>
        { w.WriteUInt32(a.CharId); w.WriteUInt32(a.Key); w.WriteByte(0); w.WriteUInt32(Guild); w.WriteString("Blue"); w.WriteByte(1); }));

        Assert.Equal(new[] { (ChiefSkill, (byte)1, 0L), (ViceSkill, (byte)1, 0L), (MemberSkill, (byte)1, 0L) }, Update(ca).ToArray());
        Assert.Equal((byte)1, db.Member[(a.CharId, MemberSkill)].Level);           // a member skill is the character's
        Assert.True(db.Guild.ContainsKey((Guild, ChiefSkill)));                    // an officer's, the guild's
        Assert.DoesNotContain(a.Char.Skills, k => k.SkillId == ChiefSkill);        // run out: not castable
    }

    [Fact]
    public async Task ARunOutSkill_IsRenewedFor300PvPPoints_And31Days()
    {
        var (h, a, ca, db) = await Setup(duty: 0);
        db.Member[(a.CharId, MemberSkill)] = new GuildSkillRow(MemberSkill, 1, 0);
        a.Char!.PvpUseablePoint = 350;
        await h.Service.DispatchWorldAsync(Cs(Msg.MW_GUILDINFO_REQ, w => { w.WriteUInt32(a.CharId); w.WriteUInt32(a.Key); }));

        await h.Service.DispatchClientAsync(a, Action(1 /* GS_RENEW */, MemberSkill, 0));
        Assert.True(h.World.Has(Msg.MW_GUILDSKILLACTION_REQ));
        await h.Service.DispatchWorldAsync(WorldAck(a, 1, MemberSkill, 0));

        Assert.Equal(50u, a.Char.PvpUseablePoint);
        long end = db.Member[(a.CharId, MemberSkill)].EndTime;
        Assert.InRange(end, Now + 31 * 86400 - 60, Now + 31 * 86400 + 60);
        Assert.Contains(a.Char.Skills, k => k.SkillId == MemberSkill && k.Level == 1);
        Assert.True(ca.Has(Msg.CS_GUILDSKILLACTION_ACK));
        Assert.False(h.World.Has(Msg.MW_UPDATEGUILDCOOLDOWN_ACK));                  // a member skill: only its holder
    }

    [Fact]
    public async Task AMemberSkillLevel_CostsTheCharactersStatPoint()
    {
        var (h, a, _, db) = await Setup(duty: 0);
        db.Member[(a.CharId, MemberSkill)] = new GuildSkillRow(MemberSkill, 1, Now + 1000);
        await h.Service.DispatchWorldAsync(Cs(Msg.MW_GUILDINFO_REQ, w => { w.WriteUInt32(a.CharId); w.WriteUInt32(a.Key); }));

        await h.Service.DispatchWorldAsync(WorldAck(a, 0 /* GS_BUY */, MemberSkill, 0));
        Assert.Equal((byte)1, db.Member[(a.CharId, MemberSkill)].Level);           // no stat point: nothing

        a.Char!.Persist.StatPoint = 1;
        await h.Service.DispatchWorldAsync(WorldAck(a, 0, MemberSkill, 0));
        Assert.Equal((byte)2, db.Member[(a.CharId, MemberSkill)].Level);
        Assert.Equal((byte)0, a.Char.Persist.StatPoint);
        Assert.Contains(a.Char.Skills, k => k.SkillId == MemberSkill && k.Level == 2);
    }

    [Fact]
    public async Task AnOfficerSkill_IsTheDutysToBuy_AndTheGuildRereadsIt()
    {
        var (h, a, _, db) = await Setup(duty: 1);
        db.Guild[(Guild, ChiefSkill)] = new GuildSkillRow(ChiefSkill, 1, Now + 1000);
        await h.Service.DispatchClientAsync(a, Action(0, ChiefSkill, 0));
        Assert.False(h.World.Has(Msg.MW_GUILDSKILLACTION_REQ));                    // a vice-chief may not

        a.Char!.GuildDuty = 2;
        await h.Service.DispatchWorldAsync(Cs(Msg.MW_GUILDINFO_REQ, w => { w.WriteUInt32(a.CharId); w.WriteUInt32(a.Key); }));
        await h.Service.DispatchClientAsync(a, Action(0, ChiefSkill, 0));
        var r = new PacketReader(h.World.Last(Msg.MW_GUILDSKILLACTION_REQ)!);
        r.ReadUInt32(); r.ReadUInt32();
        Assert.Equal(((byte)0, (byte)2, ChiefSkill, (ushort)2), (r.ReadByte(), r.ReadByte(), r.ReadUInt16(), r.ReadUInt16()));   // its type for the world

        await h.Service.DispatchWorldAsync(WorldAck(a, 0, ChiefSkill, 2));
        Assert.Equal((byte)2, db.Guild[(Guild, ChiefSkill)].Level);
        r = new PacketReader(h.World.Last(Msg.MW_UPDATEGUILDCOOLDOWN_ACK)!);
        Assert.Equal((Guild, ChiefSkill, (byte)2, 1u, 0u), (r.ReadUInt32(), r.ReadUInt16(), r.ReadByte(), r.ReadUInt32(), r.ReadUInt32()));
    }

    [Fact]
    public async Task ACast_StartsTheGuildsCooldown_ForItsMembersHere()
    {
        var (h, a, ca, db) = await Setup();
        db.Guild[(Guild, ChiefSkill)] = new GuildSkillRow(ChiefSkill, 3, Now + 1000);
        await h.Service.DispatchWorldAsync(Cs(Msg.MW_GUILDINFO_REQ, w => { w.WriteUInt32(a.CharId); w.WriteUInt32(a.Key); }));
        var skill = a.Char!.Skills.Single(k => k.SkillId == ChiefSkill);
        Assert.True(skill.CanUse(h.Service.NowMs));

        await h.Service.DispatchWorldAsync(Cs(Msg.MW_ADDCOOLDOWN_REQ, w => { w.WriteUInt32(Guild); w.WriteUInt16(ChiefSkill); }));

        Assert.False(skill.CanUse(h.Service.NowMs));
        Assert.True(ca.Has(Msg.CS_SKILLBUY_ACK));
        await h.Service.DispatchWorldAsync(Cs(Msg.MW_GUILDINFO_REQ, w => { w.WriteUInt32(a.CharId); w.WriteUInt32(a.Key); }));
        Assert.False(a.Char.Skills.Single(k => k.SkillId == ChiefSkill).CanUse(h.Service.NowMs));   // read again: still waiting
        Assert.DoesNotContain(MapService.BuildSkillSaves(a.Char, h.Service.NowMs), k => k.SkillId == ChiefSkill);   // not a character skill
    }

    private static byte[] Cast(ClientSession s, ushort skill) => Cs(Msg.CS_SKILLUSE_REQ, w =>
    {
        w.WriteUInt32(s.CharId); w.WriteByte(1 /* OT_PC */); w.WriteByte(1); w.WriteUInt16(0); w.WriteUInt16(skill);
        w.WriteByte(0); w.WriteUInt32(0); w.WriteUInt32(0); w.WriteFloat(0); w.WriteFloat(0); w.WriteFloat(0);
        w.WriteByte(1); w.WriteUInt32(s.CharId); w.WriteByte(1); w.WriteByte(1);
    });

    [Fact]
    public async Task AnOfficersCast_TellsTheWorld_AndWithoutTheGuildItIsRefused()
    {
        var (h, a, ca, db) = await Setup();
        a.Char!.Hp = a.Char.MaxHp = 100;
        db.Guild[(Guild, ChiefSkill)] = new GuildSkillRow(ChiefSkill, 2, Now + 1000);
        await h.Service.DispatchWorldAsync(Cs(Msg.MW_GUILDINFO_REQ, w => { w.WriteUInt32(a.CharId); w.WriteUInt32(a.Key); }));

        await h.Service.DispatchClientAsync(a, Cast(a, ChiefSkill));
        Assert.Equal((byte)0, ca.Last(Msg.CS_SKILLUSE_ACK)![PacketHeader.Size]);
        var r = new PacketReader(h.World.Last(Msg.MW_UPDATEGUILDCOOLDOWN_ACK)!);
        Assert.Equal((Guild, ChiefSkill, (byte)2, 0u, 1u), (r.ReadUInt32(), r.ReadUInt16(), r.ReadByte(), r.ReadUInt32(), r.ReadUInt32()));

        a.Char.GuildDuty = 1;                                                       // no longer the chief
        a.Char.Skills.Single(k => k.SkillId == ChiefSkill).ResetCooldown();
        ca.Clear();
        await h.Service.DispatchClientAsync(a, Cast(a, ChiefSkill));
        Assert.NotEqual((byte)0, ca.Last(Msg.CS_SKILLUSE_ACK)![PacketHeader.Size]);
    }

    [Fact]
    public async Task OutOfTheGuild_TheGuildSkillsGo()
    {
        var (h, a, ca, db) = await Setup(duty: 0);
        db.Member[(a.CharId, MemberSkill)] = new GuildSkillRow(MemberSkill, 2, Now + 1000);
        await h.Service.DispatchWorldAsync(Cs(Msg.MW_GUILDINFO_REQ, w => { w.WriteUInt32(a.CharId); w.WriteUInt32(a.Key); }));
        Assert.Contains(a.Char!.Skills, k => k.SkillId == MemberSkill);

        await h.Service.DispatchWorldAsync(Cs(Msg.MW_GUILDLEAVE_REQ, w =>
        { w.WriteUInt32(a.CharId); w.WriteUInt32(a.Key); w.WriteString("Ann"); w.WriteByte(12); w.WriteUInt32(0); }));

        Assert.DoesNotContain(a.Char.Skills, k => k.SkillId == MemberSkill);
        Assert.Empty(Update(ca));
    }
}
