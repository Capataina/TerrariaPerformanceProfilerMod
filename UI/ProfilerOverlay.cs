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
/// open into a Systems / Players / NPCs / Projectiles breakdown.
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
/// Draggable by its header strip; the per-mod rows fold open on click.
/// </summary>
internal sealed class OverlayPanel : UIElement
{
    public const float PanelWidth = 640f;

    private const float HeaderHeight = 26f;
    private const float MetricToggleX = 422f;
    private const float PauseToggleX = 500f;
    private const float ToggleY = 5f;
    private const float MetricToggleWidth = 68f;
    private const float PauseToggleWidth = 92f;
    private const float ToggleHeight = 16f;
    private const float StatGap = 24f;
    private const float HealthTopOffset = 106f;
    private const float ListDividerOffset = 202f;
    private const float RowsTopOffset = 230f;
    private const float RowHeight = 17f;
    private const float SubRowHeight = 15f;
    private const float HookRowHeight = 14f;
    private const int MaxModRows = 12;

    private bool _dragging;
    private Vector2 _dragOffset;
    private bool _showAverage;
    private bool _paused;

    // Per-mod cost rows, reused and sorted each frame without allocating.
    private ModRow[] _rows = Array.Empty<ModRow>();
    private int _rowCount;
    private double[] _frozenCategoryMs = Array.Empty<double>();
    private double[] _frozenHookMs = Array.Empty<double>();

    // ModIds whose rows are folded open. Keyed by ModId so the set survives the
    // rows being re-sorted every frame.
    private readonly HashSet<int> _expanded = new HashSet<int>();

    private float _appliedHeight;

    /// <summary>The panel height with every mod row collapsed.</summary>
    public static float CollapsedHeight(int modCount)
    {
        int shown = modCount < MaxModRows ? modCount : MaxModRows;
        float height = RowsTopOffset + shown * RowHeight + 10f;
        if (modCount > shown)
        {
            height += 14f; // room for the "+ N more" line
        }

        return height;
    }

    public override void LeftMouseDown(UIMouseEvent evt)
    {
        base.LeftMouseDown(evt);

        float localY = evt.MousePosition.Y - GetDimensions().Position().Y;
        float localX = evt.MousePosition.X - GetDimensions().Position().X;

        // The header strip starts a drag, the way a window title bar works.
        if (localY <= HeaderHeight)
        {
            if (ClickHeaderControl(localX, localY))
            {
                return;
            }

            _dragging = true;
            _dragOffset = evt.MousePosition - GetDimensions().Position();
            return;
        }

        // A click on a mod row folds it open or shut.
        int modId = ModRowAt(localY);
        if (modId >= 0 && !_expanded.Remove(modId))
        {
            _expanded.Add(modId);
        }
    }

    public override void LeftMouseUp(UIMouseEvent evt)
    {
        base.LeftMouseUp(evt);
        _dragging = false;
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        if (IsMouseHovering || _dragging)
        {
            Main.LocalPlayer.mouseInterface = true;
        }

        if (_dragging)
        {
            if (!Main.mouseLeft)
            {
                _dragging = false;
            }
            else
            {
                FollowMouse();
            }
        }

        // Rebuild the sorted rows once per frame so DrawSelf and the click
        // hit-test agree, then resize the panel for the current fold state.
        MetricCollector? collector = ModContent.GetInstance<ProfilerSystem>()?.Collector;
        if (collector != null)
        {
            if (!_paused)
            {
                BuildSortedRows(SelectedCategoryMs(collector));
            }

            ApplyHeight(SelectedCategoryMs(collector), SelectedHookMs(collector));
        }
    }

    private bool ClickHeaderControl(float localX, float localY)
    {
        if (localY < ToggleY || localY > ToggleY + ToggleHeight)
        {
            return false;
        }

        MetricCollector? collector = ModContent.GetInstance<ProfilerSystem>()?.Collector;
        if (localX >= MetricToggleX && localX <= MetricToggleX + MetricToggleWidth)
        {
            _showAverage = !_showAverage;
            if (_paused && collector != null)
            {
                CaptureSnapshot(collector);
            }

            return true;
        }

        if (localX >= PauseToggleX && localX <= PauseToggleX + PauseToggleWidth)
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

    /// <summary>Returns the ModId of the row at panel-local y, or -1 if y is not on a mod row.</summary>
    private int ModRowAt(float localY)
    {
        int catCount = PerModAttribution.CategoryCount;
        MetricCollector? collector = ModContent.GetInstance<ProfilerSystem>()?.Collector;
        int visible = _rowCount < MaxModRows ? _rowCount : MaxModRows;
        float y = RowsTopOffset;

        for (int i = 0; i < visible; i++)
        {
            if (localY >= y && localY < y + RowHeight)
            {
                return _rows[i].ModId;
            }

            y += RowHeight;
            if (_expanded.Contains(_rows[i].ModId))
            {
                IReadOnlyList<double>? categoryMs = collector != null ? SelectedCategoryMs(collector) : null;
                for (int c = 0; c < catCount; c++)
                {
                    if (categoryMs != null)
                    {
                        int cell = _rows[i].ModId * catCount + c;
                        double catMs = cell < categoryMs.Count ? categoryMs[cell] : 0d;
                        if (catMs <= 0.0005d)
                        {
                            continue;
                        }
                    }

                    y += SubRowHeight;
                    if (collector != null)
                    {
                        y += CountVisibleHooks(_rows[i].ModId, c, SelectedHookMs(collector)) * HookRowHeight;
                    }
                }
            }
        }

        return -1;
    }

    /// <summary>Resizes the panel to fit the current fold state.</summary>
    private void ApplyHeight(IReadOnlyList<double> categoryMs, IReadOnlyList<double> hookMs)
    {
        int catCount = PerModAttribution.CategoryCount;
        int visible = _rowCount < MaxModRows ? _rowCount : MaxModRows;
        float height = RowsTopOffset + 10f;

        for (int i = 0; i < visible; i++)
        {
            height += RowHeight;
            if (_expanded.Contains(_rows[i].ModId))
            {
                for (int c = 0; c < catCount; c++)
                {
                    int cell = _rows[i].ModId * catCount + c;
                    double catMs = cell < categoryMs.Count ? categoryMs[cell] : 0d;
                    if (catMs <= 0.0005d)
                    {
                        continue;
                    }

                    height += SubRowHeight;
                    height += CountVisibleHooks(_rows[i].ModId, c, hookMs) * HookRowHeight;
                }
            }
        }

        if (_rowCount > visible)
        {
            height += 14f;
        }

        if (Math.Abs(height - _appliedHeight) > 0.5f)
        {
            _appliedHeight = height;
            Height.Set(height, 0f);
            Recalculate();
        }
    }

    /// <summary>Moves the panel so the grabbed point stays under the cursor, clamped on-screen.</summary>
    private void FollowMouse()
    {
        Vector2 target = Main.MouseScreen - _dragOffset;
        float maxX = Main.screenWidth - GetDimensions().Width;
        float maxY = Main.screenHeight - GetDimensions().Height;

        Left.Set(MathHelper.Clamp(target.X, 0f, maxX < 0f ? 0f : maxX), 0f);
        Top.Set(MathHelper.Clamp(target.Y, 0f, maxY < 0f ? 0f : maxY), 0f);
        Recalculate();
    }

    protected override void DrawSelf(SpriteBatch spriteBatch)
    {
        Rectangle area = GetDimensions().ToRectangle();

        // Panel surface and header strip.
        ProfilerTheme.DrawPanel(spriteBatch, area, ProfilerTheme.Panel, ProfilerTheme.Border);
        Rectangle header = new Rectangle(area.X, area.Y, area.Width, (int)HeaderHeight);
        ProfilerTheme.FillRect(spriteBatch, header, ProfilerTheme.Header);
        ProfilerTheme.DrawBorder(spriteBatch, header, ProfilerTheme.Border);
        DrawText(spriteBatch, "PERFORMANCE PROFILER", new Vector2(area.X + 10, area.Y + 7), ProfilerTheme.Accent, 0.82f);
        DrawHeaderToggle(spriteBatch, area.X + MetricToggleX, area.Y + ToggleY, MetricToggleWidth,
            _showAverage ? "30S AVG" : "NOW", _showAverage);
        DrawHeaderToggle(spriteBatch, area.X + PauseToggleX, area.Y + ToggleY, PauseToggleWidth,
            _paused ? "PAUSED" : "LIVE", _paused);

        float x = area.X + 14;
        float y = area.Y + HeaderHeight + 14f;

        MetricCollector? collector = ModContent.GetInstance<ProfilerSystem>()?.Collector;
        if (collector == null || collector.History.Count == 0)
        {
            DrawText(spriteBatch, "waiting for a world...", new Vector2(x, y), ProfilerTheme.TextMuted, 0.8f);
            return;
        }

        // ---- Overall per-tick stats ----
        RingBuffer<TickFrame> history = collector.History;
        TickFrame latest = history.Newest;

        DrawStat(spriteBatch, "tick", $"{latest.FrameTimeMs:F2} ms", x, y, ProfilerTheme.Amber);
        DrawStat(spriteBatch, "avg 30s", $"{AverageFrameTimeMs(history):F2} ms", x + 250f, y, ProfilerTheme.Text);
        DrawStat(spriteBatch, "gc pause", $"{latest.GcTimeMs:F2} ms", x, y + StatGap, ProfilerTheme.Good);
        DrawStat(spriteBatch, "entities",
            $"npc {latest.NpcCount}   proj {latest.ProjectileCount}   dust {latest.DustCount}",
            x + 250f, y + StatGap, ProfilerTheme.Text);
        DrawText(spriteBatch, $"tick #{latest.TickIndex}", new Vector2(x, y + StatGap * 2f + 4f),
            ProfilerTheme.TextDim, 0.72f);

        DrawProfilerHealth(spriteBatch, area, x, area.Y + HealthTopOffset);

        // ---- Foldable per-mod CPU tree ----
        DrawModTree(spriteBatch, area, collector);
    }

    /// <summary>Draws aggregate measurement coverage so cheap-looking mods cannot hide unmeasured hooks.</summary>
    private static void DrawProfilerHealth(SpriteBatch spriteBatch, Rectangle area, float x, float y)
    {
        CoverageTotals(out int total, out int measured, out int fullMods, out int partialMods);
        double coverage = total > 0 ? measured / (double)total : 1d;
        Color coverageColor = coverage >= 0.95d ? ProfilerTheme.Good : coverage >= 0.75d ? ProfilerTheme.Amber : ProfilerTheme.Danger;

        DrawText(spriteBatch, "PROFILER HEALTH", new Vector2(x, y), ProfilerTheme.Accent, 0.68f);
        DrawText(spriteBatch, $"hooks {measured}/{total} ({coverage:P0})", new Vector2(x + 142f, y), coverageColor, 0.68f);
        DrawText(spriteBatch, $"mods full {fullMods}  partial {partialMods}", new Vector2(x + 342f, y), ProfilerTheme.TextMuted, 0.68f);

        ProfilerTheme.FillRect(spriteBatch, new Rectangle((int)x, (int)y + 18, area.Width - 28, 8), ProfilerTheme.Border);
        int fill = (int)((area.Width - 28) * coverage);
        if (fill > 0)
        {
            ProfilerTheme.FillRect(spriteBatch, new Rectangle((int)x, (int)y + 18, fill, 8), coverageColor);
        }
    }

    /// <summary>Draws the foldable per-mod cost tree with colour-graded bars.</summary>
    private void DrawModTree(SpriteBatch spriteBatch, Rectangle area, MetricCollector collector)
    {
        int dividerY = area.Y + (int)ListDividerOffset;
        ProfilerTheme.FillRect(spriteBatch, new Rectangle(area.X + 8, dividerY, area.Width - 16, 1), ProfilerTheme.Border);
        DrawText(spriteBatch, "PER-MOD CPU   ·   click a mod to fold open", new Vector2(area.X + 14, dividerY + 8f),
            ProfilerTheme.Accent, 0.72f);

        if (_rowCount == 0)
        {
            DrawText(spriteBatch, "no per-mod data yet", new Vector2(area.X + 14, area.Y + RowsTopOffset),
                ProfilerTheme.TextMuted, 0.72f);
            return;
        }

        IReadOnlyList<double> categoryMs = SelectedCategoryMs(collector);
        IReadOnlyList<double> hookMs = SelectedHookMs(collector);
        int catCount = PerModAttribution.CategoryCount;
        double maxMs = _rows[0].TotalMs; // rows are sorted most-expensive first
        int visible = _rowCount < MaxModRows ? _rowCount : MaxModRows;
        float rowY = area.Y + RowsTopOffset;

        for (int i = 0; i < visible; i++)
        {
            ModRow row = _rows[i];
            bool expanded = _expanded.Contains(row.ModId);
            DrawModRow(spriteBatch, row, area.X, rowY, maxMs, expanded);
            rowY += RowHeight;

            if (expanded)
            {
                for (int c = 0; c < catCount; c++)
                {
                    int cell = row.ModId * catCount + c;
                    double catMs = cell < categoryMs.Count ? categoryMs[cell] : 0d;
                    if (catMs <= 0.0005d)
                    {
                        continue;
                    }

                    DrawCategoryRow(spriteBatch, PerModAttribution.CategoryNames[c], catMs, row.TotalMs, area.X, rowY);
                    rowY += SubRowHeight;
                    rowY = DrawHotHookRows(spriteBatch, row.ModId, c, catMs, hookMs, area.X, rowY);
                }
            }
        }

        if (_rowCount > visible)
        {
            DrawText(spriteBatch, $"+ {_rowCount - visible} more mods", new Vector2(area.X + 14, rowY + 1f),
                ProfilerTheme.TextDim, 0.66f);
        }
    }

    /// <summary>Draws one mod row: a fold marker, the name, a colour-graded cost bar, the value.</summary>
    private static void DrawModRow(SpriteBatch spriteBatch, ModRow row, int panelX, float y, double maxMs, bool expanded)
    {
        DrawText(spriteBatch, expanded ? "-" : "+", new Vector2(panelX + 12, y), ProfilerTheme.TextMuted, 0.7f);
        DrawText(spriteBatch, Truncate(row.Name, 34), new Vector2(panelX + 26, y), ProfilerTheme.Text, 0.78f);

        DrawBar(spriteBatch, panelX + 356, (int)y + 3, row.TotalMs, maxMs);
        DrawText(spriteBatch, row.TotalMs.ToString("F3"), new Vector2(panelX + 540, y), ProfilerTheme.Amber, 0.72f);
        DrawText(spriteBatch, CoverageBadge(row.ModId), new Vector2(panelX + 590, y), CoverageColor(row.ModId), 0.58f);
    }

    /// <summary>Draws an indented category sub-row beneath an expanded mod row.</summary>
    private static void DrawCategoryRow(SpriteBatch spriteBatch, string label, double catMs, double modTotalMs,
        int panelX, float y)
    {
        DrawText(spriteBatch, label, new Vector2(panelX + 44, y), ProfilerTheme.TextMuted, 0.68f);
        DrawBar(spriteBatch, panelX + 356, (int)y + 3, catMs, modTotalMs);
        DrawText(spriteBatch, catMs.ToString("F3"), new Vector2(panelX + 540, y), ProfilerTheme.TextMuted, 0.64f);
    }

    /// <summary>Draws the hottest registered hooks beneath one category row.</summary>
    private static float DrawHotHookRows(SpriteBatch spriteBatch, int modId, int categoryId, double categoryMs,
        IReadOnlyList<double> hookMs, int panelX, float y)
    {
        int firstHook = -1;
        int secondHook = -1;
        double firstMs = 0d;
        double secondMs = 0d;
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

    private static void DrawHookRow(SpriteBatch spriteBatch, string label, double hookMs, double categoryMs, int panelX, float y)
    {
        DrawText(spriteBatch, Truncate(label, 42), new Vector2(panelX + 64, y), ProfilerTheme.TextDim, 0.6f);
        DrawBar(spriteBatch, panelX + 356, (int)y + 3, hookMs, categoryMs);
        DrawText(spriteBatch, hookMs.ToString("F3"), new Vector2(panelX + 540, y), ProfilerTheme.TextDim, 0.58f);
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
            if (hook.ModId != modId || hook.CategoryId != categoryId)
            {
                continue;
            }

            double ms = hookMs[i];
            if (ms <= 0.0005d)
            {
                continue;
            }

            if (ms > firstMs)
            {
                secondHook = firstHook;
                secondMs = firstMs;
                firstHook = i;
                firstMs = ms;
            }
            else if (ms > secondMs)
            {
                secondHook = i;
                secondMs = ms;
            }
        }
    }

    /// <summary>Draws a dim track with a graded fill, <paramref name="value"/> relative to <paramref name="max"/>.</summary>
    private static void DrawBar(SpriteBatch spriteBatch, int barX, int barY, double value, double max)
    {
        const int barWidth = 170;
        ProfilerTheme.FillRect(spriteBatch, new Rectangle(barX, barY, barWidth, 8), ProfilerTheme.Border);

        double fraction = max > 0d ? value / max : 0d;
        if (fraction < 0d)
        {
            fraction = 0d;
        }

        int fillWidth = (int)(barWidth * fraction);
        if (fillWidth > 0)
        {
            ProfilerTheme.FillRect(spriteBatch,
                new Rectangle(barX, barY, fillWidth, 8), ProfilerTheme.CostColor(fraction));
        }
    }

    /// <summary>Fills and sorts <see cref="_rows"/> from the collector's per-mod totals.</summary>
    private void BuildSortedRows(IReadOnlyList<double> categoryMs)
    {
        string[] names = HookInterceptor.ProfiledModNames;
        int catCount = PerModAttribution.CategoryCount;
        int n = names.Length;

        if (_rows.Length < n)
        {
            _rows = new ModRow[n];
        }

        for (int i = 0; i < n; i++)
        {
            double total = 0d;
            for (int c = 0; c < catCount; c++)
            {
                int cell = i * catCount + c;
                if (cell < categoryMs.Count)
                {
                    total += categoryMs[cell];
                }
            }

            _rows[i] = new ModRow(i, names[i], total);
        }

        if (n > 1)
        {
            Array.Sort(_rows, 0, n);
        }

        _rowCount = n;
    }

    private IReadOnlyList<double> SelectedCategoryMs(MetricCollector collector)
    {
        return _paused ? _frozenCategoryMs : _showAverage ? collector.PerModCategoryAverageMs : collector.PerModCategoryMs;
    }

    private IReadOnlyList<double> SelectedHookMs(MetricCollector collector)
    {
        return _paused ? _frozenHookMs : _showAverage ? collector.PerHookAverageMs : collector.PerHookMs;
    }

    private void CaptureSnapshot(MetricCollector collector)
    {
        IReadOnlyList<double> categorySource = _showAverage ? collector.PerModCategoryAverageMs : collector.PerModCategoryMs;
        IReadOnlyList<double> hookSource = _showAverage ? collector.PerHookAverageMs : collector.PerHookMs;

        if (_frozenCategoryMs.Length != categorySource.Count)
        {
            _frozenCategoryMs = new double[categorySource.Count];
        }

        if (_frozenHookMs.Length != hookSource.Count)
        {
            _frozenHookMs = new double[hookSource.Count];
        }

        for (int i = 0; i < categorySource.Count; i++)
        {
            _frozenCategoryMs[i] = categorySource[i];
        }

        for (int i = 0; i < hookSource.Count; i++)
        {
            _frozenHookMs[i] = hookSource[i];
        }
    }

    private static void DrawHeaderToggle(SpriteBatch spriteBatch, float x, float y, float width, string label, bool active)
    {
        Rectangle area = new Rectangle((int)x, (int)y, (int)width, (int)ToggleHeight);
        ProfilerTheme.FillRect(spriteBatch, area, active ? ProfilerTheme.RowHover : ProfilerTheme.Panel);
        ProfilerTheme.DrawBorder(spriteBatch, area, active ? ProfilerTheme.Accent : ProfilerTheme.Border);
        DrawText(spriteBatch, label, new Vector2(area.X + 6, area.Y + 3), active ? ProfilerTheme.Accent : ProfilerTheme.TextMuted, 0.55f);
    }

    private static string Truncate(string text, int max)
    {
        return text.Length <= max ? text : text.Substring(0, max - 2) + "..";
    }

    /// <summary>Draws a "label   value" stat line: a muted label, then the value in its colour.</summary>
    private static void DrawStat(SpriteBatch spriteBatch, string label, string value, float x, float y, Color valueColor)
    {
        DrawText(spriteBatch, label, new Vector2(x, y), ProfilerTheme.TextMuted, 0.8f);
        DrawText(spriteBatch, value, new Vector2(x + 102f, y), valueColor, 0.8f);
    }

    /// <summary>Draws a line of text with the slight border that keeps it readable over gameplay.</summary>
    private static void DrawText(SpriteBatch spriteBatch, string text, Vector2 position, Color color, float scale)
    {
        Utils.DrawBorderString(spriteBatch, text, position, color, scale);
    }

    private static double AverageFrameTimeMs(RingBuffer<TickFrame> history)
    {
        if (history.Count == 0)
        {
            return 0d;
        }

        double sum = 0d;
        for (int i = 0; i < history.Count; i++)
        {
            sum += history[i].FrameTimeMs;
        }

        return sum / history.Count;
    }

    private static string CoverageBadge(int modId)
    {
        int measured = modId < HookInterceptor.MeasuredHookCounts.Count ? HookInterceptor.MeasuredHookCounts[modId] : 0;
        int total = modId < HookInterceptor.TotalHookCounts.Count ? HookInterceptor.TotalHookCounts[modId] : 0;
        return total == measured ? "full" : measured == 0 ? "none" : $"{measured}/{total}";
    }

    private static Color CoverageColor(int modId)
    {
        int measured = modId < HookInterceptor.MeasuredHookCounts.Count ? HookInterceptor.MeasuredHookCounts[modId] : 0;
        int total = modId < HookInterceptor.TotalHookCounts.Count ? HookInterceptor.TotalHookCounts[modId] : 0;
        double coverage = total > 0 ? measured / (double)total : 1d;
        return coverage >= 0.95d ? ProfilerTheme.Good : coverage >= 0.75d ? ProfilerTheme.Amber : ProfilerTheme.Danger;
    }

    private static void CoverageTotals(out int total, out int measured, out int fullMods, out int partialMods)
    {
        total = 0;
        measured = 0;
        fullMods = 0;
        partialMods = 0;
        int mods = HookInterceptor.ProfiledModNames.Length;

        for (int i = 0; i < mods; i++)
        {
            int modTotal = i < HookInterceptor.TotalHookCounts.Count ? HookInterceptor.TotalHookCounts[i] : 0;
            int modMeasured = i < HookInterceptor.MeasuredHookCounts.Count ? HookInterceptor.MeasuredHookCounts[i] : 0;
            total += modTotal;
            measured += modMeasured;
            if (modTotal == modMeasured)
            {
                fullMods++;
            }
            else
            {
                partialMods++;
            }
        }
    }
}
