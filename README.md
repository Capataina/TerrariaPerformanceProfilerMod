# Performance Profiler

### A live performance dashboard for your Terraria modlist — runs in your browser.

![status](https://img.shields.io/badge/status-v0.22.0%20·%20six%20live%20tabs-79c0ff?style=flat-square)
![C#](https://img.shields.io/badge/C%23-.NET%208-512BD4?style=flat-square&logo=dotnet&logoColor=white)
![tModLoader](https://img.shields.io/badge/tModLoader-1.4.4-1b7340?style=flat-square)
![read-only](https://img.shields.io/badge/instrumentation-read--only-95d4a3?style=flat-square)
![license](https://img.shields.io/badge/license-MIT-79c0ff?style=flat-square)

Performance Profiler is a [tModLoader](https://github.com/tModLoader/tModLoader) mod that watches your modded Terraria session and tells you, in real time, exactly which mods are costing you frames — and it shows you everything in a clean browser dashboard, not in the game.

You install it, load a world, press **F9**, and your default browser opens to a live dashboard at `http://127.0.0.1:27277/`. It updates 2–4 times a second while you play. When something laggy happens, you can see who did it. When you fight a boss, you can see what the fight cost you. When the session ends, the dashboard still shows you a full retrospective of what happened.

> [!NOTE]
> The mod itself is **invisible inside the game**. There's no in-game overlay, no toasts, no popups. The only thing it adds to your gameplay is a single chat line when you enter a world ("press F9 for the dashboard"). Everything else lives in the browser. Why we built it this way is explained [further down](#why-no-in-game-ui).

---

## How it works

You don't have to do anything extra. The mod ships with everything it needs:

1. **Install** the mod from the Steam Workshop (or build from source — see below).
2. **Launch** tModLoader and load any world.
3. **Press F9** in-game.

That's it. Your default browser opens to the dashboard. You can drag the browser window next to Terraria, or onto a second monitor, and watch it update live while you play.

If F9 doesn't open the browser for some reason (Linux sometimes has weird `xdg-open` behaviour), the dashboard URL is also printed in chat — copy and paste it into any browser.

---

## What you see on the dashboard

The dashboard has six tabs across the top:

**Summary** — the live mission-control view, and where you spend most of your time. A frame-time chart for the last 30 seconds; the impact-share donut; a per-mod **cost stream** (every mod stacked over a rolling window, so you watch the whole modlist's cost breathe and which mod owns the frame); a **cost-flow sankey** (which subsystem — NPCs, projectiles, world — each heavy mod is heavy in); the session minute-by-minute heatmap; the now-playing segments; a recent-events feed; and the full per-mod cascading tree (sort by current cost, session average, or composite; outliers highlighted).

**Timeline** — every closed segment from your session as a time-scaled swimlane: every biome visit, weather event, boss fight, invasion, death-run, each block filled by its cost intensity. Click a block to drill into what it cost, alongside a session chronicle and biome attendance.

**Lag** — every lag spike and stall the profiler detected, with the per-mod breakdown at the worst tick, GC pressure over time, a per-segment lag-density table, and the lag rhythm (how often hitches recur). When you get a 24 ms frame, you see which mod owned it.

**Insights** — pattern-detection records from the insights engine (outliers, context-conditional cost, leaks, cost concentration, cross-cutting signals), plus a per-mod observatory, a modlist-composition **waffle** (active vs dormant at a glance), dormant-content ranking, an **engagement-vs-cost bubble scatter**, and a mod-pair cost-correlation table.

**Self** — the profiler measuring itself: its overhead gauge against budget, install footprint, bytes-per-hook, process context, and the per-mod hook distribution. We're transparent about our own cost.

**Memory** — per-mod RAM: each mod's profiler-scaffolding footprint and tModLoader's own estimate, as a split strip plus a sortable table with a per-mod drill-down breakdown.

---

## Why no in-game UI?

Honest answer: because we tried, and the game's rendering tools just aren't very good for dashboard work.

Terraria draws everything using a blocky hand-drawn font and a sprite-based UI system. We built five different versions of an in-game overlay (the code is still in the repository for reference). Every version ran into the same problems: text was hard to read at small sizes, charts looked rough, click-targets drifted, tabs didn't fit on narrower panels, and the overlay was always either taking up too much screen real estate or being too tiny to actually use.

Then we asked: what if we just sent the data to a browser instead?

The browser wins on every dimension:
- Real typography. Smooth charts. Real CSS layouts.
- Bigger screen real estate (especially if you have a second monitor).
- No overlap with the game's HUD.
- You can ignore it when you don't need it; it doesn't sit on top of your inventory.
- Way easier for *us* to iterate on. Web tech is a more mature design surface than Terraria's UI primitives.

So in v0.9.0 we archived the in-game overlay entirely. The mod's footprint inside the game is now just the F9 keybind and a one-line chat hint. Nothing covers your screen, nothing pops up while you fight.

---

## Why a local HTTP server?

The dashboard runs **inside the mod**. There's no separate program to install, no Node.js to set up, no Docker container, no config files to edit. When you launch tModLoader with the mod enabled, the mod starts a tiny HTTP server on your own machine, bound only to `127.0.0.1` (localhost — your computer can talk to itself, the outside world cannot reach this server).

We use raw TCP sockets (`TcpListener` in C# terms) rather than .NET's `HttpListener`. The reason is dumb but real: on Windows, `HttpListener` requires either admin rights or a one-time admin command (`netsh http add urlacl ...`) for ordinary users to bind a port. That breaks the "press F9 and it just works" promise. Raw TCP doesn't have that restriction. So we built a minimal HTTP server by hand on top of TCP. About 250 lines of C#. Works on Windows, macOS, and Linux with zero setup, zero permission prompts, zero admin elevation.

The browser then polls a few JSON endpoints (`/api/now`, `/api/mods`, `/api/segments`, etc.) every few hundred milliseconds. Nothing fancy. Just enough to keep the dashboard up to date.

**Nothing leaves your computer.** The server only listens on loopback. No firewall prompts (loopback bypasses macOS' application firewall). No telemetry. No analytics. Your data stays on your machine forever.

---

## Heads up: Terraria pauses when it loses focus

This is a Terraria thing, not a us thing. By default, Terraria pauses the simulation the moment the window stops being the focused window. If you click into your browser to look at the dashboard, the dashboard freezes — because the game stopped ticking.

Three workarounds:

1. **Side-by-side without clicking** — keep Terraria focused, glance at the browser. The dashboard updates as long as the game is the active window. You can have both visible at once on the same screen.
2. **Second monitor** — Terraria on monitor 1 (focused), dashboard on monitor 2 (visible but not clicked into).
3. **Host & Play** — open your single-player world via Multiplayer → Host & Play instead. Multiplayer servers never pause. You're still playing solo, the game just doesn't freeze when alt-tabbed. The save file is the same — you can switch back to regular Single Player on the same world any time.

We can't fix the focus-pause without modifying Terraria's internals, which we've made a deliberate choice not to do.

---

## What it actually captures

Every tick (60 times a second by default), the mod records:

- **Per-mod CPU cost**, broken down by category (Systems / Players / NPCs / Projectiles / Items / World / Buffs)
- **Frame time** — how long the game took to process that tick
- **GC pauses** — how much of the frame was lost to garbage collection
- **Entity counts** — active NPCs, projectiles, dust particles
- **Game-state context** — biome bits, weather flags, active bosses, invasions, hardmode, subworlds, the player's death state

From those raw measurements we build:

- **Spike windows** — coalesced runs of unusually slow ticks, with the per-mod contributor breakdown at the worst frame
- **Stall events + clusters** — sustained main-thread freezes, attributed by cause (GC, MainThreadFreeze, etc.)
- **Segments** — temporally-bounded slices of play: every biome visit, every boss fight, every blood moon, every death-bracketed "run", every user bookmark. Each segment carries the per-mod cost accrued during it.
- **Lifetime aggregates** — across all your sessions, what does an average Jungle visit cost? Which mod is the most consistent #1 in Blood Moons?
- **Insights** — heuristic pattern records computed off the above: outliers, top-mod-of-segment-class, death/cost correlations

Everything is persisted to a local LiteDB file. Nothing is shared. Nothing is uploaded.

---

## The honesty contract

The profiler is **descriptive, never prescriptive**. We never tell you a mod is "the problem" or "should be removed". We tell you what it costs and what you do with it. Every insight cites the measurement that produced it. The dashboard copy never reads "remove X" — it reads "X cost Y ms/t on average across N visits". You decide what that means.

Three rules we enforce on ourselves:

1. **No mod-specific code.** Every detector, classifier, and event handler operates on generic surfaces tModLoader exposes (biome bits, NPC type IDs, weather flag enums). We never hard-code "if mod == CheatSheet then X". A profiler that knew "Calamity does Y" would be brittle and unfair.
2. **Read-only.** We measure. We never alter game behaviour, never edit save data, never touch other mods' state. The worst tolerable failure mode is the profiler refusing to load.
3. **Abort-clean on host drift.** tModLoader's internals change between versions. If something we depend on no longer matches what we expect, we disable our instrumentation and report it — we never proceed against internals we can't verify. We're not going to corrupt your run trying to measure it.

---

## What it costs you

Profiling is never free, so we measure our own cost too — and surface it on the **Self** tab so the claim is verifiable, not trusted.

- **CPU**: target < 1% overhead in the default mode. On a real 18-mod install we measured 0.12 ms/tick, which is about 0.7% of a 16.6 ms frame budget at 60 fps.
- **RAM**: ~50-60 KB of managed memory per installed hook, mostly MonoMod/Cecil per-hook detour scaffolding. This is the dominant cost and the focus of ongoing optimisation. A ~10,000-hook install is roughly 0.5 GB; a heavy ~60,000-hook modlist (full Calamity + Thorium-class) measured ~3.5 GB; a ~150,000-hook kitchen-sink measured ~8 GB. (Yes, it scales with modlist size, and we measure and surface our own footprint on the Self tab.)
- **Disk**: a few KB per minute of play. The database keeps a rolling window of recent ticks at full resolution and downsampled aggregates for older data.
- **Network**: zero. Loopback only.

---

## Project status

Currently at **v0.22.0**. Six live tabs with a rich chart vocabulary — frame trace, impact donut, per-mod cost stream, category-to-mod cost-flow sankey, modlist waffle, engagement-vs-cost bubble scatter, radial gauges, heatmaps, and a time-scaled swimlane. The insights engine has landed (five detector families, cross-session baselines), and a layered off-game testing harness (`tools/testing/`, the L4/L6/L8 axes) screenshots and audits every tab so visual regressions are caught without a build.

Roadmap from here:

- **v1.0** — first public Workshop release. Real first-launch UX, screenshots, GIFs, full Workshop description.
- **Next** — a player-facing insight feed on the Insights tab (the engine ranks insights; the tab does not yet render them as a feed), cross-session comparison views, and a shareable self-contained post-session HTML report.
- **Maybe later** — Steam Deck handheld dashboard view, mobile-friendly dashboard layout, multiplayer-server-side variant.

---

## Building from source

You need tModLoader (Steam App `1281930`), the **.NET 8 SDK** (`brew install --cask dotnet-sdk@8` on macOS; matching installer on Windows), and Git.

This repository lives inside tModLoader's `ModSources/` directory — tModLoader only discovers source folders there. Clone or symlink it into place, then:

```sh
dotnet msbuild
```

…from the mod folder. This produces a `.tmod` and copies it into tModLoader's `Mods/` directory. Reload mods in the game and you're done.

The dashboard is bundled inside the `.tmod`. No additional setup.

---

## Repository layout

```
PerformanceProfiler/
├── Profiling/            # The measurement engine: hooks, attribution, segments,
│                         # spike detector, stall detector, persistence, insights.
├── Web/                  # HTTP server + dashboard SPA (HTML / CSS / JS).
├── UI/                   # ARCHIVED — the legacy in-game overlay code. Not
│                         # compiled in v0.9+; kept on disk for future revival
│                         # in handheld / Steam-Deck variants.
├── Localization/         # tModLoader hjson localisation files.
├── Tests/                # xUnit test project (pure-logic detectors).
├── tools/                # Off-game tooling (not shipped): preview/ renders the
│                         # dashboard straight from the .cs source with no build;
│                         # testing/ is the L4/L6/L8 Playwright audit harness.
├── design/               # Design docs, the offline interactive preview, mockups. Not shipped.
├── context/              # Engineering notes, plans, and per-page audit dossiers. Not shipped.
└── build.txt             # tModLoader build manifest (version + asset rules).
```

---

## License

MIT. Public source code on GitHub. No telemetry. No analytics. No mod data ever leaves your machine.

Built by [Capataina](https://github.com/Capataina) with help from Claude.
