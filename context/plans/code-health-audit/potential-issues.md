# Potential Issues — Post-Rework Focused Sweep (2026-07-07)

Supersedes the 2026-06-25 potential-issues for this run's scope. One suspicion surfaced.

### 1. Per-hook harvest could fuse into one array walk

**Locations to inspect:**
- `Profiling/MetricCollector.cs:464-476` — `HarvestHooksInto(_perHookRawMs)` then the smoothing/average EMA fold that re-reads `_perHookRawMs`.
- `Data/Aggregators/PerModAttribution.cs:343-357` — `HarvestHooksInto` (the source walk).

**Observation:** after B1, the per-hook path does two full walks of a 62k-element array per tick:
`HarvestHooksInto` writes `_perHookRawMs[i] = hookTicks[i] * TicksToMs`, then the fold re-reads
`_perHookRawMs[i]` to update the two EMAs. `_perHookRawMs` is internal-only (grep confirms no
external consumer), so the two walks could fuse into one — a `FoldHooksInto(smoothed, average,
fastAlpha, slowAlpha, floor)` on `PerModAttribution` that reads `hookTicks` directly and updates
the EMAs, eliminating the intermediate array and one 62k walk.

**Reasoning:** provably free by construction (same `ticks * TicksToMs` value, same EMA math, same
order → bit-identical output), and a small hot-path win (~498 KB array + one 62k walk/tick removed).

**Suggested investigation:** add a benchmark in `Tests/` timing the two-walk vs fused path over a
62k-hook synthetic accumulator; if the fused path measures faster with identical output, implement
it. Confirm `_perHookRawMs` truly has no consumer first (it currently does not).

**Why not a certain finding + not applied this run:** it adds a new `PerModAttribution` method
(borderline against "no new API for a one-time pattern") and, more importantly, it is a *fourth*
hot-path change on top of a session that already reworked this exact loop heavily (A1/A2/B1/C2).
The right sequence is: let the current rework be play-test-verified stable first, then fuse with a
benchmark proving the win — not stack another unmeasured change onto an unverified hot path. Held
for a measured follow-up, deliberately, not dropped.
