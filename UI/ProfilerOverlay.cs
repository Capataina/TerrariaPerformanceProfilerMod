#nullable enable

using System;
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
/// them, the btop-style per-mod CPU list built from <see cref="HookInterceptor"/>'s
/// attribution data.
/// </summary>
public sealed class ProfilerOverlay : UIState
{
    public override void OnInitialize()
    {
        OverlayPanel panel = new OverlayPanel();
        panel.Left.Set(16f, 0f);
        panel.Top.Set(16f, 0f);
        panel.Width.Set(OverlayPanel.PanelWidth, 0f);
        panel.Height.Set(OverlayPanel.PanelHeightFor(HookInterceptor.ProfiledModNames.Length), 0f);
        Append(panel);
    }
}

/// <summary>One per-mod row of the cost list: a mod name and its smoothed cost.</summary>
internal readonly struct ModRow : IComparable<ModRow>
{
    public readonly string Name;
    public readonly double Ms;

    public ModRow(string name, double ms)
    {
        Name = name;
        Ms = ms;
    }

    /// <summary>Sorts most-expensive first.</summary>
    public int CompareTo(ModRow other) => other.Ms.CompareTo(Ms);
}

/// <summary>
/// The custom-drawn overlay panel. Everything is hand-drawn in <see cref="DrawSelf"/>
/// with <see cref="ProfilerTheme"/>; no stock tModLoader widget chrome is used.
/// Draggable by its header strip.
/// </summary>
internal sealed class OverlayPanel : UIElement
{
    public const float PanelWidth = 380f;

    private const float HeaderHeight = 26f;
    private const float StatGap = 24f;
    private const float ListDividerOffset = 162f;
    private const float RowsTopOffset = 188f;
    private const float RowHeight = 17f;
    private const int MaxModRows = 12;

    private bool _dragging;
    private Vector2 _dragOffset;

    // Reused each frame to sort the per-mod list without allocating.
    private ModRow[] _rows = Array.Empty<ModRow>();

    /// <summary>Panel height needed to show <paramref name="modCount"/> mods (capped at <see cref="MaxModRows"/>).</summary>
    public static float PanelHeightFor(int modCount)
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

        // Drag only by the header strip, the way a window title bar works.
        Vector2 panelPosition = GetDimensions().Position();
        if (evt.MousePosition.Y - panelPosition.Y <= HeaderHeight)
        {
            _dragging = true;
            _dragOffset = evt.MousePosition - panelPosition;
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
        DrawStat(spriteBatch, "avg 30s", $"{AverageFrameTimeMs(history):F2} ms", x, y + StatGap, ProfilerTheme.Text);
        DrawStat(spriteBatch, "gc pause", $"{latest.GcTimeMs:F2} ms", x, y + StatGap * 2f, ProfilerTheme.Good);
        DrawStat(spriteBatch, "entities",
            $"npc {latest.NpcCount}   proj {latest.ProjectileCount}   dust {latest.DustCount}",
            x, y + StatGap * 3f, ProfilerTheme.Text);
        DrawText(spriteBatch, $"tick #{latest.TickIndex}", new Vector2(x, y + StatGap * 4f + 4f),
            ProfilerTheme.TextDim, 0.72f);

        // ---- Per-mod CPU list ----
        DrawModList(spriteBatch, area, collector);
    }

    /// <summary>Draws the btop-style sorted per-mod cost list with colour-graded bars.</summary>
    private void DrawModList(SpriteBatch spriteBatch, Rectangle area, MetricCollector collector)
    {
        // Section divider and heading.
        int dividerY = area.Y + (int)ListDividerOffset;
        ProfilerTheme.FillRect(spriteBatch, new Rectangle(area.X + 8, dividerY, area.Width - 16, 1), ProfilerTheme.Border);
        DrawText(spriteBatch, "PER-MOD CPU   ·   ms per tick", new Vector2(area.X + 14, dividerY + 8f),
            ProfilerTheme.Accent, 0.72f);

        int rowCount = BuildSortedRows(collector);
        if (rowCount == 0)
        {
            DrawText(spriteBatch, "no per-mod data yet", new Vector2(area.X + 14, area.Y + RowsTopOffset),
                ProfilerTheme.TextMuted, 0.72f);
            return;
        }

        double maxMs = _rows[0].Ms; // rows are sorted most-expensive first
        int shown = rowCount < MaxModRows ? rowCount : MaxModRows;
        float rowY = area.Y + RowsTopOffset;

        for (int i = 0; i < shown; i++)
        {
            DrawModRow(spriteBatch, _rows[i], area.X, rowY, maxMs);
            rowY += RowHeight;
        }

        if (rowCount > shown)
        {
            DrawText(spriteBatch, $"+ {rowCount - shown} more mods", new Vector2(area.X + 14, rowY + 1f),
                ProfilerTheme.TextDim, 0.66f);
        }
    }

    /// <summary>Draws one mod row: name, a colour-graded cost bar, the millisecond value.</summary>
    private static void DrawModRow(SpriteBatch spriteBatch, ModRow row, int panelX, float y, double maxMs)
    {
        DrawText(spriteBatch, Truncate(row.Name, 20), new Vector2(panelX + 14, y), ProfilerTheme.Text, 0.7f);

        // Bar: a dim track with a graded fill proportional to the heaviest mod.
        const int barWidth = 132;
        Rectangle track = new Rectangle(panelX + 168, (int)y + 3, barWidth, 8);
        ProfilerTheme.FillRect(spriteBatch, track, ProfilerTheme.Border);

        double fraction = maxMs > 0d ? row.Ms / maxMs : 0d;
        int fillWidth = (int)(barWidth * fraction);
        if (fillWidth > 0)
        {
            ProfilerTheme.FillRect(spriteBatch,
                new Rectangle(track.X, track.Y, fillWidth, track.Height), ProfilerTheme.CostColor(fraction));
        }

        DrawText(spriteBatch, row.Ms.ToString("F3"), new Vector2(panelX + 312, y), ProfilerTheme.Amber, 0.68f);
    }

    /// <summary>Fills and sorts <see cref="_rows"/> from the collector; returns the row count.</summary>
    private int BuildSortedRows(MetricCollector collector)
    {
        string[] names = HookInterceptor.ProfiledModNames;
        System.Collections.Generic.IReadOnlyList<double> costs = collector.PerModCpuMs;
        int n = names.Length < costs.Count ? names.Length : costs.Count;

        if (_rows.Length < n)
        {
            _rows = new ModRow[n];
        }

        for (int i = 0; i < n; i++)
        {
            _rows[i] = new ModRow(names[i], costs[i]);
        }

        if (n > 1)
        {
            Array.Sort(_rows, 0, n);
        }

        return n;
    }

    private static string Truncate(string text, int max)
    {
        return text.Length <= max ? text : text.Substring(0, max - 2) + "..";
    }

    /// <summary>Draws a "label   value" stat line: a muted label, then the value in its colour.</summary>
    private static void DrawStat(SpriteBatch spriteBatch, string label, string value, float x, float y, Color valueColor)
    {
        DrawText(spriteBatch, label, new Vector2(x, y), ProfilerTheme.TextMuted, 0.8f);
        DrawText(spriteBatch, value, new Vector2(x + 92f, y), valueColor, 0.8f);
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
}
