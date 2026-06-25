#nullable enable

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Persistence.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Persistence;
namespace PerformanceProfiler.Persistence.Records;

/// <summary>
/// One row inside <see cref="PlayerDeathRow.DamageWeighting"/>: a single
/// damage source's share of the damage the player took in the death window.
/// The list is sorted descending by <see cref="TotalDamage"/>; the first
/// entry is the damage-weighted "killer".
///
/// v0.5 (and earlier) attributed deaths to whichever source landed the final
/// blow, which under-credits softening damage. v0.6 aggregates over a
/// configurable window (default 10 s) so a vulture swarm that did 90% of
/// the damage doesn't get out-credited by a slime stealing the last 4 HP.
/// The full breakdown is persisted so the player can see the full story,
/// not just the top line.
/// </summary>
public sealed class DeathDamageContributor
{
    public string SourceKind { get; set; } = "";
    public int SourceId { get; set; }
    public string SourceName { get; set; } = "";
    public int TotalDamage { get; set; }

    /// <summary>Hit count from this source in the window. Useful for "the slime hit me 14 times" narration.</summary>
    public int HitCount { get; set; }

    /// <summary>Fraction of window total damage, in [0, 1]. Sums across all contributors to 1.0.</summary>
    public float Fraction { get; set; }
}
