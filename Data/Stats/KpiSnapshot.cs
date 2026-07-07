#nullable enable

using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Persistence.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Persistence;
using PerformanceProfiler.Persistence.Records;
namespace PerformanceProfiler.Data.Stats;

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
    /// <summary>Mean FPS over the rolling window, from the real inter-frame period (unclamped — drops below 60 during genuine slow-motion).</summary>
    public double AvgFps;

    /// <summary>Rendered frames per second (draw cadence). Diverges below <see cref="AvgFps"/> when frameskip is dropping draws to keep updates real-time. 0 when no draw beat has been observed (e.g. sessions read from the DB).</summary>
    public double RenderFps;

    /// <summary>Real-time speed fraction (0..1]: 1.0 = full 60 UPS, 0.5 = half-speed slow-motion. 0 when unknown (archived sessions).</summary>
    public double RealtimeSpeed;

    /// <summary>Session-cumulative wall ms spent below the slow threshold (90% speed).</summary>
    public double TimeBelowThresholdMs;

    /// <summary>Game-time ms lost per wall second at the current pace; 0 at full speed.</summary>
    public double DeficitMsPerSecond;

    /// <summary>Wall ms of ProcessSuspended + WorldLoad gaps, EXCLUDED from the stall headline numbers (X3: an alt-tab is not a stall).</summary>
    public double PausedMs;

    /// <summary>Count of suspend/world-load gaps excluded from the stall headline.</summary>
    public int PauseCount;

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

    /// <summary>Smallest frame ms in the window — for the "best frame" sub-stat.</summary>
    public double BestFrameMs;

    /// <summary>Cumulative session total: how many ms of slow frames (>50 ms) we've seen.</summary>
    public double TotalLagMs;

    /// <summary>The worst single stall's duration this session, in ms. Lets the dashboard show "biggest stall".</summary>
    public double WorstStallMs;

    /// <summary>Mean stall duration this session, ms. Sub-stat companion to <see cref="StallCount"/>.</summary>
    public double AvgStallMs;

    /// <summary>True when no game data is available yet; consumer renders dashes.</summary>
    public bool IsEmpty;
}
