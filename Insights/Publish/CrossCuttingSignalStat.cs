#nullable enable

using PerformanceProfiler.Data;

using System.Collections.Generic;
using PerformanceProfiler.Data.Contracts;
using PerformanceProfiler.Insights;
using PerformanceProfiler.Insights.Shared;
using PerformanceProfiler.Profiling;

namespace PerformanceProfiler.Insights.Publish;

/// <summary>
/// I5 — cross-cutting signal aggregation. Walks
/// <see cref="InsightsEngine.Shared"/>'s live store, groups records by
/// <see cref="PatternKey"/>, and ranks mods by how often they appear in
/// each pattern's records.
///
/// <para>
/// The <c>SignalClass</c> string emitted on each group is the
/// <c>PatternKey</c> name (e.g. "AllocationBurst", "SegmentOutlier") —
/// stable across schema bumps because the enum values themselves are
/// stable.
/// </para>
/// </summary>
public sealed class CrossCuttingSignalStat : IDataStat<CrossCuttingSnapshot>
{
    public const string StreamName = RolloutStreamNames.CrossCutting;
    private const int MaxLeadersPerGroup = 5;

    public string Name => StreamName;
    public DataStreamCadence Cadence => DataStreamCadence.OnDemand;
    public DataStage Stage => DataStage.Stat;

    public void Initialise(SessionContext session) { }
    public void Reset() { }
    public void Dispose() { }

    public CrossCuttingSnapshot CurrentSnapshot()
    {
        InsightsEngine? engine = InsightsEngine.Shared;
        if (engine == null) return CrossCuttingSnapshot.Empty;

        string[] modNames = HookInterceptor.ProfiledModNames;

        // Group records by PatternKey; within each group, count Appearances per ModId.
        // Outer dict: PatternKey -> (modId -> appearances).
        var byPattern = new Dictionary<PatternKey, Dictionary<int, int>>();
        foreach (Insight rec in engine.Store.AllLive())
        {
            int modId = rec.Subject.ModId;
            if (modId < 0) continue;
            if (!byPattern.TryGetValue(rec.Pattern, out var counter))
            {
                counter = new Dictionary<int, int>();
                byPattern[rec.Pattern] = counter;
            }
            counter.TryGetValue(modId, out int prev);
            counter[modId] = prev + 1;
        }

        if (byPattern.Count == 0) return CrossCuttingSnapshot.Empty;

        var groups = new List<CrossCuttingGroup>(byPattern.Count);
        foreach (var kv in byPattern)
        {
            var leaders = new List<CrossCuttingEntry>(kv.Value.Count);
            foreach (var mc in kv.Value)
            {
                leaders.Add(new CrossCuttingEntry(
                    ModId: mc.Key,
                    ModName: ModNames.SafeName(mc.Key, modNames),
                    Appearances: mc.Value));
            }
            Shares.TopN(leaders, MaxLeadersPerGroup,
                (a, b) => b.Appearances.CompareTo(a.Appearances));
            groups.Add(new CrossCuttingGroup(
                SignalClass: kv.Key.ToString(),
                Leaders: leaders));
        }

        return new CrossCuttingSnapshot(worldLoaded: true, groups: groups);
    }

    public object CurrentSnapshotBoxed() => CurrentSnapshot();
}
