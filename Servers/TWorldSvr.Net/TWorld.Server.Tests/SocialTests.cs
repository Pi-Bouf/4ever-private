using TWorld.Protocol;
using TWorld.Server.World;
using Xunit;

namespace TWorld.Server.Tests;

/// <summary>Phase-3 end-to-end tests: friends (ask → reply → add, list) and soulmate search, driven
/// through the real batch task with no DB. Characters get names via a seeded one-member guild (the world
/// learns names from the guild roster on CHARDATA, mirroring Phase 2).</summary>
public class SocialTests
{
    private static void Connect(TcpTestClient client, byte serverId)
    {
        ushort wid = (ushort)((4 << 8) | serverId);
        var c = new PacketWriter(Msg.MW_CONNECT_ACK);
        c.WriteUInt16(wid); c.WriteByte(1); c.WriteByte(0);
        client.Send(c);
    }

    private static void EnterNamed(TcpTestClient client, uint charId, uint key, byte level, byte country)
    {
        var add = new PacketWriter(Msg.MW_ADDCHAR_ACK);
        add.WriteUInt32(charId); add.WriteUInt32(key); add.WriteUInt32(0x0100007F); add.WriteUInt16(5816); add.WriteUInt32(charId + 1000);
        client.Send(add);
        Assert.Equal(Msg.MW_ENTERSVR_REQ, client.Receive(TimeSpan.FromSeconds(5)).Id);

        var cd = new PacketWriter(Msg.MW_CHARDATA_ACK);
        cd.WriteUInt32(charId); cd.WriteUInt32(key); cd.WriteByte(0); cd.WriteByte(level);
        cd.WriteUInt32(500); cd.WriteUInt32(500); cd.WriteUInt32(200); cd.WriteUInt32(200);
        cd.WriteByte(country); cd.WriteByte(0); cd.WriteByte(0);
        client.Send(cd);
        Assert.Equal(Msg.MW_ENTERCHAR_REQ, client.Receive(TimeSpan.FromSeconds(5)).Id);

        var ack = new PacketWriter(Msg.MW_ENTERCHAR_ACK);
        ack.WriteUInt32(charId); ack.WriteUInt32(key);
        client.Send(ack);

        // Complete the main-server check handshake added to the enter flow (CHECKMAIN_REQ -> _ACK -> CONRESULT_REQ).
        Assert.Equal(Msg.MW_CHECKMAIN_REQ, client.Receive(TimeSpan.FromSeconds(5)).Id);
        var cm = new PacketWriter(Msg.MW_CHECKMAIN_ACK);
        cm.WriteUInt32(charId); cm.WriteUInt32(key);
        client.Send(cm);
        Assert.Equal(Msg.MW_CONRESULT_REQ, client.Receive(TimeSpan.FromSeconds(5)).Id);
    }

    private static void SeedNamedChar(WorldState state, uint charId, string name)
    {
        var gl = state.GuildLevels.TryGetValue(1, out var l) ? l : (state.GuildLevels[1] = new GuildLevel { Level = 1, MaxCnt = 20 });
        var g = new Guild { Id = 1000 + charId, Name = "G" + charId, Chief = charId, ChiefName = name, Level = 1, LevelChart = gl };
        g.Members[charId] = new GuildMember { CharId = charId, Name = name, Level = 1, Duty = (byte)GuildDuty.Chief };
        state.Guilds[g.Id] = g;
        state.CharGuild[charId] = g.Id;
    }

    [Fact]
    public async Task Friend_Ask_Reply_LinksBothSides()
    {
        const uint a = 5001, b = 5002, ka = 0xA1, kb = 0xB2;
        await using var host = new WorldTestHost(s => { SeedNamedChar(s, a, "Alice"); SeedNamedChar(s, b, "Bob"); });

        using var ca = await host.ConnectAsync();
        using var cb = await host.ConnectAsync();
        Connect(ca, 1); Connect(cb, 2);
        EnterNamed(ca, a, ka, 20, 4);
        EnterNamed(cb, b, kb, 20, 4);

        // Alice asks Bob.
        var ask = new PacketWriter(Msg.MW_FRIENDASK_ACK);
        ask.WriteUInt32(a); ask.WriteUInt32(ka); ask.WriteString("Bob");
        ca.Send(ask);

        var prompt = cb.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.MW_FRIENDASK_REQ, prompt.Id);
        Assert.Equal(b, prompt.ReadUInt32());
        prompt.ReadUInt32();
        Assert.Equal("Alice", prompt.ReadString());

        // Bob accepts.
        var reply = new PacketWriter(Msg.MW_FRIENDREPLY_ACK);
        reply.WriteUInt32(b); reply.WriteUInt32(kb); reply.WriteString("Alice"); reply.WriteByte(Ask.Yes);
        cb.Send(reply);

        var addB = cb.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.MW_FRIENDADD_REQ, addB.Id);
        addB.ReadUInt32(); addB.ReadUInt32();
        Assert.Equal((byte)FriendResult.Success, addB.ReadByte());
        Assert.Equal(a, addB.ReadUInt32());
        Assert.Equal("Alice", addB.ReadString());

        var addA = ca.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.MW_FRIENDADD_REQ, addA.Id);

        await Task.Delay(100);
        Assert.True(host.State.Characters[a].Friends.ContainsKey(b));
        Assert.Equal(FriendType.FriendFriend, host.State.Characters[a].Friends[b].Type);
        Assert.Equal(FriendType.FriendFriend, host.State.Characters[b].Friends[a].Type);
    }

    [Fact]
    public async Task Friend_List_EmitsGroupsAndFriends()
    {
        const uint a = 6001, ka = 0xC3;
        await using var host = new WorldTestHost(s => SeedNamedChar(s, a, "Solo"));
        using var ca = await host.ConnectAsync();
        Connect(ca, 1);
        EnterNamed(ca, a, ka, 15, 4);

        // Seed an in-memory friend + a group on the live character, then ask for the list.
        await Task.Delay(50);
        var ch = host.State.Characters[a];
        ch.FriendGroups[1] = "Buddies";
        ch.Friends[7777] = new Friend { Id = 7777, Name = "Pal", Level = 12, Group = 1, Class = 2, Type = FriendType.FriendFriend, Connected = false };

        var list = new PacketWriter(Msg.MW_FRIENDLIST_ACK);
        list.WriteUInt32(a); list.WriteUInt32(ka);
        ca.Send(list);

        var resp = ca.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.MW_FRIENDLIST_REQ, resp.Id);
        Assert.Equal(a, resp.ReadUInt32());
        Assert.Equal(ka, resp.ReadUInt32());
        Assert.Equal(0u, resp.ReadUInt32());               // no soulmate -> DWORD(0)
        Assert.Equal((byte)1, resp.ReadByte());            // group count
        Assert.Equal((byte)1, resp.ReadByte());            // group id
        Assert.Equal("Buddies", resp.ReadString());
        Assert.Equal((byte)1, resp.ReadByte());            // friend count
        Assert.Equal(7777u, resp.ReadUInt32());
        Assert.Equal("Pal", resp.ReadString());
    }

    [Fact]
    public async Task Soulmate_Search_PairsTwoChars()
    {
        const uint a = 7001, b = 7002, ka = 0xD4, kb = 0xE5;
        await using var host = new WorldTestHost(s => { SeedNamedChar(s, a, "Yin"); SeedNamedChar(s, b, "Yang"); });
        using var ca = await host.ConnectAsync();
        using var cb = await host.ConnectAsync();
        Connect(ca, 1); Connect(cb, 2);
        EnterNamed(ca, a, ka, 30, 4);
        EnterNamed(cb, b, kb, 30, 4);

        await Task.Delay(50);
        host.State.Characters[a].RealSex = 0; host.State.Characters[a].Sex = 0;
        host.State.Characters[b].RealSex = 1; host.State.Characters[b].Sex = 1;

        // minLevel acts as the "lowest so far" ceiling; the C++ picks the lowest-level eligible candidate
        // strictly below it, so the client passes a high sentinel.
        var search = new PacketWriter(Msg.MW_SOULMATESEARCH_ACK);
        search.WriteUInt32(a); search.WriteUInt32(ka); search.WriteByte(99); search.WriteByte(0); search.WriteByte(0);
        ca.Send(search);

        var resp = ca.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.MW_SOULMATESEARCH_REQ, resp.Id);
        Assert.Equal(a, resp.ReadUInt32());
        Assert.Equal(ka, resp.ReadUInt32());
        Assert.Equal((byte)SoulmateResult.Success, resp.ReadByte());
        Assert.Equal(b, resp.ReadUInt32());
        Assert.Equal("Yang", resp.ReadString());

        await Task.Delay(100);
        Assert.True(host.State.Characters[a].Soulmates.ContainsKey(a));
        Assert.Equal(b, host.State.Characters[a].Soulmates[a].Target);
        Assert.True(host.State.Characters[b].Soulmates.ContainsKey(a));
    }
}
