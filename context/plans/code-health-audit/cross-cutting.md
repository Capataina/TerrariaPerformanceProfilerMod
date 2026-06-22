# Cross-Cutting — Code Health Findings

**Systems covered:** project-wide (duplicate `using` directives, documentation rot in conventions/comments/README).
**Finding count:** 3 certain (F6 low, F7 low, F8 low).

> Mechanical and documentation-hygiene findings that span many files rather than one
> system. All are trivial-effort, zero-behaviour-change.

---

## F6 — CS0105 duplicate `using` directives across 18 files {#f6}

- [ ] Remove the second occurrence of the duplicated `using` directive in each of the 18 listed files (one redundant line per file).

**Category:** Inconsistent Patterns
**Severity:** Low
**Effort:** Trivial
**Behavioural Impact:** None — a duplicate `using` is a CS0105 warning; removing it changes nothing the compiler emits.

**Location (file → duplicated namespace → line pair to dedupe):**

`PerformanceProfiler.Profiling.Persistence.Records` duplicated:

| File | Lines |
|------|-------|
| `Data/Streams/StallClusterStream.cs` | 4, 9 |
| `Data/Streams/SegmentStream.cs` | 4, 9 |
| `Data/Streams/PerSessionAggregateStream.cs` | 5, 10 |
| `Data/Streams/PlayerDeathStream.cs` | 4, 9 |
| `Data/Streams/SessionRecorder.cs` | 7, 12 |
| `Data/Streams/SessionStream.cs` | 5, 10 |
| `Data/Streams/InsightStream.cs` | 4, 9 |
| `Data/Streams/SpikeStream.cs` | 4, 9 |
| `Data/Streams/ModlistStream.cs` | 5, 10 |
| `Data/Streams/ContextTransitionStream.cs` | 4, 9 |
| `Data/Streams/InteractionStreams.cs` | 4, 8 |
| `Data/Streams/WorldSnapshotStream.cs` | 4, 9 |
| `Data/Streams/StallStream.cs` | 4, 9 |
| `Data/Streams/TickAggregateStream.cs` | 4, 9 |

`PerformanceProfiler.Profiling.Events` duplicated:

| File | Lines |
|------|-------|
| `Data/Aggregators/Segments/SegmentNameTable.cs` | 3, 6 |
| `Data/Aggregators/Segments/SegmentPromoter.cs` | 3, 6 |
| `Data/Aggregators/Segments/SegmentDetector.cs` | 6, 11 |
| `Data/Aggregators/Segments/OpenSegment.cs` | 4, 7 |

**Current State:**
Each of the 18 files imports the same namespace twice. The pattern is uniform: a block of
project `using`s near the top (added by the v0.12 file-split / contract-decoupling wave)
re-imports a namespace that an earlier line already imported. The build emits a CS0105
"directive appeared previously" warning per file. The duplication is the second line of
each pair (the later occurrence is the redundant one, but either can be removed — they are
identical).

**Proposed Change:**
Delete the second listed `using` line in each file (keep the first). 18 single-line
deletions. No other change.

**Justification:**
Direct evidence — produced by `grep -nE '^using ' <file> | awk … | sort | uniq -d`
across every `.cs` file, with the exact duplicate-line pairs confirmed per file (table
above). The shared shape (all 14 stream files duplicate the same `Records` namespace; all
4 segment files duplicate the same `Events` namespace) is consistent with a copy-paste
header block introduced during the v0.12 split.

**Expected Benefit:**
Clears 18 CS0105 warnings from the build, making genuine new warnings visible against a
clean baseline. Removes 18 redundant lines.

**Impact Assessment:**
None — duplicate `using` directives are semantically inert; the compiler resolves the
namespace once regardless. Removing the redundant line cannot change any emitted IL.

---

## F7 — `conventions.md` #13 contradicts the code (claims no `AggressiveInlining` anywhere; the hot path uses it) {#f7}

- [ ] Correct convention #13 in `context/notes/conventions.md` to reflect that `[MethodImpl(MethodImplOptions.AggressiveInlining)]` IS used on the per-tick probe path, and update the README/code bytes-per-hook reconciliation tracked in F3.

**Category:** Documentation Rot
**Severity:** Low
**Effort:** Trivial
**Behavioural Impact:** None — `conventions.md` is a notes file (not shipped, not compiled).

**Location:**
- `context/notes/conventions.md:60-62` — convention #13: "no `[MethodImpl(MethodImplOptions.AggressiveInlining)]` is used anywhere in the codebase".
- Contradicted by `Profiling/ProbeStack.cs:81,112,142,172` (`Enter`/`Leave`/`EnterCpuAlloc`/`LeaveCpuAlloc` all carry `[MethodImpl(MethodImplOptions.AggressiveInlining)]`) and `Data/Aggregators/PerModAttribution.Add` (per `decisions.md:43`, Phase β added AggressiveInlining to it).

**Current State:**
Convention #13 asserts the codebase uses no aggressive inlining and that any future
inlining "goes through the Invariant-2 measurement gate … with a comment with the
measurement". But the v0.6 Phase β work (`decisions.md:43`) already added
`[MethodImpl(MethodImplOptions.AggressiveInlining)]` to `ProbeStack.Enter/Leave/
EnterCpuAlloc/LeaveCpuAlloc` and `PerModAttribution.Add`. The convention note is stale: it
describes a pre-v0.6 state. A reader trusting #13 would wrongly believe the hot path has
no inlining annotations.

**Proposed Change:**
Rewrite convention #13 to state the current reality: AggressiveInlining IS applied to the
per-tick probe entry/exit methods (`ProbeStack.Enter/Leave/EnterCpuAlloc/LeaveCpuAlloc`,
`PerModAttribution.Add`), added in v0.6 Phase β, and the convention going forward is that
new hot-path inlining is measured against Invariant 2 before being added. Keep the
note's intent (measurement gate) but fix the false "none anywhere" claim.

**Justification:**
Direct evidence — `grep -rn 'AggressiveInlining'` over the source returns the
ProbeStack/PerModAttribution call sites; `decisions.md:43` records when they were added.
The note and the code disagree; the code is reality (per the Source Hierarchy in
CLAUDE.md).

**Expected Benefit:**
A newcomer (human or agent) reading the conventions to learn the hot-path idioms gets the
true picture instead of a false negative that would lead them to mis-apply or wrongly
"clean up" the existing annotations.

**Impact Assessment:**
None — notes-file edit, no compiled artefact affected.

---

## F8 — Phantom `_TempAllocBench` reference in `HookBackend.cs` doc-comment {#f8}

- [ ] Update the `HookBackend.AllocationTracking` doc-comment that cites `_TempAllocBench` — no such symbol exists in the codebase; either restate the measurement without the dead reference or note where the benchmark now lives.

**Category:** Documentation Rot
**Severity:** Low
**Effort:** Trivial
**Behavioural Impact:** None — XML doc-comment text; not executable.

**Location:**
- `Profiling/HookBackend.cs:69` — `/// Default: true. The benchmark in <c>_TempAllocBench</c> measured the alloc API at ~3.2 ns/call vs Stopwatch at ~17.2 ns …`.

**Current State:**
The `AllocationTracking` property's doc-comment cites a benchmark "in `_TempAllocBench`"
as the evidence for keeping allocation tracking on by default. A repository-wide
`grep -rn '_TempAllocBench'` finds only this doc-comment — there is no `_TempAllocBench`
type, method, or field anywhere in the source. The benchmark it references was either a
throwaway local harness deleted after the measurement, or never committed. The cited
numbers (3.2 ns vs 17.2 ns) may be accurate but are now unverifiable from the codebase —
the comment points at a thing that does not exist.

**Proposed Change:**
Either (a) drop the `<c>_TempAllocBench</c>` reference and keep the measured numbers as a
stated finding ("measured at ~3.2 ns/call vs ~17.2 ns for Stopwatch"), or (b) if the
benchmark is worth keeping, re-add it as a real fixture in the (repaired, per F4) test
project and re-point the comment at it. Option (a) is the trivial doc fix; option (b) is a
small test addition.

**Justification:**
Direct evidence — `grep -rn '_TempAllocBench' --include='*.cs'` returns exactly one hit,
the doc-comment itself. The referenced symbol does not exist; the comment is a dangling
reference (Documentation Rot §"Comments referencing files, modules, or functions that no
longer exist").

**Expected Benefit:**
Removes a misleading pointer. A future engineer who wants to re-verify the alloc-tracking
cost decision will not waste time hunting for a `_TempAllocBench` that was never there.

**Impact Assessment:**
None — comment-only change.
