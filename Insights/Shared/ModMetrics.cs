#nullable enable

using System.Collections.Generic;
using PerformanceProfiler.Data.Contracts;

namespace PerformanceProfiler.Insights.Shared;

/// <summary>
/// Pure per-mod metric primitives shared across every interpretation surface.
/// Each formula has exactly one home here; the duplication census in
/// <c>context/plans/insights-engine.md</c> enumerates the scattered call sites
/// these replace (usage weight in 4 places, roster size in 2, the per-mod
/// category fold in 5).
///
/// <para>
/// Nothing here is relative or interpretive — these are the building blocks the
/// detectors and the published interpretation stats compose. They take contract
/// snapshots / entries as input and never touch collection internals (the input
/// contract in the plan's §"Input contract").
/// </para>
/// </summary>
public static class ModMetrics
{
    /// <summary>
    /// Engagement-event weight attributed to a mod this session: items created
    /// + NPCs spawned + bosses fought + buffs applied, plus invasions fought
    /// when <paramref name="includeInvasions"/> is true.
    ///
    /// <para>
    /// <b>Known divergence (preserved deliberately):</b> the I1/I6 surfaces
    /// (<see cref="PerformanceProfiler.Data.Contracts.ModObservatorySnapshot"/>,
    /// engagement-cost) include invasions; the I2 dormant surface historically
    /// did not. The flag captures that one difference explicitly rather than
    /// letting it drift silently across copies. Wave 4 (the active-use rework)
    /// replaces this creation-weighted proxy with a real "actively used" signal,
    /// at which point the divergence is reconciled at its root.
    /// </para>
    /// </summary>
    public static long UsageWeight(in ModUsageEntry u, bool includeInvasions = true) =>
        u.ItemsCreated + u.NpcsSpawned + u.BossesFought + u.BuffsApplied
        + (includeInvasions ? u.InvasionsFought : 0L);

    /// <summary>
    /// Total registered-content count for a mod: items + NPCs + buffs +
    /// projectiles + mounts + accessories + invasions + bosses. Deliberately
    /// excludes <see cref="PerformanceProfiler.Data.Contracts.ModRosterEntry.Biomes"/>,
    /// matching both historical call sites — biomes are a context surface, not a
    /// roster-size contributor for the dormancy denominator.
    /// </summary>
    public static int RosterSize(in ModRosterEntry r) =>
        r.Items + r.NPCs + r.Buffs + r.Projectiles
        + r.Mounts + r.Accessories + r.Invasions + r.Bosses;

    /// <summary>
    /// Sums one mod's row across all categories in a row-major
    /// <c>[modCount * catCount]</c> array (index <c>modId * catCount + c</c>).
    /// Tolerates a <paramref name="rowMajor"/> shorter than the implied length
    /// (the bound matches the inline folds it replaces, which guard against a
    /// per-mod array wider than the smoothed source).
    /// </summary>
    public static double SumModCategories(IReadOnlyList<double> rowMajor, int modId, int catCount)
    {
        double sum = 0d;
        int baseIdx = modId * catCount;
        for (int c = 0; c < catCount; c++)
        {
            int idx = baseIdx + c;
            if (idx < rowMajor.Count) sum += rowMajor[idx];
        }
        return sum;
    }
}
