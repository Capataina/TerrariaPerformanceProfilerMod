# Performance Profiler

### btop, but for your modlist — with a session retrospective at the end.

![status](https://img.shields.io/badge/status-early%20development-f5b342?style=flat-square)
![milestone](https://img.shields.io/badge/milestone-0%20·%20scaffold-6e7480?style=flat-square)
![C#](https://img.shields.io/badge/C%23-.NET%208-512BD4?style=flat-square&logo=dotnet&logoColor=white)
![tModLoader](https://img.shields.io/badge/tModLoader-1.4.4-1b7340?style=flat-square)
![read-only](https://img.shields.io/badge/instrumentation-read--only-95d4a3?style=flat-square)
![license](https://img.shields.io/badge/license-MIT-79c0ff?style=flat-square)

A public [tModLoader](https://github.com/tModLoader/tModLoader) mod that **runs always-on while you play**, **attributes every per-tick cost to the mod — and the subsystem inside that mod — that produced it**, and **surfaces session-long insights** like:

> *"Your worst lag spike of the session was Cryogen Phase 2 in the Sulphurous Sea — 87 ms, and 67 % of that frame was Calamity's `SnowstormCallback()`."*

You subscribe to it on the Steam Workshop, install it once, and forget it. It never touches gameplay, never edits your save, and never picks a fight with another mod. It just **measures** — and then, at `save-and-exit`, it hands you a screenshot-shareable card showing exactly where the last two hours of frame time went.

**It is equal parts profiler and tracker.** The *profiler* half attributes CPU cost — per mod, per subsystem, down to the individual hook. The *tracker* half records, in real time, where your session actually went: how long you spent in each biome, every boss fight and how it played out, what you fired, used, killed and explored — and what you never touched. The profiler half tells you what is *expensive*; the tracker half tells you what is *yours*. Every number comes from the live runtime — nothing is estimated, nothing is hard-coded. The end-of-session retrospective is where the two halves meet.

> [!IMPORTANT]
> **Status: early development — Milestone 0 (hello-world scaffold).** The mod currently compiles, packs into a `.tmod`, loads, prints to chat, and logs to `client.log`. **Nothing below the [Project status](#project-status--milestones) heading is implemented yet.** This README is the *directional document* — the contract for what Performance Profiler is being built into, not a description of what it does today.

---

## What it looks like

The deployment surface is an in-game **F9 overlay** drawn over live gameplay — not a tool you open before or after play. Here is the `Overview` tab as laid out in the [design mockup](design/Mockups.html), on a representative 94-mod stack — the values shown are illustrative, fixed for the mockup only; the live mod reads every one of them from the actual runtime:

```
 F9 ┌─ PERFORMANCE PROFILER ──────────────────────────────  MODE: STANDARD ▾  2.3 % overhead ─┐
    │  FPS 47     TICK 23.4 ms     GC 2.1 ms/s     UPTIME 02:17:43     490,820 samples         │
    ├──────────────────────────────────────────────────────────────────────────────────────┤
    │  TOP MODS · 94 loaded                                          sort ‹ consistent drag › │
    │                                                                                        │
    │  ▾ Calamity Mod                ████████████████████████████████░░░░░░░░░░░░░░   32.1 %  │
    │     ▾ Boss AI · Cryogen        █████████████░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░    3.1 ms │
    │         SnowstormCallback()    █████████░░░░░░░░░░░░░░░░░░░░░░░    1.9 ms · 60×/s        │
    │     ▾ Particle System          █████████░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░    2.1 ms │
    │         DustSpawn (afterimage) ██████░░░░░░░░░░░░░░░░░░░░░░░░░░    1.2 ms · 4,200×/s     │
    │  ▾ Fargo's Souls Mod           ████████████████░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░   16.2 %  │
    │  ▸ Spirit Reforged             █████████░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░    8.7 %  │
    │  ▸ Runeterran Accessories      ████████░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░    8.1 %  ◆ dormant
    └──────────────────────────────────────────────────────────────────────────────────────┘
       cost bars are green → red graded · fold any row · double-click to drill into a hook
```

Every row is the kind of measurement the live mod surfaces. Fold `Calamity Mod` open and you walk down `Mod → Subsystem → Hook → per-call breakdown` — no stack traces, no C# knowledge required. The `◆ dormant` flag on `Runeterran Accessories` means it cost frames this session with **zero** observed interaction. More on that [below](#the-dormant-cost-surface).

---

## Table of contents

- [The problem](#the-problem)
- [A session, end to end](#a-session-end-to-end)
- [What makes this different](#what-makes-this-different)
- [The overlay, tab by tab](#the-overlay-tab-by-tab)
- [The honesty contract](#the-honesty-contract)
- [The Dormant cost surface](#the-dormant-cost-surface)
- [Always-on overhead model](#always-on-overhead-model)
- [What it captures — the data model](#what-it-captures--the-data-model)
- [System architecture](#system-architecture)
- [Architecture decision: mod-only](#architecture-decision-mod-only)
- [Tech stack](#tech-stack)
- [Project status & milestones](#project-status--milestones)
- [Public-mod quality bar](#public-mod-quality-bar)
- [Building & developing the mod](#building--developing-the-mod)
- [Repository layout](#repository-layout)
- [Contributing](#contributing)
- [License](#license)

---

## The problem

**The modded Terraria community is flying blind.** When a 90-mod Calamity + Fargo's Souls stack drops to 20 FPS, the only debugging primitive a player has is *"disable a mod at a time and squint at the FPS counter."* That is not a workflow. It is a guessing game with a 30-second reload between every guess.

There is **no measurement layer**. The best-in-class optimisation-mod authors are too busy fixing performance to ship instrumentation; recent community attempts at a profiler have been flagged as AI-generated wrapper code. The gap is not *"another optimisation mod"* — it is that **players have no way to know which mod is costing them frames.**

Performance Profiler closes that loop without touching runtime behaviour:

```
                ┌──────────────────────────────────────────────┐
   always-on    │   real-time visualisation while you play      │   the F9 overlay
   instrument   └───────────────────────┬──────────────────────┘
                                        ▼
                ┌──────────────────────────────────────────────┐
   read-only    │   drill-down debugging when something feels   │   Mod ▸ Subsystem ▸ Hook
   measurement  │   wrong — fold down to the exact hook         │
                └───────────────────────┬──────────────────────┘
                                        ▼
                ┌──────────────────────────────────────────────┐
   evidence     │   end-of-session retrospective — where the    │   the shareable card
   not opinion  │   last 2 hours of frame time actually went    │
                └──────────────────────────────────────────────┘
```

Because it is **read-only instrumentation**, it carries zero save-corruption risk, zero compatibility war with content mods, and it *compounds* with every optimisation mod that will ever ship after it. The player makes their own pruning decisions — with evidence, not vibes.

---

## A session, end to end

Below is the design-target output for a single ~2-hour session, laid out as in the [design mockup](design/Mockups.html). **Every value here is illustrative** — the mockup fixes the visual design, not the numbers. The live mod extracts all of it from the actual runtime: frame times, per-hook costs, biome dwell, boss-fight windows, engagement counts. Your card tells your story, not this one.

```
 ╔═ SESSION RETROSPECTIVE ════════════════════════════════════════════════════════╗
 ║  Session #47 · 2026-05-19 · 2 h 17 min of modded Terraria                       ║
 ║  94 mods · Master + Calamity Death + Fargo's Eternity · 490,820 frame samples   ║
 ╠═════════════════════════════════════════════════════════════════════════════════╣
 ║                                                                                 ║
 ║   51 FPS average        ↑ +3 vs your previous session                           ║
 ║   Best stretch   Forest exploration, 56 FPS                                     ║
 ║   Hardest        Cryogen Phase 2 — 25 FPS sustained for 2:14                     ║
 ║                                                                                 ║
 ║   COST PODIUM                                                                   ║
 ║   #1  Calamity Mod        by Ozzatron       7.8 ms/frame   ███████████   32.1 %  ║
 ║   #2  Fargo's Souls Mod   by Fargowilta     3.9 ms/frame   █████         16.2 %  ║
 ║   #3  Spirit Reforged     by GabeHasWon     2.1 ms/frame   ███            8.7 %  ║
 ║                                                                                 ║
 ║   ◆ MEMORABLE FIGHT — Cryogen Phase 2 · 01:47:18 · result: WON                  ║
 ║     87 ms worst frame · 25 FPS held for 2:14                                     ║
 ║     Calamity ▸ Cryogen ▸ SnowstormCallback() accounted for 67 % of frame cost   ║
 ║                                                                                 ║
 ║   ◆ DORMANT COST — 13.7 % of frame time went to 3 mods you never interacted with ║
 ║     Runeterran Accessories   8.1 %   0 of 300+ accessories equipped              ║
 ║     Projectile Auto-Homing   1.7 %   84,222 enemy projectiles scanned, 0 yours   ║
 ║     Risk of Slime Rain       1.5 %   0 of 35 items picked up all session         ║
 ║                                                                                 ║
 ║   WHAT YOU USED         14 weapons · 23 items · 487 kills · 5 bosses · 6 biomes  ║
 ╚═════════════════════════════════════════════════════════════════════════════════╝
```

That card is the **headline feature for casual players** — and, not coincidentally, the organic Workshop-marketing surface. It is built to be dropped straight into a Steam screenshot.

Notice what it does *not* say. It never tells you to remove a mod. It reports `Runeterran Accessories cost 8.1 % with 0 observed interaction` and lets you decide whether your build will ever touch the AP/AD axis. That restraint is deliberate — see [the honesty contract](#the-honesty-contract).

---

## What makes this different

This is **not** dotTrace or PerfView for tModLoader *developers*. Those target authors of mods. Performance Profiler is the opposite — **a player-facing telemetry mod** for people who play modlists, not write them.

| Distinction | A developer profiler | **Performance Profiler** |
|---|---|---|
| **When it runs** | Fired up for a 5-minute capture, then closed | Always-on, `world-load → world-exit` — every tick captured, every fight and biome attributed |
| **How it shows cost** | Flame graphs and stack traces — a wall for non-engineer players | A foldable `Mod → Subsystem → Hook → Method` tree with green→red graded cost bars, readable without C# |
| **What it hands you** | *"Frame #45,231 took 87 ms"* | *"Your biggest spike was the Cryogen fight, mostly Calamity's afterimage rendering — config path: `Calamity ▸ Visual Effects ▸ Afterimages`"* |
| **Who it is for** | Mod authors, in a dev environment | Anyone with a 90-mod Workshop stack and a framerate problem |

---

## The overlay, tab by tab

The F9 overlay is a single panel with a tab bar. `Esc` always dismisses it — even mid-fight, no modal traps. A persistent **mode pill** (`MODE: STANDARD ▾ · 2.3 % overhead`) gives you runtime control over the profiler's *own* cost.

The [HTML mockup](design/Mockups.html) is the visual design target for all nine tabs:

| Tab | What it answers |
|---|---|
| **Overview** | Glanceable summary — current FPS, tick time, GC/s, top mods by cost, top spike sources, a live frame-time heatmap. Designed to be read in 200 ms mid-fight. |
| **Full tree** | The btop-style drill-down across all 94 mods. Every mod is a row; expand through `Subsystem → Hook category → Method → per-call`. Real-time graded cost bars at every level. |
| **Hot moments** | The worst frames of the session, clustered. *"3 of the top 5 spikes were Cryogen P2 — this is fight-driven, not random."* |
| **Events** | Per-event-class attribution. Which mods fire on `Enemy Hit` (3,847×), `Boss Spawn` (8×), `Biome Enter` (14×), `Item Pickup` (2,103×), `Projectile Spawn` (84,222×), `Weapon Use` (12,041×) — and *why*. |
| **Biomes** | FPS ranking per biome (worst → best), time spent per biome, and cost attribution on every biome transition. *Glowing Mushroom: 39 FPS. Forest: 56.* |
| **Boss fights** | A ledger of every named fight, sorted by worst FPS — duration, average FPS, worst frame, top cost cause, and outcome (won / died / fled). |
| **Dormant cost** | Mods ranked by `cost × (1 − engagement)` — frames paid for with nothing to show. The single most actionable surface the mod ships. [Detailed below.](#the-dormant-cost-surface) |
| **Cross-mod chains** `research-gated` | *"Mod A's projectile triggered Mod B's status applied via Mod C's accessory"* as a single attributed chain. Pinned for feasibility research before Milestone 1 commits to it. |
| **Session retrospective** | The post-session card shown [above](#a-session-end-to-end) — Steam-screenshot-shareable. |

A worked example of the **Events ▸ Enemy Hit** tab — what fires every time you land a hit (3,847 hits in the mockup's example session):

```
  ENEMY HIT · 3,847 events                                          share   why it fires
  ───────────────────────────────────────────────────────────────────────────────────────
  Hollow Knight Charms      ██████████████░░░░░░░░░░░░░░░░░░░░░░░░░    28 %   synergy scan, 20 slots
  Calamity Mod              ██████████░░░░░░░░░░░░░░░░░░░░░░░░░░░░░    21 %   on-hit status checks
  Runeterran Accessories    ████████░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░    17 %   AP/AD hooks — 0 equipped
  Risk of Slime Rain        ███████░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░    14 %   proc checks — 0 stacks
  Fargo's Souls Mod         █████░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░    11 %   8 enchantment effects
  ARPG Enemy System         ███░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░     6 %   elemental damage roll
  (other 88 mods)           █░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░     3 %
```

Two of the top four — `Runeterran Accessories` and `Risk of Slime Rain` — are spending on-hit budget for content you never equipped or picked up. The profiler shows you the *why* column so the number is never a mystery.

---

## The honesty contract

The profiler runs in front of *players*, on machines we do not own, alongside modlists we did not curate. That posture demands one discipline: **the profiler describes, it never editorialises.**

1. **Descriptive, not normative.** No mod is "core". No mod is "removable: yes/no". Whether Calamity is essential depends entirely on the playthrough. The profiler reports cost and engagement; the player decides what is worth their slot.
2. **Evidence-tagged insights.** Every insight surface shows the measurements that produced it. *"Synergy scan ran 3,847× across 20 slots, 1 active synergy of 25 possible"* is honest. *"This mod is conditional based on your playstyle"* is editorial and does not ship.
3. **Engagement-weighted, not just cost-weighted.** Cost without use is the real waste. The profiler instruments engagement — items used, weapons fired, NPCs killed, bosses fought, biomes entered — alongside cost. The intersection (*costs frames, no engagement*) is the [Dormant cost](#the-dormant-cost-surface) surface.
4. **Single-session vs lifetime signal, honestly distinguished.** A 0-engagement mod across one session is weak signal; across 22 hours and 12 sessions it is strong. Insight badges (`this session` / `lifetime data` / `needs persistence`) make the strength visible at a glance.
5. **Neutral phrasing everywhere.** *"Clean cut"*, *"must keep"*, *"cannot be removed"* are out. *"Costs X with Y engagement"*, *"config flag available at path Z"* are in. The recommendation emerges from the data; the profiler never tells the player what they *should* do.

The summary: **the profiler tells the player what *is*, not what they *should do*.**

A correctly-shaped insight, neutral and evidence-led, looks like this:

```
 ┌ ◆ Calamity Visual Effects ─────────────────────────────────  badge: this session ┐
 │   Afterimages were active during all 5 boss fights.                              │
 │   evidence  · DustSpawn fired at 4,200×/s peak                                    │
 │             · afterimage render measured at 1.2 ms/frame in boss fights           │
 │             · config path — Calamity ▸ Visual Effects ▸ Afterimages               │
 │   A config flag exists: Afterimages = false. Visual-only — no content change.     │
 └───────────────────────────────────────────────────────────────────────────────────┘
```

It states the cost, cites the evidence, and points at the exact config knob. It does **not** say "you should turn this off."

---

## The Dormant cost surface

Every mod costs frame time whether you interact with it or not — its hook chains fire on every tick regardless. **Dormant cost** ranks mods by `cost × (1 − engagement)`: frames paid, with nothing observed in return *this session*.

```
  DORMANT COST · cost × (1 − engagement) · this session only
  ───────────────────────────────────────────────────────────────────────────────────
  Runeterran Accessories    8.1 %   ████████████████   0 of 300+ accessories equipped
                                                       0 of 5 rune paths touched
                                                       AP/AD hook fires on every hit anyway
  Projectile Auto-Homing    1.7 %   ███░░░░░░░░░░░░░░   friendly homing disabled in config
                                                       hostile-side scanned 84,222 dodged shots
  Risk of Slime Rain        1.5 %   ███░░░░░░░░░░░░░░   0 of 35 items collected in 2 h 17 min
                                                       Magma Worm never summoned
  ───────────────────────────────────────────────────────────────────────────────────
  13.7 % of total frame time · 0 mods removed automatically — the profiler reports, you decide
```

This is the most actionable pattern the mod ships, and it is exactly where the honesty contract earns its keep. A 0-engagement mod across *one* session may be a heavily-used mod across the *next* twenty — which is why every Dormant line is badged `this session` until [persistent storage](#what-it-captures--the-data-model) extends the signal from *"today"* to *"your whole playthrough"*. The profiler surfaces the pattern; it never pulls the trigger.

---

## Always-on overhead model

A profiler that costs 15 % of the frame budget defeats its own purpose. **The instrumentation must be cheaper than the FPS gain it enables.** Overhead is a hard budget, not an aspiration, and it is tunable live from the mode pill:

| Mode | Per-frame overhead | Behaviour |
|---|---|---|
| **Lite** *(default)* | **< 1 %** | Always-on. Per-mod CPU aggregate only. 5 Hz UI refresh. |
| **Standard** | **2 – 4 %** | Opt-in. Adds per-hook breakdown + event attribution + 60 Hz UI. |
| **Deep** | **5 – 10 %** | Opt-in. Adds allocation tracking + call-graph capture + cross-mod chains. For diagnostic sessions. |
| **Off** | **0 %** | Mod loaded, hook patching dormant. Overlay shows the last completed session. |

Lite mode stays under budget through **foreach-level aggregation**: time each `HookList.Enumerate` foreach body *once* and sum per-mod-assembly afterwards (≈ 30 ns × ≈ 30 hooks × 60 fps ≈ 0.3 %), plus sampling, pre-allocated per-mod aggregator structs, a lock-free ring buffer, and batched off-thread persistence. Per-call timing for sub-microsecond hooks is *not* viable — `Stopwatch` noise dominates — so aggregate-per-frame is the technique. **The headline overhead figure is established de novo by the Milestone 1 spike on a real 94-mod stack — it is not borrowed from prior art.**

---

## What it captures — the data model

**Per tick** — held in an in-memory ring buffer, a 30-second window (≈ 1,800 frames):

```csharp
struct TickFrame {
    long  timestampUnix;
    float frameTimeMs, gcTimeMs;
    int   projectileCount, npcCount, dustCount;
    Biome currentBiome;
    EncounterTag activeEncounter;
    PerModSample[] modSamples;     // pre-allocated, indexed by mod ID
}

struct PerModSample {
    int  modId;
    float cpuMs;
    long  allocatedBytes;
    int   hookCalls;
}
```

**Per encounter** — when the player crosses a threshold (boss spawn, biome change, event start, world-load) a new encounter window opens; on close it is finalised and appended to the session's JSON-lines file:

```
~/Library/Application Support/Terraria/tModLoader/PerformanceProfiler/
├── sessions/2026-05-19_session-47.jsonl     one JSON object per encounter
├── lifetime-rollup.json                     incrementally maintained aggregate
└── modlists/<fingerprint-hash>.json         one file per distinct modlist
```

A **session** is `world-load → save-and-exit`. A crash mid-session is detected on the next launch and the partial is written `incomplete: true` so it can never poison aggregates. Each session stamps its **modlist fingerprint** — a hash of the sorted `mod-name + version` tuple — so engagement signal never bleeds across different modlists.

**Why JSON-lines, not SQLite:** pure managed (no native dependency, no Workshop Known-Natives-List question), append-only and crash-safe, trivially version-migrated (every row carries a `schema` field), and hand-readable. SQLite is a v3 question, and only if cross-session query speed ever becomes a real bottleneck.

**Per-mod engagement** is instrumented alongside cost — `weaponsFired`, `itemsUsed`, `npcsKilled`, `bossesFought`, `biomesEntered`, `accessoriesEquipped`, `classesUsed`, `petsEquipped` — sourced from `ModItem.UseItem`, `GlobalNPC.OnKill`, `Player.InModBiome` transitions, accessory-slot scans, and similar. **Cost data tells you what is expensive; engagement data tells you whether the expense is *for you*.**

---

## System architecture

A pure-C# mod — no out-of-process components, no IPC surface — delivered as a single `.tmod`:

```
┌─ PerformanceProfiler.tmod ────────────────────────────────────────────┐
│                                                                       │
│   Hook Interceptor ───────▶ Per-Tick Metric Collector                 │
│   MonoMod IL detours        Stopwatch + GC alloc deltas;              │
│   on tML loader hooks       emits PerModSample[] every tick           │
│                                     │                                 │
│   Context Tagger ───────────────────┼────────▶ Ring Buffer (30 s)     │
│   biome / boss / event              │               │                 │
│                                     ▼               ▼                 │
│   Encounter Detector ──────▶ Persistent Store     UI Renderer         │
│   meaningful windows         JSON-lines append     tML UIElement —    │
│   vs noise                   batched per encounter  the 9 tabs        │
│                                     │                                 │
│                                     ▼                                 │
│   Insights Engine ── post-session natural-language retrospective      │
└───────────────────────────────────────────────────────────────────────┘
```

| # | Component | Responsibility |
|---|---|---|
| 1 | **Hook Interceptor** | MonoMod IL detours via tModLoader's `MonoModHooks` API. ILHook the `*Loader.<HookName>` method bodies for Lite mode; per-`(GlobalType, hookMethod)` detours for Standard / Deep. Per-mod identity from `MethodBase.DeclaringType.Assembly`. **Aborts clean** if loader signatures change across a tML update — it disables itself rather than corrupt the run. |
| 2 | **Per-Tick Metric Collector** | Pre-allocated `PerModSample[]` indexed by mod ID. Zero per-call allocations in the hot path. |
| 3 | **Ring Buffer** | Fixed-size circular buffer of `TickFrame` structs, allocated once at world-load, never grown. Lock-free write (main thread) / read (UI worker). |
| 4 | **Context Tagger** | Tracks biome / boss / event / world position; maintains the encounter tag every tick inherits. |
| 5 | **Encounter Detector** | Distinguishes meaningful encounters (dwell > 30 s, boss-spawn, named event) from noise. |
| 6 | **UI Renderer** | tModLoader's native `UIElement` / `UIPanel` system — Steam-controller input, font scaling, and z-ordering come for free. |
| 7 | **Persistent Store** | JSON-lines append-only via `System.Text.Json`. Batched per encounter-close; final flush on save-and-exit; crash-safe partial-session recovery. |
| 8 | **Insights Engine** | Post-session heuristic rules (boss / biome attribution, hook-rate anomaly, cross-session comparison) producing the natural-language retrospective. |

Every component is a swappable subsystem: the test is *"can you comment one out and have the rest still work?"* The profiling modes (Lite / Standard / Deep / Off) are that same principle exposed to the player — instrumentation layers that are cleanly removable, not entangled.

---

## Architecture decision: mod-only

Three architectural shapes were evaluated during the 2026-05-19 feasibility research:

| Path | Verdict |
|---|---|
| **Pure standalone** — external app reading tModLoader state | **Blocked.** `ICorProfilerCallback` attach-mode forbids method enter/leave hooks; no external visibility into per-mod hook dispatch; no way to render an in-game overlay. |
| **Hybrid** — shim mod + external companion app over the .NET diagnostic port | **Feasible but dropped.** Adds an Apple Developer Program subscription + per-release notarisation, doubles the distribution surface, and forces the player to install two artefacts. The added value did not justify the friction. |
| **Mod-only** | **Committed.** One `.tmod` artefact, Workshop-distributed, one-click install, everything inside the game. |

Performance Profiler is a tModLoader mod. Full stop. No companion app, no out-of-process components, no IPC surface — everything runs inside the tModLoader process and ships as one `.tmod` archive via the Steam Workshop.

---

## Tech stack

| Layer | Choice |
|---|---|
| Language / runtime | C# on **.NET 8** (tModLoader 1.4.4 is pinned to .NET 8 — not 9 or 10) |
| Modding API | tModLoader, Steam App `1281930` |
| Instrumentation | MonoMod IL detours via tModLoader's official `MonoModHooks` API |
| UI | tModLoader native `UIElement` / `UIPanel` / `UIState` with custom `DrawSelf` |
| Persistence | JSON-lines via `System.Text.Json` — no native dependencies |
| Build | `dotnet msbuild`, or in-game **Build + Reload** |
| Distribution | a single `.tmod` via the Steam Workshop |
| License | MIT |

---

## Project status & milestones

**Current: Milestone 0 — hello-world scaffold.** The mod compiles, packs, loads, prints to chat on world-entry, and logs lifecycle events to `client.log`. The instrumentation work has not started.

| Milestone | Scope | Gate |
|---|---|---|
| **0 — Feasibility spikes** | Three throwaway-mod spikes resolving the open unknowns: **(0.A)** detour install cost at 94-mod scale, **(0.B)** JSON-lines write performance + crash safety, **(0.C)** engagement-hook coverage. | < 1 week total |
| **1 — Lite-mode MVP** | ILHook per-loader-method timing, per-mod CPU aggregate, a single overlay panel (top-10 by cost + 30 s rolling), F9 toggle. | **< 1 % overhead measured on a real modlist** |
| **2 — Tree + Standard mode** | Hierarchical foldable tree UI, Hot Path capture, per-mod icons, colour-gradient bars, per-`(GlobalType, hookMethod)` detours. | — |
| **3 — Persistence + retrospective** | JSON-lines storage, encounter detection, per-encounter + end-of-session retrospective cards, cross-session engagement insights. | — |
| **4 — Insights engine** | Heuristic attribution rules, a curated config-knowledge dataset, natural-language insight generation. | — |
| **5 — Public Workshop release** | Description, GIF demo, screenshots, first-launch tutorial, MIT GitHub repo. | — |
| **6+** | Cross-session analytics, Deep mode, allocation tracking, multiplayer, theming. | — |

**Milestone 1 is the promotion gate:** a working Lite-mode MVP at < 2 % overhead on a 94-mod stack, with real per-mod cost attribution visible in-game.

---

## Public-mod quality bar

This is a Workshop install button — everyone sees it. The polish floor:

- **First-launch UX** — a *"Welcome to Profiler"* tutorial overlay (what it does, the hotkey, *"we don't touch your save"*, *"toggle off anytime"*).
- **Visual coherence** — a real palette + typography + spacing system, not the stock-tML-UI look.
- **Performance honesty** — the mod profiles *itself*; the overlay can show the profiler's own overhead, so the < 1 % claim is verifiable, not merely trusted.
- **License + transparency** — MIT, public GitHub, no telemetry, everything stays local on the player's machine.
- **Author response** — within 48 h on Workshop comments + GitHub issues for the first 90 days.

v1 is single-player only; multiplayer is a v2 feature, not a v1 blocker. English-only at launch, with the localisation system open for community PRs.

---

## Building & developing the mod

**Prerequisites:** Terraria + tModLoader (Steam App `1281930`), the **.NET 8 SDK** (`brew install --cask dotnet-sdk@8` — *not* 9 or 10), and Git.

This repository lives inside tModLoader's `ModSources/` directory — tModLoader only discovers mod source folders there.

**Iteration loop:**

```
  edit .cs  ──▶  dotnet msbuild   ──or──   in-game Build + Reload
                       │
                       ▼
   tModLoader → Mods → Reload Mods  ──▶  re-enter the world (re-fires OnEnterWorld)
```

Build failures and runtime logs land in:

```
~/Library/Application Support/Steam/steamapps/common/tModLoader/tModLoader-Logs/client.log
```

(the Steam *install* directory, not the save directory).

> [!NOTE]
> **Apple Silicon:** launch tModLoader in **windowed** mode only — a fullscreen transition triggers a silent crash on Apple Silicon ([tModLoader #4941](https://github.com/tModLoader/tModLoader/issues/4941)).

The full design rationale — every feature, every milestone, and the feasibility-research record behind the [mod-only decision](#architecture-decision-mod-only) — is maintained separately by the project author. This README is its directional summary; read it before re-opening a settled scope or architecture question.

---

## Repository layout

```
PerformanceProfiler/
├── PerformanceProfiler.cs       Mod entry point + Milestone 0 smoke test
├── PerformanceProfiler.csproj   SDK-style project; imports ..\tModLoader.targets
├── build.txt                    tModLoader build metadata (name, author, version)
├── description.txt              Workshop / mod-browser description
├── README.md                    this file — the directional document
├── CLAUDE.md                    engineering collaborator brief
├── .gitignore
├── Localization/
│   └── en-US_Mods.PerformanceProfiler.hjson    tModLoader localisation stub
└── design/
    └── Mockups.html             the visual design target for all nine overlay tabs
```

---

## Contributing

The mod is in early development — the most useful contributions right now are **feasibility input** on the open Milestone 0 spikes (detour cost at scale, JSON-lines crash safety, engagement-hook coverage) and **modlist test data**. Once Milestone 1 lands, the localisation system is open for community translation PRs. Issues and discussion are welcome.

---

## License

[MIT](LICENSE). Public GitHub repository, no telemetry, everything stays local.
