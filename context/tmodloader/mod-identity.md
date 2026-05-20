# tModLoader Integration Surface — Mod Identity, Assemblies & Modlist

> Source: tModLoader.xml (tModLoader 1.4.4, build ~#5089). Serves: per-mod attribution (components 1,2), modlist fingerprint (component 7).

## Summary

The mod-identity primitives the profiler needs are split across two trust tiers. Per-mod *metadata* — internal name, display name, version, the compiled `Assembly`, the `TmodFile` — is fully and publicly documented on `Terraria.ModLoader.Mod`, and the `Mod.Code` property gives the exact `Assembly` object that `MethodBase.DeclaringType.Assembly` will return for that mod's code, so a one-time `Dictionary<Assembly, ModId>` is buildable. The blocker is *enumeration*: the documented public `ModLoader` surface only offers name-keyed lookups (`GetMod` / `TryGetMod` / `HasMod`) and exposes **no public member returning the set of currently-loaded mods**. The profiler therefore cannot build its lookup table without either an undocumented `ModLoader.Mods` member (likely exists, not in the XML) or an enumeration seam yet to be confirmed. Author/homepage metadata from `build.txt` is also not surfaced anywhere in the documented API.

## The surface

| Fully-qualified member | Kind | What it does / why the profiler cares |
|---|---|---|
| `Terraria.ModLoader.Mod` | T | Abstract base every mod overrides; the per-mod identity object. Each loaded mod is one instance. |
| `Terraria.ModLoader.Mod.Name` | P | Internal name — the mod's stable identification key. The fingerprint's `mod-name` field and the natural `ModId` primary key. |
| `Terraria.ModLoader.Mod.DisplayName` | P | Display name shown in the Mods menu. May contain chat tags. Retrospective-card label (raw). |
| `Terraria.ModLoader.Mod.DisplayNameClean` | P | `DisplayName` with chat tags stripped. The doc explicitly recommends it for logging, console output, and name-based search/filter — this is the correct card/overlay/`client.log` label. |
| `Terraria.ModLoader.Mod.Version` | P | `System.Version` of the mod build. The fingerprint's `version` field; retrospective version stamp. |
| `Terraria.ModLoader.Mod.TModLoaderVersion` | P | tModLoader version the mod was built against. Useful for abort-clean diagnostics, not for the fingerprint. |
| `Terraria.ModLoader.Mod.Code` | P | **The compiled `System.Reflection.Assembly` for this mod.** The keystone: this is the exact object `MethodBase.DeclaringType.Assembly` yields for the mod's own code, so it is the lookup-table key. |
| `Terraria.ModLoader.Mod.File` | P | The `TmodFile` created when tModLoader read the mod. Path into `.tmod` archive contents (e.g. `build.txt`, `description.txt`). |
| `Terraria.ModLoader.Mod.Side` | P | `ModSide` enum — client/server/both/no-sync. Diagnostic context, not fingerprint input (v1 is single-player). |
| `Terraria.ModLoader.Mod.SourceFolder` | P | Build-time source path. Dev-only; null/irrelevant for Workshop installs. |
| `Terraria.ModLoader.Mod.Logger` | P | The mod's `ILog`. Per the Dual-Surface rule, the profiler's *own* lifecycle/abort events go here; not a per-mod identity input. |
| `Terraria.ModLoader.ModLoader` | T | Central static mod-loading class. "Contains many static fields and methods related to mods" — but the documented public surface is thin (see below). |
| `Terraria.ModLoader.ModLoader.GetMod(System.String)` | M | Returns the `Mod` for a name; **throws `KeyNotFoundException`** if absent. Only for mods known-enabled. |
| `Terraria.ModLoader.ModLoader.TryGetMod(System.String,Terraria.ModLoader.Mod@)` | M | Safe name→`Mod` lookup, returns `bool`. The non-throwing form. |
| `Terraria.ModLoader.ModLoader.HasMod(System.String)` | M | Safe "is a mod with this internal name loaded" check. |
| `Terraria.ModLoader.ModLoader._enabledMods` | F | **Private.** Cached enabled-mod list mirroring `enabled.json`. Reflects *enabled* (not necessarily loaded/installed) mods. Not callable from mod code. |
| `Terraria.ModLoader.Core.AssemblyManager.GetLoadableTypes(System.Reflection.Assembly)` | M | Types loadable from an assembly; the doc-mandated replacement for `Assembly.GetTypes()` on `Mod.Code` (raw `GetTypes()` throws when a mod uses `ExtendsFromModAttribute`). Needed only if the profiler walks a mod's types, not for the `Assembly`→`ModId` map itself. |
| `Terraria.ModLoader.MonoModHooks.Add(System.Reflection.MethodBase,System.Delegate)` | M | Adds a detour. tModLoader tracks per-assembly detour ownership through this — the Hook Interceptor's install path. Not an identity *query* member. |
| `Terraria.ModLoader.MonoModHooks.Modify(System.Reflection.MethodBase,MonoMod.Cil.ILContext.Manipulator)` | M | Adds an IL hook. The Lite-mode per-loader-method ILHook install path. |
| `Terraria.ModLoader.BuildInfo.stableVersion` | F | Major.Minor of the stable tModLoader release at build time. tModLoader's own version, not a mod's. |
| `Terraria.ModLoader.Core.TmodFile` | T | The `.tmod` archive object behind `Mod.File`. Only `AddFile(string,byte[])` is documented — no documented public *read* accessor. |

## Plug-in points

### 1. Building the `Assembly → ModId` lookup `[partial]`

The lookup is, in principle, a one-time loop at world-load:

```
foreach (Mod mod in <enumeration of loaded mods>)
    map[mod.Code] = new ModId(mod.Name, mod.DisplayNameClean, mod.Version);
```

- **The value side is `[public-API]`.** `Mod.Code` (the `Assembly`), `Mod.Name`, `Mod.DisplayNameClean`, `Mod.Version` are all documented public properties. `Mod.Code` is exactly the `Assembly` instance `MethodBase.DeclaringType.Assembly` returns for that mod's code — no per-call reflection, the map is a plain dictionary keyed by reference identity.
- **The enumeration side is the gap, hence `[partial]`.** The documented public `ModLoader` surface offers *only* name-keyed lookups: `GetMod`, `TryGetMod`, `HasMod`. There is **no documented public member that returns the set of loaded `Mod` instances** — no `ModLoader.Mods` property, no `LoadedMods`, no `Mod[]`/`IEnumerable<Mod>` accessor anywhere in the XML. `_enabledMods` exists but is a private field. Without an enumeration source the profiler cannot discover the mods to put *in* the dictionary.

There is a public **non-mod-assembly fallback** worth recording: `MethodBase.DeclaringType.Assembly` will sometimes resolve to `Terraria` itself or a tModLoader assembly (vanilla code, the loader). Any assembly not present as a key in the map is correctly attributed to a single synthetic "tModLoader / vanilla" bucket — no documented API needed, this falls out of the dictionary miss.

> **`NEEDS DECOMPILER VERIFICATION`** — confirm the real name and accessibility of `ModLoader`'s loaded-mod collection. tModLoader source historically exposes `ModLoader.Mods` (a `Mod[]`); `GetMod`/`TryGetMod`/`HasMod` must read *some* internal collection, and the type doc explicitly says it "contains many static fields and methods related to mods" — but it is not in this XML. The profiler's table cannot be built until this is resolved. If `ModLoader.Mods` turns out to be `internal`, the loop must be sourced another way (see Open questions).

### 2. Per-mod display / author / version metadata `[partial]`

- **Display name + version: `[public-API]`.** `Mod.DisplayNameClean` is the card label (chat-tag-free, doc-endorsed for exactly this); `Mod.Version` is the version stamp.
- **Author: `[needs-internals]`.** No member on `Mod`, `ModLoader`, or `ModContent` exposes the author string. The retrospective card's `by Ozzatron` line (README "Cost Podium") has **no documented public source**. Author lives in each mod's `build.txt` (`author=`), which is packed inside the `.tmod` reachable via `Mod.File` (a `TmodFile`) — but `TmodFile` documents only `AddFile`, no public read API. The realistic path is reading `build.txt` bytes out of the `TmodFile`, which needs an internal `TmodFile` read accessor.
- **Homepage: `[needs-internals]`.** Same as author — `build.txt`'s `homepage=` field, no documented accessor.

> **`NEEDS DECOMPILER VERIFICATION`** — whether `TmodFile` (or a `BuildProperties`/`LocalMod` internal) exposes author/homepage/`build.txt` fields readably from mod code. Until confirmed, the card degrades gracefully: omit the `by <author>` line, or show `DisplayNameClean` only. This satisfies Invariant 3 (honesty) — better a missing field than a fabricated author.

### 3. Enumerating the enabled modlist for the fingerprint `[partial]`

The fingerprint is `hash(sorted (Name, Version) tuples of the enabled modlist)`.

- The **field values** — `Mod.Name`, `Mod.Version` — are `[public-API]`.
- The **enumeration** is the same gap as plug-in point 1: no documented public way to iterate the loaded/enabled set. `_enabledMods` is private.
- **Scope nuance the design should pin down:** the README says "sorted `mod-name + version` tuple of the **enabled** modlist". `_enabledMods` is described as *enabled but not necessarily loaded/installed*. The set the profiler can actually attribute cost to is the **loaded** set (mods with a live `Assembly`). For a single-player session these usually coincide, but a mod that is enabled-but-failed-to-load would differ. **Recommendation: fingerprint the *loaded* set, not the *enabled* set** — it is the set the engagement signal is actually about, and it is the set the plug-in-point-1 enumeration yields anyway. This keeps fingerprint and attribution sourced from one identical loop, which Invariant 3 favours (the fingerprint then provably matches the cost data it gates).

## Invariant checks

- **Invariant 1 — Read-only.** Every member in this slice is a getter or a name→object lookup. `Mod.Name/DisplayNameClean/Version/Code/File`, `ModLoader.GetMod/TryGetMod/HasMod`, `AssemblyManager.GetLoadableTypes` — all pure reads, zero mutation of game/mod state. The only mutating members named (`MonoModHooks.Add/Modify`) belong to the Hook Interceptor slice, not this one. This slice is read-only by construction.
- **Invariant 2 — Overhead budget.** The design is sound: the `Dictionary<Assembly, ModId>` is built **once at world-load** (`ModSystem.OnWorldLoad`) and the per-tick hot path does a single reference-keyed dictionary `TryGetValue` — no reflection, no allocation, no boxing. `MethodBase.DeclaringType.Assembly` itself is a cheap property read on the already-resolved `MethodBase`. The one caution: do **not** call `AssemblyManager.GetLoadableTypes` or any `Mod.Find`/`GetContent` per-tick — those are world-load-only. The hot-path cost of this slice is one dictionary probe; well inside the Lite < 1% budget.
- **Invariant 4 — Abort-clean.** The enumeration gap *is* the abort-clean trigger for this slice. The profiler must resolve the loaded-mod collection through one guarded accessor at world-load; if that accessor's signature no longer matches across a tML update (the `[needs-internals]` risk), the profiler **disables instrumentation and reports via `Mod.Logger`** rather than proceeding with a half-built map. A half-built `Assembly→ModId` map would misattribute cost — silently wrong numbers are worse than no numbers, and Invariant 3 forbids shipping them. `GetMod` throwing `KeyNotFoundException` must never be used on the hot path; use `TryGetMod` and treat a miss as the tModLoader/vanilla bucket.

## Coverage verdict

| Capability | Buildable on documented public API? |
|---|---|
| `Assembly → ModId` map *values* (name, display, version, assembly) | **Yes** — fully public |
| `Assembly → ModId` map *enumeration* (the loop source) | **No** — no documented public mod-enumeration member |
| Modlist fingerprint *field values* | **Yes** — `Mod.Name`, `Mod.Version` |
| Modlist fingerprint *enumeration* | **No** — same gap |
| Per-mod author / homepage for the card | **No** — `build.txt` fields, no documented accessor |
| Hot-path attribution (one-time map, per-tick probe) | **Yes** — pattern needs no further API |

**Verdict: the slice is one accessor away from fully public.** Everything the profiler needs to *describe* a mod once it has the `Mod` instance is documented public API. The entire slice hinges on a single missing primitive — a way to enumerate the loaded `Mod` instances — plus the secondary, non-blocking gap of `build.txt`-derived author/homepage. The `MethodBase.DeclaringType.Assembly → Mod.Code` mapping strategy is **sound and confirmed**: `Mod.Code` is documented as the mod's compiled assembly, so the dictionary key is correct and the one-time-build / per-tick-probe shape satisfies Invariant 2. If `ModLoader` turns out to expose loaded mods publicly (the type doc strongly implies a richer surface than the XML shows), this slice is **100% public-API**. If that enumeration is `internal`, this slice is **`[partial]`** and the abort-clean guard around that one internal accessor becomes load-bearing.

## Open questions / NEEDS DECOMPILER VERIFICATION

1. **`NEEDS DECOMPILER VERIFICATION` — the loaded-mod enumeration.** What public/internal member of `Terraria.ModLoader.ModLoader` returns all loaded mods? Historically `ModLoader.Mods` (`Mod[]`). Confirm its exact name, type, and accessibility. This is the single blocker for both plug-in point 1 and 3.
2. **`NEEDS DECOMPILER VERIFICATION` — `TmodFile` read surface.** Can mod code read `build.txt` (author, homepage, version, side) bytes from a `Mod.File` `TmodFile`? Only `AddFile` is documented. Check for an internal `GetBytes`/`GetStream` on `TmodFile` and for a `BuildProperties`/`LocalMod` type that already parses `build.txt`.
3. **`NEEDS DECOMPILER VERIFICATION` — sub-mod / weak-reference assemblies.** A mod using `ExtendsFromModAttribute` weakly references another mod; `Mod.Code` is described as one assembly per mod, but confirm there is exactly one `Assembly` per `Mod` (no satellite/companion assemblies). If a mod can own more than one assembly, the `Dictionary<Assembly,ModId>` must map *every* assembly the mod owns, or that mod's cost is misattributed.
4. **`NEEDS DECOMPILER VERIFICATION` — non-mod assembly identity.** Confirm the assembly objects for vanilla `Terraria` code and for tModLoader's own loader code, so the "everything not in the map = tModLoader/vanilla" fallback bucket is correctly labelled rather than lumping loader overhead under a misleading name.
5. **Design clarification (not a decompiler question) — enabled vs loaded set for the fingerprint.** The README says "enabled modlist"; the only enabled-set primitive (`_enabledMods`) is private and includes not-loaded mods. Recommendation in plug-in point 3: fingerprint the *loaded* set so fingerprint and attribution share one source loop. Confirm with the design pitch before Milestone 3 (Persistent Store) commits the schema.
6. **`Mod.Code` lifetime.** Confirm the `Assembly` from `Mod.Code` is stable for the whole `world-load → save-and-exit` session and is not swapped on a `Mods → Reload`. The map is rebuilt every `OnWorldLoad`, so this is low-risk, but worth confirming the assembly is not recycled mid-session.
