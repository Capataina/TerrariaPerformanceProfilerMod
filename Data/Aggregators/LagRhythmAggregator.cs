#nullable enable

using System;
using System.Collections.Generic;
using Terraria.ModLoader;

using PerformanceProfiler.Data.Contracts;
using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Profiling;

namespace PerformanceProfiler.Data.Aggregators;

/// <summary>
/// L7 — lag rhythm / periodicity. Computes inter-event intervals across the
/// merged spike+stall timeline, bins them into log-spaced buckets between
/// 0.1 s and 600 s, and runs a simple peak detector to surface dominant
/// recurrence patterns ("a stall every ~17 s during the boss phase").
///
/// <para>
/// Peak detection is a 3-cell moving-max with a (mean + 2·stddev) threshold.
/// Deliberately conservative: false negatives (a real rhythm missed) are
/// preferable to false positives (random clustering surfaced as a "pattern"
/// the user then over-interprets). Invariant 3 — descriptive, not normative.
/// </para>
/// </summary>
public sealed class LagRhythmAggregator : IDataAggregator<LagRhythmSnapshot>
{
    /// <summary>Number of log-spaced buckets across the interval range.</summary>
    public const int BucketCount = 256;

    /// <summary>Inclusive lower bound of the interval range, in seconds.</summary>
    public const double MinSeconds = 0.1;

    /// <summary>Inclusive upper bound of the interval range, in seconds.</summary>
    public const double MaxSeconds = 600d;

    /// <summary>Standard-deviation multiplier for the peak threshold.</summary>
    public const double PeakSigma = 2d;

    /// <summary>Hard cap on emitted clusters.</summary>
    public const int MaxClusters = 16;

    public string Name => RolloutStreamNames.LagRhythm;
    public DataStreamCadence Cadence => DataStreamCadence.OnDemand;
    public DataStage Stage => DataStage.Aggregator;

    public void Initialise(SessionContext session) { }
    public void Reset() { }
    public void Dispose() { }

    public LagRhythmSnapshot CurrentSnapshot()
    {
        MetricCollector? c = ModContent.GetInstance<ProfilerSystem>()?.Collector;
        if (c == null) return LagRhythmSnapshot.Empty;

        IReadOnlyList<SpikeWindow> spikes = c.Spikes;
        IReadOnlyList<StallEvent> stalls = c.Stalls;
        int eventCount = spikes.Count + stalls.Count;
        if (eventCount < 2)
        {
            return new LagRhythmSnapshot(worldLoaded: true,
                Array.Empty<RhythmHistogramBucket>(), Array.Empty<RhythmCluster>());
        }

        // Merge + sort by UnixMs. Spikes don't carry a UnixMs directly; derive
        // it from the stall ring when timestamps align, otherwise fall back
        // to the tick index converted at 60 tps from session start (we don't
        // have a clean session-start anchor here, so we use tick-derived
        // relative timing for spikes by giving them a synthetic UnixMs of
        // WorstTick * 1000/60. Comparisons are intra-merge only, so the
        // arbitrary origin cancels out.)
        Event[] all = new Event[eventCount];
        int idx = 0;
        for (int i = 0; i < spikes.Count; i++)
        {
            SpikeWindow w = spikes[i];
            int topMod = TopModFromSpike(in w);
            all[idx++] = new Event(w.WorstTick * 1000L / 60L, topMod);
        }
        for (int i = 0; i < stalls.Count; i++)
        {
            StallEvent s = stalls[i];
            all[idx++] = new Event(s.StartTimestampUnixMs, s.C0.ModId);
        }
        Array.Sort(all, static (a, b) => a.UnixMs.CompareTo(b.UnixMs));

        // Bucketise inter-event intervals.
        int[] counts = new int[BucketCount];
        int[][] modBuckets = new int[BucketCount][];  // lazily allocated per bucket

        double logMin = Math.Log(MinSeconds);
        double logMax = Math.Log(MaxSeconds);
        double logSpan = logMax - logMin;

        string[] modNames = HookInterceptor.ProfiledModNames;
        int modCount = modNames.Length;

        for (int i = 1; i < all.Length; i++)
        {
            double dt = (all[i].UnixMs - all[i - 1].UnixMs) / 1000d;
            if (dt <= 0d || double.IsNaN(dt)) continue;
            if (dt < MinSeconds || dt > MaxSeconds) continue;

            double frac = (Math.Log(dt) - logMin) / logSpan;
            int b = (int)(frac * BucketCount);
            if (b < 0) b = 0;
            if (b >= BucketCount) b = BucketCount - 1;
            counts[b]++;

            int mod = all[i].TopModId;
            if (mod >= 0 && mod < modCount)
            {
                int[]? mb = modBuckets[b];
                if (mb == null) { mb = new int[modCount]; modBuckets[b] = mb; }
                mb[mod]++;
            }
        }

        // Emit non-zero histogram buckets.
        List<RhythmHistogramBucket> hist = new();
        for (int b = 0; b < BucketCount; b++)
        {
            if (counts[b] == 0) continue;
            double centre = BucketCentreSeconds(b, logMin, logSpan);
            hist.Add(new RhythmHistogramBucket(centre, counts[b]));
        }

        // Mean + stddev across all (incl. zero) buckets — empty buckets are
        // information about absence of recurrence, so they belong in the
        // distribution.
        double mean = 0d;
        for (int b = 0; b < BucketCount; b++) mean += counts[b];
        mean /= BucketCount;

        double variance = 0d;
        for (int b = 0; b < BucketCount; b++)
        {
            double d = counts[b] - mean;
            variance += d * d;
        }
        variance /= BucketCount;
        double stddev = Math.Sqrt(variance);
        double threshold = mean + PeakSigma * stddev;
        if (threshold < 1d) threshold = 1d;  // never call a single event a peak

        // 3-cell moving-max peak detector. A bucket is a peak iff:
        //   * count strictly exceeds the threshold, AND
        //   * count >= each of its two neighbours.
        List<RhythmCluster> clusters = new();
        for (int b = 0; b < BucketCount && clusters.Count < MaxClusters; b++)
        {
            int v = counts[b];
            if (v <= threshold) continue;
            int left = b > 0 ? counts[b - 1] : 0;
            int right = b < BucketCount - 1 ? counts[b + 1] : 0;
            if (v < left || v < right) continue;

            // Width = ±2 bucket widths. Sum counts and per-mod tallies across
            // the window.
            int from = Math.Max(0, b - 2);
            int to = Math.Min(BucketCount - 1, b + 2);
            int windowCount = 0;
            int[] modTally = new int[modCount];
            for (int k = from; k <= to; k++)
            {
                windowCount += counts[k];
                int[]? mb = modBuckets[k];
                if (mb == null) continue;
                for (int m = 0; m < modCount; m++) modTally[m] += mb[m];
            }

            int topModId = -1;
            int topModCount = 0;
            for (int m = 0; m < modCount; m++)
            {
                if (modTally[m] > topModCount) { topModCount = modTally[m]; topModId = m; }
            }
            double topShare = windowCount > 0 ? (double)topModCount / windowCount : 0d;
            string topName = topModId >= 0 && topModId < modNames.Length ? modNames[topModId] : "—";

            double centre = BucketCentreSeconds(b, logMin, logSpan);
            double widthLo = BucketCentreSeconds(from, logMin, logSpan);
            double widthHi = BucketCentreSeconds(to, logMin, logSpan);
            double width = (widthHi - widthLo) / 2d;

            double sessionShare = eventCount > 0 ? (double)windowCount / eventCount : 0d;

            clusters.Add(new RhythmCluster(
                CentreSeconds: centre,
                WidthSeconds: width,
                EventCount: windowCount,
                ShareOfSession: sessionShare,
                TopModId: topModId,
                TopModName: topName,
                TopModShare: topShare));
        }

        return new LagRhythmSnapshot(worldLoaded: true, hist, clusters);
    }

    public object CurrentSnapshotBoxed() => CurrentSnapshot();

    private static double BucketCentreSeconds(int bucket, double logMin, double logSpan)
    {
        double frac = (bucket + 0.5) / BucketCount;
        return Math.Exp(logMin + frac * logSpan);
    }

    private static int TopModFromSpike(in SpikeWindow w)
    {
        int cats = PerModAttribution.CategoryCount;
        if (w.PerModCatMs == null || cats <= 0) return -1;
        int modCount = w.PerModCatMs.Length / cats;
        int topId = -1;
        double topSum = 0d;
        for (int mod = 0; mod < modCount; mod++)
        {
            double sum = 0d;
            int baseIdx = mod * cats;
            for (int k = 0; k < cats; k++) sum += w.PerModCatMs[baseIdx + k];
            if (sum > topSum) { topSum = sum; topId = mod; }
        }
        return topId;
    }

    private readonly struct Event
    {
        public readonly long UnixMs;
        public readonly int TopModId;
        public Event(long unixMs, int topModId) { UnixMs = unixMs; TopModId = topModId; }
    }
}
