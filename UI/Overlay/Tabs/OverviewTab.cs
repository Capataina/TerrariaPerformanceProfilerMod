#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using PerformanceProfiler.Profiling;

namespace PerformanceProfiler.UI.Overlay.Tabs;

/// <summary>
/// The default landing tab: every loaded mod ranked by a single composite
/// "performance impact" score that fuses CPU cost, spike contribution
/// (placeholder until <c>SpikeTracker</c> lands), and allocation pressure
/// converted to ms-equivalent via a self-calibrated GC heuristic.
///
/// <para>
/// Each row shows the composite bar, the absolute ms value, the share of the
/// modlist's total, and (when expanded) three component sub-bars exposing the
/// decomposition. Sort by any axis through the header chips; hide low-cost
/// rows through the filter chip. Colour bands are absolute (Green &lt; 1 ms,
/// Amber 1-4 ms, Red &gt; 4 ms) so a green row means "objectively fine", not
/// "fine relative to this modlist". See <c>context/notes/overview-tab-plan.md</c>
/// for the full design rationale, scoring methodology survey, and risk
/// register.
/// </para>
///
/// <para>
/// Per the project's invariants the tab is descriptive, never normative: it
/// quantifies what each mod costs and shows the calibration factor next to
/// the leaderboard so the user can see and trust the recipe. There is no
/// "core" or "remove this" copy anywhere.
/// </para>
/// </summary>
internal sealed class OverviewTab : IOverlayTab
{
    public string Label => "OVERVIEW";

    // ---- Layout constants ----------------------------------------------------
    // Sort-chip strip sits just below the section header; filter chip sits below
    // that; the leaderboard rows begin at HeaderStripBottomOffset.
    private const float SortStripY      = OverlayLayout.RowsTopOffset - 18f;
    private const float FilterStripY    = OverlayLayout.RowsTopOffset;
    private const float LeaderboardYOff = 22f;
    private const float RowHeight       = 18f;
    private const float SubRowHeight    = 14f;
    private const int   MaxVisibleRows  = 11;
    private const float FooterPadding   = 14f;

    // Pixel positions inside a leaderboard row.
    private const int RowExpandX        = 12;
    private const int RowNameX          = 26;
    private const int RowBarX           = 256;
    private const int RowBarWidth       = 240;
    private const int RowBarHeight      = 10;
    private const int RowValueX         = 506;
    private const int RowShareX         = 580;

    // Sub-row geometry (one per expanded mod row).
    private const int SubLabelX         = 44;
    private const int SubBarX           = 152;
    private const int SubBarWidth       = 200;
    private const int SubBarHeight      = 6;
    private const int SubValueX         = 360;
    private const int SubAnnotationX    = 420;

    // Sort-chip layout (composite / cpu / spike / alloc).
    private static readonly (ImpactSortMode Mode, string Label)[] SortChips =
    {
        (ImpactSortMode.Composite, "composite"),
        (ImpactSortMode.Cpu,       "cpu"),
        (ImpactSortMode.Spike,     "spike"),
        (ImpactSortMode.Alloc,     "alloc"),
    };
    private const float SortChipFirstX = 90f;
    private const float SortChipSpacing = 78f;

    // Filter chip rectangle (panel-local), used for both drawing and hit-testing.
    private static readonly Rectangle FilterChipLocal = new Rectangle(14, (int)FilterStripY, 142, 14);

    // ---- State --------------------------------------------------------------
    private readonly ModImpactScorer _scorer = new ModImpactScorer();
    private ImpactSortMode _sortMode = ImpactSortMode.Composite;
    private bool _sortDescending = true;
    private bool _hideLowImpact = true;
    private readonly HashSet<int> _expanded = new HashSet<int>();
    private int _scrollOffset;
    private long _tickCounter;

    /// <summary>
    /// Truncated display names indexed by ModId. Built lazily on first draw
    /// (HookInterceptor.ProfiledModNames is empty until PostSetupContent runs)
    /// and reused thereafter — names don't change within a session, so the
    /// Substring + ".." that <see cref="OverlayDraw.Truncate"/> would produce
    /// per row per frame is computed exactly once per mod here. The audit's
    /// overlay-ui finding ("Move truncation allocations out of per-row draw
    /// paths") flagged this allocation specifically.
    /// </summary>
    private string[] _truncatedNames = Array.Empty<string>();
    private const int NameTruncateMax = 32;

    private string TruncatedNameFor(int modId)
    {
        string[] source = HookInterceptor.ProfiledModNames;
        if (_truncatedNames.Length < source.Length)
        {
            string[] grown = new string[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                grown[i] = OverlayDraw.Truncate(source[i], NameTruncateMax);
            }
            _truncatedNames = grown;
        }
        return (uint)modId < (uint)_truncatedNames.Length
            ? _truncatedNames[modId]
            : "mod #" + modId;
    }

    // Double-click drill-down state. A second click on the same mod-row within
    // DoubleClickWindowTicks switches to TREE and asks TreeTab to pre-expand
    // that mod (see OverlayState.PreselectedModId). The first click still
    // toggles inline expansion of the OverviewTab row -- the drill is an
    // additional gesture on top, not a replacement.
    private const long DoubleClickWindowTicks = 30; // ~500 ms at 60 fps
    private int _lastClickModId = -1;
    private long _lastClickTick = long.MinValue;

    public bool IsAvailable(MetricCollector? collector) => collector != null;

    // ---- Lifecycle -----------------------------------------------------------

    public void Tick(MetricCollector collector)
    {
        _tickCounter++;
        if (!OverlayState.Paused)
        {
            _scorer.Recompute(collector, _sortMode, _sortDescending, _tickCounter);
        }

        int visibleCount = CountVisibleRows();
        int maxOff = Math.Max(0, visibleCount - MaxVisibleRows);
        if (_scrollOffset > maxOff) _scrollOffset = maxOff;
        if (_scrollOffset < 0) _scrollOffset = 0;
    }

    public float MeasurePanelHeight(MetricCollector collector)
    {
        float h = OverlayLayout.RowsTopOffset + LeaderboardYOff;

        int visibleCount = CountVisibleRows();
        int rendered = Math.Min(visibleCount - _scrollOffset, MaxVisibleRows);
        if (rendered < 0) rendered = 0;

        for (int i = 0; i < rendered; i++)
        {
            h += RowHeight;
            int modId = ResolveVisibleModId(_scrollOffset + i);
            if (modId >= 0 && _expanded.Contains(modId)) h += 3 * SubRowHeight;
        }

        // Hidden-rows summary line if anything is folded by the filter or the scroll.
        // Count each line separately -- Draw renders one line for scroll-hidden
        // and a second for filter-hidden, so we must reserve both heights.
        int hidden = Math.Max(0, visibleCount - _scrollOffset - rendered);
        int filtered = _scorer.Count - visibleCount;
        if (hidden > 0) h += 14f;
        if (filtered > 0) h += 14f;

        // Footer (calibration note) always present.
        h += FooterPadding + 12f;
        return h;
    }

    public void HandleScroll(int delta, MetricCollector collector)
    {
        int visibleCount = CountVisibleRows();
        int maxOff = Math.Max(0, visibleCount - MaxVisibleRows);
        _scrollOffset = Math.Clamp(_scrollOffset + delta, 0, maxOff);
    }

    public void HandleClick(float localX, float localY, MetricCollector collector)
    {
        // Sort-chip strip.
        if (localY >= SortStripY && localY < SortStripY + 14f)
        {
            for (int i = 0; i < SortChips.Length; i++)
            {
                float chipX = SortChipFirstX + i * SortChipSpacing;
                if (localX >= chipX - 4f && localX <= chipX + 72f)
                {
                    if (_sortMode == SortChips[i].Mode)
                    {
                        _sortDescending = !_sortDescending;
                    }
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
            return;
        }

        // Filter chip strip.
        if (FilterChipLocal.Contains((int)localX, (int)localY))
        {
            _hideLowImpact = !_hideLowImpact;
            _scrollOffset = 0;
            return;
        }

        // Leaderboard rows.
        float rowY = OverlayLayout.RowsTopOffset + LeaderboardYOff;
        int rendered = Math.Min(CountVisibleRows() - _scrollOffset, MaxVisibleRows);
        if (rendered < 0) rendered = 0;
        for (int i = 0; i < rendered; i++)
        {
            int modId = ResolveVisibleModId(_scrollOffset + i);
            if (localY >= rowY && localY < rowY + RowHeight && modId >= 0)
            {
                // Double-click on the same row → drill into TREE.
                bool isDoubleClick = modId == _lastClickModId
                                  && (_tickCounter - _lastClickTick) <= DoubleClickWindowTicks;
                if (isDoubleClick)
                {
                    OverlayState.PreselectedModId = modId;
                    OverlayState.ActiveTabIndex = 1; // TREE
                    _lastClickModId = -1;
                    _lastClickTick = long.MinValue;
                    return;
                }

                // Single click → toggle the inline component breakdown.
                if (!_expanded.Remove(modId)) _expanded.Add(modId);
                _lastClickModId = modId;
                _lastClickTick = _tickCounter;
                return;
            }
            rowY += RowHeight;
            if (modId >= 0 && _expanded.Contains(modId)) rowY += 3 * SubRowHeight;
        }
    }

    // ---- Drawing -------------------------------------------------------------

    public void Draw(SpriteBatch sb, Rectangle area, MetricCollector collector)
    {
        DrawSectionHeader(sb, area);
        DrawSortChips(sb, area);
        DrawFilterChip(sb, area);

        if (_scorer.Count == 0)
        {
            OverlayDraw.Text(sb, "no per-mod data yet",
                new Vector2(area.X + 14, area.Y + OverlayLayout.RowsTopOffset + LeaderboardYOff),
                ProfilerTheme.TextMuted, 0.72f);
            DrawFooter(sb, area, area.Y + OverlayLayout.RowsTopOffset + LeaderboardYOff + 28f, collector);
            return;
        }

        int visibleCount = CountVisibleRows();
        int rendered = Math.Min(visibleCount - _scrollOffset, MaxVisibleRows);
        if (rendered < 0) rendered = 0;

        // Determine the leaderboard's maximum composite so the bars share a scale.
        double maxComposite = 0d;
        IReadOnlyList<ModImpact> sorted = _scorer.Sorted;
        for (int i = 0; i < sorted.Count; i++)
        {
            double v = sorted[i].Composite;
            if (v > maxComposite) maxComposite = v;
        }
        if (maxComposite < 0.001d) maxComposite = 0.001d;

        float rowY = area.Y + OverlayLayout.RowsTopOffset + LeaderboardYOff;
        float mouseY = Main.MouseScreen.Y;

        for (int i = 0; i < rendered; i++)
        {
            int sortedIndex = ResolveVisibleSortedIndex(_scrollOffset + i);
            if (sortedIndex < 0) break;
            ModImpact impact = sorted[sortedIndex];

            bool expanded = _expanded.Contains(impact.ModId);
            bool hovered  = mouseY >= rowY && mouseY < rowY + RowHeight;

            if (hovered)
            {
                ProfilerTheme.FillRect(sb,
                    new Rectangle(area.X + 1, (int)rowY, area.Width - 2, (int)RowHeight),
                    ProfilerTheme.RowHover);
            }

            DrawLeaderboardRow(sb, area.X, rowY, impact, maxComposite, expanded);
            rowY += RowHeight;

            if (expanded)
            {
                double rowMax = ComponentBarMax(impact);
                DrawComponentRow(sb, area.X, rowY, "cpu",   impact.CpuMs,     rowMax, string.Empty,                          true);
                rowY += SubRowHeight;
                DrawComponentRow(sb, area.X, rowY, "spike", impact.SpikeMs,   rowMax, "pending (sibling SpikeTracker)",       false);
                rowY += SubRowHeight;
                DrawComponentRow(sb, area.X, rowY, "alloc", impact.AllocMsEq, rowMax, AllocComponentAnnotation(impact),       _scorer.IsCalibrated && impact.AllocBytesPerTick > 0d);
                rowY += SubRowHeight;
            }
        }

        // "+ N more" summary if the filter or scroll hid anything.
        int filtered = _scorer.Count - visibleCount;
        int unrendered = Math.Max(0, visibleCount - _scrollOffset - rendered);
        if (unrendered > 0)
        {
            OverlayDraw.Text(sb, $"+ {unrendered} more (scroll)",
                new Vector2(area.X + 14, rowY + 1f), ProfilerTheme.TextDim, 0.62f);
            rowY += 14f;
        }
        if (filtered > 0)
        {
            string msg = $"+ {filtered} hidden  (< {ImpactBands.MinInterestingMs:F1} ms threshold)";
            OverlayDraw.Text(sb, msg,
                new Vector2(area.X + 14, rowY + 1f), ProfilerTheme.TextDim, 0.62f);
            rowY += 14f;
        }

        DrawFooter(sb, area, rowY + FooterPadding, collector);
    }

    // ---- Header / chips ------------------------------------------------------

    private static void DrawSectionHeader(SpriteBatch sb, Rectangle area)
    {
        int divY = area.Y + (int)OverlayLayout.DividerOffset;
        ProfilerTheme.FillRect(sb, new Rectangle(area.X + 8, divY, area.Width - 16, 1), ProfilerTheme.Border);
        ProfilerTheme.FillRect(sb, new Rectangle(area.X + 8, divY + 5, 2, 14), ProfilerTheme.Accent);
        OverlayDraw.Text(sb, "IMPACT LEADERBOARD   ·   click a row to expand",
            new Vector2(area.X + 18, divY + 6f), ProfilerTheme.Accent, 0.72f);
    }

    private void DrawSortChips(SpriteBatch sb, Rectangle area)
    {
        float y = area.Y + SortStripY;
        OverlayDraw.Text(sb, "sort by:", new Vector2(area.X + 14, y), ProfilerTheme.TextDim, 0.62f);
        for (int i = 0; i < SortChips.Length; i++)
        {
            bool active = _sortMode == SortChips[i].Mode;
            Color colour = active ? ProfilerTheme.Accent : ProfilerTheme.TextMuted;
            string label = active ? SortChips[i].Label + (_sortDescending ? " v" : " ^") : SortChips[i].Label;
            OverlayDraw.Text(sb, label,
                new Vector2(area.X + SortChipFirstX + i * SortChipSpacing, y),
                colour, 0.64f);
        }
    }

    private void DrawFilterChip(SpriteBatch sb, Rectangle area)
    {
        Rectangle screen = new Rectangle(area.X + FilterChipLocal.X, area.Y + FilterChipLocal.Y,
            FilterChipLocal.Width, FilterChipLocal.Height);
        Color border = _hideLowImpact ? ProfilerTheme.Accent : ProfilerTheme.Border;
        Color label  = _hideLowImpact ? ProfilerTheme.Accent : ProfilerTheme.TextMuted;
        ProfilerTheme.DrawBorder(sb, screen, border);
        OverlayDraw.Text(sb, _hideLowImpact ? $"hide < {ImpactBands.MinInterestingMs:F1} ms  [on]" : $"hide < {ImpactBands.MinInterestingMs:F1} ms  [off]",
            new Vector2(screen.X + 6, screen.Y + 1), label, 0.58f);

        OverlayDraw.Text(sb, "bands: green <1 ms  ·  amber 1-4 ms  ·  red >4 ms",
            new Vector2(area.X + 180, area.Y + FilterStripY + 1f), ProfilerTheme.TextDim, 0.58f);
    }

    // ---- Row drawing ---------------------------------------------------------

    private void DrawLeaderboardRow(SpriteBatch sb, int panelX, float y, in ModImpact impact, double maxComposite, bool expanded)
    {
        // Mod names are session-stable, so the truncated form is computed once
        // per mod into _truncatedNames and reused thereafter. Avoids the per-row,
        // per-frame Substring allocation the audit flagged in overlay-ui.md.
        string name = TruncatedNameFor(impact.ModId);

        OverlayDraw.Text(sb, expanded ? "-" : "+", new Vector2(panelX + RowExpandX, y + 2), ProfilerTheme.Accent, 0.72f);
        OverlayDraw.Text(sb, name, new Vector2(panelX + RowNameX, y + 2), BandTextColor(impact.Band), 0.78f);

        DrawCompositeBar(sb, panelX + RowBarX, (int)y + 4, impact.Composite, maxComposite, impact.Band, RowBarHeight);
        OverlayDraw.Text(sb, impact.Composite.ToString("F2") + " ms", new Vector2(panelX + RowValueX, y + 2), BandTextColor(impact.Band), 0.72f);

        if (impact.ShareOfTotal > 0d)
        {
            OverlayDraw.Text(sb, $"{impact.ShareOfTotal * 100d:F0}%",
                new Vector2(panelX + RowShareX, y + 2), ProfilerTheme.TextMuted, 0.66f);
        }
    }

    private void DrawComponentRow(SpriteBatch sb, int panelX, float y,
        string label, double value, double localMax, string annotation, bool active)
    {
        Color textColor = active ? ProfilerTheme.TextMuted : ProfilerTheme.TextDim;
        Color barColor  = active ? ProfilerTheme.CostColor(localMax > 0d ? value / localMax : 0d) : ProfilerTheme.Border;

        OverlayDraw.Text(sb, label, new Vector2(panelX + SubLabelX, y), textColor, 0.62f);
        ProfilerTheme.FillRect(sb,
            new Rectangle(panelX + SubBarX, (int)y + 3, SubBarWidth, SubBarHeight),
            ProfilerTheme.Border);
        if (active && localMax > 0d && value > 0d)
        {
            int fill = (int)(SubBarWidth * Math.Clamp(value / localMax, 0d, 1d));
            if (fill > 0)
            {
                ProfilerTheme.FillRect(sb,
                    new Rectangle(panelX + SubBarX, (int)y + 3, fill, SubBarHeight),
                    barColor);
            }
        }

        OverlayDraw.Text(sb, active ? value.ToString("F2") + " ms" : "--",
            new Vector2(panelX + SubValueX, y), textColor, 0.6f);

        if (!string.IsNullOrEmpty(annotation))
        {
            OverlayDraw.Text(sb, annotation, new Vector2(panelX + SubAnnotationX, y), ProfilerTheme.TextDim, 0.56f);
        }
    }

    private static void DrawCompositeBar(SpriteBatch sb, int barX, int barY, double value, double max, ImpactBand band, int height)
    {
        ProfilerTheme.FillRect(sb, new Rectangle(barX, barY, RowBarWidth, height), ProfilerTheme.Border);
        if (max <= 0d || value <= 0d) return;
        double fraction = value / max;
        if (fraction < 0d) fraction = 0d;
        if (fraction > 1d) fraction = 1d;
        int fillW = (int)(RowBarWidth * fraction);
        if (fillW <= 0) return;
        ProfilerTheme.FillRect(sb, new Rectangle(barX, barY, fillW, height), BandBarColor(band));
    }

    // ---- Footer --------------------------------------------------------------

    private void DrawFooter(SpriteBatch sb, Rectangle area, float y, MetricCollector collector)
    {
        ProfilerTheme.FillRect(sb,
            new Rectangle(area.X + 8, (int)y - 4, area.Width - 16, 1),
            ProfilerTheme.Border);

        string calibrationText;
        if (collector.PerModCategoryAverageBytes == null)
        {
            calibrationText = "allocation tracking: off  ·  composite = cpu only";
        }
        else if (!_scorer.IsCalibrated)
        {
            double secs = _scorer.CalibrationSamples / 60d;
            calibrationText = $"calibrating allocation factor  ·  {secs:F1} s of history";
        }
        else if (_scorer.GcCalibratedFromHistory)
        {
            calibrationText = $"alloc-to-ms factor: {_scorer.GcMsPerByte:0.##e+0} ms/byte  ·  auto-calibrated, {_scorer.CalibrationSamples / 60d:F0} s window";
        }
        else
        {
            calibrationText = $"alloc-to-ms factor: {_scorer.GcMsPerByte:0.##e+0} ms/byte  ·  fallback (no GC observed in window)";
        }
        OverlayDraw.Text(sb, calibrationText, new Vector2(area.X + 14, y), ProfilerTheme.TextDim, 0.58f);
    }

    // ---- Helpers -------------------------------------------------------------

    private int CountVisibleRows()
    {
        if (!_hideLowImpact) return _scorer.Count;
        IReadOnlyList<ModImpact> sorted = _scorer.Sorted;
        int n = 0;
        for (int i = 0; i < sorted.Count; i++)
        {
            if (sorted[i].Composite >= ImpactBands.MinInterestingMs) n++;
        }
        return n;
    }

    private int ResolveVisibleSortedIndex(int visibleIndex)
    {
        IReadOnlyList<ModImpact> sorted = _scorer.Sorted;
        if (!_hideLowImpact)
        {
            return visibleIndex < sorted.Count ? visibleIndex : -1;
        }

        int seen = 0;
        for (int i = 0; i < sorted.Count; i++)
        {
            if (sorted[i].Composite < ImpactBands.MinInterestingMs) continue;
            if (seen == visibleIndex) return i;
            seen++;
        }
        return -1;
    }

    private int ResolveVisibleModId(int visibleIndex)
    {
        int sortedIdx = ResolveVisibleSortedIndex(visibleIndex);
        if (sortedIdx < 0) return -1;
        return _scorer.Sorted[sortedIdx].ModId;
    }

    private static double ComponentBarMax(in ModImpact impact)
    {
        // Scale sub-bars to whichever of this row's three components is biggest,
        // so the bars are internally comparable for that mod. This is the
        // per-row scaling discussed in plan §8.2.
        double m = impact.CpuMs;
        if (impact.SpikeMs > m) m = impact.SpikeMs;
        if (impact.AllocMsEq > m) m = impact.AllocMsEq;
        return m;
    }

    private static string AllocComponentAnnotation(in ModImpact impact) =>
        impact.AllocBytesPerTick > 0d
            ? $"({OverlayDraw.FormatBytes(impact.AllocBytesPerTick)}/tick)"
            : string.Empty;

    private static Color BandBarColor(ImpactBand band) => band switch
    {
        ImpactBand.Green => ProfilerTheme.Good,
        ImpactBand.Amber => ProfilerTheme.Amber,
        ImpactBand.Red   => ProfilerTheme.Danger,
        _ => ProfilerTheme.TextMuted,
    };

    private static Color BandTextColor(ImpactBand band) => band switch
    {
        ImpactBand.Red   => ProfilerTheme.Danger,
        ImpactBand.Amber => ProfilerTheme.Amber,
        _ => ProfilerTheme.Text,
    };
}
