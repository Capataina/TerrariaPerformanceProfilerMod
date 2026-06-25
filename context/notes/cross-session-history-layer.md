# Cross-session history layer — executed state (v0.27.0)

Resolved record of the database/history rework executed 2026-06-25 (plan:
`context/plans/database-rework.md`). Commits `d3ee250` → `3704d67`. The cross-session
intelligence is **live and verified off-game**; runtime behaviour is **play-test-pending**.

## What landed (core waves 0–5)

| Wave | What | Key files |
|---|---|---|
| 0a | Consolidated persistence into a top-level `Persistence/` module | `Persistence/` (was `Profiling/Persistence/` + `Data/Streams/` + `Insights/ReferenceFrames/CrossSessionStore.cs`) |
| 0b | Froze the two-level rollup contracts + identity fields | `Persistence/Records/{WelfordStat,ModLifetimeRollupRow,ModModlistRollupRow}.cs`; `ContextBaselineRow` gained `InternalName`/`ModVersion` |
| 1a | Rollup write path — every session folds into a permanent per-mod history, keyed on `InternalName` | `Persistence/History/{SessionRollupInput,RollupFold,RollupApplier}.cs`; `RollupStream`; `DbOpKind.RollupFold`; `SessionRecorder.End` builds + enqueues; engagement captured game-thread in `ProfilerSystem.KickOffSessionEndAsync` |
| 1b | One-time backfill from existing sessions (marker-guarded, at open) | `Persistence/History/RollupBackfill.cs`; `MetadataRow.RollupBackfillUtc` |
| 2 | `HistoryStore` read layer (instance, testable; scope = mod-membership overlap) | `Persistence/History/{HistoryStore,HistoryViews}.cs` |
| 4 | Cross-session + cross-modpack detectors + the insight producer (closed the orphaned `insights` collection) | `Insights/CrossSession/*`; `InsightsEngine.CrossSessionInsights`; producer in `ProfilerSystem.KickOffSessionEndAsync` |
| 3 | Modlist-change detection + reset control (two scopes) + the read endpoints | `Persistence/Lifecycle/{ModlistChange,StoreReset}.cs`; `ProfilerDatabase.DropAllUserData`; `/api/reset`, `/api/data-health`, `/api/history`; reset button (`Js.Reset`/`Css.Reset`) |
| 5 | Cross-session kanban columns + the data-health view | `Js.Insights` family map; `Js.Self.renderDataHealth` + Self-tab panel |

The three motivating questions are answerable: **unused-in-last-3**, **top-spike-contributor-over-5**,
**costly-despite-low-usage-over-4** (the `Insights/CrossSession/` detectors), plus a
cross-modpack cost-divergence detector. All badge `EvidenceScope.LifetimeData`.

## Load-bearing design facts (don't re-derive)

- **Identity is `InternalName` (Mod.Name)**, never the session-local `ModId`. The roster
  `HookInterceptor.ProfiledModNames`/`ProfiledModVersions` (public static `string[]`) is
  the modId↔name map, available everywhere.
- **Replay/dup idempotency** of the fold is the **ring-as-dedup-marker**: a session
  already in a mod's ring is skipped (`RollupFold.AlreadyFolded`). Exact for the
  un-checkpointed journal tail; the backfill is marker-guarded for older sessions.
- **Engagement** = `ModMetrics.UsageWeight`, captured on the game thread before the
  background session-end (the usage snapshot is cleared by `OnWorldUnload.ResetAll`).
  Backfilled historical sessions fold **0 engagement** (usage wasn't persisted pre-1a).
- **"Forget this modlist" preserves the global per-mod rollup** (the spine: per-mod
  cross-modlist history survives a forget); only the stack's sessions + per-stack rows go.
- Cross-session insights live in a **separate `InsightsEngine` collection**, not the TTL'd
  live `InsightStore` (whose confirmation ladder + TTL + cap-eviction would all fight a
  once-emitted, data-derived-confidence lifetime finding). `/api/insights` concatenates both.

## Deferred (flagged, not done — do these deliberately, not rushed)

- **Wave 1c — re-key `ContextBaseline`/`CrossSessionStore` on `InternalName`.** The new
  rollup already is identity-keyed; this is the *secondary* benefit (the per-context cost
  baselines — the "costs more during invasions" detectors — still reset on a modlist edit).
  Needs the modId↔name array threaded into `CrossSessionStore.Load/Save`, the merge re-keyed
  to `(InternalName, Dim, Key)` with fingerprint demoted to a tag, and a backfill for legacy
  rows (empty `InternalName`, keyed `Fingerprint+ModId`). The fields exist (`ContextBaselineRow`).
  **This is the recommended next step.**
- **Per-mod lifetime trend in the Observatory/Insights drawer** — `/api/history?mod=` is
  built and ready; only the drawer consumer (a sparkline of the ring) is unbuilt.
- **Auto-compaction on a cadence** (manual `ProfilerCompactCommand` covers it today),
  **deeper corruption hardening** (the backup-ring + crash-quarantine recovery stands),
  and **version-boundary marking** (the ring carries per-session version; the consumer is
  roadmap F1 regression tracking).

## Verification done

Off-game only: `dotnet msbuild` → 0 `error CS` at every wave; `dotnet test` → 159 passed
(fold, rollup write→read integration, backfill dedup, HistoryStore, the four cross-session
detectors, modlist diff, both reset scopes); preview regenerated + app JS `node --check`
clean. The **runtime** path (in-game accumulation across a build→play→restart→play cycle,
the producer's persisted rows, the reset button against a live store) is **untested** —
that is the pending play-test.
