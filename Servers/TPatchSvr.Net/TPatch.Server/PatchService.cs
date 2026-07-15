using Microsoft.Extensions.Logging;
using TPatch.Data;
using TPatch.Protocol;
using TPatch.Server.Net;

namespace TPatch.Server;

/// <summary>
/// The patch-server logic: a faithful port of <c>TPatchSvr/Handler.cpp</c> + <c>Sender.cpp</c>. All work
/// runs on the single batch task (mirroring the C++ per-packet handler pattern), so the session map needs
/// no locking. Each handler runs one query against the <see cref="IPatchSource"/> and writes one reply;
/// the two "complete" handlers close the connection after acting (the C++ <c>EC_SESSION_EXIT</c>).
/// </summary>
public sealed class PatchService
{
    private readonly IPatchSource _src;
    private readonly PatchServerOptions _opt;
    private readonly uint _loginIp;      // packed exactly as inet_addr → sin_addr.s_addr (network-order octets)
    private readonly ushort _loginPort;  // host-order port number (the C++ sends the raw DB value)
    private readonly ILogger _log;

    private sealed class SessionInfo { public long LastTickMs; public bool IsServer; }
    private readonly Dictionary<PacketConnection, SessionInfo> _sessions = new();

    public PatchService(IPatchSource src, PatchServerOptions opt, uint loginIp, ushort loginPort, ILogger log)
    {
        _src = src;
        _opt = opt;
        _loginIp = loginIp;
        _loginPort = loginPort;
        _log = log;
    }

    private SessionInfo Touch(PacketConnection conn)
    {
        if (!_sessions.TryGetValue(conn, out var s))
        {
            s = new SessionInfo();
            _sessions[conn] = s;
        }
        s.LastTickMs = Environment.TickCount64;
        return s;
    }

    public void OnDisconnect(PacketConnection conn) => _sessions.Remove(conn);

    public async Task DispatchAsync(PacketConnection conn, byte[] packet)
    {
        var r = new PacketReader(packet);
        var s = Touch(conn);

        switch (r.Id)
        {
            case Msg.CT_NEWPATCH_REQ: await OnNewPatchAsync(conn, r); break;
            case Msg.CT_PATCH_REQ: await OnPatchAsync(conn, r); break;
            case Msg.CT_PREPATCH_REQ: await OnPrePatchAsync(conn, r); break;
            case Msg.CT_CHANGEIF_REQ: await OnChangeIfAsync(conn, r); break;
            case Msg.CT_PREPATCHCOMPLETE_REQ: await OnPrePatchCompleteAsync(conn, r); break;
            case Msg.CT_PATCHSTART_REQ: conn.RequestClose(); break;             // EC_SESSION_EXIT (no reply)
            case Msg.CT_SERVICEMONITOR_ACK: OnMonitor(conn, r, s); break;
            case Msg.CT_SERVICEDATACLEAR_ACK: break;                             // EC_NOERROR, no-op
            case Msg.CT_CTRLSVR_REQ: break;                                      // EC_NOERROR, no-op
            default:
                _log.LogWarning("Unknown patch packet id 0x{Id:X4} from {Ep}; closing.", r.Id, conn.RemoteEndPoint);
                conn.RequestClose(); // C++ returns EC_SESSION_INVALIDMSG → session closed
                break;
        }
    }

    // OnCT_NEWPATCH_REQ → CT_NEWPATCH_ACK: full patch list newer than the client's version + min beta.
    private async Task OnNewPatchAsync(PacketConnection conn, PacketReader r)
    {
        uint version = r.ReadUInt32();
        var files = await SafeFilesAsync(() => _src.GetVersionsAsync(version), "GetVersions");
        uint minBeta = await SafeMinBetaAsync();

        var w = new PacketWriter(Msg.CT_NEWPATCH_ACK);
        w.WriteString(_opt.FtpUrl);
        w.WriteUInt32(_loginIp);
        w.WriteUInt16(_loginPort);
        w.WriteUInt32(minBeta);
        w.WriteUInt16((ushort)files.Count);
        foreach (var f in files)
        {
            w.WriteUInt32(f.Version);
            w.WriteString(f.Path);
            w.WriteString(f.Name);
            w.WriteUInt32(f.Size);
            w.WriteUInt32(f.BetaVer);
        }
        conn.Send(w);
    }

    // OnCT_PATCH_REQ → CT_PATCH_ACK: the older list (no min-beta, no per-file beta).
    private async Task OnPatchAsync(PacketConnection conn, PacketReader r)
    {
        uint version = r.ReadUInt32();
        var files = await SafeFilesAsync(() => _src.GetVersionsAsync(version), "GetVersions");

        var w = new PacketWriter(Msg.CT_PATCH_ACK);
        w.WriteString(_opt.FtpUrl);
        w.WriteUInt32(_loginIp);
        w.WriteUInt16(_loginPort);
        w.WriteUInt16((ushort)files.Count);
        foreach (var f in files)
        {
            w.WriteUInt32(f.Version);
            w.WriteString(f.Path);
            w.WriteString(f.Name);
            w.WriteUInt32(f.Size);
        }
        conn.Send(w);
    }

    // OnCT_PREPATCH_REQ → CT_PREPATCH_ACK: beta/prepatch list from TPREVERSION.
    private async Task OnPrePatchAsync(PacketConnection conn, PacketReader r)
    {
        uint betaVer = r.ReadUInt32();
        var files = await SafeFilesAsync(() => _src.GetPreVersionsAsync(betaVer), "GetPreVersions");

        var w = new PacketWriter(Msg.CT_PREPATCH_ACK);
        w.WriteString(_opt.PreFtpUrl);
        w.WriteUInt32(_loginIp);
        w.WriteUInt16(_loginPort);
        w.WriteUInt16((ushort)files.Count);
        foreach (var f in files)
        {
            w.WriteUInt32(f.BetaVer);
            w.WriteString(f.Path);
            w.WriteString(f.Name);
            w.WriteUInt32(f.Size);
        }
        conn.Send(w);
    }

    // OnCT_CHANGEIF_REQ → CT_NEWPATCH_ACK (interface files): FTP base gets "/interface", min beta = 0.
    private async Task OnChangeIfAsync(PacketConnection conn, PacketReader r)
    {
        byte option = r.ReadByte();
        var files = await SafeFilesAsync(() => _src.GetInterfaceAsync(option), "GetInterface");

        var w = new PacketWriter(Msg.CT_NEWPATCH_ACK);
        w.WriteString(_opt.FtpUrl + "/interface");
        w.WriteUInt32(_loginIp);
        w.WriteUInt16(_loginPort);
        w.WriteUInt32(0); // dwMinBetaVer = NULL in the C++
        w.WriteUInt16((ushort)files.Count);
        foreach (var f in files)
        {
            w.WriteUInt32(f.Version);
            w.WriteString(f.Path);   // always "" for interface files
            w.WriteString(f.Name);
            w.WriteUInt32(f.Size);
            w.WriteUInt32(f.BetaVer); // always 0 for interface files
        }
        conn.Send(w);
    }

    // OnCT_PREPATCHCOMPLETE_REQ → TPreCompleteAdd, then close (EC_SESSION_EXIT).
    private async Task OnPrePatchCompleteAsync(PacketConnection conn, PacketReader r)
    {
        uint betaVer = r.ReadUInt32();
        try { await _src.AddPreCompleteAsync(betaVer); }
        catch (Exception ex) { _log.LogWarning(ex, "TPreCompleteAdd({Beta}) failed.", betaVer); }
        conn.RequestClose();
    }

    // OnCT_SERVICEMONITOR_ACK → reply CT_SERVICEMONITOR_REQ, mark this peer as a server, sweep idle clients.
    private void OnMonitor(PacketConnection conn, PacketReader r, SessionInfo s)
    {
        r.Skip(8);                     // the C++ skips a leading INT64 in the ACK body
        uint tick = r.ReadUInt32();

        uint count = (uint)_sessions.Count; // C++ reports m_mapTSESSION.size() for both curSession and curUser
        var w = new PacketWriter(Msg.CT_SERVICEMONITOR_REQ);
        w.WriteInt64(0);
        w.WriteUInt32(tick);
        w.WriteUInt32(count);
        w.WriteUInt32(count);
        w.WriteUInt32(0);              // dwActiveUser
        conn.Send(w);

        s.IsServer = true;

        long now = Environment.TickCount64;
        long timeoutMs = _opt.IdleTimeoutSeconds * 1000L;
        foreach (var (c, info) in _sessions.ToArray())
            if (!info.IsServer && now - info.LastTickMs > timeoutMs)
                c.RequestClose();
    }

    private async Task<IReadOnlyList<PatchFile>> SafeFilesAsync(Func<Task<IReadOnlyList<PatchFile>>> load, string what)
    {
        try { return await load(); }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "{What} failed; reporting no files.", what);
            return Array.Empty<PatchFile>();
        }
    }

    private async Task<uint> SafeMinBetaAsync()
    {
        try { return await _src.GetMinBetaVerAsync(); }
        catch (Exception ex) { _log.LogWarning(ex, "GetMinBetaVer failed; reporting 0."); return 0; }
    }
}
