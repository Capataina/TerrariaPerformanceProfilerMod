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
    public override void Load()
    {
        // Written to client.log. The machine-readable proof that the mod
        // loaded — verifiable from the log without anyone watching chat.
        Logger.Info("Performance Profiler loaded — Milestone 0 scaffold.");
    }
}

/// <summary>
/// Milestone 0 smoke test. Proves the scaffold reaches the runtime and can
/// reach the player-visible chat layer. Replaced by the real metric-collection
/// systems once the hook-interceptor work begins (see README.md).
/// </summary>
public class ProfilerPlayer : ModPlayer
{
    public override void OnEnterWorld()
    {
        // OnEnterWorld, NOT ModSystem.OnWorldLoad: OnWorldLoad fires mid-load,
        // and tModLoader clears the chat during the load -> in-game transition,
        // so a message printed there is wiped before the player sees it.
        // OnEnterWorld fires once the player is fully in the world, chat live.
        Main.NewText("Performance Profiler: hello world", 255, 220, 100);
        Mod.Logger.Info("OnEnterWorld fired — hello world sent to chat.");
    }
}
