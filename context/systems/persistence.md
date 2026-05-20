# Persistence

*Maturity: comprehensive · Stability: stable — shipped v0.3, no deferred audit items.*

## Scope / Purpose

Persistence owns every byte the profiler writes to disk between sessions. One LiteDB file (`profiler.litedb`) is the queryable store; one append-only NDJSON file (`profiler.events.log`) is the redo log; up to three rotating backups (`profiler.litedb.bak-{1,2,3}`) protect against catastrophic file-system failure. All four file types live in the same per-platform folder under tModLoader's `Main.SavePath`.

Persistence is the **agent surface** for cross-session analytics and the future HTML report. It replaces the JSON-per-session writer that shipped in v0.2 (deleted in v0.3). The new layer is queryable (LINQ over indexed collections), crash-safe (journal replay + bounded backups), idempotent (every op upserts on a natural key), and self-contained (single managed DLL packed inside the `.tmod`).

## Boundaries / Ownership

Files (every line under `Profiling/Persistence/`):

| Concern | File |
|---|---|
| Mod-wide lifecycle | `PerformanceProfiler.cs` (`Database` singleton) |
| Per-world recorder | `SessionRecorder.cs` |
| 1Hz / 1min downsampling | `TickDownsampler.cs` |
| Context-diff transitions | `ContextTransitionWatcher.cs` |
| Modlist identity hashing | `ModlistFingerprint.cs` |
| Legacy JSON ingestion (one-shot) | `LegacyJsonImporter.cs` |
| Compaction chat command | `ProfilerCompactCommand.cs` |
| Cross-platform paths | `ProfilerPaths.cs`, `PersistenceFileNames.cs` |
| DB facade: open / recover / lifecycle | `ProfilerDatabase.cs` |
| Writer thread + queue + checkpoint cadence | `DbWriterThread.cs` |
| Append-only redo log | `EventJournal.cs` |
| Schema migrations | `Migrations.cs` |
| Producer op shape | `DbWriteOp.cs` |
| Stream contract + registry | `IPersistenceStream.cs`, `StreamRegistry.cs` |
| Per-collection writer logic | `Streams/{Session,Spike,Stall,ContextTransition,TickAggregate,PerSessionAggregate,Modlist,Insight}Stream.cs` |
| BSON document shapes | `Records/*.cs` (one file per collection) |

Owns:

- Opening, recovering, and disposing the LiteDB file at the persistence root.
- Owning the single writer thread; the game thread never touches LiteDB.
- The four-layer crash-safety stack (journal-first writes, periodic checkpoint, backup rotation on clean end, replay on next open).
- Schema versioning via the `USER_VERSION` LiteDB pragma plus per-document `_schema` integer.
- Pre-warming collections at first open to sidestep LiteDB issue #2401 (ENSURE-page corruption under heavy burst writes against a zero-page file).
- Marking orphan sessions as `crash-detected` on the next launch.
- 24-hour TTL sweep on the warm tier.
- Backup rotation on every clean session end.
- Compaction (`/profiler-compact` chat command — refuses to run inside a world).

Does **not** own:

- Per-tick CPU attribution (lives in `MetricCollector` + `PerModAttribution`).
- The spike or stall detectors themselves (lives in `SpikeDetector`, `StallDetector`).
- UI rendering of session history (future HTML report).

## Architecture

```
   game thread                writer thread                disk
   ───────────                ─────────────                ────
   PostUpdateEverything ──▶  Channel<DbWriteOp>           profiler.events.log     (append-only NDJSON)
   SessionRecorder.OnTick     │
   │                         ▼
   │       TickDownsampler   batch up to 64 ops
   │       (1Hz / 1min)       │
   │       OnWorldUnload      ▼
   │       SessionRecorder.End  EventJournal.AppendBatch ───────────────────────▶ flushed at 64KB or 60s
   │                          │
   │                          ▼
   │                          stream.Apply per op ────────────────────────────▶ profiler.litedb
   │                          │                                                  (LiteDB WAL: .litedb-log)
   │                          │  every 60s of activity:
   │                          ▼
   │                          db.Checkpoint() ◀──────────────────────────────── coalesce WAL into main
   │
   ▼
   Mod.Unload ──▶ ProfilerDatabase.Dispose():
                    1. writer.Dispose()  — drains queue, final checkpoint
                    2. RotateBackups()   — copy main file to .bak-1, shift ring
                    3. db.Dispose()
                    4. journal.TruncateOnCleanShutdown()
```

## Data model

Documented exhaustively in `notes/litedb-migration-plan.md` §3. Headline collections:

| Collection | One row per | Purpose |
|---|---|---|
| `sessions` | Game-load session | Cross-session identity, mode, end reason |
| `modlists` | Distinct sorted modlist | Dedupe target keyed on fingerprint |
| `mods` | (fingerprint, internalName) | Version-history-over-time per mod |
| `worlds` | (name, uniqueId) | Future use; recorder stub passes null today |
| `perSessionModAggregates` | (session, mod) | Headline per-mod summary (Overview tab) |
| `perSessionHookAggregates` | (session, hook) | Deep-drill source |
| `spikeWindows` | One coalesced spike | Spike + frame-time forensics |
| `stallEvents` | One detected stall | GC / OS-suspend / draw-thread detection events |
| `contextTransitions` | One detected transition | Hardmode, primary biome, boss presence flips |
| `tickAggregatesWarm` | One second per session | 1Hz frame + per-mod stats, 24h retention |
| `tickAggregatesCold` | One minute per session | 1/min frame stats, session-lifetime retention |
| `tickAggregatesArchive` | One per session | Whole-session summary, forever |
| `insights` | One surfaced insight | Schema placeholder for M4+ |
| `metadata` | Single row (`_id="metadata"`) | DB-level open count, version history |
| `stallClusters` | One coalesced stall cluster | The "one freeze the player perceived" rollup over consecutive `stallEvents`. Carries dominant cause + dominant contributor. |
| `playerDeaths` | One local-player death event | Position, HP at death, active bosses, killer from last `damageTakenEvents` row, human-readable summary. |
| `worldSnapshots` | One every ~30s of in-world time | Player position/HP/mana, primary biome, hardmode, game mode, time-of-day, entity counts, primary boss. The "what was happening at minute N" reconstruction table. |
| `damageTakenEvents` | One per `Player.OnHurt` edge | `PlayerDeathReason`-encoded source (npc/projectile/other/custom), damage raw + dealt, HP before/after, active buffs. Killer attribution lives here. |
| `damageDealtEvents` | One per `OnHitNPC` / `OnHitNPCWithItem` / `OnHitNPCWithProj` | Path (melee / item / projectile), weapon id, projectile id, NPC type hit, damage, crit flag, loadout fingerprint. The "is it the sword or the projectile" answer. |
| `npcSpawnEvents` | One per `GlobalNPC.OnSpawn` | NPC type, owning mod (dynamic), source category (`IEntitySource` subclass name), position, boss flag. Universal — every spawning mod surfaces identically. |
| `itemCreatedEvents` | One per `GlobalItem.OnCreated` | Item type, owning mod, context category (`ItemCreationContext` subclass). Captures recipe-craft, init-spawn, debug-spawn alike. |
| `loadoutSnapshots` | On change (+ periodic 30s anchor) | Held item + every occupied equipment slot (armor / accessory / vanity / dye / modSlot) with item type. Stable fingerprint string used as join key for damage-dealt + cost-correlation insights. |
| `buffEvents` | One per buff add/remove edge | Buff type, owning mod, edge (on/off). The Dead Cells Mechanics "buff after damage" pattern surfaces as paired on/off rows. |

Every row carries a `_schema: int` field so a future per-collection schema bump can be detected at read time without forcing a whole-DB migration.

## Modular extension points

Adding a new tracked subsystem (engagement, allocation tier, custom event) is a **one-file-per-concern** change. To add an `engagementEvents` collection:

1. Create `Records/EngagementRow.cs` with the BSON shape.
2. Add `EngagementAdd` to the `DbOpKind` enum in `DbWriteOp.cs` and a factory method (`DbWriteOp.EngagementAdd(...)`).
3. Add `ILiteCollection<EngagementRow> Engagement => ...` to `ProfilerDatabase.cs` (one line).
4. Create `Streams/EngagementStream.cs` implementing `IPersistenceStream` with `Apply`, `Reconstruct`, `EnsureIndexes`.
5. Register the stream in `StreamRegistry.Default()` (one line).

No edit to the writer thread, the journal, the facade's dispatch path, or any other stream. The shape is open-for-extension, closed-for-modification by design.

## Crash safety

Four layers, ordered from cheapest to strongest, each catching the failure modes the previous misses (full evaluation in `notes/litedb-migration-plan.md` §5):

| Layer | What it catches |
|---|---|
| LiteDB WAL (built-in, `CHECKPOINT=1000` pragma) | Mid-write process crash |
| `profiler.events.log` (NDJSON redo log) | Corrupt LiteDB log; main-file truncation |
| 3 rotating backups (`.bak-{1,2,3}`) | Catastrophic main-file failure |
| Quarantine + fresh start (`broken-<utc>` rename) | All backups unreadable |

Recovery flow on next launch:

1. If main file fails read-only probe: promote the newest readable backup, rename the broken one with a timestamp.
2. If `profiler.events.log` is non-empty: replay each line through the stream registry's `Reconstruct` + `Apply` paths (idempotent), truncate the log.
3. Mark any session with `Incomplete=true` and no `EndedUtc` as `EndReason="crash-detected"`; do not fabricate an end time.
4. Sweep expired warm rows (`ExpireAtUtc < now`).
5. Touch the `metadata` row's `LastOpenedUtc` and append the current profiler version if new.

## Performance characteristics

Measured by `Tests/Persistence/PersistenceBenchmarkTests.cs` (M-series Apple Silicon, debug build):

| Surface | Cost |
|---|---|
| Game-thread enqueue | 276 ns / op |
| Writer-thread sustained throughput | 310 ops / sec |
| 10-minute Calamity-scale session DB size | 752 KB |
| `FindTop10 by start time` from 50 sessions | 0.39 ms |

The game thread pays a single `Interlocked.Increment` + `Channel.Writer.TryWrite` per enqueued op. No disk in the per-tick path. Invariant 2 (overhead budget) is intact.

## Invariants

1. **Game thread never touches LiteDB.** Every write enqueues; the writer thread drains.
2. **Idempotent apply.** Journal replay re-runs ops that already committed; every stream upserts on a natural key.
3. **No fabricated timestamps.** A crash-cut session's `EndedUtc` stays null; consumers know the run was crash-cut.
4. **Disk-failure surfaces.** Recovery quarantines unreadable files with a timestamped rename; the log line is the only record of the failure but the file is preserved.

## How it plugs in

`PerformanceProfiler.Mod.Load` opens the `ProfilerDatabase` singleton. `ProfilerSystem.OnWorldLoad` creates a `SessionRecorder` against it and upserts the modlist identity rows. `PostUpdateEverything` calls `SessionRecorder.OnTick(latestFrame, collector)` plus the `ContextTransitionWatcher` after each `EventContext` snapshot. `OnWorldUnload` calls `SessionRecorder.End("clean")` which builds the per-mod and per-hook aggregates, the archive row, and the SessionEnd op. `Mod.Unload` disposes the database (writer drain, final checkpoint, backup rotation, journal truncate).

The legacy JSON path (`Profiling/SessionLogWriter.cs` and `Sessions/*.json` files) was deleted in v0.3. `LegacyJsonImporter.RunOnceIfNeeded` performs a one-shot ingestion of any pre-existing JSON files into the new schema and moves them to `ImportedLegacyJson/` so the directory is empty for future launches.

## Open work (post-v0.3)

- **Per-hook CallCount**: currently 0 in `PerSessionHookAggregate`. The plan notes this as a future enrichment; would require `PerModAttribution` to track per-hook call counts.
- **HTML report**: separate feature (`notes/future-html-report.md`). The schema is friendly to a future reader.
- **`bossFights` precomputed collection**: today, boss windows are reconstructable from `contextTransitions` (`type=boss`). Promoting them to a first-class collection is `notes/litedb-migration-plan.md` §15.
- **Engagement attribution**: schema is forward-compatible (an `engagementEvents` collection plugs in via the stream registry without disturbing existing rows), but the per-tick instrumentation that would populate it is its own engineering surface.
