# Feature Atlas — every slot the mod has

> The tracking map of everything Performance Profiler is, could be, and should be.
> Created 2026-07-07 from the ten-slot discussion; expanded to 31 slots across five
> domains. **This file is the tracker**: the matrix below carries status; each slot
> has a brief. Deep designs live in `context/plans/<slot>.md` when a slot is picked
> for implementation. Update the matrix row when a slot's status changes.
>
> Status vocabulary: `built` (shipped + verified), `partial` (some of it exists),
> `planned` (plan file exists), `idea` (named, undesigned), `deferred` (explicit
> decision to postpone, with the trigger to revisit), `blocked` (named prerequisite).

## The organising question

The mod's promise: **"what is your modlist costing you, and what are you getting
for it?"** Every slot answers a player question. A slot is *not* a chart or a
panel — it is a capability with its own measurement, pipeline stage, surface, and
persistence footprint (the insights-engine scale bar).

## Status matrix

| # | Slot | Domain | Player question | Status | Size |
|---|------|--------|----------------|--------|------|
| S01 | Loop-anatomy attribution | Measurement | Where in the frame does mod X hurt — update, draw, GC? | **built** (84409c1, 0.32.0; Observatory split bars fb2d061) | L |
| S02 | Load-time profiler | Measurement | Why does my game take 4 minutes to boot, and which mod owns it? | idea | L |
| S03 | Content-level attribution | Measurement | It's not Calamity, it's *that boss* | idea | XL |
| S04 | Memory ownership engine | Measurement | Who owns my 10 GB, and who is growing? | **partial** (guard slice built 0f9e844, 0.33.0; per-mod ownership open) | XL |
| S05 | Stutter forensics | Measurement | What exactly happened in that one 400ms hitch? | idea | L |
| S06 | Real-cadence honesty completion | Measurement | Is every number the dashboard shows true during slow-motion? | **built** (448f447, 0.30.0; pinned) | M |
| S07 | Fingerprint robustness | Measurement | Why does the profiler think I ran 10 different modlists? | **built** (448f447; v2 identity) | S |
| S08 | Multiplayer / server profiling | Measurement | What does my modlist cost the server? | deferred (v2 decision, 2026-05-19) | XL |
| S09 | The Lab — experiment engine | Intelligence | The insight says X is expensive — prove disabling it helps | idea (top-3 leverage) | XL |
| S10 | Mod-update regression tracking | Intelligence | Calamity auto-updated last night — did it get slower? | idea (top-3 leverage) | L |
| S11 | Engagement beyond combat | Intelligence | RecipeBrowser reads 0 engagement forever, but I use it constantly | idea | L |
| S12 | Live sentinel | Intelligence | Warn me *in game* when things degrade | idea | M |
| S13 | Insight lifecycle | Intelligence | I acted on that insight — did the situation change? | idea | M |
| S14 | Modlist doctor report card | Intelligence | Give me the one-screen verdict on my whole modlist | idea | M |
| S15 | Session DVR | Presentation | Show me the whole session's story, not the last 30s | idea | L |
| S16 | Session-time gradient ribbon | Presentation | When during my session did it go bad? | **built** (fb2d061; axis + minute drill) | S |
| S17 | HTML session report | Presentation | Let me share/keep my session's story without the game | **built** (ef74479, 0.34.0; three triggers) | M |
| S18 | Popup card system | Presentation | Click the boss fight, get its report card | **built** (fb2d061; boss + minute cards) | M |
| S19 | Per-tab UX quality | Presentation | Does every pane read honestly, instantly, beautifully? | **partial** (audit rows closed c1cf962+fb2d061; enhancement backlog lives) | L |
| S20 | Warming states | Presentation | Why does minute 2 claim 97% of my mods are dormant? | **built** (fb2d061; panelState + gates) | S |
| S21 | Mobile / Steam Deck layout | Presentation | Can I read this on the couch? | deferred (revisit at v1.0 polish) | M |
| S22 | In-game overlay revival | Presentation | Quick HUD without alt-tabbing (archived UI/ tree exists) | deferred (dashboard-first decision D1) | L |
| S23 | Per-feature settings (ModConfig) | Platform | Let me turn the heavy parts down without losing the rest | **built** (88f10f4, 0.31.0; minus the S24 trim lever) | M |
| S24 | Self-optimisation: RAM | Platform | Why does the profiler itself cost 2.4 GB? | partial (B1 EMA cut 1.8 GB; scaffolding diet + reload-leak remain) | L |
| S25 | Self-optimisation: CPU | Platform | What does measuring cost, and can it cost less? | partial (harvest fusion candidate filed; budget guard missing) | M |
| S26 | DB health | Platform | Does the store stay small, fast, and uncorrupted forever? | partial (compaction manual; no size budget; backups bounded) | M |
| S27 | E2E testing framework | Platform | Can we catch bugs without launching the game? | **built** (rings 1-2 + run_all; Ring-3 contract deferred) | L |
| S28 | Backend parity surfacing | Platform | If the two measurement backends disagree, who tells the player? | idea (gap G2) | S |
| S29 | Abort-clean telemetry | Platform | When instrumentation aborts, does the player learn why? | idea | S |
| S30 | Workshop release kit | Release | Screenshots, description, first-launch UX, publish flow | idea (v1.0 gate list in vault Roadmap) | M |
| S31 | Localisation | Release | Does the dashboard read in the player's language? | partial (Localization/ exists for hjson; dashboard strings hardcoded) | M |

Size legend: S < 1 day · M ~1-2 days · L ~3-5 days · XL = milestone-scale.

---

## Domain A — Measurement (what we can see)

### S01 · Loop-anatomy attribution *(planned — `plans/loop-anatomy.md`)*
Per-mod cost split by loop phase: update / draw / (later: GC, load). Today every
hook sample lands in one bucket, so a draw-bound mod is indistinguishable from an
update-bound one — the exact blindness behind the 2026-07-07 slow-motion mystery.
Evidence the draw side is big: 24,227 probe calls/tick observed **while paused**
(zero update ticks — all draw-phase traffic). First slice: a phase flag set at
`PreUpdateEntities`/`PostUpdateEverything`/`PostDrawInterface`, folded per-mod into
update-ms vs draw-ms EMAs, split bars in Observatory + Summary impact donut.

### S02 · Load-time profiler *(idea)*
Attribution for the minutes before the world exists: mod load, asset load, JIT,
world-gen hooks. Nothing measures this today (WorldLoad is excluded even from
stalls). Surfaces as a "startup cost" panel + lifetime load-time trends per mod.
Prerequisite: instrumentation windows during `Mod.Load`/asset binds without
violating the abort-clean invariant while loader internals are mid-flight.

### S03 · Content-level attribution *(idea, XL)*
Cost per content *type*, not per mod: which NPC/projectile/item family dominates.
Generic surfaces only (type introspection at `NPCLoader.OnSpawn` etc., never mod
name matching — Invariant 5). This answers "keep the mod, avoid that boss".
Heavy: per-type accumulators at 62k-hook scale need a bounded top-K design.

### S04 · Memory ownership engine *(planned first slice — `plans/memory-guard.md`)*
Who owns the heap, who is growing, and is it a leak? First slice (memory-guard):
process-level trend + growth verdicts + reload-stack detection. Full slot: per-mod
managed ownership (alloc-site sampling), texture/asset footprint attribution, leak
suspects ranked. The 2026-07-07 cross-reload finding (install delta 1.82 → 2.46 GB,
bytes/hook 30 → 40.5 KB, same 62,203 hooks) is the live case study.

### S05 · Stutter forensics *(idea)*
A black-box flight recorder: when a spike/stall fires, snapshot the offending
tick's *full* per-hook anatomy (top-N frames, GC state, entity counts, event
context) into a bounded forensics ring, drillable from the Lag tab. Today a spike
stores top-contributor summaries; the "what exactly happened" question dies there.

### S06 · Real-cadence honesty completion *(planned — `plans/honesty-completion.md`)*
The 0.28.1 KPI repoint fixed the strip; the detector/insight layer still reads
update-window time. Live capture caught FRAME HEADROOM claiming "you sustain 60
fps" during 31-fps slow-motion (X1), the Lag tab reading 0 events at 2× budget
(X2), and stall KPIs headlining a 122s alt-tab (X3). This slot closes the class:
every consumer of `FrameTimeMs` audited, sustained-deficit signal added, stall
aggregation made cause-aware.

### S07 · Fingerprint robustness *(planned — inside honesty-completion)*
11 sessions produced 10 "modlists seen": the fingerprint fractures on every dev
build (version-sensitive + profiler-inclusive), so cross-modpack baselines never
accumulate. Fix: fingerprint on the InternalName *set* excluding the profiler
itself; keep version data as metadata (feeds S10) rather than identity.

### S08 · Multiplayer / server profiling *(deferred — v2 decision 2026-05-19)*
Server-side variant: what the modlist costs the host, per-player cost deltas,
sync-hook attribution. The instrumentation surface barely changes; the coverage
interpretation does. Revisit trigger: v1.x stable on Workshop + a real MP request.

## Domain B — Intelligence (what we conclude)

### S09 · The Lab — experiment engine *(idea; top-3 leverage)*
Insights describe; nothing verifies. The Lab proposes an experiment ("next session,
disable X"), detects the fingerprint diff automatically, runs the before/after
comparison with the existing Welch/Cohen machinery, and reports descriptively
("sessions without X averaged 4.1ms less"). Closes the loop the insights engine
opens, still honest (never "remove X" — Invariant 3). No profiler in any modding
scene has this.

### S10 · Mod-update regression tracking *(idea; top-3 leverage)*
Steam auto-updates mods invisibly. Track version-per-mod in session metadata
(S07 moves versions out of identity, making this possible); when a version changes,
compare cost baselines before/after: "Calamity 2.0.3 → 2.0.4: +18% update cost,
first seen yesterday". Uniquely enabled by the cross-session layer.

### S11 · Engagement beyond combat *(idea)*
The engagement axis is combat-centric (damage, buffs, held items), so QoL mods
read 0 forever — exactly where remove-it-or-not doubt lives. Add generic UI-time
(menus opened via `ModSystem` UI hooks), recipe interactions, teleports, NPC
dialogue. Needs care: every counter is a per-tick surface (Invariant 2).

### S12 · Live sentinel *(idea)*
In-game degradation alerts without alt-tab: "last 2 min: frame cost 2× baseline —
top riser: X" as chat/toast, threshold-configurable (S23), quiet by default. The
detector machinery exists; this is a delivery channel + rate-limiter design.

### S13 · Insight lifecycle *(idea)*
Insights are stateless verdicts today. Track them: first-seen, still-true,
resolved ("this stopped being true after your modlist change"), acted-on. Feeds
S09 and stops the feed re-alarming on known-stable facts.

### S14 · Modlist doctor report card *(idea)*
The one-screen composite: every mod graded on cost, engagement, trend, stability,
with lifetime badges and honest phrasing. Pulls S01/S04/S10/S11 into a single
ranked table — the screen a player screenshots and posts.

## Domain C — Presentation (how it's told)

### S15 · Session DVR *(idea)*
Scrubbable full-session timeline from the warm tier: cost, events, spikes,
segments, insights on one axis. The Timeline tab today is a 30s live window +
lists; the DVR is the whole story. S16 is its seed.

### S16 · Session-time gradient ribbon *(planned — inside ui-overhaul)*
The user's design: the Timeline strip as a session-long per-minute heat gradient
(green → amber where the middle went bad → green again). Data already exists
(heatmap per-minute buckets); the strip currently renders a single unreadable
block with a duplicate-number caption.

### S17 · HTML session report *(planned — `plans/html-session-report.md`)*
Self-contained shareable HTML generated from LiteDB (embedded CSS/JS, no server,
no game). Captured as a future-note 2026-05-20; schema is reader-friendly since
v0.27. Trigger: dashboard button + chat command.

### S18 · Popup card system *(planned — inside ui-overhaul)*
Click-to-open report cards as centred modal popups (not only side drawers): boss
fights, stall clusters, mods, minutes. One reusable card component; the boss card
is the flagship (segment engine already stores the data).

### S19 · Per-tab UX quality *(planned — `plans/ui-overhaul.md`)*
The standing audit ledger (`plans/ui-ux-audit.md`, 2026-07-07: X1-X8 + 30 per-tab
items) plus the enhancement pass: motion/interactivity, colour language,
intuitiveness, new dataviz per pane. Two-pass protocol: enumerate in chat, then
implement.

### S20 · Warming states *(planned — inside ui-overhaul)*
Session-age-aware empty/warming states everywhere: no more "97% dormant" verdicts
from 3 minutes of data, no grey-slab charts at 1 sample, no seven stacked "no
events" panels. One pattern component, applied per-surface with each surface's
minimum-data threshold.

### S21 · Mobile / Steam Deck layout *(deferred)*
Responsive pass for couch/handheld. Revisit at v1.0 polish alongside S30.

### S22 · In-game overlay revival *(deferred — decision D1 dashboard-first)*
The archived `UI/` tree (5 tabs, ~5,500 lines, preserved deliberately) as a quick
F9 HUD for players who won't alt-tab. Revisit trigger: post-v1.0 player feedback.

## Domain D — Platform (what it runs on)

### S23 · Per-feature settings via ModConfig *(planned — `plans/feature-settings.md`)*
The user's specified design: tModLoader's own ModConfig UI (no custom settings
surface to maintain), features grouped by impact (heavy-RAM section, heavy-CPU
section), each a toggle or slider from disable → full. Replaces the never-built
Lite/Standard/Deep presets with per-feature control; the heaviest configuration
remains the default and the verification target.

### S24 · Self-optimisation: RAM *(partial)*
Done: B1 per-hook history EMA (−1.8 GB), v0.12.1 scaffolding trim (3.7 → 1.0 GB
at the time), B4 reclaim diagnostic. Open: the reload-stack leak (old install
residue pinned across Reload Mods — 30 → 40.5 KB/hook observed), further
scaffolding diet (SourceCloneIl retention vs Invariant 4), per-hook static cost
(41 KB × 62k = 2.4 GB is still the #1 RAM line in the player's own Memory tab).

### S25 · Self-optimisation: CPU *(partial)*
Done: A2/A3 self-cost visibility (harvest 0.4-0.5ms/tick, 93% of self-cost),
per-hook fold fusion candidate filed in the 2026-07-07 audit. Open: harvest
budget guard (adaptive stride when over budget), probe dispatch cost review,
EMA fold consolidation — all coverage-preserving only (the heaviest version must
keep working; the user's explicit constraint).

### S26 · DB health *(partial)*
Bounded backups exist (ring of 3); rebuild-rollup control shipped 0.28.0. Open:
auto-compaction cadence (manual `/profiler-compact` today), store size budget +
alerting (27.4 MB after 11 sessions — fine, but ungoverned), corruption recovery
drill, migration story for schema bumps.

### S27 · E2E testing framework *(planned — `plans/e2e-testing.md`)*
Catch bug classes without launching the game: synthetic-session simulator driving
the real collector→stats→API path off-game; fixture live-mimic UI harness
(exists, needs corrected selectors + new-feature scenarios + interaction states);
regression pins mined from the git history's fixed-bug classes (LiteDB predicate
indexers, UTC date kinds, shutdown races, denormals, JIT generic sharing).

### S28 · Backend parity surfacing *(idea — gap G2)*
`Parallel` mode computes delegate-vs-ILHook divergence and logs it; no surface
shows it. Small: a Self-tab row + a warning band when divergence exceeds a
threshold.

### S29 · Abort-clean telemetry *(idea)*
Invariant 4 aborts are designed but quiet: surface "instrumentation disabled:
<reason>" as a first-class dashboard state + a persisted incident record, so a
Workshop user can report *why* instead of "it stopped working".

## Domain E — Release (how it ships)

### S30 · Workshop release kit *(idea — v1.0 gate list in vault Roadmap)*
description.txt rewrite (still says "Milestone 0 hello-world"), screenshots/GIF
set, first-launch UX (one-shot dashboard tutorial hint), publish flow + appid.

### S31 · Localisation *(partial)*
`Localization/` exists for hjson keys; dashboard strings are hardcoded English.
Slot: string table for UI copy + insight templates (insight templates are the
hard part — they carry the honesty-contract phrasing).

---

## Cross-slot dependency sketch

```
S23 settings ──gates──> S01, S04/guard, S05, S11, S12 (every heavy feature registers a control)
S07 fingerprint ──unlocks──> S10 update regression ──feeds──> S09 the Lab
S06 honesty ──prerequisite for──> every player-facing number the other slots add
S27 testing ──safety net under──> all implementation waves
S16 ribbon ──seed of──> S15 DVR;  S18 cards ──container for──> S05 forensics drill, S14 report card
S01 anatomy + S04 ownership + S11 engagement ──feed──> S14 doctor card
```

## 2026-07-07 implementation batch (this session's picks)

EXECUTED 2026-07-07 (see the per-plan status banners). Originally picked: **S06+S07** (honesty),
**S01** (loop anatomy), **S23** (settings), **S04 first slice** (memory guard),
**S17** (HTML report), **S27** (testing), **S16/S18/S19/S20** (ui-overhaul
umbrella). Plus the ledgered bug batch (H1/H2 + audit BUG rows).
