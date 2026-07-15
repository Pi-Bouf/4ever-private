using TPatch.Data;

namespace TPatch.Server;

/// <summary>Bound from the <c>Patch</c> configuration section (appsettings + environment overrides).</summary>
public sealed class PatchServerOptions
{
    /// <summary>TCP listen port. Default 3715 (DEF_PATCHPORT). The C++ reads this from the registry.</summary>
    public int Port { get; set; } = 3715;

    public byte ServerId { get; set; } = 1;
    public byte GroupId { get; set; } = 0;

    /// <summary>FTP/HTTP base URL handed to clients for the full patch (CT_NEWPATCH_ACK / CT_PATCH_ACK).
    /// This is the value the C++ <c>LoadData</c> hard-codes; override it for your deployment.</summary>
    public string FtpUrl { get; set; } = "http://ni665833_1.vweb18.nitrado.net/patch/pvp";

    /// <summary>Prepatch (beta) base URL handed to clients in CT_PREPATCH_ACK.</summary>
    public string PreFtpUrl { get; set; } = "";

    /// <summary>Login-server address handed to clients. Overridden at startup by TLoadService when
    /// <see cref="ResolveLoginFromDb"/> is set and the DB is reachable (mirrors the C++ LoadData).</summary>
    public string LoginAddress { get; set; } = "127.0.0.1";

    /// <summary>Login-server port handed to clients (host-order integer, as the C++ sends it raw).</summary>
    public int LoginPort { get; set; } = 4815;

    /// <summary>When true and a DB is configured, resolve the login endpoint from TLoadService(0, LOGINSVR).</summary>
    public bool ResolveLoginFromDb { get; set; } = true;

    /// <summary>Idle client sessions older than this are dropped on a control monitor tick (C++: 60s).</summary>
    public int IdleTimeoutSeconds { get; set; } = 60;

    public PatchDbOptions Db { get; set; } = new();
}
