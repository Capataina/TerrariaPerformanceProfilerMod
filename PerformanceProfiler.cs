#nullable enable

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Terraria;
using Terraria.GameInput;
using Terraria.ModLoader;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Persistence;
using PerformanceProfiler.UI;
using PerformanceProfiler.Web;
using PerformanceProfiler.Web.Server;

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

    /// <summary>
    /// Cached log4net logger for the persistence/recorder layer so it can
    /// write narration lines to <c>client.log</c> without taking a
    /// dependency on the <c>Mod</c> singleton or having to chase
    /// <c>ModContent.GetInstance</c>. Set in <see cref="Load"/>, cleared
    /// in <see cref="Unload"/>. Null if the mod hasn't loaded yet.
    /// </summary>
    public static log4net.ILog? LoggerOrNull { get; private set; }

    /// <summary>
    /// Local HTTP dashboard server. Loopback-only (127.0.0.1), bound at
    /// <see cref="Load"/>, disposed at <see cref="Unload"/>. Null if the
    /// bind failed (every port in the search range busy); in that case
    /// the F10 keybind prints a failure message and the rest of the mod
    /// keeps working.
    /// </summary>
    public static DashboardHttpServer? Dashboard { get; private set; }

    public override void Load()
    {
        LoggerOrNull = Logger;
        Logger.Info($"Performance Profiler loaded (backend: {HookBackend.Mode}).");

        // v0.9.x data pipeline — register every IDataStream once at mod
        // load. The registry stays populated across world loads/unloads;
        // per-session state lives inside each stream and is cleared by
        // ResetAll. Order of Register calls does not matter, but stable
        // names matter (consumers look them up by name).
        RegisterDataPipeline();

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

        // v0.8 step 1 prototype: start the loopback HTTP dashboard server.
        // TcpListener-based, no admin needed, binds 127.0.0.1:27277 (or the
        // next free port up to 27287). F10 in-game opens the default browser
        // to the chosen URL.
        try
        {
            Dashboard = new DashboardHttpServer(
                route: DashboardRouter.Route,
                log: (msg, ex) =>
                {
                    if (ex != null) Logger.Warn($"{msg}: {ex.GetType().Name}: {ex.Message}");
                    else Logger.Info(msg);
                });
            Logger.Info($"Dashboard server bound at {Dashboard.Url} (press F10 in-game to open).");
        }
        catch (Exception ex)
        {
            Dashboard = null;
            Logger.Warn($"Dashboard server failed to start ({ex.GetType().Name}: {ex.Message}); F10 keybind will be inert.");
        }
    }

    /// <summary>
    /// Runs in reverse load order on Mods → Reload. Disposes the ILHook detours
    /// constructed via <c>new ILHook(...)</c> in <see cref="ILHookInterceptor"/>;
    /// these are not auto-tracked by tModLoader the way <c>MonoModHooks.Add</c>
    /// detours are, so without explicit disposal here the IL patches would
    /// reference types in this assembly that's about to be unloaded.
    /// </summary>
    /// <summary>
    /// Register every <see cref="Data.IDataStream"/> the mod ships with.
    /// Called once at <see cref="Load"/> before any world exists. Each
    /// migration step adds new <c>Register</c> lines here; the registry
    /// is single-source-of-truth for "what data does this mod produce".
    /// </summary>
    private static void RegisterDataPipeline()
    {
        var r = Data.DataRegistry.Shared;
        r.Register(new Data.Stats.KpiStat());
        r.Register(new Data.Stats.EventsFeedStat());
        r.Register(new Data.Aggregators.HeatmapAggregator());
    }

    public override void Unload()
    {
        ILHookInterceptor.Uninstall();

        // v0.9.x data pipeline — dispose every registered stream and
        // empty the registry. Has to run BEFORE the DB dispose so any
        // stream that holds a DB reference can flush cleanly.
        try
        {
            Data.DataRegistry.Shared.DisposeAll();
        }
        catch (Exception ex)
        {
            Logger.Warn($"DataRegistry dispose failed: {ex.GetType().Name}: {ex.Message}");
        }

        // Stop the HTTP dashboard server before the DB so the route handler
        // can't call into a half-disposed DB on the way out.
        try
        {
            Dashboard?.Dispose();
        }
        catch (Exception ex)
        {
            Logger.Warn($"Dashboard server dispose failed: {ex.GetType().Name}: {ex.Message}");
        }
        Dashboard = null;

        try
        {
            Database?.Dispose();
        }
        catch (Exception ex)
        {
            Logger.Warn($"Profiler DB dispose failed: {ex.GetType().Name}: {ex.Message}");
        }
        Database = null;
        LoggerOrNull = null;
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
        //
        // v0.9.0: the in-game overlay is archived. Only the browser dashboard
        // remains as a player surface; this chat hint is the discovery
        // mechanism. Single line, single keybind, single URL.
        string? url = PerformanceProfiler.Dashboard?.Url;
        if (url != null)
        {
            Main.NewText($"Performance Profiler ready. Press F9 for the dashboard ({url}).", 180, 220, 255);
        }
        else
        {
            Main.NewText("Performance Profiler ready — dashboard server failed to start; see client.log.", 255, 180, 100);
        }
        Mod.Logger.Info("OnEnterWorld fired; dashboard hotkey announced.");
    }

    public override void ProcessTriggers(TriggersSet triggersSet)
    {
        // ProcessTriggers runs only during gameplay, on the local client.
        ModKeybind? dashboard = ProfilerOverlaySystem.DashboardKeybind;
        if (dashboard != null && dashboard.JustPressed)
        {
            OpenDashboardInBrowser();
        }
    }

    /// <summary>
    /// Launches the player's default browser at the local dashboard URL.
    ///
    /// <para>
    /// Cross-platform <c>Process.Start</c> is a minefield: on Windows
    /// <c>UseShellExecute=true</c> with a URL goes to the default browser;
    /// on macOS the same call falls through to a missing <c>xdg-open</c>
    /// path and silently fails; on Linux <c>xdg-open</c> usually exists
    /// but is sometimes missing in minimal installs. The reliable answer
    /// is to dispatch on <see cref="RuntimeInformation"/> and invoke the
    /// platform's native URL-opener directly: <c>open</c> on macOS,
    /// <c>xdg-open</c> on Linux, the shell on Windows.
    /// </para>
    ///
    /// <para>
    /// Failure path always prints the URL in chat so the player can copy
    /// it manually (also what WikiThis falls back to on
    /// <c>tModLoader/tModLoader#4223</c>).
    /// </para>
    /// </summary>
    private static void OpenDashboardInBrowser()
    {
        DashboardHttpServer? server = PerformanceProfiler.Dashboard;
        if (server == null)
        {
            Main.NewText("Dashboard server not running — see client.log.", 255, 120, 120);
            return;
        }
        string url = server.Url;
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", url);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("xdg-open", url);
            }
            else
            {
                // Windows path. UseShellExecute=true is required so the URL
                // gets dispatched to the OS's default-browser handler.
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true,
                });
            }
            PerformanceProfiler.LoggerOrNull?.Info($"Browser dashboard launched: {url}");
        }
        catch (Exception ex)
        {
            // Shell open failed. Print the URL so the player can copy/paste.
            Main.NewText($"Couldn't auto-open browser. Visit {url} manually.", 255, 220, 100);
            PerformanceProfiler.LoggerOrNull?.Warn(
                $"OpenDashboardInBrowser({url}) failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
