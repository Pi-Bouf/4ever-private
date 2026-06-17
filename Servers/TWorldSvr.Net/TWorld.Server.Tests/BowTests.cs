using TWorld.Protocol;
using TWorld.Server.World;
using Xunit;

namespace TWorld.Server.Tests;

/// <summary>Phase-4 Battle-of-the-Warlords tests: queue add/cancel through the real handlers, and the
/// alarm→peace→battle phase machine driven via the test tick seam. No DB.</summary>
public class BowTests
{
    private static void Connect(TcpTestClient client, byte serverId)
    {
        ushort wid = (ushort)((4 << 8) | serverId);
        var c = new PacketWriter(Msg.MW_CONNECT_ACK);
        c.WriteUInt16(wid); c.WriteByte(1); c.WriteByte(0);
        client.Send(c);
    }

    private static void EnterChar(TcpTestClient client, uint charId, uint key, byte country)
    {
        var add = new PacketWriter(Msg.MW_ADDCHAR_ACK);
        add.WriteUInt32(charId); add.WriteUInt32(key); add.WriteUInt32(0x0100007F); add.WriteUInt16(5816); add.WriteUInt32(charId + 1000);
        client.Send(add);
        Assert.Equal(Msg.MW_ENTERSVR_REQ, client.Receive(TimeSpan.FromSeconds(5)).Id);

        var cd = new PacketWriter(Msg.MW_CHARDATA_ACK);
        cd.WriteUInt32(charId); cd.WriteUInt32(key); cd.WriteByte(0); cd.WriteByte(20);
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

    private static BowState NewBow() => new(Proto.BowMapId, minPlayersCount: 1, maxNationDifference: 2)
    {
        AlarmDur = 2, BuyTimeDur = 2, BattleDur = 2,
    };

    [Fact]
    public async Task Queue_Add_Then_Cancel()
    {
        const uint charId = 9001, key = 0xF00D;
        await using var host = new WorldTestHost(s =>
        {
            var bow = NewBow();
            bow.Status = BattleStatus.Alarm;   // queue is open during the alarm window
            bow.Times.Add(100);
            s.Bow = bow;
        });
        using var c = await host.ConnectAsync();
        Connect(c, 1);
        EnterChar(c, charId, key, country: 0);

        var add = new PacketWriter(Msg.MW_ADDTOBOWQUEUE_REQ);
        add.WriteUInt32(charId); add.WriteUInt32(key);
        c.Send(add);

        var ack = c.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.MW_ADDTOBOWQUEUE_ACK, ack.Id);
        Assert.Equal((byte)BowReg.Success, ack.ReadByte());
        Assert.Equal(charId, ack.ReadUInt32());

        await Task.Delay(80);
        Assert.True(host.State.Bow!.Reg.ContainsKey(charId));

        var cancel = new PacketWriter(Msg.MW_CANCELBOWQUEUE_REQ);
        cancel.WriteUInt32(charId); cancel.WriteUInt32(key);
        c.Send(cancel);

        var cack = c.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.MW_CANCELBOWQUEUE_ACK, cack.Id);
        Assert.Equal((byte)BowReg.Success, cack.ReadByte());

        await Task.Delay(80);
        Assert.False(host.State.Bow!.Reg.ContainsKey(charId));
    }

    [Fact]
    public async Task PhaseMachine_Alarm_Peace_Battle_StartsBattle()
    {
        await using var host = new WorldTestHost(s =>
        {
            var bow = NewBow();
            bow.Times.Add(100);
            bow.Start = 100;
            s.Bow = bow;
        });
        using var bowServer = await host.ConnectAsync();
        Connect(bowServer, Proto.BowServerId);     // server id 30 -> registers as the BoW battle host
        await Task.Delay(100);
        Assert.NotNull(host.State.Bow!.BowServer);

        // Drive the schedule: alarm (101-102), peace (103-104), battle (105-106).
        foreach (uint t in new uint[] { 101, 102, 103, 104, 105, 106 })
            await host.Service.BowTickAsync(t);

        // Collect what the BoW server received and confirm the battle was commanded to start.
        bool sawBattleStatus = false, sawStart = false;
        for (int i = 0; i < 40; i++)
        {
            PacketReader p;
            try { p = bowServer.Receive(TimeSpan.FromMilliseconds(300)); }
            catch { break; }
            if (p.Id == Msg.MW_BOWTIMEUPDATE_ACK && p.ReadByte() == (byte)BattleStatus.Battle) sawBattleStatus = true;
            else if (p.Id == Msg.MW_BOWCOMMANDEXEC_REQ && p.ReadByte() == (byte)BowCommand.Start) sawStart = true;
        }

        Assert.True(sawBattleStatus, "expected a BOWTIMEUPDATE_ACK with Battle status");
        Assert.True(sawStart, "expected a BOWCOMMANDEXEC_REQ(Start)");
        Assert.Equal(BattleStatus.Battle, host.State.Bow!.Status);
    }
}
