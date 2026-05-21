#nullable enable

using System.Collections.Generic;

using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling.Persistence;
using PerformanceProfiler.Profiling.Persistence.Records;
namespace PerformanceProfiler.Data.Stats;

/// <summary>
/// Single source of truth for "which hook coverage counters does the active
/// backend write to right now?".
///
/// <para>
/// The delegate path (<see cref="HookInterceptor"/>) and the ILHook path
/// (<see cref="ILHookInterceptor"/>) each maintain their own measured/total
/// counters. In default <c>ILHook</c> mode the delegate counters never get
/// written; in <c>Delegate</c> mode the IL counters stay empty. Reading the
/// wrong source produces the agent-visible "session JSON says 0/X measured
/// while the overlay says 100% coverage" mismatch the audit's
/// hook-instrumentation finding called out.
/// </para>
///
/// <para>
/// Every consumer of coverage numbers — the overlay chrome's PROFILER HEALTH
/// strip, the TREE tab's per-mod coverage line, and the session JSON's
/// <c>coverage</c> block — routes through these helpers so the source is
/// chosen once and consistently.
/// </para>
///
/// <para>
/// In <see cref="HookBackendMode.Parallel"/> the primary surface stays the
/// delegate counters (the proven baseline; ILHook divergence is logged via
/// <see cref="MetricCollector.BackendDivergence"/> instead).
/// </para>
/// </summary>
internal static class HookCoverageView
{
    /// <summary>True if the IL backend is the active counter source.</summary>
    internal static bool UseILHookCounters =>
        HookBackend.Mode == HookBackendMode.ILHook;

    /// <summary>Total hooks discovered across all profiled mods (denominator for coverage %).</summary>
    internal static int TotalHooks()
    {
        IReadOnlyList<int> totals = TotalHookCounts;
        int sum = 0;
        for (int i = 0; i < totals.Count; i++) sum += totals[i];
        return sum;
    }

    /// <summary>Hooks the active backend successfully wrapped with timing instrumentation.</summary>
    internal static int MeasuredHooks()
    {
        IReadOnlyList<int> measured = MeasuredHookCounts;
        int sum = 0;
        for (int i = 0; i < measured.Count; i++) sum += measured[i];
        return sum;
    }

    /// <summary>Per-mod measured-hook count, indexed by ModId.</summary>
    internal static IReadOnlyList<int> MeasuredHookCounts =>
        UseILHookCounters
            ? ILHookInterceptor.MeasuredHookCounts
            : HookInterceptor.MeasuredHookCounts;

    /// <summary>Per-mod total discovered-hook count, indexed by ModId.</summary>
    internal static IReadOnlyList<int> TotalHookCounts =>
        UseILHookCounters
            ? ILHookInterceptor.TotalHookCounts
            : HookInterceptor.TotalHookCounts;

    /// <summary>Measured-hook count for one mod; <c>0</c> when the index is out of range.</summary>
    internal static int MeasuredForMod(int modId)
    {
        IReadOnlyList<int> source = MeasuredHookCounts;
        return (uint)modId < (uint)source.Count ? source[modId] : 0;
    }

    /// <summary>Discovered-hook count for one mod; <c>0</c> when the index is out of range.</summary>
    internal static int TotalForMod(int modId)
    {
        IReadOnlyList<int> source = TotalHookCounts;
        return (uint)modId < (uint)source.Count ? source[modId] : 0;
    }
}
