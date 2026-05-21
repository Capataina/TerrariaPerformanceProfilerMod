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
    private const string CssPalette = @"
/* Palette: command-center / Palantir-style. Near-black backgrounds,
   restrained electric-blue accent, desaturated semantic colours so
   alerts pop without the whole UI feeling like a Christmas tree. */
:root {
  --bg-deep:      #07090c;
  --bg-page:      #0a0d12;
  --panel:        #0d1117;
  --panel-2:      #0a0d12;
  --surface:      #11161c;
  --surface-2:    #161b22;
  --header:       #0a0d12;
  --border:       #1b2028;
  --border-soft:  #14191f;
  --hover:        #161c24;

  --text:         #d6dae0;
  --text-bright:  #f0f3f8;
  --muted:        #6a727f;
  --dim:          #3d434c;

  /* Restrained, desaturated semantic accents */
  --good:         #4f9d6a;   /* muted green */
  --good-bar:     #4b9c8c;   /* dim teal */
  --amber:        #b88a25;   /* dim amber */
  --orange:       #c97f3c;   /* burnt orange */
  --danger:       #b94e58;   /* muted red */
  --magenta:      #8367a3;   /* dim magenta */
  --purple:       #6e5d96;   /* dim purple */
  --accent:       #4a9eff;   /* THE single signature electric blue */
  --cyan:         #4ab8c2;
  --accent-soft:  rgba(74, 158, 255, 0.08);
  --accent-line:  rgba(74, 158, 255, 0.30);

  /* Series colours — used everywhere a value has a single semantic axis */
  --cpu:          #4f9d6a;
  --alloc:        #6e5d96;
  --spike:        #c97f3c;
  --stall:        #b94e58;
  --gc:           #6e5d96;

  /* Performance gradient (good → bad), mod bars + heatmap cells */
  --perf-0: #4f9d6a;   /* healthy green */
  --perf-1: #6aa3a8;   /* teal */
  --perf-2: #b88a25;   /* amber */
  --perf-3: #c97f3c;   /* orange */
  --perf-4: #b94e58;   /* red */

  --mono: 'JetBrains Mono', 'SFMono-Regular', 'Menlo', monospace;
  --ui:   'Inter', -apple-system, 'Segoe UI', sans-serif;
}

* { box-sizing: border-box; }

html, body {
  margin: 0; padding: 0;
  background: var(--bg-deep);
  color: var(--text);
  font-family: var(--ui);
  font-size: 14px;
  line-height: 1.45;
  height: 100vh;
  overflow: hidden;
}

.hidden { display: none !important; }
.muted  { color: var(--muted); }
";
}
