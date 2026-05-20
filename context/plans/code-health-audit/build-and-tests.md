# Build And Test Infrastructure — Code Health Findings

**Systems covered:** `PerformanceProfiler.csproj`, `build.txt`, repository test surface, build baseline  
**Finding count:** 2 findings (0 critical, 1 high, 1 medium, 0 low)

## Test Coverage Gaps

### Create A Non-Shipping C# Test Harness Before More Pure Logic Accumulates
- [ ] Add a test project that can exercise pure profiler logic without being packed into the `.tmod`

**Category:** Test Coverage Gaps  
**Severity:** High  
**Effort:** Medium  
**Behavioural Impact:** None

**Location:**
- `PerformanceProfiler.csproj:1-9` — mod project only, targeting `net8.0`
- `build.txt:4` — build ignore excludes docs/context/build artefacts but no test directory is present
- `context/plans/code-health-audit/PASS-1-CHECKPOINT.md:17-23` — test detector found no recognised test stack

**Current State:**
There is no automated test project. The codebase now contains pure logic that should be testable without a running game: insights scoring, report shaping, ring buffers, attribution arrays, ranking, and formatting contracts. The audit could not write diagnostic C# tests safely because adding `.cs` files inside the mod source can make them candidates for tModLoader packaging unless the packaging surface is explicitly handled.

**Proposed Change:**
Add a non-shipping test harness that runs through `dotnet test` and is excluded from `.tmod` packaging by design. The implementation should first decide the safe location/exclusion rule, then add minimal tests for pure logic surfaced in this audit: insights ranking, confidence promotion, session schema shape, and coverage projection.

**Justification:**
Microsoft documents `dotnet test` as the standard .NET test runner surface. This project’s own context already calls ring buffer, metric structures, JSON schema, modlist fingerprinting, and insights logic unit-testable. Without a harness, every future cleanup touches critical surfaces with only manual build/game verification.

**Expected Benefit:**
Unlocks diagnostic tests for the findings in this audit and creates a regression baseline for future refactors without requiring tModLoader to run.

**Impact Assessment:**
No runtime behaviour change if tests are excluded from the `.tmod` package. The key implementation constraint is to avoid shipping test code.

## Known Issues And Active Risks

### Treat The Current `.tmod` Packaging Failure As An Environment Blocker
- [ ] Re-run `dotnet msbuild` after tModLoader releases the locked `.tmod`, and record the clean package baseline

**Category:** Known Issues and Active Risks  
**Severity:** Medium  
**Effort:** Trivial  
**Behavioural Impact:** None

**Location:**
- `context/plans/code-health-audit/PASS-1-CHECKPOINT.md:21-23` — DLL compile succeeded, package write failed with TML003 due locked `.tmod`

**Current State:**
The Pass-1 build reached `bin/Debug/net8.0/PerformanceProfiler.dll`, then packaging failed because `/Users/atacanercetinkaya/Library/Application Support/Terraria/tModLoader/Mods/PerformanceProfiler.tmod` was locked by a running tModLoader/mod instance.

**Proposed Change:**
Close tModLoader or disable the mod in-game, then rerun `dotnet msbuild` to capture a clean packaging baseline. Keep this as an environment known issue until a clean package run is recorded.

**Justification:**
The compile signal is healthy, but the package gate is part of the mod’s actual development loop. Leaving the baseline in a locked state makes future build failures harder to classify.

**Expected Benefit:**
Separates source/build regressions from a known file-lock condition.

**Impact Assessment:**
No code or runtime behaviour change. This is a verification/environment action.
