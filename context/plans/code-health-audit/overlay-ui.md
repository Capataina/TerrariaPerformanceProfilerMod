# Overlay UI — Code Health Findings

**Systems covered:** `UI/Overlay/**`, overlay tab chrome, shared drawing helpers, impact scoring  
**Finding count:** 2 findings (0 critical, 0 high, 2 medium, 0 low)

## Known Issues And Active Risks

### Reconcile `IOverlayTab.IsAvailable` With Tab Chrome Behaviour
- [ ] Make tab drawing, tab selection, and active-tab dispatch honour `IsAvailable`, or revise the contract if all tabs are intentionally always visible

**Category:** Known Issues and Active Risks  
**Severity:** Medium  
**Effort:** Small  
**Behavioural Impact:** Possible (requires decision) — hiding unavailable tabs changes current UI behaviour; revising the contract changes documentation only.

**Location:**
- `UI/Overlay/IOverlayTab.cs:42-48` — contract says unavailable tabs can hide themselves from the strip and receive no input
- `UI/Overlay/TabRegistry.cs:46-54` — active tab ignores availability
- `UI/Overlay/OverlayPanel.cs:142-151` — tab clicks select raw slot index
- `UI/Overlay/OverlayPanel.cs:249-253` — tab strip draws every registered tab
- `UI/Overlay/Tabs/EventsTab.cs:90-94` — concrete tab availability implementation

**Current State:**
Tabs implement `IsAvailable`, but the chrome draws and selects from `TabRegistry.Tabs` unconditionally. The documented availability contract is therefore not enforced.

**Proposed Change:**
Choose one behaviour and make code plus docs agree. If availability is intended, compute the visible tab list from `IsAvailable(collector)`, clamp the active tab to the first available tab, and dispatch only to visible tabs. If tabs are intended to stay visible with empty states, remove or reword the hiding/no-dispatch part of the interface contract.

**Justification:**
The current split is a contract drift bug: future tabs can rely on `IsAvailable` and still receive active selection/draw calls. This matters as more data-dependent tabs land.

**Expected Benefit:**
Prevents tabs with missing dependencies from being selected into broken or misleading empty states, or removes a misleading interface promise before more tabs copy it.

**Impact Assessment:**
Implementing the documented contract changes player-visible tab availability. Rewording the contract is behaviour-neutral but accepts current UI behaviour. The implementing engineer should choose deliberately.

## Performance Improvement

### Move Truncation Allocations Out Of Per-Row Draw Paths
- [ ] Stop calling `OverlayDraw.Truncate` in per-frame row drawing, or cache truncated labels at the same cadence rows are rebuilt

**Category:** Performance Improvement  
**Severity:** Medium  
**Effort:** Small  
**Behavioural Impact:** None

**Location:**
- `UI/Overlay/OverlayDraw.cs:9-14` — helper file explicitly requires allocation-free additions
- `UI/Overlay/OverlayDraw.cs:68-70` — `Substring(...) + ".."` allocates on truncation
- `UI/Overlay/Tabs/OverviewTab.cs:323-330` — per-row draw caller
- `UI/Overlay/Tabs/InsightsTab.cs:125-127` — per-row draw caller

**Current State:**
The shared draw helper says row drawing should stay allocation-free, but `Truncate` allocates a new string whenever text exceeds the limit. Multiple tabs call it from row draw paths.

**Proposed Change:**
Cache truncated labels when row arrays are built/refreshed, or store bounded display strings in row structs. Preserve the same visible text and ellipsis convention.

**Justification:**
Microsoft CA1846 documents that `Substring` allocates a new heap string and that many short-lived strings on hot paths create GC pressure. The overlay is not the instrumentation hot path, but it is the player-facing surface a profiler keeps open while measuring allocations.

**Expected Benefit:**
Removes avoidable overlay-generated garbage from rows with long mod names/insight strings and keeps the profiler from polluting the allocation signal it displays.

**Impact Assessment:**
No visual or interaction change. The same text can be displayed; only the timing of string construction changes from per-frame draw to row-refresh cadence.
