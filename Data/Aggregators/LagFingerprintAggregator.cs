#nullable enable

using System;
using System.Collections.Generic;
using Terraria.ModLoader;

using PerformanceProfiler.Data.Contracts;
using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;

namespace PerformanceProfiler.Data.Aggregators;

/// <summary>
/// L1+L2 — fingerprint clustering of spike + stall events plus the cause×context
/// cell matrix.
///
/// <para>
/// Walks the live <see cref="MetricCollector.Spikes"/> and
/// <see cref="MetricCollector.Stalls"/> rings, hashes each event into a
/// (cause-class, top-mod, biome-bit, weather-flags, hardmode) fingerprint,
/// and rolls per-cluster stats up against that hash. The same walk emits the
/// cause×context cell matrix consumed by the Lag tab's heat ledger.
/// </para>
///
/// <para>
/// <b>Honest limitation:</b> per-event <see cref="EventContext"/> is not
/// tracked yet — <c>SpikeWindow</c> and <c>StallEvent</c> only store their
/// per-mod attribution snapshot, not the biome/weather/hardmode tuple that
/// was live at the moment the event fired. Until the detectors are extended
/// to capture context at-event-time, every event's PrimaryBiome and
/// WeatherFlags fall back to "—" / "" and Hardmode reads the current
/// <see cref="Terraria.Main.hardMode"/> bit at snapshot time. Clusters
/// therefore degenerate to (cause × top-mod × current-hardmode) groupings —
/// useful for surfacing "GC stalls dominated by Mod X" but unable to surface
/// "stalls clustered in Blood Moon" until the context-at-event work lands.
/// </para>
/// </summary>
public sealed class LagFingerprintAggregator : IDataAggregator<LagClusterSnapshot>
{
    /// <summary>Hard cap on clusters returned per snapshot.</summary>
    public const int MaxClusters = 64;

    /// <summary>Hard cap on cause×context cells returned per snapshot.</summary>
    public const int MaxCells = 128;

    public string Name => RolloutStreamNames.LagClusters;
    public DataStreamCadence Cadence => DataStreamCadence.OnDemand;
    public DataStage Stage => DataStage.Aggregator;

    public void Initialise(SessionContext session) { }
    public void Reset() { }
    public void Dispose() { }

    public LagClusterSnapshot CurrentSnapshot()
    {
        MetricCollector? c = ModContent.GetInstance<ProfilerSystem>()?.Collector;
        if (c == null) return LagClusterSnapshot.Empty;

        IReadOnlyList<SpikeWindow> spikes = c.Spikes;
        IReadOnlyList<StallEvent> stalls = c.Stalls;
        if (spikes.Count == 0 && stalls.Count == 0)
        {
            return new LagClusterSnapshot(worldLoaded: true,
                Array.Empty<LagCluster>(), Array.Empty<CauseContextCell>());
        }

        // Current hardmode bit + a "no context tracked" placeholder. See
        // class doc-comment for the honest-limitation note.
        bool hardmode;
        try { hardmode = Terraria.Main.hardMode; }
        catch { hardmode = false; }
        const string PrimaryBiome = "—";
        const string WeatherFlags = "";
        const string Context = "—";

        string[] modNames = HookInterceptor.ProfiledModNames;

        Dictionary<string, ClusterAccumulator> clusters = new();
        Dictionary<(string cause, string ctx), CellAccumulator> cells = new();

        // ----- Spikes --------------------------------------------------------
        for (int i = 0; i < spikes.Count; i++)
        {
            SpikeWindow w = spikes[i];
            (int topModId, double topModMs, double totalModMs) = TopModFromSpike(in w, modNames.Length);
            string causeClass = "Spike";
            double durationMs = w.WorstFrameMs;

            string fp = MakeFingerprint(causeClass, topModId, PrimaryBiome, WeatherFlags, hardmode);
            Accumulate(clusters, fp, causeClass, topModId, topModMs, totalModMs,
                PrimaryBiome, WeatherFlags, hardmode, durationMs, modNames);

            AccumulateCell(cells, causeClass, Context, durationMs);
        }

        // ----- Stalls --------------------------------------------------------
        for (int i = 0; i < stalls.Count; i++)
        {
            StallEvent s = stalls[i];
            string causeClass = EnumStringTable.CauseName(s.Cause);
            int topModId = s.C0.ModId;
            double topModMs = s.C0.RecentMs;
            double totalModMs = SumStallContribs(in s);
            double durationMs = s.TickPeriodMs;

            string fp = MakeFingerprint(causeClass, topModId, PrimaryBiome, WeatherFlags, hardmode);
            Accumulate(clusters, fp, causeClass, topModId, topModMs, totalModMs,
                PrimaryBiome, WeatherFlags, hardmode, durationMs, modNames);

            AccumulateCell(cells, causeClass, Context, durationMs);
        }

        // ----- Finalise clusters: compute P95 + share + project -------------
        List<LagCluster> outClusters = new(clusters.Count);
        foreach (var kv in clusters)
        {
            ClusterAccumulator acc = kv.Value;
            double p95 = Percentile(acc.Durations, 0.95);
            double share = acc.TotalModMs > 0d ? acc.TopModMs / acc.TotalModMs : 0d;
            string topName = (acc.TopModId >= 0 && acc.TopModId < modNames.Length)
                ? modNames[acc.TopModId] : "—";
            outClusters.Add(new LagCluster(
                FingerprintId: kv.Key,
                CauseClass: acc.CauseClass,
                PrimaryBiome: acc.PrimaryBiome,
                WeatherFlags: acc.WeatherFlags,
                Hardmode: acc.Hardmode,
                TopModId: acc.TopModId,
                TopModName: topName,
                TopModShare: share,
                EventCount: acc.EventCount,
                P95Ms: p95,
                TotalMs: acc.TotalMs));
        }

        // Sort by EventCount desc, TotalMs desc as tiebreak; cap.
        outClusters.Sort((a, b) =>
        {
            int byCount = b.EventCount.CompareTo(a.EventCount);
            if (byCount != 0) return byCount;
            return b.TotalMs.CompareTo(a.TotalMs);
        });
        if (outClusters.Count > MaxClusters) outClusters.RemoveRange(MaxClusters, outClusters.Count - MaxClusters);

        List<CauseContextCell> outCells = new(cells.Count);
        foreach (var kv in cells)
        {
            outCells.Add(new CauseContextCell(
                CauseClass: kv.Key.cause,
                Context: kv.Key.ctx,
                EventCount: kv.Value.EventCount,
                TotalMs: kv.Value.TotalMs));
        }
        outCells.Sort((a, b) => b.EventCount.CompareTo(a.EventCount));
        if (outCells.Count > MaxCells) outCells.RemoveRange(MaxCells, outCells.Count - MaxCells);

        return new LagClusterSnapshot(worldLoaded: true, outClusters, outCells);
    }

    public object CurrentSnapshotBoxed() => CurrentSnapshot();

    // ---- Helpers ------------------------------------------------------------

    private static (int topModId, double topModMs, double totalModMs) TopModFromSpike(
        in SpikeWindow w, int modNameCount)
    {
        int cats = PerModAttribution.CategoryCount;
        if (w.PerModCatMs == null || cats <= 0) return (-1, 0d, 0d);
        int modCount = w.PerModCatMs.Length / cats;
        int topId = -1;
        double topSum = 0d;
        double total = 0d;
        for (int mod = 0; mod < modCount; mod++)
        {
            double sum = 0d;
            int baseIdx = mod * cats;
            for (int k = 0; k < cats; k++) sum += w.PerModCatMs[baseIdx + k];
            total += sum;
            if (sum > topSum) { topSum = sum; topId = mod; }
        }
        return (topId, topSum, total);
    }

    private static double SumStallContribs(in StallEvent s)
    {
        double total = 0d;
        if (s.C0.ModId >= 0) total += s.C0.RecentMs;
        if (s.C1.ModId >= 0) total += s.C1.RecentMs;
        if (s.C2.ModId >= 0) total += s.C2.RecentMs;
        if (s.C3.ModId >= 0) total += s.C3.RecentMs;
        if (s.C4.ModId >= 0) total += s.C4.RecentMs;
        return total;
    }

    private static string MakeFingerprint(string causeClass, int topModId,
        string primaryBiome, string weatherFlags, bool hardmode)
        => $"{causeClass}|m{topModId}|{primaryBiome}|{weatherFlags}|h{(hardmode ? 1 : 0)}";

    private static void Accumulate(Dictionary<string, ClusterAccumulator> clusters, string fp,
        string causeClass, int topModId, double topModMs, double totalModMs,
        string primaryBiome, string weatherFlags, bool hardmode,
        double durationMs, string[] modNames)
    {
        if (!clusters.TryGetValue(fp, out ClusterAccumulator? acc))
        {
            acc = new ClusterAccumulator
            {
                CauseClass = causeClass,
                TopModId = topModId,
                PrimaryBiome = primaryBiome,
                WeatherFlags = weatherFlags,
                Hardmode = hardmode,
                Durations = new List<double>(4),
            };
            clusters[fp] = acc;
        }
        acc.EventCount++;
        acc.TotalMs += durationMs;
        acc.TopModMs += topModMs;
        acc.TotalModMs += totalModMs;
        acc.Durations.Add(durationMs);
    }

    private static void AccumulateCell(Dictionary<(string, string), CellAccumulator> cells,
        string causeClass, string context, double durationMs)
    {
        var key = (causeClass, context);
        if (!cells.TryGetValue(key, out CellAccumulator? cell))
        {
            cell = new CellAccumulator();
            cells[key] = cell;
        }
        cell.EventCount++;
        cell.TotalMs += durationMs;
    }

    private static double Percentile(List<double> values, double q)
    {
        if (values.Count == 0) return 0d;
        if (values.Count == 1) return values[0];
        values.Sort();
        double idx = q * (values.Count - 1);
        int lo = (int)Math.Floor(idx);
        int hi = (int)Math.Ceiling(idx);
        if (lo == hi) return values[lo];
        double frac = idx - lo;
        return values[lo] * (1d - frac) + values[hi] * frac;
    }

    private sealed class ClusterAccumulator
    {
        public string CauseClass = "";
        public int TopModId;
        public string PrimaryBiome = "";
        public string WeatherFlags = "";
        public bool Hardmode;
        public int EventCount;
        public double TotalMs;
        public double TopModMs;
        public double TotalModMs;
        public List<double> Durations = new();
    }

    private sealed class CellAccumulator
    {
        public int EventCount;
        public double TotalMs;
    }
}
