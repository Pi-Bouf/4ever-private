namespace TPatch.Data;

/// <summary>
/// A no-data <see cref="IPatchSource"/> used when the server boots without a database. Every query
/// returns nothing, so the server still accepts clients and replies "no files" — matching the graceful
/// degradation the TWorldSvr.Net port uses when the DB is absent.
/// </summary>
public sealed class EmptyPatchSource : IPatchSource
{
    public static readonly EmptyPatchSource Instance = new();

    public Task<IReadOnlyList<PatchFile>> GetVersionsAsync(uint fromVersion, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PatchFile>>(Array.Empty<PatchFile>());

    public Task<IReadOnlyList<PatchFile>> GetPreVersionsAsync(uint fromBetaVer, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PatchFile>>(Array.Empty<PatchFile>());

    public Task<IReadOnlyList<PatchFile>> GetInterfaceAsync(byte option, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PatchFile>>(Array.Empty<PatchFile>());

    public Task<uint> GetMinBetaVerAsync(CancellationToken ct = default) => Task.FromResult(0u);

    public Task AddPreCompleteAsync(uint betaVer, CancellationToken ct = default) => Task.CompletedTask;
}
