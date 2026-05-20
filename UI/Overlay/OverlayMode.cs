#nullable enable

namespace PerformanceProfiler.UI.Overlay;

/// <summary>
/// The two presentation modes the overlay supports. Default is the
/// "stand-in-the-base-and-read-it" view — generous, info-rich. Compact is
/// the "walk-around-with-it-during-a-boss" view — denser, HUD-style.
/// </summary>
public enum OverlayMode : byte
{
    /// <summary>Generous 1120 px panel; full charts; larger typography. The at-rest inspection view.</summary>
    Default = 0,

    /// <summary>Tight 720 px panel; smaller charts; denser typography. The during-combat HUD view.</summary>
    Compact = 1,
}

/// <summary>
/// Active-mode accessors that resolve <see cref="OverlayLayout"/>'s
/// dual-mode constants into a single value per call. Components read these
/// instead of querying <see cref="OverlayState.Mode"/> directly, so
/// switching mode is a single state flip rather than a per-component
/// branch.
///
/// <para>
/// Each accessor is a tiny conditional — no allocation, no cache, no
/// staleness. The compiler inlines them at the call site.
/// </para>
/// </summary>
internal static class OverlayLayoutCurrent
{
    private static bool IsCompact => OverlayState.Mode == OverlayMode.Compact;

    // ---- Panel ---------------------------------------------------------------
    public static float PanelWidth => IsCompact ? OverlayLayout.PanelWidthCompact : OverlayLayout.PanelWidthDefault;
    public static float PanelPaddingX => OverlayLayout.PanelPaddingX;
    public static float PanelPaddingY => OverlayLayout.PanelPaddingY;

    // ---- Chrome regions ------------------------------------------------------
    public static float HeaderHeight => IsCompact ? OverlayLayout.HeaderHeightCompact : OverlayLayout.HeaderHeightV2;
    public static float TabStripHeight => IsCompact ? OverlayLayout.TabStripHeightCompact : OverlayLayout.TabStripHeightV2;
    public static float TabWidth => IsCompact ? OverlayLayout.TabWidthCompact : OverlayLayout.TabWidthV2;
    public static float TabGap => IsCompact ? OverlayLayout.TabGapCompact : OverlayLayout.TabGapV2;
    public static float StatCardHeight => IsCompact ? OverlayLayout.StatCardHeightCompact : OverlayLayout.StatCardHeight;
    public static float StatCardGap => OverlayLayout.StatCardGap;
    public static float ProfilerHealthCardHeight => IsCompact ? OverlayLayout.ProfilerHealthCardHeightCompact : OverlayLayout.ProfilerHealthCardHeight;
    public static float ChromeRegionGap => OverlayLayout.ChromeRegionGap;
    public static float CardTitleStripHeight => IsCompact ? OverlayLayout.CardTitleStripHeightCompact : OverlayLayout.CardTitleStripHeight;

    // ---- Rows ----------------------------------------------------------------
    public static float RowHeight => IsCompact ? OverlayLayout.RowHeightCompact : OverlayLayout.RowHeightV2;
    public static float SubRowHeight => IsCompact ? OverlayLayout.SubRowHeightCompact : OverlayLayout.SubRowHeightV2;
    public static float HookRowHeight => IsCompact ? OverlayLayout.HookRowHeightCompact : OverlayLayout.HookRowHeightV2;
    public static int BarH_Mod => IsCompact ? OverlayLayout.BarH_ModCompact : OverlayLayout.BarH_ModV2;
    public static int BarH_Cat => IsCompact ? OverlayLayout.BarH_CatCompact : OverlayLayout.BarH_CatV2;
    public static int BarH_Hook => IsCompact ? OverlayLayout.BarH_HookCompact : OverlayLayout.BarH_HookV2;

    // ---- Typography ---------------------------------------------------------
    public static float TextScaleH1 => IsCompact ? OverlayLayout.TextScaleH1Compact : OverlayLayout.TextScaleH1;
    public static float TextScaleH2 => IsCompact ? OverlayLayout.TextScaleH2Compact : OverlayLayout.TextScaleH2;
    public static float TextScaleRow => IsCompact ? OverlayLayout.TextScaleRowCompact : OverlayLayout.TextScaleRow;
    public static float TextScaleBody => IsCompact ? OverlayLayout.TextScaleBodyCompact : OverlayLayout.TextScaleBody;

    /// <summary>
    /// Total chrome height for the active mode. Tab content starts here.
    /// </summary>
    public static float ChromeHeight => PanelPaddingY
        + HeaderHeight + ChromeRegionGap
        + TabStripHeight + ChromeRegionGap
        + StatCardHeight + ChromeRegionGap
        + ProfilerHealthCardHeight + ChromeRegionGap;
}
