#nullable enable

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace PerformanceProfiler.UI.Overlay;

/// <summary>
/// Shared drawing primitives used by <see cref="OverlayPanel"/> (chrome) and
/// by every <see cref="IOverlayTab"/> implementation. Keep additions to this
/// file allocation-free — these run on the draw thread once per frame per
/// row, so a per-call allocation here adds up fast.
/// </summary>
internal static class OverlayDraw
{
    /// <summary>
    /// Draws a string with the bordered-text style the overlay uses
    /// throughout. Thin wrapper over <see cref="Utils.DrawBorderString"/>
    /// so we don't have to repeat the call shape in every drawing method.
    /// </summary>
    public static void Text(SpriteBatch spriteBatch, string text, Vector2 position, Color color, float scale) =>
        Utils.DrawBorderString(spriteBatch, text, position, color, scale);

    /// <summary>
    /// Draws a "label   value" pair: muted label on the left, value in its
    /// caller-chosen color to the right. Used for the per-tick stats row.
    /// </summary>
    public static void Stat(SpriteBatch spriteBatch, string label, string value,
        float x, float y, Color valueColor)
    {
        Text(spriteBatch, label, new Vector2(x,         y), ProfilerTheme.TextMuted, 0.8f);
        Text(spriteBatch, value, new Vector2(x + 102f,  y), valueColor,              0.8f);
    }

    /// <summary>
    /// Draws a colour-graded bar: dim track, then a fill from green→amber→red
    /// based on <paramref name="value"/>/<paramref name="max"/>. The bar
    /// height varies by row class (mod / category / hook) so callers pass it.
    /// </summary>
    public static void Bar(SpriteBatch spriteBatch, int barX, int barY, double value, double max, int height)
    {
        const int barWidth = 170;
        ProfilerTheme.FillRect(spriteBatch, new Rectangle(barX, barY, barWidth, height), ProfilerTheme.Border);

        double fraction = max > 0d ? value / max : 0d;
        if (fraction < 0d) fraction = 0d;
        int fillWidth = (int)(barWidth * fraction);
        if (fillWidth > 0)
            ProfilerTheme.FillRect(spriteBatch,
                new Rectangle(barX, barY, fillWidth, height), ProfilerTheme.CostColor(fraction));
    }

    /// <summary>
    /// Draws a toggle-pill (NOW/30S AVG, LIVE/PAUSED, the active tab, the
    /// metric pill). Active variant has a blue-tinted fill + accent border;
    /// inactive variant is panel-fill with a muted-text label.
    /// </summary>
    public static void Toggle(SpriteBatch spriteBatch, float x, float y, float width, string label, bool active)
    {
        Rectangle area = new Rectangle((int)x, (int)y, (int)width, (int)OverlayLayout.ToggleHeight);
        ProfilerTheme.FillRect(spriteBatch, area, active ? new Color(25, 40, 60) : ProfilerTheme.Panel);
        ProfilerTheme.DrawBorder(spriteBatch, area, active ? ProfilerTheme.Accent : ProfilerTheme.Border);
        Text(spriteBatch, label, new Vector2(area.X + 6, area.Y + 3),
            active ? ProfilerTheme.Accent : ProfilerTheme.TextMuted, 0.55f);
    }

    /// <summary>Truncates with a trailing ".." if longer than <paramref name="max"/>.</summary>
    public static string Truncate(string text, int max) =>
        text.Length <= max ? text : text.Substring(0, max - 2) + "..";

    /// <summary>
    /// Formats a byte count as the most readable unit. Falls through
    /// B → KB → MB → GB with one decimal place: "12 B" / "3.4 KB" / "1.2 MB".
    /// Values below 1 B render as "0 B" (defensive; shouldn't happen).
    /// </summary>
    public static string FormatBytes(double bytes)
    {
        if (bytes < 1d) return "0 B";
        if (bytes < 1024d) return $"{bytes:F0} B";
        if (bytes < 1024d * 1024d) return $"{bytes / 1024d:F1} KB";
        if (bytes < 1024d * 1024d * 1024d) return $"{bytes / (1024d * 1024d):F1} MB";
        return $"{bytes / (1024d * 1024d * 1024d):F1} GB";
    }
}
