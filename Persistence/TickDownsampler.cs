#nullable enable

using System;
using LiteDB;
using PerformanceProfiler.Persistence.Records;

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Persistence.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
namespace PerformanceProfiler.Persistence;

/// <summary>
/// Turns per-tick frame data into the 1 Hz warm rows and 1 / minute cold rows
/// the DB retains. Tier story per §4 of the migration plan: raw 60 Hz never
/// reaches disk; warm tier is summarised seconds; cold tier is summarised
/// minutes. Both arrays sit on the writer queue, never on the game thread's
/// blocking path.
///
/// The downsampler is allocation-quiet on the game thread by design: rolling
/// percentile state lives in pre-sized arrays; only the per-emit row carries
/// fresh allocations and emits run once per second / minute, not per tick.
/// </summary>
public sealed class TickDownsampler
{
    public const int TicksPerSecond = 60;
    public const int TicksPerMinute = 60 * 60;

    private readonly RollingFrame _second = new RollingFrame(TicksPerSecond);
    private readonly RollingFrame _minute = new RollingFrame(TicksPerMinute);
    private long _lastSecondEmitted = -1L;
    private long _lastMinuteEmitted = -1L;

    /// <summary>Number of warm rows emitted so far this session.</summary>
    public long WarmEmitted { get; private set; }

    /// <summary>Number of cold rows emitted so far this session.</summary>
    public long ColdEmitted { get; private set; }

    /// <summary>
    /// Called once per closed tick from the game thread. Pushes the tick's
    /// frame stats into the rolling windows and, on bucket boundaries,
    /// enqueues a warm / cold aggregate op to the writer.
    /// </summary>
    public void OnTickCommitted(TickFrame frame, MetricCollector collector, DbWriterThread writer, ObjectId sessionId)
    {
        _second.Push(frame.FrameTimeMs, frame.GcTimeMs);
        _minute.Push(frame.FrameTimeMs, frame.GcTimeMs);

        long secondIndex = frame.TickIndex / TicksPerSecond;
        if (secondIndex != _lastSecondEmitted && _second.Count > 0)
        {
            _lastSecondEmitted = secondIndex;
            EmitWarm(secondIndex, collector, writer, sessionId);
        }

        long minuteIndex = frame.TickIndex / TicksPerMinute;
        if (minuteIndex != _lastMinuteEmitted && _minute.Count > 0)
        {
            _lastMinuteEmitted = minuteIndex;
            EmitCold(minuteIndex, collector, writer, sessionId);
        }
    }

    private void EmitWarm(long secondIndex, MetricCollector collector, DbWriterThread writer, ObjectId sessionId)
    {
        var row = new TickAggregateWarm
        {
            SessionId = sessionId,
            SecondIndex = secondIndex,
            AvgFrameMs = _second.Average,
            P95FrameMs = _second.P95,
            GcMs = _second.GcAverage,
            PerModMs = ProjectPerMod(collector.PerModCategoryMs, collector),
            PerModBytes = collector.TracksAllocations
                ? ProjectPerMod(collector.PerModCategoryBytes!, collector)
                : null,
            ExpireAtUtc = DateTime.UtcNow + ProfilerDatabase.WarmRetention,
        };
        writer.Enqueue(DbWriteOp.WarmAggregate(row));
        WarmEmitted++;
    }

    private void EmitCold(long minuteIndex, MetricCollector collector, DbWriterThread writer, ObjectId sessionId)
    {
        var row = new TickAggregateCold
        {
            SessionId = sessionId,
            MinuteIndex = minuteIndex,
            AvgFrameMs = _minute.Average,
            P95FrameMs = _minute.P95,
            MaxFrameMs = _minute.Max,
            GcMs = _minute.GcAverage,
            PerModMs = ProjectPerMod(collector.PerModCategoryMs, collector),
            PerModBytes = collector.TracksAllocations
                ? ProjectPerMod(collector.PerModCategoryBytes!, collector)
                : null,
        };
        writer.Enqueue(DbWriteOp.ColdAggregate(row));
        ColdEmitted++;
    }

    /// <summary>
    /// Collapse the per-mod-per-category array into a per-mod-only list by
    /// summing categories. The warm/cold rows don't need the full category
    /// breakdown — that lives on the per-session archive aggregate.
    /// </summary>
    private static System.Collections.Generic.List<double> ProjectPerMod(
        System.Collections.Generic.IReadOnlyList<double> perModCategory,
        MetricCollector collector)
    {
        int categoryCount = PerModAttribution.CategoryCount;
        int modCount = PerModAttribution.ModCount;
        var result = new System.Collections.Generic.List<double>(modCount);
        for (int modId = 0; modId < modCount; modId++)
        {
            double sum = 0d;
            int offset = modId * categoryCount;
            for (int cat = 0; cat < categoryCount; cat++)
            {
                int idx = offset + cat;
                if (idx < perModCategory.Count) sum += perModCategory[idx];
            }
            result.Add(sum);
        }
        return result;
    }

    /// <summary>
    /// Bounded rolling window over the last N frames. Holds frame ms +
    /// GC ms for averaging and percentile estimation. Allocation-quiet at
    /// steady state — the buffer is sized once.
    /// </summary>
    private sealed class RollingFrame
    {
        private readonly double[] _frames;
        private readonly double[] _gc;
        private int _head;
        private int _count;
        private double _sum;
        private double _gcSum;
        private double _max;

        public RollingFrame(int capacity)
        {
            _frames = new double[capacity];
            _gc = new double[capacity];
        }

        public int Count => _count;
        public double Average => _count > 0 ? _sum / _count : 0d;
        public double GcAverage => _count > 0 ? _gcSum / _count : 0d;
        public double Max => _max;

        // Recompute _max from the current ring contents. Called when an
        // outgoing sample held the current max — otherwise stale.
        private void RecomputeMax()
        {
            double m = 0d;
            for (int i = 0; i < _count; i++) if (_frames[i] > m) m = _frames[i];
            _max = m;
        }

        public double P95
        {
            get
            {
                if (_count == 0) return 0d;
                // Cheap O(n) approximation for a small window: copy + sort.
                // 60 samples for second; 3600 for minute. The minute window
                // copies 3600 doubles once a minute (sub-millisecond).
                double[] scratch = new double[_count];
                for (int i = 0; i < _count; i++) scratch[i] = _frames[i];
                Array.Sort(scratch);
                int idx = Math.Min(_count - 1, (int)Math.Floor(_count * 0.95));
                return scratch[idx];
            }
        }

        public void Push(double frameMs, double gcMs)
        {
            if (_count < _frames.Length)
            {
                _frames[_head] = frameMs;
                _gc[_head] = gcMs;
                _sum += frameMs;
                _gcSum += gcMs;
                _count++;
            }
            else
            {
                // Window full; the slot at _head is the eviction.
                double evicted = _frames[_head];
                _sum += frameMs - evicted;
                _gcSum += gcMs - _gc[_head];
                _frames[_head] = frameMs;
                _gc[_head] = gcMs;
                // If we just evicted the holder of the current max, we
                // have to rescan; otherwise the max is still correct.
                bool maxEvicted = evicted >= _max;
                if (frameMs > _max) _max = frameMs;
                else if (maxEvicted) RecomputeMax();
                _head++;
                if (_head == _frames.Length) _head = 0;
                return;
            }
            if (frameMs > _max) _max = frameMs;
            _head++;
            if (_head == _frames.Length) _head = 0;
        }
    }
}
