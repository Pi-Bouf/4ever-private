using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TControl.Protocol;
using TControl.Server.Ops;
using Xunit;

namespace TControl.Server.Tests;

/// <summary>
/// End-to-end over loopback: the control server connects out to a fake world (registers via
/// CT_CTRLSVR_REQ), a fake GM manager logs in, and a relayed CT_CHATBAN round-trips
/// manager → control → world → control → manager. Exercises the connector, registration, operator login,
/// the relay path, and manager-id ACK routing with no database.
/// </summary>
public class EndToEndTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Chatban_round_trips_manager_to_world_and_back()
    {
        int worldPort = FreePort();
        int ctrlPort = FreePort();

        using var worldRegistered = new ManualResetEventSlim(false);
        using var worldCts = new CancellationTokenSource();
        var world = RunFakeWorldAsync(worldPort, worldRegistered, worldCts.Token);

        var opt = Options.Create(new ControlServerOptions
        {
            Port = ctrlPort,
            ConnectIntervalSeconds = 1,
            Servers = { new ConfiguredServer { Host = "127.0.0.1", Port = worldPort, Group = 1, Type = Proto.SVRGRP_WORLDSVR, ServerId = 1, Name = "WORLD" } },
        });

        var worker = new ControlWorker(opt, NullLoggerFactory.Instance,
            new NoOpServiceController(NullLogger<NoOpServiceController>.Instance),
            new NoOpPlatformMonitor(),
            new NoOpFileDeploy(NullLogger<NoOpFileDeploy>.Instance));

        await worker.StartAsync(CancellationToken.None);
        try
        {
            using var mgr = ConnectWithRetry(ctrlPort);

            // Operator login (DB-less => authority ALL); expect ret == 0.
            mgr.Send(new PacketWriter(Msg.CT_OPLOGIN_REQ).WriteString("op").WriteString("pw"));
            var loginAck = new PacketReader(mgr.ReceiveUntil(Msg.CT_OPLOGIN_ACK, Timeout));
            _ = loginAck.Id;
            Assert.Equal(0, loginAck.ReadByte()); // ret

            // Wait for the control server to connect + register with the fake world.
            Assert.True(worldRegistered.Wait(Timeout), "control server did not register with the world");

            // Relay a chat-ban; expect the aggregated success ACK back on the manager.
            mgr.Send(new PacketWriter(Msg.CT_CHATBAN_REQ).WriteString("cheater").WriteUInt16(60).WriteString("spam"));
            var banAck = new PacketReader(mgr.ReceiveUntil(Msg.CT_CHATBAN_ACK, Timeout));
            _ = banAck.Id;
            Assert.Equal(1, banAck.ReadByte()); // success
        }
        finally
        {
            worldCts.Cancel();
            await worker.StopAsync(CancellationToken.None);
            try { await world; } catch { }
        }
    }

    private static WireTestClient ConnectWithRetry(int port)
    {
        for (int i = 0; ; i++)
        {
            try { return new WireTestClient("127.0.0.1", port); }
            catch (SocketException) when (i < 50) { Thread.Sleep(100); }
        }
    }

    /// <summary>Accepts one control connection, acks the registration, and replies to CT_CHATBAN_REQ.</summary>
    private static async Task RunFakeWorldAsync(int port, ManualResetEventSlim registered, CancellationToken token)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        try
        {
            using var socket = await listener.AcceptSocketAsync(token);
            var framer = new PacketFramer();
            var buffer = new byte[8192];
            while (!token.IsCancellationRequested)
            {
                int n = await socket.ReceiveAsync(buffer, SocketFlags.None, token);
                if (n == 0) break;
                framer.Append(buffer.AsSpan(0, n));
                while (framer.TryReadPacket(out var packet))
                {
                    var r = new PacketReader(packet);
                    switch (r.Id)
                    {
                        case Msg.CT_CTRLSVR_REQ:
                            registered.Set();
                            break;
                        case Msg.CT_CHATBAN_REQ:
                            _ = r.ReadString();               // name
                            _ = r.ReadUInt16();               // minutes
                            uint banSeq = r.ReadUInt32();
                            uint managerId = r.ReadUInt32();
                            var ack = new PacketWriter(Msg.CT_CHATBAN_ACK)
                                .WriteByte(1).WriteUInt32(banSeq).WriteUInt32(managerId).ToArray();
                            await socket.SendAsync(ack, SocketFlags.None, token);
                            break;
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException) { }
        finally { listener.Stop(); }
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }
}
