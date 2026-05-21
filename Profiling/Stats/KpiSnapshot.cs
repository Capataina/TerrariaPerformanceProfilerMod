#nullable enable

namespace PerformanceProfiler.Profiling.Stats;

/// <summary>
/// A single rolled-up summary of the player-facing performance numbers.
/// The four KPI cards at the top of the dashboard read these fields
/// directly; the values used to be computed in JavaScript on every poll
/// against the same data the dashboard had to keep around for the
/// frame chart. Moving the computation to C# keeps a single source of
/// truth and lets the values be persisted to LiteDB for cross-session
/// comparisons.
///
/// <para>
/// All numbers reflect the most-recent rolling window unless noted —
/// for the live session the window is the last 30 s of frame data
/// (the same buffer the frame chart uses). For a closed session the
/// snapshot is computed once at session end from the full
/// <c>TickAggregateArchive</c> row.
/// </para>
/// </summary>
public struct KpiSnapshot
{
    /// <summary>Mean FPS over the rolling window, clamped to 60 (Terraria's tick ceiling).</summary>
    public double AvgFps;

    /// <summary>Worst single-frame duration in ms inside the rolling window.</summary>
    public double WorstFrameMs;

    /// <summary>Median frame ms — robust mid-point. Useful for the chart's threshold line.</summary>
    public double MedianFrameMs;

    /// <summary>Frames in the window that exceeded 50 ms (the perceptual "noticeable hitch" threshold).</summary>
    public int LagSpikeCount;

    /// <summary>Session-cumulative stall count (from <see cref="MetricCollector.Stalls"/>).</summary>
    public int StallCount;

    /// <summary>Session-cumulative spike count (from <see cref="MetricCollector.Spikes"/>).</summary>
    public int SpikeCount;

    /// <summary>How many frames the snapshot was computed over.</summary>
    public int SampleN;

    /// <summary>True when no game data is available yet; consumer renders dashes.</summary>
    public bool IsEmpty;
}
