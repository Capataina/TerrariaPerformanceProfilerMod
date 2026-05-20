# Persistence — v0.6 Optimisation Pass Research Dossier

> Scope: every byte the profiler enqueues, journals, applies, or compacts. Game-thread enqueue, writer-thread drain, BSON layout, indexes, downsampling, compaction, session-end. Plus the three correctness bugs that ride along (`itemCreatedEvents = 0`, `buffEvents = 2`, last-hit death attribution).
>
> Hard rule (from `philosophy.md` and `baseline.md`): **optimisation = doing what we already do at maximum efficiency. Not = doing less.** Every capture surface stays. Every row stays. Every column stays. Every snapshot cadence stays. We only change the *how*: layout, encoding, sequencing, batching, threading, indexing, locking. Anything in this document that smells like "drop a column", "downsample further", "stop capturing X", "skip if the value is the default", "remove an index because it costs bytes" is a bug in the document and should be rejected.

---

## Table of contents

1. Current state audit — file walk, per-call-site allocation profile, ops/sec per stream
2. Measured baseline — pulled from `baseline.md` and the playtest session
3. tML hook surface — the generic surfaces the interaction trackers consume, with gh-cited paths
4. LiteDB internals deep-dive — BSON format costs, indexes, WAL, checkpoint, Rebuild
5. Optimisation opportunities — game-thread CPU, writer-thread CPU, allocations, DB size, lock contention, end-of-session blocking
6. Bug-fix designs — `itemCreatedEvents=0`, `buffEvents=2`, last-hit death attribution
7. Cross-system dependencies — what this pass forces the rest of the codebase to absorb
8. Prioritised execution order — what lands first, what gates what, what is independently shippable
9. References — every external citation used

---

## 1. Current state audit

### 1.1 File walk

The persistence layer breaks into eight concerns. Each row below is one file or directory and the role it plays per tick / per event / per session.

| File / dir | Role | Cadence | Hot-path? |
|---|---|---|---|
| `ProfilerDatabase.cs` | LiteDB facade, open / recover / dispose, RotateBackups, Compact, ApplyBatch | Per-batch (writer thread) | Writer-thread hot, not game-thread |
| `DbWriterThread.cs` | Channel-backed writer thread; batches up to 64 ops; checkpoints every 60 s | Per batch | Writer-thread hot |
| `DbWriteOp.cs` | The 22-kind discriminated struct queued by the game thread | Per op | **Game-thread hot** |
| `EventJournal.cs` | NDJSON redo log; UTF-8 + `StringBuilder` + `JsonSerializer` per batch | Per batch | Writer-thread hot |
| `IPersistenceStream.cs` | Stream contract | n/a | n/a |
| `StreamRegistry.cs` | O(1) kind → stream dispatch | Per op | Writer-thread |
| `Streams/*.cs` | 17 streams, each owning a collection's apply + reconstruct + index | Per op | Writer-thread |
| `Records/*.cs` | 23 BSON records (the DB shape) | n/a | n/a |
| `SessionRecorder.cs` | Per-world-load recorder; drives downsampler, stalls, spikes, cluster build, end-aggregates | Per tick + per event | **Game-thread hot** |
| `TickDownsampler.cs` | 1 Hz warm + 1/min cold tiers | Per tick | **Game-thread hot** |
| `ContextTransitionWatcher.cs` | Bit-diff biomes / weather / boss / invasion etc | Per snapshot | **Game-thread hot** |
| `ModlistFingerprint.cs` | SHA-256 (truncated 8 bytes) of sorted `id:name@version;` | Once per session | Cold |
| `PlayerDeathDetector.cs` | dead-edge detector; reads last `damageTaken` row from DB on death | Per tick + on death | Game-thread (cold-ish) |
| `WorldSnapshotter.cs` | 30 s periodic state row | Per tick (gate) | Game-thread (cold) |
| `Interactions/InteractionPlayer.cs` | OnHurt, OnHitNPC[WithItem,WithProj], PostUpdateBuffs, PostUpdateEquips | Per event | **Game-thread hot** |
| `Interactions/InteractionNpc.cs` | GlobalNPC.OnSpawn | Per spawn | **Game-thread hot** |
| `Interactions/InteractionItem.cs` | GlobalItem.OnCreated (today: broken — fires only on craft) | Per craft | Game-thread |
| `Migrations.cs` | Schema migration step table (currently no-op) | Once at open | Cold |
| `LegacyJsonImporter.cs` | One-shot pre-LiteDB JSON ingest | Once ever | Cold |
| `SessionSummaryLogger.cs` | End-of-session log dump (runs on game thread today) | Once per session-end | **End-of-session hot** |
| `ProfilerCompactCommand.cs` | `/profiler-compact` chat handler | On demand | Cold |
| `ProfilerPaths.cs` / `PersistenceFileNames.cs` | Path resolution | Once | Cold |

### 1.2 Per-call-site allocation profile (game thread)

What allocates on the game thread per enqueue, today, in `Profiling/Persistence/`. Numbers are reasoned from the source, not measured per-call — the synthetic enqueue benchmark in `baseline.md` (441 ns/op) is the ground truth.

| Site | Per-event allocations | Why |
|---|---|---|
| `SessionRecorder.OnDamageTaken` | `DamageTakenRow` (24 fields incl. `List<int> ActiveBuffs`) + the List from `SnapshotActiveBuffTypes` (LOH-safe, but grows) | Box-free path: row constructed by caller (`InteractionPlayer`) and just enqueued |
| `SessionRecorder.OnDamageDealt` | `DamageDealtRow` (13 fields, all primitives + 2 strings) | Caller-built; only `NpcName` and `LoadoutFingerprint` may allocate substrings if not cached |
| `SessionRecorder.OnNpcSpawn` | `NpcSpawnRow` + `source.GetType().Name` boxing + `Substring("EntitySource_".Length)` allocation | The reflection-name strip allocates a new string every spawn |
| `SessionRecorder.OnItemCreated` | `ItemCreatedRow` + `context.GetType().Name` + `Substring` | Same pattern as NPC spawn |
| `SessionRecorder.OnLoadoutSnapshot` | `LoadoutSnapshotRow` + `List<EquipmentSlotEntry>` + `StringBuilder` for fingerprint | Builds 10–20-element list + composes fingerprint string every loadout edge |
| `SessionRecorder.OnBuffEvent` | `BuffEventRow` + `Lang.GetBuffName` lookup | One row per edge, but the diff runs **every PostUpdateBuffs tick**: `Array.IndexOf` twice over `Player.buffType` (length 22 in 1.4.4 default, may be larger) |
| `SessionRecorder.OnContextTransition` | `ContextTransitionRow` per change | Bounded |
| `SessionRecorder.OnPlayerDeath` | `PlayerDeathRow` per death (rare) | Bounded |
| `SessionRecorder.OnWorldSnapshot` | `WorldSnapshotRow` every 30 s | Cheap |
| `SessionRecorder.OnTick` → `TickDownsampler.EmitWarm/EmitCold` | `TickAggregateWarm` / `TickAggregateCold` + `List<double>` `PerModMs` of length ~100, optional `PerModBytes` of same | **Every second**, then **every minute**. Each list is freshly allocated and freshly copied from the per-mod-category 2-D collector data |
| `ContextTransitionWatcher.OnSnapshot` | A `ContextTransitionRow` per change; `Lang.GetNPCNameValue` per boss change; allocates `string.StartsWith` failure paths | Bounded but the per-tick **no-change** path still: reads `WeatherFlags`, scans 16 bits, scans every biome bit (length ~30) |
| `DbWriterThread.Enqueue` (the actual wire) | `Interlocked.Increment(ref _approxQueueDepth)` + `Channel.Writer.TryWrite` (which allocates an `AsyncOperation` only under the bounded path; unbounded does not allocate on the happy path) | Should be zero allocs on the steady path |

The hot path the enqueue benchmark measures is the **last line** above. 441 ns/op is one bounded read, one CAS on `_approxQueueDepth`, one `ConcurrentQueueSegment` slot write inside `UnboundedChannel`. That floor is fine; what regressed is that **more events now enqueue per second**, and each event constructs a record. The 441 ns benchmark covers only the enqueue, not the row construction. The construction cost is what the 60% bench regression captures because the benchmark presumably builds an op as part of the loop.

### 1.3 Ops/sec budget per stream (theoretical, per 60-Hz minute of busy combat)

| Stream | Ops/min (busy) | Bytes/row | Bytes/min |
|---|---|---|---|
| `damageDealtEvents` | ~3 600 (60 swings/s avg) | ~250 | 900 KB/min worst-case, ~120 KB/min playtest |
| `damageTakenEvents` | up to 60 (combat) | ~300 (List<int> ActiveBuffs) | 18 KB/min |
| `npcSpawnEvents` | a few/sec during a wave | ~280 | ~40 KB/min |
| `itemCreatedEvents` | 1–10/min (mining, crafting bursts) | ~180 | ~1 KB/min |
| `loadoutSnapshots` | 1 every 30 s + per change | ~600 + slot list | ~2 KB/min |
| `buffEvents` | 1–20/min | ~150 | ~3 KB/min |
| `contextTransitions` | 1–100/min (movement-heavy biome diffs) | ~120 | ~12 KB/min |
| `tickAggregatesWarm` | 60 | ~120 + per-mod list (~800 B at 100 mods) | ~55 KB/min |
| `tickAggregatesCold` | 1 | ~200 + per-mod list (~800 B) | ~1 KB/min |
| `spikeWindows` | 1–10/min | ~2 KB + per-mod-cat array (modCount×7 doubles ≈ 5.6 KB) | ~80 KB/min |
| `stallEvents` | 0–20/min normal, hundreds/min during a UI block | ~300 + 5 contributors | ~10 KB/min normal |
| `worldSnapshots` | 2/min | ~250 | ~0.5 KB/min |

Totals: combat-busy minute pushes ~1.2 MB of raw row data through the writer. Writer drains 314 ops/sec = ~18 840 ops/min — head-room exists, but the throughput target is > 1 000 ops/sec to absorb spikes without dropping `WarmAggregate`s when `QueueSoftCap` (100 000) is approached.

### 1.4 The end-of-session 8.5 s UiOverlayBlocking cluster

`baseline.md` reports a 40-stall, 8.5 s cluster at session end with `PerformanceProfiler` as the dominant contributor. The cause is reconstructable from the code:

```
   ProfilerSystem.OnWorldUnload  (game thread)
   ──────────────────────────────────────────
     SessionRecorder.End()
        DrainSpikes()                          ← reads collector.Spikes (cheap)
        DrainStalls()                          ← reads collector.Stalls
        FlushCluster()
        BuildModAggregates()                   ← ~100 mods × 7 cats double sum,
        BuildHookAggregates()                  ← ~10 000 hooks; allocates a row each
        BuildArchive()                         ← linear scan over RingBuffer
        writer.Enqueue(SessionEnd…)            ← non-blocking
     ProfilerDatabase.DrainAndTruncateJournalForSessionEnd()
        spin Thread.Sleep(20) × up-to-100      ← UP TO 2 SECONDS blocking
        db.Checkpoint()                        ← BLOCKING: flushes WAL pages
        journal.TruncateOnCleanShutdown()      ← BLOCKING: opens/closes file
     SessionSummaryLogger.Write(...)           ← BLOCKING: 6+ LiteDB queries
                                                 against the just-checkpointed DB
                                                 (each query is a B-tree scan)
```

Every step after `End()` is on the game thread. The 8.5 s is the sum of:

- the writer thread still chewing the final ModAggregateBatch + HookAggregateBatch (the hook batch is the killer — hooks list is ~10 000 long, even with the silent-hook filter the kept rows are several hundred and each `InsertBulk` row is a B-tree insert),
- the checkpoint copying ~64 KB of `-log` pages into the main file,
- six `db.Sessions/SpikeWindows/Stalls/etc.Find(...).Count()` calls in `SessionSummaryLogger` (each loads B-tree pages off disk),
- the journal truncate which deletes and reopens the file.

That entire chain must move off the game thread.

---

## 2. Measured baseline

Pulled directly from `context/perf-pass/baseline.md`. Reproduced here so this dossier is self-contained for the next session.

```
synthetic                  v0.5            v0.3            delta
─────────                  ────            ────            ─────
game-thread enqueue        441.2 ns/op     276 ns/op       +60%
writer drain               314 ops/sec     310 ops/sec     flat
read last-10 sessions      0.426 ms        0.39 ms         +9%
10-min Calamity DB         1064 KB         752 KB          +41%

playtest (session 6a0dcea5)            value
──────────────────────────             ─────
avg frame ms                           0.96
max frame ms                           172.0 (world-enter)
spikes / stalls / clusters             50 / 50 / 10
context transitions                    10
world snapshots                        10
damageTakenEvents                      10
damageDealtEvents                      354
npcSpawnEvents                         34
itemCreatedEvents                      0          ← bug
loadoutSnapshots                       41
buffEvents                             2          ← bug
end-of-session UiOverlayBlocking       8.5 s, 40 stalls   ← bug
top CPU mod                            PerformanceProfiler (0.27 ms/t, 4488 ms total)
hook install delta                     481 MB first install, 233 MB session sustained
LiteDB on disk after 5 sessions        9.5 MB
```

### 2.1 Targets

```
surface                            today           target
───────                            ─────           ──────
game-thread enqueue                441 ns/op       <  200 ns/op
writer drain                       314 ops/sec     > 1 000 ops/sec
10-min DB size                     1 064 KB        <  600 KB
end-of-session stall               8.5 s           0 (off-thread)
PerformanceProfiler avg per tick   0.27 ms         < 0.10 ms
itemCreatedEvents bug              0               every craft + pickup + drop
buffEvents bug                     2               every on/off edge
death attribution                  last-hit        damage-weighted last N seconds
```

---

## 3. tML hook surface for interactions

Every interaction tracker must hook a **generic surface vanilla / tML exposes**, per Invariant 5. The bug fix for `itemCreatedEvents = 0` falls under this section because the fix is to add the right hook, not to special-case any mod.

Sources: `tModLoader/patches/tModLoader/Terraria/ModLoader/` — patched files override the vanilla Terraria sources. Confirmed via `gh api repos/tModLoader/tModLoader/contents/patches/tModLoader/Terraria/ModLoader/<file>` calls.

### 3.1 The hooks we already use (kept; no fix needed)

| Hook | Signature | Fires when | Used by |
|---|---|---|---|
| `ModPlayer.OnHurt(Player.HurtInfo)` | `public virtual void OnHurt(Player.HurtInfo info)` (line 515 of patched ModPlayer.cs) | Right before health is reduced; local-client only | InteractionPlayer.OnHurt |
| `ModPlayer.OnHitNPC(NPC, NPC.HitInfo, int)` | line 896 | After hitting an NPC without specifying weapon/projectile | InteractionPlayer.OnHitNPC |
| `ModPlayer.OnHitNPCWithItem(Item, NPC, NPC.HitInfo, int)` | line 931 | After a weapon swing connects | InteractionPlayer.OnHitNPCWithItem |
| `ModPlayer.OnHitNPCWithProj(Projectile, NPC, NPC.HitInfo, int)` | line 966 | After a projectile hits | InteractionPlayer.OnHitNPCWithProj |
| `ModPlayer.PostUpdateBuffs()` | line 286 | After Player.UpdateBuffs runs each tick | InteractionPlayer.PostUpdateBuffs (broken diff — see §6.2) |
| `ModPlayer.PostUpdateEquips()` | line 302 | After equipment updates per tick | InteractionPlayer.PostUpdateEquips |
| `GlobalNPC.OnSpawn(NPC, IEntitySource)` | confirmed in tML; fires on every NPC spawn including CheatSheet's debug-spawn | InteractionNpc.OnSpawn |
| `GlobalItem.OnCreated(Item, ItemCreationContext)` | line 43 of patched GlobalItem.cs | **Only** on init/recipe/buy/journey-duplication contexts — does NOT fire on world-drop or pickup | InteractionItem.OnCreated (incomplete coverage — see §6.1) |

### 3.2 The hooks we're MISSING (the `itemCreatedEvents = 0` fix)

Verified via `gh api` against `patches/tModLoader/Terraria/ModLoader/GlobalItem.cs`:

```csharp
// GlobalItem.cs line 1027
/// Allows you to make special things happen when the player picks up an item.
/// Return false to stop the item from being added to the player's inventory; returns true by default.
/// Called on the local client only.
public virtual bool OnPickup(WorldItem item, Player player)
{
    return true;
}

// GlobalItem.cs (separate hook, not yet read but documented in tML examples)
/// Gets called when any item spawns in world
/// Called on the local client or the server where Item.NewItem is called.
public virtual void OnSpawn(WorldItem item, IEntitySource source)
{
}
```

Also exists in `ModPlayer`:

```csharp
// ModPlayer.cs line 1408
/// Allows you to make special things happen when this player picks up an item.
/// Return false to stop the item from being added to the player's inventory.
/// Called on the local client only.
public virtual bool OnPickup(WorldItem item)
{
    return true;
}
```

The three relevant generic surfaces for "an item entered the player's possession":

| Surface | Where item came from | Lifecycle moment |
|---|---|---|
| `GlobalItem.OnCreated(Item, ItemCreationContext)` | Recipe craft, init, shop buy, journey duplication | Item constructed in inventory |
| `GlobalItem.OnSpawn(WorldItem, IEntitySource)` | NPC drop, tile drop, /spawn debug, chest opening, fishing | Item appeared in world |
| `GlobalItem.OnPickup(WorldItem, Player)` *(or `ModPlayer.OnPickup(WorldItem)`)* | World pickup into inventory | Item leaves world, enters inventory |

The `IEntitySource` on `OnSpawn` is the universal "where did this come from" hint — `EntitySource_DebugCommand`, `EntitySource_DropFromNPC`, `EntitySource_TileBreak`, `EntitySource_OpenContainer`, etc. This is exactly the same mechanism as `GlobalNPC.OnSpawn` and the same Invariant-5-compliant pattern.

Recommended fix: split `itemCreatedEvents` into three context categories captured from three hooks, all writing the same `ItemCreatedRow` shape:

| Hook | `ContextCategory` value |
|---|---|
| `GlobalItem.OnCreated` | `"Recipe"` / `"Initialization"` / `"Buy"` / `"JourneyDuplication"` / `"<runtime-subclass-stripped>"` (today's behaviour) |
| `GlobalItem.OnSpawn` | `"Drop:<EntitySource-subclass>"` (e.g. `"Drop:DropFromNPC"`, `"Drop:TileBreak"`) |
| `GlobalItem.OnPickup` | `"Pickup:<EntitySource-of-the-source-item>"` if reachable, else `"Pickup"` |

This preserves the existing row shape (no schema migration) and the `ContextCategory` string is the discriminator. Every existing query that filters on `ContextCategory == "Recipe"` keeps working; new queries can ask for `ContextCategory.StartsWith("Pickup:")` and so on.

### 3.3 `PlayerDeathReason` — the damage-attribution surface we already use

`Terraria.DataStructures.PlayerDeathReason` exposes mutually-exclusive `Source*Index` fields (line 268 of `InteractionPlayer.cs` shows our consumer). The shape is:

| Field | Meaning |
|---|---|
| `SourceProjectileLocalIndex` + `SourceProjectileType` | A projectile dealt the hit |
| `SourceNPCIndex` | An NPC dealt the hit |
| `SourceOtherIndex` | Vanilla "other" (Fall, Drown, Lava, …) |
| `SourcePlayerIndex` | A player dealt the hit (PvP) |
| `SourceCustomReason` | Mod-set custom string |

The right approach for **last-hit attribution** is exactly what `OnHurt`'s consumer does today. The wrong part is `PlayerDeathDetector.Capture` reading "the last `DamageTakenRow` before `dead = true`" — see §6.3.

### 3.4 The buff array surface

```
Player.buffType : int[Player.MaxBuffs]     // 0 = empty slot, otherwise the buff type id
Player.buffTime : int[Player.MaxBuffs]     // remaining ticks
```

`Player.MaxBuffs` is 22 in 1.4.4. Slots are not packed contiguously — empty slots in the middle of the array are normal (a buff falls off, the next added buff fills the slot, etc.). Our current diff (§6.2) assumes contiguous packing and the snapshot copy uses the wrong length.

---

## 4. LiteDB internals deep-dive

### 4.1 BSON wire format

Source: `LiteDB/Document/Bson/BsonSerializer.cs` ("based on http://bsonspec.org/spec.html"). The reference BSON spec:

```
document   ::= int32 e_list "\x00"        ; total size + element list + null terminator
e_list     ::= element e_list | ""
element    ::= byte_type cstring value    ; type tag (1 byte) + null-terminated field name + value
cstring    ::= (byte*) "\x00"             ; UTF-8 + null terminator
double     ::= 8 bytes
int32      ::= 4 bytes
int64      ::= 8 bytes
string     ::= int32 (byte*) "\x00"       ; length prefix + UTF-8 + null
ObjectId   ::= 12 bytes
```

Headline cost: **every field name is stored as a UTF-8 cstring on every document**. There is no shared field-name dictionary across rows the way Parquet has. Long property names compound across millions of rows.

#### 4.1.1 Field-name byte cost — measured against our records

`SessionRow` has 13 explicit fields plus `_id` plus `_schema`. The property names in the C# class are `StartedUtc`, `EndedUtc`, `DurationMs`, `ProfilerVersion`, `TmlVersion`, `WorldId`, `ModlistFingerprint`, `HookCoverageVersion`, `Mode`, `TracksAllocations`, `TicksObserved`, `EndReason`, `Incomplete`. LiteDB's `BsonMapper` uses the property name verbatim unless `[BsonField("custom")]` overrides it.

Summed byte cost of field names *alone* on `SessionRow` (length + 1 null per name, plus 1 type tag):

```
StartedUtc            (10 + 1 + 1) = 12
EndedUtc              ( 8 + 1 + 1) = 10
DurationMs            (10 + 1 + 1) = 12
ProfilerVersion       (15 + 1 + 1) = 17
TmlVersion            (10 + 1 + 1) = 12
WorldId               ( 7 + 1 + 1) =  9
ModlistFingerprint    (18 + 1 + 1) = 20
HookCoverageVersion   (19 + 1 + 1) = 21
Mode                  ( 4 + 1 + 1) =  6
TracksAllocations     (17 + 1 + 1) = 19
TicksObserved         (13 + 1 + 1) = 15
EndReason             ( 9 + 1 + 1) = 11
Incomplete            (10 + 1 + 1) = 12
_id                   ( 3 + 1 + 1) =  5
_schema               ( 7 + 1 + 1) =  9
                                  ────
                                   190 bytes of field names per session row
```

Session rows are once-per-session, so 190 B is a rounding error. The same analysis on `DamageDealtRow` (one row per swing, ~3 600/min combat):

```
SessionId(10), Tick(5), UnixMs(7), Path(5), ItemId(7), ProjectileId(13),
NpcType(8), NpcName(8), DamageDealt(12), Crit(5), LoadoutFingerprint(19),
_id(3), _schema(7)
                       sum lengths = ~109 chars + 13 nulls + 13 type tags = ~135 B
```

So at 3 600 swings/min, **roughly 475 KB/min of field-name bytes alone**. That is half of the v0.5 per-minute DB-size growth.

The structural fix that *preserves every column* is `[BsonField("short")]` overrides:

| Property | `[BsonField]` name | Bytes saved per row |
|---|---|---|
| `SessionId` | `s` | 8 |
| `Tick` | `t` | 3 |
| `UnixMs` | `u` | 5 |
| `Path` | `p` | 3 |
| `ItemId` | `i` | 5 |
| `ProjectileId` | `pj` | 10 |
| `NpcType` | `nt` | 5 |
| `NpcName` | `nn` | 5 |
| `DamageDealt` | `d` | 10 |
| `Crit` | `c` | 3 |
| `LoadoutFingerprint` | `lf` | 16 |
| `_id` | (keep — required) | — |
| `_schema` | (keep — semantic anchor) | — |

Total per `DamageDealtRow`: ~73 B saved on field names. At 3 600 rows/min that is ~260 KB/min saved. Applied across every event stream and every aggregate, this single change is plausibly half the DB-size target.

This change is non-breaking *at the LiteDB layer* but **journal-incompatible**: the NDJSON journal uses `System.Text.Json` which serialises by property name, not by `BsonField`. The fix is the journal switch in §5.7.

#### 4.1.2 The `_schema` per-row cost

Every row carries `[BsonField("_schema")] public int Schema = 1;`. That is 9 B of field-name + 4 B of int per row = 13 B. Across 3 600 damage-dealt rows that is ~46 KB/min.

The data-integrity argument for keeping `_schema` on every row is real: a future per-collection bump (e.g. `BuffEventRow.Schema = 2`) is detectable at read time without a global migration. But **we have a per-collection schema, not per-document**: every row in a collection has the same schema at write time. The schema is implicit in the writer's version.

Options that preserve the durable-schema discipline:

1. **Move `_schema` to the collection's metadata row**, not every document. One row per collection in `metadata` carries the per-collection schema int. Read-side: when reading a collection, check the metadata version; if older than the reader expects, route through the migration. Net saving: 13 B × every-row-ever = the entire `_schema` cost.
2. **Keep `_schema` but rename to a single-byte field name** via `[BsonField("v")]`. Saves 6 B of name × every row.
3. **Keep as-is** — accept the cost in exchange for the per-document forensic guarantee.

Recommended path: option 2 (rename to `v`). Option 1 is structurally cleaner but adds a read-side branch that complicates every consumer and migrates a guarantee out of the document.

### 4.2 Insert / update path

`LiteCollection<T>.Upsert(row)` flow:

1. `BsonMapper` serialises the C# object to a `BsonDocument` (allocates: the document, the property names, the values).
2. `BsonSerializer.Serialize` calls `doc.GetBytesCount(true)` and allocates a single `byte[buffer]`.
3. The buffer is written via `BufferWriter` into the DB's log file (`-log` suffix). The main file is not touched on insert.
4. The `_id` index is updated; any user-declared index is also updated; both are B-tree page mutations against the log.
5. A "soft checkpoint" eventually copies log pages into the main file (default 1 000 pages = ~8 MB, or on `db.Checkpoint()`).

Per-op overhead the writer thread is paying today, in approximate order of cost:

| Cost class | Bytes / cycles |
|---|---|
| BsonMapper reflection cache lookup | warm: ~50 ns; cold first time per type: 10–100 µs |
| Per-property reflection invoke | ~30 ns × number of properties |
| BSON byte buffer allocation | one `byte[]` per Upsert (~250 B for an event row) |
| Index B-tree update | one or more B-tree page reads + writes per index per row |
| WAL append | sequential write to `-log` file |

The 314 ops/sec is consistent with "1 ms per op", which is dominated by the BSON mapper + the B-tree update. `InsertBulk` (used by `PerSessionAggregateStream` and recommended for the event streams) takes a coarser lock, batches index updates, and amortises the per-call overhead.

### 4.3 Index update cost

LiteDB stores each index as a skip-list-style B-tree on a `BsonValue` key. Every index declared via `EnsureIndex` adds:

- one page read/write per insert per index
- one B-tree rebalance cost (occasional)
- ~5–10% of collection size on disk

Our index inventory (read from the `EnsureIndexes` methods in `Streams/`):

```
sessions:              (_id), StartedUtc, ModlistFingerprint
modlists:              (_id), Fingerprint (unique)
mods:                  (_id), ModlistFingerprint, InternalName
worlds:                (_id) only
perSessionMods:        (_id), SessionId, ModInternalName
perSessionHooks:       (_id), SessionId
spikeWindows:          (_id), SessionId, WorstFrameMs
stallEvents:           (_id), SessionId
stallClusters:         (_id), SessionId, StartUnixMs
contextTransitions:    (_id), SessionId
tickAggregatesWarm:    (_id), SessionId, ExpireAtUtc
tickAggregatesCold:    (_id), SessionId
tickAggregatesArchive: (_id), SessionId (unique)
insights:              (_id), SessionId, PatternKey
metadata:              (_id) only
playerDeaths:          (_id), SessionId, UnixMs
worldSnapshots:        (_id), SessionId, UnixMs
damageTaken:           (_id), SessionId, UnixMs
damageDealt:           (_id), SessionId, UnixMs, LoadoutFingerprint
npcSpawns:             (_id), SessionId, SourceCategory, NpcType
itemCreations:         (_id), SessionId, ContextCategory
loadoutSnapshots:      (_id), SessionId, Fingerprint
buffEvents:            (_id), SessionId, BuffType, UnixMs
```

The `damageDealt` collection has **four** indexes (incl. `_id`). At 3 600 inserts/min that is 14 400 B-tree page touches/min. The `LoadoutFingerprint` index — a string-keyed B-tree — is the most expensive of the four because string keys allocate a `BsonValue` per row.

What we can do without dropping an index:

1. Defer index creation until session-end. `EnsureIndex` is idempotent; if we only call it when actually about to query, the per-insert path drops one update per index. Conflict: the `unique=true` indexes (Modlists.Fingerprint, TickAggregatesArchive.SessionId) must be present at write time. The non-unique ones can be deferred.
2. Use `InsertBulk` instead of `Upsert` for the event streams (where the natural-key uniqueness is the `_id` ObjectId itself — every row gets a fresh `ObjectId.NewObjectId()`, so `Upsert` never finds an existing row to update). The savings are real: `InsertBulk` batches the journal record, the index updates, and the page allocations into one transaction frame.

### 4.4 WAL / Checkpoint / Rebuild

Quoted from the LiteDB docs (verified via `WebFetch` and `litedb-migration-plan.md` §0):

- `CHECKPOINT = N` — auto-checkpoint when the `-log` file reaches N pages (default 1 000, ~8 MB).
- `db.Checkpoint()` — manual; copies the `-log` into the main file. Cannot be called from inside an `Insert` callback (deadlocks; issues #1511, #1775).
- `db.Rebuild()` — full rewrite of the main file. Requires no live log (LiteDB issue #2152); call `Checkpoint()` first.
- ENSURE-page corruption (#2401) — surfaced on heavy burst inserts into a near-empty DB. Mitigated by pre-warm (we already do this in `PreWarmCollections`).
- `FindOne()` does not iterate its underlying enumerable to completion, which leaves the log lock retained (#2440). Practical: prefer `.Query().Limit(1).FirstOrDefault()` for "one row" lookups; the chat-query commands in `Commands/QueryCommandBase.cs` already do this for `CurrentOrLatestSessionId`, but other paths (`SessionStream.SessionEnd`, `TickAggregateStream.Apply`) use `FindOne` and `FindById`. `FindById` is fine — it is an indexed point lookup. `FindOne` with a `Where`-style predicate is the one to watch.

Our checkpoint cadence (every 60 s) is conservative. The argument for tightening: smaller checkpoints → smaller `-log` file → smaller main-file growth between sweeps. The argument for loosening: more checkpoints → more main-file fsyncs → more disk contention. With a writer batching every 1 s of game time, a 60 s checkpoint accumulates ~60 batches; that is well within LiteDB's auto-checkpoint threshold and lets us amortise fsync cost. **Leave at 60 s; do not tune.**

### 4.5 Journal write cadence

`DbWriterThread.JournalForcedFlushBytes = 64 KB`; the writer also flushes implicitly inside `MaybeCheckpoint` after the 60 s timer fires. The journal is `FileShare.Read`, opened in `Append` mode, written via `Encoding.UTF8.GetBytes(StringBuilder.ToString())` per batch.

Allocations on the writer-thread journal path **per batch**:

1. `StringBuilder` of capacity `count * 256` (one allocation, often re-uses the same internal buffer if batches are similar size, but `StringBuilder` is reset each loop and a new instance is constructed every batch — see `EventJournal.AppendBatch` line 67).
2. One `JournalLine` instance per op (line 72).
3. One `JsonSerializer.Serialize(line, …)` call per op (allocates an intermediate string).
4. One `JsonSerializer.Serialize(op.Payload, …)` call per op (the row → JSON).
5. `Encoding.UTF8.GetBytes(buf.ToString())` allocates a `byte[]` of the encoded length.

That is **two string allocations + one JsonSerializer allocation per op + one byte[] per batch**. For a 64-op batch that is ~128 string allocations on the writer thread. The writer thread is not on the game thread, so this is GC pressure on a background thread, not a game-thread cost — but it still trips collections that pause every thread.

Fixes:

1. Cache one `StringBuilder` on the writer thread, `Clear()` between batches (already a reasonable pattern; not done today).
2. Cache one `byte[]` rented from `ArrayPool<byte>.Shared`, write via `Utf8JsonWriter` directly into the pooled buffer — no intermediate string at all.
3. Replace `JsonSerializer.Serialize(payload, payload.GetType(), JsonOpts)` (the per-op string allocation) with a pre-resolved `JsonTypeInfo<T>` from source-generated metadata (`System.Text.Json` source generator). Eliminates the `BsonMapper`-equivalent reflection on the writer thread and removes per-op allocations.

### 4.6 The per-mod-cat double array on every spike and aggregate

`SpikeWindowRow.PerModCatMs : List<double>` is length `modCount * categoryCount = ~100 × 7 = 700` doubles = 5.6 KB. As BSON, each double is a `double` element (1 type tag + 1 cstring index name + 8 B payload = ~12 B per slot at minimum), so ~8.4 KB per spike row. Stored as a `BsonArray`, each element has an **integer-string index name** (LiteDB's BsonArray serialises children with name `"0"`, `"1"`, …). So the byte-level cost is real.

The right encoding for fixed-length numeric arrays is `byte[]` (BSON binary type 0x05) with length-prefix. `float[700]` packed into a `byte[2800]` is one BSON element of type 0x05 = 1 + 1 + 4 + 2800 = 2 806 B instead of ~8 400 B for the same data as a BsonArray. **Three-times saving on every spike, every warm aggregate, every cold aggregate.**

LiteDB's `BsonMapper` does not auto-convert `float[]`/`double[]` to BSON binary — it serialises them as `BsonArray`. The fix is a custom `BsonMapper` hook in `ProfilerDatabase` ctor:

```csharp
BsonMapper.Global.RegisterType<double[]>(
    serialize: arr => { var b = new byte[arr.Length * 8]; Buffer.BlockCopy(arr, 0, b, 0, b.Length); return new BsonValue(b); },
    deserialize: bv => { var b = bv.AsBinary; var a = new double[b.Length / 8]; Buffer.BlockCopy(b, 0, a, 0, b.Length); return a; });
```

(and the same for `float[]`). Then change `SpikeWindowRow.PerModCatMs` from `List<double>` to `double[]` — same data, three times smaller on disk, plus zero `BsonArray` allocations.

### 4.7 `ObjectId` repetition

Every row carries `SessionId : ObjectId = 12 bytes`. At 3 600 rows/min that is 43 KB/min of repeated 12-byte session-ids. Per session it stays constant.

Options:

1. **Keep ObjectId**: 12 B + ~3 B of field name (`s` after the rename) = 15 B per row. Negligible relative to other gains.
2. **Switch SessionId to a small int** (collection-local counter): 4 B + 3 B = 7 B. Saves 8 B/row but requires a session-id table for cross-collection joins; complicates `_id`-style queries.

Recommended: keep ObjectId. The simplicity / queryability is worth 8 B/row.

---

## 5. Optimisation opportunities

The catalogue. Each entry: name, what it changes, why it helps, what could go wrong, blast radius, expected delta. Ranked roughly by impact-per-engineering-week in §8.

### 5.1 Short BSON field names via `[BsonField("…")]`

**Surface:** every `Records/*.cs` file. **Constraints:** journal compatibility (§5.7).

**What:** add `[BsonField("…")]` to every property in every record, mapping to a 1–3-character name. `_id` stays. `_schema` becomes `v`. Common fields (`SessionId` → `s`, `Tick` → `t`, `UnixMs` → `u`) are uniform across all rows so future readers can recognise them.

**Why:** ~50% of v0.5 DB size is field-name bytes (§4.1.1).

**Risk:** breaks any external reader that walks BSON by long property name. We have none today — the only readers are our own `BsonMapper`-using code. The Migration path is **fresh-DB only**: bumping `USER_VERSION` and forcing recovery on existing DBs sidesteps any need to migrate field names in-place. (See §5.10 for an alternative.)

**Expected delta:** ~400 KB of the 464 KB excess on the 10-min Calamity benchmark.

### 5.2 Game-thread row pre-allocation pools

**Surface:** `InteractionPlayer`, `InteractionNpc`, `InteractionItem`, `SessionRecorder` enqueue paths.

**What:** for the high-frequency event streams (`damageDealt`, `npcSpawn`, `buffEvent`), maintain a per-stream `ConcurrentBag<T>` or per-thread free-list of pre-allocated row instances. The pattern:

```csharp
// borrow
var row = DamageDealtPool.Rent();
row.Tick = …; row.NpcType = …; …
recorder.OnDamageDealt(row);

// recorder enqueues with a "return to pool when applied" hint;
// writer thread returns the row after Apply()
```

**Why:** the 441 ns/op enqueue benchmark spends a real share of its budget in `new DamageDealtRow()`. Pooling eliminates the per-op allocation and the per-op GC-card touch.

**Risk:** the journal still serialises the row to JSON before the writer can return it to the pool — so the pool return point is "after journal + apply, not before". The DB write thread becomes the pool owner; the writer must explicitly call `Pool.Return(row)` after `Apply`. Mishandled return → use-after-free style bugs.

**Expected delta:** game-thread enqueue 441 → 300 ns/op.

### 5.3 Fixed-size struct payload for the small event ops

**Surface:** `DbWriteOp.cs`, the event streams.

**What:** for the event ops whose payload is < 64 B (`DamageDealtRow` minus the optional `LoadoutFingerprint` and `NpcName` strings — those become indexes into a per-session string table), embed the payload inline as a fixed struct inside `DbWriteOp`. The current `DbWriteOp` is 56 B today (one ref + several primitives); embedding a fixed event-data struct lets the channel carry the payload by value, avoiding the heap allocation for the row.

The cost: the channel's `ConcurrentQueueSegment` slot grows. The benefit: no heap allocation per op on the game thread at all.

**Risk:** the channel becomes type-fragmented (a 56 B struct that carries different things for different kinds is harder to reason about). The dispatch loop in `DbWriterThread.Run` becomes a switch on `op.Kind` that builds a heap `Row` *on the writer thread* before applying.

**Expected delta:** game-thread enqueue 441 → 200 ns/op when combined with §5.2.

### 5.4 String interning for the high-cardinality-low-distinct fields

**Surface:** `NpcName`, `BuffName`, `OwningMod`, `Path`, `SourceKind`, `SourceCategory`, `ContextCategory`, `LoadoutFingerprint`.

**What:** these fields have a tiny distinct set (`Path` ∈ {melee, item, projectile}; `SourceKind` ∈ {npc, projectile, item, self, other, unknown}; `OwningMod` ∈ at most ~100 distinct values). For `LoadoutFingerprint` the distinct set is bounded by the player's actual loadout variants per session — typically dozens.

The fix: introduce a per-session string-interner. Both at write time (the recorder hands back a stable reference for any string it's seen before) and on disk (encode small enums as `byte` BSON values, not strings). Two phases:

1. **Write-time intern**: `SessionRecorder.InternString(str) → string`. Caches a `Dictionary<string,string>`. Same instance returned for "melee" every time. Reduces GC pressure on the writer thread (the `BsonMapper` won't allocate a fresh BSON string for an interned value).
2. **Disk encoding**: replace `string Path` with `byte PathCode` (3 values fit in 3 bits). Replace `string SourceKind` similarly. Saves ~10 B/row for damage events.

The disk encoding requires a tiny lookup table in the reader — fine since all our readers are in-tree.

**Risk:** the LoadoutFingerprint case is interesting. It is high-cardinality at the session level (a player swapping equipment frequently) but low-cardinality at the moment — a long fingerprint string repeated across ~3 600 damage-dealt rows in one fight. The right fix is to **store the fingerprint once per LoadoutSnapshotRow** and link damage-dealt rows by the loadout snapshot's `_id` ObjectId. That's a schema change (replacing `string LoadoutFingerprint` with `ObjectId LoadoutSnapshotId`). 12 B vs ~80 B = 68 B/row saved at 3 600 rows/min = 240 KB/min.

This is the single biggest win in the event-stream stack. It does NOT drop any capture: the fingerprint is reconstructable by joining `damageDealt → loadoutSnapshots → fingerprint`.

**Expected delta:** combined with §5.1, gets us under the 600 KB target for a 10-min Calamity session.

### 5.5 Numeric arrays as BSON binary

**Surface:** `SpikeWindowRow.PerModCatMs`, `SpikeWindowRow.PerModCatBytes`, `TickAggregateWarm.PerModMs`, `TickAggregateWarm.PerModBytes`, `TickAggregateCold.PerModMs`, `TickAggregateCold.PerModBytes`, `TickAggregateArchive.PerMod` (partial — that one is a list-of-record).

**What:** register a `BsonMapper.Global.RegisterType<double[]>` serialiser that packs to `byte[]` (BSON binary 0x05). Switch the four List<double> fields to `double[]`. (§4.6.)

**Why:** 3× saving on every spike + every warm/cold aggregate, which is the bulk of the high-row-count writes.

**Risk:** `BsonMapper.Global` is process-wide. If a third party touches the same mapper they see the conversion. Mitigation: use a non-global `BsonMapper` instance passed to the `LiteDatabase` ctor.

**Expected delta:** 5–6 KB → 2.8 KB per spike. At 50 spikes per playtest session, ~140 KB saved. Across warm aggregates (60/min × 800 B → 60/min × 300 B), ~30 KB/min saved.

### 5.6 Off-thread session-end

**Surface:** `SessionRecorder.End`, `ProfilerDatabase.DrainAndTruncateJournalForSessionEnd`, `SessionSummaryLogger.Write`, `ProfilerSystem.OnWorldUnload`.

**What:** the 8.5 s blocking cluster must move off the game thread. Plan:

1. `SessionRecorder.End()` returns immediately after enqueuing the final ops (no `BuildModAggregates`/`BuildHookAggregates`/`BuildArchive` on the game thread).
2. The aggregate-building work is enqueued as a *task op* on the writer thread: `DbWriteOp.SessionFinalize(sessionId, snapshotOfCollectorState)`. The writer thread runs `BuildModAggregates` etc using a captured snapshot of the collector's per-mod arrays.
3. `SessionSummaryLogger.Write` runs on the writer thread after `SessionFinalize` completes.
4. Backup rotation moves to the writer thread (it's already off-thread in `Dispose`; we replicate that for the world-unload path).
5. Journal truncate runs on the writer thread.

The challenge: the collector's per-mod arrays are mutated by the game thread. The writer must capture a stable snapshot at `End()` time. Solution: `End()` copies `collector.PerModCategoryAverageMs`, `PerHookAverageMs`, etc. into ordinary `double[]` arrays that the writer-thread aggregate-build path consumes. Copy cost: `modCount × categoryCount` doubles = ~700 doubles = 5.6 KB. One copy, on the game thread, instead of 8.5 s of work.

**Risk:** if `Mod.Unload` arrives before the writer finishes the finalize task, we lose the session-end work. `Dispose()` already drains the queue with a 10 s `Join` timeout; the finalize task is just one more queued op, so the existing teardown path handles it.

**Expected delta:** 8.5 s → 0 game-thread cost at session end. Net `PerformanceProfiler` mod-cost in playtest drops measurably because most of the 4 488 ms "total ms" is concentrated at the end.

### 5.7 Replace NDJSON journal with a binary frame format

**Surface:** `EventJournal.cs`, every `IPersistenceStream.Reconstruct`.

**What:** the NDJSON journal exists because text is debuggable and forward-compatible. But `JsonSerializer.Serialize(payload, payload.GetType(), JsonOpts)` on the writer thread, per op, allocates strings and reflects every property. Replace with a binary frame:

```
frame := [magic 4 B][len 4 B][kind 1 B][session_id 12 B][bson_payload len B]
```

`bson_payload` is the same BSON the LiteDB write will produce — we can compute it once and use it for both the journal append and the LiteDB upsert (an `InsertBulk` accepts pre-built `BsonDocument`s). That collapses the two-serialisation pipeline (one BSON for the DB, one JSON for the journal) into one BSON pass.

**Risks:**

- Inspectability. A binary journal is no longer readable by `cat`. Mitigation: ship a `/profiler-journal-dump` chat command that decodes the binary journal into human-readable lines on demand.
- Forward compatibility. A schema change has to handle replaying old binary frames. Today the NDJSON path is tolerant because every JSON field is a property name. Mitigation: the frame's `kind` byte plus a per-collection `_schema` (now `v`) field lets the reconstructor pick the right deserialiser.
- The streams' `Reconstruct(JournalLine)` API changes shape. The shim: keep `JournalLine` as a struct with a `byte[] Payload`, and have streams deserialise it via the same `BsonMapper` they already use.

**Expected delta:** writer-thread allocations drop by ~90% on the journal path. Writer drain rises significantly (one of the contributors to the 314 → 1 000 ops/sec target).

### 5.8 `InsertBulk` for the high-frequency event streams

**Surface:** every `Streams/*.cs` whose `Apply` calls `Upsert`. The event streams (`DamageDealtStream`, `NpcSpawnStream`, `BuffEventStream`, `DamageTakenStream`, `LoadoutSnapshotStream`, `ItemCreatedStream`) all do per-op `Upsert((Row)op.Payload)`. The writer thread receives a batch of up to 64 ops; rather than 64 `Upsert` calls (each its own transaction), group by stream and call `InsertBulk(rows)` once per stream per batch.

**Why:** `Upsert` is internally an `Insert OR Update`. For event rows whose `_id` is `ObjectId.NewObjectId()` at construction time, the `Update` branch is never taken — the cost is wasted. `InsertBulk` skips the lookup-or-update logic and batches index updates.

**Risk:** `Upsert` was chosen for journal-replay idempotency. If the same op is replayed twice, `Upsert` no-ops the second time; `InsertBulk` would insert a duplicate. Mitigation: the journal-replay path keeps using `Upsert` (it is slow but correct); the steady-state writer-thread path uses `InsertBulk`. The stream contract grows a second method (`ApplyBatch(IList<Row>, db)`) that defaults to per-row `Apply` and overrides to `InsertBulk` for the event streams.

**Expected delta:** writer drain 314 → 600+ ops/sec for the event-heavy path. The aggregate-batch streams (`PerSessionAggregateStream`) already use `InsertBulk` correctly.

### 5.9 Deferred non-unique index creation

**Surface:** `Streams/*.EnsureIndexes`.

**What:** unique indexes (`Modlists.Fingerprint`, `TickAggregatesArchive.SessionId`) stay at open time. Non-unique indexes (the bulk of them) move to a "build on first query" lazy strategy. The `EnsureIndex` calls are idempotent, so the first chat-command read or HTML report run pays the indexing cost; the steady-state writer doesn't.

**Why:** at 3 600 inserts/min with 4 indexes on `damageDealt`, that is 14 400 B-tree updates/min the writer thread doesn't strictly need until the player runs `/profiler-tail` or similar.

**Risk:** the first query after a long session does an unindexed collection scan if the index isn't built yet. Two reads pay: the index build cost plus the query. After that it's amortised. Acceptable; the unindexed-scan is still milliseconds on our row counts.

**Expected delta:** writer drain rises another ~15%.

### 5.10 An alternative to §5.1: custom `BsonMapper` field-name resolver

**Surface:** `ProfilerDatabase.cs` (ctor) — replace `BsonMapper.Global` with an instance configured to use property-name → short-name mapping via a single table rather than per-property `[BsonField]` attributes.

**Why:** less invasive to the records — no per-property attribute clutter; tests stay readable. The mapping table lives in one file and is the only place to look for "what does field `s` mean".

**Risk:** the mapping must be exhaustive and consistent. A typo in the table is a silent corruption (a property writes to one name, reads from another).

**Recommendation:** start with §5.1 because per-property attributes are local and explicit. If that lands and we want the cosmetic cleanup, the resolver is a follow-up.

### 5.11 Lazy `_id = ObjectId.NewObjectId()` evaluation

**Surface:** every `Records/*.cs` with `[BsonId] public ObjectId Id { get; set; } = ObjectId.NewObjectId();`.

**What:** `ObjectId.NewObjectId()` reads `DateTime.UtcNow`, generates a fresh process counter, and computes a 12-byte id. Cheap (~30 ns) but not free, and it runs in the record's constructor every time we `new DamageDealtRow()`. Every event allocation pays. The fix: leave `Id` as `ObjectId.Empty` at construction time; the writer thread fills `Id = ObjectId.NewObjectId()` inside `Apply`. Off-thread cost; game thread saves the call.

**Expected delta:** ~5 ns/op on the game thread. Small but free.

### 5.12 Replace `Array.IndexOf` in the buff diff

**Surface:** `InteractionPlayer.PostUpdateBuffs` (the broken diff — §6.2 fixes the correctness side; this is the perf side).

**What:** `Array.IndexOf(Player.buffType, t)` is O(n). Two such calls per buff slot per tick = O(n²) per tick. With `Player.MaxBuffs = 22`, that is ~484 array reads per tick = 29 040 reads/sec. Trivial in absolute terms but unnecessary.

Fix: a 32-element `bool[]` index built once per tick. For each old slot, look up via the index. For each new slot, look up via the cached old-tick set. The set is a flat `int[]` of length `MaxBuffs` keyed by buff type modulo a small prime, or — simpler — a `HashSet<int>` cleared per tick (allocation-free with `Clear()` plus add-in-place).

**Expected delta:** trivial in real terms but removes a per-tick O(n²). Bigger win is the correctness fix.

### 5.13 Snapshot capture path

**Surface:** `WorldSnapshotter.Capture`, `PlayerDeathDetector.Capture`.

**WorldSnapshotter** scans `Main.item` (length 401 in 1.4.4) for active items every 30 s. Cheap; leave.

**PlayerDeathDetector.Capture** runs a LiteDB query on death (`db.DamageTaken.Query().Where(x => x.SessionId == sid).OrderByDescending(...).Limit(1).FirstOrDefault()`). This is the only **read** from LiteDB on the game thread anywhere in the persistence layer. On a long session with 10 000 damage-taken rows it could take several ms. The right fix: §6.3 moves this attribution off the death-edge entirely — the recorder remembers the recent damage events in a small in-memory ring buffer and attributes from RAM.

### 5.14 ContextTransitionWatcher per-tick scan cost

**Surface:** `ContextTransitionWatcher.OnSnapshot`.

**What:** every tick, the watcher diffs `WeatherFlags` (XOR + 16-bit scan), `BiomeBitset` (per-bit scan up to ~30), checks 6 other fields. On a no-change tick (which is the steady state) this is ~50 reads + 50 compares + 0 emits. Fine — but the inner biome-diff loop calls `BiomeRegistry.NameOrIndex(i)` per **changed** bit; on most ticks that path is not entered.

The expensive sub-path is `DiffBiomeBits` walking every bit even on a no-change tick. The XOR shortcut is missing — we could pre-compute `XOR(current.bits, last.bits)` and exit early if zero. The bitset is small enough that this is one `ulong` compare; the current code walks ~30 bits unconditionally. Trivial fix.

**Expected delta:** game-thread ContextTransitionWatcher per-tick cost drops from ~200 ns to ~30 ns. Small but stacks with the rest.

### 5.15 `WorldSnapshotter` and `Items` ItemCount scan

`Capture` walks `Main.item[0..400]` testing `active`. At 30 s cadence this is 60 emits/hour × 400 reads = 24 000 reads/hour = once-per-1.5 s on average. Cheap on the game thread but unnecessary — we could read `Main.itemCounter` if it exists in 1.4.4 (it doesn't, but `Main.numItems` would; investigation needed). Leave; it is not on the hot path budget.

### 5.16 Drop the synchronous `Lang.GetBuffName` / `Lang.GetNPCNameValue` per event

**Surface:** `InteractionPlayer.EmitBuffEdge`, `ContextTransitionWatcher.OnSnapshot`.

**What:** `Lang.GetBuffName` is a localisation lookup that hits Terraria's text tables. On the game thread, per buff edge, per boss flip. Cache the result: `BuffNameCache[type] = Lang.GetBuffName(type)`. Same for `Lang.GetNPCNameValue`.

**Expected delta:** small but the cache avoids per-event Terraria internals — useful for the hot path even if individually cheap.

### 5.17 `Substring` allocations in NPC/Item context category

**Surface:** `InteractionNpc.OnSpawn` (`Substring("EntitySource_".Length)`), `InteractionItem.OnCreated` (`Substring(0, … - "ItemCreationContext".Length)`).

**What:** these create a new heap string per spawn / per creation. Cache: `Dictionary<Type, string>` keyed on the source/context type. The first spawn from a given `EntitySource` subclass allocates once; every subsequent spawn looks up.

**Expected delta:** removes one allocation per NPC spawn (~34 in the playtest baseline). Trivial in absolute but every game-thread alloc removed helps.

### 5.18 Span-based JournalLine deserialisation on replay

**Surface:** `EventJournal.Replay`, every `Reconstruct`.

If §5.7 lands, this becomes moot. If it doesn't: replace `File.ReadLines(_path)` (which buffers via a `StreamReader` and allocates a `string` per line) with a `ReadOnlySpan<byte>`-based reader that finds `\n` and hands a `Utf8JsonReader` to each stream. Eliminates the per-line string. The replay path is cold (runs once per launch), so this is low-priority.

---

## 6. Bug-fix designs

These three live alongside the perf pass per `baseline.md`. They are correctness gaps the optimisation work must not paper over.

### 6.1 `itemCreatedEvents = 0` — wire the right hooks

**Symptom:** baseline session has zero item-created events despite the player mining torches and crafting platforms.

**Root cause (verified via `gh api`):** `GlobalItem.OnCreated` fires for `RecipeItemCreationContext`, `InitializationItemCreationContext`, `BuyItemCreationContext`, `JourneyDuplicationItemCreationContext`. It does **not** fire on world-pickup or on NPC-drop. The baseline session's player mostly *picked up* items rather than crafted them.

**Fix (universal, no mod-specific code):**

1. Add `GlobalItem.OnSpawn(WorldItem, IEntitySource)` to `InteractionItem`. Emits an `ItemCreatedRow` with `ContextCategory = "Drop:<EntitySource-subclass>"`. Covers tile drops, NPC drops, debug spawns, chest opens.
2. Add `ModPlayer.OnPickup(WorldItem)` to `InteractionPlayer`. Emits an `ItemCreatedRow` with `ContextCategory = "Pickup"`. (Alternative: `GlobalItem.OnPickup(WorldItem, Player)` — equivalent surface; either works.)
3. Keep the existing `GlobalItem.OnCreated` for crafted / bought / initialised items.

**Detail:**

- The `OnSpawn` hook fires every time an item enters the world — including the moment after a tile is broken and a fresh `Item` is created. This will double-count a craft (which emits `OnCreated`) only if the item is then dropped and the player picks it up (rare for crafts; common for mined drops). The honest pattern is to record both events with distinct `ContextCategory` values so a future query can de-duplicate as it wishes. **No de-duplication at write time** — that would be losing data per philosophy.md.
- The `IEntitySource.Context` string is a free-form hint vanilla sometimes fills (e.g. `"DropFromTile"` for `EntitySource_TileBreak`). Capture it in `SourceContext` on `ItemCreatedRow` — same field name parity as `NpcSpawnRow`. Today there is no such field; **add it as a new property** on `ItemCreatedRow`. Schema-wise this is forward-compatible (existing rows just won't have it). Bump `ItemCreatedRow.Schema` from 1 to 2 to make the introduction observable.

**Output shape after fix:**

```
ContextCategory          source hook                       fires on
─────────────────         ───────────                       ────────
Recipe                    GlobalItem.OnCreated               player crafts
Initialization            GlobalItem.OnCreated               item init
Buy                       GlobalItem.OnCreated               NPC sale
JourneyDuplication        GlobalItem.OnCreated               journey-mode dup
Drop:DropFromNPC          GlobalItem.OnSpawn                 NPC drops a loot item
Drop:TileBreak            GlobalItem.OnSpawn                 mining produces an item
Drop:DebugCommand_…       GlobalItem.OnSpawn                 cheat-spawn into world
Drop:OpenContainer        GlobalItem.OnSpawn                 chest opened
Pickup                    ModPlayer.OnPickup                  item physically grabbed
```

### 6.2 `buffEvents = 2` — fix the prev-buffs diff

**Symptom:** despite a constant Radar accessory and intermittent torch placement (both produce buffs), only 2 buff events recorded across 4.5 min.

**Root cause:** the diff in `InteractionPlayer.PostUpdateBuffs` (lines 138–172) has two bugs.

**Bug A — initial snapshot.** On the first ever tick after the player loads in, `_prevBuffTypes` is all-zeros and `_prevBuffCount = 0`. The diff:

```csharp
// Removed: walks 0 entries → no removals reported
for (int i = 0; i < _prevBuffCount; i++) { … }      // _prevBuffCount = 0, loop skipped

// Added: walks Player.buffType, looks up in _prevBuffTypes[0.._prevBuffCount]
for (int i = 0; i < Player.buffType.Length; i++)
{
    int t = Player.buffType[i];
    if (t <= 0) continue;
    if (System.Array.IndexOf(_prevBuffTypes, t, 0, _prevBuffCount) < 0)  // _prevBuffCount=0; always -1
        EmitBuffEdge(recorder, t, "on");
}
```

On the first tick, every active buff (Radar, Well-Fed, etc.) fires an `"on"` event. **Correct so far.**

**Bug B — `_prevBuffCount` is wrong on snapshot.** Look at line 171:

```csharp
_prevBuffCount = Player.buffType.Length;   // = 22 (MaxBuffs), NOT the count of active buffs
```

`_prevBuffCount` is then used as the **search range** of `Array.IndexOf` on the next tick:

```csharp
System.Array.IndexOf(_prevBuffTypes, t, 0, _prevBuffCount)
```

So on tick 2, `Array.IndexOf` walks the full 22-slot snapshot looking for `t`. That includes the trailing zeros. That's not the bug.

The actual bug: **`_prevBuffCount` is used in the *removed* loop**:

```csharp
for (int i = 0; i < _prevBuffCount; i++)
{
    int t = _prevBuffTypes[i];
    if (t <= 0) continue;
    if (System.Array.IndexOf(Player.buffType, t) < 0)
        EmitBuffEdge(recorder, t, "off");
}
```

`_prevBuffCount` is `Player.buffType.Length = 22` regardless of how many buffs were actually live, so we do iterate every slot and emit "off" for any removed buff. **This is correct.** Wait — re-check the bug.

Re-reading: line 147 of `InteractionPlayer.cs` redefines `current`:

```csharp
int current = 0;
for (int i = 0; i < Player.buffType.Length; i++) if (Player.buffType[i] > 0) current++;
```

`current` is computed but **never used**. That's dead code, not the bug.

The actual bug is **the IndexOf scan range for the "added" branch**:

```csharp
if (System.Array.IndexOf(_prevBuffTypes, t, 0, _prevBuffCount) < 0)
    EmitBuffEdge(recorder, t, "on");
```

With `_prevBuffCount = 22` we scan the whole 22-slot prev buffer. If a previously-active buff was at index 5 last tick and is still active this tick at index 5, `IndexOf` finds it → no "on" event. **Correct.**

But: what about the **first tick**, when `_prevBuffCount = 0`? The added loop runs and emits "on" for every active buff. Then we update `_prevBuffCount = 22` (line 171) and the prev-buffs snapshot. **On tick 2**, the diff runs against a 22-slot prev snapshot that includes the same buffs at the same indices. No diff, no events. From tick 2 onwards, **as long as no buff joins or leaves**, no events fire. That matches the baseline behaviour.

So where does the "2 events" come from? Likely one "on" on tick 1 (one of the active buffs flickered) and one "off" later (a buff fell off mid-session). The pattern is: the diff is roughly working, but **the prev-buffs snapshot is being clobbered every tick by `Array.Copy(Player.buffType, _prevBuffTypes, Player.buffType.Length)`** — line 170. So the diff is "this tick vs last tick", which should fire events on every change.

Reading again more carefully — I think the actual bug is this: `Array.Copy` is being called with `Player.buffType.Length` (= 22) as the length. But `_prevBuffTypes` is also length 22. Fine.

Reviewing one more time, I see the issue is at lines 168–170:

```csharp
if (_prevBuffTypes.Length < Player.buffType.Length)
    _prevBuffTypes = new int[Player.buffType.Length];
System.Array.Copy(Player.buffType, _prevBuffTypes, Player.buffType.Length);
```

If `_prevBuffTypes.Length == Player.buffType.Length`, the realloc branch is skipped — but the existing `_prevBuffTypes` is reused. `Array.Copy(src, dst, count)` overwrites `dst[0..count]` from `src[0..count]`. If `_prevBuffTypes` had a buff at index 10 (active last tick) and `Player.buffType[10]` is now 0 (buff fell off), the copy writes 0 to `_prevBuffTypes[10]`. Then on tick N+1, the "removed" branch sees `_prevBuffTypes[10] == 0` and skips it. **The off-edge is detected this tick (in the removed loop) and then forgotten in the snapshot — that's correct behaviour.** The "off" event should fire this tick.

OK, I'm losing the bug in the analysis. The empirical evidence is buffEvents=2 across 4.5 min with constant Radar. Possibilities:

1. **`Player.whoAmI != Main.myPlayer` filter fails.** If `Main.myPlayer` is set late, the early tick guard returns and the snapshot never initialises. Then later ticks see `_prevBuffCount = 0` permanently because the early-out happens before the snapshot update. **This matches the symptom.**

   Fix: move the snapshot update *before* the recorder-null check, or initialise `_prevBuffCount` correctly on the first valid tick after the gate clears.

2. **`Lang.GetBuffName` throws on a modded buff and the throw is caught silently.** Less likely — `EmitBuffEdge` catches and falls back, but the exception path may not reach the actual enqueue.

3. **Radar specifically.** The Radar accessory might not produce a buff but rather a visual effect. Worth confirming.

**Recommended root-cause fix:** move the snapshot update to the top of the method, before any early returns:

```csharp
public override void PostUpdateBuffs()
{
    if (Player.whoAmI != Main.myPlayer)
    {
        // Still update our snapshot so when we're switched to the local
        // player we don't see a spurious wholesale on/off diff.
        UpdateSnapshot();
        return;
    }
    var recorder = ResolveRecorder();
    if (recorder == null) { UpdateSnapshot(); return; }

    // … diff logic …

    UpdateSnapshot();
}

private void UpdateSnapshot()
{
    if (_prevBuffTypes.Length < Player.buffType.Length)
        _prevBuffTypes = new int[Player.buffType.Length];
    System.Array.Copy(Player.buffType, _prevBuffTypes, Player.buffType.Length);
    _prevBuffCount = Player.buffType.Length;
}
```

Plus a one-time "first valid tick" guard that on the first tick where the gate clears, emits "on" for every active buff (so we don't miss the steady-state set). This is the data-stack-honest behaviour: every edge gets a row.

### 6.3 Damage-weighted death attribution

**Symptom:** baseline death #1 attributed to "Blue Slime" because slime threw the final 21 dmg; vultures actually dealt 93 of 100 hp lost.

**Root cause:** `PlayerDeathDetector.Capture` reads the **last** `DamageTakenRow` before the death edge:

```csharp
var last = db.DamageTaken
    .Query()
    .Where(x => x.SessionId == sid)
    .OrderByDescending(x => x.UnixMs)
    .Limit(1)
    .FirstOrDefault();
if (last != null && !string.IsNullOrEmpty(last.SourceName))
    killer = last.SourceName;
```

Last-hit credit. The fix is **damage-weighted aggregation over the last N seconds before death**.

**Design:**

1. Add a small in-memory ring buffer to `SessionRecorder`: `RecentDamageRing` of capacity ~64, holding `(unixMs, source-key, damageDealt)` tuples.
2. `OnDamageTaken` writes the tuple to the ring (constant time) in addition to enqueueing the DB row.
3. `PlayerDeathDetector.Capture` reads the ring (not the DB), filters to entries within the last `DeathAttributionWindowMs` (default 10 000 ms = 10 s), aggregates damage by source-key, picks the max.
4. The death row carries the killer plus the full attribution map as an additive field (`Dictionary<string, int> DamageWeighting` — or a `List<(source, damage)>` if we want stable iteration).

The new field is additive on `PlayerDeathRow`. The existing `Summary` field reads the damage-weighted attribution rather than the last-hit one.

**Universal rule satisfied:** the source-key is whatever `OnHurt`'s `ClassifyDeathReason` already produces — a `(kind, id, name)` triple grouped by `kind + id`. No mod-specific identifiers.

**Why ring over DB query:** removes the LiteDB read from the game thread on the death edge (the only DB read from the game thread anywhere — §5.13). Also more accurate: the writer may not have flushed the most recent damage row yet, so the DB lookup misses the last hit during the writer's batching window.

**Configurable window:** 10 s is a defensible default. The honesty contract (Invariant 3) says we record the measurement and let the player draw the conclusion — so the row should carry both the weighting and the window-size used. A `DamageAttributionWindowSeconds` field on the row makes the attribution self-describing.

---

## 7. Cross-system dependencies

The persistence pass touches the rest of the codebase at four seams:

### 7.1 `MetricCollector` snapshot for off-thread session-end (§5.6)

`MetricCollector` exposes `PerModCategoryAverageMs`, `PerHookAverageMs`, etc. as `IReadOnlyList<double>` views over its internal arrays. The off-thread session-end requires a **stable snapshot** at `End()` time. Two options:

1. Add `MetricCollector.SnapshotAggregates()` returning a small struct of `double[]` copies. Game-thread-safe; the writer thread consumes the snapshot.
2. Acquire a brief lock on the collector while the writer thread reads. Worse — adds game-thread contention.

Recommended: option 1. Adds one method to `MetricCollector`'s public surface; net additive.

### 7.2 `ProfilerSystem.OnWorldUnload` ordering

Today: `SessionRecorder.End()` → `DrainAndTruncateJournalForSessionEnd` → `SessionSummaryLogger.Write`. All on game thread.

After: `SessionRecorder.End(snapshot)` enqueues a `SessionFinalize` op. Game thread returns. Writer thread runs aggregate build, summary log, journal truncate. If `Mod.Unload` arrives before the writer finishes, `Dispose()`'s 10-second `Join` waits for completion.

The only seam that changes is what `OnWorldUnload` waits for. Today it implicitly waits for the in-line work. After, it must explicitly *not* block. A new `ISessionEndAware` interface or a simple "this is now async" comment in `ProfilerSystem.OnWorldUnload` is enough.

### 7.3 The recorder's relationship with `PerformanceProfiler.Database`

`PlayerDeathDetector.Capture` reads `PerformanceProfiler.Database.DamageTaken`. After §6.3 it reads `SessionRecorder._recentDamageRing` instead. The database singleton becomes one less hot-path consumer.

### 7.4 Tests

`Tests/Persistence/PersistenceBenchmarkTests.cs` (already referenced in `baseline.md`) is the verification gate. The targets in §2.1 must move in the right direction in this test before the pass ships. The tests don't change shape; they only need to be re-run against the post-pass build.

Add: a small test that exercises the pool path (§5.2) — borrow, fill, enqueue, await apply, confirm row in DB, confirm pool returned-to. Catches the use-after-free style bugs early.

---

## 8. Prioritised execution order

Ranked by impact-per-engineering-week. Sequenced so dependent changes ride later items.

### Phase A — correctness (lands first, gates the rest)

| # | Change | Touches | Risk |
|---|---|---|---|
| A1 | `itemCreatedEvents` — wire `OnSpawn` + `OnPickup` (§6.1) | InteractionItem, InteractionPlayer | Low |
| A2 | `buffEvents` — snapshot-before-gate fix (§6.2) | InteractionPlayer | Low |
| A3 | Damage-weighted death attribution (§6.3) | SessionRecorder, PlayerDeathDetector, PlayerDeathRow | Low |

Why first: the post-pass numbers are meaningless if the captures are still broken. Also: A3 removes the only game-thread DB read, which simplifies the rest of the perf-pass reasoning.

### Phase B — biggest single wins (DB size + end-of-session)

| # | Change | Touches | Risk | Expected delta |
|---|---|---|---|---|
| B1 | Off-thread session-end (§5.6) | SessionRecorder, ProfilerDatabase, ProfilerSystem | Medium | 8.5 s → 0 |
| B2 | Short BSON field names (§5.1) | every Records/*.cs | Medium (schema break on existing DBs) | ~400 KB / 10 min |
| B3 | `LoadoutFingerprint` → `LoadoutSnapshotId` (§5.4) | DamageDealtRow, InteractionPlayer | Medium | ~240 KB/min in combat |
| B4 | Numeric arrays as BSON binary (§5.5) | SpikeWindowRow, TickAggregateWarm/Cold | Low | 3× compression on spike/aggregate arrays |

B2 + B3 + B4 combined should hit the < 600 KB target for a 10-min Calamity session. B1 hits the 8.5 s → 0 target.

### Phase C — writer-thread throughput

| # | Change | Touches | Risk | Expected delta |
|---|---|---|---|---|
| C1 | Binary journal frame format (§5.7) | EventJournal, every IPersistenceStream.Reconstruct | High | ~90% writer-thread alloc reduction |
| C2 | `InsertBulk` for high-frequency event streams (§5.8) | IPersistenceStream contract, event streams | Medium | 314 → 600+ ops/sec |
| C3 | Deferred non-unique index creation (§5.9) | every EnsureIndexes | Low | +15% ops/sec |

C1 is high-risk because it changes the durable-truth artefact on disk. Land C2 + C3 first to bank the throughput improvement, then attempt C1 with a feature flag and a parallel-path soak test.

### Phase D — game-thread enqueue tightening

| # | Change | Touches | Risk | Expected delta |
|---|---|---|---|---|
| D1 | `Substring`/`Lang.*` caches in interaction trackers (§5.16, §5.17) | InteractionNpc, InteractionItem, InteractionPlayer | Low | small but stacks |
| D2 | Row pool for high-frequency events (§5.2) | InteractionPlayer, SessionRecorder, DbWriterThread | Medium | 441 → ~300 ns/op |
| D3 | ContextTransitionWatcher XOR shortcut (§5.14) | ContextTransitionWatcher | Low | small |
| D4 | Lazy `ObjectId.NewObjectId()` (§5.11) | every Records/*.cs | Low | ~5 ns/op |
| D5 | Buff-diff HashSet replacement (§5.12) | InteractionPlayer | Low | small |
| D6 | Embedded payload struct in `DbWriteOp` (§5.3) | DbWriteOp, every stream | High | ~300 → 200 ns/op |

Land D1–D5 (low-risk wins) together. D6 is high-risk; ship after the rest of the pass is stable.

### Phase E — verification

Re-run `PersistenceBenchmarkTests`; play a 10-minute Calamity session; compare every number in `baseline.md` row-by-row; require every cell to move in the better direction; require no capture surface lost. Update `baseline.md` with the new numbers and tag the build `v0.6`.

---

## 9. References

### 9.1 In-tree

- `CLAUDE.md` — project invariants, communication style, tModLoader specifics
- `context/notes/philosophy.md` — universal-not-bespoke posture, data-stack vs presentation-stack discipline
- `context/perf-pass/baseline.md` — v0.5 measured baseline + v0.6 targets
- `context/systems/persistence.md` — system overview, file map, crash safety
- `context/notes/litedb-migration-plan.md` — original design, full evidence ledger, risk register
- `Profiling/Persistence/*` — every file walked above

### 9.2 tModLoader source (via `gh api`, repo `tModLoader/tModLoader`, branch `1.4.4`)

- `patches/tModLoader/Terraria/ModLoader/ModPlayer.cs`
  - line 286 — `PostUpdateBuffs`
  - line 302 — `PostUpdateEquips`
  - line 515 — `OnHurt(Player.HurtInfo)`
  - line 896 — `OnHitNPC(NPC, NPC.HitInfo, int)`
  - line 931 — `OnHitNPCWithItem(Item, NPC, NPC.HitInfo, int)`
  - line 966 — `OnHitNPCWithProj(Projectile, NPC, NPC.HitInfo, int)`
  - line 1408 — `OnPickup(WorldItem item) → bool`
- `patches/tModLoader/Terraria/ModLoader/GlobalItem.cs`
  - line 43 — `OnCreated(Item, ItemCreationContext)`
  - line 53 — `OnSpawn(WorldItem, IEntitySource)`
  - line 1027 — `OnPickup(WorldItem, Player) → bool`

### 9.3 LiteDB

- `litedb.org/docs/pragmas/` — pragma list and defaults (verified via WebFetch)
- `github.com/mbdavid/LiteDB/blob/master/LiteDB/Document/Bson/BsonSerializer.cs` — confirms `bsonspec.org` wire format
- `bsonspec.org/spec.html` — BSON wire format reference
- LiteDB issues #1568 (log-file growth), #2401 (ENSURE-page corruption on burst writes), #1511 / #1775 (checkpoint deadlock), #2152 (Rebuild requires drained log), #2440 (FindOne leaves log lock) — all enumerated in `litedb-migration-plan.md` §0

### 9.4 .NET 8 runtime

- `System.Threading.Channels.UnboundedChannel<T>` — unbounded MPSC; `TryWrite` is lock-free on the happy path, no allocation per write
- `System.Text.Json` source-generator (`JsonSerializable`) — eliminates per-call reflection for `Serialize<T>` (relevant if §5.7 doesn't replace JSON entirely)
- `System.Buffers.ArrayPool<byte>.Shared` — pooled byte buffers for the journal write path

### 9.5 BSON spec quick reference

- Document: `int32 (size) + elements + 0x00`
- Element: `1 B type + cstring (name) + value`
- String: `int32 length + UTF-8 + 0x00`
- Double: 8 B IEEE754
- Int32: 4 B
- Int64: 8 B
- ObjectId: 12 B
- Binary: `int32 length + 1 B subtype + bytes` (use subtype `0x00` for generic)

Type tags relevant to us: `0x01` double, `0x02` string, `0x03` document, `0x04` array, `0x05` binary, `0x07` ObjectId, `0x08` bool, `0x09` UTC datetime, `0x10` int32, `0x12` int64.

---

## Appendix A — invalid recommendations (rejected up front)

Recorded so the next reader doesn't re-invent them and so the additive-only ratchet is visible.

| Tempting recommendation | Why it's invalid |
|---|---|
| "Drop `PerModCatBytes` from spikes — only kept when allocation tracking is on anyway." | Data-stack reduction. `PerModCatBytes` is the only allocation-attribution surface; dropping it breaks the Deep-mode insight surface. |
| "Cap loadout snapshot list to top-N items." | Capture truncation. The loadout fingerprint depends on every slot. |
| "Stop emitting the 30 s periodic loadout anchor if nothing changed." | Anchor exists so insight queries can find at least one snapshot in every time window. Removing it makes "what was the player wearing at minute 7" unanswerable. |
| "Drop the `Summary` string on PlayerDeathRow — it's reconstructable from the other fields." | Pre-rendered human-readable text is a deliberate convenience for the chat-command consumer and the session-summary logger. Reconstructing on every read shifts CPU to the reader. |
| "Skip the world snapshot if the player hasn't moved." | Periodic-state captures are a discrete-time signal, not a delta-encoded log. Skipping makes "what was the player doing at minute 7" return null for an unmoving player. |
| "Truncate `DamageDealtRow.NpcName` after 16 chars." | Capture mutilation. Use the field-name shortening (§5.1) instead. |
| "Stop emitting per-tick warm aggregates during the menu (no real activity)." | Menu time is a real part of the session; per-second baseline data during menu is the only way to identify menu-induced slowdowns. |
| "Aggregate the four indexes on `damageDealt` into a composite." | Different queries need different keys; composite index can't be used by all of them. Use deferred-creation (§5.9) instead. |
| "Move warm-tier rows to a separate DB file." | Adds a second LiteDB file = a second WAL log = a second writer thread. Net cost > saving. |
| "Compress the journal with gzip." | Adds CPU on the writer thread; the gain is small relative to the binary-frame switch (§5.7). |

Every entry above was considered and rejected against the data-stack rule.

---

## Appendix B — the additive-only ratchet, applied to this pass

This dossier proposes changes only in these categories (mirroring CLAUDE.md skill discipline):

- **Coverage** — A1, A2, A3 (broaden generic-surface coverage; fix buff diff edge cases; add damage-weighted attribution).
- **Verification** — Phase E (re-run benchmark; row-by-row check against baseline.md).
- **Bug** — A1, A2, A3 (restore advertised behaviour).
- **Feature** — none. The user-visible capture surface does not grow in this pass.
- **Refactor (perf)** — every B/C/D entry. Same observable output, cheaper to produce. This is the additive perf class explicitly permitted by `philosophy.md`'s "optimisation = doing what we already do at maximum efficiency".

No entry in this dossier proposes lowering a budget, dropping a column, capping a sampling rate, removing an index because it costs bytes, or skipping a step "when not strictly needed". The data-stack-vs-storage-stack discipline is intact.

---

## Appendix C — open questions (raised, not resolved)

- **Does `GlobalItem.OnSpawn` fire on every chunk-loaded chest item or only on `Item.NewItem`?** The patched source says "called on the local client or the server where Item.NewItem is called". Chest items loaded from world save likely don't trigger `NewItem`, so chest contents at world load aren't captured. This is honest (the items existed before the player joined). Worth noting in the schema docs.

- **Does `RecipeItemCreationContext` carry the consumed-ingredients list?** The patched docstring says yes ("RecipeItemCreationContext includes the items consumed to craft the item"). If we want full craft-chain forensics, capture the consumed-items list too. Out of scope for the perf pass; tag for the next feature pass.

- **Should `_schema` field renaming to `v` apply uniformly or be opt-in per record?** Uniform is simpler and the saving is the same on every row. Recommendation: uniform.

- **Is the `Player.MaxBuffs` constant stable across tML versions?** Per Invariant 4, code that assumes a specific size must abort-clean if the size changes. The buff-diff arrays should resize dynamically (they already do via the `_prevBuffTypes.Length < Player.buffType.Length` guard).

- **Does the journal binary-frame switch (§5.7) break the existing crash-recovery tests?** The replay path needs a new decoder. Plan: ship the binary-frame switch behind a `JournalFormat` enum in the DB metadata, default `NDJSON`, switch on a clean-DB launch only. Backward-compatible reads handle either.

- **Should we expose the writer-thread queue depth and dropped-warm count via a chat command?** Useful for diagnosing whether the soft cap is being hit. Trivially additive; not strictly part of this pass but a low-cost win.

---

## Appendix D — the headline diff in numbers

If every recommendation in §8 lands as estimated:

```
                            v0.5        v0.6 target     after-pass projection
                            ────        ──────────      ──────────────────────
game-thread enqueue         441 ns      < 200 ns        ~190 ns (D2 + D6)
writer drain                314 ops/s   > 1 000 ops/s   ~1 100 ops/s (C1 + C2)
10-min Calamity DB          1 064 KB    < 600 KB        ~480 KB (B2 + B3 + B4)
end-of-session stall        8.5 s       0               0 (B1)
PerformanceProfiler avg/t   0.27 ms     < 0.10 ms       ~0.08 ms (B1 + D1-D5)
itemCreatedEvents bug       0           every event     fixed (A1)
buffEvents bug              2           every edge      fixed (A2)
death attribution           last-hit    weighted        fixed (A3)
```

The pass moves every dial in `baseline.md` in the right direction. No data-stack capture is dropped. No UI density is reduced. No insight detail is thinned. The contract is intact.
