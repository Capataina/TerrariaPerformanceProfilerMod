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

    /// <summary>Default panel width post-overhaul. Stored once here; the chrome reads it at construction.</summary>
    public const float PanelWidthDefault = 880f;

    /// <summary>Minimum panel width after Phase 7's resize handle ships.</summary>
    public const float PanelWidthMin = 720f;

    /// <summary>Maximum panel width; chosen so even ultra-wide users keep the panel manageable.</summary>
    public const float PanelWidthMax = 1400f;

    /// <summary>Header strip height for the new chrome. Larger than the current 28 px to give the title room and improve readability.</summary>
    public const float HeaderHeightV2 = 32f;

    /// <summary>Tab strip height for the new chrome.</summary>
    public const float TabStripHeightV2 = 28f;

    /// <summary>Width of each tab pill in the new strip. Sized to fit "INSIGHTS" / "SUMMARY" comfortably at the new typography scale.</summary>
    public const float TabWidthV2 = 102f;

    /// <summary>Gap between adjacent tab pills.</summary>
    public const float TabGapV2 = 6f;

    /// <summary>Height of a single raised stat card. Carries one label + value at the new H2 scale.</summary>
    public const float StatCardHeight = 48f;

    /// <summary>Horizontal gap between adjacent stat cards.</summary>
    public const float StatCardGap = 6f;

    /// <summary>Outer horizontal padding inside the panel; cards and rows align to this.</summary>
    public const float PanelPaddingX = 12f;

    /// <summary>Outer vertical padding inside the panel.</summary>
    public const float PanelPaddingY = 8f;

    /// <summary>Height of the PROFILER HEALTH card in the new chrome.</summary>
    public const float ProfilerHealthCardHeight = 56f;

    /// <summary>Vertical gap between the chrome regions (header → tabs → stats → health).</summary>
    public const float ChromeRegionGap = 6f;

    /// <summary>Row heights at the new scale tier.</summary>
    public const float RowHeightV2 = 22f;
    public const float SubRowHeightV2 = 20f;
    public const float HookRowHeightV2 = 18f;

    /// <summary>Bar heights for the new HeatBar component.</summary>
    public const int BarH_ModV2 = 12;
    public const int BarH_CatV2 = 10;
    public const int BarH_HookV2 = 8;

    /// <summary>Card title strip height; sits on top of the card body.</summary>
    public const float CardTitleStripHeight = 18f;

    // ---- Typography scale tiers (see plan §6) --------------------------------

    /// <summary>H1: panel title, tab headers.</summary>
    public const float TextScaleH1 = 0.92f;

    /// <summary>H2: section titles, stat block values.</summary>
    public const float TextScaleH2 = 0.78f;

    /// <summary>Row: primary row text (mod name, hook name).</summary>
    public const float TextScaleRow = 0.72f;

    /// <summary>Body: secondary row text (annotations, units, captions).</summary>
    public const float TextScaleBody = 0.62f;
}
