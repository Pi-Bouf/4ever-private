using TWorld.Protocol;
using TWorld.Server.World;
using Xunit;

namespace TWorld.Server.Tests;

/// <summary>
/// RW relay plane: registration ack + map (re)connect broadcast, the inbound RW_ENTERCHAR char query, and an
/// outbound forward (chat-ban → RW_CHATBAN_ACK). No DB. A "relay" / "control" peer is just a TcpTestClient
/// that announces itself with the matching registration packet.
/// </summary>
public class RelayTests
{
    private const byte ServerId = 1, ServerType = 4;
    private static ushort WId => (ushort)((ServerType << 8) | ServerId);

    private static void ConnectMap(TcpTestClient client)
    {
        var c = new PacketWriter(Msg.MW_CONNECT_ACK);
        c.WriteUInt16(WId); c.WriteByte(1); c.WriteByte(0);
        client.Send(c);
    }

    private static void RegisterRelay(TcpTestClient client)
    {
        var w = new PacketWriter(Msg.RW_RELAYSVR_REQ);
        w.WriteUInt16(0x01);
        client.Send(w);
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
    public async Task RelayRegister_RepliesAckAndTellsMapsToConnect()
    {
        await using var host = new WorldTestHost(s => { s.Nation = 7; s.ServerMessages[1] = "hello"; });
        using var map = await host.ConnectAsync();
        ConnectMap(map);
        using var relay = await host.ConnectAsync();
        RegisterRelay(relay);

        var ack = relay.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.RW_RELAYSVR_ACK, ack.Id);
        Assert.Equal((byte)7, ack.ReadByte());        // nation
        Assert.Equal((ushort)0, ack.ReadUInt16());    // operators (not modelled)
        Assert.Equal((ushort)1, ack.ReadUInt16());    // svr-msg count
        Assert.Equal(1u, ack.ReadUInt32());
        Assert.Equal("hello", ack.ReadString());

        var conn = map.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.MW_RELAYCONNECT_REQ, conn.Id);
        Assert.Equal(0u, conn.ReadUInt32());
    }

    [Fact]
    public async Task RelayEnterCharQuery_OnlineChar_ReturnsState()
    {
        await using var host = new WorldTestHost();
        using var map = await host.ConnectAsync();
        ConnectMap(map);
        EnterChar(map, 90, 0x90);
        var ch = host.State.Characters[90];
        ch.Name = "Rel"; ch.Country = 1; host.State.CharactersByName["Rel"] = ch;

        using var relay = await host.ConnectAsync();
        RegisterRelay(relay);
        relay.Receive(TimeSpan.FromSeconds(5)); // RW_RELAYSVR_ACK
        map.Receive(TimeSpan.FromSeconds(5));   // MW_RELAYCONNECT_REQ

        var q = new PacketWriter(Msg.RW_ENTERCHAR_REQ);
        q.WriteUInt32(90); q.WriteString("Rel");
        relay.Send(q);

        var a = relay.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.RW_ENTERCHAR_ACK, a.Id);
        Assert.Equal(90u, a.ReadUInt32());
        Assert.Equal("Rel", a.ReadString());
        Assert.True(a.ReadBool());                    // result: online
        Assert.Equal((byte)1, a.ReadByte());          // country
    }

    [Fact]
    public async Task RelayEnterCharQuery_MissingChar_ReturnsFalse()
    {
        await using var host = new WorldTestHost();
        using var relay = await host.ConnectAsync();
        RegisterRelay(relay);
        relay.Receive(TimeSpan.FromSeconds(5)); // RW_RELAYSVR_ACK

        var q = new PacketWriter(Msg.RW_ENTERCHAR_REQ);
        q.WriteUInt32(123); q.WriteString("Ghost");
        relay.Send(q);

        var a = relay.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.RW_ENTERCHAR_ACK, a.Id);
        Assert.Equal(123u, a.ReadUInt32());
        Assert.Equal("Ghost", a.ReadString());
        Assert.False(a.ReadBool());
    }

    [Fact]
    public async Task ChatBan_ForwardsToRelay()
    {
        await using var host = new WorldTestHost();
        using var map = await host.ConnectAsync();
        ConnectMap(map);
        EnterChar(map, 91, 0x91);
        var ch = host.State.Characters[91];
        ch.Name = "Bad"; host.State.CharactersByName["Bad"] = ch;

        using var relay = await host.ConnectAsync();
        RegisterRelay(relay);
        relay.Receive(TimeSpan.FromSeconds(5)); // RW_RELAYSVR_ACK
        map.Receive(TimeSpan.FromSeconds(5));   // MW_RELAYCONNECT_REQ

        using var ctrl = await host.ConnectAsync();
        ctrl.Send(new PacketWriter(Msg.CT_CTRLSVR_REQ));
        await Task.Delay(40);

        var w = new PacketWriter(Msg.CT_CHATBAN_REQ);
        w.WriteString("Bad"); w.WriteUInt16(10); w.WriteUInt32(1); w.WriteUInt32(2);
        ctrl.Send(w);

        Assert.Equal(Msg.MW_CHATBAN_REQ, map.Receive(TimeSpan.FromSeconds(5)).Id);

        var fwd = relay.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.RW_CHATBAN_ACK, fwd.Id);
        Assert.Equal("Bad", fwd.ReadString());
    }
}
