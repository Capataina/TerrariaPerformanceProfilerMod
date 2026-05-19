# Performance Profiler

**btop, but for your modlist, with a session retrospective.**

A public [tModLoader](https://github.com/tModLoader/tModLoader) mod that **runs always-on during play**, **attributes every per-tick cost to the mod (and the subsystem within that mod) that produced it**, and **surfaces session-long insights** like *"the biggest lag spike of the session was during the Eye of Cthulhu fight in the Glowing Mushroom biome."*

> **Status:** early development — Milestone 0 (hello-world scaffold). Nothing below the *Project status* heading is implemented yet; this README is the directional document for what the mod is being built into.

---

## Table of contents

- [The problem](#the-problem)
- [What makes this different](#what-makes-this-different)
- [Architecture decision: mod-only](#architecture-decision-mod-only)
- [Design principles — the honesty contract](#design-principles--the-honesty-contract)
- [Features](#features)
  - [The overlay and its six views](#the-overlay-and-its-six-views)
  - [Always-on overhead model](#always-on-overhead-model)
  - [What it captures — the data model](#what-it-captures--the-data-model)
  - [Expanded scope](#expanded-scope)
- [System architecture](#system-architecture)
- [Public-mod quality bar](#public-mod-quality-bar)
- [Tech stack](#tech-stack)
- [Project status and milestones](#project-status-and-milestones)
- [Developing the mod](#developing-the-mod)
- [Repository layout](#repository-layout)
- [License](#license)

---

## The problem

The modded Terraria community is flying blind. When a 90-mod Calamity + Fargo's Souls stack drops to 20 FPS, the only debugging primitive players have is *"disable a mod at a time and squint at the FPS counter."*

There is no measurement layer. The best-in-class optimisation mod authors are too busy fixing performance to ship instrumentation; recent community attempts at a profiler have been flagged as AI-generated wrapper code. The gap is not "another optimisation mod" — it is that **players have no way to know which mod is costing them frames.**

Performance Profiler closes that loop without touching runtime behaviour. It is read-only instrumentation: zero save-corruption risk, zero compatibility war with content mods, and it compounds with every optimisation mod that will ever ship after it. The artefact is the full feedback loop:

```
real-time visualisation while playing
        │
        ▼
drill-down debugging when something feels wrong
        │
        ▼
end-of-session retrospective — where the last 2 hours actually went
```

The player makes their own pruning decisions, with evidence.

---

## What makes this different

This is **not** dotTrace / PerfView for tModLoader developers. Those target authors of mods. Performance Profiler is the opposite: **a player-facing telemetry mod** you subscribe to on the Workshop, install once, and forget. Three things distinguish it from a developer-facing profiler:

| Distinction | Developer profiler | Performance Profiler |
|---|---|---|
| **Always-on, not a debug toggle** | Fired up for a 5-minute capture | Runs world-load → world-exit; every tick captured, every fight and biome attributed |
| **btop-style hierarchy, not flame graphs** | Stack traces — a barrier for non-engineer players | Foldable `Mod → Subsystem → Hook → Method` tree with colour-graded cost bars; readable without C# knowledge |
| **Natural-language retrospective, not just charts** | *"Frame №45,231 took 87 ms"* | *"Your biggest spike was the Cryogen fight, mostly Calamity's afterimage rendering — config path: Calamity ▸ Visual ▸ Afterimages"* |

---

## Architecture decision: mod-only

Three architectural shapes were evaluated during the 2026-05-19 feasibility research:

| Path | Verdict |
|---|---|
| **Pure standalone** (external app reading tModLoader state) | **Blocked.** `ICorProfilerCallback` attach-mode forbids method-enter/leave hooks; no external visibility into per-mod hook dispatch; no way to render an in-game overlay. |
| **Hybrid** (shim mod + external companion app over the .NET diagnostic port) | **Feasible but dropped.** Adds an Apple Developer Program subscription + per-release notarisation, doubles the distribution surface, and forces the player to install two artefacts. The added value did not justify the friction. |
| **Mod-only** | **Committed.** A single `.tmod` artefact, Workshop-distributed, one-click install, all features inside the game. |

Performance Profiler is a tModLoader mod. Full stop. No companion app, no out-of-process components, no IPC surface. Everything runs inside the tModLoader process and ships as one `.tmod` archive via the Steam Workshop.

---

## Design principles — the honesty contract

The profiler runs in front of *players*, on machines we do not own, alongside modlists we did not curate. That posture demands one discipline: **the profiler describes, it never editorialises.**

1. **Descriptive, not normative.** No mod is "core". No mod is "removable: yes/no". Whether Calamity is essential depends entirely on the player's playthrough. The profiler reports cost and engagement; the player decides what is worth their slot.
2. **Evidence-tagged insights.** Every insight surface displays the measurements that produced it. *"Synergy scan ran 3,847× across 20 slots, 1 active synergy"* is honest. *"This mod is conditional based on your playstyle"* is editorial and does not ship.
3. **Engagement-weighted, not just cost-weighted.** Cost without use is the real waste. The profiler instruments engagement (items used, weapons fired, NPCs killed, bosses fought, biomes entered) alongside cost. The intersection — *costs frames, no engagement* — is the **Dormant cost** surface, the single most actionable insight pattern the mod ships.
4. **Single-session vs lifetime signal, honestly distinguished.** A 0-engagement mod across one session is weak signal; across 22 hours and 12 sessions it is strong. Insight badges (`this session` / `lifetime data`) make the strength visible.
5. **Neutral phrasing everywhere.** "Clean cut", "must keep", "cannot be removed" are out. "Costs X with Y engagement", "config flag available at path Z" are in. The recommendation emerges from the data; the profiler does not editorialise about what the player *should* do.

The summary: **the profiler tells the player what is, not what they should do.**

---

## Features

### The overlay and its six views

The deployment frame is an **F9 in-game overlay** layered over live gameplay — not a standalone tool opened before or after play. `Esc` always dismisses, even mid-fight; no modal traps. A persistent **mode pill** near the header (`MODE: STANDARD ▾ · 2.3% overhead`) gives the player runtime control over the profiler's own cost.

| # | View | What it answers |
|---|---|---|
| 1 | **Overview** | Glanceable summary — current FPS, tick time, GC/s, top-5 mods by current-tick cost. Designed to be read in 200 ms mid-fight. |
| 2 | **Tree** | The btop-style drill-down. Every mod is a row; expands through `Subsystem → Hook category → Method → per-call breakdown`. Real-time cost bars, green→red graded. |
| 3 | **Hot Path** | Per-encounter live attribution. *"Every time you take damage, this mod's effect-check fires across all 250 stacked items, costing 3.2 ms."* |
| 4 | **Encounter** | Per-fight retrospective. After a boss dies: avg FPS, worst frame, top-5 mods for that fight, a frame-time heatmap, comparison to previous attempts. |
| 5 | **Boss-fight ledger** | Session-wide list of every named fight, sorted by worst FPS. Answers *"how did the fights actually go."* |
| 6 | **Session retrospective** | Post-session card, Steam-screenshot-shareable. The headline feature for casual players — and the organic Workshop-marketing surface. |

The HTML mockup at [`design/Mockups.html`](design/Mockups.html) is the visual design target.

### Always-on overhead model

A profiler that costs 15% of frame budget defeats its own purpose. The instrumentation must be cheaper than the FPS gain it enables.

| Mode | Per-frame overhead | Behaviour |
|---|---|---|
| **Lite** (default) | < 1% | Always-on. Per-mod CPU aggregate only. 5 Hz UI refresh. |
| **Standard** | 2–4% | Opt-in. Adds per-hook breakdown + 60 Hz UI refresh. |
| **Deep** | 5–10% | Opt-in. Adds allocation tracking + call-graph capture. For diagnostic sessions. |
| **Off** | 0% | Mod loaded, hook patching dormant. |

Lite mode stays under budget via **foreach-level aggregation** — time each `HookList.Enumerate` foreach body once and sum per-mod-assembly afterwards (≈ 30 ns × ≈ 30 hooks × 60 fps ≈ 0.3%), plus sampling, pre-allocated per-mod aggregator structs, a lock-free ring buffer, and batched off-thread persistence. Per-call timing for sub-µs hooks is *not* viable (Stopwatch noise dominates); aggregate-per-frame is. The headline overhead number is established de novo by the Milestone 1 spike on a real 94-mod stack — it is not borrowed from prior art.

### What it captures — the data model

**Per-tick** (in-memory ring buffer, 30 s window ≈ 1,800 frames):

```
struct TickFrame { timestampUnix, frameTimeMs, gcTimeMs, projectileCount,
                   npcCount, dustCount, currentBiome, activeEncounter,
                   PerModSample[] modSamples }

struct PerModSample { modId, cpuMs, allocatedBytes, hookCalls }
```

**Per-encounter** — when the player crosses a threshold (boss spawn, biome change, event start, world-load) a new encounter window opens; on close it is finalised and appended to the session's JSON-lines file:

```
~/Library/Application Support/Terraria/tModLoader/PerformanceProfiler/
├── sessions/2026-05-19_session-47.jsonl    one JSON object per encounter
├── lifetime-rollup.json                    incrementally maintained aggregate
└── modlists/<fingerprint-hash>.json        per distinct modlist fingerprint
```

A **session** is `world-load → save-and-exit`. A crash mid-session is detected on next launch and the partial is written `incomplete: true` so it cannot poison aggregates. Each session stamps its **modlist fingerprint** (hash of the sorted mod-name+version tuple) so engagement signal never crosses modlists.

**Why JSON-lines, not SQLite:** pure managed (no native dependency, no Workshop Known-Natives-List question), append-only and crash-safe, trivially version-migrated (each row carries a `schema` field), and hand-readable. SQLite is a v3 question only if cross-session query speed becomes a real bottleneck.

**Per-mod engagement** is instrumented alongside cost — `weaponsFired`, `itemsUsed`, `npcsKilled`, `bossesFought`, `biomesEntered`, `accessoriesEquipped`, `classesUsed`, `petsEquipped` — sourced from `ModItem.UseItem`, `GlobalNPC.OnKill`, `Player.InModBiome` transitions, accessory-slot scans, and so on. Cost data tells you what is expensive; engagement data tells you whether the expense is *for you*.

### Expanded scope

The 2026-05-19 design pass widened the original "rolling view + retrospective card" framing into nine areas:

| | Area | Summary |
|---|---|---|
| **A** | Lifetime recording | Every session persisted, not just the rolling 30 s. The worst frame of your *month* is recoverable. |
| **B** | Two "slowest" leaderboards | *Most lag spikes caused* (peak severity × frequency) and *Most consistent FPS drag* (sustained cost) — usually different top-10 lists, different fixes. |
| **C** | Event-triggered attribution | Per-event leaderboards: *top mods when an enemy is hit / on boss spawn / on biome transition / on item pickup*. |
| **D** | Cross-mod entity attribution `research-gated` | Every entity tagged with its source mod. Single-entity attribution is trivial; chain attribution (*A's projectile triggered B's status via C's accessory*) is pinned for investigation. |
| **E** | Magnitude × duration prioritisation | A 3 s spike vs a 1 ms constant drag weighted by real total impact, so recommendations are *worth-fixing*-aware. |
| **F** | Evidence-led observations | Mod-specific observation rules, each citing its evidence and badging data strength. Includes **F.1 Dormant cost** (rank by `cost × (1 − engagement)`) and **F.2 counterfactual "if removed" simulation**. |
| **G** | Cascading tree depth | Full `Mod → Subsystem → Method → per-call → per-entity` drill-down. |
| **H** | "Hot moments" heatmap | 80×8 frame-time heatmap; click any cell to see which mods fired, in what order, at what cost. Event markers overlay boss spawns / deaths / biome enters. |
| **I** | Per-encounter cross-fight comparison | *"Cryogen attempt 4 — avg 41 FPS vs 38 in attempt 3."* One session is data; ten sessions is a story. |

Full rationale for every area lives in the design pitch (see [*Developing the mod*](#developing-the-mod)).

---

## System architecture

A pure-C# mod, no out-of-process components, delivered as a single `.tmod`:

```
┌─ PerformanceProfiler.tmod ──────────────────────────────────────┐
│                                                                 │
│  Hook Interceptor ──▶ Per-Tick Metric Collector                 │
│  (MonoMod IL detours    (Stopwatch + GC alloc deltas;           │
│   on tML loader hooks)   emits PerModSample[] per tick)         │
│                              │                                  │
│  Context Tagger ─────────────┼──▶ Ring Buffer (last 30 s)       │
│  (biome / boss / event)      │         │                        │
│                              ▼         ▼                        │
│  Encounter Detector ──▶ Persistent Store    UI Renderer         │
│  (meaningful windows     (JSON-lines under   (tML UIElement:    │
│   vs noise)              Main.SavePath)       6 views)          │
│                              │                                  │
│                              ▼                                  │
│  Insights Engine (post-session natural-language summaries)      │
└─────────────────────────────────────────────────────────────────┘
```

| # | Component | Responsibility |
|---|---|---|
| 1 | **Hook Interceptor** | MonoMod IL detours via tModLoader's `MonoModHooks` API. ILHook the `*Loader.<HookName>` method bodies for Lite mode; per-`(GlobalType, hookMethod)` detours for Standard/Deep. Per-mod identity from `MethodBase.DeclaringType.Assembly`. **Aborts clean** if loader signatures change across a tML update — it disables itself rather than corrupting the run. |
| 2 | **Per-Tick Metric Collector** | Pre-allocated `PerModSample[]` indexed by mod ID. Zero per-call allocations in the hot path. |
| 3 | **Ring Buffer** | Fixed-size circular buffer of `TickFrame` structs, allocated once at world-load, never grown. Lock-free write (main thread) / read (UI worker). |
| 4 | **Context Tagger** | Tracks biome / boss / event / world position; maintains the encounter tag every tick inherits. |
| 5 | **Encounter Detector** | Distinguishes meaningful encounters (dwell > 30 s, boss-spawn, named event) from noise. |
| 6 | **UI Renderer** | tModLoader's native `UIElement` / `UIPanel` system — gets Steam-controller input, font scaling, and z-ordering for free. |
| 7 | **Persistent Store** | JSON-lines append-only via `System.Text.Json`. Batched per encounter-close; final flush on save-and-exit; crash-safe partial-session recovery. |
| 8 | **Insights Engine** | Post-session heuristic rules (boss / biome attribution, hook-rate anomaly, cross-session comparison) producing the natural-language retrospective. |

---

## Public-mod quality bar

This is a Workshop install button — everyone sees it. The polish floor:

- **First-launch UX** — a "Welcome to Profiler" tutorial overlay (what it does, the hotkey, *"we don't touch your save"*, *"toggle off anytime"*).
- **Visual coherence** — a real palette + typography + spacing system, not the stock-tML-UI look.
- **Performance honesty** — the mod profiles itself; the overlay can show the profiler's own overhead so the claim is verifiable, not just trusted.
- **License + transparency** — MIT, public GitHub, no telemetry, everything stays local.
- **Author response** — within 48 h on Workshop comments + GitHub issues for the first 90 days.

v1 is single-player only; multiplayer is a v2 feature, not a v1 blocker. English-only, with the localisation system open for community PRs.

---

## Tech stack

| Layer | Choice |
|---|---|
| Language / runtime | C# on **.NET 8** (tModLoader 1.4.4 is pinned to .NET 8 — not 9 or 10) |
| Modding API | tModLoader, Steam App `1281930` |
| Instrumentation | MonoMod IL detours via tModLoader's `MonoModHooks` |
| UI | tModLoader native `UIElement` / `UIPanel` / `UIState` with custom `DrawSelf` |
| Persistence | JSON-lines via `System.Text.Json` — no native dependencies |
| Build | `dotnet msbuild` or in-game **Build + Reload** |
| Distribution | single `.tmod` via the Steam Workshop |
| License | MIT |

---

## Project status and milestones

**Current: Milestone 0 — hello-world scaffold.** The mod compiles, packs, loads, and prints to chat on world-load. The instrumentation work has not started.

| Milestone | Scope |
|---|---|
| **0 — Feasibility spikes** | Three throwaway-mod spikes resolving the open unknowns: (0.A) detour install cost at 94-mod scale, (0.B) JSON-lines write performance + crash safety, (0.C) engagement-hook coverage. < 1 week total. |
| **1 — Lite-mode MVP** | ILHook per-loader-method timing, per-mod CPU aggregate, a single overlay panel (top-10 by cost + 30 s rolling), F9 toggle. Gate: **< 1% overhead measured on a real modlist session.** |
| **2 — Tree + Standard mode** | Hierarchical foldable tree UI, Hot Path capture, per-mod icons, colour-gradient bars, per-`(GlobalType, hookMethod)` detours. |
| **3 — Persistence + retrospective** | JSON-lines storage, encounter detection, per-encounter + end-of-session retrospective cards, cross-session engagement insights. |
| **4 — Insights engine** | Heuristic attribution rules, a curated config-knowledge dataset, natural-language insight generation. |
| **5 — Public Workshop release** | Description, GIF demo, screenshots, first-launch tutorial, MIT GitHub repo. |
| **6+** | Cross-session analytics, Deep mode, allocation tracking, multiplayer, theming. |

Milestone 1 is the project-promotion gate: a working Lite-mode MVP at < 2% overhead on the 94-mod stack with real cost attribution visible.

---

## Developing the mod

The full design pitch — ~1,100 lines, every feature and rationale, the from-zero macOS walkthrough, the feasibility-research record — lives in the LifeOS vault at:

```
Projects/Potential Projects/Modded Terraria Profiler.md
```

Read it before re-discussing scope or architecture; most "what about X?" questions are already answered there.

**Prerequisites:** Terraria + tModLoader (Steam App `1281930`), the **.NET 8 SDK** (`brew install --cask dotnet-sdk@8` — *not* 9 or 10), and Git.

**Iteration loop:**

```
edit .cs  ──▶  dotnet msbuild   ──or──   in-game Build + Reload
                     │
                     ▼
   tModLoader → Mods → Reload Mods  ──▶  re-enter world (re-fires OnWorldLoad)
```

Build failures and runtime logs land in `~/Library/Application Support/Steam/steamapps/common/tModLoader/tModLoader-Logs/client.log` (in the Steam install dir, not the save dir).

> **Apple Silicon note:** launch tModLoader in **windowed** mode only — a fullscreen transition triggers a silent crash on Apple Silicon ([tModLoader #4941](https://github.com/tModLoader/tModLoader/issues/4941)).

---

## Repository layout

```
PerformanceProfiler/
├── PerformanceProfiler.cs       Mod entry point + Milestone 0 smoke test
├── PerformanceProfiler.csproj   SDK-style project; imports ..\tModLoader.targets
├── build.txt                    tModLoader build metadata (name, author, version)
├── description.txt              Workshop / mod-browser description
├── README.md                    this file — directional document
├── CLAUDE.md                    engineering collaborator brief
├── .gitignore
└── design/
    └── Mockups.html             the visual design target (the six views)
```

This repository lives inside tModLoader's `ModSources/` directory — tModLoader only discovers mod source folders there. That path does not sync via iCloud, so it is safe for a git repo.

---

## License

MIT. Public GitHub repository, no telemetry, everything stays local.
