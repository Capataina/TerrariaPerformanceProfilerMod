# Hook Instrumentation — Code Health Findings

14 findings: 0 critical · 4 high · 6 medium · 4 low. Cluster = the per-tick measurement engine (`HookInterceptor`, `ILHookInterceptor`, `ProbeStack`, `HookCategoryRouter`, `HookBackend`, `HookSurfaceCache`, `ModOwnerCache`, `ProfilerSelfHealth`). Every finding is FREE: identical observable behaviour, no new production-code dependency, abstraction, or per-tick overhead. The two big landed RAM levers (Gen2-force in `MarkInstallEnd`, `TrimRetainedScaffolding`) are NOT re-proposed.

Scope note on the default backend: `HookBackend.Mode` defaults to `ILHook` (`HookBackend.cs:60`), so the entire delegate path (`HookInterceptor.HookSupportedOverrides` and below, plus all of `HookProbe`) is dormant in production. It is deliberately retained as an "archived fallback" (`HookBackend.cs:56-58`), so its *bodies* are NOT dead code. But several of its *public read surfaces* are now never read by anyone — those are the high-value dead-surface findings below.

---

## Dead Code Removal

### Write-only delegate-path coverage surface (`InstallFailures`, `UnsupportedHookSignatures`, `UnsupportedSignatureFrequency`, `UnsupportedHookSamples`)
- [ ] Mark the four write-only public read surfaces `[Obsolete]`-or-internal, or collapse them to the private backing fields, since nothing in the repo reads them.
**Category:** API Surface Bloat
**Severity:** high   **Effort:** small   **Behavioural Impact:** none
**Location:** `Profiling/HookInterceptor.cs:259,269,278,281` — `UnsupportedHookSignatures`, `InstallFailures`, `UnsupportedHookSamples`, `UnsupportedSignatureFrequency`
**Current State:** A repo-wide grep for `.InstallFailures`, `.UnsupportedHookSignatures`, `.UnsupportedSignatureFrequency`, `.UnsupportedHookSamples` (excluding `HookInterceptor.cs` itself) returns **zero hits**. These four public properties are written by `HookSupportedOverrides` / `RecordUnsupported` (`HookInterceptor.cs:442,462,466,477`) but never observed. By contrast `MeasuredHookCounts` / `TotalHookCounts` *are* read (via `HookCoverageView.cs:68-77`, `SessionRecorder.cs:538-539`, `OverlayPanel.cs:641-642`, `DashboardRouter.Memory.cs:53`), so the coverage accounting is not all dead — only the unsupported-signature / install-failure reporting half. The doc (`context/systems/hook-instrumentation.md:50-52`) describes the unsupported-signature histogram as a live coverage-debt surface in `client.log`, but no code path actually emits or reads it.
**Proposed Change:** Either (a) reduce the four properties to their private fields (`_installFailures` etc. already exist) and delete the public getters, keeping the increments so the bodies still type-check; or (b) leave them and add an XML-doc note that they are an archived-fallback diagnostic with no current reader. The increments and the `_unsupportedSignatureFrequency`/`_unsupportedHookSamples` writes stay regardless — they are cheap install-time bookkeeping, not hot-path. Recommendation: (a), and surface the result in `client.log`'s existing install-summary line (`HookInterceptor.cs:349-352`) only if the delegate path is ever re-activated.
**Justification:** Evidence type = exhaustive call-site grep (mode-3 anti-pattern: write-only property, `IDE0052` class — Roslyn flags exactly this shape as "has write references but no read references"). The audit confirms the class is real: four siblings share the one mechanism.
**Expected Benefit:** ~25 lines of public API surface removed; the install-summary already logs `_unsupportedHookSignatures`/`_installFailures` as locals (`HookInterceptor.cs:351-352`) so no information is lost. Lowers the "what does this class expose?" cognitive load for the next reader.
**Impact Assessment:** Zero behaviour change — these getters are never called, so removing them cannot alter any observable output. Flag: confirm against any out-of-repo consumer (mod-call API / reflection) before deletion; the codebase has no public mod-call surface today, so this is reasoned-safe but worth one grep against `Mod.Call`.

### `ProbeStack.CurrentDepth` has no callers
- [ ] Remove `CurrentDepth` (and its "validation logging" docstring) or wire it to the leak-detection it claims to back.
**Category:** Dead Code Removal
**Severity:** medium   **Effort:** trivial   **Behavioural Impact:** none
**Location:** `Profiling/ProbeStack.cs:199-204` — `CurrentDepth`
**Current State:** The property's own docstring says it is "Used by the validation logging to surface 'probe stack leaking' if Enter is called without a matching Leave." A repo-wide grep for `CurrentDepth` (excluding `ProbeStack.cs`) returns **zero hits** — the validation logging it describes does not exist. The property exposes `_depth` (a `[ThreadStatic]` int) but nothing reads it.
**Proposed Change:** Delete the property. If a leak detector is desired later, it is a feature (out of scope for this free-only audit), not a reason to keep an unread accessor today.
**Justification:** Evidence type = exhaustive grep. The docstring promises a consumer that was never built — a documentation-rot signal alongside the dead code.
**Expected Benefit:** 6 lines removed; eliminates a docstring that misleads the next reader into thinking leak detection is live.
**Impact Assessment:** Zero behaviour change — `[ThreadStatic]` state read is side-effect-free and unobserved.

### `ProfilerSelfHealth.Reset()` is never called
- [ ] Remove `Reset()` or document why it exists (the singleton is never reset in practice).
**Category:** Dead Code Removal
**Severity:** medium   **Effort:** trivial   **Behavioural Impact:** none
**Location:** `Profiling/ProfilerSelfHealth.cs:266-280` — `Reset()`
**Current State:** `ProfilerSelfHealth` is instantiated once as a process-lifetime singleton (`ProfilerSystem.cs:120: internal static ProfilerSelfHealth SelfHealth { get; } = new ProfilerSelfHealth();`). A grep for `SelfHealth.Reset` / `health.Reset` returns zero hits; the only `.Reset()` calls in the repo are on `Baseline`, `Time`, `_contextTagger`, `RowPool`, `DataRegistry`. `MarkInstallStart`/`MarkInstallEnd` are called once per process and re-set the fields directly, so `Reset()`'s 15-line field-clearing block is unreachable. The docstring claims "so a fresh session starts clean", but no session-restart path invokes it — a mod reload tears down and re-creates the whole `ModSystem`, getting a fresh singleton anyway.
**Proposed Change:** Delete `Reset()`. If the intent was to support in-session re-measurement, that is a feature, not free cleanup.
**Justification:** Evidence type = exhaustive call-site grep + singleton-lifecycle reasoning. The `Reset()` precondition (a re-used singleton) never occurs.
**Expected Benefit:** 15 lines removed; one fewer method to keep in sync with the field list (it already risks drift — see the F-below note that `_trimmedContexts`-equivalent state lives on `ILHookInterceptor`, not here, so this method is the only place the field list is duplicated).
**Impact Assessment:** Zero behaviour change — unreachable code.

### Delegate-path sample-failure flag is never re-armed (latent, but in dead default path)
- [ ] Reset `HookInterceptor._sampleFailureLogged = false` at the top of `Install`, matching `ILHookInterceptor.Install` (`ILHookInterceptor.cs:166`).
**Category:** Inconsistent Patterns
**Severity:** low   **Effort:** trivial   **Behavioural Impact:** none (in default ILHook mode); negligible (flagged) if delegate path is re-activated
**Location:** `Profiling/HookInterceptor.cs:237,800-807` — `_sampleFailureLogged` / `LogSampleHookFailure`
**Current State:** `_sampleFailureLogged` is a static `bool` set `true` on the first install-failure log and never reset. The IL backend resets its identical flag at `ILHookInterceptor.cs:166` (`_sampleFailureLogged = false;`) at the top of `Install`. The delegate `Install` (`HookInterceptor.cs:299-353`) does not. On a `Mods → Reload` with the delegate backend active, a fresh install-failure would be silently suppressed because the flag carried over from the prior load.
**Proposed Change:** Add `_sampleFailureLogged = false;` alongside the existing field resets at `HookInterceptor.cs:301-303`. This makes the two backends symmetric and is the same one-line fix the IL path already carries.
**Justification:** Evidence type = direct cross-backend diff (`ILHookInterceptor.cs:166` resets; `HookInterceptor.cs` does not). Anti-pattern class = process-static log-once flag not re-armed on lifecycle restart, identical shape to the `_hasEverRefreshed` overflow bug that was already fixed in `ProfilerSelfHealth` (`6c45f34`). One mechanism: static state surviving a reload it should not.
**Expected Benefit:** Restores dual-surface observability (Invariant: agent reads `client.log`) on reload for the fallback path; the first install-failure warning fires on every load, not only the first-ever.
**Impact Assessment:** Zero behaviour change in production (delegate path dormant). Behaviour-affecting only if the user flips to `Delegate`/`Parallel` and reloads — and there it only *adds* a log line that should have fired, never suppresses one. Flag as negligible.

---

## Complexity Hotspots

### `TryHookSupportedOverride` re-reads `parameters`/`returnType` per branch; the 30-way chain is a sequential `==` scan
- [ ] Leave the dispatch chain as-is structurally, but hoist the `p0`/`p1` reads to the top of each arity block (already mostly done) — no behavioural change, install-time only.
**Category:** Complexity Hotspots
**Severity:** low   **Effort:** small   **Behavioural Impact:** none
**Location:** `Profiling/HookInterceptor.cs:505-780` — `TryHookSupportedOverride`
**Current State:** The method is a ~275-line cascade of `if (parameters.Length == N && p0 == typeof(X) && returnType == typeof(Y))` branches, ~30 signature families. It runs **only at install time**, once per discovered override, inside the dormant delegate path. `parameters` and `returnType` are read once at the top (`HookInterceptor.cs:507-508`); the arity blocks re-read `parameters[i].ParameterType` repeatedly. This is install-time cost, not hot-path, so the sequential `typeof` scan is not a per-tick concern.
**Proposed Change:** No structural change recommended (a dictionary-dispatch refactor would be a real abstraction with maintenance cost, failing the free-only bar, and the install-time cost is negligible). The honest verdict: this is acceptable as-is. Recorded so the next reader does not "discover" it as a smell and refactor it speculatively — the cost is install-time, the chain is exhaustively documented in `context/systems/hook-instrumentation.md:80-101`, and a `Dictionary<(int arity, Type ret, Type p0...), Func<>>` would not be free.
**Justification:** Evidence type = read + call-graph (`ProfilerSystem.cs:142: HookInterceptor.Install` runs in `PostSetupContent`, once). Mode-1 research (switch-vs-dictionary dispatch) confirms dictionary dispatch only wins at high call frequency; this is called O(overrides) once per load, so the switch is fine.
**Expected Benefit:** None beyond preventing a speculative refactor. This is a "leave-as-is, here's why" record.
**Impact Assessment:** No change proposed. Net informational.

### `ApplyTimingWrap` is a dense 120-line IL transform with no single-responsibility seams (acceptable)
- [ ] Leave as-is; it is a genuinely cohesive IL-rewrite state machine, not a modularisation candidate.
**Category:** Complexity Hotspots
**Severity:** low   **Effort:** n/a   **Behavioural Impact:** none
**Location:** `Profiling/ILHookInterceptor.cs:586-705` — `ApplyTimingWrap`
**Current State:** One method does: void-detection, ret-local allocation, alloc-mode branch selection, per-`ret` rewriting, prologue emit, tail-handler emit, and exception-handler registration. It is dense but every step shares the same `body`/`il`/anchor instructions, and the ordering is load-bearing (the `ret` rewrite must precede the prologue emit so the loop indices stay valid; the handler registration must be last). The byte-identical IL contract (`install-ram.md` capability table row 4) means it cannot be split without re-verifying equivalence.
**Proposed Change:** No split. Record the verdict: cohesive transform, the apparent "smell" (long method) is intrinsic to single-pass IL rewriting where intermediate state cannot escape the method. Splitting into `RewriteReturns` / `EmitPrologue` / `RegisterHandler` would pass anchor instructions across method boundaries, raising the chance of an off-by-one in the leave-target wiring — higher risk than the readability gain.
**Justification:** Evidence type = read + the in-tree doc's explicit byte-identical contract. Engineering-standards "extract a seam when the second consumer arrives" — there is one consumer.
**Expected Benefit:** None beyond preventing a speculative split.
**Impact Assessment:** No change proposed.

---

## Performance Improvement

### Hot-path `Stopwatch.GetTimestamp()` is read AFTER the array/null setup in `ProbeStack.Enter` — start clock is slightly late, but consistent
- [ ] No change; verify the start-timestamp placement is intentional (it brackets the same way on Leave, so the lazy-alloc cost is excluded symmetrically). Recorded for completeness.
**Category:** Performance Improvement
**Severity:** low   **Effort:** trivial   **Behavioural Impact:** none
**Location:** `Profiling/ProbeStack.cs:82-103` — `Enter` / `EnterCpuAlloc`
**Current State:** `Enter` does the null-check + (rare) resize *before* reading `Stopwatch.GetTimestamp()` (line 101). On the first call per thread the `new Frame[32]` allocation (line 87) happens before the clock starts, so its cost is never attributed to the hooked body — correct. After warmup the branch is a single not-taken `if`, so the timestamp read is effectively at entry. This is the right shape (the allocation is warmup-only and is correctly excluded), and `Leave` reads the clock first thing (line 124), so the bracket is symmetric. Nothing to fix; flagged only so a future reader does not "optimise" by moving the timestamp read earlier (which would wrongly start the clock before the resize on growth ticks).
**Proposed Change:** None. A comment at line 100 noting "timestamp deliberately read after the resize guard so warmup-resize cost is excluded" would prevent a future mis-optimisation, and is a free clarity win.
**Justification:** Evidence type = read + mode-2 research on `Stopwatch.GetTimestamp` (it is the allocation-free primitive precisely chosen here; ~17 ns/call per the `HookBackend.cs:54-55` in-code measurement). The placement is correct.
**Expected Benefit:** Prevents a latent regression from a well-meaning reorder.
**Impact Assessment:** No behaviour change proposed (comment only).

### `ModOwnerCache.FromEntitySource` allocates a substring on every cache-miss path and is NOT memoised
- [ ] Cache the `EntitySource_`-stripped name by source `Type` so the `Substring` runs once per source subclass, not once per call.
**Category:** Algorithm Optimisation
**Severity:** medium   **Effort:** small   **Behavioural Impact:** none
**Location:** `Profiling/ModOwnerCache.cs:72-79` — `FromEntitySource`
**Current State:** Every call does `source.GetType().Name` then, when it starts with `EntitySource_`, `n.Substring(...)` — a fresh `string` allocation **every call**. The docstring claims "repeated calls with the same source subclass are zero-alloc beyond the initial bookkeeping" (`ModOwnerCache.cs:67-70`), but that is **false**: there is no dictionary memoising the stripped result. `Type.Name` itself is runtime-cached, but the `Substring` is not. The callers are `InteractionNpc.cs:40` (per NPC spawn interaction). Spawns are event-driven, not per-tick, so this is not a hot-path zero-alloc violation, but on a spawn-heavy session (a swarm event) it allocates one short string per spawn unnecessarily, and the docstring overstates the caching.
**Proposed Change:** Add a `static readonly ConcurrentDictionary<Type, string>` keyed by the source `Type`, populated via `GetOrAdd` with the strip logic. The sibling `_byTypeId` cache already establishes the pattern in this exact file, so this is the consistent shape, not a new abstraction. Each source subclass strips exactly once.
**Justification:** Evidence type = read (the docstring's claim is contradicted by the code — no memoisation exists). Anti-pattern class = "docstring asserts caching the code does not implement" (a Documentation-Rot + missed-memoisation pair; the same `ModOwnerCache` already memoises `_byTypeId`, so this is an inconsistency within one file).
**Expected Benefit:** One `Substring` per distinct `EntitySource_*` subclass for the whole session instead of one per spawn interaction. At a Calamity swarm (~hundreds of spawns/s) this is a measurable reduction in Gen0 churn during exactly the spike windows the profiler is trying to measure cleanly. Also makes the docstring true.
**Impact Assessment:** Zero behaviour change — same returned string for the same input; only the allocation count drops. Bounded-growth concern: the key set is the number of distinct `IEntitySource` subclasses loaded (tens), not unbounded.

---

## Documentation Rot

### `ProfilerSelfHealth.BaselineBytesPerHook` comment lists stale per-release baselines and pre-trim normal
- [ ] Update the `v0.5 / v0.6.1 / v0.7.x` baseline table and the `36 KB` "measured normal" to reflect the post-`TrimRetainedScaffolding` reality (~30 KB/hook per `install-ram.md` execution log).
**Category:** Documentation Rot
**Severity:** medium   **Effort:** trivial   **Behavioural Impact:** none
**Location:** `Profiling/ProfilerSelfHealth.cs:85-95` — `BaselineBytesPerHook` block
**Current State:** The comment pins `BaselineBytesPerHook = 36 KB` as "v0.7.x measured normal" and lists a release history ending at `v0.7.x 36.8 KB/hook`. But `install-ram.md`'s execution log (line 327) records the landed `TrimRetainedScaffolding` cut took self-health to **58 → 30 KB/hook** (and tML attribution 3.7 → 1.0 GB) at v0.13. So the baseline constant and its comment now describe a *pre-trim* world. With the real per-hook cost at ~30 KB and the baseline pinned at 36 KB, the severity classifier (`ClassifySeverity`, ratio bands 1.5×/2.5×) is comparing against a stale-high floor — `Concerning` triggers at 54 KB, `Severe` at 90 KB, both far above the new ~30 KB normal, so the signal is currently slack (it will under-report a regression that lands between 30 and 54 KB). This is the exact failure the comment itself warns about at lines 88-90 ("leave it alone if a per-hook regression slips in — then Severity surfaces it") — but the regression-surfacing only works if the baseline tracks the current intentional-improvement floor, which the comment at 90-92 says to update on such an improvement. The trim *was* that improvement and the baseline was not updated.
**Proposed Change:** Update the comment's release table to add the `v0.13 ~30 KB/hook (post-scaffolding-trim)` row, and bump `BaselineBytesPerHook` from `36L * 1024L` to the measured post-trim normal (~`30L * 1024L`). NOTE: the constant change is a behaviour-affecting tuning change (it shifts the Severity thresholds), so it is "possible (requires decision)", NOT free — flagged distinctly below. The **comment** update (recording the v0.13 measured number) is free and behaviour-neutral.
**Justification:** Evidence type = cross-doc reconciliation (`install-ram.md:327` measured 30 KB/hook vs `ProfilerSelfHealth.cs:95` pinned 36 KB). Honesty-contract relevant: self-health Severity is a player- and agent-visible badge; a stale-high baseline makes the badge read `Healthy` across a band where it should read `Concerning`.
**Expected Benefit:** The Severity badge regains its designed sensitivity; the comment stops lying about the current floor.
**Impact Assessment:** Comment-only update = zero behaviour change. The constant retune is split out as the next finding because it shifts a user-visible threshold.

### Constant retune: `BaselineBytesPerHook` 36 KB → ~30 KB (NOT free — flagged for decision)
- [ ] Decide whether to re-pin the Severity baseline to the post-trim measured normal; this shifts the amber/red thresholds.
**Category:** Configuration Drift
**Severity:** medium   **Effort:** trivial   **Behavioural Impact:** possible (requires decision)
**Location:** `Profiling/ProfilerSelfHealth.cs:95` — `BaselineBytesPerHook`
**Current State:** As above: the constant is `36 KB`, the post-trim measured normal is ~`30 KB`. Changing it retunes when the overlay/JSON shows amber/red.
**Proposed Change:** Surface to the engineer: re-pin to the measured post-trim normal so the bands track reality (the design intent per the in-code comment lines 90-92). This is a tuning decision, not a free refactor — it alters observable Severity output, so it is explicitly OUT of the free-only scope and recorded here for the orchestrator to route to a decision, not auto-apply.
**Justification:** Evidence type = measured value vs pinned constant. The design doc (the comment itself) says to update on an intentional improvement; the trim was one.
**Expected Benefit:** Severity badge sensitivity restored.
**Impact Assessment:** Behaviour-affecting (Severity thresholds move). Requires engineer sign-off; not a free win.

### `HookSurfaceCache` docstring says delegate backend is "dormant in v0.6"; mod is now v0.16+
- [ ] Refresh the version reference (`v0.6` → current) and the §-citation in the `HookSurfaceCache` / `ILHookInterceptor` "(active backend)" comments.
**Category:** Documentation Rot
**Severity:** low   **Effort:** trivial   **Behavioural Impact:** none
**Location:** `Profiling/HookSurfaceCache.cs:21-22` — class docstring; also `ILHookInterceptor.cs:360,90`, `HookInterceptor.cs:363` ("v0.6.1")
**Current State:** `HookSurfaceCache.cs:21` reads "Both `HookInterceptor` (delegate backend, dormant in v0.6) and `ILHookInterceptor` (active backend)". The mod is at v0.16+ (per git log `c946d87 ... + v0.16`). The "v0.6" framing is stale; the delegate backend is still dormant, but the version anchor misleads. Several `// v0.6.1:` / `// v0.13:` inline tags scatter the cluster — these are change-history annotations embedded in forward-facing comments, which the editing-discipline standard flags as stale-triple risk.
**Proposed Change:** Replace "dormant in v0.6" with "dormant by default (the active backend is ILHook; see HookBackend.Mode)". Leave the genuinely-load-bearing dated decision references (the `7da4058`/`5725572` crash-fix lineage in the doc) intact — those are operationally necessary history. Strip only the version anchors that imply a current state that has moved.
**Justification:** Evidence type = git log version vs in-code version anchor. Editing-discipline: forward-facing comments should describe desired state, not change history, unless the history is operationally necessary.
**Expected Benefit:** Comment describes current reality; the next reader does not mistake "v0.6" for "recently changed".
**Impact Assessment:** Comment-only — zero behaviour change.

---

## Inconsistent Patterns

### `DisplayName` is duplicated verbatim across both backends with divergent arity handling
- [ ] Note the duplication; do not extract yet (the two copies differ — one takes `parameters` precomputed, one recomputes — extracting would add a parameter-passing seam).
**Category:** Inconsistent Patterns
**Severity:** low   **Effort:** small   **Behavioural Impact:** none
**Location:** `Profiling/HookInterceptor.cs:788-796` and `Profiling/ILHookInterceptor.cs:541-550` — `DisplayName`
**Current State:** Both define a private `DisplayName(Type, MethodInfo[, ParameterInfo[]])` producing `Type.Name.Method(p0, p1)`. The delegate version takes `parameters` as an argument (already computed in the dispatch); the IL version recomputes `method.GetParameters()` internally (`ILHookInterceptor.cs:543`). The string shapes are intended to be identical so `RegisterOrReuseHook` (`ILHookInterceptor.cs:514`) can share hookIds across backends in Parallel mode — a *correctness* coupling, not just style. They are currently consistent, but the duplication means a future edit to one (e.g. adding p2 to the display) silently breaks Parallel-mode hookId sharing.
**Proposed Change:** Record as a watch item. A shared `HookDisplayName(Type, MethodInfo)` helper (e.g. on `HookCategoryRouter`, which both already reference) would be the natural home and would make the cross-backend identity contract single-sourced. This is a *small* extraction with one real second consumer (the two backends), so it passes the "seam at second consumer" bar — borderline free. Recommend extracting to `HookCategoryRouter.HookDisplayName` since both files already depend on it and it carries no per-tick cost (install-time only).
**Justification:** Evidence type = side-by-side read; the Parallel-mode hookId-sharing contract (`RegisterOrReuseHook`) depends on the two producing byte-identical strings. Anti-pattern class = duplicated logic guarding a cross-component invariant.
**Expected Benefit:** The hookId-sharing contract becomes un-breakable-by-divergence; one edit site instead of two.
**Impact Assessment:** Zero behaviour change if extracted carefully (same string output). The IL version's internal `GetParameters()` call would move into the shared helper; the delegate version would drop its `parameters` arg — verify the output is character-identical (a diagnostic test, flagged below).

### `IsHookOverride` is duplicated identically in both backends
- [ ] Single-source `IsHookOverride(MethodInfo)` (identical bodies) onto the shared router.
**Category:** Inconsistent Patterns
**Severity:** low   **Effort:** trivial   **Behavioural Impact:** none
**Location:** `Profiling/HookInterceptor.cs:454-458` and `Profiling/ILHookInterceptor.cs:535-539` — `IsHookOverride`
**Current State:** Byte-identical private methods in both files:
```
MethodInfo baseDefinition = method.GetBaseDefinition();
return baseDefinition != method && baseDefinition.DeclaringType != typeof(object);
```
This is the structural "is this an override of a base virtual" test both backends use to discover hooks. Two copies, same logic.
**Proposed Change:** Lift to `HookCategoryRouter` (or a small `HookSurface` static both already reference). One real second consumer exists today (both backends), so the seam is justified now, not speculative.
**Justification:** Evidence type = side-by-side read (identical). The shared-category-router commit (`77a99d2`) established exactly this "both backends must agree, so single-source it" precedent for category resolution; `IsHookOverride` is the same shape and was missed.
**Expected Benefit:** One definition of the override-discovery predicate; matches the existing single-sourcing of `ResolveCategory`.
**Impact Assessment:** Zero behaviour change — identical logic, install-time only.

---

## Data Layout and Memory Access Patterns

### Verdict: applies to the hot path; current layout is correct — one micro-note
- [ ] No change. `ProbeStack.Frame` layout and `PerModAttribution`'s flat indexed arrays are already cache-friendly and zero-alloc; recorded as a deliberate "no Data-Layout findings beyond confirmation".
**Category:** Data Layout and Memory Access Patterns
**Severity:** low   **Effort:** n/a   **Behavioural Impact:** none
**Location:** `Profiling/ProbeStack.cs:50-63` — `Frame` struct; `Data/Aggregators/PerModAttribution.cs:225-291` — `Add`
**Current State:** The category DOES apply (this is the per-tick hot path). Review result: the layout is already right. `Frame` is a value struct (`int HookId; long StartTicks; long StartAllocBytes`) stored in a pre-grown per-thread `Frame[]` — contiguous, no per-frame heap object, no boxing. `PerModAttribution.Add` writes into flat `long[]` indexed by `modId * CategoryCount + categoryId` and a parallel `hookTicks[hookId]` — sequential-ish writes, bounds-checked with `(uint)` casts (the idiomatic single-compare bounds check). The `StartAllocBytes` field is carried in the same `Frame` even in CPU-only mode (8 wasted bytes/frame when alloc-tracking off), but the stack depth is tiny (<8 typical, capacity 32), so the waste is ~256 bytes/thread — negligible, and the doc (`ProbeStack.cs:54-63`) explicitly justifies one-stack-as-one-source-of-truth over a split parallel array. That justification is sound: splitting to save 8 bytes/frame would risk the CPU/alloc desync the comment warns about.
**Proposed Change:** None. The 8-byte-per-frame waste in Lite mode is the correct trade for the single-stack invariant. Recorded so a future reader does not split the struct to "save memory" and reintroduce desync risk.
**Justification:** Evidence type = read + the in-code rationale. Mode-3 check (false-sharing / struct-padding anti-patterns): the per-thread `[ThreadStatic]` array means no cross-thread false sharing; struct field order (`int` then two `long`) has natural alignment with 4 bytes padding after `HookId` — re-ordering to `long,long,int` would pack tighter but the array is tiny so it does not matter.
**Expected Benefit:** None beyond preventing a speculative split.
**Impact Assessment:** No change proposed.

---

## Modularisation verdicts

### `HookInterceptor.cs` (1227 LOC) — verdict: `leave-as-is`
The file is large but genuinely cohesive: it is **one** thing — the delegate-pair timing backend — expressed as (a) ~30 delegate type declarations (lines 26-198), (b) the discovery+dispatch state machine (`Install` → `InstallForMod` → `HookSupportedOverrides` → `TryHookSupportedOverride`), and (c) the `HookProbe` class with one `Time*` method per delegate shape (lines 816-1227). The LOC is dominated by the irreducible 1-delegate-pair-+-1-probe-method-per-signature fan-out that the doc (`context/systems/hook-instrumentation.md:101`) explicitly documents as "three edits, all in one file" — the cohesion is the design. Splitting `HookProbe` into its own file is the only defensible move (it is a separate class already), but it would not reduce complexity, only relocate it, and it would separate the probe from the dispatch branch that constructs it. The delegate declarations could move to a `HookDelegates.cs` partial — also pure relocation. Recommendation: leave as one file; if anything, the *free* win is removing the dead read-surface (finding 1) which trims ~25 lines without touching the cohesive core. The 30-branch dispatch is install-time and exhaustively documented; not a hotspot.

### `ILHookInterceptor.cs` (706 LOC) — verdict: `leave-as-is`
Cohesive: the install/teardown lifecycle (`Install`/`Uninstall`/`TrimRetainedScaffolding`), the type-walk + closed-generic inheritance pass (`InstallForMod`/`InstrumentTypeOverrides`), and the IL manipulator (`InstallTimingHook`/`ApplyTimingWrap`). Every part shares the same `_installedHooks` / `_instrumentedHandles` state and the byte-identical-IL contract. `TrimRetainedScaffolding` (96 lines of guarded reflection, lines 260-356) is the one section that is arguably separable — it is a distinct concern (post-install RAM reclaim via MonoMod internals) with its own Invariant-4 guard discipline. But it reads `_installedHooks` directly and is called only from `Install`, so extracting it to a `ScaffoldingTrimmer` would require passing the hook list across a boundary and re-establishing the "only our own entries" reference-equality check elsewhere — a real seam cost for a single consumer. Verdict: leave-as-is; the file is one subsystem. If the engineer later wants the trim isolated for independent testing, that is a feature-shaped move (it needs MonoMod internals, untestable off-game anyway), not a free refactor.

---

## Diagnostic tests flagged (orchestrator writes them)

NOTE: **None** of this cluster's files are currently linked into `Tests/PerformanceProfiler.Tests.csproj`. The csproj explicitly excludes anything pulling in `Terraria.ModLoader` (see `Tests/PerformanceProfiler.Tests.csproj:94-98,115-119`), and every cluster file depends on tModLoader types (`HookCategoryRouter` → `ModSystem`/`ModPlayer`/`GlobalNPC`; `ProbeStack` → `PerModAttribution` which is linked, but `ProbeStack` also references `HookBackend`; the interceptors → `MonoModHooks`/`Mod`/`Mono.Cecil`). So the surfaces below would need a *new* linkable pure-logic extraction or an integration test, NOT a simple add to the existing csproj. Flagging accordingly:

| Finding | Test name | What it asserts | Target surface | Feasibility |
|---|---|---|---|---|
| `DisplayName` duplication (cross-backend identity) | `DisplayName_DelegateAndIlProduceIdenticalStrings` | For a fixed `(Type, MethodInfo[])` set, both backends' `DisplayName` return byte-identical strings (guards Parallel-mode hookId sharing) | Would need `DisplayName` lifted to a Terraria-free static; today it is private + uses `MethodInfo` which is fine (System.Reflection, no Terraria), so a small extracted `HookDisplayName(Type, MethodInfo)` **could** be linked into tests. Confidence today: HIGH from reading (the two are visibly identical) — test would only be needed if the extraction lands. |
| `ModOwnerCache.FromEntitySource` memoisation | `FromEntitySource_MemoisesPerType` | Calling twice with the same source `Type` returns the same string and (post-fix) does not allocate a second `Substring` | `ModOwnerCache.FromEntitySource` takes `IEntitySource` (Terraria type) — NOT linkable as-is. Would require an integration test or a refactor to split the string-strip into a Terraria-free `StripEntitySourcePrefix(string)` helper (which IS linkable). Confidence: HIGH from reading that the memoisation is absent; the test is for the *fix*, not the diagnosis. |
| `_sampleFailureLogged` reset | (none) | — | The flag is a static `bool`; the fix is a one-line add verified by reading the IL backend's symmetric `Install`. HIGH confidence, no test needed. |

All other findings (dead read-surface, `CurrentDepth`, `Reset()`, doc rot, data-layout confirmation) are **HIGH confidence from reading + exhaustive grep** — no test required.

---

## Potential-issues candidates (engineer's domain knowledge required)

These are suspicions grounded in code but needing the engineer's runtime/domain judgement — routed to the separate potential-issues bar, NOT free findings:

1. **`HookProbe` delegate-path still allocates one `HookProbe` object per installed hook (`CreateProbe`, `HookInterceptor.cs:782-786`).** In the dormant delegate path this is install-time, fine. But if the delegate or Parallel backend is ever re-activated at 152k hooks, that is 152k small heap objects retained for the session (each captured by its `MonoModHooks.Add` delegate). The IL path avoids this entirely (hookId is an inline IL constant, no per-hook object). Worth the engineer confirming the delegate path's per-hook object cost is acceptable before any Parallel-mode playtest at scale — it is a second RAM source the `install-ram.md` analysis (focused on the IL path's Cecil retention) did not cover.

2. **`TrimRetainedScaffolding` reflects into MonoMod 25.3.2 internals (`state`/`hook`/`noConfigIlhooks`/`ILHookEntry.LastContext`).** Invariant-4-guarded (every field lookup checked, abort-clean on shape mismatch), and the doc (`hook-instrumentation.md`, `install-ram.md`) covers it. The potential issue is *version drift*: the guard skips the trim on a shape mismatch (correct, safe), but there is no surfaced signal to the *player* that RAM reclaim silently stopped working after a tModLoader/MonoMod update — only a `client.log` Info line (`ILHookInterceptor.cs:274`). The engineer should decide whether a silent-no-op-after-update is acceptable, given the trim is now load-bearing for the 32 GB kitchen-sink target (RAM goes from 1.0 GB back to ~3.7 GB if it silently stops). This is a known-risk surfacing decision, not a code defect.

3. **`HookSurfaceCache` is process-static and `Clear()`'d only inside `ILHookInterceptor.Uninstall` (`ILHookInterceptor.cs:228`).** Teardown wiring is sound for the normal case: `PerformanceProfiler.Unload` (`PerformanceProfiler.cs:184-186`) calls `ILHookInterceptor.Uninstall()` **unconditionally**, so the cache is cleared on every reload regardless of which backend installed (even in `Delegate` mode where the IL path never installed any hooks). The residual coupling worth the engineer's eye: the *only* clear path is routed through the IL backend's `Uninstall`. If a future refactor ever makes that call conditional on `HookBackend.ILHookActive` (an easy, plausible "don't run IL teardown if IL never installed" optimisation), the delegate/Parallel paths would leak a stale `Type[]` cache across reloads — and the `Install` guard (`if (Installed) return;`) would not re-scan. This is a latent shared-static-lifecycle coupling between two backends, not a current defect; flagged so the unconditional-teardown invariant is recorded as load-bearing before anyone "optimises" it. (The `Mod.Unload`-didn't-fire scenario at `hook-instrumentation.md:211` is the separate, already-documented tModLoader-bug case.)
