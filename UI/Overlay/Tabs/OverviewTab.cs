#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.UI.Overlay.Components;

namespace PerformanceProfiler.UI.Overlay.Tabs;

/// <summary>
/// SUMMARY — the multi-dimensional impact view. The default landing tab;
/// designed to answer "what do I have, and what's hurting me most across
/// CPU, allocation, and spike contribution" in a single glance.
///
/// <para>
/// Four regions stack from top to bottom:
/// </para>
/// <list type="number">
///   <item><b>Impact donut</b> — slices sized by composite share; slice hue
///   from the mod-identity palette; inner band tinted by dominant axis
///   (cpu/alloc/spike) so a glance shows WHY each mod is heavy, not just
///   THAT it's heavy. Centre stat: top contributor + share + composite.</item>
///   <item><b>Top-5 contributors strip</b> — composite bar + per-axis
///   component mini-stats on the second line.</item>
///   <item><b>Session trend</b> — three stacked sparklines (cpu frame time,
///   alloc rate, spike markers) on a synchronised x-axis.</item>
///   <item><b>All-mods ranking</b> — every loaded mod (no hidden-low filter
///   by default) with composite bar + component sub-stats. Sort dropdown
///   replaces the old sort chips.</item>
/// </list>
///
/// <para>
/// <b>Relativity.</b> Impact bands derive from <see cref="Baseline.FrameMsMedian"/>,
/// not hardcoded ms thresholds — the old <c>green &lt; 1 ms / amber 1-4 ms /
/// red &gt; 4 ms</c> bands lied on both ends of the hardware spectrum and
/// are gone.
/// </para>
/// </summary>
internal sealed class OverviewTab : IOverlayTab
{
    public string Label => "SUMMARY";

    // ---- Layout constants ----------------------------------------------------
    // Tab content area is everything below the chrome (OverlayLayoutCurrent.ChromeHeight).
    // Sections inside the tab are laid out relative to the tab content top.

    private const float SectionGap = 8f;
    private const float DonutHeight = 220f;
    private const float DonutHeightCompact = 160f;
    private const float SparklineRowHeight = 32f;
    private const float SparklineRowHeightCompact = 22f;
    private const int   AllModsVisibleRows = 12;
    private const int   AllModsVisibleRowsCompact = 8;

    // ---- State --------------------------------------------------------------
    private readonly ModImpactScorer _scorer = new ModImpactScorer();
    private ImpactSortMode _sortMode = ImpactSortMode.Composite;
    private bool _sortDescending = true;
    private bool _hideLowImpact;            // defaults OFF (audit/Caner: show ALL mods like the tree tab)
    private int _scrollOffset;
    private long _tickCounter;

    // Cached mod-name truncations (sized at ProfiledModNames.Length).
    private string[] _truncatedNames = Array.Empty<string>();
    private const int NameTruncateMax = 26;

    // Pre-computed donut slice list — refreshed at the scorer's 1 Hz cadence.
    private readonly List<DonutSlice> _slices = new List<DonutSlice>(12);
    private double _slicesTotal;
    private int _topModId = -1;
    private double _topModComposite;
    private double _topModShare;

    // Sparkline scratch buffers (sized lazily).
    private double[] _frameSeries = Array.Empty<double>();
    private double[] _allocSeries = Array.Empty<double>();
    private readonly List<double> _spikeMarkers = new List<double>(16);
    private double _frameSeriesMax;
    private double _allocSeriesMax;

    // Sort-chip layout: chips along a horizontal strip, click to switch / toggle direction.
    private static readonly (ImpactSortMode Mode, string Label)[] SortChips =
    {
        (ImpactSortMode.Composite, "composite"),
        (ImpactSortMode.Cpu,       "cpu"),
        (ImpactSortMode.Spike,     "spike"),
        (ImpactSortMode.Alloc,     "alloc"),
    };

    public bool IsAvailable(MetricCollector? collector) => collector != null;

    public void Tick(MetricCollector collector)
    {
        _tickCounter++;
        if (!OverlayState.Paused)
        {
            _scorer.Recompute(collector, _sortMode, _sortDescending, _tickCounter);
            RefreshSlices();
            RefreshSparklines(collector);
        }

        int visibleCount = CountVisibleRows();
        int maxOff = Math.Max(0, visibleCount - RowsPerView());
        if (_scrollOffset > maxOff) _scrollOffset = maxOff;
        if (_scrollOffset < 0) _scrollOffset = 0;
    }

    public float MeasurePanelHeight(MetricCollector collector)
    {
        // Tab content geometry:
        //   top row    : impact donut + top-5 contributors (DonutHeight tall)
        //   section gap
        //   sparklines : three stacked rows (SparklineRowHeight each)
        //   section gap
        //   sort chips : 18 px
        //   all-mods   : N rows × row height
        //   footer     : calibration line
        float donutH = DonutHForMode();
        float sparkH = SparklineRowHForMode() * 3 + 8f; // labels in between
        float chipsH = 24f;
        int visibleCount = CountVisibleRows();
        int rowsShown = Math.Min(visibleCount - _scrollOffset, RowsPerView());
        if (rowsShown < 0) rowsShown = 0;
        float rowsH = rowsShown * OverlayLayoutCurrent.RowHeight + (visibleCount > rowsShown ? 14f : 0f);
        float footerH = 14f;

        return OverlayLayoutCurrent.ChromeHeight
             + donutH + SectionGap
             + sparkH + SectionGap
             + chipsH
             + rowsH
             + footerH + 8f;
    }

    public void HandleScroll(int delta, MetricCollector collector)
    {
        int visibleCount = CountVisibleRows();
        int maxOff = Math.Max(0, visibleCount - RowsPerView());
        _scrollOffset = Math.Clamp(_scrollOffset + delta, 0, maxOff);
    }

    public void HandleClick(float localX, float localY, MetricCollector collector)
    {
        // Compute section Ys for hit-testing.
        float topY = OverlayLayoutCurrent.ChromeHeight;
        float donutH = DonutHForMode();
        float sparkH = SparklineRowHForMode() * 3 + 8f;
        float chipsY = topY + donutH + SectionGap + sparkH + SectionGap;

        // Sort chips.
        if (localY >= chipsY && localY < chipsY + 22f)
        {
            float chipFirstX = OverlayLayoutCurrent.PanelPaddingX + 4f;
            float chipSpacing = 84f;
            for (int i = 0; i < SortChips.Length; i++)
            {
                float chipX = chipFirstX + i * chipSpacing;
                if (localX >= chipX - 4f && localX <= chipX + 76f)
                {
                    if (_sortMode == SortChips[i].Mode)
                        _sortDescending = !_sortDescending;
                    else
                    {
                        _sortMode = SortChips[i].Mode;
                        _sortDescending = true;
                    }
                    _scorer.MarkDirty();
                    _scrollOffset = 0;
                    return;
                }
            }
            // Filter chip on the right.
            float filterChipX = OverlayLayoutCurrent.PanelWidth - OverlayLayoutCurrent.PanelPaddingX - 160f;
            if (localX >= filterChipX && localX <= filterChipX + 160f)
            {
                _hideLowImpact = !_hideLowImpact;
                _scrollOffset = 0;
                return;
            }
        }
    }

    // ---- Draw ----------------------------------------------------------------

    public void Draw(SpriteBatch sb, Rectangle area, MetricCollector collector)
    {
        EnsureTruncatedNames();
        // Top of tab content.
        int contentTop = area.Y + (int)OverlayLayoutCurrent.ChromeHeight;
        int contentLeft = area.X + (int)OverlayLayoutCurrent.PanelPaddingX;
        int contentRight = area.Right - (int)OverlayLayoutCurrent.PanelPaddingX;
        int contentWidth = contentRight - contentLeft;

        // ---- Region 1: donut + contributors ----
        int donutH = (int)DonutHForMode();
        Rectangle donutCardRect = new Rectangle(contentLeft, contentTop, donutH + 40, donutH);
        Rectangle contribRect = new Rectangle(donutCardRect.Right + (int)OverlayLayoutCurrent.StatCardGap,
            contentTop, contentRight - donutCardRect.Right - (int)OverlayLayoutCurrent.StatCardGap, donutH);

        DrawDonutCard(sb, donutCardRect, collector);
        DrawContributorsCard(sb, contribRect, collector);

        // ---- Region 2: sparklines ----
        int sparklineY = donutCardRect.Bottom + (int)SectionGap;
        int sparklineRowH = (int)SparklineRowHForMode();
        Rectangle sparkCardRect = new Rectangle(contentLeft, sparklineY,
            contentWidth, sparklineRowH * 3 + 16);
        DrawSparklineCard(sb, sparkCardRect, collector);

        // ---- Region 3: sort chips ----
        int chipsY = sparkCardRect.Bottom + (int)SectionGap;
        DrawSortChips(sb, contentLeft, contentRight, chipsY);

        // ---- Region 4: all-mods ranking ----
        int rowsTop = chipsY + 22;
        DrawAllModsRanking(sb, contentLeft, contentRight, rowsTop, collector);
    }

    // ---- Donut card ----------------------------------------------------------

    private void DrawDonutCard(SpriteBatch sb, Rectangle cardRect, MetricCollector collector)
    {
        Rectangle body = ProfilerCard.Draw(sb, cardRect, "IMPACT SHARE", _scorer.Count > 0 ? $"{_scorer.Count} mods" : null);

        if (_slicesTotal <= 0d || _slices.Count == 0)
        {
            OverlayDraw.Text(sb, "calibrating...", new Vector2(body.X + 12, body.Y + 12),
                ProfilerTheme.TextMuted, OverlayLayoutCurrent.TextScaleRow);
            return;
        }

        // Centre the donut in the body.
        float donutOuterR = Math.Min(body.Width, body.Height) * 0.42f;
        float donutInnerR = donutOuterR * 0.58f;
        Vector2 centre = new Vector2(body.Center.X, body.Center.Y);
        DonutChart.Draw(sb, centre, donutOuterR, donutInnerR, _slices);

        // Centre stat: top contributor name + share.
        if (_topModId >= 0 && _topModId < _truncatedNames.Length)
        {
            string name = _truncatedNames[_topModId];
            string sharePct = $"{_topModShare * 100d:F0}%";
            string composite = $"{_topModComposite:F1} ms";
            float textScale = OverlayLayoutCurrent.TextScaleH2;
            float bodyScale = OverlayLayoutCurrent.TextScaleBody;
            OverlayDraw.Text(sb, name, new Vector2(centre.X - name.Length * 3f, centre.Y - 18),
                ProfilerTheme.Text, bodyScale);
            OverlayDraw.Text(sb, sharePct, new Vector2(centre.X - sharePct.Length * 5f, centre.Y - 4),
                ProfilerTheme.Accent, textScale);
            OverlayDraw.Text(sb, composite, new Vector2(centre.X - composite.Length * 3f, centre.Y + 14),
                ProfilerTheme.TextMuted, bodyScale);
        }

        // Legend in the bottom-left of the body: three coloured dots + axis labels.
        float legendY = body.Bottom - 14;
        DrawLegendDot(sb, body.X + 8, legendY, ProfilerTheme.CpuDominant, "cpu");
        DrawLegendDot(sb, body.X + 8 + 56, legendY, ProfilerTheme.AllocDominant, "alloc");
        DrawLegendDot(sb, body.X + 8 + 56 + 70, legendY, ProfilerTheme.SpikeDominant, "spike");
    }

    private void DrawLegendDot(SpriteBatch sb, float x, float y, Color hue, string label)
    {
        ProfilerTheme.FillRect(sb, new Rectangle((int)x, (int)y + 2, 8, 8), hue);
        OverlayDraw.Text(sb, label, new Vector2(x + 12, y), ProfilerTheme.TextDim,
            OverlayLayoutCurrent.TextScaleBody);
    }

    // ---- Contributors card ---------------------------------------------------

    private void DrawContributorsCard(SpriteBatch sb, Rectangle cardRect, MetricCollector collector)
    {
        Rectangle body = ProfilerCard.Draw(sb, cardRect, "TOP CONTRIBUTORS", null);
        IReadOnlyList<ModImpact> sorted = _scorer.Sorted;
        if (sorted.Count == 0)
        {
            OverlayDraw.Text(sb, "no per-mod data yet", new Vector2(body.X + 12, body.Y + 12),
                ProfilerTheme.TextMuted, OverlayLayoutCurrent.TextScaleRow);
            return;
        }

        int rowsToShow = Math.Min(5, sorted.Count);
        float rowH = (body.Height - 8f) / rowsToShow;
        double maxComposite = sorted[0].Composite;
        if (maxComposite < 0.001d) maxComposite = 0.001d;

        for (int i = 0; i < rowsToShow; i++)
        {
            ModImpact m = sorted[i];
            float y = body.Y + 4 + i * rowH;
            DrawContributorRow(sb, body, m, y, rowH, maxComposite, collector);
        }
    }

    private void DrawContributorRow(SpriteBatch sb, Rectangle body, ModImpact m, float y, float rowH,
        double maxComposite, MetricCollector collector)
    {
        float bodyScale = OverlayLayoutCurrent.TextScaleBody;
        float rowScale = OverlayLayoutCurrent.TextScaleRow;
        int nameLen = 18;
        string name = m.ModId < _truncatedNames.Length ? _truncatedNames[m.ModId] : $"#{m.ModId}";
        if (name.Length > nameLen) name = name.Substring(0, nameLen);

        // Identity dot.
        Color identity = ProfilerTheme.ModColor(m.ModId);
        ProfilerTheme.FillRect(sb, new Rectangle(body.X + 8, (int)y + 6, 8, 8), identity);

        // Name.
        OverlayDraw.Text(sb, name, new Vector2(body.X + 22, y + 2), ProfilerTheme.Text, rowScale);

        // Composite bar.
        int barX = body.X + 22 + nameLen * 7;
        int barW = body.Right - barX - 60;
        if (barW > 20)
        {
            Rectangle barRect = new Rectangle(barX, (int)y + 4, barW, OverlayLayoutCurrent.BarH_Mod);
            double sevFrac = ComputeSeverityFraction(m.Composite, collector);
            HeatBar.Draw(sb, barRect, m.Composite, maxComposite, sevFrac);
        }

        // Composite value.
        OverlayDraw.Text(sb, $"{m.Composite:F1}", new Vector2(body.Right - 50, y + 2),
            ProfilerTheme.Text, rowScale);

        // Component mini-stats on the second line.
        string components = ComposeComponentLine(m);
        OverlayDraw.Text(sb, components, new Vector2(body.X + 22, y + 16),
            ProfilerTheme.TextDim, bodyScale);
    }

    private static string ComposeComponentLine(in ModImpact m)
    {
        string allocStr = m.AllocBytesPerTick >= 1024d
            ? $"{m.AllocBytesPerTick / 1024d:F1}KB"
            : $"{m.AllocBytesPerTick:F0}B";
        return $"cpu {m.CpuMs:F2}ms · alloc {allocStr}/t · spike {m.SpikeMs:F1}ms";
    }

    // ---- Sparkline card ------------------------------------------------------

    private void DrawSparklineCard(SpriteBatch sb, Rectangle cardRect, MetricCollector collector)
    {
        Rectangle body = ProfilerCard.Draw(sb, cardRect, "SESSION TREND  ·  LAST 30 S", null);

        int rowH = (int)SparklineRowHForMode();
        int labelW = 60;
        int sparkX = body.X + labelW + 6;
        int sparkW = body.Right - sparkX - 6;

        // Row 1: frame time
        Rectangle frameSparkRect = new Rectangle(sparkX, body.Y + 4, sparkW, rowH);
        OverlayDraw.Text(sb, "frame", new Vector2(body.X + 6, frameSparkRect.Y + (rowH - 10) / 2),
            ProfilerTheme.TextMuted, OverlayLayoutCurrent.TextScaleBody);
        if (_frameSeries.Length > 0)
        {
            Sparkline.DrawFilledArea(sb, frameSparkRect, _frameSeries, _frameSeriesMax,
                ProfilerTheme.Accent, ProfilerTheme.Accent);
        }

        // Row 2: alloc rate (bars)
        Rectangle allocSparkRect = new Rectangle(sparkX, frameSparkRect.Bottom + 4, sparkW, rowH);
        OverlayDraw.Text(sb, "alloc", new Vector2(body.X + 6, allocSparkRect.Y + (rowH - 10) / 2),
            ProfilerTheme.TextMuted, OverlayLayoutCurrent.TextScaleBody);
        if (_allocSeries.Length > 0 && _allocSeriesMax > 0d)
        {
            Sparkline.DrawBars(sb, allocSparkRect, _allocSeries, _allocSeriesMax, ProfilerTheme.AllocDominant);
        }

        // Row 3: spike markers
        Rectangle spikeSparkRect = new Rectangle(sparkX, allocSparkRect.Bottom + 4, sparkW, rowH);
        OverlayDraw.Text(sb, "spikes", new Vector2(body.X + 6, spikeSparkRect.Y + (rowH - 10) / 2),
            ProfilerTheme.TextMuted, OverlayLayoutCurrent.TextScaleBody);
        Sparkline.DrawMarkers(sb, spikeSparkRect, _spikeMarkers, ProfilerTheme.SpikeDominant);
    }

    // ---- Sort chips + filter ------------------------------------------------

    private void DrawSortChips(SpriteBatch sb, int contentLeft, int contentRight, int y)
    {
        OverlayDraw.Text(sb, "sort by:", new Vector2(contentLeft + 4, y + 4),
            ProfilerTheme.TextMuted, OverlayLayoutCurrent.TextScaleBody);
        float chipFirstX = contentLeft + 60;
        float chipSpacing = 84;
        for (int i = 0; i < SortChips.Length; i++)
        {
            bool active = _sortMode == SortChips[i].Mode;
            Color color = active ? ProfilerTheme.Accent : ProfilerTheme.TextMuted;
            string label = SortChips[i].Label;
            if (active) label += _sortDescending ? " ▼" : " ▲";
            OverlayDraw.Text(sb, label, new Vector2(chipFirstX + i * chipSpacing, y + 4),
                color, OverlayLayoutCurrent.TextScaleBody);
        }

        // Filter chip on the right.
        int filterX = contentRight - 160;
        Rectangle filterChip = new Rectangle(filterX, y, 160, 20);
        Pill.Draw(sb, filterChip,
            _hideLowImpact ? "showing top only" : "showing all mods",
            active: !_hideLowImpact);
    }

    // ---- All-mods ranking ----------------------------------------------------

    private void DrawAllModsRanking(SpriteBatch sb, int contentLeft, int contentRight, int y,
        MetricCollector collector)
    {
        IReadOnlyList<ModImpact> sorted = _scorer.Sorted;
        if (sorted.Count == 0) return;

        double maxComposite = 0d;
        for (int i = 0; i < sorted.Count; i++) if (sorted[i].Composite > maxComposite) maxComposite = sorted[i].Composite;
        if (maxComposite < 0.001d) maxComposite = 0.001d;

        int rowsPerView = RowsPerView();
        int shown = 0;
        int visibleIndex = 0;
        for (int i = 0; i < sorted.Count && shown < rowsPerView; i++)
        {
            ModImpact m = sorted[i];
            if (_hideLowImpact && m.Composite < ComputeLowImpactCutoff(collector)) continue;
            if (visibleIndex >= _scrollOffset)
            {
                DrawAllModsRow(sb, contentLeft, contentRight, y + shown * (int)OverlayLayoutCurrent.RowHeight,
                    m, maxComposite, collector, i + 1);
                shown++;
            }
            visibleIndex++;
        }

        // Footer: total + calibration note.
        int footerY = y + shown * (int)OverlayLayoutCurrent.RowHeight + 4;
        string calNote = _scorer.IsCalibrated
            ? $"calibrated · {_scorer.GcMsPerByte * 1_000_000d:F2} ms/MB"
            : $"calibrating · {_scorer.CalibrationSamples}/{ModImpactScorer.MinCalibrationTicks} ticks";
        OverlayDraw.Text(sb, calNote, new Vector2(contentLeft + 4, footerY),
            _scorer.IsCalibrated ? ProfilerTheme.TextDim : ProfilerTheme.Amber,
            OverlayLayoutCurrent.TextScaleBody);
    }

    private void DrawAllModsRow(SpriteBatch sb, int contentLeft, int contentRight, int y,
        in ModImpact m, double maxComposite, MetricCollector collector, int rank)
    {
        float rowScale = OverlayLayoutCurrent.TextScaleRow;
        float bodyScale = OverlayLayoutCurrent.TextScaleBody;

        // Hover background (subtle). We don't have hover state in IOverlayTab yet; skip.

        // Rank + identity dot.
        OverlayDraw.Text(sb, rank.ToString(), new Vector2(contentLeft + 4, y),
            ProfilerTheme.TextDim, bodyScale);
        Color identity = ProfilerTheme.ModColor(m.ModId);
        ProfilerTheme.FillRect(sb, new Rectangle(contentLeft + 24, y + 4, 8, 8), identity);

        // Name.
        string name = m.ModId < _truncatedNames.Length ? _truncatedNames[m.ModId] : $"mod #{m.ModId}";
        OverlayDraw.Text(sb, name, new Vector2(contentLeft + 38, y - 1),
            ProfilerTheme.Text, rowScale);

        // Composite bar (heat-coloured by severity).
        int barX = contentLeft + 220;
        int barW = contentRight - barX - 160;
        if (barW > 30)
        {
            double sevFrac = ComputeSeverityFraction(m.Composite, collector);
            HeatBar.Draw(sb, new Rectangle(barX, y + 4, barW, OverlayLayoutCurrent.BarH_Mod),
                m.Composite, maxComposite, sevFrac);
        }

        // Composite value.
        OverlayDraw.Text(sb, $"{m.Composite:F2} ms", new Vector2(contentRight - 154, y - 1),
            ColourForBand(m.Composite, collector), rowScale);

        // Component string.
        OverlayDraw.Text(sb, ComposeShortComponents(m), new Vector2(contentRight - 90, y - 1),
            ProfilerTheme.TextDim, bodyScale);
    }

    private static string ComposeShortComponents(in ModImpact m)
    {
        char dom = m.CpuMs >= m.AllocMsEq && m.CpuMs >= m.SpikeMs ? 'c'
                 : m.AllocMsEq >= m.SpikeMs ? 'a' : 's';
        return dom switch
        {
            'c' => "cpu",
            'a' => "alloc",
            _   => "spike",
        };
    }

    // ---- Sparkline + slice refresh -------------------------------------------

    private void RefreshSlices()
    {
        _slices.Clear();
        _slicesTotal = 0d;
        _topModId = -1;
        _topModComposite = 0d;
        _topModShare = 0d;

        IReadOnlyList<ModImpact> sorted = _scorer.Sorted;
        if (sorted.Count == 0) return;

        // Take top 8 by composite, fold the rest into "others".
        const int MaxSlices = 8;
        double othersTotal = 0d;

        for (int i = 0; i < sorted.Count; i++) _slicesTotal += sorted[i].Composite;
        if (_slicesTotal <= 0d) return;

        for (int i = 0; i < sorted.Count; i++)
        {
            ModImpact m = sorted[i];
            if (i < MaxSlices)
            {
                _slices.Add(new DonutSlice
                {
                    Value = m.Composite,
                    SliceColor = ProfilerTheme.ModColor(m.ModId),
                    DominantHue = DominantHueFor(m),
                    Label = null,
                });
            }
            else
            {
                othersTotal += m.Composite;
            }
        }

        if (othersTotal > 0d)
        {
            _slices.Add(new DonutSlice
            {
                Value = othersTotal,
                SliceColor = ProfilerTheme.TextDim,
                DominantHue = ProfilerTheme.TextDim,
                Label = "others",
            });
        }

        _topModId = sorted[0].ModId;
        _topModComposite = sorted[0].Composite;
        _topModShare = _slicesTotal > 0d ? sorted[0].Composite / _slicesTotal : 0d;
    }

    private static Color DominantHueFor(in ModImpact m)
    {
        if (m.SpikeMs >= m.CpuMs && m.SpikeMs >= m.AllocMsEq) return ProfilerTheme.SpikeDominant;
        if (m.AllocMsEq >= m.CpuMs) return ProfilerTheme.AllocDominant;
        return ProfilerTheme.CpuDominant;
    }

    private void RefreshSparklines(MetricCollector collector)
    {
        RingBuffer<TickFrame> history = collector.History;
        int n = history.Count;
        if (n == 0) return;

        if (_frameSeries.Length < n) _frameSeries = new double[n];
        if (_allocSeries.Length < n) _allocSeries = new double[n];

        double frameMax = 0d;
        double allocMax = 0d;
        long firstTick = n > 0 ? history[0].TickIndex : 0;
        long lastTick = n > 0 ? history[n - 1].TickIndex : 0;
        double tickSpan = Math.Max(1d, (double)(lastTick - firstTick));

        // Per-tick alloc total: sum across mods from the per-tick ring.
        bool hasAlloc = collector.TracksAllocations;
        int modCount = HookInterceptor.ProfiledModNames.Length;

        for (int i = 0; i < n; i++)
        {
            TickFrame f = history[i];
            double frame = f.FrameTimeMs;
            _frameSeries[i] = frame;
            if (frame > frameMax) frameMax = frame;

            if (hasAlloc)
            {
                double allocSum = 0d;
                for (int mod = 0; mod < modCount; mod++)
                {
                    allocSum += collector.PerTickRing.GetPerModBytes(f.TickIndex, mod);
                }
                _allocSeries[i] = allocSum;
                if (allocSum > allocMax) allocMax = allocSum;
            }
            else _allocSeries[i] = 0d;
        }

        // Pad above max so the line never touches the top of the box.
        _frameSeriesMax = frameMax > 0d ? frameMax * 1.1d : 1d;
        _allocSeriesMax = allocMax > 0d ? allocMax * 1.1d : 1d;

        // Spike markers — fraction along the visible window.
        _spikeMarkers.Clear();
        IReadOnlyList<SpikeWindow> spikes = collector.Spikes;
        for (int i = 0; i < spikes.Count; i++)
        {
            long t = spikes[i].WorstTick;
            if (t < firstTick || t > lastTick) continue;
            double frac = (t - firstTick) / tickSpan;
            _spikeMarkers.Add(frac);
        }
    }

    // ---- Helpers ------------------------------------------------------------

    private void EnsureTruncatedNames()
    {
        string[] source = HookInterceptor.ProfiledModNames;
        if (_truncatedNames.Length < source.Length)
        {
            string[] grown = new string[source.Length];
            for (int i = 0; i < source.Length; i++)
                grown[i] = OverlayDraw.Truncate(source[i], NameTruncateMax);
            _truncatedNames = grown;
        }
    }

    private int CountVisibleRows()
    {
        if (!_hideLowImpact) return _scorer.Count;
        int c = 0;
        var sorted = _scorer.Sorted;
        for (int i = 0; i < sorted.Count; i++) if (sorted[i].Composite >= ImpactBands.MinInterestingMs) c++;
        return c;
    }

    private int RowsPerView() => OverlayState.Mode == OverlayMode.Compact
        ? AllModsVisibleRowsCompact
        : AllModsVisibleRows;

    private float DonutHForMode() => OverlayState.Mode == OverlayMode.Compact
        ? DonutHeightCompact
        : DonutHeight;

    private float SparklineRowHForMode() => OverlayState.Mode == OverlayMode.Compact
        ? SparklineRowHeightCompact
        : SparklineRowHeight;

    /// <summary>
    /// Severity fraction relative to the user's session baseline frame time.
    /// 1× baseline = 0.5 (mid heat), 4× = 1.0 (full red), 0.25× = 0 (full green).
    /// </summary>
    private static double ComputeSeverityFraction(double valueMs, MetricCollector collector)
    {
        double baseline = collector.Baseline.FrameMsMedian;
        if (baseline <= 0d) return 0.5d;
        double ratio = valueMs / baseline;
        // Map [0, 4×] → [0, 1].
        return Math.Min(1d, ratio / 4d);
    }

    private static Color ColourForBand(double valueMs, MetricCollector collector)
        => ProfilerTheme.CostColor(ComputeSeverityFraction(valueMs, collector));

    private static double ComputeLowImpactCutoff(MetricCollector collector)
    {
        // Relative low-impact threshold: 10% of the baseline frame median.
        // A 25 fps player's "low-impact" mod is anything below 4 ms; a 120 fps
        // player's is below 0.8 ms. Same proportional meaning.
        double baseline = collector.Baseline.FrameMsMedian;
        return baseline > 0d ? baseline * 0.10d : ImpactBands.MinInterestingMs;
    }
}
