#nullable enable

using System;
using System.Collections.Generic;
using PerformanceProfiler.Profiling;

namespace PerformanceProfiler.UI.Overlay;

/// <summary>Which metric the value column shows: CPU ms, allocation bytes, or both.</summary>
internal enum MetricView { Cpu = 0, Mem = 1, Both = 2 }

/// <summary>
/// Cross-tab state for the overlay.
///
/// <para>
/// Tabs are singletons (one per process) and share these flags so the
/// LIVE/PAUSED, NOW/30S AVG, and CPU/MEM/BOTH choices apply uniformly across
/// every view. The chrome (<see cref="OverlayPanel"/>) is the only writer
/// for the pause / snapshot machinery; tabs read but don't write.
/// </para>
///
/// <para>
/// State here is static so it survives F9 toggles (close and reopen the
/// overlay → returns to the same tab with the same metric/pause state).
/// </para>
/// </summary>
internal static class OverlayState
{
    /// <summary>Index into <see cref="TabRegistry.Tabs"/> of the active tab.</summary>
    public static int ActiveTabIndex { get; set; } = 0;

    /// <summary>CPU/MEM/BOTH choice for the value column in tree/list views.</summary>
    public static MetricView CurrentMetric { get; set; } = MetricView.Cpu;

    /// <summary>True between a PAUSE click and an UNPAUSE click; tabs read frozen arrays during this window.</summary>
    public static bool Paused { get; set; }

    /// <summary>True if the user selected 30S AVG over NOW.</summary>
    public static bool ShowAverage { get; set; }

    // Frozen snapshot arrays — written by the chrome on pause, read by tabs.
    // Each is the collector's smoothed view at the moment PAUSE was hit, so
    // the tab keeps showing that exact slice instead of moving past it.
    private static double[] _frozenCategoryMs    = Array.Empty<double>();
    private static double[] _frozenHookMs        = Array.Empty<double>();
    private static double[] _frozenCategoryBytes = Array.Empty<double>();
    private static double[] _frozenHookBytes     = Array.Empty<double>();

    /// <summary>
    /// Returns the per-mod-category ms view the active tab should bind to:
    /// frozen if paused, average if NOW/30S AVG is on AVG, otherwise the
    /// smoothed live view.
    /// </summary>
    public static IReadOnlyList<double> SelectedCategoryMs(MetricCollector collector) =>
        Paused ? _frozenCategoryMs
               : ShowAverage ? collector.PerModCategoryAverageMs
                             : collector.PerModCategoryMs;

    /// <summary>Per-hook ms view, matching <see cref="SelectedCategoryMs"/>'s semantics.</summary>
    public static IReadOnlyList<double> SelectedHookMs(MetricCollector collector) =>
        Paused ? _frozenHookMs
               : ShowAverage ? collector.PerHookAverageMs
                             : collector.PerHookMs;

    /// <summary>
    /// Returns the per-mod-category bytes view, or null if allocation tracking
    /// is off. Same pause/avg semantics as <see cref="SelectedCategoryMs"/>.
    /// </summary>
    public static IReadOnlyList<double>? SelectedCategoryBytes(MetricCollector collector)
    {
        if (!collector.TracksAllocations) return null;
        if (Paused) return _frozenCategoryBytes;
        return ShowAverage ? collector.PerModCategoryAverageBytes : collector.PerModCategoryBytes;
    }

    /// <summary>Per-hook bytes view, or null if tracking is off.</summary>
    public static IReadOnlyList<double>? SelectedHookBytes(MetricCollector collector)
    {
        if (!collector.TracksAllocations) return null;
        if (Paused) return _frozenHookBytes;
        return ShowAverage ? collector.PerHookAverageBytes : collector.PerHookBytes;
    }

    /// <summary>
    /// Copies the collector's smoothed views into the frozen-snapshot arrays.
    /// Called by the chrome on PAUSE (and again if the user flips NOW/AVG
    /// while paused, so the snapshot follows the toggle).
    /// </summary>
    public static void CaptureSnapshot(MetricCollector collector)
    {
        IReadOnlyList<double> catSrc  = ShowAverage ? collector.PerModCategoryAverageMs : collector.PerModCategoryMs;
        IReadOnlyList<double> hookSrc = ShowAverage ? collector.PerHookAverageMs        : collector.PerHookMs;

        if (_frozenCategoryMs.Length != catSrc.Count)  _frozenCategoryMs = new double[catSrc.Count];
        if (_frozenHookMs.Length != hookSrc.Count)     _frozenHookMs     = new double[hookSrc.Count];

        for (int i = 0; i < catSrc.Count;  i++) _frozenCategoryMs[i] = catSrc[i];
        for (int i = 0; i < hookSrc.Count; i++) _frozenHookMs[i]     = hookSrc[i];

        if (collector.TracksAllocations)
        {
            IReadOnlyList<double>? catBytesSrc  = ShowAverage ? collector.PerModCategoryAverageBytes : collector.PerModCategoryBytes;
            IReadOnlyList<double>? hookBytesSrc = ShowAverage ? collector.PerHookAverageBytes        : collector.PerHookBytes;
            if (catBytesSrc != null && hookBytesSrc != null)
            {
                if (_frozenCategoryBytes.Length != catBytesSrc.Count) _frozenCategoryBytes = new double[catBytesSrc.Count];
                if (_frozenHookBytes.Length != hookBytesSrc.Count)    _frozenHookBytes     = new double[hookBytesSrc.Count];
                for (int i = 0; i < catBytesSrc.Count;  i++) _frozenCategoryBytes[i] = catBytesSrc[i];
                for (int i = 0; i < hookBytesSrc.Count; i++) _frozenHookBytes[i]     = hookBytesSrc[i];
            }
        }
    }
}
