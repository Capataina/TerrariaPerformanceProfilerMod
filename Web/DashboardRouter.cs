#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;
using PerformanceProfiler.Data.Detectors;
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
internal static partial class DashboardRouter
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
            "/api/memory"        => HttpResponse.Json(BuildMemory()),
            "/api/heatmap"       => HttpResponse.Json(BuildHeatmap()),
            "/api/events"        => HttpResponse.Json(BuildEvents()),

            "/api/segment-lifetime"         => HttpResponse.Json(BuildSegmentLifetime()),
            "/api/segment-mod-attribution"  => HttpResponse.Json(BuildSegmentModAttribution()),
            "/api/transitions"              => HttpResponse.Json(BuildTransitions()),
            "/api/activity-strip"           => HttpResponse.Json(BuildActivityStrip()),
            "/api/attendance"               => HttpResponse.Json(BuildAttendance()),
            "/api/deaths"                   => HttpResponse.Json(BuildDeaths()),
            "/api/chronicle"                => HttpResponse.Json(BuildChronicle()),

            "/api/lag-clusters"             => HttpResponse.Json(BuildLagClusters()),
            "/api/gc"                       => HttpResponse.Json(BuildGcPressure()),
            "/api/lag-density"              => HttpResponse.Json(BuildSegmentLagDensity()),
            "/api/gc-causality"             => HttpResponse.Json(BuildAllocCausality()),
            "/api/lag-rhythm"               => HttpResponse.Json(BuildLagRhythm()),

            "/api/mod-observatory"          => HttpResponse.Json(BuildModObservatory()),
            "/api/dormant"                  => HttpResponse.Json(BuildDormantSurface()),
            "/api/cross-cutting"            => HttpResponse.Json(BuildCrossCutting()),
            "/api/engagement-cost"          => HttpResponse.Json(BuildEngagementCost()),
            "/api/mod-interaction"          => HttpResponse.Json(BuildModInteraction()),

            _                    => HttpResponse.NotFound,
        };
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

}
