#nullable enable

using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PerformanceProfiler.UI.Overlay.Components;

/// <summary>
/// One sector of an impact-share visualisation. Value drives sector arc
/// length; identity colour drives slice hue (from
/// <see cref="ProfilerTheme.ModColor"/>); dominant colour optionally tints
/// the inner edge of the slice to encode which axis (cpu / alloc / spike)
/// the mod is most heavy on.
/// </summary>
internal struct DonutSlice
{
    public double Value;
    public Color SliceColor;
    public Color DominantHue;
    public string? Label;
}

/// <summary>
/// Impact-share visualiser. Originally drawn as a donut (sectored ring), but
/// the rotation-based wedge rendering produced off-screen artifacts on tested
/// hardware (long parallel diagonal lines, see Caner's screenshot
/// 2026-05-20). Replaced with a SAFE stacked-horizontal-bar layout: zero
/// rotation, only <c>FillRect</c> primitives, identical information density.
///
/// <para>
/// Two-band per slice:
/// </para>
/// <list type="bullet">
///   <item>Top 65% of the bar height — identity colour (which mod)</item>
///   <item>Bottom 35% — dominant-axis tint (cpu blue / alloc purple /
///         spike amber) so a glance shows WHY each mod is heavy.</item>
/// </list>
///
/// <para>
/// We keep the type name <c>DonutChart</c> so call sites don't churn; the
/// header docstring of any future tab makes the actual shape clear.
/// </para>
/// </summary>
internal static class DonutChart
{
    /// <summary>
    /// Renders the share bar inside the supplied area. Slices are drawn
    /// left-to-right in the order supplied; values normalised against the
    /// sum of all slice values. Zero-total input is a no-op.
    /// </summary>
    public static void Draw(SpriteBatch sb, Rectangle area, IReadOnlyList<DonutSlice> slices)
    {
        if (slices.Count == 0 || area.Width < 4 || area.Height < 8) return;

        double total = 0d;
        for (int i = 0; i < slices.Count; i++) total += slices[i].Value;
        if (total <= 0d) return;

        // Track background.
        ProfilerTheme.FillRect(sb, area, ProfilerTheme.Border);

        // Two-band split (identity on top, dominant tint on bottom).
        int identityH = (int)(area.Height * 0.65f);
        int dominantH = area.Height - identityH;
        int identityY = area.Y;
        int dominantY = area.Y + identityH;

        // Track running x position; ensure we always exactly fill the area
        // by snapping the last slice to area.Right.
        float cursor = 0f;
        int x = area.X;
        for (int i = 0; i < slices.Count; i++)
        {
            DonutSlice s = slices[i];
            double frac = s.Value / total;
            cursor += (float)(area.Width * frac);
            int rightX = i == slices.Count - 1
                ? area.Right
                : area.X + (int)cursor;
            int w = rightX - x;
            if (w <= 0) { x = rightX; continue; }

            // Identity band.
            ProfilerTheme.FillRect(sb, new Rectangle(x, identityY, w, identityH), s.SliceColor);
            // Dominant tint band.
            ProfilerTheme.FillRect(sb, new Rectangle(x, dominantY, w, dominantH), s.DominantHue);
            // Thin vertical divider between slices.
            if (i > 0)
                ProfilerTheme.FillRect(sb, new Rectangle(x, area.Y, 1, area.Height), ProfilerTheme.Panel);

            x = rightX;
        }
    }

    /// <summary>
    /// Convenience overload that draws the bar centred inside a rectangular
    /// area roughly proportional to a donut's outer bounding box. Used by the
    /// SUMMARY tab where the donut centre stat is rendered separately.
    /// </summary>
    public static void Draw(SpriteBatch sb, Vector2 centre, float outerR, float innerR, IReadOnlyList<DonutSlice> slices)
    {
        // Convert the donut bounding box into a horizontal-bar area centred at the same point.
        int barH = (int)(outerR * 0.45f);
        int barW = (int)(outerR * 2.4f);
        Rectangle area = new Rectangle(
            (int)centre.X - barW / 2,
            (int)centre.Y - barH / 2,
            barW,
            barH);
        Draw(sb, area, slices);
    }
}
