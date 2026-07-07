# Findings — Post-Rework Focused Sweep (2026-07-07)

The headline result is a **clean bill of health**. This session landed a large measurement-
honesty + RAM + correctness rework; this sweep verified it introduced no dead code, orphans,
unused members, or per-tick allocations, and it re-verified that the 2026-06-25 audit's free
findings backlog has since been fully implemented. There are **no open free findings to apply**
— the value here is the verification, recorded so a future reader knows the ground was covered.

---

### F1 — B1 rework left no orphaned code [Dead Code · verified-clean]

**Claim checked:** the B1 removal of `_perHookHistoryMs`/`_perHookRollingMs` (and bytes twins)
might have orphaned `UpdateRollingAverage`, `historyCapacity`, or `_perModHistory*`.

**Evidence:** `grep` shows `UpdateRollingAverage` still called for the per-mod path
(`MetricCollector.cs:463,497`); `historyCapacity`/`_historyCapacity` still used by the per-mod
history ring, the `RingBuffer`, and the `_sampleSlot` wrap. `dotnet msbuild` emits **zero**
CS0169/0414/0219 unused-member warnings. **Verdict: no orphans; B1 correctly kept the per-mod
windowed mean while removing only the giant per-hook rings.** No action.

### F2 — No unused members introduced by the A/C rework [Dead Code · verified-clean]

**Evidence:** compiler warning scan (CS0169 unused field, CS0414 assigned-not-read, CS0219
unused local) is empty across the mod. The new members (`_prevBeginTimestamp`,
`_realFramePeriodMs`, `ProbeStack._callCount`, `HarvestMsEma`, `ProbeCallsPerTickEma`,
`DenormalFloor`) are all read. `SumAll` still used by the parallel-backend path. **Verdict: clean.** No action.

### F3 — Hot path is allocation-clean (Invariant 2) [Performance / Data Layout · verified-clean]

**Evidence:** the only `new` in the per-tick path are stack-only `Span<double>` and
`Vector<double>` (both `struct` / `ref struct`, zero heap) inside `UpdateRollingAverage`'s SIMD
loop; every other `new` is one-time construction. `ProbeStack.Enter` adds one non-atomic
`long` increment (allocation-free). The C2 denormal flush is a one-sided compare, no alloc.
**Verdict: the rework preserved the zero-allocation hot-path invariant.** No action.

### F4 — Install-RAM residual correctly left in place [Known Risk · verified, documented]

**Evidence + research:** the B4 diagnostic confirms `TrimRetainedScaffolding` disposes the
ILContext bodies; the residual is `SourceCloneIl`. The tModLoader MonoMod wiki
(<https://github.com/tModLoader/tModLoader/wiki/Patching-Other-Mods-Using-MonoMod>) confirms two
mods hooking the same method risk incompatibility — so the clone is required for re-chain safety.
**Verdict: removing it would violate Invariant 4; correctly retained + now measured.** No action.

### F5 — LiteDB indexer-in-predicate crash class fully swept [Correctness · fixed this session]

**Evidence:** the C1/C3 work fixed both instances (`ProfilerSystem.cs:334`,
`DashboardRouter.History.cs:112`) and a repo-wide grep of `.Find/.FindOne/.Delete/.Exists`
predicates found only simple captured-local predicates elsewhere — **no third instance.**
**Verdict: class closed.** (Applied this session, not an open finding.)

### F6 — All modularisation candidates: leave-as-is / not-applicable [Modularisation]

Per the Pass-1 candidate table: 12 cohesive production files → `leave-as-is`, 2 archived `UI/`
files → `not-applicable`, **0 `split-recommended`** — consistent with the 2026-06-25 verdict.

---

## Prior-audit backlog status (2026-06-25 → now): implemented

Spot-checked the 2026-06-25 Priority Actions; **every free finding is already in the code**:

| Prior finding (free) | Status | Evidence |
|----------------------|--------|----------|
| Cache `Stopwatch.Frequency` reciprocal | ✅ done | `TicksToMs` cached in `PerModAttribution` + `MetricCollector` |
| Devirtualise per-tick `IReadOnlyList<double>` fold | ✅ done | `MetricCollector.PerModCategoryRawMsArray` (internal `double[]`) |
| Adopt `renderIfChanged` on heavy poll panels | ✅ done | 10+ call sites in `Js.Self/Memory/Observatory` |
| Remove dead `ProbeStack.CurrentDepth` / `SelfHealth.Reset()` / `MetricCollector.StallDetectorRef` | ✅ done | all grep to zero |
| Delete dead `Baseline._periodMadHist` | ✅ done | gone (`_frameMadHist` remains, live) |
| `405` reason-phrase | ✅ done | `DashboardHttpServer.cs:301` "Method Not Allowed" |
| Clamp Pearson `r` to `[-1,1]` | ✅ done | `ModInteractionAggregator.cs:220` `Math.Clamp`, comment cites "CHA data-pipeline finding" |
| Guard negative `FreedBytes` | ✅ done | `AllocationCausalityStat.cs:122` `Math.Abs`, comment cites GcPressureStat |
| `EventJournal.AppendBatch` double-buffer | ✅ done | `SerializeToUtf8Bytes` (no StringBuilder round-trip) |

The one behaviour-changing item (the `RunningStat.Without` catastrophic-cancellation guard) is
outside the free-fix scope and was not applied blind this run.
