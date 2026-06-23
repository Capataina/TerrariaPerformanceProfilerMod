#nullable enable

namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    // Memory tab: panel chrome only. Every visual inside the panels comes from
    // the shared component library — the summary band is a statGrid, the hero
    // strip is a splitBar + legend(), the basis toggle is a segmented() rendered
    // into the #mem-basis slot, the per-mod table is a .dtable, and the breakdown
    // drawer is sectionBlock()s of splitBar/splitLegend + statGrid. The old
    // bespoke .mem-summary / .mem-strip / .mem-table-wrap / .mem-val / .mem-bd /
    // .mem-drawer / .mem-sect / .mem-card classes were retired onto components.
    // Only the .mem-layout column grid stays as genuinely-bespoke layout.
    private const string HtmlMemory = @"<!-- ========================================================== MEMORY -->
    <section class=""tab-pane"" data-pane=""memory"">
      <div class=""mem-layout"">

        <div class=""panel panel-wide"">
          <header class=""panel-h"">
            <span class=""panel-title"">memory · where it goes</span>
            <span class=""panel-sub"">slices sorted by size · click one for its breakdown</span>
            <span class=""segctl"" id=""mem-basis""></span>
          </header>
          <div class=""panel-body"">
            <div id=""mem-summary""></div>
            <div id=""mem-strip"" style=""margin-top:0.6rem""></div>
            <div id=""mem-legend""></div>
          </div>
        </div>

        <div class=""panel panel-wide"">
          <header class=""panel-h""><span class=""panel-title"">per mod</span></header>
          <div class=""scroll-region"" id=""mem-table"" style=""max-height:28rem""></div>
        </div>

        <div class=""panel"">
          <header class=""panel-h""><span class=""panel-title"">breakdown</span></header>
          <div class=""panel-body"" id=""mem-drawer""></div>
        </div>

      </div>
    </section>

    ";
}
