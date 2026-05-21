#nullable enable

using System;
using System.Collections.Generic;
using PerformanceProfiler.Data.Contracts;
using PerformanceProfiler.Profiling;

namespace PerformanceProfiler.Data.Aggregators;

/// <summary>
/// I7 — mod-pair cost correlation. Computes a Pearson matrix over the F3
/// 1 Hz per-mod cost buckets so the renderer can show which mods' costs
/// move together.
///
/// <para>
/// Cost bound: full N² Pearson over ~hour of seconds × ~50 mods is a few
/// million multiply-adds per call. Cached for 5 s between recomputes so
/// dashboard polling does not blow the budget. When more than 60 mods are
/// loaded the matrix is restricted to the top 30 by cost share; the rest
/// are excluded from <see cref="ModInteractionSnapshot.MatrixRowMajor"/>
/// (renderer is expected to render only the included subset).
/// </para>
///
/// <para>
/// Pearson handles zero-variance pairs by returning 0; constant series
/// produce no signal here regardless of how often they fire.
/// </para>
/// </summary>
public sealed class ModInteractionAggregator : IDataAggregator<ModInteractionSnapshot>
{
    public const string StreamName = RolloutStreamNames.ModInteraction;
    private const int MaxModsInMatrix = 30;
    private const int LargeModCutoff = 60;
    private const int TopCoupledCap = 10;
    private const long RecomputeIntervalMs = 5000;

    public string Name => StreamName;
    public DataStreamCadence Cadence => DataStreamCadence.OnDemand;
    public DataStage Stage => DataStage.Aggregator;

    private ModInteractionSnapshot _cached = ModInteractionSnapshot.Empty;
    private long _lastComputeUnixMs;
    private readonly object _gate = new object();

    public void Initialise(SessionContext session)
    {
        _cached = ModInteractionSnapshot.Empty;
        _lastComputeUnixMs = 0;
    }

    public void Reset()
    {
        _cached = ModInteractionSnapshot.Empty;
        _lastComputeUnixMs = 0;
    }

    public void Dispose() { }

    public ModInteractionSnapshot CurrentSnapshot()
    {
        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        lock (_gate)
        {
            if (_lastComputeUnixMs != 0 &&
                nowMs - _lastComputeUnixMs < RecomputeIntervalMs)
            {
                return _cached;
            }

            ModCostTimeSeriesSnapshot series = DataRegistry.Shared
                .Lookup<ModCostTimeSeriesSnapshot>(RolloutStreamNames.PerModCostTimeSeries)
                ?.CurrentSnapshot() ?? ModCostTimeSeriesSnapshot.Empty;

            _cached = Compute(series);
            _lastComputeUnixMs = nowMs;
            return _cached;
        }
    }

    public object CurrentSnapshotBoxed() => CurrentSnapshot();

    private static ModInteractionSnapshot Compute(ModCostTimeSeriesSnapshot series)
    {
        IReadOnlyList<ModCostBucket> buckets = series.Buckets;
        int bucketCount = buckets.Count;
        int totalMods = series.ModCount;
        if (bucketCount < 2 || totalMods < 2)
        {
            return ModInteractionSnapshot.Empty;
        }

        string[] modNames = HookInterceptor.ProfiledModNames;

        // ---- Decide included modIds --------------------------------------
        // For ≤ 60 mods include everyone; above that, restrict to the top 30
        // by total cost share across the captured window.
        int[] modIds;
        if (totalMods <= LargeModCutoff)
        {
            modIds = new int[totalMods];
            for (int i = 0; i < totalMods; i++) modIds[i] = i;
        }
        else
        {
            double[] totals = new double[totalMods];
            for (int b = 0; b < bucketCount; b++)
            {
                IReadOnlyList<double> per = buckets[b].PerModMs;
                int n = Math.Min(totals.Length, per.Count);
                for (int m = 0; m < n; m++) totals[m] += per[m];
            }
            // Pick top MaxModsInMatrix by total.
            int[] ranked = new int[totalMods];
            for (int i = 0; i < totalMods; i++) ranked[i] = i;
            Array.Sort(ranked, (a, b) => totals[b].CompareTo(totals[a]));
            int keep = Math.Min(MaxModsInMatrix, totalMods);
            modIds = new int[keep];
            Array.Copy(ranked, modIds, keep);
            Array.Sort(modIds); // canonical order
        }

        int N = modIds.Length;
        string[] names = new string[N];
        for (int i = 0; i < N; i++)
        {
            int id = modIds[i];
            names[i] = (uint)id < (uint)modNames.Length ? modNames[id] : "mod-" + id;
        }

        // ---- Extract per-mod series into contiguous arrays ---------------
        double[,] series2d = new double[N, bucketCount];
        for (int b = 0; b < bucketCount; b++)
        {
            IReadOnlyList<double> per = buckets[b].PerModMs;
            for (int i = 0; i < N; i++)
            {
                int id = modIds[i];
                series2d[i, b] = (uint)id < (uint)per.Count ? per[id] : 0d;
            }
        }

        // Pre-compute means and variances.
        double[] means = new double[N];
        double[] variances = new double[N];
        for (int i = 0; i < N; i++)
        {
            double s = 0d;
            for (int b = 0; b < bucketCount; b++) s += series2d[i, b];
            double mean = s / bucketCount;
            means[i] = mean;
            double v = 0d;
            for (int b = 0; b < bucketCount; b++)
            {
                double d = series2d[i, b] - mean;
                v += d * d;
            }
            variances[i] = v;
        }

        // ---- Pearson matrix ----------------------------------------------
        double[] matrix = new double[N * N];
        for (int i = 0; i < N; i++)
        {
            matrix[i * N + i] = 1d; // diagonal
            for (int j = i + 1; j < N; j++)
            {
                double r = Pearson(series2d, i, j, bucketCount, means, variances);
                matrix[i * N + j] = r;
                matrix[j * N + i] = r;
            }
        }

        // ---- Top coupled pairs ------------------------------------------
        var pairs = new List<ModInteractionPair>(N * (N - 1) / 2);
        for (int i = 0; i < N; i++)
        {
            for (int j = i + 1; j < N; j++)
            {
                double r = matrix[i * N + j];
                pairs.Add(new ModInteractionPair(
                    ModIdA: modIds[i],
                    ModNameA: names[i],
                    ModIdB: modIds[j],
                    ModNameB: names[j],
                    Pearson: r,
                    SamplesUsed: bucketCount));
            }
        }
        pairs.Sort((a, b) => Math.Abs(b.Pearson).CompareTo(Math.Abs(a.Pearson)));
        if (pairs.Count > TopCoupledCap) pairs.RemoveRange(TopCoupledCap, pairs.Count - TopCoupledCap);

        return new ModInteractionSnapshot(
            worldLoaded: true,
            ids: modIds,
            names: names,
            matrix: matrix,
            top: pairs);
    }

    private static double Pearson(double[,] series, int i, int j, int n,
        double[] means, double[] variances)
    {
        double vi = variances[i];
        double vj = variances[j];
        if (vi <= 0d || vj <= 0d) return 0d; // zero-variance — no signal.

        double mi = means[i];
        double mj = means[j];
        double cov = 0d;
        for (int b = 0; b < n; b++)
        {
            cov += (series[i, b] - mi) * (series[j, b] - mj);
        }
        double denom = Math.Sqrt(vi * vj);
        if (denom <= 0d) return 0d;
        return cov / denom;
    }
}
