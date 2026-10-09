using TMap.Data;
using TMap.Protocol;
using TMap.Server.Map;
using Xunit;

namespace TMap.Server.Tests;

/// <summary>Guilds, batch G1: the map's side of the guild relay — the checks before the world, and the world's answers applied
/// and shown around.</summary>
public class GuildTests
{
    private const uint Guild = 77;

    private static async Task<(MapTestHarness h, ClientSession a, FakeClientChannel ca, ClientSession b, FakeClientChannel cb)> Setup()
    {
        var h = new MapTestHarness(new TemplateStore());
        var (a, ca) = await h.EnterAsync(1, 1, 1, x: 100, z: 100, name: "Ann", preSeeded: new Character { CharId = 1, Name = "Ann", Level = 30 });
        var (b, cb) = await h.EnterAsync(2, 2, 2, x: 101, z: 100, name: "Bob", preSeeded: new Character { CharId = 2, Name = "Bob", Level = 30 });
        a.Char!.Level = b.Char!.Level = 30;
        ca.Clear(); cb.Clear(); h.World.Clear();
        return (h, a, ca, b, cb);
    }

    private static byte[] Cs(ushort id, Action<PacketWriter>? body = null) { var w = new PacketWriter(id); body?.Invoke(w); return w.ToArray(); }

    private static byte[] World(ushort id, ClientSession s, Action<PacketWriter> body)
    {
        var w = new PacketWriter(id);
        w.WriteUInt32(s.CharId); w.WriteUInt32(s.Key);
        body(w);
        return w.ToArray();
    }

    private static void MakeChief(ClientSession s, byte duty = 2) => (s.Char!.GuildId, s.Char.GuildName, s.Char.GuildDuty) = (Guild, "Wolves", duty);

    [Fact]
    public async Task Founding_GoesToTheWorld_WithTheTrimmedName()
    {
        var (h, a, _, _, _) = await Setup();

        await h.Service.DispatchClientAsync(a, Cs(Msg.CS_GUILDESTABLISH_REQ, w => w.WriteString(" Wolves ")));

        var r = new PacketReader(h.World.Last(Msg.MW_GUILDESTABLISH_ACK)!);
        Assert.Equal((a.CharId, a.Key, "Wolves"), (r.ReadUInt32(), r.ReadUInt32(), r.ReadString()));
    }

    [Theory]
    [InlineData("", 30, 0u, 10)]            // GUILD_ESTABLISH_ERR: no name
    [InlineData("Bad.Name", 30, 0u, 10)]    // a dot inside
    [InlineData("Wolves", 19, 0u, 10)]      // under level 20
    [InlineData("Wolves", 30, 5u, 8)]       // GUILD_HAVEGUILD
    public async Task Founding_IsRefused_OnTheMap(string name, byte level, uint guild, byte result)
    {
        var (h, a, ca, _, _) = await Setup();
        a.Char!.Level = level; a.Char.GuildId = guild;

        await h.Service.DispatchClientAsync(a, Cs(Msg.CS_GUILDESTABLISH_REQ, w => w.WriteString(name)));

        Assert.Equal(result, new PacketReader(ca.Last(Msg.CS_GUILDESTABLISH_ACK)!).ReadByte());
        Assert.False(h.World.Has(Msg.MW_GUILDESTABLISH_ACK));
    }

    [Fact]
    public async Task TheWorldFoundsIt_TheFounderIsChief_AndItShowsAround()
    {
        var (h, a, ca, _, cb) = await Setup();

        await h.Service.DispatchWorldAsync(World(Msg.MW_GUILDESTABLISH_REQ, a, w => { w.WriteByte(0); w.WriteUInt32(Guild); w.WriteString("Wolves"); w.WriteByte(1); }));

        Assert.Equal((Guild, (byte)2, "Wolves"), (a.Char!.GuildId, a.Char.GuildDuty, a.Char.GuildName));
        var r = new PacketReader(ca.Last(Msg.CS_GUILDESTABLISH_ACK)!);
        Assert.Equal(((byte)0, Guild, "Wolves"), (r.ReadByte(), r.ReadUInt32(), r.ReadString()));
        var attr = new PacketReader(cb.Last(Msg.CS_GUILDATTR_ACK)!);
        Assert.Equal((a.CharId, Guild), (attr.ReadUInt32(), attr.ReadUInt32()));
        Assert.True(ca.Has(Msg.CS_GUILDSKILLUPDATE_ACK));
    }

    [Fact]
    public async Task OnlyAViceChiefOrTheChief_MayInvite()
    {
        var (h, a, ca, _, _) = await Setup();
        MakeChief(a, duty: 0);

        await h.Service.DispatchClientAsync(a, Cs(Msg.CS_GUILDINVITE_REQ, w => w.WriteString("Bob")));
        Assert.Equal((byte)16 /* GUILD_NODUTY */, new PacketReader(ca.Last(Msg.CS_GUILDINVITE_ACK)!).ReadByte());

        a.Char!.GuildDuty = 1;
        await h.Service.DispatchClientAsync(a, Cs(Msg.CS_GUILDINVITE_REQ, w => w.WriteString("Bob")));
        var r = new PacketReader(h.World.Last(Msg.MW_GUILDINVITE_ACK)!);
        r.ReadUInt32(); r.ReadUInt32();
        Assert.Equal("Bob", r.ReadString());
    }

    [Fact]
    public async Task TheInvitee_IsAsked_OrTheInviterToldItHasAGuild()
    {
        var (h, a, ca, b, cb) = await Setup();
        MakeChief(a);
        byte[] Invite() => World(Msg.MW_GUILDINVITE_REQ, b, w => { w.WriteString("Wolves"); w.WriteUInt32(a.CharId); w.WriteString("Ann"); });

        await h.Service.DispatchWorldAsync(Invite());
        var r = new PacketReader(cb.Last(Msg.CS_GUILDINVITE_ACK)!);
        Assert.Equal(((byte)0, "Wolves", a.CharId, "Ann"), (r.ReadByte(), r.ReadString(), r.ReadUInt32(), r.ReadString()));

        b.Char!.GuildId = 5;
        await h.Service.DispatchWorldAsync(Invite());
        Assert.Equal((byte)8 /* GUILD_HAVEGUILD */, new PacketReader(ca.Last(Msg.CS_GUILDINVITE_ACK)!).ReadByte());
    }

    [Fact]
    public async Task TheAnswer_GoesToTheWorld()
    {
        var (h, _, _, b, _) = await Setup();

        await h.Service.DispatchClientAsync(b, Cs(Msg.CS_GUILDINVITEANSWER_REQ, w => { w.WriteByte(0); w.WriteUInt32(1); }));

        var r = new PacketReader(h.World.Last(Msg.MW_GUILDINVITEANSWER_ACK)!);
        Assert.Equal((b.CharId, b.Key, (byte)0, 1u), (r.ReadUInt32(), r.ReadUInt32(), r.ReadByte(), r.ReadUInt32()));
    }

    [Fact]
    public async Task AJoin_IsToldToTheMember_AndTheNewcomerTakesTheGuild()
    {
        var (h, a, ca, b, cb) = await Setup();
        MakeChief(a);
        byte[] Join(ClientSession to) => World(Msg.MW_GUILDJOIN_REQ, to, w =>
        {
            w.WriteByte(0); w.WriteUInt32(Guild); w.WriteUInt32(9); w.WriteUInt32(3); w.WriteString("Wolves");
            w.WriteUInt32(b.CharId); w.WriteString("Bob"); w.WriteByte(50);
        });

        await h.Service.DispatchWorldAsync(Join(a));
        await h.Service.DispatchWorldAsync(Join(b));

        Assert.True(ca.Has(Msg.CS_GUILDJOIN_ACK));
        Assert.Equal((Guild, "Wolves", 9u, (byte)0), (b.Char!.GuildId, b.Char.GuildName, b.Char.Fame, b.Char.GuildDuty));
        Assert.True(ca.Has(Msg.CS_GUILDATTR_ACK));                       // shown to A, B's neighbour
    }

    [Fact]
    public async Task TheChiefMayNotLeave_OthersMay()
    {
        var (h, a, ca, b, _) = await Setup();
        MakeChief(a);
        MakeChief(b, duty: 0);

        await h.Service.DispatchClientAsync(a, Cs(Msg.CS_GUILDLEAVE_REQ));
        await h.Service.DispatchClientAsync(b, Cs(Msg.CS_GUILDLEAVE_REQ));

        Assert.Equal((byte)16, new PacketReader(ca.Last(Msg.CS_GUILDLEAVE_ACK)!).ReadByte());
        Assert.Single(h.World.WithId(Msg.MW_GUILDLEAVE_ACK));
    }

    [Fact]
    public async Task TheOneWhoLeft_LosesTheGuildAndItsCastleSignUp()
    {
        var (h, _, ca, b, cb) = await Setup();
        MakeChief(b, duty: 0);
        (b.Char!.Castle, b.Char.Camp) = (4, 1);

        await h.Service.DispatchWorldAsync(World(Msg.MW_GUILDLEAVE_REQ, b, w => { w.WriteString("Bob"); w.WriteByte(12 /* GUILD_LEAVE_SELF */); w.WriteUInt32(1234); }));

        Assert.Equal((0u, "", (ushort)0, (byte)0), (b.Char.GuildId, b.Char.GuildName, b.Char.Castle, b.Char.Camp));
        Assert.Equal(((byte)12, 1234u), (b.Char.Persist.GuildLeave, b.Char.Persist.GuildLeaveTime));
        var r = new PacketReader(cb.Last(Msg.CS_GUILDLEAVE_ACK)!);
        Assert.Equal(((byte)0, "Bob", (byte)12), (r.ReadByte(), r.ReadString(), r.ReadByte()));
        var attr = new PacketReader(ca.Last(Msg.CS_GUILDATTR_ACK)!);
        Assert.Equal((b.CharId, 0u), (attr.ReadUInt32(), attr.ReadUInt32()));
    }

    [Fact]
    public async Task KickOut_Duty_Peer_AndDisband_NeedTheirRank()
    {
        var (h, a, ca, _, _) = await Setup();
        MakeChief(a, duty: 1);                                                // a vice-chief

        await h.Service.DispatchClientAsync(a, Cs(Msg.CS_GUILDKICKOUT_REQ, w => w.WriteString("Bob")));
        await h.Service.DispatchClientAsync(a, Cs(Msg.CS_GUILDDUTY_REQ, w => { w.WriteString("Bob"); w.WriteByte(1); }));
        await h.Service.DispatchClientAsync(a, Cs(Msg.CS_GUILDPEER_REQ, w => { w.WriteString("Bob"); w.WriteByte(2); }));
        await h.Service.DispatchClientAsync(a, Cs(Msg.CS_GUILDDISORGANIZATION_REQ, w => w.WriteByte(1)));

        Assert.True(h.World.Has(Msg.MW_GUILDKICKOUT_ACK));                   // a vice-chief may put out
        Assert.False(h.World.Has(Msg.MW_GUILDDUTY_ACK));                     // the rest is the chief's
        Assert.False(h.World.Has(Msg.MW_GUILDPEER_ACK));
        Assert.False(h.World.Has(Msg.MW_GUILDDISORGANIZATION_ACK));
        Assert.Equal((byte)16, new PacketReader(ca.Last(Msg.CS_GUILDDISORGANIZATION_ACK)!).ReadByte());
    }

    [Fact]
    public async Task ANewDutyOrPeerage_IsTakenAndShown()
    {
        var (h, a, ca, b, cb) = await Setup();
        MakeChief(b, duty: 0);

        await h.Service.DispatchWorldAsync(World(Msg.MW_GUILDDUTY_REQ, b, w => { w.WriteString("Bob"); w.WriteByte(1); }));
        await h.Service.DispatchWorldAsync(World(Msg.MW_GUILDPEER_REQ, b, w => { w.WriteByte(0); w.WriteString("Bob"); w.WriteByte(3); w.WriteByte(0); }));

        Assert.Equal(((byte)1, (byte)3), (b.Char!.GuildDuty, b.Char.GuildPeer));
        Assert.True(cb.Has(Msg.CS_GUILDDUTY_ACK));
        var attr = new PacketReader(ca.Last(Msg.CS_GUILDATTR_ACK)!);
        attr.ReadUInt32(); attr.ReadUInt32(); attr.ReadUInt32(); attr.ReadUInt32(); attr.ReadString();
        Assert.Equal((byte)3, attr.ReadByte());
    }

    [Fact]
    public async Task TheGuildWindow_PassesTheWorldsInfo_WithOnesOwnStats()
    {
        var (h, a, ca, _, _) = await Setup();
        MakeChief(a);
        (a.Char!.Persist.StatLevel, a.Char.Persist.StatPoint, a.Char.Persist.StatExp) = (3, 1, 500);

        await h.Service.DispatchClientAsync(a, Cs(Msg.CS_GUILDINFO_REQ));
        await h.Service.DispatchWorldAsync(World(Msg.MW_GUILDINFO_REQ, a, w => { w.WriteByte(0); w.WriteUInt32(Guild); }));
        await h.Service.DispatchWorldAsync(World(Msg.MW_GUILDMEMBERLIST_REQ, a, w => w.WriteByte(7)));

        Assert.True(h.World.Has(Msg.MW_GUILDINFO_ACK));
        var r = new PacketReader(ca.Last(Msg.CS_GUILDINFO_ACK)!);
        Assert.Equal(((byte)0, Guild, (byte)3, (byte)1, 500u), (r.ReadByte(), r.ReadUInt32(), r.ReadByte(), r.ReadByte(), r.ReadUInt32()));
        Assert.Equal((byte)7, new PacketReader(ca.Last(Msg.CS_GUILDMEMBERLIST_ACK)!).ReadByte());
    }
}
