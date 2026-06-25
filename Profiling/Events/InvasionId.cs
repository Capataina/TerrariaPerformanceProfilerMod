#nullable enable

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Persistence.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Persistence;
using PerformanceProfiler.Persistence.Records;
namespace PerformanceProfiler.Profiling.Events;

/// <summary>
/// Vanilla invasion ids, mirrored from <c>Terraria.ID.InvasionID</c> plus
/// <c>OldOnesArmy</c> which is detected via <c>DD2Event.Ongoing</c> rather
/// than <c>Main.invasionType</c> (it is a tower-defence event, not a
/// classic invasion).
///
/// <para>
/// tModLoader 1.4.4 has no <c>ModInvasion</c> registration API; modded
/// invasions are a documented gap. See plan §4.4.
/// </para>
/// </summary>
internal enum InvasionId : byte
{
    None        = 0,
    Goblins     = 1,
    FrostLegion = 2,
    Pirates     = 3,
    Martians    = 4,
    OldOnesArmy = 5,
}
