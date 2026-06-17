using TWorld.Data;

namespace TWorld.Server;

/// <summary>Bound from the <c>World</c> configuration section (appsettings + environment overrides).</summary>
public sealed class WorldServerOptions
{
    /// <summary>TCP listen port for inbound server peers. Default 3815 (DEF_WORLDPORT).</summary>
    public int Port { get; set; } = 3815;

    public byte GroupId { get; set; } = 1;
    public byte ServerId { get; set; } = 1;

    /// <summary>Optional nation override; when null the value is read from TGetNation.</summary>
    public byte? Nation { get; set; }

    public WorldDbOptions Db { get; set; } = new();
}
