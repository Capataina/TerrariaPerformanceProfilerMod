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
namespace PerformanceProfiler.Insights.Detectors;

// Each detector below exists so the catalog is complete and the engine can
// honestly report its coverage gaps via the JSONL `gated` block. They all
// return immediately from Evaluate; lighting one up is one local change to
// its Evaluate body, no engine refactor. See plan §4 for the full design
// of each pattern.

/// <summary>
/// CONTEXT_CORRELATED_SPIKE — within K ticks of a context transition the
/// per-mod ms rises sharply against a rolling baseline. Gated on the Events
/// tab's transition stream (sibling plan <c>events-tab-plan.md</c>); the
/// detector is wired in so the catalog is complete and the JSONL exporter
/// can declare the gate honestly.
/// </summary>
public sealed class ContextCorrelatedSpikeDetector : IInsightDetector
{
    public PatternKey Pattern => PatternKey.ContextCorrelatedSpike;
    public Audience DefaultAudience => Audience.Both;
    public bool IsGated => true;
    public string? GatedOn => "events-tab";

    public bool IsAvailable(MetricCollector collector) => false;

    public void Evaluate(MetricCollector collector, long nowTick, long sessionLengthTicks, List<Insight> emit)
    {
        // TODO: requires Events tab GameContext + transition stream. See plan §4.1 and §11 step 8.
    }
}

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
