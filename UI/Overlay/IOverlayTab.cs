// ARCHIVED v0.9.0: in-game overlay replaced by browser dashboard.
// Sources retained for possible Steam-Deck variant or future revival;
// remove the #if false / #endif guards below to restore compilation.
#if false
#nullable enable

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PerformanceProfiler.Profiling;

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Persistence.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Persistence;
using PerformanceProfiler.Persistence.Records;
namespace PerformanceProfiler.UI.Overlay;

/// <summary>
/// One tab inside the profiler overlay. A tab owns its own state (scroll
/// position, expanded sets, derived rankings, whatever it needs) and renders
/// only its content area — the chrome (header strip, NOW/30S AVG and
/// LIVE/PAUSED toggles, tab strip, per-tick stats, PROFILER HEALTH bar) is
/// drawn by <see cref="OverlayPanel"/> before the tab's <see cref="Draw"/>
/// fires.
///
/// <para><b>Lifecycle per frame, in order:</b></para>
/// <list type="number">
///   <item><see cref="Tick"/> — recompute any derived state (sorting, etc).</item>
///   <item><see cref="MeasurePanelHeight"/> — chrome resizes the panel.</item>
///   <item><see cref="Draw"/> — render content below the chrome's divider.</item>
/// </list>
///
/// <para>
/// Input is dispatched only when the tab is active. Header and tab-strip
/// clicks never reach the tab — the chrome consumes them; only clicks below
/// the tab-strip Y are delegated via <see cref="HandleClick"/>.
/// </para>
///
/// <para>
/// Cross-tab state (paused, 30s-avg vs NOW, frozen snapshot arrays, the
/// current CPU/MEM/BOTH metric pill) lives in <see cref="OverlayState"/>
/// and is shared across tabs. New tabs should READ from OverlayState but
/// not write to it; pause/snapshot management is the chrome's job.
/// </para>
/// </summary>
internal interface IOverlayTab
{
    /// <summary>Short label shown in the tab strip (e.g. "TREE", "SPIKES").</summary>
    string Label { get; }

    /// <summary>
    /// True if the tab's content can be rendered for the given collector
    /// state. A tab whose dependencies aren't satisfied (e.g. allocation
    /// data needed but tracking is off) can return false to hide itself
    /// from the strip; the chrome then won't dispatch input to it.
    /// </summary>
    bool IsAvailable(MetricCollector? collector);

    /// <summary>
    /// Per-frame refresh hook. Tabs maintaining derived state (sorted mod
    /// rankings, bucketed event aggregates, etc) recompute here. Called
    /// once per frame BEFORE <see cref="MeasurePanelHeight"/> and
    /// <see cref="Draw"/>. Tabs that have no per-frame work can leave
    /// this empty.
    /// </summary>
    void Tick(MetricCollector collector);

    /// <summary>
    /// Returns the total panel height the tab wants — INCLUDING the chrome.
    /// The chrome's content area starts at <see cref="OverlayLayout.RowsTopOffset"/>;
    /// add the chrome offset to whatever content height your tab needs.
    /// </summary>
    float MeasurePanelHeight(MetricCollector collector);

    /// <summary>
    /// Renders the tab's content area. The chrome has already drawn the
    /// panel background, header, tab strip, stats, and health bar; this
    /// method is responsible for everything from
    /// <see cref="OverlayLayout.DividerOffset"/> downward.
    /// </summary>
    void Draw(SpriteBatch sb, Rectangle area, MetricCollector collector);

    /// <summary>
    /// Handles a left-mouse-down inside the tab's content area. Coordinates
    /// are panel-local (0,0 at the panel's top-left corner). Clicks above
    /// <c>HeaderHeight + TabStripHeight</c> are handled by the chrome and
    /// never reach this method.
    /// </summary>
    void HandleClick(float localX, float localY, MetricCollector collector);

    /// <summary>
    /// Handles a scroll-wheel event while the mouse is over the panel.
    /// <paramref name="delta"/> is <c>+1</c> for scroll-down (advance toward
    /// later content) and <c>-1</c> for scroll-up. The chrome only dispatches
    /// when the cursor is hovering the panel.
    /// </summary>
    void HandleScroll(int delta, MetricCollector collector);
}

#endif
