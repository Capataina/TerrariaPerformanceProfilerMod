# Code Health Audit — Potential Issues (2026-06-25)

Suspicions grounded in concrete code reading that did **not** meet the certain-free bar — either not-free (behaviour-changing / public-shape) or resolvable only with the implementing engineer's domain knowledge / out-of-process state. Separate bar from `findings.md`. Supersedes the 2026-06-22 list.

---

### 1. Delegate-path `HookProbe` per-hook object retention at scale

**Locations:** `Profiling/HookInterceptor.cs` (the per-signature `HookProbe` instances).
**Observation:** the delegate-pair backend allocates one small `HookProbe`-family object per hooked method. On a 150k-hook kitchen-sink stack the dormant delegate/Parallel path would retain ~150k small objects — a second RAM source the `install-ram-optimisation.md` (IL/Cecil-focused) analysis never covered.
**Reasoning:** IL is the default backend, so this is dormant today; but Parallel mode runs both, and a delegate-only fallback would surface it.
**Suggested investigation:** measure managed-heap delta on a large stack in delegate + Parallel mode vs IL-only; if material, consider a flyweight/struct probe.
**Why not a certain finding:** not free (a probe redesign), and the cost only bites in a non-default backend — needs the engineer's call on whether Parallel-at-scale is a real scenario.

### 2. `TrimRetainedScaffolding` silently no-ops on MonoMod version drift

**Locations:** `Profiling/ILHookInterceptor.cs` (`TrimRetainedScaffolding`, reflection into `ILHookEntry.LastContext`).
**Observation:** the RAM reclaim (~3.7→1.0 GB) is now load-bearing for the 32 GB target, but it reaches MonoMod internals by reflection; a host update that renames those internals makes it silently no-op (Invariant-4-guarded — a `client.log` Info line, no crash) and RAM reverts with no player-visible signal.
**Reasoning:** safe-by-construction, but the silent regression is the risk given the trim is now relied upon.
**Suggested investigation:** add a surfaced signal (Self-tab warning) when the trim path detects a signature mismatch — a feature decision, not free.
**Why not a certain finding:** the fix is a new surfacing feature (out of scope); flagging the load-bearing reflection invariant is the in-scope action.

### 3. `RunningStat.Without` catastrophic cancellation → dishonest confidence (HIGH — routed to findings, restated here for the engineer)

**Locations:** `Insights/Shared/Stats.cs:67` — `Without(in RunningStat subset)`.
**Observation:** the reverse-merge (`Without`) subtracts a subset's `(Count, Mean, M2)` to recover the complement; on near-equal large means with a small subset this is catastrophic cancellation → a degenerate/negative variance → a spuriously small p-value → a dishonest Medium/High confidence badge (Invariant 3).
**Reasoning:** research-confirmed (Schubert SSDBM18: the deletion case is the dangerous one); the existing `ReferenceFrameTests.RunningStat_Without_RecoversTheComplement` uses well-separated integers and does NOT exercise the cancellation regime.
**Suggested investigation / fix:** guard the complement (floor variance at 0; or require a minimum complement count before trusting the recovered variance) — this is a **correctness fix** filed as a Known-Issue in `insights.md`; it changes p-values (for the better), so it is NOT a zero-behaviour-change free finding. Implementation writes a cancellation-regime test that asserts the post-fix stable variance.
**Why here too:** the *fix* is behaviour-changing (more honest output), so it cannot be presented as a free upgrade; it is a deliberate correctness change.

### 4. KPI `IsEmpty` sentinel diverges from the universal `Empty` convention

**Locations:** `Data/Stats/KpiStat.cs` (`IsEmpty` flag) vs the `Empty` static sentinel every other snapshot exposes.
**Observation:** KPI uses an `IsEmpty` boolean where peers use a shared `Empty` default; the inconsistency is the strongest consistency signal in the data layer but fixing it is a public-shape change.
**Suggested investigation:** grep KPI consumers (dashboard + tests); if uniform, migrate to the `Empty` sentinel.
**Why not a certain finding:** public-shape change → not zero-behaviour by construction; recommended default is leave-as-is unless the engineer wants the consistency pass.

### 5. Five stats declare a local `public const StreamName` instead of the central `RolloutStreamNames`

**Locations:** five `Data/Stats/*` + `Insights/Publish/*` files.
**Observation:** convention §16 says streams are looked up by the central name constant; five stats re-declare the name locally.
**Why not a certain finding:** consolidating touches the public surface of each stat; needs a consumer grep + the engineer's call. Recommended default: leave unless doing a consistency pass.

### 6. `Baseline.AllocBytesPerTickMedian` is named "Median" but computes an EMA

**Locations:** `Data/Stats/Baseline.cs`.
**Observation:** the field/accessor name says Median; the computation is an exponential moving average.
**Suggested investigation:** the doc-comment correction is free (filed as doc-rot); the *rename* has blast radius (consumers + any persisted name) → not free.
**Why not a certain finding:** the rename is behaviour-adjacent (serialised names / consumers); only the comment fix is free.

### 7. Web UI semantic-colour collisions + per-render rebind (subsumed by CC-1)

**Locations:** `Js.Timeline.cs` (death-chip `buff-on→green` vs the perf-ramp green), `Js.Insights.cs` (`sortableHead` per-render `setTimeout` re-bind), `Js.Lag.cs` (`lagApplySort` per-render sort).
**Observation:** the green-means-two-things collision is a readability ambiguity, not a bug; the per-render rebind/sort are subsumed by adopting `renderIfChanged` (CC-1).
**Why not a certain finding:** the colour collision is a design-judgement call (the engineer decides if the two greens must differ); the rebind/sort dissolve once CC-1 lands, so they are not independent findings.

### 8. `ContextBaselines` writes bypass the single writer thread

**Locations:** `Profiling/ProfilerSystem.cs:402` — `CrossSessionStore.Save` on a background `Task`.
**Observation:** the documented invariant is "all DB writes go through the single `DbWriterThread`"; the cross-session baseline save runs on its own background task instead.
**Reasoning:** it is off the game thread (so not a hot-path block) and fires at world-unload, but it is an architectural exception to the writer-thread contract.
**Suggested investigation:** confirm LiteDB tolerates the concurrent writer (it is the same DB file); if not, route through the writer-thread queue.
**Why not a certain finding:** needs the engineer's knowledge of whether the two writers ever overlap in practice; routing it through the queue is a behaviour-adjacent change.
