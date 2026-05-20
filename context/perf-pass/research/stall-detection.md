# Performance Pass Research — Stall Detection

*Scope: `Profiling/StallDetector.cs`, `Profiling/ProfilerFocusProbe.cs`,
the recent-stall window inside the detector, the `StallStream` +
`StallEventRow` write path, and the `SessionRecorder` cluster aggregator
that consumes the detector's output. Cross-cuts `MetricCollector.BeginTick`
because that is the single per-tick call site of the detector.*

*Output target: zero per-tick allocation; no scope cut; every cause
classified today (Unknown / MajorGc / MinorGc / ProcessSuspended /
LongFrame / UiOverlayBlocking / WorldLoad / MainThreadFreeze) stays
classified; cluster detection stays; every row written stays; universal
on every modlist.*

---

## 0. Why a dedicated stall-detection research file

There is no `context/systems/stall-detection.md`. The stall classifier
lives in the metric-collection surface conceptually but operationally is a
separate hot-path component with its own GC counter reads, its own
allocation-sensitive Process handle, its own ring of recent events, its
own focus probe, and its own write fan-out into `stallEvents` +
`stallClusters`. It also has *more* coupling to .NET 8 runtime internals
than any other detector in the codebase — `GC.GetTotalPauseDuration`,
`GC.CollectionCount(0/1/2)`, `GC.GetTotalMemory(false)`,
`Process.TotalProcessorTime` plus `Process.Refresh()`. Each of those is a
distinct cost surface that the perf pass needs evidence on, and the
classifier itself has had two narrative-breaking misdiagnoses in the last
two sessions (v0.4 ProcessSuspended-vs-MainThreadFreeze, v0.5 cluster
detection over-rounding short bursts) that justify a dedicated dive.

`spike-detection.md` and `metric-collection.md` are sibling reads — the
spike detector consumes the same `TickFrame` stream, but inside a tick;
the stall detector measures the *gap between* ticks. Both feel like "lag
spikes" in colloquial usage; both need different attribution and different
optimisations. This file is the analogue to `spike-detection.md`'s system
doc, scoped to the stall surface.

---

## 1. Current state audit — every method walked

The file `Profiling/StallDetector.cs` is 565 lines. Walked top-to-bottom
against the v0.5 source, naming every hot-path read, every allocation
risk, every classifier branch.

### 1.1 Enum + value-type schema (`StallCause`, `StallSeverity`, `StallEvent`, `StallContributor`)

* `StallCause` is a `byte`-backed enum. 8 values today: Unknown / MajorGc /
  MinorGc / ProcessSuspended / LongFrame / UiOverlayBlocking / WorldLoad /
  MainThreadFreeze. Byte-backing is correct: it packs cleanly into the
  `StallEvent` struct alongside the `StallSeverity` byte. Adding causes
  is cheap until we cross 256, which is not a real ceiling.
* `StallSeverity` is also `byte`-backed; 4 values (Minor / Noticeable /
  Disruptive / Freeze).
* `StallEvent` is a struct with 16 fields plus 5 inline `StallContributor`
  values (C0..C4). Field layout walked:
  - Two `long` indices (StartTickIndex, EndTickIndex), 16 B.
  - One `long` timestamp (StartTimestampUnixMs), 8 B.
  - Six `double` deltas (TickPeriodMs, BaselineMs, ExcessOverBaselineMs,
    GcPauseDurationMs, ProcessCpuTimeDeltaMs, HeapSizeBeforeBytes,
    HeapSizeAfterBytes — wait, the last two are `long`, not `double`.
    Re-check: yes, `long` per definition. So four `double`, two `long`.
    32 B + 16 B = 48 B).
  - Three `int` gen counts (Gen0/1/2), 12 B (typically padded to 16 B).
  - Two `byte` enums (Cause, Severity), 2 B (padded).
  - One `bool` Warming, 1 B (padded with the enums into 8 B alignment).
  - Five `StallContributor` structs. Each is one `int` + one `double` =
    12 B raw, padded to 16 B on 8-byte alignment. 5 × 16 = 80 B.
  Total `StallEvent`: roughly 16 + 8 + 48 + 16 + 8 + 80 = 176 B. The ring
  is `RingBuffer<StallEvent>(50)` so 50 × 176 B ≈ 8.6 KB pre-allocated.
  Cheap and pre-pinned.
* `StallContributor` is a struct (int + double). `Empty` is a static
  factory returning a new struct value — no allocation because it's a
  value type, but worth noting: the JIT can elide the property entirely
  when the receiver of `StallContributor.Empty` is used as an RHS in an
  assignment. Good shape.

**Audit finding 1.1.A.** The struct fields are correctly value-typed,
correctly laid out, correctly pre-pinned in a fixed ring. No drift.

### 1.2 Constants

`DefaultThresholdMultiplier = 3.0`, `WarmupTicks = 600`,
`SeverityNoticeableMultiplier = 3.0`, `SeverityDisruptiveMultiplier = 8.0`,
`SeverityFreezeMultiplier = 20.0`, `UiOverlayClusterMaxMultiplier = 30.0`,
`LongSuspendMultiplier = 50.0`. All const doubles. All relative to
baseline. The "everything relative" decision is implemented correctly
here — there are no leftover absolute-ms constants for severity or cause
classification. Cross-checked against `decisions.md` 2026-05-20 ("UI
overhaul + v0.2") entry where the relative-baseline shift was promised;
the stall path delivered.

**Audit finding 1.2.A.** No absolute-ms thresholds in the stall classifier.
This is the durable lesson and is encoded correctly.

### 1.3 `_events` ring + `_view`

`_events = new RingBuffer<StallEvent>(50)` is allocated once at
construction. `_view = new StallEventsView(_events)` exposes it as
`IReadOnlyList<StallEvent>` and is cached — same pattern as
`SpikeDetector._windowsView` after the audit fix in commit `77a99d2`. The
inner `StallEventsView` is a `private sealed class` with an enumerator
method using `yield return`, which DOES allocate when iterated, but the
hot path (`SessionRecorder.DrainStalls`) uses the indexer `stalls[_stallCursor++]`
not `foreach`, so the yield enumerator is never spun up under normal
operation.

**Audit finding 1.3.A.** Confirmed by grep: `Events.GetEnumerator()` and
`foreach(... in stalls)` are absent from the per-tick path. The yield
allocation only happens if a debug surface enumerates, which is fine. No
drift.

### 1.4 `_self` (cached `Process` handle)

```csharp
try { _self = Process.GetCurrentProcess(); }
catch { _self = null; }
```

Constructed once. `_self` is then used in:
- `OnBeginTick` (full overload): `_self?.Refresh(); cpuNow = _self.TotalProcessorTime;`
- `CaptureBaseline`: same `Refresh + TotalProcessorTime` pair.

The cache pattern is intentional and right — without it, every BeginTick
would `Process.GetCurrentProcess()` which DOES allocate (it returns a
fresh `Process` instance per call: one `Process` object, one
`ProcessInfo` record, plus the OS-side handle, ~150 B+).

But the deeper question: is `Process.Refresh()` itself cheap? Per the
.NET 8 macOS runtime source for `TotalProcessorTime` on the current
process (verified via the runtime tree), the read routes through
`Environment.CpuUsage.TotalTime`, which is a single native call into the
host CLR that returns a struct. No allocation in the steady state. On
Linux it reads `/proc/self/stat`, which is a file open + parse and IS more
expensive (microseconds). On Windows it's a GetProcessTimes syscall.

**However** — the cache doesn't avoid the syscall, only the wrapper
allocation. The syscall fires twice per stall (once at `CaptureBaseline`
on every tick that is a stall-terminator, once at the stall path itself).
That is the actual cost.

**Audit finding 1.4.A.** The `Process` cache is correct and load-bearing.
But `Process.Refresh()` is itself a syscall on Linux (file I/O against
`/proc/self/stat`) and on Windows (GetProcessTimes). Replacing the
`_self.Refresh() + _self.TotalProcessorTime` pair with a direct
`Environment.CpuUsage.TotalTime` read on .NET 7+ is strictly cheaper:
zero allocation, no `Process` instance state mutation, and on macOS it
short-circuits to the same underlying counter without going through the
managed wrapper. See §5.A for the proposed change.

**Audit finding 1.4.B.** `try { ... } catch { _self = null; }` masks
construction failures silently. In a test/CI environment without a
process handle, every stall classifies as `cpuDelta = 0` which is the
"CPU starved" path, which is the `ProcessSuspended`/`MainThreadFreeze`
branch — which is wrong by construction. A test environment that loses
the Process handle should NOT report ProcessSuspended on every stall.
Mitigation in §5.D.

### 1.5 Per-tick scratch state

```
_prevBeginStamp / _prevGcPauseMs / _prevGen0/1/2 /
_prevHeapBytes / _prevCpuTime / _hasBaselineSample / _ticksSeen /
_focusHeldAcrossGap
```

All value-typed primitives on the detector instance. Updated every tick
via `CaptureBaseline`. No allocation, no boxing, no per-call objects. Good
shape.

### 1.6 `OnBeginTick` (no-arg overload + 5-arg overload + full 6-arg)

Three overloads:

| Overload | Use site |
|---|---|
| `OnBeginTick(beginStamp, tickIndex, tickStartUnixMs, baseline)` | Back-compat for tests / callers without per-mod attribution. |
| `OnBeginTick(beginStamp, tickIndex, tickStartUnixMs, baseline, perModSmoothedMs)` | Pre-v0.5 callers. |
| `OnBeginTick(beginStamp, tickIndex, tickStartUnixMs, baseline, perModSmoothedMs, hadFocusThisTick)` | The live caller (`MetricCollector.BeginTick`, line 326). |

The simpler overloads delegate to the full one with `null` /
`hadFocusThisTick: true`. No allocation in the dispatch chain — just
parameter forwarding.

**Audit finding 1.6.A.** Three overloads with default parameters
collapsed into the full one would be syntactically cleaner, but the
current explicit chain is clearer for callers and the JIT inlines all
three. No optimisation lever here.

### 1.7 Full `OnBeginTick(beginStamp, tickIndex, tickStartUnixMs, baseline, perModSmoothedMs, hadFocusThisTick)`

Walked line by line:

1. `_ticksSeen++` — primitive increment.
2. `if (!hadFocusThisTick) _focusHeldAcrossGap = false;` — branch
   predicted always-taken (focus is held 99%+ of the time on a normal
   playtest).
3. First-tick path: `!_hasBaselineSample || !baseline.IsCalibrated`. If
   either, call `CaptureBaseline` and return. `CaptureBaseline` itself
   reads `GC.GetTotalPauseDuration` + `GC.CollectionCount(0/1/2)` +
   `GC.GetTotalMemory(false)` + `_self?.Refresh()` + `TotalProcessorTime`.
   Every tick before calibration pays this cost. At 60 ticks per second
   and `MinCalibrationTicks = 60`, that's 60 calls before the warmup
   check turns true, but `_hasBaselineSample` is also a gate — the very
   first tick captures and returns; subsequent calls do the threshold
   check.

4. `double tickPeriodMs = (beginStamp - _prevBeginStamp) * 1000d / stopwatchFreq;`
   — three primitive ops, zero alloc, sub-nanosecond.

5. `double baselineMs = baseline.TickPeriodMsMedian;` — property read,
   single field load.

6. `if (tickPeriodMs < baselineMs * ThresholdMultiplier) { CaptureBaseline; return; }`
   — the 99.9% path. Every non-stall tick (which is the steady-state) pays
   the `CaptureBaseline` cost: 5 GC counter reads + 1 Process syscall (via
   `Refresh + TotalProcessorTime`) + the heap byte read.

   **This is the headline cost surface.** The stall-detector path is
   "free" 99.9% of the time except for the bookkeeping in `CaptureBaseline`,
   and `CaptureBaseline` is the hot path. The expensive subset is the
   Process syscall (microseconds on Linux/Windows, sub-microsecond on
   macOS via Environment.CpuUsage).

7. Stall path. Reads all five deltas, builds the `StallEvent`, calls
   `CaptureTopContributors`, calls `_events.Push(in ev)`. Then
   `CaptureBaseline` (resnapshots state) and resets `_focusHeldAcrossGap`.
   This path fires roughly 50 times per 5-minute session — 0.17 stalls/sec
   on the baseline playtest. The cost is dominated by the GC + Process
   reads, which the steady-state path already pays.

**Audit finding 1.7.A.** The CaptureBaseline call is the per-tick cost
surface, not the stall-detected path. Optimising the stall path saves
nothing measurable; optimising CaptureBaseline pays back 60 times per
second.

### 1.8 `CountRecentStallsInWindow(nowUnixMs, windowMs: 5000)`

```csharp
int count = 0;
for (int i = 0; i < _events.Count; i++)
{
    if (nowUnixMs - _events[i].StartTimestampUnixMs <= windowMs) count++;
}
return count;
```

Linear scan of up to 50 elements. Each iteration is an index into the
ring buffer (a `_head - _count + i` correction plus mod), then a `long`
subtract and compare. Zero allocation, ~50 ns worst case.

Called only on the stall-fired branch, so once per ~0.02 seconds in the
steady state. The cost is negligible.

**Audit finding 1.8.A.** This is correctly shaped. There is no optimisation
to do here; even replacing with a smarter sliding window would save under a
microsecond per stall, on a path that fires once every 2 seconds at peak.

### 1.9 `CaptureTopContributors` (5-way top-N)

Pure function; takes `IReadOnlyList<double>?` as the `perModSmoothedMs`
input. Walked:

1. Zeroes ev.C0..C4 with `StallContributor.Empty`.
2. Returns if null or count zero.
3. Reads `PerModAttribution.CategoryCount` (a static int).
4. Computes `modCount = perModSmoothedMs.Count / cats`.
5. Inner loop: for each mod, sum across categories, then run a 5-way
   branchless insertion (the cascading if/else chain in lines 389-393).

Zero allocation. The `IReadOnlyList<double>` parameter is concerning
because indexer access on the interface is virtual — JIT may not be able
to devirtualise to the underlying `double[]` indexer. On the live call
site (`MetricCollector._perModSmoothedMs`), the actual runtime type is
`double[]`, but the call goes through the interface boundary. .NET 8's
PGO is good at this, but a measurable cost remains.

**Audit finding 1.9.A.** The `IReadOnlyList<double>?` parameter creates
an interface-dispatch boundary on the hot loop (`perModSmoothedMs[offset + c]`
× cats × modCount times — up to 200 calls per stall on a 40-mod stack).
Replacing the parameter type with `double[]?` (concrete) would let the
JIT use the array indexer directly. The caller passes `double[]` today,
so this is a no-op refactor for callers. See §5.B.

### 1.10 Static `ClassifyCause` (4 overloads → one canonical)

Four overloads cascade to the most specific 7-arg form:
`ClassifyCause(wallMs, gcMs, gen2Delta, cpuMs, recentStallsInLast5s, baselineMs, focusHeldAcrossGap)`.

Walked the branch order (lines 449-491):

1. **Defensive zeros.** `wallMs <= 0 → Unknown`. `baselineMs <= 0 →
   fallback 16.67ms`. Both correct.
2. **Compute `cpuStarved` and `isLone`.** `cpuStarved = cpuMs < wallMs * 0.2`.
   `isLone = recentStallsInLast5s <= 2`. Both branchless single-compare.
3. **First priority: long + lone + CPU-starved.** If
   `wallMs >= baselineMs * LongSuspendMultiplier (50.0)`, return
   `MainThreadFreeze` (focus held) or `ProcessSuspended` (focus lost).
4. **Second priority: GC-dominated.** If `gcMs > wallMs * 0.5`, return
   `MajorGc` (gen2Delta > 0) or `MinorGc`.
5. **Third priority: clustered + short.** If `recentStallsInLast5s >= 5
   && wallMs < baselineMs * UiOverlayClusterMaxMultiplier (30.0)`, return
   `UiOverlayBlocking`.
6. **Fourth priority: short + lone + CPU-starved.** Same focus split as
   priority 1: `MainThreadFreeze` or `ProcessSuspended`.
7. **Fallback: LongFrame.** Wall + CPU both advanced, GC didn't.

**Audit finding 1.10.A.** No branch for `WorldLoad` cause. The enum
declares it (value 6) but no classifier path emits it. This is a known
gap — `WorldLoad` is documented in the enum but never assigned. The v0.4
decision entry mentions it ("New StallCause.WorldLoad for stalls during
the world-load tick window") but the implementation route is incomplete.
Two options: route world-load detection through `ContextTagger`-style
window check, OR remove the value to avoid the dead-code state. Listed in
§5.E.

**Audit finding 1.10.B.** The cluster classifier (priority 3) tests
`wallMs < baselineMs * UiOverlayClusterMaxMultiplier (30.0)`. At a 60-fps
baseline (16.67 ms), this is 500 ms — matches the enum doc string "each
80–500 ms". At a 30-fps baseline (33 ms), this is 1000 ms — also reasonable.
At 120-fps baseline (8.3 ms), this is 250 ms — feels narrow but the player
on that hardware would perceive a 250 ms hitch as a real freeze. Universal
scaling holds.

**Audit finding 1.10.C.** Priority 3 (`recentStallsInLast5s >= 5`) is
half-open: a cluster of exactly 5 starting at the 5th member returns
`UiOverlayBlocking` once the 5th stall fires, but stalls 1-4 were
classified individually as `LongFrame` or `MainThreadFreeze` first. The
classification doesn't retroactively rewrite. SessionRecorder.DrainStalls
+ FlushCluster resolve this at cluster-close time by computing the
*dominant* cause across the cluster, which is correct. But the
per-event row carries the moment-of-detection classification. This is
documented behaviour, not a bug. The dual-surface (event vs cluster)
captures both: the row is the receipt, the cluster is the story.

### 1.11 `ClassifySeverity(tickPeriodMs, baselineMs)`

Pure function. Two branches on `mult >= SeverityFreezeMultiplier (20)`,
`mult >= SeverityDisruptiveMultiplier (8)`, `mult >= SeverityNoticeableMultiplier (3)`,
else Minor. Sub-nanosecond. No drift.

### 1.12 `Reset()`

Called by `MetricCollector` at world-unload analogue. Clears every field
including `_events.Clear()`. Correct semantics: the next world's session
starts cold.

### 1.13 `CaptureBaseline(beginStamp)`

Five reads:
1. `_prevBeginStamp = beginStamp;` — primitive.
2. `_prevGcPauseMs = SafeGcPauseMs();` — wraps `GC.GetTotalPauseDuration().TotalMilliseconds`.
3. `_prevGen0 = GC.CollectionCount(0);` × 3 (Gen 0/1/2).
4. `_prevHeapBytes = GC.GetTotalMemory(forceFullCollection: false);` — single managed call.
5. `_self?.Refresh(); _prevCpuTime = _self.TotalProcessorTime;` — wrapped in try/catch.

Five GC counter reads per tick. Each is a single managed function call.
`GC.GetTotalPauseDuration()` allocates a `TimeSpan` struct (returned by
value, ABI-stamped — no heap alloc). `GC.CollectionCount` is a single
field load from CLR-internal counters. `GC.GetTotalMemory(false)` walks
the segment list briefly to sum committed bytes. All cheap.

The Process pair (`Refresh + TotalProcessorTime`) is the expensive part:
- macOS: routes via `Environment.CpuUsage` → cheap.
- Linux: `/proc/self/stat` file open + read + parse → microseconds.
- Windows: GetProcessTimes syscall → sub-microsecond.

At 60 Hz, the Linux path is ~60 × 5 µs = ~300 µs/sec of Process syscall
overhead. That's a fraction of a percent but it's measurable and
avoidable by switching to `Environment.CpuUsage.TotalTime` directly.

**Audit finding 1.13.A.** The CaptureBaseline path is the per-tick
cost; the most expensive read is the Process pair on non-macOS. Replacing
with `Environment.CpuUsage.TotalTime` is cross-platform cheaper, removes
the `try/catch` (no wrapping needed because there's no `_self?.` null
check), and frees the `_self` field for deletion. See §5.A.

### 1.14 `SafeGcPauseMs()`

```csharp
private static double SafeGcPauseMs() {
    try { return GC.GetTotalPauseDuration().TotalMilliseconds; }
    catch { return 0d; }
}
```

The catch handles "older runtimes" per the comment. tModLoader 1.4.4 is
pinned to .NET 8 (verified). `GC.GetTotalPauseDuration` was added in .NET 7.
The catch is therefore dead code in the production environment, but
keeping it is conservative — if a future tML release fragments runtime
support, the detector degrades gracefully. The throw itself never fires;
the cost of the try/catch when no exception happens is ~zero (the runtime
emits a try region but no actual instructions on the no-throw path).

**Audit finding 1.14.A.** The catch is defensive and free in the
no-throw case. Comment should be updated to reflect "tModLoader pins
.NET 8, GetTotalPauseDuration is in .NET 7+; the catch protects against
future runtime regressions, not older runtimes" — minor doc fix, not an
optimisation.

### 1.15 `StallEventsView` private inner class

Already covered in §1.3. Indexed via `this[index]`; enumeration via
`yield return` is not hit on the hot path.

### 1.16 `ProfilerFocusProbe.Read()`

```csharp
public static bool Read() {
    try { return Terraria.Main.hasFocus; }
    catch (Exception) { return true; }
}
```

Single static read of `Terraria.Main.hasFocus`. The field is a `bool`
maintained by FNA's SDL event loop on the main thread. Read on the main
thread (the BeginTick call site is the main thread) is a single field
load. The try/catch is purely defensive for the test path (`Main` may
not be initialised in xUnit).

**Audit finding 1.16.A.** The catch DOES have a measurable cost on the
hot path — even without an exception firing, the try-catch region inserts
EH metadata that affects JIT inlining decisions. In .NET 8 this is
usually a small penalty, but the call is per-tick.

Sources confirm `Main.hasFocus` is updated by FNA's SDL2_FNAPlatform
event loop (`SDL_WINDOWEVENT_FOCUS_GAINED` → true, `SDL_WINDOWEVENT_FOCUS_LOST`
→ false). The update happens on the main game thread during the SDL
event pump, which runs before each `Game.Update()` (and therefore before
each tModLoader `PreUpdateEntities` and therefore before each `BeginTick`).
So our read sees a value that is at most one tick stale.

**Audit finding 1.16.B.** The probe is correct but the try-catch is
overkill. The only way `Terraria.Main` could throw is a `TypeInitializer`
failure during the very first access, which would already have failed the
whole mod load. We can collapse to a direct field read inside an
`if (Terraria.Main is null) return true;` style guard, but actually
`Main` is a static class with `hasFocus` as a static field, so even the
null check is wrong. The cleanest path is to keep the catch but mark the
method with `[MethodImpl(MethodImplOptions.AggressiveInlining)]` so the
JIT inlines past the EH boundary. The cost saving is sub-nanosecond per
tick; in a 1.7M-ticks-per-day playtest, that's ~1.7 ms total. Marginal
but free. See §5.C.

### 1.17 `MetricCollector.BeginTick` (the call site, line 326)

```csharp
bool hadFocus = ProfilerFocusProbe.Read();
_stallDetector.OnBeginTick(_tickStartTimestamp, tickIndex, nowUnixMs, _baseline, _perModSmoothedMs, hadFocus);
```

One probe call, one detector call. `nowUnixMs` is computed inline via
`DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()`. That is itself a
non-trivial cost on the hot path:
- `DateTimeOffset.UtcNow` calls into the OS clock (1-2 µs on Linux/macOS,
  fast on Windows).
- `ToUnixTimeMilliseconds()` is a constant-time conversion.

Per tick: ~1-2 µs for the UTC read. At 60 Hz: 60-120 µs/sec, ~0.01%
overhead. Real but bounded.

**Audit finding 1.17.A.** `DateTimeOffset.UtcNow` per tick is the second
cost surface (after the Process pair). For stall-attribution log
cross-reference we need wall-clock time, so we can't replace with
`Stopwatch.GetTimestamp()` outright — the spike detector already uses
Stopwatch for relative timing, but stall events carry an absolute Unix
timestamp so a future agent can grep `client.log` against the DB row's
UnixMs. The optimisation lever is to compute UnixMs *only on the stall
path*, not every tick. The detector already takes `beginStamp` (Stopwatch
ticks) as its primary timing input; UnixMs is only used for the
`StartTimestampUnixMs` field of the eventually-written row and the
`CountRecentStallsInWindow` window math. Both consumers fire only on the
stall-detected path, so we can defer the UnixMs computation. See §5.F.

### 1.18 Persistence path — `StallStream`

`Profiling/Persistence/Streams/StallStream.cs` is 30 lines. Owns the
`stallEvents` collection. `Apply(op, db)` upserts a `StallEventRow`.
`Reconstruct(line)` deserialises JSON for replay. `EnsureIndexes`
creates an index on `SessionId`.

**Audit finding 1.18.A.** No per-event index beyond `SessionId`. Queries
like "what was the worst stall today" must scan every row in the
session. Acceptable for the current 50-stall cap per session, but if the
ring grows or persistence-tier downsampling later writes more rows, an
index on `(SessionId, DurationMs DESC)` becomes useful. Not urgent.

**Audit finding 1.18.B.** `StallStream.Apply` does a single `Upsert`,
keyed on the `_id` (ObjectId). Idempotency is by ObjectId — the writer
thread reuses the same ObjectId for the same event, so journal replay
won't dupe. Good shape.

### 1.19 `StallEventRow` BSON shape

Walked field by field:
- `Id` (ObjectId, 12 B).
- `Schema` (int, 4 B).
- `SessionId` (ObjectId, 12 B).
- `TickIndex` (long, 8 B).
- `UnixMs` (long, 8 B).
- `DurationMs` (double, 8 B).
- `BaselineTickMs` (double, 8 B).
- `Cause` (string, variable — typically 6-19 chars, BSON-encoded ~10-25 B).
- `Severity` (string, variable — 5-11 chars, ~10-15 B).
- `GcPauseDurationMs` (double, 8 B).
- `Gen0/1/2 Collections` (int × 3, 12 B).
- `HeapDeltaBytes` (long, 8 B).
- `CpuTimeDeltaMs` (double, 8 B).
- `Warming` (bool, 1 B in BSON).
- `ClusterId` (ObjectId?, 12 B or 0 B if null).
- `TopContributors` (List<StallContributorEntry>, ~5 × 40 B = 200 B).

Total per row: ~300-350 B BSON. 50 stalls per session → ~16 KB on disk.

**Audit finding 1.19.A.** `Cause` and `Severity` are stored as strings,
not as the byte-backed enum values. This trades 1 B for 6-25 B per row.
On 50 stalls/session that's ~600 B-1.2 KB lost per session. Across 100
sessions, 60-120 KB. Per-row, the readability of the stored string is
worth it (a human or agent reading the BSON sees "MajorGc" not "1"); the
total cost is modest. But for the per-session DB-size target (1064 KB →
< 600 KB), every kilobyte matters. The clean optimisation is to keep the
enum as a byte and add a `[BsonIgnore] public string CauseName => Cause.ToString();`
projection. Listed in §5.G.

**Audit finding 1.19.B.** `TopContributors` is a `List<StallContributorEntry>`
where each entry is `{ int ModId, string Name, double RecentMs }`. The
Name field is the mod's display name (e.g. "PerformanceProfiler",
"CheatSheet", "Verdant") — 8-30 chars per. Repeating the name in every
stall row is BSON bloat: the mod-name → mod-id mapping is already in the
`mods` collection. Storing just the ModId and joining at read time would
save ~100 B per stall, ~5 KB per session, ~500 KB across 100 sessions.
That's the headline storage win.

The trade-off is read-side complexity: a query for "what mod was
dominant in stall X" today reads one row; with the rewrite it needs a
join. LiteDB has `db.Stalls.Find(...).Select(s => new { s, modName = mods.FindById(s.TopContributors[0].ModId).Name })`
patterns. The join is cheap.

Listed in §5.G.

### 1.20 `StallClusterRow` BSON shape

Walked:
- `Id` (ObjectId, 12 B).
- `Schema` (int, 4 B).
- `SessionId` (ObjectId, 12 B).
- `StartTick / EndTick` (long × 2, 16 B).
- `StartUnixMs / EndUnixMs` (long × 2, 16 B).
- `StallCount` (int, 4 B).
- `TotalDurationMs / WorstDurationMs / SpanMs` (double × 3, 24 B).
- `DominantCause` (string).
- `DominantContributorModId` (int).
- `DominantContributorName` (string).

Per row ~150 B. 10 clusters/session = ~1.5 KB. Small.

**Audit finding 1.20.A.** Same `DominantCause` string-instead-of-byte
issue as `StallEventRow`. Same `DominantContributorName` redundancy.
Same fix applies. Marginal in absolute bytes for the cluster row alone
because there are 5× fewer cluster rows than event rows, but applying
the change uniformly across the two row types is the clean refactor.

### 1.21 `SessionRecorder.DrainStalls + FlushCluster` (consumer side)

The consumer of detector output. Reads `collector.Stalls` (which is
`_view`, the cached IReadOnlyList), tracks a `_stallCursor`, walks new
events.

For each new event:
1. Cluster window break check: `s.StartTimestampUnixMs - _liveCluster.EndUnixMs > ClusterIdleMs (2000)` → flush current.
2. Open new cluster if needed.
3. Update cluster aggregates (end ticks, count, total ms, worst ms, span).
4. Increment `_clusterCauseCounts[causeKey]` via `Dictionary<string, int>`.
5. Accumulate per-mod cost via `_clusterContribCost[modId] += recentMs` (Dictionary<int, double>).
6. Build a `topContribs = new List<StallContributorEntry>(5);` then `AddContrib` × 5 from C0..C4.
7. Build a `StallEventRow` and enqueue.

**Audit finding 1.21.A.** Step 6 allocates a fresh `List<StallContributorEntry>(5)`
plus up to 5 `StallContributorEntry` objects per stall. This is OFF the
per-tick hot path (stalls fire ~once per 2 seconds at peak), and these
allocations are then handed to the writer thread which serialises them
into BSON — they have to land on the heap somewhere because they end up
in LiteDB. But two cleanups exist:
- The `new List<>(5)` could be a pre-sized `StallContributorEntry[5]`
  if `StallEventRow` accepted an array instead of `List<>`.
- The five `AddContrib` calls each `new StallContributorEntry { ... }`
  unconditionally even when the contributor slot is empty (`ModId == -1`).
  Skipping empty slots saves 0-5 allocations per stall.

Both are cleanups, not load-bearing. Listed in §5.H.

**Audit finding 1.21.B.** `_clusterCauseCounts` is keyed by `string`
(the cause's `.ToString()`). Enum.ToString() allocates a string per call
in older runtimes; .NET 8 caches enum names so subsequent calls are
free, but the dictionary lookup uses string-equality semantics. Replacing
with `Dictionary<StallCause, int>` (or a `Span<int>` indexed by
`(int)cause`) is cheaper and clearer. See §5.I.

**Audit finding 1.21.C.** `FlushCluster` iterates `_clusterCauseCounts`
twice (find max) and `_clusterContribCost` once (find max). Both are
small dictionaries (≤ 8 entries each typically). Cost is negligible.

**Audit finding 1.21.D.** Drain runs from `SessionRecorder.Tick`, which
ProfilerSystem fires per tick. So the drain itself runs per tick even
when no new stalls have landed. Cost = checking `_stallCursor < stalls.Count`,
which is one int compare. Free.

### 1.22 Cross-coupling map

```
ProfilerSystem.PreUpdateEntities
   ↓
MetricCollector.BeginTick(tickIndex)
   ↓
  _gcPauseMsAtTickStart = GcPauseMilliseconds()    // duplicate of detector's read
   ↓
  ProfilerFocusProbe.Read()                        // [ TRY/CATCH ]
   ↓
  StallDetector.OnBeginTick(..., hadFocus)
       ↓
      CaptureBaseline:
           GC.GetTotalPauseDuration()    ← REPEATED CALL #1
           GC.CollectionCount(0,1,2)
           GC.GetTotalMemory(false)
           Process.Refresh + TotalProcessorTime    ← [ TRY/CATCH ]
       ↓
       if stall:
           SafeGcPauseMs() again         ← REPEATED CALL #2
           GC.CollectionCount(0,1,2) again
           Process pair again            ← [ TRY/CATCH ]
           CaptureTopContributors        ← interface dispatch loop
           _events.Push(in ev)
           CountRecentStallsInWindow     ← 50-elem scan
           CaptureBaseline AGAIN         ← repeats everything

SessionRecorder.Tick  (per tick)
   ↓
  DrainStalls(collector)                ← cursor walk, usually nop
   ↓
  if cluster idle > 2s: FlushCluster   ← only fires per cluster boundary
```

**Audit finding 1.22.A. THE BIG DUPLICATION.** `MetricCollector.BeginTick`
calls `GcPauseMilliseconds()` (its own copy) AND then via
`_stallDetector.OnBeginTick` → `CaptureBaseline` calls
`SafeGcPauseMs()`. That's two reads of `GC.GetTotalPauseDuration` per
tick (one for the collector's per-tick GC delta, one for the detector's
baseline snapshot). They could share. Even cheaper: the collector could
compute the delta once and pass it through to the detector.

The collector reads at the START of the tick (`BeginTick`) and again at
the END (`EndTick`) for the per-tick GC delta. The detector reads at the
START of every tick. So we have three GC pause reads per tick (collector
start, detector start, collector end) where two are sufficient.

Net waste: `GC.GetTotalPauseDuration` × 1 redundant call per tick. At
60 Hz that's 60 extra reads/sec. The call is ~50 ns; total ~3 µs/sec —
negligible per call but cumulative across the 5-counter family this
optimisation pattern repeats. The bigger lever is reading the GC counters
ONCE per tick and sharing across all consumers (detector + collector +
spike-detector if it wants them, +allocation tracking).

See §5.J for the proposed shared `GcCounterSnapshot` value-type passed
through `BeginTick`.

---

## 2. Baseline — what the v0.5 playtest actually produced

From `context/perf-pass/baseline.md`:

| Surface | Value |
|---|---|
| Session length | 4 min 55 s wall, 16,009 ticks |
| Spikes detected | 50 |
| Stalls detected | 50 |
| Stall clusters | 10 |
| End-of-session UiOverlayBlocking cluster | 40 stalls / 8.5 s, contributor: PerformanceProfiler |

So 50 stalls over 4.5 min = 0.18 stalls/sec. 10 clusters = 1 cluster per
27 seconds. The largest cluster was 40 stalls in 8.5 s = 4.7 stalls/sec
DURING that cluster — well above the `recentStallsInLast5s >= 5`
threshold, so the cluster path fired correctly.

**Per-stall cost contributions** (estimated, no measurement yet — pinned
in §3 for verification):

| Read | Per-call cost | Per-tick cost | Per-sec at 60 Hz |
|---|---|---|---|
| `GC.GetTotalPauseDuration` | ~50 ns | 1× | 3 µs/sec |
| `GC.CollectionCount(0/1/2)` × 3 | ~10 ns × 3 | 3× | 1.8 µs/sec |
| `GC.GetTotalMemory(false)` | ~80 ns | 1× | 4.8 µs/sec |
| `Process.Refresh + TotalProcessorTime` | ~5 µs (Linux), <1 µs (macOS) | 1× | 60-300 µs/sec |
| `DateTimeOffset.UtcNow` | ~1 µs | 1× | 60 µs/sec |
| `ProfilerFocusProbe.Read` | ~5 ns | 1× | 0.3 µs/sec |
| `StallDetector.CaptureBaseline` (sum) | ~5-7 µs Linux, <2 µs macOS | 1× | **300-420 µs/sec Linux** |

The CaptureBaseline path is dominated by the Process pair on Linux. On
macOS it's much cheaper. On Windows it's somewhere between. The dev
machine (Apple Silicon, macOS) is biased toward the cheap path; the
distribution of player hardware skews Windows + Linux, so the optimisation
is more valuable for shipped users than for the dev playtest baseline.

### 2.1 Classifier truth table — pinned

The classifier branch order (§1.10) implies the following table. This is
the verification target for §5.K's diagnostic tests.

| wallMs/baseline | gcMs/wallMs | gen2Δ | cpuMs/wallMs | recent5s | focusHeld | Expected Cause |
|---|---|---|---|---|---|---|
| 0 | * | * | * | * | * | Unknown |
| ≥ 50 | < 0.5 | * | < 0.2 | ≤ 2 | true | MainThreadFreeze |
| ≥ 50 | < 0.5 | * | < 0.2 | ≤ 2 | false | ProcessSuspended |
| 3..50 | > 0.5 | > 0 | * | * | * | MajorGc |
| 3..50 | > 0.5 | = 0 | * | * | * | MinorGc |
| 3..30 | < 0.5 | * | * | ≥ 5 | * | UiOverlayBlocking |
| 3..30 | < 0.5 | * | < 0.2 | ≤ 2 | true | MainThreadFreeze |
| 3..30 | < 0.5 | * | < 0.2 | ≤ 2 | false | ProcessSuspended |
| 3..30 | < 0.5 | * | ≥ 0.2 | * | * | LongFrame |
| 3..50 | < 0.5 | * | ≥ 0.2 | ≤ 2 | * | LongFrame |

Each row corresponds to a unit-test case in `StallClassifierTests`. The
test file currently covers a subset; the perf pass adds tests for every
truth-table row, plus boundary cases (wallMs exactly 50× baseline, exactly
30× baseline, exactly 5 recent stalls, exactly 0.5 gc-to-wall ratio).
Truth-table-driven testing is the testability + reproducibility
discipline from CLAUDE.md applied here.

### 2.2 Cluster algorithm — what fires when

From `SessionRecorder.DrainStalls`:

```
if (_liveCluster != null
    && s.StartTimestampUnixMs - _liveCluster.EndUnixMs > ClusterIdleMs)
    FlushCluster();
```

Plain: if the new stall's start is more than 2 s after the cluster's last
recorded end, close the cluster.

The `EndUnixMs` is assigned `s.StartTimestampUnixMs` for every stall —
i.e., the cluster's end is the START of its last stall, not the END. This
is a small bug shape: a 10-second stall starting at t=0 would have
cluster.EndUnixMs = 0, not 10000. The next stall at t=11000 would be
classified as "11000 - 0 = 11000 > 2000 → flush", which is correct in
practice but the bookkeeping is conceptually off.

**Audit finding 2.2.A.** `_liveCluster.EndUnixMs = s.StartTimestampUnixMs`
should be `s.StartTimestampUnixMs + (long)s.TickPeriodMs` (start + duration),
giving the cluster's true end time. The current value approximates correctly
for the cluster-boundary check (because all stalls are short relative to
ClusterIdleMs=2000) but the cluster's `SpanMs` field is then 2 ms off per
stall. For a cluster of 40 stalls, that's 80 ms of "missing" span. Cosmetic
fix — listed in §5.L.

---

## 3. .NET 8 process / GC counter research

Citations and behavioural detail for every counter the detector reads.

### 3.1 `GC.GetTotalPauseDuration` (.NET 7+)

* **API:** Returns `TimeSpan`. "Gets the total amount of time paused in
  GC since the beginning of the process." Available in .NET 7, 8, 9, 10.
  Source path: `dotnet/runtime` `src/coreclr/System.Private.CoreLib/src/System/GC.CoreCLR.cs`.
* **Implementation:** Calls into the CoreCLR's `GCHeapInternal::GetTotalPauseDuration`
  which reads a single per-heap counter accumulated by every SuspendEE /
  RestartEE pair. No locking; the read is a single `long` load. Cost:
  ~50 ns including the managed-to-native transition.
* **Thread-safety:** Safe to call from any managed thread. Cross-thread
  reads see eventually-consistent values; for our use case (per-tick
  delta over a single thread's reads), the eventual consistency is
  irrelevant.
* **Defensive try/catch:** The catch in `SafeGcPauseMs` is dead code in
  .NET 7+ runtimes (the API is present and never throws). It protects
  against an unimaginable future regression. Free at runtime on the
  no-throw path. Keep.
* **Citation:** [Microsoft Learn — GC.GetTotalPauseDuration](https://learn.microsoft.com/en-us/dotnet/api/system.gc.gettotalpauseduration). [API proposal #66036](https://github.com/dotnet/runtime/issues/66036) (added in .NET 7 per the proposal timeline).

### 3.2 `GC.CollectionCount(int generation)`

* **API:** Returns `int`. "The number of times garbage collection has
  occurred for the specified generation of objects." Available since
  .NET Framework 2.0.
* **Implementation:** Single `int` load from the GC's per-generation
  counter. ~10 ns each.
* **Thread-safety:** Atomic read. Safe.
* **Citation:** [Microsoft Learn — GC.CollectionCount](https://learn.microsoft.com/en-us/dotnet/api/system.gc.collectioncount).

### 3.3 `GC.GetTotalMemory(bool forceFullCollection)`

* **API with `false`:** Returns `long`. "Retrieves the number of bytes
  currently thought to be allocated. A parameter indicates whether this
  method can wait a short interval before returning, to allow the system
  to collect garbage and finalize objects."
* **Behaviour with `false`:** Walks the segment list briefly to sum
  committed bytes. Does NOT trigger a collection. Cost ~80 ns on a heap
  of a few segments; grows linearly with segment count. For a 200 MB
  Terraria modded heap, expect ~100-200 ns.
* **Critical:** With `true`, this DOES trigger a collection and is ~ms
  cost. We pass `false`. Confirmed safe.
* **Citation:** [Microsoft Learn — GC.GetTotalMemory](https://learn.microsoft.com/en-us/dotnet/api/system.gc.gettotalmemory).

### 3.4 `Process.TotalProcessorTime` + `Process.Refresh()`

* **API:** Returns `TimeSpan`. "Gets the total processor time for this
  process."
* **macOS implementation (verified via runtime source `Process.OSX.cs`):**
  For the current process, internally routes via
  `Environment.CpuUsage.TotalTime`. This is a single native call
  (`mach_thread_times` or equivalent) and returns a struct value. Cost
  sub-microsecond. *For other processes*, it would call
  `Interop.libproc.proc_pid_rusage(_processId)` which is a syscall.
* **Linux implementation:** Reads `/proc/self/stat`, parses the utime +
  stime fields, multiplies by the user-HZ rate. File open + 1KB read +
  parse. Cost: 5-10 µs per call typical.
* **Windows implementation:** Calls `GetProcessTimes()` via P/Invoke.
  Sub-microsecond.
* **`Refresh()`:** Discards cached property values so the next read
  re-fetches. The cache is per-`Process` instance. For our usage —
  reading `TotalProcessorTime` once per tick — we MUST call `Refresh()`
  before each read, otherwise we'd see the same cached value forever.
* **macOS quirk:** [Issue #29527](https://github.com/dotnet/runtime/issues/29527)
  notes that `TotalProcessorTime` on macOS can return values that look
  inflated. The underlying counters multiply by core count differently
  than Linux/Windows. For the *delta* between two reads (our use case),
  the bias is consistent — a 2× overstatement on read 1 and 2× on read
  2 still produces the correct delta. We are not affected.
* **Citations:** [Process.TotalProcessorTime](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.totalprocessortime). [Process.Refresh](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.refresh). [Issue 29527 - macOS values](https://github.com/dotnet/runtime/issues/29527). [Issue 15405 - macOS process info](https://github.com/dotnet/runtime/issues/15405).

### 3.5 `Environment.CpuUsage` (the better path)

* **API:** Returns `ProcessCpuUsage` struct with `UserTime`,
  `PrivilegedTime`, `TotalTime`. Available in .NET 7+.
* **Implementation:** Single native call returning a struct. NO `Process`
  instance, NO refresh state, NO try/catch (it doesn't throw in the
  contexts we use).
* **Cross-platform behaviour:** On macOS routes via the same
  `mach_thread_times` path. On Linux uses `getrusage(RUSAGE_SELF)` —
  faster than `/proc/self/stat` because it's a single syscall returning
  a struct. On Windows uses `GetProcessTimes()`. All sub-microsecond.
* **Allocation:** Zero — return is a `struct`, three fields. Stack-only.
* **Citation:** [Environment.CpuUsage](https://learn.microsoft.com/en-us/dotnet/api/system.environment.cpuusage). [Issue 60281 - API proposal](https://github.com/dotnet/runtime/issues/60281).

**This is the optimisation lever.** Replacing the
`_self.Refresh() + _self.TotalProcessorTime` pair with
`Environment.CpuUsage.TotalTime` saves:
- macOS: parity (same underlying counter, no wrapper overhead) — ~200 ns
  per tick saved.
- Linux: faster path (`getrusage` vs `/proc/self/stat`) — 4-9 µs saved
  per tick → 240-540 µs/sec saved.
- Windows: parity.

At 60 Hz on Linux, that's 0.024-0.054 ms/sec of pure CPU saved on every
modded player's machine. Universal benefit.

### 3.6 `Stopwatch.GetTimestamp` (already used)

For completeness — the detector uses `Stopwatch.GetTimestamp()` for the
`beginStamp` parameter. This is well-documented: sub-100 ns on every
platform, no allocation, monotonic. Already optimal.

### 3.7 `DateTimeOffset.UtcNow` (the per-tick second cost)

* **API:** Returns `DateTimeOffset`. The UTC wall-clock time.
* **Implementation:** Calls `DateTime.UtcNow` which calls
  `Interop.GetSystemTimeAsFileTime` (Windows) or `clock_gettime(CLOCK_REALTIME)`
  (Linux/macOS). 200-1000 ns typical.
* **Optimisation:** Move the read to the stall-fired path only (see §5.F).
* **Citation:** [DateTimeOffset.UtcNow](https://learn.microsoft.com/en-us/dotnet/api/system.datetimeoffset.utcnow).

### 3.8 `Terraria.Main.hasFocus` + FNA SDL focus events

* **Source:** The `Main.hasFocus` field is a `bool` on `Terraria.Main`,
  maintained by FNA's SDL2_FNAPlatform. SDL2 dispatches
  `SDL_WINDOWEVENT_FOCUS_GAINED` and `SDL_WINDOWEVENT_FOCUS_LOST` events.
  FNA's event pump processes these synchronously inside `Game.Tick()`,
  which runs BEFORE the game's `Update` (and therefore before
  `PreUpdateEntities` and therefore before our `BeginTick`).
* **Thread:** Updated on the main thread. Read on the main thread (our
  BeginTick). No cross-thread visibility issue.
* **Staleness:** At most one tick stale. For stall classification this is
  the right granularity — a focus change mid-stall is the exact signal we
  want to capture.
* **Apple Silicon caveat:** [tModLoader #4941](https://github.com/tModLoader/tModLoader/issues/4941)
  notes fullscreen transitions silently crash on Apple Silicon. This is
  outside our scope but worth knowing for the dev environment.
* **Citations:** [FNA SDL2_FNAPlatform.cs](https://github.com/FNA-XNA/FNA/blob/master/src/FNAPlatform/SDL2_FNAPlatform.cs). [SDL_WindowEventID](https://wiki.libsdl.org/SDL2/SDL_WindowEventID). [tModLoader #4941](https://github.com/tModLoader/tModLoader/issues/4941).

### 3.9 Runtime version constraint

* **Confirmed:** tModLoader 1.4.4-stable targets .NET 8 (`net8.0`). [tModLoader and .NET 8 forum thread](https://discourse.cubecoders.com/t/tmodloader-and-net-8/12172). [tModLoader #4900 - runtime detection](https://github.com/tModLoader/tModLoader/issues/4900).
* **Implication:** Every API in §3.1-3.7 is available. The defensive
  try/catches in the detector are protecting against future runtime
  regressions, not current absences. Comment updates listed in §5.M.

---

## 4. Cluster algorithm analysis

### 4.1 The current algorithm in one paragraph

A stall extends the live cluster if its start time is within `ClusterIdleMs = 2000` ms of the cluster's end time; otherwise it flushes the
current cluster and opens a new one. Each stall contributes to the
cluster's running aggregates (count, total ms, worst ms, span, cause
counts, per-mod cost sums). At cluster flush time, the dominant cause is
the most-frequent across the cluster's events, and the dominant
contributor is the mod with the highest cumulative `RecentMs`.

The detector's own per-event classifier uses `recentStallsInLast5s` (a
**5-second** window over the detector's own ring) to decide
`UiOverlayBlocking` vs `LongFrame`. So there are TWO temporal windows:
- 5 seconds, inside the detector, used at per-event classification time.
- 2 seconds, inside SessionRecorder, used for cluster-boundary detection.

### 4.2 Is this the right shape

**For per-event classification:** Yes. The 5-second window is the
"clustered short bursts" signal. It needs to be long enough to span a
burst of UI-overlay-blocked frames at 60 Hz (a 0.5 s burst is 30 frames
~= 30 potential stall candidates), but short enough that a true OS
suspend (5+ seconds of frozen time, then resume) doesn't look like a
cluster. 5 seconds works for the CheatSheet menu case (47 stalls over
13 seconds — within any 5-second window, ≥ 5 stalls had landed).

**For cluster-boundary detection:** Mostly yes. The 2-second gap is the
"the player saw a freeze, then the game caught up, then a new freeze"
boundary. A 2-second gap of normal frames between two stall bursts is
"two separate freezes" from the player's perspective.

**One open question.** A UI-overlay-blocking cluster that has a 2.5 s
quiet patch in the middle (the player paused dragging items, then
resumed) would be flushed as two separate clusters. From the player's
narrative, "the spawn menu was open for 13 seconds and laggy throughout"
should be one cluster, not two. The 2-second threshold is somewhat
arbitrary.

**Suggested investigation (not a §5 change, but a flag):** instrument the
cluster boundary decision with a per-stall trace at debug log level. If
the next playtest produces clusters that "should have been one" but
split, raise ClusterIdleMs to 3000-5000 ms. Don't change preemptively.

### 4.3 Can it be tightened (performance, not semantics)

The cluster machinery in SessionRecorder is on the writer-side path and
fires per stall (~0.2/sec). Its CPU cost is negligible. Optimisation
candidates:

- **Replace `Dictionary<string, int>` cluster-cause counts with `Span<int>`.**
  The `StallCause` enum is byte-backed with ≤8 values. A fixed-size
  `int[8]` indexed by `(int)cause` is one memory access vs a dict hash +
  probe. Save: ~200 ns per stall on the writer side. (§5.I)
- **Replace `Dictionary<int, double>` cluster-contrib costs with a
  small open-addressing array.** Mod count is ≤ ~50 typically; an inline
  16-entry linear-probe array would beat the Dictionary on hash + probe
  cost. Save: ~300-500 ns per stall on the writer side. Probably not
  worth the complexity given how rare stalls are. (§5.I, secondary)
- **`StallContributorEntry.Name` redundancy** — already covered in §1.19/1.21.
  The Name field is denormalised; storing only ModId and joining at read
  time saves storage and per-stall string-copy alloc. (§5.G, §5.H)

### 4.4 Cluster-cause dominance correctness

The dominant cause is "most frequent across events", which works when the
cluster is homogeneous (all UiOverlayBlocking, or all MajorGc). For a
mixed cluster — say 3 LongFrame + 2 MajorGc + 5 UiOverlayBlocking — the
result is UiOverlayBlocking (correctly). For 3+3+3 the result is
implementation-order-dependent (whichever the foreach hits first wins on
ties). This is a stable-but-arbitrary tie-break. Not a bug, but worth
documenting.

**Audit finding 4.4.A.** Add a tiebreaker — pick the cause with higher
median severity, or higher total cumulative wall time. The latter is
already trackable (sum of `s.TickPeriodMs` per cause). Listed in §5.N.

### 4.5 Cluster-contributor correctness

`_clusterContribCost[modId] += s.Cx.RecentMs` accumulates the per-stall
top-5 mod cost. This double-counts mods that appear in multiple stalls'
top-5: a mod that is contributor #1 in 20 stalls and contributor #2 in
10 stalls gets summed for ALL 30 stalls. That's the intent — the cluster
wants total cumulative cost attribution. Correct.

But the per-stall `RecentMs` is the SMOOTHED 30-second rolling average
at the moment the stall fired. Summing rolling-averages across 40 stalls
that all fired within 8.5 seconds (and therefore all read the SAME
smoothed value) double-counts. The cluster's dominant contributor's
`domVal` is therefore (mod's recent ms) × (stall count) when all stalls
land in one short burst.

The relative ordering across mods is preserved (whoever is the dominant
contributor in one stall is dominant in all of them, assuming smoothing
is slow), so the *winner* is correct. The *value* (`domVal`) is wrong by
a factor of stall count, but `domVal` isn't written to the DB —
`DominantContributorModId` is.

**Audit finding 4.5.A.** No bug in the output. Worth a comment because
the next reader of `FlushCluster` will likely also wonder why we sum
smoothed values. Listed in §5.O.

---

## 5. Optimisation opportunities — prioritised list

### §5.A — Replace `Process.GetCurrentProcess() + Refresh + TotalProcessorTime` with `Environment.CpuUsage.TotalTime`

**Priority: 1 (headline win).** **Universal: yes.** **Category: alloc + syscall removal.**

**What.** In `StallDetector`:
- Remove the `_self` field and its construction try/catch.
- Replace the `_self?.Refresh(); cpuNow = _self.TotalProcessorTime;` pair in `OnBeginTick` with `cpuNow = Environment.CpuUsage.TotalTime;`.
- Same change in `CaptureBaseline`.

**Why.**
- Per §3.4, `Process.TotalProcessorTime` on the current process already routes via `Environment.CpuUsage.TotalTime` on macOS. On Linux it goes the slow path (`/proc/self/stat`). On Windows it routes via `GetProcessTimes`.
- `Environment.CpuUsage` is universally the cheapest path: no `Process` wrapper, no Refresh state, no instance caching to worry about, no try/catch.
- Available .NET 7+; tModLoader 1.4.4 is .NET 8 — supported.

**Expected delta.** Linux: -240 to -540 µs/sec of detector CPU. macOS / Windows: -60 to -120 µs/sec (savings from avoiding the `Process` wrapper overhead). No allocation change in the steady state (the `Process` cache was already preventing alloc). Removal of try/catch construction path. Zero behaviour change.

**Verification.** Add `StallDetectorBenchmark.MeasureCaptureBaseline_BeforeAndAfter` to the existing test harness; measure the per-call cost on the dev machine and compare. Baseline-target rows update accordingly.

**Risk.** Low. `Environment.CpuUsage` semantics match `Process.TotalProcessorTime` for the current process. The macOS quirk (§3.4) about inflated absolute values is a non-issue for deltas. The catch removal does mean construction-time failures no longer get swallowed silently — but `Environment.CpuUsage` doesn't fail in normal contexts.

### §5.B — Change `IReadOnlyList<double>?` to `double[]?` in `OnBeginTick` + `CaptureTopContributors`

**Priority: 2.** **Universal: yes.** **Category: devirtualisation.**

**What.** Change the parameter type from interface to concrete array. Callers already pass `double[]` (verified at the `MetricCollector` call site).

**Why.** The hot loop `perModSmoothedMs[offset + c]` × cats × modCount times currently goes through interface dispatch. The JIT in .NET 8 has tiered PGO and may devirtualise after a few hundred calls, but `double[]` indexer is intrinsified at JIT time — no dispatch at all.

**Expected delta.** On a 40-mod stack with 7 categories, 40 × 7 = 280 indexer calls per stall. Saving ~5-10 ns each = 1.4-2.8 µs per stall. Stall rate is ~0.18/sec, so ~0.5 µs/sec saved. Marginal but free.

**Verification.** Add `CaptureTopContributors` unit test for null-safe behaviour with array input. The signature change is binary-compatible at the callsite (any caller using IReadOnlyList<double> still compiles if they have a `double[]` reference; the test back-compat overloads need a similar update).

**Risk.** Low. The interface-typed overloads are public surface — back-compat shims need to forward.

### §5.C — Mark `ProfilerFocusProbe.Read` with `AggressiveInlining` + drop the catch

**Priority: 3.** **Universal: yes.** **Category: inlining + EH removal.**

**What.** `[MethodImpl(MethodImplOptions.AggressiveInlining)] internal static bool Read() => Terraria.Main.hasFocus;` — but keep the try/catch IF the static field access can throw a TypeInitializationException on first access (which it can, in principle).

**Why.** Sub-nanosecond saving per tick × 60 Hz × per-day playtest = ~1.7 ms/day saved on the per-tick path. Free win. The harder question is whether to drop the catch. `Terraria.Main` is loaded by tModLoader before any of our hooks fire, so the type initializer has already run; the catch is purely defensive against weird init paths that don't exist in practice.

**Recommendation.** Keep the catch (it's the honest-failure path), but add `[MethodImpl(MethodImplOptions.AggressiveInlining)]`. The JIT will then inline past the EH boundary on hot paths.

**Expected delta.** ~5 ns/tick. 0.3 µs/sec at 60 Hz.

**Verification.** Existing tests cover the no-Main path.

**Risk.** Zero.

### §5.D — Replace the silent Process-construction catch with a Logger.Warn

**Priority: 4.** **Universal: yes.** **Category: observability + failure honesty.**

**What.** Currently: `catch { _self = null; }` silently — if construction fails, every stall classifies as CPU-starved. After §5.A removes `_self`, this finding becomes moot. But if §5.A is rejected for any reason, the catch should log once via `Mod.Logger.Warn` so the agent surface sees the failure.

**Note:** Subsumed by §5.A. Listed separately so that if §5.A is split across commits, §5.D doesn't get lost.

### §5.E — Resolve `StallCause.WorldLoad` dead-code state

**Priority: 5.** **Universal: yes.** **Category: classifier completeness.**

**What.** The enum declares `StallCause.WorldLoad = 6` but no classifier path emits it. Choose:
- **Option E1:** Route a "during world load" detector through the `ContextTagger` and check `IsWorldLoading()` at classify time. Emit `WorldLoad` for any stall that fires during the world-load window (first ~5 s after `OnWorldLoad`).
- **Option E2:** Remove the enum value. Less honest because the v0.4 decision committed to it.

**Recommendation.** Option E1 — the v0.4 decision was specific. Use `ProfilerSystem.IsInWorldLoadWindow(tickIndex)` (a new helper that returns true for ticks within N seconds of `OnWorldLoad`) and check at classify-time. Plumbing already exists for the ContextTagger.

**Expected delta.** No CPU change; one new branch in `ClassifyCause` before priority 1.

**Verification.** Add `StallClassifierTests.WorldLoadTickWindow_ClassifiesAsWorldLoad`.

**Risk.** Low.

### §5.F — Defer `DateTimeOffset.UtcNow` to the stall-fired path

**Priority: 6.** **Universal: yes.** **Category: lazy evaluation.**

**What.** Move the UnixMs computation from `MetricCollector.BeginTick` into the stall-detected branch of `StallDetector.OnBeginTick`. The detector signature changes to NOT take `tickStartUnixMs` per call; it asks for it only on the stall path.

**Plumbing.** A `Func<long>? unixMsProvider` parameter, or a static helper `DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()` called lazily inside the stall branch.

**Expected delta.** -60 µs/sec at 60 Hz (the UtcNow cost). Saved per-tick; paid only ~0.18 times/sec on stall. Net win ~58 µs/sec.

**Verification.** Existing tests pass UnixMs explicitly; the new lazy path produces the same value when invoked.

**Risk.** Low. The UnixMs value goes into the row's `StartTimestampUnixMs` field; computing it 50 µs after `beginStamp` makes that field 50 µs late vs Stopwatch — negligible for log cross-reference.

### §5.G — Persist `Cause` and `Severity` as bytes, not strings; remove `Name` redundancy from contributors

**Priority: 7.** **Universal: yes.** **Category: storage compaction.**

**What.**
- `StallEventRow.Cause` and `.Severity`: change to `byte`. Add BSON-ignored projection properties for readability.
- `StallClusterRow.DominantCause`: same change.
- `StallContributorEntry`: remove `Name`. Read-side joins to the `mods` collection.

**Expected delta.**
- Per stall row: ~25 B → ~2 B for cause+severity = ~23 B saved.
- Per contributor entry: ~20-30 B → 16 B = ~10 B saved × up to 5 per row = ~50 B per stall row.
- Per session: 50 stalls × ~73 B = ~3.6 KB saved. Across 100 sessions: ~360 KB.

This is the headline storage win from the stall path; it directly contributes to the 1064 KB → < 600 KB target.

**Verification.** Schema bump v2 → v3 with a migration path. Existing v2 rows continue to read via fallback. New rows write the byte fields.

**Risk.** Schema migration. Has to be journal-safe — both schemas in flight at the same time during a rolling upgrade. The `Schema` field on the row is exactly the migration lever; LiteDB has BSON converters per schema. Established pattern.

### §5.H — Drop the `List<StallContributorEntry>` allocation per stall

**Priority: 8.** **Universal: yes.** **Category: allocation removal.**

**What.** In `SessionRecorder.DrainStalls`, replace
```csharp
var topContribs = new List<StallContributorEntry>(5);
AddContrib(topContribs, s.C0, modNames);
... × 5
```
with a pre-pinned `StallContributorEntry[5]` slot field on the recorder, filled in place. The `StallEventRow.TopContributors` becomes `StallContributorEntry[]` (or stays `List<>` if BSON serialisation requires it; the array form is just a copy-out at row-build time).

Skip empty slots (`ModId == -1`) at row-build time so the in-disk row only carries populated entries.

**Expected delta.** Per stall: -1 List allocation + up to -5 individual struct boxings (depends on how BSON serialiser handles structs in List vs array). Estimated -100-200 B per stall. 50 stalls/session = -5-10 KB per session.

**Verification.** Allocation profiler confirms zero allocations in `DrainStalls` outside the row creation itself.

**Risk.** Schema-shape change on the row's `TopContributors` field. Same migration story as §5.G.

### §5.I — Replace `Dictionary<string, int>` cluster-cause counts with `int[8]` indexed by enum

**Priority: 9.** **Universal: yes.** **Category: data-structure swap.**

**What.** In `SessionRecorder`:
```csharp
private readonly int[] _clusterCauseCounts = new int[8];  // one per StallCause value
```
Increment via `_clusterCauseCounts[(int)s.Cause]++;`. Reset via `Array.Clear(_clusterCauseCounts, 0, _clusterCauseCounts.Length);`.

**Expected delta.** Per stall on the writer-side: -1 string allocation (`s.Cause.ToString()` is cached by .NET 8 but the dictionary still hashes), -1 dict TryGetValue + Insert. ~200-400 ns per stall.

**Verification.** Dominant-cause logic ports cleanly (foreach over array indices, pick max).

**Risk.** Zero. Pure refactor.

### §5.J — Share a single `GcCounterSnapshot` across detector + collector per tick

**Priority: 10.** **Universal: yes.** **Category: redundant read removal.**

**What.** A shared per-tick `struct GcCounterSnapshot { double PauseMs; int Gen0, Gen1, Gen2; long HeapBytes; }`. The `MetricCollector.BeginTick` reads it once at the very start and passes it to `_stallDetector.OnBeginTick` instead of re-reading.

**Expected delta.** -1 `GC.GetTotalPauseDuration` call per tick, -3 `GC.CollectionCount` calls per tick, -1 `GC.GetTotalMemory(false)` call per tick. Total: ~150-250 ns per tick × 60 Hz = ~9-15 µs/sec saved.

Less measurable than §5.A but still strictly cheaper, and cleaner — the GC counter reads are owned by one place.

**Verification.** Existing tests pass the snapshot in; the detector path no longer re-reads.

**Risk.** Low. Signature change on the detector hot path.

### §5.K — Truth-table test coverage

**Priority: 11.** **Universal: yes.** **Category: testing.**

**What.** Add xUnit tests for every row of the §2.1 truth table, including boundary cases (wallMs exactly at 50× / 30× baseline; recentStallsInLast5s exactly 2/3/4/5; gcMs exactly at 0.5 × wallMs).

**Expected delta.** Zero CPU change; pin the classifier behaviour against future drift.

**Verification.** The test file itself; targets `StallClassifierTests`.

**Risk.** Zero.

### §5.L — Fix `_liveCluster.EndUnixMs` to use stall END not stall START

**Priority: 12.** **Universal: yes.** **Category: bookkeeping correctness.**

**What.** `_liveCluster.EndUnixMs = s.StartTimestampUnixMs + (long)s.TickPeriodMs;` (start + duration). And `SpanMs` recomputes from the corrected end.

**Expected delta.** Cosmetic; cluster span values become accurate to the millisecond.

**Verification.** Test that a 1-stall cluster with TickPeriodMs=500 has SpanMs=500 (currently SpanMs=0).

**Risk.** Zero.

### §5.M — Update `SafeGcPauseMs` comment to mention "runtime regression defence", not "older runtimes"

**Priority: 13.** **Universal: yes.** **Category: documentation accuracy.**

**What.** The comment ".NET 7+; defensive for older runtimes" implies we might run on .NET 6. We don't — tModLoader pins .NET 8. The catch is genuinely defensive but the rationale is "future regression / unexpected throw" not "supporting older runtimes".

**Risk.** Zero.

### §5.N — Cluster-cause tiebreaker

**Priority: 14.** **Universal: yes.** **Category: classifier correctness.**

**What.** Add a tiebreaker to `FlushCluster.DominantCause` — when two causes tie on frequency, pick the one with higher cumulative wall-ms. Track `double[] _clusterCauseWallMs = new double[8]` alongside the counts; on tie, `if (sumWallMs[a] > sumWallMs[b]) a wins`.

**Expected delta.** Zero CPU. More honest dominant-cause output on mixed clusters.

**Verification.** Test mixed-cause cluster ties.

**Risk.** Zero.

### §5.O — Comment why we sum smoothed values across cluster contributors

**Priority: 15.** **Universal: yes.** **Category: documentation.**

**What.** A 3-line comment in `FlushCluster` near `_clusterContribCost` documenting that we're summing rolling-averages across stalls, that the ordinal output is correct but the magnitude is inflated by stall count, and that the magnitude is intentionally not written to the DB.

**Risk.** Zero.

---

## 6. Cross-system dependencies

### 6.1 Upstream

- `MetricCollector.BeginTick` (line 326) — the single caller. Changes to detector signature ripple here.
- `MetricCollector.BeginTick` reads `Stopwatch.GetTimestamp`, `GcPauseMilliseconds()`, `DateTimeOffset.UtcNow`, `ProfilerFocusProbe.Read`. §5.J (shared snapshot) and §5.F (defer UnixMs) both modify this call site.
- `Baseline.TickPeriodMsMedian` is read every detector call. Cheap field read. No coupling to optimise.
- `Baseline.IsCalibrated` is read every detector call. Cheap field read.

### 6.2 Downstream

- `SessionRecorder.DrainStalls` consumes `collector.Stalls` (the cached view over the ring). §5.H, §5.I, §5.L, §5.N, §5.O all target the recorder.
- `StallStream.Apply` upserts to `db.Stalls`. §5.G (byte-encoded enums) changes the row shape and therefore the stream's payload type.
- `StallClusterStream` upserts to `db.StallClusters`. Same §5.G impact.
- `SessionSummaryLogger.Write` (at world unload) reads stall + cluster aggregates. Shape changes from §5.G need a read-side projection or a schema-versioned reader.
- The five chat commands (`/profiler-stalls`, `/profiler-summary`, etc.) query the same rows. Same projection needed.
- `Mod.Logger` inline narration in `DrainStalls` (line 346-350) uses `s.Cause.ToString()`. After §5.I the enum-to-string conversion still works (cached by .NET 8); no change needed there.

### 6.3 Compatibility map

| Change | Detector signature | Row schema | Cluster schema | Stream payload | Logger output |
|---|---|---|---|---|---|
| §5.A | (Process removal, internal) | — | — | — | — |
| §5.B | params type change | — | — | — | — |
| §5.C | (inlining) | — | — | — | — |
| §5.E | (classifier path) | — | — | — | new cause value emitted |
| §5.F | UnixMs deferred | — | — | — | — |
| §5.G | — | schema v3 | schema v2 | byte fields | — |
| §5.H | — | TopContributors as array | — | — | — |
| §5.I | (recorder only) | — | — | — | — |
| §5.J | snapshot param added | — | — | — | — |
| §5.L | — | — | SpanMs corrected | — | — |
| §5.N | — | — | — | — | dominant cause output |

Schema bumps in §5.G / §5.H are coordinated; one schema version bump can cover both.

---

## 7. Prioritised order

In the order they should land, with rationale:

1. **§5.A — Environment.CpuUsage.** Highest universal CPU saving, zero behaviour risk, simple change. Pure win. **Land first.**
2. **§5.J — Shared GcCounterSnapshot.** Touches the same hot path as §5.A; rolling them together is natural. Removes the duplicate GC counter reads documented in §1.22.A.
3. **§5.F — Defer UnixMs.** Same hot path; one more lazy-eval lever.
4. **§5.B — `double[]` instead of `IReadOnlyList<double>`.** Signature change on the detector; cluster all signature changes together.
5. **§5.C — AggressiveInlining on FocusProbe.** Trivial one-line change.
6. **§5.K — Truth-table tests.** Pin classifier behaviour BEFORE §5.E adds a new branch. Tests-first discipline.
7. **§5.E — WorldLoad cause emission.** Behavioural change; should land with tests in place.
8. **§5.I — `int[8]` for cluster cause counts.** Recorder side; pairs naturally with §5.N.
9. **§5.N — Cluster-cause tiebreaker.** Recorder side; pairs with §5.I.
10. **§5.L — Fix EndUnixMs bookkeeping.** Cosmetic but real; cheap to land.
11. **§5.O — Doc comment.** Free.
12. **§5.M — Doc comment.** Free.
13. **§5.G — Persist byte-encoded enums.** Schema bump; coordinate with §5.H.
14. **§5.H — Drop `List<>` allocation per stall.** Schema-shape change; together with §5.G makes one cohesive v3 schema bump.

The schema bump (§5.G + §5.H) is intentionally last so the upstream
detector + recorder changes are pinned by tests first.

### 7.1 Expected cumulative delta on the per-tick path

| Change | Linux saving/sec at 60 Hz | macOS saving/sec at 60 Hz |
|---|---|---|
| §5.A | -240 to -540 µs | -60 to -120 µs |
| §5.J | -9 to -15 µs | -9 to -15 µs |
| §5.F | -58 µs (net) | -55 µs (net) |
| §5.B | -0.5 µs | -0.5 µs |
| §5.C | -0.3 µs | -0.3 µs |
| **Sum** | **-308 to -614 µs/sec** | **-125 to -191 µs/sec** |

The per-tick detector cost on Linux drops by roughly half. On macOS the
saving is smaller but still real. None of these changes reduce capture
fidelity, classifier surface, or cluster aggregation. Strictly more
efficient at strictly the same behaviour.

### 7.2 Expected cumulative delta on storage

| Change | Saving per session | Saving per 100 sessions |
|---|---|---|
| §5.G enum bytes | -3.6 KB | -360 KB |
| §5.H drop List + Name | -5 to -10 KB | -500 KB to -1 MB |
| **Sum** | **-8.6 to -13.6 KB** | **-860 KB to -1.36 MB** |

Total stall-path contribution to the 1064 KB → < 600 KB target: ~10 KB
per session (roughly 1% of the total goal, proportional to the stall
path's share of the captured surfaces). The bulk of the storage target
will come from the event-stream paths (damage, NPC spawn, item created,
buff edges) which are higher-volume.

---

## 8. References

### Microsoft Learn (.NET 8 API surface)

- [GC.GetTotalPauseDuration Method](https://learn.microsoft.com/en-us/dotnet/api/system.gc.gettotalpauseduration?view=net-8.0)
- [GC.CollectionCount Method](https://learn.microsoft.com/en-us/dotnet/api/system.gc.collectioncount?view=net-8.0)
- [GC.GetTotalMemory Method](https://learn.microsoft.com/en-us/dotnet/api/system.gc.gettotalmemory?view=net-8.0)
- [Process.TotalProcessorTime Property](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.totalprocessortime?view=net-8.0)
- [Process.Refresh Method](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.refresh?view=net-8.0)
- [Process.GetCurrentProcess Method](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.getcurrentprocess?view=net-8.0)
- [Environment.CpuUsage Property](https://learn.microsoft.com/en-us/dotnet/api/system.environment.cpuusage?view=net-8.0)
- [DateTimeOffset.UtcNow Property](https://learn.microsoft.com/en-us/dotnet/api/system.datetimeoffset.utcnow?view=net-8.0)
- [.NET runtime metrics](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/built-in-metrics-runtime)
- [Garbage collector config settings](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/garbage-collector)
- [Performance Improvements in .NET 8 (devblogs)](https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-8/)

### dotnet/runtime source

- [GC.CoreCLR.cs - GC.GetTotalPauseDuration source](https://github.com/dotnet/runtime/blob/main/src/coreclr/System.Private.CoreLib/src/System/GC.CoreCLR.cs)
- [Process.OSX.cs - macOS implementation of TotalProcessorTime](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Diagnostics.Process/src/System/Diagnostics/Process.OSX.cs)
- [Environment.cs - CpuUsage implementation](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Environment.CoreCLR.cs)

### Issues + proposals on the dotnet/runtime tracker

- [API proposal: Expose total paused duration in GC (#66036)](https://github.com/dotnet/runtime/issues/66036) — origin of `GC.GetTotalPauseDuration`.
- [API proposal: Environment.CpuUsage (#60281)](https://github.com/dotnet/runtime/issues/60281) — origin of `Environment.CpuUsage`.
- [TotalProcessorTime returns strange values on macOS (#29527)](https://github.com/dotnet/runtime/issues/29527) — macOS counter behaviour.
- [Switch OS X process info to sysctl (#15405)](https://github.com/dotnet/runtime/issues/15405) — historical context on macOS process info path.
- [.NET 7+8 GC regression analysis (#95191)](https://github.com/dotnet/runtime/issues/95191) — for reference on GC pause behaviour in our target runtime.

### FNA / SDL2 / Terraria

- [FNA - SDL2_FNAPlatform.cs](https://github.com/FNA-XNA/FNA/blob/master/src/FNAPlatform/SDL2_FNAPlatform.cs) — where `Game.IsActive` and equivalently `Main.hasFocus` are updated.
- [FNA - Game.cs](https://github.com/FNA-XNA/FNA/blob/master/src/Game.cs) — event-loop driver.
- [SDL2 - SDL_WindowEventID](https://wiki.libsdl.org/SDL2/SDL_WindowEventID) — `FOCUS_GAINED` / `FOCUS_LOST` events.
- [SDL2 - Managing Window Input Focus (study guide)](https://www.studyplan.dev/sdl2/sdl2-input-focus)
- [tModLoader Issue #4941 - Apple Silicon fullscreen crash](https://github.com/tModLoader/tModLoader/issues/4941)
- [tModLoader Issue #4900 - .NET 8 runtime detection](https://github.com/tModLoader/tModLoader/issues/4900)
- [tModLoader and .NET 8 (CubeCoders thread)](https://discourse.cubecoders.com/t/tmodloader-and-net-8/12172) — confirms .NET 8 is the pinned runtime.

### Third-party performance context

- [Analysing Pause times in the .NET GC (Matt Warren)](https://mattwarren.org/2017/01/13/Analysing-Pause-times-in-the-.NET-GC/) — historical context on GC pause measurement.
- [.NET 6 vs 4.8 GC stats (nietras)](https://nietras.com/2021/11/26/dotnet-6-vs-4-8-gc-stats/) — pause-time evolution.
- [Getting CPU usage % in .NET Core (Jack Wild, Medium)](https://medium.com/@jackwild/getting-cpu-usage-in-net-core-7ef825831b8b) — pattern reference for `TotalProcessorTime` consumption.

### Internal project context

- `Profiling/StallDetector.cs` — primary subject.
- `Profiling/ProfilerFocusProbe.cs` — auxiliary focus signal.
- `Profiling/Persistence/Streams/StallStream.cs` — write path.
- `Profiling/Persistence/Streams/StallClusterStream.cs` — cluster write path.
- `Profiling/Persistence/Records/StallEventRow.cs` — row schema.
- `Profiling/Persistence/Records/StallClusterRow.cs` — cluster schema.
- `Profiling/Persistence/SessionRecorder.cs` — `DrainStalls`, `FlushCluster`.
- `Profiling/MetricCollector.cs:307-326` — the single live `OnBeginTick` call site.
- `Profiling/Baseline.cs` — the calibration source consumed by the detector.
- `context/perf-pass/baseline.md` — v0.5 baseline measurements.
- `context/notes/decisions.md` 2026-05-20 entries (v0.4 cluster-shape, v0.5 MainThreadFreeze) — recent narrative drift / fixes leading to current state.
- `context/systems/metric-collection.md` — the per-tick frame engine doc.
- `context/systems/spike-detection.md` — sibling detector for context on the design idiom.
- `context/notes/philosophy.md` — universal-modlist, capture-over-storage, descriptive-not-normative posture.

---

*This file is the dossier for the stall-detection slice of the v0.6 perf
pass. Optimisations land in the order set in §7. The cumulative target
(§7.1 + §7.2) is the verification contract. Every change preserves every
cause classification today, every cluster aggregation today, every row
written today; every change is universal across modlists and platforms;
every change keeps the per-tick path zero-allocation.*
