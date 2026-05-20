#nullable enable

using System;
using System.IO;
using Terraria;
using Terraria.ModLoader;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Profiling.Insights;

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
    /// <summary>
    /// Process-wide self-health measurement. Owned here instead of on
    /// <see cref="MetricCollector"/> because the install-time delta is
    /// captured BEFORE the per-world collector exists; the collector picks
    /// up the same instance at world-load via <c>InstallSelfHealth</c>.
    /// </summary>
    internal static ProfilerSelfHealth SelfHealth { get; } = new ProfilerSelfHealth();

    public override void PostSetupContent()
    {
        // Capture the managed-heap baseline immediately before our install
        // pass. ProfilerSelfHealth forces a Gen2 here so transient content-
        // load junk doesn't end up counted against us. Cost: ~50-150 ms once.
        SelfHealth.MarkInstallStart();

        // Delegate path always runs first -- it does the mod-list enumeration
        // and PerModAttribution.Configure that the ILHook path reuses.
        HookInterceptor.Install(Mod);

        if (HookBackend.ILHookActive)
        {
            ILHookInterceptor.Install(Mod, HookInterceptor.ProfiledMods);
        }

        // Capture the post-install heap. Delta = our hook-install cost,
        // including Mono.Cecil method body cache + MonoMod trampolines.
        // Bytes-per-hook is the headline metric for the eventual memory-burn
        // mitigation work: tracking it across versions tells us whether we're
        // getting heavier or lighter as the codebase evolves.
        // PerModAttribution.HookCount is the union across both backends
        // (RegisterOrReuseHook collapses parallel-mode duplicates), so a
        // single read is the right denominator.
        SelfHealth.MarkInstallEnd(PerModAttribution.HookCount);
        Mod.Logger.Info(
            $"Profiler self-health: install delta {SelfHealth.InstallDeltaBytes / (1024 * 1024):F1} MB " +
            $"across {SelfHealth.InstalledHookCount} hooks " +
            $"({SelfHealth.BytesPerHook / 1024:F1} KB/hook).");

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
        // Inject the process-singleton self-health so install-delta measurements
        // captured at PostSetupContent survive across world loads. The
        // collector handles per-tick refresh; install-time state stays put.
        Collector = new MetricCollector(HistoryCapacity, SelfHealth);

        // Session logging is an agent surface, never a gameplay dependency. A
        // permissions/path/IO failure here must NEVER take down the profiler
        // lifecycle — degrade to "no session JSON for this world" and report
        // it in client.log so the loss is observable.
        try
        {
            _sessionLog = SessionLogWriter.Create();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            _sessionLog = null;
            Mod.Logger.Warn($"Session log disabled for this world ({ex.GetType().Name}: {ex.Message}); metric collection continues normally.");
        }

        _contextTagger = new ContextTagger();
        _contextTagger.Reset();
        Events = new EventAggregator();
        Mod.Logger.Info($"Profiler armed: {HistoryCapacity}-tick rolling history allocated.");
    }

    /// <summary>Releases the engine at world exit.</summary>
    public override void OnWorldUnload()
    {
        // Force-close any open spike window so an in-progress spike that
        // happened to coincide with the world exit still lands in the final
        // session report. Without the flush the detector keeps the open
        // window in scratch and the JSON misses it.
        Collector?.FlushSpikes();

        if (Collector != null && _sessionLog != null)
        {
            try
            {
                _sessionLog.End(Collector);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                Mod.Logger.Warn($"Session log end-write failed ({ex.GetType().Name}: {ex.Message}); world unload continues.");
            }
        }

        _sessionLog?.Dispose();
        _sessionLog = null;
        Collector = null;
        _contextTagger = null;
        Events = null;
        // Insights engine carries per-session detector state; clear it so the
        // next world starts with an empty live + history set rather than
        // inheriting the previous session's records.
        InsightsEngine.Shared = null;
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

        if (_sessionLog != null)
        {
            try
            {
                _sessionLog.Tick(collector);
            }
            catch (SessionLogFailureException ex)
            {
                Mod.Logger.Warn(
                    $"Session log disabled for this world ({ex.InnerException?.GetType().Name}: {ex.InnerException?.Message}); metric collection continues normally.");
                _sessionLog = null;
            }
        }

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
