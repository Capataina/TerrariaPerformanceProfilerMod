#nullable enable

using System;
using System.Collections.Generic;
using Terraria.ModLoader;

using PerformanceProfiler.Data.Contracts;
using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Profiling;

namespace PerformanceProfiler.Data.Stats;

/// <summary>
/// L4 — per-segment lag density. For each recent closed segment, counts how
/// many spikes + stalls landed inside the segment's tick window and converts
/// to an events-per-minute rate. Each entry's <c>VsBaseline</c> divides the
/// segment rate by the session baseline so the UI can flag outliers without
/// re-doing the math.
///
/// <para>
/// The baseline is computed from session totals: <c>(spikes + stalls) /
/// sessionMinutes</c>. When fewer than ~60 s of session have elapsed the
/// baseline is clamped to the elapsed minutes so a young session doesn't
/// produce divide-by-tiny-number ratios.
/// </para>
/// </summary>
public sealed class PerSegmentLagDensityStat : IDataStat<SegmentLagDensitySnapshot>
{
    /// <summary>Hard cap on entries returned per snapshot.</summary>
    public const int MaxEntries = 64;

    public string Name => RolloutStreamNames.SegmentLagDensity;
    public DataStreamCadence Cadence => DataStreamCadence.OnDemand;
    public DataStage Stage => DataStage.Stat;

    private long _sessionStartUnixMs;

    public void Initialise(SessionContext session)
    {
        _sessionStartUnixMs = Time.UnixMsNow();
    }

    public void Reset() { _sessionStartUnixMs = 0L; }
    public void Dispose() { }

    public SegmentLagDensitySnapshot CurrentSnapshot()
    {
        ProfilerSystem? sys = ModContent.GetInstance<ProfilerSystem>();
        MetricCollector? c = sys?.Collector;
        SegmentStore? store = sys?.SegmentStore;
        if (c == null || store == null) return SegmentLagDensitySnapshot.Empty;

        IReadOnlyList<SpikeWindow> spikes = c.Spikes;
        IReadOnlyList<StallEvent> stalls = c.Stalls;

        long nowUnixMs = Time.UnixMsNow();
        double sessionMinutes = Math.Max((nowUnixMs - _sessionStartUnixMs) / 60_000d, 1d / 60d);
        double baselinePerMin = (spikes.Count + stalls.Count) / sessionMinutes;

        List<SegmentLagDensityEntry> entries = new(Math.Min(store.Recent.Count, MaxEntries));
        int taken = 0;
        foreach (Segment seg in store.Recent)
        {
            if (taken >= MaxEntries) break;
            taken++;

            long startTick = seg.StartTick;
            long endTick = seg.EndTick;

            int spikeCount = 0;
            for (int i = 0; i < spikes.Count; i++)
            {
                long t = spikes[i].WorstTick;
                if (t >= startTick && t <= endTick) spikeCount++;
            }
            int stallCount = 0;
            for (int i = 0; i < stalls.Count; i++)
            {
                long t = stalls[i].StartTickIndex;
                if (t >= startTick && t <= endTick) stallCount++;
            }

            // events / minutes-in-segment, where minutes-in-segment is
            // derived from tick count at 60 tps. Guard against zero-tick
            // segments (degenerate but possible on rapid open/close edges).
            double segMinutes = seg.Ticks > 0 ? seg.Ticks / 60d / 60d : 1d / 60d;
            int totalEvents = spikeCount + stallCount;
            double eventsPerMin = totalEvents / segMinutes;
            double vsBaseline = baselinePerMin > 0d ? eventsPerMin / baselinePerMin : 0d;

            entries.Add(new SegmentLagDensityEntry(
                SegmentStartTick: seg.StartTick,
                Family: (int)seg.Family,
                Name: seg.Name,
                DurationMs: seg.DurationMs,
                SpikeCount: spikeCount,
                StallCount: stallCount,
                EventsPerMin: eventsPerMin,
                VsBaseline: vsBaseline));
        }

        return new SegmentLagDensitySnapshot(worldLoaded: true, baselinePerMin, entries);
    }

    public object CurrentSnapshotBoxed() => CurrentSnapshot();
}
