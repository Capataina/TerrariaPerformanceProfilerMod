#nullable enable

namespace PerformanceProfiler.UI.Overlay;

/// <summary>
/// Shared visual constants used by the chrome (<see cref="OverlayPanel"/>)
/// and by every tab implementing <see cref="IOverlayTab"/>. Centralized so
/// changing one offset doesn't require hunting through the codebase, and so
/// new tabs read the same vocabulary as the existing ones.
///
/// <para>
/// The panel layout from top to bottom:
/// </para>
/// <list type="bullet">
///   <item><b>0 to HeaderHeight</b> — title strip with the NOW/30S AVG and
///         LIVE/PAUSED toggles. Drag region.</item>
///   <item><b>HeaderHeight to +TabStripHeight</b> — tab strip with the
///         CPU/MEM/BOTH metric pill on the right edge.</item>
///   <item><b>+StatStartY (from below tab strip)</b> — three lines of
///         per-tick stats (tick / avg 30s / gc / entities, plus tick#).</item>
///   <item><b>HealthTopOffset</b> — PROFILER HEALTH section: coverage,
///         backend label, in Parallel mode the divergence strip.</item>
///   <item><b>DividerOffset</b> — horizontal rule with accent bar, then the
///         tab-specific content header.</item>
///   <item><b>RowsTopOffset</b> — first row of tab content begins here.</item>
/// </list>
/// </summary>
internal static class OverlayLayout
{
    public const float PanelWidth      = 640f;

    public const float HeaderHeight    = 28f;
    public const float TabStripHeight  = 22f;
    public const float MetricToggleX   = 426f;
    public const float PauseToggleX    = 506f;
    public const float ToggleY         = 6f;
    public const float MetricToggleW   = 70f;
    public const float PauseToggleW    = 90f;
    public const float ToggleHeight    = 16f;
    public const float StatStartY      = 12f;
    public const float StatGap         = 22f;
    public const float HealthTopOffset = 122f;
    public const float DividerOffset   = 170f;
    public const float RowsTopOffset   = 194f;

    public const float RowHeight       = 18f;
    public const float SubRowHeight    = 16f;
    public const float HookRowHeight   = 14f;

    public const int   MaxModRows      = 12;
    public const int   BarH_Mod        = 10;
    public const int   BarH_Cat        = 8;
    public const int   BarH_Hook       = 6;

    public const float ScrollTrackW    = 4f;
    public const float ScrollTrackGap  = 8f;

    public const float TabFirstX       = 14f;
    public const float TabWidth        = 92f;
    public const float TabGap          = 4f;

    /// <summary>
    /// Height of the panel content above any tab's rows -- header, tab strip,
    /// stats, profiler health. Tabs add their own content height on top via
    /// <see cref="IOverlayTab.MeasurePanelHeight"/>.
    /// </summary>
    public const float ChromeHeight    = RowsTopOffset;
}
