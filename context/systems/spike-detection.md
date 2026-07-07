# Spike Detection

*Maturity: working · Stability: stable.*

## Scope / Purpose

Spike detection identifies frame-time anomalies (a 60ms+ outlier in a 16.7ms baseline) and attributes them to the mod most responsible. It runs against the same per-tick stream `MetricCollector` produces; output feeds the dashboard spike surface (`/api/spikes` + `/api/lag-clusters`) and the persisted `spikes` collection.

## Boundaries / Ownership

Files: `Data/Detectors/SpikeDetector.cs`, `Data/Aggregators/PerTickAttributionRing.cs` (both moved out of `Profiling/` in v0.11).

Owns:

- Spike window detection using median + MAD (median absolute deviation) over a rolling baseline.
- The 50-window retained spike ring.
- Per-tick per-mod attribution lookup at spike time via `PerTickAttributionRing`.
- `FlushSpikes()` — force-close any open spike window at world unload.

Does not own:

- The frame-time data stream — produced by `MetricCollector.EndTick`.
- The persisted spike block layout — that lives in the persistence layer (`SessionRecorder` + `Data/Streams/SpikeStream.cs`); see `systems/persistence.md`.
- The spike UI — the live surface is the browser dashboard (`systems/web-dashboard.md`); the in-game SpikesTab is archived under `UI/`.

## Current Implemented Reality

### Detection algorithm

`SpikeDetector.Observe(TickFrame)` (called from `MetricCollector.EndTick`) maintains a rolling baseline of recent `FrameTimeMs` values, computes the median and MAD, and opens a spike window when the current frame exceeds `median + k * MAD`. The window stays open while consecutive frames remain above threshold; closes when the stream returns to baseline for several frames.

Each open window records:

- `StartTick`, `EndTick`
- `PeakFrameTimeMs`, `MedianFrameTimeMs` (at start)
- Per-mod peak attribution (the mod that contributed the highest delta within the window)

### `PerTickAttributionRing`

Separate 50-window ring (`Data/Aggregators/PerTickAttributionRing.cs`) retaining raw per-tick per-mod CPU samples. Used at spike close time to compute "which mod's per-tick cost diverged most from its rolling mean during this window?" — that's the peak-contributor attribution.

The 50-window cap means the spike surface shows the 50 most recent spikes; older windows are evicted.

### `Windows` exposure

`SpikeDetector.Windows` is a cached `SpikeWindowsView` constructed once at `SpikeDetector` construction (`MetricCollector` initialisation). Before commit `77a99d2`, this property allocated a fresh view per read from the consumer path (then the overlay + session log). The audit (`plans/code-health-audit/hook-instrumentation.md` finding "Cache the spike window view exposed to consumers") flagged it; the fix is the cached `_windowsView` field.

### `FlushSpikes`

`SpikeDetector.Flush()` forces any open window to close, so an in-progress spike that coincided with world exit is captured. Called from `MetricCollector.FlushSpikes()` → `ProfilerSystem.KickOffSessionEndAsync()` (run from `PreSaveAndQuit` or `OnWorldUnload`) before `SessionRecorder.End()`.

Audit potential-issue #3: "Open spike windows may be missing from final session reports." Fixed in commit `77a99d2`.

## Key Interfaces / Data Flow

```
MetricCollector.EndTick(frame):
    _spikeDetector.Observe(frame)
        ↓ rolling median + MAD
        ↓ open window if frame > threshold
        ↓ extend window if still above
        ↓ close window if below for N frames → push to Windows ring

SpikeDetector.Windows → SpikeWindowsView (cached)
    ↑ read by SpikesStat (dashboard /api/spikes)
    ↑ read by SessionRecorder + SpikeStream (persisted spikes)
    ↑ read by PeakContributorToSpikeDetector (insights)

session end (PreSaveAndQuit or OnWorldUnload → KickOffSessionEndAsync):
    Collector.FlushSpikes() → SpikeDetector.Flush()
        force-close any open window → push to Windows
```

## Implemented Outputs / Artifacts

| Surface | Source |
|---------|--------|
| Dashboard `/api/spikes` rows (archived SpikesTab) | `SpikeDetector.Windows` |
| Persisted `spikes` collection | `SpikeDetector.Windows` via `SessionRecorder` / `SpikeStream` |
| `PeakContributorToSpike` insight | reads `SpikeDetector.Windows` |

## Known Issues / Active Risks

- **Spike threshold is process-constant.** No player surface. A modlist with a tight frame budget (low-end hardware) might want a lower threshold; today everyone sees the same `median + k * MAD` cutoff.
- **`PerTickAttributionRing` capacity is 50.** Spike-heavy sessions (every minute or so on a stressed modlist) will evict the earliest spikes from the player's view. Acceptable today; the session JSON also has the same cap.
- **Median + MAD baseline can be fooled by sustained high frame times.** If the player walks into a hot zone and stays, the baseline tracks the new median; the spike detector stops firing because the new normal is the old "spike." Documented behaviour, not a bug — by design the detector finds outliers vs recent history.

## Partial / In Progress

Nothing in progress.

## Planned / Missing / Likely Changes

- **Settings exposure of spike threshold.** Sketched in `notes/future-settings-design.md`.
- **Spike-pattern detector** is gated (insights engine §6 / §5.5). Would consume the same `Windows` stream and surface "this spike pattern repeats every N minutes."

## Durable Notes / Discarded Approaches

- **The original spike attribution compared per-tick CPU against a per-mod *all-time* mean.** This was the bug fixed in commit `45baf02` ("Fix spike attribution: ring buffer was comparing two unrelated counters"). The all-time mean is dominated by the most-active mods, so a transient spike from a normally-quiet mod looked tiny in relative terms. The fix is to compare against a per-mod *rolling* mean over the same window the spike spans.

## Obsolete / No Longer Relevant

Nothing.

## Cross-references

- `systems/metric-collection.md` — the `TickFrame` stream the detector observes.
- `systems/insights-engine.md` — `PeakContributorToSpikeDetector`.
- `systems/web-dashboard.md` — the live spike surface (`/api/spikes`, `/api/lag-clusters`); the in-game SpikesTab is archived under `UI/`.
- `systems/persistence.md` — the `SpikeStream` + `SessionRecorder` path that persists windows.
- `notes/decisions.md` — the spikes-and-allocations design rationale (the per-feature plan note was folded in here).
- `plans/code-health-audit/hook-instrumentation.md` — `_windowsView` caching finding.

## 2026-07-07: real-cadence trigger + sensitivity

`SpikeDetector.OnTick` triggers on `frame.RealFrameTimeMs` against the (now
real-cadence) baseline median — a spike is a frame the PLAYER felt, not a
compute excursion the draw phase absorbed (`448f447`). Per-mod attribution is
unchanged (it reads the tick's compute breakdown regardless). Window worsts
inherit real cadence, so chart spike marks align with the honest trace.
`SpikeSensitivity`/`StallSensitivity` config values divide the tuned threshold
multipliers (1.0 = the 2×/3× defaults) via
`MetricCollector.ConfigureDetectorSensitivity` — runtime-safe, applied on
OnChanged.
