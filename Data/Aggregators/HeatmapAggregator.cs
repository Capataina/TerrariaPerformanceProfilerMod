#nullable enable

using System;
using System.Collections.Generic;
using Terraria.ModLoader;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Segments;
using PerformanceProfiler.Profiling.Persistence;

namespace PerformanceProfiler.Data.Aggregators;

/// <summary>One minute of session frame-time data, plus the worst single frame in that minute.</summary>
public readonly struct HeatmapBucket
{
    public readonly long StartUnixMs;
    public readonly int Ticks;
    public readonly double AvgMs;
    public readonly double WorstMs;

    public HeatmapBucket(long startUnixMs, int ticks, double avgMs, double worstMs)
    {
        StartUnixMs = startUnixMs;
        Ticks = ticks;
        AvgMs = avgMs;
        WorstMs = worstMs;
    }
}

/// <summary>A boss-segment span used for the heatmap's red-halo overlay.</summary>
public readonly struct HeatmapBossOverlay
{
    public readonly string Name;
    public readonly long StartUnixMs;
    public readonly long EndUnixMs;
    public readonly bool Killed;

    public HeatmapBossOverlay(string name, long startUnixMs, long endUnixMs, bool killed)
    {
        Name = name; StartUnixMs = startUnixMs; EndUnixMs = endUnixMs; Killed = killed;
    }
}

/// <summary>Snapshot of the per-minute heatmap data + boss overlays.</summary>
public readonly struct HeatmapSnapshot
{
    public readonly bool WorldLoaded;
    public readonly long BucketMs;
    public readonly IReadOnlyList<HeatmapBucket> Buckets;
    public readonly IReadOnlyList<HeatmapBossOverlay> BossOverlays;

    public HeatmapSnapshot(bool worldLoaded, long bucketMs,
        IReadOnlyList<HeatmapBucket> buckets, IReadOnlyList<HeatmapBossOverlay> bossOverlays)
    {
        WorldLoaded = worldLoaded;
        BucketMs = bucketMs;
        Buckets = buckets;
        BossOverlays = bossOverlays;
    }

    public static readonly HeatmapSnapshot Empty = new HeatmapSnapshot(
        false, 60_000L, Array.Empty<HeatmapBucket>(), Array.Empty<HeatmapBossOverlay>());
}

/// <summary>
/// Aggregator that buckets per-tick frame data into 60-second minutes and
/// joins boss-segment overlays. Replaces the inline bucketing that used
/// to live in <c>DashboardRouter.BuildHeatmap</c> — the canonical
/// "kill the inline math" extraction (see
/// <c>context/plans/unified-data-pipeline.md §5 step 5</c>).
///
/// <para>
/// Pulls from <see cref="ProfilerDatabase.TickAggregatesWarm"/> when the
/// DB is open (covers the full session); falls back to the in-memory
/// 30-second rolling history when not.
/// </para>
///
/// <para>
/// OnDemand cadence — the snapshot is computed fresh on every dashboard
/// poll (~3 s in practice). Cheap because the DB query is indexed on
/// SessionId.
/// </para>
/// </summary>
public sealed class HeatmapAggregator : IDataAggregator<HeatmapSnapshot>
{
    public const string StreamName = "heatmap";
    public const long BucketMs = 60_000L;

    public string Name => StreamName;
    public DataStreamCadence Cadence => DataStreamCadence.OnDemand;
    public DataStage Stage => DataStage.Aggregator;

    public void Initialise(SessionContext session) { }
    public void Reset() { }
    public void Dispose() { }

    public HeatmapSnapshot CurrentSnapshot()
    {
        ProfilerSystem? sys = ModContent.GetInstance<ProfilerSystem>();
        MetricCollector? c = sys?.Collector;
        SegmentStore? store = sys?.SegmentStore;
        SegmentDetector? det = sys?.Segments;
        if (c == null || c.History.Count == 0) return HeatmapSnapshot.Empty;

        var buckets = BucketFromDb(sys) ?? BucketFromMemory(c);

        var overlays = new List<HeatmapBossOverlay>();
        if (store != null)
        {
            foreach (var s in store.Recent)
            {
                if (s.Family != SegmentFamily.Boss) continue;
                overlays.Add(new HeatmapBossOverlay(s.Name, s.StartUnixMs, s.EndUnixMs, s.BossKillCount > 0));
            }
        }
        if (det != null)
        {
            long nowUnix = Time.UnixMsNow();
            foreach (var os in det.OpenSegments)
            {
                if (os.Family != SegmentFamily.Boss) continue;
                overlays.Add(new HeatmapBossOverlay(os.Name, os.StartUnixMs, nowUnix, killed: false));
            }
        }

        return new HeatmapSnapshot(worldLoaded: true, BucketMs, buckets, overlays);
    }

    public object CurrentSnapshotBoxed() => CurrentSnapshot();

    private static List<HeatmapBucket>? BucketFromDb(ProfilerSystem? sys)
    {
        var db = PerformanceProfiler.Database;
        var sid = sys?.LiveRecorderSessionId;
        if (db == null || sid == null) return null;

        try
        {
            long sessionStart = Time.UnixMsNow();
            var sessionRow = db.Sessions.FindById(sid);
            if (sessionRow != null && sessionRow.StartedUtc.Year > 2000)
            {
                // SessionRow.StartedUtc reads back from LiteDB with Kind=Unspecified;
                // DateTimeOffset's ctor with TimeSpan.Zero offset rejects that, so
                // force-tag the kind before constructing.
                var utc = DateTime.SpecifyKind(sessionRow.StartedUtc, DateTimeKind.Utc);
                sessionStart = new DateTimeOffset(utc).ToUnixTimeMilliseconds();
            }

            var result = new List<HeatmapBucket>();
            long bucketSecond = -1L;
            long curStart = 0L;
            int curTicks = 0;
            double curTotalMs = 0d;
            double curWorstMs = 0d;
            using var rows = db.TickAggregatesWarm.Find(x => x.SessionId == sid).GetEnumerator();
            while (rows.MoveNext())
            {
                var row = rows.Current;
                long minuteSecond = (row.SecondIndex / 60L) * 60L;
                if (minuteSecond != bucketSecond)
                {
                    if (bucketSecond >= 0L)
                    {
                        double avg = curTicks > 0 ? curTotalMs / curTicks : 0d;
                        result.Add(new HeatmapBucket(curStart, curTicks, avg, curWorstMs));
                    }
                    bucketSecond = minuteSecond;
                    curStart = sessionStart + minuteSecond * 1000L;
                    curTicks = 0;
                    curTotalMs = 0d;
                    curWorstMs = 0d;
                }
                curTicks += 60; // 1 warm row ≈ 60 ticks, approximation
                curTotalMs += row.AvgFrameMs * 60d;
                if (row.P95FrameMs > curWorstMs) curWorstMs = row.P95FrameMs;
            }
            if (bucketSecond >= 0L)
            {
                double avg = curTicks > 0 ? curTotalMs / curTicks : 0d;
                result.Add(new HeatmapBucket(curStart, curTicks, avg, curWorstMs));
            }
            return result.Count > 0 ? result : null;
        }
        catch
        {
            return null;
        }
    }

    private static List<HeatmapBucket> BucketFromMemory(MetricCollector c)
    {
        var result = new List<HeatmapBucket>();
        long bucketStart = -1L;
        int curTicks = 0;
        double curTotalMs = 0d;
        double curWorstMs = 0d;
        for (int i = 0; i < c.History.Count; i++)
        {
            var tf = c.History[i];
            long approxUnix = Time.UnixMsNow() - (c.History.Count - 1 - i) * 1000 / 60;
            long bs = (approxUnix / BucketMs) * BucketMs;
            if (bs != bucketStart)
            {
                if (bucketStart >= 0L)
                {
                    double avg = curTicks > 0 ? curTotalMs / curTicks : 0d;
                    result.Add(new HeatmapBucket(bucketStart, curTicks, avg, curWorstMs));
                }
                bucketStart = bs;
                curTicks = 0;
                curTotalMs = 0d;
                curWorstMs = 0d;
            }
            curTicks++;
            curTotalMs += tf.FrameTimeMs;
            if (tf.FrameTimeMs > curWorstMs) curWorstMs = tf.FrameTimeMs;
        }
        if (bucketStart >= 0L)
        {
            double avg = curTicks > 0 ? curTotalMs / curTicks : 0d;
            result.Add(new HeatmapBucket(bucketStart, curTicks, avg, curWorstMs));
        }
        return result;
    }
}
