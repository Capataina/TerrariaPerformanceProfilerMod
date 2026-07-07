# Plan — E2E Testing Framework (S27): catch bugs without launching the game

> **Status: EXECUTED rings 1-2 + runner (448f447, f15303a). Ring-3 scenario-contract emission deferred with reason (own pass); the harness's existing fixtures cover the UI surface. run_all.sh green end-to-end 2026-07-07 (2f2fc1c).**

> Slot: atlas S27. Version target: tooling (no shipped version bump; test csproj
> + tools/ only). The user's directive: "I won't have the game open while you
> are doing the development… implement a full end to end testing framework…
> so that we don't have to reload the game and everything to test the bugs."

## The gap it closes (mined from this session + git history)

Bug classes that shipped past the existing gates, and the gate that would have
caught each:

| Bug (real, from history) | Class | Missing gate |
|---|---|---|
| "Dashboard says 60 fps during slow-mo" (A1/X1) | metric semantics vs felt reality | scenario simulation: a slow-mo *session* with assertions on every player-facing number |
| FRAME HEADROOM fires during slow-mo (X1) | detector reads wrong field | same scenario + insight assertions |
| Lag tab all-zero at 2× budget (X2) | level-blind model | same |
| LiteDB `FindOne(x => x.F == arr[0].F)` TargetException (C1, twice) | expression-tree translation | LiteDB round-trip tests exercising every predicate shape against a real temp DB |
| UTC DateTime kind 500s `/api/now` (v0.27 era) | serialisation edge | API-contract round-trips on archived paths |
| EventJournal / DbWriter shutdown races (C3, H2) | teardown ordering | deterministic teardown harness: arm → run → unload under load, races assert clean |
| Denormal 2.34e-284 garbage (C2) | float decay | synthetic long-idle stream pins |
| 122s alt-tab headlining stalls (X3) | cause aggregation | suspend-pattern scenario |
| Grey-slab cost stream at 1 sample (S1) | degenerate-input rendering | fixture scenario `warming` + DOM asserts |
| Clipped chip / wrapped reset button (T3/X8) | CSS overflow | mechanical overflow sweep (selectors now known) |

## Architecture: three rings + one runner

### Ring 1 — Scenario simulation (C#, the new core)

`Tests/Simulation/` — drives the REAL pipeline (MetricCollector → detectors →
stats → aggregators → router JSON) with scripted sessions, no game:

- `SessionScript`: declarative tick stream — per-tick (computeMs, realMs,
  gcMs, allocBytes, per-mod probe samples, focus flag), plus world events
  (suspend gaps, world-load, reload-arm markers).
- `ScenarioRunner`: feeds the script through `BeginTick`/probe records/
  `EndTick` exactly as ProfilerSystem would, then materialises every stat
  snapshot and every `DashboardRouter.Build*` JSON (they're internal static —
  `InternalsVisibleTo` the test assembly).
- `Scenarios.cs`: the canonical library, mirrored by the UI fixture generator
  (same names, same shapes): `healthy60`, `slowmo30` (compute 4ms/real 33ms),
  `frameskipOn` (update 60, render 40), `spiky`, `gcStorm`, `altTabbed`,
  `warming` (first 2 min), `slowBleed` (memory climb), `reloadStacked`,
  `configMinimal`.
- Assertions read like the honesty contract: *"in `slowmo30`, avgFps < 40 AND
  no insight contains 'sustain 60' AND lag headline realtimeSpeed < 0.6 AND
  heatmap worst ≥ 30ms"*.

### Ring 2 — Persistence & teardown (C#)

- LiteDB round-trips against a temp-file DB: every collection's write + read
  path, every predicate *shape* used anywhere (captured-local, indexer-hoisted,
  string-key) — the C1 class becomes uncompilable-without-a-test.
- Teardown determinism: arm → heavy write load → `OnWorldUnload` mid-batch ×
  N seeds; asserts no ObjectDisposed/NRE (the H2/C3 class), journal + writer
  drain cleanly.
- Store lifecycle: rebuild-rollup idempotence, backup ring bound, reset scopes.

### Ring 3 — UI against mimicked live data (Python/Playwright, existing harness grown)

- `tools/testing/pp_testing/scenarios.py` gains the same scenario names,
  generating fixture JSON from the same shapes (a `scenario-contract.json`
  emitted by Ring 1 keeps the two in lockstep — C# writes it, Python reads it;
  drift fails the harness).
- Corrected DOM sweep (`.tab-pane`, `panel-h/panel-body`) as an `assert` rule:
  zero unintended `scrollWidth > clientWidth` (T3/X8 class), allowlist for
  intentional scrollers.
- Interaction states (the audit's blind spot): drawer open, popup card open,
  swimlane drill, memory slice select, kanban horizontal scroll — per scenario.
- Screenshot suite per scenario per tab → `audit.py` L8 flow unchanged.

### The runner

`tools/testing/run_all.sh`: `dotnet test` (rings 1-2) → `audit.py contract` →
`audit.py assert` (ring 3) → exit non-zero on any failure. One command = the
whole no-game gate. CI-shaped even though there's no CI yet.

## Work plan

1. `InternalsVisibleTo` + SessionScript/ScenarioRunner + `healthy60`/`slowmo30`
   (the X1 pin lands first — it's the class that hurt today).
2. Scenario library + honesty assertions battery.
3. LiteDB + teardown rings.
4. scenario-contract emission + Python consumption + corrected sweep +
   interaction states.
5. run_all.sh + README (tools/testing) update.

## Acceptance

1. `run_all.sh` green from a cold checkout with the game closed.
2. Reverting the 0.28.1 KPI repoint makes `slowmo30` fail (mutation-check the
   pin actually bites).
3. Reintroducing an indexer-in-predicate LiteDB call fails Ring 2.
4. The harness sweep flags a deliberately-clipped test element (sweep bites).
5. Every scenario has: C# assertions + fixture + per-tab screenshots.
