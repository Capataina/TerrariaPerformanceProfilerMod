#nullable enable

using LiteDB;
using PerformanceProfiler.Insights.Shared;

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Persistence.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Persistence;
namespace PerformanceProfiler.Persistence.Records;

/// <summary>
/// The three Welford components (count, mean, M2) of a running distribution, stored
/// as plain scalars so LiteDB round-trips them exactly — the same shape
/// <see cref="ContextBaselineRow"/> already persists, lifted into a reusable embedded
/// value so a rollup row can hold several distributions (cost / alloc / engagement)
/// without three scalar fields each.
///
/// <para>
/// It is the persistence twin of <see cref="RunningStat"/> (the in-memory struct that
/// folds samples): a session's freshly accumulated <see cref="RunningStat"/> merges
/// into the lifetime block via <see cref="Fold"/>, which is Chan's parallel-variance
/// combine — no re-reading the raw samples, O(1), numerically stable. This is what
/// makes "across your last N sessions" a cheap rollup read rather than an O(history)
/// re-scan (the plan's substrate decision).
/// </para>
/// </summary>
public sealed class WelfordStat
{
    public long Count { get; set; }
    public double Mean { get; set; }
    public double M2 { get; set; }

    /// <summary>Reconstructs the in-memory running stat from the persisted components.</summary>
    public RunningStat AsStat() => RunningStat.FromComponents(Count, Mean, M2);

    /// <summary>Snapshots a running stat into a fresh persisted block.</summary>
    public static WelfordStat FromStat(in RunningStat s) =>
        new WelfordStat { Count = s.Count, Mean = s.Mean, M2 = s.M2 };

    /// <summary>
    /// Folds one more distribution into this block in place (Chan's parallel merge via
    /// <see cref="RunningStat.Merge"/>). Used at session-end to add this session's stat
    /// to the lifetime total. Idempotent only across distinct inputs — call once per
    /// session per metric.
    /// </summary>
    public void Fold(in RunningStat session)
    {
        RunningStat merged = AsStat().Merge(session);
        Count = merged.Count;
        Mean = merged.Mean;
        M2 = merged.M2;
    }

    /// <summary>Folds a single scalar sample (a per-session summary value) into the block.</summary>
    public void FoldSample(double value)
    {
        var one = new RunningStat();
        one.Add(value);
        Fold(one);
    }
}
