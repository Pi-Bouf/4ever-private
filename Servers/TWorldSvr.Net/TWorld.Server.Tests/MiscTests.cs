using TWorld.Protocol;
using TWorld.Server.World;
using Xunit;

namespace TWorld.Server.Tests;

/// <summary>Phase 5j — friend protected-check (online-status sync) and arena-join party split. No DB.</summary>
public class MiscTests
{
    private const byte ServerId = 1, ServerType = 4;
    private static ushort WId => (ushort)((ServerType << 8) | ServerId);

    private static void Connect(TcpTestClient client)
    {
        var c = new PacketWriter(Msg.MW_CONNECT_ACK);
        c.WriteUInt16(WId); c.WriteByte(1); c.WriteByte(0);
        client.Send(c);
    }

    private static void EnterChar(TcpTestClient client, uint charId, uint key)
    {
        var add = new PacketWriter(Msg.MW_ADDCHAR_ACK);
        add.WriteUInt32(charId); add.WriteUInt32(key); add.WriteUInt32(0x0100007F); add.WriteUInt16(5816); add.WriteUInt32(charId + 1000);
        client.Send(add);
        Assert.Equal(Msg.MW_ENTERSVR_REQ, client.Receive(TimeSpan.FromSeconds(5)).Id);
        var cd = new PacketWriter(Msg.MW_CHARDATA_ACK);
        cd.WriteUInt32(charId); cd.WriteUInt32(key); cd.WriteByte(0); cd.WriteByte(20);
        cd.WriteUInt32(500); cd.WriteUInt32(500); cd.WriteUInt32(200); cd.WriteUInt32(200);
        cd.WriteByte(0); cd.WriteByte(0); cd.WriteByte(0);
        client.Send(cd);
        Assert.Equal(Msg.MW_ENTERCHAR_REQ, client.Receive(TimeSpan.FromSeconds(5)).Id);
        var ack = new PacketWriter(Msg.MW_ENTERCHAR_ACK);
        ack.WriteUInt32(charId); ack.WriteUInt32(key);
        client.Send(ack);
        Assert.Equal(Msg.MW_CHECKMAIN_REQ, client.Receive(TimeSpan.FromSeconds(5)).Id);
        var cm = new PacketWriter(Msg.MW_CHECKMAIN_ACK);
        cm.WriteUInt32(charId); cm.WriteUInt32(key);
        client.Send(cm);
        Assert.Equal(Msg.MW_CONRESULT_REQ, client.Receive(TimeSpan.FromSeconds(5)).Id);
    }

    [Fact]
    public async Task ProtectedCheck_Connection_SyncsBothSidesAndNotifiesPeer()
    {
        await using var host = new WorldTestHost();
        using var client = await host.ConnectAsync();
        Connect(client);
        EnterChar(client, 70, 0x70);
        EnterChar(client, 71, 0x71);

        var a = host.State.Characters[70]; a.Name = "Alice"; a.Region = 100; host.State.CharactersByName["Alice"] = a;
        var b = host.State.Characters[71]; b.Name = "Bob"; b.Region = 200; host.State.CharactersByName["Bob"] = b;
        a.Friends[71] = new Friend { Id = 71, Name = "Bob", Type = FriendType.FriendFriend };
        b.Friends[70] = new Friend { Id = 70, Name = "Alice", Type = FriendType.FriendFriend };

        // Alice reports Bob online.
        var p = new PacketWriter(Msg.MW_PROTECTEDCHECK_ACK);
        p.WriteUInt32(70); p.WriteUInt32(0x70); p.WriteByte((byte)FriendConnState.Connection); p.WriteString("Bob");
        client.Send(p);

        var req = client.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.MW_FRIENDCONNECTION_REQ, req.Id);
        Assert.Equal(71u, req.ReadUInt32());        // notify Bob
        Assert.Equal(0x71u, req.ReadUInt32());
        Assert.Equal((byte)FriendConnState.Connection, req.ReadByte());
        Assert.Equal("Alice", req.ReadString());
        Assert.Equal(100u, req.ReadUInt32());        // Alice's region

        Assert.True(a.Friends[71].Connected);
        Assert.Equal(200u, a.Friends[71].Region);    // Alice's view of Bob -> Bob's region
        Assert.True(b.Friends[70].Connected);
        Assert.Equal(100u, b.Friends[70].Region);    // Bob's view of Alice -> Alice's region
    }

    [Fact]
    public async Task ArenaJoin_SplitsNonJoiningMembers()
    {
        await using var host = new WorldTestHost();
        using var client = await host.ConnectAsync();
        Connect(client);
        EnterChar(client, 72, 0x72);
        EnterChar(client, 73, 0x73);
        EnterChar(client, 74, 0x74);

        var a = host.State.Characters[72];
        var b = host.State.Characters[73];
        var c = host.State.Characters[74];
        var party = new Party { Id = 0x400, ChiefId = 72 };
        party.AddMember(a); party.AddMember(b); party.AddMember(c);
        host.State.Parties[party.Id] = party;

        // A joins arena keeping A + B; C is split off.
        var arena = new PacketWriter(Msg.MW_ARENAJOIN_ACK);
        arena.WriteUInt32(72); arena.WriteUInt32(0x72); arena.WriteByte(1); arena.WriteUInt32(2);
        arena.WriteUInt32(72); arena.WriteUInt32(73);
        client.Send(arena);

        // C's removal broadcasts PARTYDEL to the remaining members + the leaver.
        Assert.Equal(Msg.MW_PARTYDEL_REQ, client.Receive(TimeSpan.FromSeconds(5)).Id);

        await Task.Delay(40);
        Assert.Equal((byte)1, party.Arena);
        Assert.NotNull(party.FindMember(72));
        Assert.NotNull(party.FindMember(73));
        Assert.Null(party.FindMember(74));
        Assert.Null(c.Party);
    }
}
