#nullable enable

// v0.6.1: the EventContext fields below (Biomes / Weather / Mode /
// VanillaInvasion / Bosses) are assigned by ContextSnapshotter and
// EventAggregator at runtime. Those two writer files reference
// Terraria.ModLoader types so the test project deliberately excludes
// them — the test build then sees this struct's fields as "never
// assigned, will always have default" (CS0649). The warning is a true
// statement for the test build but a false positive for the runtime
// build, so we suppress it locally here.
#pragma warning disable CS0649

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Data.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Persistence;
using PerformanceProfiler.Profiling.Persistence.Records;
namespace PerformanceProfiler.Profiling.Events;

/// <summary>
/// Per-tick game-state context the Events tab aggregates against: biome bits,
/// weather/event flags, difficulty/hardmode, vanilla invasion id, an array of
/// active-boss NPC types (multi-segment collapsed to the head), and an
/// optional SubworldLibrary key.
///
/// <para>
/// Value type with no managed references aside from the bitset's backing
/// array (allocated once at install in <see cref="BiomeRegistry.Populate"/>).
/// A frame holds one of these by value; per-tick population is allocation
/// free (see plan §3.1).
/// </para>
/// </summary>
public struct EventContext
{
    /// <summary>The game tick this snapshot describes.</summary>
    public long TickIndex;

    /// <summary>Bitset of every active biome (vanilla Zone* properties plus modded ModBiomes).</summary>
    internal BiomeBitset Biomes;

    /// <summary>Bitset of vanilla weather, moon, and world-event flags. See <see cref="Events.WeatherFlags"/>.</summary>
    internal WeatherFlags Weather;

    /// <summary>True when Hardmode has been triggered for this world.</summary>
    public bool Hardmode;

    /// <summary>Classic, Expert, Master, or Journey. Stable for the session.</summary>
    internal GameMode Mode;

    /// <summary>Active vanilla invasion. None if no invasion is running. Old-Ones-Army is folded in via DD2Event.Ongoing.</summary>
    internal InvasionId VanillaInvasion;

    /// <summary>Active boss NPC type ids this tick, multi-segment collapsed to head, max <see cref="BossSlotArray.SlotCount"/>.</summary>
    internal BossSlotArray Bosses;

    /// <summary>Non-zero when the player is inside a SubworldLibrary subworld. Keyed by FullName in <see cref="SubworldProbe"/>'s dictionary.</summary>
    public int SubworldKey;
}
