// ARCHIVED v0.9.0: in-game overlay replaced by browser dashboard.
// Sources retained for possible Steam-Deck variant or future revival;
// remove the #if false / #endif guards below to restore compilation.
#if false
#nullable enable

using Terraria.UI;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.UI.Overlay;

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Data.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Profiling.Persistence;
using PerformanceProfiler.Profiling.Persistence.Records;
namespace PerformanceProfiler.UI;

/// <summary>
/// The F9 profiler overlay UIState. tModLoader's UI infrastructure attaches
/// this state when the keybind toggles. The state's sole job is to anchor a
/// single <see cref="OverlayPanel"/> at a default position; everything
/// inside the panel — header, tab strip, per-tick stats, profiler health,
/// the tabs themselves — lives in the <see cref="PerformanceProfiler.UI.Overlay"/>
/// namespace.
///
/// <para>
/// Adding a new tab does not touch this file. See
/// <see cref="TabRegistry"/> for the one line agents edit to plug in a
/// new <see cref="IOverlayTab"/> implementation.
/// </para>
/// </summary>
public sealed class ProfilerOverlay : UIState
{
    public override void OnInitialize()
    {
        // Pull stored preferences from the ModConfig if available — first
        // open of the overlay after a session start applies the user's
        // persisted mode / default tab choice.
        ProfilerConfig? cfg = Terraria.ModLoader.ModContent.GetInstance<ProfilerConfig>();
        if (cfg != null)
        {
            Overlay.OverlayState.Mode = cfg.DefaultMode;
            int idx = cfg.DefaultTabIndex;
            if (idx >= 0 && idx < Overlay.TabRegistry.Tabs.Count)
            {
                Overlay.OverlayState.ActiveTabIndex = idx;
            }
        }

        OverlayPanel panel = new OverlayPanel();
        panel.Left.Set(16f, 0f);
        panel.Top.Set(16f, 0f);
        panel.Width.Set(OverlayPanel.PanelWidth, 0f);
        panel.Height.Set(OverlayPanel.InitialHeight(HookInterceptor.ProfiledModNames.Length), 0f);
        Append(panel);
    }
}

#endif
