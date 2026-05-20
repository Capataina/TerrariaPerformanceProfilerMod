#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.GameContent;

namespace PerformanceProfiler.UI.Overlay.Components;

/// <summary>
/// One sector of a <see cref="DonutChart"/>. Value drives sector arc
/// length; identity colour drives slice hue (from
/// <see cref="ProfilerTheme.ModColor"/>); dominant colour optionally tints
/// the inner edge of the ring to encode which axis (cpu / alloc / spike)
/// the mod is most heavy on.
/// </summary>
internal struct DonutSlice
{
    public double Value;
    public Color SliceColor;
    public Color DominantHue;    // tint applied to the inner edge of the slice
    public string? Label;
}

/// <summary>
/// Sectored donut chart. Drawn as many thin rotated rectangles ("wedges")
/// stitched into each slice's arc — about 1° per wedge — so the whole
/// chart goes through <see cref="SpriteBatch"/> without needing raw
/// <c>DrawUserPrimitives</c> or any SpriteBatch state juggling.
///
/// <para>
/// At 60 Hz with 8-slice donuts that's ~360 wedges per frame. Cheap on a
/// modern GPU; well under the SpriteBatch budget. We can refresh-cadence
/// the donut to 1 Hz later if the per-frame redraw shows up in our own
/// profiling.
/// </para>
///
/// <para>
/// Each slice paints in two layers: the SliceColor at its identity hue
/// covering the outer two-thirds of the ring, then the DominantHue tinted
/// over the inner third. The result reads as "which mod" by hue identity
/// + "what's it heavy on" by the inner-edge wash. The legend caller is
/// responsible for labelling.
/// </para>
/// </summary>
internal static class DonutChart
{
    /// <summary>
    /// Draws the donut centred at <paramref name="centre"/> with the given
    /// outer/inner radii. Slices are drawn clockwise starting at 12 o'clock.
    /// Values are normalised against the sum of all slice values; pass a
    /// zero-total slice list as a no-op.
    /// </summary>
    public static void Draw(SpriteBatch sb, Vector2 centre, float outerR, float innerR,
        IReadOnlyList<DonutSlice> slices)
    {
        if (slices.Count == 0 || outerR <= innerR) return;

        double total = 0d;
        for (int i = 0; i < slices.Count; i++) total += slices[i].Value;
        if (total <= 0d) return;

        Texture2D pixel = TextureAssets.MagicPixel.Value;
        const float TwoPi = (float)(Math.PI * 2d);
        // Angular resolution: one wedge every ~1° gives a smooth-looking ring
        // at 100-px radii; smaller resolutions look chunky on outer edges.
        const float WedgeStepRad = (float)(Math.PI / 180d);

        float startAngle = -MathHelper.PiOver2; // 12 o'clock
        float midR = (outerR + innerR) * 0.5f;
        float thickness = outerR - innerR;
        // Split into outer 70% (identity colour) and inner 30% (dominant tint).
        float identityRadius = innerR + thickness * 0.30f;
        float identityThickness = outerR - identityRadius;
        float dominantThickness = identityRadius - innerR;
        float identityMidR = (outerR + identityRadius) * 0.5f;
        float dominantMidR = (identityRadius + innerR) * 0.5f;

        for (int i = 0; i < slices.Count; i++)
        {
            float sweep = (float)(TwoPi * slices[i].Value / total);
            if (sweep <= 0f) continue;
            int wedgeCount = System.Math.Max(2, (int)System.Math.Ceiling(sweep / WedgeStepRad));
            float wedgeStep = sweep / wedgeCount;

            for (int w = 0; w < wedgeCount; w++)
            {
                float a = startAngle + (w + 0.5f) * wedgeStep;
                float cos = (float)Math.Cos(a);
                float sin = (float)Math.Sin(a);

                // Identity-coloured outer band.
                {
                    Vector2 pos = centre + new Vector2(cos * identityMidR, sin * identityMidR);
                    float arcLen = wedgeStep * identityMidR + 1.5f;
                    sb.Draw(pixel, pos, null, slices[i].SliceColor,
                        a + MathHelper.PiOver2,
                        new Vector2(0.5f, 0.5f),
                        new Vector2(arcLen, identityThickness),
                        SpriteEffects.None, 0f);
                }
                // Dominant-tinted inner band.
                {
                    Vector2 pos = centre + new Vector2(cos * dominantMidR, sin * dominantMidR);
                    float arcLen = wedgeStep * dominantMidR + 1.5f;
                    Color tinted = slices[i].DominantHue;
                    sb.Draw(pixel, pos, null, tinted,
                        a + MathHelper.PiOver2,
                        new Vector2(0.5f, 0.5f),
                        new Vector2(arcLen, dominantThickness),
                        SpriteEffects.None, 0f);
                }
            }

            startAngle += sweep;
        }
    }
}
