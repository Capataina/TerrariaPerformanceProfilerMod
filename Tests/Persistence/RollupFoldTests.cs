#nullable enable

using System;
using System.Collections.Generic;
using LiteDB;
using PerformanceProfiler.Persistence.History;
using PerformanceProfiler.Persistence.Records;
using Xunit;

namespace PerformanceProfiler.Tests.Persistence;

/// <summary>
/// Pure-logic tests for the cross-session rollup fold (DB rework wave 1). The fold is
/// the substrate the cross-session detectors read, so its Welford accumulation,
/// ring-bounding, and replay-idempotency are verified against synthetic sessions with
/// no game runtime — the contract is "fold N session summaries → a correct lifetime
/// distribution + a bounded recency ring".
/// </summary>
public sealed class RollupFoldTests
{
    private static SessionRollupInput Session(double cost, bool active, int spikes = 0,
                                              double engagement = 0, string version = "1.0",
                                              string fingerprint = "fp-A", DateTime? endedUtc = null,
                                              long ticks = 2000)
    {
        // Default ticks sit above RollupFold.MinSessionTicks (1800), so a session folds into the
        // cost/alloc/engagement distributions unless a test deliberately passes a thin value.
        var input = new SessionRollupInput
        {
            SessionId = ObjectId.NewObjectId(),
            Fingerprint = fingerprint,
            EndedUtc = endedUtc ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            TicksObserved = ticks,
        };
        input.Mods.Add(new ModSessionContribution
        {
            InternalName = "ExampleMod",
            DisplayName = "Example Mod",
            Version = version,
            CostMs = cost,
            EngagementScore = engagement,
            SpikeContributions = spikes,
            WasActive = active,
        });
        return input;
    }

    [Fact]
    public void FoldGlobal_FreshRow_RecordsOneSession()
    {
        var row = new ModLifetimeRollupRow();
        SessionRollupInput s = Session(cost: 2.5, active: true, spikes: 3);

        RollupFold.FoldGlobal(row, s, s.Mods[0]);

        Assert.Equal("ExampleMod", row.InternalName);
        Assert.Equal(1, row.SessionCount);
        Assert.Equal(1, row.ActiveSessionCount);
        Assert.Equal(1, row.Cost.Count);
        Assert.Equal(2.5, row.Cost.Mean, 6);
        Assert.Equal(3, row.TotalSpikeContributions);
        Assert.Single(row.Ring);
        Assert.Equal(s.EndedUtc, row.FirstSeenUtc);
        Assert.Equal(s.EndedUtc, row.LastSeenUtc);
    }

    [Fact]
    public void FoldGlobal_MultipleSessions_WelfordMeanIsAverageOfPerSessionCosts()
    {
        var row = new ModLifetimeRollupRow();
        foreach (double c in new[] { 1.0, 2.0, 3.0 })
        {
            SessionRollupInput s = Session(cost: c, active: true);
            RollupFold.FoldGlobal(row, s, s.Mods[0]);
        }

        Assert.Equal(3, row.SessionCount);
        Assert.Equal(3, row.Cost.Count);
        Assert.Equal(2.0, row.Cost.Mean, 6);  // mean of {1,2,3}
        Assert.Equal(3, row.Ring.Count);
    }

    [Fact]
    public void FoldGlobal_ThinSession_SkipsDistributionsButKeepsPresenceAndCounts()
    {
        var row = new ModLifetimeRollupRow();

        // One substantial session establishes the cost distribution.
        SessionRollupInput sub = Session(cost: 5.0, active: true, spikes: 0, ticks: 2000);
        RollupFold.FoldGlobal(row, sub, sub.Mods[0]);

        // A thin load-window session (below MinSessionTicks) with absurd cost and a real spike.
        SessionRollupInput thin = Session(cost: 100.0, active: true, spikes: 2,
                                          ticks: RollupFold.MinSessionTicks - 1);
        RollupFold.FoldGlobal(row, thin, thin.Mods[0]);

        // The thin session's garbage cost never enters the distribution: the mean stays the
        // substantial-only mean, and Cost.Count (1) sits below SessionCount (2) by design.
        Assert.Equal(1, row.Cost.Count);
        Assert.Equal(5.0, row.Cost.Mean, 6);
        Assert.Equal(5.0, row.Cost.WeightedMean, 6);

        // Its presence is fully recorded: counted as a session, appended to the ring, and its
        // spike membership added (event counts are not divided-by-ticks averages).
        Assert.Equal(2, row.SessionCount);
        Assert.Equal(2, row.Ring.Count);
        Assert.Equal(100.0, row.Ring[row.Ring.Count - 1].CostMs, 6); // the thin entry is in the ring
        Assert.Equal(2, row.TotalSpikeContributions);
    }

    [Fact]
    public void FoldGlobal_TickWeighting_LongSessionDominatesLifetimeAverage()
    {
        var row = new ModLifetimeRollupRow();

        // A long, cheap session and a short-but-valid, expensive one — both substantial.
        SessionRollupInput lng = Session(cost: 2.0, active: true, ticks: 20000);
        SessionRollupInput shrt = Session(cost: 20.0, active: true, ticks: 2000);
        RollupFold.FoldGlobal(row, lng, lng.Mods[0]);
        RollupFold.FoldGlobal(row, shrt, shrt.Mods[0]);

        // Equal-weight mean is one-session-one-vote: (2 + 20) / 2 = 11.
        Assert.Equal(11.0, row.Cost.Mean, 6);

        // Tick-weighted lifetime average: (2·20000 + 20·2000) / 22000 ≈ 3.64, dominated by the
        // long cheap session and far closer to its 2.0 than the equal-weight 11 is.
        Assert.Equal(80000d / 22000d, row.Cost.WeightedMean, 6);
        Assert.True(Math.Abs(row.Cost.WeightedMean - 2.0) < Math.Abs(row.Cost.Mean - 2.0));
    }

    [Fact]
    public void FoldGlobal_IdleSessions_ActiveCountStaysZero()
    {
        var row = new ModLifetimeRollupRow();
        for (int i = 0; i < 3; i++)
        {
            SessionRollupInput s = Session(cost: 0d, active: false);
            RollupFold.FoldGlobal(row, s, s.Mods[0]);
        }

        Assert.Equal(3, row.SessionCount);
        Assert.Equal(0, row.ActiveSessionCount); // "unused in your last 3 sessions" substrate
        Assert.All(row.Ring, e => Assert.False(e.WasActive));
    }

    [Fact]
    public void FoldGlobal_BeyondCapacity_RingKeepsNewest()
    {
        var row = new ModLifetimeRollupRow();
        int n = ModLifetimeRollupRow.RingCapacity + 10;
        for (int i = 0; i < n; i++)
        {
            SessionRollupInput s = Session(cost: i, active: true);
            RollupFold.FoldGlobal(row, s, s.Mods[0]);
        }

        Assert.Equal(n, row.SessionCount);                          // lifetime count is not bounded
        Assert.Equal(ModLifetimeRollupRow.RingCapacity, row.Ring.Count); // ring is
        // The newest entry is the last folded (cost == n-1).
        Assert.Equal(n - 1, row.Ring[row.Ring.Count - 1].CostMs, 6);
        // The oldest retained entry is cost == n - capacity (front dropped).
        Assert.Equal(n - ModLifetimeRollupRow.RingCapacity, row.Ring[0].CostMs, 6);
    }

    [Fact]
    public void AlreadyFolded_DetectsSessionInRing_ForReplayIdempotency()
    {
        var row = new ModLifetimeRollupRow();
        SessionRollupInput s = Session(cost: 1.0, active: true);
        RollupFold.FoldGlobal(row, s, s.Mods[0]);

        Assert.True(RollupFold.AlreadyFolded(row, s.SessionId));
        Assert.False(RollupFold.AlreadyFolded(row, ObjectId.NewObjectId()));
    }

    [Fact]
    public void FoldModlist_AccumulatesScopedToStack_NoRing()
    {
        var row = new ModModlistRollupRow();
        SessionRollupInput s1 = Session(cost: 4.0, active: true, fingerprint: "fp-A");
        SessionRollupInput s2 = Session(cost: 6.0, active: true, fingerprint: "fp-A");
        RollupFold.FoldModlist(row, s1, s1.Mods[0]);
        RollupFold.FoldModlist(row, s2, s2.Mods[0]);

        Assert.Equal("ExampleMod", row.InternalName);
        Assert.Equal("fp-A", row.Fingerprint);
        Assert.Equal(2, row.SessionCount);
        Assert.Equal(5.0, row.Cost.Mean, 6);   // mean of {4,6}
    }

    [Fact]
    public void WelfordStat_FoldSample_MatchesRunningMean()
    {
        var block = new WelfordStat();
        block.FoldSample(10d);
        block.FoldSample(20d);
        block.FoldSample(30d);

        Assert.Equal(3, block.Count);
        Assert.Equal(20d, block.Mean, 6);
        Assert.True(block.M2 > 0d); // variance present
    }

    [Fact]
    public void FoldGlobal_OutOfOrderEndedUtc_KeepsTrueFirstAndLastSeen()
    {
        var row = new ModLifetimeRollupRow();
        var early = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var late = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

        SessionRollupInput sLate = Session(cost: 1, active: true, endedUtc: late);
        RollupFold.FoldGlobal(row, sLate, sLate.Mods[0]);
        SessionRollupInput sEarly = Session(cost: 1, active: true, endedUtc: early);
        RollupFold.FoldGlobal(row, sEarly, sEarly.Mods[0]);

        Assert.Equal(early, row.FirstSeenUtc);
        Assert.Equal(late, row.LastSeenUtc);
    }
}
