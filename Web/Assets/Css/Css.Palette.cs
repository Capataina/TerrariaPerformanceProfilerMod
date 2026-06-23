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
/* Palette: shadcn ""neutral"" base, dark, MONOCHROME chrome. The entire UI
   surface is pure neutral grey (zero chroma) and the one signature accent is
   near-white, so the ONLY colour on screen is data. Colour is reserved for
   meaning: the perf ramp (green->bad red), per-mod series, and the semantic
   state hues (good / warn / danger). Chrome never competes with the data.

   Colour is defined in OKLCH (perceptually uniform) on two layers:
     1. a shadcn SEMANTIC token layer (--background / --card / --primary /
        --muted-foreground / --border / --ring / --radius …) the component
        library reads, and
     2. the LEGACY vars (--bg-deep / --panel / --text / --accent …) every
        not-yet-migrated surface still reads, aliased onto the same ramp so the
        two can never drift. One source of truth.

   The data-viz ramp (--perf-0..4, --cpu, --alloc, the state hues …) is a
   DELIBERATELY separate, still-colourful block: chart encodings and UI chrome
   are different concerns. Greying the chrome must never grey the data.

   NB --muted is the muted TEXT grey here (every surface reads it that way),
   NOT a shadcn ""muted surface"" token; surfaces are --card/--secondary/
   --surface. --accent is OUR codebase's bright SIGNAL (active tab / selection /
   focus), so it maps to shadcn --primary (near-white), not shadcn's grey
   --accent. */
:root {
  /* ===== shadcn neutral semantic tokens (OKLCH, dark, zero chroma) ===== */
  --background:           oklch(0.145 0 0);   /* neutral-950 page base */
  --foreground:           oklch(0.985 0 0);   /* neutral-50 body text */
  --card:                 oklch(0.205 0 0);   /* neutral-900 Panel surface */
  --card-foreground:      oklch(0.985 0 0);
  --popover:              oklch(0.235 0 0);   /* Drawer / tooltip surface */
  --popover-foreground:   oklch(0.985 0 0);
  --primary:              oklch(0.922 0 0);   /* near-white: THE signal accent */
  --primary-foreground:   oklch(0.205 0 0);   /* dark text on a light primary */
  --secondary:            oklch(0.269 0 0);   /* neutral-800 raised control */
  --secondary-foreground: oklch(0.985 0 0);
  --muted-foreground:     oklch(0.610 0 0);   /* muted / label text */
  --accent:               oklch(0.922 0 0);   /* == primary (the one accent) */
  --accent-foreground:    oklch(0.205 0 0);
  --destructive:          oklch(0.585 0.140 22); /* danger / stalls (kept red) */
  --border:               oklch(0.269 0 0);   /* hard lines (neutral-800) */
  --input:                oklch(0.245 0 0);   /* field / soft lines */
  --ring:                 oklch(0.708 0 0);   /* neutral-400 focus ring */
  --radius:               5px;                /* one radius scale */

  /* ===== Legacy vars (aliased onto the neutral ramp) =================== */
  --bg-deep:      oklch(0.120 0 0);
  --bg-page:      var(--background);
  --panel:        var(--card);
  --panel-2:      oklch(0.175 0 0);
  --surface:      oklch(0.235 0 0);
  --surface-2:    var(--secondary);
  --header:       oklch(0.170 0 0);
  --border-soft:  var(--input);
  --hover:        oklch(0.255 0 0);

  --text:         var(--foreground);
  --text-bright:  oklch(1 0 0);
  --muted:        var(--muted-foreground);  /* muted TEXT, not a surface */
  --dim:          oklch(0.430 0 0);

  /* Chrome accent is monochrome: the bright signal is near-white, its washes
     are white-alpha. No blue anywhere in the chrome. */
  --accent-soft:  oklch(1 0 0 / 0.09);   /* selected-row wash */
  --accent-line:  oklch(1 0 0 / 0.22);   /* focus / hairline tint */

  /* ===== Data-viz ramp (KEPT COLOURFUL — the only colour on screen) ==== */
  /* Semantic state hues: health signal, so they stay coloured. */
  --good:         oklch(0.62 0.10 152);   /* green */
  --good-bar:     oklch(0.63 0.075 185);  /* teal */
  --amber:        oklch(0.66 0.105 85);   /* amber */
  --orange:       oklch(0.66 0.115 60);   /* orange */
  --danger:       var(--destructive);     /* red */
  --magenta:      oklch(0.55 0.085 312);  /* magenta */
  --purple:       oklch(0.50 0.090 290);  /* purple */
  --cyan:         oklch(0.70 0.080 205);

  /* Series colours — a value with a single semantic axis. */
  --cpu:          oklch(0.62 0.10 152);
  --alloc:        oklch(0.50 0.090 290);
  --spike:        oklch(0.66 0.115 60);
  --stall:        var(--destructive);
  --gc:           oklch(0.50 0.090 290);

  /* Performance gradient (good -> bad), stepped evenly in OKLCH. */
  --perf-0: oklch(0.62 0.10 152);   /* healthy green */
  --perf-1: oklch(0.66 0.085 185);  /* teal */
  --perf-2: oklch(0.66 0.105 85);   /* amber */
  --perf-3: oklch(0.66 0.115 60);   /* orange */
  --perf-4: oklch(0.585 0.140 22);  /* red */

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
  /* Kill the document-level rubber-band: scrolling the inner content to its end
     was bouncing the whole fixed-height app, which shunted the top bar off the
     top edge. Nothing should ever scroll the document itself. */
  overscroll-behavior: none;
}

.hidden { display: none !important; }
.muted  { color: var(--muted); }
";
}
