#nullable enable

namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    private const string HtmlPreamble = @"<!DOCTYPE html>
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
      <span class=""brand-version"" id=""brand-version"">v0.18</span>
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
    <button class=""tab"" data-tab=""memory""><span class=""ki"">6</span>Memory</button>
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

    ";
}
