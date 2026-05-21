#nullable enable

namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    // Timeline tab — Gantt-style swimlane dashboard.
    //
    // Layout (top → bottom):
    //   tl-heatstrip      minute-bucket activity strip (T4)
    //   tl-transitions    context-transition diamond markers (T3)
    //   tl-gantt          5 stacked lanes — Biome, Weather, Boss, Invasion, Subworld (T1+T2)
    //   tl-bottom         detail pane + attendance roll-up (T5)
    //   tl-deaths         one strip per death (T6)
    //   tl-chronicle      timestamped chronicle lines (T7)
    //
    // The lanes are absolutely positioned over the session time domain.
    // The JS reads window start/end from the union of segments + transitions
    // + activity buckets and scales each block accordingly. No fancy
    // visualisations yet — plain rectangles, inline waterfalls, diamonds.
    private const string HtmlTimeline = @"<!-- ========================================================= TIMELINE -->
    <section class=""tab-pane"" data-pane=""timeline"">
      <div class=""tl-shell"">
        <div class=""tl-heatstrip"" id=""tl-heatstrip""></div>
        <div class=""tl-transitions"" id=""tl-transitions""></div>
        <div class=""tl-gantt"">
          <div class=""tl-lane"" data-family=""Biome""    id=""tl-lane-biome""></div>
          <div class=""tl-lane"" data-family=""Weather""  id=""tl-lane-weather""></div>
          <div class=""tl-lane"" data-family=""Boss""     id=""tl-lane-boss""></div>
          <div class=""tl-lane"" data-family=""Invasion"" id=""tl-lane-invasion""></div>
          <div class=""tl-lane"" data-family=""Subworld"" id=""tl-lane-subworld""></div>
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
