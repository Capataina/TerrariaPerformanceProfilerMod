#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ModLoader;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Insights;
using PerformanceProfiler.Insights.ReferenceFrames;
using PerformanceProfiler.Persistence;
using PerformanceProfiler.Data.Aggregators.Segments;

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Persistence.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Data.Contracts;
using PerformanceProfiler.Insights.Shared;
using PerformanceProfiler.Insights.CrossSession;
using PerformanceProfiler.Persistence.History;
using PerformanceProfiler.Persistence.Lifecycle;
using PerformanceProfiler.Persistence.Records;
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
    /// <summary>Rolling-history length fallback: 30 seconds at Terraria's 60 ticks per second. The live value comes from ProfilerConfig.FrameHistoryTicks at world-arm.</summary>
    private const int HistoryCapacity = 30 * 60;

    /// <summary>
    /// Insights-evaluation stride in ticks, cached from
    /// <see cref="ProfilerConfig.DetectorCadenceTicks"/> at world-arm and on
    /// OnChanged — the per-tick modulo check reads this field, never the
    /// config instance (S23 read discipline).
    /// </summary>
    private int _insightsCadenceTicks = 60;

    /// <summary>
    /// Push the runtime-safe config values to the live systems. Called from
    /// <see cref="ProfilerConfig.OnChanged"/> (any thread tML calls it on —
    /// the touched members are an int field write and detector property
    /// writes, all tolerant of a mid-tick apply) and from world-arm.
    /// </summary>
    internal void ApplyRuntimeConfig(ProfilerConfig cfg)
    {
        _insightsCadenceTicks = cfg.DetectorCadenceTicks is >= 30 and <= 600 ? cfg.DetectorCadenceTicks : 60;
        Collector?.ConfigureDetectorSensitivity(cfg.SpikeSensitivity, cfg.StallSensitivity);
    }

    /// <summary>
    /// The per-tick measuring engine, live only while a world is loaded.
    /// Null between worlds.
    ///
    /// <para>
    /// v0.10 made this <c>internal</c> as part of the unified data pipeline
    /// commitment: external consumers (router, UI, exporters) read state
    /// through <see cref="Data.DataRegistry.Shared"/> by stream name, not
    /// by reaching into the named field. Same-assembly consumers that
    /// still need direct access (collector adapters, the per-tick
    /// callback driver) are inside <c>Data/</c> or <c>Profiling/</c>,
    /// where internal access is fine.
    /// </para>
    /// </summary>
    internal MetricCollector? Collector { get; private set; }

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
    public SessionRecorder? LiveRecorder => _recorder;

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

        // v0.12 F1 — install-time per-mod content roster scan. Walks every
        // content loader once and produces ModRosterSnapshot. Runs here so
        // every mod's content is registered + BiomeRegistry has its owner
        // map before we walk it. The scanner caches its result; later
        // Lookup calls from the dashboard read the cached snapshot.
        try
        {
            var roster = Data.DataRegistry.Shared.Lookup<Data.Contracts.ModRosterSnapshot>(
                Data.Contracts.RolloutStreamNames.ModRoster) as Data.Collectors.ModRosterScanner;
            roster?.Scan();
        }
        catch (Exception ex)
        {
            Mod.Logger.Warn($"ModRosterScanner.Scan failed: {ex.GetType().Name}: {ex.Message}");
        }
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
        // World-arm config snapshot (atlas S23): read once here, never on the
        // hot path. FrameHistoryTicks resizes the rolling window (RAM scales
        // with hooks × window); detector sensitivities apply immediately and
        // again on any OnChanged; the insights stride caches into a field the
        // per-tick check reads.
        ProfilerConfig? cfg = Terraria.ModLoader.ModContent.GetInstance<ProfilerConfig>();
        int historyCapacity = cfg?.FrameHistoryTicks is > 0 and int ticks ? ticks : HistoryCapacity;
        _insightsCadenceTicks = cfg?.DetectorCadenceTicks is >= 30 and <= 600 and int cad ? cad : 60;

        // Inject the process-singleton self-health so install-delta measurements
        // captured at PostSetupContent survive across world loads. The
        // collector handles per-tick refresh; install-time state stays put.
        Collector = new MetricCollector(historyCapacity, SelfHealth);
        if (cfg != null)
        {
            Collector.ConfigureDetectorSensitivity(cfg.SpikeSensitivity, cfg.StallSensitivity);
        }

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
                    // The mod's build.txt version (Mod.Version), NOT the assembly version —
                    // the .csproj never stamps AssemblyVersion, so the assembly reads 0.0.0.0,
                    // which made every session record "0.0.0.0" and left the roadmap-F1 version
                    // -boundary / regression detection with nothing to detect.
                    profilerVersion: Mod.Version?.ToString() ?? "unknown",
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

        // Wave 6: seed the insights engine's context baseline with this stack's prior
        // sessions, so cross-session confidence and the LifetimeData badge are
        // truthful from the first detection pass. Guarded end-to-end: any failure
        // leaves a fresh (unseeded) baseline — a degraded feature, never a crash
        // (Invariant 4: persistence is an agent surface, not a gameplay dependency).
        try
        {
            ProfilerDatabase? cbDb = PerformanceProfiler.Database;
            if (cbDb != null && PerModAttribution.ModCount > 0)
            {
                ContextBaseline seeded = CrossSessionStore.Load(
                    cbDb.ContextBaselines, ModlistFingerprint.Compute(), PerModAttribution.ModCount);
                InsightsEngine.GetOrCreateShared().SeedContextBaseline(seeded);
            }
        }
        catch (Exception ex)
        {
            Mod.Logger.Warn($"Cross-session baseline seed skipped ({ex.GetType().Name}: {ex.Message}); fresh baseline this session.");
        }

        // DB rework wave 4: compute this stack's cross-session insights (LifetimeData) over
        // the persisted rollup — the player's PRIOR sessions — on a background task, so the
        // "across your last N sessions" feed populates without blocking world load. Guarded;
        // a failure just leaves the cross-session feed empty this session (Invariant 4).
        try
        {
            ProfilerDatabase? histDb = PerformanceProfiler.Database;
            string[] roster = HookInterceptor.ProfiledModNames;
            string fp = ModlistFingerprint.Compute();
            // S23 gate: CrossSessionInsights off ⇒ the lifetime feed stays
            // empty this session; live (this-session) insights are unaffected.
            bool crossEnabled = Terraria.ModLoader.ModContent.GetInstance<ProfilerConfig>()?.CrossSessionInsights ?? true;
            if (histDb != null && roster.Length > 0 && crossEnabled)
            {
                System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        var store = new HistoryStore(histDb);
                        List<Insight> cross = CrossSessionEvaluator.Run(store, roster, fp);
                        InsightsEngine.Shared?.SetCrossSessionInsights(cross);
                        PerformanceProfiler.LoggerOrNull?.Info($"Cross-session insights: {cross.Count} lifetime findings over the current stack.");

                        // Modlist-change detection (wave 3) — agent surface here (client.log);
                        // the player surface is /api/data-health's modlistChanged fields.
                        var recent = store.RecentSessions(1);
                        if (recent.Count > 0)
                        {
                            // C1 fix: hoist the value out of the LiteDB predicate. An
                            // indexer+member access (recent[0].Fingerprint) INSIDE the
                            // Expression makes LiteDB's LINQ-to-BsonExpression translator
                            // treat recent[0] as a document path and invoke the Fingerprint
                            // getter reflectively on the wrong instance, throwing
                            // "TargetException: Object does not match target type" — the
                            // crash that killed cross-session eval on its first live run.
                            // A plain captured string translates to a clean constant.
                            string prevFingerprint = recent[0].Fingerprint;
                            ModlistRow? prevList = histDb.Modlists.FindOne(x => x.Fingerprint == prevFingerprint);
                            if (prevList != null)
                            {
                                var prevNames = new List<string>(prevList.Mods.Count);
                                foreach (ModEntry me in prevList.Mods) prevNames.Add(me.Name);
                                ModlistChange change = ModlistChange.Diff(roster, prevNames);
                                if (change.Changed)
                                    PerformanceProfiler.LoggerOrNull?.Info(
                                        $"Modlist changed since last session: +{change.Added.Count} / -{change.Removed.Count}" +
                                        $"  added=[{string.Join(", ", change.Added)}]  removed=[{string.Join(", ", change.Removed)}]");
                            }
                        }
                    }
                    catch (Exception ex) { PerformanceProfiler.LoggerOrNull?.Warn($"Cross-session insight eval failed: {ex.GetType().Name}: {ex.Message}"); }
                });
            }
        }
        catch (Exception ex) { Mod.Logger.Warn($"Cross-session insight eval skipped: {ex.GetType().Name}: {ex.Message}"); }

        _wasDeadLastTick = false;

        // v0.9.x data pipeline — initialise every registered IDataStream
        // with the per-session context. The registry's Freeze() captures
        // the per-tick callbacks into a frozen array driven from
        // PostUpdateEverything. No streams are registered yet at this
        // step; subsequent commits migrate them one at a time.
        var pipelineSession = new Data.SessionContext
        {
            SessionId = _recorder?.SessionId ?? LiteDB.ObjectId.Empty,
            ModlistFingerprint = ModlistFingerprint.Compute(),
            TracksAllocations = PerModAttribution.TracksAllocations,
            HookBackendMode = HookBackend.Mode.ToString(),
            Database = PerformanceProfiler.Database,
        };
        Data.DataRegistry.Shared.InitialiseAll(pipelineSession);

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
        // Wave 6: capture the engine's lifetime baseline + this stack's fingerprint on
        // the game thread (the modlist is stable here) so the off-thread save below is
        // self-contained and survives the InsightsEngine.Shared = null at unload.
        ContextBaseline? capturedBaseline = InsightsEngine.Shared?.ContextBaseline;
        string capturedFingerprint = ModlistFingerprint.Compute();
        // Dual-surface observability: render the engine's top insights on the game
        // thread (where mod names resolve) so the off-thread summary log carries the
        // same interpretation the dashboard shows. Guarded — a render failure just
        // drops the insights line from the log.
        string[]? capturedInsights = null;
        try
        {
            InsightsEngine? engine = InsightsEngine.Shared;
            if (engine != null)
            {
                var top = engine.Store.Top(8, (long)Main.GameUpdateCount);
                if (top.Count > 0)
                {
                    capturedInsights = new string[top.Count];
                    for (int i = 0; i < top.Count; i++)
                        capturedInsights[i] = InsightRenderer.Render(top[i], Audience.Both, Density.Short);
                }
            }
        }
        catch { capturedInsights = null; }

        // DB rework wave 4 — the insight producer: render this session's top insights
        // (live + cross-session) to InsightRows on the game thread (where mod names
        // resolve), captured for the off-thread enqueue below. Closes the long-orphaned
        // `insights` collection (a writer with no producer), so confidence can accrue
        // across sessions and the persisted feed exists.
        List<InsightRow>? capturedInsightRows = null;
        try
        {
            InsightsEngine? engine = InsightsEngine.Shared;
            if (engine != null)
            {
                var rows = new List<InsightRow>();
                // Mod names resolve on the game thread; resolve every contributor's session-local
                // ModId to its name HERE, so the persisted row is self-contained (a stored ModId
                // would mis-name the mod on the next launch when load order shifts).
                string[] insightModNames = HookInterceptor.ProfiledModNames;
                List<InsightContributorRow> ResolveContribs(Insight rec)
                {
                    var list = new List<InsightContributorRow>();
                    if (rec.Contributors != null)
                    {
                        foreach (InsightContributor c in rec.Contributors)
                        {
                            int id = c.Subject.ModId;
                            string nm = (id >= 0 && id < insightModNames.Length) ? insightModNames[id]
                                      : (id >= 0 ? "mod " + id : "session");
                            list.Add(new InsightContributorRow { ModName = nm, Value = c.Value, Share = c.Share });
                        }
                    }
                    return list;
                }
                void AddRows(IEnumerable<Insight> src)
                {
                    foreach (Insight rec in src)
                    {
                        rows.Add(new InsightRow
                        {
                            SessionId = sessionId,
                            PatternKey = rec.Pattern.ToString(),
                            Audience = rec.Audience.ToString(),
                            RenderedShort = InsightRenderer.Render(rec, Audience.Both, Density.Short),
                            RenderedLong = InsightRenderer.Render(rec, Audience.Modder, Density.Long),
                            Confidence = rec.Confidence.ToString(),
                            EvidenceScope = rec.Scope.ToString(),
                            PValueAdjusted = rec.Evidence.PValueAdjusted,
                            FirstSeenTick = rec.FirstSeenTick,
                            LastConfirmedTick = rec.LastSeenTick,
                            // Roster context for aggregate patterns (LoadedCount>0); 0/empty otherwise.
                            LoadedModCount = rec.Magnitude.LoadedCount,
                            ActiveModCount = rec.Magnitude.LoadedCount > 0 ? rec.Evidence.SampleN : 0,
                            Contributors = ResolveContribs(rec),
                        });
                    }
                }
                AddRows(engine.Store.Top(8, (long)Main.GameUpdateCount));
                AddRows(engine.CrossSessionInsights);
                if (rows.Count > 0) capturedInsightRows = rows;
            }
        }
        catch { capturedInsightRows = null; }

        // Capture per-mod engagement weights on the game thread (wave 1). The live usage
        // snapshot is cleared by OnWorldUnload's DataRegistry.ResetAll, which can race the
        // background End below, so the rollup's engagement axis must be read here, indexed
        // by ModId. Guarded — a failure just folds 0 engagement this session.
        double[]? capturedEngagement = null;
        try
        {
            ModUsageSnapshot usage = Data.DataRegistry.Shared
                .Lookup<ModUsageSnapshot>(RolloutStreamNames.PerModUsage)?.CurrentSnapshot()
                ?? ModUsageSnapshot.Empty;
            if (usage.Entries.Count > 0)
            {
                int modCount = HookInterceptor.ProfiledModNames.Length;
                capturedEngagement = new double[modCount];
                for (int i = 0; i < usage.Entries.Count; i++)
                {
                    ModUsageEntry u = usage.Entries[i];
                    if ((uint)u.ModId < (uint)modCount) capturedEngagement[u.ModId] = ModMetrics.UsageWeight(u);
                }
            }
        }
        catch { capturedEngagement = null; }

        _ = Task.Run(() =>
        {
            try
            {
                capturedRecorder.End(capturedCollector, endReason: "clean", capturedEngagement);
                capturedDb?.DrainAndTruncateJournalForSessionEnd();
                if (capturedDb != null && capturedLogger != null)
                {
                    SessionSummaryLogger.Write(capturedLogger, capturedDb, sessionId, capturedInsights);
                }
                // Persist the per-context baselines for this stack (prior + this
                // session). Independently guarded: a failure here must not abort the
                // recorder-end work above (Invariant 4).
                // Persist this session's top insights to the `insights` collection (wave 4).
                if (capturedDb != null && capturedInsightRows != null)
                {
                    try
                    {
                        foreach (InsightRow row in capturedInsightRows)
                            capturedDb.Writer.Enqueue(DbWriteOp.Insight(row));
                    }
                    catch (Exception ex) { PerformanceProfiler.LoggerOrNull?.Warn($"Insight producer enqueue failed: {ex.GetType().Name}: {ex.Message}"); }
                }
                if (capturedDb != null && capturedBaseline != null)
                {
                    try { CrossSessionStore.Save(capturedDb.ContextBaselines, capturedFingerprint, capturedBaseline); }
                    catch (Exception ex) { PerformanceProfiler.LoggerOrNull?.Warn($"Cross-session baseline save failed: {ex.GetType().Name}: {ex.Message}"); }
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

        // v0.9.x data pipeline — discard every registered IDataStream's
        // per-session state. Registered streams stay registered (we'll
        // re-Initialise them on the next world load); only their internal
        // buffers are cleared.
        Data.DataRegistry.Shared.ResetAll();

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
    /// Draw beat. <see cref="ModSystem.PostDrawInterface"/> fires once per
    /// RENDERED frame — the beat frameskip drops when the game runs behind —
    /// which makes it the render-fps counterpart to the update-cadence hooks
    /// above. Feeds the collector's draw-period EMA (render fps on the KPI
    /// strip). Generic tModLoader surface; observes only, draws nothing.
    /// </summary>
    public override void PostDrawInterface(SpriteBatch spriteBatch)
    {
        Collector?.OnDrawFrame();
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
        if (collector.History.Count > 0 && (collector.History.Count % _insightsCadenceTicks) == 0
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
            // Real cadence: per-context cost accumulation ("Forest costs X ms/t")
            // is a player-facing number (2026-07-07 honesty pass).
            double frameMs = collector.History[collector.History.Count - 1].RealFrameTimeMs;
            events.Accumulate(in tagger.Current, frameMs);

            // v0.9.x data pipeline — drive every PerTick stream's capture
            // callback in a single tight loop. The TickContext is stack-
            // allocated; the callbacks come from a frozen array; the
            // dispatch is a static delegate invocation per callback. Empty
            // array today, populated as we migrate stages in later steps.
            var pipelineCbs = Data.DataRegistry.Shared.PerTickCallbacks;
            if (pipelineCbs.Length > 0)
            {
                var latestFrame = collector.History[collector.History.Count - 1];
                var pipelineCtx = new Data.TickContext(
                    tickIndex, Time.UnixMsNow(),
                    latestFrame.RealFrameTimeMs, latestFrame.GcTimeMs,
                    latestFrame.NpcCount, latestFrame.ProjectileCount, latestFrame.DustCount,
                    in tagger.Current);
                for (int i = 0; i < pipelineCbs.Length; i++)
                {
                    pipelineCbs[i](in pipelineCtx);
                }
            }

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
                segs.OnTick(tickIndex, unixMs, in tagger.Current, frameMs, collector.PerModCategoryRawMsArray);

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
