#nullable enable

using System;
using System.Collections.Generic;
using Terraria.ModLoader;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Data.Contracts;

namespace PerformanceProfiler.Data.Aggregators;

/// <summary>
/// F3 — folds per-tick per-mod CPU cost into 1-second buckets and keeps a
/// rolling ring of those buckets. The output is the time series the
/// dashboard renders as per-mod cost-trajectory sparklines and that the
/// mod-interaction correlation matrix (I7) consumes.
///
/// <para>
/// <b>Bucket shape.</b> One bucket = one second of session wall-time
/// (typically 60 ticks at 60 TPS). Each bucket holds an immutable
/// <c>double[ModCount]</c> of summed-across-categories per-mod ms for
/// the ticks that fell inside that second. Buckets, once rolled out of
/// the in-flight slot, are never mutated; <see cref="CurrentSnapshot"/>
/// can hand out direct references without races.
/// </para>
///
/// <para>
/// <b>Hot-path discipline (Invariant 2).</b> <see cref="OnTick"/> runs
/// every tick at 60 Hz. The per-tick working buffer is allocated once at
/// <see cref="Initialise"/>; per-tick work is one indexed walk across
/// <see cref="MetricCollector.PerModCategoryRawMs"/> with no LINQ, no
/// foreach over interfaces, no allocations. A bucket roll-over allocates
/// exactly one <c>double[ModCount]</c> per closed bucket — that is one
/// allocation per second, not per tick.
/// </para>
///
/// <para>
/// <b>Threading.</b> <see cref="OnTick"/> is invoked from the registry's
/// per-tick drive site on the game update thread.
/// <see cref="CurrentSnapshot"/> is OnDemand cadence and may be called
/// from the HTTP worker thread; it reads <see cref="_buckets"/> entries
/// (which are immutable once written) under a short lock that also
/// guards the ring's head/count fields against the per-tick writer.
/// The lock is only contended at snapshot time (~once per dashboard
/// poll, not per tick) and is uncontended in steady state.
/// </para>
/// </summary>
public sealed class PerModCostTimeSeriesAggregator
    : IDataAggregator<ModCostTimeSeriesSnapshot>, IHasPerTickCallback
{
    /// <summary>Default ring capacity in seconds. One hour covers any realistic session.</summary>
    public const int DefaultCapacitySeconds = 3600;

    public string Name => RolloutStreamNames.PerModCostTimeSeries;
    public DataStreamCadence Cadence => DataStreamCadence.PerTick;
    public DataStage Stage => DataStage.Aggregator;

    /// <summary>
    /// The currently-initialised instance the static <see cref="Capture"/>
    /// delegate routes to. Null outside a session. Set in <see cref="Initialise"/>,
    /// cleared in <see cref="Reset"/>. Volatile so the per-tick writer and
    /// the world-load/unload thread agree on visibility without a lock.
    /// </summary>
    private static volatile PerModCostTimeSeriesAggregator? Live;

    /// <summary>Static-delegate per-tick callback. Allocation-free at the call site.</summary>
    public static readonly TickCapture Capture =
        static (in TickContext ctx) => Live?.OnTick(in ctx);

    public TickCapture? PerTickCallback => Capture;

    // --- ring state -----------------------------------------------------
    private readonly int _capacity;
    private readonly ModCostBucket[] _buckets;
    private int _head;     // index where the next closed bucket will be written
    private int _count;    // number of valid buckets currently in the ring

    // --- in-flight bucket (mutated per tick) ----------------------------
    private double[] _currentBucket = Array.Empty<double>();
    private long _currentBucketStartUnixMs;
    private long _currentBucketStartTick;
    private bool _hasCurrentBucket;
    private int _modCount;

    // --- cached references -----------------------------------------------
    private MetricCollector? _collector;

    private readonly object _ringLock = new object();

    public PerModCostTimeSeriesAggregator() : this(DefaultCapacitySeconds) { }

    public PerModCostTimeSeriesAggregator(int capacitySeconds)
    {
        if (capacitySeconds < 1) capacitySeconds = 1;
        _capacity = capacitySeconds;
        _buckets = new ModCostBucket[capacitySeconds];
    }

    public void Initialise(SessionContext session)
    {
        _collector = ModContent.GetInstance<ProfilerSystem>()?.Collector;
        _modCount = PerModAttribution.ModCount;
        _currentBucket = _modCount > 0 ? new double[_modCount] : Array.Empty<double>();
        _hasCurrentBucket = false;
        _currentBucketStartUnixMs = 0L;
        _currentBucketStartTick = 0L;

        lock (_ringLock)
        {
            Array.Clear(_buckets, 0, _buckets.Length);
            _head = 0;
            _count = 0;
        }

        Live = this;
    }

    public void Reset()
    {
        Live = null;
        _collector = null;

        lock (_ringLock)
        {
            Array.Clear(_buckets, 0, _buckets.Length);
            _head = 0;
            _count = 0;
        }

        if (_currentBucket.Length > 0)
        {
            Array.Clear(_currentBucket, 0, _currentBucket.Length);
        }
        _hasCurrentBucket = false;
        _currentBucketStartUnixMs = 0L;
        _currentBucketStartTick = 0L;
        _modCount = 0;
    }

    public void Dispose() { }

    /// <summary>
    /// Per-tick fold. Sums each mod's per-category ms for this tick and
    /// adds the total into the in-flight bucket. Closes the bucket when
    /// one second of wall-time has elapsed.
    /// </summary>
    private void OnTick(in TickContext ctx)
    {
        MetricCollector? c = _collector;
        if (c == null) return;

        IReadOnlyList<double> perCat = c.PerModCategoryRawMs;
        int catCount = PerModAttribution.CategoryCount;
        int modCount = _modCount;
        double[] bucket = _currentBucket;

        if (modCount <= 0 || bucket.Length < modCount) return;

        // Initialise the in-flight bucket on the first tick of the session.
        if (!_hasCurrentBucket)
        {
            _currentBucketStartUnixMs = ctx.UnixMs;
            _currentBucketStartTick = ctx.TickIndex;
            _hasCurrentBucket = true;
        }

        // Sum across categories per mod and add into the bucket.
        // Guarded against shape drift (perCat shorter than expected) without throwing.
        int perCatLen = perCat.Count;
        for (int modId = 0; modId < modCount; modId++)
        {
            int baseIdx = modId * catCount;
            int endIdx = baseIdx + catCount;
            if (endIdx > perCatLen) break;

            double sum = 0d;
            for (int catId = 0; catId < catCount; catId++)
            {
                sum += perCat[baseIdx + catId];
            }
            bucket[modId] += sum;
        }

        // Close the bucket if one second has elapsed. Use `while` so a long
        // pause (debugger break, world-load stall) cannot leave a bucket
        // straddling many seconds — each second-window flushes one bucket.
        while (ctx.UnixMs - _currentBucketStartUnixMs >= 1000L)
        {
            CloseBucket(ctx.TickIndex);
            _currentBucketStartUnixMs += 1000L;
            _currentBucketStartTick = ctx.TickIndex;
        }
    }

    /// <summary>
    /// Allocates an immutable copy of the in-flight bucket and enqueues
    /// it. One allocation per second; zero allocations on per-tick path.
    /// </summary>
    private void CloseBucket(long tickIndex)
    {
        double[] frozen = new double[_modCount];
        Array.Copy(_currentBucket, frozen, _modCount);
        Array.Clear(_currentBucket, 0, _modCount);

        var bucket = new ModCostBucket(_currentBucketStartUnixMs, _currentBucketStartTick, frozen);

        lock (_ringLock)
        {
            _buckets[_head] = bucket;
            _head = (_head + 1) % _capacity;
            if (_count < _capacity) _count++;
        }
    }

    public ModCostTimeSeriesSnapshot CurrentSnapshot()
    {
        if (_collector == null || _modCount == 0) return ModCostTimeSeriesSnapshot.Empty;

        ModCostBucket[] copy;
        int count;
        int head;
        lock (_ringLock)
        {
            count = _count;
            if (count == 0)
            {
                return new ModCostTimeSeriesSnapshot(worldLoaded: true, modCount: _modCount,
                    buckets: Array.Empty<ModCostBucket>());
            }
            head = _head;
            copy = new ModCostBucket[count];
            // Oldest first → newest last. The oldest entry sits at
            // (head - count) mod capacity when the ring is full, or at 0
            // when it isn't.
            int start = count < _capacity ? 0 : head;
            for (int i = 0; i < count; i++)
            {
                copy[i] = _buckets[(start + i) % _capacity];
            }
        }

        return new ModCostTimeSeriesSnapshot(worldLoaded: true, modCount: _modCount, buckets: copy);
    }

    public object CurrentSnapshotBoxed() => CurrentSnapshot();
}
