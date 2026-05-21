#nullable enable

namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    private const string HtmlTimeline = @"<!-- ========================================================= TIMELINE -->
    <section class=""tab-pane"" data-pane=""timeline"">
      <div class=""panel"">
        <header class=""panel-h"">
          <span class=""panel-title"">session segments</span>
          <span class=""panel-actions"">
            <span class=""segctl"" id=""timeline-filter"">
              <button class=""active"" data-filter=""all"">all</button>
              <button data-filter=""boss"">boss</button>
              <button data-filter=""biome"">biome</button>
              <button data-filter=""weather"">weather</button>
              <button data-filter=""drama"">has drama</button>
            </span>
            <span class=""panel-sub"" id=""timeline-sub"">—</span>
          </span>
        </header>
        <div class=""timeline"" id=""timelinelist""></div>
      </div>
    </section>

    ";
}
