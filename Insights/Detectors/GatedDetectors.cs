#nullable enable

using System.Collections.Generic;

using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Persistence.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Persistence;
using PerformanceProfiler.Persistence.Records;
namespace PerformanceProfiler.Insights.Detectors;

// Each detector below exists so the catalog is complete and the engine can
// honestly report its coverage gaps via the JSONL `gated` block. They all
// return immediately from Evaluate; lighting one up is one local change to
// its Evaluate body, no engine refactor. See plan §4 for the full design
// of each pattern.

// CONTEXT_CORRELATED_SPIKE moved to its own file
// (Detectors/ContextCorrelatedSpikeDetector.cs) when it was un-gated in Wave 5
// against the per-context spike counts the ContextBaseline accumulates.

// CONTEXT_CONDITIONAL_COST moved to its own file
// (Detectors/ContextConditionalCostDetector.cs) when it was un-gated in Wave 3
// against the ContextBaseline reference frame. It is now active; see that file.

// SUSTAINED_COST_SHIFT and NEW_CONTRIBUTOR moved to their own files
// (Detectors/SustainedCostShiftDetector.cs, Detectors/NewContributorDetector.cs) when
// they were un-gated in Wave 5 against the TemporalBaseline (early-vs-late session
// windows). Both are now active; see those files.

// GC_PAUSE_CULPRIT moved to its own file (Detectors/GcPauseCulpritDetector.cs)
// when it was un-gated. The detector is now active; see that file for the
// implementation.

/// <summary>
/// HOOK_FREQUENCY_TAIL — for a hook with mean calls/tick &gt;= 50, the p99
/// per-call ms is &gt;= 5× the median. Needs per-call timing samples (or
/// per-hook call counts) which the current collector does not yet expose;
/// gated on a sibling per-hook call-count surface.
/// </summary>
public sealed class HookFrequencyTailDetector : IInsightDetector
{
    public PatternKey Pattern => PatternKey.HookFrequencyTail;
    public Audience DefaultAudience => Audience.Modder;
    public bool IsGated => true;
    public string? GatedOn => "per-hook-call-counts";

    public bool IsAvailable(MetricCollector collector) => false;

    public void Evaluate(MetricCollector collector, long nowTick, long sessionLengthTicks, List<Insight> emit)
    {
        // TODO: requires per-hook call counts + per-call ms distribution. See plan §4.10.
    }
}
