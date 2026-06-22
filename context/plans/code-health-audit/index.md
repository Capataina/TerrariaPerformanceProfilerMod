# Code Health Audit

**Date:** 2026-06-22
**Scope:** full repository, with the deepest Pass-2 dive on the **hook-install RAM path** (the live investigation's priority), plus a repository-wide sweep for build/test health, duplicate-using lint, documentation rot, and modularisation.
**Status:** active (findings ready to implement)
**Supersedes:** the 2026-05-20/21 audit previously in this folder (plan-lifecycle regeneration against current code; the prior run's per-system finding files for hook-instrumentation / insights / overlay / persistence were removed as this run does not re-audit those systems at depth — the prior critical fixes already landed per `context/notes/decisions.md`).

## Summary

The audit confirmed, with **decompiled ground-truth evidence from the shipped MonoMod
binaries** (`RuntimeDetour 25.3.2`, `Utils 25.0.10`), that the ~8.2 GB tModLoader
attributes to the profiler on a 100-mod / 152,310-hook stack is **mostly genuinely
retained state, not a measurement illusion**: per hooked method, MonoMod keeps two Cecil
method bodies alive for the hook's applied lifetime (the `SourceCloneIl` DMD and a
read-only `LastContext` ILContext), reclaimed only on mod unload. The audit also found a
real **measurement gap** — `ProfilerSelfHealth.MarkInstallEnd` samples the unstable
`GetTotalMemory(false)` form with no collection, while `MarkInstallStart` forces a Gen2;
a diagnostic test the audit wrote shows this swings the reported number by ~50%. Beyond
the priority area, the **pure-logic test suite does not build** (all 24 linked-source
paths stale after the v0.10/v0.12 reorganisation), plus 18 CS0105 duplicate-`using`
warnings and three documentation-rot items. 8 certain findings + 2 potential issues.
Production source was not edited; one diagnostic test (2 passing cases) was added.

The single genuinely-free, coverage-preserving, in-repo win is **F2** (force a Gen2 in
`MarkInstallEnd`): trivial, makes the install-delta honest and repeatable, no feature
removed. The structural retained-memory reduction (reclaiming the read-only ILContext)
is real but needs MonoMod upstream cooperation or risky reflection, so it is filed as
**P-1** (potential) rather than presented as free.

## What I Did Not Do

| Obligation | Status | Evidence |
|------------|--------|----------|
| Pre-Pass-1 front-loaded WebSearch | done | Query "code health audit patterns for C# .NET MonoMod IL hook instrumentation profiler memory"; URLs in `obligation-evidence-map.md` front-load row; recorded before any reference read. |
| Mandatory reference loading (all 10) | done | Read all `code-health-audit/references/*.md` in load order before Pass 1. |
| Pass-1 checkpoint written before Pass 2 | done | `PASS-1-CHECKPOINT.md`. |
| Project test/build baseline captured | done | `dotnet test Tests/PerformanceProfiler.Tests.csproj` → **build fails, CS2001 ×7, 24/24 paths stale**; recorded in `PASS-1-CHECKPOINT.md` §3. |
| Pre-existing test failures recorded as Known Issues | done | The broken build IS the finding → F4 (`build-and-tests.md`). |
| Research per substantive system | done | 6 system rows in `obligation-evidence-map.md`, each with query + URL + mode; mechanical lint / doc-vs-code systems recorded as reasoned omissions with justification. |
| Research-mode variety (≥3 modes) | done | Modes 1, 2, 3 all represented; distribution table at top of `obligation-evidence-map.md` (lint confirms `[1, 2, 3]`). |
| Diagnostic tests written where moderate→high upgrade applies | done | `Tests/HookInstallRetentionDiagnostics.cs` (2 cases, both passing) + **decompilation of the shipped MonoMod binaries** (stronger than any synthetic test) for the F1 retention root cause. Referenced from F1/F2. |
| Confidence upgrade pathway attempted before moderate/low findings | done | F1's retention claim upgraded from "suspected Cecil retention" (decisions.md:201) to confirmed via decompilation; F2 upgraded via the passing diagnostic test. The not-free structural reclaim is filed as P-1, not a lukewarm certain finding. |
| Pass-2 systems-audited checkpoint written | done | `PASS-2-SYSTEMS-AUDITED.md`. |
| Potential-issues sweep ran (Pass 2.5) | done | `potential-issues.md` — 2 entries (P-1 ILContext reclaim, P-2 denominator mismatch), each with locations + observation + reasoning + suggested investigation + why-not-certain. |
| Certain-set non-regression check | done | No finding demoted to potential to dodge proof; see `PASS-2-SYSTEMS-AUDITED.md` final section. |
| Modularisation candidate list enumerated in Pass 1 | done | `PASS-1-CHECKPOINT.md` §4 — all compiled files ≥550 LOC + top-decile. |
| Every modularisation candidate has a verdict | done | `PASS-2-SYSTEMS-AUDITED.md` — 13 leave-as-is (each justified per §3), generated/archived groups not-applicable; 0 "out-of-scope" verdicts. |
| Data Layout / Memory Access analysis per audited system | done | Per-system applicability table in `PASS-2-SYSTEMS-AUDITED.md`; the priority system IS a Data-Layout finding (F1). |
| Production source not modified | done | `git status` shows only `context/plans/code-health-audit/**` + `Tests/HookInstallRetentionDiagnostics.cs` + `Tests/Diagnostics/**` (test infrastructure carve-out) + the pre-existing untouched `context/notes/modlist-pre-upgrade-2026-06-22.md`. |
| Scripts: Python/Rust path vs C# fallback | done | Project is C# (outside `scripts/*.py` coverage). Fallback path (`find`+`wc`, `grep`, `dotnet test`) recorded as reasoned omission in `obligation-evidence-map.md` §"Language-coverage note". `evidence_map_lint.py` + `finalize_audit.py` (language-agnostic) run normally. |
| Evidence-map lint exit 0 before final output | done | `evidence_map_lint.py` → "lint: clean", 7 rows, modes [1,2,3], exit 0. |

## Findings Overview

| File | Concern | Critical | High | Medium | Low | Total |
|------|---------|----------|------|--------|-----|-------|
| [hook-install-ram.md](hook-install-ram.md) | Hook-install RAM (priority) | 0 | 2 | 1 | 0 | 3 |
| [build-and-tests.md](build-and-tests.md) | Build / test infrastructure | 0 | 1 | 1 | 0 | 2 |
| [cross-cutting.md](cross-cutting.md) | Project-wide hygiene / doc rot | 0 | 0 | 0 | 3 | 3 |
| [potential-issues.md](potential-issues.md) | Suspicions (separate bar) | — | — | — | — | 2 |
| **Total certain** | | **0** | **3** | **2** | **3** | **8** |

## Priority Actions

1. **[HIGH]** Force a Gen2 in `MarkInstallEnd` so the install-delta / KB-per-hook / severity are honest and repeatable (the only free in-repo win in the priority area) — [hook-install-ram.md#f2](hook-install-ram.md#f2).
2. **[HIGH]** Repair the test project's 24 stale `<Compile Include>` paths so the pure-logic suite builds again — [build-and-tests.md#f4](build-and-tests.md#f4) (path map in [#f5](build-and-tests.md#f5)).
3. **[HIGH]** Acknowledge / act on the confirmed per-hook Cecil retention root cause; the structural reclaim is P-1 (needs MonoMod cooperation) — [hook-install-ram.md#f1](hook-install-ram.md#f1).
4. **[MED]** Reconcile README ~36 KB/hook vs code baseline 36 KB vs observed ~60 KB/hook (honesty contract) — [hook-install-ram.md#f3](hook-install-ram.md#f3).
5. **[LOW]** Remove 18 CS0105 duplicate-`using` lines (exact line pairs listed) — [cross-cutting.md#f6](cross-cutting.md#f6).
6. **[LOW]** Fix stale `conventions.md` #13 (AggressiveInlining IS used) and the phantom `_TempAllocBench` doc reference — [cross-cutting.md#f7](cross-cutting.md#f7), [#f8](cross-cutting.md#f8).

## By Category

- Data Layout and Memory Access Patterns: 1 (F1) — the priority finding.
- Known Issues and Active Risks: 3 (F2, F4, F5).
- Documentation Rot: 3 (F3, F7, F8).
- Inconsistent Patterns: 1 (F6).
- Potential issues (separate bar): 2 (P-1, P-2).

## Diagnostic tests written by this audit

| Test | What it asserts | Result |
|------|-----------------|--------|
| `Tests/HookInstallRetentionDiagnostics.cs` → `ForcedCollection_IsRepeatable` | Two consecutive `GetTotalMemory(true)` samples on a stable set agree (drift ≤ 1 MB) | PASS (0 KB drift) |
| `Tests/HookInstallRetentionDiagnostics.cs` → `ForcedCollection_GivesStableRetainedMeasurement` | `GetTotalMemory(false)` vs `(true)` differ materially (methodology spread); forced form is a sound retained lower bound | PASS (24.3 MB vs 32.2 MB on 16 MB set; 7.9 MB spread) |

Built/run via the self-contained `Tests/Diagnostics/HookInstallRetentionDiagnostics.csproj`
(separate from the broken main test project per F4). The fixture's doc-comment honestly
records that an over-report claim it first attempted did NOT reproduce synthetically, and
narrows to the property it can prove. Decompiled binaries (`/tmp/dm.cs`, `/tmp/dmd.cs`)
back the F1 retention claim with ground truth.

## Note for the implementing engineer

Two concurrent agents were running against this repo during this audit. This run touched
**only** `context/plans/code-health-audit/**` and the two new test files; it did not edit
any production `.cs` (including the install-path files `ILHookInterceptor.cs`,
`HookBackend.cs`, `ProfilerSelfHealth.cs`, which the main session was investigating live)
nor the rest of `context/` (architecture/systems/_Overview owned by a concurrent
`upkeep-context` agent), nor the untouched `context/notes/modlist-pre-upgrade-2026-06-22.md`.

## Audit Termination Receipt

> Note: the receipt's "Counts" line is `finalize_audit.py`'s own coarse regex heuristic
> (it scans for a couple of marker strings); the authoritative counts are 8 certain
> findings + 2 potential issues, and 13 leave-as-is / generated-archived-not-applicable
> modularisation verdicts, as detailed in `PASS-2-SYSTEMS-AUDITED.md`. The receipt's
> load-bearing content is the verbatim lint result proving the evidence map passed at the
> terminal moment.

```
# Audit Termination Receipt — generated by finalize_audit.py

_Generated: 2026-06-22T13:26:45Z_
_Audit folder: `/Users/atacanercetinkaya/Library/Application Support/Terraria/tModLoader/ModSources/PerformanceProfiler/context/plans/code-health-audit`_

## Lint

- Command: `python3 scripts/evidence_map_lint.py /Users/atacanercetinkaya/Library/Application Support/Terraria/tModLoader/ModSources/PerformanceProfiler/context/plans/code-health-audit/obligation-evidence-map.md`
- Exit code: 0
- Output (verbatim):

\`\`\`
# Evidence map lint: clean

_Checked: `/Users/atacanercetinkaya/Library/Application Support/Terraria/tModLoader/ModSources/PerformanceProfiler/context/plans/code-health-audit/obligation-evidence-map.md`_

Rows inspected: 7
Research modes detected: [1, 2, 3]
\`\`\`

## Counts

- Certain findings: 1
- Potential issues: 2
- Modularisation verdicts: split-recommended=1, leave-as-is=1, not-applicable=0

## Audit folder contents

\`\`\`
- PASS-1-CHECKPOINT.md
- PASS-2-SYSTEMS-AUDITED.md
- build-and-tests.md
- cross-cutting.md
- hook-install-ram.md
- index.md
- obligation-evidence-map.md
- potential-issues.md
\`\`\`

_Paste this entire block verbatim into `index.md` under the section `## Audit Termination Receipt`. The Quality Checklist requires its presence; absence of this section is a checklist failure._
```
