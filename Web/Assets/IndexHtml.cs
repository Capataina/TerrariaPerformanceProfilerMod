#nullable enable

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Data.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Profiling.Persistence;
using PerformanceProfiler.Profiling.Persistence.Records;
namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    /// <summary>The dashboard SPA. Single page; JS handles tab routing + polling.</summary>
    public const string IndexHtml = @"<!DOCTYPE html>
<html lang=""en"">
<head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<title>Performance Profiler</title>
<link rel=""preconnect"" href=""https://fonts.googleapis.com"">
<link rel=""preconnect"" href=""https://fonts.gstatic.com"" crossorigin>
<link href=""https://fonts.googleapis.com/css2?family=JetBrains+Mono:wght@400;500;700&family=Inter:wght@400;500;600;700&display=swap"" rel=""stylesheet"">
<link rel=""stylesheet"" href=""/dashboard.css"">
</head>
<body>

<div class=""app"">

  <!-- ===== Top bar: identity + persistent live status =================== -->
  <header class=""topbar"">
    <div class=""brand"">
      <span class=""brand-mark""></span>
      <span class=""brand-name"">Performance Profiler</span>
      <span class=""brand-version"" id=""brand-version"">v0.9</span>
    </div>
    <div class=""live"">
      <span class=""live-dot"" id=""live-dot""></span>
      <span class=""live-text"" id=""live-text"">connecting…</span>
    </div>
    <div class=""topstats"" id=""topstats"">
      <div class=""topstat""><span class=""k"">tick</span><span class=""v"" id=""ts-tick"">—</span></div>
      <div class=""topstat"" data-explain=""tick-frame""><span class=""k"">frame</span><span class=""v"" id=""ts-frame"">—</span></div>
      <div class=""topstat"" data-explain=""tick-avg""><span class=""k"">avg 30s</span><span class=""v"" id=""ts-avg"">—</span></div>
      <div class=""topstat"" data-explain=""tick-gc""><span class=""k"">gc</span><span class=""v"" id=""ts-gc"">—</span></div>
      <div class=""topstat"" data-explain=""backend""><span class=""k"">backend</span><span class=""v"" id=""ts-backend"">—</span></div>
    </div>
  </header>

  <!-- ===== Tab strip ====================================================== -->
  <nav class=""tabs"" id=""tabs"">
    <button class=""tab active"" data-tab=""summary""><span class=""ki"">1</span>Summary</button>
    <button class=""tab"" data-tab=""timeline""><span class=""ki"">2</span>Timeline</button>
    <button class=""tab"" data-tab=""lag""><span class=""ki"">3</span>Lag</button>
    <button class=""tab"" data-tab=""insights""><span class=""ki"">4</span>Insights</button>
    <button class=""tab"" data-tab=""self""><span class=""ki"">5</span>Self</button>
  </nav>

  <!-- ===== Disconnect / no-world overlays =============================== -->
  <div class=""overlay-state hidden"" id=""disconnected"">
    <div class=""overlay-inner"">
      <h2>lost connection</h2>
      <p>the mod's dashboard server stopped responding — Terraria probably exited.</p>
      <p class=""hint"">close this tab when you're done. it'll auto-reconnect if you re-launch tModLoader.</p>
    </div>
  </div>

  <div class=""overlay-state hidden"" id=""empty"">
    <div class=""overlay-inner"">
      <h2>no world loaded</h2>
      <p>open a save in tModLoader and walk around — the dashboard will populate automatically.</p>
      <p class=""hint"">tip: in single-player Terraria pauses when the window loses focus.
        for a live dashboard while alt-tabbed, host via Multiplayer → Host &amp; Play on the same world.</p>
    </div>
  </div>

  <!-- ===== Main content ================================================== -->
  <main class=""content"" id=""content"">

    <!-- ========================================================== SUMMARY -->
    <section class=""tab-pane active"" data-pane=""summary"">

      <div class=""grid-summary"">

        <!-- KPI strip: each card has a hero number + 3 sub-stats + sparkline -->
        <div class=""kpi-strip"">
          <div class=""kpi"" data-explain=""kpi-fps"">
            <div class=""kpi-head"">
              <span class=""k"">avg fps</span>
              <span class=""kpi-tag"" id=""kpi-fps-tag"">—</span>
            </div>
            <div class=""kpi-hero""><span class=""v"" id=""kpi-fps-v"">—</span><span class=""v-suffix"">/ 60</span></div>
            <div class=""kpi-subs"" id=""kpi-fps-subs""></div>
            <svg class=""kpi-spark"" id=""kpi-fps-spark"" viewBox=""0 0 100 16"" preserveAspectRatio=""none""></svg>
          </div>
          <div class=""kpi"" data-explain=""kpi-worst"">
            <div class=""kpi-head"">
              <span class=""k"">worst frame</span>
              <span class=""kpi-tag"" id=""kpi-worst-tag"">—</span>
            </div>
            <div class=""kpi-hero""><span class=""v"" id=""kpi-worst-v"">—</span><span class=""v-suffix"">ms</span></div>
            <div class=""kpi-subs"" id=""kpi-worst-subs""></div>
            <svg class=""kpi-spark"" id=""kpi-worst-spark"" viewBox=""0 0 100 16"" preserveAspectRatio=""none""></svg>
          </div>
          <div class=""kpi"" data-explain=""kpi-spikes"">
            <div class=""kpi-head"">
              <span class=""k"">lag spikes</span>
              <span class=""kpi-tag"" id=""kpi-spikes-tag"">—</span>
            </div>
            <div class=""kpi-hero""><span class=""v"" id=""kpi-spikes-v"">—</span><span class=""v-suffix"">in 30s</span></div>
            <div class=""kpi-subs"" id=""kpi-spikes-subs""></div>
            <svg class=""kpi-spark"" id=""kpi-spikes-spark"" viewBox=""0 0 100 16"" preserveAspectRatio=""none""></svg>
          </div>
          <div class=""kpi"" data-explain=""kpi-stalls"">
            <div class=""kpi-head"">
              <span class=""k"">stalls</span>
              <span class=""kpi-tag"" id=""kpi-stalls-tag"">—</span>
            </div>
            <div class=""kpi-hero""><span class=""v"" id=""kpi-stalls-v"">—</span><span class=""v-suffix"">session</span></div>
            <div class=""kpi-subs"" id=""kpi-stalls-subs""></div>
            <svg class=""kpi-spark"" id=""kpi-stalls-spark"" viewBox=""0 0 100 16"" preserveAspectRatio=""none""></svg>
          </div>
        </div>

        <!-- Frame chart hero -->
        <div class=""panel panel-hero"" style=""grid-area: chart;"">
          <header class=""panel-h"">
            <span class=""panel-title"" id=""chart-title"">frame time · last 30s</span>
            <span class=""panel-sub"" id=""chart-sub"">—</span>
            <span class=""chart-toggle"" id=""chart-mode"">
              <button class=""active"" data-mode=""ms"">frame ms</button>
              <button data-mode=""fps"">fps</button>
            </span>
          </header>
          <div class=""chart-wrap"">
            <svg class=""chart"" id=""frame-chart"" viewBox=""0 0 100 28"" preserveAspectRatio=""none"" aria-hidden=""true""></svg>
            <div class=""chart-axis""><span>0</span><span>spike threshold</span><span>worst</span></div>
          </div>
        </div>

        <!-- Impact share donut -->
        <div class=""panel"" style=""grid-area: donut;"">
          <header class=""panel-h"">
            <span class=""panel-title"">impact share</span>
            <span class=""panel-sub"" id=""donut-sub"">—</span>
          </header>
          <div class=""donut-wrap"">
            <svg class=""donut"" id=""donut-svg"" viewBox=""-50 -50 100 100"" aria-hidden=""true""></svg>
            <div class=""donut-center"" id=""donut-center"">
              <span class=""dc-pct"" id=""donut-pct"">—</span>
              <span class=""dc-name"" id=""donut-name"">—</span>
              <span class=""dc-ms"" id=""donut-ms"">—</span>
            </div>
          </div>
          <div class=""donut-legend"" id=""donut-legend""></div>
        </div>

        <!-- 3-row session-trend sparklines -->
        <div class=""panel"" style=""grid-area: trends;"">
          <header class=""panel-h"">
            <span class=""panel-title"">session trend · last 30s</span>
            <span class=""panel-sub"" data-explain=""sparklines"">frame · alloc · spikes</span>
          </header>
          <div class=""trends"">
            <div class=""trend-row""><span class=""tr-k"" data-explain=""spark-frame"">frame</span><svg class=""tr-spark"" id=""spark-frame"" viewBox=""0 0 100 16"" preserveAspectRatio=""none""></svg></div>
            <div class=""trend-row""><span class=""tr-k"" data-explain=""spark-gc"">gc</span><svg class=""tr-spark"" id=""spark-alloc"" viewBox=""0 0 100 16"" preserveAspectRatio=""none""></svg></div>
            <div class=""trend-row""><span class=""tr-k"" data-explain=""spark-spike"">spikes</span><svg class=""tr-spark"" id=""spark-spike"" viewBox=""0 0 100 16"" preserveAspectRatio=""none""></svg></div>
          </div>
        </div>

        <!-- Session timeframe heatmap -->
        <div class=""panel heatmap-panel"">
          <header class=""panel-h"">
            <span class=""panel-title"">session timeframe · minute by minute</span>
            <span class=""panel-sub"" id=""heatmap-sub"" data-explain=""heatmap"">—</span>
          </header>
          <div class=""heatmap-wrap"">
            <div class=""heatmap-grid"" id=""heatmap-grid""></div>
            <div class=""heatmap-legend"">
              <span><span class=""lg-sw"" style=""background: var(--perf-0)""></span>smooth (≤17ms)</span>
              <span><span class=""lg-sw"" style=""background: var(--perf-1)""></span>17–25ms</span>
              <span><span class=""lg-sw"" style=""background: var(--perf-2)""></span>25–40ms</span>
              <span><span class=""lg-sw"" style=""background: var(--perf-3)""></span>40–60ms</span>
              <span><span class=""lg-sw"" style=""background: var(--perf-4)""></span>&gt;60ms · spikes</span>
              <span><span class=""lg-sw lg-boss""></span>boss fight</span>
            </div>
          </div>
        </div>

        <!-- Now playing segments -->
        <div class=""panel"" style=""grid-area: now;"">
          <header class=""panel-h"">
            <span class=""panel-title"">now playing</span>
            <span class=""panel-sub"" id=""now-sub"">0 open</span>
          </header>
          <div class=""nowlist"" id=""nowlist""></div>
        </div>

        <!-- Events feed -->
        <div class=""panel"" style=""grid-area: events;"">
          <header class=""panel-h"">
            <span class=""panel-title"">recent events</span>
            <span class=""panel-sub"">last 12</span>
          </header>
          <div class=""events"" id=""nowevents""></div>
        </div>

        <!-- Full mod ranking with tree expansion + mod cards -->
        <div class=""panel panel-wide"" style=""grid-area: mods;"">
          <header class=""panel-h"">
            <span class=""panel-title"">mods · cascading tree</span>
            <span class=""panel-actions"">
              <input type=""search"" id=""mod-filter"" placeholder=""filter…"" class=""filter-input"" />
              <button id=""mods-collapse-all"" class=""mini-btn"" title=""collapse every expanded mod + category"">collapse all</button>
              <span class=""segctl"" id=""mods-sort"">
                <button class=""active"" data-sort=""composite"" data-explain=""composite"">composite</button>
                <button data-sort=""cpu"">cpu</button>
                <button data-sort=""avg"">avg</button>
                <button data-sort=""alloc"" id=""sort-alloc"">alloc</button>
              </span>
            </span>
          </header>
          <div class=""modtable-head"">
            <span class=""mh rank"">#</span>
            <span class=""mh name"">mod</span>
            <span class=""mh bar"">cost</span>
            <span class=""mh trend"">30s</span>
            <span class=""mh num""><span data-explain=""tick-frame"">now</span></span>
            <span class=""mh num""><span data-explain=""tick-avg"">avg</span></span>
            <span class=""mh num"" id=""mh-alloc""><span data-explain=""alloc"">alloc</span></span>
          </div>
          <div class=""modtable"" id=""modtable""></div>
        </div>

      </div>
    </section>

    <!-- ========================================================= TIMELINE -->
    <section class=""tab-pane"" data-pane=""timeline"">
      <div class=""panel"">
        <header class=""panel-h"">
          <span class=""panel-title"">session segments</span>
          <span class=""panel-actions"">
            <span class=""segctl"" id=""timeline-filter"">
              <button class=""active"" data-filter=""all"">all</button>
              <button data-filter=""boss"">boss</button>
              <button data-filter=""biome"">biome</button>
              <button data-filter=""weather"">weather</button>
              <button data-filter=""drama"">has drama</button>
            </span>
            <span class=""panel-sub"" id=""timeline-sub"">—</span>
          </span>
        </header>
        <div class=""timeline"" id=""timelinelist""></div>
      </div>
    </section>

    <!-- ============================================================== LAG -->
    <section class=""tab-pane"" data-pane=""lag"">
      <div class=""panel"">
        <header class=""panel-h"">
          <span class=""panel-title"">lag events · spikes &amp; stalls</span>
          <span class=""panel-actions"">
            <span class=""segctl"" id=""lag-filter"">
              <button class=""active"" data-lag-filter=""all"">all</button>
              <button data-lag-filter=""spikes"" data-explain=""spike"">spikes</button>
              <button data-lag-filter=""stalls"" data-explain=""stall"">stalls</button>
            </span>
            <span class=""panel-sub"" id=""lag-sub"">—</span>
          </span>
        </header>
        <div class=""lagfeed"" id=""lagfeed""></div>
      </div>
    </section>

    <!-- ========================================================= INSIGHTS -->
    <section class=""tab-pane"" data-pane=""insights"">
      <div class=""panel"">
        <header class=""panel-h"">
          <span class=""panel-title"">live insights</span>
          <span class=""panel-sub"" id=""insights-sub"" data-explain=""insights"">—</span>
        </header>
        <div class=""insights"" id=""insightslist""></div>
      </div>
    </section>

    <!-- ============================================================= SELF -->
    <section class=""tab-pane"" data-pane=""self"">
      <div class=""self-layout"">

        <!-- Hero: profiler-health gauge + headline metric -->
        <div class=""panel self-hero"">
          <header class=""panel-h"">
            <span class=""panel-title"">profiler health</span>
            <span class=""panel-sub"" data-explain=""self-severity"">bytes-per-hook ratio · honest budget signal</span>
          </header>
          <div class=""hero-body"">
            <div class=""gauge"" id=""self-gauge"">
              <svg viewBox=""0 0 100 60"" preserveAspectRatio=""xMidYMid meet""></svg>
            </div>
            <div class=""hero-stats"">
              <div class=""hero-stat""><span class=""k"">severity</span><span class=""v"" id=""hero-sev"">—</span></div>
              <div class=""hero-stat""><span class=""k"">bytes/hook</span><span class=""v"" id=""hero-bph"">—</span></div>
              <div class=""hero-stat""><span class=""k"">vs baseline</span><span class=""v"" id=""hero-ratio"">—</span></div>
              <div class=""hero-stat""><span class=""k"">hooks installed</span><span class=""v"" id=""hero-hooks"">—</span></div>
            </div>
          </div>
        </div>

        <!-- Install footprint card with visual delta bar -->
        <div class=""panel"">
          <header class=""panel-h"">
            <span class=""panel-title"">install footprint</span>
            <span class=""panel-sub"" data-explain=""install-delta"">heap delta over baseline</span>
          </header>
          <div class=""self-body"" id=""self-install""></div>
          <div class=""footprint-bar"" id=""footprint-bar""></div>
        </div>

        <!-- Process context with managed-vs-native split bar -->
        <div class=""panel"">
          <header class=""panel-h"">
            <span class=""panel-title"">process context</span>
            <span class=""panel-sub"" data-explain=""process-context"">managed heap vs total working set</span>
          </header>
          <div class=""self-body"" id=""self-process""></div>
          <div class=""split-bar"" id=""self-split""></div>
        </div>

        <!-- Backend mode card -->
        <div class=""panel"">
          <header class=""panel-h"">
            <span class=""panel-title"">attribution backend</span>
            <span class=""panel-sub"" data-explain=""backend"">how we instrument hooks</span>
          </header>
          <div class=""self-body"" id=""self-backend""></div>
        </div>

        <!-- Hook distribution per mod (mini chart) -->
        <div class=""panel panel-wide"">
          <header class=""panel-h"">
            <span class=""panel-title"">hook distribution · top 12 mods by hook count</span>
            <span class=""panel-sub"" id=""hookdist-sub"">—</span>
          </header>
          <div class=""hookdist"" id=""self-hookdist""></div>
        </div>

      </div>
    </section>

  </main>

  <footer class=""footstrip"">
    <span id=""foot-cadence"">polling /api · 500 ms · 1-5 to switch tabs</span>
    <span id=""foot-clock"">—</span>
    <span class=""foot-spacer""></span>
    <span id=""foot-mode"">—</span>
  </footer>

  <!-- ===== Mod card slide-in (right-side panel) ========================= -->
  <aside class=""modcard hidden"" id=""modcard"" role=""dialog"" aria-label=""mod detail"">
    <header class=""mc-h"">
      <span class=""mc-back"" id=""mc-close"" title=""close"">&times;</span>
      <span class=""mc-name"" id=""mc-name"">—</span>
      <span class=""mc-rank"" id=""mc-rank"">—</span>
    </header>
    <div class=""mc-body"" id=""mc-body""></div>
  </aside>

  <!-- ===== Tooltip layer (single instance reused) ======================= -->
  <div class=""tooltip hidden"" id=""tooltip""></div>

</div>

<script src=""/dashboard.js""></script>
</body>
</html>
";
}
