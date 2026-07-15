using Microsoft.Extensions.Logging;

namespace TControl.Server.Ops;

/// <summary>
/// Abstraction over the patch-file deployment transport. The C++ server wrote uploaded files to a remote
/// machine's SMB admin share (<c>\\host\c$\Services\</c>). The cross-platform default
/// (<see cref="NoOpFileDeploy"/>) accepts the upload wire flow but discards the bytes; a real
/// implementation can be dropped in behind this seam. Returns C++-style result codes
/// (0 = success; non-zero = the various failure states).
/// </summary>
public interface IFileDeploy
{
    /// <summary>Begin an upload; returns 0 on success, or a non-zero failure code.</summary>
    byte Begin(byte machineId, string fileName);
    /// <summary>Write a chunk; returns 0 on success, non-zero on failure.</summary>
    byte Write(ReadOnlySpan<byte> data);
    /// <summary>Finish (cancel=false) or abort (cancel=true) the upload; returns 0 on success.</summary>
    byte End(bool cancel);
}

/// <summary>Cross-platform default: accepts and discards uploaded bytes (logs a warning).</summary>
public sealed class NoOpFileDeploy : IFileDeploy
{
    private readonly ILogger<NoOpFileDeploy> _log;
    public NoOpFileDeploy(ILogger<NoOpFileDeploy> log) => _log = log;

    public byte Begin(byte machineId, string fileName)
    {
        _log.LogWarning("[no-op] File upload of '{File}' to machine {Machine} accepted but discarded (SMB deploy disabled).", fileName, machineId);
        return 0;
    }

    public byte Write(ReadOnlySpan<byte> data) => 0;
    public byte End(bool cancel) => 0;
}
