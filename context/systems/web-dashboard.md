# Web Dashboard

*Maturity: comprehensive · Stability: unstable — the server, asset pipeline, and routing are settled; per-tab endpoint shapes and JS renderers evolve with each measurement subsystem.*

## Scope / Purpose

The web dashboard is the **primary player surface** from v0.9.0 onward. It replaced the in-game overlay (now archived under `UI/`, see `systems/overlay.md`) after five overlay iterations all hit the same wall: Terraria's sprite-font UI is poor for charts, dense tables, and click-targets. The pivot sends the data to a browser instead.

The mod stands up a loopback-only HTTP server inside its own process at `Mod.Load`. The player presses **F9** in-game; the mod opens the default browser at `http://127.0.0.1:27277/` (or the next free port). A single-page app loads once, then polls JSON endpoints a few times per second and renders live.

Hard properties:

- **Loopback-only.** The server binds `127.0.0.1` exclusively. Nothing leaves the machine; no telemetry, no firewall prompt (loopback bypasses the macOS application firewall).
- **GET-only, read-only.** The server serves the SPA bundle and JSON state. It has no write path into game state (Invariant 1). The dashboard is the descriptive surface; it never edits anything.
- **Zero setup.** The HTML/CSS/JS ship as C# string constants packed inside the `.tmod`. No Node, no Docker, no asset build, no config files.
- **One discovery line.** On world entry the mod prints a single chat hint ("Press F9 for the dashboard (<url>)"). The mod is otherwise invisible in-game.

## Boundaries / Ownership

Files (every line under `Web/`):

| Concern | File |
|---|---|
| Raw-TCP HTTP/1.1 server (accept loop, request parse, response write) | `Web/Server/DashboardHttpServer.cs` |
| Parsed inbound request (method, path, raw target) | `Web/Server/HttpRequest.cs` |
| Outbound response + factory helpers (Html / Json / PlainText / NotFound) | `Web/Server/HttpResponse.cs` |
| Route table + per-tab `Build*` builders + asset byte cache | `Web/DashboardRouter.cs` (+ `.Summary` / `.Mods` / `.Hooks` / `.Timeline` / `.Lag` / `.Insights` / `.Self` partials) |
| Bundle assembly (concat CSS / JS / HTML fragments, cache once) | `Web/Assets/DashboardAssets.cs` |
| SPA HTML shell | `Web/Assets/IndexHtml.cs` (+ `.Preamble` / `.Summary` / `.Timeline` / `.Lag` / `.Insights` / `.Self` / `.Closing` partials) |
| Stylesheet fragments (17 partials) | `Web/Assets/Css/Css.*.cs` |
| Script fragments (16 partials) | `Web/Assets/Js/Js.*.cs` |
| Mod-wide lifecycle (`Dashboard` singleton, bind, dispose) | `PerformanceProfiler.cs` |
| F9 keybind registration + cross-platform browser open | `UI/ProfilerOverlaySystem.cs`, `PerformanceProfiler.cs` (`ProfilerPlayer`) |

Owns:

- The loopback TCP listener, the accept loop, the thread-per-request fan-out, HTTP/1.1 parse + write.
- The strict route allowlist; anything unmatched returns 404, any non-GET returns 405.
- Formatting pipeline snapshots into JSON wire shapes (one flat anonymous object per endpoint).
- Assembling and byte-caching the SPA bundle once at type-init.
- The cross-platform browser launch (`open` / `xdg-open` / shell), with a chat-URL fallback on failure.

Does **not** own:

- **Any number the dashboard displays.** Per the data-pipeline policy (`systems/data-pipeline.md`): if it produces a number, it lives in `Data/`. The routers format snapshots; they do not derive ratios, ranks, thresholds, or aggregates. The only arithmetic that survives in a router is trivial wire-shape adaptation (e.g. summing per-category ms into a per-mod total, byte→MB unit conversion, a 30s rolling mean noted as a known formatting-layer shortcut in `BuildNow`).
- **Snapshot production.** Every `Build*` method pulls via `DataRegistry.Shared.Lookup<TSnapshot>(name).CurrentSnapshot()`. The streams and their math live in `Data/`.
- **The keybind binding itself.** `KeybindLoader.RegisterKeybind(Mod, "OpenDashboard", "F9")` is owned by `ProfilerOverlaySystem` (a vestige of the overlay system that now only carries this one bind); the binding object lives in tModLoader.

## Current Implemented Reality

### The raw-TCP server

`DashboardHttpServer` is a hand-rolled HTTP/1.1 server (~320 LOC incl. doc-comments; the implementation the doc-comment calls "~250 LOC") built on `System.Net.Sockets.TcpListener`, not `HttpListener`.

**Why raw TCP, not `HttpListener`:** `HttpListener` routes through Windows' `http.sys` kernel driver, which refuses to bind for non-admin users unless a URL ACL is configured via `netsh http add urlacl`. That breaks the "load → F9 → browser just works" contract; Workshop players will not run admin commands. `TcpListener` is a userspace socket and needs no admin on any port ≥ 1024 on every target platform.

**Scope (deliberate constraints):** loopback-only, GET-only, plain HTTP/1.1, `Connection: close` per response, single-threaded accept loop with one background thread per request. Sufficient for a handful of `fetch` calls per second from one client; explicitly not a general-purpose server.

**Port binding + search:** binds the first free port in `[PortRangeStart=27277, PortRangeEnd=27287]` on `IPAddress.Loopback`. 27277 is chosen to avoid Terraria's multiplayer host port (7777). If every port in the range is busy, the constructor throws; `Mod.Load` catches it, sets `Dashboard = null`, and logs a warning (see degradation under Known Issues).

**Request handling specifics:**

- `TryReadRequest` reads until `\r\n\r\n` (end of headers) into an 8 KB buffer; no body is ever read (GET-only).
- Read timeout is enforced via non-throwing `Socket.Poll` (100 ms poll interval, 3 s total budget), deliberately **not** `NetworkStream.ReadTimeout`. A throwing read timeout would raise `IOException` on every idle browser preconnect / aborted keep-alive, which tModLoader's first-chance exception hook then logs as noise in `client.log`. Polling returns cleanly instead.
- The response writer emits status line, `Content-Type`, `Content-Length`, `Access-Control-Allow-Origin: *` (harmless on loopback, eases dev-time HTML editing), `Cache-Control: no-store` (every poll returns fresh state), `Connection: close`.
- Route exceptions are caught per-request and returned as a `500` with the exception type/message; the accept loop survives any single request failure.

Renamed from `TinyHttpServer` to `DashboardHttpServer` on 2026-05-21 (the "tiny" framing referred to LOC, not role; it is the only server the mod ships).

### Route table (the full endpoint inventory)

`DashboardRouter.Route(HttpRequest)` is a strict `switch` on `req.Path`. Non-GET → 405. Unmatched → 404. **32 named route arms + a default** (33 arms total): 4 asset/static + 28 JSON `/api/*` endpoints, plus the `_` fallthrough to 404. Each `Build*` reads one or more `Data/` snapshots and serialises a flat anonymous object via `System.Text.Json`.

Static / asset routes (4):

| Path | Served | Source |
|---|---|---|
| `/` | SPA HTML | `DashboardAssets.IndexHtml` (string) |
| `/dashboard.css` | Stylesheet | `CachedCssBytes` (UTF-8, cached at type-init) |
| `/dashboard.js` | Script bundle | `CachedJsBytes` (UTF-8, cached at type-init) |
| `/favicon.ico` | Empty 200 | `Array.Empty<byte>()` |

JSON endpoints, grouped by the dashboard tab that consumes them:

| Group | Endpoint | `Build*` | Snapshot read (via `DataRegistry.Shared.Lookup`) |
|---|---|---|---|
| Summary (core, always polled) | `/api/now` | `BuildNow` | `FrameTime` + `Kpi` + `SelfHealth` + `Allocation` + `Spikes` + `Stalls` + `Segments` |
| Summary | `/api/frames` | `BuildFrames` | `FrameTime` + `Spikes` |
| Summary | `/api/segments` | `BuildSegments` | `Segments` (`SegmentAggregator`) |
| Summary | `/api/heatmap` | `BuildHeatmap` | `Heatmap` (`HeatmapAggregator`) |
| Summary | `/api/events` | `BuildEvents` | `EventsFeed` (`EventsFeedStat`) |
| Summary (Mods table) | `/api/mods` | `BuildMods` | `HookCpu` + `Allocation` |
| Summary (tree drill, on-demand) | `/api/hooks` | `BuildHooks` | `HookCpu` + `Allocation` |
| Summary (insight cards, legacy) | `/api/insights` | `BuildInsights` | `Insights` (`InsightsStat`) |
| Lag | `/api/spikes` | `BuildSpikes` | `Spikes` (`SpikesStat`) |
| Lag | `/api/stalls` | `BuildStalls` | `Stalls` (`StallsStat`) |
| Lag | `/api/lag-clusters` | `BuildLagClusters` | `LagClusters` (L1+L2) |
| Lag | `/api/gc` | `BuildGcPressure` | `GcPressure` (L3) |
| Lag | `/api/lag-density` | `BuildSegmentLagDensity` | `SegmentLagDensity` (L4) |
| Lag | `/api/gc-causality` | `BuildAllocCausality` | `AllocCausality` (L6) |
| Lag | `/api/lag-rhythm` | `BuildLagRhythm` | `LagRhythm` (L7) |
| Timeline | `/api/segment-lifetime` | `BuildSegmentLifetime` | `SegmentLifetime` (T1+T2) |
| Timeline | `/api/segment-mod-attribution` | `BuildSegmentModAttribution` | `SegmentModAttribution` (T1) |
| Timeline | `/api/transitions` | `BuildTransitions` | `TransitionTrack` (T3) |
| Timeline | `/api/activity-strip` | `BuildActivityStrip` | `ActivityHeatStrip` (T4) |
| Timeline | `/api/attendance` | `BuildAttendance` | `Attendance` (T5) |
| Timeline | `/api/deaths` | `BuildDeaths` | `DeathReplay` (T6) |
| Timeline | `/api/chronicle` | `BuildChronicle` | `SessionChronicle` (T7) |
| Insights | `/api/mod-observatory` | `BuildModObservatory` | `ModObservatory` (I1+I3+I4) |
| Insights | `/api/dormant` | `BuildDormantSurface` | `DormantSurface` (I2) |
| Insights | `/api/cross-cutting` | `BuildCrossCutting` | `CrossCutting` (I5) |
| Insights | `/api/engagement-cost` | `BuildEngagementCost` | `EngagementCost` (I6) |
| Insights | `/api/mod-interaction` | `BuildModInteraction` | `ModInteraction` (I7) |

The Timeline / Lag / Insights endpoints all resolve their snapshots through the `RolloutStreamNames` constants in `Data/Contracts/RolloutContracts.cs` (the frozen contract layer). The Self tab adds the 28th `/api/*` endpoint:

| Group | Endpoint | `Build*` | Snapshot read |
|---|---|---|---|
| Self | `/api/self` | `BuildSelf` | `SelfHealth` (`SelfHealthStat`) |

`/api/self` is polled on its own 5 s cadence (`pollSelf`) rather than per-tab.

The lone shared helper, `TopContributors`, lives in `DashboardRouter.cs` and sorts a spike window's per-mod ms for the spike contributor list. It is a wire-shape formatter (sort + take-N + name deref), not a derivation.

### Asset pipeline

`DashboardAssets` is a partial static class. The HTML, CSS, and JS each live as `private const string` raw verbatim strings split across many partial-class fragment files for editability:

- **CSS** — 17 fragments under `Web/Assets/Css/` (`Css.Palette.cs`, `Css.Shell.cs`, `Css.Panels.cs`, then per-component: `Summary`, `Mods`, `Timeline`, `Lag`, `Insights`, `Self`, `ModCard`, `Tooltip`, `Footer`, `Kpis`, `Heatmap`, `NowPlaying`, `ChartToggle`, `Scrollbar`). `Css.Palette.cs` defines the `:root` design tokens (colour palette, `--perf-0..4` gradient, `--mono` / `--ui` font stacks). `DashboardAssets.Css` concatenates them in a deliberate order: palette first (defines vars), shell next (layout primitives), components, scrollbar last (so the webkit override wins). No `@import`; the Inter / JetBrains Mono fonts are loaded by `<link>` in the HTML preamble.
- **JS** — 16 fragments under `Web/Assets/Js/`. `DashboardAssets.Js` concatenates them in execution order: `Config` (state + `POLL_*` constants) → `Tabs` (routing) → `Polling` (core loops) → `Helpers` → `Tooltips` → `Topbar` → `Render` → per-tab renderers (`Kpis`, `Summary`, `Mods`, `ModCard`, `Timeline`, `Lag`, `Insights`, `Self`). Order matters: later fragments reference earlier-declared globals (`lastNow`, `activeTab`) and helpers.
- **HTML** — `IndexHtml.cs` concatenates `Preamble`, `Summary`, `Timeline`, `Lag`, `Insights`, `Self`, `Closing` into one SPA shell with a top bar, a five-button tab strip, two state overlays (disconnected / no-world), and one `tab-pane` per tab.

**Invariant-2 alignment in the asset path:** the CSS and JS bundles are immutable for the mod's lifetime, so `DashboardRouter` encodes each to UTF-8 bytes **once** at type-init (`CachedCssBytes` / `CachedJsBytes`). No per-request re-encoding; cold-tab refreshes previously paid tens of KB of allocator pressure per hit. The SPA HTML is also assembled once into the static `IndexHtml` string.

### The SPA: tabs and polling

The shipped SPA has **five tabs**: **Summary, Timeline, Lag, Insights, Self** (tab strip in `IndexHtml.Preamble.cs`, keyboard `1`-`5` in `Js.Tabs.cs`). Note the gap from the README's narrative six (Now, Mods, Timeline, Spikes, Insights, Self): the SPA merges *Now* + *Mods* into the single **Summary** tab, and renames/expands *Spikes* into **Lag**. The README describes the conceptual model; the SPA tab layout above is the code reality.

Polling cadences (`POLL_*` in `Js.Config.cs`; loops in `Js.Polling.cs` and per-tab files):

| Loop | Cadence | Endpoints | Gate |
|---|---|---|---|
| `pollNow` | 500 ms | `/api/now`, `/api/frames`, `/api/segments` | always |
| `pollDetail` | 1500 ms | `/api/mods`, `/api/spikes`, `/api/stalls`, `/api/insights`, `/api/events` | always |
| `pollHooks` | 2500 ms | `/api/hooks` | only when Summary active **and** ≥1 mod row expanded |
| `pollSelf` | 5000 ms | `/api/self` | always |
| `pollHeatmap` | 3000 ms | `/api/heatmap` | always |
| `updateConnection` | 1000 ms | (none — DOM state) | always |
| `pollTimelineData` | 2500 ms | the 7 Timeline endpoints | only when `activeTab === 'timeline'` |
| `pollLagData` | 3000 ms | the 5 Lag endpoints | only when `activeTab === 'lag'` |
| `pollInsightsData` | 3000 ms | the 5 Insights endpoints | only when `activeTab === 'insights'` |

The README's "updates 2-4 times a second" describes the core `pollNow` (500 ms = 2 Hz) layered with `pollDetail`. The v0.12 Timeline / Lag / Insights endpoints are **not** in the core loops; each tab registers its own gated `setInterval` (in `Js.Timeline.cs` / `Js.Lag.cs` / `Js.Insights.cs`) that only fetches while its tab is the active one. Switching away stops the expensive auxiliary fetches.

`pollNow` fetches the core triplet in `Promise.all`, caches into `lastNow` / `lastFrames` / `lastSegments`, then renders top bar, footer, overlays, and (if Summary or Timeline is active) the full pane. `foldModSparkHistory` only advances the per-mod spark history when `lastNow.tickIndex` actually changed, so the spark line tracks game progress, not browser poll count (the same guard powers the "game paused" detection).

### Per-tab JS renderers

| Tab | Master renderer | Reads | Notable sub-renderers |
|---|---|---|---|
| Summary | `renderSummary` (`Js.Render.cs`) | `lastNow`, `lastFrames`, `lastMods`, `lastHooks`, `lastSegments`, `lastEvents`, `lastHeatmap` | `renderKpiStrip`, `renderFrameChart` (SVG, ms/fps toggle), `renderDonut`, `renderTrendSparklines`, `renderHeatmap`, `renderNowPlaying`, `renderNowEvents`, `renderSummaryMods` (cascading mod→category→hook tree) |
| Timeline | `renderTimeline` (`Js.Timeline.cs`) | the 7 Timeline `lastXxx` caches | heatstrip seismograph, transition track, segment swimlanes (Gantt), segment detail + mod-attribution waterfall, attendance treemap, death strips, chronicle film-strip |
| Lag | `renderLag` (`Js.Lag.cs`) | the 5 Lag caches + `lastSpikes` / `lastStalls` | lag KPI strip, cause×context hex heatmap, fingerprint galaxy scatter, per-segment density, GC pressure, allocation→GC causality, rhythm histogram |
| Insights | `renderInsights` (`Js.Insights.cs`) | the 5 Insights caches | observatory KPI rings, dormant "dust shelf", observatory card list + detail, cross-cutting constellation, engagement-vs-cost scatter, mod-interaction matrix |
| Self | `renderSelf` (`Js.Self.cs`) | `lastSelf`, `lastHooks` | self-health gauge (vs ~36 KB/hook baseline), footprint stats, hook distribution table |

Shared helpers in `Js.Helpers.cs`: `fmtMs`, `fmtInt`, `fmtBytes`, `fmtDuration`, `fmtAgo`, `truncate`, `escapeHtml`, `modColor` (deterministic per-mod colour). `Js.Topbar.cs` renders the persistent tick/frame/avg/gc/backend bar and footer plus the `renderAll` active-tab dispatcher. `Js.Tooltips.cs` drives the `data-explain` tooltip system seen in the HTML. `Js.ModCard.cs` owns the slide-in per-mod detail card (`openModCard` / `closeModCard`, Esc-dismissed).

## Key Interfaces / Data Flow

```
 browser tab                  mod process (game thread owns the data)
 ───────────                  ───────────────────────────────────────

 fetch('/api/X')
   │  (loopback TCP 127.0.0.1:27277)
   ▼
 DashboardHttpServer.AcceptLoop
   │  accepts on the PerfProfiler/HTTP thread
   ▼
 new Thread → HandleClient            ← one background thread per request
   │  TryReadRequest (Socket.Poll, GET-only, no body)
   ▼
 DashboardRouter.Route(req)
   │  strict switch on req.Path
   ▼
 BuildX()                             ← runs on the HTTP WORKER THREAD
   │
   ▼
 DataRegistry.Shared
     .Lookup<TSnapshot>(name)
     .CurrentSnapshot()              ← pull a fresh immutable snapshot,
   │                                   race-free vs the game thread
   ▼
 System.Text.Json.Serialize(...)      ← flat anonymous object → JSON string
   │
   ▼
 HttpResponse.Json(...)
   │  WriteResponse: status + headers + body bytes, Connection: close
   ▼
 browser caches into lastX, renders the active pane
```

The load-bearing property is the snapshot pull. Every `Build*` runs on the HTTP worker thread, concurrent with the game thread that mutates `MetricCollector`'s ring buffers and lists. The pipeline's `CurrentSnapshot()` returns a fresh immutable value, so the worker thread never races the game thread. This was the v0.10 `BuildNow` fix: pre-v0.10 `BuildNow` reached directly into `ProfilerSystem.Collector.History` / `.Spikes` / `.Stalls` from the worker thread, a real (small-window) data race against the game thread's mutations. All endpoints now go through snapshots. Static asset routes (`/`, `/dashboard.css`, `/dashboard.js`) return the cached bundles directly and never touch the pipeline.

Lifecycle plug-in:

- `PerformanceProfiler.Load` constructs `Dashboard = new DashboardHttpServer(route: DashboardRouter.Route, log: ...)`. On bind failure the catch sets `Dashboard = null` and the mod keeps running without the dashboard.
- `ProfilerOverlaySystem.PostSetupContent` registers the `"OpenDashboard"` keybind on **F9**.
- `ProfilerPlayer.OnEnterWorld` prints the one-line chat hint with the live URL (or a failure line if `Dashboard == null`).
- `ProfilerPlayer.ProcessTriggers` watches the F9 bind and calls `OpenDashboardInBrowser`, which dispatches on `RuntimeInformation`: `open` (macOS), `xdg-open` (Linux), shell `UseShellExecute=true` (Windows). Any failure prints the URL in chat for manual copy.
- `PerformanceProfiler.Unload` disposes `Dashboard` **before** the database, so a late in-flight request can't call into a half-disposed DB. `Dispose` sets the stop flag, stops the listener, and joins the accept thread (500 ms budget).

## Implemented Outputs / Artifacts

| Surface | Source |
|---|---|
| SPA HTML at `/` | `DashboardAssets.IndexHtml` |
| Stylesheet at `/dashboard.css` | `CachedCssBytes` from `DashboardAssets.Css` |
| Script bundle at `/dashboard.js` | `CachedJsBytes` from `DashboardAssets.Js` |
| 28 `/api/*` JSON state endpoints | the `Build*` methods across the `DashboardRouter.*` partials |
| One-line chat discovery hint | `ProfilerPlayer.OnEnterWorld` (player surface) |
| Server lifecycle / bind-port / launch logs in `client.log` | `Mod.Logger` calls in `Load` / `Unload` / `OpenDashboardInBrowser` (agent surface) |

This satisfies dual-surface observability: the browser is the player surface; `client.log` (bind URL, chosen port, browser-launch confirmation, route exceptions) is the agent surface.

## Known Issues / Active Risks

- **Terraria pauses when its window loses focus.** In single-player, the moment the browser becomes the focused window the game stops ticking and the dashboard freezes (no new data). Three documented workarounds, surfaced in the no-world overlay copy and the README: (1) keep Terraria focused and glance at a side-by-side browser, (2) put the dashboard on a second monitor, (3) open the world via Multiplayer → Host & Play (multiplayer servers never pause; still solo, same save). The SPA distinguishes this from a true disconnect: `updateConnection` shows "game paused (window unfocused)" when polls still succeed but `tickIndex` has been stuck > 1500 ms.
- **Port-exhaustion degradation.** If all of `27277..27287` are busy, `BindFirstFreePort` throws, `Load` sets `Dashboard = null`, and `client.log` carries the warning. F9 then prints "Dashboard server not running — see client.log" and the mod runs without a dashboard. No crash, no retry; the player resolves the port conflict manually.
- **Stale F10 references in code comments.** Three comments in `PerformanceProfiler.cs` (the `Dashboard` property doc and two `Load` log-comment lines) still say "F10". The actual keybind, the chat hint, and `ProfilerOverlaySystem` all use **F9**. The comments are pre-v0.9 drift (the overlay era had F9 = toggle overlay, F10 = open browser); the binding is F9. Harmless but worth correcting on the next pass through that file.
- **Thread-per-request, no concurrency cap.** Each request spins a fresh background thread. Fine at the dashboard's read rate (a handful of polls/sec from one client); a misbehaving client opening many sockets could spawn many threads. Acceptable given loopback-only + single-consumer scope; the doc-comment flags an async pump as the revisit if the scale ever changes.
- **`/api/hooks` payload size.** One row per installed hook (~10k entries on a kitchen-sink modlist). Mitigated two ways: `BuildHooks` skips hooks with zero current-and-average cost, and `pollHooks` only fires when Summary is active and a mod row is expanded.

## Partial / In Progress

- **README vs SPA tab naming.** The README narrates six tabs (Now, Mods, Timeline, Spikes, Insights, Self); the SPA ships five (Summary merging Now+Mods, Timeline, Lag, Insights, Self). The endpoints for all six conceptual areas exist and are wired; only the tab grouping differs. Reconciling the README's tab section to the shipped five-tab layout is an outstanding doc-sync item.
- **`/api/insights` (legacy) alongside the five Insights endpoints.** `BuildInsights` (the original `InsightsStat` feed) is still routed and still polled by `pollDetail`, kept for back-compat while the v0.12 Insights tab reads the five `mod-observatory` / `dormant` / `cross-cutting` / `engagement-cost` / `mod-interaction` endpoints. Two insight surfaces coexist.

## Planned / Missing / Likely Changes

- **Dashboard genericisation (shadcn-style component library).** A directional plan to rebuild the per-pane bespoke CSS/JS into a small set of reusable components (`Panel`, `ScrollRegion`, `DataTable`, `SplitBar`, `RowList`, `Drawer`, …) over a shadcn-rooted OKLCH token system, so spacing / scroll / selection / hover live in one place per primitive instead of drifting per pane (the recurring source of leak / scroll / centring bugs). `Css.Coherence.cs` (canonical empty-state + selection accent) is the embryo. See `context/plans/ui-component-library.md`. Not started.
- **Heatmap spans only the in-memory window.** `BuildHeatmap` buckets from the same ~30 s `FrameTime.History` the chart reads, so the session heatmap only covers the rolling window. The method comment flags pulling from `tickAggregatesWarm` in LiteDB to cover the whole session (a persistence-backed read; see `systems/persistence.md`).
- **Post-session HTML report.** README roadmap (v1.1+): a single self-contained shareable file. Would reuse the asset-bundling pattern and the snapshot reads, written to disk rather than served live.
- **Binary asset serving.** `HttpResponse`'s raw-bytes constructor is kept public for future PNG / WOFF assets the dashboard might serve from disk; nothing uses it yet (fonts come from Google Fonts over the network, not the mod).
- **Query-string handling.** `TryReadRequest` strips the query string before routing; no endpoint reads parameters today. Per-mod or per-segment filtering currently happens client-side.

## Durable Notes / Discarded Approaches

- **`HttpListener` was rejected for the Windows urlacl admin requirement.** The whole reason the server is hand-rolled on `TcpListener`. Documented at length in the `DashboardHttpServer` class doc-comment and the README "How it works" section. This is the load-bearing architecture decision of the subsystem; do not "simplify" it back to `HttpListener`.
- **Throwing read-timeout was tried and abandoned.** A `NetworkStream.ReadTimeout` raises `IOException` on idle browser preconnects, which tModLoader's first-chance hook logs as "Silently Caught Exception" noise in `client.log` on every speculative socket. Replaced with non-throwing `Socket.Poll`. The polling loop's odd shape (100 ms intervals, 3 s budget) exists for this reason.
- **The in-game overlay was the player surface through v0.8, then archived.** Five overlay iterations all hit Terraria's sprite-font/UI limits for charts and dense tables. v0.9.0 sent the data to a browser instead and archived the overlay code under `UI/` for reference (it is no longer in the player path; the chat hint replaced the F9 overlay toggle). See `systems/overlay.md` for the archived sibling's full design. The dashboard supersedes it.
- **`TinyHttpServer` → `DashboardHttpServer` rename (2026-05-21).** The "tiny" name implied a throwaway test artifact; it is the only server the mod ships. Name now reflects role, not LOC.
- **Inline math in routers was systematically killed.** `BuildHeatmap` once did the minute-bucketing + boss-overlay join inline; extracted to `HeatmapAggregator` as the canonical "kill the inline math" step. `BuildNow` once read collector internals directly; migrated to snapshots in v0.10. The standing rule (from `systems/data-pipeline.md`): routers format, `Data/` computes.

## Obsolete / No Longer Relevant

- **`ProfilerConfig` is now empty.** All overlay-era config knobs (hook backend toggle, allocation tracking, log verbosity, spike threshold) were removed in v0.9.0 when the overlay was archived. The class is preserved only as a hook for hypothetical future browser-side preferences in the standard Mod Config menu; it has no fields today.
- **F10 as the browser-launch keybind.** Pre-v0.9 the mod had two binds (F9 = toggle overlay, F10 = open browser). With the overlay archived, F9 alone opens the browser. The lingering "F10" comments in `PerformanceProfiler.cs` describe the retired two-bind scheme, not current behaviour.

## Cross-references

- `systems/data-pipeline.md` — the source of every number the dashboard displays. Every `Build*` reads `DataRegistry.Shared.Lookup<TSnapshot>(name).CurrentSnapshot()`; the stream names, stages, and the "if it produces a number it lives in `Data/`" policy live there.
- `systems/overlay.md` — the archived predecessor player surface (the in-game F9 overlay). The dashboard supersedes it; the overlay code remains under `UI/` for reference only.
- `systems/persistence.md` — the LiteDB-backed lifetime data the dashboard surfaces (and the future full-session heatmap source). The persistence layer is the cross-session agent surface; the dashboard is the live read-side.
- `tmodloader/ui-system.md` — `KeybindLoader.RegisterKeybind` for the F9 bind (`ProfilerOverlaySystem`), and the tModLoader lifecycle the server's `Load` / `Unload` hang off.
- `systems/insights-engine.md` — the engine behind `/api/insights` (legacy) and the source feeding several v0.12 Insights endpoints.
