#nullable enable

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Persistence.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Persistence;
using PerformanceProfiler.Persistence.Records;
namespace PerformanceProfiler.Web;

/// <summary>
/// Bundles the dashboard's HTML / CSS / JS as C# string constants. The mod
/// ships as a single self-contained <c>.tmod</c> with no asset pipeline,
/// so the markup and scripts live in source as <c>@"..."</c> verbatim
/// strings, split across many partial-class files for editability.
///
/// <para>
/// Each section (palette / shell / panels / etc) is its own partial-class
/// file under <c>Web/Assets/Css/</c> and <c>Web/Assets/Js/</c> contributing
/// one <c>private const string</c>. The <see cref="Css"/> and <see cref="Js"/>
/// properties concatenate them in stable order for emission. The HTML lives
/// whole in <c>Web/Assets/IndexHtml.cs</c>.
/// </para>
///
/// <para>
/// The <c>Web/DashboardRouter.cs</c> serves each at its corresponding path
/// (<c>/dashboard.css</c>, <c>/dashboard.js</c>, <c>/</c>). The dashboard
/// SPA loads them once on page open; no further reloads after that, only
/// JSON polls into the API endpoints.
/// </para>
/// </summary>
internal static partial class DashboardAssets
{
    /// <summary>
    /// The full dashboard stylesheet. Concatenation of every <c>CssXxx</c>
    /// constant in stable order: palette must be first (defines vars),
    /// shell next (defines the layout primitives), then specific
    /// components. The scrollbar block at the very end applies after
    /// every other selector so the dashboard's webkit scrollbar override
    /// wins.
    /// </summary>
    public static readonly string Css = string.Concat(
        CssPalette,
        CssShell,
        CssComponents,
        CssCharts,
        CssPanels,
        CssSummary,
        CssMods,
        CssTimeline,
        CssLag,
        CssObservatory,
        CssInsights,
        CssSelf,
        CssMemory,
        CssModCard,
        CssTooltip,
        CssFooter,
        CssKpis,
        CssHeatmap,
        CssNowPlaying,
        CssChartToggle,
        CssCoherence,
        CssReset,
        CssScrollbar);

    /// <summary>
    /// The full dashboard script bundle. Concatenation of every <c>JsXxx</c>
    /// fragment in execution order — config + state declarations first, then
    /// tab routing, polling, helpers, the per-tab renderers, and finally the
    /// per-feature surfaces (mod card, timeline, lag, insights, self).
    /// Order matters because later fragments reference earlier-declared
    /// globals (e.g. <c>lastNow</c>, <c>activeTab</c>) and helpers.
    /// </summary>
    public static readonly string Js = string.Concat(
        JsConfig,
        JsTabs,
        JsPolling,
        JsHelpers,
        JsComponents,
        JsCharts,
        JsTooltips,
        JsTopbar,
        JsRender,
        JsKpis,
        JsSummary,
        JsMods,
        JsModCard,
        JsTimeline,
        JsLag,
        JsObservatory,
        JsInsights,
        JsSelf,
        JsMemory,
        JsReset);
}
