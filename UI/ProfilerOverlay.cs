#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ModLoader;
using Terraria.UI;
using PerformanceProfiler.Profiling;

namespace PerformanceProfiler.UI;

/// <summary>
/// The F9 profiler overlay: a dark panel, custom-drawn via <see cref="ProfilerTheme"/>
/// to match design/Mockups.html. It shows the overall per-tick stats and, below
/// them, the btop-style foldable per-mod CPU tree. Clicking a mod row folds it
/// open into a Systems / Players / NPCs / Projectiles breakdown; clicking a
/// category row further reveals its hottest hooks. The list scrolls if it
/// overflows MaxModRows.
/// </summary>
public sealed class ProfilerOverlay : UIState
{
    public override void OnInitialize()
    {
        OverlayPanel panel = new OverlayPanel();
        panel.Left.Set(16f, 0f);
        panel.Top.Set(16f, 0f);
        panel.Width.Set(OverlayPanel.PanelWidth, 0f);
        panel.Height.Set(OverlayPanel.CollapsedHeight(HookInterceptor.ProfiledModNames.Length), 0f);
        Append(panel);
    }
}

/// <summary>One per-mod row of the cost tree: its ModId, name, and smoothed total cost.</summary>
internal readonly struct ModRow : IComparable<ModRow>
{
    public readonly int ModId;
    public readonly string Name;
    public readonly double TotalMs;

    public ModRow(int modId, string name, double totalMs)
    {
        ModId = modId;
        Name = name;
        TotalMs = totalMs;
    }

    /// <summary>Sorts most-expensive first.</summary>
    public int CompareTo(ModRow other) => other.TotalMs.CompareTo(TotalMs);
}

/// <summary>
/// The custom-drawn overlay panel. Everything is hand-drawn in <see cref="DrawSelf"/>
/// with <see cref="ProfilerTheme"/>; no stock tModLoader widget chrome is used.
/// Draggable by its header strip. Mod rows fold open on click; category sub-rows
/// fold open to reveal their hottest hooks. The list scrolls with the mouse wheel.
/// </summary>
internal sealed class OverlayPanel : UIElement
{
    public const float PanelWidth = 640f;

    private const float HeaderHeight    = 28f;
    private const float MetricToggleX   = 426f;
    private const float PauseToggleX    = 506f;
    private const float ToggleY         = 6f;
    private const float MetricToggleW   = 70f;
    private const float PauseToggleW    = 90f;
    private const float ToggleHeight    = 16f;
    private const float StatStartY      = 12f;   // px below header bottom
    private const float StatGap         = 22f;
    private const float HealthTopOffset = 100f;
    private const float DividerOffset   = 148f;
    private const float RowsTopOffset   = 172f;
    private const float RowHeight       = 18f;
    private const float SubRowHeight    = 16f;
    private const float HookRowHeight   = 14f;
    private const int   MaxModRows      = 12;
    private const int   BarH_Mod        = 10;
    private const int   BarH_Cat        = 8;
    private const int   BarH_Hook       = 6;
    private const float ScrollTrackW    = 4f;
    private const float ScrollTrackGap  = 8f;

    private bool    _dragging;
    private Vector2 _dragOffset;
    private bool    _showAverage;
    private bool    _paused;
    private int     _scrollOffset;

    // Per-mod cost rows, reused and sorted each frame without allocating.
    private ModRow[] _rows = Array.Empty<ModRow>();
    private int      _rowCount;
    private double[] _frozenCategoryMs = Array.Empty<double>();
    private double[] _frozenHookMs     = Array.Empty<double>();

    // ModIds whose top-level rows are folded open.
    private readonly HashSet<int> _expanded = new HashSet<int>();

    // (modId, categoryId) pairs whose hook sub-rows are visible.
    private readonly HashSet<(int modId, int catId)> _expandedCats = new HashSet<(int, int)>();

    private float _appliedHeight;

    /// <summary>The panel height with every mod row collapsed.</summary>
    public static float CollapsedHeight(int modCount)
    {
        int shown  = modCount < MaxModRows ? modCount : MaxModRows;
        float h    = RowsTopOffset + shown * RowHeight + 10f;
        if (modCount > shown) h += 14f;
        return h;
    }

    // ---- Input ---------------------------------------------------------------

    public override void LeftMouseDown(UIMouseEvent evt)
    {
        base.LeftMouseDown(evt);

        float localY = evt.MousePosition.Y - GetDimensions().Position().Y;
        float localX = evt.MousePosition.X - GetDimensions().Position().X;

        if (localY <= HeaderHeight)
        {
            if (ClickHeaderControl(localX, localY)) return;
            _dragging  = true;
            _dragOffset = evt.MousePosition - GetDimensions().Position();
            return;
        }

        HitTestRows(localY, out int modId, out int catId);
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

    public override void LeftMouseUp(UIMouseEvent evt)
    {
        base.LeftMouseUp(evt);
        _dragging = false;
    }

    public override void ScrollWheel(UIScrollWheelEvent evt)
    {
        base.ScrollWheel(evt);
        if (!IsMouseHovering) return;
        int maxOffset = Math.Max(0, _rowCount - MaxModRows);
        _scrollOffset = evt.ScrollWheelValue > 0
            ? Math.Max(_scrollOffset - 1, 0)
            : Math.Min(_scrollOffset + 1, maxOffset);
    }

    // ---- Update --------------------------------------------------------------

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        if (IsMouseHovering || _dragging)
            Main.LocalPlayer.mouseInterface = true;

        if (_dragging)
        {
            if (!Main.mouseLeft) _dragging = false;
            else                 FollowMouse();
        }

        MetricCollector? collector = ModContent.GetInstance<ProfilerSystem>()?.Collector;
        if (collector == null) return;

        if (!_paused) BuildSortedRows(SelectedCategoryMs(collector));

        // Clamp scroll after row rebuild so a mod-count drop doesn't strand the offset.
        int maxOff = Math.Max(0, _rowCount - MaxModRows);
        if (_scrollOffset > maxOff) _scrollOffset = maxOff;

        ApplyHeight(SelectedCategoryMs(collector), SelectedHookMs(collector));
    }

    private bool ClickHeaderControl(float localX, float localY)
    {
        if (localY < ToggleY || localY > ToggleY + ToggleHeight) return false;

        MetricCollector? collector = ModContent.GetInstance<ProfilerSystem>()?.Collector;

        if (localX >= MetricToggleX && localX <= MetricToggleX + MetricToggleW)
        {
            _showAverage = !_showAverage;
            if (_paused && collector != null) CaptureSnapshot(collector);
            return true;
        }

        if (localX >= PauseToggleX && localX <= PauseToggleX + PauseToggleW)
        {
            _paused = !_paused;
            if (_paused && collector != null)
            {
                CaptureSnapshot(collector);
                BuildSortedRows(_frozenCategoryMs);
            }
            return true;
        }

        return false;
    }

    // ---- Hit-test ------------------------------------------------------------

    /// <summary>
    /// Maps a panel-local Y to the row it lands on.
    /// <paramref name="modId"/> is -1 on miss. <paramref name="catId"/> is -1
    /// when the hit is a mod row (not a category sub-row).
    /// </summary>
    private void HitTestRows(float localY, out int modId, out int catId)
    {
        modId = -1;
        catId = -1;
        int catCount = PerModAttribution.CategoryCount;
        MetricCollector? collector = ModContent.GetInstance<ProfilerSystem>()?.Collector;
        int visible = Math.Min(_rowCount - _scrollOffset, MaxModRows);
        float y = RowsTopOffset;

        for (int i = _scrollOffset; i < _scrollOffset + visible; i++)
        {
            if (localY >= y && localY < y + RowHeight) { modId = _rows[i].ModId; return; }
            y += RowHeight;

            if (!_expanded.Contains(_rows[i].ModId)) continue;

            IReadOnlyList<double>? categoryMs = collector != null ? SelectedCategoryMs(collector) : null;
            for (int c = 0; c < catCount; c++)
            {
                if (categoryMs != null)
                {
                    int cell  = _rows[i].ModId * catCount + c;
                    double ms = cell < categoryMs.Count ? categoryMs[cell] : 0d;
                    if (ms <= 0.0005d) continue;
                }

                if (localY >= y && localY < y + SubRowHeight) { modId = _rows[i].ModId; catId = c; return; }
                y += SubRowHeight;

                if (_expandedCats.Contains((_rows[i].ModId, c)) && collector != null)
                    y += CountVisibleHooks(_rows[i].ModId, c, SelectedHookMs(collector)) * HookRowHeight;
            }
        }
    }

    // ---- Height management ---------------------------------------------------

    private void ApplyHeight(IReadOnlyList<double> categoryMs, IReadOnlyList<double> hookMs)
    {
        int catCount = PerModAttribution.CategoryCount;
        int visible  = Math.Min(_rowCount - _scrollOffset, MaxModRows);
        float h      = RowsTopOffset + 10f;

        for (int i = _scrollOffset; i < _scrollOffset + visible; i++)
        {
            h += RowHeight;
            if (!_expanded.Contains(_rows[i].ModId)) continue;

            for (int c = 0; c < catCount; c++)
            {
                int cell    = _rows[i].ModId * catCount + c;
                double catMs = cell < categoryMs.Count ? categoryMs[cell] : 0d;
                if (catMs <= 0.0005d) continue;

                h += SubRowHeight;
                if (_expandedCats.Contains((_rows[i].ModId, c)))
                    h += CountVisibleHooks(_rows[i].ModId, c, hookMs) * HookRowHeight;
            }
        }

        if (_rowCount > _scrollOffset + visible) h += 14f;

        if (Math.Abs(h - _appliedHeight) > 0.5f)
        {
            _appliedHeight = h;
            Height.Set(h, 0f);
            Recalculate();
        }
    }

    // ---- Drag ----------------------------------------------------------------

    private void FollowMouse()
    {
        Vector2 target = Main.MouseScreen - _dragOffset;
        float maxX = Main.screenWidth  - GetDimensions().Width;
        float maxY = Main.screenHeight - GetDimensions().Height;
        Left.Set(MathHelper.Clamp(target.X, 0f, maxX < 0f ? 0f : maxX), 0f);
        Top.Set(MathHelper.Clamp(target.Y,  0f, maxY < 0f ? 0f : maxY), 0f);
        Recalculate();
    }

    // ---- Drawing -------------------------------------------------------------

    protected override void DrawSelf(SpriteBatch spriteBatch)
    {
        Rectangle area = GetDimensions().ToRectangle();

        // Panel surface.
        ProfilerTheme.DrawPanel(spriteBatch, area, ProfilerTheme.Panel, ProfilerTheme.Border);

        // Header strip with left accent bar.
        Rectangle header = new Rectangle(area.X, area.Y, area.Width, (int)HeaderHeight);
        ProfilerTheme.FillRect(spriteBatch, header, ProfilerTheme.Header);
        ProfilerTheme.DrawBorder(spriteBatch, header, ProfilerTheme.Border);
        ProfilerTheme.FillRect(spriteBatch, new Rectangle(area.X, area.Y, 2, (int)HeaderHeight), ProfilerTheme.Accent);

        DrawText(spriteBatch, "PERFORMANCE PROFILER",
            new Vector2(area.X + 12, area.Y + 8), ProfilerTheme.Accent, 0.82f);
        DrawHeaderToggle(spriteBatch, area.X + MetricToggleX, area.Y + ToggleY, MetricToggleW,
            _showAverage ? "30S AVG" : "NOW", _showAverage);
        DrawHeaderToggle(spriteBatch, area.X + PauseToggleX, area.Y + ToggleY, PauseToggleW,
            _paused ? "PAUSED" : "LIVE", _paused);

        float x = area.X + 14f;
        float y = area.Y + HeaderHeight + StatStartY;

        MetricCollector? collector = ModContent.GetInstance<ProfilerSystem>()?.Collector;
        if (collector == null || collector.History.Count == 0)
        {
            DrawText(spriteBatch, "waiting for a world...", new Vector2(x, y), ProfilerTheme.TextMuted, 0.8f);
            return;
        }

        // ---- Per-tick stats --------------------------------------------------
        RingBuffer<TickFrame> history = collector.History;
        TickFrame latest = history.Newest;

        DrawStat(spriteBatch, "tick",    $"{latest.FrameTimeMs:F2} ms",           x,        y,            ProfilerTheme.Amber);
        DrawStat(spriteBatch, "avg 30s", $"{AverageFrameTimeMs(history):F2} ms",  x + 250f, y,            ProfilerTheme.Text);
        DrawStat(spriteBatch, "gc",      $"{latest.GcTimeMs:F2} ms",              x,        y + StatGap,  ProfilerTheme.Good);
        DrawStat(spriteBatch, "entities",
            $"npc {latest.NpcCount}   proj {latest.ProjectileCount}   dust {latest.DustCount}",
            x + 250f, y + StatGap, ProfilerTheme.Text);
        DrawText(spriteBatch, $"tick #{latest.TickIndex}",
            new Vector2(x, y + StatGap * 2f + 2f), ProfilerTheme.TextDim, 0.72f);

        // Thin separator below stats.
        ProfilerTheme.FillRect(spriteBatch,
            new Rectangle(area.X + 8, area.Y + (int)HealthTopOffset - 6, area.Width - 16, 1),
            ProfilerTheme.Border);

        DrawProfilerHealth(spriteBatch, area, x, area.Y + HealthTopOffset, collector);
        DrawModTree(spriteBatch, area, collector);
    }

    private static void DrawProfilerHealth(SpriteBatch spriteBatch, Rectangle area, float x, float y, MetricCollector collector)
    {
        CoverageTotals(out int total, out int measured, out int fullMods, out int partialMods);
        double coverage    = total > 0 ? measured / (double)total : 1d;
        Color  covColor    = coverage >= 0.95d ? ProfilerTheme.Good
                           : coverage >= 0.75d ? ProfilerTheme.Amber
                           : ProfilerTheme.Danger;

        DrawText(spriteBatch, "PROFILER HEALTH",                               new Vector2(x,        y), ProfilerTheme.Accent,   0.68f);
        DrawText(spriteBatch, $"hooks {measured}/{total} ({coverage:P0})",     new Vector2(x + 142f, y), covColor,               0.68f);
        DrawText(spriteBatch, $"full {fullMods}  partial {partialMods}",       new Vector2(x + 358f, y), ProfilerTheme.TextMuted, 0.68f);

        // Backend badge: shows which instrumentation backend is feeding the tree.
        // In Parallel mode, the right-hand text shows the ILHook total and divergence.
        string backendLabel = HookBackend.Mode switch
        {
            HookBackendMode.Delegate => "backend: delegate",
            HookBackendMode.ILHook   => "backend: ilhook",
            HookBackendMode.Parallel => "backend: parallel",
            _                        => "backend: ?",
        };
        DrawText(spriteBatch, backendLabel, new Vector2(x + 510f, y), ProfilerTheme.Accent, 0.58f);

        int barW = area.Width - 28;
        ProfilerTheme.FillRect(spriteBatch, new Rectangle((int)x,    (int)y + 18, barW,                     10), ProfilerTheme.Border);
        int fill = (int)(barW * coverage);
        if (fill > 0)
            ProfilerTheme.FillRect(spriteBatch, new Rectangle((int)x, (int)y + 18, fill, 10), covColor);

        // Parallel-mode comparison strip: one line under the coverage bar.
        if (HookBackend.Mode == HookBackendMode.Parallel)
        {
            double del = collector.BackendTotalMs0;
            double il  = collector.BackendTotalMs1;
            double pct = collector.BackendDivergence * 100d;
            Color  pctColor = System.Math.Abs(pct) <= 5d ? ProfilerTheme.Good
                            : System.Math.Abs(pct) <= 20d ? ProfilerTheme.Amber
                            : ProfilerTheme.Danger;
            DrawText(spriteBatch,
                $"compare   delegate {del:F2}ms   ilhook {il:F2}ms",
                new Vector2(x, y + 32f), ProfilerTheme.TextMuted, 0.62f);
            DrawText(spriteBatch, $"Δ {pct:+0.0;-0.0;0.0}%",
                new Vector2(x + 380f, y + 32f), pctColor, 0.62f);
        }
    }

    private void DrawModTree(SpriteBatch spriteBatch, Rectangle area, MetricCollector collector)
    {
        int divY = area.Y + (int)DividerOffset;
        ProfilerTheme.FillRect(spriteBatch, new Rectangle(area.X + 8, divY, area.Width - 16, 1), ProfilerTheme.Border);
        ProfilerTheme.FillRect(spriteBatch, new Rectangle(area.X + 8, divY + 5, 2, 14), ProfilerTheme.Accent);
        DrawText(spriteBatch, "PER-MOD CPU   ·   click to expand",
            new Vector2(area.X + 18, divY + 6f), ProfilerTheme.Accent, 0.72f);

        if (_rowCount == 0)
        {
            DrawText(spriteBatch, "no per-mod data yet",
                new Vector2(area.X + 14, area.Y + RowsTopOffset), ProfilerTheme.TextMuted, 0.72f);
            return;
        }

        IReadOnlyList<double> categoryMs = SelectedCategoryMs(collector);
        IReadOnlyList<double> hookMs     = SelectedHookMs(collector);
        int    catCount  = PerModAttribution.CategoryCount;
        double maxMs     = _rows[0].TotalMs;
        int    visible   = Math.Min(_rowCount - _scrollOffset, MaxModRows);
        bool   hasScroll = _rowCount > MaxModRows;
        float  rowY      = area.Y + RowsTopOffset;
        float  mouseY    = Main.MouseScreen.Y;

        // Scroll indicator track.
        if (hasScroll)
        {
            float trackX = area.X + area.Width - ScrollTrackGap;
            float trackY = area.Y + RowsTopOffset;
            float trackH = visible * RowHeight;
            ProfilerTheme.FillRect(spriteBatch,
                new Rectangle((int)trackX, (int)trackY, (int)ScrollTrackW, (int)trackH),
                ProfilerTheme.Border);
            float thumbH  = Math.Max(20f, trackH * MaxModRows / _rowCount);
            float fraction = _rowCount > MaxModRows ? (float)_scrollOffset / (_rowCount - MaxModRows) : 0f;
            float thumbY  = trackY + fraction * (trackH - thumbH);
            ProfilerTheme.FillRect(spriteBatch,
                new Rectangle((int)trackX, (int)thumbY, (int)ScrollTrackW, (int)thumbH),
                ProfilerTheme.TextMuted);
        }

        int contentW = area.Width - 2 - (hasScroll ? (int)(ScrollTrackGap + ScrollTrackW + 2) : 0);

        for (int i = _scrollOffset; i < _scrollOffset + visible; i++)
        {
            ModRow row      = _rows[i];
            bool   expanded = _expanded.Contains(row.ModId);
            bool   hovered  = IsMouseHovering && mouseY >= rowY && mouseY < rowY + RowHeight;

            if (hovered)
                ProfilerTheme.FillRect(spriteBatch,
                    new Rectangle(area.X + 1, (int)rowY, contentW, (int)RowHeight),
                    ProfilerTheme.RowHover);

            DrawModRow(spriteBatch, row, area.X, rowY, maxMs, expanded);
            rowY += RowHeight;

            if (!expanded) continue;

            for (int c = 0; c < catCount; c++)
            {
                int    cell  = row.ModId * catCount + c;
                double catMs = cell < categoryMs.Count ? categoryMs[cell] : 0d;
                if (catMs <= 0.0005d) continue;

                bool catExpanded = _expandedCats.Contains((row.ModId, c));
                bool catHovered  = IsMouseHovering && mouseY >= rowY && mouseY < rowY + SubRowHeight;

                if (catHovered)
                    ProfilerTheme.FillRect(spriteBatch,
                        new Rectangle(area.X + 1, (int)rowY, contentW, (int)SubRowHeight),
                        ProfilerTheme.RowHover);

                DrawCategoryRow(spriteBatch, PerModAttribution.CategoryNames[c],
                    catMs, row.TotalMs, area.X, rowY, catExpanded);
                rowY += SubRowHeight;

                if (catExpanded)
                    rowY = DrawHotHookRows(spriteBatch, row.ModId, c, catMs, hookMs, area.X, rowY);
            }
        }

        if (_rowCount > _scrollOffset + visible)
            DrawText(spriteBatch, $"+ {_rowCount - _scrollOffset - visible} more",
                new Vector2(area.X + 14, rowY + 1f), ProfilerTheme.TextDim, 0.66f);
    }

    private static void DrawModRow(SpriteBatch spriteBatch, ModRow row, int panelX, float y, double maxMs, bool expanded)
    {
        DrawText(spriteBatch, expanded ? "−" : "+", new Vector2(panelX + 12, y + 2), ProfilerTheme.Accent, 0.72f);
        DrawText(spriteBatch, Truncate(row.Name, 34), new Vector2(panelX + 26, y + 2), ProfilerTheme.Text, 0.78f);
        DrawBar(spriteBatch, panelX + 356, (int)y + 4, row.TotalMs, maxMs, BarH_Mod);
        DrawText(spriteBatch, row.TotalMs.ToString("F3"), new Vector2(panelX + 540, y + 2), ProfilerTheme.Amber, 0.72f);
        DrawText(spriteBatch, CoverageBadge(row.ModId), new Vector2(panelX + 592, y + 2), CoverageColor(row.ModId), 0.58f);
    }

    private static void DrawCategoryRow(SpriteBatch spriteBatch, string label, double catMs, double modTotalMs,
        int panelX, float y, bool expanded)
    {
        DrawText(spriteBatch, expanded ? "−" : "+", new Vector2(panelX + 30, y + 2), ProfilerTheme.TextDim, 0.62f);
        DrawText(spriteBatch, label, new Vector2(panelX + 44, y + 2), ProfilerTheme.TextMuted, 0.68f);
        DrawBar(spriteBatch, panelX + 356, (int)y + 4, catMs, modTotalMs, BarH_Cat);
        DrawText(spriteBatch, catMs.ToString("F3"), new Vector2(panelX + 540, y + 2), ProfilerTheme.TextMuted, 0.64f);
    }

    private static float DrawHotHookRows(SpriteBatch spriteBatch, int modId, int categoryId, double categoryMs,
        IReadOnlyList<double> hookMs, int panelX, float y)
    {
        int    firstHook  = -1;
        int    secondHook = -1;
        double firstMs    = 0d;
        double secondMs   = 0d;
        FindTopHooks(modId, categoryId, hookMs, ref firstHook, ref firstMs, ref secondHook, ref secondMs);

        if (firstHook >= 0)
        {
            DrawHookRow(spriteBatch, PerModAttribution.Hooks[firstHook].DisplayName, firstMs, categoryMs, panelX, y);
            y += HookRowHeight;
        }

        if (secondHook >= 0)
        {
            DrawHookRow(spriteBatch, PerModAttribution.Hooks[secondHook].DisplayName, secondMs, categoryMs, panelX, y);
            y += HookRowHeight;
        }

        return y;
    }

    private static void DrawHookRow(SpriteBatch spriteBatch, string label, double hookMs, double categoryMs,
        int panelX, float y)
    {
        DrawText(spriteBatch, Truncate(label, 42), new Vector2(panelX + 64, y), ProfilerTheme.TextDim, 0.6f);
        DrawBar(spriteBatch, panelX + 356, (int)y + 2, hookMs, categoryMs, BarH_Hook);
        DrawText(spriteBatch, hookMs.ToString("F3"), new Vector2(panelX + 540, y), ProfilerTheme.TextDim, 0.58f);
    }

    private static int CountVisibleHooks(int modId, int categoryId, IReadOnlyList<double> hookMs)
    {
        int    firstHook  = -1;
        int    secondHook = -1;
        double firstMs    = 0d;
        double secondMs   = 0d;
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

    private static void DrawBar(SpriteBatch spriteBatch, int barX, int barY, double value, double max, int height)
    {
        const int barWidth = 170;
        ProfilerTheme.FillRect(spriteBatch, new Rectangle(barX, barY, barWidth, height), ProfilerTheme.Border);

        double fraction = max > 0d ? value / max : 0d;
        if (fraction < 0d) fraction = 0d;
        int fillWidth = (int)(barWidth * fraction);
        if (fillWidth > 0)
            ProfilerTheme.FillRect(spriteBatch,
                new Rectangle(barX, barY, fillWidth, height), ProfilerTheme.CostColor(fraction));
    }

    // ---- Data helpers --------------------------------------------------------

    private void BuildSortedRows(IReadOnlyList<double> categoryMs)
    {
        string[] names    = HookInterceptor.ProfiledModNames;
        int      catCount = PerModAttribution.CategoryCount;
        int      n        = names.Length;

        if (_rows.Length < n) _rows = new ModRow[n];

        for (int i = 0; i < n; i++)
        {
            double total = 0d;
            for (int c = 0; c < catCount; c++)
            {
                int cell = i * catCount + c;
                if (cell < categoryMs.Count) total += categoryMs[cell];
            }
            _rows[i] = new ModRow(i, names[i], total);
        }

        if (n > 1) Array.Sort(_rows, 0, n);
        _rowCount = n;
    }

    private IReadOnlyList<double> SelectedCategoryMs(MetricCollector collector) =>
        _paused ? _frozenCategoryMs : _showAverage ? collector.PerModCategoryAverageMs : collector.PerModCategoryMs;

    private IReadOnlyList<double> SelectedHookMs(MetricCollector collector) =>
        _paused ? _frozenHookMs : _showAverage ? collector.PerHookAverageMs : collector.PerHookMs;

    private void CaptureSnapshot(MetricCollector collector)
    {
        IReadOnlyList<double> catSrc  = _showAverage ? collector.PerModCategoryAverageMs : collector.PerModCategoryMs;
        IReadOnlyList<double> hookSrc = _showAverage ? collector.PerHookAverageMs        : collector.PerHookMs;

        if (_frozenCategoryMs.Length != catSrc.Count)  _frozenCategoryMs = new double[catSrc.Count];
        if (_frozenHookMs.Length != hookSrc.Count)     _frozenHookMs     = new double[hookSrc.Count];

        for (int i = 0; i < catSrc.Count;  i++) _frozenCategoryMs[i] = catSrc[i];
        for (int i = 0; i < hookSrc.Count; i++) _frozenHookMs[i]     = hookSrc[i];
    }

    // ---- Draw helpers --------------------------------------------------------

    private static void DrawHeaderToggle(SpriteBatch spriteBatch, float x, float y, float width, string label, bool active)
    {
        Rectangle area = new Rectangle((int)x, (int)y, (int)width, (int)ToggleHeight);
        ProfilerTheme.FillRect(spriteBatch, area, active ? new Color(25, 40, 60) : ProfilerTheme.Panel);
        ProfilerTheme.DrawBorder(spriteBatch, area, active ? ProfilerTheme.Accent : ProfilerTheme.Border);
        DrawText(spriteBatch, label, new Vector2(area.X + 6, area.Y + 3),
            active ? ProfilerTheme.Accent : ProfilerTheme.TextMuted, 0.55f);
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text.Substring(0, max - 2) + "..";

    private static void DrawStat(SpriteBatch spriteBatch, string label, string value,
        float x, float y, Color valueColor)
    {
        DrawText(spriteBatch, label,  new Vector2(x,         y), ProfilerTheme.TextMuted, 0.8f);
        DrawText(spriteBatch, value,  new Vector2(x + 102f,  y), valueColor,              0.8f);
    }

    private static void DrawText(SpriteBatch spriteBatch, string text, Vector2 position, Color color, float scale) =>
        Utils.DrawBorderString(spriteBatch, text, position, color, scale);

    private static double AverageFrameTimeMs(RingBuffer<TickFrame> history)
    {
        if (history.Count == 0) return 0d;
        double sum = 0d;
        for (int i = 0; i < history.Count; i++) sum += history[i].FrameTimeMs;
        return sum / history.Count;
    }

    private static string CoverageBadge(int modId)
    {
        int measured = modId < HookInterceptor.MeasuredHookCounts.Count ? HookInterceptor.MeasuredHookCounts[modId] : 0;
        int total    = modId < HookInterceptor.TotalHookCounts.Count    ? HookInterceptor.TotalHookCounts[modId]    : 0;
        return total == measured ? "full" : measured == 0 ? "none" : $"{measured}/{total}";
    }

    private static Color CoverageColor(int modId)
    {
        int    measured = modId < HookInterceptor.MeasuredHookCounts.Count ? HookInterceptor.MeasuredHookCounts[modId] : 0;
        int    total    = modId < HookInterceptor.TotalHookCounts.Count    ? HookInterceptor.TotalHookCounts[modId]    : 0;
        double coverage = total > 0 ? measured / (double)total : 1d;
        return coverage >= 0.95d ? ProfilerTheme.Good : coverage >= 0.75d ? ProfilerTheme.Amber : ProfilerTheme.Danger;
    }

    private static void CoverageTotals(out int total, out int measured, out int fullMods, out int partialMods)
    {
        total = 0; measured = 0; fullMods = 0; partialMods = 0;
        int mods = HookInterceptor.ProfiledModNames.Length;
        for (int i = 0; i < mods; i++)
        {
            int modTotal    = i < HookInterceptor.TotalHookCounts.Count    ? HookInterceptor.TotalHookCounts[i]    : 0;
            int modMeasured = i < HookInterceptor.MeasuredHookCounts.Count ? HookInterceptor.MeasuredHookCounts[i] : 0;
            total    += modTotal;
            measured += modMeasured;
            if (modTotal == modMeasured) fullMods++;
            else                         partialMods++;
        }
    }
}
