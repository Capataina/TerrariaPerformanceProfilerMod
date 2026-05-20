# Overlay

*Maturity: comprehensive · Stability: unstable — tab content evolves with each new measurement subsystem; the framework itself is settled.*

## Scope / Purpose

The overlay is the **player surface** for everything the profiler measures. F9 toggles it. A header strip drives shared chrome (NOW vs 30s-avg, LIVE vs PAUSED, the CPU/MEM/BOTH metric pill, PROFILER HEALTH coverage bar). Five concrete tabs hang off a single registry: Overview, Tree, Spikes, Events, Insights.

The overlay is **read-only** (Invariant 1) and **non-modal** (Invariant 3 — the player can dismiss with Esc mid-fight; no `IngameFancyUI` lockout).

## Boundaries / Ownership

Files:

- Framework: `UI/Overlay/IOverlayTab.cs`, `TabRegistry.cs`, `OverlayPanel.cs`, `OverlayDraw.cs`, `OverlayLayout.cs`, `OverlayState.cs`.
- Mount glue: `UI/ProfilerOverlay.cs`, `UI/ProfilerOverlaySystem.cs`, `UI/ProfilerTheme.cs`.
- Tabs: `UI/Overlay/Tabs/OverviewTab.cs`, `TreeTab.cs`, `SpikesTab.cs`, `EventsTab.cs`, `InsightsTab.cs`.

Owns:

- The F9 keybind + layer mount via `ModifyInterfaceLayers`.
- The tab registry and visible-tab resolution.
- The chrome (header, tab strip, stats line, PROFILER HEALTH bar).
- Per-tab state (scroll position, expanded sets, derived rankings).
- The 1 Hz tab Tick cadence and the truncation caches.

Does not own:

- The data the tabs display — Overview/Tree/Spikes read from `MetricCollector`, Events reads from `EventAggregator`, Insights reads from `InsightsEngine.Shared`.
- The F9 keybind registration target — `KeybindLoader` is owned by `ProfilerOverlaySystem`, but the binding itself lives in tModLoader.

## Current Implemented Reality

### Tab contract (`IOverlayTab`)

Six members (`UI/Overlay/IOverlayTab.cs`):

| Member | Role |
|--------|------|
| `string Label { get; }` | Short tab-strip label |
| `bool IsAvailable(MetricCollector? collector)` | Hide-or-show predicate; enforced by `TabRegistry.Visible` |
| `void Tick(MetricCollector collector)` | Per-frame refresh; tabs that maintain derived state recompute here (1 Hz where derived data is 30s-smoothed) |
| `float MeasurePanelHeight(MetricCollector collector)` | Total panel height including chrome; chrome offset is `OverlayLayout.RowsTopOffset` |
| `void Draw(SpriteBatch sb, Rectangle area, MetricCollector collector)` | Renders content area below the chrome divider |
| `void HandleClick(float localX, float localY, MetricCollector collector)` | Panel-local click; clicks above tab-strip Y never reach this method |
| `void HandleScroll(int delta, MetricCollector collector)` | Scroll-wheel events; dispatched only when cursor over panel |

### Tab registry

`TabRegistry.Tabs` is a static `List<IOverlayTab>` (`UI/Overlay/TabRegistry.cs:38-45`):

```csharp
public static List<IOverlayTab> Tabs { get; } = new List<IOverlayTab> {
    new OverviewTab(),    // index 0, default landing
    new TreeTab(),
    new SpikesTab(),
    new EventsTab(),
    new InsightsTab(),
};
```

Singletons across F9 toggles. The chrome reads `OverlayState.ActiveTabIndex` to know which is active.

### Visible-list indexing

`TabRegistry.Visible(collector)` filters by `IsAvailable` into a reused scratch buffer (`UI/Overlay/TabRegistry.cs:58-71`). Important properties:

- Returns the full list when `collector` is null (chrome runs before the collector exists).
- Falls back to the full list when **every** tab is unavailable (collector exists but no history yet) so the player still sees the strip.
- The returned `IReadOnlyList<IOverlayTab>` is a shared, reused buffer — never store the reference past the current frame.

`TabRegistry.ResolveActive(collector)` clamps `OverlayState.ActiveTabIndex` into the visible list. A previously-active-but-now-hidden tab falls back to index 0. The click handler, the tab-strip drawer, and the dispatch path all index against the visible list, so disabling a tab transparently shifts later tabs up.

Before commit `aa914ce` the chrome dispatched against `Tabs` directly; `IsAvailable = false` was the docstring contract but the chrome ignored it. Returning `false` did not hide a tab from the strip and did not stop input from reaching it.

### Lifecycle per frame, in order

1. **Tick (1 Hz):** the active tab's `Tick(collector)` recomputes derived state. Each tab uses its own throttle off `Main.GameUpdateCount % 60`.
2. **MeasurePanelHeight:** chrome resizes the panel based on the active tab's content height.
3. **Draw:** chrome draws background, header, tab strip, stats line, PROFILER HEALTH bar. Then `tab.Draw(sb, area, collector)` renders content below `OverlayLayout.DividerOffset`.
4. **Input:** if the cursor is over the panel, the chrome consumes header / tab-strip / metric-pill clicks itself; clicks below the tab-strip Y are forwarded via `tab.HandleClick(localX, localY, collector)`.
5. **Scroll:** if the cursor is over the panel, `tab.HandleScroll(delta, collector)` is invoked.

### Shared state in `OverlayState`

`UI/Overlay/OverlayState.cs` owns cross-tab state that the chrome reads and writes:

- `ActiveTabIndex` — the visible-list index of the active tab.
- `Paused` — pauses metric updates from the player's POV; the data freezes.
- `Use30SecondAverage` — toggles NOW vs 30s-avg in the chrome's stats line.
- `MetricMode` — the CPU/MEM/BOTH pill state.
- `Visible` — F9 toggle state.

Tabs **read** from `OverlayState`. The chrome owns writes. Tabs should not flip `Paused` or `MetricMode` themselves; the pattern is "chrome consumes user input on cross-cutting controls, tabs consume input on tab-specific controls."

### Truncation caches (audit fix)

Two tabs cache truncated row labels:

| Tab | Cache | Key | Refilled |
|-----|-------|-----|----------|
| `OverviewTab` | `_truncatedNames` (`Dictionary<int, string>`) | `ModId` | First paint after `HookInterceptor.ProfiledModNames` populates |
| `InsightsTab` | `_rankedBodies` (`List<string>`) | parallel index into `_ranked` | 1 Hz Tick alongside `_ranked` |

Before commit `aa914ce` both paths called `OverlayDraw.Truncate(...)` per row per frame. At 60 Hz with ~30 mods or ~10 insight rows that was ~600-1800 string allocations per second from a profiler explicitly designed to measure allocation. The audit caught it; the fix is the cache.

### Drill-into-TREE

A click on a leaderboard mod row in OverviewTab switches the active tab to TreeTab and pre-expands the clicked mod. Implementation: `OverviewTab.HandleClick` writes the target mod into `OverlayState` and sets `ActiveTabIndex`; `TreeTab.Tick` reads the target on the next refresh.

### Drawing primitives

`OverlayDraw` (`UI/Overlay/OverlayDraw.cs`) wraps `SpriteBatch.Draw` and font measure / draw operations:

- `OverlayDraw.Rect(sb, x, y, w, h, color)` — filled rectangle via the magic-pixel texture.
- `OverlayDraw.Text(...)` / `OverlayDraw.TextRight(...)` / `OverlayDraw.TextCentered(...)`.
- `OverlayDraw.Truncate(text, maxWidth, font)` — fits a string to a pixel-width budget.

`OverlayLayout` carries layout constants (`HeaderHeight`, `TabStripHeight`, `DividerOffset`, `RowsTopOffset`, padding, row heights).

`ProfilerTheme` carries the palette (background, divider, badge colours, NOW vs 30S text colour, the CPU/MEM/BOTH pill colour mapping).

## Key Interfaces / Data Flow

```
ProfilerOverlaySystem (ModSystem):
   PostSetupContent → KeybindLoader.RegisterKeybind("ToggleOverlay", F9)
   ModifyInterfaceLayers → insert custom LegacyGameInterfaceLayer
   UpdateUI → if Visible, drive _userInterface.Update(gameTime)
   ToggleVisibility → flip OverlayState.Visible

ProfilerPlayer (ModPlayer):
   ProcessTriggers → ToggleKeybind.JustPressed → ProfilerOverlaySystem.ToggleVisibility

per frame (the layer's draw method):
   if !OverlayState.Visible: return
   panel = OverlayPanel
   collector = ModContent.GetInstance<ProfilerSystem>().Collector
   tab = TabRegistry.ResolveActive(collector)
   tab.Tick(collector)
   panel.Draw(sb, collector, tab)
       │
       ├─ draw background + chrome
       ├─ draw tab strip from TabRegistry.Visible(collector)
       ├─ tab.Draw(sb, contentArea, collector)
       └─ if hover: read mouse coords
          ├─ if click in tab strip → set OverlayState.ActiveTabIndex (visible-list index)
          ├─ if click in content area → tab.HandleClick(localX, localY, collector)
          └─ if scroll → tab.HandleScroll(delta, collector)
   if hover: Player.mouseInterface = true  (vanilla input suppression)
```

### Per-tab summary

| Tab | Reads | Cadence | Width drivers |
|-----|-------|---------|---------------|
| OverviewTab | `MetricCollector` per-mod totals + `ModImpactScorer` composite ranking | 1 Hz Tick → cached `Sorted`, `_truncatedNames` | 536 lines; ranks + bars + component breakdown |
| TreeTab | `PerModAttribution` per `(mod, category, hookId)` | 1 Hz Tick → cached row layout | 459 lines; fold/expand, per-mod coverage badge from `HookCoverageView` |
| SpikesTab | `SpikeDetector.Windows`, `PerTickAttributionRing` | 1 Hz Tick | 150 lines; spike rows + per-mod attribution drill |
| EventsTab | `EventAggregator.Buckets` | 1 Hz Tick → cached `_cachedNowSummary` | 373 lines; per-dimension bucket rows + NOW summary |
| InsightsTab | `InsightsEngine.GetOrCreateShared()` → `Store.TopInto` | 1 Hz Tick → cached `_ranked`, `_rankedBodies`, `_gatedLabel` | 176 lines; ranked card rows + gated list |

## Implemented Outputs / Artifacts

| Surface | Source |
|---------|--------|
| F9 overlay panel | `ProfilerOverlaySystem.ModifyInterfaceLayers` → `LegacyGameInterfaceLayer` |
| Overlay PROFILER HEALTH strip | `HookCoverageView` via `OverlayPanel` |
| Five tab views | `TabRegistry.Tabs[*]` |
| In-game mouse-interaction suppression | `Player.mouseInterface = true` while hovering |

## Known Issues / Active Risks

- **Tab singletons hold state across worlds.** A tab whose Tick cache survives a world unload could show stale data on the next world's first frame. Today each tab's `Tick` rebuilds from `collector.History`, which is `null`-checked, and the chrome forces a re-Tick on collector change. Watch when adding new tabs.
- **`_truncatedNames` does not invalidate on language/font change.** If the player ever changes the in-game font scale mid-session, the cached widths could be wrong. Today the in-game font is fixed; not a current bug.
- **`OverlayState.ActiveTabIndex` is the visible-list index, not the `Tabs` index.** Persisting it across launches (if that ever happens) would silently mis-resolve if the tab list reorders. Today the index is process-only. The docstring warns about reordering `Tabs`.
- **Drill-into-TREE uses `OverlayState` as a one-shot signal channel.** Overview writes the target mod ID, Tree reads and consumes it. Not type-safe; a future tab adding a similar drill-down would need its own field. Acceptable for two callers; brittle if a third arrives.
- **Hover detection is via `Main.MouseScreen` against the panel rect.** No hit-test against individual tab rows; row hover effects are tab-internal. If a tab grows a tooltip surface, it has its own hit-test responsibility.

## Partial / In Progress

Nothing in progress as of 2026-05-20. All audit overlay-ui findings are marked done in `plans/code-health-audit/index.md`.

## Planned / Missing / Likely Changes

- **Settings tab.** Sketched in `notes/future-settings-design.md`. Would expose `HookBackend.Mode`, allocation tracking on/off, log verbosity, the spike threshold.
- **HTML report viewer link.** When the HTML report sibling (`notes/future-html-report.md`) lands, the overlay could surface a "Open last session report" link.
- **Tab strip overflow.** Today five tabs fit. Six or seven might not at default UI scale; a "more" affordance or horizontal scroll would be needed.

## Durable Notes / Discarded Approaches

- **`IngameFancyUI` was considered as the mount.** Rejected because it locks the player out of gameplay (its docstring says so), which contradicts the README's "Esc dismisses mid-fight, no modal traps" requirement. The current `ModifyInterfaceLayers` + mod-owned `UserInterface` path is the non-modal route.
- **A single monolithic `ProfilerOverlay.cs` was the M1 shape.** Refactored into `UI/Overlay/` with the plug-and-play tab registry in commit `037f8d5`. The old `ProfilerOverlay.cs` is now a 34-line shell; all tab work moved into the registry. The refactor unlocked the per-tab files that the audit could then critique independently.
- **Tab Tick used to run at 60 Hz.** Audit-flagged in commit `6537950` ("Audit fixes for the three merged tabs: throttle Tick to 1 Hz, kill hot-path allocations"). The data the tabs bind to is already 30s-smoothed in the collector, so a 60 Hz Tick was rebuilding state from data that had not changed. The 1 Hz throttle cuts the per-frame work by 60×.
- **`OverviewTab.Sorted` allocated a fresh `ArraySegment` per access.** Casting a struct to `IReadOnlyList<T>` boxes the segment; the property was being read at 60 Hz, allocating ~360 boxes per second. `ModImpactScorer` now caches a `SortedView` wrapper at construction. Same commit.
- **`EventsTab.BuildRows` allocated a fresh `List<EventBucketRow>` per Tick (~100-300 entries).** That was ~200-600 KB/s of garbage. Throttled to 1 Hz alongside the rest. Same commit.
- **`InsightStore.Top` allocated 2 lists + 1 dictionary + 1 lambda per call.** The fix is `TopInto` with reusable scratch buffers; documented in `systems/insights-engine.md`. The InsightsTab calls `TopInto`, not `Top`.

## Obsolete / No Longer Relevant

- **`HookOverrides` / `HookNpcOverrides` etc. category arrays in the chrome.** Were originally read by the overlay to label per-mod hook breakdowns. Now everything routes through `HookCategoryRouter` and `PerModAttribution.CategoryNames`. The arrays themselves are deleted from the codebase (see `systems/hook-instrumentation.md` obsolete section).

## Cross-references

- `tmodloader/ui-system.md` — the tModLoader UI API the overlay sits on (KeybindLoader, ModifyInterfaceLayers, UIElement, the magic-pixel texture).
- `systems/hook-instrumentation.md` — `HookCoverageView` feeding PROFILER HEALTH.
- `systems/metric-collection.md` — what Overview, Tree, Spikes read.
- `systems/events-and-context.md` — what EventsTab reads.
- `systems/insights-engine.md` — what InsightsTab reads.
- `notes/overview-tab-plan.md`, `notes/events-tab-plan.md`, `notes/spikes-and-allocations-plan.md`, `notes/insights-engine-plan.md` — design plans (all shipped).
- `plans/code-health-audit/overlay-ui.md` — audit findings driving the IsAvailable enforcement and truncation caches.
