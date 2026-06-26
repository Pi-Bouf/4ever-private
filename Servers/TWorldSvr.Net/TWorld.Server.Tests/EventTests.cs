using TWorld.Protocol;
using TWorld.Server.World;
using Xunit;

namespace TWorld.Server.Tests;

/// <summary>
/// Event subsystem: CT_EVENTUPDATE (event-map + broadcast incl. EVENTINFO round-trip), the SM lucky-event
/// quarter draw / pre-announce, the timed-expiry queue ordering, and the (DB-free) event-quarter list. No DB.
/// </summary>
public class EventTests
{
    private const byte ServerId = 1, ServerType = 4;
    private static ushort WId => (ushort)((ServerType << 8) | ServerId);

    private static void ConnectMap(TcpTestClient client)
    {
        var c = new PacketWriter(Msg.MW_CONNECT_ACK);
        c.WriteUInt16(WId); c.WriteByte(1); c.WriteByte(0);
        client.Send(c);
    }

    private static void RegisterControl(TcpTestClient client) => client.Send(new PacketWriter(Msg.CT_CTRLSVR_REQ));

    [Fact]
    public async Task EventUpdate_NonLottery_StoresAndBroadcasts()
    {
        await using var host = new WorldTestHost();
        using var map = await host.ConnectAsync();
        ConnectMap(map);
        await Task.Delay(40);

        var ev = new EventInfo { Index = 100, Id = 1 /* EXPADD, not lottery/gift */, State = 1, Title = "DoubleXP", StartMsg = "go", EndMsg = "done" };
        ev.Lotteries.Add(new EventInfo.Lottery { ItemId = 50, Num = 1, Winner = 2 });

        var w = new PacketWriter(Msg.CT_EVENTUPDATE_REQ);
        w.WriteByte(1);        // eventId
        w.WriteUInt16(7);      // value (!=0 -> stored)
        ev.WriteTo(w);
        map.Send(w);           // any peer can deliver it; map also receives the broadcast back

        var req = map.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.MW_EVENTUPDATE_REQ, req.Id);
        Assert.Equal((byte)1, req.ReadByte());     // eventId
        Assert.Equal((ushort)7, req.ReadUInt16()); // value
        // EVENTINFO body echo: Index, Id, State...
        Assert.Equal(100u, req.ReadUInt32());
        Assert.Equal((byte)1, req.ReadByte());     // Id
        Assert.Equal((byte)1, req.ReadByte());     // State

        Assert.True(host.State.EventInfos.ContainsKey(100));
        Assert.Equal("DoubleXP", host.State.EventInfos[100].Title);
        Assert.Single(host.State.EventInfos[100].Lotteries);
    }

    [Fact]
    public async Task EventQuarterDraw_FansSelectorToMaps()
    {
        await using var host = new WorldTestHost();
        using var map = await host.ConnectAsync();
        ConnectMap(map);
        await Task.Delay(40);

        var w = new PacketWriter(Msg.SM_EVENTQUARTER_REQ);
        w.WriteByte(3); w.WriteByte(20); w.WriteByte(30); w.WriteString("Cake");
        map.Send(w);

        var req = map.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.MW_EVENTQUARTER_REQ, req.Id);
        Assert.Equal((byte)3, req.ReadByte());      // day
        Assert.Equal((byte)20, req.ReadByte());     // hour
        Assert.Equal((byte)30, req.ReadByte());     // minute
        Assert.True(req.ReadByte() < 100);          // selector 0..99
        Assert.Equal("Cake", req.ReadString());
    }

    [Fact]
    public async Task EventQuarterNotify_BroadcastsWorldChat()
    {
        await using var host = new WorldTestHost();
        using var map = await host.ConnectAsync();
        ConnectMap(map);
        await Task.Delay(40);

        var w = new PacketWriter(Msg.SM_EVENTQUARTERNOTIFY_REQ);
        w.WriteString("Event soon!");
        map.Send(w);

        var req = map.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.MW_CHAT_REQ, req.Id);
        req.ReadUInt32(); req.ReadUInt32(); req.ReadByte(); req.ReadUInt32(); // charId,key,channel,senderId
        req.ReadString();                          // operator name
        req.ReadByte(); req.ReadByte();            // country, warCountry
        Assert.Equal((byte)3, req.ReadByte());     // CHAT_WORLD type
        Assert.Equal((byte)3, req.ReadByte());     // CHAT_WORLD group
        req.ReadUInt32();                          // targetId
        Assert.Equal("Event soon!", req.ReadString());
    }

    [Fact]
    public async Task EventExpired_QueueStaysSortedAndRemovable()
    {
        await using var host = new WorldTestHost();
        using var peer = await host.ConnectAsync();
        await Task.Delay(40);

        void Send(bool insert, byte type, long time, uint v1, uint v2)
        {
            var w = new PacketWriter(Msg.SM_EVENTEXPIRED_REQ);
            w.WriteByte(insert ? (byte)1 : (byte)0); w.WriteByte(type); w.WriteInt64(time); w.WriteUInt32(v1); w.WriteUInt32(v2);
            peer.Send(w);
        }

        Send(true, 1, 300, 10, 0);
        Send(true, 1, 100, 11, 0);
        Send(true, 1, 200, 12, 0);
        await Task.Delay(60);

        Assert.Equal(3, host.State.Expired.Count);
        Assert.Equal(100, host.State.Expired[0].TimeExpired);
        Assert.Equal(200, host.State.Expired[1].TimeExpired);
        Assert.Equal(300, host.State.Expired[2].TimeExpired);

        Send(false, 1, 200, 12, 0); // remove the middle entry
        await Task.Delay(60);
        Assert.Equal(2, host.State.Expired.Count);
        Assert.DoesNotContain(host.State.Expired, e => e.TimeExpired == 200);
    }

    [Fact]
    public async Task EventQuarterList_NoDb_ReturnsEmpty()
    {
        await using var host = new WorldTestHost();
        using var ctrl = await host.ConnectAsync();
        RegisterControl(ctrl);
        await Task.Delay(40);

        var w = new PacketWriter(Msg.CT_EVENTQUARTERLIST_REQ);
        w.WriteUInt32(5); w.WriteByte(3);
        ctrl.Send(w);

        var ack = ctrl.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.CT_EVENTQUARTERLIST_ACK, ack.Id);
        Assert.Equal(5u, ack.ReadUInt32());
        Assert.Equal((ushort)0, ack.ReadUInt16());
    }
}
