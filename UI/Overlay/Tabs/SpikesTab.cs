#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PerformanceProfiler.Profiling;

namespace PerformanceProfiler.UI.Overlay.Tabs;

/// <summary>
/// The "lag events" feed: every <see cref="SpikeWindow"/> from
/// <see cref="SpikeDetector"/> and every <see cref="StallEvent"/> from
/// <see cref="StallDetector"/>, merged by tick index, newest first.
///
/// <para>
/// The tab is called "SPIKES" in the strip because that's what the player
/// thinks they're seeing — "lag spikes". Underneath it surfaces two
/// different phenomena with different data models:
/// </para>
/// <list type="bullet">
///   <item><b>● Spike</b> — a tick whose own work took too long. We know
///   who caused it (per-mod attribution frozen at the worst tick).</item>
///   <item><b>▲ Stall</b> — wall time elapsed without the game advancing.
///   GC pause, OS suspended the process, or a draw-thread hook blocked.
///   We can't attribute to a mod from inside the gap, but we classify the
///   cause from GC / CPU / heap signals.</item>
/// </list>
///
/// <para>
/// Stall rows are highlighted by severity per <see cref="StallSeverity"/>:
/// Minor (text), Noticeable (amber), Disruptive (amber + bold dot),
/// Freeze (red). Spike rows are coloured by absolute worst-frame ms because
/// the per-row colour answers "did this hurt to play through" and a spike
/// hurts at the same wall threshold regardless of FPS.
/// </para>
/// </summary>
internal sealed class SpikesTab : IOverlayTab
{
    public string Label => "SPIKES";

    private const float RowHeight = 32f;
    private const int VisibleRows = 8;

    private int _scrollOffset;

    public bool IsAvailable(MetricCollector? collector) => collector != null && collector.History.Count > 0;

    public void Tick(MetricCollector collector)
    {
        int total = collector.Spikes.Count + collector.Stalls.Count;
        int maxOff = Math.Max(0, total - VisibleRows);
        if (_scrollOffset > maxOff) _scrollOffset = maxOff;
    }

    public float MeasurePanelHeight(MetricCollector collector)
    {
        int total = collector.Spikes.Count + collector.Stalls.Count;
        int rowsToShow = Math.Min(total - _scrollOffset, VisibleRows);
        if (rowsToShow < 0) rowsToShow = 0;

        float h = OverlayLayout.RowsTopOffset + 10f;
        if (rowsToShow == 0)
        {
            h += 24f;
        }
        else
        {
            h += rowsToShow * RowHeight;
            if (total > _scrollOffset + rowsToShow) h += 14f;
        }
        return h;
    }

    public void HandleClick(float localX, float localY, MetricCollector collector)
    {
        // Per-row drill-down is a future feature. No-op for contract compliance.
    }

    public void HandleScroll(int delta, MetricCollector collector)
    {
        int total = collector.Spikes.Count + collector.Stalls.Count;
        int maxOff = Math.Max(0, total - VisibleRows);
        _scrollOffset = Math.Clamp(_scrollOffset + delta, 0, maxOff);
    }

    public void Draw(SpriteBatch sb, Rectangle area, MetricCollector collector)
    {
        int divY = area.Y + (int)OverlayLayout.DividerOffset;
        ProfilerTheme.FillRect(sb, new Rectangle(area.X + 8, divY, area.Width - 16, 1), ProfilerTheme.Border);
        ProfilerTheme.FillRect(sb, new Rectangle(area.X + 8, divY + 5, 2, 14), ProfilerTheme.Accent);

        IReadOnlyList<SpikeWindow> spikes = collector.Spikes;
        IReadOnlyList<StallEvent> stalls = collector.Stalls;
        int total = spikes.Count + stalls.Count;

        string header = total == 0
            ? "LAG EVENTS   ·   none yet — play a session"
            : $"LAG EVENTS   ·   {spikes.Count} spikes + {stalls.Count} stalls (newest first)";
        OverlayDraw.Text(sb, header, new Vector2(area.X + 18, divY + 6f), ProfilerTheme.Accent, 0.72f);

        if (total == 0)
        {
            OverlayDraw.Text(sb, "● spike: tick over your session median   ▲ stall: wall time without game progress",
                new Vector2(area.X + 18, area.Y + OverlayLayout.RowsTopOffset + 4),
                ProfilerTheme.TextDim, 0.58f);
            return;
        }

        // Walk both lists newest-first, picking the row with the highest tick
        // index at each step. Allocation-free; bounded by VisibleRows draws.
        int spikeIdx = spikes.Count - 1;
        int stallIdx = stalls.Count - 1;
        int skipped = 0;
        int drawn = 0;
        float rowY = area.Y + OverlayLayout.RowsTopOffset;

        while (drawn < VisibleRows && (spikeIdx >= 0 || stallIdx >= 0))
        {
            long spikeT = spikeIdx >= 0 ? spikes[spikeIdx].WorstTick : long.MinValue;
            long stallT = stallIdx >= 0 ? stalls[stallIdx].StartTickIndex : long.MinValue;

            if (spikeT >= stallT)
            {
                if (skipped >= _scrollOffset)
                {
                    DrawSpikeRow(sb, area, spikes[spikeIdx], rowY);
                    rowY += RowHeight;
                    drawn++;
                }
                else { skipped++; }
                spikeIdx--;
            }
            else
            {
                if (skipped >= _scrollOffset)
                {
                    DrawStallRow(sb, area, stalls[stallIdx], rowY);
                    rowY += RowHeight;
                    drawn++;
                }
                else { skipped++; }
                stallIdx--;
            }
        }

        int remaining = total - _scrollOffset - drawn;
        if (remaining > 0)
        {
            OverlayDraw.Text(sb, $"+ {remaining} older",
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

        // Kind glyph: bullet for spikes. Coloured to match severity so the
        // strip reads at a glance.
        OverlayDraw.Text(sb, "●", new Vector2(area.X + 12, y), severityColor, 0.78f);

        string line1 = duration > 1
            ? $"tick {w.StartTick}..{w.EndTick}  ({duration} ticks)"
            : $"tick {w.WorstTick}";
        OverlayDraw.Text(sb, line1, new Vector2(area.X + 28, y),
            w.Warming ? ProfilerTheme.TextMuted : ProfilerTheme.Text, 0.7f);

        string line1right = $"{w.WorstFrameMs:F1} ms  ·  {multiplier:F1}× median {w.BaselineMs:F1}";
        OverlayDraw.Text(sb, line1right, new Vector2(area.X + 280f, y), severityColor, 0.7f);

        if (w.Warming)
        {
            OverlayDraw.Text(sb, "warming", new Vector2(area.X + 540f, y), ProfilerTheme.TextDim, 0.58f);
        }

        string contributor = TopContributorLabel(w);
        OverlayDraw.Text(sb, contributor, new Vector2(area.X + 30f, y + 15f), ProfilerTheme.TextMuted, 0.62f);
    }

    private static void DrawStallRow(SpriteBatch sb, Rectangle area, StallEvent s, float y)
    {
        Color severityColor = s.Severity switch
        {
            StallSeverity.Freeze     => ProfilerTheme.Danger,
            StallSeverity.Disruptive => ProfilerTheme.Danger,
            StallSeverity.Noticeable => ProfilerTheme.Amber,
            _                        => ProfilerTheme.Text,
        };

        // Kind glyph: triangle for stalls — the "something stopped" signal.
        OverlayDraw.Text(sb, "▲", new Vector2(area.X + 12, y), severityColor, 0.78f);

        string line1 = $"tick {s.StartTickIndex}";
        OverlayDraw.Text(sb, line1, new Vector2(area.X + 28, y),
            s.Warming ? ProfilerTheme.TextMuted : ProfilerTheme.Text, 0.7f);

        // Format the absolute wall duration; stalls run hundreds-of-ms to
        // seconds so format both ranges sensibly.
        string period = s.TickPeriodMs >= 1000d
            ? $"{s.TickPeriodMs / 1000d:F2} s"
            : $"{s.TickPeriodMs:F0} ms";
        string line1right = $"{period}  ·  {s.Severity.ToString().ToLowerInvariant()}";
        OverlayDraw.Text(sb, line1right, new Vector2(area.X + 280f, y), severityColor, 0.7f);

        if (s.Warming)
        {
            OverlayDraw.Text(sb, "warming", new Vector2(area.X + 540f, y), ProfilerTheme.TextDim, 0.58f);
        }

        // Second line: cause + the strongest diagnostic detail for that cause.
        string detail = StallDetailLine(s);
        OverlayDraw.Text(sb, detail, new Vector2(area.X + 30f, y + 15f), ProfilerTheme.TextMuted, 0.62f);
    }

    /// <summary>
    /// Returns the most useful per-cause detail string for a stall: GC pause
    /// magnitude for GC causes, CPU-suspend ratio for ProcessSuspended,
    /// gen counts for code stalls.
    /// </summary>
    private static string StallDetailLine(StallEvent s)
    {
        switch (s.Cause)
        {
            case StallCause.MajorGc:
            {
                double heapMb = (s.HeapSizeBeforeBytes - s.HeapSizeAfterBytes) / (1024d * 1024d);
                return heapMb > 1d
                    ? $"major GC  ·  gen2 fired  ·  heap freed {heapMb:F0} MB ({s.GcPauseDurationMs:F0} ms paused)"
                    : $"major GC  ·  gen2 fired  ·  {s.GcPauseDurationMs:F0} ms paused";
            }
            case StallCause.MinorGc:
                return $"minor GC  ·  {s.Gen0Collections + s.Gen1Collections} collections  ·  {s.GcPauseDurationMs:F0} ms paused";
            case StallCause.ProcessSuspended:
                return $"process suspended  ·  CPU advanced only {s.ProcessCpuTimeDeltaMs:F0} ms of {s.TickPeriodMs:F0} ms wall";
            case StallCause.LongFrame:
                return s.GcPauseDurationMs > 1d
                    ? $"code stall  ·  CPU {s.ProcessCpuTimeDeltaMs:F0} ms  ·  some GC ({s.GcPauseDurationMs:F0} ms)"
                    : $"code stall  ·  CPU {s.ProcessCpuTimeDeltaMs:F0} ms";
            default:
                return $"unknown cause  ·  wall {s.TickPeriodMs:F0} ms / cpu {s.ProcessCpuTimeDeltaMs:F0} ms / gc {s.GcPauseDurationMs:F0} ms";
        }
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
