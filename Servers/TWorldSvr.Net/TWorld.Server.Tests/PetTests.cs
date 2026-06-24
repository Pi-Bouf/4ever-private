using TWorld.Protocol;
using Xunit;

namespace TWorld.Server.Tests;

/// <summary>Phase 5g — pets / mounts / summons. Tame/blood/magic-mirror route to the char's main; mount and
/// helmet changes update state; summon removals fan to the char's connections. On a single map server the
/// "route to main"/"broadcast to connections" relays come back to the same client. No DB.</summary>
public class PetTests
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
    public async Task MonTempt_ForwardedToMain()
    {
        const uint charId = 40, key = 0xE1;
        await using var host = new WorldTestHost();
        using var client = await host.ConnectAsync();
        Connect(client);
        EnterChar(client, charId, key);

        var t = new PacketWriter(Msg.MW_MONTEMPT_ACK);
        t.WriteUInt32(charId); t.WriteUInt16(77);
        client.Send(t);

        var req = client.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.MW_MONTEMPT_REQ, req.Id);
        Assert.Equal(charId, req.ReadUInt32());
        Assert.Equal(key, req.ReadUInt32());
        Assert.Equal((ushort)77, req.ReadUInt16());
    }

    [Fact]
    public async Task GetBlood_PcAttacker_ForwardedToMain()
    {
        const uint charId = 41, key = 0xE2;
        await using var host = new WorldTestHost();
        using var client = await host.ConnectAsync();
        Connect(client);
        EnterChar(client, charId, key);

        var b = new PacketWriter(Msg.MW_GETBLOOD_ACK);
        b.WriteUInt32(charId); b.WriteByte(Proto.ObjTypePc); b.WriteUInt32(999); b.WriteByte(2); b.WriteUInt32(50);
        client.Send(b);

        var req = client.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.MW_GETBLOOD_REQ, req.Id);
        Assert.Equal(charId, req.ReadUInt32());     // owner charId
        Assert.Equal(key, req.ReadUInt32());
        Assert.Equal(charId, req.ReadUInt32());     // atkId
        Assert.Equal(Proto.ObjTypePc, req.ReadByte());
        Assert.Equal((byte)2, req.ReadByte());      // bloodType
        Assert.Equal(50u, req.ReadUInt32());        // blood
    }

    [Fact]
    public async Task HelmetHide_RecordsAndEchoes()
    {
        const uint charId = 42, key = 0xE3;
        await using var host = new WorldTestHost();
        using var client = await host.ConnectAsync();
        Connect(client);
        EnterChar(client, charId, key);

        var h = new PacketWriter(Msg.MW_HELMETHIDE_ACK);
        h.WriteUInt32(charId); h.WriteUInt32(key); h.WriteByte(1);
        client.Send(h);

        var req = client.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.MW_HELMETHIDE_REQ, req.Id);
        Assert.Equal(charId, req.ReadUInt32());
        Assert.Equal(key, req.ReadUInt32());
        Assert.Equal((byte)1, req.ReadByte());
        Assert.Equal((byte)1, host.State.Characters[charId].HelmetHide);
    }

    [Fact]
    public async Task PetRiding_UpdatesState()
    {
        const uint charId = 43, key = 0xE4;
        await using var host = new WorldTestHost();
        using var client = await host.ConnectAsync();
        Connect(client);
        EnterChar(client, charId, key);

        var p = new PacketWriter(Msg.MW_PETRIDING_ACK);
        p.WriteUInt32(charId); p.WriteUInt32(key); p.WriteUInt32(42);
        client.Send(p);

        await Task.Delay(60);
        Assert.Equal(42u, host.State.Characters[charId].Riding);
        // The only connection is the sender, so nothing is fanned back out.
    }

    [Fact]
    public async Task RecallMonDel_FannedToConnections()
    {
        const uint charId = 44, key = 0xE5;
        await using var host = new WorldTestHost();
        using var client = await host.ConnectAsync();
        Connect(client);
        EnterChar(client, charId, key);

        var d = new PacketWriter(Msg.MW_RECALLMONDEL_ACK);
        d.WriteUInt32(charId); d.WriteUInt32(key); d.WriteUInt32(5); d.WriteByte(1);
        client.Send(d);

        var req = client.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.MW_RECALLMONDEL_REQ, req.Id);
        Assert.Equal(charId, req.ReadUInt32());
        Assert.Equal(key, req.ReadUInt32());
        Assert.Equal(5u, req.ReadUInt32());
        Assert.Equal((byte)1, req.ReadByte());
    }
}
