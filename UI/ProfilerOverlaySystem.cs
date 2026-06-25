#nullable enable

using Terraria.ModLoader;

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Persistence.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Persistence;
using PerformanceProfiler.Persistence.Records;
namespace PerformanceProfiler.UI;

/// <summary>
/// Owns the F9 keybind that opens the browser dashboard.
///
/// <para>
/// v0.9.0 archived the in-game overlay entirely. The browser dashboard
/// (served by <see cref="Web.Server.DashboardHttpServer"/> on loopback) is the only
/// rendered surface this mod has. The historical overlay code lives under
/// <c>UI/Overlay/</c> but nothing instantiates it any more — kept on disk
/// so a future variant (Steam Deck handheld mode, post-1.0 secondary
/// overlay, etc.) can resurrect it without re-implementing from scratch.
/// </para>
///
/// <para>
/// Pre-v0.9 had two keybinds: F9 to toggle the in-game overlay and F10 to
/// launch the browser. With the overlay archived, F9 takes over the
/// dashboard-launch role — single keybind, single mental model.
/// </para>
/// </summary>
public sealed class ProfilerOverlaySystem : ModSystem
{
    /// <summary>The F9 keybind. Opens the browser dashboard via <see cref="PerformanceProfiler"/>'s handler.</summary>
    public static ModKeybind? DashboardKeybind { get; private set; }

    public override void OnModLoad()
    {
        DashboardKeybind = KeybindLoader.RegisterKeybind(Mod, "OpenDashboard", "F9");
    }

    public override void OnModUnload()
    {
        DashboardKeybind = null;
    }
}
