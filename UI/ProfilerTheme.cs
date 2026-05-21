// ARCHIVED v0.9.0: in-game overlay replaced by browser dashboard.
// Sources retained for possible Steam-Deck variant or future revival;
// remove the #if false / #endif guards below to restore compilation.
#if false
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

    /// <summary>High-risk / incomplete-coverage signal (mockup #f47174).</summary>
    public static readonly Color Danger = new Color(244, 113, 116);

    // ---- UI overhaul Phase 0 additions ---------------------------------------
    //
    // The new component library + chrome rewrite need a few additional colours
    // that the existing 3-tier surface system doesn't cover. These sit
    // alongside the existing palette so Phase 0 doesn't break anything; tabs
    // and the chrome adopt them in Phases 2-6.

    /// <summary>Elevated surface tier, sits between panel and header brightness. Used by raised cards.</summary>
    public static readonly Color SurfaceElevated = new Color(22, 27, 34);   // #161b22

    /// <summary>Hover state for interactive rows; same as the existing RowHover, named alongside for the new surface vocabulary.</summary>
    public static readonly Color SurfaceHover = RowHover;

    /// <summary>Border for a selected / active card or row.</summary>
    public static readonly Color BorderActive = new Color(42, 54, 69);      // #2a3645

    /// <summary>Modder-targeted insight badge tint; teal so it reads as a category, not a severity.</summary>
    public static readonly Color Modder = new Color(146, 197, 182);         // #92c5b6

    /// <summary>Dominant-axis hue for CPU-heavy mods on the SUMMARY donut. Aliases <see cref="Accent"/> for visual identity.</summary>
    public static readonly Color CpuDominant = Accent;

    /// <summary>Dominant-axis hue for allocation-heavy mods. Aliases <see cref="Dormant"/> (purple) so it reads as an attribute, not a severity.</summary>
    public static readonly Color AllocDominant = Dormant;

    /// <summary>Dominant-axis hue for spike-prone mods. Aliases <see cref="Amber"/>.</summary>
    public static readonly Color SpikeDominant = Amber;

    /// <summary>
    /// Stable per-mod identity palette: 12 muted, mid-saturation hues chosen
    /// to maximise pairwise distance (alternating warm/cool) so adjacent
    /// donut slices read as distinct without screaming. Indexing is
    /// <c>palette[modId % palette.Length]</c> — deterministic, so the same
    /// mod gets the same colour across sessions and surfaces (donut, sparkline
    /// slice, future Self-tab histograms). The heat-ramp foreground still
    /// renders on top of these where severity matters; the slice colour
    /// conveys IDENTITY, not COST.
    /// </summary>
    public static readonly Color[] ModPalette =
    {
        new Color(91, 155, 213),    // #5b9bd5 — soft blue
        new Color(244, 164, 96),    // #f4a460 — sandy orange
        new Color(126, 196, 148),   // #7ec494 — sage green
        new Color(224, 126, 155),   // #e07e9b — dusty pink
        new Color(176, 149, 212),   // #b095d4 — mauve
        new Color(212, 180, 99),    // #d4b463 — muted gold
        new Color(122, 184, 212),   // #7ab8d4 — sky cyan
        new Color(212, 114, 114),   // #d47272 — soft brick
        new Color(148, 196, 106),   // #94c46a — moss
        new Color(184, 132, 206),   // #b884ce — orchid
        new Color(204, 163, 126),   // #cca37e — cocoa
        new Color(130, 182, 201),   // #82b6c9 — slate cyan
    };

    /// <summary>
    /// Returns the deterministic identity colour for <paramref name="modId"/>.
    /// Same mod, same colour, every session.
    /// </summary>
    public static Color ModColor(int modId)
    {
        if (modId < 0) modId = 0;
        return ModPalette[modId % ModPalette.Length];
    }

    // ---- Cost gradient (5-stop heat ramp) -----------------------------------
    //
    // The previous version interpolated across three stops (green / amber /
    // red), which produced harsh visual jumps at the midpoints. The 5-stop
    // ramp adds intermediate lime (between green and amber) and orange
    // (between amber and red) so the gradient reads as a continuous heat
    // map. Same domain ([0, 1] clamped), same call sites, smoother output.

    private static readonly Color HeatGood   = new Color(149, 212, 163);  // #95d4a3 — green
    private static readonly Color HeatLime   = new Color(184, 212, 121);  // #b8d479 — lime  (NEW, fills the green→amber gap)
    private static readonly Color HeatAmber  = new Color(245, 179, 66);   // #f5b342 — amber
    private static readonly Color HeatHot    = new Color(232, 138, 60);   // #e88a3c — orange (NEW, fills the amber→red gap)
    private static readonly Color HeatDanger = new Color(244, 113, 116);  // #f47174 — red

    /// <summary>
    /// Maps a 0..1 cost fraction to the heat ramp. Five stops produce a
    /// smoother gradient than the original three; the perceived transition
    /// at the midpoints no longer feels like a step change. Values outside
    /// [0, 1] are clamped.
    /// </summary>
    public static Color CostColor(double fraction)
    {
        float t = (float)(fraction < 0d ? 0d : fraction > 1d ? 1d : fraction);
        // Four equal segments across the 5 stops: [0, 0.25], (0.25, 0.5],
        // (0.5, 0.75], (0.75, 1.0].
        if (t < 0.25f) return Color.Lerp(HeatGood,  HeatLime,   t * 4f);
        if (t < 0.50f) return Color.Lerp(HeatLime,  HeatAmber,  (t - 0.25f) * 4f);
        if (t < 0.75f) return Color.Lerp(HeatAmber, HeatHot,    (t - 0.50f) * 4f);
        return                Color.Lerp(HeatHot,   HeatDanger, (t - 0.75f) * 4f);
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

#endif
