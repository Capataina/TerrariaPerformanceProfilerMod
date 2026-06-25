/* ============================================================
   data.js - generated skeleton from upkeep-context arch_seed.py
   schema: v1
   Edit agent-owned sections per references/arch-fill-checklist.md.
   Re-runs of upkeep-context preserve agent prose via arch_merge.py.
   ============================================================ */
window.ARCH = JSON.parse(`{
  "_meta": {
    "deleted_node_ids": [
      "bin_Debug",
      "bin_Release",
      "obj",
      "design",
      "Localization"
    ],
    "deleted_edge_keys": [],
    "frontier_locked": false,
    "repotree_locked": true
  },
  "schema": "v1",
  "project": {
    "name": "PerformanceProfiler",
    "file": "context/architecture.html",
    "head": "2aa9e1c",
    "headRange": "2aa9e1c..2aa9e1c",
    "regenerated": "2026-06-25",
    "stack": "C# · .NET 8 · tModLoader 1.4.4 · MonoMod · LiteDB",
    "milestone": "v0.22.0 — six live dashboard tabs + reference-frame insights",
    "tests": "~70 xUnit L1 pure-logic tests (sub-second) + L4/L6/L8 Playwright dashboard audit",
    "frameBudget": "16.6 ms (60 fps); Lite < 1% (~0.12 ms/tick)",
    "commits": 71,
    "lines": 418698,
    "tagline": "A read-only tModLoader 1.4.4 client mod (C#/.NET 8) that attributes per-tick CPU, allocation, and RAM cost to individual mods in the player's modlist, correlates that cost with what the player was doing (biome, boss, weather, loadout, deaths), and surfaces it through a local browser dashboard plus a queryable LiteDB store. The architectural spine is a unified Data/ pipeline: every number lives in one named, typed stream looked up by stable name from DataRegistry.Shared, so two consuming surfaces (the HTTP dashboard and the LiteDB writer thread) only ever read immutable snapshots. Instrumentation is dual-backend (a MonoMod delegate-pair path and a default IL-injection path at ~100% coverage) and abort-clean: it disables and reports rather than ever corrupting a run.",
    "purpose": "Orient a new engineer to the runtime layers, ownership boundaries, dependency direction, and the per-tick hook-timing flow that feeds everything downstream. This is the map, not the territory: per-subsystem deep dives live in context/systems/*.md, the cross-component plug-in detail in context/integration/integration-map.md, and the per-API tModLoader surface in context/tmodloader/*.md.",
    "techStack": [
      {
        "name": "C#",
        "meta": "language · .NET 8"
      },
      {
        "name": ".NET",
        "meta": "8.0 · pinned by tModLoader 1.4.4"
      },
      {
        "name": "tModLoader",
        "meta": "1.4.4 · mod host"
      },
      {
        "name": "MonoMod.RuntimeDetour",
        "meta": "25.3.2 · ILHook / delegate detours"
      },
      {
        "name": "LiteDB",
        "meta": "5.0.21 · single-file embedded DB"
      },
      {
        "name": "System.Net.Sockets",
        "meta": "raw-TCP loopback HTTP/1.1 server"
      },
      {
        "name": "xUnit",
        "meta": "2.9 · L1 pure-logic harness"
      },
      {
        "name": "Playwright + Python",
        "meta": "L4/L6/L8 dashboard audit"
      }
    ]
  },
  "nodes": [
    {
      "id": "Data",
      "label": "Data",
      "kind": "foundation",
      "layer": 0,
      "root": "Data/",
      "tagline": "THE pipeline — every number the mod produces flows through one named, typed stream registered in DataRegistry.Shared.",
      "owns": "The Data/ tree: DataRegistry (the .Shared singleton + Freeze), the stream contracts (IDataStream, DataStage, the marker interfaces), TickContext/SessionContext, the frozen Data/Contracts/RolloutContracts.cs snapshot types, and every concrete Collector / Aggregator / Stat / Detector / Stream. It owns calculation: routers and exporters format snapshots, they never derive numbers. It does NOT own the hot-path engine (Profiling/MetricCollector + the hook backends) or the LiteDB database itself; it adapts over the former and writes through the latter.",
      "files": [
        "DataRegistry.cs - Process-wide stream registry (.Shared singleton); Freeze() snapshots per-tick callbacks",
        "DataStage.cs - Stage enum: Collector | Aggregator | Stat | Detector | Stream | Exporter",
        "IDataStream.cs - Base + typed stream contracts + per-tick marker interfaces",
        "SessionContext.cs - Immutable per-session record passed to each stream's Initialise",
        "TickContext.cs - Readonly ref struct passed to frozen per-tick callbacks (zero-alloc)"
      ],
      "state": [
        "DataRegistry.Shared (static singleton, registered at Mod.Load)",
        "PerTickCallbacks (frozen immutable array, snapshot at Freeze)",
        "PerModAttribution accumulator [(modId,categoryId,hookId)] (hot path)",
        "PerTickAttributionRing (50-window per-tick per-mod samples)",
        "SegmentStore ring + SegmentDetector open segments",
        "every stream's per-session buffers (InitialiseAll/ResetAll)"
      ]
    },
    {
      "id": "Insights",
      "label": "Insights",
      "kind": "observer",
      "layer": 1,
      "root": "Insights/",
      "tagline": "The interpretation layer over the pipeline: a roster of 16 statistically-guarded detectors that report deviations from a reference frame, not absolute magnitudes.",
      "owns": "The detector roster (13 live, 3 gated across five families), the per-context (ContextBaseline, Family A) and early/late (TemporalBaseline, Family B) reference frames, the entity/age/heap drivers, the live+history InsightStore (TTL eviction, p-value-gated confidence promotion, pattern-aware ranking), the cross-session baseline persistence keyed by modlist fingerprint, the slot-filling banned-vocabulary renderer, and the seven Publish/ stats that compose the dashboard Insights tab. It reads smoothed/aggregated pipeline outputs (never collector internals) and writes back via DataRegistry + an in-memory feed. It does NOT own the metric data, the LiteDB plumbing, or the dashboard UI.",
      "files": [
        "CollectorInsightInput.cs - Adapts MetricCollector to IInsightInput (the pure-logic testability seam)",
        "IInsightDetector.cs - Detector interface: Pattern / IsAvailable / IsGated / GatedOn / Evaluate",
        "Insight.cs - Insight record + all enums (PatternKey, Confidence, EvidenceScope, Magnitude)",
        "InsightRenderer.cs - Slot-filling templates; banned-vocabulary header enforces the honesty contract",
        "InsightStore.cs - Live/history store: dedup, TTL eviction, confidence promotion, ranking",
        "InsightsEngine.cs - Detector roster + Evaluate pass + reference-frame substrate + Shared singleton",
        "RankingScorer.cs - Stateless 6-component weighted score (share/ratio regime split)"
      ],
      "state": [
        "InsightsEngine.Shared (Volatile static, per-session, cleared on world unload)",
        "InsightStore live dict (LiveCap=32) + history (TTL ~5 min)",
        "ContextBaseline (16-bucket bounded, per-context per-mod RunningStat)",
        "TemporalBaseline (frozen early window + late window)",
        "static _heapDriver / _entityDriver",
        "contextBaselines LiteDB rows (via CrossSessionStore, fingerprint-keyed)"
      ]
    },
    {
      "id": "Profiling",
      "label": "Profiling",
      "kind": "foundation",
      "layer": 3,
      "root": "Profiling/",
      "tagline": "The measurement engine: two hook backends, the per-tick frame collector, the probe stack, the Events context structs, and the LiteDB persistence infrastructure.",
      "owns": "The hook install/teardown (delegate-pair + IL backends), HookCategoryRouter, ProbeStack, MetricCollector + RingBuffer + TickFrame, the ProfilerSystem ModSystem lifecycle driver, ProfilerSelfHealth, the Profiling/Events/ context support structs, and Profiling/Persistence/ (the LiteDB facade, single writer thread, journal, migrations, and side-channel event detectors). It produces the raw signal; it does NOT own the stream-shaped artefacts (those moved to Data/ in v0.11) nor any number derivation.",
      "files": [
        "EnumStringTable.cs - Pre-built enum-to-string arrays; kills per-render boxing/alloc",
        "HookBackend.cs - Mode flags (Delegate/ILHook/Parallel) + AllocationTracking switch",
        "HookCategoryRouter.cs - Shared type-to-category map (seven ids); both backends call ResolveCategory",
        "HookInterceptor.cs - Delegate-pair backend: MonoModHooks.Add per matched signature (~71.6%)",
        "HookSurfaceCache.cs - Process-scoped GetLoadableTypes cache shared by both backends",
        "ILHookInterceptor.cs - IL backend (default ~100%): per-method ILHook + ProbeStack timing wrap",
        "LangNameCache.cs - Pre-resolves Lang names into flat string[]; one indexer read per event",
        "MetricCollector.cs - Per-tick frame engine: BeginTick/EndTick, ring buffer, spike detector"
      ],
      "state": [
        "ProfiledMods / ProfiledModNames / ProfiledModVersions (static, HookInterceptor)",
        "_installedHooks + _instrumentedHandles (process-scoped, ILHookInterceptor)",
        "Collector / RingBuffer<TickFrame>[1800] (per-world, ProfilerSystem)",
        "PerformanceProfiler.Database (LiteDB, static)",
        "DbWriterThread queue + EventJournal",
        "ProfilerSelfHealth install-delta (process singleton)"
      ]
    },
    {
      "id": "Tests",
      "label": "Tests",
      "kind": "observer",
      "layer": 4,
      "root": "Tests/",
      "tagline": "The non-shipping xUnit L1 harness: pure-logic regressions pinned on synthetic input, with zero tModLoader or game-runtime dependency.",
      "owns": "The xUnit project, the Compile-Include+Link mechanism that lifts pure-logic source files in without dragging tModLoader assemblies into the runner, and the build-time exclusion from the .tmod (build.txt buildIgnore + the main csproj Compile Remove). It pins ranking, confidence promotion, the Insights/Shared primitives, the reference-frame substrate, ring-buffer wrap-around, stall classification, the object pools, and the LiteDB persistence round-trip. It does NOT own production source (reached via Link) or any game-runtime test (those are the manual in-game L7 cycle).",
      "files": [
        "BaselineTests.cs - Pins the per-session rolling baseline behind the relative spike threshold",
        "BoolIndexTests.cs - Pins the BoolIndex bitset set-membership helper",
        "HookInstallRetentionDiagnostics.cs - Diagnostic: self-health install-RAM conflates retained vs transient garbage",
        "InsightStoreTests.cs - Pins p-value-gated confidence promotion + Submit dedup",
        "PerformanceProfiler.Tests.csproj - xUnit project; Compile-Include+Link lifts pure-logic source, no ProjectReference",
        "PoolsTests.cs - Pins RowPool/ListPool — the per-tick zero-alloc contract",
        "RankingScorerTests.cs - Pins the share-vs-ratio magnitude split (90% now outranks 40%)",
        "RingBufferTests.cs - Pins ring-buffer wrap-around (the 30s history + 50-window spike ring)"
      ],
      "state": [
        "the Link'd source set (no copies)",
        "Tests/bin + Tests/obj (gitignored build output)"
      ]
    },
    {
      "id": "UI",
      "label": "UI",
      "kind": "observer",
      "layer": 5,
      "root": "UI/",
      "tagline": "The ARCHIVED in-game overlay — five sprite-font tabs kept on disk for a possible Steam-Deck revival, not compiled into the player path since v0.9.0.",
      "owns": "The in-game tab framework (IOverlayTab, TabRegistry, OverlayPanel, the seven Tabs/, the Components/ draw widgets) and ProfilerTheme. As of v0.9.0 the only live class is ProfilerOverlaySystem, which now owns nothing but the F9 'OpenDashboard' keybind registration. It does NOT participate in the active player surface — the browser dashboard (Web/) superseded it.",
      "files": [
        "ProfilerOverlay.cs - Archived in-game overlay root draw (not in the player path)",
        "ProfilerOverlaySystem.cs - Live only as the F9 OpenDashboard keybind registrar (rest archived)",
        "ProfilerTheme.cs - Archived overlay colour/font theme constants"
      ],
      "state": [
        "DashboardKeybind (ModKeybind, the one live binding)",
        "TabRegistry.Tabs (archived, not instantiated)",
        "OverlayState (archived MetricMode/active-tab)"
      ]
    },
    {
      "id": "Web",
      "label": "Web",
      "kind": "boundary",
      "layer": 6,
      "root": "Web/",
      "tagline": "The live player surface: a raw-TCP loopback HTTP/1.1 server serving a single-page app over ~29 read-only /api/* JSON endpoints.",
      "owns": "The loopback TCP listener (127.0.0.1:27277, port search to 27287), the accept loop + thread-per-request fan-out, the strict GET-only route allowlist, the ~29 Build* endpoint formatters, the SPA asset bundle (HTML shell + 21 CSS + 18 JS fragments incl. the shared component library and chart module), byte-cached once at type-init, and the cross-platform browser launch. It owns formatting snapshots into wire shapes; it does NOT own any number it displays (those live in Data/), nor snapshot production, nor any write path into game state (Invariant 1).",
      "files": [
        "DashboardRouter.Hooks.cs - BuildHooks: per-hook drill-down rows; zero-cost rows skipped",
        "DashboardRouter.Insights.cs - BuildInsights + the five Publish-backed Insights endpoints",
        "DashboardRouter.Lag.cs - Lag builders: spikes, stalls, clusters, GC pressure, density, causality, rhythm",
        "DashboardRouter.Memory.cs - BuildMemory: joins install-delta scaffolding with tML's per-mod RAM",
        "DashboardRouter.Mods.cs - BuildMods: per-mod CPU+alloc table rows",
        "DashboardRouter.Self.cs - BuildSelf: profiler self-health (overhead, footprint, hook counts)",
        "DashboardRouter.Summary.cs - Summary builders: now, frames, segments, heatmap, events",
        "DashboardRouter.Timeline.cs - Timeline builders: lifetime, attribution, transitions, attendance, deaths, chronicle"
      ],
      "state": [
        "DashboardHttpServer (static singleton, bound at Mod.Load)",
        "CachedCssBytes / CachedJsBytes (UTF-8, cached at type-init)",
        "the strict Route() switch (34 arms)",
        "browser-side lastNow/lastFrames/lastSegments/lastXxx poll caches (client state, not mod state)"
      ]
    },
    {
      "id": "lib",
      "label": "lib",
      "kind": "foundation",
      "layer": 10,
      "root": "lib/",
      "tagline": "Vendored LiteDB 5.0.21 — the single managed DLL the persistence layer ships inside the .tmod.",
      "owns": "LiteDB.dll (MIT, ~510 KB, fully managed), referenced via build.txt dllReferences = LiteDB and the test project's direct Reference HintPath so the persistence tests exercise the exact shipped assembly. It is the embedded DB engine; it does NOT own any profiler logic.",
      "files": [
        "LiteDB.dll - Vendored LiteDB 5.0.21 (MIT, single managed DLL) packed in the .tmod"
      ],
      "state": [
        "LiteDB.dll (vendored 5.0.21)"
      ]
    },
    {
      "id": "tools_preview",
      "label": "preview",
      "kind": "observer",
      "layer": 12,
      "root": "tools/preview/",
      "tagline": "The offline source-to-HTML dashboard render: regenerates the dashboard against fixtures so layout/colour/sort can be checked without a running game.",
      "owns": "render.py + build_preview_html.py and the committed fixtures/ feeding both the preview and the L4/L6/L8 audit harness. The regenerated preview reflects current .cs source (the running tModLoader locks the .tmod, so in-game render lags a Build+Reload). It is buildIgnore'd. It does NOT prove interactive states (hover/selection/scroll-extremes) — that is the L4 harness's job.",
      "files": [
        "README.md - How the offline source-to-HTML preview render works",
        "build_preview_html.py - Builds a self-contained dashboard HTML from current source + fixtures",
        "render.py - Regenerates the dashboard render against fixtures (static layout/colour/sort)"
      ],
      "state": [
        "fixtures/*.json (the captured-session contract, shared with tools_testing)",
        "the regenerated preview HTML (reflects current source)"
      ]
    },
    {
      "id": "tools_testing",
      "label": "testing",
      "kind": "observer",
      "layer": 13,
      "root": "tools/testing/",
      "tagline": "The self-describing L4/L6/L8 Playwright+Python dashboard audit harness — drives the real browser page off-game and DOM-discovers every tab so it scales without harness edits.",
      "owns": "The audit CLI (doctor/contract/gen/assert/capture/synthesize), the L6 generative fixtures + contract-drift report, the L4 deterministic layout invariants, the L8 clean-slate screenshot sweep + agent fan-out, and the per-page dossiers it writes into context/pages/. It is buildIgnore'd from the .tmod. It does NOT touch the game runtime or the .cs build — it proves the dashboard's layout, interaction, and visual quality only.",
      "files": [
        "README.md - How the self-describing L4/L6/L8 audit harness works + setup",
        "audit.py - Audit CLI: doctor / contract / gen / assert / capture / synthesize",
        "design-bar.md - L8 visual-quality bar + chart vocabulary read by every review agent",
        "requirements.txt - Playwright dependency pin for the audit harness",
        "rubric.md - L8 shared audit checklist read by every review agent"
      ],
      "state": [
        "the committed fixture contract (tools/preview/fixtures/*.json, 29 files)",
        "context/pages/ dossiers (audit owns Findings, human owns Notes)",
        ".venv + Playwright browser (gitignored, recreated)"
      ]
    }
  ],
  "edges": [
    {
      "from": "Profiling",
      "to": "Data",
      "rel": "strong",
      "label": "MetricCollector / PerModAttribution feed Collectors + Stats; per-tick callbacks driven by ProfilerSystem"
    },
    {
      "from": "Data",
      "to": "Web",
      "rel": "strong",
      "label": "DashboardRouter.Build* pulls Lookup<TSnapshot>(name).CurrentSnapshot() (race-free immutable)"
    },
    {
      "from": "Data",
      "to": "Insights",
      "rel": "dep",
      "label": "detectors read smoothed pipeline snapshots + foundation streams (roster F1, usage F2, cost F3)"
    },
    {
      "from": "Insights",
      "to": "Data",
      "rel": "write",
      "label": "seven Publish/ stats register back into DataRegistry; CrossCuttingSignal reads InsightsEngine.Shared"
    },
    {
      "from": "Insights",
      "to": "Web",
      "rel": "dep",
      "label": "/api/insights + five Publish endpoints serialise the live insight feed + composite stats"
    },
    {
      "from": "Data",
      "to": "Profiling",
      "rel": "write",
      "label": "Data/Streams/* enqueue DbWriteOps; DbWriterThread drains to ProfilerDatabase (LiteDB)"
    },
    {
      "from": "Profiling",
      "to": "lib",
      "rel": "dep",
      "label": "ProfilerDatabase wraps LiteDatabase from the vendored LiteDB.dll"
    },
    {
      "from": "Insights",
      "to": "Profiling",
      "rel": "dep",
      "label": "CrossSessionStore persists/seeds context baselines to the LiteDB contextBaselines collection"
    },
    {
      "from": "tools_testing",
      "to": "Web",
      "rel": "peer",
      "label": "Playwright drives the real dashboard page off-game; DOM-discovered, no source coupling"
    },
    {
      "from": "tools_preview",
      "to": "Web",
      "rel": "peer",
      "label": "renders the SPA against fixtures offline; reflects current .cs source"
    },
    {
      "from": "Tests",
      "to": "Data",
      "rel": "peer",
      "label": "Compile-Include+Link lifts pure-logic Data/ sources (PerModAttribution, Baseline, StallDetector, Streams, Contracts)"
    },
    {
      "from": "Tests",
      "to": "Insights",
      "rel": "peer",
      "label": "Link lifts Insight/InsightStore/RankingScorer/Shared/ReferenceFrames/Drivers for L1 pins"
    },
    {
      "from": "Tests",
      "to": "Profiling",
      "rel": "peer",
      "label": "Link lifts RingBuffer/TickFrame/Time/Pools/Events + the LiteDB-only Persistence sources"
    },
    {
      "from": "UI",
      "to": "Web",
      "rel": "dep",
      "label": "ProfilerOverlaySystem registers the F9 keybind that opens the dashboard (only live UI role)"
    }
  ],
  "kindMeta": {
    "entry": {
      "label": "Entry point",
      "swatch": "neutral"
    },
    "foundation": {
      "label": "Foundation",
      "swatch": "slate"
    },
    "env": {
      "label": "Environment truth",
      "swatch": "cyan"
    },
    "boundary": {
      "label": "Control boundary",
      "swatch": "teal"
    },
    "learner": {
      "label": "Learner",
      "swatch": "violet"
    },
    "observer": {
      "label": "Observer (read-only)",
      "swatch": "amber"
    }
  },
  "layers": [
    {
      "name": "Entry & lifecycle",
      "role": "PerformanceProfiler (Mod) + ProfilerSystem (ModSystem): Load/Unload, world load/unload, the per-tick PreUpdateEntities -> PostUpdateEverything driver."
    },
    {
      "name": "Measurement (hot path)",
      "role": "Profiling/: the two hook backends, ProbeStack, MetricCollector + RingBuffer, the spike/stall detectors. Zero-allocation per tick."
    },
    {
      "name": "Pipeline (calculation locus)",
      "role": "Data/: DataRegistry + Collectors -> Aggregators -> Stats -> Detectors -> Streams. Every number is named and looked up by stable string."
    },
    {
      "name": "Interpretation",
      "role": "Insights/: off-thread (~1 Hz) detector roster over reference frames; publishes back into DataRegistry + an in-memory feed."
    },
    {
      "name": "Consuming surfaces",
      "role": "Web/ (player: HTTP worker thread, immutable snapshots) and Data/Streams -> Profiling/Persistence (agent: LiteDB writer thread). Both read-only over the pipeline."
    },
    {
      "name": "Off-game & archived",
      "role": "Tests/ (L1 xUnit), tools/ (L4/L6/L8 audit + preview), UI/ (archived overlay), lib/ (vendored LiteDB). None in the per-tick player path."
    }
  ],
  "layersNote": "Arrows are unidirectional and the hot path stays inside the measurement layer. The two consuming surfaces (dashboard router, persistence streams) only read pipeline snapshots; neither reaches into the live collector — ProfilerSystem.Collector is internal to enforce this (the v0.10 race-free posture, after BuildNow once read MetricCollector.History from the HTTP worker thread). Insights reads the pipeline and writes back through DataRegistry, but is never the source of truth for a number. The archived overlay (UI/) and the off-game tooling are not in the player path.",
  "dataFlow": {
    "intro": "One hook-timing observation, end to end, extended to the browser surface. The game advances a tick; every profiled mod's hook override runs inside a timing wrap (delegate try/finally or IL-emitted finally); the per-mod ticks are credited; the tick closes into a TickFrame; the pipeline folds it; the insights engine evaluates it off-thread; and the browser polls an immutable snapshot of the result. This is the chain Invariant 1 (read-only) and Invariant 2 (zero-alloc hot path) both govern.",
    "simsets": [
      "Tick open",
      "Hook dispatch (hot path)",
      "Tick close",
      "Pipeline fold",
      "Off-thread interpretation",
      "Persist (agent)",
      "Player read"
    ],
    "steps": [
      {
        "n": 1,
        "sys": "Profiling",
        "fn": "ProfilerSystem.PreUpdateEntities",
        "set": "Tick open",
        "reads": "Main.GameUpdateCount",
        "writes": "Collector.BeginTick: reads entry alloc-bytes, Stopwatch.GetTimestamp, PerModAttribution.SnapshotForTick, sets _tickOpen",
        "fail": false
      },
      {
        "n": 2,
        "sys": "Profiling",
        "fn": "tModLoader *Loader.HookList<T>.Enumerate",
        "set": "Hook dispatch (hot path)",
        "reads": "each profiled mod's hook override",
        "writes": "iterates every override; each enters a method patched by one of the two backends",
        "fail": false
      },
      {
        "n": 3,
        "sys": "Profiling",
        "fn": "HookProbe.Time* (delegate path)",
        "set": "Hook dispatch (hot path)",
        "reads": "Stopwatch.GetTimestamp() at entry",
        "writes": "try { orig(...) } finally { credit } — try/finally not try/catch so a mod throw propagates unchanged (Inv 1)",
        "fail": true
      },
      {
        "n": 4,
        "sys": "Profiling",
        "fn": "ProbeStack.Enter / Leave (IL path, default)",
        "set": "Hook dispatch (hot path)",
        "reads": "ldc.i4 hookId; emitted finally",
        "writes": "prologue + finally-protected body; every ret rewritten to stloc;leave; Leave credits in the finally (Inv 1)",
        "fail": true
      },
      {
        "n": 5,
        "sys": "Data",
        "fn": "PerModAttribution.Add",
        "set": "Hook dispatch (hot path)",
        "reads": "(modId, categoryId, hookId, deltaTicks)",
        "writes": "one indexed-array write, zero allocation; the only per-detour cost besides two Stopwatch reads (Inv 2)",
        "fail": false
      },
      {
        "n": 6,
        "sys": "Profiling",
        "fn": "ProfilerSystem.PostUpdateEverything -> Collector.EndTick",
        "set": "Tick close",
        "reads": "exit alloc-bytes + entity counts",
        "writes": "reads exit counter, assembles TickFrame, pushes to RingBuffer[1800], runs SpikeDetector.Observe, PerModAttribution.CloseTick",
        "fail": false
      },
      {
        "n": 7,
        "sys": "Profiling",
        "fn": "SpikeDetector.Observe / StallDetector",
        "set": "Tick close",
        "reads": "TickFrame frame-time",
        "writes": "median+MAD spike window open/extend/close; stall classification; peak attribution via PerTickAttributionRing",
        "fail": false
      },
      {
        "n": 8,
        "sys": "Data",
        "fn": "ContextTagger.Snapshot",
        "set": "Pipeline fold",
        "reads": "tickIndex",
        "writes": "stamps the just-closed TickFrame.Context (biome/boss/weather/invasion) — runs after EndTick so it tags the right frame",
        "fail": false
      },
      {
        "n": 9,
        "sys": "Data",
        "fn": "EventAggregator.Accumulate",
        "set": "Pipeline fold",
        "reads": "tagger.Current + frameMs",
        "writes": "folds the context snapshot into per-dimension bucket stats",
        "fail": false
      },
      {
        "n": 10,
        "sys": "Data",
        "fn": "DataRegistry.PerTickCallbacks[i](in ctx)",
        "set": "Pipeline fold",
        "reads": "frozen immutable callback array",
        "writes": "drives every PerTick stream (F2 usage, F3 cost time series) in a for-loop, zero virtual dispatch (Inv 2)",
        "fail": false
      },
      {
        "n": 11,
        "sys": "Data",
        "fn": "SegmentDetector.OnTick",
        "set": "Pipeline fold",
        "reads": "tagger.Current + PerModCategoryRawMs",
        "writes": "opens/closes biome/boss/weather/invasion segments; folds spike/stall/death edges in",
        "fail": false
      },
      {
        "n": 12,
        "sys": "Profiling",
        "fn": "_recorder.OnTick -> TickDownsampler -> DbWriteOp",
        "set": "Persist (agent)",
        "reads": "latest TickFrame + collector",
        "writes": "downsamples 1Hz/1min, enqueues queue-only; the game thread never blocks on disk (Inv 2)",
        "fail": true
      },
      {
        "n": 13,
        "sys": "Profiling",
        "fn": "DbWriterThread -> ProfilerDatabase",
        "set": "Persist (agent)",
        "reads": "Channel<DbWriteOp> batch",
        "writes": "single writer thread drains the queue, journal-first then LiteDB apply; backups rotate on clean end",
        "fail": true
      },
      {
        "n": 14,
        "sys": "Insights",
        "fn": "InsightsEngine.Evaluate (off-thread ~60 ticks)",
        "set": "Off-thread interpretation",
        "reads": "collector snapshot via CollectorInsightInput",
        "writes": "Interlocked.CompareExchange latch (an inline Evaluate once wedged the main thread >1s); updates reference frames, runs detectors",
        "fail": true
      },
      {
        "n": 15,
        "sys": "Insights",
        "fn": "InsightStore.Submit + PromoteConfidence",
        "set": "Off-thread interpretation",
        "reads": "detector records",
        "writes": "dedup on full-width InsightKey, TTL eviction, confidence promotion gated on PValueAdjusted",
        "fail": false
      },
      {
        "n": 16,
        "sys": "Insights",
        "fn": "Publish/ stats + InsightsStat",
        "set": "Off-thread interpretation",
        "reads": "Store.AllLive() + foundation streams",
        "writes": "compose the seven Insights-tab snapshots; register back into DataRegistry",
        "fail": false
      },
      {
        "n": 17,
        "sys": "Web",
        "fn": "browser SPA fetch('/api/X')",
        "set": "Player read",
        "reads": "loopback TCP 127.0.0.1:27277",
        "writes": "SPA polls /api/now ~500ms + per-tab endpoints; one request -> one background thread",
        "fail": false
      },
      {
        "n": 18,
        "sys": "Web",
        "fn": "DashboardRouter.Route -> Build*",
        "set": "Player read",
        "reads": "HttpRequest path (strict switch)",
        "writes": "runs on the HTTP worker thread, concurrent with the game thread",
        "fail": false
      },
      {
        "n": 19,
        "sys": "Data",
        "fn": "DataRegistry.Shared.Lookup<TSnapshot>(name).CurrentSnapshot()",
        "set": "Player read",
        "reads": "stream name (RolloutStreamNames)",
        "writes": "pulls a fresh immutable snapshot — race-free vs the game thread (the v0.10 BuildNow fix)",
        "fail": true
      },
      {
        "n": 20,
        "sys": "Web",
        "fn": "HttpResponse.Json",
        "set": "Player read",
        "reads": "flat anonymous object",
        "writes": "System.Text.Json serialise; Connection: close; the browser caches into lastX and renders the active pane",
        "fail": false
      }
    ]
  },
  "failures": [
    {
      "step": "3 -> 5",
      "link": "3/4 -> 5",
      "title": "Read-only credit via try/finally (Invariant 1)",
      "body": "Both backends wrap the original body in try/finally, never try/catch. A mod-thrown exception bubbles unchanged and only the time up to the throw is credited. If this ever became try/catch (or the IL endfinally consumed the exception), the profiler would swallow a mod's error and silently change game behaviour — the one thing Invariant 1 forbids. The profiler measures; it never alters what the game does."
    },
    {
      "step": "19",
      "link": "6 -> 19",
      "title": "Race-free snapshot pull (the v0.10 BuildNow fix)",
      "body": "Build* runs on the HTTP worker thread while the game thread mutates MetricCollector's ring buffers and lists. Every endpoint pulls Lookup<TSnapshot>(name).CurrentSnapshot(), a fresh immutable value, so the worker never races the game thread. Pre-v0.10 BuildNow read ProfilerSystem.Collector.History directly from the worker thread — a real (small-window) data race. ProfilerSystem.Collector was made internal to enforce the snapshot discipline; reverting it would re-open the race and the dashboard would tear or crash under load."
    },
    {
      "step": "4",
      "link": "4 (Mod.Unload)",
      "title": "ILHook teardown before assembly unload (Invariant 4)",
      "body": "The IL backend patches other mods' methods to call into ProbeStack, which lives in our assembly. Mod.Unload MUST run ILHookInterceptor.Uninstall() to dispose every installed ILHook before tModLoader unloads our assembly. If Unload were skipped or the teardown removed, the patched IL would call into a vanished assembly on the next tick — InvalidProgramException, a player-visible crash. The delegate backend needs no teardown (tModLoader auto-removes MonoModHooks.Add detours per-assembly)."
    },
    {
      "step": "2",
      "link": "2 (install)",
      "title": "Abort-clean install (Invariant 4)",
      "body": "Hook install (both backends) wraps its per-mod loop in an outer try/catch; the IL backend disposes already-installed hooks on failure (Uninstall) and leaves Installed=false. A per-method manipulator failure is caught, counted, and the rest of the install continues. The mod may decline to instrument; it never proceeds against internals it cannot verify or crashes the game. The 5725572 world-load crash (hooking tModLoader-internal closed generics, JIT shared-body trap) is the cautionary tale the _tmlAssembly filter now guards."
    },
    {
      "step": "12 -> 13",
      "link": "12 -> 13",
      "title": "Persistence self-disable, collection continues (Invariant 4)",
      "body": "SessionRecorder.OnTick is wrapped in try/catch in PostUpdateEverything; an IO/permissions failure sets _recorder=null for the rest of the world and metric collection plus the live dashboard continue. The single writer thread means the game thread never touches LiteDB (276 ns enqueue, no disk in the per-tick path). If the DB open failed at Mod.Load, Database=null and everything runs in-memory only. The agent surface goes dark; the player surface never does."
    }
  ],
  "relationships": [
    {
      "a": "Profiling",
      "b": "Data",
      "mech": "Data/Collectors/* adapt over MetricCollector's public read accessors; Data/Stats/* derive from PerModAttribution; ProfilerSystem drives the frozen PerTickCallbacks each PostUpdateEverything",
      "data": "per-tick frame-time, per-mod CPU/alloc, entity counts, spike/stall windows",
      "breaks": "every dashboard endpoint and persisted aggregate loses its source signal; the pipeline is empty"
    },
    {
      "a": "Data",
      "b": "Web",
      "mech": "DashboardRouter.Build* calls Lookup<TSnapshot>(name).CurrentSnapshot() on the HTTP worker thread, resolving names through RolloutStreamNames",
      "data": "immutable per-endpoint snapshots (KPIs, frames, segments, lag, timeline, insights)",
      "breaks": "the dashboard serves stale or empty JSON; pre-v0.10 this read the live collector directly and raced the game thread"
    },
    {
      "a": "Data",
      "b": "Insights",
      "mech": "detectors read smoothed pipeline outputs via CollectorInsightInput; the Publish/ stats compose foundation streams (roster F1, usage F2, HookCpu, cost F3) via DataRegistry.Lookup, never direct refs",
      "data": "per-mod cost/alloc distributions, segment store, spike/stall windows",
      "breaks": "detectors lose their input and the Insights tab goes empty; the contract-decoupling that let the rework parallelise is what isolates this"
    },
    {
      "a": "Insights",
      "b": "Data",
      "mech": "the seven Publish/ stats register into DataRegistry.Shared like any stream; CrossCuttingSignalStat + InsightsStat read InsightsEngine.Shared directly",
      "data": "composed per-mod cards, dormant tiers, pattern leaderboards, engagement-cost tuples, interaction matrix",
      "breaks": "the v0.12 Insights endpoints lose their feed; the live insight surface diverges from the store"
    },
    {
      "a": "Data",
      "b": "Profiling",
      "mech": "Data/Streams/* enqueue DbWriteOps through SessionRecorder; the single DbWriterThread batches and applies them to ProfilerDatabase (LiteDB)",
      "data": "sessions, spikes, stalls, segments, tick aggregates, deaths, interactions, context baselines",
      "breaks": "nothing persists to LiteDB; the agent surface and cross-session lifetime data go dark, but metric collection + the live dashboard continue (Invariant 4)"
    },
    {
      "a": "Profiling",
      "b": "lib",
      "mech": "ProfilerDatabase wraps LiteDatabase from the vendored LiteDB 5.0.21 DLL; packed via build.txt dllReferences",
      "data": "BSON documents across ~25 collections + the WAL/journal",
      "breaks": "the DB cannot open; persistence degrades to no-op; the live session still serves the dashboard"
    },
    {
      "a": "Insights",
      "b": "Profiling",
      "mech": "CrossSessionStore persists/seeds the per-context per-mod baselines to the LiteDB contextBaselines collection, keyed by ModlistFingerprint.Compute()",
      "data": "Welford components per (fingerprint, dim, key, mod)",
      "breaks": "the durability layer is lost; confidence can never climb past Low because no lifetime baseline seeds the session"
    },
    {
      "a": "UI",
      "b": "Web",
      "mech": "ProfilerOverlaySystem (the one live class in the archived overlay) registers the F9 OpenDashboard keybind that ProfilerPlayer.ProcessTriggers polls to launch the browser",
      "data": "the F9 ModKeybind + the dashboard URL",
      "breaks": "F9 does nothing; the player cannot open the dashboard (the chat hint still prints the URL for manual copy)"
    },
    {
      "a": "Profiling",
      "b": "Web",
      "mech": "PerformanceProfiler.cs binds the DashboardHttpServer at Mod.Load and disposes it (before the DB) at Mod.Unload; BuildMemory also joins HookInterceptor.MeasuredHookCounts + ModRamReader",
      "data": "the server lifecycle + the self-health/memory join sources",
      "breaks": "the dashboard never binds (F9 inert, chat shows the failure) or a late request calls into a half-disposed DB"
    },
    {
      "a": "Tests",
      "b": "Insights",
      "mech": "Compile-Include+Link lifts Insight/InsightStore/RankingScorer/Shared/ReferenceFrames/Drivers into the runtime-free xUnit harness",
      "data": "pure-logic assertions over ranking, promotion, Welford/Welch stats, baselines",
      "breaks": "the load-bearing insight regressions (share-vs-ratio split, p-value gating) lose their pin; a refactor could silently reintroduce them"
    },
    {
      "a": "Tests",
      "b": "Data",
      "mech": "Link lifts PerModAttribution, Baseline, StallDetector, Streams, RolloutContracts; LiteDB-only persistence sources lift for the round-trip/benchmark fixtures",
      "data": "ring-buffer wrap, stall classification, persistence write/read fidelity",
      "breaks": "pure-logic pipeline regressions go uncaught until they fail in-game"
    },
    {
      "a": "tools_testing",
      "b": "Web",
      "mech": "Playwright boots the real SPA off-game, DOM-discovers every tab/pane/endpoint, asserts L4 layout invariants + L8 visual quality; fixtures shared with tools_preview",
      "data": "rendered DOM, computed styles, screenshots, per-page dossiers",
      "breaks": "no machine check on the dashboard's layout/interaction/readability; regressions surface only by eyeball in-game"
    }
  ],
  "stateOwnership": [
    {
      "owner": "Profiling",
      "items": "ProfiledMods/Names/Versions (static, process lifetime), the per-world Collector + RingBuffer<TickFrame>[1800] (internal, nulled on world unload), the static Database (LiteDB) + DbWriterThread queue + EventJournal, ProfilerSelfHealth (process singleton), _installedHooks/_instrumentedHandles (process-scoped ILHook lists). Read by Data/ adapters, the recorder, and self-health; none mutate the collector externally."
    },
    {
      "owner": "Data",
      "items": "DataRegistry.Shared (static, registered at Mod.Load), the frozen PerTickCallbacks array, PerModAttribution (hot-path accumulator), PerTickAttributionRing (50-window), SegmentStore + SegmentDetector, and every stream's per-session buffers (InitialiseAll on world load, ResetAll on unload, DisposeAll at Mod.Unload). Read by Web (snapshots), Insights (smoothed reads), and Profiling/Persistence (writes)."
    },
    {
      "owner": "Insights",
      "items": "InsightsEngine.Shared (Volatile static, per-session, cleared on world unload), InsightStore live dict + history, ContextBaseline (16-bucket) + TemporalBaseline, the static heap/entity drivers, and the contextBaselines LiteDB rows (via CrossSessionStore). Read by the dashboard Insights endpoints; the engine itself is the only writer."
    },
    {
      "owner": "Web",
      "items": "DashboardHttpServer (static singleton, bound at Mod.Load, disposed before the DB), CachedCssBytes/CachedJsBytes (type-init), the strict Route() switch. Browser-side lastNow/lastXxx poll caches are client state, not mod state. Owns no number — every value is pulled from Data/."
    },
    {
      "owner": "UI",
      "items": "DashboardKeybind (the one live ModKeybind, on ProfilerOverlaySystem). TabRegistry.Tabs + OverlayState are archived, not instantiated in the player path."
    },
    {
      "owner": "Tests",
      "items": "The Link'd pure-logic source set (no copies) + gitignored Tests/bin and Tests/obj. Owns no production state."
    },
    {
      "owner": "tools_testing",
      "items": "The committed fixture contract (tools/preview/fixtures/*.json, 29 files), the context/pages/ dossiers (Findings owned by the audit, Notes by the human), and the gitignored .venv + Playwright browser."
    },
    {
      "owner": "tools_preview",
      "items": "fixtures/*.json (shared with tools_testing) + the regenerated preview HTML reflecting current source. Owns no runtime state."
    },
    {
      "owner": "lib",
      "items": "LiteDB.dll (vendored 5.0.21). Read by ProfilerDatabase and the persistence tests; owns no profiler logic."
    }
  ],
  "coverage": {
    "cols": [
      "docs",
      "code",
      "hotpath",
      "persist",
      "dashboard",
      "insights",
      "tested"
    ],
    "rows": [
      {
        "label": "Data/",
        "node": "Data",
        "cells": {
          "docs": 3,
          "code": 2,
          "hotpath": 3,
          "persist": 3,
          "dashboard": 3,
          "insights": 2,
          "tested": 2
        },
        "prev": {}
      },
      {
        "label": "Insights/",
        "node": "Insights",
        "cells": {
          "docs": 3,
          "code": 2,
          "hotpath": 1,
          "persist": 2,
          "dashboard": 3,
          "insights": 3,
          "tested": 2
        },
        "prev": {}
      },
      {
        "label": "Profiling/",
        "node": "Profiling",
        "cells": {
          "docs": 3,
          "code": 2,
          "hotpath": 3,
          "persist": 3,
          "dashboard": 1,
          "insights": 1,
          "tested": 2
        },
        "prev": {}
      },
      {
        "label": "Tests/",
        "node": "Tests",
        "cells": {
          "docs": 3,
          "code": 2,
          "hotpath": 1,
          "persist": 2,
          "dashboard": 1,
          "insights": 2,
          "tested": 3
        },
        "prev": {}
      },
      {
        "label": "UI/",
        "node": "UI",
        "cells": {
          "docs": 2,
          "code": 1,
          "hotpath": 1,
          "persist": 1,
          "dashboard": 1,
          "insights": 1,
          "tested": 1
        },
        "prev": {}
      },
      {
        "label": "Web/",
        "node": "Web",
        "cells": {
          "docs": 3,
          "code": 2,
          "hotpath": 1,
          "persist": 1,
          "dashboard": 3,
          "insights": 2,
          "tested": 1
        },
        "prev": {}
      },
      {
        "label": "lib/",
        "node": "lib",
        "cells": {
          "docs": 2,
          "code": 1,
          "hotpath": 1,
          "persist": 3,
          "dashboard": 1,
          "insights": 1,
          "tested": 2
        },
        "prev": {}
      },
      {
        "label": "tools/preview/",
        "node": "tools_preview",
        "cells": {
          "docs": 2,
          "code": 1,
          "hotpath": 1,
          "persist": 1,
          "dashboard": 2,
          "insights": 1,
          "tested": 1
        },
        "prev": {}
      },
      {
        "label": "tools/testing/",
        "node": "tools_testing",
        "cells": {
          "docs": 3,
          "code": 1,
          "hotpath": 1,
          "persist": 1,
          "dashboard": 2,
          "insights": 1,
          "tested": 2
        },
        "prev": {}
      }
    ],
    "note": "Inspection scope for this arch fill (2026-06-25): the context/systems/*.md + integration-map.md + architecture.md docs were read in full as the digested source of truth; per-file annotations were produced against them with class doc-comment reads for the ~40 files the docs only name-list. The hot-path and persistence reality is doc-grounded and cross-checked; the SPA JS renderer internals and the archived overlay draw code were trusted from the docs, not re-read line by line. The whole v0.13-v0.22 dashboard arc remains runtime-unverified in-game (the running tModLoader locks the .tmod)."
  },
  "milestones": [
    {
      "id": "m-readonly",
      "title": "Read-only instrumentation + dual hook backend",
      "status": "done",
      "note": "Delegate-pair + IL backends; ILHook default at ~100% coverage; abort-clean install + teardown."
    },
    {
      "id": "m-litedb",
      "title": "LiteDB persistence (v0.3)",
      "status": "done",
      "note": "Single writer thread, four-layer crash safety, ~25 collections; the legacy JSON writer was deleted."
    },
    {
      "id": "m-pipeline",
      "title": "Unified Data/ pipeline (v0.10-v0.11)",
      "status": "done",
      "note": "Every number in a named typed stream; ProfilerSystem.Collector made internal; the BuildNow race fixed."
    },
    {
      "id": "m-dashboard",
      "title": "Browser dashboard + v0.12 tab rework",
      "status": "done",
      "note": "Loopback HTTP SPA replaced the archived overlay; six live tabs; F1/F2/F3 foundations + tab streams."
    },
    {
      "id": "m-insights",
      "title": "Reference-frame insights engine (v0.13-v0.22)",
      "status": "done",
      "note": "Top-level Insights/ module; 16 detectors (13 live, 3 gated); ContextBaseline/TemporalBaseline; cross-session baselines."
    },
    {
      "id": "m-runtime",
      "title": "In-game runtime verification of the v0.13-v0.22 arc",
      "status": "next",
      "note": "The dashboard + insights surfaces are runtime-unverified; the .tmod is locked while tModLoader runs."
    },
    {
      "id": "m-insights-persist",
      "title": "Wire the per-insight LiteDB persistence path",
      "status": "planned",
      "note": "The insights collection (InsightRow/InsightStream/DbOpKind.Insight) is scaffolded but has no producer."
    },
    {
      "id": "m-mp",
      "title": "Multiplayer hook coverage + post-session HTML report",
      "status": "planned",
      "note": "v1 is single-player; the HTML report would reuse the asset-bundling + snapshot reads."
    }
  ],
  "criticalPaths": [
    {
      "name": "Per-tick hook-timing capture (hot path)",
      "len": "~6 steps · 2 subsystems · zero-alloc",
      "steps": [
        "PreUpdateEntities",
        "Collector.BeginTick",
        "HookList enumerate",
        "ProbeStack.Enter/Leave (or HookProbe.Time*)",
        "PerModAttribution.Add",
        "PostUpdateEverything",
        "Collector.EndTick",
        "SpikeDetector.Observe"
      ],
      "blast": "This is the budget-governed path (Invariant 2: Lite < 1%, ~0.12 ms/tick). Any allocation, boxing, or virtual dispatch added here is measured before it is considered done. A regression here is invisible until the profiler's own overhead shows up in the numbers it reports. It is shared by both backends; the frozen PerTickCallbacks fan-out runs in the same PostUpdateEverything. Blast radius: every downstream number (dashboard, persistence, insights) is sourced from this loop, so a correctness bug here mis-attributes everywhere at once."
    },
    {
      "name": "Player read path (dashboard poll)",
      "len": "~5 steps · 2 subsystems · HTTP worker thread",
      "steps": [
        "browser fetch /api/X",
        "DashboardHttpServer.Accept",
        "DashboardRouter.Route",
        "Build*",
        "Lookup<TSnapshot>(name).CurrentSnapshot()",
        "HttpResponse.Json"
      ],
      "blast": "Runs entirely off the game thread on a per-request background thread. The load-bearing property is the immutable snapshot pull (the v0.10 race-free posture). Reordering this to read live collector state re-opens the data race that BuildNow once had. Blast radius: all six tabs + ~29 endpoints; a router that derived a number instead of formatting a snapshot would violate the calculation-locus rule and could diverge from the persisted/agent view."
    },
    {
      "name": "Session-end persistence kickoff",
      "len": "~4 steps · 2 subsystems · async, off game thread",
      "steps": [
        "PreSaveAndQuit/OnWorldUnload",
        "KickOffSessionEndAsync",
        "Collector.FlushSpikes",
        "SessionRecorder.End",
        "DbWriterThread drain",
        "backup rotation"
      ],
      "blast": "Idempotent via the _preSaveEndKickedOff latch; FlushSpikes runs before End so an open spike window lands. A throw in PreSaveAndQuit (outside tML's SystemLoader catch) would abort the user's world save, so it is wrapped. Blast radius: the agent surface + cross-session lifetime data; a failure self-disables persistence but never blocks the save or the live dashboard."
    }
  ],
  "notes": [
    {
      "tag": "design",
      "sev": "",
      "title": "Two-stack model: data vs presentation",
      "body": "The mod separates a data stack (everything captured — ticks, per-mod CPU/alloc, spikes, stalls, segments, context, deaths, interactions) from a presentation/storage stack (what is written, served, surfaced). 'How much we can observe' wants more; 'how we spend the overhead budget and the player's attention' is the gate. The structural rule since v0.10: calculation only happens inside a Data/ pipeline stage; routers and exporters format snapshots."
    },
    {
      "tag": "live",
      "sev": "ok",
      "title": "IL backend is the default (~100% vs ~71.6%)",
      "body": "HookBackend.Mode chooses Delegate / ILHook / Parallel. ILHook is default since b52f8b6 — signature-agnostic, so it covers the ~28% of overrides the delegate path's fixed signature set misses. Parallel runs both and logs divergence; the player-visible numbers stay on the selected backend."
    },
    {
      "tag": "gap",
      "sev": "watch",
      "title": "Per-insight LiteDB persistence is scaffolded but unfed",
      "body": "The insights collection (ProfilerDatabase.Insights), InsightRow, InsightStream, and DbOpKind.Insight all exist, but no producer enqueues a DbWriteOp.Insight. The live insight feed reaches the dashboard purely in-memory via Store.AllLive(). The cross-session persistence that IS live is contextBaselines (the reference-frame substrate), not per-insight rows."
    },
    {
      "tag": "gap",
      "sev": "watch",
      "title": "Three detectors gated; descriptive patterns stay Low by design",
      "body": "FreeRemovalCandidate (engagement-signal), LoadoutCombinationCost (cross-session-loadout-aggregation), and HookFrequencyTail (per-hook-call-counts) are registered stubs that emit nothing until their gate clears. Separately, the share/structural/segment patterns run no hypothesis test (PValueAdjusted=1) and sit at Low/Preliminary forever — correct under the honesty contract; the statistical context/temporal/heap detectors do compute corrected p-values and can reach Medium/High."
    },
    {
      "tag": "pending",
      "sev": "watch",
      "title": "The v0.13-v0.22 dashboard + insights arc is runtime-unverified",
      "body": "The component-library rebuild, the chart vocabulary, the six-tab SPA, and the reference-frame engine are all built and lint/compile clean, but unconfirmed in a running game — the running tModLoader locks the .tmod, so .cs fixes are not live until a Build+Reload. The regenerated preview reflects current source; the L4 Playwright harness machine-verifies layout/interaction off-game; the irreducible L7 in-game check is still outstanding."
    },
    {
      "tag": "design",
      "sev": "",
      "title": "No mod-specific code (Invariant 5)",
      "body": "Every detector, tracker, classifier, and event listener reads the interaction shape, never the mod identity. NPC spawns key on the IEntitySource subclass, damage on the PlayerDeathReason struct, item creation on the ItemCreationContext — never on a named mod's string. A profiler that string-matched 'CheatSheet' would break for HEROsMod; one that reads SpawnSource is universal."
    },
    {
      "tag": "design",
      "sev": "",
      "title": "Attribution is free via our own reflection, not a tML ownership table",
      "body": "The README/design say per-mod attribution 'comes for free because tModLoader tracks per-assembly detour ownership.' The public tML API exposes no such table. Attribution IS free, but via the profiler's own MethodBase.DeclaringType.Assembly -> Mod.Code dictionary built once at PostSetupContent. Same outcome; the wording is a known correction (2026-05-19)."
    }
  ],
  "concept": {
    "root": "Per-mod CPU + RAM + engagement attribution for a modded Terraria session",
    "branches": [
      {
        "head": "Measurement",
        "kind": "foundation",
        "leaves": [
          "MonoMod delegate-pair detours",
          "IL-injection backend (default)",
          "ProbeStack Enter/Leave timing",
          "PerModAttribution indexed accumulator",
          "RingBuffer<TickFrame> 30s history",
          "GC.GetAllocatedBytesForCurrentThread alloc"
        ],
        "trunks": [
          "Profiling"
        ]
      },
      {
        "head": "Pipeline",
        "kind": "foundation",
        "leaves": [
          "DataRegistry.Shared name-keyed lookup",
          "Collectors -> Aggregators -> Stats -> Detectors -> Streams",
          "frozen RolloutContracts snapshots",
          "Segments (biome/boss/weather/invasion)",
          "Spike (median+MAD) + Stall detection",
          "frozen per-tick callback fan-out"
        ],
        "trunks": [
          "Data"
        ]
      },
      {
        "head": "Interpretation",
        "kind": "observer",
        "leaves": [
          "reference frames (Context A / Temporal B)",
          "16 detectors across five families",
          "Welford/Welch/Cohen statistics",
          "Bonferroni-corrected p-values",
          "confidence promotion + EvidenceScope",
          "cross-session fingerprinted baselines"
        ],
        "trunks": [
          "Insights"
        ]
      },
      {
        "head": "Surfaces",
        "kind": "boundary",
        "leaves": [
          "loopback HTTP SPA (player)",
          "six tabs · ~29 /api/* endpoints",
          "OKLCH monochrome-chrome component library",
          "LiteDB store + writer thread (agent)",
          "client.log via Mod.Logger (agent)",
          "off-game preview + L4/L6/L8 audit"
        ],
        "trunks": [
          "Web",
          "tools_preview",
          "tools_testing"
        ]
      },
      {
        "head": "Trust posture",
        "kind": "env",
        "leaves": [
          "Inv 1: read-only (try/finally)",
          "Inv 2: zero-alloc budget",
          "Inv 3: descriptive never prescriptive",
          "Inv 4: abort-clean on host drift",
          "Inv 5: no mod-specific code"
        ],
        "trunks": []
      }
    ],
    "note": "The combination — per-mod attribution, an engagement axis, time-bounded segments, a statistically-guarded insights engine, cross-session memory, and a browser dashboard, all under a strict read-only trust posture — does not exist elsewhere in the tModLoader ecosystem."
  },
  "glossary": [
    {
      "term": "ILHook",
      "def": "A MonoMod.RuntimeDetour IL-level method patch. The default backend emits a timing prologue + finally into every mod hook override, giving ~100% (signature-agnostic) coverage."
    },
    {
      "term": "MonoMod",
      "def": "The runtime-patching library tModLoader exposes via MonoModHooks. The delegate-pair backend uses MonoModHooks.Add; the IL backend constructs ILHook directly for deterministic Dispose() on unload."
    },
    {
      "term": "ProbeStack",
      "def": "Static Enter/Leave[CpuAlloc] methods called from the emitted IL. They read Stopwatch.GetTimestamp() (and the GC alloc counter) and credit PerModAttribution in the finally."
    },
    {
      "term": "DataRegistry",
      "def": "The process-wide .Shared singleton that holds every named, typed stream. Consumers call Lookup<TSnapshot>(name).CurrentSnapshot(); Freeze() snapshots the per-tick callback array."
    },
    {
      "term": "snapshot",
      "def": "A fresh immutable struct/record returned by CurrentSnapshot(). The race-free contract between the game thread (which mutates state) and the HTTP worker thread (which reads it)."
    },
    {
      "term": "segment",
      "def": "A time-bounded slice of a session (a biome visit, boss fight, weather event, invasion, or death-bracketed run), opened/closed by SegmentDetector and costed per mod."
    },
    {
      "term": "spike",
      "def": "A frame-time outlier (e.g. 60 ms in a 16.7 ms baseline) detected by median + MAD over a rolling window, attributed to the mod whose per-tick cost diverged most during the window."
    },
    {
      "term": "stall",
      "def": "A multi-tick freeze the player perceives, classified by cause (GC pause, OS-suspend, draw-thread) and rolled up into a stallCluster — 'the one freeze you felt'."
    },
    {
      "term": "EvidenceScope",
      "def": "An insight badge orthogonal to Confidence: ThisSession / LifetimeData / NeedsPersistence. It says how durable the evidence is, independent of how statistically strong the claim is."
    },
    {
      "term": "Confidence",
      "def": "An insight's statistical strength: Preliminary -> Low -> Medium -> High. Promotion is gated on PValueAdjusted, so an untested observation (p=1) can never climb past Low by repetition."
    },
    {
      "term": "reference frame",
      "def": "The IReferenceFrame contract: 'what is normal for this signal in this context', a centre (Expected) + a spread (Dispersion). A detector reports a deviation, never a raw magnitude (the spine law)."
    },
    {
      "term": "driver",
      "def": "A workload signal (entity count, session age, heap MB) a detector can regress cost against or control for — e.g. HeapLeak controls for entity count to tell a leak from progression."
    },
    {
      "term": "Welford",
      "def": "The online (O(1)/sample) variance algorithm RunningStat uses; with Chan's Merge and a Without complement it lets reference frames combine and split distributions without storing points."
    },
    {
      "term": "five families",
      "def": "The detector taxonomy: Deviation (vs own baseline), Temporal (later vs earlier), Distribution (frame-time shape), Headroom (budget remaining), Structure (cross-mod relationships)."
    },
    {
      "term": "OKLCH",
      "def": "The perceptually-uniform colour space the dashboard's :root tokens are defined in. The chrome is zero-chroma neutral grey; only the data carries colour."
    },
    {
      "term": "loopback",
      "def": "127.0.0.1-only binding. The dashboard server is loopback-exclusive so nothing leaves the machine and no firewall prompt fires (loopback bypasses the macOS application firewall)."
    },
    {
      "term": "DbWriteOp",
      "def": "The producer op shape the writer thread dispatches on (keyed by DbOpKind). Streams enqueue ops; the single DbWriterThread batches and applies them to LiteDB — the game thread never touches disk."
    },
    {
      "term": "backend divergence",
      "def": "In Parallel mode both backends install on the same modlist; their running totals are compared and the relative delta is logged as [backend-compare] lines, without per-tick spam."
    }
  ],
  "decisions": [
    {
      "title": "Raw TcpListener, not HttpListener",
      "why": "HttpListener routes through Windows http.sys, which refuses to bind for non-admin users without a netsh urlacl. That breaks the 'load -> F9 -> browser just works' contract for Workshop players. TcpListener is a userspace socket needing no admin on any port >= 1024 on every platform. This is the load-bearing architecture decision of the Web subsystem; do not simplify it back.",
      "node": "Web"
    },
    {
      "title": "IL-injection backend as the default",
      "why": "The delegate-pair path matches ~30 fixed signature families and covers ~71.6% of overrides. The IL backend is signature-agnostic and covers ~100%. ILHook became the default at b52f8b6; the delegate path stays for the proven baseline and Parallel-mode divergence checks.",
      "node": "Profiling"
    },
    {
      "title": "Insights consolidated into a top-level Insights/ module (v0.13-v0.22)",
      "why": "The engine was lifted out of Data/Detectors/Insights/ into its own module with reference frames, drivers, a cross-session store, and the Publish/ stats. The reference-frame substrate (ContextBaseline/TemporalBaseline) makes every insight a deviation from a comparable baseline, not an absolute magnitude — the spine law the honesty contract requires.",
      "node": "Insights"
    },
    {
      "title": "OKLCH monochrome-chrome component library (v0.17)",
      "why": "Per-pane bespoke CSS/JS caused duplication-driven drift (recurring scroll/leak/centring bugs, ~12 scroll-region reinventions, 5 sparkline impls). Rebuilt onto a shared Js.Components vocabulary + a shadcn-neutral OKLCH token system. The chrome is greyed so the only colour on screen is data; the rule is 'greying the chrome never greys the data'.",
      "node": "Web"
    },
    {
      "title": "Persistence is LiteDB, not JSON",
      "why": "The legacy JSON SessionLogWriter (~940 lines) was deleted in v0.3. LiteDB 5.0.21 gives a queryable (LINQ over indexed collections), crash-safe (journal + bounded backups), idempotent, single-DLL store packed in the .tmod. The 6.0 prerelease was rejected — a public mod cannot ship a year-long prerelease DB engine.",
      "node": "Profiling"
    },
    {
      "title": "Calculation locus: every number lives in Data/",
      "why": "Pre-v0.10 the router did heatmap bucketing and median maths inline, and the BuildNow endpoint read collector internals from the HTTP worker thread (a data race). v0.10-v0.11 moved every stream-shaped class into Data/, made ProfilerSystem.Collector internal, and forbade deriving a persist-worthy number outside a Data/ stage. Routers format; Data/ computes.",
      "node": "Data"
    },
    {
      "title": "Deferred world-load init to the first tick",
      "why": "The heavy construction (collector + recorder + watchers + segment engine + DataRegistry.InitialiseAll) once ran inline in OnWorldLoad and measured a 172 ms world-enter freeze. It is now deferred to the first PostUpdateEverything tick, where the cost lands during gameplay (allowed to spike) instead of UI-blocking the world-enter.",
      "node": "Profiling"
    },
    {
      "title": "Off-thread insights evaluation, latched",
      "why": "An inline InsightsEngine.Evaluate once wedged the main loop for over a second on a long session. It now runs on the thread pool every ~60 ticks, gated by an Interlocked.CompareExchange single-slot latch so a pass can never overlap itself; reference frames are fed at 1 Hz so the engine adds no per-tick cost.",
      "node": "Insights"
    }
  ],
  "risks": [
    {
      "sev": "med",
      "title": "Per-insight LiteDB persistence has no producer",
      "node": "Insights",
      "trigger": "The insights collection, InsightRow, InsightStream, and DbOpKind.Insight are all scaffolded, but nothing enqueues a DbWriteOp.Insight — the live feed is in-memory only. A reader expecting a lifetime insight history finds the collection empty."
    },
    {
      "sev": "high",
      "title": "The v0.13-v0.22 dashboard + insights arc is runtime-unverified",
      "node": "Web",
      "trigger": "The whole arc compiles and lints clean and renders in the offline preview + L4 harness, but has not been confirmed in a running game. The running tModLoader locks the .tmod, so the irreducible in-game L7 check is outstanding; a runtime-only break (a JS console error, a tML API mismatch) would not surface until Build+Reload."
    },
    {
      "sev": "med",
      "title": "Three detectors are gated and emit nothing",
      "node": "Insights",
      "trigger": "FreeRemovalCandidate, LoadoutCombinationCost, and HookFrequencyTail are registered only so GatedPatterns() honestly reports the coverage gap. They stay dark until engagement-signal / cross-session-loadout-aggregation / per-hook-call-counts land."
    },
    {
      "sev": "high",
      "title": "ILHook teardown depends on Mod.Unload firing",
      "node": "Profiling",
      "trigger": "If tModLoader ever skipped Mod.Unload, the IL patches on other mods' methods would call into our vanished assembly's ProbeStack on the next tick — a crash. There is no defence beyond tModLoader correctness; the _installedHooks list is process-scoped and only cleared via Unload -> Uninstall."
    },
    {
      "sev": "med",
      "title": "JIT shared-body trap on closed-generic inheritance",
      "node": "Profiling",
      "trigger": "The _tmlAssembly filter (ILHookInterceptor) guards against the .NET JIT sharing a compiled body across reference-type generic instantiations — patching one would patch both and crash tML's player path with an InvalidCastException. A new closed-generic scenario with a non-tML generic parent could re-introduce the failure (the 5725572 one-day regression)."
    },
    {
      "sev": "low",
      "title": "ContextBaseline evicts its least-sampled bucket silently past 16 contexts",
      "node": "Insights",
      "trigger": "MaxBuckets = 16. A >16-context session drops a bucket; Evictions is exposed but nothing logs it, so the drop has no agent-surface warning."
    },
    {
      "sev": "low",
      "title": "Persisted-schema snapshot test is deferred",
      "node": "Tests",
      "trigger": "PersistenceRoundTripTests covers write/read fidelity but there is no frozen-schema snapshot test. A per-collection schema change that silently altered a record shape would not be caught until a read failed in-game."
    },
    {
      "sev": "low",
      "title": "Dashboard freezes when Terraria loses window focus",
      "node": "Web",
      "trigger": "Single-player Terraria pauses ticking when unfocused, so the dashboard stops receiving data. The SPA distinguishes this from a disconnect ('game paused'); the documented workarounds are a second monitor or Multiplayer -> Host & Play (servers never pause)."
    }
  ],
  "alerts": [
    {
      "sev": "watch",
      "text": "v0.13-v0.22 dashboard + insights surfaces compile/lint clean but are unverified in a running game (the .tmod is locked while tModLoader runs).",
      "meta": "runtime gate"
    },
    {
      "sev": "ok",
      "text": "Off-game verification is green: dotnet msbuild has zero error CS, ~70 L1 tests pass sub-second, and the L4/L6/L8 audit harness drives the dashboard.",
      "meta": "off-game CI"
    }
  ],
  "changeFrontier": [
    {
      "name": "Data/",
      "node": "Data",
      "bars": [
        0,
        0,
        100,
        0,
        0,
        0,
        26
      ]
    },
    {
      "name": "Insights/",
      "node": "Insights",
      "bars": [
        0,
        0,
        0,
        0,
        0,
        0,
        100
      ]
    },
    {
      "name": "Profiling/",
      "node": "Profiling",
      "bars": [
        0,
        100,
        42,
        0,
        0,
        0,
        2
      ]
    },
    {
      "name": "Tests/",
      "node": "Tests",
      "bars": [
        0,
        100,
        0,
        0,
        0,
        0,
        57
      ]
    },
    {
      "name": "UI/",
      "node": "UI",
      "bars": [
        0,
        100,
        5,
        0,
        0,
        0,
        0
      ]
    },
    {
      "name": "Web/",
      "node": "Web",
      "bars": [
        0,
        0,
        100,
        0,
        0,
        0,
        58
      ]
    },
    {
      "name": "lib/",
      "node": "lib",
      "bars": [
        0,
        0,
        0,
        0,
        0,
        0,
        0
      ]
    },
    {
      "name": "tools/preview/",
      "node": "tools_preview",
      "bars": [
        0,
        0,
        0,
        0,
        0,
        0,
        100
      ]
    },
    {
      "name": "tools/testing/",
      "node": "tools_testing",
      "bars": [
        0,
        0,
        0,
        0,
        0,
        0,
        100
      ]
    }
  ],
  "kpis": [
    {
      "label": "Frame budget",
      "value": "16.6",
      "unit": "ms",
      "delta": "60 fps ceiling",
      "tone": ""
    },
    {
      "label": "Lite overhead",
      "value": "< 1",
      "unit": "%",
      "delta": "~0.12 ms/tick measured",
      "tone": "sage"
    },
    {
      "label": "IL coverage",
      "value": "~100",
      "unit": "%",
      "delta": "vs 71.6% delegate path",
      "tone": "sage"
    },
    {
      "label": "Install RAM",
      "value": "1.0",
      "unit": "GB",
      "delta": "3.7 -> 1.0 (58 -> 30 KB/hook)",
      "tone": "sage"
    },
    {
      "label": "Dashboard tabs",
      "value": "6",
      "unit": "live",
      "delta": "~29 /api/* endpoints",
      "tone": ""
    },
    {
      "label": "Detectors",
      "value": "16",
      "unit": "",
      "delta": "13 live · 3 gated",
      "tone": "violet"
    },
    {
      "label": "L1 tests",
      "value": "~70",
      "unit": "pure-logic",
      "delta": "sub-second",
      "tone": ""
    },
    {
      "label": "Subsystems",
      "value": "9",
      "unit": "live",
      "delta": "5 noise nodes pruned",
      "tone": ""
    },
    {
      "label": "Last commit",
      "value": "2aa9e1c",
      "unit": "",
      "delta": "2026-06-25 · README rewrite",
      "tone": ""
    }
  ],
  "repoTree": {
    "name": "PerformanceProfiler/",
    "anno": "tModLoader 1.4.4 client mod: per-mod CPU/RAM/engagement profiler with a browser dashboard",
    "children": [
      {
        "name": "AGENTS.md",
        "anno": "Agent-facing brief mirroring CLAUDE.md (collaborator conventions)",
        "file": true
      },
      {
        "name": "CLAUDE.md",
        "anno": "Engineering-collaborator brief: the five invariants, dual-surface rule, standards",
        "file": true
      },
      {
        "name": "Data/",
        "anno": "THE pipeline: every number flows through one named typed stream (DataRegistry.Shared)",
        "node": "Data",
        "children": [
          {
            "name": "Aggregators/",
            "anno": "Fold many ticks into structured bins (per-mod, segment, heatmap, lag)",
            "children": [
              {
                "name": "EventAggregator.cs",
                "anno": "Per-dimension bucket stats (biome/boss/weather) for the Events surface",
                "file": true
              },
              {
                "name": "HeatmapAggregator.cs",
                "anno": "Per-minute frame-time heatmap buckets + boss-segment halo overlays",
                "file": true
              },
              {
                "name": "LagFingerprintAggregator.cs",
                "anno": "L1+L2 lag fingerprint clusters + cause-by-context cell matrix",
                "file": true
              },
              {
                "name": "LagRhythmAggregator.cs",
                "anno": "L7 inter-event interval histogram + rhythm clusters",
                "file": true
              },
              {
                "name": "PerModAttribution.cs",
                "anno": "Hot-path per-tick per-mod/per-hook CPU accumulator (indexed arrays, zero-alloc)",
                "file": true
              },
              {
                "name": "PerModCostTimeSeriesAggregator.cs",
                "anno": "F3 1Hz per-mod cost buckets in a 3600-bucket (one hour) ring",
                "file": true
              },
              {
                "name": "PerModSample.cs",
                "anno": "Per-tick per-mod CPU+allocation cost struct (tModLoader-free, testable)",
                "file": true
              },
              {
                "name": "PerModUsageAggregator.cs",
                "anno": "F2 per-mod session usage counters from events + per-tick context fold",
                "file": true
              },
              {
                "name": "PerTickAttributionRing.cs",
                "anno": "50-window ring of raw per-tick per-mod samples for spike drill-down",
                "file": true
              },
              {
                "name": "SegmentAggregator.cs",
                "anno": "Adapter exposing SegmentDetector + Store snapshots (open + closed)",
                "file": true
              },
              {
                "name": "Segments/",
                "anno": "Biome/boss/weather/invasion segment engine: detector, store, promoter, stats"
              },
              {
                "name": "SessionActivityHeatStripAggregator.cs",
                "anno": "T4 minute-bucketed activity-intensity heat strip",
                "file": true
              }
            ]
          },
          {
            "name": "Collectors/",
            "anno": "Raw per-tick signal capture (zero-alloc pull-side adapters)",
            "children": [
              {
                "name": "AllocationCollector.cs",
                "anno": "Snapshot of per-mod allocation bytes (null in Lite mode)",
                "file": true
              },
              {
                "name": "ContextTagger.cs",
                "anno": "Per-tick game-state snapshotter (biome/boss/weather/invasion/subworld)",
                "file": true
              },
              {
                "name": "FrameTimeCollector.cs",
                "anno": "Read-only adapter over MetricCollector's rolling frame-time history",
                "file": true
              },
              {
                "name": "HookCpuCollector.cs",
                "anno": "Snapshot of per-mod/per-category CPU arrays",
                "file": true
              },
              {
                "name": "ModRosterScanner.cs",
                "anno": "F1 install-time per-mod content roster, scanned once at PostSetupContent",
                "file": true
              }
            ]
          },
          {
            "name": "Contracts/",
            "anno": "Frozen snapshot types + stable stream-name constants",
            "children": [
              {
                "name": "RolloutContracts.cs",
                "anno": "Frozen v0.12 snapshot types + RolloutStreamNames constants",
                "file": true
              }
            ]
          },
          {
            "name": "DataRegistry.cs",
            "anno": "Process-wide stream registry (.Shared singleton); Freeze() snapshots per-tick callbacks",
            "file": true
          },
          {
            "name": "DataStage.cs",
            "anno": "Stage enum: Collector | Aggregator | Stat | Detector | Stream | Exporter",
            "file": true
          },
          {
            "name": "Detectors/",
            "anno": "Threshold logic + pattern firing (spike, stall) and the gated insight detector roster",
            "children": [
              {
                "name": "SpikeDetector.cs",
                "anno": "Frame-time spike detector (median+MAD); 50-window ring + peak attribution",
                "file": true
              },
              {
                "name": "StallDetector.cs",
                "anno": "Multi-tick stall detector with GC/OS-suspend/draw-thread cause classification",
                "file": true
              }
            ]
          },
          {
            "name": "IDataStream.cs",
            "anno": "Base + typed stream contracts + stage/per-tick marker interfaces",
            "file": true
          },
          {
            "name": "SessionContext.cs",
            "anno": "Immutable per-session record passed to each stream's Initialise",
            "file": true
          },
          {
            "name": "Stats/",
            "anno": "Derived OnDemand numbers the dashboard pulls",
            "children": [
              {
                "name": "AllocationCausalityStat.cs",
                "anno": "L6 per-stall 5s allocation-to-GC causality chain, top mods by bytes",
                "file": true
              },
              {
                "name": "Baseline.cs",
                "anno": "Per-session rolling baseline values detectors compare against",
                "file": true
              },
              {
                "name": "DeathReplayStat.cs",
                "anno": "T6 per-death 30s pre-death event-window reconstruction",
                "file": true
              },
              {
                "name": "EventsFeed.cs",
                "anno": "Pure feed builder + FeedEvent row (merges segments/spikes/stalls)",
                "file": true
              },
              {
                "name": "EventsFeedStat.cs",
                "anno": "/api/events pre-merged most-recent-first feed adapter",
                "file": true
              },
              {
                "name": "GcPressureStat.cs",
                "anno": "L3 gen0/1/2 rates, paused ms, heap-MB sparkline",
                "file": true
              },
              {
                "name": "HookCoverageView.cs",
                "anno": "Backend-aware projection of which coverage counters are live",
                "file": true
              },
              {
                "name": "KpiCalculator.cs",
                "anno": "Pure logic computing KpiSnapshot from a live MetricCollector",
                "file": true
              },
              {
                "name": "KpiSnapshot.cs",
                "anno": "Immutable headline-KPI value struct (fps, worst frame, spike/stall counts)",
                "file": true
              },
              {
                "name": "KpiStat.cs",
                "anno": "/api/now headline-KPI adapter; reference IDataStat implementation",
                "file": true
              },
              {
                "name": "ModImpactScorer.cs",
                "anno": "Per-mod Overview leaderboard row: composite ms-equivalent + components",
                "file": true
              },
              {
                "name": "PerModContextAttendanceStat.cs",
                "anno": "T5 per-mod biome/invasion/boss attendance roll-up (reads F2)",
                "file": true
              },
              {
                "name": "PerSegmentLagDensityStat.cs",
                "anno": "L4 spikes+stalls per segment as events/min vs baseline",
                "file": true
              },
              {
                "name": "SelfHealthStat.cs",
                "anno": "Process WorkingSet + per-hook overhead snapshot (by-value)",
                "file": true
              },
              {
                "name": "SessionChronicleStat.cs",
                "anno": "T7 timestamped factual sentences (Invariant-3-guarded vocabulary)",
                "file": true
              },
              {
                "name": "SpikesStat.cs",
                "anno": "Live-reference snapshot of this session's detected spike windows",
                "file": true
              },
              {
                "name": "StallsStat.cs",
                "anno": "Live-reference snapshot of this session's detected stall events",
                "file": true
              },
              {
                "name": "TransitionTrackStat.cs",
                "anno": "T3 contextTransitions rows projected onto the swimlane time domain",
                "file": true
              }
            ]
          },
          {
            "name": "Streams/",
            "anno": "LiteDB-backed persistence writers (one collection per stream)",
            "children": [
              {
                "name": "ContextTransitionStream.cs",
                "anno": "Persistence stream owning the contextTransitions collection",
                "file": true
              },
              {
                "name": "IPersistenceStream.cs",
                "anno": "Contract: Apply(DbWriteOp)/Reconstruct/EnsureIndexes per collection",
                "file": true
              },
              {
                "name": "InsightStream.cs",
                "anno": "Persistence stream owning the insights collection (scaffolded, unfed)",
                "file": true
              },
              {
                "name": "InteractionStreams.cs",
                "anno": "The interaction streams (damage/spawn/item/loadout/buff) in one file",
                "file": true
              },
              {
                "name": "ModlistStream.cs",
                "anno": "Stream owning the modlists/mods/worlds identity tables",
                "file": true
              },
              {
                "name": "PerSessionAggregateStream.cs",
                "anno": "Per-session per-mod + per-hook aggregate batches (wipe-and-insert)",
                "file": true
              },
              {
                "name": "PlayerDeathStream.cs",
                "anno": "Persistence stream owning the playerDeaths collection",
                "file": true
              },
              {
                "name": "SegmentStream.cs",
                "anno": "Stream owning segments; indexed on SessionId and (Family, Key)",
                "file": true
              },
              {
                "name": "SessionRecorder.cs",
                "anno": "Per-world recorder orchestrator; game-thread caller, enqueues writer ops",
                "file": true
              },
              {
                "name": "SessionStream.cs",
                "anno": "Stream owning the sessions collection (SessionStart/SessionEnd)",
                "file": true
              },
              {
                "name": "SpikeStream.cs",
                "anno": "Persistence stream owning the spikeWindows collection",
                "file": true
              },
              {
                "name": "StallClusterStream.cs",
                "anno": "Persistence stream owning the stallClusters collection",
                "file": true
              },
              {
                "name": "StallStream.cs",
                "anno": "Persistence stream owning the stallEvents collection",
                "file": true
              },
              {
                "name": "StreamJson.cs",
                "anno": "Shared JsonSerializer options for stream journal reconstruction",
                "file": true
              },
              {
                "name": "StreamRegistry.cs",
                "anno": "Maps DbOpKind -> IPersistenceStream; O(1) dispatch, one-line to extend",
                "file": true
              },
              {
                "name": "TickAggregateStream.cs",
                "anno": "Stream owning the three tick-aggregate tiers (warm/cold/archive)",
                "file": true
              },
              {
                "name": "WorldSnapshotStream.cs",
                "anno": "Persistence stream owning the worldSnapshots collection",
                "file": true
              }
            ]
          },
          {
            "name": "TickContext.cs",
            "anno": "Readonly ref struct passed to per-tick callbacks (stack-only, zero-alloc)",
            "file": true
          }
        ]
      },
      {
        "name": "Insights/",
        "anno": "Top-level interpretation module: engine, 16 detectors, reference frames, ranking",
        "node": "Insights",
        "children": [
          {
            "name": "CollectorInsightInput.cs",
            "anno": "Adapts MetricCollector to IInsightInput (the pure-logic testability seam)",
            "file": true
          },
          {
            "name": "Contracts/",
            "anno": "Frozen snapshot types + stable stream-name constants",
            "children": [
              {
                "name": "IDriver.cs",
                "anno": "Driver interface: samples a workload signal an insight can regress against",
                "file": true
              },
              {
                "name": "IInsightInput.cs",
                "anno": "Read-only metric surface detectors consume (decouples from MetricCollector)",
                "file": true
              },
              {
                "name": "IReferenceFrame.cs",
                "anno": "Reference-frame interface: Expected centre + Dispersion spread (the spine law)",
                "file": true
              }
            ]
          },
          {
            "name": "Detectors/",
            "anno": "Threshold logic + pattern firing (spike, stall) and the gated insight detector roster",
            "children": [
              {
                "name": "AllocationBurstDetector.cs",
                "anno": "Deviation: mod share of session allocation throughput (Standard/Deep only)",
                "file": true
              },
              {
                "name": "ContextConditionalCostDetector.cs",
                "anno": "Structure: in-context vs out-of-context per-mod cost (Bonferroni-corrected)",
                "file": true
              },
              {
                "name": "ContextCorrelatedSpikeDetector.cs",
                "anno": "Structure: context spike-share vs dwell-share (Bonferroni-corrected)",
                "file": true
              },
              {
                "name": "CostConcentrationDetector.cs",
                "anno": "Structure: Pareto count of mods carrying >=70% of cost",
                "file": true
              },
              {
                "name": "FrameHeadroomDetector.cs",
                "anno": "Headroom: median frame vs 16.67 ms 60 fps ceiling",
                "file": true
              },
              {
                "name": "FrameJitterDetector.cs",
                "anno": "Distribution: robust frame-time CV (MAD/median)",
                "file": true
              },
              {
                "name": "FreeRemovalCandidateDetector.cs",
                "anno": "Deviation (gated engagement-signal): cheap-cost detection, NeedsPersistence",
                "file": true
              },
              {
                "name": "GatedDetectors.cs",
                "anno": "Holds HookFrequencyTailDetector (gated per-hook-call-counts); rest moved out",
                "file": true
              },
              {
                "name": "GcPauseCulpritDetector.cs",
                "anno": "Deviation: top-mod alloc share in 60-tick pre-GC window",
                "file": true
              },
              {
                "name": "HeapLeakDetector.cs",
                "anno": "Temporal: late vs early heap controlling for entity ratio",
                "file": true
              },
              {
                "name": "HotHookDominanceDetector.cs",
                "anno": "Deviation: hook share of a mod's session cost",
                "file": true
              },
              {
                "name": "InteractionInsightDetectors.cs",
                "anno": "LoadoutCorrelatedCost, EventConditionalCost, LoadoutCombinationCost (gated)",
                "file": true
              },
              {
                "name": "NewContributorDetector.cs",
                "anno": "Temporal: idle-early to active-late per-mod (Bonferroni-corrected)",
                "file": true
              },
              {
                "name": "PeakContributorToSpikeDetector.cs",
                "anno": "Deviation: top-mod share of a spike's per-mod snapshot",
                "file": true
              },
              {
                "name": "SegmentDeathCorrelationDetector.cs",
                "anno": "Segment: death-containing vs clean segment ms/tick",
                "file": true
              },
              {
                "name": "SegmentOutlierDetector.cs",
                "anno": "Segment: a segment vs lifetime avg for its (family, key)",
                "file": true
              },
              {
                "name": "SegmentTopModDetector.cs",
                "anno": "Segment: mod's #1-rank frequency across a segment class",
                "file": true
              },
              {
                "name": "SustainedCostShiftDetector.cs",
                "anno": "Temporal: early vs late per-mod cost (Bonferroni-corrected)",
                "file": true
              }
            ]
          },
          {
            "name": "Drivers/",
            "anno": "Workload drivers a detector can regress cost against",
            "children": [
              {
                "name": "Drivers.cs",
                "anno": "EntityCountDriver, SessionAgeDriver, HeapDriver (sample IInsightInput)",
                "file": true
              }
            ]
          },
          {
            "name": "IInsightDetector.cs",
            "anno": "Detector interface: Pattern / IsAvailable / IsGated / GatedOn / Evaluate",
            "file": true
          },
          {
            "name": "Insight.cs",
            "anno": "Insight record + all enums (PatternKey, Confidence, EvidenceScope, Magnitude)",
            "file": true
          },
          {
            "name": "InsightRenderer.cs",
            "anno": "Slot-filling templates; banned-vocabulary header enforces the honesty contract",
            "file": true
          },
          {
            "name": "InsightStore.cs",
            "anno": "Live/history store: dedup, TTL eviction, confidence promotion, ranking",
            "file": true
          },
          {
            "name": "InsightsEngine.cs",
            "anno": "Detector roster + Evaluate pass + reference-frame substrate + Shared singleton",
            "file": true
          },
          {
            "name": "Publish/",
            "anno": "Seven pipeline-facing stats composing the dashboard Insights tab",
            "children": [
              {
                "name": "CrossCuttingSignalStat.cs",
                "anno": "I5: groups live insights by PatternKey into per-pattern leaderboards",
                "file": true
              },
              {
                "name": "DormantSurfaceStat.cs",
                "anno": "I2: normalised per-mod active-use intensity (the dormant dust shelf)",
                "file": true
              },
              {
                "name": "EngagementCostScatterStat.cs",
                "anno": "I6: per-mod engagement-vs-cost scatter points",
                "file": true
              },
              {
                "name": "InsightsStat.cs",
                "anno": "/api/insights: ranked live insight rows via Store.AllLive + InsightRenderer",
                "file": true
              },
              {
                "name": "ModInteractionAggregator.cs",
                "anno": "I7: Pearson correlation matrix over per-mod cost series",
                "file": true
              },
              {
                "name": "ModObservatoryStat.cs",
                "anno": "I1+I3+I4: per-mod roster + usage + CPU + loadout observatory",
                "file": true
              }
            ]
          },
          {
            "name": "RankingScorer.cs",
            "anno": "Stateless 6-component weighted score (share/ratio regime split)",
            "file": true
          },
          {
            "name": "ReferenceFrames/",
            "anno": "Reference frames + cross-session durability substrate",
            "children": [
              {
                "name": "ContextBaseline.cs",
                "anno": "Family A: per-context per-mod cost distribution; bounded 16-bucket eviction",
                "file": true
              },
              {
                "name": "CrossSessionStore.cs",
                "anno": "Persists/seeds context baselines to LiteDB keyed by modlist fingerprint",
                "file": true
              },
              {
                "name": "TemporalBaseline.cs",
                "anno": "Family B: frozen early vs late window; carries entity count for confound control",
                "file": true
              }
            ]
          },
          {
            "name": "Shared/",
            "anno": "Pure-logic primitives shared across detectors",
            "children": [
              {
                "name": "ModMetrics.cs",
                "anno": "Per-mod usage/creation weights (active-use ticks; post-Flute-bug definition)",
                "file": true
              },
              {
                "name": "ModNames.cs",
                "anno": "Mod-id to display-name resolution",
                "file": true
              },
              {
                "name": "Shares.cs",
                "anno": "Share/fraction computation helpers over per-mod totals",
                "file": true
              },
              {
                "name": "Stats.cs",
                "anno": "RunningStat (Welford/Chan), Cohen's d, Welch t-test (the statistical core)",
                "file": true
              }
            ]
          }
        ]
      },
      {
        "name": "LICENSE",
        "anno": "MIT licence",
        "file": true
      },
      {
        "name": "Localization/",
        "anno": "Build/design artefact",
        "node": "Localization",
        "children": [
          {
            "name": "en-US_Mods.PerformanceProfiler.hjson",
            "anno": "English localisation strings",
            "file": true
          }
        ]
      },
      {
        "name": "PerformanceProfiler.cs",
        "anno": "Mod entry: Load opens LiteDB + binds dashboard + RegisterDataPipeline; Unload tears ILHook down; hosts ProfilerPlayer (F9)",
        "file": true
      },
      {
        "name": "PerformanceProfiler.csproj",
        "anno": "Main mod project (excludes Tests/** from compile)",
        "file": true
      },
      {
        "name": "ProfilerConfig.cs",
        "anno": "ModConfig — now empty (overlay-era knobs removed in v0.9.0)",
        "file": true
      },
      {
        "name": "Profiling/",
        "anno": "Measurement infrastructure: hook backends, MetricCollector, ProbeStack, Events, Persistence DB",
        "node": "Profiling",
        "children": [
          {
            "name": "EnumStringTable.cs",
            "anno": "Pre-built enum-to-string arrays; kills per-render boxing/alloc",
            "file": true
          },
          {
            "name": "Events/",
            "anno": "Per-tick game-state context support structs (not pipeline streams)",
            "children": [
              {
                "name": "BiomeBitset.cs",
                "anno": "Per-tick 'biome N active?' bitset; allocated once at install",
                "file": true
              },
              {
                "name": "BiomeDescriptor.cs",
                "anno": "One biome registry entry: bit id, display + canonical name, source mod",
                "file": true
              },
              {
                "name": "BiomeRegistry.cs",
                "anno": "Enumerates vanilla (reflection) + modded biomes once at PostSetupContent",
                "file": true
              },
              {
                "name": "BossSampler.cs",
                "anno": "Scans Main.npc[] for active boss; dedupes multi-segment via NPC.realLife",
                "file": true
              },
              {
                "name": "BossSlotArray.cs",
                "anno": "By-value 8-slot active-boss-type buffer inside EventContext; zero per-tick alloc",
                "file": true
              },
              {
                "name": "BucketStats.cs",
                "anno": "Running per-bucket frame-time stats: count, mean/std sums, peak, spikes",
                "file": true
              },
              {
                "name": "EventContext.cs",
                "anno": "Per-tick value struct: biome/boss/weather/invasion/subworld/mode snapshot",
                "file": true
              },
              {
                "name": "GameMode.cs",
                "anno": "Mirror of Terraria's four difficulty modes; read from Main.GameModeInfo",
                "file": true
              },
              {
                "name": "InvasionId.cs",
                "anno": "Vanilla invasion ids (+ OldOnesArmy); modded invasions a known gap",
                "file": true
              },
              {
                "name": "SubworldProbe.cs",
                "anno": "Optional reflection probe for SubworldLibrary; abort-clean if absent",
                "file": true
              },
              {
                "name": "WeatherFlags.cs",
                "anno": "Vanilla weather/moon/event flags packed into one bitset",
                "file": true
              },
              {
                "name": "WeatherSources.cs",
                "anno": "Declarative table mapping each WeatherFlags bit to its vanilla boolean",
                "file": true
              }
            ]
          },
          {
            "name": "HookBackend.cs",
            "anno": "Mode flags (Delegate/ILHook/Parallel) + AllocationTracking switch",
            "file": true
          },
          {
            "name": "HookCategoryRouter.cs",
            "anno": "Shared type-to-category map (seven ids); both backends call ResolveCategory",
            "file": true
          },
          {
            "name": "HookInterceptor.cs",
            "anno": "Delegate-pair backend: MonoModHooks.Add per matched signature (~71.6%)",
            "file": true
          },
          {
            "name": "HookSurfaceCache.cs",
            "anno": "Process-scoped GetLoadableTypes cache shared by both backends",
            "file": true
          },
          {
            "name": "ILHookInterceptor.cs",
            "anno": "IL backend (default ~100%): per-method ILHook + ProbeStack timing wrap",
            "file": true
          },
          {
            "name": "LangNameCache.cs",
            "anno": "Pre-resolves Lang names into flat string[]; one indexer read per event",
            "file": true
          },
          {
            "name": "MetricCollector.cs",
            "anno": "Per-tick frame engine: BeginTick/EndTick, ring buffer, spike detector",
            "file": true
          },
          {
            "name": "ModOwnerCache.cs",
            "anno": "Lazy 'which mod owns this id' cache keyed by (kind, id); vanilla -> Terraria",
            "file": true
          },
          {
            "name": "ModRamReader.cs",
            "anno": "Guarded reflection into tML's per-mod RAM estimates; abort-clean read-only",
            "file": true
          },
          {
            "name": "Persistence/",
            "anno": "DB infrastructure: LiteDB facade, writer thread, journal, side-channel detectors, Records",
            "children": [
              {
                "name": "BsonShortNames.cs",
                "anno": "BsonMapper remap of every record property to a short BSON field name",
                "file": true
              },
              {
                "name": "Commands/",
                "anno": "Chat commands that read the profiler DB (/profiler-summary etc.)"
              },
              {
                "name": "ContextTransitionWatcher.cs",
                "anno": "Diffs each tick's EventContext, emits contextTransitions rows on change",
                "file": true
              },
              {
                "name": "DbReadModel.cs",
                "anno": "Cached read-only view of the last ended session; no-world dashboard fallback",
                "file": true
              },
              {
                "name": "DbWriteOp.cs",
                "anno": "Producer op shape + DbOpKind discriminator the writer thread dispatches on",
                "file": true
              },
              {
                "name": "DbWriterThread.cs",
                "anno": "Single background thread owning every LiteDB write; channel enqueue",
                "file": true
              },
              {
                "name": "EventJournal.cs",
                "anno": "Append-only NDJSON redo log; layer-2 crash safety, replayed on next launch",
                "file": true
              },
              {
                "name": "Interactions/",
                "anno": "Generic vanilla/tML event capture (item/NPC/player), Invariant-5 clean"
              },
              {
                "name": "LegacyJsonImporter.cs",
                "anno": "One-shot ingest of legacy Sessions/*.json into the new schema, then archives",
                "file": true
              },
              {
                "name": "Migrations.cs",
                "anno": "Idempotent LiteDB user-version schema migrations",
                "file": true
              },
              {
                "name": "ModlistFingerprint.cs",
                "anno": "Stable hex digest of (id,name,version) tuples; dedupe key for the same modlist",
                "file": true
              },
              {
                "name": "PersistenceFileNames.cs",
                "anno": "File-name constants split out so tests avoid ProfilerPaths' Terraria dependency",
                "file": true
              },
              {
                "name": "PlayerDeathDetector.cs",
                "anno": "Diffs Player.dead edge; captures position/HP/bosses + damage-weighted killer",
                "file": true
              },
              {
                "name": "ProfilerCompactCommand.cs",
                "anno": "/profiler-compact: LiteDB Checkpoint+Rebuild; refuses inside a world",
                "file": true
              },
              {
                "name": "ProfilerDatabase.cs",
                "anno": "Facade over LiteDatabase + journal + writer thread; one per Mod.Load/Unload",
                "file": true
              },
              {
                "name": "ProfilerPaths.cs",
                "anno": "Cross-platform persistence-root resolution under tModLoader's SavePath",
                "file": true
              },
              {
                "name": "Records/",
                "anno": "One BSON row shape per LiteDB collection (sessions, spikes, deaths, etc.)"
              },
              {
                "name": "SessionSummaryLogger.cs",
                "anno": "Writes a multi-line session-end summary block to client.log on world unload",
                "file": true
              },
              {
                "name": "TickDownsampler.cs",
                "anno": "Folds per-tick frames into 1Hz warm + 1/min cold rows; alloc-quiet on game thread",
                "file": true
              },
              {
                "name": "WorldSnapshotter.cs",
                "anno": "Emits a WorldSnapshotRow every ~30s: position/HP/biome/boss/entity counts",
                "file": true
              }
            ]
          },
          {
            "name": "Pools/",
            "anno": "Object/list pooling primitives the persistence emit path uses",
            "children": [
              {
                "name": "IPoolReset.cs",
                "anno": "Reset contract every poolable row implements; branch-free, alloc-free",
                "file": true
              },
              {
                "name": "ListPool.cs",
                "anno": "Thread-safe List<T> pool; take/clear/return cycle for hot-path lists",
                "file": true
              },
              {
                "name": "RowPool.cs",
                "anno": "Thread-safe Rent/Return pool for record rows; cuts per-emit game-thread allocs",
                "file": true
              }
            ]
          },
          {
            "name": "ProbeStack.cs",
            "anno": "Static Enter/Leave[CpuAlloc] called from emitted IL; credits PerModAttribution",
            "file": true
          },
          {
            "name": "ProfilerFocusProbe.cs",
            "anno": "Reads Main.hasFocus per tick so StallDetector separates OS-suspend from freeze",
            "file": true
          },
          {
            "name": "ProfilerSelfHealth.cs",
            "anno": "Measures the profiler's own install-delta + bytes-per-hook footprint",
            "file": true
          },
          {
            "name": "ProfilerSystem.cs",
            "anno": "ModSystem lifecycle owner; drives BeginTick/EndTick + frozen per-tick callbacks",
            "file": true
          },
          {
            "name": "RingBuffer.cs",
            "anno": "Generic fixed-capacity circular buffer (TickFrame[1800] = 30s at 60Hz)",
            "file": true
          },
          {
            "name": "TickFrame.cs",
            "anno": "Per-tick observation struct: frame ms, alloc bytes, entity counts, context",
            "file": true
          },
          {
            "name": "Time.cs",
            "anno": "Stopwatch-backed Unix-ms clock; alloc/boxing-free replacement for UtcNow",
            "file": true
          },
          {
            "name": "Util/",
            "anno": "Small allocation-avoidance primitives",
            "children": [
              {
                "name": "BoolIndex.cs",
                "anno": "Fixed bool[] set-membership probe; O(1) replaces Array.IndexOf in the buff-diff hot path",
                "file": true
              }
            ]
          }
        ]
      },
      {
        "name": "README.md",
        "anno": "Directional source of truth: what the mod is, the six tabs, the trust posture",
        "file": true
      },
      {
        "name": "Tests/",
        "anno": "Non-shipping xUnit L1 pure-logic harness (Compile-Include + Link)",
        "node": "Tests",
        "children": [
          {
            "name": "BaselineTests.cs",
            "anno": "Pins the per-session rolling baseline behind the relative spike threshold",
            "file": true
          },
          {
            "name": "BoolIndexTests.cs",
            "anno": "Pins the BoolIndex bitset set-membership helper",
            "file": true
          },
          {
            "name": "HookInstallRetentionDiagnostics.cs",
            "anno": "Diagnostic: install-RAM measurement conflates retained vs transient garbage",
            "file": true
          },
          {
            "name": "InsightStoreTests.cs",
            "anno": "Pins p-value-gated confidence promotion + Submit dedup",
            "file": true
          },
          {
            "name": "Insights/",
            "anno": "Top-level interpretation module: engine, 16 detectors, reference frames, ranking",
            "children": [
              {
                "name": "CrossSessionStoreTests.cs",
                "anno": "Pins the LiteDB round-trip of per-context baselines",
                "file": true
              },
              {
                "name": "ReferenceFrameTests.cs",
                "anno": "Pins the reference-frame substrate (Stats, ContextBaseline)",
                "file": true
              },
              {
                "name": "SharedPrimitivesTests.cs",
                "anno": "Pins the Insights/Shared primitives (ModMetrics, Shares, ModNames)",
                "file": true
              },
              {
                "name": "TemporalBaselineTests.cs",
                "anno": "Pins the family-B early/late temporal baseline + driver contracts",
                "file": true
              }
            ]
          },
          {
            "name": "PerformanceProfiler.Tests.csproj",
            "anno": "xUnit project; Compile-Include+Link lifts pure-logic source, no ProjectReference",
            "file": true
          },
          {
            "name": "Persistence/",
            "anno": "DB infrastructure: LiteDB facade, writer thread, journal, side-channel detectors, Records",
            "children": [
              {
                "name": "PersistenceBenchmarkTests.cs",
                "anno": "LiteDB write throughput/latency under the persistence layer",
                "file": true
              },
              {
                "name": "PersistenceRoundTripTests.cs",
                "anno": "LiteDB write -> read fidelity across the streams",
                "file": true
              }
            ]
          },
          {
            "name": "PoolsTests.cs",
            "anno": "Pins RowPool/ListPool — the per-tick zero-alloc contract",
            "file": true
          },
          {
            "name": "RankingScorerTests.cs",
            "anno": "Pins the share-vs-ratio magnitude split (90% now outranks 40%)",
            "file": true
          },
          {
            "name": "RingBufferTests.cs",
            "anno": "Pins ring-buffer wrap-around (the 30s history + 50-window spike ring)",
            "file": true
          },
          {
            "name": "StallClassifierTests.cs",
            "anno": "Pins stall cause classification",
            "file": true
          },
          {
            "name": "StallDetectorTests.cs",
            "anno": "Pins stall-window detection over the per-tick stream",
            "file": true
          },
          {
            "name": "TimeTests.cs",
            "anno": "Pins the Stopwatch-based Time.UnixMsNow helper",
            "file": true
          },
          {
            "name": "_TestNamespaceStubs.cs",
            "anno": "xUnit serial-execution config + empty namespace stubs (no tests)",
            "file": true
          },
          {
            "name": "bin/",
            "anno": "Build/runtime artefact (gitignored)",
            "children": [
              {
                "name": "Debug/",
                "anno": "Build/runtime artefact (gitignored)"
              }
            ]
          },
          {
            "name": "obj/",
            "anno": "Build/runtime artefact (gitignored)",
            "children": [
              {
                "name": "Debug/",
                "anno": "Build/runtime artefact (gitignored)"
              },
              {
                "name": "PerformanceProfiler.Tests.csproj.nuget.dgspec.json",
                "anno": "NuGet restore artefact (gitignored)",
                "file": true
              },
              {
                "name": "PerformanceProfiler.Tests.csproj.nuget.g.props",
                "anno": "Build/design artefact",
                "file": true
              },
              {
                "name": "PerformanceProfiler.Tests.csproj.nuget.g.targets",
                "anno": "Build/design artefact",
                "file": true
              },
              {
                "name": "project.assets.json",
                "anno": "NuGet restore artefact (gitignored)",
                "file": true
              },
              {
                "name": "project.nuget.cache",
                "anno": "NuGet restore artefact (gitignored)",
                "file": true
              }
            ]
          }
        ]
      },
      {
        "name": "UI/",
        "anno": "ARCHIVED in-game overlay (kept for a Steam-Deck revival; only the F9 keybind is live)",
        "node": "UI",
        "children": [
          {
            "name": "Overlay/",
            "anno": "Archived tab framework + five tabs + draw components (not in the player path)",
            "children": [
              {
                "name": "Components/",
                "anno": "Archived overlay draw widgets (donut, sparkline, heat bar, cards, badges)"
              },
              {
                "name": "IOverlayTab.cs",
                "anno": "Archived overlay tab interface",
                "file": true
              },
              {
                "name": "OverlayDraw.cs",
                "anno": "Archived overlay primitive draw helpers",
                "file": true
              },
              {
                "name": "OverlayLayout.cs",
                "anno": "Archived overlay layout maths",
                "file": true
              },
              {
                "name": "OverlayMode.cs",
                "anno": "Archived overlay mode enum",
                "file": true
              },
              {
                "name": "OverlayPanel.cs",
                "anno": "Archived overlay panel container",
                "file": true
              },
              {
                "name": "OverlayState.cs",
                "anno": "Archived overlay state (active tab, MetricMode CPU/MEM/BOTH)",
                "file": true
              },
              {
                "name": "TabRegistry.cs",
                "anno": "Archived overlay tab registry (not instantiated since v0.9.0)",
                "file": true
              },
              {
                "name": "Tabs/",
                "anno": "Archived in-game tabs (Overview/Tree/Spikes/Events/Self/Timeline/Insights)"
              }
            ]
          },
          {
            "name": "ProfilerOverlay.cs",
            "anno": "Archived in-game overlay root draw",
            "file": true
          },
          {
            "name": "ProfilerOverlaySystem.cs",
            "anno": "Live only as the F9 OpenDashboard keybind registrar (rest archived)",
            "file": true
          },
          {
            "name": "ProfilerTheme.cs",
            "anno": "Archived overlay colour/font theme constants",
            "file": true
          }
        ]
      },
      {
        "name": "Web/",
        "anno": "The player surface: loopback HTTP server + six-tab SPA, ~29 /api/* routes",
        "node": "Web",
        "children": [
          {
            "name": "Assets/",
            "anno": "SPA bundle source: HTML shell partials + CSS/JS fragments, byte-cached once",
            "children": [
              {
                "name": "Css/",
                "anno": "Stylesheet fragments (21 partials) concatenated into /dashboard.css"
              },
              {
                "name": "DashboardAssets.cs",
                "anno": "Concatenates CSS/JS/HTML fragments in fixed order; caches the SPA bundle once",
                "file": true
              },
              {
                "name": "IndexHtml.Closing.cs",
                "anno": "Closes the main element; persistent footer strip",
                "file": true
              },
              {
                "name": "IndexHtml.Insights.cs",
                "anno": "Insights tab-pane markup (panel chrome only)",
                "file": true
              },
              {
                "name": "IndexHtml.Lag.cs",
                "anno": "Lag tab-pane markup (panel chrome only)",
                "file": true
              },
              {
                "name": "IndexHtml.Memory.cs",
                "anno": "Memory tab-pane markup (panel chrome only)",
                "file": true
              },
              {
                "name": "IndexHtml.Preamble.cs",
                "anno": "HTML head, font links, top bar, six-button tab strip, state overlays",
                "file": true
              },
              {
                "name": "IndexHtml.Self.cs",
                "anno": "Self tab-pane markup (panel chrome only)",
                "file": true
              },
              {
                "name": "IndexHtml.Summary.cs",
                "anno": "Summary tab-pane markup (panel chrome only; JS fills content)",
                "file": true
              },
              {
                "name": "IndexHtml.Timeline.cs",
                "anno": "Timeline tab-pane markup (panel chrome only)",
                "file": true
              },
              {
                "name": "IndexHtml.cs",
                "anno": "Assembles the SPA HTML shell from its partials into one static string",
                "file": true
              },
              {
                "name": "Js/",
                "anno": "Script fragments (18 partials) concatenated into /dashboard.js"
              }
            ]
          },
          {
            "name": "DashboardRouter.Hooks.cs",
            "anno": "BuildHooks: per-hook drill-down rows (HookCpu+Allocation), zero-cost skipped",
            "file": true
          },
          {
            "name": "DashboardRouter.Insights.cs",
            "anno": "BuildInsights + the five Publish-backed Insights endpoints",
            "file": true
          },
          {
            "name": "DashboardRouter.Lag.cs",
            "anno": "Lag builders: spikes, stalls, clusters, GC pressure, density, causality, rhythm",
            "file": true
          },
          {
            "name": "DashboardRouter.Memory.cs",
            "anno": "BuildMemory: joins install-delta scaffolding with tML's per-mod RAM",
            "file": true
          },
          {
            "name": "DashboardRouter.Mods.cs",
            "anno": "BuildMods: per-mod CPU+alloc table rows for the Summary mods view",
            "file": true
          },
          {
            "name": "DashboardRouter.Self.cs",
            "anno": "BuildSelf: profiler self-health (overhead, footprint, hook counts)",
            "file": true
          },
          {
            "name": "DashboardRouter.Summary.cs",
            "anno": "Summary builders: now, frames, segments, heatmap, events",
            "file": true
          },
          {
            "name": "DashboardRouter.Timeline.cs",
            "anno": "Timeline builders: lifetime, attribution, transitions, attendance, deaths, chronicle",
            "file": true
          },
          {
            "name": "DashboardRouter.cs",
            "anno": "Strict GET-only route switch (34 arms); asset byte cache + TopContributors helper",
            "file": true
          },
          {
            "name": "Server/",
            "anno": "Hand-rolled HTTP/1.1 stack: server, request, response",
            "children": [
              {
                "name": "DashboardHttpServer.cs",
                "anno": "Raw-TCP loopback HTTP/1.1 server (127.0.0.1:27277); GET-only, thread-per-request",
                "file": true
              },
              {
                "name": "HttpRequest.cs",
                "anno": "Parsed inbound request (method, path, raw target); query string stripped",
                "file": true
              },
              {
                "name": "HttpResponse.cs",
                "anno": "Outbound response + Html/Json/PlainText/NotFound factory helpers",
                "file": true
              }
            ]
          }
        ]
      },
      {
        "name": "bin/",
        "anno": "Build/runtime artefact (gitignored)",
        "children": [
          {
            "name": "Debug/",
            "anno": "Build/runtime artefact (gitignored)",
            "node": "bin_Debug",
            "children": [
              {
                "name": "net8.0/",
                "anno": "Build/runtime artefact (gitignored)"
              }
            ]
          },
          {
            "name": "Release/",
            "anno": "Build/runtime artefact (gitignored)",
            "node": "bin_Release",
            "children": [
              {
                "name": "net8.0/",
                "anno": "Build/runtime artefact (gitignored)"
              }
            ]
          }
        ]
      },
      {
        "name": "build.txt",
        "anno": "tModLoader manifest: version=0.22.0; buildIgnore excludes Tests/tools/context/*.md; dllReferences=LiteDB",
        "file": true
      },
      {
        "name": "context/",
        "anno": "Repository implementation memory (this folder)",
        "children": [
          {
            "name": "_Overview.md",
            "anno": "Context-folder entry point + map",
            "file": true
          },
          {
            "name": "_staleness-report.md",
            "anno": "Per-file staleness verdicts from the last upkeep-context pass",
            "file": true
          },
          {
            "name": "arch/",
            "anno": "This interactive architecture explorer (data.js is the only project-specific file)",
            "children": [
              {
                "name": "app.js",
                "anno": "Arch-explorer renderer shell (vendored; not edited)",
                "file": true
              },
              {
                "name": "features.js",
                "anno": "Arch-explorer feature shell (vendored; not edited)",
                "file": true
              },
              {
                "name": "graph.js",
                "anno": "Arch-explorer dependency-graph shell (vendored; not edited)",
                "file": true
              },
              {
                "name": "index.html",
                "anno": "Arch-explorer HTML shell (vendored; not edited)",
                "file": true
              },
              {
                "name": "styles.css",
                "anno": "Arch-explorer stylesheet (vendored; not edited)",
                "file": true
              }
            ]
          },
          {
            "name": "architecture.md",
            "anno": "The markdown structural map this explorer is generated from",
            "file": true
          },
          {
            "name": "integration/",
            "anno": "Cross-component maps (which subsystem plugs into which tML API)",
            "children": [
              {
                "name": "integration-map.md",
                "anno": "Per-component plug-in points + hot-path chain + invariant enforcement tables",
                "file": true
              }
            ]
          },
          {
            "name": "notes/",
            "anno": "Topical inbox: decisions, conventions, philosophy, future-work sketches",
            "children": [
              {
                "name": "compile-gate.md",
                "anno": "The off-game compile/test verification gate",
                "file": true
              },
              {
                "name": "conventions.md",
                "anno": "Codebase conventions (hot-path, logging, naming)",
                "file": true
              },
              {
                "name": "decisions.md",
                "anno": "Accepted design decisions + rationale",
                "file": true
              },
              {
                "name": "future-html-report.md",
                "anno": "Sketch: post-session shareable HTML report",
                "file": true
              },
              {
                "name": "future-insights-rework.md",
                "anno": "Sketch that drove the v0.13-v0.22 insights rework",
                "file": true
              },
              {
                "name": "future-settings-design.md",
                "anno": "Sketch: player-facing settings UI (backend/alloc/threshold toggles)",
                "file": true
              },
              {
                "name": "future-unified-data-interface.md",
                "anno": "Framing note for the Data/ pipeline (folder-reorg + runtime registry)",
                "file": true
              },
              {
                "name": "insights-rework-status.md",
                "anno": "Status log of the insights rework",
                "file": true
              },
              {
                "name": "modlist-pre-upgrade-2026-06-22.md",
                "anno": "Snapshot of the 99-mod pre-upgrade play modlist",
                "file": true
              },
              {
                "name": "philosophy.md",
                "anno": "The two-stack posture + the descriptive-not-prescriptive stance",
                "file": true
              },
              {
                "name": "ui-overhaul-plan.md",
                "anno": "Plan for the dashboard-first pivot + component-library rebuild",
                "file": true
              }
            ]
          },
          {
            "name": "notes.md",
            "anno": "Context notes index",
            "file": true
          },
          {
            "name": "pages/",
            "anno": "Per-tab dashboard dossiers written by the L4/L6/L8 audit harness",
            "children": [
              {
                "name": "_index.md",
                "anno": "Pages-folder index",
                "file": true
              },
              {
                "name": "insights.md",
                "anno": "Insights-tab dossier",
                "file": true
              },
              {
                "name": "lag.md",
                "anno": "Lag-tab dossier",
                "file": true
              },
              {
                "name": "memory.md",
                "anno": "Memory-tab dossier",
                "file": true
              },
              {
                "name": "self.md",
                "anno": "Self-tab dossier",
                "file": true
              },
              {
                "name": "summary.md",
                "anno": "Summary-tab dossier",
                "file": true
              },
              {
                "name": "timeline.md",
                "anno": "Timeline-tab dossier",
                "file": true
              }
            ]
          },
          {
            "name": "perf-pass/",
            "anno": "v0.5->v0.6 performance-research record (baseline/deferred/verification)",
            "children": [
              {
                "name": "baseline.md",
                "anno": "Performance baseline measurements",
                "file": true
              },
              {
                "name": "deferred.md",
                "anno": "Deferred perf items",
                "file": true
              },
              {
                "name": "verification.md",
                "anno": "Perf verification record",
                "file": true
              }
            ]
          },
          {
            "name": "plans/",
            "anno": "Substantial plan files (audit, testing, insights, RAM, UI library)",
            "children": [
              {
                "name": "code-health-audit/",
                "anno": "Code-health audit findings + implementation receipt"
              },
              {
                "name": "extensive-testing-infrastructure.md",
                "anno": "The L1/L4/L6/L8 layered testing strategy plan",
                "file": true
              },
              {
                "name": "insights-engine.md",
                "anno": "Insights-engine subsystem doc / plan (per-folder)",
                "file": true
              },
              {
                "name": "install-ram-optimisation.md",
                "anno": "The 3.7->1.0 GB install-RAM optimisation plan",
                "file": true
              },
              {
                "name": "ui-component-library.md",
                "anno": "The OKLCH component-library build plan",
                "file": true
              }
            ]
          },
          {
            "name": "systems/",
            "anno": "Per-subsystem deep dives (the canonical reality)",
            "children": [
              {
                "name": "allocation-tracking.md",
                "anno": "Subsystem doc: allocation tracking",
                "file": true
              },
              {
                "name": "data-pipeline.md",
                "anno": "Subsystem doc: the Data/ pipeline",
                "file": true
              },
              {
                "name": "events-and-context.md",
                "anno": "Subsystem doc: events and context",
                "file": true
              },
              {
                "name": "hook-instrumentation.md",
                "anno": "Subsystem doc: hook instrumentation",
                "file": true
              },
              {
                "name": "insights-engine.md",
                "anno": "Insights-engine subsystem doc / plan (per-folder)",
                "file": true
              },
              {
                "name": "metric-collection.md",
                "anno": "Subsystem doc: metric collection",
                "file": true
              },
              {
                "name": "mod-lifecycle.md",
                "anno": "Subsystem doc: mod lifecycle",
                "file": true
              },
              {
                "name": "overlay.md",
                "anno": "Subsystem doc: the archived overlay",
                "file": true
              },
              {
                "name": "persistence.md",
                "anno": "Subsystem doc: persistence",
                "file": true
              },
              {
                "name": "spike-detection.md",
                "anno": "Subsystem doc: spike detection",
                "file": true
              },
              {
                "name": "test-harness.md",
                "anno": "Subsystem doc: the L1 test harness",
                "file": true
              },
              {
                "name": "web-dashboard.md",
                "anno": "Subsystem doc: the web dashboard",
                "file": true
              }
            ]
          },
          {
            "name": "tmodloader/",
            "anno": "Per-tModLoader-API reference + 'how we plug in'",
            "children": [
              {
                "name": "engagement-surfaces.md",
                "anno": "tML API ref: engagement surfaces",
                "file": true
              },
              {
                "name": "hook-surface.md",
                "anno": "tML API ref: the hook surface",
                "file": true
              },
              {
                "name": "ilhook-migration-research.md",
                "anno": "Research: the ILHook migration",
                "file": true
              },
              {
                "name": "lifecycle-and-loop.md",
                "anno": "tML API ref: lifecycle and the update loop",
                "file": true
              },
              {
                "name": "mod-identity.md",
                "anno": "tML API ref: mod identity / per-assembly attribution",
                "file": true
              },
              {
                "name": "monomod-detours.md",
                "anno": "tML API ref: MonoMod detours",
                "file": true
              },
              {
                "name": "ui-system.md",
                "anno": "tML API ref: the UI system + keybinds",
                "file": true
              }
            ]
          }
        ]
      },
      {
        "name": "description.txt",
        "anno": "Workshop store description",
        "file": true
      },
      {
        "name": "design/",
        "anno": "Build/design artefact",
        "node": "design",
        "children": [
          {
            "name": "dashboard-preview.html",
            "anno": "Design mockup / preview artefact",
            "file": true
          },
          {
            "name": "dashboard-preview.html.artifact.json",
            "anno": "Design mockup / preview artefact",
            "file": true
          },
          {
            "name": "dashboard-shots/",
            "anno": "Build/design artefact",
            "children": [
              {
                "name": "crosscut-overlap-bug.png",
                "anno": "Design render / screenshot",
                "file": true
              },
              {
                "name": "crosscut-render.png",
                "anno": "Design render / screenshot",
                "file": true
              },
              {
                "name": "tab-insights.png",
                "anno": "Design render / screenshot",
                "file": true
              },
              {
                "name": "tab-lag.png",
                "anno": "Design render / screenshot",
                "file": true
              },
              {
                "name": "tab-memory.png",
                "anno": "Design render / screenshot",
                "file": true
              },
              {
                "name": "tab-self.png",
                "anno": "Design render / screenshot",
                "file": true
              },
              {
                "name": "tab-summary.png",
                "anno": "Design render / screenshot",
                "file": true
              },
              {
                "name": "tab-timeline.png",
                "anno": "Design render / screenshot",
                "file": true
              }
            ]
          },
          {
            "name": "dashboard-ui-spec.md",
            "anno": "Design / notes document",
            "file": true
          },
          {
            "name": "mockups/",
            "anno": "Build/design artefact",
            "children": [
              {
                "name": "Mockups.html",
                "anno": "Design mockup / preview artefact",
                "file": true
              },
              {
                "name": "in-game-ui-designs-1.html",
                "anno": "Design mockup / preview artefact",
                "file": true
              }
            ]
          },
          {
            "name": "renders/",
            "anno": "Build/design artefact",
            "children": [
              {
                "name": "mqs2zac8-image.png",
                "anno": "Design render / screenshot",
                "file": true
              },
              {
                "name": "mqs3uz2g-image.png",
                "anno": "Design render / screenshot",
                "file": true
              },
              {
                "name": "mqs3vq55-image.png",
                "anno": "Design render / screenshot",
                "file": true
              },
              {
                "name": "mqs3wx35-image.png",
                "anno": "Design render / screenshot",
                "file": true
              },
              {
                "name": "mqs3z83o-image.png",
                "anno": "Design render / screenshot",
                "file": true
              },
              {
                "name": "mqs4072w-image.png",
                "anno": "Design render / screenshot",
                "file": true
              },
              {
                "name": "mqs4sz3b-image.png",
                "anno": "Design render / screenshot",
                "file": true
              }
            ]
          },
          {
            "name": "workshop-description.txt",
            "anno": "Design/workshop text artefact",
            "file": true
          }
        ]
      },
      {
        "name": "lib/",
        "anno": "Vendored LiteDB 5.0.21 (packed in the .tmod)",
        "node": "lib",
        "children": [
          {
            "name": "LiteDB.dll",
            "anno": "Vendored LiteDB 5.0.21 (MIT, single managed DLL) packed in the .tmod",
            "file": true
          }
        ]
      },
      {
        "name": "obj/",
        "anno": "Build/runtime artefact (gitignored)",
        "node": "obj",
        "children": [
          {
            "name": "Debug/",
            "anno": "Build/runtime artefact (gitignored)",
            "children": [
              {
                "name": "net8.0/",
                "anno": "Build/runtime artefact (gitignored)"
              }
            ]
          },
          {
            "name": "PerformanceProfiler.csproj.nuget.dgspec.json",
            "anno": "NuGet restore artefact (gitignored)",
            "file": true
          },
          {
            "name": "PerformanceProfiler.csproj.nuget.g.props",
            "anno": "Build/design artefact",
            "file": true
          },
          {
            "name": "PerformanceProfiler.csproj.nuget.g.targets",
            "anno": "Build/design artefact",
            "file": true
          },
          {
            "name": "Release/",
            "anno": "Build/runtime artefact (gitignored)",
            "children": [
              {
                "name": "net8.0/",
                "anno": "Build/runtime artefact (gitignored)"
              }
            ]
          },
          {
            "name": "project.assets.json",
            "anno": "NuGet restore artefact (gitignored)",
            "file": true
          },
          {
            "name": "project.nuget.cache",
            "anno": "NuGet restore artefact (gitignored)",
            "file": true
          }
        ]
      },
      {
        "name": "tools/",
        "anno": "Off-game tooling: the dashboard preview render + the L4/L6/L8 audit harness",
        "children": [
          {
            "name": "preview/",
            "anno": "Offline source-to-HTML dashboard render (reflects current source; static layout/colour/sort)",
            "node": "tools_preview",
            "children": [
              {
                "name": "README.md",
                "anno": "Directional source of truth: what the mod is, the six tabs, the trust posture",
                "file": true
              },
              {
                "name": "build_preview_html.py",
                "anno": "Builds a self-contained dashboard HTML from current source + fixtures",
                "file": true
              },
              {
                "name": "fixtures/",
                "anno": "The captured-session contract (29 JSON files) shared by preview + audit"
              },
              {
                "name": "render.py",
                "anno": "Regenerates the dashboard render against fixtures (static)",
                "file": true
              }
            ]
          },
          {
            "name": "testing/",
            "anno": "Self-describing L4/L6/L8 Playwright dashboard audit harness (DOM-discovered)",
            "node": "tools_testing",
            "children": [
              {
                "name": "README.md",
                "anno": "Directional source of truth: what the mod is, the six tabs, the trust posture",
                "file": true
              },
              {
                "name": "audit.py",
                "anno": "Audit CLI: doctor / contract / gen / assert / capture / synthesize",
                "file": true
              },
              {
                "name": "design-bar.md",
                "anno": "L8 visual-quality bar + chart vocabulary read by every review agent",
                "file": true
              },
              {
                "name": "pp_testing/",
                "anno": "Audit harness package: scenarios, site, driver, layout, capture"
              },
              {
                "name": "requirements.txt",
                "anno": "Playwright dependency pin for the audit harness",
                "file": true
              },
              {
                "name": "rubric.md",
                "anno": "L8 shared audit checklist read by every review agent",
                "file": true
              }
            ]
          }
        ]
      }
    ]
  },
  "bespoke": []
}`);
