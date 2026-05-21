#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;
using Terraria.ModLoader;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Insights;
using PerformanceProfiler.Profiling.Segments;

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

    public static HttpResponse Route(HttpRequest req)
    {
        if (req.Method != "GET") return HttpResponse.PlainText(405, "Method Not Allowed");
        return req.Path switch
        {
            "/"                  => HttpResponse.Html(DashboardAssets.IndexHtml),
            "/dashboard.css"     => new HttpResponse(200, "text/css; charset=utf-8",
                                        System.Text.Encoding.UTF8.GetBytes(DashboardAssets.Css)),
            "/dashboard.js"      => new HttpResponse(200, "application/javascript; charset=utf-8",
                                        System.Text.Encoding.UTF8.GetBytes(DashboardAssets.Js)),
            "/favicon.ico"       => new HttpResponse(200, "image/x-icon", Array.Empty<byte>()),

            "/api/now"           => HttpResponse.Json(BuildNow()),
            "/api/mods"          => HttpResponse.Json(BuildMods()),
            "/api/frames"        => HttpResponse.Json(BuildFrames()),
            "/api/segments"      => HttpResponse.Json(BuildSegments()),
            "/api/spikes"        => HttpResponse.Json(BuildSpikes()),
            "/api/insights"      => HttpResponse.Json(BuildInsights()),
            "/api/self"          => HttpResponse.Json(BuildSelf()),

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
            installDeltaMb = h.InstallDeltaBytes / (1024d * 1024d),
            processWorkingSetMb = h.ProcessWorkingSetBytes / (1024d * 1024d),
            bytesPerHookKb = h.BytesPerHook / 1024d,
            hookCount = h.InstalledHookCount,
            severity = h.Severity.ToString(),
            backend = HookBackend.Mode.ToString(),
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
    // /api/mods — per-mod ranking with per-category breakdown.
    // ----------------------------------------------------------------------
    private static string BuildMods()
    {
        MetricCollector? c = ModContent.GetInstance<ProfilerSystem>()?.Collector;
        if (c == null || c.History.Count == 0)
        {
            return JsonSerializer.Serialize(new { worldLoaded = false, mods = Array.Empty<object>() }, JsonOpts);
        }

        int categoryCount = PerModAttribution.CategoryCount;
        string[] modNames = HookInterceptor.ProfiledModNames;
        IReadOnlyList<double> smoothed = c.PerModCategoryMs;
        IReadOnlyList<double> averaged = c.PerModCategoryAverageMs;

        var mods = new List<object>(modNames.Length);
        for (int i = 0; i < modNames.Length; i++)
        {
            double cpu = 0d, avgCpu = 0d;
            double[] cats = new double[categoryCount];
            int baseIdx = i * categoryCount;
            for (int cat = 0; cat < categoryCount; cat++)
            {
                cats[cat] = smoothed[baseIdx + cat];
                cpu += smoothed[baseIdx + cat];
                avgCpu += averaged[baseIdx + cat];
            }
            mods.Add(new
            {
                id = i,
                name = modNames[i],
                cpuMs = cpu,
                avgCpuMs = avgCpu,
                categories = cats,
            });
        }

        return JsonSerializer.Serialize(new
        {
            worldLoaded = true,
            categories = PerModAttribution.CategoryNames,
            mods,
        }, JsonOpts);
    }

    // ----------------------------------------------------------------------
    // /api/frames — last 30 s of raw frame times. The hero chart data.
    // ----------------------------------------------------------------------
    private static string BuildFrames()
    {
        MetricCollector? c = ModContent.GetInstance<ProfilerSystem>()?.Collector;
        if (c == null || c.History.Count == 0)
        {
            return JsonSerializer.Serialize(new { worldLoaded = false, frames = Array.Empty<object>() }, JsonOpts);
        }

        int n = c.History.Count;
        var ticks = new long[n];
        var ms = new double[n];
        var gc = new double[n];
        for (int i = 0; i < n; i++)
        {
            ticks[i] = c.History[i].TickIndex;
            ms[i] = c.History[i].FrameTimeMs;
            gc[i] = c.History[i].GcTimeMs;
        }

        return JsonSerializer.Serialize(new
        {
            worldLoaded = true,
            ticks,
            frameMs = ms,
            gcMs = gc,
        }, JsonOpts);
    }

    // ----------------------------------------------------------------------
    // /api/segments — open + recent closed.
    // ----------------------------------------------------------------------
    private static string BuildSegments()
    {
        ProfilerSystem? sys = ModContent.GetInstance<ProfilerSystem>();
        SegmentDetector? det = sys?.Segments;
        SegmentStore? store = sys?.SegmentStore;

        var open = new List<object>();
        if (det != null)
        {
            string[] modNames = HookInterceptor.ProfiledModNames;
            long nowUnix = Time.UnixMsNow();
            foreach (OpenSegment s in det.OpenSegments)
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
        if (store != null)
        {
            string[] modNames = HookInterceptor.ProfiledModNames;
            foreach (Segment s in store.Recent)
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
            worldLoaded = sys?.Collector != null,
            open,
            recent,
        }, JsonOpts);
    }

    // ----------------------------------------------------------------------
    // /api/spikes — recent spike windows with top contributors.
    // ----------------------------------------------------------------------
    private static string BuildSpikes()
    {
        MetricCollector? c = ModContent.GetInstance<ProfilerSystem>()?.Collector;
        if (c == null)
        {
            return JsonSerializer.Serialize(new { worldLoaded = false, spikes = Array.Empty<object>() }, JsonOpts);
        }

        int categoryCount = PerModAttribution.CategoryCount;
        string[] modNames = HookInterceptor.ProfiledModNames;

        var spikes = new List<object>();
        foreach (var w in c.Spikes)
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
    // /api/insights — live insight records from InsightsEngine.
    // ----------------------------------------------------------------------
    private static string BuildInsights()
    {
        InsightsEngine? eng = InsightsEngine.Shared;
        if (eng == null)
        {
            return JsonSerializer.Serialize(new { worldLoaded = false, records = Array.Empty<object>() }, JsonOpts);
        }

        string[] modNames = HookInterceptor.ProfiledModNames;
        var records = new List<object>();
        foreach (var rec in eng.Store.AllLive())
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
        MetricCollector? c = ModContent.GetInstance<ProfilerSystem>()?.Collector;
        ProfilerSelfHealth h = c?.SelfHealth ?? ProfilerSystem.SelfHealth;

        return JsonSerializer.Serialize(new
        {
            installed = h.IsInstalled,
            installDeltaBytes = h.InstallDeltaBytes,
            installDeltaMb = h.InstallDeltaBytes / (1024d * 1024d),
            bytesPerHook = h.BytesPerHook,
            bytesPerHookKb = h.BytesPerHook / 1024d,
            installedHookCount = h.InstalledHookCount,
            processWorkingSetMb = h.ProcessWorkingSetBytes / (1024d * 1024d),
            processManagedHeapMb = h.ProcessManagedHeapBytes / (1024d * 1024d),
            managedFractionOfWorkingSet = h.ManagedFractionOfWorkingSet,
            severity = h.Severity.ToString(),
            backend = HookBackend.Mode.ToString(),
        }, JsonOpts);
    }
}
