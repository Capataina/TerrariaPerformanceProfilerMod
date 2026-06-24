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
    /// Active-use weight for a mod this session: the tick-credits its content
    /// accrued while actually wielded or worn — held item + accessories + armour
    /// + time stood in the mod's biomes. This is the "is this mod's content in
    /// use" signal, and it is the single home for the formula the duplication
    /// census found copied across four surfaces.
    ///
    /// <para>
    /// <b>Wave 4 — the meaning changed at the root.</b> The old weight was
    /// <c>ItemsCreated + NpcsSpawned + BossesFought + BuffsApplied (+ Invasions)</c>,
    /// i.e. content <i>created or encountered</i>. That was the Flute-reads-zero
    /// bug: holding or swinging an already-owned weapon fires no creation hook, so
    /// a wielded-but-not-crafted item read as unused and a session that crafted
    /// nothing collapsed every usage share to 0%. The creation / encounter counts
    /// are still on <see cref="ModUsageEntry"/> as their own legitimate axis (what
    /// was made), via <see cref="CreationWeight"/>; they are simply no longer a
    /// proxy for use.
    /// </para>
    /// </summary>
    public static long UsageWeight(in ModUsageEntry u) =>
        u.ItemsHeldTicks + u.AccessoryEquippedTicks + u.ArmorEquippedTicks + u.TicksInOwnedBiomes;

    /// <summary>
    /// Creation / encounter weight: content this mod made or the player met this
    /// session (items created + NPCs spawned + bosses fought + buffs applied +
    /// invasions fought). A distinct, legitimate signal from <see cref="UsageWeight"/>
    /// — "what got made" rather than "what is in use" — kept so a surface that
    /// genuinely wants the creation count has one home for it too.
    /// </summary>
    public static long CreationWeight(in ModUsageEntry u) =>
        u.ItemsCreated + u.NpcsSpawned + u.BossesFought + u.BuffsApplied + u.InvasionsFought;

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
