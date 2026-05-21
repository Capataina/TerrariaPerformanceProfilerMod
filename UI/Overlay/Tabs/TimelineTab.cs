// ARCHIVED v0.9.0: in-game overlay replaced by browser dashboard.
// Sources retained for possible Steam-Deck variant or future revival;
// remove the #if false / #endif guards below to restore compilation.
#if false
#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.ModLoader;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.UI.Overlay.Components;
using PerformanceProfiler.UI;

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Data.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Profiling.Persistence;
using PerformanceProfiler.Profiling.Persistence.Records;
namespace PerformanceProfiler.UI.Overlay.Tabs;

/// <summary>
/// TIMELINE — scrollable history of closed segments (biome visits,
/// weather events, boss fights, invasions, deaths, bookmarks). Newest
/// first. Click a row to expand it inline into a full retrospective
/// <see cref="SegmentCard"/>; click again to collapse.
///
/// <para>
/// Reads from <see cref="SegmentStore.Recent"/> — the live in-memory ring
/// of the last ~64 closed segments this session. Older segments live in
/// LiteDB and aren't surfaced here; that's a Phase B+ scroll-back feature
/// (chat command <c>/profiler segments lifetime</c> already pulls from DB).
/// </para>
/// </summary>
internal sealed class TimelineTab : IOverlayTab
{
    public string Label => "TIMELINE";

    private const int RowHeight = 22;
    private const int CardHeightExpanded = SegmentCard.PreferredHeight;

    private int _scroll;          // 0-based row index at top of viewport
    private LiteDB.ObjectId _expandedId = LiteDB.ObjectId.Empty;
    private SegmentFamily? _filter;

    public bool IsAvailable(MetricCollector? collector) => collector != null;

    public void Tick(MetricCollector collector) { }

    public float MeasurePanelHeight(MetricCollector collector)
    {
        // 14 rows visible by default + chrome.
        return OverlayLayoutCurrent.ChromeHeight + 14 * RowHeight + 32f + 20f /* filter strip */;
    }

    public void HandleClick(float localX, float localY, MetricCollector collector)
    {
        ProfilerSystem? sys = ModContent.GetInstance<ProfilerSystem>();
        SegmentStore? store = sys?.SegmentStore;
        if (store == null) return;

        // Filter strip click
        float chromeY = OverlayLayoutCurrent.ChromeHeight;
        if (localY >= chromeY && localY < chromeY + 18f)
        {
            CycleFilter();
            return;
        }

        // Row clicks
        float listTopY = chromeY + 22f;
        if (localY < listTopY) return;

        IReadOnlyCollection<Segment> recent = store.Recent;
        var filtered = ApplyFilter(recent);

        int rowOffset = (int)((localY - listTopY) / RowHeight);
        int dataIndex = _scroll + rowOffset;
        if (dataIndex < 0 || dataIndex >= filtered.Count) return;

        Segment clicked = filtered[dataIndex];
        _expandedId = clicked.Id == _expandedId ? LiteDB.ObjectId.Empty : clicked.Id;
    }

    public void HandleScroll(int delta, MetricCollector collector)
    {
        _scroll = Math.Max(0, _scroll + delta);
    }

    public void Draw(SpriteBatch sb, Rectangle area, MetricCollector collector)
    {
        int contentLeft = area.X + (int)OverlayLayoutCurrent.PanelPaddingX;
        int contentRight = area.Right - (int)OverlayLayoutCurrent.PanelPaddingX;
        int contentWidth = contentRight - contentLeft;
        int y = area.Y + (int)OverlayLayoutCurrent.ChromeHeight;

        ProfilerSystem? sys = ModContent.GetInstance<ProfilerSystem>();
        SegmentStore? store = sys?.SegmentStore;
        if (store == null)
        {
            OverlayDraw.Text(sb, "segments not initialised yet",
                new Vector2(contentLeft, y + 6),
                ProfilerTheme.TextMuted, OverlayLayoutCurrent.TextScaleRow);
            return;
        }

        // ---- Filter strip ----
        DrawFilterStrip(sb, new Rectangle(contentLeft, y, contentWidth, 18), store.SessionClosedCount);
        y += 22;

        // ---- Segment list ----
        IReadOnlyCollection<Segment> recent = store.Recent;
        var filtered = ApplyFilter(recent);

        if (filtered.Count == 0)
        {
            string empty = _filter == null
                ? "no segments yet — explore biomes, fight bosses, or wait for weather"
                : $"no {_filter.Value} segments yet";
            OverlayDraw.Text(sb, empty,
                new Vector2(contentLeft + 4, y + 6),
                ProfilerTheme.TextMuted, OverlayLayoutCurrent.TextScaleBody);
            return;
        }

        if (_scroll >= filtered.Count) _scroll = Math.Max(0, filtered.Count - 1);

        int rowsAvailable = (area.Bottom - y) / RowHeight;
        int drawn = 0;

        for (int i = _scroll; i < filtered.Count && drawn < rowsAvailable; i++)
        {
            Segment seg = filtered[i];
            bool expanded = seg.Id == _expandedId;
            int height = expanded ? CardHeightExpanded : RowHeight;

            if (y + height > area.Bottom) break;

            var rowRect = new Rectangle(contentLeft, y, contentWidth, height);
            if (expanded)
            {
                double avg = store.LifetimeAvgMsPerTick(seg.Family, seg.Key);
                int samples = store.LifetimeSampleCount(seg.Family, seg.Key);
                SegmentCard.Draw(sb, rowRect, seg, avg, samples);
            }
            else
            {
                DrawRow(sb, rowRect, seg);
            }

            y += height;
            drawn++;
        }
    }

    private void DrawFilterStrip(SpriteBatch sb, Rectangle area, int total)
    {
        ProfilerTheme.FillRect(sb, area, ProfilerTheme.Header);
        string label = _filter == null ? "all" : _filter.Value.ToString().ToLowerInvariant();
        string txt = $"filter: {label}   ·   {total} segment(s) this session   ·   click to cycle";
        OverlayDraw.Text(sb, txt,
            new Vector2(area.X + 6, area.Y + 3),
            ProfilerTheme.TextMuted, OverlayLayoutCurrent.TextScaleBody);
    }

    private static void DrawRow(SpriteBatch sb, Rectangle row, Segment seg)
    {
        // Background tint when promoted.
        if (seg.Promoted)
        {
            ProfilerTheme.FillRect(sb, row, ProfilerTheme.RowHover);
        }

        float bodyScale = OverlayLayoutCurrent.TextScaleBody;
        float rowScale = OverlayLayoutCurrent.TextScaleRow;

        // Family marker (colored dot-like prefix)
        Color marker = seg.Family switch
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
        ProfilerTheme.FillRect(sb, new Rectangle(row.X + 4, row.Y + 6, 6, row.Height - 12), marker);

        // Name (left) + tag chips (mid) + duration (right)
        int x = row.X + 16;
        OverlayDraw.Text(sb, seg.Name, new Vector2(x, row.Y + 4), ProfilerTheme.Text, rowScale);

        // Tag chips
        int chipX = row.X + row.Width / 2 - 60;
        if (seg.DeathCount > 0)
        {
            OverlayDraw.Text(sb, $"☠ {seg.DeathCount}",
                new Vector2(chipX, row.Y + 5), ProfilerTheme.Danger, bodyScale);
            chipX += 32;
        }
        if (seg.SpikeCount > 0)
        {
            OverlayDraw.Text(sb, $"⚡ {seg.SpikeCount}",
                new Vector2(chipX, row.Y + 5), ProfilerTheme.Amber, bodyScale);
            chipX += 32;
        }
        if (seg.StallCount > 0)
        {
            OverlayDraw.Text(sb, $"⏸ {seg.StallCount}",
                new Vector2(chipX, row.Y + 5), ProfilerTheme.Danger, bodyScale);
        }

        // Duration + ms/t
        string dur = SegmentCard.FormatDuration(seg.DurationMs);
        string ms = $"{seg.AvgFrameMs:F2} ms/t";
        OverlayDraw.Text(sb, dur, new Vector2(row.Right - 110, row.Y + 4), ProfilerTheme.TextMuted, bodyScale);
        OverlayDraw.Text(sb, ms, new Vector2(row.Right - 60, row.Y + 4), ProfilerTheme.TextMuted, bodyScale);
    }

    private List<Segment> ApplyFilter(IReadOnlyCollection<Segment> recent)
    {
        var list = new List<Segment>(recent.Count);
        foreach (var s in recent)
        {
            if (_filter == null || s.Family == _filter.Value) list.Add(s);
        }
        return list;
    }

    private void CycleFilter()
    {
        if (_filter == null) _filter = SegmentFamily.Biome;
        else if ((int)_filter.Value >= (int)SegmentFamily.UserBookmark) _filter = null;
        else _filter = (SegmentFamily)((int)_filter.Value + 1);
        _scroll = 0;
    }
}

#endif
