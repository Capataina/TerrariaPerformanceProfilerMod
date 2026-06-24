#nullable enable

namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    private const string HtmlSelf = @"<!-- ============================================================= SELF -->
    <section class=""tab-pane"" data-pane=""self"">
      <div class=""self-layout"">

        <!-- Hero: profiler-health gauge + headline metrics (component-rendered) -->
        <div class=""panel self-hero"">
          <header class=""panel-h"">
            <span class=""panel-title"">profiler health</span>
            <span class=""panel-sub"" data-explain=""self-severity"">bytes-per-hook ratio · honest budget signal</span>
          </header>
          <div class=""panel-body self-hero-body"">
            <div class=""self-gauge"" id=""self-gauge""></div>
            <div id=""self-herostats""></div>
          </div>
        </div>

        <!-- Install footprint + delta split bar -->
        <div class=""panel self-pair"">
          <header class=""panel-h"">
            <span class=""panel-title"">install footprint</span>
            <span class=""panel-sub"" data-explain=""install-delta"">heap delta over baseline</span>
          </header>
          <div class=""panel-body"">
            <div id=""self-install""></div>
            <div id=""footprint-bar"" style=""margin-top:0.6rem""></div>
          </div>
        </div>

        <!-- Process context + managed/native split bar -->
        <div class=""panel self-pair"">
          <header class=""panel-h"">
            <span class=""panel-title"">process context</span>
            <span class=""panel-sub"" data-explain=""process-context"">managed heap vs total working set</span>
          </header>
          <div class=""panel-body"">
            <div id=""self-process""></div>
            <div id=""self-split"" style=""margin-top:0.6rem""></div>
          </div>
        </div>

        <!-- Backend mode (full-width so it doesn't sit narrow-left and leave the
             right column empty below process-context) -->
        <div class=""panel self-span"">
          <header class=""panel-h"">
            <span class=""panel-title"">attribution backend</span>
            <span class=""panel-sub"" data-explain=""backend"">how we instrument hooks</span>
          </header>
          <div class=""panel-body"" id=""self-backend""></div>
        </div>

        <!-- Hook distribution per mod -->
        <div class=""panel self-span"">
          <header class=""panel-h"">
            <span class=""panel-title"">hook distribution · top 12 mods by hook count</span>
            <span class=""panel-sub"" id=""hookdist-sub"">—</span>
          </header>
          <div class=""panel-body"" id=""self-hookdist""></div>
        </div>

      </div>
    </section>

  ";
}
