#nullable enable

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PerformanceProfiler.UI.Overlay.Components;

/// <summary>
/// A titled raised surface — the new visual primitive for "a thing with a
/// title and some content". Used by the chrome (stat cards, PROFILER
/// HEALTH), by the SUMMARY tab (impact-share card, top-contributors card,
/// session-trend card, all-mods card), by the INSIGHTS tab (one card per
/// record), and by the SELF tab (heap sparkline card, hook table card).
///
/// <para>
/// Two layers: a 20 px title strip in the panel-fill tone with the title
/// text in muted small caps and an optional right-side stat, and a body
/// region in the elevated-fill tone with a 1 px border around the whole
/// thing. Sharp corners by design (no asset shipping per Caner's call).
/// </para>
///
/// <para>
/// The intent here is "one place defines what a card looks like"; every
/// surface adopting this primitive looks the same whether it's owned by
/// the chrome or by a tab. Future polish (hover state, selected state,
/// click-to-expand) extends the primitive once and benefits everyone.
/// </para>
/// </summary>
internal static class ProfilerCard
{
    /// <summary>
    /// Draws a complete card and returns the inner body rectangle for the
    /// caller to render their content into. Title strip is omitted if
    /// <paramref name="title"/> is null or empty.
    /// </summary>
    /// <param name="sb">Active SpriteBatch.</param>
    /// <param name="outer">The full card rectangle (chrome + body).</param>
    /// <param name="title">Card title shown in muted small caps; null hides the title strip.</param>
    /// <param name="rightStat">Optional small stat anchored to the right of the title strip (e.g. "100%").</param>
    /// <param name="active">If true, the card uses the active border colour and a slightly brighter title strip.</param>
    /// <returns>The body rectangle (interior, after title strip and border insets).</returns>
    public static Rectangle Draw(SpriteBatch sb, Rectangle outer, string? title, string? rightStat, bool active = false)
    {
        // Border + body fill. The body sits on the elevated surface tier so it
        // reads as raised against the panel-fill below.
        ProfilerTheme.FillRect(sb, outer, ProfilerTheme.SurfaceElevated);
        ProfilerTheme.DrawBorder(sb, outer, active ? ProfilerTheme.BorderActive : ProfilerTheme.Border);

        // No title? Body is just the outer minus the border.
        if (string.IsNullOrEmpty(title))
        {
            return new Rectangle(outer.X + 1, outer.Y + 1, outer.Width - 2, outer.Height - 2);
        }

        // Title strip in panel-fill (one tone DARKER than the body so the
        // body still reads as raised).
        int stripH = (int)OverlayLayoutCurrent.CardTitleStripHeight;
        Rectangle titleStrip = new Rectangle(outer.X + 1, outer.Y + 1, outer.Width - 2, stripH);
        ProfilerTheme.FillRect(sb, titleStrip, active ? ProfilerTheme.Header : ProfilerTheme.Panel);

        // Bottom border of the title strip — a 1 px line separating title from body.
        ProfilerTheme.FillRect(sb, new Rectangle(titleStrip.X, titleStrip.Bottom, titleStrip.Width, 1),
            active ? ProfilerTheme.BorderActive : ProfilerTheme.Border);

        // Title text — H2 scale, muted, left-aligned.
        OverlayDraw.Text(sb, title, new Vector2(titleStrip.X + 8, titleStrip.Y + 3),
            active ? ProfilerTheme.Accent : ProfilerTheme.TextMuted,
            OverlayLayoutCurrent.TextScaleBody);

        // Right-aligned stat in the title strip when supplied. Approximation
        // of width via character count; the bordered-text helper doesn't
        // give us a measure call, so we estimate ~6 px per char at this
        // scale. Good enough for a 1-4 char stat like "100%" or "527 MB".
        if (!string.IsNullOrEmpty(rightStat))
        {
            float approxWidth = rightStat.Length * 6.5f * OverlayLayoutCurrent.TextScaleBody / 0.62f;
            OverlayDraw.Text(sb, rightStat,
                new Vector2(titleStrip.Right - 8 - approxWidth, titleStrip.Y + 3),
                ProfilerTheme.Text,
                OverlayLayoutCurrent.TextScaleBody);
        }

        // Body region: under the title strip, inside the border.
        return new Rectangle(outer.X + 1, outer.Y + stripH + 2, outer.Width - 2, outer.Height - stripH - 3);
    }
}
