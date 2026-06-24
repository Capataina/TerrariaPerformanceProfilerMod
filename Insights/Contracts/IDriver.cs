#nullable enable

namespace PerformanceProfiler.Insights;

/// <summary>
/// A measurable dimension a detector can slice or regress a signal against — the
/// thing that <i>explains</i> a change. Entity count, session age, heap size, and
/// active-use are drivers. Two families lean on them:
/// <list type="bullet">
///   <item>Family E (Scaling) regresses cost against a driver: cost ≈ slope·driver
///   + intercept, with a cliff where the linear fit breaks.</item>
///   <item>Family B (Temporal) controls for a driver to stay honest: heap rising
///   <i>at constant entity count</i> is a leak; heap rising <i>with</i> entity count
///   is just progression (more map, more NPCs), not a leak.</item>
/// </list>
///
/// <para>
/// Drivers read generic surfaces only (Invariant 5): the held item via
/// <c>ModOwnerCache</c>, entity counts already on the tick frame, the managed heap
/// — never a named mod. Sampling reads an <see cref="IInsightInput"/> so a driver
/// is pure logic over declared input (the L1 test axis).
/// </para>
/// </summary>
public interface IDriver
{
    /// <summary>Stable name for the dimension (e.g. "projectiles", "sessionAgeMinutes", "heapMB").</summary>
    string Name { get; }

    /// <summary>The driver's current value, sampled from the processed-pipeline view.</summary>
    double Sample(IInsightInput input);
}
