# Hook Instrumentation — Code Health Findings

**Systems covered:** `HookInterceptor`, `ILHookInterceptor`, backend selection, coverage projection, spike exposure  
**Finding count:** 4 findings (0 critical, 0 high, 2 medium, 2 low)

## Dead Code Removal

### Remove Legacy Delegate Hook-Name Arrays And Helper Methods
- [ ] Delete the unused curated hook-name arrays and obsolete helper methods from `HookInterceptor.cs`

**Category:** Dead Code Removal  
**Severity:** Medium  
**Effort:** Small  
**Behavioural Impact:** None

**Location:**
- `Profiling/HookInterceptor.cs:231-252` — legacy `SystemHooks`, `PlayerHooks`, `EntityHooks`, and related arrays
- `Profiling/HookInterceptor.cs:808-993` — `HookOverrides`, `HookNpcOverrides`, `HookGameTimeOverrides`, `HookInterfaceLayerOverrides`, `HookSpriteBatchOverrides`, `HookProjectileOverrides`

**Current State:**
`HookInterceptor` now discovers every override through `HookSupportedOverrides` and routes signature shapes through `TryHookSupportedOverride`. The old curated arrays and per-signature helper methods remain in the file but are not called. A repository-wide `Grep` for those symbols found only their declarations.

**Proposed Change:**
Remove the unused arrays and helper methods. Keep `HookSupportedOverrides`, `TryHookSupportedOverride`, the delegate wrapper types, and the active hook probes unchanged.

**Justification:**
The removed code is not part of any live call path. It makes the largest file in the repo look like it still has two hook-discovery strategies, when only the generalized signature path is active. Removing it reduces the file by a meaningful block and makes the active delegate fallback easier to audit.

**Expected Benefit:**
Removes roughly 180 lines of dead fallback scaffolding from the central instrumentation file and lowers future risk of editing the wrong hook path.

**Impact Assessment:**
No observable behaviour changes. Static reference search found zero call sites, and active install flow goes through `InstallForMod` -> `HookSupportedOverrides` -> `TryHookSupportedOverride`.

## Known Issues And Active Risks

### Use Backend-Aware Coverage Counters In Session JSON
- [ ] Make session coverage projection choose the same delegate-vs-ILHook counter source as the overlay

**Category:** Known Issues and Active Risks  
**Severity:** Medium  
**Effort:** Small  
**Behavioural Impact:** Possible (requires decision) — the agent-readable JSON coverage output will change in default ILHook mode, but the change corrects a current mismatch.

**Location:**
- `Profiling/HookBackend.cs:42-51` — default backend is `ILHook`
- `Profiling/HookInterceptor.cs:344-351` — delegate backend does not install in ILHook mode
- `UI/Overlay/OverlayPanel.cs:373-389` and `UI/Overlay/Tabs/TreeTab.cs:421-439` — overlay selects ILHook counters when mode is not delegate
- `Profiling/SessionLogWriter.cs:254-267`, `Profiling/SessionLogWriter.cs:384-425` — session JSON always reads delegate counters

**Current State:**
The player overlay reports coverage from `ILHookInterceptor` in default ILHook mode. Session JSON reports coverage from `HookInterceptor`, whose delegate counters remain empty because delegate installation is skipped when `HookBackend.DelegateActive` is false.

**Proposed Change:**
Extract a small backend-aware coverage projection helper and use it from both overlay chrome/tree code and `SessionLogWriter`. Preserve field names in the JSON; only the counter source changes.

**Justification:**
Dual-surface observability requires player and agent surfaces to describe the same runtime state. Today, the overlay can say coverage is full while JSON can report zero discovered/measured delegate hooks for the same session.

**Expected Benefit:**
Makes `current-session.json` and final session reports trustworthy in the default backend, preventing future agent diagnosis from chasing a false coverage failure.

**Impact Assessment:**
This changes agent-visible output in default ILHook mode. It does not change instrumentation, gameplay, hook timing, or per-mod cost data. Treat it as a correctness fix to observability, not as behaviour-neutral cleanup.

## Pattern Extraction

### Share Hook Category Routing Between Backends
- [ ] Move the duplicated type-to-category mapping into one internal helper used by both backends

**Category:** Pattern Extraction  
**Severity:** Low  
**Effort:** Small  
**Behavioural Impact:** None

**Location:**
- `Profiling/HookInterceptor.cs:386-422` — delegate backend category routing
- `Profiling/ILHookInterceptor.cs:270-282` — ILHook backend category routing

**Current State:**
Both hook backends map tModLoader types to the same seven category ids. The mapping is currently consistent, but the logic is duplicated in two files.

**Proposed Change:**
Extract the mapping into one internal helper near the attribution/category model and have both backends call it. Keep category ids and fallback `-1` semantics identical.

**Justification:**
This is copy-paste logic on the attribution boundary. Any future category addition currently has two edit sites, and a mismatch would create a hard-to-diagnose player/agent split in category totals.

**Expected Benefit:**
Removes one duplicated routing table and prevents category-drift bugs between delegate and ILHook backends.

**Impact Assessment:**
No behaviour change if the helper mechanically preserves the existing mapping. The same input types produce the same category ids.

## Performance Improvement

### Cache The Spike Window View Exposed To Consumers
- [ ] Avoid allocating a new `SpikeWindowsView` on every `MetricCollector.Spikes` access

**Category:** Performance Improvement  
**Severity:** Low  
**Effort:** Trivial  
**Behavioural Impact:** None

**Location:**
- `Profiling/MetricCollector.cs:229-230` — `Spikes` forwards to detector window view
- `Profiling/SpikeDetector.cs:148-149` — property constructs a new `SpikeWindowsView`

**Current State:**
`SpikeDetector.Windows` creates a fresh view object every time consumers read the property. The overlay and session writer read spike windows repeatedly outside the per-tick instrumentation hot path.

**Proposed Change:**
Store one `SpikeWindowsView` instance per detector and return it from the property, or expose the ring through a stable read-only wrapper that is allocated once with the detector.

**Justification:**
The view is a wrapper over the same retained ring. Reallocating it does not change the data snapshot and gives the profiler avoidable garbage in its own UI/reporting path.

**Expected Benefit:**
Eliminates a small repeated allocation from spike display/report reads and aligns with the project’s overhead discipline.

**Impact Assessment:**
No observable behaviour change. The returned view still reads the same `_windows` ring and reports the same chronological windows.
