#nullable enable

using System.Collections.Generic;
using PerformanceProfiler.Insights.Shared;

namespace PerformanceProfiler.Insights.ReferenceFrames;

/// <summary>
/// The Family-A relativity substrate: per game-context, per mod, the running
/// distribution of that mod's cost while the context was active. It answers the
/// conditional question "does this mod cost more when context C is active than the
/// rest of the time?" with an in-context distribution and its complement, so a
/// detector can express the result as an effect size against the comparable
/// baseline (the spine law) rather than an absolute number.
///
/// <para>
/// <b>Bounded by construction (Invariant 2).</b> It is fed once per engine
/// evaluation (1 Hz, already off the game thread), so it adds no per-tick cost at
/// all — the overhead the plan flagged for per-tick reference-frame sampling does
/// not arise here. Storage is capped at <see cref="MaxBuckets"/> contexts × modCount
/// running stats (24 B each); a 16-bucket, 150-mod stack is ~58 KB. When a new
/// context would exceed the cap the least-sampled bucket is evicted and the drop is
/// the caller's to log.
/// </para>
///
/// <para>
/// A "bucket" is an opaque <c>long</c> the caller assembles from a context dimension
/// and value (see <see cref="MakeBucket"/>); this class never interprets game state,
/// keeping it pure logic over declared input (the L1 test axis) and free of any
/// mod-specific or even Terraria-specific knowledge.
/// </para>
/// </summary>
public sealed class ContextBaseline
{
    /// <summary>Cap on simultaneously tracked context buckets; the least-sampled evicts.</summary>
    public const int MaxBuckets = 16;

    /// <summary>
    /// Minimum in-context AND out-of-context samples before a conditional comparison
    /// is even a candidate — the first line against p-hacking (the plan's hard rule:
    /// candidate-gate by real co-occurrence before any statistical test).
    /// </summary>
    public const long MinSamplesForTest = 30;

    private readonly int _modCount;
    private readonly RunningStat[] _global;                       // [modId] session-wide
    private readonly Dictionary<long, RunningStat[]> _byBucket;   // bucketId -> [modId]
    private readonly Dictionary<long, long> _bucketSamples;       // bucketId -> sample count
    private int _evictions;

    public ContextBaseline(int modCount)
    {
        _modCount = modCount < 0 ? 0 : modCount;
        _global = new RunningStat[_modCount];
        _byBucket = new Dictionary<long, RunningStat[]>(MaxBuckets);
        _bucketSamples = new Dictionary<long, long>(MaxBuckets);
    }

    public int ModCount => _modCount;

    /// <summary>How many buckets have been evicted under the cap (surfaced so silent truncation is visible).</summary>
    public int Evictions => _evictions;

    /// <summary>Packs a context dimension (0..255) and value into an opaque bucket id.</summary>
    public static long MakeBucket(byte dim, int value) => ((long)dim << 32) | (uint)value;

    /// <summary>The dimension byte a bucket was built from.</summary>
    public static byte BucketDim(long bucket) => (byte)(bucket >> 32);

    /// <summary>The value a bucket was built from.</summary>
    public static int BucketValue(long bucket) => (int)(bucket & 0xFFFFFFFFL);

    /// <summary>
    /// Folds one 1 Hz sample in: each mod's current total cost updates its global
    /// distribution, and the per-mod distribution of every currently-active context
    /// bucket. <paramref name="perModCost"/> is indexed by modId.
    /// </summary>
    public void Observe(IReadOnlyList<long> activeBuckets, IReadOnlyList<double> perModCost)
    {
        int n = _modCount < perModCost.Count ? _modCount : perModCost.Count;
        for (int m = 0; m < n; m++) _global[m].Add(perModCost[m]);

        for (int i = 0; i < activeBuckets.Count; i++)
        {
            RunningStat[] arr = GetOrAddBucket(activeBuckets[i]);
            if (arr.Length == 0) continue; // bucket was evicted / not admitted
            for (int m = 0; m < n; m++) arr[m].Add(perModCost[m]);
            _bucketSamples[activeBuckets[i]] = _bucketSamples.TryGetValue(activeBuckets[i], out long c) ? c + 1 : 1;
        }
    }

    /// <summary>
    /// Returns the in-context distribution and its complement (out-of-context =
    /// global − in-context) for a (bucket, mod), or false when the bucket is unknown,
    /// the mod is out of range, the context is effectively always-on (no complement
    /// to compare against), or either side is below <see cref="MinSamplesForTest"/>.
    /// </summary>
    public bool TryConditional(long bucketId, int modId, out RunningStat inContext, out RunningStat outOfContext)
    {
        inContext = default;
        outOfContext = default;
        if ((uint)modId >= (uint)_modCount) return false;
        if (!_byBucket.TryGetValue(bucketId, out RunningStat[]? arr) || arr.Length == 0) return false;

        inContext = arr[modId];
        outOfContext = _global[modId].Without(inContext);
        return inContext.Count >= MinSamplesForTest && outOfContext.Count >= MinSamplesForTest;
    }

    /// <summary>The currently-tracked bucket ids (for a detector to sweep).</summary>
    public IEnumerable<long> Buckets => _byBucket.Keys;

    private RunningStat[] GetOrAddBucket(long bucket)
    {
        if (_byBucket.TryGetValue(bucket, out RunningStat[]? arr)) return arr;
        if (_byBucket.Count >= MaxBuckets) EvictLeastSampled();
        var fresh = new RunningStat[_modCount];
        _byBucket[bucket] = fresh;
        _bucketSamples[bucket] = 0;
        return fresh;
    }

    private void EvictLeastSampled()
    {
        long victim = 0;
        long fewest = long.MaxValue;
        bool found = false;
        foreach (KeyValuePair<long, long> kv in _bucketSamples)
        {
            if (kv.Value < fewest) { fewest = kv.Value; victim = kv.Key; found = true; }
        }
        if (!found) return;
        _byBucket.Remove(victim);
        _bucketSamples.Remove(victim);
        _evictions++;
    }
}
