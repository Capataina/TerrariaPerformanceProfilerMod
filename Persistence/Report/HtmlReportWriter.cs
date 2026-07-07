#nullable enable

using System;
using System.Globalization;
using System.Text;

namespace PerformanceProfiler.Persistence.Report;

/// <summary>
/// Renders a <see cref="SessionReportData"/> into one SELF-CONTAINED HTML
/// document (atlas S17): inline CSS, inline SVG, zero JavaScript, zero
/// network references — double-click it years later with the network off and
/// it renders identically (a test pins the no-external-refs property).
/// Server-side static render chosen over the planned JSON-blob-plus-JS
/// renderer: strictly more self-contained and nothing in the report is
/// interactive.
///
/// <para>
/// Pure: string in, string out. The colour tokens mirror the dashboard's
/// OKLCH ramp (source of truth: Web/Assets/Css/Css.Palette.cs); they are
/// duplicated here as a compact block rather than refactoring the asset
/// bundler mid-batch — a recorded trade, revisit if the palette churns.
/// The honesty contract travels into the artefact: every stat is badged,
/// the stall section names the pause exclusion, the footer carries the
/// descriptive-never-normative note.
/// </para>
/// </summary>
public static class HtmlReportWriter
{
    public static string Render(SessionReportData d)
    {
        var sb = new StringBuilder(64 * 1024);
        CultureInfo inv = CultureInfo.InvariantCulture;

        string F(double v, string fmt = "0.0") => v.ToString(fmt, inv);
        string Dur(long ms)
        {
            long s = ms / 1000;
            return s >= 3600 ? $"{s / 3600}h {(s % 3600) / 60}m" : s >= 60 ? $"{s / 60}m {s % 60}s" : $"{s}s";
        }
        string Esc(string t) => t.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

        double fps = d.AvgFrameMs > 0 ? 1000d / d.AvgFrameMs : 0;
        string fpsClass = fps >= 55 ? "good" : fps >= 30 ? "warn" : "bad";

        sb.Append("<!doctype html><html><head><meta charset=\"utf-8\">");
        sb.Append("<title>Performance Profiler — session ").Append(d.StartedUtc.ToString("yyyy-MM-dd HH:mm", inv)).Append("</title>");
        sb.Append("<style>");
        // Compact token block mirroring Css.Palette's OKLCH ramp.
        sb.Append(":root{--bg:oklch(0.16 0 0);--panel:oklch(0.2 0 0);--border:oklch(0.32 0 0);--text:oklch(0.92 0 0);--dim:oklch(0.62 0 0);--muted:oklch(0.48 0 0);--good:oklch(0.78 0.14 155);--amber:oklch(0.8 0.14 85);--orange:oklch(0.74 0.16 55);--danger:oklch(0.68 0.19 25);--accent:oklch(0.85 0 0);}");
        sb.Append("body{background:var(--bg);color:var(--text);font:14px/1.5 ui-monospace,Menlo,Consolas,monospace;margin:0;padding:2rem;max-width:1100px;margin-inline:auto;}");
        sb.Append("h1{font-size:1.3rem;margin:0 0 .2rem}h2{font-size:.85rem;letter-spacing:.08em;text-transform:uppercase;color:var(--dim);margin:2rem 0 .6rem;border-bottom:1px solid var(--border);padding-bottom:.3rem}");
        sb.Append(".sub{color:var(--dim);font-size:.85rem}.badge{display:inline-block;border:1px solid var(--border);border-radius:4px;padding:0 .4em;font-size:.7rem;color:var(--dim);margin-left:.5em;text-transform:lowercase}");
        sb.Append(".kpis{display:grid;grid-template-columns:repeat(auto-fit,minmax(150px,1fr));gap:.8rem;margin-top:1rem}.kpi{background:var(--panel);border:1px solid var(--border);border-radius:8px;padding:.8rem}.kpi .v{font-size:1.6rem;font-weight:700}.kpi .k{font-size:.7rem;color:var(--dim);text-transform:uppercase;letter-spacing:.06em}");
        sb.Append(".good{color:var(--good)}.warn{color:var(--amber)}.orange{color:var(--orange)}.bad{color:var(--danger)}.dim{color:var(--dim)}");
        sb.Append("table{border-collapse:collapse;width:100%;font-size:.85rem}th{text-align:left;color:var(--dim);font-weight:400;font-size:.7rem;text-transform:uppercase;letter-spacing:.06em;padding:.3rem .6rem;border-bottom:1px solid var(--border)}td{padding:.3rem .6rem;border-bottom:1px solid oklch(0.24 0 0)}td.r,th.r{text-align:right}");
        sb.Append(".bar{height:8px;border-radius:4px;background:var(--accent);display:inline-block;vertical-align:middle}");
        sb.Append(".ribbon{display:flex;gap:2px;align-items:flex-end;height:64px;background:var(--panel);border:1px solid var(--border);border-radius:8px;padding:.6rem}.ribbon div{flex:1;border-radius:2px 2px 0 0;min-width:3px}");
        sb.Append(".legend{display:flex;gap:1rem;font-size:.75rem;color:var(--dim);margin-top:.4rem}.sw{display:inline-block;width:.7em;height:.7em;border-radius:2px;vertical-align:-1px;margin-right:.3em}");
        sb.Append(".foot{margin-top:3rem;padding-top:1rem;border-top:1px solid var(--border);color:var(--muted);font-size:.75rem}");
        sb.Append("</style></head><body>");

        // ---- header --------------------------------------------------------
        sb.Append("<h1>Performance Profiler — session report</h1>");
        sb.Append("<div class=\"sub\">").Append(d.StartedUtc.ToString("yyyy-MM-dd HH:mm", inv)).Append(" UTC · ")
          .Append(Dur(d.DurationMs)).Append(" · ").Append(d.TicksObserved.ToString("N0", inv)).Append(" ticks · ")
          .Append(d.ModVersions.Count).Append(" mods · modlist ").Append(Esc(d.ModlistFingerprint))
          .Append(" · profiler v").Append(Esc(d.ProfilerVersion)).Append("</div>");

        // ---- KPI strip ------------------------------------------------------
        sb.Append("<div class=\"kpis\">");
        void Kpi(string k, string v, string cls = "", string badge = "session")
            => sb.Append("<div class=\"kpi\"><div class=\"v ").Append(cls).Append("\">").Append(v)
                 .Append("</div><div class=\"k\">").Append(k).Append("<span class=\"badge\">").Append(badge).Append("</span></div></div>");
        Kpi("avg fps", F(fps, "0"), fpsClass);
        Kpi("avg frame", F(d.AvgFrameMs) + " ms");
        Kpi("median frame", F(d.MedianFrameMs) + " ms");
        Kpi("worst frame", F(d.MaxFrameMs) + " ms", d.MaxFrameMs > 100 ? "bad" : d.MaxFrameMs > 50 ? "orange" : "");
        Kpi("spikes", d.SpikeCount.ToString(inv), d.SpikeCount > 20 ? "orange" : "");
        Kpi("stalls", d.StallCount.ToString(inv), d.StallCount > 5 ? "orange" : "");
        if (d.PausedMs > 0) Kpi("paused (excl.)", Dur((long)d.PausedMs), "dim");
        sb.Append("</div>");

        // ---- minute ribbon --------------------------------------------------
        if (d.Minutes.Count > 0)
        {
            sb.Append("<h2>session timeline — per-minute frame health</h2><div class=\"ribbon\">");
            double worstCap = 0d;
            foreach (var m in d.Minutes) if (m.AvgMs > worstCap) worstCap = m.AvgMs;
            if (worstCap <= 0d) worstCap = 16.7;
            foreach (var m in d.Minutes)
            {
                string c = m.AvgMs <= 17 ? "var(--good)" : m.AvgMs <= 25 ? "var(--amber)" : m.AvgMs <= 40 ? "var(--orange)" : "var(--danger)";
                int h = (int)Math.Max(6, Math.Min(48, m.AvgMs / Math.Max(worstCap, 40d) * 48));
                sb.Append("<div style=\"height:").Append(h).Append("px;background:").Append(c)
                  .Append("\" title=\"min ").Append(m.Minute).Append(": avg ").Append(F(m.AvgMs))
                  .Append(" ms, worst ").Append(F(m.WorstMs)).Append(" ms\"></div>");
            }
            sb.Append("</div><div class=\"legend\"><span><span class=\"sw\" style=\"background:var(--good)\"></span>≤17 ms</span><span><span class=\"sw\" style=\"background:var(--amber)\"></span>17–25</span><span><span class=\"sw\" style=\"background:var(--orange)\"></span>25–40</span><span><span class=\"sw\" style=\"background:var(--danger)\"></span>&gt;40 ms</span></div>");
        }

        // ---- per-mod cost ---------------------------------------------------
        if (d.PerMod.Count > 0)
        {
            sb.Append("<h2>per-mod cost <span class=\"badge\">session avg</span></h2><table><tr><th>mod</th><th class=\"r\">avg ms/t</th><th class=\"r\">peak ms</th><th></th></tr>");
            double top = d.PerMod[0].AvgMs > 0 ? d.PerMod[0].AvgMs : 1;
            int shown = Math.Min(d.PerMod.Count, 20);
            for (int i = 0; i < shown; i++)
            {
                var pm = d.PerMod[i];
                int w = (int)Math.Max(2, pm.AvgMs / top * 220);
                sb.Append("<tr><td>").Append(Esc(pm.Name)).Append("</td><td class=\"r\">").Append(F(pm.AvgMs, "0.000"))
                  .Append("</td><td class=\"r\">").Append(F(pm.PeakMs)).Append("</td><td><span class=\"bar\" style=\"width:")
                  .Append(w).Append("px\"></span></td></tr>");
            }
            if (d.PerMod.Count > shown)
            {
                sb.Append("<tr><td class=\"dim\">+").Append(d.PerMod.Count - shown).Append(" more</td><td></td><td></td><td></td></tr>");
            }
            sb.Append("</table>");
        }

        // ---- moments --------------------------------------------------------
        if (d.Stalls.Count > 0 || d.Spikes.Count > 0)
        {
            sb.Append("<h2>rough moments <span class=\"badge\">worst first</span></h2>");
            if (d.Stalls.Count > 0)
            {
                sb.Append("<table><tr><th>stall</th><th class=\"r\">duration</th><th>cause</th><th>severity</th></tr>");
                foreach (var s in d.Stalls)
                {
                    sb.Append("<tr><td class=\"dim\">").Append(DateTimeOffset.FromUnixTimeMilliseconds(s.UnixMs).ToString("HH:mm:ss", inv))
                      .Append("</td><td class=\"r\">").Append(F(s.DurationMs, "0")).Append(" ms</td><td>").Append(Esc(s.Cause))
                      .Append("</td><td>").Append(Esc(s.Severity)).Append("</td></tr>");
                }
                sb.Append("</table>");
                if (d.PauseCount > 0)
                {
                    sb.Append("<div class=\"sub\">").Append(d.PauseCount).Append(" pause(s) totalling ")
                      .Append(Dur((long)d.PausedMs)).Append(" (alt-tab / world-load) excluded from the stall list.</div>");
                }
            }
            if (d.Spikes.Count > 0)
            {
                sb.Append("<table style=\"margin-top:1rem\"><tr><th>spike (tick)</th><th class=\"r\">worst frame</th><th class=\"r\">baseline</th></tr>");
                foreach (var w in d.Spikes)
                {
                    sb.Append("<tr><td class=\"dim\">#").Append(w.WorstTick).Append("</td><td class=\"r\">")
                      .Append(F(w.WorstFrameMs)).Append(" ms</td><td class=\"r\">").Append(F(w.BaselineMs)).Append(" ms</td></tr>");
                }
                sb.Append("</table>");
            }
        }

        // ---- segments -------------------------------------------------------
        if (d.Segments.Count > 0)
        {
            sb.Append("<h2>encounters &amp; segments</h2><table><tr><th>segment</th><th class=\"r\">duration</th><th class=\"r\">avg frame</th><th class=\"r\">spikes</th></tr>");
            foreach (var seg in d.Segments)
            {
                sb.Append("<tr><td>").Append(Esc(seg.Name)).Append("</td><td class=\"r\">").Append(Dur(seg.DurationMs))
                  .Append("</td><td class=\"r\">").Append(F(seg.AvgFrameMs)).Append(" ms</td><td class=\"r\">")
                  .Append(seg.SpikeCount).Append("</td></tr>");
            }
            sb.Append("</table>");
        }

        // ---- insights -------------------------------------------------------
        if (d.Insights.Count > 0)
        {
            sb.Append("<h2>insights <span class=\"badge\">as ranked at session end</span></h2><table><tr><th>finding</th><th>confidence</th><th>data</th></tr>");
            foreach (var ins in d.Insights)
            {
                string scope = ins.Scope == "LifetimeData" ? "lifetime data" : "this session";
                sb.Append("<tr><td>").Append(Esc(ins.Text)).Append("</td><td>").Append(Esc(ins.Confidence))
                  .Append("</td><td class=\"dim\">").Append(scope).Append("</td></tr>");
            }
            sb.Append("</table>");
        }

        // ---- roster ---------------------------------------------------------
        if (d.ModVersions.Count > 0)
        {
            sb.Append("<h2>modlist</h2><div class=\"sub\">").Append(Esc(string.Join(" · ", d.ModVersions))).Append("</div>");
        }

        sb.Append("<div class=\"foot\">Generated ").Append(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm", inv))
          .Append(" UTC by Performance Profiler v").Append(Esc(d.ProfilerVersion))
          .Append(". Every number is descriptive, never normative — the profiler reports what it measured; it does not recommend removing anything.</div>");
        sb.Append("</body></html>");
        return sb.ToString();
    }
}
