using Terraria;
using Terraria.ModLoader;

namespace PerformanceProfiler;

/// <summary>
/// Mod entry point. tModLoader discovers and instantiates exactly one
/// <see cref="Mod"/> subclass per mod. Mod-level lifecycle hooks (Load / Unload,
/// global content registration, the future hook-interceptor install / teardown)
/// hang off this class as the project grows past the scaffold.
/// </summary>
public class PerformanceProfiler : Mod
{
    // No mod-level overrides required for the Milestone 0 smoke test.
}

/// <summary>
/// Milestone 0 smoke test. Its only job is to prove the scaffold compiles, packs
/// into a .tmod, loads, and can reach the in-game chat layer. It is replaced by
/// the real metric-collection systems once the hook-interceptor work begins
/// (see README.md — System architecture).
/// </summary>
public class HelloWorldSystem : ModSystem
{
    public override void OnWorldLoad()
    {
        // Single-player: Main.NewText writes straight to the chat readout.
        // Multiplayer servers use ChatHelper.BroadcastChatMessage instead.
        // OnWorldLoad, not Mod.Load: chat only exists once a world is live;
        // Mod.Load runs before any world, so Main.NewText would have nothing
        // to render into.
        Main.NewText("Performance Profiler: hello world", 255, 220, 100);
    }
}
