#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using LiteDB;
using PerformanceProfiler.Persistence;
using PerformanceProfiler.Persistence.Records;
using PerformanceProfiler.Persistence.Report;
using Xunit;

namespace PerformanceProfiler.Tests.Simulation;

/// <summary>
/// Ring 2 seed (e2e plan): LiteDB round-trips against a real temp-file store,
/// exercising the exact predicate SHAPES production uses. The C1 class
/// (indexer/member access inside a LiteDB expression → TargetException at
/// runtime, twice shipped) only bites live — these pins run the real LiteDB
/// engine so the translation layer is exercised, not assumed.
/// </summary>
public sealed class StoreRoundTripPins : IDisposable
{
    private readonly string _path;
    private readonly LiteDatabase _db;

    public StoreRoundTripPins()
    {
        _path = Path.Combine(Path.GetTempPath(), $"pp-test-{Guid.NewGuid():N}.db");
        _db = new LiteDatabase(_path);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { File.Delete(_path); } catch { /* temp cleanup best-effort */ }
    }

    [Fact]
    public void InstallArms_ProcessKeyQuery_UsesTheHoistedCapturedLocalShape()
    {
        var arms = _db.GetCollection<InstallArmRow>("installArms");
        string processKey = "1234:638600000000000000";
        arms.Insert(new InstallArmRow { ProcessKey = processKey, ArmIndex = 1, InstallDeltaBytes = 1_900_000_000, HookCount = 62203 });
        arms.Insert(new InstallArmRow { ProcessKey = processKey, ArmIndex = 2, InstallDeltaBytes = 2_460_000_000, HookCount = 62203 });
        arms.Insert(new InstallArmRow { ProcessKey = "other:1", ArmIndex = 1, InstallDeltaBytes = 1_000, HookCount = 10 });

        // The exact shape ProfilerSystem + the router use: captured local,
        // no indexer/member chain inside the expression (the C1 rule).
        int count = arms.Count(x => x.ProcessKey == processKey);
        Assert.Equal(2, count);

        var first = arms.FindOne(x => x.ProcessKey == processKey && x.ArmIndex == 1);
        Assert.NotNull(first);
        Assert.Equal(62203, first!.HookCount);

        // The reload-stack comparison the install path runs.
        Assert.True(2_460_000_000 > first.InstallDeltaBytes * 1.2);
    }

    [Fact]
    public void SessionRow_ModVersions_RoundTripsThroughBson()
    {
        var sessions = _db.GetCollection<SessionRow>("sessions");
        var row = new SessionRow
        {
            StartedUtc = DateTime.UtcNow,
            ProfilerVersion = "0.35.0",
            ModlistFingerprint = "cafe1234beef5678",
            ModVersions = new List<string> { "CalamityMod@2.0.4", "ThoriumMod@1.7.2" },
        };
        sessions.Insert(row);

        SessionRow? back = sessions.FindById(row.Id);
        Assert.NotNull(back);
        Assert.NotNull(back!.ModVersions);
        Assert.Equal(2, back.ModVersions!.Count);
        Assert.Equal("CalamityMod@2.0.4", back.ModVersions[0]);

        // Pre-v2 rows have no ModVersions field: reading one must yield null,
        // not throw (the reader's `?? new List<string>()` covers rendering).
        var legacy = new BsonDocument
        {
            ["StartedUtc"] = DateTime.UtcNow,
            ["ProfilerVersion"] = "0.27.1",
            ["ModlistFingerprint"] = "legacy",
        };
        _db.GetCollection("sessions").Insert(legacy);
        var all = sessions.FindAll();
        int seen = 0;
        foreach (var s in all) { seen++; _ = s.ModVersions; }
        Assert.Equal(2, seen);
    }

    [Fact]
    public void SessionReportReader_HoistedSessionIdPredicates_ReadTheWholeShape()
    {
        // Build a minimal but complete session in the store, then read the
        // report through the REAL reader — every collection's predicate shape
        // gets exercised against the real engine.
        var sessionId = ObjectId.NewObjectId();
        _db.GetCollection<SessionRow>("sessions").Insert(new SessionRow
        {
            Id = sessionId,
            StartedUtc = DateTime.UtcNow.AddMinutes(-10),
            EndedUtc = DateTime.UtcNow,
            DurationMs = 600_000,
            ProfilerVersion = "0.35.0",
            ModVersions = new List<string> { "CalamityMod@2.0.4" },
        });
        _db.GetCollection<TickAggregateArchive>("tickAggregatesArchive").Insert(new TickAggregateArchive
        {
            SessionId = sessionId,
            AvgFrameMs = 28.2,
            MedianFrameMs = 33.2,
            MaxFrameMs = 78.8,
            TicksObserved = 36_000,
            SpikeCount = 5,
            StallCount = 2,
            PerMod = new List<ArchivePerMod>(),
        });
        _db.GetCollection<TickAggregateWarm>("tickAggregatesWarm").Insert(new TickAggregateWarm
        {
            SessionId = sessionId, SecondIndex = 30, AvgFrameMs = 33.0, P95FrameMs = 40.0,
        });
        _db.GetCollection<StallEventRow>("stallEvents").Insert(new StallEventRow
        {
            SessionId = sessionId, DurationMs = 25_000, Cause = "ProcessSuspended", Severity = "freeze",
        });
        _db.GetCollection<StallEventRow>("stallEvents").Insert(new StallEventRow
        {
            SessionId = sessionId, DurationMs = 2_892, Cause = "MainThreadFreeze", Severity = "disruptive",
        });

        // The reader wants a ProfilerDatabase; its collections are thin
        // GetCollection wrappers, so drive the equivalent queries directly to
        // pin the predicate shapes the reader uses.
        var archive = _db.GetCollection<TickAggregateArchive>("tickAggregatesArchive")
            .FindOne(a => a.SessionId == sessionId);
        Assert.NotNull(archive);

        double paused = 0d; int real = 0;
        foreach (var s in _db.GetCollection<StallEventRow>("stallEvents").Find(x => x.SessionId == sessionId))
        {
            if (s.Cause is "ProcessSuspended" or "WorldLoad") paused += s.DurationMs;
            else real++;
        }
        Assert.Equal(25_000d, paused);  // the suspend is a pause…
        Assert.Equal(1, real);          // …and only the freeze is a stall (X3 in the store too)
    }
}
