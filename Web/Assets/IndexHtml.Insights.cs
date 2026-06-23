#nullable enable

namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    // Insights pane: mount points only. Every surface (KPI gauges, dormant
    // table, observatory master-detail, cross-cutting / engagement /
    // correlation tables) is built in Js.Insights.cs from the shared component
    // library (panel / scrollRegion / statGrid / row / .dtable). The only
    // bespoke markup here is the two responsive grids (.ins-mid, .ins-lower)
    // that arrange the component-rendered panels; their CSS lives in
    // Css.Insights.cs. renderInsights() fills these slots.
    private const string HtmlInsights = @"<!-- ========================================================= INSIGHTS -->
    <section class=""tab-pane"" data-pane=""insights"">
      <div class=""ins-shell"">
        <div id=""ins-kpi""></div>
        <div class=""ins-mid"">
          <div class=""ins-observatory"">
            <div id=""ins-dormant""></div>
            <div id=""ins-obs-list""></div>
          </div>
          <div id=""ins-detail""></div>
        </div>
        <div class=""ins-lower"">
          <div id=""ins-cross""></div>
          <div id=""ins-scatter""></div>
          <div id=""ins-matrix""></div>
        </div>
      </div>
    </section>

    ";
}
