/* ============================================================
   data.js - written by upkeep-context arch_merge.py
   schema: v2
   Edit agent-owned sections per references/arch-pipeline.md.
   Re-runs of upkeep-context preserve agent prose via arch_merge.py.
   ============================================================ */
window.ARCH = JSON.parse(`{
  "_meta": {
    "deleted_node_ids": [
      "bin_Debug",
      "bin_Release",
      "obj",
      "design",
      "lib"
    ],
    "deleted_edge_keys": [],
    "frontier_locked": false,
    "repotree_locked": false
  },
  "schema": "v2",
  "project": {
    "name": "PerformanceProfiler",
    "file": "context/architecture.html",
    "head": "2f2fc1c",
    "headRange": "c426015..2f2fc1c",
    "regenerated": "2026-07-07",
    "stack": "C# / .NET 8 · tModLoader 1.4.4 client mod · LiteDB store · vanilla-JS dashboard",
    "milestone": "v0.35.0 — the 2026-07-07 honesty + feature mega-batch (0.28.1→0.35.0)",
    "tests": "205 xUnit cases green (runtime-free linked-source harness + LiteDB round-trips)",
    "frameBudget": "zero-allocation per-tick hot path; harvest ~0.4-0.5 ms/t at 62,203 hooks (Self tab measures it)",
    "commits": 120,
    "lines": 428403,
    "tagline": "A read-only performance observatory for modded Terraria: per-mod CPU, RAM, and engagement attribution served to a local browser dashboard, with cross-session memory, an insights engine, and its own cost measured with the same rigour it applies to everyone else.",
    "purpose": "Players with large modlists cannot tell which mod costs what, or whether the cost buys anything they use. The profiler IL-hooks every mod hook tModLoader exposes (62k on a kitchen-sink stack), attributes per-tick CPU/alloc cost per mod and per loop phase (update vs draw), tracks engagement through generic interaction surfaces, persists per-session and lifetime rollups in LiteDB, and serves seven dashboard tabs plus a self-contained HTML session report. Five invariants govern everything: read-only instrumentation, budgeted overhead, descriptive-never-normative copy, abort-clean on host drift, and no mod-specific code.",
    "overview": "Runtime composition: PerformanceProfiler.cs (Mod entry) opens the LiteDB store and the loopback HTTP server at load; ProfilerSystem (ModSystem, in Profiling/) arms a MetricCollector per world and drives BeginTick/EndTick from PreUpdateEntities/PostUpdateEverything plus a draw beat from PostDrawInterface. Hook probes credit PerModAttribution's phase-laned accumulators; EndTick harvests into smoothed views, folds the honest real-frame metrics (RealtimeSpeed), and feeds detectors (spikes, stalls) plus the Insights engine on a worker cadence. The Data/ pipeline exposes everything as named snapshots the Web routers serialise; Persistence/ owns the writer thread, journal, session records, cross-session rollup, install-arm history, and the HTML report module. Tests/ compiles ~40 production files runtime-free via linked sources; tools/testing drives the un-built dashboard assets through Playwright.",
    "techStack": [
      {
        "name": "C# / .NET 8",
        "meta": "tModLoader 1.4.4 pins .NET 8; dotnet msbuild"
      },
      {
        "name": "tModLoader",
        "meta": "1.4.4 · ModSystem hooks + MonoModHooks"
      },
      {
        "name": "MonoMod (via tML)",
        "meta": "62k IL timing detours; SourceCloneIl retained for re-chain safety"
      },
      {
        "name": "LiteDB",
        "meta": "5.x vendored dll · single writer thread + redo journal"
      },
      {
        "name": "vanilla JS + CSS (OKLCH)",
        "meta": "C# verbatim-string assets bundled into one dashboard page"
      },
      {
        "name": "xUnit",
        "meta": "205 cases · linked-source, no tML runtime"
      },
      {
        "name": "Playwright (Python)",
        "meta": "tools/testing L4/L6/L8 harness against un-built assets"
      }
    ]
  },
  "nodes": [
    {
      "id": "Data",
      "label": "Data",
      "kind": "foundation",
      "layer": 1,
      "root": "Data/",
      "tagline": "The calculation pipeline: collectors, aggregators, stats, detectors — the only place maths lives",
      "owns": "DataRegistry (named snapshot streams), collectors (pull-side adapters over the collector), aggregators (PerModAttribution accumulator with phase lanes, heatmap fold, per-tick ring, segments), stats (KpiCalculator core, Baseline, RealtimeSpeed, MemoryTrend, SelfHealth snapshots), detectors (spike/stall). Does NOT own rendering or storage.",
      "files": [
        "Aggregators/PerModAttribution.cs - phase-laned per-mod/per-hook credit grids (S01)",
        "Aggregators/HeatmapFold.cs - pure per-minute bucket fold (real cadence; ribbon substrate)",
        "Stats/KpiCalculator.cs - pure KPI fold (cause-split stalls, honest fps); Live partial reads collector",
        "Stats/RealtimeSpeed.cs - level-detection maths: speed fraction, deficit, sustained-fire constants",
        "Stats/MemoryTrend.cs - trend ring + least-squares growth slope + phase table (S04)",
        "Stats/Baseline.cs - relative thresholds: medians/MADs the detectors key on",
        "Detectors/StallDetector.cs - gap classifier (suspend vs freeze vs GC) + LastGapCause",
        "Detectors/SpikeDetector.cs - real-cadence spike windows with per-mod snapshots"
      ],
      "state": [
        "PerModAttribution static accumulator grids (ticks/bytes/draw mirrors per backend)",
        "DataRegistry.Shared stream registry (persists across worlds; per-session state reset)",
        "Baseline frame/period histograms (real-cadence since 0.30)",
        "MemoryTrend 1440-sample process-lifetime ring"
      ]
    },
    {
      "id": "Insights",
      "label": "Insights",
      "kind": "learner",
      "layer": 2,
      "root": "Insights/",
      "tagline": "The pattern engine: 22 descriptive detectors over reference frames, ranked and rendered under the honesty contract",
      "owns": "InsightsEngine (detector registry + evaluation), detectors + pure cores (incl. SustainedSlowness and DrawBoundMod from the 2026-07-07 batch, FrameHeadroom gated on measured full speed), reference frames (context/temporal baselines), RankingScorer, InsightRenderer (the only place insight copy is written), InsightStore dedup/promotion. Does NOT own the kanban UI or persistence of records.",
      "files": [
        "InsightsEngine.cs - detector registry; evaluation entry the worker cadence drives",
        "InsightRenderer.cs - per-pattern copy templates (descriptive, never normative)",
        "RankingScorer.cs - share/ratio scoring + confidence ladder",
        "Detectors/SustainedSlownessCore.cs - the level detector (X2): speed<90% held 30s",
        "Detectors/DrawBoundModCore.cs - S01 finding: >=60% of a mod's cost in the draw phase",
        "Detectors/FrameHeadroomDetector.cs - compute-budget headroom, gated on RealtimeSpeed>=0.98 (X1 fix)"
      ],
      "state": [
        "InsightsEngine.Shared per-session engine (null between worlds)",
        "InsightStore records with confidence promotion + TTL",
        "ContextBaseline / TemporalBaseline accumulators"
      ]
    },
    {
      "id": "Localization",
      "label": "Localization",
      "kind": "env",
      "layer": 5,
      "root": "Localization/",
      "tagline": "hjson string table: keybind + the full ModConfig label/tooltip surface (S23)",
      "owns": "en-US keys for the config UI (every tooltip names its feature's cost). Dashboard strings remain hardcoded English (atlas S31 partial).",
      "files": [
        "en-US_Mods.PerformanceProfiler.hjson - keybind + Configs.* labels/tooltips"
      ],
      "state": [
        "none"
      ]
    },
    {
      "id": "Persistence",
      "label": "Persistence",
      "kind": "foundation",
      "layer": 2,
      "root": "Persistence/",
      "tagline": "The memory layer: LiteDB store, writer thread + journal, session records, lifetime rollup, install-arm history, HTML report",
      "owns": "ProfilerDatabase (collections + guarded dispose), DbWriterThread (single-writer queue; ObjectDisposed terminal guard), EventJournal (redo log), SessionRecorder (per-world capture), fingerprint v2 (FingerprintCore: sorted InternalName set, self-excluded), cross-session rollup + HistoryStore, InstallArmRow reload-stack detection, Report/ (reader + pure HTML writer + exporter), reset scopes. Does NOT own live measurement or serving.",
      "files": [
        "ProfilerDatabase.cs - collections, ApplyBatch stream dispatch, worker-alive dispose guard (H2)",
        "DbWriterThread.cs - single writer: batch drain, checkpoint cadence, store-closed terminal guard",
        "ModlistFingerprint.cs / FingerprintCore.cs - v2 identity (sorted names, self-excluded); versions to metadata",
        "Streams/SessionRecorder.cs - per-tick capture -> warm/cold tiers + session archive (real-cadence)",
        "History/RollupFold.cs + HistoryStore.cs - lifetime per-mod rollup keyed on InternalName",
        "Records/InstallArmRow.cs - per-install measurements keyed by process (reload-stack signature)",
        "Report/HtmlReportWriter.cs - pure static-HTML session report (no JS, no network refs; pinned)"
      ],
      "state": [
        "profiler.db LiteDB file + 3-backup ring + redo journal",
        "sessions/modlists/mods/rollup/insights/installArms collections",
        "DbWriterThread unbounded channel + approx depth"
      ]
    },
    {
      "id": "Profiling",
      "label": "Profiling",
      "kind": "entry",
      "layer": 0,
      "root": "Profiling/",
      "tagline": "The measuring engine: game-loop hooks, probe stack, per-tick collector, self-health",
      "owns": "ProfilerSystem (ModSystem lifecycle + tick/draw hooks), MetricCollector (frame metrics incl. the suspend-guarded RealFrameTimeMs and RealtimeSpeed folds, per-mod/per-hook EMAs, detector sensitivity), ProbeStack (IL-called Enter/Leave with phase-aware call counters), the two hook interceptors, HookBackend mode/alloc statics, ProfilerSelfHealth (install delta + memory-trend guard + growth severity). Does NOT own persistence, presentation, or insight logic.",
      "files": [
        "ProfilerSystem.cs - ModSystem: world arm/disarm, tick+draw hooks, insights cadence, session-end task, config apply",
        "MetricCollector.cs - per-tick engine: frame build, suspend guard, harvests, folds, detector drive",
        "ProbeStack.cs - IL prologue/epilogue: LIFO timing frames, phase-aware call counts",
        "ILHookInterceptor.cs - 62k IL detour installer + scaffolding trim + abort-clean",
        "HookInterceptor.cs - delegate-backend installer + PerModAttribution.Configure (phase lanes)",
        "HookBackend.cs - backend mode + AllocationTracking statics (config-gated at Load)",
        "ProfilerSelfHealth.cs - install delta, bytes/hook severity, memory-trend guard (H3 growth axis)"
      ],
      "state": [
        "MetricCollector rolling TickFrame ring (config-sized, default 1800)",
        "per-mod/per-hook smoothed + average EMA arrays",
        "ProbeStack thread-local frame stacks + phase call counters",
        "ProfilerSelfHealth singleton (survives world loads): install deltas, MemoryTrend ring",
        "PerModAttribution.CurrentPhaseIsUpdate static phase flag (set in Begin/EndTick)"
      ]
    },
    {
      "id": "Tests",
      "label": "Tests",
      "kind": "observer",
      "layer": 4,
      "root": "Tests/",
      "tagline": "Runtime-free xUnit harness: production sources compiled via linked-file curation, no tModLoader",
      "owns": "The linked-source csproj (each entry verified Terraria-free), the Ring-1 scenario engine (ScenarioRunner driving real Baseline/StallDetector/SpikeDetector/KpiCalculator/RealtimeSpeed against scripted sessions), honesty/phase-lane/memory-trend/fingerprint/report pins, Ring-2 LiteDB round-trips, diagnostics benches. Does NOT ship in the .tmod (buildIgnore).",
      "files": [
        "Simulation/ScenarioRunner.cs - drives the real pipeline classes with scripted ticks (EndTick contract mirror)",
        "Simulation/HonestyPins.cs - the X1/X2/X3 classes as permanent assertions",
        "Simulation/PhaseLanePins.cs + PhaseLaneBench.cs - S01 lane contract + measured overhead",
        "Simulation/StoreRoundTripPins.cs - real-engine LiteDB predicate shapes (C1 class)",
        "PerformanceProfiler.Tests.csproj - the curated linked-source list (the Terraria-free rule)"
      ],
      "state": [
        "no runtime state; temp LiteDB files per round-trip fixture"
      ]
    },
    {
      "id": "UI",
      "label": "UI",
      "kind": "env",
      "layer": 5,
      "root": "UI/",
      "tagline": "Archived in-game overlay tree (v0.9 dashboard pivot) preserved for a Steam-Deck revival",
      "owns": "The pre-v0.9 in-game overlay (5 tabs, ~5.5k lines). Deliberately preserved, not compiled into the dashboard path; revisit trigger is post-v1.0 handheld demand.",
      "files": [
        "Overlay/OverlayPanel.cs - archived overlay root"
      ],
      "state": [
        "none (archived)"
      ]
    },
    {
      "id": "Web",
      "label": "Web",
      "kind": "boundary",
      "layer": 3,
      "root": "Web/",
      "tagline": "The serving boundary: hand-rolled TCP HTTP server, JSON routers, and the entire dashboard as C# verbatim-string assets",
      "owns": "DashboardHttpServer (loopback TcpListener, port walk 27277-87), DashboardRouter partials (one Build* per endpoint; serialisation only, no derivation), the asset bundle (IndexHtml/Css.*/Js.* verbatim strings incl. the popup-card + panel-state primitives), the export-report endpoint. Does NOT compute stats (Data/ does) or read game state directly.",
      "files": [
        "Server/DashboardHttpServer.cs - hand-rolled HTTP/1.1 over TcpListener (no http.sys)",
        "DashboardRouter.cs - route table; partials per tab payload",
        "DashboardRouter.Summary.cs - /api/now + frames + heatmap (honest real-cadence reads)",
        "DashboardRouter.Self.cs - self-health + memory-guard block + arm history",
        "DashboardRouter.Report.cs - /api/export-report -> ReportExporter",
        "Assets/Js/Js.Cards.cs - popup card system (boss report card, minute drill)",
        "Assets/Js/Js.Components.cs - shared component vocabulary + panelState warming primitive"
      ],
      "state": [
        "singleton DashboardHttpServer bound at Load (config-gated)",
        "JS client state: lastNow/lastMods/... poll caches, re-armable poll timers (PollMs config)"
      ]
    },
    {
      "id": "tools_preview",
      "label": "preview",
      "kind": "observer",
      "layer": 5,
      "root": "tools/preview/",
      "tagline": "Offline dashboard preview: renders the C# verbatim-string assets to one HTML file without building",
      "owns": "build_preview_html.py extraction of Css./Js./IndexHtml constants. Superseded for verification by tools/testing but still the quickest eyeball loop.",
      "files": [
        "build_preview_html.py - asset extraction + single-file preview"
      ],
      "state": [
        "none"
      ]
    },
    {
      "id": "tools_testing",
      "label": "testing",
      "kind": "observer",
      "layer": 5,
      "root": "tools/testing/",
      "tagline": "The L4/L6/L8 Playwright harness: fixtures, layout invariants, screenshots, agent audits — plus run_all.sh, the one-command no-game gate",
      "owns": "audit.py subcommands (gen/contract/assert/capture/synthesize), pp_testing package (site assembly from un-built assets, fixture scenarios, layout rules incl. the 2026-07-07 rebuilt selection-feedback rule), run_all.sh chaining dotnet test + compile gate + harness.",
      "files": [
        "audit.py - harness entry",
        "run_all.sh - the whole no-game verification gate",
        "pp_testing/layout.py - DOM invariants (index-tracked selection rule)",
        "pp_testing/site.py - serves the CURRENT source's assets (no build needed)"
      ],
      "state": [
        "/tmp/pp-audit site assembly; .venv with pinned Playwright"
      ]
    }
  ],
  "edges": [
    {
      "from": "Profiling",
      "to": "Data",
      "rel": "strong",
      "label": "probe credits -> phase-laned accumulator grids; EndTick harvests + folds into stats"
    },
    {
      "from": "Data",
      "to": "Profiling",
      "rel": "peer",
      "label": "detectors/baseline owned in Data but driven per-tick by MetricCollector (shared tick lifecycle)"
    },
    {
      "from": "Web",
      "to": "Data",
      "rel": "dep",
      "label": "DataRegistry.Shared snapshot lookups per poll; routers never derive"
    },
    {
      "from": "Persistence",
      "to": "Profiling",
      "rel": "dep",
      "label": "SessionRecorder reads TickFrame/collector views; install path persists SelfHealth arm measurements"
    },
    {
      "from": "Web",
      "to": "Persistence",
      "rel": "dep",
      "label": "DbReadModel idle fallback, reset scopes, /api/export-report -> ReportExporter"
    },
    {
      "from": "Insights",
      "to": "Profiling",
      "rel": "dep",
      "label": "detectors read collector accessors (RealtimeSpeedNow, PerModCategoryDrawMs, Baseline)"
    },
    {
      "from": "Insights",
      "to": "Persistence",
      "rel": "dep",
      "label": "cross-session detectors read HistoryStore rollup; session-end producer enqueues InsightRows"
    },
    {
      "from": "Profiling",
      "to": "Persistence",
      "rel": "write",
      "label": "session-end task: recorder.End -> writer queue -> archive; install-arm insert at PostSetupContent"
    },
    {
      "from": "Tests",
      "to": "Data",
      "rel": "peer",
      "label": "linked-source compilation of the pure pipeline classes (no tML runtime)"
    },
    {
      "from": "tools_testing",
      "to": "Web",
      "rel": "peer",
      "label": "extracts the verbatim-string assets and serves them un-built to Playwright"
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
      "name": "game boundary",
      "ids": [
        "Profiling"
      ]
    },
    {
      "name": "calculation",
      "ids": [
        "Data",
        "Insights"
      ]
    },
    {
      "name": "memory",
      "ids": [
        "Persistence"
      ]
    },
    {
      "name": "serving",
      "ids": [
        "Web"
      ]
    },
    {
      "name": "verification + assets",
      "ids": [
        "Tests",
        "tools_testing",
        "tools_preview",
        "UI",
        "Localization"
      ]
    }
  ],
  "layersNote": "Dependency direction flows downward-left: Web serialises what Data computed from what Profiling measured; Persistence records the same pipeline outputs; Insights reads collector accessors on a worker cadence. The calculation rule (v0.10): the ONLY place maths lives is a Data/ pipeline stage — routers serialise, JS renders, the DB stores.",
  "dataFlow": {
    "intro": "The spine is the honest-frame chain built across 0.28.1-0.30.0: it exists because the original frame metric measured only the update window and reported 60 fps during visible slow-motion (the 2026-07-07 live diagnosis).",
    "simsets": [
      "BeginTick",
      "probes",
      "EndTick",
      "draw beat",
      "worker",
      "serve",
      "session end"
    ],
    "steps": [
      {
        "n": 1,
        "fn": "BeginTick (PreUpdateEntities)",
        "sys": "Profiling::BeginTick",
        "data": "stamp -> real inter-frame period; StallDetector classifies the preceding gap (suspend/freeze/GC via focus + ceilings); phase flag -> update"
      },
      {
        "n": 2,
        "fn": "probes (IL detours)",
        "sys": "Profiling::ProbeStack",
        "data": "Enter/Leave credit elapsed ticks to PerModAttribution: primary grid always (total), draw mirror when the phase flag says outside the update window"
      },
      {
        "n": 3,
        "fn": "EndTick (PostUpdateEverything)",
        "sys": "Profiling::EndTick",
        "data": "TickFrame built: FrameTimeMs (compute) + RealFrameTimeMs (whole loop, suspend-guarded to compute on pause gaps); RealtimeSpeed folds; harvest walks 62k hooks into smoothed EMAs; harvest cost + phase-split probe counts -> SelfHealth"
      },
      {
        "n": 4,
        "fn": "draw beat (PostDrawInterface)",
        "sys": "Profiling::PostDrawInterface",
        "data": "render-cadence EMA (render fps) — the frameskip divergence signal"
      },
      {
        "n": 5,
        "fn": "worker cadence (configurable)",
        "sys": "Insights::worker cadence",
        "data": "InsightsEngine evaluates detectors off-thread; memory-trend sampler pushes the SelfHealth ring"
      },
      {
        "n": 6,
        "fn": "serving (poll)",
        "sys": "Web::routers",
        "data": "routers serialise registry snapshots: /api/now kpi block (avgFps, renderFps, realtimeSpeed, cause-split stalls) -> fps card, Lag headline"
      },
      {
        "n": 7,
        "fn": "session end",
        "sys": "Persistence::session end",
        "data": "recorder.End -> writer drain -> archive row (real-cadence) -> optional auto HTML report; rollup fold; insight rows persisted"
      }
    ]
  },
  "failures": [
    {
      "link": "steps 1,3",
      "title": "Alt-tab reads as slow-motion",
      "body": "GUARDED: ProcessSuspended/WorldLoad gaps fall back to compute time in RealFrameTimeMs (collector) AND are excluded from stall headlines into pausedMs (KpiCalculator) — pinned by the altTabbed scenario."
    },
    {
      "link": "step 7",
      "title": "Store closed under the writer",
      "body": "GUARDED (H2): ProfilerDatabase.Dispose skips LiteDB dispose if the worker outlived its join; the worker treats ObjectDisposedException as terminal and journals the tail for replay."
    },
    {
      "link": "step 2",
      "title": "JIT shared-body generic trap (G1)",
      "body": "WATCHED: _tmlAssembly filter guards patching shared compiled bodies; a novel closed-generic inheritance shape could reintroduce a world-load InvalidCastException; abort-clean uninstall is the backstop."
    },
    {
      "link": "steps 2,3",
      "title": "Reload-stack RAM growth",
      "body": "DETECTED, NOT FIXED: Reload Mods pins prior-install residue (~1.8->2.5 GB observed at constant hooks). InstallArmRow staircase detection WARNs with the restart remedy; the trim lever is atlas S24."
    },
    {
      "link": "steps 1-7",
      "title": "Loader-lock on live rebuild",
      "body": "KNOWN: a running game holds the .tmod; builds compile (0 error CS) but MSB3073 on pack until the game closes. run_all.sh ignores exactly that noise."
    },
    {
      "link": "steps 1-7",
      "title": "Runtime verification gap",
      "body": "STANDING: the whole 0.28.1-0.35.0 batch is off-game verified (205 tests + harness) and awaits a Build + Reload playtest; every commit carries the same caveat."
    }
  ],
  "relationships": [
    {
      "a": "Profiling",
      "b": "Data",
      "mech": "static accumulator (PerModAttribution) + per-tick calls",
      "note": "ProbeStack credits grids the collector clears/harvests each tick; the phase flag (CurrentPhaseIsUpdate) is set by MetricCollector and read by Add + ProbeStack counters. Break: attribution loses phase truth or drops samples; totals were kept bit-identical by design (primary=total, mirror=draw)."
    },
    {
      "a": "Profiling",
      "b": "Persistence",
      "mech": "call + queue",
      "note": "Session-end task calls recorder.End then drains the writer BEFORE the summary/auto-report read the archive; install path inserts InstallArmRow directly at load time (writer exists to decouple the GAME loop, not load). Break: reports read a missing archive row (null -> no report, never half)."
    },
    {
      "a": "Data",
      "b": "Web",
      "mech": "registry snapshot reads",
      "note": "Routers look up named streams (DataRegistry.Shared) and serialise; the v0.10 rule bans router-side derivation. Break: a missing stream renders the honest empty state, not a 500 (guarded lookups + Empty defaults)."
    },
    {
      "a": "Insights",
      "b": "Profiling",
      "mech": "accessor reads on worker thread",
      "note": "Detectors read IReadOnlyList views + scalar EMAs (RealtimeSpeedNow, UpdateWindowEmaMs, PerModCategoryDrawMs) — no mutable buffer access, so off-thread evaluation is race-tolerant by design. Break: a detector reading a mutable buffer would race the tick loop; the accessor discipline is the guard."
    },
    {
      "a": "ProfilerConfig",
      "b": "Profiling+Web+Persistence",
      "mech": "config gates at three boundaries",
      "note": "Load-time ([ReloadRequired]: backend mode, alloc tracking, server), world-arm snapshot (history size, cadence, sensitivities via ApplyRuntimeConfig), poll-time (PollMs via /api/now -> JS re-arm). Read discipline: the hot path never touches ModContent.GetInstance. Break: a hot-path config read would add per-tick cost; the discipline is documented at every gate."
    },
    {
      "a": "Tests",
      "b": "production sources",
      "mech": "linked-file compilation",
      "note": "The csproj curates Terraria-free files; MetricCollector cannot link (tML-transitive via SelfHealth), so KpiCalculator split into pure core + Live partial and ScenarioRunner mirrors EndTick's documented contract with move-together pointer comments. Break: a linked file gaining a Terraria using breaks the suite compile — which is the enforcement."
    },
    {
      "a": "tools_testing",
      "b": "Web assets",
      "mech": "source extraction",
      "note": "site.py regex-extracts the C# verbatim-string constants and serves them un-built; the harness therefore reflects UNCOMMITTED source. Break: an asset renamed outside the extraction patterns silently drops from the harness page — the doctor command exists for that."
    },
    {
      "a": "Persistence",
      "b": "Insights",
      "mech": "rollup read + row write",
      "note": "Cross-session detectors read HistoryStore (lifetime rollup, InternalName-keyed); the session-end producer writes InsightRows. Fingerprint v2 (self-excluded name set) stops dev rebuilds fracturing these baselines — the X7 fix. Break: identity fracture = lifetime columns never accumulate (the observed 10-modlists-in-11-sessions failure)."
    },
    {
      "a": "Web",
      "b": "Localization",
      "mech": "none (gap, by design)",
      "note": "Dashboard strings are hardcoded English while the ModConfig surface is fully hjson-localised — the split is deliberate until atlas S31; the coupling to watch is copy DUPLICATED between insight templates (C#) and config tooltips (hjson) describing the same features."
    },
    {
      "a": "UI",
      "b": "Web",
      "mech": "historical succession",
      "note": "The archived overlay tree is the dashboard's predecessor (v0.9 pivot); it shares no code today but its preserved components are the seed for a Steam-Deck revival (S22). Break: nothing at runtime — the risk is doc drift implying it still renders."
    }
  ],
  "stateOwnership": [
    {
      "owner": "Profiling/MetricCollector",
      "items": "TickFrame ring, per-mod/per-hook EMAs, RealtimeSpeed accumulators — world-scoped; rebuilt at arm"
    },
    {
      "owner": "Data/PerModAttribution (static)",
      "items": "credit grids incl. draw mirrors + phase flag — process-scoped statics; Configure re-sizes at install"
    },
    {
      "owner": "Profiling/ProfilerSystem.SelfHealth (static)",
      "items": "install deltas, MemoryTrend ring, growth severity — process singleton — deliberately survives world loads; ALC reload resets it (why InstallArmRow persists per process key)"
    },
    {
      "owner": "Persistence/ProfilerDatabase",
      "items": "LiteDB collections + writer queue + journal — opened at Load, disposed at Unload behind the worker-alive guard"
    },
    {
      "owner": "Web JS client",
      "items": "poll caches (lastNow/lastMods/...), render signatures, poll timers — browser-side only; re-armed when PollMs changes"
    }
  ],
  "coverage": {
    "cols": [
      "docs",
      "tests",
      "runtime"
    ],
    "rows": [
      {
        "label": "Data/",
        "node": "Data",
        "cells": {},
        "prev": {}
      },
      {
        "label": "Insights/",
        "node": "Insights",
        "cells": {},
        "prev": {}
      },
      {
        "label": "Localization/",
        "node": "Localization",
        "cells": {},
        "prev": {}
      },
      {
        "label": "Persistence/",
        "node": "Persistence",
        "cells": {},
        "prev": {}
      },
      {
        "label": "Profiling/",
        "node": "Profiling",
        "cells": {},
        "prev": {}
      },
      {
        "label": "Tests/",
        "node": "Tests",
        "cells": {},
        "prev": {}
      },
      {
        "label": "UI/",
        "node": "UI",
        "cells": {},
        "prev": {}
      },
      {
        "label": "Web/",
        "node": "Web",
        "cells": {},
        "prev": {}
      },
      {
        "label": "tools/preview/",
        "node": "tools_preview",
        "cells": {},
        "prev": {}
      },
      {
        "label": "tools/testing/",
        "node": "tools_testing",
        "cells": {},
        "prev": {}
      }
    ],
    "note": "docs = systems/*.md depth; tests = off-game coverage (linked suite / harness); runtime = in-game verification recency. The batch's standing gap is the runtime column: 0.28.1-0.35.0 awaits a playtest."
  },
  "milestones": [],
  "criticalPaths": [
    {
      "name": "per-tick hot path",
      "steps": [
        "IL probe Enter (stamp+push)",
        "hook body",
        "Leave -> PerModAttribution.Add (phase-laned)",
        "EndTick harvest: 62k walk + EMA folds (measured: harvest ~0.4-0.5 ms/t; phase lanes +0.001 ms/t)"
      ],
      "len": "4 steps",
      "blast": "zero-allocation contract; every change here is measured before done (Invariant 2)"
    },
    {
      "name": "the honest frame number",
      "steps": [
        "BeginTick real period",
        "suspend guard (LastGapCause)",
        "RealFrameTimeMs on TickFrame",
        "RealtimeSpeed folds",
        "KpiCalculator.ComputeCore",
        "/api/now kpi",
        "fps card + Lag headline"
      ],
      "len": "7 steps",
      "blast": "the X1/X2 class is pinned by slowmo30/altTabbed scenarios"
    },
    {
      "name": "session end",
      "steps": [
        "PreSaveAndQuit/OnWorldUnload latch",
        "recorder.End (archive enqueue)",
        "writer drain + journal truncate",
        "summary log + insight rows + rollup fold",
        "optional auto HTML report"
      ],
      "len": "5 steps",
      "blast": "ordering is load-bearing: the report reads the archive the drain guarantees"
    }
  ],
  "notes": [
    {
      "tag": "honesty",
      "title": "real cadence everywhere player-facing",
      "body": "The 2026-07-07 batch was measurement-honesty-first: the KPI strip, detectors, aggregators, and insight copy now all read the REAL loop cadence; compute-window reads survive only where deliberate (attribution internals, self-overhead, headroom-with-gate) and each carries a comment saying so."
    },
    {
      "tag": "config",
      "title": "heaviest defaults, sliders only turn DOWN",
      "body": "Defaults are the HEAVIEST configuration by explicit user decision: the sliders exist to turn specific costs DOWN; there is no Lite/Standard/Deep preset and none is planned."
    },
    {
      "tag": "testing",
      "title": "pure-core pattern is the testing seam",
      "body": "The pure-core pattern is the testing seam: any detector/stat logic worth testing gets a Terraria-free Core file linked into the runtime-free suite."
    }
  ],
  "concept": {
    "root": "trusted observation",
    "branches": [
      {
        "head": "measure honestly",
        "leaves": [
          "real cadence (RealFrameTimeMs)",
          "cause-split pauses",
          "gated headroom claims",
          "render vs update fps"
        ],
        "trunks": [
          "the 2026-07-07 arc: every player-facing number means what it says"
        ]
      },
      {
        "head": "measure yourself",
        "leaves": [
          "harvest ms/tick",
          "probe calls (phase-split)",
          "install deltas per arm",
          "memory-trend growth axis"
        ],
        "trunks": [
          "the profiler is its own first subject"
        ]
      },
      {
        "head": "never disturb",
        "leaves": [
          "read-only probes",
          "abort-clean on drift",
          "no mod-specific code",
          "descriptive copy only"
        ],
        "trunks": [
          "the five invariants are the product"
        ]
      }
    ],
    "note": "The 2026-07-07 batch extended the trust posture inward: honest numbers first, then features spent on that trust (phase attribution, config, memory guard, report, cards)."
  },
  "glossary": [
    {
      "term": "real cadence / RealFrameTimeMs",
      "def": "wall period between BeginTicks (update+draw+vsync), suspend-guarded; the number the player FEELS"
    },
    {
      "term": "update window / FrameTimeMs",
      "def": "PreUpdateEntities->PostUpdateEverything compute time; attribution-internal, deliberately kept"
    },
    {
      "term": "phase lanes",
      "def": "S01: primary accumulator grid carries the TOTAL; a draw mirror carries out-of-window credits; update = total - draw"
    },
    {
      "term": "RealtimeSpeed",
      "def": "clamp01(16.67 / real-period EMA): 1.0 = full 60 UPS, 0.5 = half-speed slow-motion"
    },
    {
      "term": "fingerprint v2",
      "def": "modlist identity = sorted InternalName set excluding the profiler; versions are session metadata (S10 substrate)"
    },
    {
      "term": "reload-stack",
      "def": "install residue pinned across Reload Mods: install delta staircases at constant hook count; restart reclaims"
    },
    {
      "term": "warming state",
      "def": "S20: age-gated panel state that refuses judgement-toned output from minutes-old data"
    }
  ],
  "decisions": [
    {
      "node": "Data",
      "title": "Primary grid keeps the TOTAL; draw is a mirror",
      "why": "phase lanes (84409c1) chose bit-identical totals for every existing consumer over a cleaner update-only primary — blast radius beat elegance."
    },
    {
      "node": "Insights",
      "title": "Headroom reads compute, gated on measured speed",
      "why": "with Baseline real-cadence, vsync pins the median at ~16.67 by construction; headroom = update-window EMA, emitted only at RealtimeSpeed>=0.98 (448f447) — the X1 sentence became impossible rather than discouraged."
    },
    {
      "node": "Profiling",
      "title": "tML's own config UI, no custom settings surface",
      "why": "user-directed (88f10f4): reduce churn; ClientSide scope; heaviest defaults; hot path never reads config (ArmedSettings-style snapshots at gates)."
    },
    {
      "node": "Persistence",
      "title": "Never dispose the store under a live writer",
      "why": "H2 (c1cf962): leak-on-stuck-thread beats closing LiteDB under an in-flight batch; the worker treats ObjectDisposed as terminal and journals the tail."
    },
    {
      "node": "Persistence",
      "title": "Static server-side HTML report, zero JS",
      "why": "ef74479 dropped the planned JSON+renderer for a pure render: strictly more self-contained; pinned by a network-blocked browser load."
    },
    {
      "node": "Persistence",
      "title": "Direct load-time DB insert for install arms",
      "why": "the writer thread exists to decouple the GAME loop; PostSetupContent runs pre-world (LegacyJsonImporter precedent)."
    }
  ],
  "risks": [
    {
      "sev": "high",
      "node": "Profiling",
      "title": "Runtime verification debt",
      "trigger": "the entire 0.28.1-0.35.0 batch is off-game verified only; the next Build + Reload playtest is the gate (fps honesty, config UI render, cards, report button)."
    },
    {
      "sev": "med",
      "node": "Profiling",
      "title": "Reload-stack residue (S24 open)",
      "trigger": "detection shipped; the trim lever (SourceCloneIl retention trade vs Invariant 4) is designed but unbuilt."
    },
    {
      "sev": "med",
      "node": "Profiling",
      "title": "G1 JIT shared-body trap",
      "trigger": "mitigated by the _tmlAssembly filter; a novel closed-generic shape could reintroduce it; abort-clean is the backstop."
    },
    {
      "sev": "low",
      "node": "Tests",
      "title": "ScenarioRunner contract mirror",
      "trigger": "the runner replicates EndTick's suspend-guard/fold contract with move-together comments; a silent EndTick change could drift the mirror — the honesty pins would catch the observable part."
    },
    {
      "sev": "low",
      "node": "Persistence",
      "title": "Fingerprint v2 one-time fracture",
      "trigger": "stored v1 digests read as a roster change once; rebuild-rollup covers the rollup side; expected and messaged."
    }
  ],
  "alerts": [
    {
      "sev": "med",
      "text": "Playtest pending — 0.28.1->0.35.0 (9 shipped versions) awaits in-game verification; watch: honest fps under slow-mo, ModConfig sliders render, boss card opens, export-report writes.",
      "meta": "standing until the next Build + Reload playtest"
    }
  ],
  "changeFrontier": [
    {
      "name": "Data/",
      "node": "Data",
      "bars": [
        100,
        0,
        0,
        0,
        0,
        40,
        6
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
        100,
        7
      ]
    },
    {
      "name": "Localization/",
      "node": "Localization",
      "bars": [
        76,
        0,
        0,
        0,
        0,
        0,
        100
      ]
    },
    {
      "name": "Persistence/",
      "node": "Persistence",
      "bars": [
        0,
        0,
        0,
        0,
        0,
        100,
        9
      ]
    },
    {
      "name": "Profiling/",
      "node": "Profiling",
      "bars": [
        100,
        0,
        0,
        0,
        1,
        19,
        2
      ]
    },
    {
      "name": "Tests/",
      "node": "Tests",
      "bars": [
        71,
        0,
        0,
        0,
        12,
        100,
        49
      ]
    },
    {
      "name": "UI/",
      "node": "UI",
      "bars": [
        100,
        0,
        0,
        0,
        0,
        2,
        0
      ]
    },
    {
      "name": "Web/",
      "node": "Web",
      "bars": [
        100,
        0,
        0,
        0,
        46,
        34,
        4
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
        100,
        81,
        0
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
        100,
        6
      ]
    }
  ],
  "kpis": [
    {
      "label": "tests",
      "value": 205,
      "unit": "green",
      "delta": "+35 this batch (scenario engine, lanes, trend, report, store)"
    },
    {
      "label": "harvest",
      "value": "0.4-0.5",
      "unit": "ms/t @62k hooks",
      "delta": "phase lanes +0.001 ms/t (bench best-of-5)"
    },
    {
      "label": "version",
      "value": "0.35.0",
      "unit": "",
      "delta": "9 versions shipped 2026-07-07"
    },
    {
      "label": "honesty pins",
      "value": 3,
      "unit": "scenarios",
      "delta": "slowmo30 / altTabbed / spiky — X1 unproducible"
    }
  ],
  "lineage": {
    "total": 268,
    "range": [
      "2026-05-19",
      "2026-07-07"
    ],
    "peak": 148,
    "buckets": [
      {
        "date": "2026-05-19",
        "total": 148,
        "counts": {}
      },
      {
        "date": "2026-05-23",
        "total": 0,
        "counts": {}
      },
      {
        "date": "2026-05-27",
        "total": 0,
        "counts": {}
      },
      {
        "date": "2026-05-31",
        "total": 0,
        "counts": {}
      },
      {
        "date": "2026-06-05",
        "total": 0,
        "counts": {}
      },
      {
        "date": "2026-06-09",
        "total": 0,
        "counts": {}
      },
      {
        "date": "2026-06-13",
        "total": 0,
        "counts": {}
      },
      {
        "date": "2026-06-17",
        "total": 0,
        "counts": {}
      },
      {
        "date": "2026-06-21",
        "total": 75,
        "counts": {}
      },
      {
        "date": "2026-06-25",
        "total": 21,
        "counts": {}
      },
      {
        "date": "2026-06-29",
        "total": 0,
        "counts": {}
      },
      {
        "date": "2026-07-03",
        "total": 24,
        "counts": {}
      }
    ],
    "themes": [
      {
        "name": "measurement honesty",
        "hint": "honest|real|cadence|slow-mo|repoint|clamp|suspend|stall|speed"
      },
      {
        "name": "dashboard + UI",
        "hint": "dashboard|tab|UI|kanban|card|ribbon|panel|css|js"
      },
      {
        "name": "persistence + history",
        "hint": "LiteDB|session|rollup|journal|writer|fingerprint|store"
      },
      {
        "name": "insights engine",
        "hint": "insight|detector|confidence|pattern"
      },
      {
        "name": "self-measurement + RAM",
        "hint": "RAM|memory|hook|install|scaffold|trim|self"
      },
      {
        "name": "testing + harness",
        "hint": "test|pin|harness|playwright|scenario|bench"
      }
    ],
    "series": [],
    "phases": [
      {
        "name": "the May sprint",
        "range": "2026-05-19 → 2026-05-21",
        "d": "scaffold → LiteDB persistence → interaction arsenal → autonomous perf pass → dashboard-first pivot → unified Data/ pipeline → the 21-addition tab rework. ~150 commits in three days; the architecture that still stands."
      },
      {
        "name": "the June consolidation",
        "range": "2026-06-22 → 2026-06-26",
        "d": "post-pause: the 3.7→1.0 GB RAM trim, the OKLCH component library, the insights-engine rework (20 detectors, 5 families), the L4/L6/L8 harness, Observatory + kanban, and the v0.27 cross-session history layer + data-quality patch."
      },
      {
        "name": "the honesty mega-batch",
        "range": "2026-07-07",
        "d": "one day, 9 versions (0.28.1→0.35.0): the real-cadence repoint end-to-end, sustained-slowness + draw-bound insights, per-feature ModConfig, phase-lane attribution (+0.001 ms/t measured), the memory guard, the shareable HTML report, popup cards + warming states, and the runtime-free scenario engine that pins all of it."
      }
    ],
    "arc": "Five-and-a-half weeks from scaffold to a 35-version observatory in three regimes: the May sprint (scaffold -> dashboard pivot -> data pipeline -> 21-addition tab rework), the June consolidation (RAM trim, component library, insights engine, cross-session history), and the 2026-07-07 honesty mega-batch — a single day that made every player-facing number mean what it says (real cadence, cause-split pauses, gated claims), then spent the trust it built on features: phase attribution, per-feature config, memory guard, the shareable report, and the popup-card UI layer, all pinned by a runtime-free scenario engine."
  },
  "repoTree": {
    "name": "PerformanceProfiler/",
    "anno": "read-only per-mod profiler for tModLoader (v0.35.0)",
    "children": [
      {
        "name": "AGENTS.md",
        "anno": "agent instructions (mirrors CLAUDE.md)",
        "file": true
      },
      {
        "name": "CLAUDE.md",
        "anno": "project collaboration brief (invariants, gates, style)",
        "file": true
      },
      {
        "name": "Data/",
        "node": "Data",
        "children": [
          {
            "name": "Aggregators/",
            "children": [
              {
                "name": "EventAggregator.cs",
                "file": true
              },
              {
                "name": "HeatmapAggregator.cs",
                "file": true
              },
              {
                "name": "HeatmapFold.cs",
                "file": true
              },
              {
                "name": "LagFingerprintAggregator.cs",
                "file": true
              },
              {
                "name": "LagRhythmAggregator.cs",
                "file": true
              },
              {
                "name": "PerModAttribution.cs",
                "file": true
              },
              {
                "name": "PerModCostTimeSeriesAggregator.cs",
                "file": true
              },
              {
                "name": "PerModSample.cs",
                "file": true
              },
              {
                "name": "PerModUsageAggregator.cs",
                "file": true
              },
              {
                "name": "PerTickAttributionRing.cs",
                "file": true
              },
              {
                "name": "SegmentAggregator.cs",
                "file": true
              },
              {
                "name": "Segments/"
              },
              {
                "name": "SessionActivityHeatStripAggregator.cs",
                "file": true
              }
            ]
          },
          {
            "name": "Collectors/",
            "children": [
              {
                "name": "AllocationCollector.cs",
                "file": true
              },
              {
                "name": "ContextTagger.cs",
                "file": true
              },
              {
                "name": "FrameTimeCollector.cs",
                "file": true
              },
              {
                "name": "HookCpuCollector.cs",
                "file": true
              },
              {
                "name": "ModRosterScanner.cs",
                "file": true
              }
            ]
          },
          {
            "name": "Contracts/",
            "children": [
              {
                "name": "RolloutContracts.cs",
                "file": true
              }
            ]
          },
          {
            "name": "DataRegistry.cs",
            "file": true
          },
          {
            "name": "DataStage.cs",
            "file": true
          },
          {
            "name": "Detectors/",
            "children": [
              {
                "name": "SpikeDetector.cs",
                "file": true
              },
              {
                "name": "StallDetector.cs",
                "file": true
              }
            ]
          },
          {
            "name": "IDataStream.cs",
            "file": true
          },
          {
            "name": "SessionContext.cs",
            "file": true
          },
          {
            "name": "Stats/",
            "children": [
              {
                "name": "AllocationCausalityStat.cs",
                "file": true
              },
              {
                "name": "Baseline.cs",
                "file": true
              },
              {
                "name": "DeathReplayStat.cs",
                "file": true
              },
              {
                "name": "EventsFeed.cs",
                "file": true
              },
              {
                "name": "EventsFeedStat.cs",
                "file": true
              },
              {
                "name": "GcPressureStat.cs",
                "file": true
              },
              {
                "name": "HookCoverageView.cs",
                "file": true
              },
              {
                "name": "KpiCalculator.Live.cs",
                "file": true
              },
              {
                "name": "KpiCalculator.cs",
                "file": true
              },
              {
                "name": "KpiSnapshot.cs",
                "file": true
              },
              {
                "name": "KpiStat.cs",
                "file": true
              },
              {
                "name": "MemoryTrend.cs",
                "file": true
              },
              {
                "name": "ModImpactScorer.cs",
                "file": true
              },
              {
                "name": "PerModContextAttendanceStat.cs",
                "file": true
              },
              {
                "name": "PerSegmentLagDensityStat.cs",
                "file": true
              },
              {
                "name": "RealtimeSpeed.cs",
                "file": true
              },
              {
                "name": "SelfHealthStat.cs",
                "file": true
              },
              {
                "name": "SessionChronicleStat.cs",
                "file": true
              },
              {
                "name": "SpikesStat.cs",
                "file": true
              },
              {
                "name": "StallsStat.cs",
                "file": true
              },
              {
                "name": "TransitionTrackStat.cs",
                "file": true
              }
            ]
          },
          {
            "name": "TickContext.cs",
            "file": true
          }
        ]
      },
      {
        "name": "Insights/",
        "node": "Insights",
        "children": [
          {
            "name": "CollectorInsightInput.cs",
            "file": true
          },
          {
            "name": "Contracts/",
            "children": [
              {
                "name": "IDriver.cs",
                "file": true
              },
              {
                "name": "IInsightInput.cs",
                "file": true
              },
              {
                "name": "IReferenceFrame.cs",
                "file": true
              }
            ]
          },
          {
            "name": "CrossSession/",
            "children": [
              {
                "name": "CrossSessionDetectors.cs",
                "file": true
              },
              {
                "name": "CrossSessionEvaluator.cs",
                "file": true
              },
              {
                "name": "CrossSessionInput.cs",
                "file": true
              }
            ]
          },
          {
            "name": "Detectors/",
            "children": [
              {
                "name": "AllocationBurstDetector.cs",
                "file": true
              },
              {
                "name": "ContextConditionalCostDetector.cs",
                "file": true
              },
              {
                "name": "ContextCorrelatedSpikeDetector.cs",
                "file": true
              },
              {
                "name": "CostConcentrationCore.cs",
                "file": true
              },
              {
                "name": "CostConcentrationDetector.cs",
                "file": true
              },
              {
                "name": "DrawBoundModCore.cs",
                "file": true
              },
              {
                "name": "DrawBoundModDetector.cs",
                "file": true
              },
              {
                "name": "FrameHeadroomDetector.cs",
                "file": true
              },
              {
                "name": "FrameJitterDetector.cs",
                "file": true
              },
              {
                "name": "FreeRemovalCandidateDetector.cs",
                "file": true
              },
              {
                "name": "GatedDetectors.cs",
                "file": true
              },
              {
                "name": "GcPauseCulpritDetector.cs",
                "file": true
              },
              {
                "name": "HeapLeakDetector.cs",
                "file": true
              },
              {
                "name": "HotHookDominanceCore.cs",
                "file": true
              },
              {
                "name": "HotHookDominanceDetector.cs",
                "file": true
              },
              {
                "name": "InteractionInsightDetectors.cs",
                "file": true
              },
              {
                "name": "NewContributorDetector.cs",
                "file": true
              },
              {
                "name": "PeakContributorToSpikeDetector.cs",
                "file": true
              },
              {
                "name": "SegmentDeathCorrelationDetector.cs",
                "file": true
              },
              {
                "name": "SegmentOutlierDetector.cs",
                "file": true
              },
              {
                "name": "SegmentTopModDetector.cs",
                "file": true
              },
              {
                "name": "SustainedCostShiftDetector.cs",
                "file": true
              },
              {
                "name": "SustainedSlownessCore.cs",
                "file": true
              },
              {
                "name": "SustainedSlownessDetector.cs",
                "file": true
              }
            ]
          },
          {
            "name": "Drivers/",
            "children": [
              {
                "name": "Drivers.cs",
                "file": true
              }
            ]
          },
          {
            "name": "IInsightDetector.cs",
            "file": true
          },
          {
            "name": "Insight.cs",
            "file": true
          },
          {
            "name": "InsightConstants.cs",
            "file": true
          },
          {
            "name": "InsightRenderer.cs",
            "file": true
          },
          {
            "name": "InsightStore.cs",
            "file": true
          },
          {
            "name": "InsightsEngine.cs",
            "file": true
          },
          {
            "name": "Publish/",
            "children": [
              {
                "name": "CrossCuttingSignalStat.cs",
                "file": true
              },
              {
                "name": "DormantSurfaceStat.cs",
                "file": true
              },
              {
                "name": "EngagementCostScatterStat.cs",
                "file": true
              },
              {
                "name": "InsightsStat.cs",
                "file": true
              },
              {
                "name": "ModInteractionAggregator.cs",
                "file": true
              },
              {
                "name": "ModObservatoryStat.cs",
                "file": true
              }
            ]
          },
          {
            "name": "RankingScorer.cs",
            "file": true
          },
          {
            "name": "ReferenceFrames/",
            "children": [
              {
                "name": "ContextBaseline.cs",
                "file": true
              },
              {
                "name": "TemporalBaseline.cs",
                "file": true
              }
            ]
          },
          {
            "name": "Shared/",
            "children": [
              {
                "name": "ModMetrics.cs",
                "file": true
              },
              {
                "name": "ModNames.cs",
                "file": true
              },
              {
                "name": "Shares.cs",
                "file": true
              },
              {
                "name": "Stats.cs",
                "file": true
              }
            ]
          }
        ]
      },
      {
        "name": "LICENSE",
        "file": true
      },
      {
        "name": "Localization/",
        "node": "Localization",
        "children": [
          {
            "name": "en-US_Mods.PerformanceProfiler.hjson",
            "file": true
          }
        ]
      },
      {
        "name": "PerformanceProfiler.cs",
        "anno": "Mod entry: config gates, DB open, HTTP server, keybind",
        "file": true
      },
      {
        "name": "PerformanceProfiler.csproj",
        "anno": "tML msbuild project",
        "file": true
      },
      {
        "name": "Persistence/",
        "node": "Persistence",
        "children": [
          {
            "name": "BsonShortNames.cs",
            "file": true
          },
          {
            "name": "Commands/",
            "children": [
              {
                "name": "ProfilerReportCommand.cs",
                "file": true
              },
              {
                "name": "QueryChatCommands.cs",
                "file": true
              },
              {
                "name": "QueryCommandBase.cs",
                "file": true
              }
            ]
          },
          {
            "name": "ContextTransitionWatcher.cs",
            "file": true
          },
          {
            "name": "CrossSessionStore.cs",
            "file": true
          },
          {
            "name": "DbReadModel.cs",
            "file": true
          },
          {
            "name": "DbWriteOp.cs",
            "file": true
          },
          {
            "name": "DbWriterThread.cs",
            "file": true
          },
          {
            "name": "EventJournal.cs",
            "file": true
          },
          {
            "name": "FingerprintCore.cs",
            "file": true
          },
          {
            "name": "History/",
            "children": [
              {
                "name": "HistoryStore.cs",
                "file": true
              },
              {
                "name": "HistoryViews.cs",
                "file": true
              },
              {
                "name": "RollupApplier.cs",
                "file": true
              },
              {
                "name": "RollupBackfill.cs",
                "file": true
              },
              {
                "name": "RollupFold.cs",
                "file": true
              },
              {
                "name": "SessionRollupInput.cs",
                "file": true
              }
            ]
          },
          {
            "name": "Interactions/",
            "children": [
              {
                "name": "InteractionItem.cs",
                "file": true
              },
              {
                "name": "InteractionNpc.cs",
                "file": true
              },
              {
                "name": "InteractionPlayer.cs",
                "file": true
              }
            ]
          },
          {
            "name": "LegacyJsonImporter.cs",
            "file": true
          },
          {
            "name": "Lifecycle/",
            "children": [
              {
                "name": "ModlistChange.cs",
                "file": true
              },
              {
                "name": "StoreReset.cs",
                "file": true
              }
            ]
          },
          {
            "name": "Migrations.cs",
            "file": true
          },
          {
            "name": "ModlistFingerprint.cs",
            "file": true
          },
          {
            "name": "PersistenceFileNames.cs",
            "file": true
          },
          {
            "name": "PlayerDeathDetector.cs",
            "file": true
          },
          {
            "name": "ProfilerCompactCommand.cs",
            "file": true
          },
          {
            "name": "ProfilerDatabase.cs",
            "file": true
          },
          {
            "name": "ProfilerPaths.cs",
            "file": true
          },
          {
            "name": "Records/",
            "children": [
              {
                "name": "BuffEventRow.cs",
                "file": true
              },
              {
                "name": "ContextBaselineRow.cs",
                "file": true
              },
              {
                "name": "ContextTransitionRow.cs",
                "file": true
              },
              {
                "name": "DamageDealtRow.cs",
                "file": true
              },
              {
                "name": "DamageTakenRow.cs",
                "file": true
              },
              {
                "name": "DeathDamageContributor.cs",
                "file": true
              },
              {
                "name": "InsightRow.cs",
                "file": true
              },
              {
                "name": "InstallArmRow.cs",
                "file": true
              },
              {
                "name": "ItemCreatedRow.cs",
                "file": true
              },
              {
                "name": "LoadoutSnapshotRow.cs",
                "file": true
              },
              {
                "name": "MetadataRow.cs",
                "file": true
              },
              {
                "name": "ModLifetimeRollupRow.cs",
                "file": true
              },
              {
                "name": "ModModlistRollupRow.cs",
                "file": true
              },
              {
                "name": "ModRow.cs",
                "file": true
              },
              {
                "name": "ModlistRow.cs",
                "file": true
              },
              {
                "name": "NpcSpawnRow.cs",
                "file": true
              },
              {
                "name": "PerSessionHookAggregate.cs",
                "file": true
              },
              {
                "name": "PerSessionModAggregate.cs",
                "file": true
              },
              {
                "name": "PlayerDeathRow.cs",
                "file": true
              },
              {
                "name": "SegmentRow.cs",
                "file": true
              },
              {
                "name": "SessionRow.cs",
                "file": true
              },
              {
                "name": "SpikeWindowRow.cs",
                "file": true
              },
              {
                "name": "StallClusterRow.cs",
                "file": true
              },
              {
                "name": "StallEventRow.cs",
                "file": true
              },
              {
                "name": "TickAggregateArchive.cs",
                "file": true
              },
              {
                "name": "TickAggregateCold.cs",
                "file": true
              },
              {
                "name": "TickAggregateWarm.cs",
                "file": true
              },
              {
                "name": "WelfordStat.cs",
                "file": true
              },
              {
                "name": "WorldRow.cs",
                "file": true
              },
              {
                "name": "WorldSnapshotRow.cs",
                "file": true
              }
            ]
          },
          {
            "name": "Report/",
            "children": [
              {
                "name": "HtmlReportWriter.cs",
                "file": true
              },
              {
                "name": "ReportExporter.cs",
                "file": true
              },
              {
                "name": "SessionReport.cs",
                "file": true
              }
            ]
          },
          {
            "name": "SessionSummaryLogger.cs",
            "file": true
          },
          {
            "name": "Streams/",
            "children": [
              {
                "name": "ContextTransitionStream.cs",
                "file": true
              },
              {
                "name": "IPersistenceStream.cs",
                "file": true
              },
              {
                "name": "InsightStream.cs",
                "file": true
              },
              {
                "name": "InteractionStreams.cs",
                "file": true
              },
              {
                "name": "ModlistStream.cs",
                "file": true
              },
              {
                "name": "PerSessionAggregateStream.cs",
                "file": true
              },
              {
                "name": "PlayerDeathStream.cs",
                "file": true
              },
              {
                "name": "RollupStream.cs",
                "file": true
              },
              {
                "name": "SegmentStream.cs",
                "file": true
              },
              {
                "name": "SessionRecorder.cs",
                "file": true
              },
              {
                "name": "SessionStream.cs",
                "file": true
              },
              {
                "name": "SpikeStream.cs",
                "file": true
              },
              {
                "name": "StallClusterStream.cs",
                "file": true
              },
              {
                "name": "StallStream.cs",
                "file": true
              },
              {
                "name": "StreamJson.cs",
                "file": true
              },
              {
                "name": "StreamRegistry.cs",
                "file": true
              },
              {
                "name": "TickAggregateStream.cs",
                "file": true
              },
              {
                "name": "WorldSnapshotStream.cs",
                "file": true
              }
            ]
          },
          {
            "name": "TickDownsampler.cs",
            "file": true
          },
          {
            "name": "WorldSnapshotter.cs",
            "file": true
          }
        ]
      },
      {
        "name": "ProfilerConfig.cs",
        "anno": "ModConfig: per-feature impact-grouped sliders (S23)",
        "file": true
      },
      {
        "name": "Profiling/",
        "node": "Profiling",
        "children": [
          {
            "name": "EnumStringTable.cs",
            "file": true
          },
          {
            "name": "Events/",
            "children": [
              {
                "name": "BiomeBitset.cs",
                "file": true
              },
              {
                "name": "BiomeDescriptor.cs",
                "file": true
              },
              {
                "name": "BiomeRegistry.cs",
                "file": true
              },
              {
                "name": "BossSampler.cs",
                "file": true
              },
              {
                "name": "BossSlotArray.cs",
                "file": true
              },
              {
                "name": "BucketStats.cs",
                "file": true
              },
              {
                "name": "EventContext.cs",
                "file": true
              },
              {
                "name": "GameMode.cs",
                "file": true
              },
              {
                "name": "InvasionId.cs",
                "file": true
              },
              {
                "name": "SubworldProbe.cs",
                "file": true
              },
              {
                "name": "WeatherFlags.cs",
                "file": true
              },
              {
                "name": "WeatherSources.cs",
                "file": true
              }
            ]
          },
          {
            "name": "HookBackend.cs",
            "file": true
          },
          {
            "name": "HookCategoryRouter.cs",
            "file": true
          },
          {
            "name": "HookInterceptor.cs",
            "file": true
          },
          {
            "name": "HookSurfaceCache.cs",
            "file": true
          },
          {
            "name": "ILHookInterceptor.cs",
            "file": true
          },
          {
            "name": "LangNameCache.cs",
            "file": true
          },
          {
            "name": "MetricCollector.cs",
            "file": true
          },
          {
            "name": "ModOwnerCache.cs",
            "file": true
          },
          {
            "name": "ModRamReader.cs",
            "file": true
          },
          {
            "name": "Pools/",
            "children": [
              {
                "name": "IPoolReset.cs",
                "file": true
              },
              {
                "name": "ListPool.cs",
                "file": true
              },
              {
                "name": "RowPool.cs",
                "file": true
              }
            ]
          },
          {
            "name": "ProbeStack.cs",
            "file": true
          },
          {
            "name": "ProfilerFocusProbe.cs",
            "file": true
          },
          {
            "name": "ProfilerSelfHealth.cs",
            "file": true
          },
          {
            "name": "ProfilerSystem.cs",
            "file": true
          },
          {
            "name": "RingBuffer.cs",
            "file": true
          },
          {
            "name": "TickFrame.cs",
            "file": true
          },
          {
            "name": "Time.cs",
            "file": true
          },
          {
            "name": "Util/",
            "children": [
              {
                "name": "BoolIndex.cs",
                "file": true
              }
            ]
          }
        ]
      },
      {
        "name": "README.md",
        "anno": "directional doc: pitch, tabs, atlas-linked roadmap",
        "file": true
      },
      {
        "name": "Tests/",
        "node": "Tests",
        "children": [
          {
            "name": "AuditPin_Baseline_FastPath.cs",
            "file": true
          },
          {
            "name": "AuditPin_Insights_Without.cs",
            "file": true
          },
          {
            "name": "AuditPin_Metric_FusedSum.cs",
            "file": true
          },
          {
            "name": "AuditPin_Metric_Reciprocal.cs",
            "file": true
          },
          {
            "name": "AuditPin_Web_Journal.cs",
            "file": true
          },
          {
            "name": "BaselineTests.cs",
            "file": true
          },
          {
            "name": "BoolIndexTests.cs",
            "file": true
          },
          {
            "name": "HookInstallRetentionDiagnostics.cs",
            "file": true
          },
          {
            "name": "InsightStoreTests.cs",
            "file": true
          },
          {
            "name": "Insights/",
            "children": [
              {
                "name": "CostConcentrationCoreTests.cs",
                "file": true
              },
              {
                "name": "CrossSessionStoreTests.cs",
                "file": true
              },
              {
                "name": "HotHookDominanceCoreTests.cs",
                "file": true
              },
              {
                "name": "ReferenceFrameTests.cs",
                "file": true
              },
              {
                "name": "SharedPrimitivesTests.cs",
                "file": true
              },
              {
                "name": "TemporalBaselineTests.cs",
                "file": true
              }
            ]
          },
          {
            "name": "PerformanceProfiler.Tests.csproj",
            "file": true
          },
          {
            "name": "Persistence/",
            "children": [
              {
                "name": "CrossSessionDetectorTests.cs",
                "file": true
              },
              {
                "name": "HistoryStoreTests.cs",
                "file": true
              },
              {
                "name": "LifecycleTests.cs",
                "file": true
              },
              {
                "name": "PersistenceBenchmarkTests.cs",
                "file": true
              },
              {
                "name": "PersistenceRoundTripTests.cs",
                "file": true
              },
              {
                "name": "RollupFoldTests.cs",
                "file": true
              }
            ]
          },
          {
            "name": "PoolsTests.cs",
            "file": true
          },
          {
            "name": "RankingScorerTests.cs",
            "file": true
          },
          {
            "name": "RingBufferTests.cs",
            "file": true
          },
          {
            "name": "Simulation/",
            "children": [
              {
                "name": "FingerprintPins.cs",
                "file": true
              },
              {
                "name": "HonestyPins.cs",
                "file": true
              },
              {
                "name": "MemoryTrendPins.cs",
                "file": true
              },
              {
                "name": "PhaseLaneBench.cs",
                "file": true
              },
              {
                "name": "PhaseLanePins.cs",
                "file": true
              },
              {
                "name": "ReportPins.cs",
                "file": true
              },
              {
                "name": "ScenarioRunner.cs",
                "file": true
              },
              {
                "name": "Scenarios.cs",
                "file": true
              },
              {
                "name": "StoreRoundTripPins.cs",
                "file": true
              }
            ]
          },
          {
            "name": "StallClassifierTests.cs",
            "file": true
          },
          {
            "name": "StallDetectorTests.cs",
            "file": true
          },
          {
            "name": "TimeTests.cs",
            "file": true
          },
          {
            "name": "_TestNamespaceStubs.cs",
            "file": true
          },
          {
            "name": "bin/",
            "children": [
              {
                "name": "Debug/"
              }
            ]
          },
          {
            "name": "obj/",
            "children": [
              {
                "name": "Debug/"
              },
              {
                "name": "PerformanceProfiler.Tests.csproj.nuget.dgspec.json",
                "file": true
              },
              {
                "name": "PerformanceProfiler.Tests.csproj.nuget.g.props",
                "file": true
              },
              {
                "name": "PerformanceProfiler.Tests.csproj.nuget.g.targets",
                "file": true
              },
              {
                "name": "project.assets.json",
                "file": true
              },
              {
                "name": "project.nuget.cache",
                "file": true
              }
            ]
          }
        ]
      },
      {
        "name": "UI/",
        "node": "UI",
        "children": [
          {
            "name": "Overlay/",
            "children": [
              {
                "name": "Components/"
              },
              {
                "name": "IOverlayTab.cs",
                "file": true
              },
              {
                "name": "OverlayDraw.cs",
                "file": true
              },
              {
                "name": "OverlayLayout.cs",
                "file": true
              },
              {
                "name": "OverlayMode.cs",
                "file": true
              },
              {
                "name": "OverlayPanel.cs",
                "file": true
              },
              {
                "name": "OverlayState.cs",
                "file": true
              },
              {
                "name": "TabRegistry.cs",
                "file": true
              },
              {
                "name": "Tabs/"
              }
            ]
          },
          {
            "name": "ProfilerOverlay.cs",
            "file": true
          },
          {
            "name": "ProfilerOverlaySystem.cs",
            "file": true
          },
          {
            "name": "ProfilerTheme.cs",
            "file": true
          }
        ]
      },
      {
        "name": "Web/",
        "node": "Web",
        "children": [
          {
            "name": "Assets/",
            "children": [
              {
                "name": "Css/"
              },
              {
                "name": "DashboardAssets.cs",
                "file": true
              },
              {
                "name": "IndexHtml.Closing.cs",
                "file": true
              },
              {
                "name": "IndexHtml.Insights.cs",
                "file": true
              },
              {
                "name": "IndexHtml.Lag.cs",
                "file": true
              },
              {
                "name": "IndexHtml.Memory.cs",
                "file": true
              },
              {
                "name": "IndexHtml.Observatory.cs",
                "file": true
              },
              {
                "name": "IndexHtml.Preamble.cs",
                "file": true
              },
              {
                "name": "IndexHtml.Self.cs",
                "file": true
              },
              {
                "name": "IndexHtml.Summary.cs",
                "file": true
              },
              {
                "name": "IndexHtml.Timeline.cs",
                "file": true
              },
              {
                "name": "IndexHtml.cs",
                "file": true
              },
              {
                "name": "Js/"
              }
            ]
          },
          {
            "name": "DashboardRouter.History.cs",
            "file": true
          },
          {
            "name": "DashboardRouter.Hooks.cs",
            "file": true
          },
          {
            "name": "DashboardRouter.Insights.cs",
            "file": true
          },
          {
            "name": "DashboardRouter.Lag.cs",
            "file": true
          },
          {
            "name": "DashboardRouter.Memory.cs",
            "file": true
          },
          {
            "name": "DashboardRouter.Modlists.cs",
            "file": true
          },
          {
            "name": "DashboardRouter.Mods.cs",
            "file": true
          },
          {
            "name": "DashboardRouter.Report.cs",
            "file": true
          },
          {
            "name": "DashboardRouter.Reset.cs",
            "file": true
          },
          {
            "name": "DashboardRouter.Self.cs",
            "file": true
          },
          {
            "name": "DashboardRouter.Summary.cs",
            "file": true
          },
          {
            "name": "DashboardRouter.Timeline.cs",
            "file": true
          },
          {
            "name": "DashboardRouter.cs",
            "file": true
          },
          {
            "name": "Server/",
            "children": [
              {
                "name": "DashboardHttpServer.cs",
                "file": true
              },
              {
                "name": "HttpRequest.cs",
                "file": true
              },
              {
                "name": "HttpResponse.cs",
                "file": true
              }
            ]
          }
        ]
      },
      {
        "name": "bin/",
        "children": [
          {
            "name": "Debug/",
            "node": "bin_Debug",
            "children": [
              {
                "name": "net8.0/"
              }
            ]
          },
          {
            "name": "Release/",
            "node": "bin_Release",
            "children": [
              {
                "name": "net8.0/"
              }
            ]
          }
        ]
      },
      {
        "name": "build.txt",
        "anno": "tML manifest (version 0.35.0; buildIgnore)",
        "file": true
      },
      {
        "name": "context/",
        "children": [
          {
            "name": "_Overview.md",
            "file": true
          },
          {
            "name": "_staleness-report.md",
            "file": true
          },
          {
            "name": "arch/",
            "children": [
              {
                "name": "app.js",
                "file": true
              },
              {
                "name": "features.js",
                "file": true
              },
              {
                "name": "graph.js",
                "file": true
              },
              {
                "name": "index.html",
                "file": true
              },
              {
                "name": "styles.css",
                "file": true
              }
            ]
          },
          {
            "name": "architecture.html",
            "file": true
          },
          {
            "name": "integration/",
            "children": [
              {
                "name": "integration-map.md",
                "file": true
              }
            ]
          },
          {
            "name": "notes/",
            "children": [
              {
                "name": "0271-data-quality-and-snapshot-context.md",
                "file": true
              },
              {
                "name": "compile-gate.md",
                "file": true
              },
              {
                "name": "conventions.md",
                "file": true
              },
              {
                "name": "cross-session-history-layer.md",
                "file": true
              },
              {
                "name": "decisions.md",
                "file": true
              },
              {
                "name": "feature-atlas.md",
                "file": true
              },
              {
                "name": "future-html-report.md",
                "file": true
              },
              {
                "name": "future-insights-rework.md",
                "file": true
              },
              {
                "name": "future-settings-design.md",
                "file": true
              },
              {
                "name": "future-unified-data-interface.md",
                "file": true
              },
              {
                "name": "insights-rework-status.md",
                "file": true
              },
              {
                "name": "modlist-pre-upgrade-2026-06-22.md",
                "file": true
              },
              {
                "name": "philosophy.md",
                "file": true
              },
              {
                "name": "ui-overhaul-plan.md",
                "file": true
              }
            ]
          },
          {
            "name": "notes.md",
            "file": true
          },
          {
            "name": "pages/",
            "children": [
              {
                "name": "_index.md",
                "file": true
              },
              {
                "name": "insights.md",
                "file": true
              },
              {
                "name": "lag.md",
                "file": true
              },
              {
                "name": "memory.md",
                "file": true
              },
              {
                "name": "self.md",
                "file": true
              },
              {
                "name": "summary.md",
                "file": true
              },
              {
                "name": "timeline.md",
                "file": true
              }
            ]
          },
          {
            "name": "perf-pass/",
            "children": [
              {
                "name": "baseline.md",
                "file": true
              },
              {
                "name": "deferred.md",
                "file": true
              },
              {
                "name": "verification.md",
                "file": true
              }
            ]
          },
          {
            "name": "plans/",
            "children": [
              {
                "name": "code-health-audit/"
              },
              {
                "name": "database-rework.md",
                "file": true
              },
              {
                "name": "e2e-testing.md",
                "file": true
              },
              {
                "name": "extensive-testing-infrastructure.md",
                "file": true
              },
              {
                "name": "feature-settings.md",
                "file": true
              },
              {
                "name": "honesty-completion.md",
                "file": true
              },
              {
                "name": "html-session-report.md",
                "file": true
              },
              {
                "name": "insights-engine.md",
                "file": true
              },
              {
                "name": "install-ram-optimisation.md",
                "file": true
              },
              {
                "name": "loop-anatomy.md",
                "file": true
              },
              {
                "name": "memory-guard.md",
                "file": true
              },
              {
                "name": "ui-component-library.md",
                "file": true
              },
              {
                "name": "ui-overhaul.md",
                "file": true
              },
              {
                "name": "ui-ux-audit.md",
                "file": true
              }
            ]
          },
          {
            "name": "systems/",
            "children": [
              {
                "name": "allocation-tracking.md",
                "file": true
              },
              {
                "name": "dashboard-audit-harness.md",
                "file": true
              },
              {
                "name": "data-pipeline.md",
                "file": true
              },
              {
                "name": "events-and-context.md",
                "file": true
              },
              {
                "name": "hook-instrumentation.md",
                "file": true
              },
              {
                "name": "insights-engine.md",
                "file": true
              },
              {
                "name": "metric-collection.md",
                "file": true
              },
              {
                "name": "mod-lifecycle.md",
                "file": true
              },
              {
                "name": "overlay.md",
                "file": true
              },
              {
                "name": "persistence.md",
                "file": true
              },
              {
                "name": "spike-detection.md",
                "file": true
              },
              {
                "name": "test-harness.md",
                "file": true
              },
              {
                "name": "web-dashboard.md",
                "file": true
              }
            ]
          },
          {
            "name": "tmodloader/",
            "children": [
              {
                "name": "engagement-surfaces.md",
                "file": true
              },
              {
                "name": "hook-surface.md",
                "file": true
              },
              {
                "name": "ilhook-migration-research.md",
                "file": true
              },
              {
                "name": "lifecycle-and-loop.md",
                "file": true
              },
              {
                "name": "mod-identity.md",
                "file": true
              },
              {
                "name": "monomod-detours.md",
                "file": true
              },
              {
                "name": "ui-system.md",
                "file": true
              }
            ]
          }
        ]
      },
      {
        "name": "description.txt",
        "anno": "Workshop blurb (stale, S30 backlog)",
        "file": true
      },
      {
        "name": "design/",
        "node": "design",
        "children": [
          {
            "name": "dashboard-preview.html",
            "file": true
          },
          {
            "name": "dashboard-preview.html.artifact.json",
            "file": true
          },
          {
            "name": "dashboard-shots/",
            "children": [
              {
                "name": "crosscut-overlap-bug.png",
                "file": true
              },
              {
                "name": "crosscut-render.png",
                "file": true
              },
              {
                "name": "tab-insights.png",
                "file": true
              },
              {
                "name": "tab-lag.png",
                "file": true
              },
              {
                "name": "tab-memory.png",
                "file": true
              },
              {
                "name": "tab-self.png",
                "file": true
              },
              {
                "name": "tab-summary.png",
                "file": true
              },
              {
                "name": "tab-timeline.png",
                "file": true
              }
            ]
          },
          {
            "name": "dashboard-ui-spec.md",
            "file": true
          },
          {
            "name": "mockups/",
            "children": [
              {
                "name": "Mockups.html",
                "file": true
              },
              {
                "name": "in-game-ui-designs-1.html",
                "file": true
              }
            ]
          },
          {
            "name": "renders/",
            "children": [
              {
                "name": "mqs2zac8-image.png",
                "file": true
              },
              {
                "name": "mqs3uz2g-image.png",
                "file": true
              },
              {
                "name": "mqs3vq55-image.png",
                "file": true
              },
              {
                "name": "mqs3wx35-image.png",
                "file": true
              },
              {
                "name": "mqs3z83o-image.png",
                "file": true
              },
              {
                "name": "mqs4072w-image.png",
                "file": true
              },
              {
                "name": "mqs4sz3b-image.png",
                "file": true
              }
            ]
          },
          {
            "name": "workshop-description.txt",
            "file": true
          }
        ]
      },
      {
        "name": "lib/",
        "node": "lib",
        "children": [
          {
            "name": "LiteDB.dll",
            "file": true
          }
        ]
      },
      {
        "name": "obj/",
        "node": "obj",
        "children": [
          {
            "name": "Debug/",
            "children": [
              {
                "name": "net8.0/"
              }
            ]
          },
          {
            "name": "PerformanceProfiler.csproj.nuget.dgspec.json",
            "file": true
          },
          {
            "name": "PerformanceProfiler.csproj.nuget.g.props",
            "file": true
          },
          {
            "name": "PerformanceProfiler.csproj.nuget.g.targets",
            "file": true
          },
          {
            "name": "Release/",
            "children": [
              {
                "name": "net8.0/"
              }
            ]
          },
          {
            "name": "project.assets.json",
            "file": true
          },
          {
            "name": "project.nuget.cache",
            "file": true
          }
        ]
      },
      {
        "name": "tools/",
        "children": [
          {
            "name": "preview/",
            "node": "tools_preview",
            "children": [
              {
                "name": "README.md",
                "anno": "directional doc: pitch, tabs, atlas-linked roadmap",
                "file": true
              },
              {
                "name": "build_preview_html.py",
                "file": true
              },
              {
                "name": "fixtures/"
              },
              {
                "name": "render.py",
                "file": true
              }
            ]
          },
          {
            "name": "testing/",
            "node": "tools_testing",
            "children": [
              {
                "name": "README.md",
                "anno": "directional doc: pitch, tabs, atlas-linked roadmap",
                "file": true
              },
              {
                "name": "audit.py",
                "file": true
              },
              {
                "name": "design-bar.md",
                "file": true
              },
              {
                "name": "pp_testing/"
              },
              {
                "name": "requirements.txt",
                "file": true
              },
              {
                "name": "rubric.md",
                "file": true
              },
              {
                "name": "run_all.sh",
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
