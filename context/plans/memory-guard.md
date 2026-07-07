# Plan — Memory Guard (S04, first slice)

> **Status: EXECUTED (0f9e844, 0.33.0). Deviation, recorded: the reload-stack feed-insight entry deferred to S13 (one-shot load-time findings don't fit the re-emitting detector model); its surfaces are the Self-tab arm table + the client.log WARN.**

> Slot: atlas S04 (memory ownership engine) — this is the trend/growth/verdict
> slice, not per-mod heap ownership. Closes H3 (SelfHealth growth-blindness)
> and instruments the open leak question. Version target: 0.33.0.

## Why (the live case)

2026-07-07 evidence: working set walked 4.2 → 10.4 GB across a play session
containing two mod reloads; install delta grew 1.82 → 2.46 GB and bytes/hook
30 → 40.5 KB at constant 62,203 hooks (reload-stack signature); the Self tab
read "1.13× healthy" throughout because SelfHealth judges only the
install-time delta (H3); the leak-vs-reload-stacking question is open pending
a clean-baseline run. The mod must be able to answer this itself.

## Design

### Sampler (off the hot path)

`Data/Collectors/MemoryTrendCollector.cs`: samples every `MemorySampleSeconds`
(config, default 5 s) from the existing insights-cadence worker path (NOT the
tick path — `Process.Refresh()` is an OS call):

- `workingSetBytes` (Process), `managedBytes` (GC.GetTotalMemory(false)),
  `gen2Count`, `unixMs`.
- Ring capacity 1440 samples (2 h at 5 s). 1440 × 32 B ≈ 46 KB — negligible.

### Growth stat

`Data/Stats/MemoryTrendStat.cs` derives per snapshot:

- `GrowthMbPerMin10` — least-squares slope over the trailing 10 min (robust to
  single GC dips vs first/last diff);
- `GrowthMbPerMin60` — the hour view;
- `PeakWorkingSetMb`, `SessionStartWorkingSetMb`;
- `Phase` classification, descriptive not diagnostic: `warming` (< 10 min
  data), `flat` (|slope| < 5 MB/min), `growing` (5–20), `climbing` (> 20),
  `reclaimed` (negative after positive).

### SelfHealth escalation (H3 closed)

`ProfilerSelfHealth` gains the growth axis: severity escalates to `Watch` at
sustained `growing` (10 min), `Degraded` at `climbing` (10 min), independent of
the install-delta axis. The Self gauge subtitle names which axis drove the
verdict ("healthy install · climbing +34 MB/min").

### Reload-stack detection

Install deltas are already measured per arm; persist them:
`SelfHealthStat` history row per world-arm (`installDeltaBytes`, `bytesPerHook`,
`hookCount`, `armIndex`, session id) into LiteDB. A cross-arm comparator flags
the signature seen live (Δdelta > 20% at equal hook count within one process
lifetime) and surfaces it as a descriptive insight: "this game process has
reloaded mods N times; the profiler's install footprint grew X → Y MB across
reloads — a full game restart reclaims it." (That sentence is the answer the
user needed this session.)

### Surfaces

| Surface | Content |
|---|---|
| Self tab | working-set sparkline (session), growth badge, per-arm install-delta history mini-table |
| Memory tab | full trend strip (WS + managed area chart over session) above the per-mod table |
| `/api/self` | `growthMbPerMin10`, `phase`, `armHistory[]` |
| `/api/memory` | `trend` series (downsampled ≤ 240 points) |
| Insights | the reload-stack insight + `climbing` phase insight (descriptive, THIS SESSION) |
| `client.log` | one line per phase transition (agent surface) |

### Config

`MemoryGuard` toggle + `MemorySampleSeconds` slider (already specified in
feature-settings plan). OFF ⇒ collector never schedules; surfaces render the
disabled state.

## Test plan

- Synthetic sample streams ⇒ slope math pins (flat/growing/climbing/reclaimed
  fixtures, GC-dip robustness: one 500 MB dip must not flip `flat`).
- Warming gate: < 10 min data never classifies beyond `warming`.
- Reload-stack comparator: synthetic arm history reproducing the live 1.82→2.46
  GB case fires the insight; equal deltas don't.
- Harness scenario `memory-climbing`: Memory/Self tabs render trend + badge
  (screenshot + DOM assert).

## Acceptance

1. The 2026-07-07 session, replayed synthetically, produces: growth badge
   visible by minute 10, reload-stack insight after arm #2, SelfHealth ≥ Watch
   — instead of the "Healthy at 10.3 GB" that actually happened.
2. Zero hot-path cost (sampler runs on the worker cadence; test asserts no
   per-tick allocation or Process.Refresh call).
3. Guard OFF ⇒ no sampler, surfaces show disabled state, everything else
   unchanged (modularity check).
