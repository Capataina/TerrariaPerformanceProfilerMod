#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Persistence;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Data.Detectors;

namespace PerformanceProfiler.Web;

internal static partial class DashboardRouter
{
    // ----------------------------------------------------------------------
    // /api/now — the live tick. Polled at ~250 ms cadence.
    // ----------------------------------------------------------------------
    private static string BuildNow()
    {
        // v0.10 — BuildNow migrated end-to-end onto the data pipeline.
        // Previously this method reached into ProfilerSystem.Collector,
        // .Segments, and the raw History/Spikes/Stalls lists from the
        // HTTP worker thread; with the game thread mutating those
        // collections concurrently, the indexer reads were a real (if
        // small-window) data race. All values now come from snapshots
        // pulled through DataRegistry.Shared.
        var frameSnap = Data.DataRegistry.Shared
            .Lookup<Data.Collectors.FrameTimeSnapshot>(Data.Collectors.FrameTimeCollector.StreamName)?
            .CurrentSnapshot() ?? Data.Collectors.FrameTimeSnapshot.Empty;
        if (!frameSnap.WorldLoaded || frameSnap.History == null || frameSnap.History.Count == 0)
        {
            // No live world — fall back to the last persisted session so the
            // Summary KPIs populate from history instead of going blank
            // ("reading from db" mode). worldLoaded is reported true so the
            // existing renderers + overlay treat it as showable; the source
            // field carries the truth for the topbar. Live-only surfaces (frame
            // trace, segments) still return their own empty state.
            var last = DbReadModel.GetLastSession();
            if (last != null)
            {
                var a = last.Archive;
                double avgFps = a.AvgFrameMs > 0d ? 1000d / a.AvgFrameMs : 0d;
                // LiteDB is opened with UTC_DATE=false, so stored UTC dates read
                // back as Local-kind. Re-label as UTC before any offset math: a
                // Local-kind value paired with a zero offset throws in
                // DateTimeOffset ("UTC Offset ... does not match"), which 500'd
                // this whole endpoint and left the dashboard stuck on "no world".
                DateTime endedUtc = DateTime.SpecifyKind(last.EndedUtc, DateTimeKind.Utc);
                return JsonSerializer.Serialize(new
                {
                    worldLoaded = true,
                    source = "db",
                    sessionLabel = endedUtc.ToLocalTime().ToString("MMM d · HH:mm"),
                    sessionEndedUnixMs = new DateTimeOffset(endedUtc).ToUnixTimeMilliseconds(),
                    unixMs = Time.UnixMsNow(),
                    tickIndex = a.TicksObserved,
                    frameMs = a.AvgFrameMs,
                    avg30sMs = a.AvgFrameMs,
                    gcMs = 0d,
                    npcCount = 0, projectileCount = 0, dustCount = 0,
                    openSegmentCount = 0,
                    historyDepth = 0,
                    spikeCount = a.SpikeCount,
                    stallCount = a.StallCount,
                    tracksAllocations = false,
                    allocBytesPerTick = 0d,
                    installDeltaMb = 0d, processWorkingSetMb = 0d, bytesPerHookKb = 0d, hookCount = 0,
                    severity = "Healthy", backend = "—",
                    kpi = new
                    {
                        avgFps,
                        // Archived sessions carry no draw-beat data; 0 renders
                        // as a dash in the KPI card rather than a fake number.
                        renderFps = 0d,
                        worstFrameMs = a.MaxFrameMs,
                        bestFrameMs = 0d,
                        medianFrameMs = a.MedianFrameMs,
                        lagSpikeCount = a.SpikeCount,
                        totalLagMs = 0d,
                        stallCount = a.StallCount,
                        worstStallMs = 0d,
                        avgStallMs = 0d,
                        spikeCount = a.SpikeCount,
                        sampleN = a.TicksObserved,
                    },
                }, JsonOpts);
            }
            return JsonSerializer.Serialize(new
            {
                worldLoaded = false,
                source = "none",
                unixMs = Time.UnixMsNow(),
            }, JsonOpts);
        }
        var history = frameSnap.History;
        var latest = history[history.Count - 1];

        KpiSnapshot kpi = Data.DataRegistry.Shared
            .Lookup<KpiSnapshot>(Data.Stats.KpiStat.StreamName)?.CurrentSnapshot()
            ?? default;
        var selfHealth = Data.DataRegistry.Shared
            .Lookup<Data.Stats.SelfHealthSnapshot>(Data.Stats.SelfHealthStat.StreamName)?
            .CurrentSnapshot() ?? Data.Stats.SelfHealthSnapshot.Empty;
        var allocSnap = Data.DataRegistry.Shared
            .Lookup<Data.Collectors.AllocationSnapshot>(Data.Collectors.AllocationCollector.StreamName)?
            .CurrentSnapshot() ?? Data.Collectors.AllocationSnapshot.Empty;
        var spikesSnap = Data.DataRegistry.Shared
            .Lookup<Data.Stats.SpikesSnapshot>(Data.Stats.SpikesStat.StreamName)?
            .CurrentSnapshot() ?? Data.Stats.SpikesSnapshot.Empty;
        var stallsSnap = Data.DataRegistry.Shared
            .Lookup<Data.Stats.StallsSnapshot>(Data.Stats.StallsStat.StreamName)?
            .CurrentSnapshot() ?? Data.Stats.StallsSnapshot.Empty;
        var segSnap = Data.DataRegistry.Shared
            .Lookup<Data.Aggregators.SegmentsSnapshot>(Data.Aggregators.SegmentAggregator.StreamName)?
            .CurrentSnapshot() ?? Data.Aggregators.SegmentsSnapshot.Empty;

        // 30s rolling average — local to the formatting layer; trivial
        // arithmetic over the snapshot, not a derivation in the spirit
        // of "if it produces a number it lives in Data/" (a future
        // refinement: hoist into KpiSnapshot itself).
        double session30sAvg = AverageRecent(history, 30 * 60);

        double allocBytesPerTick = 0d;
        bool tracksAlloc = allocSnap.TracksAllocations && allocSnap.SmoothedBytesByCategory != null;
        if (tracksAlloc)
        {
            var bytes = allocSnap.SmoothedBytesByCategory!;
            for (int i = 0; i < bytes.Count; i++) allocBytesPerTick += bytes[i];
        }

        int openSegmentCount = segSnap.Open?.Count ?? 0;
        int spikeCount = spikesSnap.Windows?.Count ?? 0;
        int stallCount = stallsSnap.Events?.Count ?? 0;

        return JsonSerializer.Serialize(new
        {
            worldLoaded = true,
            source = "live",
            unixMs = Time.UnixMsNow(),
            tickIndex = latest.TickIndex,
            frameMs = latest.RealFrameTimeMs,
            avg30sMs = session30sAvg,
            gcMs = latest.GcTimeMs,
            npcCount = latest.NpcCount,
            projectileCount = latest.ProjectileCount,
            dustCount = latest.DustCount,
            openSegmentCount,
            historyDepth = history.Count,
            spikeCount,
            stallCount,
            tracksAllocations = tracksAlloc,
            allocBytesPerTick,
            installDeltaMb = selfHealth.InstallDeltaBytes / (1024d * 1024d),
            processWorkingSetMb = selfHealth.ProcessWorkingSetBytes / (1024d * 1024d),
            bytesPerHookKb = selfHealth.BytesPerHook / 1024d,
            hookCount = selfHealth.InstalledHookCount,
            severity = selfHealth.Severity.ToString(),
            backend = selfHealth.BackendMode.ToString(),
            kpi = new
            {
                avgFps = kpi.AvgFps,
                renderFps = kpi.RenderFps,
                worstFrameMs = kpi.WorstFrameMs,
                bestFrameMs = kpi.BestFrameMs,
                medianFrameMs = kpi.MedianFrameMs,
                lagSpikeCount = kpi.LagSpikeCount,
                totalLagMs = kpi.TotalLagMs,
                stallCount = kpi.StallCount,
                worstStallMs = kpi.WorstStallMs,
                avgStallMs = kpi.AvgStallMs,
                spikeCount = kpi.SpikeCount,
                sampleN = kpi.SampleN,
            },
        }, JsonOpts);
    }

    // ----------------------------------------------------------------------
    // /api/frames — last 30 s of raw frame times. The hero chart data.
    // ----------------------------------------------------------------------
    private static string BuildFrames()
    {
        // Migration step 11 — frames via the FrameTimeCollector adapter.
        // The adapter exposes the live history reference; the router
        // does the format conversion (per-tick frame ms → ticks/ms/gc
        // arrays) but performs no derivation. The spike-mark join is a
        // simple in-range filter, not a derivation.
        var snap = Data.DataRegistry.Shared
            .Lookup<Data.Collectors.FrameTimeSnapshot>(Data.Collectors.FrameTimeCollector.StreamName)?
            .CurrentSnapshot() ?? Data.Collectors.FrameTimeSnapshot.Empty;
        var history = snap.History;
        if (!snap.WorldLoaded || history == null || history.Count == 0)
        {
            return JsonSerializer.Serialize(new { worldLoaded = false, frames = Array.Empty<object>() }, JsonOpts);
        }

        // Spike-marks live on the collector; pull via the spikes stat so
        // the router doesn't reach into MetricCollector directly.
        var spikesSnap = Data.DataRegistry.Shared
            .Lookup<Data.Stats.SpikesSnapshot>(Data.Stats.SpikesStat.StreamName)?
            .CurrentSnapshot() ?? Data.Stats.SpikesSnapshot.Empty;

        int n = history.Count;
        var ticks = new long[n];
        var ms = new double[n];
        var gc = new double[n];
        long firstTick = history[0].TickIndex;
        long lastTick = history[n - 1].TickIndex;
        for (int i = 0; i < n; i++)
        {
            ticks[i] = history[i].TickIndex;
            ms[i] = history[i].RealFrameTimeMs;
            gc[i] = history[i].GcTimeMs;
        }

        // Spike markers within the visible window — one entry per spike
        // whose WorstTick falls in [firstTick, lastTick]. Lets the dashboard
        // overlay spike dots on the chart without re-running the detector.
        var spikeMarks = new List<object>();
        var spikeWindows = spikesSnap.Windows ?? (IReadOnlyList<SpikeWindow>)Array.Empty<SpikeWindow>();
        foreach (var w in spikeWindows)
        {
            if (w.WorstTick < firstTick || w.WorstTick > lastTick) continue;
            spikeMarks.Add(new
            {
                tick = w.WorstTick,
                ms = w.WorstFrameMs,
                warming = w.Warming,
            });
        }

        return JsonSerializer.Serialize(new
        {
            worldLoaded = true,
            firstTick,
            lastTick,
            ticks,
            frameMs = ms,
            gcMs = gc,
            spikeMarks,
        }, JsonOpts);
    }

    // ----------------------------------------------------------------------
    // /api/heatmap — session frame-time heatmap. One bucket per minute of
    // play; each bucket carries avg/worst frame-ms plus boss-segment
    // overlay info (which minutes had boss fights, so the dashboard can
    // highlight those cells with a red background tint).
    //
    // Currently derives buckets from the same in-memory History the chart
    // reads (so the heatmap only spans the last 30 seconds — the full
    // rolling window we have). A future iteration could pull from
    // TickAggregateWarm in the LiteDB for the entire session.
    // ----------------------------------------------------------------------
    private static string BuildHeatmap()
    {
        // v0.9.x unified data pipeline migration step 5 — the canonical
        // "kill the inline math" extraction. The bucketing + boss-overlay
        // join used to live inline in this method; now owned by
        // Data.Aggregators.HeatmapAggregator. The router just serialises.
        var snap = Data.DataRegistry.Shared
            .Lookup<Data.Aggregators.HeatmapSnapshot>(Data.Aggregators.HeatmapAggregator.StreamName)?
            .CurrentSnapshot() ?? Data.Aggregators.HeatmapSnapshot.Empty;

        if (!snap.WorldLoaded)
        {
            return JsonSerializer.Serialize(new { worldLoaded = false, buckets = Array.Empty<object>() }, JsonOpts);
        }

        var bucketsOut = new List<object>(snap.Buckets.Count);
        foreach (var b in snap.Buckets)
        {
            bucketsOut.Add(new
            {
                startUnixMs = b.StartUnixMs,
                ticks = b.Ticks,
                avgMs = b.AvgMs,
                worstMs = b.WorstMs,
            });
        }
        var bossOut = new List<object>(snap.BossOverlays.Count);
        foreach (var o in snap.BossOverlays)
        {
            bossOut.Add(new
            {
                name = o.Name,
                startUnixMs = o.StartUnixMs,
                endUnixMs = o.EndUnixMs,
                killed = o.Killed,
            });
        }
        return JsonSerializer.Serialize(new
        {
            worldLoaded = true,
            bucketMs = snap.BucketMs,
            buckets = bucketsOut,
            bossOverlays = bossOut,
        }, JsonOpts);
    }

    private static double AverageRecent(RingBuffer<TickFrame> hist, int max)
    {
        int n = hist.Count < max ? hist.Count : max;
        if (n == 0) return 0d;
        double sum = 0d;
        int start = hist.Count - n;
        for (int i = 0; i < n; i++) sum += hist[start + i].RealFrameTimeMs;
        return sum / n;
    }
}
