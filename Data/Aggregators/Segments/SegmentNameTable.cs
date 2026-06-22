#nullable enable

using PerformanceProfiler.Profiling.Events;

using PerformanceProfiler.Profiling;
using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Data.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling.Persistence;
using PerformanceProfiler.Profiling.Persistence.Records;
namespace PerformanceProfiler.Data.Aggregators.Segments;

/// <summary>
/// Family-aware display name resolver. Centralised so the detector,
/// retrospective card, Timeline tab, and chat commands all spell the same
/// segment the same way without each one re-implementing the family→source
/// dispatch.
///
/// <para>
/// Names are resolved at segment OPEN time (when the predicate first fires)
/// and stored on the OpenSegment, so a modded biome whose name registry
/// disappears at world unload doesn't leave us with a row reading
/// "biome:23". The exception is bosses, which we resolve at CLOSE time —
/// the friendly name is more useful than "NPC:type" for the retrospective,
/// and BossSampler caches them inside <see cref="BossSampler.DisplayName"/>.
/// </para>
/// </summary>
internal static class SegmentNameTable
{
    public static string For(SegmentFamily family, int key) => family switch
    {
        SegmentFamily.Biome     => BiomeName(key),
        SegmentFamily.Weather   => WeatherSources.DisplayName((WeatherFlags)key),
        SegmentFamily.Boss      => BossSampler.DisplayName(key) + " fight",
        SegmentFamily.Invasion  => InvasionName(key),
        SegmentFamily.Subworld  => SubworldProbe.DisplayName(key) + " trip",
        SegmentFamily.Hardmode  => "Hardmode",
        SegmentFamily.Combat    => "Combat window",
        SegmentFamily.DeathBracket => "Run #" + key,
        SegmentFamily.UserBookmark => "Bookmark #" + key,
        _ => family + ":" + key,
    };

    private static string BiomeName(int id)
    {
        if (id >= 0 && id < BiomeRegistry.Biomes.Count)
        {
            string name = BiomeRegistry.Biomes[id].DisplayName;
            return name + " visit";
        }
        return "biome:" + id + " visit";
    }

    private static string InvasionName(int key) => ((InvasionId)key) switch
    {
        InvasionId.Goblins     => "Goblin Army",
        InvasionId.FrostLegion => "Frost Legion",
        InvasionId.Pirates     => "Pirate Invasion",
        InvasionId.Martians    => "Martian Madness",
        InvasionId.OldOnesArmy => "Old One's Army",
        _ => "Invasion " + key,
    };
}
