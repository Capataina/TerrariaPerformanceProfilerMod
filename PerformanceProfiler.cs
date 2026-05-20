#nullable enable

using System;
using Terraria;
using Terraria.GameInput;
using Terraria.ModLoader;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Persistence;
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
    /// <summary>
    /// Mod-wide LiteDB-backed persistence layer. Opened at <see cref="Load"/>,
    /// disposed at <see cref="Unload"/>. Null if the open path failed — in
    /// that case the rest of the mod still runs, just without persistence
    /// (Invariant 4: abort-clean on host drift; here the "host" is the
    /// file system).
    /// </summary>
    public static ProfilerDatabase? Database { get; private set; }

    public override void Load()
    {
        Logger.Info($"Performance Profiler loaded (backend: {HookBackend.Mode}).");

        // Open the DB on the main thread before any world loads. Failure to
        // open degrades to no-persistence; the live overlay and metric
        // collection still work.
        try
        {
            Database = new ProfilerDatabase(
                ProfilerPaths.Root(),
                log: (msg, ex) =>
                {
                    if (ex != null) Logger.Warn($"{msg}: {ex.GetType().Name}: {ex.Message}");
                    else Logger.Info(msg);
                },
                profilerVersion: typeof(PerformanceProfiler).Assembly.GetName().Version?.ToString() ?? "unknown");

            // Best-effort import of any pre-existing JSON sessions written
            // by the legacy SessionLogWriter. Runs once per launch; nothing
            // imports a second time because the file is moved to a sentinel.
            LegacyJsonImporter.RunOnceIfNeeded(Database, Logger);

            Logger.Info($"Profiler DB opened at {Database.Root} (size {Database.DbFileSize / 1024} KB).");
        }
        catch (Exception ex)
        {
            Database = null;
            Logger.Warn($"Profiler DB unavailable this session ({ex.GetType().Name}: {ex.Message}); the overlay still works in-memory only.");
        }
    }

    /// <summary>
    /// Runs in reverse load order on Mods → Reload. Disposes the ILHook detours
    /// constructed via <c>new ILHook(...)</c> in <see cref="ILHookInterceptor"/>;
    /// these are not auto-tracked by tModLoader the way <c>MonoModHooks.Add</c>
    /// detours are, so without explicit disposal here the IL patches would
    /// reference types in this assembly that's about to be unloaded.
    /// </summary>
    public override void Unload()
    {
        ILHookInterceptor.Uninstall();
        try
        {
            Database?.Dispose();
        }
        catch (Exception ex)
        {
            Logger.Warn($"Profiler DB dispose failed: {ex.GetType().Name}: {ex.Message}");
        }
        Database = null;
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
