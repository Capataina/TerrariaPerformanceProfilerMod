#nullable enable

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
