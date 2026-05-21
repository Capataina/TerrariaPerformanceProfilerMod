#nullable enable

using Terraria;
using Terraria.GameContent.Events;
using Terraria.ID;

using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Data.Streams;
using PerformanceProfiler.Profiling.Persistence;
using PerformanceProfiler.Profiling.Persistence.Records;
namespace PerformanceProfiler.Data.Collectors;

/// <summary>
/// Per-tick snapshot of the game's state into an <see cref="EventContext"/>.
/// Reads are split across three cadences (plan §3.3):
///
/// <list type="bullet">
///   <item><b>Every tick</b> — boss scan (spawns/despawns matter at tick granularity).</item>
///   <item><b>Every 6 ticks (10 Hz)</b> — weather, vanilla invasion, biome bitset (the underlying tile-count machinery itself runs at game-tick cadence).</item>
///   <item><b>Every 60 ticks (1 Hz)</b> — difficulty, hardmode, subworld key (effectively constant within a session).</item>
/// </list>
///
/// <para>
/// Between reads the previous tick's values stay in the snapshot — a single
/// cached <see cref="EventContext"/> the tagger updates in place. The
/// aggregator copies the relevant slices out per tick.
/// </para>
/// </summary>
internal sealed class ContextTagger
{
    private const int WeatherCadenceTicks = 6;
    private const int SlowCadenceTicks    = 60;

    private EventContext _ctx;
    private int _tickPhase;
    private bool _initialised;

    /// <summary>The most recent snapshot. Returned by reference for the aggregator's hot path.</summary>
    public ref readonly EventContext Current => ref _ctx;

    /// <summary>Resets phase counters and the cached context. Called on world load.</summary>
    public void Reset()
    {
        _tickPhase = 0;
        _initialised = false;
        _ctx = default;
        _ctx.Biomes = new BiomeBitset(BiomeRegistry.Count);
    }

    /// <summary>
    /// Updates the cached <see cref="EventContext"/> from <see cref="Main"/>
    /// and <see cref="Main.LocalPlayer"/>. Allocation-free in steady state.
    /// </summary>
    public void Snapshot(long tickIndex)
    {
        _ctx.TickIndex = tickIndex;

        // Boss scan — every tick. The existing entity-count scan in
        // ProfilerSystem.CountActive already pays for the full Main.npc[]
        // walk; this adds only a `.boss || ShouldBeCountedAsBoss[type]`
        // predicate per slot.
        BossSampler.Sample(ref _ctx.Bosses);

        bool firstRun = !_initialised;
        if (firstRun)
        {
            SampleWeather();
            SampleBiomes();
            SampleInvasion();
            SampleSlow();
            _initialised = true;
            _tickPhase = 1;
            return;
        }

        int phase = _tickPhase;
        if (phase % WeatherCadenceTicks == 0)
        {
            SampleWeather();
            SampleBiomes();
            SampleInvasion();
        }
        if (phase % SlowCadenceTicks == 0)
        {
            SampleSlow();
        }
        _tickPhase = phase + 1;
        if (_tickPhase >= SlowCadenceTicks) _tickPhase = 0;
    }

    private void SampleWeather()
    {
        WeatherFlags w = WeatherFlags.None;
        var sources = WeatherSources.All;
        for (int i = 0; i < sources.Length; i++)
        {
            if (sources[i].Read()) w |= sources[i].Flag;
        }
        _ctx.Weather = w;
    }

    private void SampleBiomes()
    {
        Player? p = Main.LocalPlayer;
        if (p == null)
        {
            _ctx.Biomes.ClearAll();
            return;
        }
        BiomeRegistry.Sample(p, ref _ctx.Biomes);
    }

    private void SampleInvasion()
    {
        // OldOnesArmy first — DD2Event.Ongoing is a separate flag from
        // Main.invasionType. If both are active (unusual), the DD2 marker
        // wins because invasions are mutually exclusive in the player's
        // mental model and DD2 is the louder world-event.
        if (DD2Event.Ongoing)
        {
            _ctx.VanillaInvasion = InvasionId.OldOnesArmy;
            return;
        }
        _ctx.VanillaInvasion = (Main.invasionType) switch
        {
            InvasionID.GoblinArmy     => InvasionId.Goblins,
            InvasionID.SnowLegion     => InvasionId.FrostLegion,
            InvasionID.PirateInvasion => InvasionId.Pirates,
            InvasionID.MartianMadness => InvasionId.Martians,
            _                         => InvasionId.None,
        };
    }

    private void SampleSlow()
    {
        _ctx.Hardmode = Main.hardMode;

        // GameModeInfo is a struct (GameModeData); read directly.
        if (Main.GameModeInfo.IsJourneyMode)      _ctx.Mode = GameMode.Journey;
        else if (Main.GameModeInfo.IsMasterMode)  _ctx.Mode = GameMode.Master;
        else if (Main.GameModeInfo.IsExpertMode)  _ctx.Mode = GameMode.Expert;
        else                                       _ctx.Mode = GameMode.Classic;

        _ctx.SubworldKey = SubworldProbe.Sample();
    }
}
