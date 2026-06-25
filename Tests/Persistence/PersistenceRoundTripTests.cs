#nullable enable

using System;
using System.IO;
using System.Linq;
using LiteDB;
using PerformanceProfiler.Persistence;
using PerformanceProfiler.Persistence.Records;
using Xunit;

namespace PerformanceProfiler.Tests.Persistence;

/// <summary>
/// Verifies the LiteDB-backed persistence layer end to end: open a fresh DB
/// in a temp folder, enqueue ops via the writer thread, drain on dispose,
/// reopen the DB read-only and assert the expected rows exist.
///
/// Every test uses its own temp directory so fixtures stay isolated from
/// each other and from any real player DB.
/// </summary>
public class PersistenceRoundTripTests : IDisposable
{
    private readonly string _root;

    public PersistenceRoundTripTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "perfprofiler-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort cleanup */ }
    }

    [Fact]
    public void Open_FreshDirectory_CreatesEmptyDb()
    {
        using var db = new ProfilerDatabase(_root, log: NullLog, profilerVersion: "test");
        Assert.True(File.Exists(Path.Combine(_root, PersistenceFileNames.Db)));
        Assert.Equal(0, db.Sessions.Count());
    }

    [Fact]
    public void SessionStart_Then_Reopen_RowSurvives()
    {
        ObjectId sessionId;
        using (var db = new ProfilerDatabase(_root, log: NullLog, profilerVersion: "test"))
        {
            var row = new SessionRow
            {
                StartedUtc = DateTime.UtcNow,
                ProfilerVersion = "0.3-test",
                ModlistFingerprint = "abc",
                Mode = "lite",
                EndReason = "clean",
                Incomplete = true,
            };
            sessionId = row.Id;
            db.Writer.Enqueue(DbWriteOp.SessionStart(row));
            // Dispose drains the writer + checkpoints.
        }

        using (var db = new ProfilerDatabase(_root, log: NullLog, profilerVersion: "test"))
        {
            var found = db.Sessions.FindById(sessionId);
            Assert.NotNull(found);
            Assert.Equal("0.3-test", found!.ProfilerVersion);
        }
    }

    [Fact]
    public void SessionEnd_AfterStart_MarksDoneAndCarriesDuration()
    {
        ObjectId sessionId;
        using (var db = new ProfilerDatabase(_root, log: NullLog, profilerVersion: "test"))
        {
            sessionId = ObjectId.NewObjectId();
            db.Writer.Enqueue(DbWriteOp.SessionStart(new SessionRow
            {
                Id = sessionId,
                StartedUtc = DateTime.UtcNow,
                ProfilerVersion = "test",
                ModlistFingerprint = "abc",
                Mode = "lite",
                Incomplete = true,
            }));
            db.Writer.Enqueue(DbWriteOp.SessionEnd(sessionId, endReason: "clean", durationMs: 12345, ticksObserved: 678));
        }

        using (var db = new ProfilerDatabase(_root, log: NullLog, profilerVersion: "test"))
        {
            var found = db.Sessions.FindById(sessionId);
            Assert.NotNull(found);
            Assert.False(found!.Incomplete);
            Assert.Equal(12345, found.DurationMs);
            Assert.Equal(678, found.TicksObserved);
            Assert.Equal("clean", found.EndReason);
            Assert.NotNull(found.EndedUtc);
        }
    }

    [Fact]
    public void WarmAggregate_DuplicateSecondIndex_IsIdempotent()
    {
        ObjectId sessionId = ObjectId.NewObjectId();
        using (var db = new ProfilerDatabase(_root, log: NullLog, profilerVersion: "test"))
        {
            db.Writer.Enqueue(DbWriteOp.SessionStart(new SessionRow
            {
                Id = sessionId,
                StartedUtc = DateTime.UtcNow,
                ProfilerVersion = "test",
                ModlistFingerprint = "abc",
                Mode = "lite",
            }));
            // Two warm rows with same (sessionId, secondIndex).
            db.Writer.Enqueue(DbWriteOp.WarmAggregate(new TickAggregateWarm
            {
                SessionId = sessionId,
                SecondIndex = 1,
                AvgFrameMs = 16.7,
                ExpireAtUtc = DateTime.UtcNow.AddHours(24),
            }));
            db.Writer.Enqueue(DbWriteOp.WarmAggregate(new TickAggregateWarm
            {
                SessionId = sessionId,
                SecondIndex = 1,
                AvgFrameMs = 99.9,        // a different value; should overwrite
                ExpireAtUtc = DateTime.UtcNow.AddHours(24),
            }));
        }

        using (var db = new ProfilerDatabase(_root, log: NullLog, profilerVersion: "test"))
        {
            var rows = db.TickAggregatesWarm.Find(x => x.SessionId == sessionId).ToList();
            Assert.Single(rows);
            Assert.Equal(99.9, rows[0].AvgFrameMs, precision: 3);
        }
    }

    [Fact]
    public void CrashDetected_Marking_FiresOnReopen()
    {
        ObjectId sessionId = ObjectId.NewObjectId();
        using (var db = new ProfilerDatabase(_root, log: NullLog, profilerVersion: "test"))
        {
            db.Writer.Enqueue(DbWriteOp.SessionStart(new SessionRow
            {
                Id = sessionId,
                StartedUtc = DateTime.UtcNow,
                ProfilerVersion = "test",
                ModlistFingerprint = "abc",
                Mode = "lite",
                Incomplete = true,
                // Note: no SessionEnd enqueued — simulates a crash.
            }));
        }

        // Reopen — orphan should be marked crash-detected.
        using (var db = new ProfilerDatabase(_root, log: NullLog, profilerVersion: "test"))
        {
            var found = db.Sessions.FindById(sessionId);
            Assert.NotNull(found);
            Assert.Equal("crash-detected", found!.EndReason);
            Assert.False(found.Incomplete);
            Assert.Null(found.EndedUtc); // honest: we don't fabricate an end time
        }
    }

    [Fact]
    public void Backups_RotateOnDispose_KeepsLastThree()
    {
        // Five session opens => five backup rotations. Only last three survive.
        for (int i = 0; i < 5; i++)
        {
            using var db = new ProfilerDatabase(_root, log: NullLog, profilerVersion: "test");
            db.Writer.Enqueue(DbWriteOp.SessionStart(new SessionRow
            {
                StartedUtc = DateTime.UtcNow,
                ProfilerVersion = "test",
                ModlistFingerprint = "abc-" + i,
                Mode = "lite",
            }));
        }

        Assert.True(File.Exists(ProfilerPathsTestHelpers.Of(_root,1)));
        Assert.True(File.Exists(ProfilerPathsTestHelpers.Of(_root,2)));
        Assert.True(File.Exists(ProfilerPathsTestHelpers.Of(_root,3)));
        Assert.False(File.Exists(ProfilerPathsTestHelpers.Of(_root,4)));
    }

    [Fact]
    public void Spike_AndStall_RowsLand()
    {
        ObjectId sessionId = ObjectId.NewObjectId();
        using (var db = new ProfilerDatabase(_root, log: NullLog, profilerVersion: "test"))
        {
            db.Writer.Enqueue(DbWriteOp.SessionStart(new SessionRow
            {
                Id = sessionId,
                StartedUtc = DateTime.UtcNow,
                ProfilerVersion = "test",
                ModlistFingerprint = "abc",
                Mode = "lite",
            }));
            db.Writer.Enqueue(DbWriteOp.Spike(new SpikeWindowRow
            {
                SessionId = sessionId,
                StartTick = 100, EndTick = 110, WorstTick = 105,
                WorstFrameMs = 87.3, BaselineMs = 16.7, MadMs = 0.4,
                Context = "test-context",
            }));
            db.Writer.Enqueue(DbWriteOp.Stall(new StallEventRow
            {
                SessionId = sessionId,
                TickIndex = 200,
                UnixMs = 1234567,
                DurationMs = 250,
                BaselineTickMs = 16.7,
                Cause = "GcPause",
            }));
        }

        using (var db = new ProfilerDatabase(_root, log: NullLog, profilerVersion: "test"))
        {
            var spikes = db.SpikeWindows.Find(x => x.SessionId == sessionId).ToList();
            Assert.Single(spikes);
            Assert.Equal(87.3, spikes[0].WorstFrameMs, precision: 3);

            var stalls = db.Stalls.Find(x => x.SessionId == sessionId).ToList();
            Assert.Single(stalls);
            Assert.Equal("GcPause", stalls[0].Cause);
        }
    }

    private static void NullLog(string _, Exception? __) { }
}

/// <summary>Test-only helper to build a backup-N path against an arbitrary root.</summary>
internal static class ProfilerPathsTestHelpers
{
    public static string Of(string root, int n) => Path.Combine(root, PersistenceFileNames.BackupPrefix + n);
}
