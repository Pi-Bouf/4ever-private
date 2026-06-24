using TWorld.Protocol;
using TWorld.Server.World;
using Xunit;

namespace TWorld.Server.Tests;

/// <summary>Phase 5i — TMS multi-person private chat: invite forms a conversation, sends fan to all members,
/// and leaving notifies + removes. No DB.</summary>
public class TmsTests
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

    /// <summary>Enter A+B (named), then A invites B — forms a conversation and drains the two INVITE packets.</summary>
    private static (uint tmsId, TcpTestClient client) SetupConversation(WorldTestHost host, TcpTestClient client,
        uint a, uint ka, uint b, uint kb)
    {
        EnterChar(client, a, ka);
        EnterChar(client, b, kb);
        host.State.Characters[a].Name = "Alice";
        host.State.Characters[b].Name = "Bob";

        var inv = new PacketWriter(Msg.MW_TMSINVITE_ACK);
        inv.WriteUInt32(a); inv.WriteUInt32(ka); inv.WriteUInt32(0); inv.WriteByte(1); inv.WriteUInt32(b);
        client.Send(inv);

        Assert.Equal(Msg.MW_TMSINVITE_REQ, client.Receive(TimeSpan.FromSeconds(5)).Id);
        Assert.Equal(Msg.MW_TMSINVITE_REQ, client.Receive(TimeSpan.FromSeconds(5)).Id);
        uint tmsId = host.State.TmsMap.Keys.Single();
        return (tmsId, client);
    }

    [Fact]
    public async Task Invite_CreatesConversation_WithBothMembers()
    {
        await using var host = new WorldTestHost();
        using var client = await host.ConnectAsync();
        Connect(client);
        var (tmsId, _) = SetupConversation(host, client, 60, 0x60, 61, 0x61);

        var tms = host.State.TmsMap[tmsId];
        Assert.Equal(2, tms.Members.Count);
        Assert.Contains(60u, tms.Members.Keys);
        Assert.Contains(61u, tms.Members.Keys);
        Assert.Contains(tmsId, host.State.Characters[60].TmsIds);
        Assert.Contains(tmsId, host.State.Characters[61].TmsIds);
    }

    [Fact]
    public async Task Send_NoReceiver_UsesServerMessageText()
    {
        await using var host = new WorldTestHost(s => s.ServerMessages[(uint)SvrMsg.TmsNoReceiver] = "No one is online");
        using var client = await host.ConnectAsync();
        Connect(client);
        EnterChar(client, 68, 0x68);
        host.State.Characters[68].Name = "Alice";

        // A 1-member conversation whose pending peer "Ghost" is offline.
        var tms = new TWorld.Server.World.Tms { Id = 0x900, LastMember = "Ghost" };
        tms.Members[68] = host.State.Characters[68];
        host.State.TmsMap[tms.Id] = tms;
        host.State.Characters[68].TmsIds.Add(tms.Id);

        var send = new PacketWriter(Msg.MW_TMSSEND_ACK);
        send.WriteUInt32(68); send.WriteUInt32(0x68); send.WriteUInt32(tms.Id); send.WriteString("anyone?");
        client.Send(send);

        var recv = client.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.MW_TMSRECV_REQ, recv.Id);
        recv.ReadUInt32(); recv.ReadUInt32();   // recipient
        Assert.Equal(tms.Id, recv.ReadUInt32());
        recv.ReadString();                       // sender
        Assert.Contains("No one is online", recv.ReadString());   // server-message text is embedded
    }

    [Fact]
    public async Task Send_DeliversToAllMembers()
    {
        await using var host = new WorldTestHost();
        using var client = await host.ConnectAsync();
        Connect(client);
        var (tmsId, _) = SetupConversation(host, client, 62, 0x62, 63, 0x63);

        var send = new PacketWriter(Msg.MW_TMSSEND_ACK);
        send.WriteUInt32(62); send.WriteUInt32(0x62); send.WriteUInt32(tmsId); send.WriteString("hi all");
        client.Send(send);

        // One TMSRECV per member (2), both carrying sender + message.
        for (int i = 0; i < 2; i++)
        {
            var recv = client.Receive(TimeSpan.FromSeconds(5));
            Assert.Equal(Msg.MW_TMSRECV_REQ, recv.Id);
            recv.ReadUInt32(); recv.ReadUInt32();         // recipient charId, key
            Assert.Equal(tmsId, recv.ReadUInt32());
            Assert.Equal("Alice", recv.ReadString());     // sender (char 62 = "Alice")
            Assert.Equal("hi all", recv.ReadString());
        }
    }

    [Fact]
    public async Task Out_NotifiesAndRemovesMember()
    {
        await using var host = new WorldTestHost();
        using var client = await host.ConnectAsync();
        Connect(client);
        var (tmsId, _) = SetupConversation(host, client, 64, 0x64, 65, 0x65);

        var outp = new PacketWriter(Msg.MW_TMSOUT_ACK);
        outp.WriteUInt32(64); outp.WriteUInt32(0x64); outp.WriteUInt32(tmsId);
        client.Send(outp);

        // TMSOUT_REQ to both members (the leaver included) before removal.
        Assert.Equal(Msg.MW_TMSOUT_REQ, client.Receive(TimeSpan.FromSeconds(5)).Id);
        Assert.Equal(Msg.MW_TMSOUT_REQ, client.Receive(TimeSpan.FromSeconds(5)).Id);

        await Task.Delay(40);
        var tms = host.State.TmsMap[tmsId];
        Assert.DoesNotContain(64u, tms.Members.Keys);
        Assert.Contains(65u, tms.Members.Keys);
        Assert.DoesNotContain(tmsId, host.State.Characters[64].TmsIds);
        Assert.Equal("Alice", tms.LastMember);   // char 64 = "Alice"
    }
}
