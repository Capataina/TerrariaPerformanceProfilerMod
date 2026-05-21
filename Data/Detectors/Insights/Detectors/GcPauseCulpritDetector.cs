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
namespace PerformanceProfiler.Data.Detectors.Insights.Detectors;

/// <summary>
/// GC_PAUSE_CULPRIT — attributes a GC-caused stall to the mod that allocated
/// the most in the <see cref="LookbackTicks"/> ticks immediately before the
/// stall fired. Now active (was previously gated on
/// <c>spikes-and-allocations</c>); <see cref="StallDetector"/> + the
/// per-tick allocation ring give us both halves of the attribution story.
///
/// <para>
/// <b>Scope of the claim.</b> This pattern says "Mod X allocated <c>share</c>%
/// of bytes in the second before a GC pause fired". It does NOT say "Mod X
/// caused the GC pause" — GC is a process-wide phenomenon and a mod might
/// happen to be allocating heavily at the same moment something else
/// triggered the collection. The renderer should hedge accordingly; the
/// honesty contract requires the language to stay descriptive.
/// </para>
///
/// <para>
/// <b>Threshold and ranking.</b> We only emit when the top contributor holds
/// at least <see cref="MinShareToFlag"/> of the lookback-window allocations
/// — otherwise no mod stands out from the noise and "attributing" would be
/// dishonest. The magnitude stored on the record is the share fraction in
/// <c>[0,1]</c>, so <see cref="RankingScorer"/> treats it correctly as a
/// share pattern (see the share-vs-ratio split in that file).
/// </para>
///
/// <para>
/// <b>Why ThisSession scope.</b> The lookback window is one second; the
/// claim is bounded by the data we have in the per-tick ring. A future
/// LiteDB-backed iteration could extend this to "mod X consistently
/// allocates a lot before GC pauses" with <see cref="EvidenceScope.LifetimeData"/>,
/// but for now we stay honest about the single-session evidence.
/// </para>
/// </summary>
public sealed class GcPauseCulpritDetector : IInsightDetector
{
    /// <summary>Lookback window (ticks) before a stall over which allocations are summed. One second at 60 Hz.</summary>
    public const int LookbackTicks = 60;

    /// <summary>Minimum share of lookback-window allocations a mod must hold to be flagged. Below this, "no clear culprit".</summary>
    public const double MinShareToFlag = 0.40;

    public PatternKey Pattern => PatternKey.GcPauseCulprit;
    public Audience DefaultAudience => Audience.Modder;

    // Ungated as of stall-detector landing. The two prerequisites — per-tick
    // per-mod allocation deltas and stall events — are now both produced by
    // MetricCollector.
    public bool IsGated => false;
    public string? GatedOn => null;

    public bool IsAvailable(MetricCollector collector)
        => collector.TracksAllocations && collector.Stalls.Count > 0;

    /// <summary>
    /// Field-cached per-pass scratch buffer. v0.5 allocated a fresh
    /// <c>double[modCount]</c> on every Evaluate (1 Hz cadence). v0.6
    /// reuses the field; per insights-engine §1.6 + cross-allocations §6.6 zeta1.
    /// </summary>
    private double[] _modBytesScratch = System.Array.Empty<double>();

    public void Evaluate(MetricCollector collector, long nowTick, long sessionLengthTicks, List<InsightRecord> emit)
    {
        if (!collector.TracksAllocations) return;
        IReadOnlyList<StallEvent> stalls = collector.Stalls;
        if (stalls.Count == 0) return;

        int modCount = HookInterceptor.ProfiledModNames.Length;
        if (modCount == 0) return;

        // Reusable scratch buffer — sized by mod count, used per-stall.
        if (_modBytesScratch.Length < modCount) _modBytesScratch = new double[modCount];
        double[] modBytes = _modBytesScratch;

        for (int i = 0; i < stalls.Count; i++)
        {
            StallEvent s = stalls[i];

            // Only GC-cause stalls have a meaningful "what was allocated
            // beforehand" story. ProcessSuspended and LongFrame need
            // different attribution (suspend cause + draw-thread mod
            // respectively); both are out of scope here.
            if (s.Cause != StallCause.MajorGc && s.Cause != StallCause.MinorGc) continue;

            // Sum each mod's allocated bytes across the lookback window.
            // GetPerModBytes returns 0 for ticks outside the retained ring,
            // so a stall near session start with a small history naturally
            // attributes against what we have rather than failing.
            System.Array.Clear(modBytes, 0, modBytes.Length);
            double totalBytes = 0d;
            long windowStart = s.StartTickIndex - LookbackTicks;
            long windowEnd = s.StartTickIndex; // exclusive

            for (int mod = 0; mod < modCount; mod++)
            {
                double sum = 0d;
                for (long t = windowStart; t < windowEnd; t++)
                {
                    sum += collector.PerTickRing.GetPerModBytes(t, mod);
                }
                modBytes[mod] = sum;
                totalBytes += sum;
            }

            if (totalBytes < 1d) continue;

            // Top contributor.
            int topMod = -1;
            double topBytes = 0d;
            for (int mod = 0; mod < modCount; mod++)
            {
                if (modBytes[mod] > topBytes) { topBytes = modBytes[mod]; topMod = mod; }
            }
            if (topMod < 0) continue;

            double share = topBytes / totalBytes;
            if (share < MinShareToFlag) continue;

            emit.Add(new InsightRecord
            {
                Pattern = Pattern,
                Subject = SubjectRef.ForMod(topMod),
                Magnitude = new Magnitude
                {
                    BaselineMs = s.GcPauseDurationMs,    // diagnostic: how long the pause was
                    ObservedMs = s.TickPeriodMs,         // diagnostic: total wall freeze
                    RatioOrDelta = share,                // share in [0,1] — RankingScorer treats this as a share pattern
                    AllocBytes = (long)topBytes,
                    Count = LookbackTicks,
                },
                Evidence = new Evidence
                {
                    SampleN = LookbackTicks,
                    BaselineN = 0,
                    PValue = 1d,
                    PValueAdjusted = 1d,
                    EffectSize = share,
                    FirstTickIndex = windowStart,
                    LastTickIndex = windowEnd,
                    Baseline = BaselineKind.PerModRollingMean,
                },
                Confidence = Confidence.Preliminary,
                Audience = DefaultAudience,
                Scope = EvidenceScope.ThisSession,
            });
        }
    }
}
