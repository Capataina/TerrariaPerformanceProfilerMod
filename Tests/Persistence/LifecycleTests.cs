#nullable enable

using System;
using System.IO;
using System.Linq;
using LiteDB;
using PerformanceProfiler.Persistence;
using PerformanceProfiler.Persistence.Lifecycle;
using PerformanceProfiler.Persistence.Records;
using Xunit;

namespace PerformanceProfiler.Tests.Persistence;

/// <summary>
/// Tests for the self-cleaning lifecycle (DB rework wave 3): the pure modlist-change diff,
/// and the two reset scopes against a synthetic store. The key invariant for
/// "forget this modlist" is the spine — per-mod lifetime rollups survive a forget.
/// </summary>
public sealed class LifecycleTests : IDisposable
{
    private readonly string _root;
    public LifecycleTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "perfprofiler-lifecycle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
    private static void NullLog(string _, Exception? __) { }

    [Fact]
    public void ModlistChange_DetectsAddedAndRemoved()
    {
        var change = ModlistChange.Diff(
            current: new[] { "A", "B", "D" },
            previous: new[] { "A", "B", "C" });
        Assert.True(change.Changed);
        Assert.True(change.HadPrior);
        Assert.Equal(new[] { "D" }, change.Added);
        Assert.Equal(new[] { "C" }, change.Removed);
    }

    [Fact]
    public void ModlistChange_NoPrior_IsNotAChange()
    {
        var change = ModlistChange.Diff(new[] { "A", "B" }, previous: null);
        Assert.False(change.Changed);
        Assert.False(change.HadPrior);
    }

    [Fact]
    public void ModlistChange_Identical_IsNotAChange()
    {
        var change = ModlistChange.Diff(new[] { "A", "B" }, new[] { "B", "A" });
        Assert.True(change.HadPrior);
        Assert.False(change.Changed);
    }

    [Fact]
    public void ForgetModlist_DropsTheStack_ButKeepsGlobalLifetime()
    {
        using var db = new ProfilerDatabase(_root, log: NullLog, profilerVersion: "test");

        // Two stacks, each with a session + per-stack rollup; one global rollup spanning both.
        var sidA = ObjectId.NewObjectId();
        var sidB = ObjectId.NewObjectId();
        db.Sessions.Insert(new SessionRow { Id = sidA, ModlistFingerprint = "fp-A", EndedUtc = DateTime.UtcNow });
        db.Sessions.Insert(new SessionRow { Id = sidB, ModlistFingerprint = "fp-B", EndedUtc = DateTime.UtcNow });
        db.SpikeWindows.Insert(new SpikeWindowRow { SessionId = sidA });
        db.SpikeWindows.Insert(new SpikeWindowRow { SessionId = sidB });
        db.Modlists.Insert(new ModlistRow { Fingerprint = "fp-A" });
        db.Modlists.Insert(new ModlistRow { Fingerprint = "fp-B" });
        db.ModModlistRollups.Insert(new ModModlistRollupRow { InternalName = "Roamer", Fingerprint = "fp-A" });
        db.ModModlistRollups.Insert(new ModModlistRollupRow { InternalName = "Roamer", Fingerprint = "fp-B" });
        db.ModLifetimeRollups.Insert(new ModLifetimeRollupRow { InternalName = "Roamer", SessionCount = 2 });

        ResetReport report = StoreReset.ForgetModlist(db, "fp-A", NullLog);

        Assert.True(report.Ok);
        Assert.Equal(1, report.SessionsCleared);
        // fp-A gone, fp-B intact.
        Assert.Empty(db.Sessions.Find(x => x.ModlistFingerprint == "fp-A"));
        Assert.Single(db.Sessions.Find(x => x.ModlistFingerprint == "fp-B"));
        Assert.Empty(db.SpikeWindows.Find(x => x.SessionId == sidA));
        Assert.Single(db.SpikeWindows.Find(x => x.SessionId == sidB));
        Assert.Empty(db.ModModlistRollups.Find(x => x.Fingerprint == "fp-A"));
        Assert.Single(db.ModModlistRollups.Find(x => x.Fingerprint == "fp-B"));
        // The spine: the global per-mod lifetime rollup survives the forget.
        Assert.Single(db.ModLifetimeRollups.Find(x => x.InternalName == "Roamer"));
    }

    [Fact]
    public void Everything_DropsAllData()
    {
        using var db = new ProfilerDatabase(_root, log: NullLog, profilerVersion: "test");
        db.Sessions.Insert(new SessionRow { Id = ObjectId.NewObjectId(), ModlistFingerprint = "fp-A", EndedUtc = DateTime.UtcNow });
        db.ModLifetimeRollups.Insert(new ModLifetimeRollupRow { InternalName = "Mod" });

        ResetReport report = StoreReset.Everything(db, NullLog);

        Assert.True(report.Ok);
        Assert.Equal(0, db.Sessions.Count());
        Assert.Equal(0, db.ModLifetimeRollups.Count());
    }
}
