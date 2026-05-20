# Persistence And Session Logging — Code Health Findings

**Systems covered:** `SessionLogWriter`, `ProfilerSystem` lifecycle logging, build metadata relevant to session artefacts  
**Finding count:** 4 findings (0 critical, 1 high, 3 medium, 0 low)

## Known Issues And Active Risks

### Isolate Session Logging Failures From World Lifecycle
- [ ] Wrap session-log create/tick/end failures so persistence can disable itself without interrupting profiler lifecycle

**Category:** Known Issues and Active Risks  
**Severity:** High  
**Effort:** Small  
**Behavioural Impact:** Possible (requires decision) — failure-path behaviour changes from exception propagation to logged persistence disablement.

**Location:**
- `Profiling/ProfilerSystem.cs:89-97` — `OnWorldLoad` calls `SessionLogWriter.Create()` directly
- `Profiling/ProfilerSystem.cs:100-115` — `OnWorldUnload` calls `_sessionLog?.End(Collector)` directly
- `Profiling/SessionLogWriter.cs:51-65` — create path performs directory creation, pruning, identity computation, and initial write

**Current State:**
Session logging I/O can escape `OnWorldLoad` or `OnWorldUnload`. Directory creation, pruning, and file writes are all performed before the world lifecycle method reports the profiler as armed/disarmed.

**Proposed Change:**
Catch `IOException`, `UnauthorizedAccessException`, and related filesystem exceptions around session logging lifecycle calls. Log one `Warn`, set the session log to null/disabled for that world, and keep metric collection/overlay lifecycle intact.

**Justification:**
The project’s failure posture is that instrumentation can decline to load or degrade, but it must not put gameplay at risk. Session reporting is an agent surface; losing it for one world is preferable to an unhandled lifecycle exception.

**Expected Benefit:**
Converts filesystem/path failures into a designed degradation path with clear `client.log` evidence.

**Impact Assessment:**
Successful sessions behave identically. Failure-path behaviour changes intentionally: persistence failure becomes logged disablement instead of exception propagation.

### Write Session Reports Through A Temp File And Same-Directory Replace
- [ ] Replace direct `File.WriteAllText` overwrites with temp-file write plus same-directory replace/move

**Category:** Known Issues and Active Risks  
**Severity:** Medium  
**Effort:** Small  
**Behavioural Impact:** Possible (requires decision) — failure-path output changes to preserve the previous complete report.

**Location:**
- `Profiling/SessionLogWriter.cs:103-129` — serialises the full report and overwrites `current-session.json` and final report with `File.WriteAllText`

**Current State:**
`WriteReport` serialises the entire report, then writes directly to the current-session file and final file. Microsoft documents `File.WriteAllText` as truncating and overwriting an existing file.

**Proposed Change:**
Write JSON to a temp file in the same directory, flush/close it, then replace the destination where supported (`File.Replace`) or move over the destination using the safest platform path available. Keep JSON schema and filenames unchanged.

**Justification:**
The report is an agent-readable diagnostic surface. A crash or process kill during direct overwrite can leave a truncated report; a temp+replace pattern narrows the corrupt-output window without changing successful output.

**Expected Benefit:**
Improves crash resilience for `current-session.json` and final session reports, especially while the game is closing.

**Impact Assessment:**
Successful JSON bytes and filenames stay the same. Failure semantics improve by retaining either the old complete report or the new complete report rather than a truncated file.

## Modularisation

### Split Pure Report Construction From File I/O In `SessionLogWriter`
- [ ] Keep `SessionLogWriter` as lifecycle/I/O owner and extract pure report/schema construction behind named internal types or helpers

**Category:** Modularisation  
**Severity:** Medium  
**Effort:** Medium  
**Behavioural Impact:** None

**Location:**
- `Profiling/SessionLogWriter.cs:22-642` — lifecycle, pruning, schema shaping, ranking, coverage projection, spike projection, hashing, and file writes share one file
- `Profiling/SessionLogWriter.cs:103-122` — top-level anonymous report object
- `Profiling/SessionLogWriter.cs:132-145`, `Profiling/SessionLogWriter.cs:368-381` — nested anonymous row shapes

**Current State:**
The file is a real modularisation candidate: it mixes path/lifecycle ownership with pure report construction. Anonymous object shapes make schema changes compile silently, and the pure parts are hard to test because they are surrounded by filesystem calls.

**Proposed Change:**
Extract report construction into named internal DTOs/helpers while leaving file paths, pruning, and write timing in `SessionLogWriter`. Preserve the JSON field names, schema version, ordering assumptions, and existing public surface.

**Justification:**
This is an internal split along an existing seam. It reduces review risk for persistence fixes and gives future tests a pure surface to exercise without tModLoader runtime or filesystem setup.

**Expected Benefit:**
Makes schema evolution and atomic-write fixes easier to implement safely. Reduces a 642-line mixed-responsibility file into a lifecycle writer plus report builder.

**Impact Assessment:**
No behaviour change if the extracted builder emits the same object graph/DTO field names. The JSON schema should be snapshot-tested once a non-shipping harness exists.

## Test Coverage Gaps

### Add Schema Snapshot Coverage For Agent-Readable Session Reports
- [ ] Once a non-shipping C# test harness exists, snapshot the schema-3 report shape generated from deterministic collector data

**Category:** Test Coverage Gaps  
**Severity:** Medium  
**Effort:** Small after test-harness work  
**Behavioural Impact:** None

**Location:**
- `Profiling/SessionLogWriter.cs:24-26` — schema version
- `Profiling/SessionLogWriter.cs:103-122` — top-level emitted fields
- `Profiling/SessionLogWriter.cs:254-267` — coverage block
- `Profiling/SessionLogWriter.cs:282-381` — mods/spikes/final report rows

**Current State:**
The agent-readable JSON schema has no automated guard. Field renames, missing blocks, or counter-source drift can compile cleanly and only surface when an agent reads `current-session.json` after gameplay.

**Proposed Change:**
After the build/test finding creates a safe test harness, add a deterministic schema snapshot or field-level assertions for `schema`, `identity`, `mods`, `coverage`, `timeline`, `spikes`, and `final`.

**Justification:**
The JSON report is one of the two required observability surfaces. It deserves tests because agents use it as execution evidence, not as a nice-to-have export.

**Expected Benefit:**
Prevents silent schema drift and gives future persistence rewrites a concrete compatibility target.

**Impact Assessment:**
Tests do not change runtime behaviour. They pin current intended output before refactors.
