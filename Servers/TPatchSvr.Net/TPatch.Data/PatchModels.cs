namespace TPatch.Data;

/// <summary>
/// One patch-file row as sent to the client. Mirrors the C++ <c>struct tagPATCHFILE</c>
/// (<c>TPatchSvr/TPatchType.h</c>). Not every field is used by every packet: the plain-patch list omits
/// <see cref="BetaVer"/>, the prepatch list carries <see cref="BetaVer"/> instead of <see cref="Version"/>,
/// and the interface list leaves <see cref="Path"/> empty.
/// </summary>
public sealed record PatchFile(uint Version, string Path, string Name, uint Size, uint BetaVer);

/// <summary>The login-server endpoint resolved from the topology proc <c>TLoadService</c>.</summary>
public readonly record struct ServiceEndpoint(string Ip, ushort Port);

/// <summary>
/// The data the patch handlers need. <see cref="PatchDatabase"/> is the live SQL implementation; tests
/// supply a fake so the whole request/response surface is exercised DB-free (the same pattern the
/// TWorldSvr.Net tests use).
/// </summary>
public interface IPatchSource
{
    /// <summary>TVERSION rows with <c>dwVersion &gt; fromVersion</c>, ordered by version (CTBLVersion).</summary>
    Task<IReadOnlyList<PatchFile>> GetVersionsAsync(uint fromVersion, CancellationToken ct = default);

    /// <summary>TPREVERSION rows with <c>dwBetaVer &gt; fromBetaVer</c>, ordered by beta version (CTBLPreVersion).</summary>
    Task<IReadOnlyList<PatchFile>> GetPreVersionsAsync(uint fromBetaVer, CancellationToken ct = default);

    /// <summary>TUSER_INTERFACE rows for a UI option, stamped with the current max TVERSION (CTBLInterface).</summary>
    Task<IReadOnlyList<PatchFile>> GetInterfaceAsync(byte option, CancellationToken ct = default);

    /// <summary>The minimum allowed beta version (proc TMinBetaVer's return value).</summary>
    Task<uint> GetMinBetaVerAsync(CancellationToken ct = default);

    /// <summary>Records that a client finished a prepatch (proc TPreCompleteAdd).</summary>
    Task AddPreCompleteAsync(uint betaVer, CancellationToken ct = default);
}
