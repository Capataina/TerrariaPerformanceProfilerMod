#nullable enable

namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    private const string HtmlMemory = @"<!-- ========================================================== MEMORY -->
    <section class=""tab-pane"" data-pane=""memory"">
      <div class=""mem-layout"">

        <div class=""panel panel-wide"">
          <header class=""panel-h"">
            <span class=""panel-title"">memory · where it goes</span>
            <span class=""panel-sub"">slices sorted by size · click one for its breakdown</span>
            <span class=""segctl"" id=""mem-basis"">
              <button class=""active"" data-basis=""modlist"">modlist RAM</button>
              <button data-basis=""overhead"">profiler overhead</button>
            </span>
          </header>
          <div class=""mem-summary"" id=""mem-summary""></div>
          <div class=""mem-strip split-bar tall"" id=""mem-strip""></div>
          <div class=""bar-legend"" id=""mem-legend""></div>
        </div>

        <div class=""panel panel-wide"">
          <header class=""panel-h""><span class=""panel-title"">per mod</span></header>
          <div class=""mem-table-wrap"" id=""mem-table""></div>
        </div>

        <div class=""panel"">
          <header class=""panel-h""><span class=""panel-title"">breakdown</span></header>
          <div class=""mem-drawer"" id=""mem-drawer""></div>
        </div>

      </div>
    </section>

    ";
}
