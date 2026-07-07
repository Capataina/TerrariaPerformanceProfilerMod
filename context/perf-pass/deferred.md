# Performance Pass — Deferred Items

> Everything from the v0.6 / v0.6.1 master plan that did NOT ship and still has a real design behind it. Each item carries the rationale + design sketch + expected delta so a future implementation pass doesn't have to re-research. The shipped items have been stripped from the research dossiers (which previously totalled ~17,800 lines) to keep this directory's context budget under control.
>
> Authoritative state ledger of what shipped: `verification.md`.
> Historical baseline numbers: `baseline.md`.

Date: 2026-05-20 · After v0.6.1 wrap.

---

## How to use this file

When picking up perf work next session, scan §1 for the categories that interest you, then read the per-item designs in §2. Each item is sized as a single coherent commit; clustering multiple within one phase is fine but the items are designed to be independently shippable.

The five Project Invariants in `CLAUDE.md` apply to every recommendation. No item below cuts capture surface, lightens features, or violates the universal "no mod-specific code" rule.

---

## 1. Index by impact category

### DB shape (storage size — currently 1064 KB / 10 min Calamity-scale)

- **§2.1 FK swap: `LoadoutFingerprint` string → `LoadoutSnapshotId` ObjectId** — saves ~240 KB/min in combat-scale `DamageDealtRow` output
- **§2.2 Numeric arrays as BSON binary** on `SpikeWindowRow` + `TickAggregateWarm/Cold` — ~3× compression on those rows
- **§2.3 Byte-encoded enums** on stall rows (`Cause`, `Severity`) — ~50 KB / session
- **§2.4 Binary journal frame format** — 90% writer-thread alloc reduction
- **§2.5 `DbWriteOp` discriminated struct union** — closes the remaining value-type-payload boxing

### Writer throughput (currently 314 ops/sec)

- **§2.6 `InsertBulk` for high-frequency event streams** — target > 1,000 ops/sec
- **§2.7 Compound indexes** on `TickAggregatesWarm`, `LoadoutSnapshots`, `BuffEvents` — 60-80% insight-query latency reduction

### Install RAM (currently ~382 MB delta)

- **§2.8 Cecil `ILContext` dispose after install** — ✅ **SHIPPED** (`ILHookInterceptor.TrimRetainedScaffolding`, on by default). Disposes every settled hook's `LastContext`/`CurrentContext` and nulls them. NOT sufficient on its own: on a 62k-hook stack the install delta is still ~1.9 GB / ~31 KB per hook post-trim. The residual is the per-hook `SourceCloneIl` (kept for re-chain safety) plus MonoMod's per-hook detour state, which cannot be trimmed blind without risking downstream mods' hook chains (Invariant 4). The heap-reclaim diagnostic the gate required now ships too (2026-07-07, B4) — the trim logs its actual MB reclaimed, so the residual is measured, not assumed. Further reduction is a runtime-gated follow-up.
- **§2.9 `BeginInstallAsync` worker thread** — 10-18 s `Mod.Load` blocking dropped to 1-2 s by running install on a background thread

### Insights

- **§2.10 T6 reader thread for insights** — gated on LiteDB read-while-write soak test

### Overlay (currently per-frame allocations in remaining tabs)

- **§2.11 Full per-tab format string caches** at 1 Hz — pattern exists in OverviewTab/EventsTab/InsightsTab; needs extension to TreeTab + SelfTab + header chrome
- **§2.12 `Sparkline.Render` `ReadOnlySpan<double>` overload** — small API win; existing IReadOnlyList overload stays

### Per-tick CPU (small remaining wins)

- **§2.13 Combine collector-boundary Stopwatch + GC reads** into one shared call
- **§2.14 Remove the 3-arg `PerModAttribution.Add` overload** — both backends migrated to the 5-arg form
- **§2.15 `Environment.CpuUsage` migration** — blocked on tML reference assemblies exposing .NET 7+ API
- **§2.16 T7 collector smoother thread** — explicitly deferred to v0.7+ per cross-concurrency analysis; not needed if §2.13/§2.14 land

---

## 2. Per-item designs

### 2.1 FK swap: LoadoutFingerprint string → LoadoutSnapshotId ObjectId

**Surface.** `DamageDealtRow.LoadoutFingerprint` is currently a string field carrying the full fingerprint (`"h3507|a3:3084|"` style, ~25–80 bytes). Every damage-dealt event writes one — in heavy combat that's ~3600 swings/min × ~50 bytes = ~180 KB/min of redundant string data when the matching `LoadoutSnapshotRow.Fingerprint` exists in another collection.

**Design.** Replace the string field with an `ObjectId LoadoutSnapshotId` referencing the matching `LoadoutSnapshotRow`. At emit time in `InteractionPlayer.OnHitNPCWithItem/WithProj/OnHitNPC`, we already track `_lastLoadoutFingerprint` — extend to also track `_lastLoadoutSnapshotId` (set when `OnLoadoutSnapshot` is called with a row that has a freshly-assigned `Id`). The damage emit writes the ObjectId instead of the string.

**Migration.** `DamageDealtRow` schema bump 1 → 2. For old rows where only `LoadoutFingerprint` exists: walk `loadoutSnapshots` for the same session, match by fingerprint string, write the snapshot's ObjectId. If no match found, leave `LoadoutSnapshotId = ObjectId.Empty` and retain the legacy field as a fallback. Migration runs row-by-row at first read so the cost amortises over normal use.

**Expected delta.** ~240 KB/min saved in heavy combat; ~80 bytes × ~3600/min = ~290 KB/min raw, partially offset by the 12-byte ObjectId — net ~240 KB/min.

**Cross-system deps.** Insight detectors that group damage events by loadout currently key off `LoadoutFingerprint` (a `Find(Query.EQ("lf", value))`); they'd need to key off the ObjectId instead. The shape is identical — equality on an indexed field — so the rewrite is mechanical.

**Risk.** Schema-break for v0.5/v0.6 DBs. v0.6.1's BSON short-name change is already a forward-only break; this rides the same migration.

---

### 2.2 Numeric arrays as BSON binary on Spike + TickAggregate rows

**Surface.** `SpikeWindowRow.PerModCatMs` is a `List<double>` of ~126 cells per spike (18 mods × 7 categories). Each row carries 126 × 8 bytes = ~1 KB of numeric data, plus the BSON list framing (~12 B/element). `TickAggregateWarm` and `TickAggregateCold` have similar `PerModMs` / `PerModBytes` arrays at the 1 Hz + 1 min cadence.

**Design.** Replace `List<double>` with `byte[]` where the bytes are a `float[]` packed via `MemoryMarshal.Cast<float, byte>`. Halves the bytes (8 → 4) AND eliminates the BSON list-element framing (replaced with a single binary subtype tag). The `float` precision is fine for ms / byte totals — 7 decimal digits of mantissa is more than the source data has.

**Migration.** Per-row schema bump. Reader checks `v` field; v ≤ 1 reads `BsonArray<double>`, v ≥ 2 reads `byte[]` → `MemoryMarshal.Cast<byte, float>().ToArray()`.

**Expected delta.** ~3× compression on the affected arrays. SpikeWindowRow.PerModCatMs: 1 KB → ~330 B. TickAggregateWarm rows × 60/sec × 4.5 min playtest = ~16 KB saved per 4.5-min session on tick-aggregate alone.

**Cross-system deps.** Spike-detail UI in SpikesTab + spike-attribution paths in insights need to read the new byte[] format. One central helper `SpikeWindowRow.GetPerModCatMs()` that returns a `float[]` regardless of schema, masks the migration.

---

### 2.3 Byte-encoded enums on stall rows

**Surface.** `StallEventRow.Cause` and `Severity` are `string` fields (e.g. `"MajorGc"`, `"Disruptive"`) — ~6-20 bytes each per row. `StallClusterRow.DominantCause` likewise. At ~50 stalls/session that's ~50 × (Cause + Severity) ≈ 2 KB of enum strings per session.

**Design.** Persist `Cause` as `byte` (the enum's underlying type) and `Severity` similarly. BsonMapper customisation: serialize as integer, deserialize back to enum. `EnumStringTable` (already shipped in v0.6 α infrastructure) provides the string projection at render time.

**Migration.** v0.5/v0.6 rows have string Cause/Severity; v0.7+ rows have byte. Reader checks `v` and routes. Lookup table for the string → byte mapping lives in `EnumStringTable` as a parallel array.

**Expected delta.** ~50 KB / session at typical activity. Smaller than the BSON layout wins but pure win — no cost beyond the migration code.

**Cross-system deps.** Stall display in SpikesTab + insight detector emit paths use the strings; either route through `EnumStringTable.CauseName(byte)` or use the C# enum directly.

---

### 2.4 Binary journal frame format

**Surface.** `EventJournal.AppendBatch` currently builds a `StringBuilder`, serialises ops as JSON, writes UTF-8 lines (NDJSON). Per persistence dossier the StringBuilder + JsonSerializer + UTF-8 transcoding is the dominant writer-thread alloc.

**Design.** Replace NDJSON frames with a custom binary frame format: `[1 B version][3 B magic][4 B opKind][4 B payloadLen][N B payload]`. Payload is the BSON-serialised row (LiteDB already serialises to BSON when it Upserts; we reuse that). Writer uses `Utf8JsonWriter` against an `ArrayPool<byte>.Shared.Rent` buffer, flushes to the journal stream in batches.

**Migration.** On journal open, peek the first 4 bytes. If they're `'{'` (NDJSON), use the legacy reader. If they're `[v=2][magic]`, use the new reader. Both readers replay through the same `IPersistenceStream.Reconstruct` path so consumers don't notice.

**Expected delta.** Per persistence dossier §5.7: ~90% reduction in writer-thread allocations. Journal file size also drops (binary is denser than NDJSON). Throughput rises because the journal write is no longer the writer-thread bottleneck.

**Cross-system deps.** EventJournal reader (replay path) needs the new format. StreamRegistry's `Reconstruct` callbacks need to receive the binary payload — easiest via a `ReadOnlySpan<byte>` parameter alongside the existing `JournalLine` for back-compat during the migration window.

---

### 2.5 DbWriteOp discriminated struct union

**Surface.** `DbWriteOp.Payload` is `object` to carry any row type. Class payloads (every Row type) don't box, but value-type payloads (e.g. tuples used internally for some lifecycle ops) do. The bigger issue: every consumer casts via `(DamageTakenRow)op.Payload`, which is a type-check + reference downcast on the writer thread per op.

**Design.** Per metric-collection dossier §1.x + cross-allocations §3.7: replace `object Payload` with a discriminated struct union — a struct containing `[FieldOffset(0)]` overlapped reference fields for each row type that the writer consumes. Switch on `Kind` to read the right slot. Eliminates the downcast.

**Expected delta.** ~30-50 ns/op on the writer thread, plus a small additional save on the game-thread side because the struct field layout means fewer write barriers when constructing the op. Marginal but pure win.

**Risk.** `FieldOffset` requires `[StructLayout(LayoutKind.Explicit)]` which has cross-platform behaviour to validate (and unsafe field overlaps must use reference-only or value-only overlapping, never mixed). Apple Silicon + .NET 8 known-good; older platforms not in scope.

---

### 2.6 InsertBulk for high-frequency event streams

**Surface.** Each `IPersistenceStream.Apply` calls `collection.Upsert(row)` per op. The writer thread batches up to 64 ops per ApplyBatch call, but the dispatch loop still hits LiteDB 64 times — once per row.

**Design.** Add an `IPersistenceStream.ApplyBatch(IReadOnlyList<DbWriteOp> ops, ProfilerDatabase db)` method that defaults to the single-Apply loop. High-volume streams (Damage*/BuffEvent/NpcSpawn/ItemCreated/LoadoutSnapshot) override to:
```
var rows = new List<T>(ops.Count);
foreach (op in ops) rows.Add((T)op.Payload);
db.Collection.InsertBulk(rows);
foreach (var row in rows) RowPool<T>.Return(row);
```
LiteDB's `InsertBulk` is materially faster than per-row Upsert because it bypasses per-row index updates until the end of the bulk.

**Expected delta.** 314 → > 1000 ops/sec writer throughput (per persistence dossier §5.8). Headroom for the high-event-rate scenarios (boss fights with hundreds of damage events per second).

**Note.** Insert vs Upsert: rows are emitted with a freshly-assigned `Id` per Rent, so they're always insertable as new rows. The previous Upsert semantics protected against duplicate-key collisions which can't happen with fresh ObjectIds.

---

### 2.7 Compound indexes for insight queries

**Surface.** v0.6 added single-field indexes on each event stream's `SessionId`. The two persistence-backed insight detectors do range queries: `LoadoutCorrelatedCost` walks `TickAggregatesWarm` by `(SessionId, SecondIndex)`, `EventConditionalCost` walks `BuffEvents` by `(SessionId, UnixMs)`. Without compound indexes the query scans every session row and filters in-memory.

**Design.** Three compound indexes via `BsonExpression` string form:
- `TickAggregatesWarm`: `(SessionId, SecondIndex)`
- `LoadoutSnapshots`: `(SessionId, Reason, UnixMs)`
- `BuffEvents`: `(SessionId, UnixMs)`

LiteDB's compound-index planner can range-scan on the second field once the first is matched.

**Expected delta.** Per insights-engine dossier §3: 60-80% query latency reduction (5-30 ms → < 5 ms per pass). Especially noticeable on long sessions where the single-`SessionId` filter still returns thousands of rows.

**Cross-system deps.** The detector query expressions need to use `BsonExpression` literal-string form (`db.LoadoutSnapshots.Find("$.SessionId = @0 AND $.Reason = 'change'", sid)`) for the planner to pick the compound index. The existing `Find(x => ...)` lambda form may not always plan optimally.

**Risk.** Index maintenance cost on writes. With 3 new compound indexes the per-Insert overhead rises ~10-20%. Acceptable given the read win.

---

### 2.8 Cecil ILContext dispose after install

> ✅ **EXECUTED (partial) — 2026-07-07.** The ILContext dispose shipped as
> `ILHookInterceptor.TrimRetainedScaffolding` (disposes every settled hook's
> `LastContext`/`CurrentContext`). It was NOT the whole story the "suspected Cecil
> dominance" note below predicted: post-trim, a 62k-hook stack still sits at ~1.9 GB
> / ~31 KB per hook. The residual is `SourceCloneIl` (kept for re-chain safety) +
> MonoMod per-hook state, not the disposed ILContext. The B4 heap-reclaim
> diagnostic (same date) now measures what the trim frees, closing the "diagnostic
> FIRST" gate; dropping `SourceCloneIl` remains deferred as an Invariant-4 risk
> (breaks downstream re-chaining). The original design note is kept below as record.

**Surface.** `ILHookInterceptor.InstallTimingHook` constructs `new ILHook(target, manipulator, applyByDefault: true)`. The manipulator is invoked once with an `ILContext` that wraps a `Mono.Cecil.Cil.MethodBody`. After the apply, MonoMod retains the `ILContext` for re-application when other mods install IL hooks on the same method.

The hook-instrumentation dossier identified Cecil retention as the suspected dominant pillar of the 233 MB install delta — each `ILContext` carries a `ModuleDefinition` with ~5 importer-cache dictionaries (~20-30 KB retained per hook × 10,000+ hooks = 200-300 MB).

**Diagnostic FIRST.** Before committing the dispose work, run a heap-snapshot diagnostic during install to confirm Cecil is > 50% of the install delta. If yes, the dispose work is worth doing; if no, the savings are smaller and the risk is unjustified.

**Design.** After `applyByDefault: true` fires the initial apply, the ILContext is no longer needed FOR THIS HOOK — UNTIL another mod installs/uninstalls a hook on the same method (which triggers re-application of the chain). MonoMod's internal state holds the manipulator delegate; the ILContext can be released and reconstructed if re-application is needed.

**Risk vector.** If a re-apply path doesn't gracefully reconstruct, hook chains break for mods that install AFTER us. Mitigation: dispose only on hooks whose target type's assembly is "stable" (not the mod's own dynamic assemblies). Conservative approach: dispose only after a 30-second post-install grace period.

**Expected delta.** 50-150 MB saved (per hook-instrumentation dossier ALLOC-1), conditional on Cecil dominance.

**Why deferred.** The risk + diagnostic dependency. Doing this blind would risk breaking hook chains in modlists with downstream IL hookers (CalamityMod, ThoriumMod, etc.). The diagnostic + careful staged rollout is its own focused session.

---

### 2.9 BeginInstallAsync worker thread (T5)

**Surface.** Both `HookInterceptor.Install` and `ILHookInterceptor.Install` run synchronously in `PostSetupContent`, blocking tML's Mod.Load worker for 10-18 s on a typical modlist. The user sees the loading screen freeze on "Initializing Mods → PerformanceProfiler".

**Design.** Per mod-lifecycle §4.5: spawn a worker thread (T5) from PostSetupContent that performs the install in the background. Mark `Installed = false` initially; flip to `true` as each mod's hooks land. The profiler's per-tick collection runs against a no-op `PerModAttribution.Add` while `!Installed`; coverage rises from 0% to 100% during the first 1-10 s of play.

**Cross-thread contract** (per cross-concurrency dossier §6.2):
- T5 owns: `_installedHooks` list, `_measuredHookCounts[]`, the IL emit work
- T1 (game thread) reads: `Installed`, `InstallProgress` (new field) — both `Volatile`
- Abort-clean: if T5 throws, mark `Installed = false`, log, never retry — Invariant 4

**Expected delta.** Mod.Load blocking 10-18 s → 1-2 s (the synchronous setup before the worker spawns). User-perceived loading time drops dramatically.

**Risk.** Race surface between T5 (IL installs) and T1 (per-tick reads via ProbeStack). The current synchronous install completes before any hook fires (because `PostSetupContent` runs before any world is loaded). Async install means hooks can fire mid-install. ProbeStack already handles "hookId out of range" gracefully (silent skip), so a partial install just gives partial coverage — no crash.

---

### 2.10 T6 reader thread for insights

**Surface.** `InsightsEngine.Evaluate` runs at 1 Hz on the main thread. Two of the detectors (`LoadoutCorrelatedCost`, `EventConditionalCost`) execute synchronous LiteDB queries on the main thread.

**Design.** Per insights-engine §5.3 + cross-concurrency §6.3: spawn a dedicated reader thread (T6) that owns the LiteDB connection for reads. The main thread submits "compute insight X for session Y" requests via a Channel; T6 runs the queries + detector pass + posts the resulting insight rows back via another Channel for the InsightStore to consume on the main thread.

**Gate.** LiteDB v5's `ConnectionType.Direct` mode allows concurrent reads while the writer thread writes. Persistence dossier flagged this as needing a soak test before relying on it:
- Writer thread writes at peak rate (10k events/sec synthetic)
- Reader thread queries the same collection 100×/sec
- Run 60 seconds; check for crashes, deadlocks, or query latency > 50 ms p99

**Expected delta.** Insight pass moves off the main thread entirely. Currently ~5-30 ms / pass; off-thread it's invisible to per-frame timing.

**Risk.** LiteDB read-while-write semantics under load. If the soak fails, fall back to Option B: extend the existing DbWriterThread with a `ReadOp` enum that the writer interleaves with writes.

---

### 2.11 Full per-tab format string caches

**Surface.** v0.6.1 verified the 1 Hz format-cache pattern exists in OverviewTab (`_truncatedNames`), EventsTab (`_cachedNowSummary`), and InsightsTab (`_rankedBodies`). The other tabs (TreeTab, SelfTab, header chrome) still build display strings per draw call.

**Design.** Each tab follows the same shape: a `Tick()` method called at 1 Hz refills cached strings; `DrawSelf` reads them. Per-frame string allocation drops to ~0.

**Sites needing the treatment:**
- `TreeTab` — every row's "ModName · ms/t · %" composite string
- `SelfTab` — install-delta, hook-count, RAM strings
- `OverlayPanel` header chrome — "v0.6.1 · Standard · 18 mods" status strings
- `OverlayDraw.FormatBytes` — small LRU cache (16 entries) so common values like "1.5 MB" / "256 KB" return the cached string

**Expected delta.** Per overlay dossier §4.1: ~120 KB/sec of draw-thread alloc → ~0 once every tab is on the cache pattern. Three of the six tabs already done; this is the rest.

---

### 2.12 Sparkline ReadOnlySpan<double> overload

**Surface.** `Sparkline.Render(IReadOnlyList<double>)` — the existing API. Callers either pass an array (boxing on the interface dispatch in the indexer hot loop) or a `List<double>` (same).

**Design.** Add `Render(ReadOnlySpan<double>)`. The existing overload stays for callers who don't have a Span handy. Internally the implementation switches on the overload — span path is the canonical version, the list path forwards by allocating a temporary span or just loops via the interface.

**Expected delta.** Small. The hot indexer access in the renderer's per-bin loop benefits from the array intrinsic that the JIT generates against `ReadOnlySpan<double>` instead of the interface dispatch.

---

### 2.13 Combine collector-boundary Stopwatch + GC reads

**Surface.** `MetricCollector.BeginTick` reads `Stopwatch.GetTimestamp()` + `GC.GetTotalPauseDuration()` + `GC.CollectionCount(2)` + (optionally) `GC.GetAllocatedBytesForCurrentThread()`. Currently each is a separate call.

**Design.** Capture all four into a `GcSnapshot` struct via one helper method, passed down to the stall detector + per-tick attribution. Per metric-collection §4.3: ~30-50 ns saved per tick by removing 3 separate method-call frames.

---

### 2.14 Remove the 3-arg PerModAttribution.Add overload

**Surface.** Two overloads exist — `Add(modId, categoryId, hookId, elapsedTicks)` (3-arg) and `Add(backendId, modId, categoryId, hookId, elapsedTicks)` (4-arg). The 3-arg forwards to the 4-arg with `backendId = 0`. v0.6 confirmed the delegate backend is dormant (0 detours installed); only the ILHook backend (`backendId = 1`) writes. The 3-arg is unreachable in the active hot path.

**Design.** Delete the 3-arg overload. Callers (which are IL-emitted, so this is a recompile-only change) all use the 4-arg.

**Expected delta.** One less method on the JIT's call-site dispatch table; trivial CPU saving but cleans up the API.

**Risk.** If any test fixture or debug path uses the 3-arg form, deletion breaks it. Trivial to fix at compile time.

---

### 2.15 Environment.CpuUsage migration

**Status.** Blocked. v0.6.1 attempted the migration (stall-detection §5.A) but tML's reference assemblies don't expose `Environment.CpuUsage` even with `TargetFramework=net8.0`. The `CS0117` error comes from the assembly-binding tML imposes, not from .NET 8 itself.

**Resolution path.** Wait for a tML release that ships reference assemblies including .NET 7+ surface. Or attempt reflection-based binding at install time (slower, less elegant). Tracked as v0.7+ when tML 1.4.5+ unblocks it.

**Expected delta.** ~125-540 µs/sec saved on the stall-detector's `Process.GetCurrentProcess().Refresh()` + `TotalProcessorTime` read path (per stall-detection dossier §5.A).

---

### 2.16 T7 collector smoother thread (explicitly v0.7+)

**Status.** Explicitly deferred per cross-concurrency §6.4. The per-tick smoothing + harvest work (`MetricCollector.EndTick` body) costs ~50-70 µs/tick. Moving it to a separate thread would close most of the remaining per-tick budget but adds significant race surface that the other v0.6.1 wins already addressed without needing.

**Design (for future reference).** T7 owns a snapshot of the latest TickFrame; main thread `EndTick` writes the frame, signals T7, returns immediately. T7 runs smoothing + rolling average + harvest into the shared arrays the overlay reads.

**Why not v0.6.1.** The compounding wins from incremental histogram + SIMD UpdateRollingAverage + dirty-flag skip + row pool deliver enough per-tick reduction (~10-12%) to make T7's complexity not worth the risk-vs-reward in the current pass.

---

## 3. Verification protocol when picking these up

For each item:

1. **Read this file's section** for design + expected delta.
2. **Implement** as a single commit (or 2-3 if the schema migration is non-trivial).
3. **Run the existing test suite** (`dotnet test Tests/PerformanceProfiler.Tests.csproj --filter "FullyQualifiedName!~Benchmark"`) — must stay green.
4. **Run the benchmark suite** under Release (`-c Release --filter Benchmark`) — capture before/after.
5. **In-game playtest** — same modlist as the 16:09-16:14 v0.5 baseline. Compare `client.log` session-summary line, DB file size, install-delta line.
6. **Commit message** documents the expected delta + the measured delta. End with the `Co-Authored-By` trailer.

---

## 4. Items NOT in this file (deliberately)

These appeared in the original master plan but were either shipped, made obsolete, or deemed out-of-scope:

- **DateTimeOffset.UtcNow → Time.UnixMsNow** — shipped (v0.6 β phase)
- **LangNameCache / ModOwnerCache wiring** — shipped (v0.6 γ partial + v0.6.1 expansion)
- **AggressiveInlining on probe path** — shipped (v0.6 β + v0.6.1 LangNameCache lookups)
- **Row pool Rent/Return cycle** — shipped (v0.6.1 — the headline)
- **Dirty-flag PostUpdateEquips/PostUpdateBuffs** — shipped (v0.6.1)
- **Incremental histogram baseline** — shipped (v0.6.1)
- **Power-of-2 ring + mask indexing** — shipped (v0.6.1)
- **SIMD UpdateRollingAverage** — shipped (v0.6.1)
- **ContextTransitionWatcher word-level XOR diff** — shipped (v0.6.1)
- **BSON short field names** — shipped (v0.6 δ partial + v0.6.1 StallCluster addition)
- **Off-thread session-end aggregation** — shipped (v0.6 ε + v0.6.1 PreSaveAndQuit overlap)
- **HookSurfaceCache type-walk dedup** — shipped (v0.6.1)
- **OverviewTab/EventsTab/InsightsTab format caches** — shipped (v0.6 ε7 deferred-init + 1 Hz cache pattern)
- **SpikesTab 60 Hz → event-only throttle** — shipped (v0.6.1)
- **DonutChart vertex array reuse** — shipped (v0.6.1)
- **Insight detector LINQ removal** — shipped (v0.6 ζ)
- **Chat-command SafeRun hardening** — shipped (v0.6.1)
- **Fall-damage naming + stall-cluster span correctness** — shipped (v0.6.1)
- **Localization migration + zero-warning build** — shipped (v0.6.1)

For the design rationale behind any shipped item, see the git commit log for v0.6 (16 commits) and v0.6.1 (17 commits). Each commit message documents the why + the expected delta.
