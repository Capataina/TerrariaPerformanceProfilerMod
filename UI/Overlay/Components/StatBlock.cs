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
/// Label-value pair with consistent typography hierarchy. The chrome's
/// raised stat cards (this tick / avg 30 s / gc / tick #) all render via
/// this primitive so they look the same and respond to mode switching
/// without per-callsite edits.
///
/// <para>
/// Three-line layout:
/// </para>
/// <list type="bullet">
///   <item>Top line: title in muted small text (<see cref="OverlayLayoutCurrent.TextScaleBody"/>).</item>
///   <item>Middle line: value at H2 scale; colour is caller-controlled so
///   amber/red severity stays in the caller's hands.</item>
///   <item>Bottom line: optional delta or unit suffix in muted text.</item>
/// </list>
/// </summary>
internal static class StatBlock
{
    /// <summary>
    /// Renders the stat block inside <paramref name="area"/>. The block
    /// vertically centres itself within the area; if the area is taller
    /// than the natural content, extra space is shared above and below.
    /// </summary>
    /// <param name="sb">Active SpriteBatch.</param>
    /// <param name="area">Containing rectangle.</param>
    /// <param name="title">Top-line label.</param>
    /// <param name="value">Middle-line value text.</param>
    /// <param name="valueColor">Colour for the value text; usually <see cref="ProfilerTheme.Text"/> or a severity tint.</param>
    /// <param name="footer">Optional bottom-line caption (delta, unit, or comparison).</param>
    public static void Draw(SpriteBatch sb, Rectangle area, string title, string value, Color valueColor, string? footer = null)
    {
        float bodyScale = OverlayLayoutCurrent.TextScaleBody;
        float h2Scale   = OverlayLayoutCurrent.TextScaleH2;

        // Vertical layout: title (10 px), gap (4), value (h2 ~ 16 px), gap (2), footer (10).
        // Total ~42 px in default mode, ~34 in compact.
        int titleH  = (int)(14 * bodyScale / 0.62f);
        int valueH  = (int)(20 * h2Scale / 0.78f);
        int footerH = footer == null ? 0 : (int)(12 * bodyScale / 0.62f);
        int gap     = 2;
        int totalH  = titleH + gap + valueH + (footer == null ? 0 : gap + footerH);
        int startY  = area.Y + (area.Height - totalH) / 2;
        if (startY < area.Y + 2) startY = area.Y + 2;

        int leftX = area.X + 8;

        OverlayDraw.Text(sb, title, new Vector2(leftX, startY), ProfilerTheme.TextMuted, bodyScale);
        OverlayDraw.Text(sb, value, new Vector2(leftX, startY + titleH + gap), valueColor, h2Scale);
        if (footer != null)
        {
            OverlayDraw.Text(sb, footer, new Vector2(leftX, startY + titleH + gap + valueH + gap),
                ProfilerTheme.TextDim, bodyScale);
        }
    }
}

#endif
