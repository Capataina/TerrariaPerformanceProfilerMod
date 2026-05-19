#nullable enable

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.GameContent;

namespace PerformanceProfiler.UI;

/// <summary>
/// The profiler overlay's visual language: the dark palette taken from
/// design/Mockups.html, and the low-level custom-drawing helpers the overlay
/// draws with instead of stock tModLoader widget chrome.
///
/// Stock UIPanel / UIText give tModLoader's default grey look; everything the
/// mockup specifies (dark panels, colour-graded cost bars) is drawn by hand
/// with these helpers. The colours are sourced directly from the mockup's CSS.
/// </summary>
public static class ProfilerTheme
{
    // ---- Surfaces ---------------------------------------------------------
    /// <summary>Deepest background, behind everything (mockup #0a0e14).</summary>
    public static readonly Color Background = new Color(10, 14, 20);

    /// <summary>Panel fill (mockup #0d1117).</summary>
    public static readonly Color Panel = new Color(13, 17, 23);

    /// <summary>Header / chrome strip fill (mockup #11161f).</summary>
    public static readonly Color Header = new Color(17, 22, 31);

    /// <summary>Panel and row border (mockup #1f2329).</summary>
    public static readonly Color Border = new Color(31, 35, 41);

    /// <summary>Hover highlight for an interactive row (mockup #161b22).</summary>
    public static readonly Color RowHover = new Color(22, 27, 34);

    // ---- Text -------------------------------------------------------------
    /// <summary>Primary readable text (mockup #c5c8ce).</summary>
    public static readonly Color Text = new Color(197, 200, 206);

    /// <summary>Secondary / label text (mockup #6e7480).</summary>
    public static readonly Color TextMuted = new Color(110, 116, 128);

    /// <summary>Faint text for de-emphasised detail (mockup #4d525d).</summary>
    public static readonly Color TextDim = new Color(77, 82, 93);

    // ---- Accents ----------------------------------------------------------
    /// <summary>Headings and active elements (mockup #79c0ff).</summary>
    public static readonly Color Accent = new Color(121, 192, 255);

    /// <summary>Numeric highlight: tick time, frame stats (mockup #f5b342).</summary>
    public static readonly Color Amber = new Color(245, 179, 66);

    /// <summary>Healthy / low-cost signal (mockup #95d4a3).</summary>
    public static readonly Color Good = new Color(149, 212, 163);

    /// <summary>Dormant-cost / engagement purple (mockup #b389e3).</summary>
    public static readonly Color Dormant = new Color(179, 137, 227);

    // ---- Cost gradient endpoints -----------------------------------------
    private static readonly Color CostLow = new Color(149, 212, 163);   // green  #95d4a3
    private static readonly Color CostMid = new Color(245, 179, 66);    // amber  #f5b342
    private static readonly Color CostHigh = new Color(244, 113, 116);  // red    #f47174

    /// <summary>
    /// Maps a 0..1 cost fraction to the green-to-amber-to-red gradient used for
    /// the tree's cost bars. Values outside [0, 1] are clamped.
    /// </summary>
    public static Color CostColor(double fraction)
    {
        float t = (float)(fraction < 0d ? 0d : fraction > 1d ? 1d : fraction);
        return t < 0.5f
            ? Color.Lerp(CostLow, CostMid, t * 2f)
            : Color.Lerp(CostMid, CostHigh, (t - 0.5f) * 2f);
    }

    /// <summary>Draws a solid filled rectangle: the building block for panels and bars.</summary>
    public static void FillRect(SpriteBatch spriteBatch, Rectangle area, Color color)
    {
        spriteBatch.Draw(TextureAssets.MagicPixel.Value, area, color);
    }

    /// <summary>Draws a border <paramref name="thickness"/> pixels thick, inside <paramref name="area"/>.</summary>
    public static void DrawBorder(SpriteBatch spriteBatch, Rectangle area, Color color, int thickness = 1)
    {
        Texture2D pixel = TextureAssets.MagicPixel.Value;
        spriteBatch.Draw(pixel, new Rectangle(area.X, area.Y, area.Width, thickness), color);
        spriteBatch.Draw(pixel, new Rectangle(area.X, area.Bottom - thickness, area.Width, thickness), color);
        spriteBatch.Draw(pixel, new Rectangle(area.X, area.Y, thickness, area.Height), color);
        spriteBatch.Draw(pixel, new Rectangle(area.Right - thickness, area.Y, thickness, area.Height), color);
    }

    /// <summary>Draws a filled panel with a one-pixel border: the standard overlay surface.</summary>
    public static void DrawPanel(SpriteBatch spriteBatch, Rectangle area, Color fill, Color border)
    {
        FillRect(spriteBatch, area, fill);
        DrawBorder(spriteBatch, area, border);
    }
}
