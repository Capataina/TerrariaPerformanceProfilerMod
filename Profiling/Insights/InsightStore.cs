#nullable enable

using System;
using System.Collections.Generic;

namespace PerformanceProfiler.Profiling.Insights;

/// <summary>
/// Live store of <see cref="InsightRecord"/>s: dedup on submit, TTL eviction
/// to a history list, hysteresis-driven confidence promotion, ranking.
///
/// <para>
/// Detectors call <see cref="Submit"/> with a fresh record each pass. Records
/// for the same <see cref="PatternKey"/> + <see cref="SubjectRef"/> collapse
/// onto the existing entry: <see cref="InsightRecord.ConfirmationCount"/>
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

    private readonly Dictionary<long, InsightRecord> _live = new Dictionary<long, InsightRecord>(32);
    private readonly List<InsightRecord> _history = new List<InsightRecord>(64);
    private readonly long _ttlTicks;

    public InsightStore() : this(DefaultTtlTicks) { }

    public InsightStore(long ttlTicks)
    {
        _ttlTicks = ttlTicks;
    }

    /// <summary>Every record ever surfaced this session, including ones evicted from live.</summary>
    public IReadOnlyList<InsightRecord> History => _history;

    /// <summary>Current live count after the last <see cref="Tick"/>.</summary>
    public int LiveCount => _live.Count;

    /// <summary>
    /// Submits a freshly produced record. If a matching live entry already
    /// exists (same pattern + subject), the existing record is refreshed in
    /// place; otherwise a new entry is inserted, evicting the stalest live
    /// record if the cap is exceeded.
    /// </summary>
    public void Submit(InsightRecord rec, long nowTick)
    {
        long key = StableKey(rec.Pattern, rec.Subject);
        if (_live.TryGetValue(key, out InsightRecord? existing) && existing != null)
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
        List<long>? toRemove = null;
        foreach (KeyValuePair<long, InsightRecord> kv in _live)
        {
            if (nowTick - kv.Value.LastSeenTick > _ttlTicks)
            {
                toRemove ??= new List<long>(4);
                toRemove.Add(kv.Key);
            }
        }
        if (toRemove == null) return;
        foreach (long k in toRemove) _live.Remove(k);
    }

    /// <summary>The full live set, unranked. Order is insertion order.</summary>
    public IEnumerable<InsightRecord> AllLive() => _live.Values;

    /// <summary>
    /// Returns up to <paramref name="n"/> ranked records, respecting the
    /// per-pattern cap. Sort is by descending score from
    /// <see cref="RankingScorer.Score"/>; ties broken by last-seen tick.
    /// </summary>
    public IReadOnlyList<InsightRecord> Top(int n, long nowTick)
    {
        if (_live.Count == 0) return Array.Empty<InsightRecord>();
        List<InsightRecord> all = new List<InsightRecord>(_live.Count);
        foreach (InsightRecord r in _live.Values) all.Add(r);
        all.Sort((a, b) =>
        {
            double sa = RankingScorer.Score(a, nowTick, _ttlTicks);
            double sb = RankingScorer.Score(b, nowTick, _ttlTicks);
            int cmp = sb.CompareTo(sa);
            return cmp != 0 ? cmp : b.LastSeenTick.CompareTo(a.LastSeenTick);
        });

        List<InsightRecord> result = new List<InsightRecord>(n);
        Dictionary<PatternKey, int> perPattern = new Dictionary<PatternKey, int>();
        for (int i = 0; i < all.Count && result.Count < n; i++)
        {
            InsightRecord rec = all[i];
            perPattern.TryGetValue(rec.Pattern, out int seen);
            if (seen >= PerPatternCap) continue;
            perPattern[rec.Pattern] = seen + 1;
            result.Add(rec);
        }
        return result;
    }

    private void EvictStalest(long nowTick)
    {
        long stalestKey = 0;
        long stalestTick = long.MaxValue;
        foreach (KeyValuePair<long, InsightRecord> kv in _live)
        {
            if (kv.Value.LastSeenTick < stalestTick)
            {
                stalestTick = kv.Value.LastSeenTick;
                stalestKey = kv.Key;
            }
        }
        _live.Remove(stalestKey);
    }

    private static long StableKey(PatternKey pattern, SubjectRef subject)
    {
        // Pack (pattern:8, contextDim:8, modId:16, hookId:16, contextKey:16) into a 64-bit key.
        // Modest collision risk if a single mod has > 65k hooks; not happening.
        long k = (long)pattern;
        k = (k << 8) | subject.ContextDim;
        k = (k << 16) | (uint)(subject.ModId & 0xFFFF);
        k = (k << 16) | (uint)(subject.HookId & 0xFFFF);
        k = (k << 16) | (uint)(subject.ContextKey & 0xFFFF);
        return k;
    }

    private static Confidence PromoteConfidence(int confirmationCount, double pAdjusted)
    {
        if (confirmationCount >= 4 && pAdjusted <= 0.05) return Confidence.High;
        if (confirmationCount >= 3) return Confidence.Medium;
        if (confirmationCount >= 2) return Confidence.Low;
        return Confidence.Preliminary;
    }
}
