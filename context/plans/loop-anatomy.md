# Plan — Loop-Anatomy Attribution (S01): per-mod update vs draw split

> Slot: atlas S01. Version target: 0.31.0.
> Evidence base: 24,227 probe calls/tick observed while the game was PAUSED
> (2026-07-07 live capture) — zero update ticks, all draw-phase hook traffic,
> none of it attributable today. The slow-motion mystery lived in the draw side.

## Why

Every hook sample lands in one per-mod bucket, so "CalamityMod 7.4 ms/t" cannot
say whether that cost blocks the update (gameplay speed) or the draw (render
cadence). The two have different player consequences (slow-motion vs dropped
frames) and different remedies (frameskip trades one for the other — proven in
this session's A/B). The profiler must attribute cost to the loop phase.

## Design

### Phase signal (zero new instrumentation)

The game loop is single-threaded and the collector already knows the phase:

```
BeginTick (PreUpdateEntities)  ──> phase = Update
EndTick   (PostUpdateEverything) ─> phase = Draw      (everything after the
                                     update window until the next BeginTick is
                                     draw + present + off-tick work)
```

One static `ProbeStack.CurrentPhaseIsUpdate` bool, set `true` in
`MetricCollector.BeginTick`, `false` at the end of `EndTick`. Same-thread
write/read (Terraria's loop), no volatile needed; a comment records that claim
and its basis. Menu/paused frames (no tick open) correctly read as Draw.

### Accumulation (the only hot-path change)

The per-hook accumulator gains a second lane. Today: one `double[hookCount]`
raw-ms array per backend. After: `updateMs[]` + `drawMs[]`; `Exit` writes to the
lane chosen by the phase bool at *Enter* time (phase captured in the probe
frame, so a hook spanning EndTick attributes to where it started — rare and
bounded by one frame).

Cost: one bool read per Enter, lane select per Exit, +0.5 MB at 62k hooks
(one extra double array). **Invariant 2 gate: measure `harvestMsEma` and a
synthetic 62k-hook bench before/after; the change must disappear into noise
(< 0.05 ms/t delta).**

### Harvest & stats

- `MetricCollector` per-mod fold produces `updateMsSmoothed` + `drawMsSmoothed`
  per mod (EMA α = PerModSmoothing, denormal-flushed like the existing folds).
  Total stays = update + draw (existing consumers unchanged).
- `ModCostSnapshot` (or the existing per-mod stat record) gains the two fields;
  `SelfHealthStat` gains `probeCallsUpdate` / `probeCallsDraw` per tick.

### Surfaces

| Surface | Change |
|---|---|
| Observatory per-mod rows | cost bar becomes a two-segment stacked bar (update ▮ draw ▯) with shares in the drawer |
| Mod detail drawer | "update 3.1 ms · draw 4.2 ms (58% draw-bound)" line |
| Summary impact donut | unchanged (total); side legend gains a `draw-bound` glyph for mods whose draw share > 60% |
| Self tab | probe calls split by phase (the 24k-while-paused number becomes legible) |
| `/api/mods`, `/api/mod-observatory` | `updateMs`, `drawMs` fields |
| Insights | new descriptive pattern: "X is draw-bound (72% of its cost is in the draw phase) — its cost shows in render cadence, not game speed" — the exact sentence that would have solved this session's mystery in one read |

### Config (S23 dependency)

Registers in ModConfig under Heavy-CPU: `PhaseSplitAttribution` toggle (default
ON — heaviest-default rule). OFF folds both lanes into update (pre-plan
behaviour), keeping the seam clean.

## Work plan

1. `ProbeStack`: phase bool + frame-capture; both `Enter` overloads.
2. Backend accumulators: second lane (delegate + ILHook backends share the
   accumulator type — verify at implementation).
3. `MetricCollector`: set/clear phase; harvest folds both lanes; new EMAs;
   probe-call phase counters.
4. Stats + API + JS surfaces per the table.
5. Synthetic bench test + phase-attribution unit tests (scripted probe streams
   with known phase patterns ⇒ exact expected splits).
6. Measure, record numbers in the commit, bump 0.31.0.

## Acceptance

1. Paused-game fixture: 100% of probe calls attribute to draw; update ms ≈ 0.
2. Synthetic update-only stream: draw lane exactly 0.
3. Overhead delta < 0.05 ms/t on the 62k-hook synthetic bench, recorded.
4. Observatory fixture screenshot shows split bars; drawer shows the shares.
5. The draw-bound insight fires on a synthetic draw-heavy mod and its copy is
   descriptive (Invariant 3 review).
