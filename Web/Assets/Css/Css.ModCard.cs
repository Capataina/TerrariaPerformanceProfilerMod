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
    private const string CssModCard = @"
/* =================================================== MOD CARD (drawer) */
/* The mod card is now the shared .drawer component (chrome, header, scrolling
   body) composed from sectionBlock / statGrid / statTile / callout / row in the
   component library. The only mod-card-specific rule left is the right-aligned
   value column in the category mini-list's row(). */
.mc-catv { color: var(--muted); text-align: right; }

/* Hero header: the mod's series colour as an accent bar + its CPU share as the
   headline, so the card opens on its most important number. */
.mc-hero { display: flex; align-items: stretch; gap: 0.7rem; margin-bottom: 0.9rem; }
.mc-hero .mc-swatch { width: 0.5rem; min-height: 2.6rem; border-radius: 2px; flex: none; }
.mc-hero-meta { display: flex; flex-direction: column; justify-content: center; }
.mc-hero-val { font-family: var(--mono); font-size: 1.9rem; line-height: 1; color: var(--text-bright); }
.mc-hero-lbl { font-family: var(--ui); font-size: 0.72rem; color: var(--muted); margin-top: 0.25rem; }
";
}
