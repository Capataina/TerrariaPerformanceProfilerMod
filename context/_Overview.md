# Performance Profiler — Context Folder

> Repository implementation memory. Read this first.

## What this folder is

A map of **where every Performance Profiler component plugs into tModLoader**, and how much of the mod is buildable on tModLoader's public API alone.

Generated **2026-05-19** by a six-agent parallel exploration of tModLoader's public modding API — the `tModLoader.xml` documentation file shipped with the install (4,946 documented members; tModLoader **1.4.4**, build ~#5089). No tModLoader source was decompiled or cloned; this is a public-API survey.

## The strategy this folder serves

**Decision (2026-05-19):** build as much of the mod as possible on tModLoader's *documented public API* first. Where the public API cannot reach, prefer a **workaround** (guarded reflection, runtime-observed IL). If a genuine wall is hit, cloning the tModLoader source to read the real code is the sanctioned next step — the *actual repository*, checked out to the installed build's commit (`RecentGitHubCommits.txt` gives the hash), not a decompiler. The mod compiles against `tModLoader.dll`'s metadata regardless; source is only ever a reading aid, never a build requirement.

## The single most important finding

The `NEEDS DECOMPILER VERIFICATION` flags scattered through the per-slice docs are **not all the same kind of gap**. They split cleanly in two, and the distinction drives the whole build plan:

| Gap type | What it means | How we resolve it (no decompiler) | Cost / fragility |
|---|---|---|---|
| **Documentation gap** | The member is `public` — fully callable — it just has no doc-comment, so it is absent from `tModLoader.xml`. Examples: `Player.ZoneSnow`, `Main.dust`, `NPC.active`, the `UIPanel`/`UIScrollbar` widgets. | The C# compiler sees every public member from the DLL's metadata regardless of docs. Confirm the exact name once via the IDE's metadata view, or a one-off **reflection dump** (`MetadataLoadContext` over `tModLoader.dll` — list members, not bodies; not a decompiler). Then write normal direct code. | Zero runtime cost, zero fragility. This is just *normal .NET development against a dependency*. |
| **Visibility gap** | The member is genuinely `internal` / `private`. Examples: tModLoader's `*Loader` dispatch internals + `HookList`, possibly `Main.SavePath`, possibly `ModLoader.Mods`. | Reach it with **guarded runtime reflection** (`BindingFlags.NonPublic`) wrapped in the abort-clean guard (Invariant 4): resolve → verify shape → use, or disable that layer and report. | Real but bounded. Internals shift across tModLoader versions — which is *exactly* the failure mode Invariant 4 was designed for. |

**Most flags are documentation gaps.** Only the Hook Interceptor's cross-mod dispatch internals are an irreducible visibility gap — and even that is reachable, just fragile. Nothing in the mod is *blocked*. A reflection/metadata dump of `tModLoader.dll` (a normal, non-decompiler step) resolves which flags are which.

## Coverage scorecard

How much of each component stands on the documented public API, and the verdict under the public-API-first strategy:

| # | Component | Public-API coverage | Verdict | The gap, and the workaround |
|---|---|---|---|---|
| 1 | **Hook Interceptor** | ~1/3 | **Workaround required** | Own-tick framing + the `MonoModHooks.Modify`/`Add` primitive are public. Cross-mod attribution needs ILHooking tModLoader's internal `*Loader` dispatch loop (`HookList`). *Workaround:* `MonoModHooks.Modify` accepts any `MethodBase`, so resolve the loader methods by guarded reflection, ILHook them, abort-clean if the IL shape mismatches. This is the Milestone 0.A spike. |
| 2 | **Per-Tick Metric Collector** | 100% (BCL) | **Build now** | None. Pure logic: `Stopwatch.GetTimestamp()`, `GC.GetAllocatedBytesForCurrentThread()`, a pre-allocated `PerModSample[]`. Unit-testable with no game running. |
| 3 | **Ring Buffer** | 100% | **Build now** | None. Pure data structure; `OnWorldLoad`/`OnWorldUnload` give the alloc/free lifecycle. Unit-testable. |
| 4 | **Context Tagger** | ~85% | **Build now** | Modded-biome + event/world flags are public. Gap: the vanilla `Zone*` boolean *names* (`ZoneSnow`, `ZoneCorrupt`, …) are a documentation gap. *Workaround:* confirm names from DLL metadata, then plain field reads. |
| 5 | **Encounter Detector** | ~80% | **Build now** | Boss/biome/event triggers are public hooks. Win/died/fled outcome is our own derived logic, not an API. No real gap. |
| 6 | **UI Renderer** | shell ~95%, paint = custom | **Build now** | The overlay *shell* (keybind, layer, widget tree) is public. The *paint* (graded bars, sparklines, heatmap, card) is custom `DrawSelf` against `SpriteBatch`/fonts — a documentation gap. *Workaround:* standard FNA drawing; confirm handles from metadata + ExampleMod. |
| 7 | **Persistent Store** | ~90% | **Build now** | The *when* (write on `PreSaveAndQuit`, sentinel-file crash recovery) is public. Gap: the writable data directory — `Main.SavePath` is a likely visibility gap. *Workaround:* guarded reflection + platform-path fallback. |
| 8 | **Insights Engine** | 100% | **Build now** | None. Pure post-session rule logic over already-collected data. No tModLoader API at all. Unit-testable. |
| — | **Mod identity / modlist fingerprint** | ~95% | **Build now** | Per-mod metadata (`Mod.Code`, `Name`, `Version`, `DisplayNameClean`) is public; the `Assembly → ModId` map strategy is confirmed sound. Gap: enumerating loaded mods (`ModLoader.Mods` — likely a visibility gap). *Workaround:* guarded reflection. |

**Bottom line:** 7 of 8 components plus mod-identity are buildable on the public API now (most fully, a few with a one-time metadata-name confirmation). **Only the Hook Interceptor's cross-mod attribution is genuinely hard** — and it is the one thing Milestone 0.A exists to spike. The README's architecture is confirmed correct throughout; nothing here contradicts it.

## The build order

Four tiers, by what each needs to be built and tested:

```
TIER 1 — buildable today, no game, fully unit-testable          ◄── START HERE
  Ring Buffer (3) · Metric Collector data structures + timing logic (2)
  Insights Engine rule logic (8) · JSON-lines schema + serialisation (7)
  modlist-fingerprint hashing
  → pure C#, no tModLoader API, no reflection. Matches the CLAUDE.md
    testability standard exactly: pure logic separable from the runtime.

TIER 2 — documented public API, needs the game to exercise
  lifecycle wiring (OnWorldLoad/Unload) · F9 keybind + overlay shell (6)
  engagement hooks — OnKill / UseItem / Shoot / ModBiome (4, 5)
  Encounter Detector triggers (5)

TIER 3 — needs a one-time metadata-name confirmation (documentation gaps)
  vanilla Zone* field names · Main.dust / active flags · UI draw substrate
  → resolved by a reflection/metadata dump of tModLoader.dll, then direct code.

TIER 4 — the genuine spike (Milestone 0.A)
  Hook Interceptor cross-mod attribution — ILHook the internal *Loader
  dispatch, guarded by abort-clean. The one irreducible visibility gap.
```

**Recommended first code: Tier 1.** The Ring Buffer, the Metric Collector's structures and timing maths, the JSON-lines schema, the fingerprint hash, and the Insights rule logic are all pure C# — no game, no tModLoader, no reflection — and all unit-testable against synthetic input. That is the project's lowest-risk, highest-certainty starting point and it matches both the README's modularity goal and the CLAUDE.md "pure logic separable from the runtime" standard.

## Folder map

| File | Covers | Serves components |
|---|---|---|
| `_Overview.md` | This file — strategy, scorecard, build order | all |
| `integration-map.md` | Per-component plug-in points and workarounds, in build order | all |
| `tmodloader-hook-surface.md` | Per-tick hooks; the public-hook vs internal-dispatch boundary | 1, 2 |
| `tmodloader-monomod-detours.md` | The `MonoModHooks` detour/IL-hook API; abort-clean surface | 1 |
| `tmodloader-lifecycle-and-loop.md` | Mod/world lifecycle, the per-tick sequence, the save-path gap | 2, 3, 5, 7 |
| `tmodloader-ui-system.md` | The overlay: keybind, layers, widget tree, custom drawing | 6 |
| `tmodloader-mod-identity.md` | Per-mod identity, the `Assembly→ModId` map, modlist fingerprint | 1, 2, 7 |
| `tmodloader-engagement-surfaces.md` | Engagement hooks, biome/boss/event detection, frame stats | 2, 4, 5 |

## Cross-cutting open items

Resolved by a reflection/metadata dump of `tModLoader.dll` (no decompiler), or empirically at the Milestone 0.A spike:

1. **`*Loader` dispatch shape + `HookList`** — the Lite-mode ILHook targets. The real visibility gap. Milestone 0.A. (`tmodloader-hook-surface.md`, `tmodloader-monomod-detours.md`)
2. **`ModLoader.Mods`** — loaded-mod enumeration. Public or internal? Metadata dump answers it. (`tmodloader-mod-identity.md`)
3. **`Main.SavePath`** — the writable data directory. Public or internal? Metadata dump. Platform-path fallback exists regardless. (`tmodloader-lifecycle-and-loop.md`)
4. **`MonoModHooks.Add`/`Modify` return types** — needed for deterministic detour teardown. Metadata dump. (`tmodloader-monomod-detours.md`)
5. **Vanilla `Zone*` field set, `Main.dust`, `active` flags** — documentation gaps; metadata dump confirms names. (`tmodloader-engagement-surfaces.md`)
6. **UI draw substrate** — `LegacyGameInterfaceLayer` ctor, `FontAssets`, the magic-pixel texture. Documentation gaps; metadata + ExampleMod. (`tmodloader-ui-system.md`)

## Notes for future sessions

- The per-slice docs cite tModLoader members by **fully-qualified name**, never line number — stable across tModLoader updates.
- This folder predates the Hook Interceptor implementation. The project CLAUDE.md anticipated a full `upkeep-context` pass *after* the hook architecture lands; that still holds — this folder is the pre-implementation reconnaissance, not the post-implementation memory.
- A design-wording correction surfaced: per-mod attribution does **not** come "for free via `MonoModHooks` ownership tracking" (as the README/design imply) — it comes from the profiler's own `MethodBase.DeclaringType.Assembly → Mod.Code` reflection. Same result, but the design wording should be corrected when next touched.
