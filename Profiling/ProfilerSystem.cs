#nullable enable

using System;
using System.IO;
using System.Threading.Tasks;
using Terraria;
using Terraria.ModLoader;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Profiling.Insights;
using PerformanceProfiler.Profiling.Persistence;
using PerformanceProfiler.Profiling.Segments;

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
    /// LiteDB-backed per-world recorder. Replaces the legacy JSON
    /// <c>SessionLogWriter</c>. Null when the mod's <see cref="ProfilerDatabase"/>
    /// failed to open (degraded session, no persistence — Invariant 4).
    /// </summary>
    private SessionRecorder? _recorder;
    private ContextTransitionWatcher? _transitionWatcher;
    private WorldSnapshotter? _snapshotter;
    private PlayerDeathDetector? _deathDetector;

    /// <summary>Live recorder's session id while a world is loaded; null otherwise. Read by chat commands to scope their queries.</summary>
    public LiteDB.ObjectId? LiveRecorderSessionId => _recorder?.SessionId;

    /// <summary>Live recorder while a world is loaded; null otherwise. Read by the GlobalNPC / GlobalItem / ModPlayer interaction hooks.</summary>
    public Persistence.SessionRecorder? LiveRecorder => _recorder;

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
    /// Per-tick segment detector — opens/closes Biome/Weather/Boss/etc segments
    /// against the same <see cref="EventContext"/> stream the
    /// <see cref="EventAggregator"/> reads, plus the side-channel spike/stall/death
    /// events that get folded into every currently-open segment.
    /// </summary>
    internal SegmentDetector? Segments { get; private set; }

    /// <summary>Live in-memory ring of closed segments + DB writer enqueue. Lives as long as the recorder.</summary>
    internal SegmentStore? SegmentStore { get; private set; }

    // Counters used to detect new spike / stall / death events arriving this
    // tick. Diffed against the live collector / death detector each tick to
    // decide whether to call SegmentDetector.OnSpike / OnStall / OnDeath.
    private int _lastSpikeCount;
    private int _lastStallCount;
    private bool _wasDeadLastTick;

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
        // v0.6 Phase α: capture the wall-clock origin once, here. Every
        // subsequent Time.UnixMsNow() call is a Stopwatch read + a multiply.
        // Cross-allocations dossier §3.1.
        Time.Reset();

        // v0.6 Phase α: pre-resolve every Lang.GetXxx name into a flat
        // string[] indexed by type id. PostSetupContent fires after every
        // modded id is registered, so the resolution captures vanilla +
        // every loaded mod's content. Cross-allocations dossier §3.2.
        LangNameCache.Populate();

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
    /// Marks the world-loaded state. v0.5 ran all the heavy construction
    /// (MetricCollector ring + SessionRecorder + ModlistFingerprint.Compute
    /// which reads every mod's assembly hash + the watchers + the aggregator)
    /// inline here on the main thread; the 16:09-16:14 playtest measured the
    /// world-enter freeze at 172 ms.
    ///
    /// v0.6 (mod-lifecycle dossier §4.4): set a deferred-init flag and run
    /// the construction on the first <see cref="PostUpdateEverything"/>
    /// call. The first tick takes the construction hit but that's during
    /// gameplay (allowed to spike per Invariant 2's overhead budgets) rather
    /// than during world-load (UI-blocking, no recovery for the player).
    /// </summary>
    private bool _deferredInitPending;

    // Single-slot guard so the InsightsEngine.Evaluate task can never
    // overlap itself. Reset to 0 when the background task completes;
    // checked + flipped via Interlocked.CompareExchange before spawn.
    private int _insightsEvalInflight;

    public override void OnWorldLoad()
    {
        _deferredInitPending = true;
    }

    /// <summary>Run the deferred OnWorldLoad construction. Called from the first PostUpdateEverything.</summary>
    private void RunDeferredWorldLoadInit()
    {
        // Inject the process-singleton self-health so install-delta measurements
        // captured at PostSetupContent survive across world loads. The
        // collector handles per-tick refresh; install-time state stays put.
        Collector = new MetricCollector(HistoryCapacity, SelfHealth);

        // Persistence is an agent surface, never a gameplay dependency. Any
        // failure here degrades to "no session in DB for this world" without
        // affecting metric collection or the live overlay (Invariant 4).
        try
        {
            ProfilerDatabase? db = PerformanceProfiler.Database;
            if (db != null)
            {
                string fingerprint = ModlistFingerprint.Compute();
                string mode = HookBackend.Mode.ToString();
                bool tracksAlloc = PerModAttribution.TracksAllocations;
                _recorder = new SessionRecorder(
                    db,
                    profilerVersion: typeof(ProfilerSystem).Assembly.GetName().Version?.ToString() ?? "unknown",
                    tmlVersion: "1.4.4",
                    mode: mode,
                    tracksAllocations: tracksAlloc,
                    modlistFingerprint: fingerprint,
                    worldId: null);

                // Mirror the active modlist into the modlists/mods collections
                // (upsert via the writer thread; no game-thread DB work).
                EnqueueModlistUpserts(db, fingerprint);
            }
            else
            {
                _recorder = null;
                Mod.Logger.Warn("Persistence unavailable this session (DB failed to open); metric collection continues normally.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            _recorder = null;
            Mod.Logger.Warn($"Session recorder disabled for this world ({ex.GetType().Name}: {ex.Message}); metric collection continues normally.");
        }

        _transitionWatcher = _recorder != null ? new ContextTransitionWatcher() : null;
        _snapshotter = _recorder != null ? new WorldSnapshotter() : null;
        _deathDetector = _recorder != null ? new PlayerDeathDetector() : null;

        _contextTagger = new ContextTagger();
        _contextTagger.Reset();
        Events = new EventAggregator();

        // Segment engine. Lives even when the recorder doesn't — closed
        // segments still flow through the live in-memory ring for the
        // Timeline tab; only the DB write enqueue degrades to a no-op.
        var sid = _recorder?.SessionId ?? LiteDB.ObjectId.Empty;
        SegmentStore = new SegmentStore(PerformanceProfiler.Database, msg => Mod.Logger.Warn(msg));
        Segments = new SegmentDetector(sid, SegmentStore);
        _lastSpikeCount = 0;
        _lastStallCount = 0;
        _wasDeadLastTick = false;

        Mod.Logger.Info($"Profiler armed: {HistoryCapacity}-tick rolling history allocated.");
    }

    /// <summary>
    /// Fires immediately before vanilla world-save begins (and before
    /// <see cref="OnWorldUnload"/>). Starts the session-end aggregation
    /// task NOW so it can run in parallel with vanilla's 1-3s save+backup
    /// chain instead of after it. Per mod-lifecycle dossier §4.7 ε6.
    /// </summary>
    private bool _preSaveEndKickedOff;

    public override void PreSaveAndQuit()
    {
        try
        {
            KickOffSessionEndAsync();
            _preSaveEndKickedOff = true;
        }
        catch (System.Exception ex)
        {
            // Catch defensively: PreSaveAndQuit is not wrapped by tML's
            // SystemLoader catch (verified via mod-lifecycle dossier §3.5).
            // A throw here would abort the user's world save.
            PerformanceProfiler.LoggerOrNull?.Warn($"PreSaveAndQuit threw: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Spawn the background session-end task. Idempotent — if PreSaveAndQuit
    /// already kicked it off, OnWorldUnload sees the latch and skips a
    /// second spawn. Internal so KickOff can be reused by both lifecycle
    /// points without code duplication.
    /// </summary>
    private void KickOffSessionEndAsync()
    {
        MetricCollector? collector = Collector;
        SessionRecorder? recorder = _recorder;
        if (collector == null || recorder == null) return;

        collector.FlushSpikes();

        var sessionId = recorder.SessionId;
        MetricCollector capturedCollector = collector;       // post-null-check local; satisfies the flow analyzer
        SessionRecorder capturedRecorder = recorder;
        var capturedDb = PerformanceProfiler.Database;
        var capturedLogger = PerformanceProfiler.LoggerOrNull;

        _ = Task.Run(() =>
        {
            try
            {
                capturedRecorder.End(capturedCollector, endReason: "clean");
                capturedDb?.DrainAndTruncateJournalForSessionEnd();
                if (capturedDb != null && capturedLogger != null)
                {
                    SessionSummaryLogger.Write(capturedLogger, capturedDb, sessionId);
                }
            }
            catch (Exception ex)
            {
                PerformanceProfiler.LoggerOrNull?.Warn($"Session recorder end failed (background): {ex.GetType().Name}: {ex.Message}");
            }
        });
    }

    /// <summary>Releases the engine at world exit.</summary>
    public override void OnWorldUnload()
    {
        // v0.6.1: PreSaveAndQuit may already have kicked off the
        // session-end task. If so, skip the second spawn — the task is
        // already running. Otherwise (quit via title-screen menu, server
        // disconnect, etc) kick it off now.
        if (!_preSaveEndKickedOff)
        {
            KickOffSessionEndAsync();
        }
        _preSaveEndKickedOff = false;

        // Flush any still-open segments as "ended at world unload" so they
        // make it to the Timeline and DB before we drop the detector.
        if (Segments != null)
        {
            long tickIndex = (long)Main.GameUpdateCount;
            long unixMs = Time.UnixMsNow();
            Segments.CloseAllOnShutdown(tickIndex, unixMs);
        }

        _recorder = null;
        _transitionWatcher = null;
        _snapshotter = null;
        _deathDetector = null;
        Collector = null;
        _contextTagger = null;
        Events = null;
        Segments = null;
        SegmentStore = null;
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
        // Pass the live game tick index so the stall detector can attribute
        // events to real ticks instead of synthetic counters.
        Collector?.BeginTick((long)Main.GameUpdateCount);
    }

    /// <summary>
    /// Tick end. <see cref="ModSystem.PostUpdateEverything"/> is the last hook in
    /// an update; here the game's entity counts and tick index are read and the
    /// frame is committed.
    /// </summary>
    public override void PostUpdateEverything()
    {
        // v0.6: lazy construction. World-load deferred the heavy allocations
        // here to keep the world-enter UI-block under the 16:09-16:14
        // baseline's 172 ms freeze (mod-lifecycle dossier §4.4). The first
        // tick after world-load pays the construction cost (allowed to
        // spike per Invariant 2 budgets) and skips the per-tick path; from
        // tick 2 on, normal flow.
        if (_deferredInitPending)
        {
            _deferredInitPending = false;
            RunDeferredWorldLoadInit();
            return;
        }

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

        // Insights engine evaluation. v0.9.x ran this inline on the game
        // thread every 60 ticks. The 2026-05-21 long-session playtest
        // showed it wedging the main loop badly — one tick attributed
        // 1211 ms to PerformanceProfiler with a real frame of 11 ms,
        // meaning Evaluate held the main thread for over a second.
        //
        // Fix: schedule on the thread pool, gated on the previous run
        // having completed. Detectors are pure-logic reads of smoothed
        // accessors; running off-thread is safe (no per-tick mutation
        // race because we don't touch the collector's mutable buffers,
        // only the IReadOnlyList views).
        if (collector.History.Count > 0 && (collector.History.Count % 60) == 0
            && System.Threading.Interlocked.CompareExchange(ref _insightsEvalInflight, 1, 0) == 0)
        {
            long latestTick = collector.History[collector.History.Count - 1].TickIndex;
            int historyDepth = collector.History.Count;
            var captured = collector;
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var engine = InsightsEngine.GetOrCreateShared();
                    engine.Evaluate(captured, latestTick, historyDepth);
                }
                catch (Exception ex)
                {
                    Mod.Logger.Warn($"InsightsEngine.Evaluate failed ({ex.GetType().Name}: {ex.Message}); engine dropped this pass.");
                }
                finally
                {
                    System.Threading.Interlocked.Exchange(ref _insightsEvalInflight, 0);
                }
            });
        }

        // Recorder feed: per-tick downsampling (1Hz / 1min aggregates) and
        // drain of any new spike/stall windows that arrived during the tick.
        // All work is queue-only — game thread never blocks on disk.
        if (_recorder != null && collector.History.Count > 0)
        {
            TickFrame latest = collector.History[collector.History.Count - 1];
            try
            {
                _recorder.OnTick(latest, collector);
            }
            catch (Exception ex)
            {
                Mod.Logger.Warn($"SessionRecorder.OnTick failed ({ex.GetType().Name}: {ex.Message}); recorder dropped for this world.");
                _recorder = null;
                _transitionWatcher = null;
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

            // Push transitions to the recorder. The watcher diffs against
            // its last snapshot internally and only enqueues when a value
            // actually changed.
            if (_recorder != null && _transitionWatcher != null)
            {
                _transitionWatcher.OnSnapshot(in tagger.Current, frameMs, _recorder);
            }
            // Periodic state snapshots — every 30s of in-world time.
            if (_recorder != null && _snapshotter != null)
            {
                _snapshotter.OnTick(_recorder, in tagger.Current,
                    CountActive(Main.npc), CountActive(Main.projectile), CountActive(Main.dust));
            }
            // Player death edge detection.
            if (_recorder != null && _deathDetector != null)
            {
                _deathDetector.OnTick(_recorder, in tagger.Current);
            }

            // Segment engine — runs against the same EventContext.
            SegmentDetector? segs = Segments;
            if (segs != null)
            {
                long unixMs = Time.UnixMsNow();
                segs.OnTick(tickIndex, unixMs, in tagger.Current, frameMs, collector.PerModCategoryRawMs);

                // Diff spike + stall counts to detect new arrivals this tick.
                int spikesNow = collector.Spikes.Count;
                if (spikesNow > _lastSpikeCount)
                {
                    int delta = spikesNow - _lastSpikeCount;
                    for (int i = 0; i < delta; i++) segs.OnSpike();
                }
                _lastSpikeCount = spikesNow;

                int stallsNow = collector.Stalls.Count;
                if (stallsNow > _lastStallCount)
                {
                    int delta = stallsNow - _lastStallCount;
                    for (int i = 0; i < delta; i++) segs.OnStall();
                }
                _lastStallCount = stallsNow;

                // Local-player death edge. Direct read (cheap) — keeps the
                // segment engine independent of PlayerDeathDetector's lifecycle.
                bool deadNow = Main.LocalPlayer != null && Main.LocalPlayer.dead;
                if (deadNow && !_wasDeadLastTick)
                {
                    segs.OnDeath(tickIndex, unixMs);
                }
                _wasDeadLastTick = deadNow;
            }
        }
    }

    /// <summary>
    /// Pushes the active modlist into the <c>modlists</c> and <c>mods</c>
    /// collections via the writer thread. Called once per world load; the
    /// writer thread is responsible for the upsert semantics (dedupe on
    /// fingerprint / (fingerprint, internalName)).
    /// </summary>
    private static void EnqueueModlistUpserts(ProfilerDatabase db, string fingerprint)
    {
        string[] names = HookInterceptor.ProfiledModNames;
        string[] versions = HookInterceptor.ProfiledModVersions;
        DateTime now = DateTime.UtcNow;

        var modlist = new Persistence.Records.ModlistRow
        {
            Fingerprint = fingerprint,
            FingerprintAlg = ModlistFingerprint.AlgName,
            FirstSeenUtc = now,
            LastSeenUtc = now,
            SessionCount = 1,
        };
        for (int i = 0; i < names.Length; i++)
        {
            modlist.Mods.Add(new Persistence.Records.ModEntry
            {
                ModId = i,
                Name = names[i],
                Version = i < versions.Length ? versions[i] : "unknown",
            });
        }
        db.Writer.Enqueue(DbWriteOp.UpsertModlist(modlist));

        for (int i = 0; i < names.Length; i++)
        {
            db.Writer.Enqueue(DbWriteOp.UpsertMod(new Persistence.Records.ModRow
            {
                ModlistFingerprint = fingerprint,
                InternalName = names[i],
                DisplayName = names[i],
                VersionSeen = i < versions.Length ? versions[i] : "unknown",
                FirstSeenUtc = now,
                LastSeenUtc = now,
                VersionHistory = new System.Collections.Generic.List<Persistence.Records.ModVersionEntry>
                {
                    new Persistence.Records.ModVersionEntry
                    {
                        Version = i < versions.Length ? versions[i] : "unknown",
                        FirstUtc = now,
                        LastUtc = now,
                    }
                },
            }));
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
