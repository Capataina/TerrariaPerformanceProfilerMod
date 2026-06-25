// ARCHIVED v0.9.0: in-game overlay replaced by browser dashboard.
// Sources retained for possible Steam-Deck variant or future revival;
// remove the #if false / #endif guards below to restore compilation.
#if false
#nullable enable

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.UI.Overlay.Components;

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Persistence.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Persistence;
using PerformanceProfiler.Persistence.Records;
namespace PerformanceProfiler.UI.Overlay.Tabs;

/// <summary>
/// SELF — the profiler's own diagnostics tab. Surfaces what
/// <see cref="ProfilerSelfHealth"/> measures: install delta, hook count,
/// bytes-per-hook, process working set, severity bucket.
///
/// <para>
/// Aimed at the modder / power-user audience but always visible — the
/// glance value is in the chrome's PROFILER HEALTH line; this tab is the
/// detail view. Two cards: install footprint, process context. There was
/// previously a third "projection" card extrapolating cost at hypothetical
/// modlist sizes (Calamity-standalone, kitchen-sink, etc.) — removed in
/// v0.4 hygiene pass: hardcoded hook counts and a "500 MB severe" curve
/// dressed up an estimate as a measurement. Players care about their own
/// modlist's actual numbers, not hypotheticals. If we want scaling
/// guidance later, we'll add a per-mod hook-count histogram instead.
/// </para>
/// </summary>
internal sealed class SelfTab : IOverlayTab
{
    public string Label => "SELF";

    public bool IsAvailable(MetricCollector? collector) => collector != null;

    public void Tick(MetricCollector collector) { }

    public float MeasurePanelHeight(MetricCollector collector)
    {
        // Two cards × ~90 px each + chrome.
        return OverlayLayoutCurrent.ChromeHeight + 200f;
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
    }

    private static void DrawInstallFootprint(SpriteBatch sb, Rectangle body, ProfilerSelfHealth h)
    {
        float rowScale = OverlayLayoutCurrent.TextScaleRow;

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
                ? $"{h.InstallDeltaBytes / (1024d * 1024d):F0} MB / {h.ProcessWorkingSetBytes / (1024d * 1024d):F0} MB"
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
        double instMb = h.InstallDeltaBytes / (1024d * 1024d);

        StatBlock.Draw(sb,
            new Rectangle(body.X, body.Y, body.Width / 3, body.Height),
            "PROCESS WORKING SET", $"{wsMb:F0} MB", ProfilerTheme.Text,
            footer: "whole tModLoader process");

        StatBlock.Draw(sb,
            new Rectangle(body.X + body.Width / 3, body.Y, body.Width / 3, body.Height),
            "MANAGED HEAP TOTAL", $"{heapMb:F0} MB", ProfilerTheme.Text,
            footer: $"{managedFraction * 100d:F0}% of working set");

        // "Our share" is now the honest ratio rendering (install MB / process
        // MB), not the misleading "% of game" that drifts past 100%.
        StatBlock.Draw(sb,
            new Rectangle(body.X + body.Width * 2 / 3, body.Y, body.Width / 3, body.Height),
            "OUR SHARE", $"{instMb:F0} / {wsMb:F0} MB",
            h.Severity == SelfHealthSeverity.Severe ? ProfilerTheme.Danger
            : h.Severity == SelfHealthSeverity.Concerning ? ProfilerTheme.Amber
            : ProfilerTheme.Good,
            footer: "install delta of process");
    }
}

#endif
