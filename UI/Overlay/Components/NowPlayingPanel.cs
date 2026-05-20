#nullable enable

using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.ModLoader;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Segments;
using PerformanceProfiler.UI;

namespace PerformanceProfiler.UI.Overlay.Components;

/// <summary>
/// "Now playing" live panel — sits at the top of the Overview tab and
/// shows every currently-open <see cref="OpenSegment"/> as one row, with
/// a live elapsed timer and the top mod-during-segment so far.
///
/// <para>
/// Compact by design. Each row is a single horizontal strip. The panel
/// reports zero height when no segments are open (gracefully invisible
/// in the menu / between worlds / right after world-load).
/// </para>
/// </summary>
internal static class NowPlayingPanel
{
    private const int RowH = 16;
    private const int HeaderH = 14;
    private const int PaddingY = 6;
    private const int MaxRows = 6;

    /// <summary>Height the panel will actually consume this frame; 0 when no segments are open.</summary>
    public static float MeasureHeight()
    {
        ProfilerSystem? sys = ModContent.GetInstance<ProfilerSystem>();
        SegmentDetector? det = sys?.Segments;
        if (det == null) return 0f;
        int rows = det.OpenSegments.Count;
        if (rows == 0) return 0f;
        if (rows > MaxRows) rows = MaxRows;
        return HeaderH + RowH * rows + PaddingY;
    }

    /// <summary>
    /// Render into <paramref name="area"/> (X/Y/Width consumed; Height ignored
    /// — we compute and return our own consumed height). Returns 0 when no
    /// segments are open (caller skips the gap).
    /// </summary>
    public static int Draw(SpriteBatch sb, Rectangle area, MetricCollector collector)
    {
        ProfilerSystem? sys = ModContent.GetInstance<ProfilerSystem>();
        SegmentDetector? det = sys?.Segments;
        if (det == null || det.OpenSegments.Count == 0) return 0;

        // Snapshot + sort: bosses/invasions/weather first (high-signal), biomes after.
        var ordered = new List<OpenSegment>(det.OpenSegments);
        ordered.Sort((a, b) => FamilyWeight(a.Family).CompareTo(FamilyWeight(b.Family)));
        if (ordered.Count > MaxRows) ordered.RemoveRange(MaxRows, ordered.Count - MaxRows);

        int rows = ordered.Count;
        int totalH = HeaderH + rows * RowH + PaddingY;

        Rectangle outer = new Rectangle(area.X, area.Y, area.Width, totalH - PaddingY);
        ProfilerTheme.DrawPanel(sb, outer, ProfilerTheme.SurfaceElevated, ProfilerTheme.Border);

        float bodyScale = OverlayLayoutCurrent.TextScaleBody;
        float rowScale = OverlayLayoutCurrent.TextScaleRow;

        // Header strip
        OverlayDraw.Text(sb, "NOW PLAYING",
            new Vector2(area.X + 8, area.Y + 1),
            ProfilerTheme.TextMuted, bodyScale);
        OverlayDraw.Text(sb, rows + " open segment" + (rows == 1 ? "" : "s"),
            new Vector2(area.Right - 110, area.Y + 1),
            ProfilerTheme.TextMuted, bodyScale);

        long tickNow = (long)Terraria.Main.GameUpdateCount;
        long unixMs = Time.UnixMsNow();
        string[] modNames = HookInterceptor.ProfiledModNames;

        int y = area.Y + HeaderH;
        for (int i = 0; i < rows; i++)
        {
            OpenSegment seg = ordered[i];

            // Family colour stripe.
            Color marker = FamilyColor(seg.Family);
            ProfilerTheme.FillRect(sb, new Rectangle(area.X + 6, y + 3, 4, RowH - 6), marker);

            // Name (left)
            OverlayDraw.Text(sb, seg.Name,
                new Vector2(area.X + 16, y + 1),
                ProfilerTheme.Text, rowScale);

            // Elapsed (centre)
            long durMs = unixMs - seg.StartUnixMs;
            string dur = SegmentCard.FormatDuration(durMs);
            OverlayDraw.Text(sb, dur,
                new Vector2(area.X + area.Width / 2 - 24, y + 1),
                ProfilerTheme.TextMuted, bodyScale);

            // Top mod-during-segment so far (right) — find best
            int bestId = -1;
            double bestMs = 0d;
            for (int m = 0; m < seg.PerModMs.Length; m++)
            {
                if (seg.PerModMs[m] > bestMs) { bestMs = seg.PerModMs[m]; bestId = m; }
            }
            string right;
            if (bestId >= 0 && bestId < modNames.Length && seg.Ticks > 0)
            {
                double mspt = bestMs / seg.Ticks;
                right = $"{Truncate(modNames[bestId], 14)}  {mspt:F2}ms/t";
            }
            else
            {
                right = "...";
            }
            OverlayDraw.Text(sb, right,
                new Vector2(area.Right - 150, y + 1),
                ProfilerTheme.TextMuted, bodyScale);

            y += RowH;
        }

        return totalH;
    }

    private static int FamilyWeight(SegmentFamily f) => f switch
    {
        SegmentFamily.Boss => 0,
        SegmentFamily.Invasion => 1,
        SegmentFamily.UserBookmark => 2,
        SegmentFamily.Weather => 3,
        SegmentFamily.Subworld => 4,
        SegmentFamily.Combat => 5,
        SegmentFamily.Hardmode => 6,
        SegmentFamily.DeathBracket => 7,
        SegmentFamily.Biome => 8,
        _ => 9,
    };

    private static Color FamilyColor(SegmentFamily f) => f switch
    {
        SegmentFamily.Boss        => ProfilerTheme.Danger,
        SegmentFamily.Invasion    => ProfilerTheme.Danger,
        SegmentFamily.Weather     => ProfilerTheme.Amber,
        SegmentFamily.Hardmode    => ProfilerTheme.Amber,
        SegmentFamily.Subworld    => ProfilerTheme.Amber,
        SegmentFamily.Biome       => ProfilerTheme.Good,
        SegmentFamily.Combat      => ProfilerTheme.SpikeDominant,
        SegmentFamily.DeathBracket => ProfilerTheme.TextMuted,
        SegmentFamily.UserBookmark => ProfilerTheme.Text,
        _ => ProfilerTheme.TextMuted,
    };

    private static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= max) return s;
        return s.Substring(0, max - 1) + "…";
    }
}
