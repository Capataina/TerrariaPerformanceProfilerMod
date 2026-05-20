#nullable enable

using Terraria;
using Terraria.ModLoader;
using PerformanceProfiler.Profiling.Events;

namespace PerformanceProfiler.Profiling;

/// <summary>
/// Drives the <see cref="MetricCollector"/> from tModLoader's world lifecycle
/// and per-tick hooks.
///
/// This is the thin glue layer between the game and the profiling engine: it
/// reads raw values from <see cref="Main"/> (entity counts, the tick index) and
/// hands them to the collector, which owns the logic. Keeping the two apart is
/// what lets <see cref="MetricCollector"/> be unit-tested without a running
/// game (CLAUDE.md testability standard).
/// </summary>
public sealed class ProfilerSystem : ModSystem
{
    /// <summary>Rolling-history length: 30 seconds at Terraria's 60 ticks per second.</summary>
    private const int HistoryCapacity = 30 * 60;

    /// <summary>
    /// The per-tick measuring engine, live only while a world is loaded. The UI
    /// reads this to draw the overlay. Null between worlds.
    /// </summary>
    public MetricCollector? Collector { get; private set; }

    private SessionLogWriter? _sessionLog;

    /// <summary>
    /// Per-tick game-state snapshotter (biomes, bosses, weather, invasion,
    /// subworld). Created once at world load, ticked from
    /// <see cref="PostUpdateEverything"/> after <see cref="MetricCollector"/>
    /// closes the frame so the same frame's <see cref="TickFrame.Context"/>
    /// can be written and the Events aggregator can accumulate against it.
    /// Live only while a world is loaded.
    /// </summary>
    private ContextTagger? _contextTagger;

    /// <summary>
    /// Per-dimension bucket aggregator that turns <see cref="TickFrame.Context"/>
    /// into the rows the Events tab renders. Lives only while a world is
    /// loaded; reset on world unload so a session's stats never bleed into
    /// the next.
    /// </summary>
    internal EventAggregator? Events { get; private set; }

    /// <summary>
    /// Installs the per-mod timing detours once, after every mod's content is
    /// set up (so all hook-override methods exist). The delegate-pair detours
    /// installed by <see cref="HookInterceptor"/> are removed automatically by
    /// tModLoader on mod unload; the ILHook detours installed by
    /// <see cref="ILHookInterceptor"/> are explicitly disposed in
    /// <c>PerformanceProfiler.Unload</c> so their references to types in this
    /// assembly don't outlive the unload.
    /// </summary>
    public override void PostSetupContent()
    {
        // Delegate path always runs first -- it does the mod-list enumeration
        // and PerModAttribution.Configure that the ILHook path reuses.
        HookInterceptor.Install(Mod);

        if (HookBackend.ILHookActive)
        {
            ILHookInterceptor.Install(Mod, HookInterceptor.ProfiledMods);
        }

        // Context registry is built after every mod's ModBiomes have been
        // registered (PostSetupContent runs after ModContent.Load). The
        // SubworldLibrary probe binds its reflection surface here too; both
        // are session-stable once populated.
        BiomeRegistry.Populate();
        SubworldProbe.Initialise();
        Mod.Logger.Info(
            $"events context: {BiomeRegistry.VanillaCount} vanilla biomes, " +
            $"{BiomeRegistry.Count - BiomeRegistry.VanillaCount} modded biomes, " +
            $"{WeatherSources.All.Length} weather flags, " +
            $"subworld={(SubworldProbe.Available ? "true" : "false")}, " +
            $"modBiomeBinding={(BiomeRegistry.ModBiomeBindingOk ? "ok" : "missing")}");
    }

    /// <summary>
    /// Allocates the engine and its ring buffer once, at world entry. Paired
    /// one-to-one with <see cref="OnWorldUnload"/> so the buffer is allocated
    /// once and freed once (Invariant 2).
    /// </summary>
    public override void OnWorldLoad()
    {
        Collector = new MetricCollector(HistoryCapacity);
        _sessionLog = SessionLogWriter.Create();
        _contextTagger = new ContextTagger();
        _contextTagger.Reset();
        Events = new EventAggregator();
        Mod.Logger.Info($"Profiler armed: {HistoryCapacity}-tick rolling history allocated.");
    }

    /// <summary>Releases the engine at world exit.</summary>
    public override void OnWorldUnload()
    {
        if (Collector != null)
        {
            _sessionLog?.End(Collector);
        }

        _sessionLog?.Dispose();
        _sessionLog = null;
        Collector = null;
        _contextTagger = null;
        Events = null;
        BossSampler.Clear();
        SubworldProbe.Clear();
        Mod.Logger.Info("Profiler disarmed: world unloaded.");
    }

    /// <summary>
    /// Tick start. <see cref="ModSystem.PreUpdateEntities"/> fires only on
    /// full-update frames, so a skipped partial frame simply never opens a tick.
    /// </summary>
    public override void PreUpdateEntities()
    {
        Collector?.BeginTick();
    }

    /// <summary>
    /// Tick end. <see cref="ModSystem.PostUpdateEverything"/> is the last hook in
    /// an update; here the game's entity counts and tick index are read and the
    /// frame is committed.
    /// </summary>
    public override void PostUpdateEverything()
    {
        MetricCollector? collector = Collector;
        if (collector == null || !collector.TickOpen)
        {
            return;
        }

        collector.EndTick(
            tickIndex: (long)Main.GameUpdateCount,
            npcCount: CountActive(Main.npc),
            projectileCount: CountActive(Main.projectile),
            dustCount: CountActive(Main.dust));

        if (collector.ConsumeDivergenceLogTrigger())
        {
            double del = collector.BackendTotalMs0;
            double il = collector.BackendTotalMs1;
            double pct = collector.BackendDivergence * 100d;
            Mod.Logger.Info(
                $"[backend-compare] delegate={del:F3}ms ilhook={il:F3}ms " +
                $"Δ={pct:+0.0;-0.0;0.0}% (mode={HookBackend.Mode})");
        }

        _sessionLog?.Tick(collector);

        // Events: snapshot the per-tick context and feed it to the
        // per-dimension aggregator. Ordering matters — EndTick has already
        // pushed the closed TickFrame into history, so the snapshot here
        // describes the same tick whose FrameTimeMs we read back below.
        ContextTagger? tagger = _contextTagger;
        EventAggregator? events = Events;
        if (tagger != null && events != null && collector.History.Count > 0)
        {
            long tickIndex = (long)Main.GameUpdateCount;
            tagger.Snapshot(tickIndex);
            double frameMs = collector.History[collector.History.Count - 1].FrameTimeMs;
            events.Accumulate(in tagger.Current, frameMs);
        }
    }

    private static int CountActive(NPC[] entities)
    {
        int count = 0;
        for (int i = 0; i < entities.Length; i++)
        {
            if (entities[i].active)
            {
                count++;
            }
        }

        return count;
    }

    private static int CountActive(Projectile[] entities)
    {
        int count = 0;
        for (int i = 0; i < entities.Length; i++)
        {
            if (entities[i].active)
            {
                count++;
            }
        }

        return count;
    }

    // Main.dust has roughly 6,000 slots, so this full scan runs every tick. That
    // is a few thousand bool checks (microseconds) and is acceptable for
    // Milestone 1. If a later overhead measurement flags it, switch to the
    // Lite-mode sampling cadence rather than scanning every tick.
    private static int CountActive(Dust[] entities)
    {
        int count = 0;
        for (int i = 0; i < entities.Length; i++)
        {
            if (entities[i].active)
            {
                count++;
            }
        }

        return count;
    }
}
