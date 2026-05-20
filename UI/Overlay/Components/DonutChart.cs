#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace PerformanceProfiler.UI.Overlay.Components;

/// <summary>
/// One slice of the impact-share donut. Slices are drawn as two concentric
/// rings: outer ring in <see cref="SliceColor"/> (mod identity), inner ring
/// in <see cref="DominantHue"/> (cpu/alloc/spike axis tint).
/// </summary>
internal struct DonutSlice
{
    public double Value;
    public Color SliceColor;
    public Color DominantHue;
    public string? Label;
    public double CpuMs;
    public double AllocMsEq;
    public double SpikeMs;
}

/// <summary>
/// Impact-share donut chart, rendered as actual triangle geometry via
/// <see cref="GraphicsDevice.DrawUserPrimitives"/>.
///
/// <para>
/// <b>Why this implementation.</b> The first attempt drew the donut as a
/// stack of rotated thin rectangles through <see cref="SpriteBatch"/>. The
/// rotation math went off-screen on tested hardware and painted huge cyan
/// diagonals across the whole game world. The second attempt was a stacked
/// horizontal bar — safe but flat. The third — a city skyline — looked
/// good but collapsed at 50+ mods because each bar got too thin.
/// </para>
///
/// <para>
/// Triangles via <c>DrawUserPrimitives</c> are the right tool. Angular
/// space is constant regardless of slice count, so a 200-mod modlist
/// renders as recognisable donut where the top contributors stay
/// legible and the long tail accurately collapses into thin slices.
/// </para>
///
/// <para>
/// <b>Implementation notes.</b> We allocate one <see cref="BasicEffect"/>
/// lazily on first draw and a reusable vertex array. The hot path:
/// </para>
/// <list type="number">
///   <item><see cref="SpriteBatch.End"/> on the in-flight batch.</item>
///   <item>Configure <c>_effect.Projection</c> (screen-space ortho) and
///   <c>_effect.World</c> (the UI scale matrix so we share the SpriteBatch's
///   coordinate space).</item>
///   <item><c>DrawUserPrimitives(TriangleList, vertices, ..., triangleCount)</c>.</item>
///   <item><see cref="SpriteBatch.Begin"/> with the same parameters
///   <c>tModLoader</c>'s UI render pass uses, so subsequent sprite draws
///   continue undisturbed.</item>
/// </list>
///
/// <para>
/// Cost per donut: ~720 triangles (2° step × full circle × 2 rings).
/// At 60 Hz that's ~43 k triangles/sec — sub-percent of a modern GPU's
/// throughput budget.
/// </para>
/// </summary>
internal static class DonutChart
{
    /// <summary>Angular resolution; smaller = smoother arcs, more triangles.</summary>
    private const float StepRad = 0.0349066f; // ~2°

    /// <summary>Maximum visible slices; anything beyond gets folded into a tail "others" slice by callers.</summary>
    public const int MaxSlicesHint = 16;

    private static BasicEffect? _effect;
    private static VertexPositionColor[] _vertices = new VertexPositionColor[2048];
    private static int _vertexCount;

    /// <summary>
    /// v0.6.1 vertex-array reuse cache (overlay dossier η10). The donut
    /// geometry only changes when slice values / colours / radii / centre
    /// change. In a stable frame (player not moving the overlay), the
    /// hash matches across consecutive draws and BuildRingTriangles can
    /// be skipped entirely — just resubmit the existing _vertices buffer.
    /// </summary>
    private static ulong _lastGeometryHash;

    /// <summary>
    /// Draws the donut centred at <paramref name="centre"/> with the given
    /// outer/inner radii. Caller pre-sorts slices in descending Value
    /// order; we draw them clockwise from 12 o'clock.
    /// </summary>
    public static void Draw(SpriteBatch sb, Vector2 centre, float outerR, float innerR, IReadOnlyList<DonutSlice> slices)
    {
        if (slices.Count == 0 || outerR <= innerR + 2f) return;

        double total = 0d;
        for (int i = 0; i < slices.Count; i++) total += slices[i].Value;
        if (total <= 0d) return;

        GraphicsDevice gd = sb.GraphicsDevice;

        // v0.6.1: hash geometry to skip BuildRingTriangles when nothing changed.
        ulong geomHash = ComputeGeometryHash(centre, innerR, outerR, slices);
        if (geomHash != _lastGeometryHash)
        {
            // Build the geometry. Two concentric rings:
            //   outer band  (75% of thickness)  identity colour
            //   inner band  (25% of thickness)  dominant-axis tint
            float thickness = outerR - innerR;
            float midR = innerR + thickness * 0.25f; // split point: lower 25% is dominant band
            BuildRingTriangles(centre, midR, outerR, slices, total, identityRing: true);
            BuildRingTriangles(centre, innerR, midR, slices, total, identityRing: false);
            _lastGeometryHash = geomHash;
        }

        if (_vertexCount == 0) return;

        // End the in-flight SpriteBatch so we can submit raw triangles.
        sb.End();

        EnsureEffect(gd);

        // Configure for 2D screen-space rendering matching tModLoader's UI pass.
        BasicEffect e = _effect!;
        Viewport vp = gd.Viewport;
        e.World = Main.UIScaleMatrix;
        e.View = Matrix.Identity;
        e.Projection = Matrix.CreateOrthographicOffCenter(0, vp.Width, vp.Height, 0, 0, 1);

        // Apply the technique and submit the triangles.
        foreach (EffectPass pass in e.CurrentTechnique.Passes)
        {
            pass.Apply();
            gd.DrawUserPrimitives(PrimitiveType.TriangleList, _vertices, 0, _vertexCount / 3);
        }

        // Restart the SpriteBatch with the same Begin params the UI pass uses
        // so subsequent sprite draws don't fall out of sync.
        sb.Begin(
            SpriteSortMode.Deferred,
            BlendState.AlphaBlend,
            Main.DefaultSamplerState,
            DepthStencilState.None,
            Main.Rasterizer,
            null,
            Main.UIScaleMatrix);
    }

    /// <summary>
    /// Convenience overload that draws into a rectangular area; computes
    /// centre + radii from the area bounds.
    /// </summary>
    public static void Draw(SpriteBatch sb, Rectangle area, IReadOnlyList<DonutSlice> slices)
    {
        float outerR = Math.Min(area.Width, area.Height) * 0.46f;
        float innerR = outerR * 0.58f;
        Vector2 centre = new Vector2(area.Center.X, area.Center.Y);
        Draw(sb, centre, outerR, innerR, slices);
    }

    private static void BuildRingTriangles(Vector2 centre, float innerR, float outerR,
        IReadOnlyList<DonutSlice> slices, double total, bool identityRing)
    {
        if (!identityRing) { /* second ring keeps existing vertex count, appending */ }
        else { _vertexCount = 0; } // first ring resets

        float startAngle = -MathHelper.PiOver2;

        for (int i = 0; i < slices.Count; i++)
        {
            float sweep = (float)(MathHelper.TwoPi * slices[i].Value / total);
            if (sweep <= 0f) continue;

            int steps = Math.Max(2, (int)Math.Ceiling(sweep / StepRad));
            float step = sweep / steps;
            Color color = identityRing ? slices[i].SliceColor : slices[i].DominantHue;

            for (int s = 0; s < steps; s++)
            {
                float a1 = startAngle + s * step;
                float a2 = startAngle + (s + 1) * step;
                float c1 = (float)Math.Cos(a1);
                float s1 = (float)Math.Sin(a1);
                float c2 = (float)Math.Cos(a2);
                float s2 = (float)Math.Sin(a2);

                Vector2 i1 = new Vector2(centre.X + c1 * innerR, centre.Y + s1 * innerR);
                Vector2 o1 = new Vector2(centre.X + c1 * outerR, centre.Y + s1 * outerR);
                Vector2 o2 = new Vector2(centre.X + c2 * outerR, centre.Y + s2 * outerR);
                Vector2 i2 = new Vector2(centre.X + c2 * innerR, centre.Y + s2 * innerR);

                EnsureVertexCapacity(_vertexCount + 6);
                _vertices[_vertexCount++] = new VertexPositionColor(new Vector3(i1, 0f), color);
                _vertices[_vertexCount++] = new VertexPositionColor(new Vector3(o1, 0f), color);
                _vertices[_vertexCount++] = new VertexPositionColor(new Vector3(o2, 0f), color);
                _vertices[_vertexCount++] = new VertexPositionColor(new Vector3(i1, 0f), color);
                _vertices[_vertexCount++] = new VertexPositionColor(new Vector3(o2, 0f), color);
                _vertices[_vertexCount++] = new VertexPositionColor(new Vector3(i2, 0f), color);
            }

            startAngle += sweep;
        }
    }

    private static void EnsureVertexCapacity(int needed)
    {
        if (_vertices.Length < needed)
        {
            int newSize = Math.Max(_vertices.Length * 2, needed);
            Array.Resize(ref _vertices, newSize);
        }
    }

    /// <summary>
    /// Cheap geometry hash for the vertex-reuse cache. FNV-1a folded over
    /// (centre, innerR, outerR, sliceCount, each slice's value + packed
    /// RGBA). No allocations, ~30 ops typical, constant per slice.
    /// </summary>
    private static ulong ComputeGeometryHash(Vector2 centre, float innerR, float outerR, IReadOnlyList<DonutSlice> slices)
    {
        ulong h = 14695981039346656037UL;
        h = (h ^ unchecked((uint)BitConverter.SingleToInt32Bits(centre.X))) * 1099511628211UL;
        h = (h ^ unchecked((uint)BitConverter.SingleToInt32Bits(centre.Y))) * 1099511628211UL;
        h = (h ^ unchecked((uint)BitConverter.SingleToInt32Bits(innerR))) * 1099511628211UL;
        h = (h ^ unchecked((uint)BitConverter.SingleToInt32Bits(outerR))) * 1099511628211UL;
        h = (h ^ (uint)slices.Count) * 1099511628211UL;
        for (int i = 0; i < slices.Count; i++)
        {
            h = (h ^ unchecked((uint)BitConverter.DoubleToInt64Bits(slices[i].Value))) * 1099511628211UL;
            h = (h ^ slices[i].Color.PackedValue) * 1099511628211UL;
        }
        return h;
    }

    private static void EnsureEffect(GraphicsDevice gd)
    {
        if (_effect == null)
        {
            _effect = new BasicEffect(gd)
            {
                VertexColorEnabled = true,
                TextureEnabled = false,
                LightingEnabled = false,
            };
        }
    }
}
