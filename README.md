<div align="center">

# Performance Profiler

### Per-mod CPU, RAM, and *engagement* attribution for your entire modded Terraria session — live, in your browser.

![status](https://img.shields.io/badge/status-v0.27.0%20·%20seven%20live%20tabs%20·%20cross--session%20memory-79c0ff?style=flat-square)
![C#](https://img.shields.io/badge/C%23-.NET%208-512BD4?style=flat-square&logo=dotnet&logoColor=white)
![tModLoader](https://img.shields.io/badge/tModLoader-1.4.4-1b7340?style=flat-square)
![read-only](https://img.shields.io/badge/instrumentation-read--only-95d4a3?style=flat-square)
![overhead](https://img.shields.io/badge/hot--path-zero--alloc-f0a35e?style=flat-square)
![license](https://img.shields.io/badge/license-MIT-79c0ff?style=flat-square)

**Install it, load a world, press F9.** Your browser opens to a live dashboard that tells you,
in real time, *exactly* which mods are costing you frames — and what you're actually getting
back for that cost.

</div>

---

> [!IMPORTANT]
> **There is nothing else like this for Terraria.** Generic .NET profilers tell you a *method*
> is slow. They cannot tell you that *this mod* cost you 0.8 ms/tick, that *that mod* eats CPU
> in the projectile system specifically, or that the mod you suspect is heavy is actually idle
> while a quiet one dominates your blood moons. Performance Profiler attributes cost **per mod,
> by category, over time, correlated with what you were doing** — and pairs it with an
> **engagement** axis so you can see cost *next to value*, not in a vacuum. No other mod in the
> tModLoader ecosystem does this. It is genuinely one of a kind.

---

## Table of contents

- [The 60-second pitch](#the-60-second-pitch)
- [What makes it one of a kind](#what-makes-it-one-of-a-kind)
- [What you see — the six tabs](#what-you-see--the-six-tabs)
- [The chart vocabulary](#the-chart-vocabulary)
- [How it works](#how-it-works)
- [What it captures, every tick](#what-it-captures-every-tick)
- [The insights engine](#the-insights-engine)
- [The trust posture — five invariants](#the-trust-posture--five-invariants)
- [What it costs you](#what-it-costs-you)
- [The testing harness](#the-testing-harness)
- [Roadmap](#roadmap)
- [Building from source](#building-from-source)
- [Repository layout](#repository-layout)
- [FAQ — the design decisions](#faq--the-design-decisions)
- [License](#license)

---

## The 60-second pitch

You run 80 mods. Your frames dip in blood moons and you have *no idea* why. You suspect the
big content mod, but you're guessing. You disable mods one at a time, relaunch, fight another
blood moon, and hope you notice a difference. It takes an evening and you learn almost nothing.

Performance Profiler replaces all of that with **measurement**:

```
You, before:  "I think Calamity is heavy?"        → disable, relaunch, guess, repeat
You, after:   "CalamityMod is 28.6% of my CPU,    → press F9, read the dashboard, done
               mostly in the World system, and
               it spikes hardest during bosses."
```

It runs *inside* the mod, costs you under 1% in its default mode, never touches your save or
any other mod's state, and shows you everything in a clean browser dashboard you can drag onto
a second monitor while you play.

---

## What makes it one of a kind

<div align="center">

| Most profilers | Performance Profiler |
|---|---|
| Per-method / per-assembly | **Per mod**, by category (NPCs, projectiles, world, …) |
| A single flame graph | **Six purpose-built tabs**, live, in a browser |
| "X is slow" | "X costs Y ms/t **with** Z engagement — *you* decide" |
| Snapshot in time | **Segments**: every biome, boss, invasion, death-run, costed |
| One session | **Cross-session** lifetime baselines, fingerprinted per modlist |
| Tells you what to remove | **Descriptive, never prescriptive** — it measures, you choose |
| Modifies/hooks aggressively | **Read-only**: zero save-corruption risk, zero compat war |
| Generic across apps | **A statistical insights engine** purpose-built for modded play |

</div>

The combination — per-mod attribution, an engagement axis, time-bounded segments, a
statistically-guarded insights engine, cross-session memory, and a browser dashboard, all
under a strict read-only trust posture — does not exist anywhere else in the Terraria /
tModLoader world.

---

## What you see — the six tabs

```
┌── F9 ─────────────────────────────────────────────────────────────────────┐
│  SUMMARY   Timeline   Lag   Insights   Self   Memory                       │
└────────────────────────────────────────────────────────────────────────────┘
```

**Summary** — the live mission-control view, where you spend most of your time:
- a **frame-time trace** for the last 30 s (with the 60 fps line and spike markers),
- the **impact donut** (each mod's share of total cost),
- a **per-mod cost stream** — every mod stacked over a rolling window, so you *watch the whole
  modlist's cost breathe* and see who owns the frame moment to moment,
- a **cost-flow sankey** — which subsystem (NPCs, projectiles, world…) each heavy mod is heavy in,
- the session **minute-by-minute heatmap**, the **now-playing** segments, a **recent-events** feed,
- and the full **per-mod cascading tree** (sort by current cost, session average, or composite).

**Timeline** — every closed segment of your session as a time-scaled **swimlane**: each biome
visit, weather event, boss fight, invasion, and death-bracketed run, each block filled by its
cost intensity. Click a block to drill into what it cost. Plus a session chronicle and biome
attendance.

**Lag** — every lag **spike** and **stall** the profiler caught, with the per-mod breakdown at
the *worst tick*, **GC pressure** over time, a per-segment **lag-density** table, and the **lag
rhythm** (how often hitches recur). Get a 24 ms frame? You see who owned it.

**Insights** — pattern-detection records from the engine, plus a per-mod **observatory**, a
modlist-composition **waffle** (active vs dormant at a glance), a **dormant-content** ranking,
an **engagement-vs-cost bubble scatter** (cost-heavy vs usage-heavy mods on one plot), and a
**mod-pair cost-correlation** table (which mods get busy together).

**Self** — the profiler measuring *itself*: an **overhead gauge** against budget, its install
footprint, bytes-per-hook, process context, and the per-mod **hook distribution**. We surface
our own cost so the claim is verifiable, not trusted.

**Memory** — per-mod **RAM**: each mod's profiler-scaffolding footprint *and* tModLoader's own
estimate, as a split strip plus a sortable table with a per-mod breakdown drawer.

---

## The chart vocabulary

The dashboard speaks a deliberately rich visual language — the right encoding for each data
shape, not "bars everywhere":

| Encoding | Where | What it answers |
|---|---|---|
| Line + area + threshold | Summary frame trace, Lag GC pressure | how is the frame doing *right now* vs 60 fps |
| **Stacked-area stream** | Summary cost stream | who owns the frame, *over time*, all mods at once |
| **Sankey flow** | Summary cost-flow | which *subsystem* is each heavy mod heavy in |
| Donut / share | Summary impact | each mod's slice of total cost |
| **Bubble scatter** | Insights engagement-vs-cost | cost-heavy vs usage-heavy, bubble = roster size |
| **Waffle grid** | Insights modlist composition | active vs dormant as countable area |
| Radial gauge | Self overhead, Insights KPIs | a value against a budget / reference |
| Heatmap | Summary timeframe, Lag cause×context | magnitude across a 2-D grid |
| **Swimlane gantt** | Timeline | every segment placed on the session's time axis |
| Sparkline + KPI | Summary KPI strip | a headline number with its own micro-trend |

> The whole layer is **monochrome chrome, colourful data**: surfaces and text are neutral grey
> on near-black (shadcn-neutral, OKLCH); the *only* colour on screen is the data itself. Colour
> always *encodes* — a per-mod hue, a severity ramp, a magnitude — it never just decorates.

---

## How it works

You don't set anything up. The mod ships with everything it needs and starts a tiny local
server when you launch tModLoader with it enabled.

```
        Terraria tick  (≈ 60 / second)
              │
              ▼
   ┌──────────────────────┐    MonoMod IL detours, installed through tModLoader's
   │   Hook Interceptor   │    official MonoModHooks API. tModLoader tracks per-assembly
   └──────────┬───────────┘    detour ownership — so per-MOD attribution comes for free.
              │  raw per-hook timings · zero allocation on the hot path
              ▼
   ┌──────────────────────┐    per-mod CPU by category (Systems / Players / NPCs /
   │   Metric Collector   │    Projectiles / Items / World / Buffs), frame time, GC pauses,
   └──────────┬───────────┘    entity counts, and the game-state context of the moment
              │
              ▼
   ┌──────────────────────┐    pre-allocated once, power-of-two sized, mask-indexed —
   │     Ring Buffer      │    no per-tick objects, no boxing, no GC churn from us
   └──────────┬───────────┘
              │
        ┌─────┴───────────┐
        ▼                 ▼
  ┌───────────┐   ┌────────────────────┐   reference frames + drivers, five detector
  │  Data/    │   │   Insights/ engine │   families, every claim statistically guarded
  │ pipeline  │   └─────────┬──────────┘
  └─────┬─────┘             │
        └────────┬──────────┘
                 ▼
   ┌──────────────────────┐    raw TCP HTTP on 127.0.0.1 only (loopback). ~250 lines,
   │  Local HTTP server   │────JSON──┐   no admin rights, no firewall prompt, no setup.
   └──────────┬───────────┘          │
              │                      ▼
              ▼              ┌────────────────┐
   ┌──────────────────────┐ │    Browser     │   the six-tab dashboard — your screen,
   │  LiteDB persistence  │ │   dashboard    │   or a second monitor, updating 2–4×/sec
   └──────────────────────┘ └────────────────┘
     sessions + cross-session baselines,
     fingerprinted per modlist
```

Every component is a **swappable subsystem** (Hook Interceptor, Metric Collector, Ring Buffer,
Context Tagger, Encounter Detector, UI Renderer, Persistent Store, Insights Engine). The
profiling **modes** expose that same modularity to you — instrumentation layers are cleanly
removable, not entangled:

<div align="center">

| Mode | Overhead budget | What it measures |
|---|---|---|
| **Off** | 0% | nothing — the mod declines to instrument |
| **Lite** | < 1% | the default; per-mod cost, frame time, segments |
| **Standard** | 2–4% | adds richer attribution + finer sampling |
| **Deep** | 5–10% | adds per-hook call histograms and the heaviest detail |

</div>

---

## What it captures, every tick

```
  per tick (60×/s)            built from those raw measurements
  ─────────────────           ──────────────────────────────────────────────
  ● per-mod CPU cost          → spike windows   (coalesced slow runs, per-mod
    by category                  breakdown at the worst frame)
  ● frame time                → stall events    (sustained main-thread freezes,
  ● GC pauses                    attributed by cause: GC, MainThreadFreeze, …)
  ● entity counts             → segments        (biome / boss / invasion / death
    (NPCs, projectiles, dust)    runs / bookmarks — each carries its accrued cost)
  ● game-state context        → lifetime aggs   (what does an average Jungle visit
    (biome bits, weather,        cost across all sessions? which mod is the most
     bosses, invasions,          consistent #1 in blood moons?)
     hardmode, subworlds,      → insights        (statistically-guarded pattern
     player death state)          records computed off all of the above)
```

Everything persists to a local **LiteDB** file. Nothing is shared, nothing is uploaded.

---

## The insights engine

The headline subsystem, and the part with no parallel anywhere. It is *not* a pile of
if-statements — it is a small statistical engine that turns raw measurements into honest,
guarded observations. Its spine law: **every insight is the deviation of a signal from the
comparable baseline for that signal, expressed as an effect size** — never an absolute number
shouted into the void.

```
  FIVE DETECTOR FAMILIES                          example record
  ──────────────────────                          ─────────────────────────────────────────
  ▸ Deviation   cost vs its own baseline          "CalamityMod runs 1.8× its usual cost
                                                    while a boss is alive"  (Welch t, Cohen's d,
                                                    Bonferroni-corrected, badged ThisSession →
                                                    LifetimeData as cross-session data grows)
  ▸ Temporal    later vs earlier in the session   "managed heap is 1.8× its early level at a
                (controls for the workload          similar entity load — a restart resets it"
                 confound — heap up *with* more     (only fires when heap rose but entity load
                 entities is progression,           did NOT — never says 'mod X leaks')
                 not a leak)
  ▸ Distribution frame-time shape                  "frames swing ±18% around 14 ms — frequent
                                                    small hitches" (stutter ≠ slowdown)
  ▸ Headroom    budget remaining                   "you sustain 60 fps with ~3 ms of frame
                                                    budget free" (how much more can you add?)
  ▸ Structure   cross-mod relationships            "3 of your 47 mods account for 71% of cost"
                                                    (a lever — never a verdict on those mods)
```

It carries **reference frames** (per-context cost distributions, Welford-online so they cost
nothing on the hot path), **drivers** (entity count, session age, heap — the dimensions it
regresses against and *controls for*), and **cross-session persistence** keyed by a
machine/modlist fingerprint, so a stack's runs combine into a lifetime distribution and
confidence can climb past "this session" honestly.

---

## The trust posture — five invariants

These are inviolable. They are *why* the mod is safe to run on a modlist you care about:

1. **Read-only instrumentation.** It *measures*; it never changes game behaviour, save data,
   world state, or any other mod's state. The worst tolerable failure is the profiler declining
   to load. Zero save-corruption risk, zero compatibility war with content mods.

2. **Overhead is a budget, not an aspiration.** Lite < 1%, Standard 2–4%, Deep 5–10%. The
   per-tick hot path is **zero-allocation** — pre-allocated structs, no boxing, no per-call
   timing objects. An unmeasured hot-path change is an incomplete change.

3. **The honesty contract.** The profiler is **descriptive, never prescriptive**. No mod is
   "core" or "removable". Every insight cites the measurement that produced it and badges its
   data strength. The copy reads *"CalamityMod costs 0.78 ms/t across 9 boss fights"*, never
   *"remove CalamityMod"*. You decide what the numbers mean.

4. **Abort-clean on host drift.** tModLoader's internals change between versions. If something
   the Hook Interceptor depends on no longer matches, it **disables its instrumentation and
   reports it** — it never proceeds against internals it cannot verify. It will not corrupt your
   run trying to measure it.

5. **No mod-specific code.** Every detector, classifier, and event handler reads **generic
   surfaces** tModLoader / vanilla Terraria expose (biome bits, `SpawnSource`, the
   `PlayerDeathReason` struct, the buff arrays, the equipment slots) — never a named mod's
   identifier. A profiler that knew "Calamity does X" would be brittle and unfair. This one
   reads the *interaction shape*, not the mod identity, so it works for every mod, including
   ones that don't exist yet.

---

## What it costs you

Profiling is never free, so we measure our own cost too — and surface it on the **Self** tab so
the claim is verifiable, not trusted.

<div align="center">

| Resource | Cost | Notes |
|---|---|---|
| **CPU** | < 1% in Lite mode | measured 0.12 ms/tick on a real 18-mod install (~0.7% of a 16.6 ms frame) |
| **RAM** | ~50–60 KB per installed hook | the dominant cost — MonoMod/Cecil per-hook detour scaffolding |
| **Disk** | a few KB / minute of play | rolling full-resolution window + downsampled older aggregates |
| **Network** | **zero** | loopback only — nothing ever leaves your machine |

</div>

RAM scales with modlist size (it is the focus of ongoing optimisation): a ~10,000-hook install
is ~0.5 GB; a heavy ~60,000-hook stack measured ~3.5 GB; a ~150,000-hook kitchen-sink measured
~8 GB. We measure and surface every byte of it on the Self tab.

---

## The testing harness

The dashboard is the product's surface, so it gets a serious testing story — a **layered,
self-describing harness** that screenshots and audits every tab off-game, with no build and no
running game:

```
  L1  pure-logic xUnit        detectors, ranking, attribution, schema — 108 tests
  L4  layout invariants       Playwright: overflow, sticky headers, selection, alignment,
                              label-overlap, dead-space — deterministic, fires on a regression
  L6  generative fixtures     fills the /api contract (discovered from source) at realistic +
                              edge-case magnitudes, so every pane renders for the audit
  L8  agent-driven UI audit   screenshots every tab + pane + state, fans out a vision agent per
                              tab against a design rubric, writes evolving per-page dossiers
```

It is **generic**: tabs are discovered from the DOM, endpoints from the JS, panes from the
markup — add a seventh tab tomorrow and it is audited with zero harness change. (See
`tools/testing/README.md`.)

---

## Roadmap

```
  NOW  ── v0.27.0 ─────────────────────────────────────────────────────────────────
         seven live tabs (Observatory + Insights split) · rich chart vocabulary ·
         insights engine (5 families) · the CROSS-SESSION HISTORY LAYER: a two-level
         per-mod rollup keyed on stable identity, a HistoryStore read layer, cross-
         session + cross-modpack detectors (LifetimeData), a session-end insight
         producer, modlist-change detection, and a player-initiated reset control ·
         off-game L4/L6/L8 testing harness

  NEXT ───────────────────────────────────────────────────────────────────────────
   ◇ ContextBaseline re-key on stable identity        (per-context cost baselines
                                                        survive a modlist edit too)
   ◇ per-mod lifetime trend in the mod-context drawer (/api/history is ready)
   ◇ shareable post-session HTML report               (one self-contained file)
   ◇ richer per-hook timing histograms (Deep mode)    (the gated HookFrequencyTail
                                                        + LoadoutCombinationCost detectors)

  v1.0 ───────────────────────────────────────────────────────────────────────────
   ◇ first public Steam Workshop release · first-launch UX · screenshots · GIFs

  LATER ──────────────────────────────────────────────────────────────────────────
   ◇ Steam Deck handheld dashboard view
   ◇ mobile-friendly dashboard layout
   ◇ multiplayer-server-side variant
```

---

## Building from source

You need tModLoader (Steam App `1281930`), the **.NET 8 SDK** (`brew install --cask
dotnet-sdk@8` on macOS; matching installer on Windows), and Git.

This repository lives inside tModLoader's `ModSources/` directory — tModLoader only discovers
source folders there. Clone or symlink it into place, then build with tModLoader's MSBuild
targets (not `dotnet build`):

```sh
dotnet msbuild
```

…from the mod folder. This produces a `.tmod` and copies it into tModLoader's `Mods/` directory.
**Mods → Reload** in the game and you're done. The dashboard is bundled inside the `.tmod` — no
additional setup, no Node, no Docker.

> [!TIP]
> Iterating on the dashboard UI? `tools/preview/build_preview_html.py` renders the dashboard
> straight from the C# source into a single offline HTML file — no build, no game — so you can
> eyeball CSS/JS changes instantly. The in-game **Build + Reload** stays the final check.

---

## Repository layout

```
PerformanceProfiler/
├── Profiling/            # The measurement engine: hooks, attribution, segments,
│                         # spike + stall detectors, persistence.
├── Insights/             # The insights engine: reference frames, drivers, the five
│                         # detector families, ranking, rendering.
├── Web/                  # Raw-TCP HTTP server + the dashboard SPA (HTML / CSS / JS,
│                         # authored as C# verbatim-string assets under Web/Assets/).
├── UI/                   # ARCHIVED — the legacy in-game overlay. Not compiled; kept on
│                         # disk for a future handheld / Steam-Deck revival.
├── Localization/         # tModLoader hjson localisation files.
├── Tests/                # xUnit test project (the L1 pure-logic axis).
├── tools/                # Off-game tooling (not shipped): preview/ renders the dashboard
│                         # from source with no build; testing/ is the L4/L6/L8 audit harness.
├── design/               # Design docs, the offline interactive preview, mockups. Not shipped.
├── context/              # Engineering notes, plans, and per-page audit dossiers. Not shipped.
└── build.txt             # tModLoader build manifest (version + asset rules).
```

---

## FAQ — the design decisions

<details>
<summary><b>Why no in-game UI? Why a browser?</b></summary>

<br>

Because we tried — five different in-game overlays (the code is still in `UI/` for reference) —
and Terraria's sprite-based UI is simply not built for dashboard work: text is hard to read at
small sizes, charts look rough, click-targets drift, tabs don't fit narrow panels. A browser
wins on every axis: real typography, smooth charts, real CSS layout, a bigger canvas (especially
on a second monitor), no overlap with the game's HUD, and a far more mature surface for us to
iterate on. So in v0.9 we archived the overlay entirely; the mod's in-game footprint is now just
the F9 keybind and a one-line chat hint.
</details>

<details>
<summary><b>Why a local HTTP server? Is anything leaving my machine?</b></summary>

<br>

Nothing leaves your machine. The server binds **only to `127.0.0.1`** (loopback) — your computer
can talk to itself, the outside world cannot reach it. We use raw `TcpListener` rather than
.NET's `HttpListener` because, on Windows, `HttpListener` needs admin rights or a one-time admin
command to bind a port for ordinary users — which would break the "press F9 and it just works"
promise. Raw TCP has no such restriction, so we hand-built a ~250-line HTTP server that works on
Windows, macOS, and Linux with zero setup, zero permission prompts, and zero admin elevation. No
telemetry, no analytics, loopback bypasses the macOS firewall, and your data stays on your
machine forever.
</details>

<details>
<summary><b>Terraria pauses when it loses focus — won't the dashboard freeze?</b></summary>

<br>

That's a Terraria behaviour, not ours: single-player pauses the simulation when the window isn't
focused, so clicking into your browser freezes the dashboard (the game stopped ticking). Three
workarounds: (1) keep Terraria focused and glance at the browser side-by-side; (2) put the
dashboard on a second monitor; (3) open your world via **Multiplayer → Host & Play** — multiplayer
servers never pause, you're still playing solo, and the save file is identical. We can't fix the
focus-pause without modifying Terraria's internals, which we've deliberately chosen not to do.
</details>

<details>
<summary><b>Will it break my mods or corrupt my save?</b></summary>

<br>

No. Invariant 1 (read-only) is the entire trust posture: there is no code path that alters what
the game does, what any other mod does, or what's in your save. The worst tolerable failure mode
is the profiler refusing to load (Invariant 4, abort-clean). It installs its measurement detours
through tModLoader's official `MonoModHooks` API — the same mechanism tModLoader uses to track
per-mod ownership — so it coexists with content mods rather than fighting them.
</details>

---

<div align="center">

**MIT licensed.** Public source on GitHub. No telemetry. No analytics. No mod data ever leaves
your machine.

Built by [Capataina](https://github.com/Capataina) with help from Claude.

*If your modlist has ever cost you frames and you didn't know why — this is the tool that tells you.*

</div>
