// ARCHIVED v0.9.0: in-game overlay replaced by browser dashboard.
// Sources retained for possible Steam-Deck variant or future revival;
// remove the #if false / #endif guards below to restore compilation.
#if false
#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.UI;

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Persistence.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Persistence;
using PerformanceProfiler.Persistence.Records;
namespace PerformanceProfiler.UI.Overlay.Components;

/// <summary>
/// Retrospective card for a single closed segment. Used in two places:
/// inline in the Timeline tab when a row is expanded, and as the body of
/// the corner-slide toast that fires on promoted segments.
///
/// <para>
/// Cards are deliberately compact — segment retrospectives are scannable,
/// not deep reports. Three regions: header (name + duration + drama
/// badges), top-mods table (top 4), and a context strip.
/// </para>
/// </summary>
internal static class SegmentCard
{
    public const int PreferredHeight = 116;

    /// <summary>
    /// Render the card into <paramref name="area"/>. Returns the actual
    /// rectangle consumed (== area for now; future variants may shrink).
    /// </summary>
    public static Rectangle Draw(SpriteBatch sb, Rectangle area, Segment seg,
        double lifetimeAvgMsPerTick, int lifetimeSamples)
    {
        // Panel + border.
        ProfilerTheme.DrawPanel(sb, area, ProfilerTheme.SurfaceElevated, ProfilerTheme.Border);

        float rowScale = OverlayLayoutCurrent.TextScaleRow;
        float bodyScale = OverlayLayoutCurrent.TextScaleBody;
        int x = area.X + 10;
        int y = area.Y + 8;
        int rightCol = area.Right - 10;

        // ---- Header: name + duration + badges -----------------------------
        OverlayDraw.Text(sb, seg.Name, new Vector2(x, y), ProfilerTheme.Text, rowScale + 0.04f);
        string duration = FormatDuration(seg.DurationMs);
        OverlayDraw.Text(sb, duration, new Vector2(rightCol - 60, y), ProfilerTheme.TextMuted, rowScale);

        y += 18;

        // Badge strip — promotion reason + drama tally.
        string badgeText = BuildBadgeLine(seg);
        Color badgeColor = seg.DeathCount > 0 ? ProfilerTheme.Danger
                         : seg.StallCount > 0 ? ProfilerTheme.Danger
                         : seg.SpikeCount > 0 ? ProfilerTheme.Amber
                         : seg.BossKillCount > 0 ? ProfilerTheme.Good
                         : ProfilerTheme.TextMuted;
        OverlayDraw.Text(sb, badgeText, new Vector2(x, y), badgeColor, bodyScale);
        y += 14;

        // ---- Top mods (up to 4) -------------------------------------------
        List<(int ModId, double Ms)> top = seg.TopMods(4);
        double totalMs = seg.TotalFrameMs > 0 ? seg.TotalFrameMs : 1d;
        string[] modNames = HookInterceptor.ProfiledModNames;

        if (top.Count == 0)
        {
            OverlayDraw.Text(sb, "no per-mod activity in this segment",
                new Vector2(x, y), ProfilerTheme.TextMuted, bodyScale);
        }
        else
        {
            for (int i = 0; i < top.Count; i++)
            {
                int modId = top[i].ModId;
                double ms = top[i].Ms;
                double pct = (ms / totalMs) * 100d;
                double msPerTick = seg.Ticks > 0 ? ms / seg.Ticks : 0d;
                string modName = modId >= 0 && modId < modNames.Length ? modNames[modId] : "mod:" + modId;

                Color rowColor = i == 0 ? ProfilerTheme.Text : ProfilerTheme.TextMuted;
                OverlayDraw.Text(sb, $"{i + 1}. {modName}",
                    new Vector2(x, y), rowColor, bodyScale);

                string rightStat = $"{msPerTick:F2} ms/t  ({pct:F0}%)";
                OverlayDraw.Text(sb, rightStat, new Vector2(rightCol - 110, y), rowColor, bodyScale);
                y += 13;
            }
        }

        // ---- Lifetime-comparison footer -----------------------------------
        if (lifetimeSamples >= 2 && lifetimeAvgMsPerTick > 0d && seg.AvgFrameMs > 0d)
        {
            double pct = (seg.AvgFrameMs / lifetimeAvgMsPerTick - 1d) * 100d;
            string sign = pct >= 0 ? "+" : "";
            string cmp = $"vs {lifetimeSamples - 1} prior: {sign}{pct:F0}%";
            Color cmpColor = pct > 30d ? ProfilerTheme.Amber
                           : pct < -20d ? ProfilerTheme.Good
                           : ProfilerTheme.TextMuted;
            OverlayDraw.Text(sb, cmp, new Vector2(x, area.Bottom - 14), cmpColor, bodyScale);
        }

        return area;
    }

    private static string BuildBadgeLine(Segment seg)
    {
        var parts = new List<string>(6);
        parts.Add($"{seg.Ticks:N0} ticks");
        if (seg.SpikeCount > 0) parts.Add($"{seg.SpikeCount} spike{(seg.SpikeCount == 1 ? "" : "s")}");
        if (seg.StallCount > 0) parts.Add($"{seg.StallCount} stall{(seg.StallCount == 1 ? "" : "s")}");
        if (seg.DeathCount > 0) parts.Add($"{seg.DeathCount} death{(seg.DeathCount == 1 ? "" : "s")}");
        if (seg.BossKillCount > 0) parts.Add($"{seg.BossKillCount} boss kill{(seg.BossKillCount == 1 ? "" : "s")}");
        if (parts.Count == 1) parts.Add("clean");
        return string.Join("  ·  ", parts);
    }

    public static string FormatDuration(long ms)
    {
        if (ms < 1000) return ms + "ms";
        long totalSec = ms / 1000;
        if (totalSec < 60) return totalSec + "s";
        long mins = totalSec / 60;
        long secs = totalSec % 60;
        if (mins < 60) return $"{mins}m {secs:00}s";
        long hrs = mins / 60;
        mins %= 60;
        return $"{hrs}h {mins:00}m";
    }
}

#endif
