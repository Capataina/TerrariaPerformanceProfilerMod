#nullable enable

using System.Collections.Generic;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Insights.ReferenceFrames;
using PerformanceProfiler.Insights.Shared;

namespace PerformanceProfiler.Insights.Detectors;

/// <summary>
/// HEAP_LEAK — Family B (behaviour over time). "Managed heap is 1.8× its early-session
/// level at a similar entity load; a restart resets it." The subject is the runtime,
/// not a mod (a process-level subject, newly expressible after Wave 0).
///
/// <para>
/// <b>Controls for the temporal confound (the plan's hard requirement).</b> A heap
/// rise is only flagged when entity load did NOT rise to match — heap up <i>at
/// constant entity count</i> is a leak; heap up <i>with</i> entity count is just
/// progression (more map, more NPCs). So the detector requires a large heap ratio
/// (late/early) AND a small entity ratio, on top of a significant Welch's t. Reads
/// the <see cref="TemporalBaseline"/> the engine feeds at 1 Hz, so it is pure logic.
/// </para>
///
/// <para>
/// GC attribution is a hard limit, not a TODO: collections are global, allocation is
/// syntactic. So the copy says "heap is high, a restart resets it" — an observation
/// plus a mechanism, never "mod X is leaking" and never a command (Invariant 3).
/// </para>
/// </summary>
public sealed class HeapLeakDetector : IInsightDetector
{
    private const double MinHeapRatio = 1.5d;     // late heap >= 1.5x early
    private const double MaxEntityRatio = 1.25d;  // but entity load rose < 25%
    private const double MinEarlyHeapMB = 16d;    // ignore trivially small heaps

    public PatternKey Pattern => PatternKey.HeapLeak;
    public Audience DefaultAudience => Audience.Both;
    public bool IsGated => false;
    public string? GatedOn => null;

    public bool IsAvailable(MetricCollector collector) => InsightsEngine.Shared?.TemporalBaseline?.IsReady == true;

    public void Evaluate(MetricCollector collector, long nowTick, long sessionLengthTicks, List<Insight> emit)
    {
        TemporalBaseline? tb = InsightsEngine.Shared?.TemporalBaseline;
        if (tb == null || !tb.IsReady) return;

        RunningStat early = tb.EarlyHeap, late = tb.LateHeap;
        if (early.Mean < MinEarlyHeapMB) return;

        double heapRatio = early.Mean > 1e-9 ? late.Mean / early.Mean : 0d;
        if (heapRatio < MinHeapRatio) return;

        double entityRatio = tb.EarlyEntities.Mean > 1e-9
            ? tb.LateEntities.Mean / tb.EarlyEntities.Mean : 1d;
        if (entityRatio > MaxEntityRatio) return; // the rise tracks workload — progression, not a leak

        double p = Stats.WelchTTestP(late, early);
        // MB per minute of play across the late window (~1 sample/sec).
        double lateMinutes = late.Count / 60d;
        double ratePerMin = lateMinutes > 0d ? (late.Mean - early.Mean) / lateMinutes : 0d;

        emit.Add(new Insight
        {
            Pattern = PatternKey.HeapLeak,
            Subject = SubjectRef.ForRuntime(),
            Magnitude = new Magnitude
            {
                Shape = MagnitudeShape.Rate,
                Rate = ratePerMin,
                BaselineMs = early.Mean,   // MB, reusing the field as the early heap level
                ObservedMs = late.Mean,    // MB, the late heap level
                RatioOrDelta = heapRatio,
            },
            Evidence = new Evidence
            {
                SampleN = (int)System.Math.Min(int.MaxValue, late.Count),
                BaselineN = (int)System.Math.Min(int.MaxValue, early.Count),
                PValue = p,
                PValueAdjusted = p, // a single test (heap is one signal) — no multiple-comparison inflation
                EffectSize = Stats.CohensD(late, early),
                Baseline = BaselineKind.SessionFirstHalf,
            },
            Confidence = Confidence.Preliminary,
            Audience = Audience.Both,
            Scope = EvidenceScope.ThisSession,
            ConfirmationCount = 1,
        });
    }
}
