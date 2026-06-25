# Web UI Assets & Cross-Cutting — Code Health Findings

**Cluster:** Web JS/CSS dashboard assets (`Web/Assets/Js/*.cs`, `Web/Assets/Css/*.cs`, `Web/Assets/IndexHtml.*.cs`) + repository-wide cross-cutting hygiene (duplicate `using`, dead code, documentation rot, dependency/config drift).
**Stance:** AUDIT ONLY — no production/test file edited. Every finding is FREE (identical behaviour, no new maintenance burden, full evidence chain). UI findings will be implemented and re-verified via the L4 Playwright harness + preview render (`tools/testing/`, `tools/preview/render.py`); none are xUnit-testable (the JS is verbatim C# strings).
**Finding count:** 19 (4 high, 7 medium, 8 low) across 6 categories. Plus the verified-clean ledger (Invariant 3, monochrome-chrome, duplicate-`using`).

**Research (mode-varied WebSearch, June 2026):**
- Mode 3 (performance) — query `JavaScript innerHTML full DOM rebuild per poll reflow performance vs diffing dashboard` → [Go Make Things: DOM diffing with vanilla JS](https://gomakethings.com/dom-diffing-with-vanilla-js/), [DEV: DOM Manipulation to improve performance](https://dev.to/grandemayta/javascript-dom-manipulation-to-improve-performance-459a). Finding: a full `innerHTML` replace forces a complete repaint and drops focus/scroll; selective/gated update is the standard mitigation for large trees, but for *small* changes `innerHTML` is fine — so the gate matters only for the heavy panels (Lag tables/heatmaps, Timeline swimlanes), not the tiny ones.
- Mode 1 (technique) — query `SVG chart rendering string concatenation innerHTML performance patterns best practice` → [O'Reilly: Planning for Performance (SVG)](https://oreillymedia.github.io/Using_SVG/extras/ch19-performance.html), [Apache ECharts: Canvas vs SVG](https://apache.github.io/echarts-handbook/en/best-practices/canvas-vs-svg/). Finding: inline-SVG string rebuild re-parses the whole subtree and inflates style-recalc; whitespace/default-attr trimming reduces parse cost; canvas only wins past ~1k elements (the dashboard is well under that, so the string-SVG approach is correct here — the win is *gating the rebuild*, not switching renderer).

Both corroborate the headline cross-cutting finding (CC-1): adopt the already-built `renderIfChanged` signature gate on the heavy poll-driven panels. They do **not** justify a renderer rewrite — string-SVG at this element count is the right call.

---

## Performance Improvement

### CC-1 — `renderIfChanged` is built, documented, and adopted nowhere; heavy poll panels rebuild the DOM every poll regardless of data change
- [x] The shared signature-gate helper has **zero call sites** repo-wide; Lag/Insights/Memory/Self rebuild their tables + heatmaps on every poll even when the snapshot is byte-identical, and Timeline still hand-rolls the very pattern the helper generalises. — IMPLEMENTED: 18 call sites now (`grep -c renderIfChanged` bundle = 18, was 1). Gated heavy panels — Lag: `renderLagHeatmap`/`renderLagClusters`/`renderLagDensity`/`renderLagRhythm` (`Js.Lag.cs`); Insights: `renderDormantSurface`/`renderObservatoryList`/`renderObservatoryDetail`/`renderCrossCutting`/`renderEngagementScatter`/`renderModInteractionMatrix` (`Js.Insights.cs`); Memory: `renderMemory` table write (`Js.Memory.cs`); Self: `renderHookDistribution` (`Js.Self.cs`). Each gates the heavy innerHTML/table rebuild behind a content signature (count + per-row figures + sort/selection/filter state); cheap sibling writes (sub-headers, legends, basis toggles) stay outside the gate. Timeline's bespoke `_tlSig` left as-is (already gating; migrating its 25 refs is mechanically large with no behaviour change — recorded as out-of-scope, not a defect). Verified: C# compile 0 error CS + full-bundle `node --check` OK + `--tabs` preview render exit 0 with all six tabs rendering populated fixtures.

**Category:** Performance Improvement / Pattern Extraction (cross-cutting)   **Severity:** High   **Effort:** M   **Behavioural Impact:** none (identical rendered output; the gate is a pure DOM-churn skip that also preserves scroll/focus)
**Location:** `Web/Assets/Js/Js.Components.cs:256` — `renderIfChanged(key, sig, el, html)` (the sole definition); consumers that should adopt it: `Js.Lag.cs:340/424/528/626` + `:108/180/198/481`, `Js.Insights.cs` sub-renderers, `Js.Memory.cs:241` (`setHTML(tableEl, html)`), `Js.Self.cs:99`; the un-migrated twin: `Js.Timeline.cs:51` (`const _tlSig = {}`) with **25** `_tlSig` references implementing the gate by hand.
**Current State:** `grep -rn renderIfChanged Web/` returns one line — the definition (verified). `pollLagData` (3000 ms when Lag active) runs `renderLag()` → seven sub-renderers, each `setHTML`/`innerHTML =` rebuilding its DOM unconditionally. `setHTML` preserves scrollTop but does **not** gate on a signature, so the largest rebuilds (the cause×context `heatmapMatrix`, the cluster + density `.dtable`s) re-parse every 3 s with no change. Timeline already proves the pattern works — it just uses a bespoke `_tlSig` dictionary instead of the shared helper.
**Proposed Change:** Wrap each heavy sub-renderer's terminal write in `renderIfChanged(key, sig, el, html)` where `sig` is a cheap stable signature including sort/selection state (e.g. `count|maxMs|sortKey|sortDir|selectedId`), and migrate Timeline's `_tlSig` blocks onto the same helper. Either adopt it everywhere heavy, or the helper is dead code (see DC-1) — but it must not be silently deleted, because adoption is the intended resolution.
**Justification:** Free under Invariant 2 (zero unnecessary churn); the helper exists, is documented for exactly this case, and preserves scroll position. Research confirms full-subtree `innerHTML` rebuild forces a complete repaint + style-recalc — wasteful on the F9 overlay which shares browser resources with the running game.
**Expected Benefit:** Eliminates per-poll repaint of the largest panels; the dashboard's own UI overhead drops on idle polls (the thing the profiler is meant to *measure*, not add).
**Impact Assessment:** Must verify the early-return doesn't strand sibling static-node writes (e.g. Lag's `setLagHeatmapTitle`/`subRoot`/`legendRoot`) — those sit outside the gated content and must stay outside the gate or be folded into the signature. Re-verify each gated panel renders + updates on real data change via L4.

### TL-1 — `renderTimelineSwimlanes` allocates two Maps + a per-segment object array BEFORE its signature gate
- [x] The swimlane renderer does its heavy index-Map build and window scan before computing the `_tlSig` bail check, so a no-change poll still allocates and discards two Maps + N `segKey` strings. — IMPLEMENTED: `Js.Timeline.cs` `renderTimelineSwimlanes` — moved the `lifetimeIx`/`attrIx` Map builds to AFTER the `_tlSig.swimlanes` gate (they feed only the lane render loop, which is past the gate; the signature needs only `win` + `byFamily` counts + filter/selection). Pure statement reorder, output identical. Verified via `--tabs` preview render (timeline tab renders swimlanes correctly).

**Category:** Performance Improvement   **Severity:** Medium   **Effort:** S   **Behavioural Impact:** none (pure reorder, same output)
**Location:** `Web/Assets/Js/Js.Timeline.cs:317-360` — `renderTimelineSwimlanes()`; gate at `:357-359`, heavy work at `:316` (`swimlaneWindow()`), `:320-331` (`lifetimeIx` + `attrIx` Maps), `:333-354` (`byFamily`).
**Current State:** Every other renderer in the file (heatstrip `:187`, transitions `:263`, attendance `:547`, deaths `:631`, chronicle `:712`) computes its signature first and bails before heavy work. Swimlanes is the lone exception: it builds `lifetimeIx`/`attrIx` (one `set` + `segKey` string-concat per entry) and the per-segment `byFamily` array, then computes the sig at `:357` and returns. On every 2.5 s no-change poll those allocations are made and thrown away.
**Proposed Change:** Reorder so the signature (which needs only `win`, `timelineFilter`, `selectedSegmentKey`, and per-family `count`/`lastStartTick`) is computed after `byFamily` (needed for the family counts) but the two index Maps are deferred behind the `:357` gate. `swimlaneWindow()` stays (it feeds `win.startMs/endMs` into the sig).
**Justification:** The file's only material per-render allocation on the no-change path; the file's own header comment (`:35-37`) sets out the caching contract that this one renderer silently breaks. Web render path, not the C# per-tick hot path — not an Invariant-2 *budget* violation, but the same avoidable-churn class.
**Expected Benefit:** Swimlane no-change polls become allocation-free, matching the other six renderers.
**Impact Assessment:** Pure statement reorder; output identical. Verify via L4 the swimlanes still render + update on segment change.

### SUM-1 — `sampleModStream` 5 s timer runs unconditionally even when Summary is not the active tab
- [x] `setInterval(sampleModStream, 5000)` fires forever; it walks every mod and mutates `modStreamHistory` even when the user is on another tab, where the result is never rendered. — DEFERRED: no change recommended by the finding itself (verified-intentional exception — tab-gating would zero the rolling cost-stream window off-Summary and show a gap on return). Left as-is per the finding's own conclusion.

**Category:** Performance Improvement   **Severity:** Low   **Effort:** S   **Behavioural Impact:** behaviour-preserving ONLY if history continuity off-tab is not required — see assessment
**Location:** `Web/Assets/Js/Js.Summary.cs:300-323` (`sampleModStream`) + `:383` (`setInterval(sampleModStream, 5000)`).
**Current State:** The sampler loops `lastMods.mods`, pushes a composite value into each mod's `modStreamHistory` array (cap 50), and prunes dropped mods — every 5 s, regardless of `activeTab`. It only *renders* when Summary is active (`:322` guards `renderModStream`), but the sampling/allocation always runs. This is intentional in one sense (the cost-stream should keep its rolling window warm so switching to Summary shows continuous history).
**Proposed Change:** Leave the **sampling** unconditional (it is what keeps the rolling window honest across tab switches — the documented intent at `:295`), but this is the one timer with a deliberate off-tab side effect, so it is recorded as a verified-intentional exception rather than gated. No change recommended; flagged so a future reader doesn't "optimise" it by tab-gating and silently break cost-stream continuity.
**Justification:** Recorded as intentional, not a defect. The sampling cost is O(mods) every 5 s — trivial. Tab-gating it would zero the history while off-Summary and the stream would show a gap on return, contradicting `:291-298`.
**Expected Benefit:** none (no change); prevents a future false-optimisation.
**Impact Assessment:** No action. Listed for completeness against the "timer fires when tab inactive" smell.

---

## Pattern Extraction

### CC-2 — The `.dtable` sortable-table scaffold is hand-rolled 11× across four tabs with no shared builder
- [x] `<table class='dtable'><thead><tr>…</tr></thead><tbody>${rows}</tbody></table>` plus the `th.sortable`/`.sorted`/caret idiom is re-implemented per tab; there is no `dtable()` / `sortableHead()` in the shared `Js.Components.cs`. — IMPLEMENTED (scaffold) / PARTIALLY DEFERRED (header unification). Added `dtable(headCells, bodyRows, o)` to `Js.Components.cs` and routed all 12 hand-rolled scaffolds through it (Insights ×6, Lag ×3, Memory ×1, Timeline ×2 — `grep "<table class='dtable" Web/Assets/Js/` now returns only the docstring). Byte-identical output. Relocated the existing `sortableHead()` from `Js.Insights.cs` to `Js.Components.cs` (its Insights callers unchanged) so it is the documented shared home. DEFERRED: collapsing `lagSortTh` (inline `onclick`) and `memTh` (`data-msort` container-delegation) onto the one `sortableHead` (deferred `setTimeout` element-delegation) — the three use DIFFERENT binding mechanisms, so unifying them is a behavioural-shape change, not byte-identical (the finding's own Impact Assessment flags this as "NOT a same-file free win; confirm before implementing"). Left `lagSortTh`/`memTh` in place.

**Category:** Pattern Extraction (cross-cutting)   **Severity:** Medium   **Effort:** M   **Behavioural Impact:** none if extraction is byte-identical
**Location:** scaffold count (verified via grep): `Js.Insights.cs` ×6 (`:263,451,463,480,532,670`), `Js.Lag.cs` ×3 (`:340,424,619`), `Js.Memory.cs` ×1 (`:218`), `Js.Timeline.cs` ×1 (`:511`). The sortable-header idiom is independently re-implemented as `sortableHead` (`Js.Insights.cs:116`) AND `memTh` (`Js.Memory.cs:194`) AND `lagSortTh` (`Js.Lag.cs`), with `Js.Memory.cs:21` literally commenting "mirrors the Insights sortableHead model" — i.e. a copy, not a reference.
**Current State:** No `dtable()` helper exists (`grep -rn "function dtable" Web/` → empty). Each table block independently writes the `<table>`/`<thead>`/`<tbody>` shell and its own sortable-header function.
**Proposed Change:** Add a shared `dtable(headCells, bodyRows)` and a shared `sortableHead(cols, state, onSort, rootId)` to `Js.Components.cs`; route the 11 scaffolds and the three header functions through them. The dormant/memory/lag sortable headers collapse onto the one `sortableHead`.
**Justification:** Eleven copies of one scaffold + three copies of the sortable-header idiom is the clearest pattern-extraction yield in the cluster. Texture is uniform; a single builder is byte-identical.
**Expected Benefit:** One source of truth for table chrome; a future column-width / caret / a11y fix lands once instead of 11–14 times (Blast-Radius Discipline).
**Impact Assessment:** Cross-file change touching four tab renderers — NOT a same-file free win; confirm before implementing. Re-verify every table tab via L4 (header sort, row render, scroll preservation). The file-local fallback (a per-file `dtable()` in just `Js.Insights.cs`) is the conservative subset if the shared move is deferred.

### LAG-1 — Context-sentinel filter implemented twice with divergent sentinel sets (latent inconsistency)
- [x] The "is this a real context vs a placeholder" guard exists in two places; the heatmap's copy is weaker than the cluster table's corrected `realCtx`, so an all-`'—'`/`'none'` context set renders a junk single-column heatmap. — IMPLEMENTED: `Js.Lag.cs` — hoisted the corrected predicate to a single file-local `lagRealCtx(s)` declared near the top of the fragment (trims + lowercases, rejects `''`/`'—'`/`'-'`/`'none'`/`'n/a'`). The heatmap's weak `c => c != null && c !== '' && c !== '-'` filter now calls `contexts.filter(lagRealCtx)`, and the cluster table's column/chip/detail logic call the same `lagRealCtx`. Guard and rows now agree; the all-placeholder junk single-column grid no longer renders. 10 `lagRealCtx` references, 0 bare `realCtx` left.

**Category:** Pattern Extraction (duplication + correctness divergence)   **Severity:** Medium   **Effort:** S   **Behavioural Impact:** changes heatmap output ONLY in the all-placeholder edge case, toward the behaviour the code already declares correct
**Location:** `Web/Assets/Js/Js.Lag.cs:154` (heatmap, weak) vs `:256-260` (`realCtx`, corrected).
**Current State (verified):** `:154` filters `c => c != null && c !== '' && c !== '-'` — case-sensitive, lets `'—'` (em-dash), `'none'`, `'n/a'` through. `:256-260` defines `realCtx = s => { … t !== '' && t !== '—' && t !== '-' && t !== 'none' && t !== 'n/a' }` (trims + lowercases), and its own comment at `:253-255` records that the earlier weak guard "let them through, so an all-'—' column survived". The heatmap (`:154`) is exactly that earlier weak guard, un-fixed.
**Proposed Change:** Hoist `realCtx` to a single file-local function declared once near the top of the fragment; use `contexts.filter(realCtx)` at `:154`.
**Justification:** Free de-duplication that also closes a real divergence using a predicate the code already declares correct.
**Expected Benefit:** Heatmap and cluster table agree on what counts as context; the all-placeholder junk-column case stops rendering.
**Impact Assessment:** Touches what renders in an edge case — confirm with user, then verify via L4 with a fixture whose contexts are all `'—'`/`'none'`.

### LAG-2 — Three near-identical "top-mod cell" blocks (modColor + truncated name + share% [+ split bar])
- [x] The dominant-mod inline cell is hand-assembled three times with the same shape; one file-local helper would collapse them. — IMPLEMENTED: `Js.Lag.cs` — added file-local `lagTopModCell(modId, modName, share, withBar)` and routed the cluster-table cell (`withBar=true`) and the rhythm-cluster cell (`withBar=false`) through it. (The causality `segs` echo at the old `:507-514` is a `splitBar` over `topContributors`, a different shape — left as-is per the finding's "partial echo" note.) Kept Lag-local. Output identical (verified via preview render).

**Category:** Pattern Extraction (internal)   **Severity:** Low   **Effort:** S   **Behavioural Impact:** none
**Location:** `Web/Assets/Js/Js.Lag.cs:288-296` (cluster `modCell`), `:608-615` (rhythm-cluster row), partial echo `:507-514` (causality `segs`).
**Current State:** Each block independently computes `modColor(...)`, clamps the share, formats `(share*100).toFixed(0)+'%'`, and `escapeHtml(truncate(name,16))` into a `<span class='nm' style='color:…'>` + muted-share span; `:296` and `:615` are near character-identical.
**Proposed Change:** Add file-local `lagTopModCell(modId, modName, share, withBar)`; call from all three sites. Keep it Lag-local (Extensibility rule: build the seam at the second consumer — these three are it).
**Justification:** Three structurally identical hand-rolls in one 629-line file clears the internal-duplication bar; composes the same shared primitives, no behaviour change.
**Expected Benefit:** One place to adjust the top-mod cell idiom.
**Impact Assessment:** File-local; verify Lag cluster/rhythm/causality cells via L4.

### TL-2 — Identical segment-span scan duplicated between `timelineWindow` and `swimlaneWindow`
- [x] A ~12-line segment recent/open min-max scan is character-for-character the same in both window helpers. — IMPLEMENTED: `Js.Timeline.cs` — extracted `_segmentSpan(s, e)` (folds the recent + open segment scan, including the open-segment `nowMs` widening, into a running `[s,e]`, returns `{s, e}`). `timelineWindow` seeds it with `Infinity/-Infinity` then folds in transitions + activity minutes; `swimlaneWindow` adds its 3% margin. One scan to maintain. Output identical (timeline tab renders correctly in preview).

**Category:** Pattern Extraction (internal)   **Severity:** Low   **Effort:** S   **Behavioural Impact:** none
**Location:** `Web/Assets/Js/Js.Timeline.cs:62-95` (`timelineWindow`) and `:110-135` (`swimlaneWindow`).
**Current State:** Lines `:64-76` and `:112-124` are identical loops over `lastSegments.recent` + `lastSegments.open` computing the start/end span (including the open-segment `nowMs` widening). `timelineWindow` then folds in transitions + activity minutes; `swimlaneWindow` adds a 3 % margin.
**Proposed Change:** Extract a local `_segmentSpan(s, e)` returning `{s, e}` after the segment scan; both callers continue to widen/margin it.
**Justification:** Identical (not merely similar) non-trivial block, two callers — a future fix to the open-segment `nowMs` logic currently needs applying twice (the drift Blast-Radius warns about).
**Expected Benefit:** One scan to maintain.
**Impact Assessment:** File-local; verify Timeline heatstrip + swimlane windows align via L4.

### LAG-3 / INS-4 — Below-threshold internal dup (recorded, not recommended as free)
- [x] `lagGalaxySort`/`lagDensitySortBy` share one toggle body (`Js.Lag.cs:226-230`/`:360-364`); Insights `compositionBar`/`legendSegs` share the `ROSTER_CATS→segs` map (`Js.Insights.cs:333-343`/`:417-419`). — DEFERRED: finding's own recommendation is "leave as-is" (below the 3+ internal-duplication floor; a shared helper would carry a mode flag costing more clarity than it saves). No action, per the finding. (NB: `lagGalaxySort` is now `lagClustersSort` after the DR-6 rename, but the toggle-body duplication verdict is unchanged.)

**Category:** Pattern Extraction (internal, 2-instance)   **Severity:** Low   **Effort:** S   **Behavioural Impact:** none
**Location:** as above.
**Current State:** Each is a 2-instance pair with a real semantic difference (the sort togglers are bound by name into `onclick` strings; the composition maps differ in normalisation vs zero-filter).
**Proposed Change:** Leave as-is. Extract only if adjacent work (LAG-2 helper pass) is already touching the region.
**Justification:** Below the 3+ internal-duplication floor; a shared helper would carry a mode flag costing more clarity than it saves.
**Expected Benefit:** none unless folded into adjacent work.
**Impact Assessment:** No action.

---

## Documentation Rot

### DR-1 — `DataRegistry` + `KpiStat` doc-comments name `ProfilerSystem.Load` as the registration site; the real site is `PerformanceProfiler.RegisterDataPipeline`
- [ ] Two XML doc-comments cite a registration site that does not exist; streams are registered in `PerformanceProfiler.RegisterDataPipeline` (called from `PerformanceProfiler.Load`), and `ProfilerSystem` has no `Load` method. — DEFERRED: handled by core agent (touches `Data/DataRegistry.cs` + `Data/Stats/KpiStat.cs`, outside this agent's `Web/Assets/` scope).

**Category:** Documentation Rot   **Severity:** Medium   **Effort:** S   **Behavioural Impact:** none
**Location:** `Data/DataRegistry.cs:28` ("populated by `ProfilerSystem.Load`") and `Data/Stats/KpiStat.cs:33` ("register in `ProfilerSystem.Load` via `DataRegistry.Shared.Register(new KpiStat())`").
**Current State (verified):** `PerformanceProfiler.cs:127` defines `private static void RegisterDataPipeline()`, called from `Load()` at `:70`; it contains every `r.Register(...)` line (`:133-181`), including `new Data.Stats.KpiStat()` at `:142`. `ProfilerSystem` (`Profiling/ProfilerSystem.cs`) has no `Load` method. The `_Overview` flagged exactly this pair.
**Proposed Change:** Replace `ProfilerSystem.Load` with `PerformanceProfiler.RegisterDataPipeline` in both doc-comments (the `KpiStat` line keeps its `DataRegistry.Shared.Register(new KpiStat())` example, which is accurate).
**Justification:** Stale triple — the comment misroutes a cold reader looking for where streams register; convention §16 makes the registration site load-bearing.
**Expected Benefit:** Doc points at real code; future stream-author finds the registration list.
**Impact Assessment:** Comment-only, zero behaviour. The `<c>ProfilerSystem.Load</c>` is the precise text to replace in each file.

### DR-2 — `Js.Tabs.cs` keyboard comment says "1-5" but the map (and tab strip) is 1-6
- [x] The comment above the keyboard handler says "Keyboard 1-5 switches tabs" while the `map` covers `1`–`6` (six tabs since the v0.17 Memory tab). — IMPLEMENTED: `Js.Tabs.cs:29` — "Keyboard 1-5 switches tabs." → "Keyboard 1-6 switches tabs." Comment-only.

**Category:** Documentation Rot   **Severity:** Low   **Effort:** S   **Behavioural Impact:** none
**Location:** `Web/Assets/Js/Js.Tabs.cs:29` (comment) vs `:32` (`{ '1':'summary','2':'timeline','3':'lag','4':'insights','5':'self','6':'memory' }`).
**Current State (verified):** Six-tab map; comment frozen at the pre-Memory five-tab era.
**Proposed Change:** "Keyboard 1-5" → "Keyboard 1-6".
**Justification:** The exact "1-5 where it's 1-6" doc-rot the brief names.
**Expected Benefit:** Comment matches code.
**Impact Assessment:** Comment-only.

### DR-3 — Timeline change-history comments encode prior state ("than before", "were unlabelled")
- [x] Two forward-facing comments narrate the pre-fix state instead of describing current behaviour, the stale-triple pattern Editing Discipline warns against. — IMPLEMENTED: `Js.Timeline.cs` — reworded both to current-state. The heatstrip-legend comment "Both were unlabelled, so a reader saw 'red dots'…" → "The legend names the marks and anchors the ramp… so the strip never reads as bare red dots…"; the transition edge-band comment "Wider edge bands than before so…" → "The edge bands are wide enough that…". Comment-only.

**Category:** Documentation Rot   **Severity:** Low   **Effort:** S   **Behavioural Impact:** none
**Location:** `Web/Assets/Js/Js.Timeline.cs:286-287` ("Wider edge bands **than before** so…") and `:220-221` ("Both **were unlabelled**, so a reader saw 'red dots'…").
**Current State:** Both comments carry change-history a future reader doesn't need; they should state current state.
**Proposed Change:** Reword to current-state ("Edge chips anchor inward so the clipped track never cuts the label"; "Each marker carries its kind label so the strip never reads as bare red dots").
**Justification:** Editing Discipline — forward-facing comments describe the desired state, not the migration story.
**Expected Benefit:** Comments age out of "what changed" framing.
**Impact Assessment:** Comment-only.

### DR-4 — Stale F10 reference is CORRECT historical narration — verified NOT rot
- [ ] The `_Overview` flagged "three F10 comments in `PerformanceProfiler.cs`"; those are already corrected to F9. The one surviving F10 mention is intentional history.

**Category:** Documentation Rot (cleared)   **Severity:** —   **Effort:** —   **Behavioural Impact:** none
**Location:** `UI/ProfilerOverlaySystem.cs:30` — the only remaining `F10` in the codebase (verified `grep -rn F10`).
**Current State:** The comment reads "Pre-v0.9 had two keybinds: F9 to toggle the in-game overlay and F10 to launch the browser. With the overlay archived, F9 takes over…" — it explicitly narrates retired history to justify the single-F9 design. `PerformanceProfiler.cs`'s `Dashboard` doc (`:52-58`) and `Load` log lines (`:99-118`) already say F9 — the three flagged comments were fixed in a prior pass.
**Proposed Change:** None. The `_Overview` Known-Issues note about "three F10 comments in `PerformanceProfiler.cs`" is itself stale and should be retired by the next `upkeep-context` pass.
**Justification:** Removing the ProfilerOverlaySystem F10 line would delete the rationale for why one bind exists. Keep it.
**Expected Benefit:** none (no change); avoids deleting load-bearing history.
**Impact Assessment:** No action; flag the `_Overview` drift to upkeep.

### DR-5 — Vault/README design docs describe a Lag "galaxy scatter" + "spikes/stalls expandable lists" the code no longer has
- [ ] The Lag tab renders a sortable `.dtable` ("fingerprint clusters"), not a scatter; spikes/stalls are `splitBar` segments, not expandable lists. The design-doc vocabulary is stale (it even misled this audit brief).

**Category:** Documentation Rot (cross-doc, out-of-repo-code)   **Severity:** Low   **Effort:** S   **Behavioural Impact:** none
**Location:** the drift is in the design docs / `web-dashboard.md` description, not in `Js.Lag.cs` (whose own docstrings are accurate). The in-code residue is identifier-level (see DR-6).
**Current State:** `Js.Lag.cs:198-213` composes `heatmapMatrix()` (no hand-rolled hex grid); the "fingerprint" panel is a `.dtable`; spikes/stalls are encoded as `splitBar` segments at `:406-412`.
**Proposed Change:** Reconcile the Lag-tab description in the next `upkeep-context`/README pass; no code edit.
**Justification:** Honesty — the in-file comments passed; the rot is in the surrounding doc layer.
**Expected Benefit:** Design docs match shipped reality.
**Impact Assessment:** Doc-only, separate owner.

### DR-6 — Vestigial "galaxy" naming in `Js.Lag.cs` (renders a table, not a scatter)
- [x] `renderLagGalaxy`/`lagGalaxySort`/`lagGalaxyPick`/`lagGalaxySelected` all manage a `.dtable` titled "fingerprint clusters"; the "galaxy"/scatter framing is dead vocabulary from a retired design. — IMPLEMENTED: `Js.Lag.cs` — renamed `renderLagGalaxy`→`renderLagClusters`, `lagGalaxySort`→`lagClustersSort`, `lagGalaxyPick`→`lagClustersPick`, `lagGalaxySelected`→`lagClustersSelected`, at every call site (the `renderLag` dispatcher, the `lagSortTh` header `onSort` args, the row `onclick`). Blast radius fully file-local (`grep "lagGalaxy\|Galaxy"` over the whole `Web/` tree = 0). Lag cluster table + click-to-select render correctly in preview.

**Category:** Documentation Rot (identifier-level)   **Severity:** Low   **Effort:** S   **Behavioural Impact:** none (rename only; blast radius fully file-local — verified `lagGalaxy*` appears nowhere outside `Js.Lag.cs`; `Js.Topbar.cs:51` dispatches `renderLag`, not `renderLagGalaxy`)
**Location:** `Web/Assets/Js/Js.Lag.cs:31` (`lagGalaxySelected`), `:226` (`lagGalaxySort`), `:232` (`renderLagGalaxy`), `:300` (`onclick='lagGalaxyPick(...)'`), `:343` (`lagGalaxyPick`).
**Current State:** Identifiers actively mislead — a reader greps "galaxy" expecting `scatter()` and finds a sortable table.
**Proposed Change:** Rename `renderLagGalaxy`→`renderLagClusters`, `lagGalaxySort`→`lagClustersSort`, `lagGalaxyPick`→`lagClustersPick`, `lagGalaxySelected`→`lagClustersSelected`. All call sites are within the one fragment.
**Justification:** Free file-local rename; the names caused real confusion (this brief inherited the stale vocabulary).
**Expected Benefit:** Identifiers match what they render.
**Impact Assessment:** File-local; verify Lag cluster table + click-to-select via L4 after rename.

---

## Configuration / Token-System Drift

### CSS-1 — Semantic colour washes hardcode raw sRGB `rgba()` approximations of OKLCH tokens (two sources of truth)
- [ ] Status-tag / chip / callout / status-glow backgrounds use raw sRGB `rgba(r,g,b,a)` literals that approximate `--good`/`--amber`/`--orange`/`--danger`/`--magenta`; if a token's OKLCH value changes, these stale copies silently drift. Convention §22 mandates OKLCH tokens. — DEFERRED: the finding itself states these rgba literals are *approximations* of the OKLCH tokens, so re-deriving from the token (`oklch(from var(--good) …)` or a `--*-wash` token computed from the OKLCH source) produces the *correct* colour, which is a real — if near-imperceptible — shift from the currently-rendered sRGB approximation. The implementation brief's explicit instruction is "if you cannot guarantee the colour is visually identical, DEFER it" and the orchestrator vision-checks the washes; visual identity cannot be guaranteed here by construction (the whole premise is that they differ). Deferred to the orchestrator's vision-checked pass rather than risk a visible status-glow shift. The pure-black/white elevation shadows + the `#11161fee` tooltip surface are noted by the finding as NOT colour and out of CSS-1's chromatic scope regardless.

**Category:** Configuration Drift / Pattern Extraction (cross-cutting CSS)   **Severity:** Medium   **Effort:** M   **Behavioural Impact:** near-imperceptible colour shift if re-derived from tokens (sRGB approximations ≈ the OKLCH source); verify visually
**Location (verified via `grep -rnE '#[0-9a-f]{3,8}|rgba?\(|hsla?\(' Web/Assets/Css/ | grep -v oklch`):
  - `Css.Kpis.cs:49-52` — `.kpi-tag.{good,warn,orange,bad}` backgrounds `rgba(79,157,106…)/(184,138,37…)/(201,127,60…)/(185,78,88…)` (sRGB copies of `--good/--amber/--orange/--danger`).
  - `Css.Components.cs:85-87` — `.chip.{good,warn,bad}` border-colors; `:209/211` `.callout.{warn,bad}` border + background.
  - `Css.Shell.cs:70/75/79/86/89-90` — `.live-dot.{ok,err,paused,db}` + `pulse` glow halos (`rgba(149,212,163)`/`(247,118,142)`/`(224,175,104)`/`(131,103,163)`/`(79,157,106)`).
  - `Css.Heatmap.cs:61/77` — boss-cell red halo `rgba(247,118,142,0.45)`.
  - `Css.Tooltip.cs:22` — tooltip background `#11161fee` (raw hex, a surface colour).
**Current State:** Every one of these encodes meaning (status/severity), so it is **not** a monochrome-chrome violation (colour is on data/status tokens, not decorating chrome). But it bypasses the OKLCH single-source-of-truth: the same semantic colour now lives as both an OKLCH `--token` and a sRGB `rgba()` approximation. `Css.Timeline.cs:24` even documents the *correct* discipline ("every colour is a `var(--…)`, no raw rgba()/hex") — these files violate it.
**Proposed Change:** Re-express the chromatic washes/glows as the token in a relative-colour or alpha form (e.g. `oklch(from var(--good) l c h / 0.10)` where supported, or a paired `--good-wash` token defined once in `Css.Palette.cs`). Pure-black/white shadows (`rgba(0,0,0,…)`, `rgba(255,255,255,…)` at `Css.Components.cs:189`, `Css.Mods.cs:62`, `Css.Shell.cs:121/143`, `Css.Tooltip.cs:28/38`) are NOT colour and may stay as-is (they are neutral elevation/scrim).
**Justification:** Token drift is the convention-§22 failure: a future palette tweak updates the OKLCH ramp but leaves the sRGB copies stale, producing a status glow that no longer matches its dot. Centralising removes the second source of truth.
**Expected Benefit:** One palette edit propagates to every wash/glow; no stale-copy drift.
**Impact Assessment:** Touches visible colour (faint washes/glows). Implement as added `--*-wash` tokens (additive, lowest-risk) and re-verify each tag/chip/callout/status-dot via L4 computed-style + a vision spot-check. The `#11161fee` tooltip background is a surface and should become a `--popover`-derived token.

### CSS-2 — Dead palette tokens `--purple` and `--cyan` (defined, zero consumers)
- [x] Two data-viz tokens are defined in `Css.Palette.cs` but referenced nowhere in any CSS or JS (verified `grep` of the whole `Web/` tree, including string literals). — IMPLEMENTED: `Css.Palette.cs` — removed the `--purple: oklch(0.72 0.11 300)` and `--cyan: oklch(0.72 0.11 215)` declarations. Re-verified zero consumers across `Web/` before removal (`grep var(--purple)|var(--cyan)` = 0 outside the defs). `--magenta` (used by the db status dot) kept. Compile + render unaffected.

**Category:** Dead Code (CSS)   **Severity:** Low   **Effort:** S   **Behavioural Impact:** none
**Location:** `Web/Assets/Css/Css.Palette.cs:107` (`--purple: oklch(0.72 0.11 300)`) and `:108` (`--cyan: oklch(0.72 0.11 215)`).
**Current State (verified):** `--purple`/`--cyan` have zero references anywhere. By contrast `--magenta` (`:106`) is used (`Css.Shell.cs:85`, the db status dot), `--good-bar` (`:102`) is used twice — those stay. There is no `--cool` token (the `.chip.cool` class at `Css.Components.cs:88` uses `--accent`, not a `--cool` token — verified).
**Proposed Change:** Remove the `--purple` and `--cyan` token definitions.
**Justification:** Confidently dead — defined-but-unreferenced design tokens; removing changes nothing rendered.
**Expected Benefit:** Palette lists only the tokens the UI actually uses.
**Impact Assessment:** Removing two `:root` custom-property declarations with zero consumers is byte-safe; verify via L4 the dashboard still renders (it must, nothing reads them).

### ProfilerConfig — 9 unused `using` directives over an empty class body
- [ ] `ProfilerConfig.cs` carries the full project `using` block (`Data.Detectors`, `Data.Aggregators`, `Data.Stats`, `Profiling`, persistence, etc.) but the class has no fields and uses none of them. — DEFERRED: `ProfilerConfig.cs` is at the repo root, outside this agent's `Web/Assets/` scope. Left for the core agent.

**Category:** Configuration Drift / Dead Code   **Severity:** Low   **Effort:** S   **Behavioural Impact:** none
**Location:** `ProfilerConfig.cs:5-14` (the `using` block); the class body (`:30-33`) only overrides `Mode => ConfigScope.ClientSide` and needs `Terraria.ModLoader.Config` (`:3`) only.
**Current State (verified):** Class is empty post-overlay-archive (documented at `:17-27`); the nine `Data.*`/`Profiling.*` usings are vestigial from the overlay-config era. Not a CS0105 risk (no in-file duplicates), just unused.
**Proposed Change:** Drop the unused `using`s; keep `using Terraria.ModLoader.Config;`.
**Justification:** Dead imports over an empty class; the file is the cleanest single instance of the project-wide over-broad `using` block.
**Expected Benefit:** The file reads as the empty placeholder it is.
**Impact Assessment:** Compile-only; `dotnet msbuild | grep 'error CS'` must stay 0.

---

## Modularisation Verdicts (required)

| Fragment | LOC | Verdict | Justification |
|---|---|---|---|
| `Js.Timeline.cs` | 808 | **Leave as-is** | The per-tab-verbatim-fragment convention (§20) IS the split unit. All sections are views of one timeline data stream, coupled by shared state that cannot be cleanly cut: `timelineFilter` is read by transitions + swimlanes + attendance; `selectedSegmentKey` couples swimlanes (click) to detail (drill); `_tlSig`/`swimlaneWindow`/`timelineWindow`/`segKey` are shared across sub-renderers; one `renderTimeline()` + one `pollTimelineData()` is the unit tML's tab system drives. Splitting would force shared state + the window helpers into a third "Timeline-common" fragment plus a hand-maintained concat-order constraint — net more files, no second consumer. Long because it is the richest tab (7 sub-views), not because it conflates domains. |
| `Js.Insights.cs` | 676 | **Leave as-is** | One tab, one concern. Seven sub-renderers share one poll loop (`pollInsightsData`), one fan-out (`renderInsights`), one set of `last*` caches. No sub-fragment seam another file would consume; the only shared-ish thing (`sortableHead`) is a *cross-file* extraction candidate (CC-2), not a Insights-internal split. Length is surface count (seven visualisations), not tangled responsibility. |
| `Js.Lag.cs` | 629 | **Leave as-is** | One cohesive tab: one poll loop, one `renderLag` dispatcher, seven panel sub-renderers each `if (!root) return` no-op-guarded (the modularity the project actually requires, achieved *within* the file). The one cross-cutting helper candidate (the top-mod cell, LAG-2) has exactly one tab's consumers. Splitting fragments the single script block for no cohesion gain. The real levers are CC-1 (`renderIfChanged` adoption) + the internal de-dups, not a file split. |

The verbatim-string-fragment convention already provides the right granularity (one fragment per tab); none of the three mixes unrelated concerns.

---

## Verified-Clean Ledger (no findings — evidence cited)

| Axis | Verdict | Evidence |
|---|---|---|
| **Duplicate `using` (CS0105 risk)** | **CLEAN** repo-wide | The per-file `grep -nE '^using ' \| sort \| uniq -d` sweep over every non-bin/obj `.cs` returned **zero** files with in-file duplicate usings (exit 1). No CS0105 risk anywhere. |
| **Invariant 3 (honesty contract)** | **CLEAN** across the whole web layer | `grep -rniE 'must remove\|core mod\|removable\|free to remove\|should remove\|bad mod\|caused by\|safe to remove'` over `Web/Assets/` returns one hit — a *comment* (`Js.Insights.cs:30`) listing banned words as things NOT to do. Every player-facing string is descriptive and several explicitly defuse the normative reading: `Js.Summary.cs:251` ("names no verdict"), `Js.ModCard.cs:106` ("a marginal upper bound… This describes measured cost, not a recommendation"), `Js.Insights.cs:145-147` ("buckets are measured usage thresholds, not judgements"), the §24 render-site guard. The mod-card "FPS without this mod" math — the closest to prescriptive — is badged as a measured upper bound, not a recommendation. The HTML shell (`IndexHtml.*.cs`) carries no prescriptive prose. |
| **Monochrome-chrome / OKLCH (§22)** | **CLEAN** (chrome) | Every colour use encodes data/status (perf ramp `--perf-0..4`, per-mod `modColor`, status hues, the `streamRamp`/`MEM_FP_CATS` monochrome luminance ramps which are on-brand ordinal encodings). No decorative hue on panels/text/borders. The raw-sRGB issue (CSS-1) is a *token-source* drift, not a chrome-colour violation — the colours are on status/data tokens and DO encode. `Css.Palette.cs:17-40` + `Js.Helpers.cs:64-68` enforce the split. |
| **`.csproj` / `build.txt` dependency hygiene** | **CLEAN** | LiteDB is genuinely used (persistence); `PrivateAssets=all` is correct. `build.txt buildIgnore` correctly excludes `*.md`, `design\`, `context\`, `tools\`, `Tests\`, VCS/build dirs. The `UI/Overlay/*` archive is deliberately `#if false`-guarded (documented in `.csproj`), not dead code to flag. No unused package refs. |
| **`Js.Mods.cs` / `Js.Summary.cs` / `Js.Memory.cs` / `Js.Self.cs` / `Js.Kpis.cs` / `Js.ModCard.cs` / `Js.Tooltips.cs`** | **CLEAN** | Read in full. Compose correctly from the shared vocabulary; well-justified per-render guards (mod-tree hover-suppression `Js.Mods.cs:19-28/266-274`, memory search-focus restore `Js.Memory.cs:301-309`, donut/memory delegated-once binding). No dead functions, no inline re-implementation of shared helpers, descriptive copy throughout. |
| **`renderIfChanged` as deletable dead code** | **Triage → adopt, don't delete** | Zero call sites (DC-1 is CC-1's flip side). The correct resolution is adoption on the heavy panels (CC-1), not deletion — Timeline's 25 `_tlSig` references prove the pattern is needed. Deleting it would be wrong. |

---

## Potential-Issues Candidates (ambiguous / needs-confirmation, not free)

- **Timeline death-chip `buff-on → green` collides semantically with the perf-ramp green** (`Js.Timeline.cs:615-625` `deathEventChipClass`). The sibling `chronicleKindClass` (`:674-682`) is deliberately severity-only with a comment justifying that discipline "so the perf-ramp green keeps its single meaning". `deathEventChipClass` colours by *event category* (legitimate categorical encoding, passes the literal monochrome-chrome rule) but is inconsistent with the sibling and re-uses green for "buff applied". Resolving changes player-facing visuals → **confirm before changing**, not a free win.
- **`sortableHead` `setTimeout(…,0)` re-binds header clicks every render** (`Js.Insights.cs:125-138`): the `dataset.bound` guard reads as cross-render dedupe but the `<th>` nodes are replaced each render, so it only dedupes within one timer pass. Not a leak (handlers GC with old nodes), negligible cost at 3 s poll. The clean fix (event-delegation on the stable container, mirroring `obs-sort` at `:297-302`) is a behavioural-shape change → confirm, not free. **Subsumed by CC-1** if the dormant panel gets a `renderIfChanged` gate.
- **`lagApplySort` per-render `.slice().sort()` allocation** (`Js.Lag.cs:72-81`): trivial at realistic row counts; **resolved by CC-1** (sort only runs on data/sort-state change once gated). No standalone action.
