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

/// <summary>
/// ALLOCATION_BURST — a mod whose smoothed allocation rate exceeds a hard
/// bytes-per-tick floor and is the dominant allocator in the session.
///
/// <para>
/// The plan's full form (plan §4.4) is context-bucketed and uses an in-bucket
/// vs out-of-bucket Mann-Whitney test. That form is gated on the Events tab
/// and the spikes-and-allocations sibling plan. This implementation emits
/// the simpler in-session shape: rank mods by smoothed alloc rate, and emit
/// a record for any mod that both clears the absolute floor and accounts
/// for &gt;= 40 % of the session's allocation throughput. When the gated
/// inputs land the per-context form replaces this in place.
/// </para>
/// </summary>
public sealed class AllocationBurstDetector : IInsightDetector
{
    /// <summary>Hard floor: mod must be allocating at least this many bytes/tick on average to be reported.</summary>
    public const double AbsoluteFloorBytesPerTick = 8 * 1024d;

    /// <summary>Mod must be at least this fraction of the session's total alloc throughput.</summary>
    public const double ShareFloor = 0.40;

    public PatternKey Pattern => PatternKey.AllocationBurst;
    public Audience DefaultAudience => Audience.Modder;
    public bool IsGated => false;
    public string? GatedOn => null;

    public bool IsAvailable(MetricCollector collector) =>
        collector.TracksAllocations && collector.PerModCategoryAverageBytes != null;

    /// <summary>
    /// Field-cached per-pass scratch buffer. v0.5 allocated a fresh
    /// <c>double[modCount]</c> on every Evaluate (1 Hz cadence). v0.6
    /// reuses the field; per insights-engine §1.3 + cross-allocations
    /// §6.6 zeta1.
    /// </summary>
    private double[] _perModBytesScratch = System.Array.Empty<double>();

    public void Evaluate(MetricCollector collector, long nowTick, long sessionLengthTicks, List<Insight> emit)
    {
        IReadOnlyList<double>? categoryBytes = collector.PerModCategoryAverageBytes;
        if (categoryBytes == null) return;

        string[] modNames = HookInterceptor.ProfiledModNames;
        int catCount = PerModAttribution.CategoryCount;

        double sessionTotal = 0d;
        if (_perModBytesScratch.Length < modNames.Length)
            _perModBytesScratch = new double[modNames.Length];
        double[] perModBytes = _perModBytesScratch;
        System.Array.Clear(perModBytes, 0, modNames.Length);
        for (int modId = 0; modId < modNames.Length; modId++)
        {
            double sum = 0d;
            for (int c = 0; c < catCount; c++)
            {
                int cell = modId * catCount + c;
                if (cell < categoryBytes.Count) sum += categoryBytes[cell];
            }
            perModBytes[modId] = sum;
            // Exclude the profiler's own allocation from the denominator too, not just from the
            // emit below: its render/persistence bytes would otherwise inflate sessionTotal and
            // deflate every other mod's share (self-identification, see InsightConstants).
            if (modNames[modId] != InsightConstants.SelfModInternalName) sessionTotal += sum;
        }
        if (sessionTotal <= 0d) return;

        for (int modId = 0; modId < modNames.Length; modId++)
        {
            // The profiler is instrumented like any other mod, so its own render /
            // persistence allocations land here; it would otherwise always rank as a
            // dominant allocator. Drop it (self-identification, see InsightConstants).
            if (modNames[modId] == InsightConstants.SelfModInternalName) continue;

            double bytes = perModBytes[modId];
            if (bytes < AbsoluteFloorBytesPerTick) continue;
            double share = bytes / sessionTotal;
            if (share < ShareFloor) continue;

            emit.Add(new Insight
            {
                Pattern = Pattern,
                Subject = SubjectRef.ForMod(modId),
                Magnitude = new Magnitude
                {
                    BaselineMs = 0d,
                    ObservedMs = 0d,
                    RatioOrDelta = share,
                    AllocBytes = (long)bytes,
                    Count = collector.History.Count,
                },
                Evidence = new Evidence
                {
                    SampleN = collector.History.Count,
                    BaselineN = collector.History.Count,
                    PValue = 1d,
                    EffectSize = share,
                    PValueAdjusted = 1d,
                    FirstTickIndex = 0,
                    LastTickIndex = nowTick,
                    Baseline = BaselineKind.SessionMean,
                },
                Confidence = Confidence.Preliminary,
                Audience = DefaultAudience,
            });
        }
    }
}
