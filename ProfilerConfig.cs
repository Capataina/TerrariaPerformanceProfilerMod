#nullable enable

using System.ComponentModel;
using Terraria.ModLoader.Config;
using PerformanceProfiler.UI.Overlay;

namespace PerformanceProfiler;

/// <summary>
/// Player-facing client-side configuration. Surfaced in the standard
/// tModLoader Mod Configuration menu; reads/writes a JSON file under the
/// platform config dir so user preferences persist across sessions.
///
/// <para>
/// Kept narrow on purpose. The mod owns its data model on its own; this
/// file is purely for VISIBLE preferences the user might want to override:
/// presentation mode, default tab on overlay open, manual panel-width
/// override. Detector thresholds and engine knobs stay code-level — the
/// philosophy is that the profiler should be smart enough to not need user
/// tuning of its measurement behaviour.
/// </para>
///
/// <para>
/// The config is read on every overlay frame via
/// <see cref="ModContent.GetInstance{T}"/>; tModLoader handles the
/// persistence and the menu plumbing.
/// </para>
/// </summary>
[BackgroundColor(13, 17, 23)]
public sealed class ProfilerConfig : ModConfig
{
    public override ConfigScope Mode => ConfigScope.ClientSide;

    /// <summary>
    /// The overlay's default presentation mode at session start. The user can
    /// still flip via the DEFAULT/COMPACT pill in the header; the choice
    /// reverts here on the next game launch.
    /// </summary>
    [DefaultValue(OverlayMode.Default)]
    [Label("Default overlay mode")]
    [Tooltip("Default = generous \"stand-in-base\" view (1120 px). Compact = tight HUD view for active combat (720 px).")]
    public OverlayMode DefaultMode { get; set; } = OverlayMode.Default;

    /// <summary>
    /// Index of the tab the overlay opens to. 0 = SUMMARY, 1 = TREE,
    /// 2 = LAG, 3 = EVENTS, 4 = INSIGHTS, 5 = SELF. Out-of-range values
    /// fall back to 0.
    /// </summary>
    [DefaultValue(0)]
    [Range(0, 5)]
    [Label("Default tab on open")]
    [Tooltip("0=SUMMARY, 1=TREE, 2=LAG, 3=EVENTS, 4=INSIGHTS, 5=SELF.")]
    public int DefaultTabIndex { get; set; } = 0;

    /// <summary>
    /// Manual panel width override; <c>0</c> means "use the active mode's
    /// default". Honoured when the resize handle is used; persists the
    /// dragged size so it survives across sessions.
    /// </summary>
    [DefaultValue(0)]
    [Range(0, 1600)]
    [Label("Panel width override (px, 0 = mode default)")]
    public int PanelWidthOverride { get; set; } = 0;

    /// <summary>
    /// Called by tModLoader whenever the user changes a value in the config
    /// menu. Pushes mode + default tab into <see cref="OverlayState"/> so the
    /// open overlay reflects the new preference immediately.
    /// </summary>
    public override void OnChanged()
    {
        OverlayState.Mode = DefaultMode;
        if (DefaultTabIndex >= 0)
        {
            OverlayState.ActiveTabIndex = DefaultTabIndex;
        }
    }
}
