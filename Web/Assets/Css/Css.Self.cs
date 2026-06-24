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
}
@media (max-width: 900px) { .self-layout { grid-template-columns: 1fr; } }
.self-hero { grid-column: 1 / -1; }
.self-span { grid-column: 1 / -1; }

/* Gauge column is sized to the 200px (~12.5rem) gauge SVG, not a padded 14rem,
   so the SEVERITY tile starts close and the hero reads as one band rather than
   leaving dead space around a centred gauge. */
.self-hero-body { display: grid; grid-template-columns: 12.5rem 1fr; gap: 1rem; align-items: center; }
@media (max-width: 700px) { .self-hero-body { grid-template-columns: 1fr; } }
.self-gauge { width: 100%; max-width: 12.5rem; }
@media (max-width: 700px) { .self-gauge { margin: 0 auto; } }
";
}
