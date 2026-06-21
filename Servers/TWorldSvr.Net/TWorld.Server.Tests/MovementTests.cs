using TWorld.Protocol;
using TWorld.Server.World;
using Xunit;

namespace TWorld.Server.Tests;

/// <summary>Phase 5a — cross-map movement/teleport tests, driven through the real batch task with no DB.
/// On a single map server a teleport reconciles to the same (already-connected) server, so the flow is
/// TELEPORT_ACK -> TELEPORT_REQ + CONLIST_REQ -> CONLIST_ACK(0) -> CHECKMAIN_REQ -> CONRESULT.</summary>
public class MovementTests
{
    private const byte ServerId = 1, ServerType = 4;
    private static ushort WId => (ushort)((ServerType << 8) | ServerId);

    private static void Connect(TcpTestClient client)
    {
        var c = new PacketWriter(Msg.MW_CONNECT_ACK);
        c.WriteUInt16(WId); c.WriteByte(1); c.WriteByte(0);
        client.Send(c);
    }

    /// <summary>Drive a character all the way into the world (incl. the CHECKMAIN handshake).</summary>
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
    public async Task Teleport_SameServer_AcksClientThenReconfirmsMain()
    {
        const uint charId = 7, key = 0xBEEF;
        await using var host = new WorldTestHost();
        using var client = await host.ConnectAsync();
        Connect(client);
        EnterChar(client, charId, key);

        // Map requests a teleport whose destination is this same map server.
        var tp = new PacketWriter(Msg.MW_TELEPORT_ACK);
        tp.WriteUInt32(charId); tp.WriteUInt32(key); tp.WriteByte(ServerId);
        client.Send(tp);

        // World acks the client (TELEPORT_REQ, result success=0) and asks for the destination connection list.
        var tpReq = client.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.MW_TELEPORT_REQ, tpReq.Id);
        Assert.Equal(charId, tpReq.ReadUInt32());
        Assert.Equal(key, tpReq.ReadUInt32());
        tpReq.ReadByte();      // channel
        tpReq.ReadUInt16();    // mapId
        tpReq.ReadFloat(); tpReq.ReadFloat(); tpReq.ReadFloat();
        Assert.Equal((byte)TprResult.Success, tpReq.ReadByte());

        Assert.Equal(Msg.MW_CONLIST_REQ, client.Receive(TimeSpan.FromSeconds(5)).Id);

        // Destination needs no new servers (we're already connected) -> world re-confirms the main.
        var cl = new PacketWriter(Msg.MW_CONLIST_ACK);
        cl.WriteUInt32(charId); cl.WriteUInt32(key); cl.WriteByte(0);
        client.Send(cl);

        Assert.Equal(Msg.MW_CHECKMAIN_REQ, client.Receive(TimeSpan.FromSeconds(5)).Id);
        var cm = new PacketWriter(Msg.MW_CHECKMAIN_ACK);
        cm.WriteUInt32(charId); cm.WriteUInt32(key);
        client.Send(cm);

        var conResult = client.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.MW_CONRESULT_REQ, conResult.Id);
    }

    [Fact]
    public async Task Teleport_UnknownDestination_FailsAndClosesChar()
    {
        const uint charId = 8, key = 0xF00D;
        await using var host = new WorldTestHost();
        using var client = await host.ConnectAsync();
        Connect(client);
        EnterChar(client, charId, key);

        var tp = new PacketWriter(Msg.MW_TELEPORT_ACK);
        tp.WriteUInt32(charId); tp.WriteUInt32(key); tp.WriteByte(99);   // no such server
        client.Send(tp);

        var tpReq = client.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.MW_TELEPORT_REQ, tpReq.Id);
        tpReq.ReadUInt32(); tpReq.ReadUInt32(); tpReq.ReadByte(); tpReq.ReadUInt16();
        tpReq.ReadFloat(); tpReq.ReadFloat(); tpReq.ReadFloat();
        Assert.Equal((byte)TprResult.NoDestination, tpReq.ReadByte());

        await Task.Delay(80);
        Assert.False(host.State.Characters.ContainsKey(charId));   // CloseChar removed it
    }

    [Fact]
    public async Task BeginTeleport_SameChannel_JustUpdatesChannel()
    {
        const uint charId = 9, key = 0xABCD;
        await using var host = new WorldTestHost();
        using var client = await host.ConnectAsync();
        Connect(client);
        EnterChar(client, charId, key);

        var bt = new PacketWriter(Msg.MW_BEGINTELEPORT_ACK);
        bt.WriteUInt32(charId); bt.WriteUInt32(key); bt.WriteByte(1); bt.WriteByte(6);   // sameChannel=1, channel=6
        client.Send(bt);

        await Task.Delay(80);
        Assert.Equal((byte)6, host.State.Characters[charId].Channel);
    }

    [Fact]
    public async Task Region_UpdatesCharRegion()
    {
        const uint charId = 10, key = 0x1234;
        await using var host = new WorldTestHost();
        using var client = await host.ConnectAsync();
        Connect(client);
        EnterChar(client, charId, key);

        var rg = new PacketWriter(Msg.MW_REGION_ACK);
        rg.WriteUInt32(charId); rg.WriteUInt32(key); rg.WriteUInt32(4242);
        client.Send(rg);

        await Task.Delay(80);
        Assert.Equal(4242u, host.State.Characters[charId].Region);
    }

    [Fact]
    public async Task SoloMap_EnterCreatesSoloParty_LeaveDissolvesIt()
    {
        const uint charId = 11, key = 0x5151;
        await using var host = new WorldTestHost();
        using var client = await host.ConnectAsync();
        Connect(client);
        EnterChar(client, charId, key);

        var enter = new PacketWriter(Msg.MW_ENTERSOLOMAP_ACK);
        enter.WriteUInt32(charId); enter.WriteUInt32(key);
        client.Send(enter);

        // World creates a solo party and broadcasts ENTERSOLOMAP_REQ to the connection.
        var soloReq = client.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.MW_ENTERSOLOMAP_REQ, soloReq.Id);

        await Task.Delay(40);
        var party = host.State.Characters[charId].Party;
        Assert.NotNull(party);
        Assert.Equal((byte)1, party!.ObtainType);   // PT_SOLO

        var leave = new PacketWriter(Msg.MW_LEAVESOLOMAP_ACK);
        leave.WriteUInt32(charId); leave.WriteUInt32(key);
        client.Send(leave);

        await Task.Delay(80);
        Assert.Null(host.State.Characters[charId].Party);
    }
}
