#nullable enable

using System;
using System.Collections.Generic;
using Terraria.ModLoader;
using PerformanceProfiler.Profiling;

namespace PerformanceProfiler.Data.Collectors;

/// <summary>
/// Snapshot of the per-mod / per-category CPU breakdown owned by
/// <see cref="MetricCollector"/>. Both lists are live references; callers
/// must not mutate them. Layout: <c>[modId * CategoryCount + categoryId]</c>.
/// </summary>
public readonly struct HookCpuSnapshot
{
    public readonly bool WorldLoaded;
    public readonly int ModCount;
    public readonly int CategoryCount;
    public readonly IReadOnlyList<double>? SmoothedMsByCategory;
    public readonly IReadOnlyList<double>? AverageMsByCategory;
    public readonly IReadOnlyList<double>? PerHookMs;
    public readonly IReadOnlyList<double>? PerHookAverageMs;

    public HookCpuSnapshot(bool worldLoaded, int modCount, int categoryCount,
        IReadOnlyList<double>? smoothed, IReadOnlyList<double>? averaged,
        IReadOnlyList<double>? perHook, IReadOnlyList<double>? perHookAvg)
    {
        WorldLoaded = worldLoaded;
        ModCount = modCount;
        CategoryCount = categoryCount;
        SmoothedMsByCategory = smoothed;
        AverageMsByCategory = averaged;
        PerHookMs = perHook;
        PerHookAverageMs = perHookAvg;
    }

    public static readonly HookCpuSnapshot Empty
        = new HookCpuSnapshot(false, 0, 0, null, null, null, null);
}

/// <summary>
/// Pipeline-facing adapter over <see cref="PerModAttribution"/> +
/// <see cref="MetricCollector"/>'s per-mod / per-hook CPU surface. Same
/// pattern as <see cref="FrameTimeCollector"/>: per-tick callback is a
/// no-op, snapshot reads pull from the live collector arrays.
/// </summary>
public sealed class HookCpuCollector : IDataCollector<HookCpuSnapshot>
{
    public const string StreamName = "hookCpu";

    public string Name => StreamName;
    // OnDemand: see FrameTimeCollector for the rationale — MetricCollector
    // owns the per-tick capture; this is a pull-side adapter.
    public DataStreamCadence Cadence => DataStreamCadence.OnDemand;
    public DataStage Stage => DataStage.Collector;
    public TickCapture? PerTickCallback => null;

    public void Initialise(SessionContext session) { }
    public void Reset() { }
    public void Dispose() { }

    public HookCpuSnapshot CurrentSnapshot()
    {
        MetricCollector? c = ModContent.GetInstance<ProfilerSystem>()?.Collector;
        if (c == null || c.History.Count == 0) return HookCpuSnapshot.Empty;
        return new HookCpuSnapshot(
            worldLoaded: true,
            modCount: PerModAttribution.ModCount,
            categoryCount: PerModAttribution.CategoryCount,
            smoothed: c.PerModCategoryMs,
            averaged: c.PerModCategoryAverageMs,
            perHook: c.PerHookMs,
            perHookAvg: c.PerHookAverageMs);
    }

    public object CurrentSnapshotBoxed() => CurrentSnapshot();
}
