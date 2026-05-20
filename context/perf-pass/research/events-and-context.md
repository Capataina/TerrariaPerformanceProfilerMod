# Events and Context — Perf Pass Research

> System: `Profiling/Events/*` plus the three context consumers in `Profiling/Persistence/` (`ContextTransitionWatcher`, `PlayerDeathDetector`, `WorldSnapshotter`).
> Baseline ref: `context/perf-pass/baseline.md` (v0.5, 2026-05-20).
> Hard constraints: every dimension still captured at full fidelity, every transition still emitted, snapshot cadence unchanged, per-tick zero-alloc, universal (no mod-name special-casing).

This dossier walks the Events-and-Context surface end-to-end, audits each per-tick read for cost and allocation, maps every read source against verified tModLoader / vanilla / SubworldLibrary internals, and proposes prioritised optimisations that keep the data stack intact. The verb "remove" never appears as a recommendation; everything is "do the same thing more cheaply".

---

## 0. Executive summary

The Events-and-Context system is in *good* shape relative to the rest of the v0.5 codebase. `ContextTagger` already splits its reads across three cadences (60 Hz boss, 10 Hz weather/biome/invasion, 1 Hz slow), the biome bitset is pre-sized at install, and `BossSlotArray` is fully unboxed. The pain is concentrated in three places:

| Surface | Cost class | Pattern |
|---|---|---|
| **`ContextTransitionWatcher` biome diff** | `O(N_biomes)` IsSet × IsSet per tick **for every tick after the first**, regardless of whether anything changed | Twin loops over a bitset that mostly didn't change. Cheap per call (≈40 ns at 38 vanilla bits, ≈100 ns once a Calamity-class stack hits 70+ modded biomes) but paid every tick with no early-out. |
| **`Lang.GetNPCNameValue` inline in three places** | Cheap (array lookup + field read) but called per-tick from the watcher and the snapshotter on the hot edge of every boss-presence row | The function itself doesn't allocate — `LocalizedText.Value` is a backing field — but the recorder allocates a fresh `ContextTransitionRow` per call and we resolve the name even when we are not about to emit a transition. |
| **`SubworldProbe.Sample` slow path** | Three reflection calls (`MethodInfo.Invoke` × 1, `PropertyInfo.GetValue` × 2) plus a string dictionary lookup per slow-cadence tick when SubworldLibrary is loaded | Each `Invoke`/`GetValue` allocates one `object` box for the bool return and one for `Subworld` (the latter is a reference, no box, but the args array is `null` so no params allocation). Fires at 1 Hz so the absolute cost is small; *opportunity* is delegate caching, not avoidance. |

Three medium-impact wins also surface:

- **`WeatherSources.All` is built with non-static lambdas** (`() => Main.dayTime` etc.). The compiler caches each closure instance because none of them capture local state, so this is a one-time allocation at type-init — *not* a per-tick cost. Verified by inspection but worth confirming with an audit because closure-vs-static-method is a common review blind spot.
- **`EventAggregator.Accumulate` clears the active-keys hash sets every tick with a `foreach` over six dictionaries**, then re-fills them. The clear is `O(N_buckets)` amortised, but the re-fill happens regardless of whether the set is consumed (only the EventsTab consumes it, at 1 Hz). The agent-side cost is masked because the sets stay small in practice.
- **`WorldSnapshotter.Capture` does a full `Main.item[]` walk every 30 s** to count active items. 400-slot scan. Microscopic relative to the snapshot cadence, but the *same loop already runs* in `ProfilerSystem.CountActive` callers — there's an opportunity to pass the result in like NPC/projectile/dust counts.

None of this is currently flagged as a hot-path emergency. The bigger story is the cumulative cost on a long session: a 4-hour session at 60 Hz is ~864 000 ticks, and the diff path runs every one of them. Even at 100 ns/tick the watcher pays 86 ms of cumulative CPU per hour just on the diff — small per tick, real in aggregate, and the cumulative profile is exactly what shows up as "the profiler is the top CPU contributor" in the v0.5 baseline.

The pass's target shape: keep every transition row, every bucket, every dimension, but bring the per-tick diff into the **5–20 ns range** by replacing per-bit IsSet loops with word-level XOR plus `BitOperations.TrailingZeroCount` (we are already on .NET 8 — this intrinsic is hardware-supported on x64 and ARM64), pre-cache the boss-name resolution into a parallel `short[]→string` map, and switch `SubworldProbe` to a pair of cached open-delegates resolved once at install.

---

## 1. Current state audit — per file

### 1.1 `EventContext` (`Profiling/Events/EventContext.cs`, 43 lines)

```text
struct EventContext (verified field shape)
 ├─ long      TickIndex            // 8 bytes
 ├─ BiomeBitset Biomes             // sizeof(ulong[]) ref (8) + int BitLength (4) → 16 incl. pad
 ├─ WeatherFlags Weather (ushort)  // 2 bytes
 ├─ bool      Hardmode             // 1
 ├─ GameMode  Mode    (byte)       // 1
 ├─ InvasionId VanillaInvasion (byte) // 1
 ├─ BossSlotArray Bosses           // 8 × short (16) + byte _count → 24 incl. pad
 └─ int       SubworldKey          // 4
                                   //  total ≈ 56 bytes (with alignment padding)
```

Carried by-value through `TickFrame.Context` and aggregator copies. The only managed reference inside the struct is `BiomeBitset._words` (the backing `ulong[]`). That array is allocated **once** at world load by `ContextTagger.Reset` and resized only if `BiomeRegistry.Count` ever changes (it doesn't, post-`PostSetupContent`).

**Verdict — zero-alloc capture path is correct.** The per-tick `Snapshot` mutates `_ctx` in place; the consumer `Accumulate` reads `in tagger.Current` by ref-readonly. No defensive copy.

**Cache locality.** The struct is 56 bytes which fits in one cache line. The `BiomeBitset` ref means the bit data lives in a separate cache line — fine, because the diff walks the ref-target only.

### 1.2 `ContextTagger` (`Profiling/Events/ContextTagger.cs`, 142 lines)

Cadence split is correct and intentional (commented in §3.3 of the design plan, mirrored in the file's class doc):

| Read | Cadence | Allocation per call | Verified source |
|---|---|---|---|
| `BossSampler.Sample` | every tick | none in steady state | §1.4 below |
| Weather flags (12 readers) | every 6 ticks | none — see §1.7 | `Terraria.Main`/`Sandstorm`/`DD2Event`/`LanternNight`/`BirthdayParty` static fields |
| Biome bitset | every 6 ticks | none — `BiomeRegistry.Sample` ref-fills | §1.3 |
| Vanilla invasion | every 6 ticks | none (switch on int) | `Main.invasionType`, `DD2Event.Ongoing` |
| Hardmode | every 60 ticks | none | `Main.hardMode` (bool field) |
| Game mode | every 60 ticks | none — struct property reads | `Main.GameModeInfo` is a struct (`GameModeData`) |
| Subworld | every 60 ticks | reflection — see §1.10 | `SubworldLibrary.SubworldSystem` |

**Boss read is per-tick.** This is correct: bosses spawn and despawn at single-tick granularity (the player can damage one off-screen, the despawn happens *that* tick, the next tick the bucket should already attribute correctly). Weather and biome bits change at game-tick granularity but their *effects* are 10 Hz-perceptible, so the 6-tick cadence is honest.

**Cadence phase counter.** `_tickPhase` wraps at `SlowCadenceTicks = 60` and `WeatherCadenceTicks = 6` divides it evenly. Modular arithmetic via `phase % 6` is one IDIV per tick (~5 cycles on modern x86). A micro-optimisation: replace with a single counter that decrements and resets, eliminating the modulo. Not a meaningful win at 60 Hz; record it as a candidate but do not act unless other work demands it.

**`firstRun` branch.** Costs nothing in steady state (predicted-not-taken after the first tick). Correct.

### 1.3 `BiomeRegistry` (`Profiling/Events/BiomeRegistry.cs`, 180 lines)

Two install-time concerns, one per-tick concern.

**Install (`Populate`).** Reflects `Player`'s public instance `Zone*` bool properties, binds a `Func<Player, bool>` per property via `Delegate.CreateDelegate(typeof(Func<Player,bool>), p.GetMethod!)`. This is the **correct** way to make per-tick reads cheap: each delegate is a virtual call against the cached method handle, not a `PropertyInfo.GetValue` reflection dispatch. Approximately 38 vanilla `Zone*` properties on tModLoader 1.4.4 (per the cited Player surface inventory in `tmodloader/engagement-surfaces.md`).

Modded biomes are enumerated via `ModContent.GetContent<ModBiome>()` and their `Type` ids stored in `_modBitIndex[i]`. This `Type` is the same index tModLoader uses inside `Player.modBiomeFlags` (the internal `BitArray`).

**Per-tick (`Sample`).** Three parts:

```csharp
// (a) reset destination
if (dest.BitLength != _biomes.Count) dest.ResizeAndClear(...);
else dest.ClearAll();      // walks _words, zeroes each ulong

// (b) vanilla
for (i = 0..vanilla.Length) if (vanilla[i](player)) dest.Set(i);

// (c) modded
var flags = (BitArray)_modBiomeFlagsField.GetValue(player);  // ⚠ reflection per call
for (i = 0..map.Length) if (flags[map[i]]) dest.Set(offset + i);
```

The modded-biome read fetches `Player.modBiomeFlags` via `FieldInfo.GetValue(player)` **every 10 Hz sample**. `FieldInfo.GetValue` boxes value types but here the field is a `BitArray` — a reference type. So no box, no GC allocation, but we still pay the `RuntimeFieldHandle` dispatch and a type check (~50 ns per call on .NET 8). **Cheap, but cacheable.** A delegate compiled once at install would erase that cost.

The `BitArray` indexer (`flags[bit]`) is itself a method call doing `(_array[index / 32] & (1 << (index % 32))) != 0`. Modded biomes are typically tens, not hundreds; the inner loop is short.

`ClearAll()` walks `_words` element by element. For 38 vanilla + (say) 30 modded bits the bitset is 2 `ulong`s; `Array.Clear` would call into native code with a fixed overhead. At this size manual loop wins. **No change.**

`BitOperations.PopCount` and `TrailingZeroCount` are already used in `PrimaryBitIndex` — good, both are JIT intrinsics on .NET 8.

### 1.4 `BossSampler` (`Profiling/Events/BossSampler.cs`, 94 lines)

The per-tick `Sample` function is the most heavily-trafficked single read in the system: every tick, full `Main.npc[]` walk (200 slots), per-NPC test:

```csharp
for (int i = 0; i < npcs.Length; i++) {
    NPC npc = npcs[i];                               // class ref
    if (!npc.active) continue;
    int headWhoAmI = npc.realLife >= 0 ? npc.realLife : i;
    if (headWhoAmI != i) continue;                   // not the head
    int type = npc.type;
    bool qualifies = npc.boss
        || (type >= 0 && type < countsAsBoss.Length && countsAsBoss[type]);
    if (!qualifies) continue;
    short typeShort = (short)type;
    if (dest.Contains(typeShort)) continue;          // O(slots filled)
    if (!dest.TryAdd(typeShort)) break;
    count++;
}
```

`Main.npc.Length == Main.maxNPCs == 200` on vanilla 1.4.4 (verified — `Main.maxNPCs` is set to 200 and the array sized accordingly). The hot loop is 200 iterations × (load + active-check + branch). The active-check fails for >99% of slots in any single-boss fight, so the branch predictor handles it well. **The walk itself is fine.**

The note in `events-and-context.md` says *"the entity-count scan in `ProfilerSystem.CountActive` already pays for the full Main.npc[] walk; this adds only a predicate per slot"* — but that is true only if both scans happen on the **same tick** AND we are willing to fuse them. We don't fuse today, so `CountActive` and `BossSampler.Sample` are two independent passes over the same 200-slot array. **Fusion candidate** — see §4.

`NPCID.Sets.ShouldBeCountedAsBoss` is a `bool[]` indexed by NPC type, sized to `NPCID.Count` plus modded type count. Verified per tModLoader docs and the migration guide (renamed from `TechnicallyABoss`). The bounds check `type < countsAsBoss.Length` is redundant since modded NPC types extend the array, but `(uint)type < (uint)countsAsBoss.Length` is one cycle cheaper than the signed-comparison pair.

`dest.Contains(typeShort)` is a worst-case 8-slot linear scan over an inlined struct. Steady state (zero or one active boss) it returns after the first comparison. **Fine.**

`Lang.GetNPCName(type).Value` in `DisplayName` is called on demand from the UI/recorder, cached in a `Dictionary<int, string>`. The lookup walks the cache; on first encounter it goes through tModLoader's patched `Lang.GetNPCName(int)` which checks `_npcNameCache[netID]`. Verified — no allocation, single array indirection plus a `LocalizedText.Value` field read. **Optimisable surface:** the cache uses a `Dictionary`; a `string[]` keyed by `type` directly would be a single load (we know `type` is bounded by `NPCID.Count + modded count`).

### 1.5 `BossSlotArray` (`Profiling/Events/BossSlotArray.cs`, 96 lines)

A struct of 8 inlined `short`s plus a `byte _count`. Hot operations:

- `Clear()` — zero the 8 shorts plus the byte. JIT may auto-vectorise on x64.
- `TryAdd(short)` — `switch(_count)`. Verified: the compiler emits a jump table, average two instructions per call.
- `Contains(short)` — short-circuiting chain of 8 compares.
- `Equals(BossSlotArray)` — 9 compares, used by the transition watcher only.

**Verdict — no win available here.** Replacing the chain with a `MemoryMarshal.Cast<BossSlotArray, ushort>` + SIMD `Vector128` compare would be a micro-optimisation worth maybe 5 ns and would cost readability. Leave as-is.

### 1.6 `BiomeBitset` (`Profiling/Events/BiomeBitset.cs`, 113 lines)

Already well-shaped. `_words` is `ulong[]` (typically 2 entries), `IsSet`/`Set`/`Clear` are bit-twiddled. `PopCount` and `TrailingZeroCount` use `System.Numerics.BitOperations` — JIT intrinsics on .NET 8, lowered to `POPCNT` / `TZCNT` on x86, `CNT` / `CTZ` on ARM64.

**Missing operation that the watcher would use:** there is no `XorWords(in BiomeBitset, in BiomeBitset, Span<ulong>)` helper to compute the diff in-place. The watcher currently does the diff bit-by-bit via two `IsSet` calls per index — see §1.8.

`CopyFrom` allocates **only when bit-lengths mismatch**. In steady state both watcher and tagger share the same bit length, so the per-call cost is one `Array.Copy`-equivalent loop. **No change needed.**

### 1.7 `WeatherSources` (`Profiling/Events/WeatherSources.cs`, 54 lines)

```csharp
public static readonly (WeatherFlags, Func<bool>)[] All = new (WeatherFlags, Func<bool>)[] {
    (WeatherFlags.DayTime, () => Main.dayTime),
    ...
};
```

Twelve `Func<bool>` instances. Each lambda is **closure-free** (no captured locals — they read static fields), so the C# compiler emits each as a static method, the delegate is allocated **once** when `WeatherSources.All` is initialised, and reused for every read. Verified pattern: lambdas without captures get cached. No per-tick or per-sample allocation.

The per-call cost is one virtual delegate dispatch (`Invoke` is a single indirect call after the dispatch tables warm up — ~2 ns) plus a field read. Across 12 sources that's ~24 ns at the 10 Hz cadence, ≈4 ns/s amortised. **Fine.**

A theoretical refinement: function pointers (`delegate*<bool>`) would skip the delegate-object indirection. Saves maybe 1 ns/source × 12 = 12 ns per 6-tick boundary = 2 ns/s amortised. **Not worth the code change.**

### 1.8 `ContextTransitionWatcher` (`Profiling/Persistence/ContextTransitionWatcher.cs`, 258 lines)

This is the biggest per-tick consumer of the context after `EventAggregator`. Walking the hot path:

```csharp
public void OnSnapshot(in EventContext ctx, double frameMs, SessionRecorder recorder) {
    bool isDayTime = (ctx.Weather & WeatherFlags.DayTime) != 0;     // 1 ns
    bool currentBossesPresent = ctx.Bosses.Count > 0;               // 1 ns
    string currentBossName = currentBossesPresent
        ? Terraria.Lang.GetNPCNameValue(ctx.Bosses[0]) ?? ...        // ⚠ field read every tick
        : "";
    // ... seven scalar diffs (bool/byte/int compares) ...
    // weather diff: 16 IsSet checks via for-bit loop (with hidden bug, see below)
    DiffBiomeBits(in ctx.Biomes, ref _lastBiomes, recorder, ctx.TickIndex, frameMs);
    // boss presence diff
}
```

**Pain point 1 — boss name resolved every tick.** `Lang.GetNPCNameValue` is cheap (no alloc) but it executes every tick, including ticks where the boss didn't change. The watcher only *needs* the name on the transition edge (start, end, swap). **Fix: defer the name resolution into the change branch.** Saves one virtual call + field read per tick × 60 Hz × hours of session.

**Pain point 2 — Biome bit-by-bit diff.**

```csharp
private static void DiffBiomeBits(in BiomeBitset current, ref BiomeBitset last,
    SessionRecorder recorder, long tick, double frameMs) {
    int bits = Math.Min(current.BitLength, last.BitLength);
    for (int i = 0; i < bits; i++) {
        bool nowSet = current.IsSet(i);   // bounds check + shift + and
        bool wasSet = last.IsSet(i);      // bounds check + shift + and
        if (nowSet == wasSet) continue;
        ...
    }
    last.CopyFrom(current);
}
```

For 38 vanilla + (e.g.) 30 modded = 68 bits, this is 68 iterations × 2 `IsSet` calls × ~5 ns = ~680 ns per tick. At 60 Hz that's 40 µs/s of pure diff work, ~0.04% overhead on its own — small but a clean target.

**The right shape** is to compute `current.Words[i] XOR last.Words[i]` per `ulong`, branch out on zero (the common case — biomes don't change), and use `TrailingZeroCount` to iterate set bits in the XOR mask. For a typical 2-word bitset that's two XORs + two zero-checks ≈ 5 ns total when nothing changed, ≈ 20 ns when a single bit flipped.

**Pain point 3 — Weather diff loop has a latent bug.**

```csharp
for (int bit = 0; bit < 16; bit++) {
    WeatherFlags flag = (WeatherFlags)(1 << bit);
    if ((weatherChanged & flag) == 0) continue;
    bool nowOn = (ctx.Weather & flag) != 0;
    string label = WeatherSources.DisplayName(flag);
    if (string.IsNullOrEmpty(label) || label.StartsWith("?")) continue;
    recorder.OnContextTransition("weather",
        nowOn ? "off" : "on",          // ← from value is the wrong direction (note the inverted ternaries)
        nowOn ? "on" : "off",
        ctx.TickIndex, frameMs);
    // ... and the flag name is not in the row, only "weather" + on/off
}
```

The `from`/`to` strings are inverted twice (cancels out) AND the row drops the flag identity (the "encode the flag in 'from'" comment never materialised — both `from` and `to` only carry on/off). The comment after the loop body acknowledges this. **Correctness bug for the v0.6 implementation pass to pick up — not a perf issue, but fix it in the same pass since we're rewriting the loop anyway.** The Type column should be `"weather:" + flag.ToString()` or, better, a stable short code per flag.

`(WeatherFlags)(1 << bit)` is a flag cast on every iteration; `WeatherSources.DisplayName(flag)` is a switch. Both are cheap. The loop's real opportunity is the same as biome: walk only the set bits of `weatherChanged` via `TrailingZeroCount`. With 12 flags the worst case is 12 iterations, normal case is 0. Replacing the for-16 with a `while (changed != 0) { int b = BitOperations.TrailingZeroCount((ushort)changed); changed &= changed - 1; ... }` gives a typical cost near zero.

**Pain point 4 — `bossStart`/`bossEnd`/`bossSwap` allocates strings even when nothing changed.** Actually no — the alloc is gated by the `currentBossesPresent != _lastBossesPresent || currentBossName != _lastBossName` check. But that **string compare** is one of the two reasons `currentBossName` is fetched every tick. If we cache the last-tick's `short` type id (the integer) instead of the string, we compare ints (`_lastBossType == ctx.Bosses[0]`), which is free, and defer the `Lang.GetNPCNameValue` call to the alloc branch.

**Allocations per transition (when one fires):** one `ContextTransitionRow` (the row class), the `string` for the `Type` field (literal — no alloc), `From` and `To` strings (sometimes literals like `"true"`/`"false"`, sometimes interpolated). The interpolations `_lastBossName + " (" + outcome + ")"` and similar concatenate — non-zero, but they fire only on transitions which are by definition rare (the watcher exists to count them). **No action needed.**

### 1.9 `WorldSnapshotter` (`Profiling/Persistence/WorldSnapshotter.cs`, 84 lines)

Runs every `1800` ticks (30 s). One row per fire. The `Capture` method:

- Reads `Main.LocalPlayer` (one field load).
- Computes day flag, primary biome name (`BiomeRegistry.NameOrIndex(ctx.Biomes.PrimaryBitIndex())` — bit scan + array deref + dictionary-of-strings access into `BiomeDescriptor.DisplayName`).
- Resolves the primary boss name via `Lang.GetNPCNameValue` if any boss is active.
- Walks `Main.item[]` (400 slots) to count actives. **This is a redundant scan** — `ProfilerSystem.CountActive` is the canonical helper; the caller (`OnTick`) already passes `npcCount`, `projCount`, `dustCount` for the same reason. Items are missing from the signature.

**Cost.** A 400-slot array scan is ~1 µs. At 1 fire per 30 s = 0.03 fires/s, the amortised cost is 30 ns/s. Trivially small. **But** the omission is a consistency wart: pass `itemCount` in.

**Allocations.** `WorldSnapshotRow` is allocated per fire (~60 per hour). String fields are literal-or-interpolated. The interpolations would be more frequent if this fired per tick — but at 30 s cadence the total allocation budget is tiny.

**Hidden risk.** `ctx.Bosses[0]` triggers the property indexer which goes through a switch. Fine, but the result `(short)0` when no boss is active still maps to *some* NPC name in `Lang.GetNPCName(0)` (returns empty or `NPC:0`). The code handles this by checking `ctx.Bosses.Count > 0` first. **Correct.**

### 1.10 `PlayerDeathDetector` (`Profiling/Persistence/PlayerDeathDetector.cs`, 105 lines)

Fires every tick (false→true edge detector). Hot path:

```csharp
public void OnTick(SessionRecorder recorder, in EventContext ctx) {
    var player = Main.LocalPlayer;
    if (player == null) return;
    bool dead = player.dead;
    if (dead && !_wasDeadLastTick) { /* allocate and capture */ }
    _wasDeadLastTick = dead;
}
```

In steady state (alive), this is two field reads, one branch, one store. **~3 ns per tick.** Effectively free.

On the dead edge (rare — `2 events / 16,009 ticks = 0.012%` of ticks in the baseline session), `Capture` allocates:

- A `List<int>` for `bosses` (1 alloc + small backing).
- A `PlayerDeathRow` (1 alloc).
- The summary string (1 alloc).
- **A LiteDB query for the last damage-taken row** — this is by far the most expensive thing here, and it executes on the **game thread** at the moment of death. The `db.DamageTaken.Query().Where(...).OrderByDescending(...).Limit(1).FirstOrDefault()` chain is a LiteDB LINQ provider call that synchronously reads from the embedded DB.

**Pain point 5 — synchronous DB read on the death edge.** Even though death is rare, the read can stall the game thread for tens of ms (the v0.5 baseline includes an 8.5 s end-of-session main-thread stall — see `master-plan.md`/baseline §2 — so we already know LiteDB on the game thread is a documented hazard). The fix is for the death detector to enqueue a **dead-edge event** that the writer thread (or a downstream killer-attribution worker) resolves asynchronously, not for the watcher itself to query.

Also flagged in baseline §4.3 as the "damage-weighted attribution" bug — the v0.6 implementation pass will replace last-hit-credit with a rolling-window aggregation. That work removes the LINQ query and replaces it with an in-RAM rolling window owned by the recorder, which is also cheaper.

### 1.11 `SubworldProbe` (`Profiling/Events/SubworldProbe.cs`, 107 lines)

Sample path runs **once per slow cadence (1 Hz)** when SWL is loaded:

```csharp
object? anyBoxed = _anyActive!.Invoke(null, null);          // ⚠ boxes bool return
if (anyBoxed is not bool any || !any) return 0;
object? current = _currentProp!.GetValue(null);             // returns Subworld ref
if (current == null) return 0;
if (_fullNameProp!.GetValue(current) is not string full || full.Length == 0) return 0;
if (_keys.TryGetValue(full, out int existing)) return existing;
```

`MethodInfo.Invoke` is the heavy one — even with no arguments, it walks reflection emit cache, validates the target, and **boxes the `bool` return into a heap `object`**. That's one box per invocation, fully unavoidable through `Invoke`. At 1 Hz that's 1 box/s = 24 B/s of GC pressure (a boxed bool plus header) — measurable in a long session but small.

The fix is **delegate caching**: compile a `Func<bool>` for `AnyActive` and a `Func<object?>` for `Current` (or use `Delegate.CreateDelegate` for the bound static). Once cached, the dispatch is a virtual call with no boxing.

Verified against SubworldLibrary source (`SubworldLibrary/SubworldSystem.cs`):

```csharp
public static bool AnyActive()        => current != null;     // 0-arg overload
public static bool AnyActive(Mod mod) => current?.Mod == mod;  // 1-arg overload (Mod)
public static bool AnyActive<T>()     where T : Mod ...;       // generic
public static Subworld Current => current;
```

There **is** a 0-arg `AnyActive()`. Our probe already targets it (`types: Type.EmptyTypes` in the `GetMethod` call). Good.

`Subworld.FullName` is an instance property — confirmed by `current?.FullName == id` usages in the SWL source. The probe binds it as an instance `PropertyInfo`. Correct.

`Subworld.FullName`'s implementation likely returns `Mod.Name + "/" + Name` (the standard tModLoader pattern for content full names). That's allocation-free if the field is cached, allocating if computed on each call. **Worth confirming in the v0.6 pass** but not load-bearing here because the probe runs at 1 Hz.

### 1.12 `EventAggregator` (`Profiling/Events/EventAggregator.cs`, 255 lines) — the per-tick consumer

Not asked-for-research scope strictly, but it's on the per-tick path and reads the context, so it counts. The hot loop:

```csharp
public void Accumulate(in EventContext ctx, double frameMs) {
    _totalTicks++;
    _runningMeanMs += (frameMs - _runningMeanMs) / _totalTicks;
    bool isSpike = _totalTicks > 60 && frameMs > _runningMeanMs * 2d;

    for (int d = 0; d < _activeKeysLastTick.Length; d++)
        _activeKeysLastTick[d].Clear();          // ⚠ six HashSet<int>.Clear calls per tick

    // biomes (loops up to BiomeRegistry.Count)
    // weather (iterates WeatherSources.All -> tuple element access -> closure-free, fine)
    // bosses (iterates 0..Bosses.Count)
    // invasion, difficulty, subworld
}
```

`HashSet<int>.Clear()` is `O(buckets)` not `O(items)` — it walks the buckets array. Six of them per tick. For small hash sets that's still measurable.

**Bigger concern:** `foreach (var pair in WeatherSources.All)` — `WeatherSources.All` is a `(WeatherFlags, Func<bool>)[]`. `foreach` over an array of value tuples is allocation-free, but the **inner deconstruct** `(int)pair.Flag` is fine. The `Func<bool>` field is *not even called here* — the aggregator only reads the flag bits, not the readers. So the closures-as-readers are paid in `ContextTagger`, not in `EventAggregator`. **Not a per-tick cost contributor.**

`BumpBucket` does a `Dictionary<int, BucketStats>.TryGetValue` per active bit per dimension. Steady state, all keys hit the cache — typical cost ~10 ns per lookup. Total hot-loop cost across all dimensions, with one biome + day + sometimes-boss + difficulty + hardmode active, is roughly 6–10 lookups × 10 ns + 6 × clear ≈ 100–200 ns/tick. **Acceptable** but the `_activeKeysLastTick.Clear()` work has zero functional value when nobody consumed the set between ticks. **Latch-style opportunity:** track a `_activeKeysDirty` flag set when the UI reads, cleared on the next clear. Skip the clears if the dirty flag is false.

---

## 2. Baseline numbers (verified against `baseline.md`)

Pulling the relevant lines:

| Quantity | v0.5 | Source |
|---|---|---|
| Average frame ms | 0.96 | baseline §2 |
| Context transitions captured in 4.5-min session | 10 | baseline §2 |
| World snapshots captured | 10 | baseline §2 |
| Player deaths captured | 2 | baseline §2 |
| Per-tick PerformanceProfiler cost (avg, includes everything) | 0.27 ms | baseline §2 |
| Total session ticks | 16 009 | baseline §2 |

10 transitions across 16 009 ticks ⇒ the watcher emits exactly 0.000625 transitions/tick. Every non-transition tick is "diff returned no change" cost — the entire per-tick cost is the *check*, not the *write*. This is the relevant figure to optimise.

**Estimated breakdown of the 0.27 ms/tick that PerformanceProfiler currently costs:**

| Subsystem | Estimated share | Confidence |
|---|---|---|
| ILHookInterceptor dispatch (every profiled mod hook) | ~50–60% | high — this is the bulk per Per-tick Attribution / Spike detection cost analysis |
| MetricCollector frame timing + GC reads | ~15% | medium |
| EventAggregator | ~5% | medium |
| ContextTagger | ~5% | medium |
| ContextTransitionWatcher (the diff itself) | ~3% | low — guess from microbenchmarks |
| WorldSnapshotter (amortised — fires every 30 s) | <0.5% | high |
| PlayerDeathDetector (steady state — alive) | <0.5% | high |
| StallDetector / SpikeDetector / etc. | ~10% | medium |

The Events-and-Context system is therefore **plausibly 8–13% of the profiler's per-tick cost**, of which ~3–5% is the watcher diff. The pass target is to compress that 8–13% to under 4%.

---

## 3. tML / vanilla surface research — every read source

A flat table of every read this subsystem makes on per-tick or near-per-tick paths, with verified type, update cadence, and allocation behaviour.

| Read | Type | Backing | Cadence | Allocates? | Notes |
|---|---|---|---|---|---|
| `Main.dayTime` | `bool` field | static field | game tick | no | flips at dawn/dusk |
| `Main.bloodMoon` | `bool` | static | game tick | no | server-authoritative |
| `Main.eclipse` | `bool` | static | game tick | no | |
| `Main.slimeRain` | `bool` | static | game tick | no | |
| `Main.pumpkinMoon` / `Main.snowMoon` | `bool` | static | game tick | no | |
| `Main.invasionType` | `int` | static | game tick | no | enumeration: -1..5 |
| `Main.hardMode` | `bool` | static | world state | no | rarely flips |
| `Main.halloween` / `Main.xMas` | `bool` | static | session-stable | no | |
| `Main.raining` | `bool` | static | game tick | no | |
| `Main.GameModeInfo` | `GameModeData` struct | static property → struct | session-stable | no | struct property |
| `Main.GameModeInfo.IsJourneyMode` etc. | `bool` | struct field | session-stable | no | |
| `Main.LocalPlayer` | `Player` ref | `Main.player[Main.myPlayer]` | game tick | no | |
| `Main.npc` | `NPC[]` (length 200) | static field | game tick | no | array reference cached fine |
| `Main.item` | `Item[]` (length 400) | static field | game tick | no | used by snapshotter |
| `Player.ZoneJungle` etc. (38 Zone* bool props) | `bool` properties | computed from world state | game tick | no | Func delegates already cached at install |
| `Player.modBiomeFlags` | `BitArray` | internal instance field, updated by tML's `UpdateBiomes` | game tick | no per read | reflection-fetched per sample today; cacheable |
| `Player.dead` / `Player.statLife` / `statLifeMax2` | bool / int | instance fields | game tick | no | |
| `Player.position` | `Vector2` field | instance | game tick | no | snapshotter only |
| `Sandstorm.Happening` | `bool` | static field on `Terraria.GameContent.Events.Sandstorm` | game tick | no | |
| `DD2Event.Ongoing` | `bool` | static | game tick | no | |
| `LanternNight.GenuineLanterns` / `ManualLanterns` | `bool` | static | game tick | no | |
| `BirthdayParty.GenuineParty` / `ManualParty` | `bool` | static | game tick | no | |
| `NPC.active`, `NPC.boss`, `NPC.realLife`, `NPC.type` | bool/bool/int/int | instance | game tick | no | `realLife` is whoAmI of head segment, -1 on head/solo (verified) |
| `NPCID.Sets.ShouldBeCountedAsBoss` | `bool[]` | static `NPCID.Sets` array | install-time | no | indexed by NPC type |
| `Lang.GetNPCName(int)` | `LocalizedText` | tML-patched, reads `_npcNameCache[netID]` | install + language reload | no | returns `LocalizedText` ref |
| `LocalizedText.Value` | `string` | private setter backing field | install + language reload | no | field read |
| `Lang.GetNPCNameValue(int)` | `string` | calls `GetNPCName(...).Value` | install + language reload | no | sum of two no-alloc reads |
| `ModContent.GetContent<ModBiome>()` | `IEnumerable<ModBiome>` | tML registry | install-time only | yes (enumerable) | `.ToArray()` materialises once |
| `ModBiome.Type` | `int` property | tML field | install | no | id used for `modBiomeFlags` bit position |
| `SubworldLibrary.SubworldSystem.AnyActive()` | `bool` | static method | game tick | no when called direct, yes (1 box) via `MethodInfo.Invoke` | 0-arg overload exists; bind directly |
| `SubworldLibrary.SubworldSystem.Current` | `Subworld` ref | static property | game tick | no when called direct | via `PropertyInfo.GetValue` today |
| `SubworldLibrary.Subworld.FullName` | `string` property | instance | game tick | unknown — likely cached field but not verified | low priority — runs at 1 Hz |
| `BiomeLoader` (tML loader) | n/a — not directly read | n/a | n/a | n/a | `ModBiome.IsBiomeActive` is the per-biome probe but we bypass via `modBiomeFlags` |

**Notable gap (already documented):** tModLoader 1.4.4 has no `ModWeather` registration API and no `ModInvasion` API. The hardcoded 12-flag weather table and 5-entry invasion enum are the documented honest hardcodes. No optimisation will change that.

---

## 4. Optimisation opportunities — prioritised catalogue

Every entry below: **what changes**, **invariant compliance** (read-only? zero-alloc? universal? abort-clean?), **expected delta**, **risks**, **test-plan handle**.

### R1 — Word-level XOR diff in `ContextTransitionWatcher.DiffBiomeBits`

**Change.** Replace the bit-by-bit `IsSet` loop with a word-by-word XOR:

```csharp
internal void DiffBiomeBitsFast(in BiomeBitset current, ref BiomeBitset last, ...) {
    int words = Math.Min(current.WordCount, last.WordCount);
    int vanillaCount = BiomeRegistry.VanillaCount;
    for (int w = 0; w < words; w++) {
        ulong cur = current.WordUnchecked(w);
        ulong lst = last.WordUnchecked(w);
        ulong diff = cur ^ lst;
        if (diff == 0UL) continue;                    // ★ early-out, the 99.9% case
        while (diff != 0UL) {
            int b = System.Numerics.BitOperations.TrailingZeroCount(diff);
            int bitIndex = (w << 6) + b;
            bool nowSet = (cur & (1UL << b)) != 0UL;
            string name = BiomeRegistry.NameOrIndex(bitIndex);
            recorder.OnContextTransition("biome",
                nowSet ? "(off)" : name,
                nowSet ? name : "(off)",
                tick, frameMs);
            diff &= diff - 1UL;                       // clear lowest set bit
        }
    }
    last.CopyFrom(current);
}
```

Requires a new `internal ulong WordUnchecked(int w)` accessor on `BiomeBitset` (a one-liner returning `_words[w]`).

**Invariants:** read-only ✅, zero-alloc per tick ✅ (the only alloc is the row, on the change branch), universal ✅, abort-clean ✅.

**Expected delta.** Common case (no biome change), the diff returns after `words × 2` loads + `words × 1` XOR + `words × 1` zero-check = roughly 6 ns for a 2-word bitset, down from ~680 ns. **~99% reduction on no-op ticks.**

**Risks.** None obvious — `BitOperations.TrailingZeroCount` is a JIT intrinsic on .NET 8. The off/name strings are swapped from the original convention; double-check the test fixture's expected row contents during implementation.

**Test handle.** A unit test that fills two synthetic `BiomeBitset`s, flips known bits, and asserts the watcher emits exactly those transitions in correct order. Already feasible — the watcher takes a `SessionRecorder` and the recorder can take a fake writer.

### R2 — Cache `Player.modBiomeFlags` access as a compiled delegate

**Change.** At `BiomeRegistry.Populate`, after binding `_modBiomeFlagsField`, also build:

```csharp
private static Func<Player, BitArray>? _modBiomeFlagsGetter;
// build:
var p = Expression.Parameter(typeof(Player), "p");
var fieldAccess = Expression.Field(p, _modBiomeFlagsField);
_modBiomeFlagsGetter = Expression.Lambda<Func<Player, BitArray>>(fieldAccess, p).Compile();
```

Per-tick replace `_modBiomeFlagsField.GetValue(player)` with `_modBiomeFlagsGetter!(player)`.

**Invariants:** read-only ✅, zero-alloc per tick ✅ (compiled lambda is a static dispatch), universal ✅, abort-clean ✅ (if `_modBiomeFlagsField` is null the getter is also null and the existing `if (_modBiomeFlagsField == null) return;` guard catches both).

**Expected delta.** `FieldInfo.GetValue` is ~50 ns; compiled delegate is ~2 ns. Saves ~48 ns × 10 Hz = 480 ns/s amortised. Small absolute, but it removes a reflection call from the per-sample hot path — a clean readability + perf win combined.

**Risks.** `System.Linq.Expressions` adds binary size; we already depend on it transitively through LiteDB so no new deps. Compilation happens once at install.

**Test handle.** Unit test that compares the compiled getter's output against `_modBiomeFlagsField!.GetValue(player)` on a stub player.

### R3 — Cache `SubworldProbe` reads as compiled delegates

**Change.** At `SubworldProbe.Initialise`:

```csharp
private static Func<bool>? _anyActiveFn;
private static Func<object?>? _currentFn;
private static Func<object, string?>? _fullNameFn;

if (_anyActive != null)
    _anyActiveFn = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), _anyActive);
if (_currentProp?.GetMethod is { } cGet)
    _currentFn = (Func<object?>)Delegate.CreateDelegate(typeof(Func<object?>), cGet);
if (_fullNameProp?.GetMethod is { } fGet)
    _fullNameFn = (object inst) => (string?)fGet.Invoke(inst, null);
    // — or, with stronger typing once Subworld type is known —
    // ParameterExpression instParam = Expression.Parameter(typeof(object), "i");
    // UnaryExpression cast = Expression.Convert(instParam, subworldType);
    // MemberExpression fnAccess = Expression.Property(cast, fGet);
    // Expression.Lambda<Func<object,string?>>(fnAccess, instParam).Compile();
```

Per-tick (well, per slow cadence — 1 Hz) replace `_anyActive!.Invoke(null,null)` with `_anyActiveFn!()`, etc.

**Invariants:** read-only ✅, zero-alloc per tick ✅ (no boxing — bool return goes straight to the stack via the typed delegate), universal ✅, abort-clean ✅ (fallback path stays).

**Expected delta.** Eliminates ~3 reflection invocations + 1 box per slow tick = ~150 ns + 24 B GC pressure. Per 1 Hz cadence: 24 B/s alloc → ~86 KB/hour, which avoids one minor GC every few hours.

**Risks.** Compiling an `Expression.Lambda` against `Subworld` type requires resolving the type at runtime (we already do — `Type.GetType("SubworldLibrary.Subworld, SubworldLibrary", ...)`). If SWL renames the type the cached delegate becomes null and the existing `Available` check catches it.

**Test handle.** Integration test that loads a stub assembly mimicking `SubworldLibrary.SubworldSystem` + `Subworld` and confirms the cached delegate path returns the same key as the reflection path.

### R4 — Defer `Lang.GetNPCNameValue` in `ContextTransitionWatcher`

**Change.** Replace the always-resolved `currentBossName` with a typed-int cache:

```csharp
private short _lastBossType;     // 0 when no boss
private string _lastBossName = "";
// ...
short curBossType = ctx.Bosses.Count > 0 ? ctx.Bosses[0] : (short)0;
bool currentBossesPresent = curBossType != 0;
// resolve the name ONLY when we know we'll emit
if (curBossType != _lastBossType) {
    string curName = curBossType != 0 ? (Lang.GetNPCNameValue(curBossType) ?? "npc-" + curBossType) : "";
    // ... emit boss transition with curName ...
    _lastBossType = curBossType;
    _lastBossName = curName;
}
```

**Invariants:** read-only ✅, zero-alloc per tick ✅ (the `Lang` call is gated), universal ✅, abort-clean ✅.

**Expected delta.** Removes one `Lang.GetNPCNameValue` call per tick on every tick where the boss didn't change. At ~60 Hz × hours that's hundreds of thousands of calls saved per session. Each call is maybe ~5 ns so the absolute saving is ~300 ns/s — comparable to the biome diff win, small but not negligible.

**Risks.** The string compare `currentBossName != _lastBossName` in the original code was the *only* way the watcher detected "boss swapped" in the same tick. The replacement uses `curBossType != _lastBossType` which is strictly stronger (boss swaps change the type). **Better than original.**

**Test handle.** Unit test for the swap case: spawn two different boss types in sequence, confirm one `bossSwap` row.

### R5 — Bit-walk weather diff via `TrailingZeroCount`

**Change.** Replace the for-16 loop in the weather-diff branch with:

```csharp
ushort changed = (ushort)(ctx.Weather ^ _lastWeather);
ushort cur = (ushort)ctx.Weather;
while (changed != 0) {
    int bit = System.Numerics.BitOperations.TrailingZeroCount((uint)changed);
    WeatherFlags flag = (WeatherFlags)(1 << bit);
    bool nowOn = (cur & (ushort)flag) != 0;
    string label = WeatherSources.DisplayName(flag);
    if (!string.IsNullOrEmpty(label) && label[0] != '?') {
        // ALSO FIX THE BUG: include flag identity in Type
        recorder.OnContextTransition("weather:" + label, nowOn ? "off" : "on", nowOn ? "on" : "off",
            ctx.TickIndex, frameMs);
    }
    changed &= (ushort)(changed - 1);
}
_lastWeather = ctx.Weather;
```

**Invariants:** read-only ✅, zero-alloc per tick ✅ (when no change — common), universal ✅, abort-clean ✅. **Also fixes the latent bug** identified in §1.8 by including the flag name in the row.

**Expected delta.** Identical algorithmic complexity for changes (still walks each changed bit) but the no-change case becomes a single XOR + zero-check ≈ 2 ns, down from 16 iterations × ~5 ns ≈ 80 ns. Saves ~80 ns × 60 Hz = ~5 µs/s amortised. Small but a free win since the bug-fix forces the rewrite anyway.

**Risks.** The Type column now varies per flag (`weather:Rain`, `weather:BloodMoon`, ...). Downstream queries/UI that filter on `Type == "weather"` would need to be updated to a prefix match. **This is the right shape** but it's a wire-format change for the persisted rows — coordinate with the persistence schema migration.

**Test handle.** Unit test that flips a known set of weather flags and asserts one transition per changed bit, with the right Type and From/To.

### R6 — Replace `BossSampler._nameCache` Dictionary with a `string[]` keyed by NPC type

**Change.** At install-time, size a `string[]` to `NPCID.Count + maxModdedNpcType`. On first request fill the entry; on subsequent requests do a single array load.

```csharp
private static string?[] _nameByType = Array.Empty<string?>();
public static void EnsureCapacity(int maxType) {
    if (_nameByType.Length <= maxType) Array.Resize(ref _nameByType, maxType + 1);
}
public static string DisplayName(int type) {
    if ((uint)type >= (uint)_nameByType.Length) return "NPC:" + type;
    string? cached = _nameByType[type];
    if (cached != null) return cached;
    cached = ResolveAndCache(type);
    _nameByType[type] = cached;
    return cached;
}
```

**Invariants:** all four ✅. Sizing comes from `NPCLoader.NPCCount` at install (verified API surface).

**Expected delta.** `Dictionary<int,string>.TryGetValue` is ~10 ns; array indexed access is ~1 ns. Saves ~9 ns per `DisplayName` call. The function is called from the watcher (now gated by R4), the snapshotter (1 per 30 s), and the UI tab (1 Hz). Marginal — call it a quality win not a perf win.

**Risks.** Memory: an extra `string?[]` of ~10 000 entries (most null) = ~80 KB of references. Trivial against the 234 MB the profiler already uses.

**Test handle.** Direct unit test.

### R7 — Fuse `BossSampler.Sample` with `ProfilerSystem.CountActive(Main.npc)`

**Change.** Both functions walk `Main.npc[]` independently each tick. Add a single fused pass that returns both the active-NPC count and writes the boss slot array.

```csharp
public static int SampleAndCount(ref BossSlotArray dest, out int activeCount) {
    dest.Clear();
    NPC[] npcs = Main.npc;
    bool[] bossSet = NPCID.Sets.ShouldBeCountedAsBoss;
    int count = 0;
    int active = 0;
    for (int i = 0; i < npcs.Length; i++) {
        NPC npc = npcs[i];
        if (!npc.active) continue;
        active++;
        int rl = npc.realLife;
        if (rl >= 0 && rl != i) continue;
        int type = npc.type;
        if (!(npc.boss || ((uint)type < (uint)bossSet.Length && bossSet[type]))) continue;
        if (dest.Contains((short)type)) continue;
        if (!dest.TryAdd((short)type)) break;
        count++;
    }
    activeCount = active;
    return count;
}
```

Then in `ProfilerSystem.PostUpdateEverything` call once per tick and pass `activeCount` into the snapshotter call site that previously called `CountActive(Main.npc)`.

**Invariants:** read-only ✅, zero-alloc ✅, universal ✅, abort-clean ✅.

**Expected delta.** One 200-slot walk instead of two. Saves ~1 µs per tick at 60 Hz = ~60 µs/s = 0.006% overhead. Small individually but it's a *no-cost change* — strict consolidation.

**Risks.** Tight coupling between `ProfilerSystem` and `BossSampler`; the fused method blurs the per-file separation. Counter: both files already exist solely to serve the per-tick path; sharing one loop is honest.

**Test handle.** Existing tests should pass unchanged; add one that confirms `activeCount` matches a parallel oracle.

### R8 — Latch active-keys hash sets in `EventAggregator`

**Change.** Track a `_lastTickKeysConsumed` flag, set by the EventsTab when it reads, cleared at the start of each `Accumulate`. If unconsumed, skip the six `HashSet<int>.Clear()` calls and the corresponding `.Add` calls.

```csharp
private bool _activeKeysConsumed = true;     // default: consumed (clear on first tick)
public void MarkActiveKeysRead() { _activeKeysConsumed = true; }

// in Accumulate:
bool refresh = _activeKeysConsumed;
if (refresh) {
    for (int d = 0; d < _activeKeysLastTick.Length; d++) _activeKeysLastTick[d].Clear();
    _activeKeysConsumed = false;
}
// ... in BumpBucket sites, only Add when refresh ...
```

**Invariants:** read-only ✅, zero-alloc per tick ✅, universal ✅, abort-clean ✅. The `IsActiveNow` semantics shift slightly — "the most-recent tick the UI read" rather than "the literal last tick". The UI consumes at 1 Hz, so the diff is sub-frame; not user-visible.

**Expected delta.** Six `HashSet<int>.Clear` calls per tick is maybe 30 ns. At 60 Hz × ~50% of ticks where UI hasn't consumed → saves 15 ns × 60 Hz = ~1 µs/s. Small, but *combined* with the `Add` skip the saving doubles to ~2 µs/s.

**Risks.** Subtle semantics change. Acceptable because the EventsTab is the sole consumer and runs at 1 Hz. If a future consumer wants per-tick active sets, they request the explicit refresh.

**Test handle.** Unit test that confirms `IsActiveNow` returns the same answer with and without `MarkActiveKeysRead`.

### R9 — Pass `itemCount` into `WorldSnapshotter.OnTick`

**Change.** Trivial: extend the signature to take `itemCount`, drop the in-snapshot walk, and have `ProfilerSystem` pass `CountActive(Main.item)` from its already-existing tick-side counters.

**Invariants:** all four ✅.

**Expected delta.** Saves a 400-slot scan per 30 s. ~1 µs × 0.033 fires/s = 33 ns/s amortised. Trivially small; the *value* is consistency with the other entity counts.

**Risks.** None.

**Test handle.** None needed beyond confirming a snapshot's `ItemCount` field matches.

### R10 — Move `PlayerDeathDetector` LiteDB query off the game thread

**Change.** Replace the inline `db.DamageTaken.Query().Where(...)...FirstOrDefault()` with an asynchronous resolver:

1. On dead-edge, enqueue a `PlayerDeathRow` *without* the killer name (or with a temporary "(resolving)" placeholder) plus a marker.
2. The writer thread, on dequeue, performs the killer lookup against the in-memory or freshly-persisted damage-taken stream.
3. Alternatively (preferred — see R10b below) replace the LINQ query with an in-RAM rolling window of the last N damage-taken events maintained by `SessionRecorder`.

**R10b — in-RAM rolling damage window (preferred).** Maintain a fixed-size ring `DamageTakenRow[8]` in the recorder; on each `OnDamageTaken` push and wrap. On death, scan the ring (allocation-free) to pick the most-recent and ideally damage-weighted top hitter (the v0.6 implementation will switch attribution to damage-weighted per baseline §4.3 anyway).

**Invariants:** read-only ✅, zero-alloc per tick (alive case) ✅, universal ✅, abort-clean ✅.

**Expected delta.** The LiteDB read in v0.5 takes ~0.4 ms (per the benchmark "Read last-10 sessions = 0.426 ms" which scans a sorted collection). Death-edge cost drops from ~0.4 ms to ~50 ns. Rare event but the kind of event that lands inside an already-bad frame (the player is dying), so removing this game-thread hit removes a documented spike contributor.

**Risks.** Requires the v0.6 damage-weighted-attribution work to land alongside; the two changes belong in the same commit. Captured in baseline §4.3.

**Test handle.** Unit test on the ring + classifier.

### R11 — Pre-resolve every biome `DisplayName` into a `string[]` indexed by bit

**Change.** `BiomeRegistry.NameOrIndex(int)` already returns `_biomes[bitIndex].DisplayName` — `_biomes` is a `List<BiomeDescriptor>`. Indexer access on `List<T>` is bounds-checked + virtual call into the array. A direct `string[]` (built once at `Populate`) shaves a few ns and removes the dictionary-style indirection.

**Invariants:** all four ✅.

**Expected delta.** ~5 ns per call. The watcher calls this once per bit-flip, the snapshotter calls it once per 30 s, the UI calls it at 1 Hz. Tiny. **Optional.**

### R12 — Replace `WeatherSources.All` `Func<bool>[]` with `delegate*<bool>[]`

**Change.** Use C# 9 function pointers:

```csharp
public static readonly (WeatherFlags Flag, delegate*<bool> Read)[] All = ...;
```

requires `unsafe` block. Each read becomes a direct indirect call — no delegate object, no `Invoke` dispatch.

**Invariants:** all four ✅. But — `unsafe` is allowed in this project? The hot path doesn't currently use unsafe code. Likely Caner would want to know about that change first.

**Expected delta.** ~1 ns per call × 12 sources × 10 Hz = ~120 ns/s. **Not worth the `unsafe` introduction.** Listed only for completeness.

### Summary — recommended set

| ID | Recommendation | Priority | Effort | Expected /s saving | Invariants |
|---|---|---|---|---|---|
| R1 | Word-XOR biome diff | high | small | ~40 µs/s | all ✅ |
| R4 | Defer boss-name lookup | high | small | ~300 ns/s + clarity | all ✅ |
| R5 | Bit-walk weather diff + flag-name bug fix | high | small | ~5 µs/s + correctness | all ✅ |
| R10 | Move death-edge DB query off game thread | high | medium | removes ~0.4 ms spike | all ✅ |
| R2 | Compiled `modBiomeFlags` getter | medium | small | ~0.5 µs/s | all ✅ |
| R7 | Fuse boss-sample with active-count | medium | small | ~60 µs/s | all ✅ |
| R3 | Compiled `SubworldProbe` delegates | medium | small | removes 1 box/s | all ✅ |
| R8 | Latch `_activeKeysLastTick` | low | small | ~2 µs/s | all ✅ |
| R6 | Boss-name array cache | low | small | ~9 ns/call | all ✅ |
| R9 | Pass `itemCount` into snapshotter | low | trivial | ~33 ns/s + consistency | all ✅ |
| R11 | Biome name `string[]` | low | trivial | ~5 ns/call | all ✅ |
| R12 | `delegate*` weather reads | drop | n/a — introduces unsafe | ~120 ns/s | unsafe — not recommended |

Cumulative expected delta on the per-tick events-and-context cost: **~70–100 µs/s** of game-thread CPU recovered, plus the ~0.4 ms death-edge stall removed, plus the latent weather-Type bug fixed in the same change. Against the 0.27 ms/tick × 60 Hz = 16.2 ms/s the profiler currently spends, that's a ~0.5% absolute reduction — modest in isolation, but stacked with the wins from `MetricCollector`, `ILHookInterceptor`, and the persistence pass should hit the baseline §6 target of `0.27 ms → 0.10 ms` per-tick.

---

## 5. Cross-system dependencies

The Events-and-Context surface is read by — and reads from — five other systems. Optimisations must not break the contracts those systems rely on.

### 5.1 `MetricCollector` ↔ `ContextTagger`

`MetricCollector.EndTick` pushes the closed `TickFrame` into history; `ContextTagger.Snapshot` runs immediately after and writes the same tick's context into `_ctx`. The aggregator then reads `collector.History[Count-1].FrameTimeMs` to pair frame time with context.

**Contract.** `ContextTagger.Snapshot(tickIndex)` must run **after** `EndTick(tickIndex)` so the history slot exists. Verified in `ProfilerSystem.PostUpdateEverything` (`collector.EndTick` precedes `tagger.Snapshot` by construction).

**Implication for R7 (fused boss-sample + active-count).** The fusion moves the `Main.npc[]` walk earlier in the tick path. We must ensure no other tick-side consumer reads `_ctx.Bosses` before the snapshot is taken — verified: `ProfilerSystem.PostUpdateEverything` is the sole call site.

### 5.2 `SessionRecorder` ← `ContextTransitionWatcher`

The watcher calls `recorder.OnContextTransition` per change. `OnContextTransition` allocates a `ContextTransitionRow` and enqueues it to the writer thread. **The watcher must remain on the game thread** because the transition event itself is tied to the game-frame timestamp `frameMs`.

**Implication for R10 (death-edge async).** Moving the *killer lookup* off-thread is fine; moving the *transition emit* off-thread would require timestamp propagation. Don't conflate the two changes.

### 5.3 `WorldSnapshotter` ← `ProfilerSystem`

The snapshotter takes pre-counted NPC/projectile/dust counts. **Implication for R9.** Extend the contract to include `itemCount` symmetrically.

### 5.4 `EventsTab` ← `EventAggregator`

The tab consumes `_lastContext`, `BucketsFor(dim)`, `IsActiveNow(dim, key)`, and `SnapshotRows(minDwell)` at ~1 Hz. **Implication for R8.** Latching the active-keys set requires the tab to call `MarkActiveKeysRead` when it consumes. Trivial to wire; an audit point during implementation.

### 5.5 Persistence schema ← `ContextTransitionRow.Type`

R5's bug-fix changes the `Type` column from `"weather"` to `"weather:Rain"` etc. **This is a schema-affecting change.** Coordinate with `Migrations.cs`. The schema version on `ContextTransitionRow` is currently `1`; bump to `2` and run a one-way migration that splits old rows or leaves them flagged as schema-1 (queries filter by prefix `weather*`).

### 5.6 PerSession aggregates ← `EventContext`

`PerSessionModAggregate` and `PerSessionHookAggregate` do not directly consume `EventContext` today; the gated `ContextCorrelatedSpikeDetector` (Insights Engine, §4.1 of `notes/insights-engine-plan.md`) will. **None of the proposed optimisations affect the gated detector's input shape.** The transition stream we emit gets richer (R5 adds flag identity) and faster (R1 batches biome diffs into the same-tick stream); both help the future detector.

---

## 6. Prioritised order — recommended implementation sequence

The order below sequences the changes so each lands cleanly with isolated testing.

**Phase 1 (mechanical, no schema impact).** R1, R4, R7, R11, R6 — all internal refactors to existing files that produce identical observable output. Land in one commit per file group with synthetic-input tests against the existing watcher behaviour.

**Phase 2 (delegate caching).** R2, R3 — both swap reflection for compiled delegates. Land together; same shape of change. Test by comparing cached-delegate output against reflection output on a stub.

**Phase 3 (schema-affecting).** R5 — weather flag name in `Type` column. Land with a schema-version bump and a migration that maps legacy `"weather"` rows to a generic flag-unknown bucket (or leaves them as-is for old sessions). Update `ContextTransitionRow.Schema` from `1` to `2`. Coordinate with EventsTab's transition timeline view if/when that lands.

**Phase 4 (cross-system contract).** R9, R8 — `itemCount` plumbing and active-keys latch. Both touch `ProfilerSystem` ↔ Events boundary. Land together.

**Phase 5 (death-edge work — coordinated with the v0.6 damage-weighted attribution fix).** R10/R10b — replace synchronous LiteDB query with the in-RAM ring. This rides along with the baseline §4.3 attribution-bug fix.

**Phase 6 (decline).** R12 — explicit decision not to introduce `unsafe` for the marginal gain.

**Verification (Phase 7).** Re-run the `PersistenceBenchmarkTests` enqueue-latency benchmark and the synthetic context-transition microbenchmark (to be added in Phase 1 alongside R1). Targets:

- No-op `OnSnapshot` cost: from ~750 ns → < 40 ns.
- Active-bit-change `OnSnapshot` cost: from ~900 ns → < 200 ns.
- Death-edge cost: from ~0.4 ms → < 50 µs.
- Slow-cadence subworld read: from ~150 ns + 24 B box → < 10 ns + 0 B.
- Total Events-and-Context share of per-tick PerformanceProfiler cost: from ~8–13% → < 4%.

The baseline contract is row-by-row: every line in `baseline.md` §6 in the better direction, no capture surface lost, no UI density reduced.

---

## 7. References

External (verified during this research):

- tModLoader source — patch files for `Lang.cs`, `NPC.cs`, `Player.TML.cs` on the `1.4.4` branch. Confirmed: `Lang.GetNPCNameValue(int)` returns `GetNPCName(int).Value`, the `_npcNameCache` is a tML-side array indexed by netID, no allocation per call. `Player.modBiomeFlags` is `internal BitArray modBiomeFlags = new BitArray(0)` updated by tML's `UpdateBiomes`. `NPC.realLife` defaults to `-1`, points to the head segment's `whoAmI` for multi-segment bosses (Destroyer, Wall of Flesh).
- tModLoader docs — `NPCID.Sets` reference. `ShouldBeCountedAsBoss` confirmed as a `bool[]` indexed by NPC type, renamed from `TechnicallyABoss`.
- Terraria source (UTINKA mirror) — `LocalizedText.Value` is `public string Value { get; private set; }` — a backing field, not a computed property; access is allocation-free.
- SubworldLibrary source — `SubworldSystem.AnyActive()` has 0-arg, `Mod`-arg, and generic overloads; `Current` is a static property returning `Subworld`; `Subworld.FullName` is an instance property used in identity comparisons. Type/method binding via reflection works as the existing probe expects.
- Wiki / community confirms `Main.npc.Length == Main.maxNPCs == 200` on vanilla 1.4.4 (Terraria 1.4.4.9 source field `npc` in `Terraria.Main.cs`).
- .NET 8 JIT — `System.Numerics.BitOperations.TrailingZeroCount`, `PopCount`, `LeadingZeroCount` are intrinsics; lowered to native `TZCNT`/`POPCNT` on x86-64 and `CLZ`/`CNT` on ARM64. Available on both x64 Windows/Linux and Apple Silicon dev machines.
- C# language reference — lambdas without captures emit a static method and the delegate instance is cached; subsequent reads of the field reuse the same delegate. `WeatherSources.All` confirmed to be one-time-init.
- C# 9 static lambdas / function pointers — viable but require `unsafe` for `delegate*<...>`; declined in R12.

Internal:

- `context/perf-pass/baseline.md` — v0.5 baseline numbers, deltas, hard constraints, target deltas.
- `context/notes/philosophy.md` — universal-mod posture, capture-the-chain principle, what the perf pass may and may not change.
- `context/systems/events-and-context.md` — system overview, current state, known issues.
- `context/notes/events-tab-plan.md` — design plan for the EventsTab, references the transition stream gap.
- `context/notes/insights-engine-plan.md` §4.1 — gated detector that will consume the transition stream.
- `Profiling/Events/*` — eight `.cs` files plus four small types.
- `Profiling/Persistence/ContextTransitionWatcher.cs`, `PlayerDeathDetector.cs`, `WorldSnapshotter.cs` — the three consumers.
- `Profiling/Persistence/SessionRecorder.cs` — the per-row enqueue surface (`OnContextTransition`, `OnPlayerDeath`, `OnWorldSnapshot`).
- `Profiling/Persistence/Records/ContextTransitionRow.cs` — schema; bump from `1` to `2` for R5.

---

## 8. Appendix — verbatim hot-loop comparisons

For future agents implementing the pass, side-by-side of the before/after shapes of the three highest-impact loops.

### 8.1 Biome diff (R1)

```text
BEFORE (per tick, 38 vanilla + N modded bits):
  for i in 0..N:
    if current.IsSet(i) != last.IsSet(i):  // 2× bounds-check + shift + and
      emit row
  last.CopyFrom(current)
  cost ≈ 10·N ns when nothing changed (≈ 380 ns for 38 bits, 680 ns for 68 bits)

AFTER (per tick):
  for w in 0..WordCount:                   // 2 words for ≤128 bits
    diff = current.Words[w] ^ last.Words[w]
    if diff == 0: continue                 // ★ 99%+ of ticks early-out here
    while diff != 0:
      bit = TrailingZeroCount(diff)
      emit row for bit
      diff &= diff - 1
  last.CopyFrom(current)
  cost ≈ 6 ns when nothing changed
```

### 8.2 Weather diff (R5)

```text
BEFORE (per tick when any bit changed):
  changed = current ^ last
  for bit in 0..16:
    flag = 1 << bit
    if (changed & flag) == 0: continue
    label = WeatherSources.DisplayName(flag)
    if invalid label: continue
    emit row with Type="weather"                  // ⚠ flag identity LOST
  cost = 16 iterations always when changed != 0

AFTER:
  changed = (ushort)(current ^ last)
  while changed != 0:
    bit = TrailingZeroCount(changed)
    flag = (WeatherFlags)(1 << bit)
    emit row with Type="weather:" + flag.ToString()  // ✅ identity preserved
    changed &= changed - 1
  cost = exactly N iterations for N changed bits

  Combined with: when changed == 0 (vast majority of ticks), the entire
  weather branch costs one XOR + one zero-check ≈ 2 ns.
```

### 8.3 SubworldProbe slow-cadence read (R3)

```text
BEFORE (1 Hz):
  anyBoxed = _anyActive!.Invoke(null, null)   // ⚠ MethodInfo dispatch + bool box
  if not bool true: return 0
  current = _currentProp!.GetValue(null)      // ⚠ PropertyInfo dispatch
  if null: return 0
  full = _fullNameProp!.GetValue(current) as string
  ...
  cost ≈ 150 ns + 24 bytes/s GC pressure (boxed bool + maybe one allocator-side header)

AFTER:
  if not _anyActiveFn!(): return 0            // direct virtual call, no box
  current = _currentFn!()
  if null: return 0
  full = _fullNameFn!(current)
  ...
  cost ≈ 8 ns, 0 bytes GC
```

---

End of research dossier. Implementation plan (which patches land in which commit, schema migration mechanics, test scaffolding) belongs in `context/perf-pass/plans/events-and-context.md` once Caner has reviewed this dossier.
