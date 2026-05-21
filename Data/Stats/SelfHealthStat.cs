#nullable enable

using Terraria.ModLoader;
using PerformanceProfiler.Profiling;

namespace PerformanceProfiler.Data.Stats;

/// <summary>
/// Immutable snapshot of <see cref="ProfilerSelfHealth"/> for pipeline
/// consumers. Captured by value so the snapshot survives the source
/// instance being swapped at world unload.
/// </summary>
public readonly struct SelfHealthSnapshot
{
    public readonly bool Installed;
    public readonly long InstallDeltaBytes;
    public readonly long BytesPerHook;
    public readonly int InstalledHookCount;
    public readonly long ProcessWorkingSetBytes;
    public readonly long ProcessManagedHeapBytes;
    public readonly double ManagedFractionOfWorkingSet;
    public readonly SelfHealthSeverity Severity;
    public readonly HookBackendMode BackendMode;

    public SelfHealthSnapshot(bool installed, long installDelta, long bph, int hookCount,
        long workingSet, long managedHeap, double managedFrac,
        SelfHealthSeverity severity, HookBackendMode backend)
    {
        Installed = installed;
        InstallDeltaBytes = installDelta;
        BytesPerHook = bph;
        InstalledHookCount = hookCount;
        ProcessWorkingSetBytes = workingSet;
        ProcessManagedHeapBytes = managedHeap;
        ManagedFractionOfWorkingSet = managedFrac;
        Severity = severity;
        BackendMode = backend;
    }

    public static SelfHealthSnapshot From(ProfilerSelfHealth h)
        => new SelfHealthSnapshot(
            installed: h.IsInstalled,
            installDelta: h.InstallDeltaBytes,
            bph: h.BytesPerHook,
            hookCount: h.InstalledHookCount,
            workingSet: h.ProcessWorkingSetBytes,
            managedHeap: h.ProcessManagedHeapBytes,
            managedFrac: h.ManagedFractionOfWorkingSet,
            severity: h.Severity,
            backend: HookBackend.Mode);
}

/// <summary>
/// Pipeline-facing adapter over <see cref="ProfilerSelfHealth"/>. The
/// underlying health metric is process-wide (lives outside the per-world
/// lifecycle), so this stat is always available regardless of world
/// state.
/// </summary>
public sealed class SelfHealthStat : IDataStat<SelfHealthSnapshot>
{
    public const string StreamName = "selfHealth";

    public string Name => StreamName;
    public DataStreamCadence Cadence => DataStreamCadence.OnDemand;
    public DataStage Stage => DataStage.Stat;

    public void Initialise(SessionContext session) { }
    public void Reset() { }
    public void Dispose() { }

    public SelfHealthSnapshot CurrentSnapshot()
    {
        MetricCollector? c = ModContent.GetInstance<ProfilerSystem>()?.Collector;
        ProfilerSelfHealth h = c?.SelfHealth ?? ProfilerSystem.SelfHealth;
        return SelfHealthSnapshot.From(h);
    }

    public object CurrentSnapshotBoxed() => CurrentSnapshot();
}
