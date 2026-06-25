#nullable enable

using System;
using System.Collections.Generic;
using PerformanceProfiler.Persistence.History;
using PerformanceProfiler.Persistence.Records;

namespace PerformanceProfiler.Insights.CrossSession;

/// <summary>
/// Runs the cross-session detector family once per session over the persisted history.
/// Assembles the current stack's <see cref="ModHistory"/> from the live roster (so every
/// finding is about a mod the player still has, with a resolvable ModId), then evaluates
/// each detector. Pure of the game runtime apart from the roster array it is handed.
///
/// <para>
/// Run at session start (the rollup holds the player's PRIOR sessions then; the current
/// session folds in at end), so a finding reads as "across your last N sessions you …".
/// Each detector is independently guarded so one throwing never sinks the rest.
/// </para>
/// </summary>
public static class CrossSessionEvaluator
{
    private static readonly ICrossSessionDetector[] Detectors =
    {
        new UnusedAcrossSessionsDetector(),
        new LifetimeSpikeContributorDetector(),
        new CostlyDespiteLowUsageDetector(),
        new CrossModpackCostDivergenceDetector(),
    };

    public static List<Insight> Run(HistoryStore history, IReadOnlyList<string> roster, string fingerprint)
    {
        var emit = new List<Insight>();
        try
        {
            var stack = new List<ModHistory>();
            var idByName = new Dictionary<string, int>();
            for (int modId = 0; modId < roster.Count; modId++)
            {
                string name = roster[modId];
                if (string.IsNullOrEmpty(name) || idByName.ContainsKey(name)) continue;
                idByName[name] = modId;
                ModHistory? h = history.GetModHistory(name, ModLifetimeRollupRow.RingCapacity);
                if (h != null) stack.Add(h);
            }

            var input = new CrossSessionInput(stack, idByName, fingerprint);
            foreach (ICrossSessionDetector d in Detectors)
            {
                try { d.Evaluate(input, emit); }
                catch { /* one detector failing must not sink the family (Invariant 4) */ }
            }
        }
        catch { /* abort-clean: a read/shape failure yields no cross-session insights */ }
        return emit;
    }
}
