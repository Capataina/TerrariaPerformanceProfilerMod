# Hook-Install RAM Path — Code Health Findings

**Systems covered:** `Profiling/ILHookInterceptor.cs`, `Profiling/HookBackend.cs`, `Profiling/ProfilerSelfHealth.cs`, `Profiling/ProfilerSystem.cs` (install orchestration), plus the MonoMod `RuntimeDetour 25.3.2` / `Utils 25.0.10` internals the install path drives.
**Finding count:** 3 certain (F1 high, F2 high, F3 medium) + 1 potential issue (P-1, see `potential-issues.md`).

> This is the live investigation's priority area. The headline: on a 100-mod /
> 152,310-hook stack the profiler installs one `ILHook` per hook override and
> `ProfilerSelfHealth` reports a ~9 GB install delta (~60 KB/hook); tModLoader
> independently attributes ~8.2 GB to the mod, more than every other mod combined.
> The audit decompiled the shipped MonoMod binaries to settle, with ground-truth
> evidence, what is **retained** (genuine per-hook cost) vs **transient** (measurement
> artefact), and what is provably-free to reclaim.

---

## The retention picture (decompiled ground truth)

Decompiled with `ilspycmd 10.1.0` against the exact shipped assemblies the mod runs
against (`MonoMod.RuntimeDetour 25.3.2`, `MonoMod.Utils 25.0.10`, both under
tModLoader's `Libraries/`). Per **distinct hooked method**, MonoMod retains this graph
for the hook's entire applied lifetime:

```
DetourManager.ManagedDetourState         (1 per hooked method, alive while any ILHook is applied)
├── SourceCloneIl : DynamicMethodDefinition          ← /tmp/dm.cs:346  — NEVER disposed until RemoveILHook
│     └── Module : ModuleDefinition                  ← /tmp/dmd.cs:59  — full Cecil module
│           └── MethodDefinition.Body                 ← cloned original IL: instructions, variables, EH table
└── ilhookGraph / noConfigIlhooks : List<ILHookEntry> (1 ILHookEntry per ILHook on this method)
      └── LastContext : ILContext (read-only)         ← /tmp/dm.cs:314 — MakeReadOnly(), NOT disposed
            └── MethodDefinition (manipulated)         ← second full Cecil method body
```

Two Cecil method bodies are kept alive per hooked method, for the process lifetime.

**Why `SourceCloneIl` is never released.** `UpdateEndOfChain` (`/tmp/dm.cs:627-664`)
re-clones it (`new DynamicMethodDefinition(SourceCloneIl)`, line 643) every time the
hook chain for that method changes, then disposes only the *temporary* working DMD in
its `finally` (line 662). The `SourceCloneIl` field itself is disposed only by
`RemoveILHook` → `RemoveGraphILHook`/`RemoveNoConfigILHook` (lines 598-616). While the
hook is applied, MonoMod must keep `SourceCloneIl` so it can rebuild the chain.

**Why `LastContext` is never released.** `InvokeManipulator` (`/tmp/dm.cs:666-681`)
runs the user manipulator inside `new ILContext(def)`, then — if the context is not
read-only — calls `MakeReadOnly()` (line 679) **instead of** `Dispose()`. A read-only
`ILContext` keeps its underlying Cecil `MethodDefinition` alive. `CleanILContexts`
(`/tmp/dm.cs:683-705`) disposes a context only when a *newer* `CurrentContext` has
superseded the `LastContext` (line 695: `if (entry.CurrentContext != entry.LastContext)`).
After the final apply, `CurrentContext == LastContext`, so nothing is disposed.

**Confirmation that disposal does free it.** `DynamicMethodDefinition.Dispose()`
(`/tmp/dmd.cs:636-648`) disposes the `Module`. So the retained Cecil graph *is*
reclaimable — MonoMod simply holds it because the hook stays applied.

### Verdict: ~8.2 GB is mostly RETAINED, not transient

The priority focus asked whether the scaffolding is "dead weight retained after
`applyByDefault:true`". Decompilation answers: **yes for `SourceCloneIl` and
`LastContext`** (genuine retained Cecil graphs, two method bodies per hook), and the
read-only-ILContext retention is the avoidable slice. At 152,310 hooks, even a few KB
of Cecil graph per hook compounds into multiple GB. This is consistent with tModLoader's
~8.2 GB attribution being mostly real retained managed state, not a measurement illusion
— the measurement gap (F2) explains the *headroom* between the honest retained number
and the reported number, not the bulk.

---

## F1 — MonoMod retains per-hook Cecil scaffolding for the process lifetime; reclaim the read-only ILContext after final apply {#f1}

- [ ] After the install pass completes, dispose the read-only `ILContext` retained per `ILHook` (the slice MonoMod keeps but the chain rebuild does not strictly need once no further hooks will be added to that method), via the MonoMod-supported path — see Proposed Change for the two provably-free options.

**Category:** Data Layout and Memory Access Patterns
**Severity:** High
**Effort:** Medium
**Behavioural Impact:** Possible (requires decision) — see Impact Assessment. The full-coverage-preserving options are flagged; the aggressive option is explicitly marked not-free.

**Location:**
- `Profiling/ILHookInterceptor.cs:88` — `_installedHooks` holds every `ILHook` for the process lifetime.
- `Profiling/ILHookInterceptor.cs:441-448` — `InstallTimingHook` → `new ILHook(target, manipulator, applyByDefault: true)`.
- MonoMod `DetourManager.ManagedDetourState.SourceCloneIl` (`/tmp/dm.cs:346`), `ILHookEntry.LastContext` (`/tmp/dm.cs:314`), `InvokeManipulator` (`/tmp/dm.cs:679`).

**Current State:**
Every hook override gets its own `ILHook` (152,310 of them on the 100-mod stack), each
constructed `applyByDefault: true` so the IL is live immediately. MonoMod's
`DetourManager` keeps, per hooked method, a `SourceCloneIl` DMD (a Cecil clone of the
original body) and one read-only `ILContext` per applied `ILHook` (the manipulated
method body). Neither is disposed until `RemoveILHook` runs, which only happens on mod
unload via `ILHookInterceptor.Uninstall()`. So for the entire play session, two Cecil
method-body graphs per hook sit on the managed heap. This is the dominant component of
the ~8.2 GB tModLoader attributes to the mod.

The mod's hook set is **install-once**: hooks are added in `PostSetupContent` and never
re-applied, removed, or re-chained during play. The `SourceCloneIl` exists to let
MonoMod rebuild the chain when it changes — but for this mod's usage, the chain for each
hooked method never changes after install (each method gets exactly one profiler ILHook,
and no other profiler hook is added to it later).

**Proposed Change:**
Two provably-free options that preserve 100% hook coverage (no Lite mode, no feature
removal), ordered by safety:

1. **Reclaim the read-only `ILContext` per hook after the install pass.** The read-only
   `ILContext` (`LastContext`) wraps the *manipulated* method body. Once the method is
   generated and JIT-compiled (`UpdateEndOfChain` line 655-656), the read-only context's
   Cecil graph is not needed for normal dispatch — it is needed only if MonoMod must
   re-run the chain (add/remove another hook on the same method). For this mod that never
   happens post-install. MonoMod does not expose a public "dispose the read-only context
   but keep the hook applied" call, so this requires either (a) upstreaming a
   `TrimRetainedIL()` method to MonoMod, or (b) reflecting into `ILHookEntry.LastContext`
   to dispose it after install — which is fragile and would itself need an abort-clean
   guard (Invariant 4). **This option is the avoidable slice but is NOT free without
   MonoMod cooperation; flagged for decision, not presented as a free change.**

2. **Coalesce many hooks per method into fewer chain entries — not applicable here**
   (each method has exactly one profiler hook), so this lever is empty for this mod.

The genuinely-free, coverage-preserving win in this finding is therefore the **honest
re-measurement** (F2) plus the **documented acknowledgement** that the retained Cecil
graph is intrinsic to per-method ILHook at this hook count. The structural reduction
(option 1) is real but crosses into "requires decision / requires upstream change",
so it is filed here as the high-severity context for F2/F3 and as P-1 in
`potential-issues.md` rather than as a clean free upgrade.

**Justification:**
Strongest-tier evidence: the retention is read directly out of the decompiled shipped
binaries (not inferred). The `SourceCloneIl` field (`/tmp/dm.cs:346`), the
`MakeReadOnly`-not-`Dispose` choice (`/tmp/dm.cs:679`), and the `CleanILContexts`
supersession gate (`/tmp/dm.cs:695`) together prove the per-hook Cecil graph survives
for the hook's applied lifetime. The mod's own context already suspected this: "Mono.Cecil
retained state is the suspected dominant cost" (`context/notes/decisions.md:201`) and
"Cecil ILContext dispose" is listed as a deferred v0.6.2+ win (`decisions.md:25`). This
finding converts the suspicion into confirmed root cause.

**Expected Benefit:**
Quantifying honestly: the read-only-`ILContext` slice is one of the two retained Cecil
bodies per hook, so reclaiming it (option 1, if MonoMod supports it) would roughly halve
the per-hook retained Cecil cost — on the order of GB at 152k hooks. But because it is
not free without upstream work, the *bankable* benefit of this finding is the corrected
understanding that drives F2 (honest measurement) and F3 (corrected public claim).

**Impact Assessment:**
Possible impact (requires decision). Option 1(b) (reflection into MonoMod internals to
dispose `LastContext`) would break MonoMod's invariant that an applied hook can be
re-chained, and would violate Invariant 4 (abort-clean on host drift) unless guarded —
MonoMod's internal field names are exactly the "loader internals that change across
updates" the invariant warns about. Option 1(a) (upstream `TrimRetainedIL`) is behaviour-
neutral but is not a change this repo can make alone. **Neither is a free in-repo change**,
which is why the bankable outputs are F2 and F3.

---

## F2 — `MarkInstallEnd` samples the unstable `GetTotalMemory(false)` form, conflating retained with transient and making the reported delta methodology-dependent {#f2}

- [ ] Force a Gen2 collection in `MarkInstallEnd` before sampling (symmetric with `MarkInstallStart`), so the reported install delta measures retained state only, not whatever transient install garbage the GC happened not to have swept.

**Category:** Known Issues and Active Risks
**Severity:** High
**Effort:** Trivial
**Behavioural Impact:** Negligible (flagged) — adds one ~50-150 ms forced Gen2 at install time (once per session), identical in cost and shape to the one `MarkInstallStart` already performs. Does not touch the per-tick hot path.

**Location:**
- `Profiling/ProfilerSelfHealth.cs:165-173` — `MarkInstallStart` (forces Gen2, then samples — correct).
- `Profiling/ProfilerSelfHealth.cs:180-189` — `MarkInstallEnd` (samples `GetTotalMemory(forceFullCollection: false)` with NO collection — the bug).

**Current State:**
`MarkInstallStart` forces a full blocking Gen2 (`GC.Collect(2, …, blocking: true)`) and
then samples `GC.GetTotalMemory(forceFullCollection: false)`. `MarkInstallEnd` does the
opposite: it deliberately skips the collection ("No forced GC here: the delta should
reflect what we ACTUALLY hold after install, including any pinning effects") and samples
`GetTotalMemory(forceFullCollection: false)`. The two ends use **asymmetric measurement
forms**: a settled-heap baseline minus an unsettled-heap endpoint. The resulting
`InstallDeltaBytes` (and the `BytesPerHook` derived from it, and the `Severity` bucket,
and the Self-tab display) therefore conflate genuinely-retained state with transient
install garbage the GC had not yet collected at the sampling instant. The comment's
intent (capture pinning effects) is sound, but the chosen form does not isolate pinning —
it captures whatever Gen0/Gen1/Gen2 churn is incidentally live.

**Proposed Change:**
In `MarkInstallEnd`, force a full blocking Gen2 before sampling, exactly as
`MarkInstallStart` does:

```
GC.Collect(generation: 2, mode: GCCollectionMode.Forced, blocking: true);
ManagedHeapAtInstallEndBytes = GC.GetTotalMemory(forceFullCollection: false);
```

(or equivalently sample `GC.GetTotalMemory(forceFullCollection: true)` directly). The
method signature, the `InstalledHookCount` capture, and `IsInstalled = true` all stay.
The only change is that the endpoint is now a settled-heap measurement matching the
baseline's form.

**Justification:**
Direct evidence from a diagnostic test the audit wrote:
`Tests/HookInstallRetentionDiagnostics.cs` (run via
`Tests/Diagnostics/HookInstallRetentionDiagnostics.csproj`), two tests, both passing:

- `ForcedCollection_IsRepeatable` — two consecutive `GetTotalMemory(true)` samples on a
  stable live set agree to **0 KB drift**. The forced form is deterministic.
- `ForcedCollection_GivesStableRetainedMeasurement` — on a 16 MB rooted retained set,
  `GetTotalMemory(false)` reported a 24.3 MB delta while `GetTotalMemory(true)` reported
  32.2 MB: a **7.9 MB methodology spread (~50% of the retained set)** driven purely by
  which sampling form is used. (The forced form here read *higher* — the unstable form
  can swing either way; that non-determinism is exactly the point.)

**Honest limitation, recorded in the test's own doc-comment:** a first cut of the test
tried to prove `GetTotalMemory(false)` directly *over-reports* by leaving transient
garbage on the heap. It did not in a synthetic model — 2,000 short-lived `byte[24 KB]`
allocations blew the Gen0 budget mid-loop and were swept before the no-collection sample,
producing a 4% spread in the *opposite* direction. That outcome is the opposite of the
over-report claim and is documented in the fixture rather than hidden. The real MonoMod
install transient is large, promoted Cecil object graphs allocated over a multi-second
install — far more likely to sit in Gen2 (uncollectable without a Gen2) at the
`MarkInstallEnd` instant — so the methodology risk is real for the real workload even
though a cheap synthetic burst on this GC cannot reproduce the over-report direction. The
test therefore proves the narrower, still-load-bearing claim: the forced form is a stable,
repeatable retained-set measurement and the unstable form is not. The exact retained
per-hook number can only be obtained by an in-game install pass with a forced Gen2 on
both ends (blocked today by F4 — no off-game install harness).

**Expected Benefit:**
The reported install delta, `BytesPerHook`, and the `Severity` bucket become honest,
repeatable measurements of retained state rather than methodology-dependent snapshots.
This is the precondition for ever validating an install-path optimisation: you cannot
tell whether a future fix helped if the baseline measurement swings ±50% by GC timing.
Directly serves the Self-tab honesty contract (Invariant 3).

**Impact Assessment:**
Negligible behavioural impact, flagged. The change adds one forced Gen2 (~50-150 ms) at
`PostSetupContent` install time — the comment on `MarkInstallStart` already accepts this
exact cost once per session, so the second one is the same order. It runs once, off the
per-tick path, during a window (post-content-load) that is not frame-time-sensitive. It
does not change what is hooked, measured, or displayed beyond making the number stable.
The only observable difference is the Self-tab install-delta number shifting to its honest
value (which is the intent).

---

## F3 — README, code baseline, and live reality disagree on bytes-per-hook (~36 KB claimed vs ~60 KB observed) {#f3}

- [ ] Reconcile the public ~36 KB/hook claim (`README.md`) and the `BaselineBytesPerHook = 36 KB` code constant against the ~60 KB/hook the live 100-mod investigation observes; update the README claim to the honestly re-measured number once F2 lands, and decide whether `BaselineBytesPerHook` should track the new floor.

**Category:** Documentation Rot
**Severity:** Medium
**Effort:** Small
**Behavioural Impact:** None — README is not shipped/compiled; `BaselineBytesPerHook` only drives the Self-tab `Severity` colour, not measurement or game behaviour.

**Location:**
- `README.md:136` — "~36 KB of managed memory per installed hook … 50,000-hook kitchen-sink that's about 1.7 GB".
- `Profiling/ProfilerSelfHealth.cs:95` — `private const long BaselineBytesPerHook = 36L * 1024L;`.
- `Profiling/ProfilerSelfHealth.cs:84-89` — the comment's measured history (v0.5 38.0, v0.6.1 35.0, v0.7.x 36.8 KB/hook).

**Current State:**
The README tells players ~36 KB/hook (→ ~1.7 GB at 50k hooks). The code pins
`BaselineBytesPerHook = 36 KB` as the "healthy normal" the `Severity` bands measure
against. The live investigation reports ~60 KB/hook on a 100-mod / 152,310-hook stack
(→ ~9 GB). Three numbers, three sources, no agreement. Even discounting the F2
measurement gap, the live number is ~1.6× the documented baseline — which, by the code's
own `ConcerningRatio = 1.5`, should already be tripping **Concerning** severity, so the
drift is not merely cosmetic: the public claim understates the real footprint by enough
to cross the mod's own alarm threshold.

**Proposed Change:**
After F2 lands (honest re-measurement), capture the true retained per-hook number on a
representative large stack and: (1) update the README's ~36 KB and ~1.7 GB figures to the
re-measured values, keeping the README's existing upfront-about-scaling tone; (2) decide
whether to bump `BaselineBytesPerHook` to the new floor (the comment at line 88-89 already
documents this as the intended maintenance action: "Bump it when an intentional install-path
improvement lands … leave it alone if a per-hook regression slips in — then Severity
surfaces it"). If the ~60 KB is a *regression* not a new floor, leave the constant and let
Severity surface it — but the README must still be corrected to reality.

**Justification:**
Analytical + direct: the three sources are read directly (README line 136, code line 95,
the live investigation's reported 60 KB/hook). The mod's honesty contract (Invariant 3,
README "The honesty contract", and the Self-tab's whole premise) requires the public
cost claim to match measured reality. A profiler that under-reports its own cost by ~1.6×
violates exactly the trust posture it sells.

**Expected Benefit:**
The public cost claim, the code's severity baseline, and observed reality agree. Players
get an accurate footprint expectation; the Self-tab severity bands key off a baseline that
reflects current reality.

**Impact Assessment:**
None. The README is documentation (not shipped — `buildIgnore` excludes `*.md`).
`BaselineBytesPerHook` only feeds `ClassifySeverity` → the Self-tab colour; it does not
affect what is measured or how the game behaves. Changing it shifts only the amber/red
threshold on the Self tab, which is the intended effect.

---

## Cross-references

- `context/systems/hook-instrumentation.md:211` — `_installedHooks` process-scope retention already flagged as a watch item; this finding root-causes it.
- `context/notes/decisions.md:201` — "Mono.Cecil retained state is the suspected dominant cost" (v0.2 next-up); `decisions.md:25` lists "Cecil ILContext dispose" as a deferred win.
- `context/perf-pass/baseline.md:122` — target "Hook install delta < 80 MB at same coverage" (aspirational; the retained Cecil graph at 152k hooks makes this target unreachable without per-method coalescing or upstream MonoMod trimming).
- `potential-issues.md` P-1 — the structural ILContext-reclaim option that needs MonoMod cooperation or risky reflection.
- `build-and-tests.md` F4 — why the exact retained per-hook number cannot be measured off-game today.
