# Code Health Audit — Potential Issues

**Date:** 2026-06-22
**Supersedes:** the 2026-05-20/21 potential-issues list in this folder.

> Suspicions grounded in concrete code reading that did not meet the certain-bar because
> resolving them needs an in-game install pass (which the audit cannot run off-game) or
> upstream MonoMod cooperation. Each cites the specific files and a concrete next step.

---

### 1. The avoidable retained slice — reclaiming the read-only `ILContext` per hook needs MonoMod cooperation, and the in-repo workaround is fragile {#p-1}

**Locations to inspect:**
- `Profiling/ILHookInterceptor.cs:441-448` — `InstallTimingHook` → `new ILHook(target, manipulator, applyByDefault: true)`.
- `Profiling/ILHookInterceptor.cs:88` — `_installedHooks` (the `ILHook` references the mod owns).
- MonoMod `DetourManager.ILHookEntry.LastContext` (`/tmp/dm.cs:314`), `InvokeManipulator` (`/tmp/dm.cs:679`, the `MakeReadOnly()`-not-`Dispose()` choice).

**Observation:**
F1 establishes that per hooked method MonoMod retains two Cecil method bodies for the
hook's lifetime: the `SourceCloneIl` DMD (needed to rebuild the chain) and the read-only
`LastContext` ILContext (the manipulated body). The mod's hooks are install-once and the
chain for each method never changes after `PostSetupContent`. So the read-only
`LastContext` — kept only so MonoMod can re-run the manipulator if the chain changes — is
dead weight for this mod's usage. At 152,310 hooks, reclaiming it could roughly halve the
per-hook retained Cecil cost (order of GB). But MonoMod exposes no public "trim retained
IL but keep the hook applied" call, and the comment-history already lists "Cecil ILContext
dispose" as a deferred win (`decisions.md:25`).

**Reasoning:**
For the reclaim to be safe, one of these must hold and none currently does in a free way:
(a) MonoMod adds a public `TrimRetainedIL()` that disposes `LastContext` while keeping the
generated method live — requires an upstream PR; (b) the mod reflects into
`ILHookEntry.LastContext` and disposes it post-install — but ILHookEntry is an internal
type whose field layout is exactly the "loader/library internals that change across
updates" Invariant 4 forbids depending on, so this needs an abort-clean guard and would
re-break on any MonoMod bump; (c) MonoMod changes `InvokeManipulator` to dispose rather
than `MakeReadOnly` when no `DetourConfig` implies future re-chaining — again upstream.
None is an in-repo free change, so F1's structural reclaim is filed here rather than as a
clean finding.

**Suggested investigation:**
1. Open a MonoMod issue/PR proposing a `RuntimeDetour` API to release the read-only
   manipulator context for install-once hooks (the use case: thousands of static IL
   detours that never re-chain). Reference the decompiled `InvokeManipulator` /
   `CleanILContexts` behaviour.
2. If upstream is slow, prototype option (b) behind a hard try/catch + a MonoMod-version
   check (`typeof(ILHook).Assembly.GetName().Version`) that disables the reflection path
   on any unrecognised version (Invariant 4). Measure the in-game install delta before/after
   with the F2-fixed forced-Gen2 measurement to confirm the reclaim actually drops retained
   bytes and does not break dispatch or unload.

**Why not a certain finding:**
No test the audit can write would help — the resolution depends on out-of-process state
the audit cannot create (a running tModLoader with mods installing real hooks, and a
MonoMod build with or without an upstream API). The decompiled binaries prove the
*retention*; whether the *reclaim* is safe and how much it actually saves can only be
established by an in-game install pass plus (for option b) a MonoMod-internals dependency
that this repo's Invariant 4 treats as conditional. This is the no-test-physically-possible
deferral, with a concrete next step.

---

### 2. `MarkInstallEnd` denominator (`PerModAttribution.HookCount`) may not equal MonoMod's installed `ILHook` count, skewing `BytesPerHook` {#p-2}

**Locations to inspect:**
- `Profiling/ProfilerSystem.cs:156` — `SelfHealth.MarkInstallEnd(PerModAttribution.HookCount)`.
- `Profiling/ILHookInterceptor.cs:170-173` — install summary logs `_installedHooks.Count` (a different count).
- `Profiling/ILHookInterceptor.cs:383-385` — `RegisterOrReuseHook` (collapses parallel-mode duplicates; the source of `HookCount`).

**Observation:**
`BytesPerHook = InstallDeltaBytes / InstalledHookCount`, and `InstalledHookCount` is set to
`PerModAttribution.HookCount`. But `PerModAttribution.HookCount` is the count of distinct
`(modId, categoryId, displayName)` hook *identities* registered via `RegisterOrReuseHook`,
whereas the number of actual `ILHook` objects (each carrying its own retained Cecil graph)
is `_installedHooks.Count`. In single-ILHook mode these may coincide, but the closed-generic
dedup path (`_instrumentedHandles`) and the `RegisterOrReuseHook` collapse can make
`HookCount` < `_installedHooks.Count` (or vice-versa). If the denominator is the identity
count but the retained-memory driver is the `ILHook` count, the headline `BytesPerHook` is
divided by the wrong N.

**Reasoning:**
The retained Cecil graphs scale with the number of `ILHook` objects (one
`ManagedDetourState` + `LastContext` per distinct hooked method), not with the number of
attribution identities. The 100-mod investigation reports 152,310 hooks installed and
~60 KB/hook — if those two numbers come from different counters, the per-hook figure (and
the README claim, and the severity band) is computed on an inconsistent basis.

**Suggested investigation:**
In-game, log both `_installedHooks.Count` and `PerModAttribution.HookCount` at install end
(the install summary at `ILHookInterceptor.cs:170` already logs the former; compare to the
denominator at `ProfilerSystem.cs:156`). If they differ materially, decide which is the
honest denominator for a *memory* metric (it should be the `ILHook`-object count, since
that is what drives retained Cecil state) and align `MarkInstallEnd`'s argument with it.

**Why not a certain finding:**
The two counts may well coincide in the production single-ILHook configuration — the audit
cannot determine that off-game without a real modlist install (the closed-generic dedup and
RegisterOrReuse collapse only diverge for specific mod content shapes). Confirming the
divergence and its magnitude needs an in-game run; this is the no-test-physically-possible
deferral with a one-line in-game probe as the next step.
