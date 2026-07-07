# UI/UX audit — live capture, 2026-07-07 (v0.28.1 in-game)

Source: Playwright full-page screenshots of all 7 tabs against the LIVE dashboard
(127.0.0.1:27277) during a real slow-motion session (31 fps avg, 33.3ms frames,
frameskip off, ~3 min into a fresh world). Screenshots in the session scratchpad
(`ui-capture/*.png`, ephemeral); this file is the durable record.

Capture limitations (honest): the DOM overflow sweep returned nothing (selector
mismatch — the app has no `.pane.active .panel` combo; panes are `tab-pane`,
panels use `panel-h`/`panel-body`), so clipping below is from visual reading of
1680px screenshots. Hover, drill-down, drawer, and scroll-at-extreme states were
NOT exercised (known harness gotcha). A corrected selector sweep is listed at the
bottom.

## Severity legend

- **[HONESTY]** violates Invariant 3 or shows a false/misleading number
- **[BUG]** broken rendering or layout
- **[UX]** correct but confusing, wasteful, or unreadable
- **[POLISH]** small texture fix

---

## Cross-cutting findings (ranked)

### X1 [HONESTY] The insight feed tells the player "you sustain 60 fps" during visible slow-motion
FRAME HEADROOM card (Insights tab, THIS SESSION badge): "You sustain 60 fps with
8.4 ms of frame budget free" — captured while the game ran 31 fps / 33.3ms real
frames. 16.7 − 8.3ms compute = 8.4: the detector reads update-window
`FrameTimeMs`. The 0.28.1 read-side repoint fixed the KPI strip but NOT the
detector/insight layer. This is the strongest possible case for H4: a
player-facing sentence that is factually false during the exact condition the
mod exists to diagnose. Fix: repoint FrameHeadroom (and audit every detector
reading FrameTimeMs) to RealFrameTimeMs.

### X2 [HONESTY] The Lag tab is structurally blind to sustained slowness
At 33ms frames / 2× budget, the Lag tab reads: EVENTS 0, SESSION LAG 0.00ms,
WORST EVENT P95 0.00ms, and six stacked "no events / no clusters / no gc data"
panels. The lag model only counts *variance* events (spikes, stalls); a game
that is uniformly 2× over budget produces zero events and a perfectly clean lag
page. Needs a "sustained deficit" concept: time-over-budget, budget-deficit ms/s,
or a headline "you are running at 55% of real-time speed" signal. (Same family
as X1 — level-blindness vs variance-blindness.)

### X3 [HONESTY] Stall KPIs headline alt-tab suspends as stalls
Summary STALLS card: "biggest 122228ms · average 122228ms" — a 122s window
unfocus counted as the session's headline stall. The A5 fix stopped *blaming
mods* for suspends but the KPI aggregation still counts them. Fix: cause-aware
KPI split — real stalls (freeze/GC/UI-blocking) headline; ProcessSuspended /
WorldLoad shown separately ("time paused: 2m 2s") or excluded with a sub-line.

### X4 [UX] Early-session degenerate states across every tab
At minute 2-3 of a session: cost stream renders as empty grey slabs (1 sample),
minute-by-minute strip is a single square, Observatory declares 97% of the
modlist "dormant" and Calamity "100.0% usage", Lag is a wall of six empty
panels, Timeline is one giant uniform block. Every surface computes on whatever
exists and renders judgement-weight output from seconds of data. Fix pattern: a
session-age gate — surfaces that need N minutes render a "warming up · 2m of
data" state instead of confident-looking degenerate output. (Persistence already
has the thin-session gate; the live UI needs its sibling.)

### X5 [BUG] Null vs zero inconsistency in tables
Observatory MOD DETAIL "USED / COUNTED": items 0, projectiles —, mounts —.
Memory ALLOC/S: ThoriumRework 0 B, Combinations —. Same semantic (no data vs
measured zero) rendered inconsistently across tabs, and the reader can't tell
which is which. Fix: one convention — measured zero renders 0, not-instrumented
renders — with a tooltip.

### X6 [POLISH] Footstrip says "1-6 to switch tabs"; there are 7 tabs
Stale since Observatory split out (v0.24). The keyboard hint also implies 6 is
Memory when it's Self.

### X7 [HONESTY/DATA] 10 "modlists seen" in 11 sessions — fingerprint fragility
Self tab, cross-session panel. Nearly every session registered a different
modlist, almost certainly because the fingerprint includes mod versions and the
dev loop bumps PerformanceProfiler's own version every build. Consequence: the
cross-modpack insights ("BannerCollector costs 60× more in one of your 2
modlists") are built on fragmented single-session "modlists", so lifetime
comparisons never accumulate depth. Fix candidates: exclude the profiler itself
from the fingerprint (it already self-excludes from rollups), and/or key the
fingerprint on InternalName-set rather than name+version.

### X8 [BUG] `reset db` chip wraps to its own line in the topbar (H1, ledgered)

---

## Per-tab findings

### Summary
| # | Sev | Finding |
|---|---|---|
| S1 | BUG | PER-MOD COST STREAM renders as three full-width grey slabs with 1 sample ("last 1 sample (~5s each)") — degenerate stream render; needs a warming state until ≥2 buckets |
| S2 | HONESTY | STALLS card = X3 (122s suspend headline) |
| S3 | UX | Frame chart's corner numbers "5.19" / "54.9" are unlabelled (they're the y-range); label as "min / max ms" or move into the axis |
| S4 | UX | SESSION TREND rows (frame/gc/spikes) have no y-scale at all; comparative shape only — fine, but the spikes row renders a single orange tick that reads as noise |
| S5 | UX | SESSION TIMEFRAME · MINUTE BY MINUTE is the seed of the session-gradient idea (per-minute colour buckets already exist + legend). It's the LAST panel and invisible early; candidate to become the Timeline strip (see T1) |

### Timeline
| # | Sev | Finding |
|---|---|---|
| T1 | UX | The context strip is a single uniform olive block: no time axis, no tick marks, no direction cue. The per-minute heat data (Summary S5) should drive it as a session-long gradient ribbon — the user's exact proposal, and the data already exists in the heatmap endpoint |
| T2 | BUG | Strip caption "26.6 ▉ 26.6 ms/t" prints the same value twice (legend min/max collapsed when session is young); "spike min / stall min" name series that render nothing |
| T3 | BUG | Events row: single right-pinned chip clipped mid-word ("change (open) → Forest · Ma"). Layout dumps newest event at the right edge with overflow hidden; needs left-flow layout + text-overflow ellipsis + tooltip |
| T4 | BUG | Biome swimlane bar runs the full container width and its right edge clips past the panel — width calc suspect (open segment rendered as 100% regardless of elapsed session share?) |
| T5 | UX | Four "none this session" lanes consume full row height each (~half the swimlane panel); collapse empties to one compact line |
| T6 | UX | "no deaths this session" floats as bare centred text between panels, not in any panel frame |
| T7 | UX | ~300px of dead black space below CHRONICLE |
| T8 | POLISH | Chronicle double-entry at the same second ("joined session" + "session opened: …") both tagged `join`; distinct tags would read better |

### Lag
| # | Sev | Finding |
|---|---|---|
| L1 | HONESTY | = X2. The whole tab reads "all clear" during objective 2×-budget slowness |
| L2 | UX | Seven stacked panels, all empty, full height each — the empty-state wall. Collapse empties into a single "collecting: 0 events, 0 clusters…" summary row until data exists |

### Observatory
| # | Sev | Finding |
|---|---|---|
| O1 | UX | Every row's sub-line "0 items · 0 npcs · 0 buffs" is *used-content* counts but reads as roster ("Calamity has 0 items"). Label them ("0 items used") or drop until nonzero |
| O2 | HONESTY | "100.0% usage" (Calamity) means "100% of the 184 usage-ticks observed so far", not "fully used". Early-session denominator presented as a confident share = X4 instance; needs the age badge |
| O3 | UX | AT A GLANCE "ACTIVE 3% / DORMANT 97%" at minute 3 is a lifetime-toned judgement on seconds of data (X4) |
| O4 | BUG | USED/COUNTED column: 0 vs — inconsistency (X5) |

### Insights
| # | Sev | Finding |
|---|---|---|
| I1 | HONESTY | FRAME HEADROOM = X1 (false "you sustain 60 fps" during slow-mo) |
| I2 | UX | Kanban column titles truncate without tooltip ("CROSS MODPACK COST D…", "COSTLY DESPITE LOW USA…") |
| I3 | UX | Last column clips off-viewport with no visible horizontal-scroll affordance |
| I4 | UX | 27 of 34 findings are LOW and the low columns dominate the fold; a confidence-weighted default sort (or collapsed LOW group) would surface the 7 MEDIUM first |

### Self
| # | Sev | Finding |
|---|---|---|
| SE1 | HONESTY | Gauge says "1.13× healthy" while bytes/hook grew 30 → 40.5 KB via reload stacking. The 36 KB baseline absorbs real growth; SelfHealth needs the growth axis (H3) — a per-session install-delta history line would have shown the 1.82 → 2.46 GB climb |
| SE2 | UX | No memory-over-time anywhere on the tab (the agreed memory-trend feature's home) |
| SE3 | DATA | probe calls/tick reads 24,227 while the game is PAUSED — draw-phase hooks firing every rendered frame with zero update ticks. Confirms significant draw-side probe activity that cost attribution can't segment (the loop-anatomy slot) |
| SE4 | POLISH | "MODLISTS SEEN 10" = X7 |

### Memory
| # | Sev | Finding |
|---|---|---|
| M1 | GOOD | PerformanceProfiler honestly listed as #1 RAM consumer (2.42 GB, above Calamity) — invariant compliance worth keeping a test on |
| M2 | BUG | PER MOD table cuts mid-row at the fold (SilkyUIFramework half-rendered) — container height/overflow suspect |
| M3 | UX | Stacked-bar legend truncates its own mod name ("PerformanceProfil… 2.42 GB") |
| M4 | BUG | ALLOC/S 0 B vs — inconsistency (X5) |
| M5 | UX | No trend-over-time; a working-set sparkline belongs here and on Self (agreed feature) |

---

## Follow-up mechanics

1. Corrected DOM sweep selectors: panes are `.tab-pane`, panel headers `.panel-h`,
   bodies `.panel-body`. Re-run the overflow sweep with those + exercise drill
   states (click a swimlane block, open a drawer, select a memory slice).
2. The L8 agent-audit harness (`audit.py capture` → fan-out) can absorb X-items
   as rubric rows so regressions get caught mechanically.
3. Ship order proposal: X1/X2/X3 (honesty, pairs with H4) → S1/T3/T4/M2 (broken
   renders) → X4 empty-state pattern + T1 gradient ribbon (one design, many
   surfaces) → polish (X5/X6/T5-T8/I2-I4/M3).

---

## Closure map (2026-07-07 evening)

| Rows | Status | Commit |
|---|---|---|
| X1, X2, X3, X7 | fixed (measurement) | 448f447 |
| X5, X6, X8, S1, T2, T3, M3 | fixed | c1cf962 |
| T4, M2 | re-diagnosed (honest bars / scroll-region existed); T4 got the ongoing-edge treatment | c1cf962 |
| X4, L2, T5, T6*, T7*, T8, O1, O2, O3, I2, I3, I4, S3, SE1, SE2 | fixed (T6/T7 folded into panel work) | fb2d061, 0f9e844 |
| S5 | superseded by the ribbon axis + minute drill | fb2d061 |
| M5 | guard data live; the area-chart visual carries to the next UI wave | 0f9e844 |
