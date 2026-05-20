#nullable enable

namespace PerformanceProfiler.Profiling.Segments;

/// <summary>
/// The kind of game-state slice a <see cref="Segment"/> describes. Each
/// family has its own start/end predicates and its own keyspace for the
/// secondary <c>Key</c> field on <see cref="Segment"/>:
///
/// <list type="bullet">
///   <item><see cref="Biome"/>     — Key = <c>BiomeRegistry</c> id.</item>
///   <item><see cref="Weather"/>   — Key = <c>WeatherFlags</c> bit value.</item>
///   <item><see cref="Boss"/>      — Key = NPC type id.</item>
///   <item><see cref="Invasion"/>  — Key = <c>InvasionId</c> value.</item>
///   <item><see cref="Subworld"/>  — Key = SubworldProbe hash.</item>
///   <item><see cref="Hardmode"/>  — Key = 1 (true).</item>
///   <item><see cref="Combat"/>    — Key = 1.</item>
///   <item><see cref="DeathBracket"/> — Key = death index (1, 2, …).</item>
///   <item><see cref="UserBookmark"/> — Key = bookmark id.</item>
/// </list>
///
/// Stored as a byte on disk so a 1M-segment lifetime DB pays 1 MB for the
/// discriminator, not 8 MB.
/// </summary>
public enum SegmentFamily : byte
{
    Biome = 0,
    Weather = 1,
    Boss = 2,
    Invasion = 3,
    Subworld = 4,
    Hardmode = 5,
    Combat = 6,
    DeathBracket = 7,
    UserBookmark = 8,
}
