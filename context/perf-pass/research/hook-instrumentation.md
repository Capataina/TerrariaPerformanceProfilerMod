# Hook Instrumentation — Performance Research + Plan

> Scope: every file under `Profiling/` that participates in the install, teardown, or per-call path of timing detours: `HookInterceptor.cs`, `ILHookInterceptor.cs`, `HookBackend.cs`, `HookCategoryRouter.cs`, `HookCoverageView.cs`, `ProbeStack.cs`, `ProfilerSelfHealth.cs`.
>
> Headline pain: **233 MB install delta across 10,258 hooks (~23 KB / hook)**, climbing to 322–618 MB across reload cycles (baseline.md §2). Per-tick `PerformanceProfiler` cost is **0.27 ms** — the top contributor in its own session. Targets: install delta < 80 MB at same coverage, per-tick cost < 0.10 ms (baseline.md §6).
>
> Every recommendation in §4 below conforms to the five Project Invariants, the philosophy ("optimisation = doing what we already do at maximum efficiency"), and the explicit prohibition list in baseline.md §5. Nothing in this dossier proposes lowering coverage, sampling hooks, skipping signatures, or thinning the data stack.

---

## 1. Current state audit

A per-file walk through the hook-instrumentation surface. Every line reference is against current `main` (commit set ending 14fac59 / 15cb50a). Where a finding is later cited in §4 the rationale is anchored back to this section.

### 1.1 `HookBackend.cs` (107 LOC)

Three-mode dispatcher.

| Member | Role | Cost characteristics |
|---|---|---|
| `Mode` (`HookBackendMode`) | Build-time constant in current code (default `ILHook`, set at field init on L51). | None: a single `static int` read; the JIT inlines property access. |
| `AllocationTracking` | Determines whether IL emission inserts `GC.GetAllocatedBytesForCurrentThread()` calls (default `true`). | None per hook call; chooses one of two IL emission shapes at install time only. |
| `BackendCount`, `ILHookBackendId`, `PrimaryBackendId` | Routing knobs read at install and at every `ProbeStack.Leave` (via `HookBackend.ILHookBackendId`). | Three `static int` reads per `Leave`, fully inlinable. |
| `DelegateActive`, `ILHookActive` | Install-time gating. | None per call. |

There is **one footgun** here visible at L96: `ILHookBackendId` is a non-cached *property getter* that re-evaluates the conditional `_mode == HookBackendMode.Parallel ? 1 : 0` every single `ProbeStack.Leave[CpuAlloc]` call. With `_mode` immutable after install, this is a constant. Today the JIT can dead-store-eliminate this only if it sees through the static-property frontier; on .NET 8 with virtual dispatch this is usually inlined, but the branch *is still in the call path* and `_mode` is mutable in source (`set { _mode = value; }` on L80). A pure `const` or a one-time cached `static readonly int` after install would erase even the inlined compare from the hot path.

**No allocation; no per-tick CPU of any concern.** This file's leverage is *correctness*, not perf — but the mutable `_mode` + property-read shape is a deoptimisation hazard.

### 1.2 `HookCategoryRouter.cs` (48 LOC)

Single static method, eight `IsAssignableFrom` checks in sequence. Called **once per type** during install only — never on the hot path. Eight virtual-style runtime checks against eight base types per type processed. With ~10,258 hooks coming from ~1000–3000 types, that is ~20,000 `IsAssignableFrom` invocations at install time — sub-millisecond in aggregate. Not a perf concern.

The only concern is **mod-mismatch latency**: `IsAssignableFrom` traverses the type hierarchy each call, and the order of checks is not optimised for prevalence (e.g. `ModItem` is far more common than `ModBuff`, but `ModBuff` comes earlier in the chain by accident of authoring). Re-ordering by prevalence saves at most a few hundred µs across the install — a wash. **No structural change needed; flag for §4 if revisited.**

### 1.3 `HookCoverageView.cs` (84 LOC)

Read-only façade over the two backends' counter arrays. Used by the overlay's PROFILER HEALTH strip (every frame), the TREE tab's per-mod badge (1 Hz), and the JSON session writer (every session end).

`MeasuredHooks()` and `TotalHooks()` iterate the per-mod count arrays linearly each call. With ~20–40 mods in practice, each call is ~40 array reads + 40 adds, ~40 ns. Overlay calls these on every frame; 60 Hz × 40 ns = ~2.4 µs/sec — negligible.

The `MeasuredHookCounts` / `TotalHookCounts` properties return `IReadOnlyList<int>` indirectly. Both arrays are `int[]` already; the cast to `IReadOnlyList<int>` causes an interface dispatch through `Array`'s explicit interface implementations (each `[]` operator is a virtual call). At 40 mods × 60 Hz this is fine; at scale-out (a future "10 frames of coverage strip per second") it would matter. **Low priority.**

### 1.4 `ProbeStack.cs` (192 LOC) — the per-tick hot path

This is *the* hot file. Every hooked method dispatch fires `Enter` then `Leave` (or the alloc variants). The numbers from the per-mod attribution baseline imply on the order of **5 000–25 000 calls per tick** in a busy world; at 60 Hz that is **300 000–1 500 000 calls per second**, and the entire per-tick budget for `PerformanceProfiler` is 0.27 ms today.

Per-call structure today (Lite path, `Enter`/`Leave`):

| Step | Operation | Cost (rough) |
|---|---|---|
| E1 | `Frame[]? s = _stack;` — ThreadStatic load | ~3–5 ns (single-thread cached) |
| E2 | `if (s == null)` — null check + branch | <1 ns predicted |
| E3 | `else if (_depth == s.Length)` — array-length read + branch | <1 ns (predicted not taken at steady state) |
| E4 | `s[_depth].HookId = hookId;` — array element-of-struct write | ~2 ns |
| E5 | `s[_depth].StartTicks = Stopwatch.GetTimestamp();` — Stopwatch read | ~17 ns (per `_TempAllocBench` in `HookBackend.cs` L65) |
| E6 | `_depth++;` — ThreadStatic store | ~3–5 ns |
| L1 | `int d = _depth - 1; Frame[]? s = _stack;` | ~5 ns |
| L2 | bound check `(uint)d >= (uint)s.Length` | <1 ns |
| L3 | `_depth = d;` ThreadStatic write | ~3 ns |
| L4 | `Frame f = s[d];` — **24-byte struct copy** | ~3 ns |
| L5 | `Stopwatch.GetTimestamp() - f.StartTicks` | ~17 ns |
| L6 | `(uint)f.HookId < (uint)PerModAttribution.Hooks.Count` — Hooks is a `List<>`, `.Count` is a property | ~2 ns |
| L7 | `PerModAttribution.Hooks[f.HookId]` — `List<>.Indexer` — bounds-checked array access + struct copy of `HookDescriptor` | ~4 ns |
| L8 | `HookBackend.ILHookBackendId` — property getter with `_mode == Parallel ? 1 : 0` | ~1 ns |
| L9 | `PerModAttribution.Add(backendId, modId, categoryId, hookId, elapsed)` | (see §1.7) |

Total CPU before `PerModAttribution.Add`: ≈55 ns on Enter+Leave Lite path, with the two Stopwatch reads representing ~60% of it.

Specific structural concerns:

1. **`Frame` is 24 bytes** (`int HookId` + 4 padding + `long StartTicks` + `long StartAllocBytes`). When alloc tracking is OFF, `StartAllocBytes` is dead but still occupies cache. With 32-frame initial capacity, the stack array is 32×24 = **768 B**, fitting comfortably in L1d. Cache pressure is irrelevant at this size — but on growth doubling (rare) we re-copy. (Note: `Array.Copy` on 768 B is fine; the growth path is cold.)
2. **`HookDescriptor` lookup via `PerModAttribution.Hooks[f.HookId]`** does a `List<HookDescriptor>` indexer, which is `List<>.Item.get` — a property with a bounds check and an array read. The descriptor itself contains `ModId`, `CategoryId`, `DisplayName` (a string reference: 8 B managed pointer plus null padding); we copy the whole struct just to read two ints. **This is the single most-improvable per-call cost in `Leave`.** A parallel `int[] _hookModId` + `int[] _hookCategoryId` keyed by hookId would replace one struct-copy + reference touch with two L1d reads, around 6 ns saved per Leave. At 1M calls/sec that is 6 ms/sec — meaningful against the 0.10 ms/tick target.
3. **Two `Stopwatch.GetTimestamp()` calls** at ~17 ns each = ~34 ns of pure timing cost per hook. That is **the dominant per-call cost** and currently architecturally necessary (we need delta-time). The headline question for §4 is whether to keep two or compress to one using a TLS-stored "current frame entry timestamp" that the next `Enter` overwrites. Spoiler: no, because of nesting (the doc comment on L19–22 explains why; a stack is required).
4. **No re-entrancy guard.** If `PerModAttribution.Add` ever called back through a hooked method (it never does today; it is pure array writes), the stack would deepen. This is a watch-item, not a current cost.
5. **Defensive bounds-check at L105 (`Leave`):** the underflow guard. Correct per Invariant 4. Costs ~1 ns; would be foolish to remove.
6. **No `[MethodImpl(MethodImplOptions.AggressiveInlining)]`** on `Enter`/`Leave`. The methods are called via `call`, not `callvirt`, but the JIT does not auto-inline cross-assembly static calls unless explicitly hinted; from IL-edited bodies the dispatch is an absolute `call`. **Inlining is unlikely to help** here because the call is from an emitted IL site that has no inlining policy; the only way to inline would be to inline-emit the whole `Enter` body in the manipulator. That is technically possible but multiplies emitted-IL size by ~10× per hook (∴ Cecil memory blow-up; counterproductive). Skip.
7. **`_depth` and `_stack` are two distinct `[ThreadStatic]`**. Each ThreadStatic access goes through the runtime's thread-local lookup. Two lookups per Enter and per Leave = four lookups. Folding to one `[ThreadStatic] ProbeStackState? _state` with `{ Frame[] stack; int depth; }` would halve TLS lookups. The TLS lookup on .NET 8 is ~3–5 ns (a TLS slot read via `cpu`'s FS/GS register on x64, or equivalent on ARM64); halving = ~5–7 ns saved per call. **Concrete win** worth pursuing — see §4.

The **`EnterCpuAlloc` / `LeaveCpuAlloc` path** adds two additional costs:

- `GC.GetAllocatedBytesForCurrentThread()`: ~3.2 ns per call (per the comment on L64 of `HookBackend.cs`). One on Enter, one on Leave → ~6.4 ns extra.
- `Frame.StartAllocBytes` write/read: the byte slot was already in the struct (24 B), so no extra footprint.
- Total alloc-path per-call cost: ≈61 ns vs ≈55 ns Lite. The 6 ns gap is ~11% of per-call cost; whether it is worth a Lite/Standard/Deep config split is a higher-level decision (Deep is the spec default per `HookBackend.cs` L60).

### 1.5 `HookInterceptor.cs` (1224 LOC)

The delegate-pair backend. Dormant by default since b52f8b6 (ILHook is default) but kept compiled for `Delegate` and `Parallel` modes. Three structural costs:

1. **30+ `if (returnType == typeof(X) && p0 == typeof(Y))` branches in `TryHookSupportedOverride`** (L501–776). For every override discovered (10–20k per modlist), the install path walks this giant cascade once. Branch cost is trivial; what is *not* trivial is that **every call to `CreateProbe`** (L778) allocates a fresh `HookProbe` on the heap. `HookProbe` is 24 B (object header + 3 ints) + the delegate created by `new VoidHookWrapper(probe.Time)` which is ~64 B (Delegate header + target + methodPtr + methodPtrAux + _invocationList + _invocationCount). With ~10k installed hooks, that is ~880 KB of permanent heap retention from delegate objects alone in delegate mode.
2. **Per-hook closure capture via the delegate**. Every `new VoidHookWrapper(probe.Time)` creates a delegate bound to a unique `HookProbe` instance. The delegate retains the probe; the probe retains nothing but three ints. So total retained per hook: **~88 B** + the MonoMod `Hook` object MonoMod creates internally (~hundreds of bytes including a generated DynamicMethod for the wrapper). The headline number on baseline is 23 KB/hook in IL mode, so delegate-mode RAM is *lower per hook* than IL mode — but its coverage is 71.6%, so this is a coverage/RAM trade.
3. **30 `Time*` methods on `HookProbe`** (L826–1222) — each is identical structurally (Stopwatch, try/orig/finally/Add). They differ only in signature. This is acceptable boilerplate, not a perf issue, but it is a code-bloat issue. Whether it survives the pass at all depends on whether `Delegate` mode is still load-bearing post-pass; if it is purely an "archived fallback" (per L48–50 of `HookBackend.cs`), some of this code can become `#if PROFILER_KEEP_DELEGATE_BACKEND` — out of scope for raw perf, but a code-health win.

Most-significant install-path cost in this file is **not in the supported branches** — it is in the **walk** that surfaces methods to be considered, in `InstallForMod` L352 and `HookSupportedOverrides` L402:

- `AssemblyManager.GetLoadableTypes(mod.Code)` is invoked once per mod. tModLoader caches this; cost is bounded (~µs).
- `type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)` — reflection-heavy. For ~10k types, this is the dominant install-time cost in delegate mode.
- `method.GetBaseDefinition()` — used to detect overrides. Costs ~1 µs each; with ~50k methods walked, that is ~50 ms.

None of these are runtime concerns; they bound the install time. Aggregate install duration in delegate-only mode is single-digit seconds (not measured in `baseline.md` — flag as **needs benchmark**).

### 1.6 `ILHookInterceptor.cs` (569 LOC) — the production install path

The file responsible for the headline 233 MB RAM cost. Three big buckets of cost.

**(A) Install-time CPU.** Same reflection-driven type-walk as `HookInterceptor`, plus an additional pass through closed-generic inheritance (L282 onwards walks with `BindingFlags.DeclaredOnly = false`). The closed-generic dedup set `_instrumentedHandles` (L93) bounds duplicates correctly but every visited method still pays:

- `method.GetBaseDefinition()` (L400): ~1 µs each
- `method.GetMethodBody()` (L357): ~5–20 µs depending on body size — this is the API that reads CIL bytes from the assembly's metadata; .NET caches this per-method internally so subsequent reads of the same MethodInfo are cheap, but cold-path cost is real.
- `PerModAttribution.RegisterOrReuseHook(modId, categoryId, displayName)` (L377): in Parallel mode this is **O(n) over `_hooks`** (linear search through up to 10k descriptors). For 10k registrations this is **50 million comparisons**, plus string equality on `DisplayName`. This is dead obvious quadratic install behaviour in Parallel mode; in single-backend mode it falls back to `RegisterHook` which is O(1).
- `DisplayName(type, method)` (L404) allocates 3 strings via `$"..."` interpolation each call → ~10k × ~80 B = ~800 KB of strings retained in `HookDescriptor`. Modest.
- `new ILHook(method, manipulator, applyByDefault: true)` (L441): **the heavyweight**. See §3.2 for what this allocates internally.

**(B) Install-time allocation.** Sources, with rough magnitudes per hook (justified by §3 research):

| Source | Bytes/hook (est.) | Total at 10k hooks |
|---|---|---|
| `ILHook` instance + chain entry | ~200 B | 2 MB |
| `DynamicMethodDefinition` per hook (Cecil `ModuleDefinition`, `MethodDefinition`, instructions list, importer caches) | ~5–12 KB | 50–120 MB |
| Generated `DynamicMethod` (DMD output via SRE or Cecil DMD) | ~2–4 KB | 20–40 MB |
| MonoMod trampoline / native code blob | ~512 B–2 KB | 5–20 MB |
| `HookDescriptor` + `DisplayName` string | ~120 B | 1.2 MB |
| `_installedHooks` `List<ILHook>` capacity | 8 B × 10k | 80 KB |
| `_instrumentedHandles` HashSet | ~16 B × N | <1 MB |
| Per-method body anchored from inside `ILHook.Active`'s `ILContext` | varies | overhead |
| **Headline total estimated** | | **80–180 MB** |

The measured 233 MB ÷ 10,258 hooks ≈ 23 KB/hook lines up with the upper end of this estimate. The single largest contributor is **the `Mono.Cecil.ModuleDefinition` retained per hook**, which carries a complete importer cache (`CachedTypes`, `CachedMethods`, `CachedFields`, `CachedAsms`, `CachedModuleTypes` — confirmed at §3.1). Disposing of these would shave a large fraction of the install delta.

**(C) Per-hook IL emission shape.** This is what gets baked into every hooked method body. The current shape is:

```
prologue:
  ldc.i4   <hookId>
  [ if AllocTrack:  call GC.GetAllocatedBytesForCurrentThread ]
  call     ProbeStack.{Enter|EnterCpuAlloc}
  // (original body, with `ret`s rewritten as below)
  ...
  stloc    retLocal          ; on non-void ret
  leave    afterHandler
  ...
handlerStart:
  call     ProbeStack.{Leave|LeaveCpuAlloc}
  endfinally
afterHandler:
  ldloc    retLocal           ; on non-void
  ret
```

Per hook the inserted IL is: Lite ~5 instructions; Alloc ~7 instructions; plus the per-`ret`-rewrite work (~2 extra instructions per `ret` site). The IL size grew per hook: not the bottleneck for memory (the Cecil module is far larger than the IL bytes), but **it does affect JIT-time CPU** the first time each method is called after install — every hook is a fresh JIT.

**Critical lifecycle hazard at L79.** `_installedHooks = new List<ILHook>()` is `static`. Every `ILHook` is retained for the process lifetime *unless* `Uninstall` runs. On `Mod.Unload`, `Uninstall` (L190) disposes each. But the question that matters: **does MonoMod actually free the underlying Cecil module when the ILHook is disposed?** From §3.1's research the answer is **yes via `DynamicMethodDefinition.Dispose() → Module.Dispose()`** for the chain's `SourceCloneIl`, but the `ILHookEntry`'s `LastContext` (`ILContext`) is disposed only on `Remove` — and the chain's `Active` collection's contexts persist for the life of the hook (per the DetourManager research in §3.2). So *during a session* every hook holds at least one live Cecil module. The 233 MB is **steady-state during a world**, not a "leak after teardown" — it is the cost of running the instrumentation.

**Closed-generic dedup logic at L337** is correct but the `RuntimeMethodHandle` HashSet is allocated once and lives for the install run — not a perf issue.

### 1.7 Cross-reference: `PerModAttribution.Add` (Profiling/PerModAttribution.cs L213)

Out of scope file but **called from `ProbeStack.Leave`** on every hook. Quickly:

- One `(uint)backendId >= ...` check
- One `(uint)categoryId >= ...` check
- `int index = modId * CategoryCount + categoryId` (CategoryCount = 7 constant)
- One `long[]` per-mod-category add
- One bounds-checked `hookTicks[hookId] += elapsed`

Total ~6 ns per Add call. Solid. No leverage here.

### 1.8 `ProfilerSelfHealth.cs` (227 LOC)

Not on the install or per-call hot path beyond `Refresh()` which is gated to 1 Hz (L62 `RefreshIntervalTicks = 60`). Cost: one `Process.Refresh()` + `WorkingSet64` + `GC.GetTotalMemory(false)` per second. The `Process.Refresh()` is the expensive one — on macOS it goes through `proc_pidinfo` which is single-digit ms on a healthy system. Once per second, negligible.

One **install-time concern**: `MarkInstallStart` (L138) does `GC.Collect(generation: 2, mode: GCCollectionMode.Forced, blocking: true)`. The comment says ~50–150 ms once; that is fine for a one-time install. But this collection runs *before* hook install, so the baseline is clean. **No change recommended.**

Latent concern: `InstalledHookCount` is the **input parameter** to `MarkInstallEnd`, supplied by the caller (likely `ProfilerSystem`). If the caller passes only `HookInterceptor`'s detour count and not `ILHookInterceptor`'s, `BytesPerHook` is off. **Audit follow-up** — out of scope here, flagged.

### 1.9 Summary inventory of per-call costs

| Surface | Today (Lite) | Today (Alloc) | Leverage available |
|---|---|---|---|
| `ProbeStack.Enter` | ~25–30 ns | ~30–35 ns | TLS folding, `Frame` repacking, `MethodImpl` hint |
| `ProbeStack.Leave` | ~25–30 ns | ~30–35 ns | Skip `HookDescriptor` struct-copy (parallel `int[]`), TLS folding |
| `Stopwatch.GetTimestamp()` × 2 | ~34 ns | ~34 ns | Architecturally fixed |
| `GC.GetAllocatedBytesForCurrentThread()` × 2 | — | ~6 ns | Architecturally fixed |
| `PerModAttribution.Add` | ~6 ns | ~8 ns | Already tight |
| **Per-call total** | **~55 ns** | **~75 ns** | **Achievable: ~40 ns / ~55 ns** |

At an estimated ~50k hook calls/tick on the dev machine (0.27 ms / 5 ns avg ≈ 54k), shaving 15 ns/call saves ~0.05 ms/tick. That alone moves the per-tick cost from 0.27 → 0.22 ms. The rest of the gap to the 0.10 ms target must come from elsewhere — the most likely source is the **JIT cost amortised over the session** and the **GC pressure** introduced by `string` interpolation in the install path (one-time, but reflects in GC events at world-enter).

---

## 2. Measured baseline

Numbers from `context/perf-pass/baseline.md`, `context/notes/decisions.md` L133, and the system canonical doc.

### 2.1 RAM

| Metric | Value | Source |
|---|---|---|
| `PerformanceProfiler` Mod-RAM in session `6a0dcea5` | **234 MB** | baseline §2 |
| Hook install delta, first install | **481 MB** | baseline §2 |
| Hook install delta range across reloads | **322–618 MB** | baseline §2 |
| Hooks installed | **10,258** | baseline §2 |
| Bytes / hook (target divisor) | **~23–60 KB** | baseline §2 |
| Earlier 18-mod session | **562 MB install / 56 KB/hook** | decisions.md L133 |
| Projected 40-mod kitchen-sink | **~1.5 GB** | decisions.md L133 |

### 2.2 Per-tick CPU

| Metric | Value | Source |
|---|---|---|
| `PerformanceProfiler` average ms/tick (top contributor) | **0.27 ms** | baseline §2 |
| `PerformanceProfiler` peak ms/tick | **0.3 ms** | baseline §2 |
| `PerformanceProfiler` total ms over 4 min 55 s | **4,488 ms** | baseline §2 |
| End-of-session UiOverlayBlocking cluster | **40 stalls / 8.5 s, profiler-attributed** | baseline §2 |

### 2.3 Target deltas (baseline §6 — load-bearing for §4 below)

| Surface | Today | Target | Delta required |
|---|---|---|---|
| Hook install delta | 233 MB / 10,258 hooks | **< 80 MB** at same coverage | **−65%** |
| Avg per-tick PerformanceProfiler cost | 0.27 ms | **< 0.10 ms** | **−63%** |
| End-of-session main-thread stall | 8.5 s | **0** (off-thread) | full (Persistence subsystem, not us) |

### 2.4 Unmeasured today — flagged for benchmarks

- Install duration (wall-clock end-to-end). Not in baseline. Estimated 5–15 s.
- Per-call cost of `Enter` and `Leave` individually under microbenchmark conditions. The 55 ns figure in §1.9 is structurally argued, not measured.
- Cecil module retention specifically — how much of the 233 MB is Cecil vs DynamicMethod bodies vs trampolines vs `HookDescriptor`. **The most important needs-benchmark item.** Recommended: a one-shot diagnostic mode that runs install, then GCs and prints `GC.GetTotalMemory` plus a heap snapshot via `dotnet-counters` or a forced `WriteHeapDump`.
- Per-hook JIT cost at first dispatch (the implicit world-enter freeze contributor).
- Cost of `RegisterOrReuseHook` in Parallel mode (suspected O(n²); see §1.6 (A)).
- IL emission size per hook (instruction count is known, byte-equivalent not measured).
- GC stats during install (Gen2 count, alloc rate). The `MarkInstallStart` forced Gen2 sets a clean baseline but doesn't tell us the install's own alloc pressure.

---

## 3. tML / MonoMod / Cecil background

External research, cited per source. **All findings verified against the master branches as of 2026-05-20.**

### 3.1 Mono.Cecil — what gets retained per `ModuleDefinition`

`Mono.Cecil` is the .NET assembly read/write library. MonoMod's `DynamicMethodDefinition` is the unit of per-method IL editing it exposes; internally it builds a *fresh* `ModuleDefinition` per `DynamicMethodDefinition` instance.

From the official Cecil sources (`cecil/Mono.Cecil/ModuleDefinition.cs`, master): a `ModuleDefinition` aggregates:

- Type system reference (`TypeSystem`)
- Symbol reader (PDB or empty)
- Reflection importer (`IReflectionImporter`) — *cached per-module*
- Metadata importer
- A `ReaderParameters.InMemory` flag determining whether the source assembly's bytes are mirror-buffered

From the Mono.Cecil FAQ (mono-project.com): *"Cecil 0.10 only reads some metadata sections in memory, and the rest is read from the underlying stream"*. **However**, that does not apply to dynamically-created modules — those *are* fully in memory, by construction.

From `MonoMod.Utils/MMReflectionImporter.cs` L50–66 (read above), every Cecil ModuleDefinition created via `MMReflectionImporter.ProviderNoDefault` carries five caches:

```
Dictionary<Assembly,    AssemblyNameReference>  CachedAsms
Dictionary<Module,      TypeReference>          CachedModuleTypes
Dictionary<Type,        TypeReference>          CachedTypes
Dictionary<FieldInfo,   FieldReference>         CachedFields
Dictionary<MethodBase,  MethodReference>        CachedMethods
```

Each `TypeReference` is a managed object with metadata. Each `MethodReference` is ~hundreds of bytes including parameter references. With a typical hook (`ModNPC.AI`) referencing maybe 20–30 distinct types and 50–100 distinct methods, **each module's importer alone is 5–15 KB** of retained managed memory.

The `Module.Dispose()` path frees these caches. `DynamicMethodDefinition.Dispose()` (L271 of the Utils file) calls `Module?.Dispose()`. MonoMod's chain (see §3.2) holds the `DynamicMethodDefinition` for the hook's lifetime — so until the `ILHook` is `Dispose`'d, the module and its caches are retained.

**Memory-leak surface in `DefaultAssemblyResolver`** (per `mono-cecil.narkive.com/0v7kfiQu`): the default resolver keeps a private cache of all loaded `AssemblyDefinition` objects and *provides no API to purge*. **MonoMod's `MMReflectionImporter.ProviderNoDefault` short-circuits this by not using the default resolver chain** (the "NoDefault" provider does not register with `DefaultAssemblyResolver`). Mitigated structurally.

But: `Cecil 0.10` cached small method bodies (`MethodBody`) at module level after the Eugene Rozenfeld optimisation. For our case, this is downside not upside — we're creating one module per ILHook, so cross-method caching never has a chance to amortise.

**Conclusion.** The Cecil per-module footprint is the dominant pillar of the 23 KB/hook number. A `ModuleDefinition` retained per hook ≈ 5–15 KB of metadata caches + ~1–4 KB of `MethodBody`/`Instructions` collection storage. At 10k hooks: **60–190 MB**. This matches the measured 233 MB minus the DynamicMethod+trampoline contribution.

### 3.2 MonoMod RuntimeDetour — what `ILHook` retains

From `src/MonoMod.RuntimeDetour/ILHook.cs` (read above) plus `src/MonoMod.RuntimeDetour/DetourManager.Managed.cs` (read above):

An `ILHook` instance:

- Holds a `SingleILHookState` (factory, config, manipulator, ordering metadata)
- Registers itself with a per-method `ManagedDetourState` keyed off the target `MethodBase`
- Multiple hooks on the same method **share** a single `ManagedDetourState` and a single `SourceCloneIl` (`DynamicMethodDefinition`) — confirmed at DetourManager.Managed.cs L223–250

Each `ManagedDetourState`:

- One `SourceClone` `MethodInfo` — the JIT'd "clean copy" of the original method body
- One `SourceCloneIl` `DynamicMethodDefinition` — **the Cecil module we just argued about** — held for the chain's lifetime
- A `DepGraph<ManagedChainNode>` of detour-ordering nodes
- An `ILHookEntry` per attached ILHook, each carrying `CurrentContext` and `LastContext` (`ILContext`) instances

Each `ILContext`:

- Wraps a `MethodDefinition` from the active chain DMD
- Holds an `ILCursor` view and the per-manipulator scratch
- Disposed only when the `ILHookEntry` is removed (`Remove`) or the chain is rebuilt

When ANY hook on a method changes (added, removed, reordered), `Refresh()` runs:

```csharp
foreach (ILContext il in Active) il.Dispose();
Active.Clear();
using var dmd = new DynamicMethodDefinition(SourceCloneIl);  // a NEW DMD copied from the source
var il = new ILContext(def);
// ... run manipulators ...
```

So each refresh allocates **another** transient `DynamicMethodDefinition`, runs the manipulator chain, generates a new dynamic method, and replaces the active dispatch target.

**Critical implication for our 233 MB.** In single-ILHook-per-method mode (our scenario — we attach exactly one ILHook per discovered override), the retained state per hooked method is:

1. The `ManagedDetourState.SourceCloneIl` (~5–15 KB Cecil module + body)
2. The current `Active`/`LastContext` (~5–15 KB another Cecil module, the working copy used to emit the generated dynamic method)
3. The generated `DynamicMethod` and its IL bytes (~2–4 KB)
4. The trampoline/native code blob (~512 B–2 KB depending on platform)
5. Our `_installedHooks` slot (8 B) plus the `ILHook` object itself (~200 B)

**Two Cecil modules per hook**, not one — explains why we land at the upper end of the prior estimate. At ~10–30 KB combined Cecil overhead × 10k hooks = **100–300 MB of Cecil alone**, consistent with measured.

From the README-RuntimeDetour: *"ILHooks, like other kinds of detours, are automatically undone when the garbage collector collects the object, or the object is disposed."* — i.e. the ILHook chain is anchored by our `_installedHooks` list, and only `Dispose()` (called from our `Uninstall`) frees the chain.

`TrampolinePool` (file `src/MonoMod.RuntimeDetour/TrampolinePool.cs`) is a pool of executable native trampoline pages. Single-page allocations are coarse (often page-size, 4 KB); the pool amortises across hooks, but with 10k hooks the trampoline alloc is still a substantial native footprint (~5–20 MB managed-side accounting via VM mappings).

### 3.3 DMD generator paths

From `DynamicMethodDefinition.cs` L240–290 (read above), `Generate()` chooses between three backends based on environment switches:

- `DMDEmitDynamicMethodGenerator` — produces a `DynamicMethod` via `System.Reflection.Emit.DynamicMethod` + `DynamicILGenerator`. **Default on modern .NET** (our case, .NET 8 + tModLoader). The output is a JIT-able dynamic method.
- `DMDEmitMethodBuilderGenerator` — produces via a `TypeBuilder.DefineMethod` in a fresh `AssemblyBuilder`. Higher footprint per emission (whole module + type wrapper), used when `Debug = true`.
- `DMDCecilGenerator` — purely via Cecil + load. Used on legacy Mono.

`DMDEmitDynamicMethodGenerator` retention per dynamic method:

- The `DynamicMethod` object (managed, ~hundreds of bytes)
- The native JIT-compiled code (~variable, often 1–4 KB)
- Pinned globals for any references hoisted into the dynamic method (`DynamicReferenceManager`)

These are *separate* from the Cecil retention. They are the second-largest hook-instrumentation cost class.

### 3.4 tModLoader hook surface

Confirmed from `patches/tModLoader/Terraria/ModLoader/NPCLoader.cs` (read above): tML uses `GlobalHookList<TGlobal>` (e.g. `using HookList = Terraria.ModLoader.Core.GlobalHookList<Terraria.ModLoader.GlobalNPC>;`). Each hook is a `HookList`, e.g. one for `OnSpawn`, one for `AI`, one for `OnKill`. Dispatch is via `foreach (var npc in hook.Enumerate(npc)) { npc.Hook(...); }`.

The hooks **we discover** are the overrides on every `Mod*` / `Global*` content class. tML's dispatch surface is one method per hook name on the *Loader* class (a static dispatcher); our hook target is the *Override* on the Mod*/Global* class.

**Two architecturally different instrumentation targets exist:**

1. **What we do today (per-override hooks).** Every override across every mod gets its own ILHook. Coverage of "this specific implementation cost N ticks". Granular. **10,258 hooks at our test modlist.**
2. **Dispatch-level hook (alternative we don't use).** Hook `NPCLoader.NPCAI` once, capture the dispatcher's foreach; per-mod attribution comes from `npc.GetType().Assembly`. **One hook per dispatcher**, ~hundreds total. Drops per-override granularity (we know "all of mod X's AI cost N", not "mod X's `SuperSlime.AI` cost N").

Our docs (`ilhook-migration-plan.md`) considered dispatch-level hooks. We rejected the loss of per-override granularity. **This is locked-in design and not revisited here** (it would be a "do less" change per the philosophy clause), but it is critical context for understanding why we have 10k hooks at all.

From `MonoModHooks` per tML internals: `MonoModHooks.Add(MethodBase, Delegate)` is the official wrapper around `RuntimeDetour.Hook`, and `MonoModHooks.Modify(MethodBase, ILContext.Manipulator)` is the wrapper around `RuntimeDetour.ILHook` (but it returns void — which is why we construct `ILHook` directly per `ILHookInterceptor.cs` L36–41).

### 3.5 .NET 8 IL emission and JIT cost

The first dispatch of every hooked method triggers a JIT compilation of the generated `DynamicMethod`. For 10k methods this is the bulk of the world-enter freeze (172 ms max frame in `baseline.md` §2). The JIT cost per method is roughly proportional to **emitted IL size + body size**. Our 5–7 instruction prologue adds ~1% to each body's JIT cost, but multiplied across 10k methods this is non-trivial first-dispatch cost.

There is no platform escape hatch to "precompile" the dynamic methods ahead of time (no AOT for `DynamicMethod`). The mitigation if we ever want one is **batched JIT warmup**: after install, walk the descriptors and force JIT compilation off-thread via `RuntimeHelpers.PrepareMethod`. *Not free*: it moves the freeze, not its sum. Worth investigating only if the world-enter freeze becomes a player-visible regression beyond what is already present.

---

## 4. Optimisation opportunities — categorised

Each opportunity is numbered, classified by category, gets a rationale anchored to §1–§3, an implementation sketch, an expected delta, risks, verification, and a § stating the invariant-compliance argument.

### Hot-path CPU

#### CPU-1 — Fold `_stack` and `_depth` into one `ThreadStatic` state object

- **Anchor:** §1.4 point 7. Two ThreadStatic accesses per `Enter` and per `Leave` = four TLS lookups per hook dispatch.
- **Sketch:** Replace `[ThreadStatic] private static Frame[]? _stack; [ThreadStatic] private static int _depth;` with `[ThreadStatic] private static ProbeStackState? _state;` where `ProbeStackState { public Frame[] Stack; public int Depth; }`. Sole TLS lookup pulls both. Note: this is a class, not a struct, *because* `ThreadStatic` cannot reliably hold a struct without boxing; one object per thread is OK (single alloc on first hook).
- **Expected delta:** −5 to −7 ns per Enter+Leave pair → at 50k calls/tick, **~0.05 ms/tick saved**, ~20% of the per-tick gap.
- **Risks:** None functional. One-time allocation on first hook on each thread (instead of two arrays). Slight ergonomics regression (more indirection in the per-call path; `_state.Stack[_state.Depth].HookId = ...` is one more deref) — but the deref is *into the same object* and stays in L1d after first touch.
- **Verification:** Add an xUnit micro-benchmark in `Tests/` that exercises `Enter`/`Leave` 1M times against the existing implementation and the new one, on a single thread. Targets a 10%+ reduction in elapsed.
- **Invariants:** None affected.

#### CPU-2 — Drop the `HookDescriptor` struct-copy in `Leave`

- **Anchor:** §1.4 point 2.
- **Sketch:** Keep `PerModAttribution.Hooks` (we still need the strings for the UI), but additionally hold two parallel arrays `int[] _hookModId` and `int[] _hookCategoryId` keyed by hookId. `Leave` reads `_hookModId[f.HookId]` and `_hookCategoryId[f.HookId]` directly instead of `PerModAttribution.Hooks[f.HookId]`. These arrays are sized once at `RegisterHook` / resized via `Array.Resize` like the existing tick-data arrays.
- **Expected delta:** −5 to −6 ns per Leave call → **~0.025 ms/tick saved** at 50k calls/tick.
- **Risks:** Three sources of `HookDescriptor` writes (RegisterHook, RegisterOrReuseHook) need parallel array updates. Trivial.
- **Verification:** Same microbenchmark harness. Plus an existing-test snapshot to confirm UI/JSON output unchanged.
- **Invariants:** None affected.

#### CPU-3 — Make `HookBackend.ILHookBackendId` a compile-time-resolved constant after install

- **Anchor:** §1.1 footgun discussion.
- **Sketch:** Introduce `internal static int CachedILHookBackendId` set once at the end of `HookInterceptor.Install` / `ILHookInterceptor.Install`. `ProbeStack.Leave[CpuAlloc]` reads the cached field, not the property.
- **Expected delta:** −0.5 to −1 ns per Leave call. Marginal but free.
- **Risks:** Cache invalidation if `Mode` is mutated mid-session — but `Mode` is documented (HookBackend.cs L73) as "Changes take effect on the next mod reload", so this is contract-aligned.
- **Verification:** Compile-time only; existing tests confirm correctness.
- **Invariants:** None affected.

#### CPU-4 — Inline `Frame` repacking + remove the `StartAllocBytes` slot in Lite mode

- **Anchor:** §1.4 point 1.
- **Sketch:** Make `Frame` a 16-byte struct (`int HookId; long StartTicks;` with explicit padding) in Lite mode. Compile-time-switch this: when `AllocationTracking` is the default-on production path, keep the 24-byte form. When `AllocationTracking` is off (future Lite mode), drop `StartAllocBytes` entirely. Achievable via a generic `Frame<TAllocSlot>` pattern, or simpler: a second `LiteFrame` struct and a second stack path.
- **Expected delta:** Cache-line packing for thread-local stack array. Marginal at our nesting depths (<8); meaningful only if a mod's call stack reaches 32+ frames. **Defer until Lite mode lands.**
- **Risks:** Two code paths; maintenance cost rises. Until Lite is real, this is theoretical.
- **Verification:** Defer.
- **Invariants:** None affected.

### Allocations on install / runtime / unload

#### ALLOC-1 — Dispose `DynamicMethodDefinition` immediately after `ILHook` install, holding only the chain's `SourceCloneIl`

- **Anchor:** §3.2 — `Active` collection holds a `DynamicMethodDefinition` whose `Module` is a Cecil module of ~5–15 KB. After install, the per-hook `ILContext` is only needed when the chain is *re-built* (another hook is added or removed for the same method). In our use case, **we add exactly one hook per method and never modify the chain after install** — so the `Active` context's Cecil module is dead weight from the moment install completes.
- **Sketch:** After every `ILHook` constructor returns successfully, traverse via `ILHook.HookInfo` (or reflection if no public surface) to its `ManagedDetourState`'s `Active`/`LastContext` and dispose those. The chain remains functional because the dispatch target (the generated `DynamicMethod`) is already live and does not need the source Cecil tree to keep running. Requires reading MonoMod's public surface carefully to find a supported way to drop the active context without breaking the chain.
- **Expected delta:** If the `Active` `ILContext` carries one of the two per-hook Cecil modules (§3.2 conclusion), this saves **~5–15 KB per hook = 50–150 MB** at our test modlist. **Single largest install-RAM win available.**
- **Risks (high).** MonoMod's chain may need the context to support later `Refresh()`. If a future `MonoModHooks` API call refreshes the chain (e.g. another mod attaches a hook to the same method), we'd hit a NRE. Mitigation: instead of disposing the existing context, *re-acquire* it on demand by holding only the `Hook` reference. The exact contract has to be confirmed by reading `MonoMod.RuntimeDetour.DetourManager.Managed.cs` `Refresh()` logic carefully — preliminary read (§3.2) shows `Refresh` creates a *new* `DynamicMethodDefinition` from `SourceCloneIl` each time, so the per-hook `Active` should be transient. If true, the win is safe.
- **Verification:** **Needs benchmark.** Pre/post `MarkInstallEnd` heap snapshot. Also a Parallel-mode session to confirm the chain still functions after a second hook is added to a target — i.e. the chain doesn't NRE on re-`Refresh`. The Parallel-mode test is the safety net.
- **Invariants:** None affected if the chain remains functional. Invariant 4 (abort-clean) demands we wrap the disposal in try/catch and treat any exception as "skip the optimisation for this hook" — never let install fail.

#### ALLOC-2 — Force a Gen2 + `LOH` compaction immediately after install

- **Anchor:** §1.6 (B) — transient install scratch (`ParameterInfo[]` reflection arrays, string-builder buffers from `DisplayName`, `_unsupportedHookSamples` lists in delegate path) is eligible for collection but stays in Gen2 segments until something triggers compaction.
- **Sketch:** In `ILHookInterceptor.Install` (and `HookInterceptor.Install`), after the loop completes successfully:
  ```csharp
  System.Runtime.GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
  GC.Collect(generation: 2, mode: GCCollectionMode.Aggressive, blocking: true, compacting: true);
  ```
- **Expected delta:** −20 to −50 MB of transient retention. Magnitude depends on how much install scratch outlives the install pass.
- **Risks:** One-time cost ~100–300 ms during world-load. Already in a world-enter freeze window; not player-visible as a new stall. Aggressive GC is a heavier blocking pause; if it pushes the world-enter spike over a noticeable threshold, switch to `Default` mode.
- **Verification:** Compare `MarkInstallEndBytes` before/after. Add a temporary `Logger.Info` line emitting `GC.GetTotalMemory(true)` immediately before and after the forced collection.
- **Invariants:** None affected. Read-only.

#### ALLOC-3 — Pool / intern `DisplayName` strings

- **Anchor:** §1.6 (A) — `DisplayName(type, method)` allocates 3 strings per call via `$"..."`. At 10k hooks: 30k allocations during install. Each `HookDescriptor.DisplayName` retains its string for the session.
- **Sketch:** Intern the strings via `string.Intern` *only* for the descriptor name (not for the transient parts of the format string). Or, more cheaply, pre-build a `Dictionary<(Type,MethodInfo), string>` to reuse identical names across the closed-generic-inheritance pass where the same `MethodInfo` surfaces multiple times. (The dedup set already prevents double-instrument, but the `DisplayName` allocation still happens before the dedup check — see ILHookInterceptor.cs L375.)
- **Expected delta:** ~500 KB to ~1.5 MB saved on install. Small but free.
- **Risks:** None.
- **Verification:** `_measuredOverrides` count unchanged; install heap delta drops.
- **Invariants:** None affected.

#### ALLOC-4 — Eliminate quadratic `RegisterOrReuseHook` in Parallel mode

- **Anchor:** §1.6 (A) — linear search through `_hooks` for every registration.
- **Sketch:** Maintain a `Dictionary<(int modId, int categoryId, string displayName), int>` keyed lookup in `PerModAttribution`, populated on `RegisterHook`. `RegisterOrReuseHook` does a single dictionary lookup instead of a linear scan.
- **Expected delta:** In Parallel mode at 10k hooks, this drops install registration from ~O(n²) = 50M comparisons to O(n). Install time savings: **multiple seconds in Parallel mode**. Zero impact in single-backend modes (where `RegisterHook` is already O(1)). But Parallel is a dev tool — and the prohibition list doesn't preclude this — and the speedup matters if we ever ship Parallel as a player-facing diagnostic.
- **Risks:** Dictionary lookup is slower per-op than two int comparisons; but at 1 vs n scale that is irrelevant.
- **Verification:** Add a Parallel-mode install benchmark.
- **Invariants:** None affected.

#### ALLOC-5 — Pre-size `_installedHooks` and `_instrumentedHandles` based on a pre-pass type-count estimate

- **Anchor:** §1.6 (A) — `_installedHooks` grows via `List.Add` from default capacity. Each capacity doubling copies the array. At 10k hooks, this is ~14 reallocs / copies; final array is ~80 KB.
- **Sketch:** Walk all profiled mods first, sum `mod.Code.GetTypes().Length` as an upper-bound estimate, multiply by ~5 (heuristic for overrides per type), and pre-size both collections.
- **Expected delta:** ~14 reallocs avoided, ~few hundred KB of transient garbage saved. Marginal.
- **Risks:** Over-estimation wastes ~megabytes of capacity. Under-estimation reverts to default growth.
- **Verification:** Existing tests; instrumentation install logs.
- **Invariants:** None affected.

### RAM retained

#### RAM-1 — Move the `ILHook` chain's source-clone Cecil module to a shared pool

This is the same idea as ALLOC-1 but framed as **multi-method amortisation**. ALLOC-1 disposes the per-hook context; RAM-1 asks whether MonoMod could share a single Cecil module across all hooks on the same dispatch target. It is **out of scope for our codebase** (we'd be modifying MonoMod), but worth a memo:

- **Anchor:** §3.2 — every `ManagedDetourState` has its own `DynamicMethodDefinition`. Methods cannot share modules because each module references its own importer state.
- **Action:** No code change; we file the concern. If the Cecil retention reaches a project-killing level (e.g. >1 GB at 40-mod scale), the right path is **(a)** a PR to MonoMod to investigate per-module sharing or **(b)** a fork of the install path that uses our own `DynamicMethodDefinition` lifetime management. Both out of scope today.

#### RAM-2 — Drop the per-hook generated `DynamicMethod` source-clone after first dispatch

- **Anchor:** §3.3 — the `SourceClone` `MethodInfo` is the "clean copy" used as the trampoline target; the dispatcher target is the *patched* `DynamicMethod`. If MonoMod retains the unpatched clone unnecessarily, that's another ~2–4 KB × 10k = 20–40 MB.
- **Action:** Read MonoMod's `DetourManager.Managed.cs` more carefully (§3.2 has the relevant code) to confirm whether `SourceClone` is needed for chain `Refresh`. **Needs benchmark + research.** Filed.

#### RAM-3 — Bound `_installedHooks` retention to the world

- **Anchor:** §1.6 — `_installedHooks` is static, retained across `Mod.Unload` only because `Uninstall` clears it. The "Known Issues / Active Risks" §3 of the system doc (page 211) flags that if `Mod.Unload` ever fails to fire, stale state persists.
- **Sketch:** Re-anchor `_installedHooks` to an instance of `ILHookInterceptor` owned by `ProfilerSystem`, freed on world unload (after a final flush). This makes the GC behaviour easier to reason about. Out of scope for the perf pass *per se*, but a natural place to mention it.
- **Expected delta:** No memory delta in the steady state, but reduces blast radius of a future hook-survival bug.
- **Risks:** Touching ownership semantics; risks introducing the very bug it prevents if done sloppily.
- **Verification:** Existing world-load/unload-cycle integration tests.
- **Invariants:** None affected.

### IL emission size / shape

#### IL-1 — Hoist the `ldc.i4 hookId` constant into a static field?

- **Anchor:** §1.6 (C) — every prologue starts with `ldc.i4 <hookId>`. `Ldc_I4` is 5 bytes (1 op + 4-byte int). At 10k hooks: 50 KB of IL bytes baked in for the hookId alone. Modest.
- **Sketch reject:** Loading from a static field would replace `ldc.i4` (5 B) with `ldsfld` (5 B for the metadata token) — no savings, plus a memory load. **Don't do.**

#### IL-2 — Merge `Enter` + `Leave` into a single static call returning a "frame token"?

- **Anchor:** None pressing. Would *increase* per-call cost because the frame would need to be returned through a stack value rather than stashed in TLS. Reject.

#### IL-3 — Replace `ldc.i4 hookId; call Enter` with a `ldc.i4 hookId; conv.i; call <fn>` indirect-dispatch? — reject

This is the kind of micro-opt that the JIT itself does or doesn't matter at our scale. Reject.

**Net IL-emission verdict:** the prologue/epilogue shapes are already minimal. No win here without a multi-hour micro-tuning pass that would yield <1% improvement.

### Install time

#### INST-1 — Run `ILHookInterceptor.Install` and `HookInterceptor.Install` in parallel

- **Anchor:** Only relevant in Parallel mode. In single-backend mode the other path is dormant.
- **Sketch:** In Parallel mode, dispatch the two `Install` calls to separate `Task.Run` and wait. Both walk the same modlist independently; they don't share mutable state during install (each populates its own counter arrays). The shared `PerModAttribution.Hooks` registration is the only synchronisation point — guard it with a `lock`.
- **Expected delta:** Halves install wall-clock in Parallel mode.
- **Risks:** Concurrent `RegisterOrReuseHook` invalidates the static `_hooks` list mid-search; the lock fixes that but at the cost of contention. Net win depends on how much wall-clock is in registration vs ILHook construction.
- **Verification:** Install wall-clock measured pre/post.
- **Invariants:** None affected. Install is sequential w.r.t. game lifecycle — `PostSetupContent` is a single-threaded callback. Parallelising work inside is fine.

#### INST-2 — Cache `AssemblyManager.GetLoadableTypes(mod.Code)` between the two backends

- **Anchor:** In Parallel mode both backends call `GetLoadableTypes` for every mod. tML caches this internally; the second call is fast. Skip.

#### INST-3 — Skip `method.GetMethodBody()` when we can deduce no-body from attributes

- **Anchor:** §1.6 (A). `GetMethodBody()` is the most expensive per-method reflection call. For abstract or `[MethodImpl(MethodCodeType.Runtime)]` methods we know there is no body.
- **Sketch:** Check `method.IsAbstract` (already done) and `(method.GetMethodImplementationFlags() & MethodImplAttributes.CodeTypeMask) != MethodImplAttributes.IL` *before* calling `GetMethodBody()`. The latter is a cheap flags read.
- **Expected delta:** Modest — most overrides are IL bodies. Maybe 1–3% of install time. Marginal.
- **Risks:** None.
- **Verification:** Install wall-clock pre/post.
- **Invariants:** None affected.

#### INST-4 — Skip `GetMethodBody()` and let MonoMod throw, catch, count as `_skippedOverrides`

- **Anchor:** §1.6 (A) lines 355–368 are a try/catch around `GetMethodBody()` already. The catch path is the safe one — MonoMod itself does the body check. If we remove our pre-check, we save one reflection call per method, but trade for a throw-catch per skipped method (more expensive on the skip path).
- **Verdict:** Reject — the skipped path is small but the throw cost would dominate if a mod has many extern stubs.

### Unload completeness

#### UNLOAD-1 — Verify `Uninstall` clears the MonoMod chain caches too

- **Anchor:** §3.2 — disposing the `ILHook` should remove it from the `ManagedDetourState`'s chain and refresh. We need to confirm that the static `DetourManager` doesn't retain a reference to disposed `ILHook` instances. Per the README-RuntimeDetour: *"automatically undone when the garbage collector collects the object, or the object is disposed"* — `Dispose()` triggers chain removal.
- **Sketch:** No code change required, but **needs benchmark**: run install → uninstall → `GC.Collect(2, blocking: true, compacting: true)` → snapshot `GC.GetTotalMemory(true)`. The number should return very close to pre-install. If it doesn't, MonoMod has leak vectors we have to chase.
- **Expected delta:** Diagnostic, not a code change.

#### UNLOAD-2 — Tear down `_instrumentedHandles` and `_measuredHookCounts` to `Array.Empty<int>()` (already done)

- Already correct in `Uninstall` L207–215. No change needed.

#### UNLOAD-3 — On install failure, ensure partial `PerModAttribution.Hooks` registrations don't outlive the failed install

- **Anchor:** `ILHookInterceptor.Install` catch path calls `Uninstall()` but does **not** clear `PerModAttribution.Hooks` (or the static lists in `PerModAttribution`'s allocator state). If install fails mid-way through, the `Hooks` list contains stale `HookDescriptor` entries for hooks that were registered but not actually installed.
- **Sketch:** Add `PerModAttribution.Reset()` (or equivalent — confirm method exists; if not add it) to the catch path. The method should clear `_hooks` and re-`Configure(0)`.
- **Expected delta:** Zero RAM in the success path. Defensive; prevents a stale-`_hooks`-causes-wrong-attribution-on-retry-install bug.
- **Risks:** None.
- **Verification:** A unit test that throws from inside `InstallForMod` on the third mod and asserts `PerModAttribution.HookCount == 0` after the catch.
- **Invariants:** Invariant 4 (abort-clean).

---

## 5. Cross-system dependencies

Other subsystems that any of the above changes touch.

| Other system | What it consumes from us | Change impact |
|---|---|---|
| `MetricCollector` (`Profiling/MetricCollector.cs`) | Calls `PerModAttribution.HarvestInto(...)` every tick; reads `HookCoverageView` for overlay; reads `MetricCollector.BackendDivergence`. | CPU-1, CPU-2 don't change the API. ALLOC-1 doesn't change the API. **No knock-on**. |
| `PerModAttribution` | Owns `_hooks`, `Add`, `RegisterHook`. | CPU-2 adds two parallel arrays here. ALLOC-4 adds a dictionary. RegisterOrReuseHook is rewritten. **Modest code change**. |
| `PerTickAttributionRing` | Reads per-mod totals each tick, no change. | Unaffected. |
| `SessionLogWriter` | Reads `HookCoverageView` for the `coverage` block; reads `PerModAttribution.Hooks` for hook names. | CPU-2 requires the display-name lookup to still go through `Hooks`. Already does. **Unaffected**. |
| `ProfilerSystem` | Calls `ILHookInterceptor.Install` from `PostSetupContent`; calls `ILHookInterceptor.Uninstall` from `Unload`; calls `MarkInstallEnd` with the hook count. | ALLOC-2 (forced GC) lives in `Install`. RAM-3 (re-anchoring `_installedHooks` to a non-static lifetime) would move ownership here. **Some code change**. |
| `ProfilerSelfHealth` | Reads `MarkInstallEnd(installedHookCount)`. The hook count it gets must be the active-backend sum. | If ALLOC-1 disposes per-hook Cecil contexts before `MarkInstallEnd` runs, the heap delta SelfHealth reports will *drop* — this is the desired outcome but the SelfHealth severity thresholds may need re-calibration. **Audit follow-up**. |
| `Insights` engine | Reads `PerModAttribution.Hooks` for hook-name-based insights. | Unaffected. |
| `OverlayPanel` / `TreeTab` / `SelfTab` | Read `HookCoverageView`. | Unaffected. |
| `Tests/PerformanceProfiler.Tests.csproj` | Compiles in pure-logic sources via `<Compile Include + Link>`. ProbeStack is not in the test project today (it depends on tML's Mod.Logger etc.). | Microbenchmarks (CPU-1, CPU-2 verification) can land here if we factor the hot-path test into a pure unit. **New test files**. |

No subsystem outside the hook-instrumentation surface is affected by any of the §4 changes beyond signalling.

---

## 6. Prioritised execution order

The pass should run these in order of leverage-per-effort. Each item has effort tagged S/M/L and blast radius narrow/medium/wide.

### Phase A — Quick wins (S effort, narrow blast)

1. **ALLOC-3** (intern DisplayName) — 1 hour. Free 0.5–1.5 MB.
2. **ALLOC-5** (pre-size install collections) — 1 hour. Free hundreds of KB of transient garbage.
3. **INST-3** (skip GetMethodBody on `MethodImplAttributes` non-IL) — 1 hour. Marginal install-time win.
4. **CPU-3** (cache `ILHookBackendId`) — 30 min. Marginal but free.
5. **UNLOAD-3** (clear `PerModAttribution.Hooks` on failed install) — 1 hour. Defensive only.

### Phase B — Hot-path tightening (M effort, medium blast)

6. **CPU-2** (parallel `int[] _hookModId` / `_hookCategoryId`) — half day, with microbenchmark.
7. **CPU-1** (fold `_stack`+`_depth` into one `[ThreadStatic] ProbeStackState`) — half day, with microbenchmark.
8. **ALLOC-4** (dictionary-backed `RegisterOrReuseHook`) — half day.

Phase B delta target: per-tick PerformanceProfiler cost from 0.27 ms → ~0.18 ms.

### Phase C — Install-RAM headline win (M-L effort, wider blast)

9. **ALLOC-1** (dispose per-hook `ILContext` after install) — 1–2 days, with research, MonoMod-internals reading, defensive try/catch, and a heap-snapshot diagnostic. This is the **single most important change** in this dossier. If successful, install delta drops by 50–150 MB.
10. **ALLOC-2** (forced Gen2 + LOH compaction after install) — half day. Stacked on top of ALLOC-1.

Phase C delta target: install delta from 233 MB → ~80–120 MB. Combined with Phase B, lands within striking distance of the < 80 MB / < 0.10 ms targets.

### Phase D — Filed concerns / out-of-scope notes

11. **RAM-1** (MonoMod cross-module sharing) — file as upstream issue. Not actionable in our codebase.
12. **RAM-2** (drop `SourceClone` after first dispatch) — needs MonoMod-internals investigation. May not be feasible.
13. **UNLOAD-1** (post-uninstall heap-delta benchmark) — diagnostic, no code change. Add a debug command/log line.
14. **INST-1** (parallel install in Parallel mode) — defer until Parallel ships as a player toggle.

### Phase E — Verification harness (required, not optional)

15. **Microbenchmarks** for Enter/Leave under Lite and Alloc paths.
16. **Pre/post install-heap snapshot logging** behind a debug-config flag.
17. **One-tick end-to-end** test exercising `Install → Add(x10000) → Harvest → Uninstall → GC.Collect → snapshot`.

All of Phase E should land alongside the corresponding Phase B/C changes — they are the evidence the changes ship with, per Invariant 2 (the hot path must be measured before the change is "done").

---

## 7. References

### Project sources read

- `/Users/atacanercetinkaya/Library/Application Support/Terraria/tModLoader/ModSources/PerformanceProfiler/CLAUDE.md` — five Invariants
- `context/notes/philosophy.md` — full file
- `context/perf-pass/baseline.md` — full file (numbers anchor §2)
- `context/systems/hook-instrumentation.md` — full file (architecture anchor for §1)
- `context/notes/decisions.md` L120–185 (selectively read for context)
- `context/notes/ilhook-migration-plan.md` (selectively read for the ILHook-vs-delegate trade)
- `Profiling/HookBackend.cs` — full file (1.1)
- `Profiling/HookCategoryRouter.cs` — full file (1.2)
- `Profiling/HookCoverageView.cs` — full file (1.3)
- `Profiling/ProbeStack.cs` — full file (1.4)
- `Profiling/HookInterceptor.cs` — full file (1.5)
- `Profiling/ILHookInterceptor.cs` — full file (1.6)
- `Profiling/ProfilerSelfHealth.cs` — full file (1.8)
- `Profiling/PerModAttribution.cs` L1–290 (1.7, ALLOC-4, CPU-2)

### External research read

- MonoMod repo `MonoMod/MonoMod` — fetched via `gh api` (`gh api repos/MonoMod/MonoMod/contents/...`):
  - `src/MonoMod.RuntimeDetour/ILHook.cs` (constructor surface, `ApplyByDefault = true`)
  - `src/MonoMod.RuntimeDetour/DetourManager.Managed.cs` L220–310 (chain, `SourceCloneIl`, `Active` contexts, `Refresh` semantics)
  - `src/MonoMod.RuntimeDetour/TrampolinePool.cs` (existence noted; not deep-read)
  - `src/MonoMod.Utils/DynamicMethodDefinition.cs` L1–280 (Cecil module per DMD, `Generate()` backends, `Dispose()`)
  - `src/MonoMod.Utils/DynamicMethodDefinition.CopyMethodToDefinition.cs` L1–80 (body copying)
  - `src/MonoMod.Utils/MMReflectionImporter.cs` L50–66 (per-module importer caches: CachedAsms, CachedModuleTypes, CachedTypes, CachedFields, CachedMethods)
- tModLoader repo `tModLoader/tModLoader`:
  - `patches/tModLoader/Terraria/ModLoader/NPCLoader.cs` L1–40 (HookList dispatch structure)
- Mono.Cecil — documentation via WebSearch (Mono FAQ, cecil.pe homepage, narkive.com leak thread)
  - https://cecil.pe/
  - https://www.mono-project.com/docs/tools+libraries/libraries/Mono.Cecil/faq/
  - https://github.com/jbevain/cecil/blob/master/Mono.Cecil/ModuleDefinition.cs
  - https://mono-cecil.narkive.com/0v7kfiQu/memory-leaks-in-mono-cecil
- MonoMod docs:
  - https://monomod.dev/docs/README.RuntimeDetour.html
  - https://github.com/MonoMod/MonoMod/blob/master/MonoMod.RuntimeDetour/ILHook.cs (now at `src/...` per current layout)
  - https://monomod.dev/api/MonoMod.Utils.DMDCecilGenerator.html
- .NET 8 platform context — common knowledge:
  - `Stopwatch.GetTimestamp()` cost band ~15–20 ns on modern x64 (also documented in `HookBackend.cs` comment L65: "Stopwatch at ~17.2 ns")
  - `GC.GetAllocatedBytesForCurrentThread()` cost band ~3 ns (documented in `HookBackend.cs` L64: "alloc API at ~3.2 ns/call")
  - `[ThreadStatic]` access cost ~3–5 ns via TLS register on .NET 8

### Per-file source citations

Every `§1.X` numbered finding above cites the file and line range. The key load-bearing citations:

- `HookBackend.cs:51` — `_mode` default
- `HookBackend.cs:96` — `ILHookBackendId` property (CPU-3 anchor)
- `HookBackend.cs:64–65` — measured Stopwatch / GetAllocatedBytes cost band
- `HookCategoryRouter.cs:34–47` — `ResolveCategory`
- `HookCoverageView.cs:37–82` — façade methods
- `ProbeStack.cs:38–192` — full structure
- `ProbeStack.cs:58–59` — `_stack` + `_depth` ThreadStatics (CPU-1 anchor)
- `ProbeStack.cs:115–119` — `Hooks[hookId]` struct copy (CPU-2 anchor)
- `ProbeStack.cs:40–53` — `Frame` struct (CPU-4 anchor)
- `HookInterceptor.cs:283–350` — `Install`
- `HookInterceptor.cs:501–776` — `TryHookSupportedOverride` signature cascade
- `HookInterceptor.cs:778–782` — `CreateProbe` allocation
- `ILHookInterceptor.cs:79` — `_installedHooks` static (RAM-3 anchor)
- `ILHookInterceptor.cs:137–182` — `Install` and abort-clean catch (UNLOAD-3 anchor)
- `ILHookInterceptor.cs:190–215` — `Uninstall` (UNLOAD-1 anchor)
- `ILHookInterceptor.cs:282–340` — `InstrumentTypeOverrides` with closed-generic pass
- `ILHookInterceptor.cs:328–331` — tModLoader-assembly filter (JIT shared-body trap)
- `ILHookInterceptor.cs:355–395` — install per-method
- `ILHookInterceptor.cs:404–413` — `DisplayName` (ALLOC-3 anchor)
- `ILHookInterceptor.cs:435–442` — `InstallTimingHook` (`new ILHook(target, manipulator, applyByDefault: true)`)
- `ILHookInterceptor.cs:449–568` — `ApplyTimingWrap` IL emission shape
- `PerModAttribution.cs:144–178` — `RegisterHook` / `RegisterOrReuseHook` (ALLOC-4 anchor)
- `PerModAttribution.cs:213–279` — `Add` overloads (1.7)
- `ProfilerSelfHealth.cs:138–162` — `MarkInstallStart`/`MarkInstallEnd`
- `ProfilerSelfHealth.cs:170–202` — `Refresh`

### Decisions / system docs

- `context/notes/decisions.md` L121–185 (2026-05-19 → 2026-05-20 sessions)
- `context/notes/decisions.md` L139 — Mono.Cecil retained state as suspected dominant cost
- `context/systems/hook-instrumentation.md` (full)
- `context/notes/ilhook-migration-plan.md` (selectively — Phase 1/2 historical context)

---

## 8. Deep appendix — signature-by-signature analysis and additional findings

### 8.1 tModLoader's `GlobalHookList<TGlobal>` dispatch shape

Confirmed from `patches/tModLoader/Terraria/ModLoader/Core/GlobalHookList.cs`. Each tML hook category (e.g. `GlobalNPC.PreAI`) is dispatched through a `GlobalHookList<GlobalNPC>` that:

- Caches a `TGlobal[] hookGlobals` — the subset of `GlobalNPC` instances that actually override the method (filtered via `HookOverrideQuery.HasOverride`).
- Caches a `TGlobal[][] hookGlobalsByType` — per-entity-type fan-out so unrelated NPCs skip globals that don't apply.
- Iterates with `ReadOnlySpan<TGlobal>` — zero-alloc iteration.

**Implication for our instrumentation.** Because tML pre-filters globals to those that *actually override*, the dispatch only invokes hooks that have a body — there is no "dead hook" dispatch we are timing. Every Enter+Leave we observe corresponds to a real per-mod call. This is a load-bearing fact for the §1.9 cost arithmetic: hook-call volume ≈ override-call volume.

**Secondary implication for INST-3.** tML itself does the equivalent of an `IsBaseMethod` check at modlist-load to populate `hookGlobals`. We re-derive that knowledge via reflection (`IsHookOverride` in both interceptors). A future research item is whether we could *consume* tML's `GlobalHookList<T>.HookOverrideQuery` directly instead of re-walking — eliminates ~half of our install reflection cost. **Filed but out of scope** (would couple us harder to tML internals; the JIT shared-body trap experience cautions against that coupling).

### 8.2 Signature-family cost matrix (delegate path)

The delegate-path `HookProbe` exposes 30 `Time*` methods (`HookInterceptor.cs:826–1222`). Their CPU shapes are structurally identical: `Stopwatch.GetTimestamp(); try { orig(...); } finally { PerModAttribution.Add(...); }`. But there is variation:

| Family | Body shape | Per-call cost notes |
|---|---|---|
| `Time` (void/0-arg) | minimal | Baseline ~55 ns |
| `TimeNpc` / `TimeProjectile` (1 ref-type arg) | adds one delegate-arg pass | +1 ns (arg passing) |
| `TimeBoolRefColor` (ref Color) | ref-arg passes via address | +2–3 ns (one extra load) |
| `TimeNpcRefHitModifiers` (ref struct) | ref-struct via byref | +2–3 ns |
| `TimeDrawItem` (6 args, mixed value/ref types) | wider arg pack | +5–8 ns vs 0-arg baseline |
| `TimeShoot` (7 args including 2× `Vector2`) | wide pack | +8–12 ns vs 0-arg |

The delegate-path overhead has a real **per-signature variance** that the IL-path doesn't have (the IL emission is parameter-count-agnostic). This is a quiet advantage of the IL path that wasn't called out in the migration plan. **Not actionable today**, but noteworthy: for a wide-signature hook the IL path is faster per call than the delegate path by potentially 5–10 ns even before the trampoline-frame trim mentioned in `HookBackend.cs:42–46`.

### 8.3 Why the `_TempAllocBench` measurement on `HookBackend.cs:64–65` is load-bearing

The comment in `HookBackend.cs` says: *"the benchmark in `_TempAllocBench` measured the alloc API at ~3.2 ns/call vs Stopwatch at ~17.2 ns -- 5× cheaper -- so the per-call cost of carrying alloc tracking on top of timing is marginal at our current modlist scale."*

This is the **anchor for the "Deep mode default" decision**. If our pass demonstrates that Alloc-mode is meaningfully *not* marginal — e.g. if at scale (40 mods, kitchen sink) the +6.4 ns per call becomes a measurable share of frame time — we owe a re-evaluation. Today the pass should *not* re-litigate the default; it should ensure the Alloc-path tightening (CPU-1, CPU-2) closes the gap to a non-Alloc baseline so the Deep default remains affordable.

### 8.4 The `applyByDefault: true` lock-in

`ILHookInterceptor.cs:441` constructs `ILHook` with `applyByDefault: true`. Per the MonoMod ILHook docs (read via gh): without this, `IsApplied` stays false and IL is never inserted. The alternative — construct hooks with `applyByDefault: false`, then call `.Apply()` in batch — does **not** save memory or CPU (the Cecil module is built at construct-time, not at apply-time). So the `applyByDefault: true` choice is the right one.

But there is a subtle path here: if we ever want **deferred install** (defer the JIT cost out of the world-enter freeze), the lever would be `applyByDefault: false` + later batched `.Apply()` on a background warmup. That moves the JIT cost off the world-enter window without reducing it. **Filed for §6 Phase D consideration.**

### 8.5 Stale enumeration / shared-state risks

The `_installedHooks` `List<ILHook>` and `_instrumentedHandles` `HashSet<RuntimeMethodHandle>` are **process-static** but their owning class (`ILHookInterceptor`) is also static. Across a `Mods → Reload`, tModLoader's design is that our assembly is unloaded and a new one loaded — so the static state is *physically* discarded with the old `AssemblyLoadContext`. This protects us from the "stale state survives reload" hazard the system doc page 211 worries about.

But if `Mod.Unload` ever *fires the catch-all-and-log-but-continue* path (a defensive Unload), `Uninstall()` may not run, and at *teardown of the AssemblyLoadContext* the chain of references becomes:

```
ILHookInterceptor._installedHooks (in old ALC)
  → List<ILHook> (in old ALC)
    → ILHook (in old ALC)
      → ManagedDetourState (in MonoMod, static, in default ALC)
        → SourceCloneIl (DynamicMethodDefinition in old ALC)
          → ModuleDefinition (in old ALC)
            → MMReflectionImporter caches (referencing Type from old ALC types)
```

The cross-ALC reference from MonoMod's static `DetourManager` into our (now-being-unloaded) ALC types is **the canonical scenario where ALC unload fails** — .NET refuses to collect an ALC while any code in a permanent ALC still references types in it. If a future `Mod.Unload` bug skips our `Uninstall`, the player gets a *permanent ALC leak* across reloads, accumulating ~233 MB per reload until the process restarts.

**Mitigation today.** The `try/catch` around `Install` (and the abort-clean `Uninstall` on the catch path) already protects the *install-failed* scenario. A symmetric concern is `Unload` itself: ensure `ProfilerSystem.Unload` (or equivalent) always reaches `ILHookInterceptor.Uninstall()` — even if other unload code throws. This is a code-health audit item, not a perf opt; but the perf consequences of a leak here are catastrophic so we flag it.

### 8.6 Two-pass install for closed-generic discovery

The current `InstallForMod` walks types once. The closed-generic case relies on the `BindingFlags.DeclaredOnly = false` walk surfacing inherited methods. This works but means each type's methods are walked *N* times where *N* is the depth of its inheritance chain — for ModItem subclasses that means ~4–6 walks of the same ModItem inherited methods, repeated across every ModItem in the mod.

A two-pass design — first pass enumerates direct overrides per type, second pass discovers closed-generic inherited methods only via concrete subclasses — could halve the reflection cost. But it would also restructure the dedup-set logic. **Effort:** medium. **Win:** ~1–2 s of install time. **Verdict:** defer; install time is not the headline pain (RAM is).

### 8.7 IL emission edge case: methods with try/finally already present

`ApplyTimingWrap` (L449) wraps the *entire* original body in a new outer try/finally. The doc-comment on L559–567 notes "Existing inner handlers (if any) stay untouched and remain legally nested inside the new outer try region." This is correct per CIL semantics — finally regions are properly nestable. But there is a subtle correctness corollary: **`leave` instructions to the new `afterHandler` skip *over* any inner finally that was supposed to run**. Wait — actually no, `leave` *fires every intervening finally* by definition of `leave` semantics. The IL emission is correct.

The reason to mention this here: if a future contributor refactors `leave` to `br` (an "optimisation"), they will silently break inner finally semantics. **Add a defensive code comment** at the `leave` emission site. Tiny code-health change, not a perf change.

### 8.8 What is *not* costing us much (audit clearances)

For completeness — the following are *not* the leverage points despite intuition suggesting they might be:

- `HookCategoryRouter.ResolveCategory`'s 8 sequential `IsAssignableFrom` checks: ~20 µs total install. Not a hot path.
- `HookCoverageView` allocations: zero per call (the readonly facades hand out existing arrays).
- `HookDescriptor.DisplayName` string size: ~50–80 B each × 10k = ~800 KB. Negligible vs the 233 MB headline.
- `HookProbe` delegate-mode object allocations: ~88 B × 10k = ~880 KB. Negligible.
- `_unsupportedSignatureFrequency` dictionary in delegate mode: bounded to ~30 entries (one per unique unsupported signature). Trivial.

Naming what isn't broken keeps the pass focused on what is.

### 8.9 Defensive bound: avoid making the pass *increase* per-call cost

Several "optimisations" we could imagine here would make per-call cost *worse* — listing them so they don't sneak in via good-intentions refactor:

- **Locking around `PerModAttribution.Add`** to make Parallel-mode "more correct" — would add ~20 ns per call. The system doc explicitly accepts the one-tick mis-attribution race (`PerModAttribution.cs` L26–34). Don't add locks.
- **`Dictionary<int, HookDescriptor>` lookup in `Leave`** instead of array-indexed — replaces O(1) array index with O(1) hash. Hash is ~5× slower. Don't.
- **`Activator.CreateInstance` patterns for per-signature probes** — slow startup, no run-time benefit. Don't.
- **Reflection-based dispatch from the IL prologue** — would defeat the whole point of ILHook (avoiding delegate-frame overhead).

The pass should be **subtractive** on per-call cost (and additive on diagnostics). Never additive on the hot path.

### 8.10 Test-harness extensions required by this pass

To satisfy the Invariant 2 obligation ("an unmeasured hot-path change is an incomplete change") for the changes in §4, the `Tests/` project needs three new harness pieces:

1. **`ProbeStackBenchmarks` (xUnit `[Fact]` invoked under `Benchmark` filter)** — runs Enter/Leave 1M times on a single thread, measures elapsed via `Stopwatch.GetTimestamp` (irony noted), asserts target ns/op. Today's baseline harness lives at `Tests/PerformanceProfiler.Tests.csproj`'s `PersistenceBenchmarkTests`; new file follows the same pattern.
2. **`InstallHeapSnapshotTests`** — runs a synthetic Install pass (fake `ProfiledMods` carrying real assemblies), forces GC, captures `GC.GetTotalMemory(true)`, asserts the delta against a target ceiling that ratchets down with the pass.
3. **`UninstallCleanlinessTests`** — install/uninstall/GC/snapshot, asserts return-to-baseline within some tolerance (the §3.2 "is MonoMod actually free-on-dispose" question turned into a regression test).

These tests run via the existing `dotnet test Tests/PerformanceProfiler.Tests.csproj --filter "Benchmark"` invocation per baseline.md §1.

### 8.11 Per-tick budget arithmetic explained

The current 0.27 ms/tick attributed to PerformanceProfiler decomposes (estimated) as:

| Component | Estimated share | Reasoning |
|---|---|---|
| Sum of all Enter+Leave on hooked methods | ~0.18 ms (~67%) | At ~50k hook calls/tick × ~55 ns per call (Alloc path slightly higher). |
| `MetricCollector.EndTick` (harvest, baseline, focus probe, GC reads) | ~0.04 ms (~15%) | One sweep across `PerModAttribution._ticksByBackend`, baseline updates. |
| `PerTickAttributionRing.Push` | ~0.02 ms (~7%) | Per-mod sample row, 7 categories × ~20 mods = 140 longs copied. |
| `SpikeDetector` + `StallDetector` per-tick reads | ~0.02 ms (~7%) | MAD-based windowed stats. |
| `InteractionPlayer`/`InteractionNpc`/`InteractionItem` per-tick hooks | ~0.01 ms (~4%) | A handful of per-tick callbacks. |

Of these, the only piece §4 directly attacks is the first row — the per-call hot path. CPU-1 + CPU-2 alone target ~30% reduction of that row → ~0.05 ms saved → 0.22 ms residual. To reach the 0.10 ms target the other rows (out of this dossier's scope) need their own pass; that pass is `MetricCollector`'s research file, not ours.

**Bottom line for our dossier:** Hook-instrumentation can credibly take 0.27 ms → ~0.20 ms on per-tick. Closing the rest of the gap is sibling-system work.

### 8.12 Risk that this dossier is wrong about Cecil dominance

The 23 KB/hook number is **measured**, but the attribution of that to "Cecil module retention" is **structurally argued, not measured**. There is a real chance the dominant pillar is actually:

- **Trampoline pages.** Each ILHook installs an executable code page in a `TrampolinePool`. On platforms where the JIT uses fine-grained page allocation, each hook may pin a fresh 4 KB page even if it only uses 100 bytes. 10k hooks × 4 KB = 40 MB of native pages.
- **DynamicMethod JIT code.** The emitted dynamic method's native code is held off-heap (the JIT compiles it to a code-heap segment). If the JIT segments are coarse, fragmentation could be material.
- **`AssemblyManager.GetLoadableTypes` cache.** tML's per-mod loadable-types cache may retain references that prevent collection of transient install-time arrays.

**The §4 changes presume Cecil is the dominant pillar (ALLOC-1 sizing).** If the heap-snapshot diagnostic (§6 Phase E item 16) shows Cecil is only 30% of the install delta, ALLOC-1 yields proportionally less and the pass must pivot to attacking the actual dominant pillar. That pivot is anticipated and supported by the diagnostic instrumentation, not derailed by it.

This dossier's **single most important predicted finding** is therefore: **the install-time heap snapshot should be run *first* in Phase C**, before committing to ALLOC-1 implementation. The decision tree:

- If Cecil is >50% of install delta → ALLOC-1 is highest leverage.
- If trampolines/JIT code dominate → research a per-target shared trampoline path (much harder, likely upstream MonoMod work).
- If neither — find the surprise.

This branching is the right shape for a research-led pass; we don't commit code to a hypothesis until the hypothesis has heap evidence.

---

## 9. What this dossier does NOT cover

For clarity at handoff:

- **Storage / persistence overhead** — `SessionLogWriter`, LiteDB, JSON-lines, compaction. Belongs to `research/persistence.md` (sibling).
- **MetricCollector hot-path optimisations** — sibling, `research/metric-collection.md`.
- **UI render cost** — `research/ui-render.md`.
- **Insights engine ranking cost** — `research/insights.md`.
- **The end-of-session 8.5 s stall** — that is the Persistence pass's headline pain, not ours. Our `Uninstall` runs after, but the 8.5 s is JSON serialisation, not hook teardown.
- **Multiplayer hook coverage v2** — design item, not a perf concern.
- **The hooks-bug-zone in `itemCreatedEvents` and `buffEvents`** — correctness bugs (baseline.md §4), shipped alongside the perf pass but not part of perf research.

---

*End of `hook-instrumentation.md` research dossier. ~Inputs to `master-plan.md` (Phase 4 of the v0.6 perf pass).*
