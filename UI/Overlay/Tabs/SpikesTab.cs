#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PerformanceProfiler.Profiling;

namespace PerformanceProfiler.UI.Overlay.Tabs;

/// <summary>
/// The recent-spikes feed: every <see cref="SpikeWindow"/>
/// <see cref="SpikeDetector"/> has captured this session, newest first.
/// Each row shows tick range, worst frame time vs the median baseline, the
/// top contributor from the frozen per-mod snapshot, and (when first-launch
/// warmup) a "warming" badge. Read-only for now; per-spike drill-down lands
/// when the design needs it.
/// </summary>
internal sealed class SpikesTab : IOverlayTab
{
    public string Label => "SPIKES";

    private const float SpikeRowHeight = 32f;
    private const int SpikesVisibleRows = 8;

    private int _scrollOffset;

    public bool IsAvailable(MetricCollector? collector) => collector != null && collector.History.Count > 0;

    public void Tick(MetricCollector collector)
    {
        int spikeCount = collector.Spikes.Count;
        int maxOff = Math.Max(0, spikeCount - SpikesVisibleRows);
        if (_scrollOffset > maxOff) _scrollOffset = maxOff;
    }

    public float MeasurePanelHeight(MetricCollector collector)
    {
        int captured = collector.Spikes.Count;
        int rowsToShow = Math.Min(captured - _scrollOffset, SpikesVisibleRows);
        if (rowsToShow < 0) rowsToShow = 0;

        float h = OverlayLayout.RowsTopOffset + 10f;
        if (rowsToShow == 0)
        {
            h += 24f; // empty-state hint
        }
        else
        {
            h += rowsToShow * SpikeRowHeight;
            if (captured > _scrollOffset + rowsToShow) h += 14f;
        }
        return h;
    }

    public void HandleClick(float localX, float localY, MetricCollector collector)
    {
        // No per-row click handler yet -- drill-down is a future feature.
        // Defined as no-op so the interface contract is satisfied.
    }

    public void HandleScroll(int delta, MetricCollector collector)
    {
        int spikeCount = collector.Spikes.Count;
        int maxOff = Math.Max(0, spikeCount - SpikesVisibleRows);
        _scrollOffset = Math.Clamp(_scrollOffset + delta, 0, maxOff);
    }

    public void Draw(SpriteBatch sb, Rectangle area, MetricCollector collector)
    {
        int divY = area.Y + (int)OverlayLayout.DividerOffset;
        ProfilerTheme.FillRect(sb, new Rectangle(area.X + 8, divY, area.Width - 16, 1), ProfilerTheme.Border);
        ProfilerTheme.FillRect(sb, new Rectangle(area.X + 8, divY + 5, 2, 14), ProfilerTheme.Accent);

        IReadOnlyList<SpikeWindow> spikes = collector.Spikes;
        string header = spikes.Count == 0
            ? "RECENT SPIKES   ·   none yet — play a session"
            : $"RECENT SPIKES   ·   {spikes.Count} captured (newest first)";
        OverlayDraw.Text(sb, header, new Vector2(area.X + 18, divY + 6f), ProfilerTheme.Accent, 0.72f);

        if (spikes.Count == 0)
        {
            OverlayDraw.Text(sb, "spikes appear when a tick exceeds 2× the rolling median (and ≥ 5 ms)",
                new Vector2(area.X + 18, area.Y + OverlayLayout.RowsTopOffset + 4),
                ProfilerTheme.TextDim, 0.6f);
            return;
        }

        float rowY = area.Y + OverlayLayout.RowsTopOffset;
        int visible = Math.Min(spikes.Count - _scrollOffset, SpikesVisibleRows);
        for (int i = 0; i < visible; i++)
        {
            int idx = spikes.Count - 1 - (_scrollOffset + i);
            DrawSpikeRow(sb, area, spikes[idx], rowY);
            rowY += SpikeRowHeight;
        }

        if (_scrollOffset + visible < spikes.Count)
        {
            OverlayDraw.Text(sb, $"+ {spikes.Count - _scrollOffset - visible} older",
                new Vector2(area.X + 18, rowY + 2f), ProfilerTheme.TextDim, 0.6f);
        }
    }

    private static void DrawSpikeRow(SpriteBatch sb, Rectangle area, SpikeWindow w, float y)
    {
        int duration = (int)(w.EndTick - w.StartTick + 1);
        double multiplier = w.BaselineMs > 0d ? w.WorstFrameMs / w.BaselineMs : 0d;
        Color severityColor = w.WorstFrameMs >= 32d ? ProfilerTheme.Danger
                            : w.WorstFrameMs >= 16d ? ProfilerTheme.Amber
                            : ProfilerTheme.Text;

        string line1 = duration > 1
            ? $"tick {w.StartTick}..{w.EndTick}  ({duration} ticks)"
            : $"tick {w.WorstTick}";
        OverlayDraw.Text(sb, line1, new Vector2(area.X + 18, y),
            w.Warming ? ProfilerTheme.TextMuted : ProfilerTheme.Text, 0.7f);

        string line1right = $"{w.WorstFrameMs:F1} ms   ({multiplier:F1}× median {w.BaselineMs:F1})";
        OverlayDraw.Text(sb, line1right, new Vector2(area.X + 280f, y), severityColor, 0.7f);

        if (w.Warming)
        {
            OverlayDraw.Text(sb, "warming", new Vector2(area.X + 540f, y), ProfilerTheme.TextDim, 0.58f);
        }

        string contributor = TopContributorLabel(w);
        OverlayDraw.Text(sb, contributor, new Vector2(area.X + 30f, y + 15f), ProfilerTheme.TextMuted, 0.62f);
    }

    private static string TopContributorLabel(SpikeWindow w)
    {
        string[] names = HookInterceptor.ProfiledModNames;
        int catCount = PerModAttribution.CategoryCount;
        int modCount = w.PerModCatMs.Length / catCount;
        if (modCount > names.Length) modCount = names.Length;

        int bestMod = -1;
        double bestMs = 0d;
        for (int mod = 0; mod < modCount; mod++)
        {
            double sum = 0d;
            int baseIdx = mod * catCount;
            for (int c = 0; c < catCount; c++) sum += w.PerModCatMs[baseIdx + c];
            if (sum > bestMs) { bestMs = sum; bestMod = mod; }
        }
        if (bestMod < 0) return "(no per-mod attribution captured)";
        return $"top: {names[bestMod]}   {bestMs:F2} ms";
    }
}
