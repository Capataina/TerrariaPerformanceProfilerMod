# Overlay — Performance Research & Plan (v0.6 perf-pass)

> Scope: every file under `UI/` recursively — `ProfilerOverlay.cs`,
> `ProfilerOverlaySystem.cs`, `ProfilerTheme.cs`, `Overlay/*.cs`,
> `Overlay/Tabs/*.cs`, `Overlay/Components/*.cs`. Total ~4 900 lines.
>
> Goal: drive the per-frame draw-thread allocation budget for the F9
> overlay toward **zero bytes per frame at steady state**, eliminate
> redundant string formatting, share vertex/triangle work, and shave
> measurable cycles off the chrome and per-tab hot paths — without
> dropping a single tab, chart, column, pill, badge, sparkline, or
> heatmap cell. Optimisation = doing the same work cheaper; never
> doing less (per `context/notes/philosophy.md` §"Optimisation is
> not doing less").
>
> Anchor: `context/perf-pass/baseline.md`. The "Top-3 mod CPU contributors"
> table there names PerformanceProfiler as the #1 contributor in its own
> session (`0.27 ms/tick avg, 4 488 ms total over ~4.5 min`). A
> non-trivial fraction of that is the overlay drawing itself when it is
> open. The Invariant-2 budget is *measured* against an overlay that was
> open during the playtest; reducing draw-side cost is one half of
> moving the headline number.
>
> Strong: the overlay is already disciplined relative to the v0.2 era —
> truncation caches exist (`OverviewTab._truncatedNames`,
> `InsightsTab._rankedBodies`), Tick is throttled to 1 Hz on derived
> state (`EventsTab._lastRefreshTick`, `InsightsTab._lastRefreshTick`,
> `OverviewTab._tickCounter`), the donut uses cached triangle vertex
> arrays (`DonutChart._vertices`), and the scratch buffers for the
> visible-tabs list (`TabRegistry._visibleScratch`), frozen snapshots
> (`OverlayState._frozenCategoryMs`/etc.), sort spans
> (`TreeTab.SortVisibleCategories` via `stackalloc`), and the
> `EventsTab._rows` array are all reused. The audit commit `aa914ce`
> closed the worst per-frame allocators. This dossier is the *next
> layer down*: the per-frame `string.Format` / interpolation calls
> that still allocate, the IL-level boxing in `IReadOnlyList<T>`
> indexer dispatch, the redundant chrome rebuilds at 60 Hz, the
> `_cachedNowSummary`-style caches that are missing for siblings, and
> the `SpriteBatch.End`/`Begin` round-trip that fires every time
> `DonutChart.Draw` is invoked.
>
> Weak: there is no per-glyph profile available without instrumentation
> the project does not yet have; numbers in this dossier are reasoned
> from sample counts × known-cost primitives, not from `BenchmarkDotNet`
> on the draw thread. Counter-scenario: a finding that looks like an
> obvious win might be cancelled by GC tier-promotion or by a
> pre-existing JIT inline that the rewrite breaks. The mitigation is in
> the verification plan in §6: each tier-1 change ships with an
> overlay-open vs overlay-closed delta measurement before the change is
> declared done.

---

## 0. Document map

| §   | Title                                                                               |
|-----|-------------------------------------------------------------------------------------|
| 1   | Current-state audit — every file walked, per-frame allocation profile, hot-path map |
| 2   | Baseline numbers and the per-frame budget the overlay must fit into                 |
| 3   | tModLoader / XNA-FNA / .NET 8 draw-surface research                                 |
| 4   | Optimisation opportunities — per-category                                           |
| 4.1 | Per-frame string allocation elimination                                             |
| 4.2 | Format-cache shape and lifetimes                                                    |
| 4.3 | `SpriteBatch.End/Begin` collapse and `BasicEffect` state-cache                      |
| 4.4 | `DonutChart` vertex-array reuse + Begin/End amortisation                            |
| 4.5 | `Sparkline` per-sample allocation collapse                                          |
| 4.6 | Layout-pass dedupe across the chrome + tabs                                         |
| 4.7 | `IReadOnlyList<double>` boxing on hot indexers                                      |
| 4.8 | `ModContent.GetInstance` call-site cache                                            |
| 4.9 | `ProfilerCard` / `Pill` / `HeatBar` micro-optimisations                             |
| 4.10| Hidden chrome rebuilds at 60 Hz                                                     |
| 4.11| Resize / drag jitter and `Recalculate` cost                                         |
| 4.12| `RebuildTimelineMarks` allocation profile                                           |
| 5   | Cross-system dependencies — MetricCollector / Persistence / InsightsEngine          |
| 6   | Prioritised order and the verification protocol per tier                            |
| 7   | References                                                                          |

---

## 1. Current-state audit

### 1.1 File-by-file walkthrough (per-frame allocation profile)

The columns mean:

- **Path**: file under `UI/`.
- **Per-frame work**: what runs on every `DrawSelf` / `Draw` / `Tick`.
- **Allocs / frame (60 Hz)**: estimated heap traffic, *steady state*,
  overlay open, default tab.
- **Hotness**: the colour the row would render at in a per-frame
  flamegraph — H = high, M = medium, L = low, Z = zero or near-zero.

#### 1.1.1 Mount glue

| Path                            | Per-frame work                                                                                      | Allocs / frame | Hotness |
|---------------------------------|-----------------------------------------------------------------------------------------------------|----------------|---------|
| `UI/ProfilerOverlay.cs`         | None on the per-frame path. `OnInitialize` runs once.                                               | 0              | Z       |
| `UI/ProfilerOverlaySystem.cs`   | `UpdateUI` -> `_userInterface.Update`; `ModifyInterfaceLayers` once per frame inserts a `new LegacyGameInterfaceLayer(...)`. **`DrawOverlay` constructs `new GameTime()` every call.** | 2 heap objects | M       |
| `UI/ProfilerTheme.cs`           | Pure static utilities. `FillRect`, `DrawBorder`, `DrawPanel` allocate **a `Rectangle` value per call** (struct, on stack) but no heap.                                              | 0              | Z       |

The `new GameTime()` and `new LegacyGameInterfaceLayer(...)` in
`ProfilerOverlaySystem` look small but they are 60 Hz allocations on
the draw thread. They tripped through prior audits because they look
like control flow. The `LegacyGameInterfaceLayer` constructor allocates
two strings internally (the `Name` and the delegate target capture),
the `GameTime` allocates the public-API wrapper. Two short-lived
objects/frame × 60 = ~120/sec, ~7 200/min. This is in Gen0 noise but
it is the kind of "one-per-frame" pattern the dossier is here to kill.

#### 1.1.2 Framework

| Path                                  | Per-frame work                                                                                                                                                              | Allocs / frame                                                                  | Hotness |
|---------------------------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------|---------------------------------------------------------------------------------|---------|
| `UI/Overlay/IOverlayTab.cs`           | Interface only.                                                                                                                                                             | 0                                                                               | Z       |
| `UI/Overlay/TabRegistry.cs`           | `Visible(collector)` rebuilds `_visibleScratch` (List<T>.Clear + Add). `ResolveActive` calls it twice per frame (Draw + DrawTabStrip read).                                | 0 heap, ~2× List traversal                                                       | L       |
| `UI/Overlay/OverlayState.cs`          | Per-frame reads from `SelectedCategoryMs/Bytes/Hooks*` — virtual dispatch into the collector's `IReadOnlyList<double>` views. `CaptureSnapshot` only on PAUSE click.        | 0                                                                               | M       |
| `UI/Overlay/OverlayLayout.cs`         | Pure constants.                                                                                                                                                              | 0                                                                               | Z       |
| `UI/Overlay/OverlayMode.cs`           | `OverlayLayoutCurrent.IsCompact` is a property; every accessor reads `OverlayState.Mode` and branches. **Hot.** Default-mode draw of the chrome calls these ~30 times/frame. | 0                                                                               | L       |
| `UI/Overlay/OverlayDraw.cs`           | `Text` snaps + quantises + calls `Utils.DrawBorderString` (5 internal SpriteBatch.Draw calls). `Bar` allocates 2 `Rectangle` (stack). `FormatBytes` uses interpolated strings with `:F0`/`:F1` — these *return new strings* every call. | varies — see §1.2 below                                                          | H       |
| `UI/Overlay/OverlayPanel.cs`          | `DrawSelf` — full chrome each frame. Reads `ModContent.GetInstance<ProfilerSystem>()` 4× per frame. Computes 4 stat cards (4× `string.Format` via interpolation). Computes PROFILER HEALTH card (~6 interpolated strings). Hit-test pill rects cached in fields. **Hot.** | ~10-14 strings + 4 ProfilerConfig lookups + 4 ProfilerSystem lookups            | H       |
| `UI/Overlay/OverlayDraw.cs::FormatBytes` | Interpolated returns: `$"{bytes:F0} B"`, `$"{bytes / 1024d:F1} KB"`, ... — these are .NET 8 interpolated handlers. `F0`/`F1` flow through `ISpanFormattable.TryFormat`; **but the final string is still a heap allocation.** | 1 string per call                                                                | M       |

The biggest single cost block is `OverlayPanel.DrawSelf` because it
runs the entire chrome every frame, including the four stat cards and
the PROFILER HEALTH card. Steady state, overlay open, this path is the
dominant draw-thread cost on the overlay side.

#### 1.1.3 Tabs

| Path                                  | Per-frame draw cost                                                                                                                                                  | 1 Hz Tick cost                                                                                                       | Hotness |
|---------------------------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------------|----------------------------------------------------------------------------------------------------------------------|---------|
| `UI/Overlay/Tabs/OverviewTab.cs`      | DrawDonutCard (slice legend + centre stat), DrawContributorsCard (5 rows × 1 composite-bar + 1 component-line interpolation), DrawSparklineCard (3 sparklines), DrawSortChips (4 chips), DrawAllModsRanking (up to 12 rows × bar + value-string + components-string). **~32 string interpolations per frame.** | `_scorer.Recompute` + `RefreshSlices` (DonutSlice list cleared + repopulated) + `RefreshSparklines` (per-tick alloc loop, ~1800 sample × modCount inner sum). | H       |
| `UI/Overlay/Tabs/TreeTab.cs`          | Up to 12 rows × per-row 3-4 interpolated values (`F3`/`F2`/`F0`/`FormatBytes`), per-row coverage badge, scroll thumb. Span-based stackalloc for cat ordering is already free. | `BuildSortedRows` allocates `_rows` only when modCount grows; otherwise overwrites in place. `Array.Sort` is in-place. | H (draw), L (tick) |
| `UI/Overlay/Tabs/SpikesTab.cs`        | Timeline strip rebuild + draw (RebuildTimelineMarks clears + appends to `_timelineMarks` per frame, **not throttled**). Up to 8 rows × 4-5 interpolated strings per row. `ToString().ToLowerInvariant()` on every stall row.            | Cheap.                                                                                                              | H       |
| `UI/Overlay/Tabs/EventsTab.cs`        | Up to 12 rows × 5-6 interpolated values per row. `FormatDwell` interpolates `D2`/`D2`. Column header draws 6 strings. `_cachedNowSummary` is 1 Hz so the header is cheap. | `BuildRows` calls `agg.SnapshotRows` which *returns a new List* (see §1.4). `ComputeNowActiveSummary` allocates `new List<string>(8)` per refresh.        | M (draw at 1 Hz refresh) |
| `UI/Overlay/Tabs/InsightsTab.cs`      | Up to 6 cards × 4-5 interpolated strings (subject line concat, evidence interpolation, hit-count interpolation). `rec.Pattern.ToString()` allocates per draw.            | 1 Hz refresh: `Engine.Evaluate` + `Store.TopInto` + `InsightRenderer.Render` per record (allocates body strings — these *are* cached in `_rankedBodies`). | M       |
| `UI/Overlay/Tabs/SelfTab.cs`          | Two cards × 3 StatBlocks each = 6 StatBlocks. Each StatBlock: 1 interpolation for the value, 1 for the footer. ~12-14 strings per draw.                                                                  | No-op Tick.                                                                                                          | M       |

#### 1.1.4 Components

| Path                                            | Per-call work                                                                                                                                                                                                                  | Allocs / call                                                                                                                                                                                          |
|-------------------------------------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `UI/Overlay/Components/DonutChart.cs`           | `BuildRingTriangles` × 2 (outer ring + inner ring). `sb.End()` + 1× `pass.Apply()` + `DrawUserPrimitives` + `sb.Begin(...)`. ~720 triangles, ~2160 vertices touched per frame.                                                  | Zero new in steady state (vertex array is cached). **But** the `foreach (EffectPass pass in e.CurrentTechnique.Passes)` allocates an enumerator on each frame for `EffectPassCollection`. See §4.3/4.4. |
| `UI/Overlay/Components/HeatBar.cs`              | 1-2 FillRects, 0 string work.                                                                                                                                                                                                  | 0                                                                                                                                                                                                      |
| `UI/Overlay/Components/ImpactSkyline.cs`        | Up to 12 towers × 4-5 FillRects + 1 value interpolation (`F0`/`F1`/`F2`) + 1 label substring. **The label `Substring` allocates a string per frame.**                                                                          | Up to 12 strings/frame even though the labels are stable                                                                                                                                                |
| `UI/Overlay/Components/Pill.cs`                 | 3 FillRects (fill, border-as-4-rects, optional dot) + `OverlayDraw.Text`.                                                                                                                                                       | 0                                                                                                                                                                                                      |
| `UI/Overlay/Components/ProfilerCard.cs`         | 4 FillRects + 1 border + 1 title string + 1 optional right-stat string. **Width approximated as `rightStat.Length * 6.5f * ...` — char-by-char measure, no allocation.**                                                       | 0 in steady state                                                                                                                                                                                      |
| `UI/Overlay/Components/SeverityBadge.cs`        | 3 FillRects + `OverlayDraw.Text`. `MeasureWidth(label)` recomputes char-width arithmetic per call. **Every DrawConfidence call does `confidence.ToString().ToLowerInvariant()` — 2 string allocs per badge.**                  | 2 strings × N badges/frame                                                                                                                                                                              |
| `UI/Overlay/Components/Sparkline.cs`            | DrawFilledArea: N samples × 2 FillRects per sample. DrawBars: N × 1 FillRect. DrawMarkers: N × 1 FillRect.                                                                                                                       | 0                                                                                                                                                                                                      |
| `UI/Overlay/Components/StatBlock.cs`            | 3 FillRect-equivalents via Text calls.                                                                                                                                                                                          | 0                                                                                                                                                                                                      |
| `UI/Overlay/Components/TimelineStrip.cs`        | Up to ~64 marks × 1 FillRect each.                                                                                                                                                                                              | 0                                                                                                                                                                                                      |

### 1.2 Per-frame string allocation profile (combined)

This is the headline number. The overlay is open, the player is on
SUMMARY (default landing tab), 30 mods are loaded, allocation tracking
is on, the scorer is calibrated.

| Source                                                                                  | Strings allocated per frame |
|-----------------------------------------------------------------------------------------|-----------------------------|
| `OverlayPanel.DrawStatCards` — 4 cards × ~2 strings each (`THIS TICK`/`AVG`/`GC`/`TICK`) | 8                           |
| `OverlayPanel.DrawHealthBody` — backend label (cached enum-switch) + detail + selfLine + sev label | 4                           |
| `OverlayPanel.DrawHeader`, `DrawTabStrip` — pause/mode/avg/metric labels are switch'd literals, **0 strings** | 0                           |
| `OverviewTab.DrawDonutCard` — slice legend (6 × 2 strings: label truncate + pct), centre stat (3 strings), `+ N more` | ~16                         |
| `OverviewTab.DrawContributorsCard` — 5 rows × (composite value + components line) | ~10                         |
| `OverviewTab.DrawSparklineCard` — 3 label strings (static literals; ideally interned) | 0                           |
| `OverviewTab.DrawSortChips` — 4 chips × `label + " ▼"/" ▲"` concat for the active chip | 1                           |
| `OverviewTab.DrawAllModsRanking` — 12 rows × `F2` interpolation for ms + components string + footer calibration string | ~25                         |
| Background: `OverlayDraw.Text` writes — 4× `MathF.Round` per call, no allocs            | 0                           |

Total per frame, steady state SUMMARY tab: **~64 string allocations
× 60 Hz = ~3 840 strings/sec**. Each is a short string (10-30 chars),
average ~32 bytes after object header. That is roughly **120 KB/sec of
short-lived heap traffic** *from the overlay's draw side alone*. The
collector's overhead budget is 0.10 ms/tick (target). Allocation-driven
GC pauses are exactly what the profiler exists to measure.

The TREE tab is worse because it draws bars + values for both ms and
bytes when `MetricView.Both` is active: 12 mod rows × 3 strings + N
category rows × 2 strings + 2 hook rows × 1 string. Realistic upper
bound: 50-80 strings/frame.

The EVENTS tab redraws 6 column-header strings on every frame even
though the column header is static. Plus 12 rows × 5 interpolated
strings per row = 60+ strings/frame. (The 1 Hz refresh throttles the
*rebuild* of `_rows` but the per-frame draw still allocates the
display strings.) **This is the single biggest miss-by-omission of
the v0.5 era.**

### 1.3 Per-frame `Rectangle`/`Vector2` value-type pressure

`Rectangle` and `Vector2` are value types so they go on the stack. They
do not allocate. They *do* incur copy cost when passed by value: a
`Rectangle` is 16 bytes, a `Vector2` is 8 bytes. The chrome and tabs
construct a lot of these per frame:

- `OverlayPanel.DrawSelf` constructs ~30 Rectangles directly.
- `DrawHealthCard` constructs ~5 more.
- `DrawStatCards` constructs 4 + the returned `Rectangle[]` array.
  **The `Rectangle[] LayoutStatCards(...)` is a heap allocation —
  `new Rectangle[CardCount]` — every frame.**
- `OverviewTab.Draw` constructs ~10 Rectangles per call.
- Every `ProfilerCard.Draw` invocation constructs 3 new Rectangles
  internally; called ~6-10 times per frame from the chrome + active
  tab.

The `Rectangle[]` in `LayoutStatCards` is the only true heap offender;
the rest are stack-allocated copies. The aggregate copy cost is low
relative to the string-formatting cost, but the array allocation is
the kind of mechanical waste this dossier kills.

### 1.4 Allocations hidden inside collaborators called per frame

These do not appear when reading the UI files in isolation. They are
real per-frame allocations because the UI calls them per frame:

- `EventAggregator.SnapshotRows(int minTicks)` returns a `List<EventBucketRow>`. Called from `EventsTab.BuildRows` at 1 Hz refresh — so the list is allocated once per refresh, ~once/sec. **At 1 Hz this is fine; flag as "no fix required".**
- `Confidence.ToString()` on enums boxes the enum, allocates the string. Called per insight card draw. **Fix: switch-to-literal.**
- `EventsTab.DimensionLabel` is a switch returning a string literal; no alloc. **Good.**
- `EventsTab.InvasionShortName` same. **Good.**
- `EventsTab.ComputeNowActiveSummary` allocates `new List<string>(8)` + `string.Join(" · ", parts)` per 1 Hz refresh; result cached in `_cachedNowSummary`. Fine.
- `InsightRenderer.Render(...)` allocates a body string per insight; result cached in `_rankedBodies`. Fine.

### 1.5 Resize/drag and `Recalculate`

`OverlayPanel.Update` calls `ApplyWidth(PanelWidth)` every frame. The
guard is `Math.Abs(w - _appliedWidth) > 0.5f`. On steady state this
returns without recalculation. But `PanelWidth` is a getter that calls
`ModContent.GetInstance<ProfilerConfig>()` on every frame — that is a
dictionary lookup. Not a heap alloc but a measurable cycle cost; see
§4.8.

When the mouse is actively dragging or resizing, `Recalculate` runs on
every frame, which is correct. Resize is rare; not a hot path concern.

---

## 2. Baseline and the per-frame budget

From `context/perf-pass/baseline.md`:

- Average frame ms: **0.96 ms** (~1 042 fps headroom).
- Player playing at vsync 60 fps gets a 16.67 ms frame budget. The
  overlay must take << 1 ms of that to stay invisible.
- PerformanceProfiler self-cost in its own session: **0.27 ms/tick avg
  → 4 488 ms over ~4.5 min**. That figure aggregates *all* of the mod's
  per-tick work (Hook Interceptor, Metric Collector, ring buffer
  writes, Stall + Spike detection, focus probe, persistence writer,
  draw-side overlay). The overlay-open contribution is a subset.
- End-of-session **`UiOverlayBlocking` cluster — 40 stalls over 8.5 s.**
  This is the persistence-end blocking the main thread, not the
  overlay's per-frame draw, but it reinforces that the overlay
  surface is already in the spike report and the player would notice
  another second of stutter accumulating from UI draw.

**Budget for the overlay's draw-side path:** ≤ 0.15 ms/frame at the
SUMMARY tab with 30 mods loaded, overlay open. Allocation budget:
**zero heap allocations per frame at steady state**, modulo the once-
per-frame `LegacyGameInterfaceLayer` and `GameTime` that
`ProfilerOverlaySystem` constructs (§4.10), which are tier-1 fixes.

Aspirational stretch goal: **the overlay open vs overlay closed delta
on `MetricCollector.History.Newest.FrameTimeMs` should be ≤ 0.1 ms at
the median.** This number is measurable today: open F9, sample the
chrome's THIS TICK card for 10 s, close F9, sample again. The chrome
already shows the number we are optimising against.

---

## 3. tModLoader / XNA-FNA / .NET 8 draw-surface research

### 3.1 The draw layer the overlay sits on

`ProfilerOverlaySystem.ModifyInterfaceLayers` inserts a
`LegacyGameInterfaceLayer` named `"PerformanceProfiler: Overlay"` just
beneath `"Vanilla: Mouse Text"`. `DrawOverlay` is the per-frame
callback. It calls `_userInterface.Draw(Main.spriteBatch, new GameTime())`
which walks the `UIState` tree and dispatches `DrawSelf` to
`OverlayPanel`.

`Main.spriteBatch` is the game's main SpriteBatch. The UI render pass
calls `sb.Begin(...)` *before* layer draws and `sb.End()` *after*. Our
draw layer runs between those calls so we can freely call `sb.Draw`
without our own Begin/End — except in `DonutChart` (see §3.4).

The cost of one `SpriteBatch.Draw` call into the in-flight batch is
the cost of writing four `VertexPositionColorTexture` into the
batch's vertex buffer plus the indexbuffer indices; the actual GPU
submission happens at `End()`. **Calls within the same batch are
near-free in CPU terms.** The expensive operation is forcing a
flush — which is what `End()` does.

### 3.2 FNA's `SpriteBatch.End()` is a flush

FNA's SpriteBatch is a small, sane implementation. `End()` flushes the
batched draws to the GPU. `Begin()` resets state. The cost of an
`End()`/`Begin()` round-trip is:

1. A draw-submission to the GPU for whatever was batched (cheap if
   the batch is small, but it forces a GPU sync point).
2. State change (SamplerState, BlendState, RasterizerState,
   DepthStencilState, the world/view/projection matrices).
3. The next `Draw` after a fresh `Begin` starts a new batch.

A pair of End/Begin on a half-empty batch is ~tens of microseconds on
modern hardware, mostly the state change. Multiplied by 60 Hz, that is
~1 ms/sec if the only End/Begin is the DonutChart one. We do it once
per frame (only on SUMMARY tab). Not free, not catastrophic.

A `DrawUserPrimitives` call submits triangles directly to the GPU
through the BasicEffect's vertex shader. The `EffectPassCollection`
returned by `e.CurrentTechnique.Passes` is *enumerable*; the
`foreach` over it allocates an enumerator on the heap. See §4.3.

> **Source**: [Nuclex Games Blog — DynamicVertexBuffer vs DrawUserPrimitives](http://blog.nuclex-games.com/2010/11/dynamicvertexbuffer-versus-drawuserprimitives-round-2/). Re-creating the whole vertex buffer each frame is roughly equivalent to `DrawUserPrimitives` performance-wise; the choice between them matters less than whether the draws are batched at all.
> **Source**: [FNA SpriteBatch source](https://github.com/FNA-XNA/FNA/blob/master/src/Graphics/SpriteBatch.cs).

### 3.3 The TextureAssets.MagicPixel texture

Every `FillRect` and `Sparkline` and `TimelineStrip` and component
draw goes through `TextureAssets.MagicPixel.Value` — a 1×1 white
texture. Sampling it is effectively free. Every call to
`ProfilerTheme.FillRect` resolves the `Value` property; the JIT can
inline this if the asset is loaded.

> **Optimisation lever** §4.9: cache `TextureAssets.MagicPixel.Value`
> once per `DrawSelf` and pass it into the components, rather than
> resolving the asset property per FillRect.

### 3.4 The DonutChart's End/Begin pattern is correct

`DonutChart.Draw` does the only End/Begin pair in the overlay codebase.
It is *correct*: `DrawUserPrimitives` cannot be batched into a
SpriteBatch, so we must flush first and restart the batch after. The
optimisation lever is to *reduce frequency* — see §4.4 (1 Hz triangle
rebuild + cached vertex set, redraw the same vertices for the 59
intermediate frames). The triangle list is identical between frames
when the slices have not changed.

### 3.5 .NET 8 interpolated strings — when they're alloc-free

`$"{x:F2}"` in .NET 6+ flows through `DefaultInterpolatedStringHandler`.
The handler uses an ArrayPool-rented char buffer to build the result,
then returns a `string`. **The final string is always a heap
allocation.** What .NET 6+ improved was eliminating *intermediate*
temporary strings — boxing of value types, intermediate `ToString()`
calls — by going through `ISpanFormattable.TryFormat` directly into
the rented buffer.

For our use, that means:

- `$"{x:F2}"` allocates **1 string** per call (the result). Boxing of
  the double is eliminated; `double` implements `ISpanFormattable`.
- `$"{x:F2} ms"` still allocates 1 string.
- `string.Format("{0:F2} ms", x)` allocates 1 string (and historically
  also a `params object[]`, but for single-arg overloads the JIT can
  often elide it via a specialised overload).
- `x.ToString("F2") + " ms"` allocates 2 strings (the formatted
  number, then the concatenated result).

**Implication.** Every `OverlayDraw.Text(sb, $"{x:F2}", ...)` site is
exactly 1 heap allocation per frame per call. Killing that requires
*either* (a) caching the formatted string at 1 Hz, (b) emitting the
formatted glyphs directly from the buffer without producing a `string`
at all, or (c) avoiding the format altogether (e.g. by drawing the
number from a pre-built glyph atlas keyed by digit, sign, and decimal
position — significant engineering, defer).

The pragmatic answer is (a): the values shown on the overlay are
already 1 Hz refresh data; the *strings* that display them should be
1 Hz too.

> **Source**: [Microsoft Learn — interpolated string handlers](https://learn.microsoft.com/en-us/dotnet/csharp/advanced-topics/performance/interpolated-string-handler), [.NET Blog — String Interpolation in C# 10 and .NET 6](https://devblogs.microsoft.com/dotnet/string-interpolation-in-c-10-and-net-6/).

### 3.6 Why `Utils.DrawBorderString` is 5 SpriteBatch.Draw calls

The vanilla helper draws each glyph 5 times: 4 black outline copies at
±1 px offset, then 1 fill colour copy. For an N-glyph string that is
5N SpriteBatch.Draw calls into the in-flight batch. On a typical
overlay frame the chrome alone draws ~30 strings totalling ~250
glyphs, so ~1 250 SpriteBatch.Draw calls per frame for chrome text
alone. Each is cheap (writing 4 vertices + 6 indices into the batch
buffer) and they all flush in one batch at the layer's exit. This is
not a leverage point on its own; bringing the *count* of strings down
is the lever (§4.1).

### 3.7 tML's `ModContent.GetInstance<T>()` cost

`ModContent.GetInstance<T>` is a dictionary lookup. Cheap, but not free
— roughly 50-100 ns per call. Called from `OverlayPanel`:

1. `Update`: `ModContent.GetInstance<ProfilerSystem>()?.Collector` — 1 call.
2. `DrawSelf` -> `ModContent.GetInstance<ProfilerSystem>()` — 1 call.
3. `DrawSelf` -> `DrawTabStrip` -> `ModContent.GetInstance<ProfilerSystem>()` — 1 call.
4. `PanelWidth` getter (called from `Update.ApplyWidth(PanelWidth)`) -> `ModContent.GetInstance<ProfilerConfig>()` — 1 call.
5. `ClickHeaderPill` (on click) -> `ModContent.GetInstance<ProfilerSystem>()` — N calls per click.
6. `LeftMouseDown.ResolveActive` -> `ModContent.GetInstance<ProfilerSystem>()` — 1 call per click.

Steady state per frame: 3-4 calls. ~300 ns total. Not a hot lever but
clean to consolidate. See §4.8.

---

## 4. Optimisation opportunities — per category

### 4.1 Per-frame string allocation elimination

**The single biggest lever in this dossier.** The plan in §1.2 is to
collapse ~64 strings/frame on the SUMMARY tab — and similar figures
for TREE/EVENTS — to a per-tab cache that refills at the data's
natural cadence (1 Hz for everything except the chrome's THIS TICK and
GC THIS TICK, which need higher cadence because they show the most
recent tick's frame time).

#### 4.1.1 Cache shape

Adopt the pattern that `EventsTab._cachedNowSummary`,
`OverviewTab._truncatedNames`, and `InsightsTab._rankedBodies`
already use: a per-tab struct holding pre-formatted strings, refilled
on a `RefreshIntervalTicks` cadence. Generalise:

```csharp
internal struct OverviewCache
{
    // Centre stat
    public string TopModName;       // refreshed 1 Hz
    public string TopModSharePct;   // "37%"
    public string TopModComposite;  // "5.4 ms"

    // Top contributors (5 rows)
    public string[] ContribComposite;     // ["5.4", "3.1", ...]
    public string[] ContribComponents;    // composed "cpu 1.2ms · alloc 80B/t · spike 2.0ms"

    // All-mods ranking (up to 12 rows visible × pre-formatted "F2 ms" + components)
    public string[] AllRowsValue;
    public string[] AllRowsComponents;

    // Slice legend (up to 6 rows)
    public string[] SliceLabels;     // already truncated
    public string[] SlicePercents;   // "37.2%"

    // Calibration footer
    public string CalibrationNote;
}
```

Refill in `Tick(...)` immediately after the existing `_scorer.Recompute`
and `RefreshSlices` work. The refill is 1 Hz; the draw reads from the
cache without allocating.

Same shape for `TreeTab` (per-mod-row formatted ms, formatted bytes,
formatted coverage badge), `EventsTab` (column header is static — hoist
to a `static readonly string[]`; per-row formatted dwell, avg, peak,
spikes), `SpikesTab` (per-row formatted period, multiplier, line1right),
`SelfTab` (per-card formatted values; this tab is already 1 Hz natural
cadence because the underlying data is sampled at ~1 Hz from
`ProfilerSelfHealth`).

#### 4.1.2 Chrome's THIS TICK / GC THIS TICK cards

These are the only chrome strings that need >1 Hz. Options:

1. **Always refresh per frame** (current behaviour). 4 strings per
   frame × 60 = 240 strings/sec. Cost: ~7 KB/sec. Acceptable if
   tier-1 cuts elsewhere bring the total under control.
2. **Quantise** — render to 0.05 ms precision and refresh only when
   the integer (frameMs × 20) changes. At typical 16 ms frames with
   tiny jitter, the integer changes every frame anyway; no win.
3. **Refresh at 5 Hz** — frameMs values change visibly faster than
   1 Hz feels right for, but 5 Hz (every 12 frames) updates 4× faster
   than the player can read. 4 strings × 5 Hz = 20 strings/sec.
   **Recommended.** Player perception of "this tick" remains
   honest; "AVG 30 S" is already 1 Hz natural.

**Implementation.** A second cache slot on `OverlayPanel` with
`_lastChromeFastRefreshTick` and `RefreshFastIntervalTicks = 12`. Refill
on this tick, read on subsequent draws.

#### 4.1.3 Format-cache invalidation rules

- Refresh whenever the underlying data refreshes (the active tab's
  Tick cadence: 1 Hz for all tabs today).
- Refresh on tab switch (the new tab's first draw should not show
  stale strings from an inactive tab).
- Refresh on PAUSE/UNPAUSE (the value changes from live to frozen).
- Refresh on NOW vs 30S AVG toggle (different source).
- Refresh on CPU/MEM/BOTH toggle (TreeTab renders different columns).
- Refresh on Mode switch (Default vs Compact — text scales change, but
  the string contents are identical; refresh is only needed because
  the truncation budget changes). Optional; can ignore in v0.6.

The invalidation is centralised in a `MarkDirty()` method on each
cache; the toggles are few and call out to it.

#### 4.1.4 Expected savings

| Surface                                  | Strings/frame today | Strings/frame after | Savings/sec |
|------------------------------------------|---------------------|---------------------|-------------|
| OverlayPanel chrome (stats + health)     | 12                  | 4 (THIS TICK / GC × 5 Hz)        | ~480       |
| OverviewTab SUMMARY                      | ~54                 | 3 (calibration footer × tab switch only) | ~3 060     |
| TreeTab                                  | ~50                 | ~2                   | ~2 880      |
| SpikesTab                                | ~30                 | ~2                   | ~1 680      |
| EventsTab                                | ~60                 | ~2                   | ~3 480      |
| InsightsTab                              | ~20 (most cached)   | ~2                   | ~1 080      |
| SelfTab                                  | ~14                 | ~2                   | ~720        |

Aggregate: from ~3 800 strings/sec down to ~50-100/sec for the
chrome's high-frequency stats. **~97% reduction.**

### 4.2 Format-cache shape and lifetimes

The cache must:

- Be a struct or sealed class owned by each tab. No statics that leak
  across F9 toggles incorrectly.
- Pre-size arrays once based on `HookInterceptor.ProfiledModNames.Length`
  or fixed visible-row caps. No `new T[N]` in the per-tick path unless
  N grew.
- Use `string` slots that null-out only on full clear; assignment to a
  slot is a simple reference write.
- Be invalidated by explicit `MarkDirty()` calls from the toggle paths
  (chrome owns the toggles, so the chrome should call into each tab's
  invalidate hook on toggle).

A small extension on the existing `IOverlayTab` is appropriate:

```csharp
internal interface IOverlayTab
{
    // ... existing members ...

    /// <summary>
    /// Called by the chrome when a cross-tab toggle changes (Mode,
    /// CurrentMetric, Paused, ShowAverage). Tabs invalidate their
    /// format caches so the next Tick recomputes them. Optional —
    /// default is no-op.
    /// </summary>
    void InvalidateCaches() { /* default: nothing */ }
}
```

C# 8 default-interface-members keep call-sites compatible. (TML 1.4.4
is .NET 8, so this is fine.)

### 4.3 `SpriteBatch.End`/`Begin` collapse and `BasicEffect` state-cache

`DonutChart.Draw` is the only End/Begin site. Each frame:

1. `sb.End()` — flush in-flight chrome draws to GPU.
2. `gd.Viewport` — getter, cheap.
3. `_effect.World/View/Projection` assignments — each is a
   `Matrix` struct copy.
4. `_effect.CurrentTechnique.Passes` — returns
   `EffectPassCollection`.
5. `foreach (EffectPass pass in e.CurrentTechnique.Passes)` —
   **allocates an enumerator on the heap** because
   `EffectPassCollection` does not implement a struct enumerator
   pattern that the C# compiler can call directly; `foreach` uses
   `IEnumerable<EffectPass>.GetEnumerator()` which returns a
   reference-typed enumerator.
6. `pass.Apply()` — submits effect state to GPU.
7. `gd.DrawUserPrimitives(...)` — submits triangles.
8. `sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, ...)` —
   restart batch.

The state-cache optimisation: BasicEffect's `World`/`View`/`Projection`
only change when the panel is being dragged or the viewport size
changes (resize). Compare the matrices against last-frame and skip the
assignment + `pass.Apply` if they're identical, *along with the
identical-triangle-set short-circuit from §4.4*.

The enumerator allocation: replace `foreach` with explicit indexer
loop:

```csharp
EffectPassCollection passes = e.CurrentTechnique.Passes;
for (int i = 0; i < passes.Count; i++)
{
    EffectPass pass = passes[i];
    pass.Apply();
    gd.DrawUserPrimitives(PrimitiveType.TriangleList, _vertices, 0, _vertexCount / 3);
}
```

`EffectPassCollection.Count` is present in XNA/FNA; the indexer is
allocation-free. For `BasicEffect.CurrentTechnique` there is exactly
1 pass in practice (the unlit vertex-colour technique), so the loop
body executes once with no enumerator on the heap.

### 4.4 `DonutChart` vertex-array reuse + Begin/End amortisation

The donut is recomputed every frame *even though the slice data only
changes at 1 Hz* (`OverviewTab.RefreshSlices` runs in the 1 Hz Tick).
Two changes:

1. **Hash the slice list.** Compute a cheap signature on the slice
   array at the end of `RefreshSlices`: sum of `Value × i` and the
   slice count. Compare in `DonutChart.Draw`; if unchanged, skip
   `BuildRingTriangles` and reuse `_vertices[0.._vertexCount]`.
   This collapses triangle generation from 60 Hz to ~1 Hz —
   59 frames/sec save ~720 trig calls each = ~43 k saved/sec.
2. **Pre-rotate sin/cos.** The current `BuildRingTriangles` calls
   `Math.Cos` and `Math.Sin` 2 × `steps` times per slice. For a
   180° slice that's ~90 trig calls per slice; for the whole donut
   over 12 slices, ~720 per frame, ~43 200/sec. A lookup table sized
   to `StepRad = ~2°` (180 entries) is cheap (~3 KB) and
   pre-computed at first draw. Trade memory for cycles.

Combined: the donut path at steady state becomes "End → indexer loop
→ DrawUserPrimitives over a cached vertex set → Begin". No vertex
generation, no trig, no enumerator.

For the **Begin/End cost itself** — that is unavoidable when mixing
SpriteBatch with DrawUserPrimitives. We minimise it by keeping the
donut as the only End/Begin site and by ensuring no other component is
tempted to take the same path. Sparkline/HeatBar/Skyline all use
`FillRect`, which stays inside the batch.

### 4.5 `Sparkline` per-sample allocation collapse

`Sparkline.DrawFilledArea` allocates a `Color softFill = fillColor * 0.35f`
on entry — fine, that's a struct on the stack. Then for each sample:
two `sb.Draw` calls into the in-flight batch. No string interpolation,
no heap allocation per sample.

But — and this is subtle — `IReadOnlyList<double>.this[int]` boxes the
indexer dispatch *if* the underlying type is a struct. `_frameSeries`
is a `double[]`, so passing it as `IReadOnlyList<double>` causes
virtual-dispatch through the array's interface methods. Same for
`values[i]` inside the loop.

**Fix.** Add `ReadOnlySpan<double>` overloads:

```csharp
public static void DrawFilledArea(SpriteBatch sb, Rectangle area,
    ReadOnlySpan<double> values, double yMax, Color fillColor, Color lineColor)
```

Callers pass `_frameSeries.AsSpan(0, n)`. The span indexer is
JIT-friendly (range check + direct array access). The existing
`IReadOnlyList<double>` overload stays for any caller that needs it,
delegating to the span overload via `values.ToArray().AsSpan()` (only
on slow paths) or via a `CollectionsMarshal.AsSpan` for `List<T>`.

Same change in `Sparkline.DrawBars` and `Sparkline.DrawMarkers`,
`TimelineStrip.Draw`, and any callers in tabs.

The win is small per-call (a virtual dispatch is ~1 ns) but the call
count is large (180+ samples per frame across the three sparklines on
SUMMARY).

### 4.6 Layout-pass dedupe across the chrome + tabs

`OverlayPanel.DrawHealthCard` and `LayoutHealthCard` both compute
`startY` from the same chain of additions:

```
area.Y + padY
      + HeaderHeight + ChromeRegionGap
      + TabStripHeight + ChromeRegionGap
      + StatCardHeight + ChromeRegionGap
```

`LayoutStatCards` computes the same chain minus the trailing
`StatCardHeight + ChromeRegionGap`. `DrawSelf` recomputes
`ChromeHeight` implicitly via `OverlayLayoutCurrent.ChromeHeight` whose
getter is the same chain.

**Compute once per `DrawSelf`** into a struct on the stack:

```csharp
private struct ChromeLayout
{
    public int HeaderTop;
    public int TabStripTop;
    public int StatCardsTop;
    public int HealthCardTop;
    public int ContentTop;
    public int CardHeight;
    public Rectangle[] StatCardRects; // owned by the panel, not freshly alloc'd
}
```

`StatCardRects` is a `Rectangle[4]` field on the panel, sized once;
`LayoutStatCards` writes into it without `new Rectangle[CardCount]`.

This single change kills the only true per-frame heap allocation in
the chrome.

Similarly, `OverviewTab.Draw` recomputes `donutH`, `sparkH`, `chipsY`,
`rowsTop` from scratch on each draw, and `HandleClick`,
`MeasurePanelHeight` recompute the same Ys. Hoist into a
`_layoutCache` invalidated on Mode change.

### 4.7 `IReadOnlyList<double>` boxing on hot indexers

Repeated through the codebase:

```csharp
IReadOnlyList<double> categoryMs = OverlayState.SelectedCategoryMs(collector);
double ms = cell < categoryMs.Count ? categoryMs[cell] : 0d;
```

The interface dispatch through `IReadOnlyList<double>.Count` and
`this[int]` is virtual. The underlying types are `double[]` (the
frozen snapshot arrays) and `MetricCollector`'s smoothed-view arrays.
The cost per access is ~1-2 ns; multiplied by:

- TreeTab `BuildSortedRows`: modCount × catCount ~ 30 × 12 = 360 access/Tick.
- TreeTab `Draw`: visible × catCount = 12 × 12 = 144 access/draw.
- HitTest, MeasurePanelHeight: similar.

Total: ~1 000 indexer dispatches per frame from TreeTab alone.

**Fix.** Add `ReadOnlySpan<double>`-returning accessors on
`OverlayState`:

```csharp
public static ReadOnlySpan<double> SelectedCategoryMsSpan(MetricCollector c)
{
    if (Paused) return _frozenCategoryMs;
    return ShowAverage
        ? c.PerModCategoryAverageMs   // need a span accessor here too
        : c.PerModCategoryMs;
}
```

`MetricCollector` exposes its smoothed views as `IReadOnlyList<double>`
today; an additional `ReadOnlySpan<double> PerModCategoryMsSpan`
accessor is a cross-system change (see §5). Default the new accessor
to fall back through `IReadOnlyList<double>` if the collector hasn't
been updated yet.

### 4.8 `ModContent.GetInstance` call-site cache

Cache the `ProfilerSystem` reference inside `OverlayPanel` once per
`DrawSelf` call, plus once per `Update` call:

```csharp
private ProfilerSystem? _cachedSystem;
private ProfilerConfig? _cachedConfig;

private ProfilerSystem? GetSystem()
    => _cachedSystem ??= ModContent.GetInstance<ProfilerSystem>();

protected override void DrawSelf(SpriteBatch sb)
{
    // Refresh cache reference once per frame so a Reload doesn't leak a stale instance.
    _cachedSystem = ModContent.GetInstance<ProfilerSystem>();
    _cachedConfig = ModContent.GetInstance<ProfilerConfig>();
    // ...
}
```

Same in tabs. Saves 200-300 ns/frame of dictionary lookup; the bigger
benefit is uniform-cleanliness — every per-frame path reads through
the cache and the staleness window is one frame.

### 4.9 `ProfilerCard` / `Pill` / `HeatBar` micro-optimisations

#### 4.9.1 `ProfilerCard.Draw`

- Cache `OverlayLayoutCurrent.CardTitleStripHeight` and
  `OverlayLayoutCurrent.TextScaleBody` into locals; both call
  `OverlayState.Mode` getters via the bridge.
- The "approximate width" calculation for the right stat
  (`rightStat.Length * 6.5f * OverlayLayoutCurrent.TextScaleBody / 0.62f`)
  computes the same constant per call. Replace with a single multiplier
  derived once per Mode change.

#### 4.9.2 `Pill.Draw`

- `OverlayLayoutCurrent.TextScaleBody` accessed per call.
- The two `Color` slots (active/inactive fill, active/inactive border)
  could be `static readonly Color`s rather than constructed on each
  call (today `new Color(25, 40, 60)` constructs a struct on every
  pill draw — stack only, but a hoist clarifies intent).

#### 4.9.3 `HeatBar.Draw`

- Already lean. Verify the `Color fill = ProfilerTheme.CostColor(...)`
  call is fast — it is, a sequence of `Color.Lerp` on struct values.

#### 4.9.4 `TextureAssets.MagicPixel.Value` hoist

The `Value` property is a `LazyAsset<Texture2D>` resolve. Once loaded,
it returns a cached field reference — call cost is low. But every
`FillRect`/`DrawBorder`/`Sparkline.DrawX` calls it once per primitive.
Pass-through helpers (`FillRect(sb, ..., Texture2D pixel)`) would let
callers resolve once per `DrawSelf`. Marginal lever; defer unless
profiling reveals it's measurable.

### 4.10 Hidden chrome rebuilds at 60 Hz

`ProfilerOverlaySystem.ModifyInterfaceLayers` is called every frame
by tModLoader. It runs:

```csharp
layers.Insert(insertAt, new LegacyGameInterfaceLayer(
    "PerformanceProfiler: Overlay",
    DrawOverlay,
    InterfaceScaleType.UI));
```

That's a `LegacyGameInterfaceLayer` allocation (the type wraps a
`Func<bool>` delegate and the name string) every frame. The `DrawOverlay`
delegate, since it captures `this`, is allocated *once* in the JIT and
re-used, **unless** the C# compiler generates a delegate-creation site
that allocates a new `Func<bool>` on each call.

Inspection: `new LegacyGameInterfaceLayer("...", DrawOverlay, ...)` —
`DrawOverlay` is a method group, the compiler emits a delegate
creation. Method-group-to-delegate conversion in C# 11+ is cached
internally by the runtime *when the target is a static method*. For an
instance method, the compiler typically caches the delegate in a
generated field. **In .NET 8 this should not allocate per frame**; the
delegate gets created once and reused. Worth confirming with
`dotnet-counters` or a stand-alone repro.

The `LegacyGameInterfaceLayer` *itself* is still a fresh heap object
per frame. The fix is to allocate it once in `OnModLoad` (or lazily on
first `ModifyInterfaceLayers`) and stash it in a field:

```csharp
private LegacyGameInterfaceLayer? _layer;

public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
{
    if (!_visible || _userInterface == null) return;
    _layer ??= new LegacyGameInterfaceLayer(
        "PerformanceProfiler: Overlay", DrawOverlay, InterfaceScaleType.UI);
    int cursorLayer = layers.FindIndex(layer => layer.Name == "Vanilla: Mouse Text");
    layers.Insert(cursorLayer >= 0 ? cursorLayer : layers.Count, _layer);
}
```

The `layers.FindIndex` allocates a `Predicate<GameInterfaceLayer>`
each call (closure over the literal `"Vanilla: Mouse Text"` is a
compile-time constant so a static lambda captures it; the JIT should
emit a cached static delegate). Verify with a profiler.

`DrawOverlay`'s `new GameTime()` is a 60 Hz heap object; cache a
single `_gameTime` field, since `_userInterface.Draw` doesn't actually
read the GameTime when drawing chrome (it'd be cleaner to inspect what
it does with it, but the conservative fix is to reuse a single
instance).

### 4.11 Resize / drag jitter and `Recalculate` cost

`Recalculate` on a `UIElement` walks its child tree. Our `OverlayPanel`
has no children appended in the post-overhaul codebase (the M1
sub-elements were all collapsed into `DrawSelf`), so `Recalculate` is
near-free. The guards in `ApplyWidth`/`ApplyHeight` already prevent
work on stable frames. **No change needed.**

The `PanelWidth` getter calls `ModContent.GetInstance<ProfilerConfig>()`
on every `Update` frame. Hoist to the `_cachedConfig` from §4.8.

### 4.12 `RebuildTimelineMarks` allocation profile

`SpikesTab.Draw` calls `RebuildTimelineMarks(collector)` **every frame**
without throttle. The method clears `_timelineMarks` (a reused list)
and re-appends every spike + stall in the history window. At 50
spikes + 50 stalls per session in the baseline, that's 100 struct
appends per frame, ~6 000/sec. No heap allocations — the list's
backing array is reused — but it's wasted work because the spike +
stall lists only change on detection (rare). Throttle to 1 Hz like the
sibling tabs:

```csharp
private long _tickCounter;
private long _lastTimelineRebuild = -60;
private const int TimelineRebuildInterval = 60;

public void Tick(MetricCollector collector)
{
    _tickCounter++;
    int total = collector.Spikes.Count + collector.Stalls.Count;
    // ... existing scrollOffset clamp ...
    if (_tickCounter - _lastTimelineRebuild >= TimelineRebuildInterval)
    {
        _lastTimelineRebuild = _tickCounter;
        RebuildTimelineMarks(collector);
    }
}
```

Move `RebuildTimelineMarks` out of `Draw` and into `Tick`. Saves ~100
struct copies × 60 = 6 000/sec.

---

## 5. Cross-system dependencies

The overlay consumes data from:

### 5.1 `MetricCollector` (`Profiling/MetricCollector.cs`)

- `History` (`RingBuffer<TickFrame>`) — read by every tab for sparklines + averages.
- `Baseline.FrameMsMedian` — relativity for severity colour mapping.
- `PerModCategoryMs/AverageMs/Bytes/AverageBytes` and `PerHook*` — read by TreeTab + OverviewTab.
- `Spikes` / `Stalls` — read by SpikesTab.
- `SelfHealth` — read by chrome + SelfTab.
- `TracksAllocations` — boolean gate.
- `PerTickRing` (`PerTickAttributionRing`) — read by OverviewTab's `RefreshSparklines` for per-tick allocation sums.

**Optimisation tie-in.** §4.7 wants `ReadOnlySpan<double>` accessors on
collector views. These are additive — the existing
`IReadOnlyList<double>` accessors stay for code paths that aren't on
the per-frame hot path. The collector's research dossier (separate
file, not authored here) will own the implementation details; the
overlay's contract is "if a `Span` accessor exists, use it".

**Cross-call note.** `RefreshSparklines` in OverviewTab loops over
`collector.PerTickRing.GetPerModBytes(tickIndex, mod)` for each
`(tick, mod)` pair — that's `n × modCount` calls per Tick. At
n = 1800 (history capacity) and modCount = 30, that's 54 000 calls
per Tick (1 Hz, so 54 000/sec). The ring's getter is fast
(array index + mask) but the sum could be precomputed in the
collector as `PerTickRing.GetTotalAllocBytesAt(tickIndex)` once per
tick, eliminating the inner loop. Cross-system change; the overlay
just adopts the new accessor when available.

### 5.2 `Persistence` (`Profiling/Persistence/*`)

The overlay does not read from persistence directly. It reads from
`MetricCollector` which is the live in-memory view; persistence is
write-only from the overlay's perspective. **No direct dependency.**

### 5.3 `InsightsEngine` (`Profiling/Insights/*`)

- `InsightsTab._engine` = `InsightsEngine.GetOrCreateShared()`.
- `_engine.Evaluate(collector, latestTick, sessionLengthTicks)` — 1 Hz.
- `_engine.Store.TopInto(_ranked, VisibleCards, latestTick)` — already alloc-free per `insights-engine.md`.
- `_engine.GatedLabel` — string read.
- `InsightRenderer.Render(rec, audience, density)` — body string allocation per record. Cached in `_rankedBodies`.

The engine work is already 1 Hz. The leftover allocation is `Render`
producing a fresh body string per record. The cache in
`_rankedBodies` already covers it. **No new overlay-side optimisation
needed**, but a cross-system note: `InsightRenderer.Render` itself
could buffer-pool its formatter to avoid the rented-char-array hot
path. The engine dossier (separate file) owns that.

### 5.4 `HookInterceptor.ProfiledModNames`

A `string[]` field, read by every tab. The names are immutable for the
session lifetime. **No allocation impact**; safe to alias to a local
`string[]` per `Draw`.

### 5.5 `HookCoverageView`

Read by TreeTab (coverage badge) and OverlayPanel (CoverageTotals).
The getters return small ints; no allocation. **No change.**

### 5.6 `ProfilerConfig`

Read by `OverlayPanel.PanelWidth` per Update frame. §4.8 hoists this
to a per-Update cache.

---

## 6. Prioritised order and verification protocol

### 6.1 Tier 1 — must land, highest payoff (~70% of total saving)

| #  | Change                                                     | Files touched                                               | Expected saving | Risk        |
|----|------------------------------------------------------------|-------------------------------------------------------------|-----------------|-------------|
| T1.1 | Format-cache for all 6 tabs (1 Hz refresh of display strings) | `OverviewTab.cs`, `TreeTab.cs`, `SpikesTab.cs`, `EventsTab.cs`, `InsightsTab.cs`, `SelfTab.cs` | ~3 700 strings/sec | Medium — invalidation correctness on toggles |
| T1.2 | Chrome `LayoutStatCards` reuses a `Rectangle[4]` field rather than `new Rectangle[CardCount]` per frame | `OverlayPanel.cs`                                           | 1 heap object/frame | Low         |
| T1.3 | `ProfilerOverlaySystem._layer` cached once, `_gameTime` cached once | `ProfilerOverlaySystem.cs`                                  | 2 heap objects/frame | Low         |
| T1.4 | Chrome's THIS TICK / GC THIS TICK on 5 Hz cache             | `OverlayPanel.cs`                                           | ~220 strings/sec | Low         |
| T1.5 | `SpikesTab.RebuildTimelineMarks` 1 Hz throttle moved to Tick | `SpikesTab.cs`                                              | ~6 000 struct copies/sec | Low         |

### 6.2 Tier 2 — substantial wins, low risk (~20%)

| #  | Change                                                     | Files                                                       | Saving           | Risk |
|----|------------------------------------------------------------|-------------------------------------------------------------|------------------|------|
| T2.1 | `DonutChart` slice-hash + skip rebuild + indexer-loop replacing `foreach` over `EffectPassCollection` | `DonutChart.cs`, `OverviewTab.cs` | ~43 k trig calls/sec; 60 enumerators/sec | Low |
| T2.2 | `Sparkline` ReadOnlySpan<double> overloads                 | `Sparkline.cs`, `OverviewTab.cs`, `SpikesTab.cs`            | ~180 virtual dispatches/frame | Low  |
| T2.3 | `ModContent.GetInstance` per-frame cache in chrome + tabs   | `OverlayPanel.cs`, tabs                                     | ~300 ns/frame    | Low  |
| T2.4 | `SeverityBadge.DrawConfidence/DrawStall/DrawSelfHealth` switch-to-literal instead of `enum.ToString().ToLowerInvariant()` | `SeverityBadge.cs`                                          | ~10 strings/frame on InsightsTab | Low  |
| T2.5 | `ImpactSkyline` label substring → cache truncated labels per slice (alongside the existing OverviewTab._truncatedNames) | `ImpactSkyline.cs`, `OverviewTab.cs` | Up to 12 strings/frame when skyline becomes the donut companion | Low |

### 6.3 Tier 3 — depth & polish (~10%)

| #  | Change                                                     | Files                                | Saving         | Risk |
|----|------------------------------------------------------------|--------------------------------------|----------------|------|
| T3.1 | `IOverlayTab.InvalidateCaches()` default-interface hook called by chrome toggles | `IOverlayTab.cs`, `OverlayPanel.cs`, every tab | structural; correctness | Medium |
| T3.2 | `OverlayLayoutCurrent.IsCompact` cached at start of `DrawSelf` to a local | `OverlayPanel.cs`, tabs              | ~30 property reads/frame | Low |
| T3.3 | DonutChart pre-built sin/cos LUT (180 entries) | `DonutChart.cs`                      | ~43 k trig/sec (when T2.1 hash misses) | Low |
| T3.4 | `ReadOnlySpan<double>` accessors on `OverlayState.SelectedCategoryMsSpan` + collector-side span views | `OverlayState.cs`, `MetricCollector.cs` (cross-system) | ~1 k dispatches/frame on TreeTab | Medium — cross-system |
| T3.5 | Pre-resolved `Texture2D` pixel passed into FillRect / sparkline / etc. helpers | `ProfilerTheme.cs`, `OverlayDraw.cs`, components | marginal | Low |

### 6.4 Verification protocol per tier

Every change at tier 1 carries a per-tab before/after measurement:

1. **Synthetic.** Add a benchmark test under `Tests/` that exercises the
   tab's `Draw` and `Tick` in isolation through a stub `SpriteBatch`
   capturing the call count. Assert the per-call allocation budget
   from `GC.GetAllocatedBytesForCurrentThread()`. The xUnit
   `PersistenceBenchmarkTests` already establishes the pattern.
2. **In-game.** Open the F9 overlay during a baseline-equivalent
   playtest. Note the THIS TICK + AVG 30 S chrome card values for
   60 s of steady-state play on each tab. Compare the deltas against
   the v0.5 baseline `0.96 ms avg`.
3. **Self-instrumented.** The profiler reports its own per-mod CPU
   in the Top-3 contributors table. The headline number — *the line
   that names PerformanceProfiler #1 in its own session* — should
   move down. A target like "PerformanceProfiler drops below the
   top-3 entirely" is aspirational; even moving from 0.27 ms/tick to
   0.20 ms/tick is a measured win.
4. **Allocation-rate counter.** `dotnet-counters monitor` on the
   tModLoader process during play, watch
   `System.Runtime[gen-0-gc-count,alloc-rate]`. Before-after.

Tier 2 + 3 carry the same protocol; the magnitude per tier is smaller
so the noise floor is higher. A change that doesn't show a measurable
delta is not "wrong" — small wins compound — but it should be
sanity-checked against the synthetic test count.

### 6.5 Rollback plan

Each tier ships as one or more commits, never as a single mega-commit.
If a tier-1 commit produces a measurable regression on the chrome's
THIS TICK card or breaks any cache-invalidation toggle (Mode, Paused,
ShowAverage, CurrentMetric), revert and re-investigate with the
specific failing toggle as the reproduction case.

The tabs are independent. Cache invalidation must be tested per-tab
with each toggle combination:

| Toggle change | Tab affected | What must refresh |
|---------------|--------------|-------------------|
| Mode switch (Default ↔ Compact) | all tabs | layout; strings stay valid |
| ShowAverage toggle (NOW ↔ 30S AVG) | TreeTab, OverviewTab | per-row value strings |
| CurrentMetric (CPU/MEM/BOTH) | TreeTab | per-row value strings + columns |
| Paused (LIVE → PAUSED) | TreeTab, OverviewTab, InsightsTab | frozen views; strings refresh once |
| Active tab switch | newly-active tab | first draw must show current data |

---

## 7. Cross-cutting design notes

### 7.1 The honesty contract is unaffected

None of these changes alter what the overlay *shows*. Every chart,
every column, every row, every badge stays. The optimisation lives
entirely inside how the strings, vertices, and rectangles are
*produced*. Invariant 3 (descriptive not normative) is untouched
because no copy is added or removed.

### 7.2 The read-only invariant is unaffected

None of these changes write to game state, world state, or any other
mod's state. The overlay continues to be read-only over the
`MetricCollector` / `EventAggregator` / `InsightsEngine` views.

### 7.3 The mod-specific-code prohibition is unaffected

None of these changes reference a named mod, namespace, or content ID.
The format caches operate on `HookInterceptor.ProfiledModNames[i]`
strings — generic surface from the data stack.

### 7.4 The host-drift abort path is unaffected

The overlay is a draw-thread consumer of measurements; it does not
hook the host. Invariant 4 lives in the Hook Interceptor's dossier.
**No interaction.**

### 7.5 What this dossier explicitly does NOT propose

- ❌ Reducing the number of tabs.
- ❌ Hiding rows from any list.
- ❌ Lowering refresh cadence below 1 Hz on already-1 Hz views (1 Hz is
   the audit-approved cadence; going below it would lose visual freshness).
- ❌ Removing any chart, sparkline, donut, skyline, or heatmap cell.
- ❌ Removing the Default/Compact toggle.
- ❌ Removing the CPU/MEM/BOTH metric pill.
- ❌ Cutting the slice legend, the contributors card, the calibration
   footer, or any other v0.5 element.

Every recommendation here is additive in capability: the same view,
cheaper to produce.

---

## 8. Worked example — what the OverviewTab cache looks like in practice

Today (per-frame, ~64 strings):

```
Tick (1 Hz):
  _scorer.Recompute  → fills sorted list
  RefreshSlices      → fills _slices, _topModId, _topModComposite, _topModShare
  RefreshSparklines  → fills _frameSeries, _allocSeries, _spikeMarkers

Draw (60 Hz):
  DrawDonutCard:
    if (_topModId >= 0) {
      string name = OverlayDraw.Truncate(_truncatedNames[_topModId], 14);   // alloc (truncate may concat "..")
      string sharePct = $"{_topModShare * 100d:F0}%";                       // alloc
      string composite = $"{_topModComposite:F1} ms";                       // alloc
      OverlayDraw.Text(..., name);
      OverlayDraw.Text(..., sharePct);
      OverlayDraw.Text(..., composite);
    }
  DrawSliceLegend:
    for each slice:
      string label = OverlayDraw.Truncate(slice.Label ?? "?", 18);          // alloc
      string pct   = $"{slice.Value / _slicesTotal * 100d:F1}%";            // alloc
    if (more):
      string moreLine = $"+ {more} more";                                   // alloc
  DrawContributorsCard:
    for each row (5):
      string components = $"cpu {m.CpuMs:F2}ms · alloc {allocStr}/t · spike {m.SpikeMs:F1}ms";  // alloc + inner alloc
      OverlayDraw.Text(..., $"{m.Composite:F1}");                                              // alloc
  DrawAllModsRanking:
    for each row (up to 12):
      OverlayDraw.Text(..., $"{m.Composite:F2} ms");                                            // alloc
      string components = ComposeShortComponents(m);                                            // returns literal
    footer:
      string calNote = _scorer.IsCalibrated ? $"calibrated · ..." : $"calibrating · ...";       // alloc
```

After (per-frame, ~0 strings):

```
Tick (1 Hz):
  _scorer.Recompute  → fills sorted list
  RefreshSlices      → fills _slices, _topModId, _topModComposite, _topModShare
  RefreshSparklines  → fills _frameSeries, _allocSeries, _spikeMarkers
  RefreshFormatCache:
    _cache.TopModName       = OverlayDraw.Truncate(_truncatedNames[_topModId], 14);
    _cache.TopModSharePct   = (_topModShare * 100d).ToString("F0") + "%";
    _cache.TopModComposite  = _topModComposite.ToString("F1") + " ms";
    for each slice i:
      _cache.SliceLabels[i]   = OverlayDraw.Truncate(slice.Label ?? "?", 18);
      _cache.SlicePercents[i] = (slice.Value / _slicesTotal * 100d).ToString("F1") + "%";
    _cache.MoreLine = sliceCount > 6 ? "+ " + (sliceCount - 6) + " more" : null;
    for each contrib row r (5):
      _cache.ContribComposite[r] = sorted[r].Composite.ToString("F1");
      _cache.ContribComponents[r] = ComposeComponentLine(sorted[r]); // unchanged helper
    for each all-mods row r (12):
      _cache.AllRowsValue[r] = sorted[r].Composite.ToString("F2") + " ms";
    _cache.CalibrationNote = _scorer.IsCalibrated
        ? "calibrated · " + (_scorer.GcMsPerByte * 1_000_000d).ToString("F2") + " ms/MB"
        : "calibrating · " + _scorer.CalibrationSamples + "/" + ModImpactScorer.MinCalibrationTicks + " ticks";

Draw (60 Hz):
  read _cache.* — zero string allocations.
```

The Tick path now allocates ~30-40 strings *once per second*, instead
of allocating ~64 strings 60 times per second. That's a **96%
reduction** on the OverviewTab's display-string heap traffic.

The other tabs follow the same pattern. EVENTS gains the column-header
hoist (a `static readonly string[]`), the per-row `FormatDwell` cache,
and the per-row F2/F1/F0 caches. TREE caches the per-row ms + bytes
strings plus the coverage badge string per modId. INSIGHTS already
caches `_rankedBodies`; the small remaining work is the per-card
"X hits"/"share Y · pAdj Z" strings and the `confidence.ToString()`
switch-to-literal.

---

## 9. Risks and counter-arguments

### 9.1 "The string allocations are gen0-cheap"

A common rebuttal to alloc elimination is "they're gen0, they're free
in the steady state — the GC is fine". Counter:

- The profiler measures GC pauses (`MetricCollector.GcTimeMs`,
  `StallDetector` MajorGc/MinorGc cause). When the profiler *itself*
  produces 120 KB/sec of gen0 traffic, it skews its own stall
  attribution: a gen0 collection during overlay-open is partly
  caused by the overlay.
- The end-of-session `UiOverlayBlocking` cluster (40 stalls over 8.5 s)
  is partly persistence-side, but the chrome's own steady-state
  drift contributes to gen0 promotion across the session. A profile
  test with the overlay closed for 5 min vs open for 5 min should
  show measurable gen0 collection delta. **This is a verification
  hypothesis, not a load-bearing claim.**
- Even if gen0 collection cost is "free" per individual event, the
  *cumulative gen2 promotion* from long-lived caches that survive
  several gen0 cycles raises the cost of every subsequent gc. The
  10-min session DB-size baseline (1 064 KB) shows the per-session
  storage cost has grown 41% with v0.5; the in-memory equivalent for
  the overlay is the kind of growth that doesn't show up until a
  player does a 4-hour run.

### 9.2 "Caching strings is harder than allocating them"

True for the invalidation rules. The mitigation: every tab already has
1 Hz Tick infrastructure; the chrome owns the toggle writes; an
`InvalidateCaches()` default-interface call from the chrome touches
each tab once per toggle. The amount of new bookkeeping is bounded
and isolated; it does not bleed across the tab boundary.

### 9.3 "DrawUserPrimitives is already cheap"

The DonutChart's biggest per-frame cost is not the triangle submission;
it is the End/Begin pair and the trig in `BuildRingTriangles`. The
hash-and-skip lever (T2.1) reduces `BuildRingTriangles` from 60 Hz to
1 Hz; that is the lever. The Begin/End we *cannot* avoid as long as
`DrawUserPrimitives` is the renderer; the sin/cos LUT (T3.3) shaves
the rebuild itself when it does fire.

### 9.4 "These wins are tiny and won't move the headline"

The baseline calls out PerformanceProfiler at 0.27 ms/tick avg total.
The overlay-open contribution to that number is one slice of the pie;
the Hook Interceptor and Metric Collector dossiers will own most of
it. The overlay's responsibility is to *not be in the top-3 of the
PerformanceProfiler-internal cost split* once the data-side dossiers
land. A 50% reduction in the overlay's allocation rate is the right
order of magnitude to move the needle when the rest of the mod also
drops.

### 9.5 "What if a future tab needs a string at 60 Hz?"

The cache layer doesn't prohibit per-frame strings; it makes them
*explicit*. A new tab can opt into per-frame formatting where it
genuinely needs to (e.g. a future "live tick" tab that renders the
current frame's per-tick stalled-on-syscall info — that data is
genuinely 60 Hz). The pattern says "if you allocate per-frame, justify
it"; it doesn't say "you can't".

### 9.6 "ReadOnlySpan<double> overloads risk source-bloat"

Each Sparkline / HeatBar / TimelineStrip helper grows one overload.
That's ~6 new methods total, each ~10 lines. The IL emitted by the
JIT is mechanical (range check + array access); not a maintainability
hazard at this scale.

### 9.7 "The donut hash might miss a real change"

The hash candidate (`sum of value × i` + count) collides if a permutation
preserves the weighted sum. Plausible only for pathological inputs;
the OverviewTab.RefreshSlices populates slices in sorted order so a
permutation requires two slices to be equal *and* a third slice to
compensate. Probability in practice is ~0; in the worst case a frame
shows a 16 ms stale donut and recovers the next second. Adding a
`slices.Count` term + a `topModId` term to the hash makes the
collision space negligible.

---

## 10. Numerical summary

If everything in Tiers 1-3 lands, the overlay's per-frame heap traffic
on the SUMMARY tab drops from approximately:

| Surface              | Before (B/sec) | After (B/sec) | Delta |
|----------------------|----------------|---------------|-------|
| Display strings      | ~120 000       | ~3 000        | -97%  |
| Per-frame layer object | ~1 200       | 0             | -100% |
| Per-frame GameTime    | ~3 600        | 0             | -100% |
| LayoutStatCards Rectangle[] | ~2 400  | 0             | -100% |
| DonutChart enumerator | ~4 800        | 0             | -100% |
| RebuildTimelineMarks (SpikesTab) | n/a (stack) | reduced 60× | -98% |
| Sparkline virtual dispatches | n/a (cycles) | -50% | -    |

**Aggregate.** ~130 KB/sec of draw-thread heap traffic eliminated.
**Per-tick cost.** Estimated 0.10-0.15 ms/tick saved when overlay is
open, primarily from gen0 reduction and trig elimination.

These numbers feed into the master plan's "every baseline number
moves in the better direction" contract. The overlay's contribution
to the headline `PerformanceProfiler 0.27 ms/tick` figure should drop
to a near-floor where the rest of the profiler's instrumentation
becomes the dominant cost — exactly where the player's intuition for
"the profiler is observing, not interfering" sits.

---

## 11. References

### tModLoader / FNA / XNA

- [FNA SpriteBatch source](https://github.com/FNA-XNA/FNA/blob/master/src/Graphics/SpriteBatch.cs) — the in-memory batching model FNA uses; flush happens at End().
- [Nuclex Games Blog — DynamicVertexBuffer vs DrawUserPrimitives](http://blog.nuclex-games.com/2010/11/dynamicvertexbuffer-versus-drawuserprimitives-round-2/) — empirical measurement that recreating the vertex buffer every frame is near-equivalent to DrawUserPrimitives in throughput.
- [MonoGame community thread — Windows perf vs XNA/FNA](https://community.monogame.net/t/bad-performance-on-windows-compared-to-xna-and-fna-is-there-a-way-to-improve-it/8323?page=3) — context on batching is the dominant lever.
- [tModLoader source — `ModifyInterfaceLayers`, `LegacyGameInterfaceLayer`](https://github.com/tModLoader/tModLoader) — the layer API the overlay sits on.
- [Terraria `Utils.DrawBorderString`](https://github.com/tModLoader/tModLoader) — the 5×SpriteBatch.Draw pattern the overlay's `OverlayDraw.Text` wraps.

### .NET 8 string interpolation

- [Microsoft Learn — Interpolated string handler](https://learn.microsoft.com/en-us/dotnet/csharp/advanced-topics/performance/interpolated-string-handler) — how DefaultInterpolatedStringHandler works, when it's allocation-minimised.
- [.NET Blog — String Interpolation in C# 10 and .NET 6](https://devblogs.microsoft.com/dotnet/string-interpolation-in-c-10-and-net-6/) — historical change to interpolation; condition under which intermediate strings are eliminated (the final string is always allocated).
- [dotnet/runtime — DefaultInterpolatedStringHandler.cs source](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Runtime/CompilerServices/DefaultInterpolatedStringHandler.cs) — the ArrayPool-rented char buffer pattern.
- [NDepend — C# String Interpolation explained](https://blog.ndepend.com/c-string-interpolation-explained/) — practitioner walkthrough of the JIT-emitted code shape.

### Project-internal

- `context/perf-pass/baseline.md` — v0.5 baseline numbers and the
  hard-constraint list from the philosophy + Invariants.
- `context/notes/philosophy.md` — the universal/dynamic-over-enumerative
  posture and the "optimisation = doing the same work cheaper, never
  doing less" rule.
- `context/systems/overlay.md` — current implemented reality of the
  overlay; the audit findings and discarded approaches section.
- `context/notes/overview-tab-plan.md` — design intent for SUMMARY.
- `context/notes/events-tab-plan.md` — design intent for EVENTS.
- `context/notes/ui-overhaul-plan.md` — the phase 0..7 UI overhaul that
  produced the current component library.
- `CLAUDE.md` — the five Project Invariants and the dual-surface
  observability contract.
- `~/.claude/projects/.../memory/spritebatch-rotation-trap.md` — the
  durable lesson behind why DonutChart uses DrawUserPrimitives instead
  of rotated SpriteBatch.Draw.

---

## 12. Open questions for the master plan author

1. **Does the overlay's tier-1 work block on the MetricCollector dossier's `ReadOnlySpan<double>` accessors?** Suggested: no. The format-cache (T1.1) is self-contained inside each tab; the span-accessor work (T3.4) is independent and can land later.
2. **Is the `_layer` cache safe across `OnModUnload`?** A `LegacyGameInterfaceLayer` references the `DrawOverlay` instance delegate. On unload + reload, the `ProfilerOverlaySystem` is reconstructed and `_layer` will be re-allocated lazily. **Verify** that no stale layer survives `Mod.Unload`; if it does, `OnModUnload` must null the field.
3. **Should the format cache be opt-in via a config switch?** No — the cache is correctness-preserving (same output) and additive. Players never see the difference. A switch adds a code path that's identical to the cached path and is "tested only when someone flips it"; better to ship as the only path.
4. **Should the 5 Hz fast-refresh cadence be a config knob?** Same answer: no. The number is empirically chosen to be faster than a human can read; we don't want users to "tune" it.
5. **Does the donut hash need a tear-down on world unload?** No — `OverviewTab.RefreshSlices` clears `_slices` at the start of each Tick, so the hash recomputes on the next 1 Hz step. A stale hash from a previous world will not be reused because the slice list itself is empty.

---

*End of overlay perf-pass research.*
