#nullable enable

using System.Collections.Generic;
using Terraria.ModLoader;

using PerformanceProfiler.Data.Contracts;
using PerformanceProfiler.Profiling;

namespace PerformanceProfiler.Data.Aggregators.Segments;

/// <summary>
/// Timeline T2 — per-recent-segment lifetime baseline + this-segment delta.
///
/// <para>
/// For every closed segment currently held in <see cref="SegmentStore.Recent"/>
/// emit a <see cref="SegmentLifetimeEntry"/> that pairs the segment's own
/// average frame-ms-per-tick against the lifetime mean for the same
/// (family, key) pair. <see cref="SegmentStore"/> already maintains the
/// lifetime running-mean (seeded from the DB at world-load, folded on
/// each close); we just read its surface — no extra DB pass.
/// </para>
///
/// <para>
/// <see cref="DataStreamCadence.OnDemand"/>; the dashboard polls this at
/// ~2 s intervals. Allocations are bounded by ring capacity (≤ 64 entries).
/// </para>
/// </summary>
public sealed class SegmentLifetimeStat : IDataStat<SegmentLifetimeSnapshot>
{
    public string Name => RolloutStreamNames.SegmentLifetime;
    public DataStreamCadence Cadence => DataStreamCadence.OnDemand;
    public DataStage Stage => DataStage.Stat;

    public void Initialise(SessionContext session) { }
    public void Reset() { }
    public void Dispose() { }

    public SegmentLifetimeSnapshot CurrentSnapshot()
    {
        ProfilerSystem? sys = ModContent.GetInstance<ProfilerSystem>();
        SegmentStore? store = sys?.SegmentStore;
        if (store == null) return SegmentLifetimeSnapshot.Empty;

        IReadOnlyCollection<Segment> recent = store.Recent;
        if (recent.Count == 0)
        {
            return new SegmentLifetimeSnapshot(worldLoaded: sys?.Collector != null,
                System.Array.Empty<SegmentLifetimeEntry>());
        }

        var list = new List<SegmentLifetimeEntry>(recent.Count);
        foreach (Segment seg in recent)
        {
            double lifetimeAvg = store.LifetimeAvgMsPerTick(seg.Family, seg.Key);
            int samples = store.LifetimeSampleCount(seg.Family, seg.Key);
            double thisAvg = seg.AvgFrameMs;
            double delta = lifetimeAvg > 0d ? (thisAvg - lifetimeAvg) / lifetimeAvg : 0d;
            list.Add(new SegmentLifetimeEntry(
                SegmentStartTick: seg.StartTick,
                Family: (int)seg.Family,
                Key: seg.Key,
                Name: seg.Name,
                LifetimeAvgMs: lifetimeAvg,
                LifetimeSampleCount: samples,
                ThisSegmentAvgMs: thisAvg,
                DeltaFraction: delta));
        }
        return new SegmentLifetimeSnapshot(worldLoaded: sys?.Collector != null, list);
    }

    public object CurrentSnapshotBoxed() => CurrentSnapshot();
}
