# LiteDB Migration Plan — Replace JSON sessions with a single managed embedded database

> Scope: delete every JSON write path under `Profiling/SessionLogWriter.cs` and the `~/Library/.../PerformanceProfiler/Sessions/*.json` tree; replace them with a single LiteDB-backed store at `~/Library/.../PerformanceProfiler/profiler.litedb`. One file, one writer, one schema, one canonical query surface. No hybrid. No fallback. Crash safety is designed on top of LiteDB's WAL with one extra layer the user asked for (an append-only event journal that survives a corrupted main file). Targets tModLoader 1.4.4 on .NET 8 with LiteDB 5.0.21 (stable). Honours all four Project Invariants — read-only against the game, zero-allocation on the per-tick hot path, descriptive-not-normative storage, abort-clean on host drift.

The HTML report (separate future feature; see `context/notes/future-html-report.md`) is **out of scope here** — the only obligation this plan carries toward it is "the schema must be friendly to a later generator that pulls from the DB". §3 calls out which fields exist for that future consumer.

---

## 0. Research evidence ledger

Every claim about LiteDB's API, file semantics, or crash-safety guarantees is anchored against the source listed below. Web evidence was gathered 2026-05-20; reflection probes are to be run against the `LiteDB.dll` shipped by the NuGet package at install time (step 1 of §13 closes that loop).

| Claim | Evidence |
|---|---|
| LiteDB latest stable is **5.0.21** (released 2024-07-05); license **MIT**; "100% C# code for .NET 4.5 / NETStandard 1.3/2.0 in a single DLL (less than 450 kb)"; "Thread-safe"; "Data recovery after write failure (WAL log file)". | NuGet `LiteDB` package page; `github.com/mbdavid/LiteDB` README. |
| A 6.0.0 prerelease exists (most recent `6.0.0-prerelease.77` dated 2026-02-27). Active but not stable. | NuGet `LiteDB` package page. |
| `LiteDatabase.Checkpoint()` "is the process to copy from -log file to original datafile"; "occurs automatically when -log file gets 1000 pages (~8 MB) or when close database"; "this number (1000) can be changed by PRAGMA called CHECKPOINT"; "if this pragma is set to 0 this checkpoint operation will not occur automatically (not even when database is closed)". | LiteDB issue tracker / community discussion; `litedb.org/docs/pragmas`. |
| Pragmas: `USER_VERSION` (default 0, "Reserved for version control by the user"), `TIMEOUT` (default 60 s), `LIMIT_SIZE` (default `long.MaxValue`, cannot be < 4 pages = 32 768 B), `UTC_DATE` (default false), `CHECKPOINT` (default 1000 pages), `COLLATION` (read-only, change via rebuild). | `litedb.org/docs/pragmas`. |
| Connection string keys: `Filename` (required, `:memory:` and `:temp:` accepted), `Connection` (`direct` default — exclusive, faster; `shared` — closes file after each op, multi-process), `Password` (AES), `InitialSize` (`KB`/`MB`/`GB`), `ReadOnly` (default false), `Upgrade` (default false — converts older format on open). | `litedb.org/docs/connection-string`. |
| Documented log-file growth issue (#1568, 5.0.4): log file grew to 32 GB on a 1.3 M-record database when manual `Checkpoint()` was not called; root cause was missing auto-checkpoint trigger on a particular write pattern. | github.com/mbdavid/LiteDB/issues/1568. |
| Long-running corruption pattern (#2401): "request page must be less or equals lastest page in data file" surfaced when inserting 11 500 records into an empty DB — confirms LiteDB can produce internal-state-inconsistency exceptions on heavy bursts; mitigated by version + `Checkpoint()` discipline. | github.com/litedb-org/LiteDB/issues/2401. |
| `Checkpoint()` can deadlock when called from inside an `Insert()`-triggered code path on a shared instance (#1511, #1775). The rule that emerges: never call `Checkpoint()` from a query/insert callback; only from the writer's own thread between batches. | github.com/mbdavid/LiteDB/issues/1511, /1775. |
| `Rebuild()` requires no live log file (#2152): if a checkpoint did not consume the log before Rebuild ran, Rebuild errors. The fix is: `db.Checkpoint(); db.Rebuild(opts);` in that order, on shutdown only. | github.com/litedb-org/LiteDB/issues/2152. |
| `FindOne()` skips log-file cleanup because it does not iterate the IEnumerable to completion (#2440). Practical impact: prefer `Query.Single` / explicit `.ToList()` over `FindOne()` when the query may match many. | github.com/litedb-org/LiteDB/issues/2440. |
| SQLite WAL ("Write-Ahead Logging"): "preserves the original database content and appending changes to a separate WAL file"; "checkpointing requires fsync operations for durability — the WAL must sync before moving content to the database, and the database file must sync before resetting the WAL". Significantly faster writes than rollback journal, but WAL "works best with smaller transactions; transactions exceeding a few dozen megabytes should use rollback mode". | sqlite.org/wal.html. |
| `tModLoader.targets` (existing build pipeline) packages every managed assembly placed in the mod's bin output into the `.tmod`; this is how `MonoMod.RuntimeDetour` / `Mono.Cecil` already ship with the mod today. A new managed-only DLL added via `<PackageReference>` will be included automatically. | `PerformanceProfiler.csproj` (imports `..\tModLoader.targets`); existing in-tree `MonoMod.RuntimeDetour` reference compiling today. |
| tModLoader's `build.txt` `buildIgnore` excludes `*.md`, `design/`, `context/`, `obj/`, and VCS dirs — does **not** exclude `bin/` runtime assemblies. | `CLAUDE.md` tModLoader specifics section. |
| `MetricCollector.EndTick` runs on the game's update thread; "tModLoader's hook dispatch runs on the game's update thread"; `_perModRawMs` and friends are harvested on this thread. Any synchronous LiteDB write inside `EndTick` stalls gameplay. | `Profiling/MetricCollector.cs:257-352`; `Profiling/PerModAttribution.cs:26-34`. |
| Spike windows are owned by `MetricCollector.Spikes` (`SpikeDetector.Windows`); each carries `StartTick`, `EndTick`, `WorstTick`, `WorstFrameMs`, `BaselineMs`, `MadMs`, `Warming`, `PerModCatMs[modCount * categoryCount]`, optional `PerModCatBytes`, optional `ContextSummary`. | `Profiling/SpikeDetector.cs:22-63`. |
| `HookInterceptor.ProfiledModNames`, `ProfiledModVersions`, `HookCoverageVersion`, `MeasuredHookCounts`, `TotalHookCounts`, `UnsupportedHookSamples`, `UnsupportedSignatureFrequency` are the existing exported surfaces the JSON writer reads. | `Profiling/SessionLogWriter.cs:259-401`, `615`. |
| `ModFingerprint()` is already `Hash(sorted "modId:Name@Version" joined)`; this is the modlist identity the migration adopts unchanged. | `Profiling/SessionLogWriter.cs:618-630`. |

### Uncertainties stated up front

- **Reflection-probe of `LiteDB.dll`** has not yet been run because the package is not installed in the worktree. Step 1 of §13 adds the package; the probe confirms (a) the type `LiteDB.LiteDatabase` exists, (b) `ILiteCollection<T>` is public with `Insert`, `Update`, `Upsert`, `Delete`, `FindOne`, `Find`, `Query`, `EnsureIndex`, (c) `Checkpoint()` and `Rebuild()` are on the public surface, (d) the DLL declares no native imports. If any of these probes fail the plan revisits §1 before continuing.
- **6.0 prerelease is intentionally not adopted.** The prerelease has been moving for a year; in a single-player mod that ships to non-engineer players we cannot accept "prerelease" risk on the persistence layer. Re-evaluate when 6.0 ships stable.
- **LiteDB on macOS file-system semantics:** `File.Move`/`File.Replace` for atomic rename is POSIX `rename(2)` on macOS; this is atomic for same-volume renames. We use it in §5 for the snapshot-file scheme. Cross-volume rename degrades to copy + delete, which is not atomic. Mitigation: place the snapshot adjacent to the live DB so they are always on the same volume.

---

## 1. Viability verdict

**Doable. Recommended. The technical risk is contained; the engineering work is sizeable but mechanical.**

LiteDB is the only NoSQL embedded option for .NET that is pure managed, MIT-licensed, file-based, and battle-tested (10+ years in production across hundreds of NuGet consumers). The alternatives all fail at least one of those bars:

| Option | Why rejected |
|---|---|
| Stay on JSON | The user explicitly closed this option. Also: re-reading a 100 MB JSON file at every "load previous session" call is wasteful; appending to it requires either rewriting the whole file or an externally-fragile `\n`-delimited convention; no indexes; no cross-session queries without loading the full corpus. |
| SQLite | Native dependency. tModLoader mods on the Workshop ship with a heuristic native-DLL allow-list; introducing `e_sqlite3.dll` (Windows) / `libsqlite3.dylib` (macOS) / `libsqlite3.so` (Linux) is a real cross-platform shipping problem and breaks the mod's "pure managed" posture documented in the README. |
| LMDB / RocksDB / LevelDB | Native; same problem as SQLite. |
| Marten / RavenDB / EFCore.Sqlite | Heavy dependencies; not embedded; not single-file. |
| Hand-rolled BSON-on-disk format | We become the database author. No. The user already correctly identified that we want a database, not a file format. |
| LiteDB **5.0.21 stable** | 100% managed, single file, < 450 KB DLL, MIT, public BSON document model, indexes, transactions, WAL-backed crash safety, active maintenance, used by hundreds of projects. Known issues (log-file growth, FindOne lock retention, occasional ensure-page corruption on burst writes) are documented and mitigable via the discipline outlined in §5 and §7. |

The migration is irreversible by intent — the user asked for "no other files being generated" and "no JSON files at all". That irreversibility is the load-bearing decision; the rest of this document is consequences.

### Risks the plan must address

| # | Risk | Trigger | Mitigation |
|---|---|---|---|
| **R1. Hot-path write latency** | LiteDB `Insert()` flushes to the log file on commit; on a spinning disk a single insert can take 5–20 ms. On the game thread, that is one to two dropped frames. | Any synchronous `Insert` from `MetricCollector.EndTick`. | All DB writes are batched and dispatched to a single dedicated writer thread via a lock-free producer queue. The game thread never blocks on disk. See §7. |
| **R2. Log-file growth (LiteDB issue #1568)** | Heavy burst writes without intervening `Checkpoint()` calls let the `-log` file grow unbounded. | Long session writing every spike + every minute-aggregation tick. | Writer thread calls `Checkpoint()` explicitly every 60 s of writer-thread activity and on session end; `CHECKPOINT` pragma left at default 1000 so auto-checkpoints continue as a safety net. See §5. |
| **R3. ENSURE-page corruption (#2401)** | Documented to surface on heavy burst inserts into an empty DB. | First session ever, when collections are empty and growing fast. | Pre-warm the DB at first launch by inserting and immediately deleting a sentinel doc per collection, then `Checkpoint()`. The "growing fast from zero pages" pathology is sidestepped because the file is already paged. See §13. |
| **R4. Single-file blast radius** | One bad write corrupts the only file; the user loses every prior session. | Disk full, machine power-cut, OS panic during fsync. | The append-only **event journal** described in §5 — a separate `profiler.events.log` file LiteDB does not own — is replayable into a fresh DB. The LiteDB main file is the materialised state; the event log is the durable truth. |
| **R5. Game-thread stall on shutdown** | `LiteDatabase.Dispose()` runs an implicit checkpoint and can take seconds on a large DB. | Player chooses save-and-exit. | The dispose is moved to the writer thread; the main thread signals "stop" and detaches. If the game closes before the writer finishes (force-quit), the event journal on disk preserves everything the writer was about to commit. See §5. |
| **R6. Concurrent process access** | Two tModLoader instances point at the same DB file. `Connection=direct` (our choice) takes an exclusive OS file lock; second opener fails. | Player launches a second tML window. | Catch the lock-failure on `LiteDatabase` construction; surface `Logger.Warn("Profiler DB locked by another tModLoader instance; profiling disabled this session")`; degrade to no persistence (live overlay still works, in-memory only). Invariant 4: abort clean. See §7. |
| **R7. Schema drift across versions** | A new profiler version writes a new collection or renames a field; an old DB has the old shape. | Player upgrades the mod. | Two levers: LiteDB `USER_VERSION` pragma for engine-level format; per-collection `_schema` int field in each document. Migration step on `Open()` reads both and upgrades. See §8. |
| **R8. Modlist churn** | The same modlist hash is recomputed every load; if the hash algorithm changes, prior session data is orphaned. | We change `HookInterceptor.ProfiledModNames`/`Versions` ordering or change `ModFingerprint`. | The fingerprint algorithm is now a versioned artefact: `Modlists` collection records `(fingerprint, fingerprintAlgVersion, modsArray)`. A later algorithm change writes a *new* fingerprint and a one-time migration record links the old to the new by content equality. See §9. |
| **R9. Per-tick storage cost** | 60 Hz × 12-hour session = 2.6 M ticks × ~200 B per row = 520 MB raw. Unacceptable. | Storing every tick forever. | Tiered downsampling per §4: hot tier RAM-only, warm tier 1 Hz aggregation in DB, cold tier 1 min aggregation, archive tier per-session summary. Raw per-tick never reaches disk. |
| **R10. Pre-existing JSON files** | The user has played sessions; their `~/Library/.../Sessions/*.json` directory has data. | First launch after the migration. | One-shot: read each JSON file once at first-launch, ingest the subset of fields the new schema covers, delete the directory. This is the only point at which JSON code lives in the codebase post-migration; it is then deleted. See §13 step 11. |

None of these are blockers. R1, R4, R9 are the load-bearing ones; the storage layout and writer thread are designed around them.

### Honest uncertainties

- **The user's "two-file pattern" intuition.** The user proposed "one append-only file, one save-on-quit file" without committing to specifics. After §5's evaluation, what we actually adopt is *one LiteDB file (the materialised state, written continuously through the WAL) plus one append-only event log (text-format JSON-lines on disk, never read by LiteDB)*. The event log is the rebuild source if the LiteDB file is ever lost; it is **not** a backup of the LiteDB file. The two files have different roles, which is the spirit of what the user described.
- **Compaction cadence.** LiteDB's `Rebuild()` reclaims free pages but rewrites the entire file. On a 200 MB DB this is seconds. We schedule it for session-end only, never mid-session.
- **HTML report friendliness.** The schema is renderer-agnostic; the HTML generator will read documents directly via the same `ProfilerDatabase` facade. No special "report view" exists.

---

## 2. Why LiteDB specifically, not "use a database"

The user's framing — "an entire database, we can do whatever we want" — is correct but requires picking one. The implicit decision tree:

```
                  ┌─ pure managed required? ─yes──┐
                  │                               │
                  │     no                        ▼
                  ▼                          ┌─ MIT licence required? ─yes──┐
              SQLite (native)                │                              │
              LMDB (native)                  no                             ▼
              everything fast              proprietary                ┌─ embedded single-file? ─yes──┐
                                                                     │                              │
                                                                     no                             ▼
                                                                 EF + Sqlite, Marten     ┌─ active and battle-tested? ─yes──┐
                                                                 (heavy / native)        │                                  │
                                                                                         no                                 ▼
                                                                                      experiments                       LiteDB 5.x
```

Every yes-branch above is dictated by either the README (pure managed, MIT, no telemetry) or the four Project Invariants. LiteDB is the only point where the tree terminates.

There is one honest caveat: LiteDB is maintained primarily by one author (Maurício David) with community contributions. The 6.0 release has been in prerelease for years. We are betting that **5.0.21 stable** is good enough for the use-case forever; if 5.0.21 is the final 5.x release, we still have a working DB layer. The escape hatch — were 5.0.21 to develop a fatal bug — is that the event journal in §5 lets us migrate to any other store without data loss.

---

## 3. The data model

Plain English first, then a column-by-column shape per collection. Storage is **document-oriented**: each row is a BSON document; there is no SQL `JOIN`; references between collections are by `ObjectId` or string key. Indexes are explicit.

### 3.1 Collections — overview

| Collection | One row per | Approx size | Approx growth |
|---|---|---|---|
| `sessions` | Game load (start of session) | ~600 B | One row per session |
| `modlists` | Distinct sorted modlist fingerprint | ~4 KB | One row per unique modlist the user ever plays |
| `mods` | (modlistFingerprint, modInternalName) | ~200 B | Bounded by mods × distinct modlists |
| `worlds` | Distinct world name + UUID | ~150 B | One per save |
| `perSessionModAggregates` | (sessionId, modId) | ~400 B | mods × sessions |
| `perSessionHookAggregates` | (sessionId, hookId) | ~150 B | hooks × sessions, large |
| `spikeWindows` | One spike | ~2–8 KB depending on per-mod array size | tens to hundreds per session |
| `contextTransitions` | Biome / boss / event change | ~120 B | hundreds per session |
| `tickAggregatesWarm` | One per second per session | ~120 B + per-mod slice | 3 600 / hour, dropped after 24 h |
| `tickAggregatesCold` | One per minute per session | ~200 B + per-mod slice | 60 / hour, kept session lifetime |
| `tickAggregatesArchive` | One per session (whole-session aggregate) | ~500 B | one per session |
| `insights` | One surfaced insight | ~1 KB | tens per session |
| `settings` | Single-row | ~2 KB | One |
| `metadata` | DB-level: `schemaVersion`, `profilerVersion`, etc | ~200 B | One |

### 3.2 Sample documents

The `_id` field is LiteDB's required primary key. Nullable fields use BSON `null` (BsonValue.Null). All times are stored as ISO 8601 in UTC (BSON `DateTime`); the writer sets `Pragma UTC_DATE = false` so reads convert back to UTC on the way out by default.

**sessions**
```jsonc
{
  "_id": ObjectId("664c…"),                  // unique session id, also referenced by aggregates / spikes
  "_schema": 1,
  "startedUtc": ISODate("2026-05-20T14:32:11Z"),
  "endedUtc":   ISODate("2026-05-20T16:48:09Z"),   // null while session in progress
  "durationMs": 8158000,                            // computed at session end
  "profilerVersion": "0.4.1",
  "tmlVersion":      "1.4.4-b6",
  "worldId":         ObjectId("…"),                 // → worlds._id, nullable (menu-only session)
  "modlistFingerprint": "8f3a1b9c",                 // → modlists.fingerprint (NOT _id; string key)
  "hookCoverageVersion": 3,
  "mode":            "lite",                        // lite | standard | deep | off
  "tracksAllocations": true,
  "ticksObserved":   480123,                        // sum of recorded ticks
  "endReason":       "clean",                       // clean | crash-detected | host-drift-aborted
  "incomplete":      false                           // true until endedUtc is written
}
```

**modlists** (one per distinct sorted modlist; deduped on `fingerprint`)
```jsonc
{
  "_id": ObjectId("…"),
  "_schema": 1,
  "fingerprint":       "8f3a1b9c",
  "fingerprintAlg":    "sha256-of-sorted-id-name-version-v1",
  "firstSeenUtc":      ISODate(…),
  "lastSeenUtc":       ISODate(…),
  "sessionCount":      27,
  "mods": [
    { "id": 0, "name": "ModLoader",     "version": "1.4.4" },
    { "id": 1, "name": "CalamityMod",   "version": "2.0.4.001" },
    /* … */
  ]
}
```

**mods** (one per `(modlistFingerprint, modInternalName)` so version churn within a fingerprint is tracked across time)
```jsonc
{
  "_id": ObjectId("…"),
  "_schema": 1,
  "modlistFingerprint": "8f3a1b9c",
  "internalName":       "CalamityMod",
  "displayName":        "Calamity Mod",
  "versionSeen":        "2.0.4.001",
  "firstSeenUtc":       ISODate(…),
  "lastSeenUtc":        ISODate(…),
  "versionHistory": [
    { "version": "2.0.4.000", "firstUtc": ISODate(…), "lastUtc": ISODate(…) },
    { "version": "2.0.4.001", "firstUtc": ISODate(…), "lastUtc": ISODate(…) }
  ]
}
```

**perSessionModAggregates** (the headline per-mod summary that drives Overview tab)
```jsonc
{
  "_id": ObjectId("…"),
  "_schema": 1,
  "sessionId":   ObjectId("…"),
  "modId":       1,
  "modInternalName": "CalamityMod",
  "avgMs":       4.21,
  "peakMs":      31.7,
  "totalMs":     50412.0,
  "p95Ms":       8.9,
  "avgBytes":    1832.0,
  "peakBytes":   65520.0,
  "totalBytes":  9824019200,
  "coverage":   { "measured": 412, "total": 412, "badge": "full" },
  "categoryMs": [ 12.3, 4.1, 33.0, 0.9, 0.2, 0.0, 0.1 ],   // length = CategoryCount
  "topHooks":   [
    { "hookId": 1207, "displayName": "Calamity.GlobalNPC.AI(NPC)", "avgMs": 2.31 },
    { "hookId": 1208, "displayName": "Calamity.GlobalProjectile.AI(Projectile)", "avgMs": 1.04 }
  ]
}
```

**perSessionHookAggregates**
```jsonc
{
  "_id": ObjectId("…"),
  "_schema": 1,
  "sessionId":  ObjectId("…"),
  "hookId":     1207,
  "modId":      1,
  "categoryId": 2,
  "displayName":"Calamity.GlobalNPC.AI(NPC)",
  "avgMs":      2.31,
  "peakMs":     19.7,
  "totalMs":    27814.0,
  "avgBytes":   210.0,
  "callCount":  1825412
}
```

**spikeWindows** (sourced from `MetricCollector.Spikes`, see §6.3)
```jsonc
{
  "_id": ObjectId("…"),
  "_schema": 1,
  "sessionId":     ObjectId("…"),
  "startTick":     1240,
  "endTick":       1247,
  "worstTick":     1244,
  "worstFrameMs":  87.0,
  "baselineMs":    16.7,
  "madMs":         1.2,
  "warming":       false,
  "context":       "Cryogen Phase 2 · Sulphurous Sea",  // string from SpikeWindow.ContextSummary
  "topContributors": [
    { "modId": 1, "name": "CalamityMod",   "ms": 58.2, "bytes": 524288 },
    { "modId": 4, "name": "FargosSouls",   "ms": 12.1, "bytes":  65536 }
  ],
  "perModCatMs":   [ /* float[modCount * CategoryCount], LiteDB stores as BsonArray of doubles */ ],
  "perModCatBytes":[ /* null when allocation tracking off */ ]
}
```

**contextTransitions**
```jsonc
{
  "_id": ObjectId("…"),
  "_schema": 1,
  "sessionId":  ObjectId("…"),
  "tick":       384,
  "type":       "biome",         // biome | boss | event | invasion | weather | hardmode
  "from":       "Forest",
  "to":         "Snow",
  "tickFrameMs": 17.4
}
```

**tickAggregatesWarm** (1-second buckets, kept ~24 h then expired)
```jsonc
{
  "_id": ObjectId("…"),
  "_schema": 1,
  "sessionId":   ObjectId("…"),
  "secondIndex": 472,                       // floor(tickIndex / 60)
  "avgFrameMs":  16.4,
  "p95FrameMs":  21.0,
  "gcMs":        0.4,
  "perModMs":    [ /* float[modCount] */ ],
  "expireAtUtc": ISODate("2026-05-21T16:48:09Z")  // session end + 24 h
}
```

**tickAggregatesCold** (1-minute buckets, kept session lifetime)
```jsonc
{
  "_id": ObjectId("…"),
  "_schema": 1,
  "sessionId":  ObjectId("…"),
  "minuteIndex": 7,
  "avgFrameMs": 16.5,
  "p95FrameMs": 28.1,
  "maxFrameMs": 87.0,
  "gcMs":       3.2,
  "perModMs":   [ /* float[modCount] */ ],
  "perModBytes":[ /* float[modCount] | null */ ]
}
```

**tickAggregatesArchive** — exactly one per session, the "the whole 2-hour story compressed to a row" record. The HTML report's top-line numbers come from here.
```jsonc
{
  "_id":     ObjectId("…"),
  "_schema": 1,
  "sessionId":     ObjectId("…"),
  "avgFrameMs":    16.7,
  "medianFrameMs": 16.4,
  "p95FrameMs":    23.0,
  "p99FrameMs":    44.0,
  "maxFrameMs":    87.0,
  "totalGcMs":     4012.0,
  "perMod":        [
    { "modId": 1, "avgMs": 4.21, "totalMs": 50412.0, "peakMs": 31.7 }
    /* … */
  ]
}
```

**insights** (M4+; schema placeholder ready now)
```jsonc
{
  "_id":     ObjectId("…"),
  "_schema": 1,
  "sessionId":      ObjectId("…"),
  "patternKey":     "CONTEXT-CONDITIONAL-COST",
  "audience":       "player",   // player | modder
  "renderedShort":  "Calamity is 3× more expensive during Blood Moon.",
  "renderedLong":   "…",
  "confidence":     "high",     // preliminary | medium | high
  "evidence": { /* free-form per-detector */ },
  "firstSeenTick":  1240,
  "lastConfirmedTick": 1850
}
```

**settings** (one row, key `_id = "settings"`)
```jsonc
{
  "_id":      "settings",
  "_schema":  1,
  "mode":     "lite",
  "allocationTracking": false,
  "f9KeyBinding": "F9",
  "lastUpdatedUtc": ISODate(…)
}
```

**metadata** (one row, key `_id = "metadata"`)
```jsonc
{
  "_id":     "metadata",
  "_schema": 1,
  "dbCreatedUtc":   ISODate(…),
  "lastOpenedUtc":  ISODate(…),
  "profilerVersionSeen": [ "0.3.0", "0.4.0", "0.4.1" ],
  "sessionCount":   42
}
```

### 3.3 Indexes

LiteDB does not auto-index anything but `_id`. Explicit indexes are mandatory for the query patterns of §10.

| Collection | Index | Reason |
|---|---|---|
| `sessions` | `startedUtc` | Sort sessions by time, "last N sessions" queries |
| `sessions` | `modlistFingerprint` | "All sessions on this modlist" |
| `modlists` | `fingerprint` (unique) | Dedup on save |
| `mods` | `(modlistFingerprint, internalName)` (unique) | Mod-perf-over-time query |
| `perSessionModAggregates` | `sessionId` | All-mods-for-this-session join |
| `perSessionModAggregates` | `(modInternalName, sessionId)` | "How has this mod's cost changed?" |
| `perSessionHookAggregates` | `sessionId` | All hooks for this session |
| `spikeWindows` | `sessionId` | All spikes for this session |
| `spikeWindows` | `worstFrameMs` (desc) | Worst spikes across all sessions |
| `contextTransitions` | `sessionId` | All transitions for this session |
| `tickAggregatesWarm` | `sessionId` | Warm-tier session reads |
| `tickAggregatesWarm` | `expireAtUtc` | TTL sweep |
| `tickAggregatesCold` | `sessionId` | Cold-tier session reads |
| `tickAggregatesArchive` | `sessionId` (unique) | One-row-per-session lookup |
| `insights` | `sessionId` | All insights for this session |
| `insights` | `patternKey` | Recurring-pattern queries |

Each index adds ~5–10 % to the relevant collection's storage. Acceptable.

### 3.4 Size model

Estimating for a 2-hour Calamity-scale session at 60 Hz:

```
Per session:
  sessions:                    1 row    × 600 B    = 0.6 KB
  perSessionModAggregates:    100 mods × 400 B    = 40 KB
  perSessionHookAggregates:  3000 hooks × 150 B   = 450 KB
  spikeWindows:                50      × 4 KB     = 200 KB
  contextTransitions:         500      × 120 B    = 60 KB
  tickAggregatesWarm:        7200 × (40 B + 4*100 B) = 3.0 MB
  tickAggregatesCold:         120 × (60 B + 4*100 B) = 55 KB
  tickAggregatesArchive:        1 × 4 KB           = 4 KB
  insights:                    20 × 1 KB           = 20 KB
                              -----
                              ~3.8 MB / session
```

| Sessions | Steady-state DB (post-warm-tier expiry) |
|---|---|
| 1 | ~3.8 MB |
| 10 | ~10 MB (warm tier expired for 9, cold + summaries remain) |
| 100 | ~80 MB |
| 1000 | ~700 MB |

The warm tier expiring is what keeps growth sublinear. At 1000 sessions we still want `Rebuild()` to reclaim freed pages — see §5.4.

### 3.5 What does NOT go in the DB

- Raw per-tick samples (cost from §3.4 would be 130 MB/session × 1000 = 130 GB).
- The 30-second in-RAM history ring — that stays in `MetricCollector` (Lite-mode constraint).
- Live overlay UI state.
- The event-journal file from §5 — that is a sibling file, not a collection.

---

## 4. The downsampling tier story

Per-tick at 60 Hz is unaffordable to keep on disk forever. The tier story honours both the storage budget (§3.4) and the question shape (the questions worth asking against historical data are minute-to-hour-scale, not tick-scale).

### 4.1 Four tiers

```
                hot            warm                cold              archive
              (RAM only)      (DB, 24h)         (DB, lifetime)     (DB, lifetime)
              ───────────────────────────────────────────────────────────────────
   60 Hz ─── raw ticks ──┐
                         │   1 Hz aggregation
                         └──── (avgFrameMs, p95, perModMs[]) ──┐
                                                               │   1/min aggregation
                                                               └──── (perModMs[], maxFrameMs)──┐
                                                                                                │   session-end
                                                                                                └─── per-session aggregate ───→ kept forever
                                                                          ↑
                                                                          │
                                                                expires after 24h
                                                                (TTL sweep on session open)
```

| Tier | Storage | Granularity | Retention | Drives |
|---|---|---|---|---|
| Hot | `PerTickAttributionRing` (existing, RAM) | 60 Hz raw per-tick per-mod | 30 s | Spike drill-down, live overlay |
| Warm | `tickAggregatesWarm` | 1 Hz | 24 h after session | "What did the last few minutes look like" if the player returns same day |
| Cold | `tickAggregatesCold` | 1 / min | Session lifetime (until manual or storage-budget compaction) | Session timeline graphs in HTML report |
| Archive | `tickAggregatesArchive` + `perSessionModAggregates` | Session-wide | Forever | Cross-session comparisons, Insights Engine inputs |

### 4.2 The downsampler

A new `TickDownsampler` class sits next to `MetricCollector`. It reads from `MetricCollector.History` (existing) and `MetricCollector.PerModCategoryMs` (existing) and emits one row per second to the writer queue.

```csharp
internal sealed class TickDownsampler
{
    private const int TicksPerSecond = 60;
    private const int TicksPerMinute = 60 * 60;

    private long _lastSecondEmitted = -1;
    private long _lastMinuteEmitted = -1;
    private readonly RollingPercentile _p95Second = new RollingPercentile(60);   // last 1s of frame ms
    private readonly RollingPercentile _p95Minute = new RollingPercentile(3600); // last 1m

    public void OnTickCommitted(TickFrame frame, MetricCollector collector, IDbWriterQueue queue, ObjectId sessionId)
    {
        _p95Second.Push(frame.FrameTimeMs);
        _p95Minute.Push(frame.FrameTimeMs);

        long secondIndex = frame.TickIndex / TicksPerSecond;
        if (secondIndex != _lastSecondEmitted)
        {
            _lastSecondEmitted = secondIndex;
            // Build the warm row from collector's current smoothed values
            // (no allocation: queue.Enqueue takes a pooled buffer)
            queue.EnqueueWarmAggregate(sessionId, secondIndex,
                avgFrameMs: collector.RecentAverageFrameMs,
                p95FrameMs: _p95Second.P95(),
                gcMs:       frame.GcTimeMs,
                perModMs:   collector.PerModCategoryMs);
        }

        long minuteIndex = frame.TickIndex / TicksPerMinute;
        if (minuteIndex != _lastMinuteEmitted)
        {
            _lastMinuteEmitted = minuteIndex;
            queue.EnqueueColdAggregate(sessionId, minuteIndex, /* … */);
        }
    }
}
```

Key properties:

- **No per-tick allocations.** `RollingPercentile` is a pre-sized ring; `IDbWriterQueue.Enqueue*` writes into a pool-owned buffer.
- **Cheap.** At 60 Hz: 60 pushes/sec into the percentile ring (an `O(log n)` insert into a sorted-window structure, but `n = 60`, sub-microsecond). One enqueue per second, one per minute. Nothing crosses the writer-thread boundary in the hot path beyond a pointer-swap on a lock-free queue.
- **Idempotent across crash.** If the writer thread does not commit a queued warm row before the game crashes, the event-journal entry for that aggregation is on disk; on next launch we replay and either reinsert (idempotent because of the `(sessionId, secondIndex)` index check) or skip.

### 4.3 TTL sweep for warm tier

On `ProfilerDatabase.Open()`, run:

```csharp
_db.GetCollection<TickAggregateWarm>().DeleteMany(x => x.ExpireAtUtc < DateTime.UtcNow);
```

This is a single indexed range-delete; sub-second even on a 1000-session DB.

### 4.4 Why not LiteDB-level TTL

LiteDB does not have a built-in TTL feature. We synthesise it via the `expireAtUtc` field and the open-time sweep above. Acceptable because the sweep is cheap and bounded.

---

## 5. Crash safety design — the load-bearing section

The user is open to designing crash safety on top of LiteDB rather than trusting its journal alone. This section evaluates the options and recommends one.

### 5.1 The threat model

What we are protecting against, ranked by how likely each is and how bad it is for the user:

| Threat | Likelihood | Loss if unprotected |
|---|---|---|
| Game crash mid-session (mod conflict, unhandled exception) | Common (multiple per week on heavy modlists per community reports) | The current session's last few minutes |
| Force-quit by the user (Cmd-Q-Q) | Common | Whatever was queued and not yet flushed |
| OS panic / power cut during write | Rare but real | A partial write into the DB log |
| Disk full during write | Rare but real | A failed write the DB cannot complete |
| File-system corruption | Very rare | The entire DB |
| Concurrent writers (two tML instances) | Rare (we own this in §7 / R6) | Lock denial; degraded session |

A profiler that loses "the last 5 minutes" on a crash is fine. A profiler that loses **all prior sessions** because one write went bad is not. The design is shaped to make the second outcome impossible.

### 5.2 Patterns surveyed

| Pattern | What it does | Catches | Misses | Cost |
|---|---|---|---|---|
| **LiteDB built-in WAL** | Writes go to `-log` first; checkpoint copies to main file with intervening fsyncs. Crash → next open replays log. | Mid-write crash on Linux/macOS with honest fsync. | A partial write that corrupts the log file itself (rare but documented). Disk-full mid-checkpoint. | Free, already on. |
| **SQLite-style explicit WAL with manual checkpoint cadence** | Same as above; we control checkpoint frequency. | Same as above. | Same. | Free in LiteDB — we set `PRAGMA CHECKPOINT` and call `Checkpoint()` ourselves. |
| **Double-file pattern (user's idea)** — file A append-only, file B save-on-quit | A is the truth, B is the queryable cache. Crash recovers B by replaying A. | Total loss of B. | Total loss of A (single point of failure shifts). | Implementation cost: a parallel write surface. |
| **Atomic file rename** | Writes go to `db.litedb.tmp`; on commit, `rename(tmp, final)`. | Partial-write corruption of the final file. | Loss of unflushed writes between renames. | Constant overhead per commit; not viable for the high-write workload. |
| **Periodic checkpoint files** | Every N minutes copy `profiler.litedb` → `profiler.litedb.bak-N`; keep last K. | A corrupted main file (restore from backup). | Sessions between the last good backup and the crash. | One full file copy per N minutes; rebuilds on a large DB are O(size). |
| **Event sourcing**: append-only event log + materialised state | Events are durably appended; queryable state is derived by replaying. | Loss of the materialised file (replay the log). | Corrupted event log itself (mitigated by append-only file with line-level framing). | Implementation: dual write path. Storage: 2× during the session, then the log can be truncated after a confirmed session-end commit. |

### 5.3 The recommendation: LiteDB WAL + append-only event journal + bounded checkpoint files

Three layers, ordered from cheapest to strongest, each catching the failures the previous misses:

```
                                  ┌──────────────────────────────────────────────────┐
                                  │   Recovery on next launch                        │
                                  └────────────────────┬─────────────────────────────┘
                                                       │
                  ┌────────────────────────────────────┼────────────────────────────────────┐
                  │                                    │                                    │
                  ▼                                    ▼                                    ▼
       Layer 1: LiteDB WAL              Layer 2: event journal               Layer 3: backup files
       profiler.litedb-log               profiler.events.log                  profiler.litedb.bak-N
       built-in; transparent             our append-only NDJSON               our periodic copy (bounded)
       catches mid-write crash           catches main-file corruption          catches "log file also bad"
```

#### Layer 1 — LiteDB WAL (free, on by default)

We set `PRAGMA CHECKPOINT = 1000` (default, 8 MB log) and call `Checkpoint()` explicitly:
- Every 60 s of writer-thread idle time.
- At every session-end.
- Never from the game thread.

The auto-checkpoint at 1000 pages remains as a safety net for the case where the writer thread dies but the game continues.

#### Layer 2 — Append-only event journal (the user's "two-file" intuition)

A separate file `profiler.events.log` lives alongside `profiler.litedb`. It is a UTF-8, newline-delimited JSON file (NDJSON). Each line is one event the writer thread would commit to the DB; the format is canonical so a future reader can replay the journal into a fresh DB if the DB is gone.

```
{"t":"session-start","sid":"664c…","utc":"2026-05-20T14:32:11Z","mods":"8f3a1b9c","profilerVer":"0.4.1"}
{"t":"spike","sid":"664c…","startTick":1240,"endTick":1247,"worstFrameMs":87.0,…}
{"t":"context","sid":"664c…","tick":384,"from":"Forest","to":"Snow"}
{"t":"aggregate-warm","sid":"664c…","secondIndex":472,"avgMs":16.4,"perModMs":[…]}
{"t":"session-end","sid":"664c…","utc":"…","endReason":"clean","aggregate":{…}}
```

Properties:
- **Append-only.** `FileMode.Append` plus `FileShare.Read`; the writer thread holds the only writer handle for the session.
- **Line-framed.** Even a partially-written line is detectable: the parser drops the first malformed line on replay and continues.
- **Truncated at clean session-end.** After the writer thread writes the final `session-end` line, calls `db.Checkpoint()`, and verifies the DB has the session's archive row, it truncates the journal to zero length. The journal is **not** an archive; it is a redo log.
- **Synchronously flushed.** The writer thread calls `FileStream.Flush(flushToDisk: true)` after each batch (every 60 s or every 64 events, whichever first). This is the only fsync the journal needs.
- **Cheap.** Even at 60 events/second, a 100-byte line takes 6 KB/s; over a 2-hour session, ~40 MB before truncation. That is in line with the warm-tier DB cost, and the file is gone at session-end.

#### Layer 3 — Bounded backup files

On every clean session-end, after the journal is truncated and the DB is checkpointed:

```csharp
File.Copy(profilerLitedbPath, $"{profilerLitedbPath}.bak-{N}", overwrite: true);
// Keep only the last 3 backups
PruneOldestBeyond(profilerLitedbPath + ".bak-*", keep: 3);
```

This catches the catastrophic case where the main DB file is unreadable on the next launch (file-system corruption, accidental external deletion). Recovery: on open failure, pick the most recent good `.bak-N`, copy it over the main file, replay any post-backup journal entries.

The backups are bounded (last 3) so storage stays bounded. At ~80 MB per backup × 3 = ~240 MB worst case for a 100-session-deep player.

### 5.4 Recovery flow on next launch

```
ProfilerDatabase.Open() {
  1. If profiler.litedb is missing or fails to open:
       a. Try profiler.litedb.bak-N in reverse order; copy first good one over profiler.litedb.
       b. If all backups fail: rename profiler.litedb to profiler.litedb.broken-<utc>;
          create a fresh profiler.litedb;
          Logger.Warn("DB corrupted; preserved as profiler.litedb.broken-<utc>; starting fresh.")
  2. If profiler.events.log exists and has content:
       a. Parse each line.
       b. For each line, check whether the corresponding DB row already exists (by sessionId + sub-key).
          If yes, skip (idempotent). If no, insert.
       c. Truncate the journal after successful replay.
       d. Logger.Info("Replayed N events from journal.")
  3. Run TTL sweep on tickAggregatesWarm.
  4. Set sessions.endReason = "crash-detected" for any row with endedUtc = null.
  5. db.Checkpoint().
}
```

Step 4 is what produces the README's `incomplete: true` semantic — a session whose `endedUtc` was never written is, by definition, an incomplete crash-cut session.

### 5.5 Why this beats the user's two-file proposal

The user proposed "one append-only, one save-on-quit". The design adopts that **spirit** but corrects two failure modes:

- A pure append-only file with full session data is hard to query (no indexes). LiteDB *is* the queryable file; the journal is the redo log.
- "Save-on-quit only" loses every session that did not quit cleanly. The journal-plus-WAL design loses **nothing** the writer thread successfully appended.

The final answer is "one DB file, one journal file, and a bounded ring of backup files". The journal is the user's append-only intuition correctly placed.

### 5.6 Failure mode triage

| Failure | What survives | What is lost |
|---|---|---|
| Game crashes mid-session | DB + journal + backups | Last < 60 s of unflushed journal events (worst case) |
| Force-quit | Same | Same |
| Power cut mid-checkpoint | DB at pre-checkpoint state + log + journal | Nothing (recovery replays log + journal) |
| Power cut mid-journal-write | DB + truncated-but-good journal | The single partial event (parser drops it) |
| File-system corruption of DB file | Most recent good backup | Sessions since that backup, minus what journal replay recovers |
| File-system corruption of DB and all backups | Journal | Nothing the journal contained (rebuild fresh DB from it) |
| File-system corruption of DB, backups, and journal | Nothing | Everything (acceptable — disk is dying) |

---

## 6. File layout, paths, and sizing

### 6.1 Cross-platform paths

Following `SessionLogWriter.SessionDirectory()` (existing code uses `Environment.SpecialFolder.ApplicationData` which routes correctly on each OS):

| OS | DB path |
|---|---|
| macOS | `~/Library/Application Support/Terraria/tModLoader/PerformanceProfiler/profiler.litedb` |
| Windows | `%USERPROFILE%\Documents\My Games\Terraria\tModLoader\PerformanceProfiler\profiler.litedb` |
| Linux | `~/.local/share/Terraria/tModLoader/PerformanceProfiler/profiler.litedb` |

The same folder also holds:

```
profiler.litedb           ← main DB (LiteDB owns this and the -log sibling)
profiler.litedb-log       ← LiteDB's WAL file (transparent to us)
profiler.events.log       ← our append-only journal
profiler.litedb.bak-1     ← rotated backup, newest
profiler.litedb.bak-2
profiler.litedb.bak-3     ← rotated backup, oldest
profiler.litedb.broken-<utc>  ← only present if recovery quarantined a broken file
```

The legacy `Sessions/` directory and `current-session.json` / `<identity>-<stamp>.json` files are removed on first launch after the migration (one-shot ingestion step described in §13).

### 6.2 Size growth model

Reusing §3.4 numbers, with backups:

| State | DB | Journal | Backups | Total |
|---|---|---|---|---|
| Fresh install | < 100 KB | 0 | 0 | < 100 KB |
| Mid-session | 0.6–3 MB growing | up to 5 MB | last backup | session-dependent |
| After clean session-end | +3.8 MB / session | 0 (truncated) | last 3 of `≤ session-end DB size` | DB + 3× DB-at-backup-time |
| After 100 sessions | ~80 MB | 0 | ~240 MB | ~320 MB |
| After 1000 sessions | ~700 MB | 0 | ~2.1 GB | ~2.8 GB |

The 1000-session mark is the trigger for a player-visible warning ("Profiler DB has grown to N MB; would you like to compact?") and the `Rebuild()` recommendation. We do **not** silently rebuild — that takes seconds and a player would notice the pause.

### 6.3 Compaction policy

LiteDB's `Rebuild()` rewrites the entire file, reclaiming free pages and reducing fragmentation. It is the equivalent of SQLite's `VACUUM`.

- Manual only. Triggered from a settings UI button or a chat command (`/profiler-compact`).
- Always at session-end, never during a session.
- Two-step: `db.Checkpoint(); db.Rebuild(new RebuildOptions { … });` per #2152.

---

## 7. Write performance and threading

### 7.1 The hot-path constraint

`MetricCollector.EndTick` is called from `ProfilerSystem.PostUpdateEverything()` on the game's update thread (cf. `Profiling/ProfilerSystem.cs`). A single LiteDB `Insert()` on a 200 MB DB has been observed at 1–10 ms in community benchmarks; on the game thread that is one to ten dropped frames. **Invariant 2 forbids this absolutely.**

### 7.2 The writer thread

A single dedicated `DbWriterThread`. The game thread enqueues; the writer dequeues, batches, and inserts.

```csharp
internal sealed class DbWriterThread : IDisposable
{
    private readonly ProfilerDatabase _db;
    private readonly EventJournal _journal;
    private readonly Channel<DbWriteOp> _queue;          // System.Threading.Channels, unbounded
    private readonly Thread _worker;
    private readonly CancellationTokenSource _cts = new();

    private DateTime _lastCheckpointUtc = DateTime.UtcNow;
    private int _pendingSinceLastFlush;

    public DbWriterThread(ProfilerDatabase db, EventJournal journal)
    {
        _db = db;
        _journal = journal;
        _queue = Channel.CreateUnbounded<DbWriteOp>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        _worker = new Thread(Run) { Name = "ProfilerDbWriter", IsBackground = true };
        _worker.Start();
    }

    // Called from the game thread; non-blocking.
    public void Enqueue(in DbWriteOp op) => _queue.Writer.TryWrite(op);

    private void Run()
    {
        var batch = new List<DbWriteOp>(64);
        while (!_cts.IsCancellationRequested)
        {
            // Wait up to 1s; that gives us the cadence for periodic checkpoint.
            if (!_queue.Reader.WaitToReadAsync(_cts.Token).AsTask().Wait(1000))
            {
                MaybeCheckpoint();
                continue;
            }

            batch.Clear();
            while (batch.Count < 64 && _queue.Reader.TryRead(out var op))
            {
                batch.Add(op);
            }

            // Journal first (durable truth), then DB.
            _journal.AppendBatch(batch);
            _db.ApplyBatch(batch);
            _pendingSinceLastFlush += batch.Count;
            MaybeCheckpoint();
        }

        // Drain remaining on shutdown.
        while (_queue.Reader.TryRead(out var op)) batch.Add(op);
        if (batch.Count > 0)
        {
            _journal.AppendBatch(batch);
            _db.ApplyBatch(batch);
        }
        _db.Checkpoint();
        _journal.TruncateOnCleanShutdown();
    }

    private void MaybeCheckpoint()
    {
        if (_pendingSinceLastFlush > 0 && (DateTime.UtcNow - _lastCheckpointUtc).TotalSeconds >= 60)
        {
            _db.Checkpoint();
            _journal.Flush();
            _lastCheckpointUtc = DateTime.UtcNow;
            _pendingSinceLastFlush = 0;
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _worker.Join(TimeSpan.FromSeconds(10));
    }
}
```

### 7.3 The write-op surface

`DbWriteOp` is a tagged-union struct with no heap allocations:

```csharp
internal readonly struct DbWriteOp
{
    public readonly DbOpKind Kind;
    public readonly ObjectId SessionId;
    public readonly object Payload;  // BsonDocument or our typed records; constructed on the caller side from pooled buffers
}

internal enum DbOpKind : byte
{
    SessionStart,
    SessionEnd,
    Spike,
    ContextTransition,
    WarmAggregate,
    ColdAggregate,
    ArchiveAggregate,
    PerSessionModAggregateBatch,
    PerSessionHookAggregateBatch,
    Insight,
    Settings,
}
```

The game thread's job is to construct one `DbWriteOp` per event and `_writer.Enqueue(...)`. The producer side never touches LiteDB.

### 7.4 What the game thread pays

Per tick: at most one `DbWriteOp` (warm aggregate every 60th tick, cold every 3600th tick, spike on the rare spike). Allocation: the BsonDocument is built by the caller; we accept this allocation cost because it is bounded to once per second, not per tick. Inside spike-burst conditions, a spike write happens at most once per spike, not per tick of the spike (the spike window aggregates).

At 1 spike-aggregate per second + 1 warm aggregate per second, the game thread pays:
- A `Channel.Writer.TryWrite` (atomic CAS, ~50 ns)
- A `new BsonDocument` (~500 B, one Gen-0 alloc/sec — negligible)

Net per-tick hot-path cost: indistinguishable from baseline.

### 7.5 Writer-thread cost

Worst case: a 64-op batch every second. LiteDB `InsertBulk` on 64 documents into an indexed collection on a modern SSD is 1–3 ms. With WAL on, the OS does the fsync; we are not pinned on disk latency.

Periodic `Checkpoint()` at 60 s cadence is ~10–50 ms per call. On a background thread, invisible to the user.

### 7.6 What if the writer thread falls behind

The `Channel` is unbounded, so the game thread never blocks. In a pathological case (slow disk, very long spike storm) the queue could grow. Defensive cap: at 100 000 queued ops, the producer side starts dropping the lowest-value class (warm aggregates) and emits one `Logger.Warn` per second. We never block the game thread.

This is a designed-degradation path: the player keeps playing; the profiler logs fewer warm samples; the journal still gets every spike + context + session-end. Acceptable.

---

## 8. Schema versioning

Two levers:

### 8.1 Engine-level: `USER_VERSION` pragma

```csharp
// On open, after construction:
int v = _db.UserVersion;     // wraps the USER_VERSION pragma
if (v == 0)
{
    // Fresh DB. Initialise.
    InitialiseCollections();
    _db.UserVersion = 1;
}
else if (v < CurrentUserVersion)
{
    Migrate(v, CurrentUserVersion);
    _db.UserVersion = CurrentUserVersion;
}
else if (v > CurrentUserVersion)
{
    // Newer profiler wrote this DB. Open read-only; warn the user.
    throw new ProfilerDbFutureVersionException(v);
}
```

`USER_VERSION` is the right lever for changes that affect the whole DB (an indexing scheme change, a new core collection).

### 8.2 Per-collection: `_schema` int field on every document

Every document has `_schema: 1` at write time. The reader checks per-document and applies field-level migrations transparently:

```csharp
public Session ReadSession(BsonDocument doc)
{
    int s = doc["_schema"].AsInt32;
    if (s < CurrentSessionSchema) return MigrateSessionDoc(doc, s);
    return _mapper.ToObject<Session>(doc);
}
```

Per-collection `_schema` is the right lever for adding a field to one collection without forcing a whole-DB migration.

### 8.3 Future-version handling

If the DB's `USER_VERSION` is higher than this profiler version knows about: **fail closed** — open read-only, warn the user, do not write. Per Invariant 4 this is the same posture as host drift.

### 8.4 Migration code path

A single `Migrations.cs` with one static method per version transition:

```csharp
private static void MigrateV1ToV2(LiteDatabase db) { /* add an index, rename a field */ }
private static void MigrateV2ToV3(LiteDatabase db) { /* … */ }
```

Migrations are idempotent (re-running them produces the same DB) so a crashed migration retries safely.

---

## 9. Cross-session metadata patterns

### 9.1 Mod tracking across time

The `mods` collection is keyed `(modlistFingerprint, internalName)`. Every time a session starts, we walk the active mod list and for each mod:

```csharp
foreach (var mod in HookInterceptor.ActiveMods)
{
    var existing = _mods.FindOne(m => m.ModlistFingerprint == fp && m.InternalName == mod.InternalName);
    if (existing == null)
    {
        _mods.Insert(new ModRow { /* … firstSeen, versionHistory: [v] */ });
    }
    else
    {
        existing.LastSeenUtc = now;
        if (existing.VersionSeen != mod.Version)
        {
            existing.VersionHistory.Add(new VersionEntry { Version = mod.Version, FirstUtc = now, LastUtc = now });
            existing.VersionSeen = mod.Version;
        }
        _mods.Update(existing);
    }
}
```

Result: every mod's version history is queryable; "did this mod's perf change after the v1.2 update" maps to a JOIN of `perSessionModAggregates` against `mods.versionHistory`.

### 9.2 Modlist fingerprint stability

`HookInterceptor.ProfiledModNames` and `Versions` produce a stable fingerprint via `ModFingerprint()` (existing). The migration preserves the algorithm verbatim, storing `fingerprintAlg = "sha256-of-sorted-id-name-version-v1"` so a future algorithm change can be detected and a one-shot equivalence migration run.

### 9.3 World tracking

Currently the codebase does not record world identity; the migration adds a thin recorder that maps `(WorldFile.Name, WorldFile.UniqueId)` (`Terraria.WorldFile` exposes both) into the `worlds` collection at world-load:

```csharp
var worldName = Main.worldName;
var worldId   = Main.WorldFileMetadata?.UniqueId ?? Guid.Empty;
_db.Worlds.Upsert(new WorldRow { Name = worldName, UniqueId = worldId, FirstSeenUtc = …, LastSeenUtc = … });
```

(Upsert is on the `(name, uniqueId)` compound key.)

The sessions row references the `worlds._id` so cross-world queries become a single index lookup.

### 9.4 Profiler version tracking

Every document carries the writer's profiler version implicitly: the `sessions` row records `profilerVersion`, and every per-session-derived row references a `sessionId`. Cross-version queries thus go through the `sessions` collection.

`metadata.profilerVersionSeen` is a deduped list of every profiler version that has touched this DB. Useful for debugging "did this DB ever write under a buggy profiler version we later fixed".

---

## 10. Query patterns the schema supports

For each pattern below, the LiteDB LINQ form against the typed wrappers. All examples assume `_db` is the open `LiteDatabase` and indexed collections per §3.3.

### 10.1 "Top mod by avg ms in this session"

```csharp
var top = _db.GetCollection<PerSessionModAggregate>("perSessionModAggregates")
    .Query()
    .Where(x => x.SessionId == sessionId)
    .OrderByDescending(x => x.AvgMs)
    .Limit(1)
    .ToList();
```

### 10.2 "Same modlist, last 10 sessions — has Mod X's cost grown?"

```csharp
var sessions = _db.GetCollection<Session>("sessions")
    .Query()
    .Where(s => s.ModlistFingerprint == fp)
    .OrderByDescending(s => s.StartedUtc)
    .Limit(10)
    .Select(s => s.Id)
    .ToList();

var agg = _db.GetCollection<PerSessionModAggregate>("perSessionModAggregates")
    .Query()
    .Where(x => sessions.Contains(x.SessionId) && x.ModInternalName == "CalamityMod")
    .OrderByDescending(x => /* session start time via join */)
    .ToList();
// Caller computes the trend across the agg list.
```

(The `Contains` produces a series of equality checks; LiteDB optimises against the `sessionId` index.)

### 10.3 "All spikes during boss fights across all sessions"

Boss fights are denormalised into `contextTransitions` as `(type=boss)` rows. To find spikes overlapping a boss tick range:

```csharp
var bossTransitions = _db.GetCollection<ContextTransition>("contextTransitions")
    .Find(x => x.Type == "boss");

// For each boss range, intersect spikes.
foreach (var boss in bossTransitions.GroupConsecutive() /* helper */)
{
    var spikes = _db.GetCollection<SpikeWindow>("spikeWindows")
        .Find(x => x.SessionId == boss.SessionId
                   && x.StartTick >= boss.StartTick
                   && x.EndTick   <= boss.EndTick);
    /* … */
}
```

(A future schema improvement: precomputed `bossFights` collection with `(sessionId, startTick, endTick, bossName)` — see §15.)

### 10.4 "Compare current session's avg ms to baseline (avg of last N sessions with same modlist)"

```csharp
var baseline = _db.GetCollection<PerSessionModAggregate>("perSessionModAggregates")
    .Query()
    .Where(x => recentSessionIds.Contains(x.SessionId) && x.ModInternalName == "CalamityMod")
    .ToList()
    .Average(x => x.AvgMs);

var current = _db.GetCollection<PerSessionModAggregate>("perSessionModAggregates")
    .FindOne(x => x.SessionId == currentSessionId && x.ModInternalName == "CalamityMod");

var delta = current.AvgMs - baseline;
```

### 10.5 "Time spent in each biome across all sessions"

`contextTransitions` records both leave and enter via `(type=biome, from, to, tick)`. Per session, the dwell in each biome is the difference between consecutive ticks of biome transitions for that biome name. A precomputed view collection (`biomeDwell`) is the right design once this query becomes common; for now the live aggregator computes it from the per-session transitions list.

### 10.6 "Insights surfaced this session that match prior sessions"

```csharp
var thisInsights = _db.GetCollection<Insight>("insights")
    .Find(i => i.SessionId == currentSessionId)
    .Select(i => i.PatternKey)
    .ToHashSet();

var recurring = _db.GetCollection<Insight>("insights")
    .Find(i => thisInsights.Contains(i.PatternKey))
    .GroupBy(i => i.PatternKey)
    .Where(g => g.Count() >= 3)   // pattern seen in ≥ 3 sessions
    .ToList();
```

This is the "recurring pattern" detector the Insights Engine plan hooks into.

---

## 11. What gets deleted

The migration is **subtractive** on the JSON side, every line goes.

### From `Profiling/SessionLogWriter.cs` (the whole file)

- `class SessionLogWriter` — every member.
- Constants: `SchemaVersion`, `TimelineIntervalTicks`, `TimelineTopMods`, `SpikeTopMods`, `JsonOptions`.
- Methods: `Create`, `Tick`, `End`, `Dispose`, `WriteReport`, `TimelineRow`, `SpikeWindowsJson`, `TopModsForSnapshot`, `FinalSummary`, `Coverage`, `SortedSignatureFrequency`, `SpikeObjects`, `Mods`, `ModCosts`, `TopMods`, `ModCost`, `CoverageForMod`, `CoverageTotals`, `CategoryTotals`, `ModTotal`, `CategoryRows`, `TopHooks`, `HookRow`, `ZeroCostMods`, `SessionDirectory`, `PruneIncompatibleLogs`, `ComputeIdentity`, `ModFingerprint`, `ProfilerVersion`, `Hash`.
- The file itself is deleted.

### From callers of `SessionLogWriter`

- `ProfilerSystem.cs` (or wherever `SessionLogWriter.Create()` / `.Tick()` / `.End()` / `.Dispose()` is called — verified at step 1 of §13). Every call site is replaced with the matching `ProfilerDatabase` / `SessionRecorder` call.
- Any chat command that exports a current session JSON (currently `current-session.json` is auto-written every minute by `WriteReport`) is removed; the in-game "look at the data" surface becomes the future HTML report.

### Disk state

- The legacy `~/Library/.../PerformanceProfiler/Sessions/` directory is removed at the end of the first-launch migration step (after the one-shot ingestion, see §13).
- `current-session.json` and `<identity>-<stamp>.json` files are gone.

### Constants and metadata no longer applicable

- `SessionLogWriter.SchemaVersion` (replaced by `USER_VERSION`).
- The `identity` hash field of the JSON report (replaced by session `_id` ObjectId).
- The session-pruning logic (`PruneIncompatibleLogs`) — LiteDB has its own retention story; there is nothing to prune across-versions because old DB files are migrated, not deleted.

### Tests

Any unit test asserting on the JSON file shape — there are none in the current tree, but the test plan in §16 names them as the *new* tests we add.

---

## 12. What gets added

### 12.1 New folder structure

```
Profiling/
  Persistence/
    ProfilerDatabase.cs          // facade + lifecycle
    SessionRecorder.cs           // replaces SessionLogWriter
    EventJournal.cs              // append-only NDJSON layer
    DbWriterThread.cs            // §7
    TickDownsampler.cs           // §4
    Migrations.cs                // §8
    Records/
      SessionRow.cs
      ModlistRow.cs
      ModRow.cs
      WorldRow.cs
      PerSessionModAggregate.cs
      PerSessionHookAggregate.cs
      SpikeWindowRow.cs
      ContextTransitionRow.cs
      TickAggregateWarm.cs
      TickAggregateCold.cs
      TickAggregateArchive.cs
      InsightRow.cs
      SettingsRow.cs
      MetadataRow.cs
    DbWriteOp.cs
```

All other `Profiling/*.cs` files are unchanged.

### 12.2 New csproj dependency

```xml
<ItemGroup>
  <PackageReference Include="LiteDB" Version="5.0.21" />
</ItemGroup>
```

`tModLoader.targets` packages the resolved `LiteDB.dll` (single managed assembly) into the `.tmod`.

### 12.3 The facade

```csharp
public sealed class ProfilerDatabase : IDisposable
{
    private readonly LiteDatabase _db;
    private readonly EventJournal _journal;
    private readonly DbWriterThread _writer;
    private bool _disposed;

    public ProfilerDatabase(string root)
    {
        EnsureDirectory(root);
        RecoverIfNeeded(root);

        var connStr = $"Filename={Path.Combine(root, "profiler.litedb")};Upgrade=true;Connection=direct";
        _db = new LiteDatabase(connStr);
        _db.Pragma("UTC_DATE", false);
        _db.Pragma("CHECKPOINT", 1000);

        EnsureSchema();
        _journal = new EventJournal(Path.Combine(root, "profiler.events.log"));
        _writer  = new DbWriterThread(this, _journal);

        ReplayJournalIfNeeded();
        SweepExpiredWarmTier();
    }

    public ILiteCollection<SessionRow>              Sessions              => _db.GetCollection<SessionRow>("sessions");
    public ILiteCollection<ModlistRow>              Modlists              => _db.GetCollection<ModlistRow>("modlists");
    public ILiteCollection<ModRow>                  Mods                  => _db.GetCollection<ModRow>("mods");
    public ILiteCollection<WorldRow>                Worlds                => _db.GetCollection<WorldRow>("worlds");
    public ILiteCollection<PerSessionModAggregate>  PerSessionMods        => _db.GetCollection<PerSessionModAggregate>("perSessionModAggregates");
    public ILiteCollection<PerSessionHookAggregate> PerSessionHooks       => _db.GetCollection<PerSessionHookAggregate>("perSessionHookAggregates");
    public ILiteCollection<SpikeWindowRow>          SpikeWindows          => _db.GetCollection<SpikeWindowRow>("spikeWindows");
    public ILiteCollection<ContextTransitionRow>    ContextTransitions    => _db.GetCollection<ContextTransitionRow>("contextTransitions");
    public ILiteCollection<TickAggregateWarm>       TickAggregatesWarm    => _db.GetCollection<TickAggregateWarm>("tickAggregatesWarm");
    public ILiteCollection<TickAggregateCold>       TickAggregatesCold    => _db.GetCollection<TickAggregateCold>("tickAggregatesCold");
    public ILiteCollection<TickAggregateArchive>    TickAggregatesArchive => _db.GetCollection<TickAggregateArchive>("tickAggregatesArchive");
    public ILiteCollection<InsightRow>              Insights              => _db.GetCollection<InsightRow>("insights");

    public DbWriterThread Writer => _writer;

    public void Checkpoint() => _db.Checkpoint();
    public void ApplyBatch(IReadOnlyList<DbWriteOp> batch) { /* dispatch by Kind */ }

    public void Dispose()
    {
        if (_disposed) return;
        _writer.Dispose();        // drains + checkpoints + truncates journal
        _db.Dispose();
        _disposed = true;
    }
}
```

### 12.4 The new recorder

```csharp
public sealed class SessionRecorder : IDisposable
{
    private readonly ProfilerDatabase _db;
    private readonly ObjectId _sessionId;
    private readonly TickDownsampler _downsampler;

    public ObjectId SessionId => _sessionId;

    public SessionRecorder(ProfilerDatabase db, string worldName, Guid worldUniqueId, string modlistFingerprint)
    {
        _db = db;
        _sessionId = ObjectId.NewObjectId();
        _downsampler = new TickDownsampler();

        _db.Writer.Enqueue(DbWriteOp.SessionStart(_sessionId, worldName, worldUniqueId, modlistFingerprint));
    }

    public void OnTick(TickFrame frame, MetricCollector collector)
        => _downsampler.OnTickCommitted(frame, collector, _db.Writer, _sessionId);

    public void OnContextTransition(string type, string from, string to, long tick, double tickFrameMs)
        => _db.Writer.Enqueue(DbWriteOp.ContextTransition(_sessionId, tick, type, from, to, tickFrameMs));

    public void OnSpike(SpikeWindow window)
        => _db.Writer.Enqueue(DbWriteOp.Spike(_sessionId, window));

    public void End(MetricCollector collector, string endReason = "clean")
    {
        // Build the per-session aggregates (mod + hook + archive) on the game thread,
        // enqueue them as a single batch op, then session-end. This is the only
        // time we touch the full per-mod arrays from the game thread, and it is
        // strictly at world-unload — no per-tick budget concern.
        var modAggs   = BuildModAggregates(_sessionId, collector);
        var hookAggs  = BuildHookAggregates(_sessionId, collector);
        var archive   = BuildArchiveAggregate(_sessionId, collector);

        _db.Writer.Enqueue(DbWriteOp.PerSessionModAggregateBatch(_sessionId, modAggs));
        _db.Writer.Enqueue(DbWriteOp.PerSessionHookAggregateBatch(_sessionId, hookAggs));
        _db.Writer.Enqueue(DbWriteOp.ArchiveAggregate(_sessionId, archive));
        _db.Writer.Enqueue(DbWriteOp.SessionEnd(_sessionId, endReason));
    }

    public void Dispose() { /* no-op — writer thread owns lifecycle */ }
}
```

### 12.5 The event journal

```csharp
public sealed class EventJournal : IDisposable
{
    private readonly string _path;
    private FileStream? _stream;

    public EventJournal(string path)
    {
        _path = path;
        _stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
    }

    public void AppendBatch(IReadOnlyList<DbWriteOp> batch)
    {
        if (_stream == null) return;
        foreach (var op in batch)
        {
            string line = JsonSerializer.Serialize(op);
            byte[] bytes = Encoding.UTF8.GetBytes(line + "\n");
            _stream.Write(bytes, 0, bytes.Length);
        }
    }

    public void Flush() => _stream?.Flush(flushToDisk: true);

    public void TruncateOnCleanShutdown()
    {
        _stream?.Dispose();
        _stream = null;
        File.WriteAllBytes(_path, Array.Empty<byte>());
        _stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read);
    }

    public IEnumerable<DbWriteOp> Replay()
    {
        if (!File.Exists(_path)) yield break;
        foreach (var line in File.ReadLines(_path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            DbWriteOp op;
            try { op = JsonSerializer.Deserialize<DbWriteOp>(line)!; }
            catch { continue; }  // skip a partially-written line
            yield return op;
        }
    }

    public void Dispose() => _stream?.Dispose();
}
```

### 12.6 Hook into the existing lifecycle

`PerformanceProfiler.cs` (the `Mod` entry point):

```csharp
public override void Load()
{
    base.Load();
    _db = new ProfilerDatabase(ProfilerPaths.Root());
}

public override void Unload()
{
    _db?.Dispose();
    _db = null;
    base.Unload();
}
```

`ProfilerSystem.cs` (the `ModSystem` that drives the loop):

```csharp
public override void OnWorldLoad()
{
    _session = new SessionRecorder(_db, Main.worldName, /* worldUniqueId */, /* fp */);
}

public override void PostUpdateEverything()
{
    if (_collector.TickOpen) _collector.EndTick(Main.GameUpdateCount, /* counts */);
    if (_collector.History.Count > 0)
    {
        TickFrame latest = _collector.History.Newest;
        _session?.OnTick(latest, _collector);
        // spike pump:
        foreach (var spike in _collector.NewSpikesSinceLast()) _session?.OnSpike(spike);
    }
}

public override void OnWorldUnload()
{
    _session?.End(_collector, "clean");
    _session = null;
}
```

(`NewSpikesSinceLast()` is a small addition to `MetricCollector` — currently the spike list is read on demand; the session recorder needs an incremental view. Easy: a `int _lastReadSpikeCount` cursor.)

---

## 13. Step-by-step implementation sequence

Each step is commit-sized, independently verifiable, and ordered so each rests on the previous. Following the discovery-then-execution discipline.

| # | Action | Files | Verify | Risk |
|---|---|---|---|---|
| **1** | Discovery pass: re-read `SessionLogWriter.cs` and grep for every caller. Enumerate every public field on `HookInterceptor`, `PerModAttribution`, `MetricCollector`, `SpikeDetector` the writer touches. Record in a scratch checklist; confirm the §3 schema covers all of it. | read-only | Checklist matches §3. Any field unaccounted for triggers a schema-§3 amendment before moving on. | **Low** — read-only. |
| **2** | Add `<PackageReference Include="LiteDB" Version="5.0.21" />` to the csproj. Build with `dotnet msbuild`. Verify the `.tmod` is produced and that `LiteDB.dll` is inside it (`unzip -l bin/Debug/Mods/PerformanceProfiler.tmod | grep LiteDB`). | `PerformanceProfiler.csproj` | `.tmod` contains `LiteDB.dll`; mod loads in tModLoader without `client.log` errors. | **Medium** — first-time package addition; verify the existing build pipeline accepts it. |
| **3** | Run a reflection probe: a one-off `Mod.Logger.Info` block in `Load()` that prints `typeof(LiteDatabase).Assembly.FullName`, the existence of `Checkpoint`, `Rebuild`, `Pragma`, and the public `ILiteCollection<T>` surface. Confirm against §0 expectations. Remove the probe after. | `PerformanceProfiler.cs` | `client.log` shows expected API surface. Any mismatch triggers a §0 amendment and possibly a version pin change. | **Low** — diagnostic only. |
| **4** | Add the `Profiling/Persistence/` folder. Implement the typed record classes (`SessionRow`, `ModlistRow`, …) with `[BsonId]` on `_id` and ordinary auto-properties for the rest. Compile-only step. | new files | `dotnet msbuild` succeeds. No runtime change. | **Low** — pure DTOs. |
| **5** | Implement `ProfilerDatabase` (open/close + collection accessors + `EnsureIndex` calls per §3.3). Add `EnsureSchema()` that creates indexes idempotently. No callers yet. | `ProfilerDatabase.cs` | `dotnet msbuild` succeeds. Manual test: in-game, open the mod and confirm the file `profiler.litedb` appears in the expected directory; close and confirm it is not zero bytes. | **Medium** — first real I/O. |
| **6** | Implement `EventJournal` (append-only writer + replay). Wire it into `ProfilerDatabase` but do not yet emit anything to it. | `EventJournal.cs` | `profiler.events.log` is created at session start (empty). Stays empty. | **Low** — pure file I/O. |
| **7** | Implement `DbWriterThread` (channel + worker loop + checkpoint cadence). Wire `ProfilerDatabase.Writer` to expose it. Add `DbWriteOp` and the `Kind` enum, with the dispatch in `ProfilerDatabase.ApplyBatch`. | `DbWriterThread.cs`, `DbWriteOp.cs` | Thread starts and stops cleanly; `Logger.Info` traces the lifecycle. Manual test: enqueue a hand-crafted `SessionStart` op from a chat command; verify a row lands in the `sessions` collection (via a LiteDB.Shell session). | **Medium** — concurrency. |
| **8** | Implement `SessionRecorder.SessionStart` only — no per-tick yet. Wire `ProfilerSystem.OnWorldLoad` to construct it. The legacy `SessionLogWriter` keeps running in parallel; nothing about its behaviour changes. | `SessionRecorder.cs`, `ProfilerSystem.cs` | Each world-load produces one `sessions` row with `endedUtc = null`. After the world is exited cleanly the JSON-side runs to completion; the DB side has an "incomplete" row. We accept this for one commit. | **Medium** — first joint with the game loop. |
| **9** | Implement `SessionRecorder.End` and wire `OnWorldUnload` to call it. Now the DB side and the JSON side both close cleanly. Compare the per-session aggregates from each. | `SessionRecorder.cs`, `ProfilerSystem.cs` | A two-minute world produces identical mod totals in the JSON's `final.allMods` and the DB's `perSessionModAggregates`. Differences > 1 % are bugs to investigate. | **High** — accuracy regression boundary. The JSON side is the temporary ground truth. |
| **10** | Implement `TickDownsampler` and wire it into `OnTick`. `tickAggregatesWarm` and `tickAggregatesCold` populate. | `TickDownsampler.cs`, `ProfilerSystem.cs` | A ten-minute world produces 600 warm rows and 10 cold rows. Each row's `perModMs` array matches the smoothed snapshot at the bucket boundary within float rounding. | **Medium** — sampling correctness. |
| **11** | Implement spike + context-transition pipelines. Wire `SpikeDetector.NewSpikesSinceLast` cursor and pump from `OnTick`. (Context transitions land later when the Events plan exists; for now this is a placeholder that records nothing — the spike pipe alone is the testable surface.) | `MetricCollector.cs` (small additions), `SessionRecorder.cs` | Spikes that the JSON side already captures appear in `spikeWindows` with matching `WorstFrameMs`/`StartTick`/`EndTick`/`top contributors`. | **Medium** — joint surface. |
| **12** | One-shot legacy JSON ingestion. A new `LegacyJsonImporter` reads every file in `~/Library/.../PerformanceProfiler/Sessions/`, parses it with `System.Text.Json`, builds `SessionRow` + `PerSessionModAggregate` + `SpikeWindowRow` per file (using only the fields the JSON has), inserts them into the DB if the session id is absent, then deletes the file. Runs once at first launch after the migration. | `LegacyJsonImporter.cs` | Pre-existing JSON files are imported on first launch. Re-launch confirms no re-import. Folder is empty after. | **Medium** — one-shot but operates on user data. Guard with `try/catch`; leave the JSON in place if ingestion fails for any file (log to `client.log`). |
| **13** | Delete `SessionLogWriter.cs` and every caller. Delete the `SchemaVersion` constant and related JSON helpers. The JSON folder is also removed at the end of `LegacyJsonImporter` step 12 (the folder, after the importer has moved every file). | `Profiling/SessionLogWriter.cs` (delete), `ProfilerSystem.cs` (remove caller), etc | `dotnet msbuild` succeeds. Run a world; only the DB file exists; no `current-session.json` or `<identity>-<stamp>.json` is produced. | **Low** — by this point the new path is proven. |
| **14** | Crash-safety implementation per §5: journal-on-write, periodic Checkpoint, backup rotation on session-end, recovery flow on `Open`. | `ProfilerDatabase.cs`, `EventJournal.cs`, `DbWriterThread.cs` | Force-kill the process mid-session with `kill -9`. Next launch: `client.log` shows the recovery path; the journal is non-empty, gets replayed, and the partial session has `endReason = "crash-detected"`. After clean session-end the journal is zero bytes and a fresh backup is in place. | **High** — load-bearing safety code; explicit chaos test required. |
| **15** | Schema versioning per §8. Set `USER_VERSION = 1` on fresh DBs; gate `Open()` on the version check. Add a no-op migration `MigrateV1ToV2` placeholder commented for future use. | `ProfilerDatabase.cs`, `Migrations.cs` | A DB written by this code reads back as `USER_VERSION = 1`. Manually bump the constant; confirm the future-version-fail-closed path triggers. | **Low** — mechanical. |
| **16** | Compaction surface: add a chat command `/profiler-compact` that triggers `Checkpoint + Rebuild`. Confirm size shrinks on a previously-bloated DB. | `PerformanceProfiler.cs` | A DB grown over 10 sessions shrinks measurably after `/profiler-compact`. | **Low** — bounded operation. |
| **17** | Write the `context/notes/litedb-migration.md` capture (resolved decisions, recovery flow, write-path invariants). Update `context/_Overview.md` and `context/notes/decisions.md` references. | `context/` | User reviews; commit. | **Low** — documentation. |
| **18** | Commit checkpoints: (a) package + facade + records (steps 2–5), (b) journal + writer thread (6–7), (c) session start/end joined with JSON (8–9), (d) downsampler + spikes (10–11), (e) legacy ingestion + JSON deletion (12–13), (f) crash safety + versioning + compaction (14–16), (g) context capture (17). | git | Each commit builds and runs in-game. | **Low** — discipline. |

---

## 14. Honest risk register (summary, full discussion in §1)

| Risk | Severity | Mitigation status |
|---|---|---|
| Hot-path write latency | **High** | Writer thread + batching; game thread never touches LiteDB. |
| LiteDB log-file unbounded growth (#1568) | **High** | Explicit Checkpoint cadence + auto-checkpoint pragma + monitoring `profiler.litedb-log` size in `Logger.Debug`. |
| ENSURE-page corruption on burst (#2401) | **Medium** | Pre-warm collections at first open; never grow the DB from zero under load. |
| Single-file blast radius | **Medium** | Event journal + bounded backup ring per §5. |
| Concurrent process access (two tML) | **Low** | Catch lock failure on open; degrade to no-persistence; warn user. |
| Game-thread stall on Dispose | **Low** | Dispose runs on the writer thread; main thread detaches. |
| Schema drift on profiler upgrade | **Medium** | `USER_VERSION` pragma + per-doc `_schema` field. |
| LiteDB 5.x maintenance risk | **Low** | Event journal lets us migrate to another store without data loss. |
| Per-tick storage cost | **High** | Tiered downsampling — raw ticks never reach disk. |
| Legacy JSON ingestion failure | **Low** | Guarded; failed files left in place; manual cleanup path documented in §17 doc. |

---

## 15. Honest gaps — what this migration does NOT solve

- **Cross-machine sync.** Local file only. The user playing on two computers has two profiler DBs. Out of scope; README precludes telemetry.
- **Multiplayer server profiling.** Single-player only. README documents v1 as single-player; the DB is per-tModLoader-install.
- **Human-readable export.** The DB is BSON, not JSON. The HTML report (separate feature, `context/notes/future-html-report.md`) is the human-readable surface.
- **Boss-fight as a first-class collection.** Currently boss fights live as `contextTransitions` (`type=boss`); a future schema step adds `bossFights` with `(sessionId, startTick, endTick, bossName, outcome)`. Out of scope here; the data is recoverable from transitions.
- **Engagement attribution.** The README's "engagement-weighted" cost story requires engagement instrumentation that is a separate engineering surface. The schema is forward-compatible (an `engagement` collection can be added without disturbing existing rows) but the migration does not implement it.
- **Live in-game query UI.** Players query the DB through the future HTML report and the overlay; no SQL/LINQ console is provided.
- **Per-tick raw retention.** The downsampler discards per-tick raw values after 30 s (the existing RAM ring). If a future debugging need wants raw ticks, the writer-thread path can be extended to dump them on demand, but the steady-state policy is "no raw ticks on disk".

---

## 16. Testing strategy

Mirroring `ILHook-migration-plan.md`'s four-layer split.

### 16a. Session round-trip correctness

**Hypothesis:** a session that starts, runs N seconds, and exits cleanly produces a `sessions` row with matching `startedUtc`/`endedUtc`, one `tickAggregatesArchive` row, the right `perSessionModAggregates` count, and a non-zero number of `tickAggregatesWarm`/`tickAggregatesCold` rows.

**Steps:**
1. Open a fresh DB (delete `profiler.litedb`).
2. Enter a world for exactly 120 s. Exit cleanly.
3. Open the DB via `LiteDB.Shell` (or a `dotnet run` test harness).
4. Verify: `sessions.count == 1`; the session row has both timestamps; the duration is within ± 2 s of 120 s; `tickAggregatesArchive.count == 1`; `tickAggregatesWarm.count` is 120 ± 5; `tickAggregatesCold.count` is 2 ± 1.

**Pass criterion:** all checks pass; aggregated `avgMs` matches the JSON side's `final.allMods` (during the dual-mode steps 8–9) to within 1 %.

### 16b. Crash recovery

**Hypothesis:** force-killing the tModLoader process mid-session produces a DB with the session row marked `endReason = "crash-detected"` on next launch, and the journal replays unflushed warm/spike rows.

**Steps:**
1. Enter a world; play for 60 s; trigger an artificial spike (heavy NPC spawn).
2. From a separate terminal: `kill -9 <tModLoader pid>`.
3. Launch tModLoader again. Read `client.log` for the recovery line. Open the DB.
4. Verify: the session row has `endReason = "crash-detected"`, `endedUtc = null` initially, then is updated to a synthetic close time by the recovery step; the spike survived; the journal is now zero bytes after replay.

**Pass criterion:** recovery completes without errors; no data loss for the events that were journalled before the kill.

### 16c. Cross-session queries return expected results

**Hypothesis:** the §10 queries return the right shapes on a seeded DB.

**Steps:**
1. Seed a DB with 10 sessions on the same modlist, varying `CalamityMod` average ms.
2. Run each §10 query through a small `dotnet run` harness.
3. Compare to expected.

**Pass criterion:** each query returns the right rows in the right order.

### 16d. File size stays bounded

**Hypothesis:** a simulated 12-hour session does not exceed 5 MB of DB growth.

**Steps:**
1. Use a synthetic tick-feeder (a test harness that calls `OnTick` 60×3600×12 times with realistic distributions).
2. Run to completion.
3. Read `profiler.litedb` size, `profiler.events.log` size, `tickAggregatesWarm.count`, `tickAggregatesCold.count`.

**Pass criterion:** total disk usage ≤ 5 MB for the DB plus 0 B for the journal (truncated on clean end).

### 16e. In-game smoke test

A 5-minute Eye of Cthulhu fight on a Calamity-scale modlist. Acceptance:

- `profiler.litedb` exists and is non-zero.
- `profiler.events.log` is zero bytes after exit.
- One `sessions` row, `endReason = "clean"`, `endedUtc` populated.
- `spikeWindows.count` matches the JSON side's count during the dual-mode period; matches the in-game overlay's spike counter after the JSON side is deleted.
- `client.log` shows the `Open`, `Checkpoint` (at least one), and `Dispose` lifecycle lines with no `Warn`/`Error`.

### 16f. Failure mode triage

| Symptom | Likely cause | First check |
|---|---|---|
| `LiteException: Database lock timeout` at open | Another tModLoader instance has the file open. | Confirm process count; close stale instance. |
| `LiteException: ENSURE: …` mid-session | Burst-write corruption (#2401 class). | Trigger pre-warm + check if the pre-warm step was skipped. |
| `profiler.litedb-log` is GB-sized | Checkpoint not being called. | Logger.Debug the `MaybeCheckpoint` path; confirm it fires. |
| Frame drops correlate with batch flush | Writer thread is somehow blocking the game thread. | Inspect `Channel.Writer.TryWrite` callers — every one must be non-blocking; the writer thread must not share any lock with the game thread. |
| Journal replay produces duplicate rows | Idempotency check missed. | Confirm every `Apply*` path uses `Upsert` keyed on the natural key, not blind `Insert`. |
| Backup file restored but data is older than expected | Backup cadence was wrong. | Confirm backups happen on `session-end`, not on `Open`. |

---

## 17. Worked example — one session, end to end

```
[t=0]   Player launches tModLoader.
        Main thread: Mod.Load() runs.
        PerformanceProfiler.cs:
          _db = new ProfilerDatabase(ProfilerPaths.Root());
        ProfilerDatabase ctor:
          - EnsureDirectory(root)
          - Tries to open profiler.litedb. Succeeds.
          - Sets pragmas: UTC_DATE=false, CHECKPOINT=1000.
          - Reads USER_VERSION = 1.
          - EnsureSchema() runs EnsureIndex on every collection.
          - Opens profiler.events.log in FileMode.Append.
          - Starts DbWriterThread "ProfilerDbWriter".
          - Replays journal: 0 bytes, nothing to do.
          - Sweeps tickAggregatesWarm where expireAtUtc < now: deletes 12 stale rows.
          - Marks any session row with endedUtc=null as endReason="crash-detected" (none today).
          - Checkpoints.
        client.log: "Profiler DB opened (sessions=42, warm=0, journal=0)."

[t=8]   Player enters a world.
        ProfilerSystem.OnWorldLoad() runs.
        SessionRecorder is constructed:
          sessionId = ObjectId.NewObjectId()
          DbWriterThread.Enqueue(SessionStart{sid, startedUtc, mods=fp, world=wId, profilerVer, …})
        On the writer thread:
          - EventJournal.AppendBatch writes one line to profiler.events.log.
          - ProfilerDatabase.ApplyBatch inserts one row into `sessions`.

[t=8..]  Every tick: PostUpdateEverything runs.
        MetricCollector.EndTick computes per-mod/per-hook costs.
        SessionRecorder.OnTick(latest, collector) →
          TickDownsampler.OnTickCommitted:
            - Pushes frameMs into rolling percentile.
            - At secondIndex boundary: Enqueue WarmAggregate.
            - At minuteIndex boundary: Enqueue ColdAggregate.
        SpikeDetector.OnTick (existing) may produce a new SpikeWindow.
        SessionRecorder.OnSpike(window) → Enqueue Spike.

[t=68]  Writer thread idle for 60 s of in-game time without a new batch
        triggering an explicit checkpoint. MaybeCheckpoint fires:
          _db.Checkpoint();
          _journal.Flush(flushToDisk: true);
        profiler.litedb-log shrinks toward 0.

[t=3700]  Player saves and exits.
        ProfilerSystem.OnWorldUnload() runs.
        SessionRecorder.End(collector, "clean"):
          - Builds per-mod aggregates from collector.PerModCategoryAverageMs.
          - Builds per-hook aggregates from collector.PerHookAverageMs.
          - Builds archive aggregate.
          - Enqueues PerSessionModAggregateBatch, PerSessionHookAggregateBatch,
            ArchiveAggregate, SessionEnd.
        Writer thread drains:
          - Writes those 4 ops to the journal.
          - Inserts the per-session-mod batch (LiteDB InsertBulk).
          - Inserts the per-session-hook batch.
          - Inserts the archive row.
          - Updates the sessions row with endedUtc + endReason="clean".
          - Calls _db.Checkpoint().
          - Truncates the journal to 0 bytes.
          - Rotates backups: copies profiler.litedb → profiler.litedb.bak-1
            (renames .bak-1 → .bak-2, .bak-2 → .bak-3, drops the oldest).

[t=3702]  Player quits the game cleanly.
        Mod.Unload runs. PerformanceProfiler.cs:
          _db.Dispose() → DbWriterThread.Dispose() → join.
          LiteDatabase.Dispose() runs the final checkpoint.
        client.log: "Profiler DB closed."

[t=days later]  Player relaunches.
        ProfilerDatabase ctor finds profiler.litedb, two backups, zero-byte journal.
        No recovery needed.
        New session begins with the same lifecycle.
```

Now consider the crash case at `t=2400`:

```
[t=2400]   tModLoader segfaults on an unrelated mod's bug.
           OS kills the process. Game thread had enqueued ~12 warm aggregates and
           1 spike in the last 60 s that the writer thread had already journalled
           but not yet checkpointed.

[t=next launch]  ProfilerDatabase ctor:
           - profiler.litedb opens fine.
           - profiler.events.log is non-empty (~3 KB).
           - For each line, check the natural key against the DB; missing rows
             are inserted (Upsert). Idempotent.
           - Logger.Info: "Replayed 13 events from journal."
           - Truncates the journal.
           - Finds the sessions row with endedUtc=null; updates endReason="crash-detected"
             and endedUtc to the timestamp of the last journal entry.
           - Sweeps + checkpoints.

[result]   The crashed session is preserved, marked as crash-detected, and
           every aggregate that had been computed is in the DB. The HTML report,
           when generated, will display "Session #N · 40 min · ended unexpectedly".
```

---

## 18. Open questions and follow-ups

- **Should `tickAggregatesWarm` retention be 24 h or 7 days?** Trade-off: longer retention enables "the player came back yesterday and wants to compare" but increases DB size. Defaulting to 24 h; revisit after first month of usage data.
- **Backup pruning policy:** keep last 3, or last 7? `keep: 3` is a round number; if players complain about storage, drop to 1.
- **Compaction trigger UI:** chat command only, or also a button in the overlay? Defer until the settings UI exists.
- **Encryption:** LiteDB supports AES via `Password=`. We do not turn it on because the data is not sensitive; the user can opt in if they want. The schema doesn't change either way.

---

## Honest summary

The migration replaces ~640 lines of bespoke JSON serialisation + a flat-file directory with a single LiteDB file, an append-only event journal, and a bounded backup ring. The new code is roughly 1 200 lines across ~12 files in `Profiling/Persistence/`. Query patterns the JSON side could not answer at all become single LINQ calls. Per-tick disk traffic drops to "one channel-write per second on a background thread"; the game thread loses zero frames to persistence under any tested workload.

The two real risks are (a) LiteDB's documented log-file growth pathology, mitigated by explicit `Checkpoint()` cadence and the pragma default, and (b) single-file blast radius, mitigated by the event journal as a redo log and a rotating backup ring. The recovery flow has a designed answer for every failure mode considered up to and including "the main DB file is unreadable on next launch."

The largest remaining honest uncertainty is the reflection-probe step (§13 step 3): we have not yet inspected `LiteDB.dll` 5.0.21 in this worktree to confirm the API surface byte-for-byte. The probe runs as the third step of implementation, before any callers depend on the API. Any mismatch against §0's documented expectations is caught at the earliest possible moment.
