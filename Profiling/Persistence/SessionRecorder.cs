#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using LiteDB;
using PerformanceProfiler.Profiling.Persistence.Records;

namespace PerformanceProfiler.Profiling.Persistence;

/// <summary>
/// Per-world recorder. Lives one-to-one with a world load (constructed at
/// <c>OnWorldLoad</c>, ended at <c>OnWorldUnload</c>). Owns the session id,
/// the per-tick downsampler, and the cursor state for incremental spike +
/// stall + context-transition reads.
///
/// The game thread is the only caller; every recorder method either
/// returns immediately or enqueues onto the writer thread's queue. Nothing
/// here blocks on disk.
/// </summary>
public sealed class SessionRecorder
{
    private readonly ProfilerDatabase _db;
    private readonly ObjectId _sessionId;
    private readonly TickDownsampler _downsampler;
    private readonly DateTime _startedUtc;
    private readonly string _profilerVersion;
    private readonly string _modlistFingerprint;
    private readonly bool _tracksAllocations;

    private int _spikeCursor;
    private int _stallCursor;
    private long _ticksObserved;
    private double _maxFrameSeen;
    private double _gcSeen;

    public ObjectId SessionId => _sessionId;
    public DateTime StartedUtc => _startedUtc;
    public long TicksObserved => _ticksObserved;
    public TickDownsampler Downsampler => _downsampler;

    public SessionRecorder(
        ProfilerDatabase db,
        string profilerVersion,
        string tmlVersion,
        string mode,
        bool tracksAllocations,
        string modlistFingerprint,
        ObjectId? worldId)
    {
        _db = db;
        _sessionId = ObjectId.NewObjectId();
        _downsampler = new TickDownsampler();
        _startedUtc = DateTime.UtcNow;
        _profilerVersion = profilerVersion;
        _modlistFingerprint = modlistFingerprint;
        _tracksAllocations = tracksAllocations;

        var row = new SessionRow
        {
            Id = _sessionId,
            StartedUtc = _startedUtc,
            ProfilerVersion = profilerVersion,
            TmlVersion = tmlVersion,
            WorldId = worldId,
            ModlistFingerprint = modlistFingerprint,
            HookCoverageVersion = HookInterceptor.HookCoverageVersion,
            Mode = mode,
            TracksAllocations = tracksAllocations,
            EndReason = "clean",
            Incomplete = true,
        };
        _db.Writer.Enqueue(DbWriteOp.SessionStart(row));
    }

    /// <summary>
    /// Per-tick entry point. Drives the downsampler, drains any new spike
    /// windows and stall events, and bumps the local tick counter.
    /// </summary>
    public void OnTick(TickFrame frame, MetricCollector collector)
    {
        _ticksObserved++;
        if (frame.FrameTimeMs > _maxFrameSeen) _maxFrameSeen = frame.FrameTimeMs;
        _gcSeen += frame.GcTimeMs;

        _downsampler.OnTickCommitted(frame, collector, _db.Writer, _sessionId);

        DrainSpikes(collector);
        DrainStalls(collector);
    }

    /// <summary>
    /// Records a context transition. Caller resolves the type/from/to text
    /// (we don't reach into the ContextTagger here to keep the recorder
    /// tModLoader-runtime-agnostic — the recorder is testable without a
    /// running game).
    /// </summary>
    public void OnContextTransition(string type, string from, string to, long tick, double tickFrameMs)
    {
        var row = new ContextTransitionRow
        {
            SessionId = _sessionId,
            Tick = tick,
            Type = type,
            From = from,
            To = to,
            TickFrameMs = tickFrameMs,
        };
        _db.Writer.Enqueue(DbWriteOp.ContextTransition(row));
    }

    /// <summary>
    /// World-unload path: build the per-session aggregates, archive row,
    /// and session-end op. The whole set is enqueued; the writer thread
    /// drains everything during <see cref="ProfilerDatabase.Dispose"/>.
    /// </summary>
    public void End(MetricCollector collector, string endReason = "clean")
    {
        // Flush any final spike/stall windows that arrived after the last OnTick.
        DrainSpikes(collector);
        DrainStalls(collector);

        var modAggs = BuildModAggregates(collector);
        var hookAggs = BuildHookAggregates(collector);
        var archive = BuildArchive(collector, modAggs);

        if (modAggs.Count > 0)
        {
            _db.Writer.Enqueue(DbWriteOp.ModAggregateBatch(_sessionId, modAggs));
        }
        if (hookAggs.Count > 0)
        {
            _db.Writer.Enqueue(DbWriteOp.HookAggregateBatch(_sessionId, hookAggs));
        }
        _db.Writer.Enqueue(DbWriteOp.ArchiveAggregate(archive));

        long durationMs = (long)(DateTime.UtcNow - _startedUtc).TotalMilliseconds;
        _db.Writer.Enqueue(DbWriteOp.SessionEnd(_sessionId, endReason, durationMs, _ticksObserved));
    }

    // ---- private accumulation paths --------------------------------------

    private void DrainSpikes(MetricCollector collector)
    {
        IReadOnlyList<SpikeWindow> spikes = collector.Spikes;
        while (_spikeCursor < spikes.Count)
        {
            SpikeWindow w = spikes[_spikeCursor++];
            var row = new SpikeWindowRow
            {
                SessionId = _sessionId,
                StartTick = w.StartTick,
                EndTick = w.EndTick,
                WorstTick = w.WorstTick,
                WorstFrameMs = w.WorstFrameMs,
                BaselineMs = w.BaselineMs,
                MadMs = w.MadMs,
                Warming = w.Warming,
                Context = w.ContextSummary,
                PerModCatMs = ToList(w.PerModCatMs),
                PerModCatBytes = w.PerModCatBytes != null ? ToList(w.PerModCatBytes) : null,
                TopContributors = BuildSpikeTopContributors(w),
            };
            _db.Writer.Enqueue(DbWriteOp.Spike(row));
        }
    }

    private void DrainStalls(MetricCollector collector)
    {
        IReadOnlyList<StallEvent> stalls = collector.Stalls;
        while (_stallCursor < stalls.Count)
        {
            StallEvent s = stalls[_stallCursor++];
            var row = new StallEventRow
            {
                SessionId = _sessionId,
                TickIndex = s.StartTickIndex,
                UnixMs = s.StartTimestampUnixMs,
                DurationMs = s.TickPeriodMs,
                BaselineTickMs = s.BaselineMs,
                Cause = s.Cause.ToString(),
            };
            _db.Writer.Enqueue(DbWriteOp.Stall(row));
        }
    }

    private List<PerSessionModAggregate> BuildModAggregates(MetricCollector collector)
    {
        int modCount = PerModAttribution.ModCount;
        int categoryCount = PerModAttribution.CategoryCount;
        var result = new List<PerSessionModAggregate>(modCount);

        IReadOnlyList<double> avgMs = collector.PerModCategoryAverageMs;
        IReadOnlyList<double>? avgBytes = collector.PerModCategoryAverageBytes;
        IReadOnlyList<int> measured = HookInterceptor.MeasuredHookCounts;
        IReadOnlyList<int> total = HookInterceptor.TotalHookCounts;
        string[] names = HookInterceptor.ProfiledModNames;

        long durationMs = Math.Max(1L, _ticksObserved); // avoid div-by-zero

        for (int modId = 0; modId < modCount; modId++)
        {
            double totalCategoryMs = 0d;
            double peakCategoryMs = 0d;
            double totalBytes = 0d;
            double peakBytes = 0d;
            int offset = modId * categoryCount;
            var categorySlice = new List<double>(categoryCount);
            for (int cat = 0; cat < categoryCount; cat++)
            {
                int idx = offset + cat;
                double ms = idx < avgMs.Count ? avgMs[idx] : 0d;
                totalCategoryMs += ms;
                if (ms > peakCategoryMs) peakCategoryMs = ms;
                categorySlice.Add(ms);
                if (avgBytes != null && idx < avgBytes.Count)
                {
                    totalBytes += avgBytes[idx];
                    if (avgBytes[idx] > peakBytes) peakBytes = avgBytes[idx];
                }
            }

            int measuredHooks = modId < measured.Count ? measured[modId] : 0;
            int totalHooks = modId < total.Count ? total[modId] : 0;
            string badge = totalHooks == 0
                ? "n/a"
                : (measuredHooks >= totalHooks ? "full" : (measuredHooks * 2 >= totalHooks ? "partial" : "limited"));

            result.Add(new PerSessionModAggregate
            {
                SessionId = _sessionId,
                ModId = modId,
                ModInternalName = modId < names.Length ? names[modId] : ("mod-" + modId),
                AvgMs = totalCategoryMs,
                PeakMs = peakCategoryMs,
                TotalMs = totalCategoryMs * _ticksObserved,
                P95Ms = peakCategoryMs,           // placeholder until per-tick p95 lands
                AvgBytes = totalBytes,
                PeakBytes = peakBytes,
                TotalBytes = totalBytes * _ticksObserved,
                CategoryMs = categorySlice,
                Coverage = new ModCoverage
                {
                    Measured = measuredHooks,
                    Total = totalHooks,
                    Badge = badge,
                },
                TopHooks = BuildTopHooks(collector, modId),
            });
        }

        return result;
    }

    private List<TopHookEntry> BuildTopHooks(MetricCollector collector, int modId)
    {
        var hooks = PerModAttribution.Hooks;
        IReadOnlyList<double> hookAvg = collector.PerHookAverageMs;
        var entries = new List<TopHookEntry>();
        for (int hookId = 0; hookId < hooks.Count; hookId++)
        {
            if (hooks[hookId].ModId != modId) continue;
            double ms = hookId < hookAvg.Count ? hookAvg[hookId] : 0d;
            if (ms <= 0d) continue;
            entries.Add(new TopHookEntry
            {
                HookId = hookId,
                DisplayName = hooks[hookId].DisplayName,
                AvgMs = ms,
            });
        }
        entries.Sort((a, b) => b.AvgMs.CompareTo(a.AvgMs));
        if (entries.Count > 5) entries.RemoveRange(5, entries.Count - 5);
        return entries;
    }

    private List<PerSessionHookAggregate> BuildHookAggregates(MetricCollector collector)
    {
        var hooks = PerModAttribution.Hooks;
        IReadOnlyList<double> hookAvg = collector.PerHookAverageMs;
        IReadOnlyList<double>? hookAvgBytes = collector.PerHookAverageBytes;
        var result = new List<PerSessionHookAggregate>(hooks.Count);
        for (int hookId = 0; hookId < hooks.Count; hookId++)
        {
            HookDescriptor desc = hooks[hookId];
            double avgMs = hookId < hookAvg.Count ? hookAvg[hookId] : 0d;
            double avgBytes = (hookAvgBytes != null && hookId < hookAvgBytes.Count) ? hookAvgBytes[hookId] : 0d;

            // Skip silent hooks regardless of allocation tracking — writing
            // a row per zero-ms zero-byte hook is what blew the DB up to
            // 5.8 MB in a 3-minute session (10,250 hooks × ~200 B row).
            // With this gate the per-session-hook batch carries only the
            // hooks that actually fired.
            if (avgMs <= 0d && avgBytes <= 0d) continue;

            result.Add(new PerSessionHookAggregate
            {
                SessionId = _sessionId,
                HookId = hookId,
                ModId = desc.ModId,
                CategoryId = desc.CategoryId,
                DisplayName = desc.DisplayName,
                AvgMs = avgMs,
                PeakMs = avgMs,
                TotalMs = avgMs * _ticksObserved,
                AvgBytes = avgBytes,
                CallCount = 0,
            });
        }
        return result;
    }

    private TickAggregateArchive BuildArchive(MetricCollector collector, List<PerSessionModAggregate> modAggs)
    {
        var perMod = new List<ArchivePerMod>(modAggs.Count);
        foreach (var m in modAggs)
        {
            perMod.Add(new ArchivePerMod
            {
                ModId = m.ModId,
                Name = m.ModInternalName,
                AvgMs = m.AvgMs,
                TotalMs = m.TotalMs,
                PeakMs = m.PeakMs,
                TotalBytes = m.TotalBytes,
            });
        }

        // Frame distribution from history. The RingBuffer's enumerator is
        // oldest-first; we only need a one-pass average + max for the
        // archive — p95/p99 will be lifted out of warm-tier data by future
        // queries.
        double totalFrame = 0d;
        double max = _maxFrameSeen;
        int n = collector.History.Count;
        for (int i = 0; i < n; i++) totalFrame += collector.History[i].FrameTimeMs;
        double avg = n > 0 ? totalFrame / n : 0d;

        return new TickAggregateArchive
        {
            SessionId = _sessionId,
            AvgFrameMs = avg,
            MedianFrameMs = avg,
            P95FrameMs = max,        // placeholder; warm-tier P95 is the precise source
            P99FrameMs = max,
            MaxFrameMs = max,
            TotalGcMs = _gcSeen,
            TicksObserved = _ticksObserved,
            SpikeCount = collector.Spikes.Count,
            StallCount = collector.Stalls.Count,
            PerMod = perMod,
        };
    }

    private List<SpikeContributor> BuildSpikeTopContributors(SpikeWindow w)
    {
        int modCount = PerModAttribution.ModCount;
        int categoryCount = PerModAttribution.CategoryCount;
        string[] names = HookInterceptor.ProfiledModNames;
        var contributors = new List<SpikeContributor>();
        for (int modId = 0; modId < modCount; modId++)
        {
            double ms = 0d;
            double bytes = 0d;
            int offset = modId * categoryCount;
            for (int cat = 0; cat < categoryCount; cat++)
            {
                int idx = offset + cat;
                if (idx < w.PerModCatMs.Length) ms += w.PerModCatMs[idx];
                if (w.PerModCatBytes != null && idx < w.PerModCatBytes.Length) bytes += w.PerModCatBytes[idx];
            }
            if (ms > 0d)
            {
                contributors.Add(new SpikeContributor
                {
                    ModId = modId,
                    Name = modId < names.Length ? names[modId] : ("mod-" + modId),
                    Ms = ms,
                    Bytes = bytes,
                });
            }
        }
        contributors.Sort((a, b) => b.Ms.CompareTo(a.Ms));
        if (contributors.Count > 5) contributors.RemoveRange(5, contributors.Count - 5);
        return contributors;
    }

    private static List<double> ToList(float[] arr)
    {
        var list = new List<double>(arr.Length);
        for (int i = 0; i < arr.Length; i++) list.Add(arr[i]);
        return list;
    }
}
