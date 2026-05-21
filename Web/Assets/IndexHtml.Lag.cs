#nullable enable

namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    private const string HtmlLag = @"<!-- ============================================================== LAG -->
    <section class=""tab-pane"" data-pane=""lag"">
      <div class=""panel"">
        <header class=""panel-h"">
          <span class=""panel-title"">lag events · spikes &amp; stalls</span>
          <span class=""panel-actions"">
            <span class=""segctl"" id=""lag-filter"">
              <button class=""active"" data-lag-filter=""all"">all</button>
              <button data-lag-filter=""spikes"" data-explain=""spike"">spikes</button>
              <button data-lag-filter=""stalls"" data-explain=""stall"">stalls</button>
            </span>
            <span class=""panel-sub"" id=""lag-sub"">—</span>
          </span>
        </header>
        <div class=""lagfeed"" id=""lagfeed""></div>
      </div>
    </section>

    ";
}
