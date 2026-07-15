namespace TControl.Server.Ops;

/// <summary>Per-machine resource sample (CPU %, MEM %, NET Mbps-ish). C++ <c>CPlatformUsage</c> via PDH.</summary>
public readonly record struct PlatformSample(uint Cpu, uint Mem, float Net);

/// <summary>
/// Abstraction over per-machine platform metrics. The C++ server sampled Windows PDH performance counters
/// (CPU/MEM/NIC). The cross-platform default (<see cref="NoOpPlatformMonitor"/>) reports zeros; a real
/// implementation can be dropped in behind this seam.
/// </summary>
public interface IPlatformMonitor
{
    PlatformSample Sample(byte machineId);
}

/// <summary>Cross-platform default: reports zero for every counter.</summary>
public sealed class NoOpPlatformMonitor : IPlatformMonitor
{
    public PlatformSample Sample(byte machineId) => new(0, 0, 0f);
}
