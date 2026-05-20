#nullable enable

using System.Collections.Generic;

namespace PerformanceProfiler.Profiling.Insights.Detectors;

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

    public void Evaluate(MetricCollector collector, long nowTick, long sessionLengthTicks, List<InsightRecord> emit)
    {
        IReadOnlyList<double>? categoryBytes = collector.PerModCategoryAverageBytes;
        if (categoryBytes == null) return;

        string[] modNames = HookInterceptor.ProfiledModNames;
        int catCount = PerModAttribution.CategoryCount;

        double sessionTotal = 0d;
        double[] perModBytes = new double[modNames.Length];
        for (int modId = 0; modId < modNames.Length; modId++)
        {
            double sum = 0d;
            for (int c = 0; c < catCount; c++)
            {
                int cell = modId * catCount + c;
                if (cell < categoryBytes.Count) sum += categoryBytes[cell];
            }
            perModBytes[modId] = sum;
            sessionTotal += sum;
        }
        if (sessionTotal <= 0d) return;

        for (int modId = 0; modId < modNames.Length; modId++)
        {
            double bytes = perModBytes[modId];
            if (bytes < AbsoluteFloorBytesPerTick) continue;
            double share = bytes / sessionTotal;
            if (share < ShareFloor) continue;

            emit.Add(new InsightRecord
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
