#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ModLoader;
using Terraria.UI;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.UI.Overlay.Components;

namespace PerformanceProfiler.UI.Overlay;

/// <summary>
/// The F9 profiler overlay's draggable panel. Owns the entire chrome
/// (header, tab strip, raised stat cards, PROFILER HEALTH card) and
/// delegates tab content rendering to the active <see cref="IOverlayTab"/>.
///
/// <para>
/// Post-overhaul layout (top to bottom):
/// </para>
/// <list type="bullet">
///   <item>Header strip — title, NOW/30S AVG pill, LIVE/PAUSED pill,
///         DEFAULT/COMPACT pill. Drag region.</item>
///   <item>Tab strip — one pill per <see cref="TabRegistry"/> entry, plus
///         the CPU/MEM/BOTH metric pill on the right.</item>
///   <item>Stat card row — four raised cards (this tick / avg 30 s / gc /
///         tick #) distributed across the panel width.</item>
///   <item>PROFILER HEALTH card — coverage progress + backend label +
///         self-footprint line. One raised card.</item>
///   <item>Tab content area — everything below the chrome. Tab decides
///         its own height; chrome resizes the panel to fit.</item>
/// </list>
/// </summary>
internal sealed class OverlayPanel : UIElement
{
    public static float PanelWidth
    {
        get
        {
            ProfilerConfig? cfg = ModContent.GetInstance<ProfilerConfig>();
            if (cfg != null && cfg.PanelWidthOverride > 0)
            {
                int o = cfg.PanelWidthOverride;
                if (o < (int)OverlayLayout.PanelWidthMin) o = (int)OverlayLayout.PanelWidthMin;
                if (o > (int)OverlayLayout.PanelWidthMax) o = (int)OverlayLayout.PanelWidthMax;
                return o;
            }
            return OverlayLayoutCurrent.PanelWidth;
        }
    }

    private const int ResizeHandleSize = 14;

    private bool _dragging;
    private Vector2 _dragOffset;
    private float _appliedHeight;
    private float _appliedWidth;

    // Resize-drag state — bottom-right corner handle.
    private bool _resizing;
    private float _resizeStartWidth;
    private float _resizeStartMouseX;

    public static float InitialHeight(int modCount)
    {
        int shown = modCount < OverlayLayout.MaxModRows ? modCount : OverlayLayout.MaxModRows;
        float h = OverlayLayoutCurrent.ChromeHeight + shown * OverlayLayoutCurrent.RowHeight + 32f;
        if (modCount > shown) h += 14f;
        return h;
    }

    // ---- Hit-test cached pill rectangles -------------------------------------
    //
    // Recomputed on every draw + click. We cache them between drawing and
    // hit-testing so the click handler doesn't recompute the layout — the
    // pills don't move unless the panel is being dragged or the mode is
    // flipped.

    private Rectangle _avgPillRect;
    private Rectangle _pausePillRect;
    private Rectangle _modePillRect;
    private Rectangle _metricPillRect;

    // ---- Input ---------------------------------------------------------------

    public override void LeftMouseDown(UIMouseEvent evt)
    {
        base.LeftMouseDown(evt);
        Vector2 panelPos = GetDimensions().Position();
        float localX = evt.MousePosition.X - panelPos.X;
        float localY = evt.MousePosition.Y - panelPos.Y;
        Rectangle dims = GetDimensions().ToRectangle();

        // Resize handle: bottom-right corner.
        if (localX >= dims.Width - ResizeHandleSize && localY >= dims.Height - ResizeHandleSize)
        {
            _resizing = true;
            _resizeStartMouseX = evt.MousePosition.X;
            _resizeStartWidth = dims.Width;
            return;
        }

        // Header band: pills + drag region.
        if (localY <= OverlayLayoutCurrent.HeaderHeight + OverlayLayoutCurrent.PanelPaddingY)
        {
            if (ClickHeaderPill(localX, localY)) return;
            _dragging = true;
            _dragOffset = evt.MousePosition - panelPos;
            return;
        }

        // Tab strip band.
        float tabStripBottom = OverlayLayoutCurrent.PanelPaddingY
                             + OverlayLayoutCurrent.HeaderHeight + OverlayLayoutCurrent.ChromeRegionGap
                             + OverlayLayoutCurrent.TabStripHeight;
        if (localY <= tabStripBottom)
        {
            ClickTabStrip(localX);
            return;
        }

        // Below chrome: delegate to active tab.
        if (localY <= OverlayLayoutCurrent.ChromeHeight) return;
        MetricCollector? collector = ModContent.GetInstance<ProfilerSystem>()?.Collector;
        if (collector == null) return;
        TabRegistry.ResolveActive(collector).HandleClick(localX, localY, collector);
    }

    public override void LeftMouseUp(UIMouseEvent evt)
    {
        base.LeftMouseUp(evt);
        _dragging = false;

        if (_resizing)
        {
            _resizing = false;
            // Persist the resized width into the config so it survives the
            // session. The config writes its JSON when the menu closes; we
            // also flag the value as dirty by saving via ConfigManager if
            // available, but since we're in the game loop the next
            // config-save pass handles it.
            ProfilerConfig? cfg = ModContent.GetInstance<ProfilerConfig>();
            if (cfg != null)
            {
                cfg.PanelWidthOverride = (int)_appliedWidth;
            }
        }
    }

    public override void ScrollWheel(UIScrollWheelEvent evt)
    {
        base.ScrollWheel(evt);
        if (!IsMouseHovering) return;
        MetricCollector? collector = ModContent.GetInstance<ProfilerSystem>()?.Collector;
        if (collector == null) return;
        int delta = evt.ScrollWheelValue > 0 ? -1 : 1;
        TabRegistry.ResolveActive(collector).HandleScroll(delta, collector);
    }

    private bool ClickHeaderPill(float localX, float localY)
    {
        MetricCollector? collector = ModContent.GetInstance<ProfilerSystem>()?.Collector;

        if (Pill.HitTest(_avgPillRect, localX + GetDimensions().X, localY + GetDimensions().Y))
        {
            // The cached rect uses panel-local coords; convert click to the
            // same frame for hit-test consistency.
        }

        // Use raw local-coord rectangles instead — easier than wrestling with
        // converted spaces. We store rects in panel-local coords below.
        if (RectContainsLocal(_avgPillRect, localX, localY))
        {
            OverlayState.ShowAverage = !OverlayState.ShowAverage;
            if (OverlayState.Paused && collector != null) OverlayState.CaptureSnapshot(collector);
            return true;
        }
        if (RectContainsLocal(_pausePillRect, localX, localY))
        {
            OverlayState.Paused = !OverlayState.Paused;
            if (OverlayState.Paused && collector != null) OverlayState.CaptureSnapshot(collector);
            return true;
        }
        if (RectContainsLocal(_modePillRect, localX, localY))
        {
            OverlayState.Mode = OverlayState.Mode == OverlayMode.Default
                ? OverlayMode.Compact
                : OverlayMode.Default;
            // Force a width refresh on next Update.
            _appliedWidth = -1f;
            return true;
        }
        return false;
    }

    private static bool RectContainsLocal(Rectangle r, float lx, float ly)
        => lx >= r.X && lx <= r.Right && ly >= r.Y && ly <= r.Bottom;

    private void ClickTabStrip(float localX)
    {
        if (RectContainsLocal(_metricPillRect, localX, _metricPillRect.Y + 1))
        {
            OverlayState.CurrentMetric = (MetricView)(((int)OverlayState.CurrentMetric + 1) % 3);
            return;
        }
        MetricCollector? collector = ModContent.GetInstance<ProfilerSystem>()?.Collector;
        IReadOnlyList<IOverlayTab> visible = TabRegistry.Visible(collector);
        float tabFirstX = OverlayLayoutCurrent.PanelPaddingX;
        float tabWidth = OverlayLayoutCurrent.TabWidth;
        float tabGap = OverlayLayoutCurrent.TabGap;
        for (int slot = 0; slot < visible.Count; slot++)
        {
            float x = tabFirstX + slot * (tabWidth + tabGap);
            if (localX >= x && localX <= x + tabWidth)
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
        if (IsMouseHovering || _dragging) Main.LocalPlayer.mouseInterface = true;

        if (_dragging)
        {
            if (!Main.mouseLeft) _dragging = false;
            else FollowMouse();
        }

        if (_resizing)
        {
            if (!Main.mouseLeft) _resizing = false;
            else
            {
                float delta = Main.MouseScreen.X - _resizeStartMouseX;
                float w = _resizeStartWidth + delta;
                if (w < OverlayLayout.PanelWidthMin) w = OverlayLayout.PanelWidthMin;
                if (w > OverlayLayout.PanelWidthMax) w = OverlayLayout.PanelWidthMax;
                ApplyWidth(w);
            }
        }
        else
        {
            // Honour config override if set; otherwise follow active mode.
            ApplyWidth(PanelWidth);
        }

        MetricCollector? collector = ModContent.GetInstance<ProfilerSystem>()?.Collector;
        if (collector == null) return;
        IOverlayTab active = TabRegistry.ResolveActive(collector);
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

    private void ApplyWidth(float w)
    {
        if (Math.Abs(w - _appliedWidth) > 0.5f)
        {
            _appliedWidth = w;
            Width.Set(w, 0f);
            Recalculate();
        }
    }

    private void FollowMouse()
    {
        Vector2 target = Main.MouseScreen - _dragOffset;
        float maxX = Main.screenWidth - GetDimensions().Width;
        float maxY = Main.screenHeight - GetDimensions().Height;
        Left.Set(MathHelper.Clamp(target.X, 0f, maxX < 0f ? 0f : maxX), 0f);
        Top.Set(MathHelper.Clamp(target.Y, 0f, maxY < 0f ? 0f : maxY), 0f);
        Recalculate();
    }

    // ---- Drawing -------------------------------------------------------------

    protected override void DrawSelf(SpriteBatch spriteBatch)
    {
        Rectangle area = GetDimensions().ToRectangle();

        // Outer panel background + border.
        ProfilerTheme.FillRect(spriteBatch, area, ProfilerTheme.Background);
        ProfilerTheme.DrawBorder(spriteBatch, area, ProfilerTheme.Border);

        DrawHeader(spriteBatch, area);
        DrawTabStrip(spriteBatch, area);

        MetricCollector? collector = ModContent.GetInstance<ProfilerSystem>()?.Collector;
        if (collector == null || collector.History.Count == 0)
        {
            DrawEmptyStatCards(spriteBatch, area);
            DrawEmptyHealthCard(spriteBatch, area);
            float emptyY = area.Y + OverlayLayoutCurrent.ChromeHeight + 14f;
            OverlayDraw.Text(spriteBatch, "waiting for a world...",
                new Vector2(area.X + OverlayLayoutCurrent.PanelPaddingX + 8, emptyY),
                ProfilerTheme.TextMuted, OverlayLayoutCurrent.TextScaleRow);
            return;
        }

        DrawStatCards(spriteBatch, area, collector);
        DrawHealthCard(spriteBatch, area, collector);

        // Tab content area.
        TabRegistry.ResolveActive(collector).Draw(spriteBatch, area, collector);

        // Resize handle: small diagonal grip in the bottom-right corner.
        DrawResizeHandle(spriteBatch, area);
    }

    private static void DrawResizeHandle(SpriteBatch sb, Rectangle area)
    {
        int x = area.Right - ResizeHandleSize;
        int y = area.Bottom - ResizeHandleSize;
        // Three diagonal lines, decreasing length, suggesting a grip.
        for (int i = 0; i < 3; i++)
        {
            int off = (i + 1) * 3;
            ProfilerTheme.FillRect(sb, new Rectangle(x + ResizeHandleSize - off, y + ResizeHandleSize - 2, off - 1, 1),
                ProfilerTheme.TextDim);
            ProfilerTheme.FillRect(sb, new Rectangle(x + ResizeHandleSize - 2, y + ResizeHandleSize - off, 1, off - 1),
                ProfilerTheme.TextDim);
        }
    }

    // ---- Header --------------------------------------------------------------

    private void DrawHeader(SpriteBatch sb, Rectangle area)
    {
        float padX = OverlayLayoutCurrent.PanelPaddingX;
        float padY = OverlayLayoutCurrent.PanelPaddingY;
        float headerH = OverlayLayoutCurrent.HeaderHeight;

        Rectangle headerArea = new Rectangle(
            area.X + (int)padX,
            area.Y + (int)padY,
            area.Width - (int)padX * 2,
            (int)headerH);
        ProfilerTheme.FillRect(sb, headerArea, ProfilerTheme.Header);
        ProfilerTheme.DrawBorder(sb, headerArea, ProfilerTheme.Border);
        // Accent stripe on the left.
        ProfilerTheme.FillRect(sb, new Rectangle(headerArea.X, headerArea.Y, 3, headerArea.Height), ProfilerTheme.Accent);

        OverlayDraw.Text(sb, "PERFORMANCE PROFILER",
            new Vector2(headerArea.X + 12, headerArea.Y + (headerArea.Height - 16) / 2),
            ProfilerTheme.Accent, OverlayLayoutCurrent.TextScaleH1);

        // Right-side pills, packed right-to-left: MODE | LIVE/PAUSED | NOW/AVG.
        int pillH = (int)System.Math.Max(20, headerH - 8);
        int pillY = headerArea.Y + (headerArea.Height - pillH) / 2;
        int pillRight = headerArea.Right - 8;

        // MODE pill (Default / Compact).
        string modeLabel = OverlayState.Mode == OverlayMode.Default ? "DEFAULT" : "COMPACT";
        int modeW = 86;
        _modePillRect = new Rectangle(pillRight - modeW - (int)area.X, pillY - (int)area.Y, modeW, pillH);
        Rectangle modeAbs = new Rectangle(pillRight - modeW, pillY, modeW, pillH);
        Pill.Draw(sb, modeAbs, modeLabel, active: OverlayState.Mode == OverlayMode.Compact, leadingDot: false);
        pillRight -= modeW + 6;

        // PAUSE pill.
        string pauseLabel = OverlayState.Paused ? "PAUSED" : "LIVE";
        int pauseW = 78;
        _pausePillRect = new Rectangle(pillRight - pauseW - (int)area.X, pillY - (int)area.Y, pauseW, pillH);
        Rectangle pauseAbs = new Rectangle(pillRight - pauseW, pillY, pauseW, pillH);
        Pill.Draw(sb, pauseAbs, pauseLabel, active: OverlayState.Paused, leadingDot: true);
        pillRight -= pauseW + 6;

        // AVG/NOW pill.
        string avgLabel = OverlayState.ShowAverage ? "30S AVG" : "NOW";
        int avgW = 72;
        _avgPillRect = new Rectangle(pillRight - avgW - (int)area.X, pillY - (int)area.Y, avgW, pillH);
        Rectangle avgAbs = new Rectangle(pillRight - avgW, pillY, avgW, pillH);
        Pill.Draw(sb, avgAbs, avgLabel, active: OverlayState.ShowAverage, leadingDot: false);
    }

    // ---- Tab strip -----------------------------------------------------------

    private void DrawTabStrip(SpriteBatch sb, Rectangle area)
    {
        float padX = OverlayLayoutCurrent.PanelPaddingX;
        float padY = OverlayLayoutCurrent.PanelPaddingY;
        float stripY = area.Y + padY + OverlayLayoutCurrent.HeaderHeight + OverlayLayoutCurrent.ChromeRegionGap;
        float stripH = OverlayLayoutCurrent.TabStripHeight;
        Rectangle stripArea = new Rectangle(area.X + (int)padX, (int)stripY, area.Width - (int)padX * 2, (int)stripH);
        ProfilerTheme.FillRect(sb, stripArea, ProfilerTheme.Panel);
        ProfilerTheme.DrawBorder(sb, stripArea, ProfilerTheme.Border);

        MetricCollector? collector = ModContent.GetInstance<ProfilerSystem>()?.Collector;
        IReadOnlyList<IOverlayTab> visible = TabRegistry.Visible(collector);
        int activeIdx = OverlayState.ActiveTabIndex;
        float tabFirstX = OverlayLayoutCurrent.PanelPaddingX;
        float tabWidth = OverlayLayoutCurrent.TabWidth;
        float tabGap = OverlayLayoutCurrent.TabGap;
        int pillH = (int)stripH - 6;
        int pillY = (int)stripY + 3;
        for (int slot = 0; slot < visible.Count; slot++)
        {
            float x = area.X + tabFirstX + slot * (tabWidth + tabGap);
            Rectangle tabRect = new Rectangle((int)x, pillY, (int)tabWidth, pillH);
            Pill.Draw(sb, tabRect, visible[slot].Label, active: slot == activeIdx);
        }

        // Metric pill on the right.
        int metricW = 76;
        int metricX = stripArea.Right - 8 - metricW;
        Rectangle metricAbs = new Rectangle(metricX, pillY, metricW, pillH);
        string metricLabel = OverlayState.CurrentMetric switch
        {
            MetricView.Cpu  => "CPU",
            MetricView.Mem  => "MEM",
            MetricView.Both => "BOTH",
            _               => "?",
        };
        Pill.Draw(sb, metricAbs, metricLabel, active: true);
        _metricPillRect = new Rectangle(metricX - area.X, pillY - area.Y, metricW, pillH);
    }

    // ---- Stat cards ----------------------------------------------------------

    private void DrawStatCards(SpriteBatch sb, Rectangle area, MetricCollector collector)
    {
        Rectangle[] cards = LayoutStatCards(area);
        TickFrame latest = collector.History.Newest;
        double avg30 = AverageFrameTimeMs(collector.History);

        // Card 1: this tick
        DrawStatCard(sb, cards[0], "THIS TICK", $"{latest.FrameTimeMs:F2} ms",
            ColourForFrameMs(latest.FrameTimeMs, collector.Baseline.FrameMsMedian));

        // Card 2: avg 30s
        DrawStatCard(sb, cards[1], "AVG 30 S", $"{avg30:F2} ms", ProfilerTheme.Text,
            footer: $"median {collector.Baseline.FrameMsMedian:F2} ms");

        // Card 3: gc this tick
        DrawStatCard(sb, cards[2], "GC THIS TICK", $"{latest.GcTimeMs:F1} ms",
            latest.GcTimeMs > 0d ? ProfilerTheme.Amber : ProfilerTheme.Good);

        // Card 4: tick + entity counts
        DrawStatCard(sb, cards[3], "TICK", $"#{latest.TickIndex}", ProfilerTheme.Text,
            footer: $"{latest.NpcCount} npc · {latest.ProjectileCount} proj · {latest.DustCount} dust");
    }

    private void DrawEmptyStatCards(SpriteBatch sb, Rectangle area)
    {
        Rectangle[] cards = LayoutStatCards(area);
        for (int i = 0; i < cards.Length; i++)
        {
            ProfilerCard.Draw(sb, cards[i], null, null);
        }
    }

    private void DrawStatCard(SpriteBatch sb, Rectangle area, string title, string value, Color valueColor, string? footer = null)
    {
        Rectangle body = ProfilerCard.Draw(sb, area, null, null);
        StatBlock.Draw(sb, body, title, value, valueColor, footer);
    }

    private Rectangle[] LayoutStatCards(Rectangle area)
    {
        const int CardCount = 4;
        float padX = OverlayLayoutCurrent.PanelPaddingX;
        float padY = OverlayLayoutCurrent.PanelPaddingY;
        float cardsY = area.Y + padY
                     + OverlayLayoutCurrent.HeaderHeight + OverlayLayoutCurrent.ChromeRegionGap
                     + OverlayLayoutCurrent.TabStripHeight + OverlayLayoutCurrent.ChromeRegionGap;
        float cardsAreaW = area.Width - padX * 2;
        float gap = OverlayLayoutCurrent.StatCardGap;
        float cardW = (cardsAreaW - gap * (CardCount - 1)) / CardCount;
        int cardH = (int)OverlayLayoutCurrent.StatCardHeight;
        Rectangle[] result = new Rectangle[CardCount];
        for (int i = 0; i < CardCount; i++)
        {
            float x = area.X + padX + i * (cardW + gap);
            result[i] = new Rectangle((int)x, (int)cardsY, (int)cardW, cardH);
        }
        return result;
    }

    // ---- PROFILER HEALTH card ------------------------------------------------

    private void DrawHealthCard(SpriteBatch sb, Rectangle area, MetricCollector collector)
    {
        Rectangle cardRect = LayoutHealthCard(area);
        CoverageTotals(out int total, out int measured, out int fullMods, out int partialMods);
        double coverage = total > 0 ? measured / (double)total : 1d;

        string title = $"PROFILER HEALTH  ·  {measured}/{total} hooks";
        string rightStat = $"{coverage:P0}";
        Rectangle body = ProfilerCard.Draw(sb, cardRect, title, rightStat);

        DrawHealthBody(sb, body, collector, coverage, measured, total, fullMods, partialMods);
    }

    private void DrawEmptyHealthCard(SpriteBatch sb, Rectangle area)
    {
        Rectangle cardRect = LayoutHealthCard(area);
        ProfilerCard.Draw(sb, cardRect, "PROFILER HEALTH", "waiting");
    }

    private Rectangle LayoutHealthCard(Rectangle area)
    {
        float padX = OverlayLayoutCurrent.PanelPaddingX;
        float padY = OverlayLayoutCurrent.PanelPaddingY;
        float startY = area.Y + padY
                     + OverlayLayoutCurrent.HeaderHeight + OverlayLayoutCurrent.ChromeRegionGap
                     + OverlayLayoutCurrent.TabStripHeight + OverlayLayoutCurrent.ChromeRegionGap
                     + OverlayLayoutCurrent.StatCardHeight + OverlayLayoutCurrent.ChromeRegionGap;
        int h = (int)OverlayLayoutCurrent.ProfilerHealthCardHeight;
        return new Rectangle(area.X + (int)padX, (int)startY,
            area.Width - (int)padX * 2, h);
    }

    private void DrawHealthBody(SpriteBatch sb, Rectangle body, MetricCollector collector,
        double coverage, int measured, int total, int fullMods, int partialMods)
    {
        int leftX = body.X + 8;
        float scale = OverlayLayoutCurrent.TextScaleBody;
        ProfilerSelfHealth health = collector.SelfHealth;

        // Three vertical bands inside the card body. Each band's Y is
        // computed against the body height instead of overlapping fixed
        // offsets, so the bar's bottom never collides with row 3's top.
        //
        //   row 1 (top)    "full X · partial Y"   +  "backend ilhook"  (right)
        //   row 2 (mid)    coverage heat bar (full body width)
        //   row 3 (bot)    "self … MB · KB/hook · % of game"  +  severity badge (right)
        //
        // Vertical budget (default body ~54px after the title bar):
        //   row1: top + 4    height ~12
        //   bar : centred    height 8
        //   row3: bottom - 4 height ~12

        const int rowH = 12;
        const int barH = 8;
        int row1Y = body.Y + 4;
        int barY  = body.Y + (body.Height - barH) / 2;
        int row3Y = body.Y + body.Height - rowH - 4;

        // Row 1 — backend + coverage-detail labels.
        string detail = $"full {fullMods}  ·  partial {partialMods}";
        OverlayDraw.Text(sb, detail, new Vector2(leftX, row1Y),
            ProfilerTheme.TextMuted, scale);

        string backendLabel = HookBackend.Mode switch
        {
            HookBackendMode.Delegate => "backend: delegate",
            HookBackendMode.ILHook   => "backend: ilhook",
            HookBackendMode.Parallel => "backend: parallel",
            _                        => "backend: ?",
        };
        // Approximate width of the backend label so we can right-align it.
        int backendApproxW = backendLabel.Length * 7;
        OverlayDraw.Text(sb, backendLabel,
            new Vector2(body.Right - backendApproxW - 8, row1Y),
            ProfilerTheme.Accent, scale);

        // Row 2 — coverage bar, full width.
        Rectangle barRect = new Rectangle(leftX, barY, body.Width - 16, barH);
        HeatBar.Draw(sb, barRect, coverage, 1d, coverage);

        // Row 3 — self-footprint + severity badge on the right.
        //
        // Text colour is always TextMuted regardless of severity. The
        // severity badge to the right is the channel for "this is bad" —
        // colouring the text Danger on a Danger-tinted bar background
        // is the contrast collision visible in the v0.2 screenshots.
        if (health.IsInstalled)
        {
            double selfMb = health.InstallDeltaBytes / (1024d * 1024d);
            double kbPerHook = health.BytesPerHook / 1024d;
            string selfLine;
            if (health.ProcessWorkingSetBytes > 0)
            {
                double processMb = health.ProcessWorkingSetBytes / (1024d * 1024d);
                // "self / process" is the honest framing — install-delta
                // bytes as a fraction of the current process working set.
                // Avoid the "% of game" label that drifts past 100% as the
                // process grows; "X MB / Y MB" reads correctly at every
                // ratio.
                selfLine = $"self  {selfMb:F0} MB / {processMb:F0} MB  ·  {kbPerHook:F1} KB/hook";
            }
            else
            {
                selfLine = $"self  {selfMb:F0} MB  ·  {kbPerHook:F1} KB/hook";
            }
            OverlayDraw.Text(sb, selfLine, new Vector2(leftX, row3Y),
                ProfilerTheme.TextMuted, scale);

            string sevLabel = health.Severity.ToString().ToLowerInvariant();
            int badgeW = SeverityBadge.MeasureWidth(sevLabel);
            SeverityBadge.DrawSelfHealth(sb,
                new Vector2(body.Right - badgeW - 8, row3Y - 1), health.Severity);
        }
    }

    // ---- Coverage totals (uses backend-aware view) ---------------------------

    private static void CoverageTotals(out int total, out int measured, out int fullMods, out int partialMods)
    {
        IReadOnlyList<int> totals    = HookCoverageView.TotalHookCounts;
        IReadOnlyList<int> measureds = HookCoverageView.MeasuredHookCounts;
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

    private static double AverageFrameTimeMs(RingBuffer<TickFrame> history)
    {
        if (history.Count == 0) return 0d;
        double sum = 0d;
        for (int i = 0; i < history.Count; i++) sum += history[i].FrameTimeMs;
        return sum / history.Count;
    }

    /// <summary>
    /// Colour for frame-time stat: green when at or below baseline median,
    /// amber when 2× the median, red when 4×. Relative threshold so the
    /// colour scales with the player's actual hardware.
    /// </summary>
    private static Color ColourForFrameMs(double frameMs, double baselineMs)
    {
        if (baselineMs <= 0d) return ProfilerTheme.Text;
        double ratio = frameMs / baselineMs;
        if (ratio >= 4d) return ProfilerTheme.Danger;
        if (ratio >= 2d) return ProfilerTheme.Amber;
        return ProfilerTheme.Good;
    }
}
