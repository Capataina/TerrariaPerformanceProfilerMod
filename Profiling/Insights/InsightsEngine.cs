#nullable enable

using System.Collections.Generic;
using PerformanceProfiler.Profiling.Insights.Detectors;

namespace PerformanceProfiler.Profiling.Insights;

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
    private readonly List<IInsightDetector> _detectors;
    private readonly InsightStore _store;
    private readonly List<InsightRecord> _scratch = new List<InsightRecord>(16);

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
        };
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
    /// Consumed by the JSONL exporter for the <c>insights.gated</c> field.
    /// </summary>
    public IReadOnlyDictionary<string, List<string>> GatedPatterns()
    {
        Dictionary<string, List<string>> result = new Dictionary<string, List<string>>();
        for (int i = 0; i < _detectors.Count; i++)
        {
            IInsightDetector det = _detectors[i];
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
}
