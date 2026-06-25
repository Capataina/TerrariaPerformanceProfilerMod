# Web Server & Persistence — Code Health Findings

**Cluster:** the HTTP worker path (`Web/Server/`, `Web/DashboardRouter*.cs`, `Web/Assets/DashboardAssets.cs`) + the LiteDB writer thread (`Data/Streams/`, `Profiling/Persistence/`).
**Finding count: 9** (1 Known-Issue/correctness, 3 Performance, 2 Dead-Code, 2 Inconsistent-Patterns, 1 Test-Coverage gap).

All findings are **free**: identical behaviour, no new maintenance burden, full evidence chain. Per the cluster's hard rules, none proposes replacing the raw-`TcpListener` server, none adds a write path, none touches the zero-alloc enqueue contract.

**Scope verdicts up front:**

| Required verdict | Answer |
|---|---|
| `SessionRecorder.cs` (737 LOC) modularisation | **Leave as is** — see [M1](#m1). |
| Inline-math / data-pipeline-policy violations in routers | **None found.** Every `Build*` reads `DataRegistry…CurrentSnapshot()` and reshapes; the only arithmetic is documented wire-shape adaptation (per-mod category summing, byte→MB, the `BuildNow` 30 s rolling mean already self-flagged at `Summary.cs:123-127`). The "routers format, Data/ computes" rule holds. |
| Abort-clean paths (port-bind / DB-open / IO failure self-disable) | **Intact.** `BindFirstFreePort` throws→`Load` nulls `Dashboard`; `RecoverIfNeeded` promotes/quarantines; every stream `Apply` is try/caught in `ApplyBatch`. No regression. |
| Read-only / GET-only invariant | **Intact.** `Route` returns 405 for non-GET; no router arm mutates game/world/DB-via-HTTP state. The only DB *writes* are on the writer thread, never the HTTP thread. |
| Zero-alloc writer enqueue | **Intact.** `Enqueue` is `Interlocked.Increment` + `Channel.Writer.TryWrite(in op)`; `DbWriteOp` is a `readonly struct`; rows are pooled (`RowPool<T>` round-trip in `InteractionStreams`). |

---

## Known Issues

### F1 — `405 Method Not Allowed` is written to the wire as `HTTP/1.1 405 OK`
- [x] Add a `405 => "Method Not Allowed"` arm to `DashboardHttpServer.StatusText`. — IMPLEMENTED: `Web/Server/DashboardHttpServer.cs` `StatusText` switch, new arm between `404` and `500`.

**Category:** Known Issues / Correctness   **Severity:** Low   **Effort:** Trivial   **Behavioural Impact:** None for the shipped SPA (it only issues GETs over loopback); a protocol-conformance fix for any non-GET probe.

**Location:** `Web/Server/DashboardHttpServer.cs:307-313` — `StatusText(int)`; the 405 is emitted at `Web/DashboardRouter.cs:55`.

**Current State:** `DashboardRouter.Route` correctly returns `HttpResponse.PlainText(405, "Method Not Allowed")` for any non-GET. But `WriteResponse` (`DashboardHttpServer.cs:290`) builds the status line as `"HTTP/1.1 " + Status + " " + StatusText(Status)`, and `StatusText` has arms for only `200/404/500` with a `_ => "OK"` fallthrough. A 405 therefore goes out as `HTTP/1.1 405 OK` — the numeric code and the reason phrase disagree. The web-dashboard doc claims "any non-GET returns 405" as a hard property (`systems/web-dashboard.md:38`); the code returns the *code* correctly but mislabels the *reason phrase*.

**Proposed Change:** Insert `405 => "Method Not Allowed",` between the `404` and `500` arms (matching the existing arm texture: one `code => "Phrase",` line).

**Justification:** The reason phrase is advisory per HTTP/1.1 (RFC 9110 §15.1 — clients act on the code, not the phrase), so behaviour is unchanged for conformant clients; but a mismatched phrase is a latent correctness bug and trivially wrong. Pure additive arm, no logic change.

**Expected Benefit:** Wire output matches the status code on the one error path the server is documented to emit but `StatusText` forgot.

**Impact Assessment:** Single switch arm. Zero blast radius — `StatusText` has exactly one caller (`WriteResponse`).

---

## Performance Improvement

### F2 — `EventJournal.AppendBatch` double-buffers every batch (StringBuilder → string → UTF-8 byte[]) on the writer thread
- [x] Stream each `JournalLine`'s UTF-8 bytes to the `FileStream` directly (or reuse a pooled byte buffer) instead of materialising the whole batch as one `string` then one `byte[]`. — IMPLEMENTED: `Profiling/Persistence/EventJournal.cs` `AppendBatch` now does option (b) — `JsonSerializer.SerializeToUtf8Bytes(line)` per op → `_stream.Write(bytes)` + `_stream.WriteByte('\n')`, dropping the StringBuilder, the whole-batch string, and the whole-batch UTF-8 encode. `UnflushedBytes` accumulated from the streamed byte count. The now-dead `using System.Text;` + the dead `Data.*`/`Profiling.*` block were pruned from the file as the free sub-part. Byte-identical output pinned by `Tests/AuditPin_Web_Journal.cs`.

**Category:** Performance Improvement (allocation reduction) — writer thread, **not** the game thread.   **Severity:** Low   **Effort:** Small   **Behavioural Impact:** None — identical bytes land in the journal.

**Location:** `Profiling/Persistence/EventJournal.cs:71-95` — `AppendBatch(IReadOnlyList<DbWriteOp>)`.

**Current State:** Per batch (up to 64 ops, fired by `DbWriterThread.Run` once per drain cycle): one `StringBuilder` pre-sized `batch.Count * 256`, then `buf.ToString()` (a full string copy of the concatenated JSON), then `Encoding.UTF8.GetBytes(string)` (a second full copy as bytes). Plus one `JsonSerializer.Serialize(line, …)` allocation per op. For a Calamity-scale session the warm tier alone is ~1 op/sec, so this runs on the order of once per second — the absolute pressure is modest, which is why this is Low severity. But the StringBuilder→string→byte[] is two redundant full-buffer copies of the same payload.

**Proposed Change:** Either (a) write each serialised line's bytes to `_stream` as it is produced (`_stream.Write(Encoding.UTF8.GetBytes(json))` per line, plus a one-byte `'\n'`), dropping the intermediate StringBuilder and the whole-batch string; or (b) serialise via `JsonSerializer.SerializeToUtf8Bytes` per line straight to bytes, skipping the UTF-16 string entirely. Option (b) is the bigger win (no UTF-16 intermediary at all) and matches the System.Text.Json guidance that the byte-oriented APIs avoid a transcoding round-trip.

**Justification:** This is the writer thread, not the game thread, so it is outside the per-tick Invariant-2 budget — but the writer is single-threaded and shared by every persisted stream, so trimming its per-batch allocation is free headroom against the documented sustained-throughput number (310 ops/sec, `systems/persistence.md`). System.Text.Json's `SerializeToUtf8Bytes` is documented as faster + lower-allocation than `Serialize`-to-string-then-encode precisely because it skips the UTF-16 intermediate.

**Expected Benefit:** ~2 fewer full-buffer copies per batch; removes the `batch.Count * 256` StringBuilder churn. Larger relative win the bigger the batch (closer to the 64-op cap during a burst, where this path matters most).

**Impact Assessment:** Contained to one method; the output bytes are byte-identical (same JSON, same `\n` framing), so `Replay()` and the round-trip test are unaffected. Verify with the existing `PersistenceRoundTripTests` (a replayed journal must reconstruct the same rows).

---

### F3 — `DashboardRouter.Route` re-runs a 29-arm string `switch` per request; `BuildNow` pulls 7 snapshots even when only KPIs render
- [ ] No code change proposed for the switch (see Justification — it is already near-optimal); the actionable half is the per-snapshot-lookup repetition flagged below as a *watch*, not a fix.

**Category:** Performance Improvement (assessed, mostly declined)   **Severity:** Informational   **Effort:** n/a   **Behavioural Impact:** n/a.

**Location:** `Web/DashboardRouter.cs:53-98` — `Route`; `Web/DashboardRouter.Summary.cs:36-121` — `BuildNow`.

**Current State / Assessment:** The `switch (req.Path)` over 29 string literals is compiled by Roslyn to a hashed jump table (string switches over >6 constants lower to a `ComputeStringHash` + bucket dispatch), so it is already O(1)-ish, not a linear scan. **No fix warranted.** `BuildNow` pulls 7 separate `DataRegistry.Shared.Lookup<T>(name).CurrentSnapshot()` calls; each `Lookup` is a dictionary hit and each `CurrentSnapshot` returns an already-built immutable value (the snapshot is produced on the game thread, not synthesised per request — confirmed by the v0.10 migration note at `Summary.cs:29-35`). So the cost is 7 dict lookups + 7 ref reads, not 7 recomputations. This is the hot endpoint (500 ms `pollNow`), but the work is bounded and allocation-light. **Recorded as informational** so a future reader does not "optimise" the switch or snapshot pulls without realising they are already cheap; left as a no-op finding rather than dropped, because the per-request `JsonSerializer.Serialize` reflection path (F4) is the real per-request cost on this endpoint.

**Justification:** Listing this prevents a future symptom-patch ("the hot endpoint pulls 7 snapshots!") that would trade clarity for no measurable gain. The genuine per-request allocation lever is F4.

---

### F4 — Every `/api/*` response re-runs reflection-based `System.Text.Json` serialisation (no source-gen `JsonSerializerContext`)
- [ ] Consider a `[JsonSerializable]` source-generated context for the wire shapes **only if** a future profile shows the serialiser on the hot path; otherwise leave as-is (see Impact — anonymous types block the cheap version of this). — DEFERRED (not-free): source-gen needs named DTO types; the 29 endpoints serialise anonymous objects, so capturing the win means converting all 29 wire shapes to named records — a high-churn change that adds maintenance surface (29 types to keep in sync with the JS readers) and fails the "provably free, no new burden" bar today. Left unchanged pending a measurement that the serialiser is on the hot path.

**Category:** Performance Improvement (conditional / flagged, not mandated)   **Severity:** Low   **Effort:** Medium-Large (and partly blocked — see below)   **Behavioural Impact:** None — identical JSON.

**Location:** every `Build*` across `Web/DashboardRouter.*.cs`; serialiser configured at `Web/DashboardRouter.cs:37-42` (`WriteIndented = false`, reflection mode).

**Current State:** All 29 endpoints serialise **anonymous objects** (`new { worldLoaded = true, … }`) via the reflection-based `JsonSerializer.Serialize`. The metadata is cached after first use (System.Text.Json caches per-type metadata), so the per-request cost is the *write* path, not repeated reflection discovery. At 500 ms `pollNow` + 1500 ms `pollDetail` from a single loopback client this is a handful of serialisations per second — well within budget. The reason this is only *flagged*: the documented 60–85 % allocation win from source generation requires named DTO types decorated with `[JsonSerializable]`; **anonymous types cannot be source-gen-serialised** (the generator needs a named type to emit a context for), so capturing the win would mean converting all 29 wire shapes to named records — a large, behaviour-neutral but high-churn change that is *not* clearly free of maintenance burden (29 new types to keep in sync with the JS readers).

**Proposed Change:** Do **not** convert pre-emptively. Record the option so it is on the table if the dashboard ever serves a high-frequency endpoint or a many-client scenario. If pursued, the additive form is a single `JsonSerializerContext` partial covering the converted DTOs, wired via `JsonOpts.TypeInfoResolver`.

**Justification:** This is the one place the cluster's per-request-allocation watch genuinely bites, but the fix is not free under current shapes (it adds maintenance surface), so it fails the "provably free, no new burden" bar **today**. Flagging > silently converting.

**Expected Benefit:** 2–10× serialise throughput and 60–85 % fewer allocations *if* converted — but only material at request rates far above the current single-client poll.

**Impact Assessment:** Declined as a free win. Re-evaluate only behind a measurement (see [T2](#t2)).

---

## Dead Code

### F5 — 10 unused `using` directives repeated verbatim in every cluster file (CS8019, distinct from cross-cutting F6's CS0105 duplicates)
- [x] Remove the unused `using PerformanceProfiler.Data.* / .Profiling.*` block from files where no symbol references those namespaces — at minimum the two pure-DTO server files where **all** of them are dead. — IMPLEMENTED: pruned to each file's actually-referenced set across the whole cluster. `Web/Server/HttpRequest.cs` (whole 10-line block deleted → zero project usings), `Web/Server/HttpResponse.cs` (block deleted → `System`, `System.Text` only), `Web/Server/DashboardHttpServer.cs` (block deleted; all 10 were dead), `Profiling/Persistence/EventJournal.cs` (block + dead `System.Text` deleted). Router files folded into the F8 sweep below. Verified by symbol-grep per file (no bare type from a removed namespace) + compile gate (0 `error CS`).

**Category:** Dead Code   **Severity:** Low   **Effort:** Small (mechanical)   **Behavioural Impact:** None — `using` directives are inert; removal changes no emitted IL.

**Location:** the worst cases: `Web/Server/HttpRequest.cs:3-12` and `Web/Server/HttpResponse.cs:6-15` (a 10-line block of `Data.Detectors / Data.Aggregators / …Segments / Data.Stats / Data.Streams / Data.Collectors / Profiling / Profiling.Events / Profiling.Persistence / Profiling.Persistence.Records` — **none referenced**; `HttpRequest` is three string properties, `HttpResponse` is bytes + four factories using only `System` + `System.Text`). The same block recurs across the router partials and persistence files.

**Current State:** Verified by inspection: `HttpRequest.cs` references no `Data.*`/`Profiling.*` type at all (10/10 usings dead); `HttpResponse.cs` likewise (its only live usings are `System`, `System.Text`). The block was clearly added wholesale by the v0.12 file-split / contract-decoupling wave and never pruned per file. Repo-wide the *block* appears in 107 files (`grep -l "using PerformanceProfiler.Data.Detectors;"`), but most of those are CSS/JS asset partials and out-of-cluster code; **within this cluster** the affected files are the 11 `Web/` source files + the persistence files, with the two server DTOs being the unambiguous all-dead cases.

**Distinction from `cross-cutting.md` F6:** F6 targets the *duplicate* `using` (the same namespace listed twice → CS0105). **This finding (F5) is different**: these `using`s are not duplicated, they are *unused* (CS8019 / IDE0005). A file can have F5 (unused) without F6 (duplicate) and vice-versa. They want different remediations and should not be collapsed.

**Proposed Change:** Per file, delete the `using` lines whose namespace no symbol in that file uses. Start with `HttpRequest.cs` and `HttpResponse.cs` (delete the whole 10-line block in each). For the router partials, prune to the namespaces each actually touches (most need `System`, `System.Collections.Generic`, `System.Text.Json`, `Terraria.ModLoader`, `PerformanceProfiler.Profiling`, `…Web.Server`, and the specific `Data.*` the builders read).

**Justification:** Inert directives, but they (a) misrepresent each file's real dependency surface to a cold reader and (b) are the template the next file-split will copy, compounding the noise. Removing them is the textbook free win.

**Expected Benefit:** Honest dependency lists; smaller blast radius signal when a namespace genuinely moves.

**Impact Assessment:** Zero runtime effect. Safe to verify with the compile gate (`dotnet msbuild` grep `error CS` → still 0). Recommend doing F5 and F6 in one sweep file-by-file since both touch the `using` block.

---

### F6 — `HttpResponse`'s raw-bytes path + `RawTarget` field are retained-for-future, not dead — recorded so they are *not* removed
- [ ] No action. Documented so a dead-code sweep does not delete a deliberately-kept seam.

**Category:** Dead Code (false-positive guard)   **Severity:** Informational   **Effort:** n/a   **Behavioural Impact:** n/a.

**Location:** `Web/Server/HttpResponse.cs:35-40` (public `(int,string,byte[])` ctor); `Web/Server/HttpRequest.cs:33-34` (`RawTarget`).

**Current State:** The public raw-bytes `HttpResponse` ctor is used today (the cached CSS/JS/favicon arms in `DashboardRouter.cs:59-61` call it directly), so it is **not** dead — the doc-comment's "kept for future PNG/WOFF" framing undersells it. `HttpRequest.RawTarget` (the query-string-bearing target) is currently stored but never read by any router arm (`TryReadRequest` strips the query before routing; `systems/web-dashboard.md:281` confirms no endpoint reads parameters). `RawTarget` is the one genuinely-unread field — but it is the documented seam for the planned query-string filtering, and the cost of keeping one string property is nil.

**Justification:** A naive "unused public member" sweep would flag `RawTarget` and the raw ctor. Recording the truth (ctor is live; `RawTarget` is an intentional retained seam) prevents a false-positive removal that would break the planned query-string work. **Leave both.**

---

## Inconsistent Patterns

### F7 — Three independent `JsonSerializerOptions` instances with the same intent (`WriteIndented=false`) across the cluster
- [ ] No merge proposed (they live in different namespaces / audiences); recorded so the *next* serialiser config is added to the right one rather than a fourth.

**Category:** Inconsistent Patterns   **Severity:** Informational   **Effort:** n/a   **Behavioural Impact:** n/a.

**Location:** `Web/DashboardRouter.cs:37` (`JsonOpts`, wire output), `Profiling/Persistence/EventJournal.cs:42` (`JsonOpts`, journal lines), `Data/Streams/StreamJson.cs:26` (`Options`, replay deserialisation).

**Current State:** Three `JsonSerializerOptions` exist. The journal-write (`EventJournal.JsonOpts`) and the replay-read (`StreamJson.Options`) are **deliberately a matched pair** — both set `WriteIndented=false, IncludeFields=false` so a serialised line deserialises identically (`StreamJson` doc-comment says exactly this). The router's `JsonOpts` is a *separate* concern (browser wire shape, no field inclusion needed). So this is not true duplication: the journal/replay pair is a contract, and merging the router's into it would couple the wire format to the on-disk format — undesirable. **No change.** Flagged only so a future "consolidate the JSON options" instinct does not wrongly fuse the two audiences.

**Justification:** Surfacing the intent boundary prevents a plausible-looking but wrong consolidation.

---

### F8 — Router-partial `using` headers are inconsistent: Lag/Timeline/Insights add `Data.Contracts`, others do not, but all carry the same dead block
- [x] Fold into the F5 sweep — normalise each partial's `using` set to what it actually references. — IMPLEMENTED: each `Web/DashboardRouter*.cs` pruned to its real reference set, determined by resolving every bare (non-`Data.`/`Profiling.`-qualified) type token to its declaring namespace. Per-file minimal sets: `DashboardRouter.cs` {System, …Generic, …Text.Json, Data.Detectors, Web.Server}; `Summary` {System, …Generic, …Text.Json, Profiling, Profiling.Persistence, Data.Stats, Data.Detectors}; `Mods` {…, Profiling, Profiling.Persistence, Data.Aggregators}; `Hooks` {…, Profiling, Data.Aggregators}; `Self` {…Text.Json} only; `Memory` {System, …Generic, …Text.Json, Profiling}; `Lag` {…, Profiling, Data.Aggregators, Data.Contracts}; `Timeline` {…, Profiling, Data.Aggregators.Segments, Data.Contracts}; `Insights` {…, Profiling, Data.Contracts, Insights}. `Data.Contracts` kept only on the three rollout partials (Lag/Timeline/Insights), as the finding requires. Note: the finding called `Memory.cs` the clean template carrying "only the 6 it needs" — in fact its `using Terraria.ModLoader;` was itself dead (no `Terraria.ModLoader` type is used bare in any partial), so the strict-minimal reading ("what it actually references") dropped `Terraria.ModLoader` cluster-wide, including from `Memory.cs`. Compile gate: 0 `error CS`.

**Category:** Inconsistent Patterns   **Severity:** Low   **Effort:** Small   **Behavioural Impact:** None.

**Location:** `Web/DashboardRouter.Lag.cs:14`, `…Timeline.cs:14`, `…Insights.cs:14` carry `using PerformanceProfiler.Data.Contracts;` (needed — they resolve `RolloutStreamNames` / the rollout snapshot types); `…Summary.cs`, `…Mods.cs`, `…Hooks.cs`, `…Self.cs`, `…Memory.cs` omit it (correctly — they read non-rollout snapshots). The inconsistency is *correct* on the `Data.Contracts` line but *masked* by all eight partials sharing the identical dead 10-line block from F5.

**Current State:** The partials diverge only where they genuinely should (the rollout-reading ones need `Data.Contracts`), but the shared dead block makes the headers look noisier and less intentional than they are. `Memory.cs` is the cleanest (it carries only the 6 usings it needs — `System`, `…Generic`, `…Text.Json`, `Terraria.ModLoader`, `…Profiling`) and is the model the others should match.

**Proposed Change:** When doing F5, make every partial's `using` set minimal-and-correct, with `Memory.cs` as the template. Keep `Data.Contracts` only on the three rollout partials.

**Justification:** Consistency that reflects real dependencies, using the already-clean `Memory.cs` as the in-repo precedent (Editing-Discipline: match the neighbour that is correct, not the noisy majority).

**Expected Benefit:** A reader can tell which partials touch the frozen contract layer by their imports.

**Impact Assessment:** Mechanical, compile-gate-verifiable. Bundle with F5/F6.

---

## Test Coverage Gaps

### F9 — No frozen-schema / serialisation-equivalence test guards the journal-line ↔ row round-trip or the wire-shape stability
- [ ] FLAG (do not write here): two diagnostic tests would close real gaps. Both are pure-logic, runnable without a game. — PARTIAL: this implementation pass added `Tests/AuditPin_Web_Journal.cs` to anchor the F2 change (asserts the new `AppendBatch` is byte-identical to the old double-buffer form for a representative 4-op batch via an independent old-form oracle). That pins the *journal line shape* — a field rename / framing drift in `AppendBatch` now fails at build/test rather than in-game. T1 (per-`IPersistenceStream` `Reconstruct` round-trip over the NDJSON line) and T2 (29-endpoint wire-shape golden) as fully specified remain DEFERRED to the test-writing pass; the dual-mapper (System.Text.Json journal vs BSON-short-name LiteDB over the same records) hazard T1 targets is still untested.

**Category:** Test Coverage Gap   **Severity:** Medium   **Effort:** Small (both tests)   **Behavioural Impact:** None (tests only).

**Location:** gap sits between `Profiling/Persistence/EventJournal.cs` (writes lines), `Data/Streams/*Stream.cs` (`Reconstruct` reads them), and `Web/DashboardRouter.*.cs` (wire shapes). Existing coverage: `Tests/Persistence/PersistenceRoundTripTests.cs` (write→reopen→read fidelity), `PersistenceBenchmarkTests.cs` (throughput/size).

**Current State:** `systems/persistence.md:179` already calls out the deferred frozen-schema test. The round-trip test exercises the *channel→LiteDB* path but **not** the *journal-serialise → journal-deserialise → Apply* path in isolation — i.e. it does not prove that what `EventJournal.SerializePayload` writes is exactly what each stream's `Reconstruct` (`StreamJson.Deserialize<T>`) can read back. A field rename or a `BsonShortNames` change that broke the System.Text.Json line shape (which uses **C# property names**, *not* the BSON short names — two different mappers on the same records) would not be caught until a real crash-replay failed in-game. This is exactly the "malformed record breaks a dashboard endpoint" downstream risk the persistence doc names.

**Flagged tests (to be written by the test-writing pass, not here):**

| # | Test | What it proves |
|---|---|---|
| T1 {#t1} | **Journal round-trip per stream.** For each `IPersistenceStream`, build a representative `DbWriteOp`, run it through `EventJournal.AppendBatch` → read the line back → `stream.Reconstruct(line)` → assert the reconstructed op's payload field-equals the original. | The NDJSON line shape and every `Reconstruct` agree. Catches a property rename / a System.Text.Json vs BSON-mapper drift. Closes the deferred frozen-schema gap for the *journal* surface. |
| T2 {#t2} | **Wire-shape snapshot (serialisation-equivalence).** Feed each `Build*` a synthetic snapshot (via a test `DataRegistry`) and assert the serialised JSON matches a checked-in golden string. | Locks the 29 endpoint shapes against accidental field renames/removals that would silently break the JS readers. Also gives F4 a baseline to prove "identical JSON" if source-gen is ever pursued. |

**Justification:** The round-trip test covers the happy path but leaves the dual-mapper hazard (System.Text.Json on the journal, BSON short-names in LiteDB, over the *same* record types) untested — a genuine, named, deferred risk. T1 is small (loop over `StreamRegistry.Default().Streams`) and pure-logic. T2 needs a synthetic `DataRegistry` seam; if that does not yet exist as a test affordance, T1 alone still closes the higher-severity half.

**Expected Benefit:** A schema/shape change that would otherwise surface as an in-game 500 or a blank dashboard tab fails at `dotnet test` instead.

**Impact Assessment:** Tests only; no production change. T1 reuses the existing temp-dir fixture pattern in `PersistenceRoundTripTests`.

---

## Modularisation Verdict

### M1 — `SessionRecorder.cs` (737 LOC): **leave as is** {#m1}

**Verdict:** Leave-as-is. Do **not** split.

**Reasoning:**
- **One owner, one lifecycle.** The class is a single cohesive unit: it owns the session id, the downsampler, and all incremental cursors (`_spikeCursor`, `_stallCursor`, the live stall cluster, the damage ring), all bound one-to-one to a world load. Splitting it would scatter shared mutable state (`_sessionId`, `_ticksObserved`, `_liveCluster`) across files that would then need to pass it around — *more* coupling, not less.
- **The methods are already cleanly sectioned.** Public `On*` enqueue methods (one per event kind) → `End` → private `Drain*` / `Build*` accumulators. Each is short and single-purpose; the file is long because there are *many* event kinds, not because any method is complex.
- **It is already testable without a game.** The doc-comment and `OnContextTransition` note confirm the recorder is runtime-agnostic by design (the caller resolves text; the recorder takes rows). The `Build*` aggregators are pure over their `MetricCollector` + array inputs.
- **A split would be speculative abstraction.** The project's own standard (Engineering Standards → "extract an abstraction for three real reasons, not an imagined fourth") argues against carving this up before a second consumer exists. There is exactly one consumer (`ProfilerSystem`).
- **LOC alone is not a smell here.** 737 lines of flat, sectioned, single-responsibility accumulation code reads cold fine. The cluster-brief's 737 figure tracks file size, not complexity.

**The one improvement worth noting (not a split):** the placeholder fields `P95Ms = peakCategoryMs` (`SessionRecorder.cs:580`) and `P95FrameMs/P99FrameMs = max` (`:687-688`) are honestly commented as placeholders pending per-tick p95; that is a feature gap, not a modularisation issue, and out of audit scope (it would change output). Left for feature work.

---

## Data Layout / Memory Access — applicability

| Surface | Decision |
|---|---|
| Per-request JSON serialisation allocations | **Applicable but mostly declined** — F4 (source-gen blocked by anonymous types) + F2 (journal double-buffer, the one free win). |
| The `DbWriteOp` queue | **Not applicable / already optimal.** `DbWriteOp` is a `readonly struct` passed `in`; `Channel<DbWriteOp>` is unbounded-lock-free on the producer; rows are pooled (`RowPool<T>`). The zero-alloc enqueue invariant holds. No change. |
| Snapshot copies on the HTTP thread | **Not applicable.** `CurrentSnapshot()` returns an already-built immutable value produced on the game thread; the HTTP thread does not copy or recompute it (F3). |
| `EventJournal.AppendBatch` buffering | **Applicable** — see F2 (the genuine data-layout win in this cluster). |

---

## Notes on what was checked and found clean (no finding)

- **Inline math in routers:** none. All 8 partials + `DashboardRouter.cs` are reshape-only. Verified method-by-method.
- **Abort-clean regressions:** none. Port-bind throw→null, DB recover/quarantine, per-op try/catch in `ApplyBatch`, per-request try/catch in `HandleClient` — all present.
- **`RowPool` lifecycle hazard:** checked. Journal-append (writer thread, before apply) serialises the row *before* `Apply` returns it to the pool (`DbWriterThread.Run:148` journal, `:153` apply); the game thread cannot re-rent a pooled row until `Return` runs inside `Apply`. No use-after-return.
- **`Socket.Poll` reliability:** the `Poll(SelectRead)`-returns-true-on-close ambiguity (a documented .NET gotcha) **is** handled — `TryReadRequest:245` checks `Available == 0` and returns cleanly. Defensible design, not a bug.
- **`ContextBaselines` / `LegacyJsonImporter`:** both wired and live (`ProfilerSystem.cs:289-402` and `PerformanceProfiler.cs:89`), not dead. `ContextBaselines` is written via `CrossSessionStore.Save` off the game thread in a captured background task — an architectural exception to "all writes go through the writer thread", but it does not block the game thread; out of strict cluster scope (lives in `Profiling/ProfilerSystem.cs`), noted for the owning cluster.

---

### Research log (mode-varied WebSearch)

| Mode | Query | Key source | Finding it supports |
|---|---|---|---|
| 3 (deep) | "System.Text.Json JsonSerializer.Serialize anonymous object per-request allocation overhead reflection caching ASP.NET" | [MS Learn: reflection vs source generation](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/reflection-vs-source-generation) | F4 — source-gen is 2–10× / 60–85 % fewer allocs, but needs **named** types (anonymous types can't be source-gen'd) → fix not free today. Confirms metadata is cached after first use → F3 not a recompute. |
| 2 (compare) | "LiteDB single writer thread Upsert batch InsertBulk throughput checkpoint WAL log file growth best practices" | [LiteDB #1775 checkpoint best practice](https://github.com/mbdavid/LiteDB/issues/1775), [#522 InsertBulk](https://github.com/litedb-org/LiteDB/issues/522) | Confirms the current design (single writer, periodic `Checkpoint()` to bound the `-log`, `InsertBulk` for batches) is the documented best practice — the writer thread + `MaybeCheckpoint` (60 s cadence) + `PerSessionAggregateStream.InsertBulk` are correct. No finding; validates the architecture. |
| 1 (lookup) | "raw TcpListener AcceptTcpClient thread-per-request pitfalls Socket.Poll SelectRead Available zero connection closed reliability" | [MS Learn: Socket.Poll](https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.socket.poll), [dotnet/runtime #32719](https://github.com/dotnet/runtime/issues/32719) | Confirms `Poll(SelectRead)` returns true on a closed connection too → the `Available == 0` guard at `TryReadRequest:245` is the correct disambiguation. No finding; validates the documented polling design. |
