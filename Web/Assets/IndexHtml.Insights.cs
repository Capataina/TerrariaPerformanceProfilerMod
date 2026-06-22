#nullable enable

namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    private const string HtmlInsights = @"<!-- ========================================================= INSIGHTS -->
    <!-- Mount points only; every surface is built from the shared readable
         vocabulary (split bars, perf-tinted sortable tables, chips, stat
         lines) in Js.Insights.cs. KPI ring strip, dormant table (I2),
         observatory list + tabular detail (I1+I3+I4), cross-cutting section
         list (I5), engagement-vs-cost table (I6), mod-pair correlation
         table (I7). The lower three analytical surfaces sit in a responsive
         grid (.ins-lower) so they fill the width instead of leaving a dead
         right-hand strip. -->
    <section class=""tab-pane"" data-pane=""insights"">
      <div class=""ins-shell"">
        <div class=""ins-kpi"" id=""ins-kpi""></div>
        <div class=""ins-mid"">
          <div class=""ins-observatory"">
            <div class=""ins-dormant"" id=""ins-dormant""></div>
            <div class=""ins-obs-list"" id=""ins-obs-list""></div>
          </div>
          <aside class=""ins-detail"" id=""ins-detail""></aside>
        </div>
        <div class=""ins-lower"">
          <div class=""ins-cross"" id=""ins-cross""></div>
          <div class=""ins-scatter"" id=""ins-scatter""></div>
          <div class=""ins-matrix"" id=""ins-matrix""></div>
        </div>
      </div>
    </section>

    ";
}
