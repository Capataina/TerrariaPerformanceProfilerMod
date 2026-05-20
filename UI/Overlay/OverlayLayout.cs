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

    // ---- UI overhaul Phase 0 additions ---------------------------------------
    //
    // New constants for the upcoming UI rewrite. Sit alongside the existing
    // values (which Phase 2's chrome rewrite will retire in favour of these).
    // Adding them now means the Phase 1 component library can be built and
    // tested against the final geometry without each component re-deriving
    // sizes from magic numbers.
    //
    // The header, tab strip, and row heights are larger than the current
    // values because the audit-driven feedback was "the UI is too small and
    // hard to read". Stat blocks become raised cards instead of a flat row
    // of label/value pairs. The PROFILER HEALTH band becomes its own card
    // so the self-footprint line we added in the relativity batch has room
    // to breathe.

    // The overlay supports two presentation modes, switched at runtime:
    //
    //   * DEFAULT — the "look at it while standing in your base" view. Big,
    //     comfortable to read, full visualisations (donut, sparklines).
    //     Designed for inspection sessions, not active play.
    //
    //   * COMPACT — the "turn it on and walk around with it" view. Roughly
    //     the size of the pre-overhaul overlay; usable during a boss fight
    //     or busy combat without occupying half the screen.
    //
    // Constants below are tagged Default vs Compact where they differ.
    // Compact mode mostly reuses the existing (pre-overhaul) constants.

    /// <summary>Default-mode panel width. The "at rest" view: generous, info-rich.</summary>
    public const float PanelWidthDefault = 1120f;

    /// <summary>Compact-mode panel width. The "walking around" HUD view.</summary>
    public const float PanelWidthCompact = 720f;

    /// <summary>Minimum panel width after Phase 7's resize handle ships.</summary>
    public const float PanelWidthMin = 640f;

    /// <summary>Maximum panel width; chosen so even ultra-wide users keep the panel manageable.</summary>
    public const float PanelWidthMax = 1600f;

    /// <summary>Header strip height for the default mode.</summary>
    public const float HeaderHeightV2 = 36f;

    /// <summary>Header strip height for compact mode (matches today's 28 px).</summary>
    public const float HeaderHeightCompact = 28f;

    /// <summary>Tab strip height for the default mode.</summary>
    public const float TabStripHeightV2 = 32f;

    /// <summary>Tab strip height for compact mode.</summary>
    public const float TabStripHeightCompact = 24f;

    /// <summary>Width of each tab pill in the default mode strip.</summary>
    public const float TabWidthV2 = 112f;

    /// <summary>Width of each tab pill in compact mode.</summary>
    public const float TabWidthCompact = 88f;

    /// <summary>Gap between adjacent tab pills (default).</summary>
    public const float TabGapV2 = 6f;

    /// <summary>Gap between adjacent tab pills (compact).</summary>
    public const float TabGapCompact = 4f;

    /// <summary>Height of a single raised stat card in default mode. Carries one label + value at the H2 scale.</summary>
    public const float StatCardHeight = 56f;

    /// <summary>Height of a stat card in compact mode.</summary>
    public const float StatCardHeightCompact = 40f;

    /// <summary>Horizontal gap between adjacent stat cards.</summary>
    public const float StatCardGap = 6f;

    /// <summary>Outer horizontal padding inside the panel; cards and rows align to this.</summary>
    public const float PanelPaddingX = 12f;

    /// <summary>Outer vertical padding inside the panel.</summary>
    public const float PanelPaddingY = 8f;

    /// <summary>Height of the PROFILER HEALTH card in default-mode chrome.</summary>
    public const float ProfilerHealthCardHeight = 64f;

    /// <summary>Height of the PROFILER HEALTH card in compact mode.</summary>
    public const float ProfilerHealthCardHeightCompact = 44f;

    /// <summary>Vertical gap between the chrome regions (header → tabs → stats → health).</summary>
    public const float ChromeRegionGap = 6f;

    /// <summary>Row heights for default mode — generous for at-rest inspection.</summary>
    public const float RowHeightV2 = 26f;
    public const float SubRowHeightV2 = 22f;
    public const float HookRowHeightV2 = 20f;

    /// <summary>Row heights for compact mode (close to today's values).</summary>
    public const float RowHeightCompact = 20f;
    public const float SubRowHeightCompact = 18f;
    public const float HookRowHeightCompact = 16f;

    /// <summary>Bar heights for HeatBar component in default mode.</summary>
    public const int BarH_ModV2 = 14;
    public const int BarH_CatV2 = 12;
    public const int BarH_HookV2 = 10;

    /// <summary>Bar heights in compact mode.</summary>
    public const int BarH_ModCompact = 10;
    public const int BarH_CatCompact = 8;
    public const int BarH_HookCompact = 6;

    /// <summary>Card title strip height; sits on top of the card body.</summary>
    public const float CardTitleStripHeight = 20f;

    /// <summary>Card title strip height in compact mode.</summary>
    public const float CardTitleStripHeightCompact = 16f;

    // ---- Typography scale tiers (see plan §6) --------------------------------
    //
    // Default-mode scales are bumped further than the original plan-draft —
    // the panel grew from 880 to 1120 so the text can grow with it. Compact
    // mode reverts to the pre-overhaul scales so the dense at-play view
    // doesn't waste vertical space.

    /// <summary>H1 (default): panel title, tab headers.</summary>
    public const float TextScaleH1 = 1.00f;

    /// <summary>H1 (compact).</summary>
    public const float TextScaleH1Compact = 0.82f;

    /// <summary>H2 (default): section titles, stat block values.</summary>
    public const float TextScaleH2 = 0.86f;

    /// <summary>H2 (compact).</summary>
    public const float TextScaleH2Compact = 0.72f;

    /// <summary>Row (default): primary row text (mod name, hook name).</summary>
    public const float TextScaleRow = 0.78f;

    /// <summary>Row (compact).</summary>
    public const float TextScaleRowCompact = 0.66f;

    /// <summary>Body (default): secondary row text (annotations, units, captions).</summary>
    public const float TextScaleBody = 0.66f;

    /// <summary>Body (compact).</summary>
    public const float TextScaleBodyCompact = 0.55f;
}
