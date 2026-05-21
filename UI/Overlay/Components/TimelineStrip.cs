// ARCHIVED v0.9.0: in-game overlay replaced by browser dashboard.
// Sources retained for possible Steam-Deck variant or future revival;
// remove the #if false / #endif guards below to restore compilation.
#if false
#nullable enable

using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PerformanceProfiler.UI.Overlay.Components;

/// <summary>
/// One event positioned on a <see cref="TimelineStrip"/>. Position is a
/// fraction in [0, 1] along the strip; colour is severity-driven; size is
/// derived from severity automatically.
/// </summary>
internal struct TimelineMark
{
    /// <summary>Position along the strip in [0, 1] from oldest to newest.</summary>
    public double Position;
    /// <summary>Marker colour, usually severity-derived.</summary>
    public Color Color;
    /// <summary>Visual weight in [0, 1]; bigger means a thicker / taller mark.</summary>
    public double Weight;
}

/// <summary>
/// Horizontal time axis with positioned event markers. Used at the top of
/// the LAG tab to show where in the session each spike/stall landed.
///
/// <para>
/// Draws a thin baseline, tick marks at quarter divisions, and a marker per
/// event. Each marker is a vertical line whose height is keyed off the
/// event's severity weight — Freeze stalls draw full strip height, Minor
/// stalls draw 30%.
/// </para>
/// </summary>
internal static class TimelineStrip
{
    /// <summary>
    /// Draws the strip inside <paramref name="area"/>. The baseline runs
    /// horizontally through the vertical centre; markers extend up and
    /// down from it proportional to their <see cref="TimelineMark.Weight"/>.
    /// </summary>
    public static void Draw(SpriteBatch sb, Rectangle area, IReadOnlyList<TimelineMark> marks)
    {
        ProfilerTheme.FillRect(sb, area, ProfilerTheme.Panel);

        // Baseline.
        int midY = area.Y + area.Height / 2;
        ProfilerTheme.FillRect(sb, new Rectangle(area.X, midY, area.Width, 1), ProfilerTheme.Border);

        // Quarter-division ticks (faint).
        for (int q = 1; q < 4; q++)
        {
            int x = area.X + (area.Width * q) / 4;
            ProfilerTheme.FillRect(sb, new Rectangle(x, midY - 2, 1, 5), ProfilerTheme.TextDim);
        }

        // Event markers.
        int halfH = area.Height / 2;
        for (int i = 0; i < marks.Count; i++)
        {
            double t = marks[i].Position;
            if (t < 0d) t = 0d;
            if (t > 1d) t = 1d;
            double weight = marks[i].Weight;
            if (weight < 0.1d) weight = 0.1d;
            if (weight > 1d) weight = 1d;
            int x = area.X + (int)(area.Width * t);
            int markH = (int)(halfH * weight);
            ProfilerTheme.FillRect(sb, new Rectangle(x, midY - markH, 1, markH * 2), marks[i].Color);
        }
    }
}

#endif
