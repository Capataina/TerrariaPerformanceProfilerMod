#nullable enable

using System.Collections.Generic;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.UI.Overlay.Tabs;

namespace PerformanceProfiler.UI.Overlay;

/// <summary>
/// The static list of tabs the overlay renders. Order in this list = order
/// in the tab strip. Index 0 is the default landing tab.
///
/// <para>
/// <b>Adding a new tab:</b>
/// </para>
/// <list type="number">
///   <item>Drop a new class in <c>UI/Overlay/Tabs/</c> implementing
///         <see cref="IOverlayTab"/>.</item>
///   <item>Add one line to <see cref="Tabs"/>'s initializer below — the
///         position determines tab-strip order.</item>
/// </list>
///
/// <para>
/// Tabs are singletons: one instance per process, holding state across F9
/// toggles. The chrome reads
/// <see cref="OverlayState.ActiveTabIndex"/> to know which tab is active
/// and dispatches lifecycle calls to <c>Tabs[ActiveTabIndex]</c>.
/// </para>
/// </summary>
internal static class TabRegistry
{
    /// <summary>
    /// The tab list. Edit this initializer to add a new tab. Existing tabs
    /// must not be reordered without updating
    /// <see cref="OverlayState.ActiveTabIndex"/>'s persisted value — players
    /// expect the tab they last had open to still be there.
    /// </summary>
    public static List<IOverlayTab> Tabs { get; } = new List<IOverlayTab>
    {
        new OverviewTab(),     // Renders as "SUMMARY" (multi-dimensional impact view).
        new TreeTab(),
        new SpikesTab(),       // Renders as "LAG" (spikes + stalls unified feed).
        new EventsTab(),
        new InsightsTab(),
        new SelfTab(),         // The profiler's own diagnostics: install delta + process context.
    };

    // Reused buffer for the visible-tab list so the per-frame chrome path
    // doesn't allocate. Rebuilt in-place by BuildVisible() each call.
    private static readonly List<IOverlayTab> _visibleScratch = new List<IOverlayTab>(8);

    /// <summary>
    /// Returns the subset of <see cref="Tabs"/> that are available for the given
    /// collector state, preserving registry order. Returns the full list when
    /// <paramref name="collector"/> is null (chrome runs before collector exists).
    /// The returned list is a shared, reused buffer — never store the reference
    /// past the current frame.
    /// </summary>
    public static IReadOnlyList<IOverlayTab> Visible(MetricCollector? collector)
    {
        _visibleScratch.Clear();
        for (int i = 0; i < Tabs.Count; i++)
        {
            IOverlayTab tab = Tabs[i];
            if (collector == null || tab.IsAvailable(collector)) _visibleScratch.Add(tab);
        }
        // If every tab is unavailable (collector exists but no history yet)
        // fall back to showing the full list so the player still sees the
        // strip; empty-state copy is each tab's own concern.
        if (_visibleScratch.Count == 0) _visibleScratch.AddRange(Tabs);
        return _visibleScratch;
    }

    /// <summary>
    /// Clamps <see cref="OverlayState.ActiveTabIndex"/> into the visible list
    /// and returns the resulting active tab. Indices are interpreted against
    /// the visible list so disabling a tab transparently shifts later tabs
    /// up — the chrome's click handler maps clicks to the same visible-list
    /// index that this getter returns. When the previously-active tab is no
    /// longer visible, the index falls back to <c>0</c>.
    /// </summary>
    public static IOverlayTab ResolveActive(MetricCollector? collector)
    {
        IReadOnlyList<IOverlayTab> visible = Visible(collector);
        int idx = OverlayState.ActiveTabIndex;
        if (idx < 0 || idx >= visible.Count)
        {
            idx = 0;
            OverlayState.ActiveTabIndex = 0;
        }
        return visible[idx];
    }

    /// <summary>
    /// Back-compat accessor for paths that don't have a collector handy. The
    /// resolution path that actually dispatches input/draws uses
    /// <see cref="ResolveActive"/> with the live collector; this getter falls
    /// back to the full-list view for paths that pre-date the contract.
    /// </summary>
    public static IOverlayTab Active => ResolveActive(null);
}
