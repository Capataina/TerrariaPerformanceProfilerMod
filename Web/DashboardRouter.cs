#nullable enable

using System;
using System.Globalization;
using System.Text;
using Terraria.ModLoader;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Segments;

namespace PerformanceProfiler.Web;

/// <summary>
/// Routes incoming HTTP requests to either the dashboard HTML page or one
/// of the JSON state endpoints. Strict allowlist — anything not matched
/// returns 404 rather than reflecting input back.
///
/// <para>
/// <b>Prototype scope.</b> Only the surface needed to prove the seamless
/// loop end-to-end:
/// </para>
/// <list type="bullet">
///   <item><c>GET /</c> → the inlined dashboard HTML page</item>
///   <item><c>GET /api/now</c> → JSON snapshot of the current tick</item>
///   <item><c>GET /favicon.ico</c> → empty 200 so the browser stops 404-ing</item>
/// </list>
///
/// <para>
/// Once viability is confirmed we expand the API surface (/api/segments,
/// /api/mods, /api/spikes, ...) and move the HTML out into an Assets/ file.
/// </para>
/// </summary>
internal static class DashboardRouter
{
    public static HttpResponse Route(HttpRequest req)
    {
        if (req.Method != "GET") return HttpResponse.PlainText(405, "Method Not Allowed");
        return req.Path switch
        {
            "/"             => HttpResponse.Html(DashboardHtml.Page),
            "/api/now"      => HttpResponse.Json(BuildNowJson()),
            "/favicon.ico"  => new HttpResponse(200, "image/x-icon", Array.Empty<byte>()),
            _               => HttpResponse.NotFound,
        };
    }

    /// <summary>
    /// Hand-rolled JSON for the live tick. Avoiding
    /// <c>System.Text.Json</c> here keeps the prototype dependency-free
    /// and makes the response shape unmistakable when reading the source.
    /// We'll swap in <c>System.Text.Json</c> once the endpoint surface
    /// grows beyond a handful of fields.
    /// </summary>
    private static string BuildNowJson()
    {
        ProfilerSystem? sys = ModContent.GetInstance<ProfilerSystem>();
        MetricCollector? collector = sys?.Collector;
        SegmentDetector? segments = sys?.Segments;

        bool worldLoaded = collector != null && collector.History.Count > 0;

        var sb = new StringBuilder(256);
        sb.Append('{');
        AppendField(sb, "worldLoaded", worldLoaded);
        sb.Append(',');
        AppendField(sb, "unixMs", Time.UnixMsNow());

        if (worldLoaded)
        {
            var latest = collector!.History[collector.History.Count - 1];
            sb.Append(',');
            AppendField(sb, "tickIndex", latest.TickIndex);
            sb.Append(',');
            AppendField(sb, "frameMs", latest.FrameTimeMs);
            sb.Append(',');
            AppendField(sb, "npcCount", latest.NpcCount);
            sb.Append(',');
            AppendField(sb, "projectileCount", latest.ProjectileCount);
            sb.Append(',');
            AppendField(sb, "dustCount", latest.DustCount);
            sb.Append(',');
            AppendField(sb, "openSegmentCount", segments?.OpenSegments.Count ?? 0);

            // Self-health snapshot.
            ProfilerSelfHealth h = collector.SelfHealth;
            sb.Append(',');
            AppendField(sb, "installDeltaMb", h.InstallDeltaBytes / (1024d * 1024d));
            sb.Append(',');
            AppendField(sb, "bytesPerHookKb", h.BytesPerHook / 1024d);
            sb.Append(',');
            AppendField(sb, "hookCount", h.InstalledHookCount);
            sb.Append(',');
            AppendField(sb, "severity", h.Severity.ToString());
        }

        sb.Append('}');
        return sb.ToString();
    }

    private static void AppendField(StringBuilder sb, string key, bool value)
    {
        sb.Append('"').Append(key).Append("\":").Append(value ? "true" : "false");
    }
    private static void AppendField(StringBuilder sb, string key, long value)
    {
        sb.Append('"').Append(key).Append("\":").Append(value.ToString(CultureInfo.InvariantCulture));
    }
    private static void AppendField(StringBuilder sb, string key, int value)
    {
        sb.Append('"').Append(key).Append("\":").Append(value.ToString(CultureInfo.InvariantCulture));
    }
    private static void AppendField(StringBuilder sb, string key, double value)
    {
        sb.Append('"').Append(key).Append("\":").Append(value.ToString("F3", CultureInfo.InvariantCulture));
    }
    private static void AppendField(StringBuilder sb, string key, string value)
    {
        sb.Append('"').Append(key).Append("\":\"").Append(EscapeJson(value)).Append('"');
    }

    private static string EscapeJson(string s)
    {
        // Cheap escape — none of our values contain anything outside the
        // basic-ASCII printable range. If a future field carries arbitrary
        // text (mod display name, biome name) we'll swap this for a proper
        // serializer.
        var sb = new StringBuilder(s.Length + 4);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            switch (c)
            {
                case '"':  sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n");  break;
                case '\r': sb.Append("\\r");  break;
                case '\t': sb.Append("\\t");  break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }
}
