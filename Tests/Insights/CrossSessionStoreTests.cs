#nullable enable

using System.Collections.Generic;
using System.IO;
using LiteDB;
using PerformanceProfiler.Insights.ReferenceFrames;
using PerformanceProfiler.Insights.Shared;
using PerformanceProfiler.Profiling.Persistence.Records;
using Xunit;

namespace PerformanceProfiler.Tests.Insights;

/// <summary>
/// Round-trips the cross-session baseline through a real (in-memory) LiteDB store:
/// the persistence mechanism that makes LifetimeData truthful. The cross-game-restart
/// lifecycle is verified in-game; this proves the save/load/merge maths is exact.
/// </summary>
public sealed class CrossSessionStoreTests
{
    private const byte DimBoss = 2;

    private static ContextBaseline AccumulateSession()
    {
        var cb = new ContextBaseline(modCount: 2);
        long boss = ContextBaseline.MakeBucket(DimBoss, 1);
        // 40 ticks with a boss alive (mod0 costs 5), 40 without (mod0 costs 1).
        for (int i = 0; i < 40; i++) cb.Observe(new List<long> { boss }, new double[] { 5d, 2d });
        for (int i = 0; i < 40; i++) cb.Observe(new List<long>(), new double[] { 1d, 2d });
        return cb;
    }

    [Fact]
    public void SaveThenLoad_ReconstructsTheConditionalDistributions()
    {
        using var db = new LiteDatabase(new MemoryStream());
        ILiteCollection<ContextBaselineRow> col = db.GetCollection<ContextBaselineRow>("ctx");
        const string fp = "fingerprint-A";
        long boss = ContextBaseline.MakeBucket(DimBoss, 1);

        CrossSessionStore.Save(col, fp, AccumulateSession());

        ContextBaseline loaded = CrossSessionStore.Load(col, fp, modCount: 2);
        Assert.True(loaded.WasSeeded);
        Assert.True(loaded.TryConditional(boss, 0, out RunningStat inCtx, out RunningStat outCtx));
        Assert.Equal(40, inCtx.Count);
        Assert.Equal(5d, inCtx.Mean, 6);   // mod0 while a boss is alive
        Assert.Equal(40, outCtx.Count);    // global(80) − in-context(40)
        Assert.Equal(1d, outCtx.Mean, 6);  // mod0 otherwise
    }

    [Fact]
    public void Fingerprints_DoNotCross()
    {
        using var db = new LiteDatabase(new MemoryStream());
        ILiteCollection<ContextBaselineRow> col = db.GetCollection<ContextBaselineRow>("ctx");
        CrossSessionStore.Save(col, "stack-A", AccumulateSession());

        // A different stack has no prior baseline.
        ContextBaseline other = CrossSessionStore.Load(col, "stack-B", modCount: 2);
        Assert.False(other.WasSeeded);
    }

    [Fact]
    public void SeededSession_SaveReplaces_DoesNotDoubleCount()
    {
        using var db = new LiteDatabase(new MemoryStream());
        ILiteCollection<ContextBaselineRow> col = db.GetCollection<ContextBaselineRow>("ctx");
        const string fp = "fingerprint-B";
        long boss = ContextBaseline.MakeBucket(DimBoss, 1);

        // Session 1 persists.
        CrossSessionStore.Save(col, fp, AccumulateSession());

        // Session 2 loads the prior (seeded) and saves it straight back. Because the
        // frame was seeded, Save replaces rather than merges — the lifetime total must
        // stay 40 in-context, not double to 80.
        ContextBaseline session2 = CrossSessionStore.Load(col, fp, modCount: 2);
        CrossSessionStore.Save(col, fp, session2);

        ContextBaseline session3 = CrossSessionStore.Load(col, fp, modCount: 2);
        Assert.True(session3.TryConditional(boss, 0, out RunningStat inCtx, out _));
        Assert.Equal(40, inCtx.Count); // not 80 — the seeded-replace path held
    }
}
