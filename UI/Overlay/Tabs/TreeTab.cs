// ARCHIVED v0.9.0: in-game overlay replaced by browser dashboard.
// Sources retained for possible Steam-Deck variant or future revival;
// remove the #if false / #endif guards below to restore compilation.
#if false
#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using PerformanceProfiler.Profiling;

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Data.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Profiling.Persistence;
using PerformanceProfiler.Profiling.Persistence.Records;
namespace PerformanceProfiler.UI.Overlay.Tabs;

/// <summary>One per-mod row of the cost tree: ModId, name, smoothed totals.</summary>
internal readonly struct ModRow : IComparable<ModRow>
{
    public readonly int ModId;
    public readonly string Name;
    public readonly double TotalMs;
    public readonly double TotalBytes;

    public ModRow(int modId, string name, double totalMs, double totalBytes)
    {
        ModId = modId;
        Name = name;
        TotalMs = totalMs;
        TotalBytes = totalBytes;
    }

    /// <summary>Sorts most-expensive first by ms. Bytes are carried alongside but not the sort key.</summary>
    public int CompareTo(ModRow other) => other.TotalMs.CompareTo(TotalMs);
}

/// <summary>
/// The btop-style per-mod tree. Each row shows one mod's smoothed cost; click
/// to fold open its category breakdown; click a category to reveal its top
/// hooks. Scroll wheel pages through the mod list. Sort order is descending
/// by ms, with categories within an expanded mod also descending by ms.
/// </summary>
internal sealed class TreeTab : IOverlayTab
{
    public string Label => "TREE";

    private ModRow[] _rows = Array.Empty<ModRow>();
    private int _rowCount;
    private int _scrollOffset;
    private readonly HashSet<int> _expanded = new HashSet<int>();
    private readonly HashSet<(int modId, int catId)> _expandedCats = new HashSet<(int, int)>();

    public bool IsAvailable(MetricCollector? collector) => collector != null && collector.History.Count > 0;

    public void Tick(MetricCollector collector)
    {
        if (!OverlayState.Paused)
        {
            BuildSortedRows(OverlayState.SelectedCategoryMs(collector), OverlayState.SelectedCategoryBytes(collector));
        }

        // Drill-down hint from the Overview tab: pre-expand the requested mod
        // and scroll it into view, then consume the hint. Runs after
        // BuildSortedRows so the row's position in _rows is up-to-date.
        int preselected = OverlayState.PreselectedModId;
        if (preselected >= 0)
        {
            _expanded.Add(preselected);
            int rowIndex = -1;
            for (int i = 0; i < _rowCount; i++)
            {
                if (_rows[i].ModId == preselected) { rowIndex = i; break; }
            }
            if (rowIndex >= 0)
            {
                // Centre it in the visible window where possible; otherwise
                // place it at the top of the visible region.
                int half = OverlayLayout.MaxModRows / 2;
                int target = Math.Max(0, rowIndex - half);
                int maxOffForScroll = Math.Max(0, _rowCount - OverlayLayout.MaxModRows);
                _scrollOffset = Math.Min(target, maxOffForScroll);
            }
            OverlayState.PreselectedModId = -1;
        }

        int maxOff = Math.Max(0, _rowCount - OverlayLayout.MaxModRows);
        if (_scrollOffset > maxOff) _scrollOffset = maxOff;
    }

    public float MeasurePanelHeight(MetricCollector collector)
    {
        IReadOnlyList<double> categoryMs = OverlayState.SelectedCategoryMs(collector);
        IReadOnlyList<double> hookMs     = OverlayState.SelectedHookMs(collector);

        int catCount = PerModAttribution.CategoryCount;
        int visible  = Math.Min(_rowCount - _scrollOffset, OverlayLayout.MaxModRows);
        float h      = OverlayLayoutCurrent.ChromeHeight + 32f;  // chrome + section header + a touch of breathing room

        Span<int> sortedCatIds = stackalloc int[catCount];
        Span<double> sortedCatMs = stackalloc double[catCount];

        for (int i = _scrollOffset; i < _scrollOffset + visible; i++)
        {
            h += OverlayLayout.RowHeight;
            if (!_expanded.Contains(_rows[i].ModId)) continue;

            int catVisible = SortVisibleCategories(_rows[i].ModId, categoryMs, sortedCatIds, sortedCatMs);
            for (int k = 0; k < catVisible; k++)
            {
                int c = sortedCatIds[k];
                h += OverlayLayout.SubRowHeight;
                if (_expandedCats.Contains((_rows[i].ModId, c)))
                    h += CountVisibleHooks(_rows[i].ModId, c, hookMs) * OverlayLayout.HookRowHeight;
            }
        }

        if (_rowCount > _scrollOffset + visible) h += 14f;
        return h;
    }

    public void HandleClick(float localX, float localY, MetricCollector collector)
    {
        HitTestRows(localY, collector, out int modId, out int catId);
        if (modId < 0) return;

        if (catId >= 0)
        {
            var key = (modId, catId);
            if (!_expandedCats.Remove(key)) _expandedCats.Add(key);
        }
        else
        {
            if (!_expanded.Remove(modId)) _expanded.Add(modId);
        }
    }

    public void HandleScroll(int delta, MetricCollector collector)
    {
        int maxOff = Math.Max(0, _rowCount - OverlayLayout.MaxModRows);
        _scrollOffset = Math.Clamp(_scrollOffset + delta, 0, maxOff);
    }

    // ---- Drawing -------------------------------------------------------------

    public void Draw(SpriteBatch sb, Rectangle area, MetricCollector collector)
    {
        int divY = area.Y + (int)OverlayLayoutCurrent.ChromeHeight;
        ProfilerTheme.FillRect(sb, new Rectangle(area.X + 8, divY, area.Width - 16, 1), ProfilerTheme.Border);
        ProfilerTheme.FillRect(sb, new Rectangle(area.X + 8, divY + 5, 2, 14), ProfilerTheme.Accent);
        OverlayDraw.Text(sb, "PER-MOD CPU   ·   click to expand",
            new Vector2(area.X + 18, divY + 6f), ProfilerTheme.Accent, 0.72f);

        if (_rowCount == 0)
        {
            OverlayDraw.Text(sb, "no per-mod data yet",
                new Vector2(area.X + 14, area.Y + OverlayLayoutCurrent.ChromeHeight + 22f),
                ProfilerTheme.TextMuted, 0.72f);
            return;
        }

        IReadOnlyList<double>  categoryMs    = OverlayState.SelectedCategoryMs(collector);
        IReadOnlyList<double>  hookMs        = OverlayState.SelectedHookMs(collector);
        IReadOnlyList<double>? categoryBytes = OverlayState.SelectedCategoryBytes(collector);

        int    catCount  = PerModAttribution.CategoryCount;
        double maxMs     = _rows[0].TotalMs;
        int    visible   = Math.Min(_rowCount - _scrollOffset, OverlayLayout.MaxModRows);
        bool   hasScroll = _rowCount > OverlayLayout.MaxModRows;
        float  rowY      = area.Y + OverlayLayoutCurrent.ChromeHeight + 22f;
        float  mouseY    = Main.MouseScreen.Y;

        // Scroll indicator.
        if (hasScroll)
        {
            float trackX = area.X + area.Width - OverlayLayout.ScrollTrackGap;
            float trackY = area.Y + OverlayLayoutCurrent.ChromeHeight + 22f;
            float trackH = visible * OverlayLayout.RowHeight;
            ProfilerTheme.FillRect(sb,
                new Rectangle((int)trackX, (int)trackY, (int)OverlayLayout.ScrollTrackW, (int)trackH),
                ProfilerTheme.Border);
            float thumbH  = Math.Max(20f, trackH * OverlayLayout.MaxModRows / _rowCount);
            float fraction = _rowCount > OverlayLayout.MaxModRows ? (float)_scrollOffset / (_rowCount - OverlayLayout.MaxModRows) : 0f;
            float thumbY  = trackY + fraction * (trackH - thumbH);
            ProfilerTheme.FillRect(sb,
                new Rectangle((int)trackX, (int)thumbY, (int)OverlayLayout.ScrollTrackW, (int)thumbH),
                ProfilerTheme.TextMuted);
        }

        int contentW = area.Width - 2 - (hasScroll ? (int)(OverlayLayout.ScrollTrackGap + OverlayLayout.ScrollTrackW + 2) : 0);

        Span<int> sortedCatIds = stackalloc int[catCount];
        Span<double> sortedCatMs = stackalloc double[catCount];

        for (int i = _scrollOffset; i < _scrollOffset + visible; i++)
        {
            ModRow row      = _rows[i];
            bool   expanded = _expanded.Contains(row.ModId);
            bool   hovered  = mouseY >= rowY && mouseY < rowY + OverlayLayout.RowHeight;

            if (hovered)
                ProfilerTheme.FillRect(sb,
                    new Rectangle(area.X + 1, (int)rowY, contentW, (int)OverlayLayout.RowHeight),
                    ProfilerTheme.RowHover);

            DrawModRow(sb, row, area.X, rowY, maxMs, expanded);
            rowY += OverlayLayout.RowHeight;

            if (!expanded) continue;

            int catVisible = SortVisibleCategories(row.ModId, categoryMs, sortedCatIds, sortedCatMs);
            for (int k = 0; k < catVisible; k++)
            {
                int    c     = sortedCatIds[k];
                double catMs = sortedCatMs[k];
                double catBytes = 0d;
                if (categoryBytes != null)
                {
                    int cell = row.ModId * catCount + c;
                    if (cell < categoryBytes.Count) catBytes = categoryBytes[cell];
                }

                bool catExpanded = _expandedCats.Contains((row.ModId, c));
                bool catHovered  = mouseY >= rowY && mouseY < rowY + OverlayLayout.SubRowHeight;

                if (catHovered)
                    ProfilerTheme.FillRect(sb,
                        new Rectangle(area.X + 1, (int)rowY, contentW, (int)OverlayLayout.SubRowHeight),
                        ProfilerTheme.RowHover);

                DrawCategoryRow(sb, PerModAttribution.CategoryNames[c], catMs, catBytes, row.TotalMs, area.X, rowY, catExpanded);
                rowY += OverlayLayout.SubRowHeight;

                if (catExpanded)
                    rowY = DrawHotHookRows(sb, row.ModId, c, catMs, hookMs, area.X, rowY);
            }
        }

        if (_rowCount > _scrollOffset + visible)
            OverlayDraw.Text(sb, $"+ {_rowCount - _scrollOffset - visible} more",
                new Vector2(area.X + 14, rowY + 1f), ProfilerTheme.TextDim, 0.66f);
    }

    // ---- Row drawing ---------------------------------------------------------

    private static void DrawModRow(SpriteBatch sb, ModRow row, int panelX, float y, double maxMs, bool expanded)
    {
        OverlayDraw.Text(sb, expanded ? "−" : "+", new Vector2(panelX + 12, y + 2), ProfilerTheme.Accent, 0.72f);
        OverlayDraw.Text(sb, OverlayDraw.Truncate(row.Name, 34), new Vector2(panelX + 26, y + 2), ProfilerTheme.Text, 0.78f);
        OverlayDraw.Bar(sb, panelX + 356, (int)y + 4, row.TotalMs, maxMs, OverlayLayout.BarH_Mod);

        switch (OverlayState.CurrentMetric)
        {
            case MetricView.Cpu:
                OverlayDraw.Text(sb, row.TotalMs.ToString("F3"), new Vector2(panelX + 540, y + 2), ProfilerTheme.Amber, 0.72f);
                break;
            case MetricView.Mem:
                OverlayDraw.Text(sb, OverlayDraw.FormatBytes(row.TotalBytes), new Vector2(panelX + 540, y + 2), ProfilerTheme.Dormant, 0.72f);
                break;
            case MetricView.Both:
                OverlayDraw.Text(sb, row.TotalMs.ToString("F2"), new Vector2(panelX + 530, y + 1), ProfilerTheme.Amber, 0.62f);
                OverlayDraw.Text(sb, OverlayDraw.FormatBytes(row.TotalBytes), new Vector2(panelX + 530, y + 9), ProfilerTheme.Dormant, 0.56f);
                break;
        }

        OverlayDraw.Text(sb, CoverageBadge(row.ModId), new Vector2(panelX + 592, y + 2), CoverageColor(row.ModId), 0.58f);
    }

    private static void DrawCategoryRow(SpriteBatch sb, string label, double catMs, double catBytes,
        double modTotalMs, int panelX, float y, bool expanded)
    {
        OverlayDraw.Text(sb, expanded ? "−" : "+", new Vector2(panelX + 30, y + 2), ProfilerTheme.TextDim, 0.62f);
        OverlayDraw.Text(sb, label, new Vector2(panelX + 44, y + 2), ProfilerTheme.TextMuted, 0.68f);
        OverlayDraw.Bar(sb, panelX + 356, (int)y + 4, catMs, modTotalMs, OverlayLayout.BarH_Cat);

        switch (OverlayState.CurrentMetric)
        {
            case MetricView.Cpu:
                OverlayDraw.Text(sb, catMs.ToString("F3"), new Vector2(panelX + 540, y + 2), ProfilerTheme.TextMuted, 0.64f);
                break;
            case MetricView.Mem:
                OverlayDraw.Text(sb, OverlayDraw.FormatBytes(catBytes), new Vector2(panelX + 540, y + 2), ProfilerTheme.TextMuted, 0.64f);
                break;
            case MetricView.Both:
                OverlayDraw.Text(sb, catMs.ToString("F2"), new Vector2(panelX + 530, y + 1), ProfilerTheme.TextMuted, 0.56f);
                OverlayDraw.Text(sb, OverlayDraw.FormatBytes(catBytes), new Vector2(panelX + 530, y + 9), ProfilerTheme.TextDim, 0.52f);
                break;
        }
    }

    private static float DrawHotHookRows(SpriteBatch sb, int modId, int categoryId, double categoryMs,
        IReadOnlyList<double> hookMs, int panelX, float y)
    {
        int    firstHook  = -1;
        int    secondHook = -1;
        double firstMs    = 0d;
        double secondMs   = 0d;
        FindTopHooks(modId, categoryId, hookMs, ref firstHook, ref firstMs, ref secondHook, ref secondMs);

        if (firstHook >= 0)
        {
            DrawHookRow(sb, PerModAttribution.Hooks[firstHook].DisplayName, firstMs, categoryMs, panelX, y);
            y += OverlayLayout.HookRowHeight;
        }

        if (secondHook >= 0)
        {
            DrawHookRow(sb, PerModAttribution.Hooks[secondHook].DisplayName, secondMs, categoryMs, panelX, y);
            y += OverlayLayout.HookRowHeight;
        }

        return y;
    }

    private static void DrawHookRow(SpriteBatch sb, string label, double hookMs, double categoryMs, int panelX, float y)
    {
        OverlayDraw.Text(sb, OverlayDraw.Truncate(label, 42), new Vector2(panelX + 64, y), ProfilerTheme.TextDim, 0.6f);
        OverlayDraw.Bar(sb, panelX + 356, (int)y + 2, hookMs, categoryMs, OverlayLayout.BarH_Hook);
        OverlayDraw.Text(sb, hookMs.ToString("F3"), new Vector2(panelX + 540, y), ProfilerTheme.TextDim, 0.58f);
    }

    // ---- Hit-test ------------------------------------------------------------

    private void HitTestRows(float localY, MetricCollector collector, out int modId, out int catId)
    {
        modId = -1;
        catId = -1;
        int catCount = PerModAttribution.CategoryCount;
        IReadOnlyList<double> categoryMs = OverlayState.SelectedCategoryMs(collector);
        int visible = Math.Min(_rowCount - _scrollOffset, OverlayLayout.MaxModRows);
        float y = OverlayLayoutCurrent.ChromeHeight + 22f;

        Span<int> sortedCatIds = stackalloc int[catCount];
        Span<double> sortedCatMs = stackalloc double[catCount];

        for (int i = _scrollOffset; i < _scrollOffset + visible; i++)
        {
            if (localY >= y && localY < y + OverlayLayout.RowHeight) { modId = _rows[i].ModId; return; }
            y += OverlayLayout.RowHeight;

            if (!_expanded.Contains(_rows[i].ModId)) continue;

            int catVisible = SortVisibleCategories(_rows[i].ModId, categoryMs, sortedCatIds, sortedCatMs);
            for (int k = 0; k < catVisible; k++)
            {
                int c = sortedCatIds[k];
                if (localY >= y && localY < y + OverlayLayout.SubRowHeight) { modId = _rows[i].ModId; catId = c; return; }
                y += OverlayLayout.SubRowHeight;

                if (_expandedCats.Contains((_rows[i].ModId, c)))
                    y += CountVisibleHooks(_rows[i].ModId, c, OverlayState.SelectedHookMs(collector)) * OverlayLayout.HookRowHeight;
            }
        }
    }

    // ---- Helpers -------------------------------------------------------------

    private static int SortVisibleCategories(int modId, IReadOnlyList<double> categoryMs,
        Span<int> outCatIds, Span<double> outCatMs)
    {
        int catCount = PerModAttribution.CategoryCount;
        int n = 0;
        for (int c = 0; c < catCount; c++)
        {
            int cell = modId * catCount + c;
            double ms = cell < categoryMs.Count ? categoryMs[cell] : 0d;
            if (ms <= 0.0005d) continue;
            outCatIds[n] = c;
            outCatMs[n] = ms;
            n++;
        }

        for (int i = 1; i < n; i++)
        {
            int idTmp = outCatIds[i];
            double msTmp = outCatMs[i];
            int j = i - 1;
            while (j >= 0 && outCatMs[j] < msTmp)
            {
                outCatIds[j + 1] = outCatIds[j];
                outCatMs[j + 1] = outCatMs[j];
                j--;
            }
            outCatIds[j + 1] = idTmp;
            outCatMs[j + 1] = msTmp;
        }

        return n;
    }

    private static int CountVisibleHooks(int modId, int categoryId, IReadOnlyList<double> hookMs)
    {
        int firstHook = -1;
        int secondHook = -1;
        double firstMs = 0d;
        double secondMs = 0d;
        FindTopHooks(modId, categoryId, hookMs, ref firstHook, ref firstMs, ref secondHook, ref secondMs);
        return (firstHook >= 0 ? 1 : 0) + (secondHook >= 0 ? 1 : 0);
    }

    private static void FindTopHooks(int modId, int categoryId, IReadOnlyList<double> hookMs,
        ref int firstHook, ref double firstMs, ref int secondHook, ref double secondMs)
    {
        IReadOnlyList<HookDescriptor> hooks = PerModAttribution.Hooks;
        int n = hooks.Count < hookMs.Count ? hooks.Count : hookMs.Count;
        for (int i = 0; i < n; i++)
        {
            HookDescriptor hook = hooks[i];
            if (hook.ModId != modId || hook.CategoryId != categoryId) continue;

            double ms = hookMs[i];
            if (ms <= 0.0005d) continue;

            if (ms > firstMs)
            {
                secondHook = firstHook; secondMs = firstMs;
                firstHook  = i;         firstMs  = ms;
            }
            else if (ms > secondMs)
            {
                secondHook = i; secondMs = ms;
            }
        }
    }

    private void BuildSortedRows(IReadOnlyList<double> categoryMs, IReadOnlyList<double>? categoryBytes)
    {
        string[] names    = HookInterceptor.ProfiledModNames;
        int      catCount = PerModAttribution.CategoryCount;
        int      n        = names.Length;

        if (_rows.Length < n) _rows = new ModRow[n];

        for (int i = 0; i < n; i++)
        {
            double totalMs = 0d;
            double totalBytes = 0d;
            for (int c = 0; c < catCount; c++)
            {
                int cell = i * catCount + c;
                if (cell < categoryMs.Count) totalMs += categoryMs[cell];
                if (categoryBytes != null && cell < categoryBytes.Count) totalBytes += categoryBytes[cell];
            }
            _rows[i] = new ModRow(i, names[i], totalMs, totalBytes);
        }

        if (n > 1) Array.Sort(_rows, 0, n);
        _rowCount = n;
    }

    private static string CoverageBadge(int modId)
    {
        int measured = HookCoverageView.MeasuredForMod(modId);
        int total    = HookCoverageView.TotalForMod(modId);
        return total == measured ? "full" : measured == 0 ? "none" : $"{measured}/{total}";
    }

    private static Color CoverageColor(int modId)
    {
        int    measured = HookCoverageView.MeasuredForMod(modId);
        int    total    = HookCoverageView.TotalForMod(modId);
        double coverage = total > 0 ? measured / (double)total : 1d;
        return coverage >= 0.95d ? ProfilerTheme.Good : coverage >= 0.75d ? ProfilerTheme.Amber : ProfilerTheme.Danger;
    }
}

#endif
