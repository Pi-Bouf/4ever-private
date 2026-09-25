using Microsoft.Extensions.Configuration;
using TMap.Server;
using Xunit;

namespace TMap.Server.Tests;

/// <summary>
/// Regression tests for the <c>Map</c> configuration binding. These exist because the .NET configuration
/// binder <i>appends</i> bound array elements to whatever the target property already holds instead of
/// replacing them — so a non-empty C# default for <see cref="MapServerOptions.Channels"/> silently
/// doubled the channel list. That shipped: a live server announced <c>channels [1,1]</c> in
/// <c>MW_CONNECT_ACK</c> and built 36692 SE_DEFAULT monster spawn points where 18346 were correct,
/// putting twice the intended monsters on the map.
/// </summary>
public class MapServerOptionsTests
{
    /// <summary>Binds the <c>Map</c> section exactly as <c>Program.cs</c> does.</summary>
    private static MapServerOptions Bind(params (string Key, string Value)[] settings)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s =>
                new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

        var opt = new MapServerOptions();
        config.GetSection("Map").Bind(opt);
        opt.Normalize();
        return opt;
    }

    [Fact]
    public void Channels_IsSingle_WhenAppsettingsDeclaresOne()
    {
        // The shape shipped in appsettings.json: "Channels": [ 1 ].
        var opt = Bind(("Map:Channels:0", "1"));
        Assert.Equal(new byte[] { 1 }, opt.Channels);
    }

    [Fact]
    public void Channels_IsSingle_WhenEnvOverridesTheSameAppsettingsKey()
    {
        // The shape the compose stack produces: appsettings.json declares "Channels": [ 1 ] and the
        // Map__Channels__0 env var overrides that same key. Two layered providers, one resulting element —
        // the env layer replaces the JSON value rather than adding a second channel.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>("Map:Channels:0", "1")])   // appsettings.json
            .AddInMemoryCollection([new KeyValuePair<string, string?>("Map:Channels:0", "1")])   // Map__Channels__0
            .Build();

        var opt = new MapServerOptions();
        config.GetSection("Map").Bind(opt);
        opt.Normalize();

        Assert.Equal(new byte[] { 1 }, opt.Channels);
    }

    [Fact]
    public void Channels_FallsBackToTheCppDefault_WhenUnconfigured()
    {
        var opt = Bind();
        Assert.Equal(new byte[] { 1 }, opt.Channels);
    }

    [Fact]
    public void Channels_HonoursASingleNonDefaultChannel_WithoutLeakingTheDefault()
    {
        // The append bug made this bind to { 1, 2 } — the default leaking in alongside the real value.
        var opt = Bind(("Map:Channels:0", "2"));
        Assert.Equal(new byte[] { 2 }, opt.Channels);
    }

    [Fact]
    public void Channels_KeepsAMultiChannelListInOrder()
    {
        var opt = Bind(("Map:Channels:0", "1"), ("Map:Channels:1", "2"), ("Map:Channels:2", "3"));
        Assert.Equal(new byte[] { 1, 2, 3 }, opt.Channels);
    }

    [Fact]
    public void Channels_CollapsesDuplicates_SoPerChannelBuildsRunOnce()
    {
        // A duplicate channel would build the per-channel spawn points twice (MapService.InitMonsterSpawns).
        var opt = Bind(("Map:Channels:0", "1"), ("Map:Channels:1", "1"), ("Map:Channels:2", "2"));
        Assert.Equal(new byte[] { 1, 2 }, opt.Channels);
    }

    [Fact]
    public void Normalize_IsIdempotent()
    {
        var opt = Bind(("Map:Channels:0", "1"));
        opt.Normalize();
        opt.Normalize();
        Assert.Equal(new byte[] { 1 }, opt.Channels);
    }
}
