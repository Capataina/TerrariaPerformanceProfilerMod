# Future settings UX — Standard vs Advanced modes

> Captured from a discussion on 2026-05-20. Not yet implemented; this note
> exists so the idea survives chat compaction.

## The user's framing, verbatim

> "I think the future would be a toggle-based system where you toggle whatever
> system you want on. You know how games do 'simple' and 'advanced'
> configurations where in simple you choose things like texture quality and
> all, but advanced ones are like anisotropic filtering mode and all. We can
> add a standard vs advanced settings menu where in standard we can have
> Lite, Standard, Deep and Off. In advanced mode we can have a per-feature
> toggle where we toggle things like per-hook, allocation tracking and
> everything individually so someone who knows what they're doing can
> enable/disable whatever feature they want."

## The shape of the proposal

### Standard mode — preset picker
A radio-button-style choice between four named profiles. The README's existing
mode budget defines these:

| Preset | Overhead budget | What's on |
|---|---|---|
| **Off** | 0% | No instrumentation. Overlay shows "profiler disabled". |
| **Lite** | < 1% | CPU timing only, per-mod aggregate only. No per-hook breakdown, no allocation tracking, no spike feed history beyond the current window. |
| **Standard** | 2–4% | CPU timing per-mod + per-category + per-hook. Allocation tracking on. Spike feed retained. (This is roughly the current Deep configuration.) |
| **Deep** | 5–10% | Standard + per-call-graph attribution + sampled stack traces + extra contextual capture. |

Switching profiles requires a mod reload (the IL emit shape changes between
"CPU only" and "CPU + alloc"). That hitch is acceptable because users won't
flip these constantly.

### Advanced mode — per-feature toggles
The same individual switches that the Standard presets group together,
exposed individually. Conceptual layout:

```
ADVANCED PROFILING SETTINGS

[x] Per-mod CPU timing                            (~0.3% overhead at 100 mods)
[x] Per-category breakdown                        (free given per-mod is on)
[x] Per-hook breakdown                            (~0.1%)
[x] Allocation tracking                           (~0.4%)
[ ] Per-call-graph attribution                    (~2-3%, M4+)
[ ] Sampled stack traces                          (~3-5%, M4+)
[x] Spike detection + feed                        (free)
[x] Event context capture                         (~0.05%)
[ ] Worldgen profiling                            (no runtime cost; load-time only)

estimated total overhead: 0.85%
```

Each toggle shows its measured overhead contribution. The estimate at the
bottom sums them so users can see what they're signing up for.

### How they interact
Selecting a Standard preset just flips the underlying advanced toggles.
Going into Advanced and changing toggles puts the preset into a "Custom"
state. Switching back to a named preset reverts the toggles. Identical to
how graphics settings work in most games.

## Why this matters

The profiler's audience splits cleanly:

- **Players** want to know "is my modlist OK" with minimum friction. Standard
  mode's four presets answer that question.
- **Modders** want fine control: they might want allocation tracking but not
  per-call-graph, or want every measurement but accept the cost. Advanced
  mode is for them.
- **Power users / curious players** want to understand what each switch does.
  Advanced mode's per-toggle overhead labels teach them.

A single setting ("how deep do you want it?") fails both groups: too coarse
for modders, too implementation-detail for players.

## What's needed before this can ship

1. The settings need somewhere to live. tModLoader provides `ModConfig`; we
   should use it rather than rolling our own persistence.
2. The advanced toggles need to actually map to runtime flags. We already
   have `HookBackend.AllocationTracking`, `HookBackend.Mode`, etc.; we'd add
   one for per-hook tracking, spike detection, event capture, etc.
3. Mode switching needs a reload path. The plan in
   `spikes-and-allocations-plan.md §6` already sketches Uninstall →
   Reconfigure → Reinstall as the transition mechanism.
4. Per-toggle overhead estimates need to be measured, not guessed. We do
   this as part of pre-release validation: run a Calamity-class modlist with
   each toggle individually on/off, measure overhead delta. Bake the numbers
   into the UI as static constants.

## When to build this

After the core features land (CPU + allocation + spikes + events + overview
+ insights) and we have something worth toggling. The README's M5 (Workshop
Release) milestone is the natural home — settings UX is a release-readiness
concern, not a feature concern.

For now (M1–M3) we run on Deep-equivalent (everything on) so we can find
and fix bugs in the deepest path. Lite / Standard / Off are deferred.

## How this relates to current code

- `HookBackend.AllocationTracking` — already exists; will become one
  advanced toggle.
- `HookBackend.Mode` — already exists (Delegate / ILHook / Parallel); not
  user-facing, used for backend validation. The Standard preset doesn't
  expose this.
- Spike threshold + floor — already configurable as fields on
  `SpikeDetector`; will become advanced inputs.
- Event capture cadence — defined by the events plan; will become an
  advanced input.

When we get here, the principle is: each toggle is one bool or a small
config record, set once at install time, baked into the IL emit / ring
size / detector params. No runtime branching in the hot path — we
re-install when settings change.
