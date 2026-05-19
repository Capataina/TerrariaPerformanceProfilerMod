using Terraria;
using Terraria.GameInput;
using Terraria.ModLoader;
using PerformanceProfiler.UI;

namespace PerformanceProfiler;

/// <summary>
/// Mod entry point. tModLoader discovers and instantiates exactly one
/// <see cref="Mod"/> subclass per mod. Mod-wide lifecycle work hangs off this
/// class; the profiling subsystems live in their own ModSystem types under
/// Profiling/ and UI/.
/// </summary>
public class PerformanceProfiler : Mod
{
    public override void Load()
    {
        // Written to client.log: the machine-readable proof the mod loaded,
        // verifiable from the log without anyone watching chat.
        Logger.Info("Performance Profiler loaded.");
    }
}

/// <summary>
/// Client-side input glue: polls the F9 keybind each gameplay tick and toggles
/// the profiler overlay, and announces the hotkey on world entry so it is
/// discoverable.
/// </summary>
public class ProfilerPlayer : ModPlayer
{
    public override void OnEnterWorld()
    {
        // OnEnterWorld, not ModSystem.OnWorldLoad: OnWorldLoad fires mid-load and
        // tModLoader clears the chat during the load-to-in-game transition, so a
        // message printed there is wiped before the player sees it.
        Main.NewText("Performance Profiler ready. Press F9 for the overlay.", 255, 220, 100);
        Mod.Logger.Info("OnEnterWorld fired; overlay hotkey announced.");
    }

    public override void ProcessTriggers(TriggersSet triggersSet)
    {
        // ProcessTriggers runs only during gameplay, on the local client.
        ModKeybind? toggle = ProfilerOverlaySystem.ToggleKeybind;
        if (toggle != null && toggle.JustPressed)
        {
            ModContent.GetInstance<ProfilerOverlaySystem>().ToggleVisibility();
        }
    }
}
