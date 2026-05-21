// ARCHIVED v0.9.0: in-game overlay replaced by browser dashboard.
// Sources retained for possible Steam-Deck variant or future revival;
// remove the #if false / #endif guards below to restore compilation.
#if false
#nullable enable

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PerformanceProfiler.UI.Overlay.Components;

/// <summary>
/// Horizontal heat-coloured progress bar. The dim track in <see cref="ProfilerTheme.Border"/>
/// sets the maximum; the fill spans <c>value / max</c> of the width and uses
/// <see cref="ProfilerTheme.CostColor"/> (the 5-stop heat ramp) on the
/// fill colour by the fraction of <paramref name="value"/> against the
/// caller-supplied severity scale.
///
/// <para>
/// Two separate fractions because they answer different questions:
/// </para>
/// <list type="bullet">
///   <item><c>value/max</c> drives the FILL WIDTH — "how much of the bar is
///   filled" — which is comparison against the other rows in this view.</item>
///   <item><c>severityFraction</c> drives the FILL COLOUR — "how concerning is
///   this value" — which can be computed against a different reference
///   (e.g. baseline frame time) so the colour tells you about the absolute
///   cost while the bar length tells you about the relative ranking.</item>
/// </list>
///
/// <para>
/// When the caller doesn't need this distinction they pass
/// <paramref name="severityFraction"/> as -1 and the colour falls back to
/// <c>value/max</c>.
/// </para>
/// </summary>
internal static class HeatBar
{
    /// <summary>
    /// Draws a heat-coloured bar inside <paramref name="area"/>.
    /// </summary>
    /// <param name="sb">Active SpriteBatch.</param>
    /// <param name="area">Bar bounds; height determines the bar thickness.</param>
    /// <param name="value">Current value driving the fill width.</param>
    /// <param name="max">Maximum value the bar tracks; values above clamp to full width.</param>
    /// <param name="severityFraction">[0..1] driving the FILL COLOUR; pass -1 to derive from value/max.</param>
    public static void Draw(SpriteBatch sb, Rectangle area, double value, double max, double severityFraction = -1d)
    {
        // Track.
        ProfilerTheme.FillRect(sb, area, ProfilerTheme.Border);

        if (max <= 0d || value <= 0d) return;

        double widthFraction = value / max;
        if (widthFraction < 0d) widthFraction = 0d;
        else if (widthFraction > 1d) widthFraction = 1d;

        double colorFraction = severityFraction >= 0d ? severityFraction : widthFraction;

        int fillWidth = (int)(area.Width * widthFraction);
        if (fillWidth <= 0) return;

        Color fill = ProfilerTheme.CostColor(colorFraction);
        ProfilerTheme.FillRect(sb, new Rectangle(area.X, area.Y, fillWidth, area.Height), fill);
    }

    /// <summary>
    /// Variant that takes an explicit colour instead of deriving from the heat
    /// ramp. Used by mod-identity bars where the colour conveys WHICH mod, not
    /// how heavy it is.
    /// </summary>
    public static void DrawSolid(SpriteBatch sb, Rectangle area, double value, double max, Color fillColor)
    {
        ProfilerTheme.FillRect(sb, area, ProfilerTheme.Border);
        if (max <= 0d || value <= 0d) return;
        double widthFraction = value / max;
        if (widthFraction < 0d) widthFraction = 0d;
        else if (widthFraction > 1d) widthFraction = 1d;
        int fillWidth = (int)(area.Width * widthFraction);
        if (fillWidth <= 0) return;
        ProfilerTheme.FillRect(sb, new Rectangle(area.X, area.Y, fillWidth, area.Height), fillColor);
    }
}

#endif
