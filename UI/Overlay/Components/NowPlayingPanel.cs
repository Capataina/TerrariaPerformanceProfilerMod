// ARCHIVED v0.9.0: in-game overlay replaced by browser dashboard.
// Sources retained for possible Steam-Deck variant or future revival;
// remove the #if false / #endif guards below to restore compilation.
#if false
#nullable enable

using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ModLoader;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.UI;

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Data.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Profiling.Persistence;
using PerformanceProfiler.Profiling.Persistence.Records;
namespace PerformanceProfiler.UI.Overlay.Components;

/// <summary>
/// Always-visible "Now Playing" widget — a compact floating panel anchored
/// near the top-left of the screen showing every currently-open
/// <see cref="OpenSegment"/> as one row, with a live elapsed timer and the
/// top mod-during-segment so far.
///
/// <para>
/// v0.7.1: hoisted out of the Overview tab into its own always-on layer
/// (similar to <see cref="RetrospectiveToast"/>). Two reasons:
/// </para>
/// <list type="bullet">
///   <item>Stops the Overview tab from overflowing the screen on 1080p+
///         when many segments are open.</item>
///   <item>Visible even with the F9 overlay closed — the live segment
///         state is the kind of thing the player wants glanceable, not
///         hidden behind a panel toggle.</item>
/// </list>
///
/// <para>
/// Gated by <c>ProfilerConfig.EnableNowPlayingPanel</c>. Renders zero
/// pixels when no segments are open or the player isn't in a world.
/// </para>
/// </summary>
internal static class NowPlayingPanel
{
    private const int RowH = 16;
    private const int HeaderH = 14;
    private const int PaddingY = 6;
    private const int MaxRows = 6;
    private const int Width = 380;
    private const int MarginX = 18;
    private const int MarginY = 30;

    /// <summary>Always-on draw entry. Called from <see cref="ProfilerOverlaySystem"/>'s float-layer.</summary>
    public static void DrawFloating(SpriteBatch sb)
    {
        // Config gate.
        ProfilerConfig? cfg = ModContent.GetInstance<ProfilerConfig>();
        if (cfg != null && !cfg.EnableNowPlayingPanel) return;

        // Only show inside a live world (Main.LocalPlayer.active is the cheap check).
        if (Main.gameMenu) return;

        ProfilerSystem? sys = ModContent.GetInstance<ProfilerSystem>();
        SegmentDetector? det = sys?.Segments;
        if (det == null || det.OpenSegments.Count == 0) return;

        // Snapshot + sort: most interesting first.
        var ordered = new List<OpenSegment>(det.OpenSegments);
        ordered.Sort((a, b) => FamilyWeight(a.Family).CompareTo(FamilyWeight(b.Family)));
        if (ordered.Count > MaxRows) ordered.RemoveRange(MaxRows, ordered.Count - MaxRows);

        int rows = ordered.Count;
        int totalH = HeaderH + rows * RowH + PaddingY;

        int x = MarginX;
        int y = MarginY;
        Rectangle outer = new Rectangle(x, y, Width, totalH);
        ProfilerTheme.FillRect(sb, outer, ProfilerTheme.SurfaceElevated);
        ProfilerTheme.DrawBorder(sb, outer, ProfilerTheme.Border);

        float bodyScale = OverlayLayoutCurrent.TextScaleBody;
        float rowScale = OverlayLayoutCurrent.TextScaleRow;

        // Header.
        OverlayDraw.Text(sb, "NOW PLAYING",
            new Vector2(x + 8, y + 1),
            ProfilerTheme.TextMuted, bodyScale);
        OverlayDraw.Text(sb, rows + " open",
            new Vector2(x + Width - 60, y + 1),
            ProfilerTheme.TextMuted, bodyScale);

        long unixMs = Time.UnixMsNow();
        string[] modNames = HookInterceptor.ProfiledModNames;

        int rowY = y + HeaderH;
        for (int i = 0; i < rows; i++)
        {
            OpenSegment seg = ordered[i];

            // Family stripe.
            Color marker = FamilyColor(seg.Family);
            ProfilerTheme.FillRect(sb, new Rectangle(x + 6, rowY + 3, 4, RowH - 6), marker);

            // Name.
            OverlayDraw.Text(sb, seg.Name,
                new Vector2(x + 16, rowY + 1),
                ProfilerTheme.Text, rowScale);

            // Elapsed.
            long durMs = unixMs - seg.StartUnixMs;
            string dur = SegmentCard.FormatDuration(durMs);
            OverlayDraw.Text(sb, dur,
                new Vector2(x + Width / 2 - 16, rowY + 1),
                ProfilerTheme.TextMuted, bodyScale);

            // Top mod-during-segment.
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
                right = "…";
            }
            OverlayDraw.Text(sb, right,
                new Vector2(x + Width - 160, rowY + 1),
                ProfilerTheme.TextMuted, bodyScale);

            rowY += RowH;
        }
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

#endif
