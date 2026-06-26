using TWorld.Protocol;
using TWorld.Server.World;
using Xunit;

namespace TWorld.Server.Tests;

/// <summary>
/// CT control-plane DB-bound handlers (inline-DB port): cash-mall gift catalog feed (CT_CMGIFTCHARTUPDATE),
/// GM gift delivery (CT_CMGIFT), and item admin (CT_ITEMFIND / CT_ITEMSTATE). No DB — the handlers degrade to
/// the fallback id allocator / empty-result paths, which is exactly what these tests exercise.
/// </summary>
public class ControlDbTests
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
    public async Task CmGiftChartUpdate_Add_AssignsIdAndReturnsCatalog()
    {
        await using var host = new WorldTestHost();
        using var ctrl = await host.ConnectAsync();
        RegisterControl(ctrl);
        await Task.Delay(40);

        var w = new PacketWriter(Msg.CT_CMGIFTCHARTUPDATE_REQ);
        w.WriteUInt16(1);                              // one op
        w.WriteByte((byte)CmGiftUpdate.Add);
        w.WriteUInt16(0);                              // wGiftID placeholder
        w.WriteByte(5);                               // giftType
        w.WriteUInt32(1000);                          // value
        w.WriteByte(1);                              // count
        w.WriteByte(0);                              // takeType (immediate)
        w.WriteByte(1);                              // maxTakeCount
        w.WriteByte(0);                              // toolOnly
        w.WriteUInt16(0);                            // errGiftId
        w.WriteString("Title"); w.WriteString("Msg");
        w.WriteUInt32(42);                           // managerId
        ctrl.Send(w);

        var ack = ctrl.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.CT_CMGIFTLIST_ACK, ack.Id);
        Assert.Equal(42u, ack.ReadUInt32());
        Assert.Equal((ushort)1, ack.ReadUInt16());    // one gift in catalog
        Assert.Equal((ushort)1, ack.ReadUInt16());    // DB-free id allocator -> 1
        Assert.Equal((byte)5, ack.ReadByte());
        Assert.Equal(1000u, ack.ReadUInt32());

        Assert.True(host.State.CmGifts.ContainsKey(1));
        Assert.Equal("Title", host.State.CmGifts[1].Title);
    }

    [Fact]
    public async Task CmGiftChartUpdate_Del_RemovesGift()
    {
        await using var host = new WorldTestHost(s =>
            s.CmGifts[7] = new CmGift { GiftId = 7, Title = "X" });
        using var ctrl = await host.ConnectAsync();
        RegisterControl(ctrl);
        await Task.Delay(40);

        var w = new PacketWriter(Msg.CT_CMGIFTCHARTUPDATE_REQ);
        w.WriteUInt16(1);
        w.WriteByte((byte)CmGiftUpdate.Del);
        w.WriteUInt16(7);
        w.WriteUInt32(0); // managerId
        ctrl.Send(w);

        var ack = ctrl.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.CT_CMGIFTLIST_ACK, ack.Id);
        Assert.False(host.State.CmGifts.ContainsKey(7));
    }

    [Fact]
    public async Task CmGift_Immediate_RoutesToTargetMap()
    {
        await using var host = new WorldTestHost();
        using var map = await host.ConnectAsync();
        ConnectMap(map);
        EnterChar(map, 80, 0x80);
        var target = host.State.Characters[80];
        target.Name = "Tgt"; host.State.CharactersByName["Tgt"] = target;
        host.State.CmGifts[10] = new CmGift { GiftId = 10, GiftType = 3, Value = 50, Count = 2, TakeType = 0, ToolOnly = 0, Title = "Gift", Msg = "Body" };

        using var ctrl = await host.ConnectAsync();
        RegisterControl(ctrl);
        await Task.Delay(40);

        var w = new PacketWriter(Msg.CT_CMGIFT_REQ);
        w.WriteString("Tgt"); w.WriteUInt16(10); w.WriteUInt32(99);
        ctrl.Send(w);

        var req = map.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.MW_CMGIFT_REQ, req.Id);
        Assert.Equal(80u, req.ReadUInt32());          // target charId
        Assert.Equal((byte)3, req.ReadByte());        // giftType
        Assert.Equal(50u, req.ReadUInt32());          // value
    }

    [Fact]
    public async Task ItemState_AppliesAndFansToMapsAndControl()
    {
        await using var host = new WorldTestHost();
        using var map = await host.ConnectAsync();
        ConnectMap(map);
        using var ctrl = await host.ConnectAsync();
        RegisterControl(ctrl);
        await Task.Delay(40);

        var w = new PacketWriter(Msg.CT_ITEMSTATE_REQ);
        w.WriteUInt32(99);            // dwID
        w.WriteUInt16(2);            // count
        w.WriteUInt16(5); w.WriteByte(1);
        w.WriteUInt16(6); w.WriteByte(2);
        ctrl.Send(w);

        var toMap = map.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.MW_ITEMSTATE_REQ, toMap.Id);
        Assert.Equal(99u, toMap.ReadUInt32());
        Assert.Equal((ushort)2, toMap.ReadUInt16());   // no DB -> all applied
        Assert.Equal((ushort)5, toMap.ReadUInt16());
        Assert.Equal((byte)1, toMap.ReadByte());

        var toCtrl = ctrl.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.CT_ITEMSTATE_ACK, toCtrl.Id);
        Assert.Equal(99u, toCtrl.ReadUInt32());
        Assert.Equal((ushort)2, toCtrl.ReadUInt16());
    }

    [Fact]
    public async Task ItemFind_NoDb_ReturnsEmpty()
    {
        await using var host = new WorldTestHost();
        using var ctrl = await host.ConnectAsync();
        RegisterControl(ctrl);
        await Task.Delay(40);

        var w = new PacketWriter(Msg.CT_ITEMFIND_REQ);
        w.WriteUInt32(3); w.WriteUInt16(7); w.WriteString("sword");
        ctrl.Send(w);

        var ack = ctrl.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.CT_ITEMFIND_ACK, ack.Id);
        Assert.Equal((ushort)0, ack.ReadUInt16());     // count
        Assert.Equal(3u, ack.ReadUInt32());            // managerId
    }
}
