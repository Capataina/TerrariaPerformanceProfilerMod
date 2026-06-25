#nullable enable

using System;
using System.Collections.Generic;

using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Persistence.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Persistence;
using PerformanceProfiler.Persistence.Records;
namespace PerformanceProfiler.Insights;

/// <summary>
/// Live store of <see cref="Insight"/>s: dedup on submit, TTL eviction
/// to a history list, hysteresis-driven confidence promotion, ranking.
///
/// <para>
/// Detectors call <see cref="Submit"/> with a fresh record each pass. Records
/// for the same <see cref="PatternKey"/> + <see cref="SubjectRef"/> collapse
/// onto the existing entry: <see cref="Insight.ConfirmationCount"/>
/// increments, magnitude / evidence refresh, last-seen advances. Confidence
/// is promoted Preliminary → Low → Medium → High as confirmations and the
/// adjusted p-value clear stricter thresholds (see plan §6.4).
/// </para>
///
/// <para>
/// The overlay surfaces only the top-N ranked records via
/// <see cref="Top"/>. <see cref="AllLive"/> exposes the full live set for
/// exporters and end-of-session reporting. <see cref="History"/> retains
/// every record ever surfaced this session for the JSONL history block.
/// </para>
/// </summary>
public sealed class InsightStore
{
    /// <summary>Hard cap on simultaneously live records; older ones evict by last-seen tick.</summary>
    public const int LiveCap = 32;

    /// <summary>No more than this many of one pattern surface at once via <see cref="Top"/>.</summary>
    public const int PerPatternCap = 2;

    /// <summary>Ticks of silence after which a record drops from live to history (≈5 minutes at 60 Hz).</summary>
    public const long DefaultTtlTicks = 60L * 60L * 5L;

    private readonly Dictionary<InsightKey, Insight> _live = new Dictionary<InsightKey, Insight>(32);
    private readonly List<Insight> _history = new List<Insight>(64);
    private readonly long _ttlTicks;

    // Reusable scratch buffers for Top(). Allocated once; the previous
    // implementation allocated a fresh List + Dictionary per call which ran
    // ~60×/sec under the InsightsTab's per-frame refresh, ironically generating
    // garbage in a profiler designed to measure it. Guarded by _topGate so two
    // concurrent TopInto callers (an exporter on the HTTP thread + the eval
    // thread) cannot corrupt them or read a torn comparison tick (gap G5).
    private readonly List<Insight> _topAllScratch = new List<Insight>(LiveCap);
    private readonly Dictionary<PatternKey, int> _topPerPattern = new Dictionary<PatternKey, int>();
    private readonly object _topGate = new object();

    public InsightStore() : this(DefaultTtlTicks) { }

    public InsightStore(long ttlTicks)
    {
        _ttlTicks = ttlTicks;
    }

    /// <summary>Every record ever surfaced this session, including ones evicted from live.</summary>
    public IReadOnlyList<Insight> History => _history;

    /// <summary>Current live count after the last <see cref="Tick"/>.</summary>
    public int LiveCount => _live.Count;

    /// <summary>
    /// Submits a freshly produced record. If a matching live entry already
    /// exists (same pattern + subject), the existing record is refreshed in
    /// place; otherwise a new entry is inserted, evicting the stalest live
    /// record if the cap is exceeded.
    /// </summary>
    public void Submit(Insight rec, long nowTick)
    {
        InsightKey key = StableKey(rec.Pattern, rec.Subject);
        if (_live.TryGetValue(key, out Insight? existing) && existing != null)
        {
            existing.Magnitude = rec.Magnitude;
            existing.Evidence = rec.Evidence;
            existing.Audience = rec.Audience;
            existing.ConfirmationCount++;
            existing.LastSeenTick = nowTick;
            existing.Confidence = PromoteConfidence(existing.ConfirmationCount, existing.Evidence.PValueAdjusted);
            existing.InvalidateRenderingCache();
            return;
        }

        if (_live.Count >= LiveCap)
        {
            EvictStalest(nowTick);
        }

        rec.FirstSeenTick = nowTick;
        rec.LastSeenTick = nowTick;
        rec.ConfirmationCount = 1;
        rec.Confidence = PromoteConfidence(1, rec.Evidence.PValueAdjusted);
        _live[key] = rec;
        _history.Add(rec);
    }

    /// <summary>
    /// Evicts records whose last-seen tick is older than the TTL. Called from
    /// the per-frame Tick of the host (typically the InsightsTab) so the live
    /// set tracks recency without the detectors having to remember to clean up.
    /// </summary>
    public void Tick(long nowTick)
    {
        if (_live.Count == 0) return;
        List<InsightKey>? toRemove = null;
        foreach (KeyValuePair<InsightKey, Insight> kv in _live)
        {
            if (nowTick - kv.Value.LastSeenTick > _ttlTicks)
            {
                toRemove ??= new List<InsightKey>(4);
                toRemove.Add(kv.Key);
            }
        }
        if (toRemove == null) return;
        foreach (InsightKey k in toRemove) _live.Remove(k);
    }

    /// <summary>The full live set, unranked. Order is insertion order.</summary>
    public IEnumerable<Insight> AllLive() => _live.Values;

    /// <summary>
    /// Writes up to <paramref name="n"/> ranked records into <paramref name="destination"/>,
    /// respecting the per-pattern cap. Sort is by descending score from
    /// <see cref="RankingScorer.Score"/>; ties broken by last-seen tick.
    /// <paramref name="destination"/> is cleared first; allocation is bounded
    /// to whatever growth that List needs to fit the result.
    ///
    /// <para>
    /// Reuses scratch buffers so steady-state operation is allocation-free past
    /// the comparer closure. The whole body runs under <c>_topGate</c>: the
    /// comparison tick is a Sort-local captured into the comparer (not a shared
    /// field), and the scratch buffers cannot be touched by a second concurrent
    /// caller — closing gap G5 (the prior shared <c>_topComparerNowTick</c> raced
    /// an exporter on the HTTP thread against the eval thread).
    /// </para>
    /// </summary>
    public void TopInto(List<Insight> destination, int n, long nowTick)
    {
        destination.Clear();
        lock (_topGate)
        {
            if (_live.Count == 0) return;

            _topAllScratch.Clear();
            foreach (Insight r in _live.Values) _topAllScratch.Add(r);

            long ttl = _ttlTicks;
            _topAllScratch.Sort((a, b) =>
            {
                double sa = RankingScorer.Score(a, nowTick, ttl);
                double sb = RankingScorer.Score(b, nowTick, ttl);
                int cmp = sb.CompareTo(sa);
                return cmp != 0 ? cmp : b.LastSeenTick.CompareTo(a.LastSeenTick);
            });

            _topPerPattern.Clear();
            for (int i = 0; i < _topAllScratch.Count && destination.Count < n; i++)
            {
                Insight rec = _topAllScratch[i];
                _topPerPattern.TryGetValue(rec.Pattern, out int seen);
                if (seen >= PerPatternCap) continue;
                _topPerPattern[rec.Pattern] = seen + 1;
                destination.Add(rec);
            }
        }
    }

    /// <summary>
    /// Convenience wrapper that allocates a fresh list. Prefer
    /// <see cref="TopInto"/> from the hot path; this exists for tests and
    /// exporters that don't run per-frame.
    /// </summary>
    public IReadOnlyList<Insight> Top(int n, long nowTick)
    {
        if (_live.Count == 0) return Array.Empty<Insight>();
        List<Insight> result = new List<Insight>(n);
        TopInto(result, n, nowTick);
        return result;
    }

    private void EvictStalest(long nowTick)
    {
        InsightKey stalestKey = default;
        long stalestTick = long.MaxValue;
        foreach (KeyValuePair<InsightKey, Insight> kv in _live)
        {
            if (kv.Value.LastSeenTick < stalestTick)
            {
                stalestTick = kv.Value.LastSeenTick;
                stalestKey = kv.Key;
            }
        }
        _live.Remove(stalestKey);
    }

    /// <summary>
    /// Identity of a live record for dedup: same (pattern, subject) collapses onto
    /// one entry. A full-width value-equality key — the prior version packed the
    /// ids into a 64-bit long with 16 bits each for modId/hookId/contextKey, which
    /// collided once a single mod exceeded 65k hooks (gap G6). The record struct
    /// carries each id at full int width and folds in <see cref="SubjectKind"/>,
    /// so a Session subject and a mod subject with the same (-1) ids never alias.
    /// </summary>
    private readonly record struct InsightKey(
        PatternKey Pattern, SubjectKind Kind, int ModId, int HookId, int ContextKey, byte ContextDim);

    private static InsightKey StableKey(PatternKey pattern, SubjectRef subject) =>
        new InsightKey(pattern, subject.Kind, subject.ModId, subject.HookId, subject.ContextKey, subject.ContextDim);

    /// <summary>
    /// Maps (confirmationCount, pAdjusted) onto the four confidence buckets.
    /// Repetition is treated as evidence of persistence, not statistical
    /// support: a record with <c>pAdjusted == 1d</c> (the detector explicitly
    /// declares "no hypothesis test ran") can never reach Medium on confirmations
    /// alone. The honesty contract requires confidence badges to be defensible
    /// independently of how often the same untested observation re-fires.
    /// </summary>
    private static Confidence PromoteConfidence(int confirmationCount, double pAdjusted)
    {
        // High requires both: stable repeat behaviour AND a small adjusted p-value.
        if (confirmationCount >= 4 && pAdjusted <= 0.05) return Confidence.High;
        // Medium requires statistical evidence at the lower bar; repetition alone
        // never promotes past Low.
        if (confirmationCount >= 3 && pAdjusted <= 0.10) return Confidence.Medium;
        if (confirmationCount >= 2) return Confidence.Low;
        return Confidence.Preliminary;
    }
}
