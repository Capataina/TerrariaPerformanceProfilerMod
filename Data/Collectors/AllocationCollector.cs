#nullable enable

using System.Collections.Generic;
using Terraria.ModLoader;
using PerformanceProfiler.Profiling;

namespace PerformanceProfiler.Data.Collectors;

/// <summary>
/// Snapshot of the per-mod allocation bytes. Lists are <c>null</c> when
/// the active backend does not track allocations (Lite-mode default);
/// the consumer is expected to gracefully render dashes in that case.
/// </summary>
public readonly struct AllocationSnapshot
{
    public readonly bool WorldLoaded;
    public readonly bool TracksAllocations;
    public readonly int ModCount;
    public readonly int CategoryCount;
    public readonly IReadOnlyList<double>? SmoothedBytesByCategory;
    public readonly IReadOnlyList<double>? AverageBytesByCategory;
    public readonly IReadOnlyList<double>? PerHookBytes;
    public readonly IReadOnlyList<double>? PerHookAverageBytes;

    public AllocationSnapshot(bool worldLoaded, bool tracks, int modCount, int categoryCount,
        IReadOnlyList<double>? smoothed, IReadOnlyList<double>? averaged,
        IReadOnlyList<double>? perHook, IReadOnlyList<double>? perHookAvg)
    {
        WorldLoaded = worldLoaded;
        TracksAllocations = tracks;
        ModCount = modCount;
        CategoryCount = categoryCount;
        SmoothedBytesByCategory = smoothed;
        AverageBytesByCategory = averaged;
        PerHookBytes = perHook;
        PerHookAverageBytes = perHookAvg;
    }

    public static readonly AllocationSnapshot Empty
        = new AllocationSnapshot(false, false, 0, 0, null, null, null, null);
}

/// <summary>
/// Pipeline-facing adapter over the optional allocation-tracking arrays
/// on <see cref="MetricCollector"/>. When the active hook backend does
/// not track allocations (<see cref="MetricCollector.TracksAllocations"/>
/// is false), the snapshot's lists are <c>null</c> and
/// <see cref="AllocationSnapshot.TracksAllocations"/> is false.
/// </summary>
public sealed class AllocationCollector : IDataCollector<AllocationSnapshot>
{
    public const string StreamName = "allocation";

    public string Name => StreamName;
    public DataStreamCadence Cadence => DataStreamCadence.PerTick;
    public DataStage Stage => DataStage.Collector;

    public TickCapture? PerTickCallback => Capture;
    private static readonly TickCapture Capture = static (in TickContext _) => { };

    public void Initialise(SessionContext session) { }
    public void Reset() { }
    public void Dispose() { }

    public AllocationSnapshot CurrentSnapshot()
    {
        MetricCollector? c = ModContent.GetInstance<ProfilerSystem>()?.Collector;
        if (c == null || c.History.Count == 0) return AllocationSnapshot.Empty;
        return new AllocationSnapshot(
            worldLoaded: true,
            tracks: c.TracksAllocations,
            modCount: PerModAttribution.ModCount,
            categoryCount: PerModAttribution.CategoryCount,
            smoothed: c.PerModCategoryBytes,
            averaged: c.PerModCategoryAverageBytes,
            perHook: c.PerHookBytes,
            perHookAvg: c.PerHookAverageBytes);
    }

    public object CurrentSnapshotBoxed() => CurrentSnapshot();
}
