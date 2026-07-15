using TMap.Data;

namespace TMap.Server;

/// <summary>
/// Bound from the <c>Map</c> configuration section (appsettings + environment overrides). Mirrors the
/// C++ <c>Configurations/TMapSvr.ini [TMapConfig]</c> keys read by <c>LoadConfig</c> (TMapSvr.cpp:635).
/// </summary>
public sealed class MapServerOptions
{
    /// <summary>This server's own client-facing TCP listen port. C++ <c>GamePort</c> (default 5816).</summary>
    public int Port { get; set; } = 5816;

    /// <summary>The World server to connect out to. C++ <c>WorldIP</c>/<c>WorldPort</c> (default 127.0.0.1:3816;
    /// the .NET TWorldSvr listens on 3815, so set this to 3815 when interoperating with that port).</summary>
    public string WorldIp { get; set; } = "127.0.0.1";
    public int WorldPort { get; set; } = 3816;

    /// <summary>This map server's identity in the topology. C++ <c>GroupID</c>/<c>ServerID</c>.</summary>
    public byte GroupId { get; set; } = 1;
    public byte ServerId { get; set; } = 1;

    /// <summary>The channels this map server serves (announced to the world in MW_CONNECT_ACK). C++ default 1.</summary>
    public byte[] Channels { get; set; } = { 1 };

    /// <summary>UDP log-sink endpoint. C++ <c>LogIP</c>/<c>LogPort</c> (default 127.0.0.1:7000).</summary>
    public string LogIp { get; set; } = "127.0.0.1";
    public int LogPort { get; set; } = 7000;

    /// <summary>
    /// When true the client plane is accepted in plaintext (no RC4/XOR cipher). The deployed C++ map runs
    /// crypto ON, so this defaults to false. Set true only to interoperate with a plaintext client/bot.
    /// </summary>
    public bool NoCrypt { get; set; }

    public MapDbOptions Db { get; set; } = new();
}
