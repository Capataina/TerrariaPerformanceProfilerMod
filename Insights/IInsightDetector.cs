#nullable enable

using System.Collections.Generic;

using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Data.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling.Persistence;
using PerformanceProfiler.Profiling.Persistence.Records;
namespace PerformanceProfiler.Insights;

/// <summary>
/// One detector class per insight pattern. Detectors are stateless functions
/// over the collector's exposed views: pull whichever accessor the pattern
/// reads, emit zero or more <see cref="Insight"/>s into the
/// <paramref name="emit"/> list, return. The host (<see cref="InsightsEngine"/>)
/// owns the BH-FDR pass and the store submission; detectors never touch the
/// store directly.
///
/// <para>
/// The detector contract is unit-testable against synthetic inputs: a
/// <see cref="MetricCollector"/> is provided but detectors are expected to
/// only read its public accessors. No tModLoader runtime types are required
/// at this layer.
/// </para>
/// </summary>
public interface IInsightDetector
{
    /// <summary>The pattern this detector emits. One detector per PatternKey.</summary>
    PatternKey Pattern { get; }

    /// <summary>
    /// Default audience for records this detector emits. Renderers may
    /// override per surface (overlay shows Player density, exporter shows
    /// both).
    /// </summary>
    Audience DefaultAudience { get; }

    /// <summary>
    /// True if the detector has all the input streams it needs. False for
    /// patterns whose required data (Events tab GameContext, per-tick
    /// allocation deltas, cross-session baselines) has not yet landed. A
    /// disabled detector returns true from <see cref="IsGated"/> as well;
    /// the exporter uses that to render the JSONL <c>gated</c> block.
    /// </summary>
    bool IsAvailable(MetricCollector collector);

    /// <summary>
    /// True if the detector is currently waiting on a sibling feature
    /// (Events tab, LiteDB, engagement signal). Distinct from
    /// <see cref="IsAvailable"/> in that a gated detector reports its
    /// dependency rather than silently doing nothing.
    /// </summary>
    bool IsGated { get; }

    /// <summary>
    /// Human-readable reason the detector is gated, when <see cref="IsGated"/>
    /// is true. Used in the JSONL <c>gated</c> block and the agent log.
    /// </summary>
    string? GatedOn { get; }

    /// <summary>
    /// Evaluates the detector against the current collector state, appending
    /// any emitted records to <paramref name="emit"/>. No allocations beyond
    /// the records themselves; detectors that need scratch storage own a
    /// reusable buffer as an instance field.
    /// </summary>
    void Evaluate(MetricCollector collector, long nowTick, long sessionLengthTicks, List<Insight> emit);
}
