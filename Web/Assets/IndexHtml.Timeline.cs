#nullable enable

namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    // Timeline tab — Gantt-style swimlane dashboard, readable vocabulary.
    //
    // Layout (top → bottom):
    //   tl-filterbar      family filter buttons (all / per-family)
    //   tl-heatstrip      per-minute activity bar strip (T4)
    //   tl-transitions    time-placed labelled transition chips (T3)
    //   tl-gantt          5 stacked lanes — Biome, Weather, Boss, Invasion, Subworld (T1+T2)
    //   tl-bottom         detail pane + attendance roll-up (T5)
    //   tl-deaths         one card per death, labelled event chips (T6)
    //   tl-chronicle      timestamped chronicle blocks (T7)
    //
    // The lanes are absolutely positioned over the session time domain.
    // The JS reads window start/end from the union of segments + transitions
    // + activity buckets and scales each block accordingly. Each lane is
    // wrapped in a tl-laneRow so the family filter can hide whole rows;
    // the inner tl-lane keeps data-family for its CSS label.
    private const string HtmlTimeline = @"<!-- ========================================================= TIMELINE -->
    <section class=""tab-pane"" data-pane=""timeline"">
      <div class=""tl-shell"">
        <div class=""tl-filterbar"" id=""tl-filterbar""></div>
        <div class=""tl-heatstrip"" id=""tl-heatstrip""></div>
        <div class=""tl-transitions"" id=""tl-transitions""></div>
        <div class=""tl-gantt"">
          <div class=""tl-laneRow"" id=""tl-laneRow-biome""><div class=""tl-lane"" data-family=""Biome"" id=""tl-lane-biome""></div></div>
          <div class=""tl-laneRow"" id=""tl-laneRow-weather""><div class=""tl-lane"" data-family=""Weather"" id=""tl-lane-weather""></div></div>
          <div class=""tl-laneRow"" id=""tl-laneRow-boss""><div class=""tl-lane"" data-family=""Boss"" id=""tl-lane-boss""></div></div>
          <div class=""tl-laneRow"" id=""tl-laneRow-invasion""><div class=""tl-lane"" data-family=""Invasion"" id=""tl-lane-invasion""></div></div>
          <div class=""tl-laneRow"" id=""tl-laneRow-subworld""><div class=""tl-lane"" data-family=""Subworld"" id=""tl-lane-subworld""></div></div>
        </div>
        <div class=""tl-bottom"">
          <aside class=""tl-detail"" id=""tl-detail""></aside>
          <div class=""tl-attendance"" id=""tl-attendance""></div>
        </div>
        <div class=""tl-deaths"" id=""tl-deaths""></div>
        <div class=""tl-chronicle"" id=""tl-chronicle""></div>
      </div>
    </section>

    ";
}
