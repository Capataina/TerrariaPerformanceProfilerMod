#nullable enable

using System.Collections.Generic;
using PerformanceProfiler.Insights.Detectors;

using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Data.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling.Persistence;
using PerformanceProfiler.Profiling.Persistence.Records;
namespace PerformanceProfiler.Insights;

/// <summary>
/// Owns the detector roster and the live <see cref="InsightStore"/>. The
/// host (typically the InsightsTab's <c>Tick</c> path or a future
/// 1 Hz scheduler) calls <see cref="Evaluate"/> to drive a pass; the engine
/// runs every available detector, gathers their emitted records, and
/// submits them to the store. Gated detectors register themselves but
/// short-circuit in <c>Evaluate</c>; the engine surfaces their gate strings
/// via <see cref="GatedDetectors"/>.
///
/// <para>
/// One <see cref="InsightsEngine"/> exists per session. It is wired through
/// <see cref="ProfilerSystem"/> (or accessed from the InsightsTab as a
/// singleton-of-process) so the overlay can read live records without
/// dragging instrumentation hooks through the UI layer.
/// </para>
/// </summary>
public sealed class InsightsEngine
{
    /// <summary>
    /// Process-wide engine reference, lazily created on first read. Owned by
    /// whichever subsystem touches it first (typically the InsightsTab); both
    /// the player overlay and the agent-readable session JSON read through
    /// this so the two surfaces never see different record sets. Cleared by
    /// <see cref="ProfilerSystem"/> at world unload to drop session state.
    /// </summary>
    private static InsightsEngine? _shared;
    public static InsightsEngine? Shared
    {
        get => System.Threading.Volatile.Read(ref _shared);
        set => System.Threading.Volatile.Write(ref _shared, value);
    }

    /// <summary>
    /// Returns the shared engine, lazily allocating it if needed. Uses
    /// CompareExchange so two concurrent first-callers see the same
    /// instance — Shared was previously a plain `??= ` which could
    /// race two `new InsightsEngine()` allocations and have one of
    /// them silently win the field write, orphaning the other.
    /// </summary>
    public static InsightsEngine GetOrCreateShared()
    {
        InsightsEngine? cur = System.Threading.Volatile.Read(ref _shared);
        if (cur != null) return cur;
        var candidate = new InsightsEngine();
        var prior = System.Threading.Interlocked.CompareExchange(ref _shared, candidate, null);
        return prior ?? candidate;
    }

    private readonly List<IInsightDetector> _detectors;
    private readonly InsightStore _store;
    private readonly List<InsightRecord> _scratch = new List<InsightRecord>(16);

    // Gated-pattern map is computed once at construction (detector roster is
    // static). The previous shape rebuilt it per call from inside Draw, which
    // ran 60×/sec and allocated a fresh Dictionary + List every frame.
    private readonly Dictionary<string, List<string>> _gatedMap;
    private readonly string _gatedLabel;

    /// <summary>
    /// Constructs the engine with the default detector roster: every pattern
    /// in <see cref="PatternKey"/> represented, in-scope ones live and
    /// gated ones registered as stubs.
    /// </summary>
    public InsightsEngine()
    {
        _store = new InsightStore();
        _detectors = new List<IInsightDetector>
        {
            // In-scope detectors (data is available today).
            new HotHookDominanceDetector(),
            new AllocationBurstDetector(),
            new FreeRemovalCandidateDetector(),
            new PeakContributorToSpikeDetector(),

            // Gated detectors (data not yet exposed; emit nothing today).
            new ContextCorrelatedSpikeDetector(),
            new ContextConditionalCostDetector(),
            new SustainedCostShiftDetector(),
            new NewContributorDetector(),
            new GcPauseCulpritDetector(),
            new HookFrequencyTailDetector(),

            // v0.5 interaction-tracking detectors. The first two query the
            // DB streams added in v0.5 (loadoutSnapshots, buffEvents,
            // tickAggregatesWarm) and are available as soon as a world has
            // been loaded; the third declares itself gated on
            // cross-session loadout aggregation.
            new LoadoutCorrelatedCostDetector(),
            new EventConditionalCostDetector(),
            new LoadoutCombinationCostDetector(),

            // v0.7 segment-driven detectors. They consume SegmentStore via
            // the ProfilerSystem singleton, so they're available the moment
            // a world is loaded (the same gate the rest of the detectors use).
            new SegmentOutlierDetector(),
            new SegmentTopModDetector(),
            new SegmentDeathCorrelationDetector(),
        };

        _gatedMap = BuildGatedMap(_detectors);
        _gatedLabel = BuildGatedLabel(_gatedMap);
    }

    private static Dictionary<string, List<string>> BuildGatedMap(List<IInsightDetector> detectors)
    {
        var result = new Dictionary<string, List<string>>();
        for (int i = 0; i < detectors.Count; i++)
        {
            IInsightDetector det = detectors[i];
            if (!det.IsGated) continue;
            string gate = det.GatedOn ?? "unknown";
            if (!result.TryGetValue(gate, out List<string>? list))
            {
                list = new List<string>(2);
                result[gate] = list;
            }
            list.Add(det.Pattern.ToString());
        }
        return result;
    }

    private static string BuildGatedLabel(Dictionary<string, List<string>> gated)
    {
        if (gated.Count == 0) return string.Empty;
        var sb = new System.Text.StringBuilder("gated detectors waiting on: ");
        bool first = true;
        foreach (string key in gated.Keys)
        {
            if (!first) sb.Append(", ");
            sb.Append(key);
            first = false;
        }
        return sb.ToString();
    }

    /// <summary>The live + history store. Tabs and exporters read from here.</summary>
    public InsightStore Store => _store;

    /// <summary>The full detector roster, including gated stubs. Used by the JSONL exporter.</summary>
    public IReadOnlyList<IInsightDetector> Detectors => _detectors;

    /// <summary>
    /// Runs one detection pass against <paramref name="collector"/>'s current
    /// state. Caller decides cadence; the InsightsTab calls this every frame
    /// while the tab is active, which is well below the per-detector budget
    /// because each detector reads already-smoothed accessors.
    /// </summary>
    public void Evaluate(MetricCollector collector, long nowTick, long sessionLengthTicks)
    {
        for (int i = 0; i < _detectors.Count; i++)
        {
            IInsightDetector det = _detectors[i];
            if (det.IsGated || !det.IsAvailable(collector)) continue;
            _scratch.Clear();
            det.Evaluate(collector, nowTick, sessionLengthTicks, _scratch);
            for (int j = 0; j < _scratch.Count; j++)
            {
                _store.Submit(_scratch[j], nowTick);
            }
        }
        _store.Tick(nowTick);
    }

    /// <summary>
    /// Returns the set of gated detector names with their gate reasons.
    /// Cached at construction; the detector roster is static so the map
    /// never changes. Consumed by the JSONL exporter for the
    /// <c>insights.gated</c> field.
    /// </summary>
    public IReadOnlyDictionary<string, List<string>> GatedPatterns() => _gatedMap;

    /// <summary>
    /// Pre-formatted "gated detectors waiting on: events, litedb" label.
    /// Cached at construction so the InsightsTab's <c>Draw</c> can render
    /// it without invoking Linq or string.Join per frame.
    /// </summary>
    public string GatedLabel => _gatedLabel;
}
