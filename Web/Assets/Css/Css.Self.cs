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
    // Self tab: pure layout only. Every visual (gauge, stat tiles, stat lines,
    // split bars, hook-distribution rows) comes from the shared component
    // library, so this file holds just the responsive grid that arranges the
    // panels. The old bespoke .hero-stat / .self-row / .footprint-bar /
    // .split-bar / .hd-row / .panel-wide classes were retired onto components
    // (which also resolved the .split-bar and .panel-wide cross-file collisions).
    private const string CssSelf = @"
/* =================================================== SELF TAB */
.self-layout {
  display: grid;
  grid-template-columns: minmax(0, 1.5fr) minmax(0, 1fr);
  gap: 0.75rem;
  grid-auto-flow: dense;
  /* Hug content: panels size to what they hold instead of stretching to match
     their tallest grid neighbour (which left install-footprint with a long empty
     tail under its few rows). The dense flow packs the shorter panels up. This is
     the systemic dead-space fix — no per-panel height tuning. */
  align-items: start;
}
@media (max-width: 900px) { .self-layout { grid-template-columns: 1fr; } }
.self-hero { grid-column: 1 / -1; }
.self-span { grid-column: 1 / -1; }
/* The install-footprint + process-context pair stretch to a shared height (the
   taller of the two) instead of hugging content, so the side-by-side row reads as
   one even band with no step between them. align-self overrides the layout's
   align-items:start for just these two; the full-width panels still hug. */
.self-pair { align-self: stretch; }

/* Gauge column shrinks to the arc's actual footprint (auto, not a fixed 12.5rem
   track that resolved wider than the 200px SVG and left the gauge floating left
   with a dead band before the SEVERITY tile), and the gap to the stats is
   tightened, so the SEVERITY tile starts close and the hero reads as one band. */
.self-hero-body { display: grid; grid-template-columns: auto 1fr; gap: 0.6rem; align-items: center; }
@media (max-width: 700px) { .self-hero-body { grid-template-columns: 1fr; } }
/* Fixed to the gauge's own 12.5rem footprint and centred in its (auto) track via
   justify-self, so the arc + its centred value sit in the middle of the column
   rather than left of it. The stats cell still stretches to fill 1fr. */
.self-gauge { width: 12.5rem; max-width: 100%; justify-self: center; }
@media (max-width: 700px) { .self-gauge { margin: 0 auto; } }

/* Process-context headline: the managed-share tile is the panel's answer, so it
   leads at hero size with the two raw byte rows as supporting detail beneath.
   Reuses the shared stat-tile; the local rules only enlarge the figure past the
   default .big and add the gap that separates headline from supporting rows. */
.self-share-hero { margin-bottom: 0.5rem; }
.self-share-hero .stat-tile .v { font-size: 2rem; }
";
}
