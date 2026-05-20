#nullable enable

using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.UI.Overlay.Components;

namespace PerformanceProfiler.UI.Overlay.Tabs;

/// <summary>
/// SELF — the profiler's own diagnostics tab. Surfaces what
/// <see cref="ProfilerSelfHealth"/> measures: install delta, hook count,
/// bytes-per-hook, process working set, severity bucket. Also projects
/// the cost at bigger-modlist scales so the player can see whether the
/// profiler would be untenable in a Calamity / Fargo's / Thorium setup.
///
/// <para>
/// Aimed at the modder / power-user audience but always visible — the
/// glance value is in the chrome's PROFILER HEALTH line; this tab is the
/// detail view. The implementation is intentionally small for now;
/// future expansion (heap sparkline over time, hook-per-mod histogram)
/// extends the cards in place.
/// </para>
/// </summary>
internal sealed class SelfTab : IOverlayTab
{
    public string Label => "SELF";

    public bool IsAvailable(MetricCollector? collector) => collector != null;

    public void Tick(MetricCollector collector) { }

    public float MeasurePanelHeight(MetricCollector collector)
    {
        return OverlayLayoutCurrent.ChromeHeight + 320f;
    }

    public void HandleClick(float localX, float localY, MetricCollector collector) { }
    public void HandleScroll(int delta, MetricCollector collector) { }

    public void Draw(SpriteBatch sb, Rectangle area, MetricCollector collector)
    {
        int contentLeft = area.X + (int)OverlayLayoutCurrent.PanelPaddingX;
        int contentRight = area.Right - (int)OverlayLayoutCurrent.PanelPaddingX;
        int contentWidth = contentRight - contentLeft;
        int y = area.Y + (int)OverlayLayoutCurrent.ChromeHeight;

        ProfilerSelfHealth h = collector.SelfHealth;

        // ---- Card 1: install footprint ----
        Rectangle card1 = new Rectangle(contentLeft, y, contentWidth, 90);
        Rectangle body1 = ProfilerCard.Draw(sb, card1, "INSTALL FOOTPRINT", h.IsInstalled ? "captured" : "pending");
        DrawInstallFootprint(sb, body1, h);

        y += 96;

        // ---- Card 2: process context ----
        Rectangle card2 = new Rectangle(contentLeft, y, contentWidth, 78);
        Rectangle body2 = ProfilerCard.Draw(sb, card2, "PROCESS CONTEXT", null);
        DrawProcessContext(sb, body2, h);

        y += 84;

        // ---- Card 3: scale projection ----
        Rectangle card3 = new Rectangle(contentLeft, y, contentWidth, 110);
        Rectangle body3 = ProfilerCard.Draw(sb, card3, "PROJECTION  ·  AT LARGER MODLIST SIZES", null);
        DrawScaleProjection(sb, body3, h);
    }

    private static void DrawInstallFootprint(SpriteBatch sb, Rectangle body, ProfilerSelfHealth h)
    {
        float rowScale = OverlayLayoutCurrent.TextScaleRow;
        float bodyScale = OverlayLayoutCurrent.TextScaleBody;

        if (!h.IsInstalled)
        {
            OverlayDraw.Text(sb, "self-health measurement pending",
                new Vector2(body.X + 8, body.Y + 12),
                ProfilerTheme.TextMuted, rowScale);
            return;
        }

        double mb = h.InstallDeltaBytes / (1024d * 1024d);
        double kbPerHook = h.BytesPerHook / 1024d;
        Color sevColor = h.Severity switch
        {
            SelfHealthSeverity.Severe     => ProfilerTheme.Danger,
            SelfHealthSeverity.Concerning => ProfilerTheme.Amber,
            _                             => ProfilerTheme.Good,
        };

        StatBlock.Draw(sb,
            new Rectangle(body.X, body.Y, body.Width / 3, body.Height),
            "MANAGED HEAP DELTA", $"{mb:F0} MB", sevColor,
            footer: "captured at install");

        StatBlock.Draw(sb,
            new Rectangle(body.X + body.Width / 3, body.Y, body.Width / 3, body.Height),
            "BYTES PER HOOK", $"{kbPerHook:F1} KB",
            sevColor,
            footer: $"{h.InstalledHookCount:N0} hooks total");

        StatBlock.Draw(sb,
            new Rectangle(body.X + body.Width * 2 / 3, body.Y, body.Width / 3, body.Height),
            "SEVERITY", h.Severity.ToString().ToUpperInvariant(),
            sevColor,
            footer: h.ProcessWorkingSetBytes > 0
                ? $"{h.InstallDeltaFractionOfProcess * 100d:F1}% of game"
                : "refreshing...");
    }

    private static void DrawProcessContext(SpriteBatch sb, Rectangle body, ProfilerSelfHealth h)
    {
        if (h.ProcessWorkingSetBytes <= 0)
        {
            OverlayDraw.Text(sb, "process state refreshing... (wait ~1 s)",
                new Vector2(body.X + 8, body.Y + 8),
                ProfilerTheme.TextMuted,
                OverlayLayoutCurrent.TextScaleRow);
            return;
        }

        double wsMb = h.ProcessWorkingSetBytes / (1024d * 1024d);
        double heapMb = h.ProcessManagedHeapBytes / (1024d * 1024d);
        double managedFraction = h.ManagedFractionOfWorkingSet;

        StatBlock.Draw(sb,
            new Rectangle(body.X, body.Y, body.Width / 3, body.Height),
            "PROCESS WORKING SET", $"{wsMb:F0} MB", ProfilerTheme.Text,
            footer: "whole tModLoader process");

        StatBlock.Draw(sb,
            new Rectangle(body.X + body.Width / 3, body.Y, body.Width / 3, body.Height),
            "MANAGED HEAP TOTAL", $"{heapMb:F0} MB", ProfilerTheme.Text,
            footer: $"{managedFraction * 100d:F0}% of working set");

        StatBlock.Draw(sb,
            new Rectangle(body.X + body.Width * 2 / 3, body.Y, body.Width / 3, body.Height),
            "OUR SHARE", $"{h.InstallDeltaFractionOfProcess * 100d:F1}%",
            h.Severity == SelfHealthSeverity.Severe ? ProfilerTheme.Danger
            : h.Severity == SelfHealthSeverity.Concerning ? ProfilerTheme.Amber
            : ProfilerTheme.Good,
            footer: "install delta ÷ working set");
    }

    private static void DrawScaleProjection(SpriteBatch sb, Rectangle body, ProfilerSelfHealth h)
    {
        if (!h.IsInstalled || h.InstalledHookCount == 0)
        {
            OverlayDraw.Text(sb, "projection unavailable until install completes",
                new Vector2(body.X + 8, body.Y + 8),
                ProfilerTheme.TextMuted,
                OverlayLayoutCurrent.TextScaleBody);
            return;
        }

        // Approximate hook counts for hypothetical modlists. The numbers
        // here are estimates from the design conversation, not measured.
        (string label, int hooks)[] scenarios =
        {
            ("THIS MODLIST",                        h.InstalledHookCount),
            ("CALAMITY (standalone)",               6000),
            ("CALAMITY + FARGO'S + THORIUM + 10",   17000),
            ("KITCHEN-SINK (40 mods)",              30000),
        };

        float rowScale = OverlayLayoutCurrent.TextScaleRow;
        float bodyScale = OverlayLayoutCurrent.TextScaleBody;
        int rowY = body.Y + 6;
        int rowH = (body.Height - 12) / scenarios.Length;

        // Find max projected MB for the heat-bar scale.
        double maxProjMb = 0d;
        for (int i = 0; i < scenarios.Length; i++)
        {
            double mb = (long)scenarios[i].hooks * h.BytesPerHook / (1024d * 1024d);
            if (mb > maxProjMb) maxProjMb = mb;
        }
        if (maxProjMb < 1d) maxProjMb = 1d;

        for (int i = 0; i < scenarios.Length; i++)
        {
            double projMb = (long)scenarios[i].hooks * h.BytesPerHook / (1024d * 1024d);
            // Heat-bar fraction relative to a "500 MB is severe" threshold.
            double sev = System.Math.Min(1d, projMb / 1000d);

            OverlayDraw.Text(sb, scenarios[i].label,
                new Vector2(body.X + 8, rowY + (rowH - 14) / 2),
                ProfilerTheme.TextMuted, bodyScale);

            int barX = body.X + 240;
            int barW = body.Width - 240 - 90;
            HeatBar.Draw(sb,
                new Rectangle(barX, rowY + (rowH - 8) / 2, barW, 8),
                projMb, maxProjMb, sev);

            Color valueColor = sev >= 0.5d ? ProfilerTheme.Danger
                             : sev >= 0.25d ? ProfilerTheme.Amber
                             : ProfilerTheme.Good;
            OverlayDraw.Text(sb, $"{projMb:F0} MB",
                new Vector2(body.Right - 70, rowY + (rowH - 14) / 2),
                valueColor, bodyScale);

            rowY += rowH;
        }
    }
}
