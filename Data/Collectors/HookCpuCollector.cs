#nullable enable

using System;
using System.Collections.Generic;
using Terraria.ModLoader;
using PerformanceProfiler.Profiling;

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Persistence.Streams;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Persistence;
using PerformanceProfiler.Persistence.Records;
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

    /// <summary>
    /// Smoothed DRAW-PHASE ms per mod/category (S01). SmoothedMsByCategory
    /// carries the TOTAL; update cost = total − draw. Null when the phase
    /// lanes are disabled in config.
    /// </summary>
    public readonly IReadOnlyList<double>? DrawMsByCategory;

    public HookCpuSnapshot(bool worldLoaded, int modCount, int categoryCount,
        IReadOnlyList<double>? smoothed, IReadOnlyList<double>? averaged,
        IReadOnlyList<double>? perHook, IReadOnlyList<double>? perHookAvg,
        IReadOnlyList<double>? drawMs = null)
    {
        WorldLoaded = worldLoaded;
        ModCount = modCount;
        CategoryCount = categoryCount;
        SmoothedMsByCategory = smoothed;
        AverageMsByCategory = averaged;
        PerHookMs = perHook;
        PerHookAverageMs = perHookAvg;
        DrawMsByCategory = drawMs;
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
            perHookAvg: c.PerHookAverageMs,
            drawMs: PerModAttribution.PhaseLanesEnabled ? c.PerModCategoryDrawMs : null);
    }

    public object CurrentSnapshotBoxed() => CurrentSnapshot();
}
