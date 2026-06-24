using TWorld.Protocol;
using TWorld.Server.World;
using Xunit;

namespace TWorld.Server.Tests;

/// <summary>Phase 5e — PvP scoring. GAINPVPPOINT forwards a character's gain or accrues/spends guild points;
/// LOCALRECORD appends per-member kill/die/point records and refreshes the week total. No DB.</summary>
public class PvpTests
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
    public async Task GainPvpPoint_CharOwner_ForwardedToMain()
    {
        const uint charId = 80, key = 0xD1;
        await using var host = new WorldTestHost();
        using var client = await host.ConnectAsync();
        Connect(client);
        EnterChar(client, charId, key);

        var g = new PacketWriter(Msg.MW_GAINPVPPOINT_ACK);
        g.WriteByte(Proto.TownerChar); g.WriteUInt32(charId); g.WriteUInt32(50); g.WriteByte(1); g.WriteByte(3); g.WriteByte(1);
        g.WriteString("Hero"); g.WriteByte(2); g.WriteByte(30);
        client.Send(g);

        var req = client.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.MW_GAINPVPPOINT_REQ, req.Id);
        Assert.Equal(charId, req.ReadUInt32());
        Assert.Equal(50u, req.ReadUInt32());
        Assert.Equal((byte)1, req.ReadByte());   // event
        Assert.Equal((byte)3, req.ReadByte());   // type
        Assert.Equal((byte)1, req.ReadByte());   // gain
        Assert.Equal("Hero", req.ReadString());
        Assert.Equal((byte)2, req.ReadByte());   // class
        Assert.Equal((byte)30, req.ReadByte());  // level
    }

    [Fact]
    public async Task GainPvpPoint_GuildOwner_AccruesAndSpends()
    {
        await using var host = new WorldTestHost();
        using var client = await host.ConnectAsync();
        Connect(client);
        host.State.Guilds[5] = new Guild { Id = 5, Name = "Knights" };

        // Gain 100 (TOTAL|USEABLE) -> total/month/useable all 100.
        var gain = new PacketWriter(Msg.MW_GAINPVPPOINT_ACK);
        gain.WriteByte(Proto.TownerGuild); gain.WriteUInt32(5); gain.WriteUInt32(100); gain.WriteByte(0);
        gain.WriteByte((byte)(Proto.PvpTotal | Proto.PvpUseable)); gain.WriteByte(1); gain.WriteString(""); gain.WriteByte(0); gain.WriteByte(0);
        client.Send(gain);
        await Task.Delay(60);
        var g = host.State.Guilds[5];
        Assert.Equal(100u, g.PvPTotalPoint);
        Assert.Equal(100u, g.PvPMonthPoint);
        Assert.Equal(100u, g.PvPUseablePoint);

        // Spend 30 useable (gain=0, type=USEABLE) -> useable 70, total unchanged.
        var use = new PacketWriter(Msg.MW_GAINPVPPOINT_ACK);
        use.WriteByte(Proto.TownerGuild); use.WriteUInt32(5); use.WriteUInt32(30); use.WriteByte(0);
        use.WriteByte(Proto.PvpUseable); use.WriteByte(0); use.WriteString(""); use.WriteByte(0); use.WriteByte(0);
        client.Send(use);
        await Task.Delay(60);
        Assert.Equal(70u, g.PvPUseablePoint);
        Assert.Equal(100u, g.PvPTotalPoint);
    }

    [Fact]
    public async Task GainPvpPoint_GuildOwner_RecomputesRanking()
    {
        await using var host = new WorldTestHost();
        using var client = await host.ConnectAsync();
        Connect(client);
        host.State.Guilds[5] = new Guild { Id = 5, Name = "A", PvPTotalPoint = 500, PvPMonthPoint = 500 };
        host.State.Guilds[6] = new Guild { Id = 6, Name = "B", PvPTotalPoint = 50, PvPMonthPoint = 50 };

        // Guild 6 gains 1000 -> it overtakes guild 5; ranks: 6 is #1, 5 is #2.
        var gain = new PacketWriter(Msg.MW_GAINPVPPOINT_ACK);
        gain.WriteByte(Proto.TownerGuild); gain.WriteUInt32(6); gain.WriteUInt32(1000); gain.WriteByte(0);
        gain.WriteByte((byte)(Proto.PvpTotal | Proto.PvpUseable)); gain.WriteByte(1); gain.WriteString(""); gain.WriteByte(0); gain.WriteByte(0);
        client.Send(gain);
        await Task.Delay(60);

        Assert.Equal(1u, host.State.Guilds[6].RankTotal);   // 1050 pts -> #1
        Assert.Equal(2u, host.State.Guilds[5].RankTotal);   // 500 pts  -> #2
        Assert.Equal(1u, host.State.Guilds[6].RankMonth);
        Assert.Equal(2u, host.State.Guilds[5].RankMonth);
    }

    [Fact]
    public async Task LocalRecord_AppendsMemberRecord_SkipsEntryPointWhenOffline()
    {
        await using var host = new WorldTestHost();
        using var client = await host.ConnectAsync();
        Connect(client);

        var guild = new Guild { Id = 5, Name = "Knights" };
        var member = new GuildMember { CharId = 70 };   // offline (not in _state.Characters)
        guild.Members[70] = member;
        host.State.Guilds[5] = guild;

        var lr = new PacketWriter(Msg.MW_LOCALRECORD_ACK);
        lr.WriteUInt32(0);          // winGuildId
        lr.WriteUInt32(0);          // guildPoint
        lr.WriteUInt16(1);          // one guild
        lr.WriteUInt32(5);          // guildId
        lr.WriteUInt16(1);          // one record
        lr.WriteUInt32(70);         // charId
        lr.WriteUInt16(3);          // kill
        lr.WriteUInt16(1);          // die
        for (uint e = 0; e < Proto.PvpeCount; e++) lr.WriteUInt32(10 + e);   // points 10..17 (index 5 = PVPE_ENTRY = 15)
        client.Send(lr);

        await Task.Delay(80);
        Assert.Single(member.Records);
        var rec = member.Records[0];
        Assert.Equal((ushort)3, rec.KillCount);
        Assert.Equal((ushort)1, rec.DieCount);
        Assert.Equal(10u, rec.Point[0]);
        Assert.Equal(0u, rec.Point[Proto.PvpeEntry]);     // ENTRY point skipped — char is offline
        Assert.Equal(17u, rec.Point[7]);
        Assert.Equal((ushort)3, member.WeekRecord.KillCount);
    }
}
