#nullable enable

using Terraria;
using Terraria.ModLoader;

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

    /// <summary>
    /// Installs the per-mod timing detours once, after every mod's content is
    /// set up (so all hook-override methods exist). The detours persist for the
    /// process and are removed by tModLoader when this mod unloads.
    /// </summary>
    public override void PostSetupContent()
    {
        HookInterceptor.Install(Mod);
    }

    /// <summary>
    /// Allocates the engine and its ring buffer once, at world entry. Paired
    /// one-to-one with <see cref="OnWorldUnload"/> so the buffer is allocated
    /// once and freed once (Invariant 2).
    /// </summary>
    public override void OnWorldLoad()
    {
        Collector = new MetricCollector(HistoryCapacity);
        Mod.Logger.Info($"Profiler armed: {HistoryCapacity}-tick rolling history allocated.");
    }

    /// <summary>Releases the engine at world exit.</summary>
    public override void OnWorldUnload()
    {
        Collector = null;
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
