#nullable enable

using System.Collections.Generic;
using Terraria.ModLoader;

using PerformanceProfiler.Data.Contracts;
using PerformanceProfiler.Profiling;

namespace PerformanceProfiler.Data.Aggregators.Segments;

/// <summary>
/// Timeline T1 — per-recent-segment per-mod ms attribution for the
/// waterfall view.
///
/// <para>
/// Each closed <see cref="Segment"/> already carries parallel
/// <see cref="Segment.ModIds"/> / <see cref="Segment.ModMs"/> arrays sourced
/// from the per-tick attribution ring. We project the top contributors per
/// segment into a stable contract shape so the renderer never has to know
/// the persistence schema.
/// </para>
///
/// <para>
/// <see cref="DataStreamCadence.OnDemand"/>; allocations are bounded by
/// ring capacity (≤ 64 segments) × <see cref="TopN"/>.
/// </para>
/// </summary>
public sealed class SegmentModAttributionStat : IDataStat<SegmentModAttributionSnapshot>
{
    /// <summary>How many top contributors we surface per segment.</summary>
    public const int TopN = 5;

    public string Name => RolloutStreamNames.SegmentModAttribution;
    public DataStreamCadence Cadence => DataStreamCadence.OnDemand;
    public DataStage Stage => DataStage.Stat;

    public void Initialise(SessionContext session) { }
    public void Reset() { }
    public void Dispose() { }

    public SegmentModAttributionSnapshot CurrentSnapshot()
    {
        ProfilerSystem? sys = ModContent.GetInstance<ProfilerSystem>();
        SegmentStore? store = sys?.SegmentStore;
        if (store == null) return SegmentModAttributionSnapshot.Empty;

        IReadOnlyCollection<Segment> recent = store.Recent;
        if (recent.Count == 0)
        {
            return new SegmentModAttributionSnapshot(worldLoaded: sys?.Collector != null,
                System.Array.Empty<SegmentModAttribution>());
        }

        var list = new List<SegmentModAttribution>(recent.Count);
        foreach (Segment seg in recent)
        {
            List<(int ModId, double Ms)> top = seg.TopMods(TopN);
            int n = top.Count;
            var ids = new int[n];
            var ms = new double[n];
            for (int i = 0; i < n; i++)
            {
                ids[i] = top[i].ModId;
                ms[i] = top[i].Ms;
            }
            list.Add(new SegmentModAttribution(
                SegmentStartTick: seg.StartTick,
                Family: (int)seg.Family,
                Key: seg.Key,
                ModIds: ids,
                ModMs: ms));
        }
        return new SegmentModAttributionSnapshot(worldLoaded: sys?.Collector != null, list);
    }

    public object CurrentSnapshotBoxed() => CurrentSnapshot();
}
