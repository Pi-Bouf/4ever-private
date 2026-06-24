using System.IO;
using TWorld.Protocol;
using TWorld.Server.World;
using Xunit;

namespace TWorld.Server.Tests;

/// <summary>Phase 5f — nation balance. Online level-130..179 chars are bucketed by war-country and 10-level
/// gap; a balance query returns the Defugel/Craxion counts sharing the asker's gap. No DB.</summary>
public class NationTests
{
    private const byte ServerId = 1, ServerType = 4;
    private static ushort WId => (ushort)((ServerType << 8) | ServerId);

    private static void Connect(TcpTestClient client)
    {
        var c = new PacketWriter(Msg.MW_CONNECT_ACK);
        c.WriteUInt16(WId); c.WriteByte(1); c.WriteByte(0);
        client.Send(c);
    }

    private static void EnterChar(TcpTestClient client, uint charId, uint key, byte level, byte country)
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
        Assert.Equal(Msg.MW_CHECKMAIN_REQ, client.Receive(TimeSpan.FromSeconds(5)).Id);
        var cm = new PacketWriter(Msg.MW_CHECKMAIN_ACK);
        cm.WriteUInt32(charId); cm.WriteUInt32(key);
        client.Send(cm);
        Assert.Equal(Msg.MW_CONRESULT_REQ, client.Receive(TimeSpan.FromSeconds(5)).Id);
    }

    [Fact]
    public async Task WarCountryBalance_CountsOnlineByCountryAndGap()
    {
        await using var host = new WorldTestHost();
        using var client = await host.ConnectAsync();
        Connect(client);

        // Gap 0 = levels 130..139. Two Defugel, one Craxion in that gap; one Defugel in gap 1 (level 145).
        EnterChar(client, 1, 0xA1, level: 135, country: (byte)Contry.Defugel);
        EnterChar(client, 2, 0xA2, level: 139, country: (byte)Contry.Defugel);
        EnterChar(client, 3, 0xA3, level: 132, country: (byte)Contry.Craxion);
        EnterChar(client, 4, 0xA4, level: 145, country: (byte)Contry.Defugel);

        await Task.Delay(40);
        Assert.Equal(2, host.State.WarCountry[(byte)Contry.Defugel][0].Count);
        Assert.Equal(1, host.State.WarCountry[(byte)Contry.Craxion][0].Count);
        Assert.Equal(1, host.State.WarCountry[(byte)Contry.Defugel][1].Count);

        // Char 1 (Defugel, gap 0) asks for balance.
        var q = new PacketWriter(Msg.MW_WARCOUNTRYBALANCE_ACK);
        q.WriteUInt32(1); q.WriteUInt32(0xA1);
        client.Send(q);

        var reply = client.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.MW_WARCOUNTRYBALANCE_REQ, reply.Id);
        Assert.Equal(1u, reply.ReadUInt32());          // charId
        Assert.Equal(0xA1u, reply.ReadUInt32());       // key
        Assert.Equal(2u, reply.ReadUInt32());          // Defugel count in gap 0
        Assert.Equal(1u, reply.ReadUInt32());          // Craxion count in gap 0
        Assert.Equal((byte)0, reply.ReadByte());       // gap
    }

    [Fact]
    public async Task LowLevelChar_NotTracked_NoBalanceReply()
    {
        await using var host = new WorldTestHost();
        using var client = await host.ConnectAsync();
        Connect(client);

        EnterChar(client, 5, 0xB5, level: 20, country: (byte)Contry.Defugel);   // below base level -> gap = MaxGap
        await Task.Delay(40);
        Assert.True(host.State.WarCountry[(byte)Contry.Defugel].All(b => b.Count == 0));

        var q = new PacketWriter(Msg.MW_WARCOUNTRYBALANCE_ACK);
        q.WriteUInt32(5); q.WriteUInt32(0xB5);
        client.Send(q);

        // gap >= MaxGap -> no reply; the receive times out (NetworkStream surfaces it as IOException).
        Assert.Throws<IOException>(() => client.Receive(TimeSpan.FromMilliseconds(300)));
    }
}
