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
";
}
