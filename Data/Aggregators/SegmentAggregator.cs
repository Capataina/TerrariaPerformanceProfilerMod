#nullable enable

using System.Collections.Generic;
using Terraria.ModLoader;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Data.Aggregators.Segments;

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Data.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Profiling.Persistence;
using PerformanceProfiler.Profiling.Persistence.Records;
namespace PerformanceProfiler.Data.Aggregators;

/// <summary>
/// Snapshot of the segment engine's current view: open segments + the
/// most-recent closed segments. Both lists are live references into the
/// detector / store; callers must not mutate them.
/// </summary>
public readonly struct SegmentsSnapshot
{
    public readonly bool WorldLoaded;
    public readonly IReadOnlyCollection<OpenSegment>? Open;
    public readonly IReadOnlyCollection<Segment>? Recent;

    public SegmentsSnapshot(bool worldLoaded,
        IReadOnlyCollection<OpenSegment>? open,
        IReadOnlyCollection<Segment>? recent)
    {
        WorldLoaded = worldLoaded;
        Open = open;
        Recent = recent;
    }

    public static readonly SegmentsSnapshot Empty
        = new SegmentsSnapshot(false, null, null);
}

/// <summary>
/// Pipeline-facing adapter over the existing <see cref="SegmentDetector"/>
/// + <see cref="SegmentStore"/>. The detector + store keep their current
/// hot-path ownership; this aggregator exposes their state through the
/// registry so consumers don't reach into <c>ProfilerSystem</c>'s named
/// properties directly.
///
/// <para>
/// Plan §7 of <c>context/plans/unified-data-pipeline.md</c> identifies the
/// detector as fusing a per-tick edge-sweep collector and an aggregator;
/// the structural split is deferred to a follow-up pass to avoid risking
/// the side-channel spike/stall/death contract. This adapter is the
/// pipeline-facing surface in the interim — consumers go through it,
/// the detector keeps its internal shape.
/// </para>
/// </summary>
public sealed class SegmentAggregator : IDataAggregator<SegmentsSnapshot>
{
    public const string StreamName = "segments";

    public string Name => StreamName;
    public DataStreamCadence Cadence => DataStreamCadence.OnDemand;
    public DataStage Stage => DataStage.Aggregator;

    public void Initialise(SessionContext session) { }
    public void Reset() { }
    public void Dispose() { }

    public SegmentsSnapshot CurrentSnapshot()
    {
        ProfilerSystem? sys = ModContent.GetInstance<ProfilerSystem>();
        if (sys == null || (sys.Segments == null && sys.SegmentStore == null))
        {
            return SegmentsSnapshot.Empty;
        }
        return new SegmentsSnapshot(
            worldLoaded: sys.Collector != null,
            open: sys.Segments?.OpenSegments,
            recent: sys.SegmentStore?.Recent);
    }

    public object CurrentSnapshotBoxed() => CurrentSnapshot();
}
