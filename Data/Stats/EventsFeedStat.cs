#nullable enable

using System.Collections.Generic;
using Terraria.ModLoader;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Persistence.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Persistence;
using PerformanceProfiler.Persistence.Records;
namespace PerformanceProfiler.Data.Stats;

/// <summary>
/// Snapshot type for <see cref="EventsFeedStat"/>: the pre-merged event
/// list (segments + spikes + stalls), most-recent-first, capped at the
/// requested size.
/// </summary>
public readonly struct EventsFeedSnapshot
{
    public readonly IReadOnlyList<FeedEvent> Events;
    public readonly bool WorldLoaded;

    public EventsFeedSnapshot(IReadOnlyList<FeedEvent> events, bool worldLoaded)
    {
        Events = events;
        WorldLoaded = worldLoaded;
    }

    public static readonly EventsFeedSnapshot Empty
        = new EventsFeedSnapshot(System.Array.Empty<FeedEvent>(), worldLoaded: false);
}

/// <summary>
/// Pre-merged "recent events" feed for the dashboard. Wraps the existing
/// pure-logic <see cref="EventsFeed.Build"/>. Default cap of 16 matches
/// what the dashboard requested previously via its inline merge.
///
/// <para>
/// Registered as <c>"events"</c>. On-demand cadence; the merge runs once
/// per dashboard poll (~1.5 Hz), well below the per-tick budget.
/// </para>
/// </summary>
public sealed class EventsFeedStat : IDataStat<EventsFeedSnapshot>
{
    public const string StreamName = "events";
    public const int DefaultMax = 16;

    public string Name => StreamName;
    public DataStreamCadence Cadence => DataStreamCadence.OnDemand;
    public DataStage Stage => DataStage.Stat;

    public void Initialise(SessionContext session) { }
    public void Reset() { }
    public void Dispose() { }

    public EventsFeedSnapshot CurrentSnapshot()
    {
        ProfilerSystem? sys = ModContent.GetInstance<ProfilerSystem>();
        MetricCollector? c = sys?.Collector;
        SegmentStore? store = sys?.SegmentStore;
        if (c == null && store == null) return EventsFeedSnapshot.Empty;

        var list = EventsFeed.Build(c, store, max: DefaultMax);
        return new EventsFeedSnapshot(list, worldLoaded: c != null);
    }

    public object CurrentSnapshotBoxed() => CurrentSnapshot();
}
