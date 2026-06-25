// ARCHIVED v0.9.0: in-game overlay replaced by browser dashboard.
// Sources retained for possible Steam-Deck variant or future revival;
// remove the #if false / #endif guards below to restore compilation.
#if false
#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.GameContent;

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
namespace PerformanceProfiler.UI.Overlay.Components;

/// <summary>
/// Tiny line/area chart over a value series. Three flavours covering the
/// overhaul's needs:
/// </summary>
/// <list type="bullet">
///   <item><see cref="DrawFilledArea"/> — filled area chart for frame time
///   or any single continuous metric.</item>
///   <item><see cref="DrawBars"/> — bar series for things that read better
///   as discrete columns (allocation per tick, where you want each bar's
///   independent magnitude visible).</item>
///   <item><see cref="DrawMarkers"/> — vertical tick marks at event
///   positions; used to show where spikes / stalls landed without a
///   continuous fill.</item>
/// </list>
///
/// <para>
/// All three use the magic pixel via <see cref="ProfilerTheme.FillRect"/>
/// so there's no SpriteBatch-state juggling. A 60-sample, 200 px-wide
/// sparkline draws as ~60 tiny FillRects — well under the per-frame
/// SpriteBatch budget.
/// </para>
/// </summary>
internal static class Sparkline
{
    /// <summary>
    /// Draws a filled area sparkline. The area below the line is filled
    /// with <paramref name="fillColor"/> at half alpha; the line itself
    /// runs in <paramref name="lineColor"/> 1 px tall.
    /// </summary>
    public static void DrawFilledArea(SpriteBatch sb, Rectangle area,
        IReadOnlyList<double> values, double yMax, Color fillColor, Color lineColor)
    {
        if (values.Count < 2 || yMax <= 0d) return;

        ProfilerTheme.FillRect(sb, area, ProfilerTheme.Panel);

        Texture2D pixel = TextureAssets.MagicPixel.Value;
        Color softFill = fillColor * 0.35f;

        int n = values.Count;
        float xStep = (float)area.Width / (n - 1);

        // For each sample, compute its bar (from y position down to bottom).
        for (int i = 0; i < n; i++)
        {
            double v = values[i];
            if (v < 0d) v = 0d;
            if (v > yMax) v = yMax;
            int barH = (int)(area.Height * (v / yMax));
            if (barH < 0) barH = 0;
            int barX = area.X + (int)(i * xStep);
            int barW = i == n - 1 ? 1 : System.Math.Max(1, (int)xStep);
            // Filled area below the value.
            sb.Draw(pixel, new Rectangle(barX, area.Bottom - barH, barW, barH), softFill);
            // Top line of the area.
            sb.Draw(pixel, new Rectangle(barX, area.Bottom - barH - 1, barW, 1), lineColor);
        }
    }

    /// <summary>
    /// Draws a bar-series sparkline. Each value gets its own vertical bar
    /// scaled to <paramref name="yMax"/>. Bars are 1 px wide minimum.
    /// </summary>
    public static void DrawBars(SpriteBatch sb, Rectangle area,
        IReadOnlyList<double> values, double yMax, Color barColor)
    {
        if (values.Count == 0 || yMax <= 0d) return;
        ProfilerTheme.FillRect(sb, area, ProfilerTheme.Panel);

        int n = values.Count;
        float xStep = (float)area.Width / n;
        for (int i = 0; i < n; i++)
        {
            double v = values[i];
            if (v < 0d) v = 0d;
            if (v > yMax) v = yMax;
            int barH = (int)(area.Height * (v / yMax));
            if (barH < 1) continue;
            int barX = area.X + (int)(i * xStep);
            int barW = System.Math.Max(1, (int)xStep - 1);
            ProfilerTheme.FillRect(sb, new Rectangle(barX, area.Bottom - barH, barW, barH), barColor);
        }
    }

    /// <summary>
    /// Draws vertical tick marks at the positions in <paramref name="markers"/>.
    /// Positions are 0..1 fractions across the area width. Used for spike /
    /// stall position markers alongside continuous sparklines.
    /// </summary>
    public static void DrawMarkers(SpriteBatch sb, Rectangle area,
        IReadOnlyList<double> markers, Color markerColor)
    {
        if (markers.Count == 0) return;
        ProfilerTheme.FillRect(sb, area, ProfilerTheme.Panel);

        for (int i = 0; i < markers.Count; i++)
        {
            double t = markers[i];
            if (t < 0d) t = 0d;
            if (t > 1d) t = 1d;
            int x = area.X + (int)(area.Width * t);
            ProfilerTheme.FillRect(sb, new Rectangle(x, area.Y, 1, area.Height), markerColor);
        }
    }
}

#endif
