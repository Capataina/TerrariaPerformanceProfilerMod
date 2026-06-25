# Pass 2 — Systems Audited (static snapshot, 2026-06-25)

Final state of the Pass-2 deep dive. The live ledger is `obligation-evidence-map.md`; this is the static per-system snapshot + the modularisation-floor verdicts + the data-layout applicability table. Supersedes the 2026-06-22 snapshot.

## Per-system rows

| Cluster | Research (modes) | Diagnostic tests | Findings | Confidence |
|---|---|---|---|---|
| 1. Hook instrumentation + self-health | 4 searches (modes 1,1,2,3) | none in audit phase — files tModLoader-dependent (not linkable) → reasoned omission; findings grep+read high-confidence | 14 (4H/6M/4L) | high (grep-confirmed dead code + read) |
| 2. Metric collection + detectors | 2 searches (modes 2,3) | equivalence pins land with fixes (PerModAttribution/Baseline linked) | 12 (1H/6M/5L) | high (ULP reasoning + grep) |
| 3. Data pipeline + segments | 2 searches (modes 2,3) | devirt identical-by-construction; clamp/promoter pins land with fixes | 21 (3H/2M/16 L-info) | high (3 disproved non-findings recorded) |
| 4. Insights engine | 3 searches (modes 1,2,3) | Without-cancellation pin lands with its fix (Stats linked) | 13 (1H/…) | high (research-grounded numerical analysis) |
| 5. Web server + persistence | 3 searches (modes 1,2,3) | round-trip/wire-shape flagged; land with serialisation touches | 9 | high (routers verified clean by reading) |
| 6. Web UI assets + cross-cutting | 2 searches (modes 1,3) | no xUnit surface (verbatim-string JS) → verified via L4 harness post-implementation | 19 (4H/7M/8L) | high (grep + L4) |

**Total: 88 findings.** Plus consolidated potential issues in `potential-issues.md`.

## Modularisation floor — verdict for every Pass-1 candidate

| File | LOC | Verdict | Justification |
|---|---|---|---|
| `Profiling/HookInterceptor.cs` | 1227 | **leave-as-is** | One backend; LOC is the irreducible 1-delegate-pair + 1-probe-per-signature fan-out the design frames as "three edits, one file". Free win is trimming the dead read-surface (~25 lines), not splitting. |
| `Web/Assets/Js/Js.Timeline.cs` | 808 | **leave-as-is** | One tab / one data stream; the per-tab verbatim-string-fragment convention (conventions §20) is the split unit; sub-views coupled by shared filter/selection/sig state. |
| `UI/Overlay/Tabs/OverviewTab.cs` | 786 | **not-applicable** | Archived overlay, compiled-out (not in the player path). |
| `Profiling/ProfilerSystem.cs` | 753 | **leave-as-is** | Single `ModSystem` lifecycle owner — Load/Unload/OnWorldLoad/OnWorldUnload/PostSetupContent + the per-tick drive loop are one coherent orchestrator over shared `Collector`/`_recorder`/`InsightsEngine.Shared` state; splitting scatters lifecycle state. (Has cross-cutting findings — dust scan, redundant `History[Count-1]` index, off-writer-thread baseline save — filed in the cluster finding files, but the file is not over-broad.) |
| `Data/Streams/SessionRecorder.cs` | 737 | **leave-as-is** | One world-lifecycle owner; shared mutable cursor state; already cleanly sectioned (On* → End → Drain/Build); long because many event kinds, not complex methods. |
| `Profiling/ILHookInterceptor.cs` | 706 | **leave-as-is** | One subsystem over shared `_installedHooks`/`_instrumentedHandles` under a byte-identical-IL contract; `TrimRetainedScaffolding` is the only arguably-separable section but has one consumer. |
| `UI/Overlay/OverlayPanel.cs` | 679 | **not-applicable** | Archived overlay, compiled-out. |
| `Web/Assets/Js/Js.Insights.cs` | 676 | **leave-as-is** | Per-tab fragment; length = sub-view count, not conflated domains. |
| `Web/Assets/Js/Js.Lag.cs` | 629 | **leave-as-is** | Per-tab fragment; coupled by shared filter/sig state. |
| `Data/Detectors/StallDetector.cs` | 594 | **leave-as-is** | Two documented enums + event structs + the pure-static tested classifier family, all coupled to one algorithm; already unit-tested in isolation. |
| `Profiling/MetricCollector.cs` | 586 | **leave-as-is** | One cohesive per-tick engine; the ~20 array fields are 5 bands × 2 surfaces × 2 metrics all needed by `EndTick`; splitting breaks hot-path cache locality, no second consumer. |
| `Data/Contracts/RolloutContracts.cs` | 560 | **leave-as-is** | Frozen-contracts file by design (header lines 8-29); flat catalogue of ~40 immutable snapshot records + the name table, zero logic; splitting scatters name-constants from their types. |
| `Data/Aggregators/Segments/SegmentDetector.cs` | 550 | **leave-as-is** | Single state machine — `OnTick` + side-channel methods mutate shared `_open`/`_pool`/`_prev*`; fails the "comment one out and the rest works" test because it is one machine; heavy collaborators already extracted (Store/Promoter/NameTable). |

**0 split-recommended, 11 leave-as-is, 2 not-applicable (archived).** No "out-of-scope" verdict. The audit's structural finding: this codebase's large files are genuinely cohesive — modularisation is not where its wins are. The wins are dead-code removal, hot-path devirtualisation/caching, the DOM-rebuild gate, and a handful of correctness fixes.

## Data Layout / Memory Access applicability (per cluster)

| Cluster | Applicable? | Outcome |
|---|---|---|
| 1. Hook instrumentation | yes | flat `T[]` per-mod grids already SoA/contiguous (confirmed clean); the finding is the `ModOwnerCache.FromEntitySource` per-call `Substring` alloc + the delegate-path `HookProbe` per-hook object retention (potential). |
| 2. Metric collection | yes (prime) | flat grids correct; findings are access-order (`SumAll` second pass, fusable) + struct width (`StallEvent` ~140 B copied) + uncached `_catCount`. |
| 3. Data pipeline | yes | per-tick fold path; findings are interface-dispatch over `double[]` (devirtualise via internal accessor) + per-pass `CollectorInsightInput` alloc (low). |
| 4. Insights | yes | Welford accumulators + bucket packing reviewed; one 1 Hz `CollectorInsightInput` alloc (immaterial, off-tick). |
| 5. Web + persistence | yes | per-request JSON serialisation allocations (source-gen NOT free — anonymous types) + `EventJournal` double-buffer (free). |
| 6. Web UI | yes (framed as Performance) | per-poll full-DOM rebuild (CC-1, the high finding); per-render Map allocations (TL-1). |

## Certain-set non-regression

No certain finding was demoted to potential to dodge the proof chain. Potential-issues are genuinely not-free or domain-knowledge-gated (e.g. `BaselineBytesPerHook` retune shifts user-visible severity thresholds; the detector-skeleton extraction risks closure allocation vs Invariant 2; KPI `IsEmpty` sentinel is a public-shape change). The only valid moves used were "certain free finding" or "potential (not-free / needs-domain-knowledge)" — never a demote-to-dodge.
