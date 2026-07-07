# Plan — Per-Feature Settings via ModConfig (S23)

> Slot: atlas S23. Version target: 0.32.0.
> User-specified design (2026-07-07, verbatim constraints): per-feature toggles
> and sliders "categorised by impact (heavy RAM section, heavy CPU section),
> each feature from disable to full"; **use the game's own mod config menu**
> ("don't implement our very own settings menu… We want to reduce churn");
> no Lite/Standard/Deep presets; the heaviest version stays the default and the
> thing we verify.

## API (verified against tModLoader 1.4.4 source, 2026-07-07)

From `patches/tModLoader/Terraria/ModLoader/Config/ConfigAttributes.cs` and
`ModConfig.cs` (fetched via gh, 1.4.4 branch): `ModConfig` base with
`ConfigScope Mode` + `OnLoaded()` + `OnChanged()`; attributes `[Header]`,
`[DefaultValue]`, `[ReloadRequired]`, `[Range(min,max)]`, `[Increment]`,
`[Slider]` (ints render as text inputs unless present), `[DrawTicks]`,
`[OptionStrings]`, `[SliderColor]`. Labels/tooltips auto-localise via
`Mods.PerformanceProfiler.Configs.<Class>.<Member>.Label/.Tooltip` hjson keys.
Access from code: `ModContent.GetInstance<ProfilerConfig>()`.

Scope decision: **ClientSide** — the profiler is read-only client
instrumentation; nothing it configures affects world/server state.

## The config surface (v1)

One class, `Configs/ProfilerConfig.cs`, four headers. Defaults = heaviest.
Sliders go disable → full wherever a knob has a magnitude, not just on/off.

```
[Header] MeasurementCpu            (heavy-CPU section)
  bool  PerHookAttribution         = true   [ReloadRequired]  // 62k IL detours vs mod-level only
  bool  PhaseSplitAttribution      = true                     // S01 update/draw lanes
  bool  AllocationTracking         = true                     // per-mod alloc deltas
  int   DetectorCadenceTicks       = 60     [Range(30,600)] [Slider] [DrawTicks]
                                            // insights evaluation stride

[Header] MeasurementRam            (heavy-RAM section)
  int   FrameHistoryTicks          = 1800   [Range(600,3600)] [Increment(300)] [Slider] [DrawTicks]
                                            // rolling window: RAM ∝ hooks × window
  bool  RetainHookScaffolding      = true   [ReloadRequired]  // Invariant-4 re-chain safety vs ~31 KB/hook
                                            // OFF = trim SourceCloneIl after install (abort-clean
                                            // still works; re-chain after another mod's late hook
                                            // install falls back to reinstall) — the S24 lever
  int   MemorySampleSeconds        = 5      [Range(2,60)] [Slider]   // S04 guard cadence; 0 disabled via toggle
  bool  MemoryGuard                = true

[Header] Dashboard
  bool  DashboardServer            = true   [ReloadRequired]  // the HTTP server itself
  int   PollMs                     = 500    [Range(250,2000)] [Increment(250)] [Slider]
  bool  AutoExportHtmlReport       = false                    // S17: write report on session end

[Header] Detectors
  float SpikeSensitivity           = 1.0f   [Range(0.5f,3f)] [Increment(0.25f)] [Slider]
                                            // multiplier on baseline-relative spike threshold
  float StallSensitivity           = 1.0f   [Range(0.5f,3f)] [Increment(0.25f)] [Slider]
  bool  CrossSessionInsights       = true
```

Rationale rows the tooltips must carry (honesty contract: name the cost, not
advice): PerHookAttribution "~40 KB RAM per hook, the deepest attribution";
FrameHistoryTicks "RAM scales with window: 1800 ticks ≈ 30 s"; each tooltip
states what turning it down removes from the dashboard.

## Plumbing rules

- **Read points, not scattered reads**: `ProfilerConfig` is read once per
  world-arm into an immutable `ArmedSettings` struct on `MetricCollector` /
  `ILHookInterceptor` (hot path never touches `ModContent.GetInstance`).
- `OnChanged()` applies runtime-safe settings live (PollMs, sensitivities,
  cadence, MemorySampleSeconds) by publishing to the running systems;
  world-arm settings (history size, phase split) take effect next world load;
  `[ReloadRequired]` ones (per-hook install, scaffolding retention, server) go
  through tML's own reload flow — no custom UI, no custom prompts.
- Every consumer feature checks its gate at its natural boundary:
  interceptor at install, collector at arm, detectors at evaluate, dashboard
  at server start. A disabled feature renders its surfaces in the existing
  "reading from db"/warming visual language with "disabled in config" wording —
  never a blank pane (pairs with S20 warming states).
- Localisation: full hjson block for every Label/Tooltip (S31 partial payment).

## Test plan

- Unit: ArmedSettings snapshot honours each field; disabled AllocationTracking
  zeroes alloc surfaces without nulls; FrameHistoryTicks resizes the ring.
- Config-off matrix in the synthetic-session suite: each toggle off ⇒ its
  surfaces render the disabled state, everything else unaffected (the
  modularity invariant, now mechanically checked).
- Harness: a `config-minimal` fixture scenario (everything off/minimum) — the
  dashboard must stay coherent.

## Acceptance

1. All settings visible and editable in tML's own Mod Config menu, grouped
   under the four headers, sliders where magnitudes exist.
2. Defaults reproduce today's heaviest behaviour exactly (diff-tested on
   synthetic sessions: default config output == pre-config output).
3. No custom settings UI anywhere; zero new dashboard surfaces to maintain.
4. Every tooltip names the feature's cost and what its absence removes.
