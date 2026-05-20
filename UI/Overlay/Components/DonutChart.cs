#nullable enable

using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.GameContent;

namespace PerformanceProfiler.UI.Overlay.Components;

/// <summary>
/// One mod in the impact-share visualisation. The slice carries the total
/// composite value plus its breakdown across the three axes — the city
/// skyline renderer draws each axis as its own stacked segment within the
/// mod's bar.
/// </summary>
internal struct DonutSlice
{
    /// <summary>Total composite value (CpuMs + AllocMsEq + SpikeMs).</summary>
    public double Value;

    /// <summary>Mod-identity colour from <see cref="ProfilerTheme.ModPalette"/>.</summary>
    public Color SliceColor;

    /// <summary>The dominant-axis hue for the slice. Stays for compat; the skyline uses the per-axis hues directly.</summary>
    public Color DominantHue;

    /// <summary>The mod's name, truncated. Drawn under its bar in the skyline.</summary>
    public string? Label;

    /// <summary>CPU contribution to <see cref="Value"/>, in raw ms.</summary>
    public double CpuMs;

    /// <summary>Allocation contribution to <see cref="Value"/>, expressed in ms-equivalent.</summary>
    public double AllocMsEq;

    /// <summary>Spike contribution to <see cref="Value"/>, in raw ms.</summary>
    public double SpikeMs;
}

/// <summary>
/// Impact-share visualiser, rendered as a city skyline. Each mod is one
/// vertical "building"; the building's height is proportional to its
/// composite impact; the building is split into three colour-coded floors
/// (CPU blue / ALLOC purple / SPIKE amber) sized by each axis's
/// contribution. Mod-identity colour reads as a thin coloured base under
/// each tower so the eye can still trace "which tower is which mod".
///
/// <para>
/// Why this shape: tried a rotated-wedge donut first; the wedge geometry
/// went off-screen on tested hardware and produced screen-spanning blue
/// streaks (Caner playtest 2026-05-20). Stacked horizontal bar was the
/// emergency fix; this is the proper second iteration — visually richer,
/// shows the three-axis breakdown directly, zero rotation primitives.
/// </para>
///
/// <para>
/// Implementation is pure <c>FillRect</c> + <see cref="SpriteBatch.Draw"/>
/// on the magic pixel. No rotation, no asset shipping, no
/// <c>DrawUserPrimitives</c> state juggling.
/// </para>
/// </summary>
internal static class DonutChart
{
    /// <summary>Maximum number of bars rendered side-by-side.</summary>
    public const int MaxBars = 12;

    /// <summary>
    /// Draws the impact-share visualisation inside <paramref name="area"/>.
    /// Slices are assumed pre-sorted in descending Value order — the
    /// tallest building lands on the left.
    /// </summary>
    public static void Draw(SpriteBatch sb, Rectangle area, IReadOnlyList<DonutSlice> slices)
    {
        if (slices.Count == 0 || area.Width < 20 || area.Height < 40) return;

        // Background.
        ProfilerTheme.FillRect(sb, area, ProfilerTheme.Panel);

        int barCount = System.Math.Min(MaxBars, slices.Count);
        if (barCount <= 0) return;

        // Find the maximum composite value to scale all bar heights against.
        double maxValue = 0d;
        for (int i = 0; i < barCount; i++)
            if (slices[i].Value > maxValue) maxValue = slices[i].Value;
        if (maxValue <= 0d) return;

        // Layout: leave room at the bottom for the mod-name label, room at
        // the top for the absolute value text. Bars fill what's between.
        const int LabelHeight = 14;
        const int ValueLabelHeight = 12;
        const int TopPadding = 6;
        const int BottomPadding = 4;

        int barAreaTop = area.Y + TopPadding + ValueLabelHeight;
        int barAreaBottom = area.Bottom - BottomPadding - LabelHeight;
        int barAreaHeight = barAreaBottom - barAreaTop;
        if (barAreaHeight < 20) return;

        int gutter = 6;
        int barSpan = area.Width - gutter * (barCount + 1);
        int barWidth = barSpan / barCount;
        if (barWidth < 8) barWidth = 8;

        // Reference grid lines: 25%, 50%, 75% of the max — gives the eye
        // a sense of scale without dominating the chart.
        for (int g = 1; g <= 3; g++)
        {
            int gy = barAreaTop + (barAreaHeight * g) / 4;
            ProfilerTheme.FillRect(sb,
                new Rectangle(area.X + 4, gy, area.Width - 8, 1),
                ProfilerTheme.Border);
        }

        // Drawing each tower.
        for (int i = 0; i < barCount; i++)
        {
            DonutSlice s = slices[i];
            int barX = area.X + gutter + i * (barWidth + gutter);

            // Compute the per-axis heights of this tower. Each axis's height
            // is proportional to its share of the maximum composite — so the
            // top of the tallest tower aligns with the chart top, and every
            // other tower scales relative to that.
            double total = s.CpuMs + s.AllocMsEq + s.SpikeMs;
            if (total <= 0d) total = s.Value;
            if (total <= 0d) continue;

            int towerHeight = (int)(barAreaHeight * (s.Value / maxValue));
            if (towerHeight < 4) towerHeight = 4;

            int cpuH   = (int)(towerHeight * (s.CpuMs   / total));
            int allocH = (int)(towerHeight * (s.AllocMsEq / total));
            int spikeH = towerHeight - cpuH - allocH;
            if (spikeH < 0) spikeH = 0;

            int towerTop = barAreaBottom - towerHeight;

            // Tower segments, bottom-up: spike (amber), alloc (purple), cpu (blue).
            int segY = barAreaBottom;
            if (spikeH > 0)
            {
                segY -= spikeH;
                ProfilerTheme.FillRect(sb, new Rectangle(barX, segY, barWidth, spikeH), ProfilerTheme.SpikeDominant);
            }
            if (allocH > 0)
            {
                segY -= allocH;
                ProfilerTheme.FillRect(sb, new Rectangle(barX, segY, barWidth, allocH), ProfilerTheme.AllocDominant);
            }
            if (cpuH > 0)
            {
                segY -= cpuH;
                ProfilerTheme.FillRect(sb, new Rectangle(barX, segY, barWidth, cpuH), ProfilerTheme.CpuDominant);
            }

            // Thin identity-colour base under each tower so the eye can trace
            // which mod each bar represents even at a glance.
            ProfilerTheme.FillRect(sb,
                new Rectangle(barX, barAreaBottom + 1, barWidth, 2),
                s.SliceColor);

            // Top-of-tower value label.
            string valStr = s.Value >= 10d ? $"{s.Value:F0}"
                          : s.Value >= 1d ? $"{s.Value:F1}"
                          : $"{s.Value:F2}";
            int valX = barX + (barWidth - valStr.Length * 5) / 2;
            OverlayDraw.Text(sb, valStr,
                new Vector2(valX, towerTop - 12),
                ProfilerTheme.Text, 0.55f);

            // Bottom mod-name label, truncated to fit under the bar.
            if (s.Label != null)
            {
                int maxChars = System.Math.Max(2, barWidth / 6);
                string label = s.Label.Length <= maxChars
                    ? s.Label
                    : s.Label.Substring(0, maxChars);
                int labelX = barX + (barWidth - label.Length * 5) / 2;
                OverlayDraw.Text(sb, label,
                    new Vector2(labelX, barAreaBottom + 6),
                    ProfilerTheme.TextMuted, 0.55f);
            }
        }
    }

    /// <summary>
    /// Convenience overload from the previous donut layout — converts a
    /// notional "centre + radii" into a rectangular skyline area.
    /// </summary>
    public static void Draw(SpriteBatch sb, Vector2 centre, float outerR, float innerR, IReadOnlyList<DonutSlice> slices)
    {
        int barH = (int)(outerR * 1.6f);
        int barW = (int)(outerR * 2.8f);
        Rectangle area = new Rectangle(
            (int)centre.X - barW / 2,
            (int)centre.Y - barH / 2,
            barW,
            barH);
        Draw(sb, area, slices);
    }
}
