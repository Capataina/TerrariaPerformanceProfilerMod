#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using LiteDB;
using Terraria.ModLoader;

using PerformanceProfiler.Data.Contracts;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Persistence;
using PerformanceProfiler.Persistence.Records;

namespace PerformanceProfiler.Data.Aggregators;

/// <summary>
/// Timeline T4 — minute-bucketed session activity heat strip.
///
/// <para>
/// Per minute since session start: how many segments closed, how many
/// spike-windows fired, how many stall events fired, and the average
/// frame-ms over the warm-tier ticks in that minute. All four feed one
/// cell of the strip; the renderer picks which dimension drives the
/// colour ramp.
/// </para>
///
/// <para>
/// All four inputs only store tick indices (no UnixMs), so the bucket key
/// is <c>tick / (60 ticks/s × 60 s) = tick / 3600</c>. We anchor the
/// strip's first minute on the session's <see cref="SessionRow.StartedUtc"/>
/// so <see cref="ActivityBucket.UnixMs"/> aligns with the swimlane.
/// </para>
/// </summary>
public sealed class SessionActivityHeatStripAggregator : IDataAggregator<ActivityHeatStripSnapshot>
{
    private const long TicksPerMinute = 60L * 60L;   // 60 ticks/sec * 60 sec

    public string Name => RolloutStreamNames.ActivityHeatStrip;
    public DataStreamCadence Cadence => DataStreamCadence.OnDemand;
    public DataStage Stage => DataStage.Aggregator;

    public void Initialise(SessionContext session) { }
    public void Reset() { }
    public void Dispose() { }

    public ActivityHeatStripSnapshot CurrentSnapshot()
    {
        ProfilerSystem? sys = ModContent.GetInstance<ProfilerSystem>();
        ProfilerDatabase? db = PerformanceProfiler.Database;
        ObjectId? sid = sys?.LiveRecorderSessionId;
        if (db == null || sid == null)
        {
            return ActivityHeatStripSnapshot.Empty;
        }

        long sessionStartUnixMs;
        try
        {
            SessionRow? row = db.Sessions.FindById(sid);
            sessionStartUnixMs = (row != null && row.StartedUtc.Year > 2000)
                ? new DateTimeOffset(DateTime.SpecifyKind(row.StartedUtc, DateTimeKind.Utc)).ToUnixTimeMilliseconds()
                : Time.UnixMsNow();
        }
        catch
        {
            sessionStartUnixMs = Time.UnixMsNow();
        }

        // Sparse minute map; populated by four independent passes then sorted.
        Dictionary<long, Bucket> buckets = new();

        try
        {
            // Segments closed in this session — bucket on StartTick.
            foreach (SegmentRow seg in db.Segments.Find(s => s.SessionId == sid))
            {
                long m = seg.StartTick / TicksPerMinute;
                Get(buckets, m).SegmentCount++;
            }
        }
        catch { /* read-only; degrade gracefully */ }

        try
        {
            // Spike windows — bucket on WorstTick (the player-perceived spike instant).
            foreach (SpikeWindowRow sp in db.SpikeWindows.Find(s => s.SessionId == sid))
            {
                long m = sp.WorstTick / TicksPerMinute;
                Get(buckets, m).SpikeCount++;
            }
        }
        catch { }

        try
        {
            // Stall events — bucket on TickIndex.
            foreach (StallEventRow st in db.Stalls.Find(s => s.SessionId == sid))
            {
                long m = st.TickIndex / TicksPerMinute;
                Get(buckets, m).StallCount++;
            }
        }
        catch { }

        try
        {
            // Frame-ms baseline — warm-tier rows are 1 Hz, so SecondIndex/60 is
            // the minute index. We accumulate AvgFrameMs and divide at emit
            // time so the bucket holds a proper minute-average.
            foreach (TickAggregateWarm w in db.TickAggregatesWarm.Find(w => w.SessionId == sid))
            {
                long m = w.SecondIndex / 60L;
                Bucket b = Get(buckets, m);
                b.FrameMsSum += w.AvgFrameMs;
                b.FrameMsSamples++;
            }
        }
        catch { }

        if (buckets.Count == 0)
        {
            return new ActivityHeatStripSnapshot(worldLoaded: sys?.Collector != null,
                Array.Empty<ActivityBucket>());
        }

        // Sort ascending and project. UnixMs = sessionStart + minute*60000.
        List<ActivityBucket> list = new(buckets.Count);
        foreach (long minute in buckets.Keys.OrderBy(k => k))
        {
            Bucket b = buckets[minute];
            double avg = b.FrameMsSamples > 0 ? b.FrameMsSum / b.FrameMsSamples : 0d;
            list.Add(new ActivityBucket(
                MinuteIndex: minute,
                UnixMs: sessionStartUnixMs + minute * 60_000L,
                SegmentCount: b.SegmentCount,
                SpikeCount: b.SpikeCount,
                StallCount: b.StallCount,
                AvgFrameMs: avg));
        }

        return new ActivityHeatStripSnapshot(worldLoaded: sys?.Collector != null, list);
    }

    public object CurrentSnapshotBoxed() => CurrentSnapshot();

    private static Bucket Get(Dictionary<long, Bucket> map, long key)
    {
        if (!map.TryGetValue(key, out Bucket? b)) { b = new Bucket(); map[key] = b; }
        return b;
    }

    private sealed class Bucket
    {
        public int SegmentCount;
        public int SpikeCount;
        public int StallCount;
        public double FrameMsSum;
        public int FrameMsSamples;
    }
}
