#nullable enable

using System;
using System.IO;
using System.Linq;
using LiteDB;
using PerformanceProfiler.Persistence;
using PerformanceProfiler.Persistence.History;
using PerformanceProfiler.Persistence.Records;
using Xunit;

namespace PerformanceProfiler.Tests.Persistence;

/// <summary>
/// Tests for the cross-session read layer (DB rework wave 2). Mostly read-model unit
/// tests over a synthetic store (rows inserted directly), plus one integration test
/// that drives the full write path (enqueue RollupFold → writer applies → reopen →
/// read) to prove the RollupStream Apply and the HistoryStore agree end-to-end.
/// </summary>
public sealed class HistoryStoreTests : IDisposable
{
    private readonly string _root;

    public HistoryStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "perfprofiler-history-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private static void NullLog(string _, Exception? __) { }

    private static SessionRingEntry Ring(double cost, bool active, int spikes, double engagement, DateTime endedUtc)
        => new SessionRingEntry
        {
            SessionId = ObjectId.NewObjectId(),
            EndedUtc = endedUtc,
            CostMs = cost,
            EngagementScore = engagement,
            SpikeContributions = spikes,
            WasActive = active,
        };

    [Fact]
    public void GetModHistory_ReturnsRollupAndRing_NewestFirst()
    {
        using var db = new ProfilerDatabase(_root, log: NullLog, profilerVersion: "test");
        var baseT = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var row = new ModLifetimeRollupRow
        {
            InternalName = "ExampleMod",
            DisplayName = "Example",
            LastVersion = "2.0",
            SessionCount = 3,
            ActiveSessionCount = 2,
        };
        row.Cost.FoldSample(1); row.Cost.FoldSample(2); row.Cost.FoldSample(3);
        row.Ring.Add(Ring(1, true, 0, 5, baseT));
        row.Ring.Add(Ring(2, true, 1, 6, baseT.AddDays(1)));
        row.Ring.Add(Ring(3, false, 0, 0, baseT.AddDays(2)));
        db.ModLifetimeRollups.Insert(row);

        var store = new HistoryStore(db);
        ModHistory? h = store.GetModHistory("ExampleMod", lastN: 2);

        Assert.NotNull(h);
        Assert.Equal("ExampleMod", h!.InternalName);
        Assert.Equal(3, h.SessionCount);
        Assert.Equal(2.0, h.LifetimeAvgCostMs, 6);
        Assert.Equal(2, h.RecentRing.Count);                       // capped at lastN
        Assert.Equal(baseT.AddDays(2), h.RecentRing[0].EndedUtc);  // newest first
    }

    [Fact]
    public void GetModHistory_UnknownMod_ReturnsNull()
    {
        using var db = new ProfilerDatabase(_root, log: NullLog, profilerVersion: "test");
        var store = new HistoryStore(db);
        Assert.Null(store.GetModHistory("NeverSeen", lastN: 5));
    }

    [Fact]
    public void WindowedStats_ComputesAggregates_AndRespectsScope()
    {
        using var db = new ProfilerDatabase(_root, log: NullLog, profilerVersion: "test");
        var t = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

        var a = new ModLifetimeRollupRow { InternalName = "ModA", DisplayName = "A" };
        a.Ring.Add(Ring(2, true, 1, 10, t));
        a.Ring.Add(Ring(4, true, 2, 30, t.AddDays(1))); // avg cost 3, spikes 3, avg eng 20
        db.ModLifetimeRollups.Insert(a);

        var b = new ModLifetimeRollupRow { InternalName = "ModB", DisplayName = "B" };
        b.Ring.Add(Ring(0, false, 0, 0, t));
        b.Ring.Add(Ring(0, false, 0, 0, t.AddDays(1)));  // fully idle
        db.ModLifetimeRollups.Insert(b);

        var store = new HistoryStore(db);

        var all = store.WindowedStats(lastN: 5, scopeMods: null);
        Assert.Equal(2, all.Count);
        ModWindowStats sa = all.First(x => x.InternalName == "ModA");
        Assert.Equal(3.0, sa.AvgCostMs, 6);
        Assert.Equal(3, sa.SpikeContributions);
        Assert.Equal(20.0, sa.AvgEngagement, 6);
        Assert.Equal(1.0, sa.ActiveFraction, 6);

        ModWindowStats sb = all.First(x => x.InternalName == "ModB");
        Assert.Equal(0.0, sb.ActiveFraction, 6);  // "unused" substrate

        var scoped = store.WindowedStats(lastN: 5, scopeMods: new[] { "ModA" });
        Assert.Single(scoped);
        Assert.Equal("ModA", scoped[0].InternalName);
    }

    [Fact]
    public void WindowedStats_Window_LimitsToLastN()
    {
        using var db = new ProfilerDatabase(_root, log: NullLog, profilerVersion: "test");
        var t = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var m = new ModLifetimeRollupRow { InternalName = "ModA" };
        for (int i = 0; i < 6; i++) m.Ring.Add(Ring(i, true, 1, 1, t.AddDays(i))); // costs 0..5
        db.ModLifetimeRollups.Insert(m);

        var store = new HistoryStore(db);
        var w = store.WindowedStats(lastN: 2, scopeMods: null);
        // last 2 by EndedUtc are costs 4 and 5 → avg 4.5
        Assert.Equal(4.5, w[0].AvgCostMs, 6);
        Assert.Equal(2, w[0].SpikeContributions);
    }

    [Fact]
    public void RollupFold_WriteThenRead_Integration()
    {
        var sessionId = ObjectId.NewObjectId();
        using (var db = new ProfilerDatabase(_root, log: NullLog, profilerVersion: "test"))
        {
            var input = new SessionRollupInput
            {
                SessionId = sessionId,
                Fingerprint = "fp-A",
                EndedUtc = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
                TicksObserved = 500,
            };
            input.Mods.Add(new ModSessionContribution
            {
                InternalName = "WiredMod", DisplayName = "Wired", Version = "1.2",
                CostMs = 7.5, AllocBytes = 100, EngagementScore = 42,
                SpikeContributions = 2, WasActive = true,
            });
            db.Writer.Enqueue(DbWriteOp.RollupFold(input));
        } // dispose drains the writer + persists

        using (var db = new ProfilerDatabase(_root, log: NullLog, profilerVersion: "test"))
        {
            var store = new HistoryStore(db);
            ModHistory? h = store.GetModHistory("WiredMod", lastN: 5);
            Assert.NotNull(h);
            Assert.Equal(1, h!.SessionCount);
            Assert.Equal(7.5, h.LifetimeAvgCostMs, 6);
            Assert.Equal(2, h.TotalSpikeContributions);
            Assert.Single(h.RecentRing);
            Assert.Equal("fp-A", h.RecentRing[0].Fingerprint);

            // per-modlist row was written too
            Assert.Single(h.ByModlist);
            Assert.Equal("fp-A", h.ByModlist[0].Fingerprint);
        }
    }

    [Fact]
    public void Backfill_RebuildsRollupFromExistingSessions()
    {
        using var db = new ProfilerDatabase(_root, log: NullLog, profilerVersion: "test");
        var baseT = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);

        // Three ended sessions, oldest first, each with the mod doing measurable work.
        for (int i = 0; i < 3; i++)
        {
            var sid = ObjectId.NewObjectId();
            db.Sessions.Insert(new SessionRow
            {
                Id = sid,
                StartedUtc = baseT.AddDays(i),
                EndedUtc = baseT.AddDays(i).AddHours(1),
                ModlistFingerprint = "fp-A",
                TicksObserved = 1000,
            });
            db.PerSessionMods.Insert(new PerSessionModAggregate
            {
                SessionId = sid,
                ModId = 0,
                ModInternalName = "HistMod",
                AvgMs = 1.5,
                AvgBytes = 200,
            });
            // One spike window attributing to the mod in the middle session.
            if (i == 1)
            {
                db.SpikeWindows.Insert(new SpikeWindowRow
                {
                    SessionId = sid,
                    TopContributors = { new SpikeContributor { ModId = 0, Name = "HistMod", Ms = 5 } },
                });
            }
        }

        int folded = RollupBackfill.Run(db, null);
        Assert.Equal(3, folded);

        var store = new HistoryStore(db);
        ModHistory? h = store.GetModHistory("HistMod", lastN: 10);
        Assert.NotNull(h);
        Assert.Equal(3, h!.SessionCount);
        Assert.Equal(3, h.ActiveSessionCount);          // cost above the active floor each session
        Assert.Equal(1.5, h.LifetimeAvgCostMs, 6);
        Assert.Equal(1, h.TotalSpikeContributions);     // the one mid-session spike
        Assert.Equal(0d, h.LifetimeAvgEngagement, 6);   // historical sessions fold 0 engagement
        Assert.Equal(3, h.RecentRing.Count);

        // Re-running is dedup-safe: RollupApplier skips sessions already in the ring, so a
        // second backfill does not double-count (SessionCount stays 3, not 6).
        RollupBackfill.Run(db, null);
        ModHistory? h2 = store.GetModHistory("HistMod", lastN: 10);
        Assert.Equal(3, h2!.SessionCount);
    }

    [Fact]
    public void DataHealth_CountsModsAndSessions()
    {
        using var db = new ProfilerDatabase(_root, log: NullLog, profilerVersion: "test");
        db.ModLifetimeRollups.Insert(new ModLifetimeRollupRow { InternalName = "ModA" });
        db.ModLifetimeRollups.Insert(new ModLifetimeRollupRow { InternalName = "ModB" });
        db.Sessions.Insert(new SessionRow
        {
            Id = ObjectId.NewObjectId(),
            StartedUtc = DateTime.UtcNow.AddHours(-1),
            EndedUtc = DateTime.UtcNow,
            ModlistFingerprint = "fp-A",
        });

        var store = new HistoryStore(db);
        DataHealthView v = store.DataHealth();
        Assert.Equal(2, v.ModsTracked);
        Assert.True(v.EndedSessionCount >= 1);
        Assert.NotNull(v.LastSessionEndedUtc);
    }
}
