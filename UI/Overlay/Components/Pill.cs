// ARCHIVED v0.9.0: in-game overlay replaced by browser dashboard.
// Sources retained for possible Steam-Deck variant or future revival;
// remove the #if false / #endif guards below to restore compilation.
#if false
#nullable enable

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
/// Modernised pill primitive — the replacement for <see cref="OverlayDraw.Toggle"/>
/// in the overhauled chrome. Same visual logic (active = blue-tinted fill +
/// accent border, inactive = panel-fill + muted border) but laid out at the
/// new mode-aware sizes and with an optional leading status dot.
/// </summary>
internal static class Pill
{
    /// <summary>
    /// Renders a pill in the supplied rectangle.
    /// </summary>
    public static void Draw(SpriteBatch sb, Rectangle area, string label, bool active, bool leadingDot = false)
    {
        Color fill = active ? new Color(25, 40, 60) : ProfilerTheme.Panel;
        Color border = active ? ProfilerTheme.Accent : ProfilerTheme.Border;
        ProfilerTheme.FillRect(sb, area, fill);
        ProfilerTheme.DrawBorder(sb, area, border);

        int textLeft = area.X + 8;
        if (leadingDot)
        {
            int dotSize = 6;
            int dotY = area.Y + (area.Height - dotSize) / 2;
            ProfilerTheme.FillRect(sb, new Rectangle(area.X + 6, dotY, dotSize, dotSize),
                active ? ProfilerTheme.Accent : ProfilerTheme.TextMuted);
            textLeft += dotSize + 4;
        }

        OverlayDraw.Text(sb, label, new Vector2(textLeft, area.Y + 3),
            active ? ProfilerTheme.Accent : ProfilerTheme.TextMuted,
            OverlayLayoutCurrent.TextScaleBody);
    }

    public static bool HitTest(Rectangle area, float localX, float localY)
        => localX >= area.X && localX <= area.Right && localY >= area.Y && localY <= area.Bottom;
}

#endif
