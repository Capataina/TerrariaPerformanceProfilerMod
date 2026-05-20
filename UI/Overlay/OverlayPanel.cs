#nullable enable

using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ModLoader;
using Terraria.UI;
using PerformanceProfiler.Profiling;

namespace PerformanceProfiler.UI.Overlay;

/// <summary>
/// The F9 profiler overlay's draggable panel. Owns chrome only:
///
/// <list type="bullet">
///   <item>Panel background and border</item>
///   <item>Header strip: title, NOW/30S AVG toggle, LIVE/PAUSED toggle, drag region</item>
///   <item>Tab strip: one pill per <see cref="TabRegistry"/> entry plus the metric pill</item>
///   <item>Per-tick stats: tick / avg / gc / entities / tick #</item>
///   <item>PROFILER HEALTH section: coverage bar, backend label, Parallel-mode compare</item>
/// </list>
///
/// <para>
/// Tab content (the per-mod tree, the spikes feed, future events / overview /
/// insights) is delegated to whichever <see cref="IOverlayTab"/> is active.
/// The chrome calls the active tab's <c>Tick</c>, <c>MeasurePanelHeight</c>,
/// and <c>Draw</c> each frame, and dispatches mouse input to it when the
/// click falls outside the chrome's regions.
/// </para>
/// </summary>
internal sealed class OverlayPanel : UIElement
{
    public const float PanelWidth = OverlayLayout.PanelWidth;

    private bool _dragging;
    private Vector2 _dragOffset;
    private float _appliedHeight;

    /// <summary>
    /// Initial panel height when the overlay is first appended. Tabs take
    /// over via <see cref="ApplyHeight"/> from the first frame onward, so
    /// the exact value here matters only for the moment between OnInitialize
    /// and the first Update.
    /// </summary>
    public static float InitialHeight(int modCount)
    {
        int shown = modCount < OverlayLayout.MaxModRows ? modCount : OverlayLayout.MaxModRows;
        float h = OverlayLayout.RowsTopOffset + shown * OverlayLayout.RowHeight + 10f;
        if (modCount > shown) h += 14f;
        return h;
    }

    // ---- Input ---------------------------------------------------------------

    public override void LeftMouseDown(UIMouseEvent evt)
    {
        base.LeftMouseDown(evt);

        float localY = evt.MousePosition.Y - GetDimensions().Position().Y;
        float localX = evt.MousePosition.X - GetDimensions().Position().X;

        // Header band: title + 2 toggles + drag.
        if (localY <= OverlayLayout.HeaderHeight)
        {
            if (ClickHeaderControl(localX, localY)) return;
            _dragging = true;
            _dragOffset = evt.MousePosition - GetDimensions().Position();
            return;
        }

        // Tab strip band: tab selection + metric pill.
        if (localY <= OverlayLayout.HeaderHeight + OverlayLayout.TabStripHeight)
        {
            ClickTabStrip(localX);
            return;
        }

        // Content band: delegate to the active tab.
        MetricCollector? collector = ModContent.GetInstance<ProfilerSystem>()?.Collector;
        if (collector == null) return;
        TabRegistry.Active.HandleClick(localX, localY, collector);
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

        MetricCollector? collector = ModContent.GetInstance<ProfilerSystem>()?.Collector;
        if (collector == null) return;

        int delta = evt.ScrollWheelValue > 0 ? -1 : 1;
        TabRegistry.Active.HandleScroll(delta, collector);
    }

    private bool ClickHeaderControl(float localX, float localY)
    {
        if (localY < OverlayLayout.ToggleY || localY > OverlayLayout.ToggleY + OverlayLayout.ToggleHeight)
            return false;

        MetricCollector? collector = ModContent.GetInstance<ProfilerSystem>()?.Collector;

        // NOW / 30S AVG pill
        if (localX >= OverlayLayout.MetricToggleX && localX <= OverlayLayout.MetricToggleX + OverlayLayout.MetricToggleW)
        {
            OverlayState.ShowAverage = !OverlayState.ShowAverage;
            if (OverlayState.Paused && collector != null)
                OverlayState.CaptureSnapshot(collector);
            return true;
        }

        // LIVE / PAUSED pill
        if (localX >= OverlayLayout.PauseToggleX && localX <= OverlayLayout.PauseToggleX + OverlayLayout.PauseToggleW)
        {
            OverlayState.Paused = !OverlayState.Paused;
            if (OverlayState.Paused && collector != null)
                OverlayState.CaptureSnapshot(collector);
            return true;
        }

        return false;
    }

    private void ClickTabStrip(float localX)
    {
        // Metric pill first (right edge of the strip).
        Rectangle metricPill = MetricPillRect(GetDimensions().ToRectangle());
        float pillLocalX = metricPill.X - (int)GetDimensions().X;
        if (localX >= pillLocalX && localX <= pillLocalX + metricPill.Width)
        {
            OverlayState.CurrentMetric = (MetricView)(((int)OverlayState.CurrentMetric + 1) % 3);
            return;
        }

        // Tab pills (left side). Number of pills follows TabRegistry.
        int tabCount = TabRegistry.Tabs.Count;
        for (int slot = 0; slot < tabCount; slot++)
        {
            float x = OverlayLayout.TabFirstX + slot * (OverlayLayout.TabWidth + OverlayLayout.TabGap);
            if (localX >= x && localX <= x + OverlayLayout.TabWidth)
            {
                OverlayState.ActiveTabIndex = slot;
                return;
            }
        }
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

        // Tab refresh + panel resize. The active tab decides its own height;
        // the chrome only enforces the change against the UIElement.
        IOverlayTab active = TabRegistry.Active;
        active.Tick(collector);
        ApplyHeight(active.MeasurePanelHeight(collector));
    }

    private void ApplyHeight(float h)
    {
        if (Math.Abs(h - _appliedHeight) > 0.5f)
        {
            _appliedHeight = h;
            Height.Set(h, 0f);
            Recalculate();
        }
    }

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

        ProfilerTheme.DrawPanel(spriteBatch, area, ProfilerTheme.Panel, ProfilerTheme.Border);
        DrawHeaderStrip(spriteBatch, area);
        DrawTabStrip(spriteBatch, area);

        MetricCollector? collector = ModContent.GetInstance<ProfilerSystem>()?.Collector;
        if (collector == null || collector.History.Count == 0)
        {
            float x = area.X + 14f;
            float y = area.Y + OverlayLayout.HeaderHeight + OverlayLayout.TabStripHeight + OverlayLayout.StatStartY;
            OverlayDraw.Text(spriteBatch, "waiting for a world...", new Vector2(x, y), ProfilerTheme.TextMuted, 0.8f);
            return;
        }

        DrawStats(spriteBatch, area, collector);
        DrawProfilerHealth(spriteBatch, area, collector);

        // Tab content: everything from DividerOffset down.
        TabRegistry.Active.Draw(spriteBatch, area, collector);
    }

    private static void DrawHeaderStrip(SpriteBatch spriteBatch, Rectangle area)
    {
        Rectangle header = new Rectangle(area.X, area.Y, area.Width, (int)OverlayLayout.HeaderHeight);
        ProfilerTheme.FillRect(spriteBatch, header, ProfilerTheme.Header);
        ProfilerTheme.DrawBorder(spriteBatch, header, ProfilerTheme.Border);
        ProfilerTheme.FillRect(spriteBatch, new Rectangle(area.X, area.Y, 2, (int)OverlayLayout.HeaderHeight), ProfilerTheme.Accent);

        OverlayDraw.Text(spriteBatch, "PERFORMANCE PROFILER",
            new Vector2(area.X + 12, area.Y + 8), ProfilerTheme.Accent, 0.82f);
        OverlayDraw.Toggle(spriteBatch, area.X + OverlayLayout.MetricToggleX, area.Y + OverlayLayout.ToggleY,
            OverlayLayout.MetricToggleW, OverlayState.ShowAverage ? "30S AVG" : "NOW", OverlayState.ShowAverage);
        OverlayDraw.Toggle(spriteBatch, area.X + OverlayLayout.PauseToggleX, area.Y + OverlayLayout.ToggleY,
            OverlayLayout.PauseToggleW, OverlayState.Paused ? "PAUSED" : "LIVE", OverlayState.Paused);
    }

    private static void DrawTabStrip(SpriteBatch spriteBatch, Rectangle area)
    {
        float stripY = area.Y + OverlayLayout.HeaderHeight;
        Rectangle stripRect = new Rectangle(area.X, (int)stripY, area.Width, (int)OverlayLayout.TabStripHeight);
        ProfilerTheme.FillRect(spriteBatch, stripRect, ProfilerTheme.Panel);
        ProfilerTheme.FillRect(spriteBatch,
            new Rectangle(area.X, (int)(stripY + OverlayLayout.TabStripHeight - 1), area.Width, 1), ProfilerTheme.Border);

        int activeIdx = OverlayState.ActiveTabIndex;
        for (int slot = 0; slot < TabRegistry.Tabs.Count; slot++)
        {
            DrawTabPill(spriteBatch, area, slot, TabRegistry.Tabs[slot].Label, slot == activeIdx);
        }

        DrawMetricPill(spriteBatch, area);
    }

    private static void DrawTabPill(SpriteBatch spriteBatch, Rectangle area, int slot, string label, bool active)
    {
        float x = area.X + OverlayLayout.TabFirstX + slot * (OverlayLayout.TabWidth + OverlayLayout.TabGap);
        float y = area.Y + OverlayLayout.HeaderHeight + 3f;
        Rectangle r = new Rectangle((int)x, (int)y, (int)OverlayLayout.TabWidth, (int)(OverlayLayout.TabStripHeight - 6));

        ProfilerTheme.FillRect(spriteBatch, r, active ? new Color(25, 40, 60) : ProfilerTheme.Panel);
        ProfilerTheme.DrawBorder(spriteBatch, r, active ? ProfilerTheme.Accent : ProfilerTheme.Border);
        OverlayDraw.Text(spriteBatch, label,
            new Vector2(r.X + 10, r.Y + 2),
            active ? ProfilerTheme.Accent : ProfilerTheme.TextMuted, 0.66f);
    }

    private static void DrawMetricPill(SpriteBatch spriteBatch, Rectangle area)
    {
        string label = OverlayState.CurrentMetric switch
        {
            MetricView.Cpu  => "CPU",
            MetricView.Mem  => "MEM",
            MetricView.Both => "BOTH",
            _               => "?",
        };
        Rectangle r = MetricPillRect(area);
        ProfilerTheme.FillRect(spriteBatch, r, new Color(25, 40, 60));
        ProfilerTheme.DrawBorder(spriteBatch, r, ProfilerTheme.Accent);
        OverlayDraw.Text(spriteBatch, label,
            new Vector2(r.X + 8, r.Y + 2),
            ProfilerTheme.Accent, 0.66f);
    }

    private static Rectangle MetricPillRect(Rectangle area)
    {
        float w = 56f;
        float x = area.X + area.Width - 14f - w;
        float y = area.Y + OverlayLayout.HeaderHeight + 3f;
        return new Rectangle((int)x, (int)y, (int)w, (int)(OverlayLayout.TabStripHeight - 6));
    }

    private static void DrawStats(SpriteBatch spriteBatch, Rectangle area, MetricCollector collector)
    {
        float x = area.X + 14f;
        float y = area.Y + OverlayLayout.HeaderHeight + OverlayLayout.TabStripHeight + OverlayLayout.StatStartY;

        RingBuffer<TickFrame> history = collector.History;
        TickFrame latest = history.Newest;

        OverlayDraw.Stat(spriteBatch, "tick",    $"{latest.FrameTimeMs:F2} ms",                                          x,        y,                              ProfilerTheme.Amber);
        OverlayDraw.Stat(spriteBatch, "avg 30s", $"{AverageFrameTimeMs(history):F2} ms",                                 x + 250f, y,                              ProfilerTheme.Text);
        OverlayDraw.Stat(spriteBatch, "gc",      $"{latest.GcTimeMs:F2} ms",                                             x,        y + OverlayLayout.StatGap,      ProfilerTheme.Good);
        OverlayDraw.Stat(spriteBatch, "entities",
            $"npc {latest.NpcCount}   proj {latest.ProjectileCount}   dust {latest.DustCount}",
            x + 250f, y + OverlayLayout.StatGap, ProfilerTheme.Text);
        OverlayDraw.Text(spriteBatch, $"tick #{latest.TickIndex}",
            new Vector2(x, y + OverlayLayout.StatGap * 2f + 2f), ProfilerTheme.TextDim, 0.72f);

        // Thin separator below stats.
        ProfilerTheme.FillRect(spriteBatch,
            new Rectangle(area.X + 8, area.Y + (int)OverlayLayout.HealthTopOffset - 6, area.Width - 16, 1),
            ProfilerTheme.Border);
    }

    private static void DrawProfilerHealth(SpriteBatch spriteBatch, Rectangle area, MetricCollector collector)
    {
        float x = area.X + 14f;
        float y = area.Y + OverlayLayout.HealthTopOffset;

        CoverageTotals(out int total, out int measured, out int fullMods, out int partialMods);
        double coverage = total > 0 ? measured / (double)total : 1d;
        Color  covColor = coverage >= 0.95d ? ProfilerTheme.Good
                        : coverage >= 0.75d ? ProfilerTheme.Amber
                        : ProfilerTheme.Danger;

        OverlayDraw.Text(spriteBatch, "PROFILER HEALTH",                          new Vector2(x,        y), ProfilerTheme.Accent,    0.68f);
        OverlayDraw.Text(spriteBatch, $"hooks {measured}/{total} ({coverage:P0})", new Vector2(x + 142f, y), covColor,                0.68f);
        OverlayDraw.Text(spriteBatch, $"full {fullMods}  partial {partialMods}",  new Vector2(x + 358f, y), ProfilerTheme.TextMuted, 0.68f);

        string backendLabel = HookBackend.Mode switch
        {
            HookBackendMode.Delegate => "backend: delegate",
            HookBackendMode.ILHook   => "backend: ilhook",
            HookBackendMode.Parallel => "backend: parallel",
            _                        => "backend: ?",
        };
        OverlayDraw.Text(spriteBatch, backendLabel, new Vector2(x + 510f, y), ProfilerTheme.Accent, 0.58f);

        int barW = area.Width - 28;
        ProfilerTheme.FillRect(spriteBatch, new Rectangle((int)x, (int)y + 18, barW, 10), ProfilerTheme.Border);
        int fill = (int)(barW * coverage);
        if (fill > 0)
            ProfilerTheme.FillRect(spriteBatch, new Rectangle((int)x, (int)y + 18, fill, 10), covColor);

        if (HookBackend.Mode == HookBackendMode.Parallel)
        {
            double del = collector.BackendTotalMs0;
            double il  = collector.BackendTotalMs1;
            double pct = collector.BackendDivergence * 100d;
            Color  pctColor = Math.Abs(pct) <= 5d ? ProfilerTheme.Good
                            : Math.Abs(pct) <= 20d ? ProfilerTheme.Amber
                            : ProfilerTheme.Danger;
            OverlayDraw.Text(spriteBatch,
                $"compare   delegate {del:F2}ms   ilhook {il:F2}ms",
                new Vector2(x, y + 32f), ProfilerTheme.TextMuted, 0.62f);
            OverlayDraw.Text(spriteBatch, $"Δ {pct:+0.0;-0.0;0.0}%",
                new Vector2(x + 380f, y + 32f), pctColor, 0.62f);
        }
    }

    private static double AverageFrameTimeMs(RingBuffer<TickFrame> history)
    {
        if (history.Count == 0) return 0d;
        double sum = 0d;
        for (int i = 0; i < history.Count; i++) sum += history[i].FrameTimeMs;
        return sum / history.Count;
    }

    private static void CoverageTotals(out int total, out int measured, out int fullMods, out int partialMods)
    {
        bool useILHook = HookBackend.Mode != HookBackendMode.Delegate;
        System.Collections.Generic.IReadOnlyList<int> totals    = useILHook ? ILHookInterceptor.TotalHookCounts    : HookInterceptor.TotalHookCounts;
        System.Collections.Generic.IReadOnlyList<int> measureds = useILHook ? ILHookInterceptor.MeasuredHookCounts : HookInterceptor.MeasuredHookCounts;

        total = 0; measured = 0; fullMods = 0; partialMods = 0;
        int mods = HookInterceptor.ProfiledModNames.Length;
        for (int i = 0; i < mods; i++)
        {
            int modTotal    = i < totals.Count    ? totals[i]    : 0;
            int modMeasured = i < measureds.Count ? measureds[i] : 0;
            total    += modTotal;
            measured += modMeasured;
            if (modTotal == modMeasured) fullMods++;
            else                         partialMods++;
        }
    }
}
