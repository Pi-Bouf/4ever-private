using TPatch.Data;

namespace TPatch.Server.Tests;

/// <summary>An in-memory <see cref="IPatchSource"/> so the whole handler surface is testable DB-free.</summary>
public sealed class FakePatchSource : IPatchSource
{
    public List<PatchFile> Versions { get; } = new();
    public List<PatchFile> PreVersions { get; } = new();
    public List<PatchFile> Interface { get; } = new();
    public uint MinBetaVer { get; set; }
    public List<uint> PreCompleteCalls { get; } = new();

    public Task<IReadOnlyList<PatchFile>> GetVersionsAsync(uint fromVersion, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PatchFile>>(Versions.Where(f => f.Version > fromVersion).ToList());

    public Task<IReadOnlyList<PatchFile>> GetPreVersionsAsync(uint fromBetaVer, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PatchFile>>(PreVersions.Where(f => f.BetaVer > fromBetaVer).ToList());

    public Task<IReadOnlyList<PatchFile>> GetInterfaceAsync(byte option, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PatchFile>>(Interface.ToList());

    public Task<uint> GetMinBetaVerAsync(CancellationToken ct = default) => Task.FromResult(MinBetaVer);

    public Task AddPreCompleteAsync(uint betaVer, CancellationToken ct = default)
    {
        PreCompleteCalls.Add(betaVer);
        return Task.CompletedTask;
    }
}
