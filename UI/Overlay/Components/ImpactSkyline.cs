// ARCHIVED v0.9.0: in-game overlay replaced by browser dashboard.
// Sources retained for possible Steam-Deck variant or future revival;
// remove the #if false / #endif guards below to restore compilation.
#if false
#nullable enable

using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Data.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Profiling.Persistence;
using PerformanceProfiler.Profiling.Persistence.Records;
namespace PerformanceProfiler.UI.Overlay.Components;

/// <summary>
/// Three-axis "city skyline" — each mod is a vertical tower split into CPU,
/// alloc, and spike colour-coded segments. The tower's HEIGHT scales with
/// the mod's composite share of the tallest contributor; identity-colour
/// base strips run beneath each tower so the eye can trace which tower is
/// which mod.
///
/// <para>
/// <b>Designed for top-N detail, NOT total-population share.</b> Skyline
/// stops being legible at ~15+ towers — each bar gets too thin to read
/// labels. For total-population share (any N from 2 to 200+ mods) use
/// <see cref="DonutChart"/>; that's what the SUMMARY tab's IMPACT card
/// uses. This component sits in the toolbox for future surfaces that
/// genuinely want top-N detail: a "biggest spenders" deep-dive,
/// the SELF tab's projection-vs-current view, or a side-panel that opens
/// when the user clicks a donut slice.
/// </para>
///
/// <para>
/// Renders entirely via <c>FillRect</c> on the magic pixel — no rotation,
/// no asset shipping, no <c>DrawUserPrimitives</c> state juggling.
/// </para>
///
/// <para>
/// Consumes the same <see cref="DonutSlice"/> type as
/// <see cref="DonutChart"/> — the slice's <c>CpuMs</c>, <c>AllocMsEq</c>,
/// and <c>SpikeMs</c> fields drive the three internal bar segments.
/// </para>
/// </summary>
internal static class ImpactSkyline
{
    /// <summary>Maximum number of towers; tail is collapsed into "others" by callers.</summary>
    public const int MaxBars = 12;

    /// <summary>
    /// Draws the skyline inside <paramref name="area"/>. Slices should be
    /// pre-sorted in descending Value order so the tallest tower lands on
    /// the left.
    /// </summary>
    public static void Draw(SpriteBatch sb, Rectangle area, IReadOnlyList<DonutSlice> slices)
    {
        if (slices.Count == 0 || area.Width < 20 || area.Height < 40) return;
        ProfilerTheme.FillRect(sb, area, ProfilerTheme.Panel);

        int barCount = System.Math.Min(MaxBars, slices.Count);
        if (barCount <= 0) return;

        double maxValue = 0d;
        for (int i = 0; i < barCount; i++)
            if (slices[i].Value > maxValue) maxValue = slices[i].Value;
        if (maxValue <= 0d) return;

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

        // Reference grid lines at 25/50/75% of the max.
        for (int g = 1; g <= 3; g++)
        {
            int gy = barAreaTop + (barAreaHeight * g) / 4;
            ProfilerTheme.FillRect(sb,
                new Rectangle(area.X + 4, gy, area.Width - 8, 1),
                ProfilerTheme.Border);
        }

        for (int i = 0; i < barCount; i++)
        {
            DonutSlice s = slices[i];
            int barX = area.X + gutter + i * (barWidth + gutter);

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

            ProfilerTheme.FillRect(sb,
                new Rectangle(barX, barAreaBottom + 1, barWidth, 2),
                s.SliceColor);

            string valStr = s.Value >= 10d ? $"{s.Value:F0}"
                          : s.Value >= 1d ? $"{s.Value:F1}"
                          : $"{s.Value:F2}";
            int valX = barX + (barWidth - valStr.Length * 5) / 2;
            OverlayDraw.Text(sb, valStr,
                new Vector2(valX, towerTop - 12),
                ProfilerTheme.Text, 0.55f);

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
}

#endif
