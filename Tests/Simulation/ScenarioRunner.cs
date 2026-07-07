#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;

namespace PerformanceProfiler.Tests.Simulation;

/// <summary>
/// One scripted tick of a synthetic session. Times are what the game would
/// have produced: <see cref="ComputeMs"/> is the update-window cost,
/// <see cref="RealMs"/> the whole-loop period (update + draw + vsync).
/// A positive <see cref="SuspendGapMs"/> injects an alt-tab/OS-sleep style
/// gap BEFORE this tick (focus lost during the gap), exercising the stall
/// classifier's suspend path and the collector's suspend guard.
/// </summary>
internal readonly struct ScriptTick
{
    public readonly double ComputeMs;
    public readonly double RealMs;
    public readonly double GcMs;
    public readonly double SuspendGapMs;

    public ScriptTick(double computeMs, double realMs, double gcMs = 0d, double suspendGapMs = 0d)
    {
        ComputeMs = computeMs;
        RealMs = realMs;
        GcMs = gcMs;
        SuspendGapMs = suspendGapMs;
    }
}

/// <summary>
/// Everything a scenario produces, materialised once so assertions read like
/// the honesty contract ("in slowmo30, avgFps &lt; 40 AND the headroom gate is
/// closed AND sustained slowness fires").
/// </summary>
internal sealed class ScenarioResult
{
    public required RingBuffer<TickFrame> History { get; init; }
    public required Baseline Baseline { get; init; }
    public required StallDetector Stalls { get; init; }
    public required SpikeDetector Spikes { get; init; }
    public required KpiSnapshot Kpi { get; init; }
    public required List<HeatmapBucket> HeatBuckets { get; init; }
    public required double RealFrameEmaMs { get; init; }
    public required double UpdateWindowEmaMs { get; init; }
    public required double ConsecutiveSlowMs { get; init; }
    public required double TimeBelowThresholdMs { get; init; }

    public double RealtimeSpeed => PerformanceProfiler.Data.Stats.RealtimeSpeed.SpeedFrom(RealFrameEmaMs);
    public bool HeadroomGateOpen => RealtimeSpeed >= PerformanceProfiler.Data.Stats.RealtimeSpeed.FullSpeedGate;
    public PerformanceProfiler.Insights.Detectors.SustainedSlownessResult? Slowness =>
        PerformanceProfiler.Insights.Detectors.SustainedSlownessCore.Compute(
            RealtimeSpeed, ConsecutiveSlowMs, TimeBelowThresholdMs,
            PerformanceProfiler.Data.Stats.RealtimeSpeed.DeficitMsPerSecond(RealFrameEmaMs),
            perModCategorySmoothedMs: null, modCount: 0, catCount: 0);
}

/// <summary>
/// Drives the REAL pipeline classes — <see cref="Baseline"/>,
/// <see cref="StallDetector"/>, <see cref="SpikeDetector"/>,
/// <see cref="KpiCalculator.ComputeCore"/>, <see cref="HeatmapFold"/>, the
/// <see cref="RealtimeSpeed"/> folds — with a scripted session, no game, no
/// tModLoader (Ring 1 of the e2e plan). <c>MetricCollector</c> itself cannot
/// link into the runtime-free test project (tModLoader-transitive via
/// ProfilerSelfHealth), so this runner mirrors <c>EndTick</c>'s documented
/// per-tick contract: the suspend guard (a ProcessSuspended/WorldLoad gap
/// falls back to the update-window time), the RealtimeSpeed folds, and the
/// slow-time accumulators. If EndTick's contract changes, this file and the
/// collector must move together — both carry a pointer comment.
/// </summary>
internal static class ScenarioRunner
{
    public static ScenarioResult Run(IReadOnlyList<ScriptTick> script, int historyCapacity = 1800)
    {
        var history = new RingBuffer<TickFrame>(historyCapacity);
        var baseline = new Baseline();
        var stalls = new StallDetector();
        var spikes = new SpikeDetector(modCount: 1, tracksAllocations: false);
        var perTickRing = new PerTickAttributionRing(
            modCount: 1, historyTicks: 64, categorySnapshotTicks: 8, trackAllocations: false);

        long stopwatchFreq = Stopwatch.Frequency;
        long stamp = stopwatchFreq; // arbitrary non-zero origin
        long unixMs = 1_700_000_000_000L;
        long tickIndex = 0;

        double realEma = 0d, updateEma = 0d, consecutiveSlowMs = 0d, timeBelowMs = 0d;

        foreach (var t in script)
        {
            bool suspended = t.SuspendGapMs > 0d;
            double gapMs = suspended ? t.SuspendGapMs : t.RealMs;

            // Advance the clocks by the inter-tick gap, then BeginTick fires.
            stamp += (long)(gapMs / 1000d * stopwatchFreq);
            unixMs += (long)gapMs;
            tickIndex++;

            // The stall detector sees exactly what ProfilerSystem gives it:
            // the begin stamp, the tick index, wall time, the shared baseline,
            // and whether focus was held across the gap.
            stalls.OnBeginTick(stamp, tickIndex, unixMs, baseline, null, hadFocusThisTick: !suspended);

            // EndTick contract mirror: the suspend guard. A gap the classifier
            // called ProcessSuspended/WorldLoad is wall time in which nothing
            // rendered; the recorded real frame falls back to compute time so
            // pauses never read as slow-motion. (MetricCollector.EndTick holds
            // the production copy of this rule.)
            StallCause? gapCause = stalls.LastGapCause;
            bool gapWasNonCompute = gapCause is StallCause.ProcessSuspended or StallCause.WorldLoad;
            double realFrameMs = gapWasNonCompute ? t.ComputeMs : t.RealMs;

            var frame = new TickFrame
            {
                TimestampUnixMs = unixMs,
                TickIndex = tickIndex,
                FrameTimeMs = t.ComputeMs,
                RealFrameTimeMs = realFrameMs,
                GcTimeMs = t.GcMs,
                NpcCount = 0,
                ProjectileCount = 0,
                DustCount = 0,
                ModSamples = null,
            };
            history.Push(in frame);
            baseline.Recompute(history, in frame, tracksAllocations: false, allocBytesThisTick: 0d);
            spikes.OnTick(frame, baseline, perTickRing);

            realEma = RealtimeSpeed.Fold(realEma, realFrameMs);
            updateEma = RealtimeSpeed.Fold(updateEma, t.ComputeMs);
            if (RealtimeSpeed.SpeedFrom(realEma) < RealtimeSpeed.SlowThreshold)
            {
                consecutiveSlowMs += realFrameMs;
                timeBelowMs += realFrameMs;
            }
            else
            {
                consecutiveSlowMs = 0d;
            }
        }

        var kpi = KpiCalculator.ComputeCore(
            history, stalls.Events, spikes.Windows.Count,
            renderFps: 0d,
            realtimeSpeed: RealtimeSpeed.SpeedFrom(realEma),
            timeBelowThresholdMs: timeBelowMs,
            deficitMsPerSecond: RealtimeSpeed.DeficitMsPerSecond(realEma));

        return new ScenarioResult
        {
            History = history,
            Baseline = baseline,
            Stalls = stalls,
            Spikes = spikes,
            Kpi = kpi,
            HeatBuckets = HeatmapFold.Fold(history, unixMs, 60_000L),
            RealFrameEmaMs = realEma,
            UpdateWindowEmaMs = updateEma,
            ConsecutiveSlowMs = consecutiveSlowMs,
            TimeBelowThresholdMs = timeBelowMs,
        };
    }
}
