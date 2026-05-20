#nullable enable

using System;
using System.Collections.Generic;
using LiteDB;
using PerformanceProfiler.Profiling.Persistence;
using PerformanceProfiler.Profiling.Persistence.Records;

namespace PerformanceProfiler.Profiling.Segments;

/// <summary>
/// Holds the most-recently-closed segments in memory for the Timeline tab,
/// and pushes each new closed segment to the DB writer thread plus the
/// retrospective toast queue.
///
/// <para>
/// Three responsibilities, kept here so the detector stays pure logic:
/// </para>
/// <list type="bullet">
///   <item>Live ring of recent closed segments (newest-first), capped so the
///         Timeline tab never traverses unbounded history at draw time.
///         Older segments are read on demand from LiteDB.</item>
///   <item>DB write enqueue via <see cref="DbWriterThread"/>. Idempotent;
///         the writer's journal handles replay.</item>
///   <item>Promotion-driven toast queue. A consumer (the overlay) drains
///         pending toasts and renders them.</item>
/// </list>
///
/// <para>
/// Lifetime aggregates (avg-ms-per-tick per (family, key)) are maintained
/// here too — incrementally folded on each close. The promoter consults
/// these to decide on the outlier card.
/// </para>
/// </summary>
internal sealed class SegmentStore : ISegmentSink
{
    private const int LiveRingCapacity = 64;

    private readonly ProfilerDatabase? _db;
    private readonly Action<string>? _log;

    private readonly LinkedList<Segment> _recent = new();
    private readonly Queue<Segment> _pendingToasts = new();
    private readonly Dictionary<long, LifetimeStat> _lifetime = new();

    public SegmentStore(ProfilerDatabase? db, Action<string>? log = null)
    {
        _db = db;
        _log = log;
        SeedLifetimeFromDb();
    }

    /// <summary>Number of segments closed during this session (in-memory ring + dropped older).</summary>
    public int SessionClosedCount { get; private set; }

    /// <summary>Recent closed-segments, newest first. Snapshot of internal ring; safe to iterate.</summary>
    public IReadOnlyCollection<Segment> Recent => _recent;

    /// <summary>Try-dequeue a freshly-promoted segment as a pending toast. Returns false when the queue is empty.</summary>
    public bool TryDequeueToast(out Segment? segment)
    {
        if (_pendingToasts.Count == 0) { segment = null; return false; }
        segment = _pendingToasts.Dequeue();
        return true;
    }

    /// <summary>Lifetime average frame-ms-per-tick for (family, key). 0 if no history.</summary>
    public double LifetimeAvgMsPerTick(SegmentFamily family, int key)
    {
        long composite = ((long)(byte)family << 56) | (uint)key;
        return _lifetime.TryGetValue(composite, out LifetimeStat stat) ? stat.AvgMsPerTick : 0d;
    }

    /// <summary>Lifetime sample count for (family, key).</summary>
    public int LifetimeSampleCount(SegmentFamily family, int key)
    {
        long composite = ((long)(byte)family << 56) | (uint)key;
        return _lifetime.TryGetValue(composite, out LifetimeStat stat) ? stat.Samples : 0;
    }

    /// <inheritdoc/>
    void ISegmentSink.OnSegmentClosed(Segment segment, OpenSegment source)
    {
        SessionClosedCount++;

        // Promote against lifetime stats BEFORE we fold the new sample, so the
        // current segment is compared to past sessions, not to itself.
        double priorAvg = LifetimeAvgMsPerTick(segment.Family, segment.Key);
        int priorSamples = LifetimeSampleCount(segment.Family, segment.Key);
        SegmentPromoter.PromotionResult promotion = SegmentPromoter.Decide(segment, priorAvg, priorSamples);

        Segment finalSeg = promotion.Promote
            ? segment with { Promoted = true, PromotionReason = promotion.Reason }
            : segment;

        // Live ring: newest first, cap at LiveRingCapacity.
        _recent.AddFirst(finalSeg);
        while (_recent.Count > LiveRingCapacity) _recent.RemoveLast();

        if (finalSeg.Promoted) _pendingToasts.Enqueue(finalSeg);

        // Fold into lifetime stats (incremental running-mean over ms-per-tick).
        FoldLifetime(finalSeg);

        // Persist.
        try
        {
            _db?.Writer.Enqueue(DbWriteOp.Segment(ToRow(finalSeg)));
        }
        catch (Exception ex)
        {
            _log?.Invoke($"SegmentStore: failed to enqueue segment write ({ex.GetType().Name}: {ex.Message})");
        }
    }

    private void FoldLifetime(Segment seg)
    {
        long composite = ((long)(byte)seg.Family << 56) | (uint)seg.Key;
        if (!_lifetime.TryGetValue(composite, out LifetimeStat stat))
        {
            stat = new LifetimeStat();
        }
        stat.Samples++;
        stat.TotalTicks += seg.Ticks;
        stat.TotalFrameMs += seg.TotalFrameMs;
        _lifetime[composite] = stat;
    }

    private void SeedLifetimeFromDb()
    {
        if (_db == null) return;
        try
        {
            foreach (SegmentRow row in _db.Segments.FindAll())
            {
                long composite = ((long)row.Family << 56) | (uint)row.Key;
                if (!_lifetime.TryGetValue(composite, out LifetimeStat stat)) stat = new LifetimeStat();
                stat.Samples++;
                stat.TotalTicks += row.Ticks;
                stat.TotalFrameMs += row.TotalFrameMs;
                _lifetime[composite] = stat;
            }
        }
        catch (Exception ex)
        {
            _log?.Invoke($"SegmentStore: lifetime seed failed ({ex.GetType().Name}: {ex.Message}); proceeding empty");
        }
    }

    private static SegmentRow ToRow(Segment seg)
    {
        var row = new SegmentRow
        {
            Id = seg.Id == ObjectId.Empty ? ObjectId.NewObjectId() : seg.Id,
            SessionId = seg.SessionId,
            Family = (byte)seg.Family,
            Key = seg.Key,
            Name = seg.Name,
            StartTick = seg.StartTick,
            EndTick = seg.EndTick,
            StartUnixMs = seg.StartUnixMs,
            EndUnixMs = seg.EndUnixMs,
            DurationMs = seg.DurationMs,
            Ticks = seg.Ticks,
            TotalFrameMs = seg.TotalFrameMs,
            SpikeCount = seg.SpikeCount,
            StallCount = seg.StallCount,
            DeathCount = seg.DeathCount,
            BossKillCount = seg.BossKillCount,
            Promoted = seg.Promoted,
            PromotionReason = seg.PromotionReason,
        };
        for (int i = 0; i < seg.ModIds.Length; i++)
        {
            row.ModIds.Add(seg.ModIds[i]);
            row.ModMs.Add(seg.ModMs[i]);
        }
        return row;
    }

    private struct LifetimeStat
    {
        public int Samples;
        public long TotalTicks;
        public double TotalFrameMs;
        public readonly double AvgMsPerTick => TotalTicks > 0 ? TotalFrameMs / TotalTicks : 0d;
    }
}
