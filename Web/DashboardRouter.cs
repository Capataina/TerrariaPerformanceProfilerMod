#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;
using Terraria.ModLoader;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Insights;
using PerformanceProfiler.Profiling.Segments;
using PerformanceProfiler.Profiling.Stats;
using PerformanceProfiler.Web.Server;

namespace PerformanceProfiler.Web;

/// <summary>
/// Routes incoming HTTP requests to either the dashboard SPA bundle or
/// one of the JSON state endpoints. Strict allowlist — anything not
/// matched returns 404.
///
/// <para>
/// <b>API surface.</b> Every endpoint returns a flat anonymous object
/// serialised by <c>System.Text.Json</c>. Shapes are documented inline at
/// each builder method. All endpoints are safe to call when no world is
/// loaded — they return <c>worldLoaded: false</c> + the minimum stable
/// fields and the dashboard JS handles that as a "between sessions" state.
/// </para>
/// </summary>
internal static class DashboardRouter
{
    private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
    {
        // The dashboard is a single consumer we control; keep the wire
        // compact (no indent) and predictable (invariant culture).
        WriteIndented = false,
    };

    // The CSS / JS bundles are immutable for the mod's lifetime; encode
    // them once at type-init so /dashboard.css and /dashboard.js don't
    // re-UTF-8-encode on every request (cold-tab refreshes were paying
    // ~tens of KB allocator pressure per hit).
    private static readonly byte[] CachedCssBytes
        = System.Text.Encoding.UTF8.GetBytes(DashboardAssets.Css);
    private static readonly byte[] CachedJsBytes
        = System.Text.Encoding.UTF8.GetBytes(DashboardAssets.Js);

    public static HttpResponse Route(HttpRequest req)
    {
        if (req.Method != "GET") return HttpResponse.PlainText(405, "Method Not Allowed");
        return req.Path switch
        {
            "/"                  => HttpResponse.Html(DashboardAssets.IndexHtml),
            "/dashboard.css"     => new HttpResponse(200, "text/css; charset=utf-8", CachedCssBytes),
            "/dashboard.js"      => new HttpResponse(200, "application/javascript; charset=utf-8", CachedJsBytes),
            "/favicon.ico"       => new HttpResponse(200, "image/x-icon", Array.Empty<byte>()),

            "/api/now"           => HttpResponse.Json(BuildNow()),
            "/api/mods"          => HttpResponse.Json(BuildMods()),
            "/api/hooks"         => HttpResponse.Json(BuildHooks()),
            "/api/frames"        => HttpResponse.Json(BuildFrames()),
            "/api/segments"      => HttpResponse.Json(BuildSegments()),
            "/api/spikes"        => HttpResponse.Json(BuildSpikes()),
            "/api/stalls"        => HttpResponse.Json(BuildStalls()),
            "/api/insights"      => HttpResponse.Json(BuildInsights()),
            "/api/self"          => HttpResponse.Json(BuildSelf()),
            "/api/heatmap"       => HttpResponse.Json(BuildHeatmap()),
            "/api/events"        => HttpResponse.Json(BuildEvents()),

            _                    => HttpResponse.NotFound,
        };
    }

    // ----------------------------------------------------------------------
    // /api/now — the live tick. Polled at ~250 ms cadence.
    // ----------------------------------------------------------------------
    private static string BuildNow()
    {
        ProfilerSystem? sys = ModContent.GetInstance<ProfilerSystem>();
        MetricCollector? c = sys?.Collector;
        SegmentDetector? seg = sys?.Segments;
        bool loaded = c != null && c.History.Count > 0;

        if (!loaded)
        {
            return JsonSerializer.Serialize(new
            {
                worldLoaded = false,
                unixMs = Time.UnixMsNow(),
            }, JsonOpts);
        }

        var latest = c!.History[c.History.Count - 1];
        ProfilerSelfHealth h = c.SelfHealth;
        double session30sAvg = AverageRecent(c.History, 30 * 60);

        // KPI snapshot via the data pipeline. The router does no arithmetic
        // here — the KpiStat owns the computation; we just emit its
        // snapshot. (v0.9.x unified data pipeline migration step 3.)
        KpiSnapshot kpi = Data.DataRegistry.Shared
            .Lookup<KpiSnapshot>(Data.Stats.KpiStat.StreamName)?.CurrentSnapshot()
            ?? default;

        // Sum the live per-mod allocation bytes (smoothed) into a single
        // "alloc bytes/tick across all mods" headline number. Null when
        // allocation tracking is off.
        double allocBytesPerTick = 0d;
        bool tracksAlloc = c.TracksAllocations && c.PerModCategoryBytes != null;
        if (tracksAlloc)
        {
            var bytes = c.PerModCategoryBytes!;
            for (int i = 0; i < bytes.Count; i++) allocBytesPerTick += bytes[i];
        }

        return JsonSerializer.Serialize(new
        {
            worldLoaded = true,
            unixMs = Time.UnixMsNow(),
            tickIndex = latest.TickIndex,
            frameMs = latest.FrameTimeMs,
            avg30sMs = session30sAvg,
            gcMs = latest.GcTimeMs,
            npcCount = latest.NpcCount,
            projectileCount = latest.ProjectileCount,
            dustCount = latest.DustCount,
            openSegmentCount = seg?.OpenSegments.Count ?? 0,
            historyDepth = c.History.Count,
            spikeCount = c.Spikes.Count,
            stallCount = c.Stalls.Count,
            tracksAllocations = tracksAlloc,
            allocBytesPerTick,
            installDeltaMb = h.InstallDeltaBytes / (1024d * 1024d),
            processWorkingSetMb = h.ProcessWorkingSetBytes / (1024d * 1024d),
            bytesPerHookKb = h.BytesPerHook / 1024d,
            hookCount = h.InstalledHookCount,
            severity = h.Severity.ToString(),
            backend = HookBackend.Mode.ToString(),
            kpi = new
            {
                avgFps = kpi.AvgFps,
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
    // /api/events — pre-merged events feed (segments + spikes + stalls).
    // Used to be assembled in JS by joining /api/segments + /api/spikes;
    // moved server-side so it's a single endpoint and the dashboard
    // doesn't need to re-merge on every render.
    // ----------------------------------------------------------------------
    private static string BuildEvents()
    {
        // v0.9.x unified data pipeline migration step 4. The router no
        // longer merges segments + spikes + stalls — that math is owned
        // by EventsFeedStat, which calls into the pure-logic
        // EventsFeed.Build. Router just serialises the snapshot.
        var snap = Data.DataRegistry.Shared
            .Lookup<Data.Stats.EventsFeedSnapshot>(Data.Stats.EventsFeedStat.StreamName)?
            .CurrentSnapshot() ?? Data.Stats.EventsFeedSnapshot.Empty;

        var serialised = new List<object>(snap.Events.Count);
        foreach (var e in snap.Events)
        {
            serialised.Add(new
            {
                kind = e.Kind,
                text = e.Text,
                unixMs = e.UnixMs,
                tickIndex = e.TickIndex,
            });
        }
        return JsonSerializer.Serialize(new
        {
            worldLoaded = snap.WorldLoaded,
            events = serialised,
        }, JsonOpts);
    }

    private static double AverageRecent(RingBuffer<TickFrame> hist, int max)
    {
        int n = hist.Count < max ? hist.Count : max;
        if (n == 0) return 0d;
        double sum = 0d;
        int start = hist.Count - n;
        for (int i = 0; i < n; i++) sum += hist[start + i].FrameTimeMs;
        return sum / n;
    }

    // ----------------------------------------------------------------------
    // /api/mods — per-mod ranking with per-category breakdown + allocation.
    // ----------------------------------------------------------------------
    private static string BuildMods()
    {
        // Migration step 11 — per-mod cost via the HookCpuCollector +
        // AllocationCollector adapters. Router does the per-mod
        // accumulation (sum across categories) for the wire shape, but
        // does not derive ratios, ranks, or thresholds — those are
        // visual choices made downstream in the JS.
        var cpuSnap = Data.DataRegistry.Shared
            .Lookup<Data.Collectors.HookCpuSnapshot>(Data.Collectors.HookCpuCollector.StreamName)?
            .CurrentSnapshot() ?? Data.Collectors.HookCpuSnapshot.Empty;
        var allocSnap = Data.DataRegistry.Shared
            .Lookup<Data.Collectors.AllocationSnapshot>(Data.Collectors.AllocationCollector.StreamName)?
            .CurrentSnapshot() ?? Data.Collectors.AllocationSnapshot.Empty;

        if (!cpuSnap.WorldLoaded || cpuSnap.SmoothedMsByCategory == null)
        {
            return JsonSerializer.Serialize(new { worldLoaded = false, mods = Array.Empty<object>() }, JsonOpts);
        }

        int categoryCount = cpuSnap.CategoryCount;
        string[] modNames = HookInterceptor.ProfiledModNames;
        IReadOnlyList<double> smoothed = cpuSnap.SmoothedMsByCategory;
        IReadOnlyList<double> averaged = cpuSnap.AverageMsByCategory!;
        IReadOnlyList<double>? smoothedBytes = allocSnap.SmoothedBytesByCategory;
        IReadOnlyList<double>? avgBytes = allocSnap.AverageBytesByCategory;
        bool tracksAlloc = allocSnap.TracksAllocations && smoothedBytes != null;

        var mods = new List<object>(modNames.Length);
        for (int i = 0; i < modNames.Length; i++)
        {
            double cpu = 0d, avgCpu = 0d, alloc = 0d, avgAlloc = 0d;
            double[] cats = new double[categoryCount];
            double[]? catBytes = tracksAlloc ? new double[categoryCount] : null;
            int baseIdx = i * categoryCount;
            for (int cat = 0; cat < categoryCount; cat++)
            {
                cats[cat] = smoothed[baseIdx + cat];
                cpu += smoothed[baseIdx + cat];
                avgCpu += averaged[baseIdx + cat];
                if (tracksAlloc)
                {
                    catBytes![cat] = smoothedBytes![baseIdx + cat];
                    alloc += smoothedBytes[baseIdx + cat];
                    avgAlloc += avgBytes![baseIdx + cat];
                }
            }
            mods.Add(new
            {
                id = i,
                name = modNames[i],
                cpuMs = cpu,
                avgCpuMs = avgCpu,
                categories = cats,
                allocBytes = alloc,
                avgAllocBytes = avgAlloc,
                categoryBytes = catBytes,
            });
        }

        return JsonSerializer.Serialize(new
        {
            worldLoaded = true,
            tracksAllocations = tracksAlloc,
            categories = PerModAttribution.CategoryNames,
            mods,
        }, JsonOpts);
    }

    // ----------------------------------------------------------------------
    // /api/hooks — full per-mod / per-category / per-hook breakdown for
    // the cascading tree view. Heavier payload than /api/mods (one row
    // per installed hook = ~10k entries on a kitchen-sink modlist), so
    // the dashboard only fetches this on demand when the tree is expanded.
    // ----------------------------------------------------------------------
    private static string BuildHooks()
    {
        // Migration step 11 — per-hook cost via HookCpuCollector. The
        // hook descriptor list still comes from PerModAttribution (it's
        // the canonical install-time registration); only the per-hook
        // ms / bytes arrays go through the pipeline adapters.
        var cpuSnap = Data.DataRegistry.Shared
            .Lookup<Data.Collectors.HookCpuSnapshot>(Data.Collectors.HookCpuCollector.StreamName)?
            .CurrentSnapshot() ?? Data.Collectors.HookCpuSnapshot.Empty;
        var allocSnap = Data.DataRegistry.Shared
            .Lookup<Data.Collectors.AllocationSnapshot>(Data.Collectors.AllocationCollector.StreamName)?
            .CurrentSnapshot() ?? Data.Collectors.AllocationSnapshot.Empty;
        if (!cpuSnap.WorldLoaded || cpuSnap.PerHookMs == null)
        {
            return JsonSerializer.Serialize(new { worldLoaded = false, hooks = Array.Empty<object>() }, JsonOpts);
        }

        string[] modNames = HookInterceptor.ProfiledModNames;
        IReadOnlyList<HookDescriptor> hooks = PerModAttribution.Hooks;
        IReadOnlyList<double> hookMs = cpuSnap.PerHookMs;
        IReadOnlyList<double> hookAvgMs = cpuSnap.PerHookAverageMs!;
        IReadOnlyList<double>? hookBytes = allocSnap.PerHookBytes;
        bool tracksAlloc = allocSnap.TracksAllocations && hookBytes != null;

        var hookList = new List<object>(hooks.Count);
        for (int hookId = 0; hookId < hooks.Count; hookId++)
        {
            HookDescriptor d = hooks[hookId];
            double ms = hookId < hookMs.Count ? hookMs[hookId] : 0d;
            double avg = hookId < hookAvgMs.Count ? hookAvgMs[hookId] : 0d;
            // Skip totally inactive hooks to keep the payload compact.
            // The tree view shows only hooks with non-zero current OR average cost.
            if (ms <= 0d && avg <= 0d) continue;
            double bytes = tracksAlloc && hookId < hookBytes!.Count ? hookBytes[hookId] : 0d;
            hookList.Add(new
            {
                modId = d.ModId,
                modName = d.ModId >= 0 && d.ModId < modNames.Length ? modNames[d.ModId] : "mod:" + d.ModId,
                categoryId = d.CategoryId,
                category = d.CategoryId >= 0 && d.CategoryId < PerModAttribution.CategoryCount
                    ? PerModAttribution.CategoryNames[d.CategoryId]
                    : "?",
                hookId,
                display = d.DisplayName,
                cpuMs = ms,
                avgCpuMs = avg,
                allocBytes = bytes,
            });
        }

        return JsonSerializer.Serialize(new
        {
            worldLoaded = true,
            tracksAllocations = tracksAlloc,
            categories = PerModAttribution.CategoryNames,
            hooks = hookList,
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
            ms[i] = history[i].FrameTimeMs;
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
    // /api/segments — open + recent closed.
    // ----------------------------------------------------------------------
    private static string BuildSegments()
    {
        // Migration step 11 — segments via registry adapter. The
        // SegmentAggregator wraps the existing SegmentDetector + Store
        // and exposes their live collections as a single snapshot.
        var snap = Data.DataRegistry.Shared
            .Lookup<Data.Aggregators.SegmentsSnapshot>(Data.Aggregators.SegmentAggregator.StreamName)?
            .CurrentSnapshot() ?? Data.Aggregators.SegmentsSnapshot.Empty;

        var open = new List<object>();
        if (snap.Open != null)
        {
            string[] modNames = HookInterceptor.ProfiledModNames;
            long nowUnix = Time.UnixMsNow();
            foreach (OpenSegment s in snap.Open)
            {
                int bestMod = -1; double bestMs = 0d;
                for (int m = 0; m < s.PerModMs.Length; m++)
                {
                    if (s.PerModMs[m] > bestMs) { bestMs = s.PerModMs[m]; bestMod = m; }
                }
                open.Add(new
                {
                    family = s.Family.ToString(),
                    key = s.Key,
                    name = s.Name,
                    elapsedMs = nowUnix - s.StartUnixMs,
                    ticks = s.Ticks,
                    spikeCount = s.SpikeCount,
                    stallCount = s.StallCount,
                    deathCount = s.DeathCount,
                    topModId = bestMod,
                    topModName = bestMod >= 0 && bestMod < modNames.Length ? modNames[bestMod] : null,
                    topModMsPerTick = s.Ticks > 0 ? bestMs / s.Ticks : 0d,
                });
            }
        }

        var recent = new List<object>();
        if (snap.Recent != null)
        {
            string[] modNames = HookInterceptor.ProfiledModNames;
            foreach (Segment s in snap.Recent)
            {
                var topMods = s.TopMods(3);
                var topList = new List<object>(topMods.Count);
                double totalMs = s.TotalFrameMs > 0 ? s.TotalFrameMs : 1d;
                foreach (var (modId, ms) in topMods)
                {
                    topList.Add(new
                    {
                        id = modId,
                        name = modId >= 0 && modId < modNames.Length ? modNames[modId] : "mod:" + modId,
                        ms,
                        share = ms / totalMs,
                    });
                }
                recent.Add(new
                {
                    family = s.Family.ToString(),
                    key = s.Key,
                    name = s.Name,
                    startUnixMs = s.StartUnixMs,
                    endUnixMs = s.EndUnixMs,
                    durationMs = s.DurationMs,
                    ticks = s.Ticks,
                    avgFrameMs = s.AvgFrameMs,
                    spikeCount = s.SpikeCount,
                    stallCount = s.StallCount,
                    deathCount = s.DeathCount,
                    bossKillCount = s.BossKillCount,
                    promoted = s.Promoted,
                    promotionReason = s.PromotionReason,
                    topMods = topList,
                });
            }
        }

        return JsonSerializer.Serialize(new
        {
            worldLoaded = snap.WorldLoaded,
            open,
            recent,
        }, JsonOpts);
    }

    // ----------------------------------------------------------------------
    // /api/spikes — recent spike windows with top contributors.
    // ----------------------------------------------------------------------
    private static string BuildSpikes()
    {
        // Migration step 11 — spikes via registry.
        var snap = Data.DataRegistry.Shared
            .Lookup<Data.Stats.SpikesSnapshot>(Data.Stats.SpikesStat.StreamName)?
            .CurrentSnapshot() ?? Data.Stats.SpikesSnapshot.Empty;
        if (!snap.WorldLoaded || snap.Windows == null)
        {
            return JsonSerializer.Serialize(new { worldLoaded = false, spikes = Array.Empty<object>() }, JsonOpts);
        }

        int categoryCount = PerModAttribution.CategoryCount;
        string[] modNames = HookInterceptor.ProfiledModNames;

        var spikes = new List<object>();
        foreach (var w in snap.Windows)
        {
            var contribs = TopContributors(w, modNames, categoryCount, take: 5);
            spikes.Add(new
            {
                startTick = w.StartTick,
                endTick = w.EndTick,
                worstTick = w.WorstTick,
                worstFrameMs = w.WorstFrameMs,
                baselineMs = w.BaselineMs,
                madMs = w.MadMs,
                warming = w.Warming,
                contributors = contribs,
            });
        }

        return JsonSerializer.Serialize(new
        {
            worldLoaded = true,
            spikes,
        }, JsonOpts);
    }

    private static List<object> TopContributors(SpikeWindow w, string[] modNames, int categoryCount, int take)
    {
        if (w.PerModCatMs == null || w.PerModCatMs.Length == 0) return new List<object>();
        int modCount = w.PerModCatMs.Length / categoryCount;
        var pairs = new List<(int modId, double ms)>(modCount);
        for (int m = 0; m < modCount; m++)
        {
            double sum = 0d;
            int baseIdx = m * categoryCount;
            for (int cat = 0; cat < categoryCount; cat++) sum += w.PerModCatMs[baseIdx + cat];
            if (sum > 0d) pairs.Add((m, sum));
        }
        pairs.Sort((a, b) => b.ms.CompareTo(a.ms));
        if (pairs.Count > take) pairs.RemoveRange(take, pairs.Count - take);
        var list = new List<object>(pairs.Count);
        foreach (var (modId, ms) in pairs)
        {
            list.Add(new
            {
                modId,
                name = modId >= 0 && modId < modNames.Length ? modNames[modId] : "mod:" + modId,
                ms,
            });
        }
        return list;
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

    // ----------------------------------------------------------------------
    // /api/stalls — recent stall events. Sustained main-thread freezes,
    // distinct from spikes (which are short outlier ticks).
    // ----------------------------------------------------------------------
    private static string BuildStalls()
    {
        // Migration step 11 — stalls via registry.
        var snap = Data.DataRegistry.Shared
            .Lookup<Data.Stats.StallsSnapshot>(Data.Stats.StallsStat.StreamName)?
            .CurrentSnapshot() ?? Data.Stats.StallsSnapshot.Empty;
        if (!snap.WorldLoaded || snap.Events == null)
        {
            return JsonSerializer.Serialize(new { worldLoaded = false, stalls = Array.Empty<object>() }, JsonOpts);
        }

        var stalls = new List<object>();
        foreach (var s in snap.Events)
        {
            stalls.Add(new
            {
                startTick = s.StartTickIndex,
                endTick = s.EndTickIndex,
                startUnixMs = s.StartTimestampUnixMs,
                durationMs = s.TickPeriodMs,
                baselineMs = s.BaselineMs,
                excessMs = s.ExcessOverBaselineMs,
                cause = s.Cause.ToString(),
                severity = s.Severity.ToString(),
                warming = s.Warming,
                gcPauseMs = s.GcPauseDurationMs,
                gen0 = s.Gen0Collections,
                gen1 = s.Gen1Collections,
                gen2 = s.Gen2Collections,
            });
        }

        return JsonSerializer.Serialize(new
        {
            worldLoaded = true,
            stalls,
        }, JsonOpts);
    }

    // ----------------------------------------------------------------------
    // /api/insights — live insight records from InsightsEngine.
    // ----------------------------------------------------------------------
    private static string BuildInsights()
    {
        // Migration step 11 — insights via registry.
        var snap = Data.DataRegistry.Shared
            .Lookup<Data.Stats.InsightsSnapshot>(Data.Stats.InsightsStat.StreamName)?
            .CurrentSnapshot() ?? Data.Stats.InsightsSnapshot.Empty;
        if (!snap.WorldLoaded || snap.Live == null)
        {
            return JsonSerializer.Serialize(new { worldLoaded = false, records = Array.Empty<object>() }, JsonOpts);
        }

        string[] modNames = HookInterceptor.ProfiledModNames;
        var records = new List<object>();
        foreach (var rec in snap.Live)
        {
            string subjectName = rec.Subject.ModId >= 0 && rec.Subject.ModId < modNames.Length
                ? modNames[rec.Subject.ModId]
                : null!;
            records.Add(new
            {
                pattern = rec.Pattern.ToString(),
                confidence = rec.Confidence.ToString(),
                scope = rec.Scope.ToString(),
                audience = rec.Audience.ToString(),
                shortText = InsightRenderer.Render(rec, Audience.Player, Density.Short),
                mediumText = InsightRenderer.Render(rec, Audience.Player, Density.Medium),
                subjectModId = rec.Subject.ModId,
                subjectModName = subjectName,
                observedMs = rec.Magnitude.ObservedMs,
                baselineMs = rec.Magnitude.BaselineMs,
                ratioOrDelta = rec.Magnitude.RatioOrDelta,
                firstSeenTick = rec.FirstSeenTick,
                lastSeenTick = rec.LastSeenTick,
                confirmationCount = rec.ConfirmationCount,
            });
        }

        return JsonSerializer.Serialize(new
        {
            worldLoaded = true,
            records,
        }, JsonOpts);
    }

    // ----------------------------------------------------------------------
    // /api/self — profiler self-health detail.
    // ----------------------------------------------------------------------
    private static string BuildSelf()
    {
        // Migration step 11 — self-health via registry.
        var snap = Data.DataRegistry.Shared
            .Lookup<Data.Stats.SelfHealthSnapshot>(Data.Stats.SelfHealthStat.StreamName)?
            .CurrentSnapshot() ?? default;

        return JsonSerializer.Serialize(new
        {
            installed = snap.Installed,
            installDeltaBytes = snap.InstallDeltaBytes,
            installDeltaMb = snap.InstallDeltaBytes / (1024d * 1024d),
            bytesPerHook = snap.BytesPerHook,
            bytesPerHookKb = snap.BytesPerHook / 1024d,
            installedHookCount = snap.InstalledHookCount,
            processWorkingSetMb = snap.ProcessWorkingSetBytes / (1024d * 1024d),
            processManagedHeapMb = snap.ProcessManagedHeapBytes / (1024d * 1024d),
            managedFractionOfWorkingSet = snap.ManagedFractionOfWorkingSet,
            severity = snap.Severity.ToString(),
            backend = snap.BackendMode.ToString(),
        }, JsonOpts);
    }
}
