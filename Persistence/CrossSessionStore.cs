#nullable enable

using System.Collections.Generic;
using LiteDB;
using PerformanceProfiler.Insights.Shared;
using PerformanceProfiler.Insights.ReferenceFrames;
using PerformanceProfiler.Persistence.Records;

namespace PerformanceProfiler.Persistence;

/// <summary>
/// The durability + fairness layer (Addition 5): persists the per-context per-mod
/// baselines across sessions, keyed by the machine / modlist fingerprint so a
/// baseline only ever combines runs of the same stack. This is what makes
/// LifetimeData truthful and lets a detector's confidence climb past Low — gap G3,
/// where every live detector was stuck at PValueAdjusted=1 with no cross-session
/// data to draw on.
///
/// <para>
/// Operates on the LiteDB collection directly (not the whole database) so the
/// round-trip is unit-testable against an in-memory store. The maths it rests on —
/// the Welford merge and from-components reconstruction — is itself unit-tested.
/// </para>
/// </summary>
public static class CrossSessionStore
{
    /// <summary>
    /// Loads the prior accumulated baseline for <paramref name="fingerprint"/> and
    /// seeds a fresh <see cref="ContextBaseline"/>, so this session continues
    /// accumulating on top of the lifetime total. Returns an unseeded frame when no
    /// prior rows exist.
    /// </summary>
    public static ContextBaseline Load(ILiteCollection<ContextBaselineRow> col, string fingerprint, int modCount)
    {
        var cb = new ContextBaseline(modCount);
        var rows = new List<(long, int, RunningStat)>();
        foreach (ContextBaselineRow r in col.Find(x => x.Fingerprint == fingerprint))
        {
            rows.Add((ContextBaseline.MakeBucket(r.Dim, r.Key), r.ModId,
                RunningStat.FromComponents(r.Count, r.Mean, r.M2)));
        }
        if (rows.Count > 0) cb.Seed(rows);
        return cb;
    }

    /// <summary>
    /// Persists <paramref name="cb"/> for <paramref name="fingerprint"/>. If the
    /// frame was seeded at load it already holds the lifetime total (prior +
    /// this session), so its rows replace the fingerprint's. If it was NOT seeded
    /// (a fresh frame, or a failed load), its stats are merged into whatever the DB
    /// holds, so a prior baseline is never silently overwritten away.
    /// </summary>
    public static void Save(ILiteCollection<ContextBaselineRow> col, string fingerprint, ContextBaseline cb)
    {
        if (cb.WasSeeded)
        {
            col.DeleteMany(x => x.Fingerprint == fingerprint);
            foreach ((long bucket, int modId, RunningStat stat) in cb.Export())
            {
                col.Insert(ToRow(fingerprint, bucket, modId, stat));
            }
            return;
        }

        var prior = new Dictionary<(byte, int, int), ContextBaselineRow>();
        foreach (ContextBaselineRow r in col.Find(x => x.Fingerprint == fingerprint))
        {
            prior[(r.Dim, r.Key, r.ModId)] = r;
        }
        foreach ((long bucket, int modId, RunningStat stat) in cb.Export())
        {
            byte dim = ContextBaseline.BucketDim(bucket);
            int key = ContextBaseline.BucketValue(bucket);
            if (prior.TryGetValue((dim, key, modId), out ContextBaselineRow? existing))
            {
                RunningStat merged = RunningStat
                    .FromComponents(existing.Count, existing.Mean, existing.M2)
                    .Merge(stat);
                existing.Count = merged.Count;
                existing.Mean = merged.Mean;
                existing.M2 = merged.M2;
                col.Update(existing);
            }
            else
            {
                col.Insert(ToRow(fingerprint, bucket, modId, stat));
            }
        }
    }

    private static ContextBaselineRow ToRow(string fingerprint, long bucket, int modId, RunningStat s) =>
        new ContextBaselineRow
        {
            Fingerprint = fingerprint,
            Dim = ContextBaseline.BucketDim(bucket),
            Key = ContextBaseline.BucketValue(bucket),
            ModId = modId,
            Count = s.Count,
            Mean = s.Mean,
            M2 = s.M2,
        };
}
