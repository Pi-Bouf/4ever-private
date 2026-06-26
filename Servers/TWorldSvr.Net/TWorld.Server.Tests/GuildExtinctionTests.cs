using TWorld.Protocol;
using TWorld.Server.World;
using Xunit;

namespace TWorld.Server.Tests;

/// <summary>
/// Guild auto-extinction timer (C++ CheckTGuildExtinction) + the SM_GUILDDISORGANIZATION cross-instance
/// disband-state sync. No DB; the guild is removed in-memory and the DB delete is a best-effort no-op.
/// </summary>
public class GuildExtinctionTests
{
    private static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private static Guild Disbanding(uint id, long disbandStart)
    {
        var g = new Guild { Id = id, Name = "G", Chief = 1, Disorg = 1, Time = (uint)disbandStart };
        g.Members[1] = new GuildMember { CharId = 1, Name = "Chief" };
        return g;
    }

    [Fact]
    public async Task Extinction_DisbandsGuildPastGrace()
    {
        await using var host = new WorldTestHost(s =>
        {
            var g = Disbanding(500, Now - 86400L * 8); // 8 days ago > 7-day grace
            s.Guilds[500] = g; s.CharGuild[1] = 500;
        });

        for (int i = 0; i < 60; i++) await host.Service.CheckGuildExtinctionAsync(); // pass the throttle

        Assert.False(host.State.Guilds.ContainsKey(500));
        Assert.False(host.State.CharGuild.ContainsKey(1));
    }

    [Fact]
    public async Task Extinction_KeepsGuildWithinGrace()
    {
        await using var host = new WorldTestHost(s =>
        {
            var g = Disbanding(501, Now - 86400L); // 1 day ago < grace
            s.Guilds[501] = g; s.CharGuild[1] = 501;
        });

        for (int i = 0; i < 60; i++) await host.Service.CheckGuildExtinctionAsync();

        Assert.True(host.State.Guilds.ContainsKey(501));
    }

    [Fact]
    public async Task SmGuildDisorg_SetsAndClearsDisbandState()
    {
        await using var host = new WorldTestHost(s =>
            s.Guilds[502] = new Guild { Id = 502, Name = "G", Chief = 1 });
        using var peer = await host.ConnectAsync();

        var set = new PacketWriter(Msg.SM_GUILDDISORGANIZATION_REQ);
        set.WriteUInt32(502); set.WriteUInt32(12345); set.WriteByte(1);
        peer.Send(set);
        await Task.Delay(60);
        Assert.Equal((byte)1, host.State.Guilds[502].Disorg);
        Assert.Equal(12345u, host.State.Guilds[502].Time);

        var clear = new PacketWriter(Msg.SM_GUILDDISORGANIZATION_REQ);
        clear.WriteUInt32(502); clear.WriteUInt32(0); clear.WriteByte(0);
        peer.Send(clear);
        await Task.Delay(60);
        Assert.Equal((byte)0, host.State.Guilds[502].Disorg);
        Assert.Equal(0u, host.State.Guilds[502].Time);
    }
}
