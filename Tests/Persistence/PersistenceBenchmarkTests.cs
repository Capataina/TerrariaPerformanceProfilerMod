#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using LiteDB;
using PerformanceProfiler.Profiling.Persistence;
using PerformanceProfiler.Profiling.Persistence.Records;
using Xunit;
using Xunit.Abstractions;

namespace PerformanceProfiler.Tests.Persistence;

/// <summary>
/// "Let's see how well LiteDB performs" — synthetic throughput + size
/// benchmarks the next iteration cycle can read back as numbers.
///
/// These are not assertions about absolute speed (CI environments vary
/// wildly) — they are observability tests. Every fixture writes its
/// measurements to <see cref="ITestOutputHelper"/>; <c>dotnet test -v n</c>
/// prints them.
///
/// What we measure:
/// <list type="bullet">
/// <item><b>Enqueue throughput</b> — how fast the game thread can post ops
/// to the writer (no disk in the path). This is the per-tick budget impact.</item>
/// <item><b>Steady-state drain</b> — writer thread sustained ops/sec under
/// realistic mix (warm aggregates dominate, spikes rare).</item>
/// <item><b>Per-session size</b> — DB file size after a simulated 10-minute
/// session at 60 Hz (600 warm rows + 10 cold + 5 spikes + 1 archive +
/// 100 mod aggregates).</item>
/// <item><b>Read latency</b> — finding the last N sessions by start time
/// (the dashboard query).</item>
/// </list>
/// </summary>
public class PersistenceBenchmarkTests : IDisposable
{
    private readonly string _root;
    private readonly ITestOutputHelper _output;

    public PersistenceBenchmarkTests(ITestOutputHelper output)
    {
        _output = output;
        _root = Path.Combine(Path.GetTempPath(), "perfprofiler-bench-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void Enqueue_GameThread_Latency()
    {
        using var db = new ProfilerDatabase(_root, log: NullLog, profilerVersion: "bench");
        ObjectId sid = ObjectId.NewObjectId();
        db.Writer.Enqueue(DbWriteOp.SessionStart(NewSessionRow(sid)));

        // Warm the JIT.
        for (int i = 0; i < 100; i++)
            db.Writer.Enqueue(DbWriteOp.WarmAggregate(NewWarmRow(sid, i)));

        const int N = 10_000;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < N; i++)
        {
            db.Writer.Enqueue(DbWriteOp.WarmAggregate(NewWarmRow(sid, 1000 + i)));
        }
        sw.Stop();

        double perOpNs = sw.Elapsed.TotalMilliseconds * 1_000_000d / N;
        _output.WriteLine($"Enqueue {N} ops in {sw.Elapsed.TotalMilliseconds:F2}ms = {perOpNs:F1} ns/op (game-thread cost).");

        // Sanity: at less than 100 microseconds per op the per-tick budget
        // (1 warm aggregate per second, max) is fine on a 60 Hz game thread.
        Assert.True(perOpNs < 100_000d, $"per-op cost {perOpNs:F0} ns is implausibly high");
    }

    [Fact]
    public void Steady_State_Drain_Throughput()
    {
        using var db = new ProfilerDatabase(_root, log: NullLog, profilerVersion: "bench");
        ObjectId sid = ObjectId.NewObjectId();
        db.Writer.Enqueue(DbWriteOp.SessionStart(NewSessionRow(sid)));

        // Drain initial enqueue.
        WaitForQueueIdle(db, maxMs: 5000);

        const int Ops = 5_000;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < Ops; i++)
        {
            db.Writer.Enqueue(DbWriteOp.WarmAggregate(NewWarmRow(sid, i)));
            // 1 spike for every 100 warm rows — realistic ratio.
            if (i % 100 == 0)
            {
                db.Writer.Enqueue(DbWriteOp.Spike(NewSpikeRow(sid, i)));
            }
        }

        // Wait for drain.
        WaitForQueueIdle(db, maxMs: 30_000);
        sw.Stop();

        long warmCount = db.TickAggregatesWarm.Count();
        long spikeCount = db.SpikeWindows.Count();
        double opsPerSec = (warmCount + spikeCount) / sw.Elapsed.TotalSeconds;

        _output.WriteLine(
            $"Drained {warmCount} warm + {spikeCount} spikes = {warmCount + spikeCount} ops in {sw.Elapsed.TotalSeconds:F2}s " +
            $"=> {opsPerSec:F0} ops/sec sustained.");

        // The game thread emits ~1 warm + ~0 spikes per tick at 60 Hz. We
        // need the writer to sustain at least 60 ops/sec; in practice
        // LiteDB on a modern SSD does thousands.
        Assert.True(opsPerSec > 60d, $"sustained {opsPerSec:F0} ops/sec is below the 60 ops/sec floor");
    }

    [Fact]
    public void Simulated_TenMinute_Session_FileSize()
    {
        using (var db = new ProfilerDatabase(_root, log: NullLog, profilerVersion: "bench"))
        {
            ObjectId sid = ObjectId.NewObjectId();
            db.Writer.Enqueue(DbWriteOp.SessionStart(NewSessionRow(sid)));

            // 10 minutes at 60 Hz = 600 warm rows + 10 cold + 5 spikes.
            for (int s = 0; s < 600; s++)
                db.Writer.Enqueue(DbWriteOp.WarmAggregate(NewWarmRow(sid, s)));
            for (int m = 0; m < 10; m++)
                db.Writer.Enqueue(DbWriteOp.ColdAggregate(NewColdRow(sid, m)));
            for (int i = 0; i < 5; i++)
                db.Writer.Enqueue(DbWriteOp.Spike(NewSpikeRow(sid, i * 60)));

            var modAggs = new List<PerSessionModAggregate>();
            for (int modId = 0; modId < 100; modId++)
            {
                modAggs.Add(new PerSessionModAggregate
                {
                    SessionId = sid, ModId = modId,
                    ModInternalName = "Mod" + modId,
                    AvgMs = modId * 0.05,
                    PeakMs = modId * 0.3,
                    TotalMs = modId * 100d,
                    CategoryMs = new List<double> { modId * 0.01, modId * 0.005, modId * 0.02, 0, 0, 0, 0 },
                });
            }
            db.Writer.Enqueue(DbWriteOp.ModAggregateBatch(sid, modAggs));

            db.Writer.Enqueue(DbWriteOp.ArchiveAggregate(new TickAggregateArchive
            {
                SessionId = sid,
                AvgFrameMs = 16.7,
                MedianFrameMs = 16.4,
                P95FrameMs = 25.1,
                P99FrameMs = 38.2,
                MaxFrameMs = 87.3,
                TotalGcMs = 230,
                TicksObserved = 36_000,
                SpikeCount = 5,
                StallCount = 1,
                PerMod = modAggs.Select(m => new ArchivePerMod
                {
                    ModId = m.ModId, Name = m.ModInternalName,
                    AvgMs = m.AvgMs, TotalMs = m.TotalMs, PeakMs = m.PeakMs,
                }).ToList(),
            }));

            db.Writer.Enqueue(DbWriteOp.SessionEnd(sid, "clean", 600_000, 36_000));
            WaitForQueueIdle(db, maxMs: 30_000);
        }

        long dbBytes = new FileInfo(Path.Combine(_root, PersistenceFileNames.Db)).Length;
        long journalBytes = new FileInfo(Path.Combine(_root, PersistenceFileNames.Journal)).Length;
        _output.WriteLine(
            $"Simulated 10-min session, 100 mods. " +
            $"DB={dbBytes / 1024d:F1} KB, journal={journalBytes / 1024d:F1} KB (post-clean-end).");

        // §3.4 of the migration plan: 10-min Calamity-scale ≈ 1 MB raw.
        // LiteDB pages in 8KB blocks so anything under 10 MB is healthy.
        Assert.InRange(dbBytes, 0, 10 * 1024 * 1024);
        // Journal truncates on clean shutdown.
        Assert.Equal(0, journalBytes);
    }

    [Fact]
    public void Read_LastNSessions_Latency()
    {
        const int SessionCount = 50;
        using (var db = new ProfilerDatabase(_root, log: NullLog, profilerVersion: "bench"))
        {
            for (int i = 0; i < SessionCount; i++)
            {
                db.Writer.Enqueue(DbWriteOp.SessionStart(new SessionRow
                {
                    StartedUtc = DateTime.UtcNow.AddMinutes(-i),
                    ProfilerVersion = "0.3-bench",
                    ModlistFingerprint = "abc",
                    Mode = "lite",
                }));
            }
            WaitForQueueIdle(db, maxMs: 10_000);
        }

        using (var db = new ProfilerDatabase(_root, log: NullLog, profilerVersion: "bench"))
        {
            var sw = Stopwatch.StartNew();
            var top = db.Sessions
                .Query()
                .OrderByDescending(x => x.StartedUtc)
                .Limit(10)
                .ToList();
            sw.Stop();

            _output.WriteLine($"FindTop10 from {SessionCount} sessions in {sw.Elapsed.TotalMilliseconds:F3} ms.");
            Assert.Equal(10, top.Count);
            Assert.True(sw.Elapsed.TotalMilliseconds < 250d,
                $"top-10 by date took {sw.Elapsed.TotalMilliseconds:F1} ms; expected under 250 ms");
        }
    }

    // ---- helpers ---------------------------------------------------------

    private static SessionRow NewSessionRow(ObjectId id) => new SessionRow
    {
        Id = id,
        StartedUtc = DateTime.UtcNow,
        ProfilerVersion = "bench",
        ModlistFingerprint = "bench-modlist",
        Mode = "lite",
        TracksAllocations = false,
        Incomplete = true,
    };

    private static TickAggregateWarm NewWarmRow(ObjectId sid, long secondIndex) => new TickAggregateWarm
    {
        SessionId = sid,
        SecondIndex = secondIndex,
        AvgFrameMs = 16.7 + (secondIndex % 7) * 0.1,
        P95FrameMs = 21.0,
        GcMs = 0.4,
        PerModMs = new List<double> { 1.1, 0.8, 2.4, 0.3 },
        ExpireAtUtc = DateTime.UtcNow.AddHours(24),
    };

    private static TickAggregateCold NewColdRow(ObjectId sid, long minuteIndex) => new TickAggregateCold
    {
        SessionId = sid,
        MinuteIndex = minuteIndex,
        AvgFrameMs = 16.5,
        P95FrameMs = 28.0,
        MaxFrameMs = 87.0,
        GcMs = 3.0,
        PerModMs = new List<double> { 1.1, 0.8, 2.4, 0.3 },
    };

    private static SpikeWindowRow NewSpikeRow(ObjectId sid, long tick) => new SpikeWindowRow
    {
        SessionId = sid,
        StartTick = tick,
        EndTick = tick + 5,
        WorstTick = tick + 2,
        WorstFrameMs = 87.0,
        BaselineMs = 16.7,
        MadMs = 0.4,
        Context = "bench-context",
        PerModCatMs = new List<double> { 1.1, 0.8, 2.4, 0.3 },
    };

    private static void WaitForQueueIdle(ProfilerDatabase db, int maxMs)
    {
        var sw = Stopwatch.StartNew();
        while (db.Writer.ApproxQueueDepth > 0 && sw.ElapsedMilliseconds < maxMs)
        {
            Thread.Sleep(20);
        }
    }

    private static void NullLog(string _, Exception? __) { }
}
