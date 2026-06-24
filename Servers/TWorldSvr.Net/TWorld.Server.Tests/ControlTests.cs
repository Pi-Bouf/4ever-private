using TWorld.Protocol;
using TWorld.Server.World;
using Xunit;

namespace TWorld.Server.Tests;

/// <summary>CT control plane — TControlSvr↔world admin/monitoring. A "map" client registers + enters a char;
/// a separate "control" client issues CT commands. Replies land on the control client; relays land on the
/// map; broadcasts hit every registered map. DB-free.</summary>
public class ControlTests
{
    private const byte ServerId = 1, ServerType = 4;
    private static ushort WId => (ushort)((ServerType << 8) | ServerId);

    private static void Connect(TcpTestClient map)
    {
        var c = new PacketWriter(Msg.MW_CONNECT_ACK);
        c.WriteUInt16(WId); c.WriteByte(1); c.WriteByte(0);
        map.Send(c);
    }

    private static void EnterChar(TcpTestClient map, uint charId, uint key)
    {
        var add = new PacketWriter(Msg.MW_ADDCHAR_ACK);
        add.WriteUInt32(charId); add.WriteUInt32(key); add.WriteUInt32(0x0100007F); add.WriteUInt16(5816); add.WriteUInt32(charId + 1000);
        map.Send(add);
        Assert.Equal(Msg.MW_ENTERSVR_REQ, map.Receive(TimeSpan.FromSeconds(5)).Id);
        var cd = new PacketWriter(Msg.MW_CHARDATA_ACK);
        cd.WriteUInt32(charId); cd.WriteUInt32(key); cd.WriteByte(0); cd.WriteByte(20);
        cd.WriteUInt32(500); cd.WriteUInt32(500); cd.WriteUInt32(200); cd.WriteUInt32(200);
        cd.WriteByte(0); cd.WriteByte(0); cd.WriteByte(0);
        map.Send(cd);
        Assert.Equal(Msg.MW_ENTERCHAR_REQ, map.Receive(TimeSpan.FromSeconds(5)).Id);
        var ack = new PacketWriter(Msg.MW_ENTERCHAR_ACK);
        ack.WriteUInt32(charId); ack.WriteUInt32(key);
        map.Send(ack);
        Assert.Equal(Msg.MW_CHECKMAIN_REQ, map.Receive(TimeSpan.FromSeconds(5)).Id);
        var cm = new PacketWriter(Msg.MW_CHECKMAIN_ACK);
        cm.WriteUInt32(charId); cm.WriteUInt32(key);
        map.Send(cm);
        Assert.Equal(Msg.MW_CONRESULT_REQ, map.Receive(TimeSpan.FromSeconds(5)).Id);
    }

    private static void NameChar(WorldTestHost host, uint charId, string name)
    {
        var ch = host.State.Characters[charId];
        ch.Name = name;
        host.State.CharactersByName[name] = ch;
    }

    [Fact]
    public async Task ServiceMonitor_RepliesWithLiveCounts()
    {
        await using var host = new WorldTestHost();
        using var map = await host.ConnectAsync();
        Connect(map);
        EnterChar(map, 200, 0x200);

        using var ctrl = await host.ConnectAsync();
        var q = new PacketWriter(Msg.CT_SERVICEMONITOR_ACK);
        q.WriteUInt32(12345);
        ctrl.Send(q);

        var rep = ctrl.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.CT_SERVICEMONITOR_REQ, rep.Id);
        Assert.Equal(12345u, rep.ReadUInt32());
        Assert.Equal(1u, rep.ReadUInt32());   // sessions (the one map)
        Assert.Equal(1u, rep.ReadUInt32());   // characters
        Assert.Equal(1u, rep.ReadUInt32());   // active users
    }

    [Fact]
    public async Task RpsGameChange_UpdatesConfig_RepliesAndBroadcasts()
    {
        await using var host = new WorldTestHost(s =>
            s.RpsGames[(ushort)(1 | (3 << 8))] = new RpsGame { Type = 1, WinCount = 3, WinKeep = 1, WinPeriod = 5 });
        using var map = await host.ConnectAsync();
        Connect(map);
        using var ctrl = await host.ConnectAsync();

        var chg = new PacketWriter(Msg.CT_RPSGAMECHANGE_REQ);
        chg.WriteByte(0);                      // group
        chg.WriteUInt16(1);                    // count
        chg.WriteByte(1); chg.WriteByte(3);    // type, winCount
        chg.WriteByte(70); chg.WriteByte(20); chg.WriteByte(10);  // probs
        chg.WriteUInt16(4); chg.WriteUInt16(9);                    // winKeep, winPeriod
        ctrl.Send(chg);

        // control gets the updated config back
        var ack = ctrl.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.CT_RPSGAMEDATA_ACK, ack.Id);
        Assert.True(ack.ReadBool());           // change
        Assert.Equal((byte)0, ack.ReadByte()); // group
        Assert.Equal((ushort)1, ack.ReadUInt16());
        Assert.Equal((byte)1, ack.ReadByte()); Assert.Equal((byte)3, ack.ReadByte());
        Assert.Equal((byte)70, ack.ReadByte());

        // map gets the change broadcast
        Assert.Equal(Msg.MW_RPSGAMECHANGE_REQ, map.Receive(TimeSpan.FromSeconds(5)).Id);

        var rps = host.State.RpsGames[(ushort)(1 | (3 << 8))];
        Assert.Equal((ushort)4, rps.WinKeep);
        Assert.Equal((ushort)9, rps.WinPeriod);
    }

    [Fact]
    public async Task CastleGuildChg_BothGuildsExist_BroadcastsAndAcksSuccess()
    {
        await using var host = new WorldTestHost(s =>
        {
            s.Guilds[10] = new Guild { Id = 10, Name = "Def" };
            s.Guilds[20] = new Guild { Id = 20, Name = "Atk" };
        });
        using var map = await host.ConnectAsync();
        Connect(map);
        using var ctrl = await host.ConnectAsync();

        var req = new PacketWriter(Msg.CT_CASTLEGUILDCHG_REQ);
        req.WriteUInt16(5); req.WriteUInt32(10); req.WriteUInt32(20); req.WriteUInt32(999); req.WriteInt64(123456);
        ctrl.Send(req);

        var bcast = map.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.MW_CASTLEGUILDCHG_REQ, bcast.Id);
        Assert.Equal((ushort)5, bcast.ReadUInt16());
        Assert.Equal(10u, bcast.ReadUInt32());
        Assert.Equal("Def", bcast.ReadString());
        Assert.Equal(20u, bcast.ReadUInt32());
        Assert.Equal("Atk", bcast.ReadString());

        var ack = ctrl.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.CT_CASTLEGUILDCHG_ACK, ack.Id);
        Assert.Equal(999u, ack.ReadUInt32());   // managerId
        Assert.True(ack.ReadBool());            // success
    }

    [Fact]
    public async Task CastleGuildChg_MissingGuild_AcksFailure()
    {
        await using var host = new WorldTestHost(s => s.Guilds[10] = new Guild { Id = 10, Name = "Def" });
        using var ctrl = await host.ConnectAsync();

        var req = new PacketWriter(Msg.CT_CASTLEGUILDCHG_REQ);
        req.WriteUInt16(5); req.WriteUInt32(10); req.WriteUInt32(77 /* missing */); req.WriteUInt32(999); req.WriteInt64(0);
        ctrl.Send(req);

        var ack = ctrl.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.CT_CASTLEGUILDCHG_ACK, ack.Id);
        Assert.Equal(999u, ack.ReadUInt32());
        Assert.False(ack.ReadBool());
    }

    [Fact]
    public async Task ChatBan_SetsBan_RelaysToMap_AndAcks()
    {
        await using var host = new WorldTestHost();
        using var map = await host.ConnectAsync();
        Connect(map);
        EnterChar(map, 201, 0x201);
        NameChar(host, 201, "Banned");
        using var ctrl = await host.ConnectAsync();

        var req = new PacketWriter(Msg.CT_CHATBAN_REQ);
        req.WriteString("Banned"); req.WriteUInt16(10); req.WriteUInt32(55); req.WriteUInt32(999);
        ctrl.Send(req);

        var relay = map.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.MW_CHATBAN_REQ, relay.Id);
        Assert.Equal("Banned", relay.ReadString());

        var ack = ctrl.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.CT_CHATBAN_ACK, ack.Id);
        Assert.True(ack.ReadBool());
        Assert.Equal(55u, ack.ReadUInt32());
        Assert.Equal(999u, ack.ReadUInt32());

        Assert.True(host.State.Characters[201].ChatBanTime > 0);
        Assert.True(host.State.ChatBans.ContainsKey("Banned"));
    }

    [Fact]
    public async Task ChatBan_UnknownChar_AcksFailureWithoutBan()
    {
        await using var host = new WorldTestHost();
        using var ctrl = await host.ConnectAsync();

        var req = new PacketWriter(Msg.CT_CHATBAN_REQ);
        req.WriteString("Ghost"); req.WriteUInt16(10); req.WriteUInt32(55); req.WriteUInt32(999);
        ctrl.Send(req);

        var ack = ctrl.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.CT_CHATBAN_ACK, ack.Id);
        Assert.False(ack.ReadBool());
        Assert.False(host.State.ChatBans.ContainsKey("Ghost"));
    }

    [Fact]
    public async Task EventMsg_BroadcastsToMaps()
    {
        await using var host = new WorldTestHost();
        using var map = await host.ConnectAsync();
        Connect(map);
        using var ctrl = await host.ConnectAsync();

        var req = new PacketWriter(Msg.CT_EVENTMSG_REQ);
        req.WriteByte(3); req.WriteByte(1); req.WriteString("Server restart soon");
        ctrl.Send(req);

        var bcast = map.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.MW_EVENTMSG_REQ, bcast.Id);
        Assert.Equal((byte)3, bcast.ReadByte());
        Assert.Equal((byte)1, bcast.ReadByte());
        Assert.Equal("Server restart soon", bcast.ReadString());
    }

    [Fact]
    public async Task ServiceDataClear_RebuildsActiveUsersFromChars()
    {
        await using var host = new WorldTestHost();
        using var map = await host.ConnectAsync();
        Connect(map);
        EnterChar(map, 202, 0x202);   // userId 1202
        EnterChar(map, 203, 0x203);   // userId 1203
        host.State.ActiveUsers.Clear();
        using var ctrl = await host.ConnectAsync();

        ctrl.Send(new PacketWriter(Msg.CT_SERVICEDATACLEAR_ACK));

        await Task.Delay(60);
        Assert.Contains(1202u, host.State.ActiveUsers);
        Assert.Contains(1203u, host.State.ActiveUsers);
    }

    [Fact]
    public async Task CashItemSale_RecordsCatalog_ResetsFlags_AndBroadcasts()
    {
        await using var host = new WorldTestHost();
        using var map = await host.ConnectAsync();
        Connect(map);
        await Task.Delay(40);
        host.State.Servers.Values.First().CashSale = true;   // pretend it confirmed an earlier sale
        using var ctrl = await host.ConnectAsync();

        var req = new PacketWriter(Msg.CT_CASHITEMSALE_REQ);
        req.WriteUInt32(42);      // index
        req.WriteUInt16(1);       // value (active)
        req.WriteUInt16(2);       // count
        req.WriteUInt16(1001); req.WriteByte(50);
        req.WriteUInt16(1002); req.WriteByte(30);
        ctrl.Send(req);

        var bcast = map.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.MW_CASHITEMSALE_REQ, bcast.Id);
        Assert.Equal(42u, bcast.ReadUInt32());
        Assert.Equal((ushort)1, bcast.ReadUInt16());
        Assert.Equal((ushort)2, bcast.ReadUInt16());
        Assert.Equal((ushort)1001, bcast.ReadUInt16());
        Assert.Equal((byte)50, bcast.ReadByte());
        Assert.Equal((ushort)1002, bcast.ReadUInt16());
        Assert.Equal((byte)30, bcast.ReadByte());

        Assert.True(host.State.CashItemSales.ContainsKey(42));
        Assert.Equal(2, host.State.CashItemSales[42].Items.Count);
        Assert.False(host.State.Servers.Values.First().CashSale);   // flag was reset for re-confirmation
    }

    [Fact]
    public async Task CmGiftList_SendsCatalogToControl()
    {
        await using var host = new WorldTestHost(s =>
            s.CmGifts[7] = new CmGift { GiftId = 7, GiftType = 2, Value = 100, Count = 1, Title = "Gift", Msg = "Enjoy" });
        using var ctrl = await host.ConnectAsync();
        ctrl.Send(new PacketWriter(Msg.CT_CTRLSVR_REQ));   // register as the control server
        await Task.Delay(40);

        var req = new PacketWriter(Msg.CT_CMGIFTLIST_REQ);
        req.WriteUInt32(999);
        ctrl.Send(req);

        var ack = ctrl.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.CT_CMGIFTLIST_ACK, ack.Id);
        Assert.Equal(999u, ack.ReadUInt32());
        Assert.Equal((ushort)1, ack.ReadUInt16());
        Assert.Equal((ushort)7, ack.ReadUInt16());   // giftId
        Assert.Equal((byte)2, ack.ReadByte());        // type
        Assert.Equal(100u, ack.ReadUInt32());         // value
    }
}
