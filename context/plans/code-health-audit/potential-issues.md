# Code Health Audit — Potential Issues

These are concrete suspicions that did not meet the certain-finding bar during the audit. They are not downgraded certain findings; each needs runtime evidence, domain intent, or a product decision before implementation.

### 1. Partial ILHook install failure may leave already-added hooks undisposed

**Locations to inspect:**
- `Profiling/ILHookInterceptor.cs:160-178` — top-level `Install` catch sets `Installed = false` and logs
- `Profiling/ILHookInterceptor.cs:187-212` — `Uninstall` has the deterministic disposal path
- `Profiling/ILHookInterceptor.cs:386-409` — successful hooks are added to `_installedHooks`; per-method failures are caught locally

**Observation:** A top-level exception during `Install` after one or more hooks were added would log a clean disablement but would not call `Uninstall` in that catch path.

**Reasoning:** Most per-method failures are caught inside `InstrumentTypeOverrides`, so this requires an unexpected outer failure. If it occurs, the code has a disposal path but does not use it before returning disabled.

**Suggested investigation:** Add fault injection around `InstallForMod` or `InstallTimingHook` in a test host/spike branch, force an exception after the first successful hook, and assert `_installedHooks` is empty after the install failure path.

**Why not a certain finding:** The audit cannot safely construct a tModLoader ILHook fault-injection host without production seams or a separate runtime spike. The risk is credible and important, but not proven on a live path.

### 2. Delegate fallback install failures may be counted as measured hooks

**Locations to inspect:**
- `Profiling/HookInterceptor.cs:451-456` — caller increments total and measured when `TryHookSupportedOverride` returns true
- `Profiling/HookInterceptor.cs:779-783` — catch logs a hook failure and returns true

**Observation:** If `MonoModHooks.Add` throws inside `TryHookSupportedOverride`, the catch logs but returns `true`, so coverage accounting can treat the failed hook as measured.

**Reasoning:** In current default ILHook mode this fallback path is dormant, but `Delegate` and `Parallel` modes are documented fallback/validation modes. Coverage health should distinguish unsupported signatures from attempted-but-failed detours.

**Suggested investigation:** Exercise delegate mode with a forced `MonoModHooks.Add` failure and assert the coverage row records an install failure rather than a measured hook or an unsupported signature.

**Why not a certain finding:** The exact desired accounting shape needs an implementation decision: returning `false` today would merge install failures into unsupported-signature accounting, which is also not semantically precise.

### 3. Open spike windows may be missing from final session reports

**Locations to inspect:**
- `Profiling/SpikeDetector.cs:241-253` — `Flush()` exists to force-close an open window at world unload
- `Profiling/ProfilerSystem.cs:100-115` — unload writes session log and clears collector but does not call spike flush
- `Profiling/SessionLogWriter.cs:88-96`, `Profiling/SessionLogWriter.cs:282-285` — final report reads `collector.Spikes`

**Observation:** A spike window that is still open when the world unloads may not be pushed to the retained window ring before the final report is written.

**Reasoning:** The code has an explicit method with a comment saying it should be called at world unload, and a repository-wide search found only the method declaration. The behavioural consequence depends on exiting during an active spike/recovery window.

**Suggested investigation:** Add a small diagnostic around `SpikeDetector` in a test harness or run an in-game repro that triggers a spike and exits before the recovery threshold. Confirm whether the final session JSON contains the open window.

**Why not a certain finding:** Fixing this changes captured session data, so it is a behaviour fix rather than a free cleanup. It belongs in a bug-fix implementation pass once confirmed.

### 4. Public two-argument `PerModAttribution.Add` appears unused but may be external surface

**Locations to inspect:**
- `Profiling/PerModAttribution.cs:195-201` — two-argument legacy overload
- `Profiling/HookInterceptor.cs:1039-1421` — delegate wrappers call the three-id overload
- `Profiling/ProbeStack.cs:118`, `Profiling/ProbeStack.cs:180` — ILHook path calls backend-explicit overloads

**Observation:** Static search found no in-repo call sites for the two-argument legacy overload.

**Reasoning:** The overload looks removable, but it is `public`. Even though this is a tModLoader mod rather than a library, public static methods can become informal external/debugging surfaces.

**Suggested investigation:** Decide whether `PerformanceProfiler.Profiling` is an internal-only assembly surface. If yes, remove the overload. If no, mark the external contract explicitly and keep it.

**Why not a certain finding:** The audit cannot prove there are no external consumers or `Mod.Call`/reflection users outside this repository.

### 5. Session log pruning may delete manual JSON artefacts in the app-owned session directory

**Locations to inspect:**
- `Profiling/SessionLogWriter.cs:596-610` — `PruneIncompatibleLogs` deletes every `*.json*` file except `current-session.json` whose filename does not start with current identity
- `Profiling/SessionLogWriter.cs:585-593` — session directory path is app-owned

**Observation:** The prune pattern can match backup, corrupt, or manually copied JSON files in the same directory, not only reports emitted by this writer.

**Reasoning:** The directory is app-owned, so broad pruning is probably acceptable today. It becomes risky if future tooling writes diagnostics or backups into the same folder.

**Suggested investigation:** Decide whether the session directory is exclusively owned by `SessionLogWriter`. If not, restrict pruning to the final report filename pattern this writer creates.

**Why not a certain finding:** No current code writes other JSON artefacts there, and changing pruning breadth changes cleanup behaviour.

### 6. Insights have a player surface before an agent/session-log surface

**Locations to inspect:**
- `UI/Overlay/Tabs/InsightsTab.cs:34-41`, `UI/Overlay/Tabs/InsightsTab.cs:96-107` — live player tab evaluates and reads insights
- `Profiling/SessionLogWriter.cs:103-122` — session report has no `insights` block
- `Profiling/Insights/InsightsEngine.cs:90-94` — comments say gated patterns are consumed by the JSONL exporter
- `context/notes/insights-engine-plan.md:1172-1191` — plan says JSONL integration is a later schema bump

**Observation:** The live Insights tab is wired as a player-facing surface, but session JSON does not yet carry insights for the agent surface.

**Reasoning:** The project requires dual-surface observability for runtime features. The plan, however, explicitly treats JSONL integration as a later step. Severity depends on whether the current Insights tab is considered shipped runtime functionality or a half-landed checkpoint.

**Suggested investigation:** Run the game, open the Insights tab during a session, exit, and inspect the final JSON. If Insights is reachable in the current build and the JSON has no `insights` or `insights.gated` block, either wire the agent surface or hide/gate the tab until both surfaces exist.

**Why not a certain finding:** The audit did not run the game, and the plan records this as staged work. The classification hinges on release/readiness intent.
