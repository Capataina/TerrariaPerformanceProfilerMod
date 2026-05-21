#nullable enable

using System;
using System.Collections.Generic;
using Terraria.ModLoader;

using PerformanceProfiler.Data.Contracts;
using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Profiling;

namespace PerformanceProfiler.Data.Stats;

/// <summary>
/// L3 — GC pressure narrative.
///
/// <para>
/// Reads the GC-cause stalls out of <see cref="MetricCollector.Stalls"/> and
/// rolls them up into per-minute generation rates, paused-time, and freed-MB
/// totals. Pairs that with a 1Hz-equivalent rolling heap-MB series sampled at
/// snapshot time.
/// </para>
///
/// <para>
/// <b>Honest limitation — heap sampling cadence.</b> The L3 spec calls for a
/// 1Hz heap sample. To stay off the per-tick hot path (Invariant 2), this
/// stat samples <see cref="GC.GetTotalMemory(bool)"/> once per
/// <see cref="CurrentSnapshot"/> call and pushes the value into an internal
/// ring. The series therefore reflects "heap at each snapshot poll" rather
/// than a true 1Hz uniform timeline; with the dashboard polling at ~3 s the
/// effective cadence is ~0.33 Hz. A future tightening would wire a PerTick
/// callback gated to <c>tickIndex % 60 == 0</c>.
/// </para>
/// </summary>
public sealed class GcPressureStat : IDataStat<GcPressureSnapshot>
{
    /// <summary>Number of heap samples kept in the rolling ring.</summary>
    public const int HeapSeriesCapacity = 60;

    public string Name => RolloutStreamNames.GcPressure;
    public DataStreamCadence Cadence => DataStreamCadence.OnDemand;
    public DataStage Stage => DataStage.Stat;

    private readonly List<double> _heapMb = new(HeapSeriesCapacity);
    private long _sessionStartUnixMs;

    public void Initialise(SessionContext session)
    {
        _sessionStartUnixMs = Time.UnixMsNow();
        _heapMb.Clear();
    }

    public void Reset()
    {
        _heapMb.Clear();
        _sessionStartUnixMs = 0L;
    }

    public void Dispose() { }

    public GcPressureSnapshot CurrentSnapshot()
    {
        MetricCollector? c = ModContent.GetInstance<ProfilerSystem>()?.Collector;
        if (c == null) return GcPressureSnapshot.Empty;

        // Sample current heap and push into rolling ring. Allocation-bounded:
        // when the ring is full we drop the oldest entry.
        double curMb = 0d;
        try { curMb = GC.GetTotalMemory(forceFullCollection: false) / (1024d * 1024d); }
        catch { /* defensive; some sandboxed runtimes throw */ }
        _heapMb.Add(curMb);
        if (_heapMb.Count > HeapSeriesCapacity) _heapMb.RemoveAt(0);

        IReadOnlyList<StallEvent> stalls = c.Stalls;
        long g0Total = 0, g1Total = 0, g2Total = 0;
        long pausedMs = 0;
        long freedBytes = 0;

        for (int i = 0; i < stalls.Count; i++)
        {
            StallEvent s = stalls[i];
            if (s.Cause != StallCause.MajorGc && s.Cause != StallCause.MinorGc) continue;

            g0Total += s.Gen0Collections;
            g1Total += s.Gen1Collections;
            g2Total += s.Gen2Collections;
            pausedMs += (long)s.TickPeriodMs;

            long delta = s.HeapSizeBeforeBytes - s.HeapSizeAfterBytes;
            if (delta < 0) delta = -delta;
            freedBytes += delta;
        }

        long nowUnixMs = Time.UnixMsNow();
        double sessionMinutes = Math.Max((nowUnixMs - _sessionStartUnixMs) / 60_000d, 1d / 60d);

        double g0PerMin = g0Total / sessionMinutes;
        double g1PerMin = g1Total / sessionMinutes;
        double g2PerMin = g2Total / sessionMinutes;
        long freedMb = freedBytes / (1024L * 1024L);

        // Hand back an immutable copy of the heap series so the consumer
        // never sees the live list mutate mid-render.
        double[] heapCopy = _heapMb.ToArray();

        return new GcPressureSnapshot(
            worldLoaded: true,
            g0: g0PerMin, g1: g1PerMin, g2: g2PerMin,
            g0t: g0Total, g1t: g1Total, g2t: g2Total,
            paused: pausedMs, freed: freedMb,
            series: heapCopy, cur: curMb);
    }

    public object CurrentSnapshotBoxed() => CurrentSnapshot();
}
