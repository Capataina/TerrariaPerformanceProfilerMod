#nullable enable

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
      <span class=""brand-version"" id=""brand-version"">v0.9.0</span>
    </div>
    <div class=""live"">
      <span class=""live-dot"" id=""live-dot""></span>
      <span class=""live-text"" id=""live-text"">connecting…</span>
    </div>
    <div class=""topstats"" id=""topstats"">
      <div class=""topstat""><span class=""k"">tick</span><span class=""v"" id=""ts-tick"">—</span></div>
      <div class=""topstat""><span class=""k"">frame</span><span class=""v"" id=""ts-frame"">—</span></div>
      <div class=""topstat""><span class=""k"">avg 30s</span><span class=""v"" id=""ts-avg"">—</span></div>
      <div class=""topstat""><span class=""k"">gc</span><span class=""v"" id=""ts-gc"">—</span></div>
      <div class=""topstat""><span class=""k"">backend</span><span class=""v"" id=""ts-backend"">—</span></div>
    </div>
  </header>

  <!-- ===== Tab strip ====================================================== -->
  <nav class=""tabs"" id=""tabs"">
    <button class=""tab active"" data-tab=""now"">Now</button>
    <button class=""tab"" data-tab=""mods"">Mods</button>
    <button class=""tab"" data-tab=""timeline"">Timeline</button>
    <button class=""tab"" data-tab=""spikes"">Spikes</button>
    <button class=""tab"" data-tab=""insights"">Insights</button>
    <button class=""tab"" data-tab=""self"">Self</button>
  </nav>

  <!-- ===== Empty-state banner shown when no world is loaded ============= -->
  <div class=""empty hidden"" id=""empty"">
    <div class=""empty-inner"">
      <h2>no world loaded</h2>
      <p>open a save in tModLoader and walk around — the dashboard will populate automatically.</p>
      <p class=""hint"">tip: in single-player Terraria pauses when the window loses focus.
        for a live dashboard while alt-tabbed, host via Multiplayer → Host &amp; Play on the same world.</p>
    </div>
  </div>

  <!-- ===== Main content ================================================== -->
  <main class=""content"" id=""content"">

    <!-- ============================================================== NOW -->
    <section class=""tab-pane active"" data-pane=""now"">

      <div class=""grid-now"">

        <!-- Frame chart hero -->
        <div class=""panel panel-hero"" style=""grid-area: chart;"">
          <header class=""panel-h"">
            <span class=""panel-title"">frame time · last 30s</span>
            <span class=""panel-sub"" id=""chart-sub"">—</span>
          </header>
          <div class=""chart-wrap"">
            <svg class=""chart"" id=""frame-chart"" viewBox=""0 0 100 28"" preserveAspectRatio=""none"" aria-hidden=""true""></svg>
            <div class=""chart-axis""><span>0</span><span>spike threshold</span><span>worst</span></div>
          </div>
        </div>

        <!-- Now playing -->
        <div class=""panel"" style=""grid-area: now;"">
          <header class=""panel-h"">
            <span class=""panel-title"">now playing</span>
            <span class=""panel-sub"" id=""now-sub"">0 open</span>
          </header>
          <div class=""nowlist"" id=""nowlist""></div>
        </div>

        <!-- Mod ranking -->
        <div class=""panel"" style=""grid-area: mods;"">
          <header class=""panel-h"">
            <span class=""panel-title"">mod ranking · live</span>
            <span class=""panel-sub"" id=""mods-sub"">—</span>
          </header>
          <div class=""modlist"" id=""nowmods""></div>
        </div>

        <!-- Events feed -->
        <div class=""panel"" style=""grid-area: events;"">
          <header class=""panel-h"">
            <span class=""panel-title"">recent events</span>
            <span class=""panel-sub"">last 12</span>
          </header>
          <div class=""events"" id=""nowevents""></div>
        </div>

      </div>
    </section>

    <!-- ============================================================= MODS -->
    <section class=""tab-pane"" data-pane=""mods"">
      <div class=""panel"">
        <header class=""panel-h"">
          <span class=""panel-title"">per-mod cost · this session</span>
          <span class=""panel-sub"">
            <span class=""segctl"" id=""mods-sort"">
              <button class=""active"" data-sort=""composite"">composite</button>
              <button data-sort=""cpu"">cpu</button>
              <button data-sort=""avg"">avg</button>
            </span>
          </span>
        </header>
        <div class=""modtable"" id=""modtable""></div>
      </div>
    </section>

    <!-- ========================================================= TIMELINE -->
    <section class=""tab-pane"" data-pane=""timeline"">
      <div class=""panel"">
        <header class=""panel-h"">
          <span class=""panel-title"">session segments</span>
          <span class=""panel-sub"" id=""timeline-sub"">—</span>
        </header>
        <div class=""timeline"" id=""timelinelist""></div>
      </div>
    </section>

    <!-- =========================================================== SPIKES -->
    <section class=""tab-pane"" data-pane=""spikes"">
      <div class=""panel"">
        <header class=""panel-h"">
          <span class=""panel-title"">spike windows</span>
          <span class=""panel-sub"" id=""spikes-sub"">—</span>
        </header>
        <div class=""spikes"" id=""spikeslist""></div>
      </div>
    </section>

    <!-- ========================================================= INSIGHTS -->
    <section class=""tab-pane"" data-pane=""insights"">
      <div class=""panel"">
        <header class=""panel-h"">
          <span class=""panel-title"">live insights</span>
          <span class=""panel-sub"" id=""insights-sub"">—</span>
        </header>
        <div class=""insights"" id=""insightslist""></div>
      </div>
    </section>

    <!-- ============================================================= SELF -->
    <section class=""tab-pane"" data-pane=""self"">
      <div class=""self-grid"">
        <div class=""panel""><header class=""panel-h""><span class=""panel-title"">install footprint</span></header><div class=""self-body"" id=""self-install""></div></div>
        <div class=""panel""><header class=""panel-h""><span class=""panel-title"">process context</span></header><div class=""self-body"" id=""self-process""></div></div>
        <div class=""panel""><header class=""panel-h""><span class=""panel-title"">attribution backend</span></header><div class=""self-body"" id=""self-backend""></div></div>
      </div>
    </section>

  </main>

  <footer class=""footstrip"">
    <span id=""foot-cadence"">polling /api · 500 ms</span>
    <span id=""foot-clock"">—</span>
    <span class=""foot-spacer""></span>
    <span id=""foot-mode"">—</span>
  </footer>

</div>

<script src=""/dashboard.js""></script>
</body>
</html>
";
}
