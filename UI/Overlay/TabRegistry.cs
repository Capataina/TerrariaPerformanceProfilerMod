#nullable enable

using System.Collections.Generic;
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
        new OverviewTab(),
        new TreeTab(),
        new SpikesTab(),
        // Two parallel agents add their tabs here, one per agent:
        //     new EventsTab(),
        //     new InsightsTab(),
    };

    /// <summary>The tab currently shown, derived from <see cref="OverlayState.ActiveTabIndex"/>.</summary>
    public static IOverlayTab Active
    {
        get
        {
            int idx = OverlayState.ActiveTabIndex;
            if (idx < 0 || idx >= Tabs.Count) idx = 0;
            return Tabs[idx];
        }
    }
}
