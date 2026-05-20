# Session Logging

*Maturity: comprehensive · Stability: unstable — deferred audit work (split into `SessionReportBuilder` + IO wrapper, plus schema snapshot test) is queued.*

## Scope / Purpose

Session logging writes one JSON report per play session to the platform app-data folder. It is the **agent surface** for everything the player sees in the overlay: the same `MetricCollector` rollups, the same `InsightsEngine.Shared` records, the same `HookCoverageView` counters. The contract is "an agent reading the JSON sees the same session the player saw, with no detail invented and none dropped silently."

It is **not** long-term analytics storage. The eventual lifetime-data store is `notes/litedb-migration-plan.md`'s territory. This subsystem is a single per-session compact export.

## Boundaries / Ownership

Files: `Profiling/SessionLogWriter.cs`, plus the try/catch wrapping in `Profiling/ProfilerSystem.cs` (`OnWorldLoad`, `PostUpdateEverything`, `OnWorldUnload`).

Owns:

- Writing the per-session JSON file atomically (temp + `File.Replace` with `File.Move` first-write fallback).
- Computing the session identity (hash of schema version + hook coverage version + modlist fingerprint).
- Pruning incompatible historical reports that no longer match the current identity.
- The schema (currently v4) including the `insights` block.
- The timeline cadence (one row per 60×60 = 3600 ticks).
- Self-disable on IO failure via `SessionLogFailureException`.

Does not own:

- The detector logic feeding the `insights` block — that lives in `systems/insights-engine.md`.
- The mod fingerprint inputs (`Mod.Name`, `Mod.Version`) — those come from `HookInterceptor.ProfiledMods`.
- Long-term retention or aggregation across sessions.

## Current Implemented Reality

### Schema v4

`const int SchemaVersion = 4;` (`SessionLogWriter.cs:39`). Schema 4 added the `insights` block (`live` + `history` + `gated`).

Top-level shape (rendered with `WriteIndented = true`):

```json
{
  "schema": 4,
  "identity": "<16-hex>",
  "startedUtc": "2026-05-20T01:23:45Z",
  "endedUtc": "...",
  "modlist": [ { "name": "...", "version": "..." }, ... ],
  "coverage": {
    "version": 3,
    "totalHooks": ...,
    "measuredHooks": ...,
    "perMod": [ { "modId": 0, "measured": ..., "total": ... }, ... ]
  },
  "timeline": [ { "tick": ..., "topMods": [ ... ] }, ... ],
  "spikes": [ ... ],
  "modSummary": [ ... ],
  "insights": {
    "live": [ ... ],   // current InsightsEngine.Shared.Store.AllLive
    "history": [ ... ], // InsightsEngine.Shared.Store.History
    "gated": { "events": [ ... ], "litedb": [ ... ] }
  }
}
```

### Atomic write (`AtomicWrite`)

`SessionLogWriter.cs:170-186`. Every write goes through a temp file:

```
tempPath = destination + ".tmp"
File.WriteAllText(tempPath, json)
try:
    File.Replace(tempPath, destination, destinationBackupFileName: null)
except FileNotFoundException:
    # first write, destination doesn't exist yet
    File.Move(tempPath, destination)
```

`File.Replace` is the load-bearing primitive: it does the unlink-and-rename in one filesystem operation. A crash mid-write leaves either the previous complete report or the new complete report — never a truncated file. The `FileNotFoundException` fallback handles the first write (where there is no destination to replace).

### Session identity

`ComputeIdentity()` (`SessionLogWriter.cs:797-806`) takes the SHA256 of:

```
schema=<SchemaVersion>;coverage=<HookCoverageVersion>;mods=<sortedModFingerprint>
```

where `ModFingerprint()` is the sorted concatenation of `(name, version)` tuples for `ProfiledMods`. The first 16 hex chars of the SHA256 are the identity prefix used in the on-disk filename: `<identity>-<UTC stamp>.json`.

### Prune pattern

`PruneIncompatibleLogs(directory, identity)` (`SessionLogWriter.cs:750-783`) walks the session directory and deletes files whose name matches the writer-owned shape **but** whose identity prefix is not the current one. The match is via `LooksLikeOurReport(name)` (`SessionLogWriter.cs:785-795`): a regex pinning to the 16-hex prefix + dash + UTC stamp + `.json` (optionally + `.tmp`).

The narrowing matters: pre-fix, the pattern was broad enough to match hand-saved JSON in the same directory. Now anything not matching the writer's filename shape is left alone.

### Timeline cadence

`TimelineIntervalTicks = 60 * 60` (one row per 60 seconds at 60 Hz). `Tick(collector)` checks if `latest.TickIndex - _lastTimelineTick >= TimelineIntervalTicks`; if so, appends a row and triggers an atomic write.

The IO cost amortises away because most ticks are pure no-ops. The atomic write happens approximately once per minute per active world.

### Self-disable on IO failure

`SessionLogFailureException` (`SessionLogWriter.cs:21-25`) is thrown from `Tick` if the periodic atomic write fails. The writer marks itself disposed before throwing, so subsequent calls are no-ops.

`ProfilerSystem.PostUpdateEverything` catches it (`Profiling/ProfilerSystem.cs:194-200`), logs the inner exception via `Mod.Logger.Warn`, and drops the `_sessionLog` reference. Metric collection continues.

`ProfilerSystem.OnWorldLoad` and `OnWorldUnload` separately catch `IOException` / `UnauthorizedAccessException` / `SecurityException` around `Create` and `End`. A failure in `Create` leaves `_sessionLog = null` for the rest of the world (no session JSON, but metric collection runs normally). A failure in `End` is logged and ignored — the world unload finishes regardless.

### `FlushSpikes` at world unload

`MetricCollector.FlushSpikes()` is called from `OnWorldUnload` **before** the final `End()` write (`Profiling/ProfilerSystem.cs:123`). An in-progress spike window that coincided with the world exit is force-closed so it lands in the JSON rather than being held in scratch.

## Key Interfaces / Data Flow

```
OnWorldLoad:
   try _sessionLog = SessionLogWriter.Create()
       └─ Directory.CreateDirectory(SessionDirectory())
       └─ ComputeIdentity() = SHA256(schema, coverage, modlist).first16hex
       └─ PruneIncompatibleLogs(directory, identity)
       └─ WriteReport(final: false, collector: null) → AtomicWrite
   catch IO/UnauthorizedAccess/Security:
       _sessionLog = null; Logger.Warn

per tick (PostUpdateEverything):
   if _sessionLog != null:
      try _sessionLog.Tick(collector)
          └─ if (newest.TickIndex - _lastTimelineTick >= 3600):
                 _timeline.Add(TimelineRow(...))
                 try WriteReport(final: false, collector) → AtomicWrite
                 catch -> throw SessionLogFailureException
      catch SessionLogFailureException:
          _sessionLog = null; Logger.Warn

OnWorldUnload:
   collector?.FlushSpikes()
   if _sessionLog != null:
      try _sessionLog.End(collector)
          └─ WriteReport(final: true, collector) to _finalPath via AtomicWrite
      catch IO/UnauthorizedAccess/Security: Logger.Warn
   _sessionLog?.Dispose(); _sessionLog = null
```

### Insights block construction

`InsightsBlock()` reads from `InsightsEngine.Shared` if it exists, otherwise emits an empty block. The records serialise with: `pattern` (string), `confidence` (enum name), `scope` (`EvidenceScope` enum name), `audience` (enum name), `confirmationCount`, `firstSeen`, `lastSeen`, `subject` (mod + hook), `magnitude` (kind + value), `evidence` (baseline kind + p-values).

`live[]` is the current live set, `history[]` is every record ever surfaced this session (including TTL-evicted ones), `gated` is the map from gate name (`events`, `litedb`) to the pattern names waiting on that gate.

## Implemented Outputs / Artifacts

| Path | What |
|------|------|
| `<savedir>/PerformanceProfiler/sessions/current-session.json` | Updated atomically every minute while the world is open |
| `<savedir>/PerformanceProfiler/sessions/<identity>-<UTC stamp>.json` | Final report written at `OnWorldUnload` |
| `<savedir>/PerformanceProfiler/sessions/*.tmp` | Should never exist after a successful write — but tolerable if a crash interrupts mid-write |
| `client.log` line "Session log disabled for this world ..." | Self-disable notification on IO failure |

## Known Issues / Active Risks

- **`SessionLogWriter.cs` is 800+ lines and mixes pure report construction with IO.** Deferred audit item (`plans/code-health-audit/persistence-session-logging.md`). The split into `SessionReportBuilder` (pure, testable) + `SessionLogWriter` (IO orchestrator) was held until the test harness existed; now that it does, the split is queued. Downstream impact: schema regressions cannot be caught by a snapshot test today; the only safety net is manual inspection of the JSON.
- **The `current-session.json` file is overwritten in place.** It is not a useful long-term artefact — only the latest per-tick state. Players or agents reading it mid-session see a partial view; the contract is "read the final file with the identity-stamped name." Watch for downstream consumers that read `current-session.json` and assume completeness.
- **No schema migration path.** Bumping `SchemaVersion` invalidates every prior report (they prune automatically). For a publicly-distributed mod this is acceptable today (the JSON is consumed by agents, not by other software), but if a downstream tool ever pins a schema version, the abrupt invalidation becomes a regression source.
- **`File.Replace` is not atomic on every filesystem.** The .NET docs note that it is atomic on NTFS and most POSIX filesystems but not guaranteed everywhere. On a player using a network drive or an exotic FS, a torn write is still possible. The first-write `File.Move` fallback is also not strictly atomic across filesystems. Acceptable risk today; the worst case is one missing or partial session report, not a crash (Invariant 4).

## Partial / In Progress

- **Deferred from the 2026-05-20 audit:** split `SessionLogWriter` into `SessionReportBuilder` (pure) + IO wrapper, and add schema snapshot coverage. Pure refactor with zero behaviour change. Tracked in `plans/code-health-audit/index.md` and in the file's audit doc.

## Planned / Missing / Likely Changes

- **LiteDB lifetime persistence (separate subsystem).** See `notes/litedb-migration-plan.md`. The session JSON stays as the compact per-session export; LiteDB will be the cross-session aggregator that powers `EvidenceScope.LifetimeData`.
- **Possible HTML report sibling.** Sketched in `notes/future-html-report.md`. Would consume the JSON, not replace it.

## Durable Notes / Discarded Approaches

- **The atomic write design replaced a single `File.WriteAllText` call.** Before commit `77a99d2`, a crash mid-write would leave a truncated JSON. The audit (`plans/code-health-audit/persistence-session-logging.md`, finding "Write session reports through a temp file and same-directory replace") flagged this as high-severity because the file the player or an agent reads after a crash is the most likely failure surface.
- **The prune pattern was widened then narrowed.** The first prune cut was a glob that swept any `*.json` in the session directory whose identity prefix did not match. Narrowed to the writer-owned filename shape so hand-saved JSON survives. Audit finding "Session log pruning may delete manual JSON artefacts" (potential-issue #5).
- **`InsightsEngine.Shared` is a lazy singleton owned via `GetOrCreateShared()`.** Before commit `aa914ce`, the InsightsTab owned its own engine instance so the session JSON's insights block was always empty. The singleton means the player surface and the agent surface read from the same store. `ProfilerSystem.OnWorldUnload` explicitly clears `InsightsEngine.Shared = null` so the next session does not inherit the previous session's records.
- **`PruneIncompatibleLogs` runs at `Create`, not at `End`.** Pruning at create-time means: a session that fails to write a final report (crash) still leaves its `current-session.json` orphaned, but the next session prunes it. No background sweep needed.

## Obsolete / No Longer Relevant

- **Schemas 1, 2, 3** are all obsolete. Schema 3 was the immediate predecessor of schema 4 and was used during the period between commits `e6a1020` (insights engine landed) and `aa914ce` (insights block added to JSON). The identity-hash prune means there are no on-disk artefacts to migrate.
- **Two-arg `PerModAttribution.Add` removal (commit `77a99d2`)** is documented under `systems/hook-instrumentation.md`'s obsolete section, not here. No interaction with the JSON shape.

## Cross-references

- `systems/insights-engine.md` — what `insights.live` / `history` / `gated` carry.
- `systems/mod-lifecycle.md` — where the `try/catch` wrapping of Create/Tick/End lives.
- `systems/hook-instrumentation.md` — `HookCoverageVersion` feeds the identity hash.
- `tmodloader/lifecycle-and-loop.md` — `OnWorldLoad` / `OnWorldUnload` / save-path resolution.
- `plans/code-health-audit/persistence-session-logging.md` — the audit findings driving the current state, including the deferred split.
- `notes/litedb-migration-plan.md` — the separate lifetime-data subsystem that will eventually populate `EvidenceScope.LifetimeData`.
