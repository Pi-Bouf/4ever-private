using TWorld.Protocol;
using Xunit;

namespace TWorld.Server.Tests;

/// <summary>
/// The previously-deferred GM tournament-event internals: the per-event schedule machinery
/// (TET_SCHEDULEADD/DEL + the TET_LIST schedule block + earliest-schedule activation) and the TET_PLAYERADD
/// char lookup (DB-free failure path). A future Nth-weekday window always resolves to next month, so
/// SetTournamentTime yields a future start without depending on today's date.
/// </summary>
public class TournamentEventScheduleTests
{
    private static void RegisterControl(TcpTestClient c) => c.Send(new PacketWriter(Msg.CT_CTRLSVR_REQ));

    private static void ScheduleAdd(TcpTestClient ctrl, ushort tid, byte week, byte day, uint battleStart)
    {
        var w = new PacketWriter(Msg.CT_TOURNAMENTEVENT_REQ);
        w.WriteUInt32(1); w.WriteByte((byte)TournamentEventCmd.ScheduleAdd);
        w.WriteUInt16(tid); w.WriteByte(week); w.WriteByte(day); w.WriteUInt32(battleStart);
        w.WriteByte(1);                 // one step
        w.WriteByte(0); w.WriteUInt32(600); // step 0, period 600s
        ctrl.Send(w);
    }

    private static void EntryAdd(TcpTestClient ctrl, ushort tid, byte entryId, string name)
    {
        var w = new PacketWriter(Msg.CT_TOURNAMENTEVENT_REQ);
        w.WriteUInt32(1); w.WriteByte((byte)TournamentEventCmd.EntryAdd);
        w.WriteUInt16(tid); w.WriteByte(1);
        w.WriteByte(entryId); w.WriteString(name); w.WriteByte(0); w.WriteUInt32(0xFF);
        w.WriteUInt32(0); w.WriteUInt32(0); w.WriteUInt16(0); w.WriteByte(0);
        w.WriteByte(1); w.WriteByte(99);
        w.WriteByte(0); // no rewards
        ctrl.Send(w);
    }

    [Fact]
    public async Task ScheduleAdd_ComputesFutureTimesAndStores()
    {
        await using var host = new WorldTestHost();
        using var ctrl = await host.ConnectAsync();
        RegisterControl(ctrl);
        await Task.Delay(40);

        ScheduleAdd(ctrl, 5, week: 1, day: 1, battleStart: 3600);

        var ack = ctrl.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.CT_TOURNAMENTEVENT_ACK, ack.Id);
        Assert.Equal(1u, ack.ReadUInt32());
        Assert.Equal((byte)TournamentEventCmd.ScheduleAdd, ack.ReadByte());
        Assert.Equal((ushort)5, ack.ReadUInt16());   // registered id

        Assert.True(host.State.EventSchedules.ContainsKey(5));
        var sched = host.State.EventSchedules[5];
        Assert.Single(sched.Steps);
        Assert.True(sched.Steps[0].Start > DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        Assert.Equal(sched.Steps[0].Start + 600, sched.Steps[0].End);
    }

    [Fact]
    public async Task List_IncludesScheduleBlock()
    {
        await using var host = new WorldTestHost();
        using var ctrl = await host.ConnectAsync();
        RegisterControl(ctrl);
        await Task.Delay(40);
        ScheduleAdd(ctrl, 5, 1, 1, 3600);
        ctrl.Receive(TimeSpan.FromSeconds(5)); // schedule-add ack

        var w = new PacketWriter(Msg.CT_TOURNAMENTEVENT_REQ);
        w.WriteUInt32(8); w.WriteByte((byte)TournamentEventCmd.List);
        ctrl.Send(w);

        var ack = ctrl.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.CT_TOURNAMENTEVENT_ACK, ack.Id);
        Assert.Equal(8u, ack.ReadUInt32());
        Assert.Equal((byte)TournamentEventCmd.List, ack.ReadByte());
        Assert.Equal((byte)1, ack.ReadByte());        // schedule count
        Assert.Equal((ushort)5, ack.ReadUInt16());    // schedule id
        Assert.Equal((byte)1, ack.ReadByte());        // week
        Assert.Equal((byte)1, ack.ReadByte());        // day
        Assert.Equal(3600u, ack.ReadUInt32());        // battleStart
        Assert.Equal((byte)1, ack.ReadByte());        // step count
        Assert.Equal((byte)0, ack.ReadByte());        // step id
        Assert.Equal(600u, ack.ReadUInt32());         // period
    }

    [Fact]
    public async Task ScheduleDel_RemovesSchedule()
    {
        await using var host = new WorldTestHost();
        using var ctrl = await host.ConnectAsync();
        RegisterControl(ctrl);
        await Task.Delay(40);
        ScheduleAdd(ctrl, 5, 1, 1, 3600);
        ctrl.Receive(TimeSpan.FromSeconds(5));

        var w = new PacketWriter(Msg.CT_TOURNAMENTEVENT_REQ);
        w.WriteUInt32(1); w.WriteByte((byte)TournamentEventCmd.ScheduleDel); w.WriteUInt16(5);
        ctrl.Send(w);
        ctrl.Receive(TimeSpan.FromSeconds(5)); // del ack
        await Task.Delay(40);

        Assert.False(host.State.EventSchedules.ContainsKey(5));
    }

    [Fact]
    public async Task ScheduleAdd_ActivatesEarliestAsRunningTournament()
    {
        await using var host = new WorldTestHost();
        using var ctrl = await host.ConnectAsync();
        RegisterControl(ctrl);
        await Task.Delay(40);

        EntryAdd(ctrl, 5, 2, "Alpha");
        ScheduleAdd(ctrl, 5, 1, 1, 3600);
        ctrl.Receive(TimeSpan.FromSeconds(5)); // schedule-add ack
        await Task.Delay(40);

        Assert.NotNull(host.State.Tournament);
        Assert.Equal((ushort)5, host.State.Tournament!.Id);
        Assert.Single(host.State.Tournament.Steps);
        Assert.True(host.State.Tournament.Entries.ContainsKey(2));
        Assert.True(host.State.Tournament.ScheduleActive);
    }

    [Fact]
    public async Task PlayerAdd_NoDb_RepliesFailure()
    {
        await using var host = new WorldTestHost();
        using var ctrl = await host.ConnectAsync();
        RegisterControl(ctrl);
        await Task.Delay(40);

        EntryAdd(ctrl, 5, 2, "Alpha");
        ScheduleAdd(ctrl, 5, 1, 1, 3600);
        ctrl.Receive(TimeSpan.FromSeconds(5)); // schedule-add ack
        await Task.Delay(40);

        var w = new PacketWriter(Msg.CT_TOURNAMENTEVENT_REQ);
        w.WriteUInt32(3); w.WriteByte((byte)TournamentEventCmd.PlayerAdd);
        w.WriteUInt16(5); w.WriteByte(2); w.WriteString("Hero");
        ctrl.Send(w);

        var ack = ctrl.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.CT_TOURNAMENTEVENT_ACK, ack.Id);
        Assert.Equal(3u, ack.ReadUInt32());
        Assert.Equal((byte)TournamentEventCmd.PlayerAdd, ack.ReadByte());
        Assert.Equal((byte)2, ack.ReadByte());     // entryId
        Assert.Equal(0u, ack.ReadUInt32());        // charId 0 -> failure (no DB)
        Assert.Equal("", ack.ReadString());
        Assert.Equal((byte)0, ack.ReadByte());     // level
        Assert.Equal((byte)6, ack.ReadByte());     // TCLASS_COUNT
        Assert.Equal((byte)3, ack.ReadByte());     // TCONTRY_N
    }
}
