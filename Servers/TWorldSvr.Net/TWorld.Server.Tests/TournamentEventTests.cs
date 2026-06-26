using TWorld.Protocol;
using Xunit;

namespace TWorld.Server.Tests;

/// <summary>
/// GM event-tournament admin (CT_TOURNAMENTEVENT): entry add/del, the entry list serialization, and the
/// player-end ack. No DB. The schedule machinery + DB char lookup are deferred (see WorldService.TournamentEvent).
/// </summary>
public class TournamentEventTests
{
    private static void RegisterControl(TcpTestClient client) => client.Send(new PacketWriter(Msg.CT_CTRLSVR_REQ));

    private static void EntryAdd(TcpTestClient ctrl, ushort tid, byte entryId, string name)
    {
        var w = new PacketWriter(Msg.CT_TOURNAMENTEVENT_REQ);
        w.WriteUInt32(1); w.WriteByte((byte)TournamentEventCmd.EntryAdd);
        w.WriteUInt16(tid); w.WriteByte(1); // one entry
        w.WriteByte(entryId); w.WriteString(name); w.WriteByte(0); w.WriteUInt32(0xFF);
        w.WriteUInt32(100); w.WriteUInt32(10); w.WriteUInt16(0); w.WriteByte(0);
        w.WriteByte(1); w.WriteByte(99);
        w.WriteByte(1); // one reward
        w.WriteByte(0); w.WriteUInt16(500); w.WriteByte(1); w.WriteUInt32(0xFF); w.WriteByte(0);
        ctrl.Send(w);
    }

    [Fact]
    public async Task EntryAdd_StoresEntries()
    {
        await using var host = new WorldTestHost();
        using var ctrl = await host.ConnectAsync();
        RegisterControl(ctrl);
        await Task.Delay(40);

        EntryAdd(ctrl, 10, 2, "Alpha");
        await Task.Delay(60);

        Assert.True(host.State.EventTournaments.ContainsKey(10));
        Assert.True(host.State.EventTournaments[10].ContainsKey(2));
        Assert.Equal("Alpha", host.State.EventTournaments[10][2].Name);
        Assert.Single(host.State.EventTournaments[10][2].Rewards);
    }

    [Fact]
    public async Task List_ReturnsEntries()
    {
        await using var host = new WorldTestHost();
        using var ctrl = await host.ConnectAsync();
        RegisterControl(ctrl);
        await Task.Delay(40);
        EntryAdd(ctrl, 10, 2, "Alpha");
        await Task.Delay(60);

        var w = new PacketWriter(Msg.CT_TOURNAMENTEVENT_REQ);
        w.WriteUInt32(7); w.WriteByte((byte)TournamentEventCmd.List);
        ctrl.Send(w);

        var ack = ctrl.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.CT_TOURNAMENTEVENT_ACK, ack.Id);
        Assert.Equal(7u, ack.ReadUInt32());
        Assert.Equal((byte)TournamentEventCmd.List, ack.ReadByte());
        Assert.Equal((byte)0, ack.ReadByte());   // schedule count (deferred)
        Assert.Equal((byte)1, ack.ReadByte());   // tournament count
        Assert.Equal((ushort)10, ack.ReadUInt16());
        Assert.Equal((byte)1, ack.ReadByte());   // entry count
        Assert.Equal((byte)2, ack.ReadByte());   // entryId
        Assert.Equal("Alpha", ack.ReadString());
    }

    [Fact]
    public async Task EntryDel_RemovesEntry()
    {
        await using var host = new WorldTestHost();
        using var ctrl = await host.ConnectAsync();
        RegisterControl(ctrl);
        await Task.Delay(40);
        EntryAdd(ctrl, 10, 2, "Alpha");
        await Task.Delay(60);

        var w = new PacketWriter(Msg.CT_TOURNAMENTEVENT_REQ);
        w.WriteUInt32(1); w.WriteByte((byte)TournamentEventCmd.EntryDel);
        w.WriteUInt16(10); w.WriteByte(2);
        ctrl.Send(w);
        await Task.Delay(60);

        Assert.False(host.State.EventTournaments.ContainsKey(10)); // last entry gone -> tournament removed
    }

    [Fact]
    public async Task PlayerEnd_Acks()
    {
        await using var host = new WorldTestHost();
        using var ctrl = await host.ConnectAsync();
        RegisterControl(ctrl);
        await Task.Delay(40);

        var w = new PacketWriter(Msg.CT_TOURNAMENTEVENT_REQ);
        w.WriteUInt32(9); w.WriteByte((byte)TournamentEventCmd.PlayerEnd);
        ctrl.Send(w);

        var ack = ctrl.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.CT_TOURNAMENTEVENT_ACK, ack.Id);
        Assert.Equal(9u, ack.ReadUInt32());
        Assert.Equal((byte)TournamentEventCmd.PlayerEnd, ack.ReadByte());
    }
}
