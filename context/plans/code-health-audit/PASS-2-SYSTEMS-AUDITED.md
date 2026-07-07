# Pass 2 — Systems Audited (static snapshot, 2026-07-07)

Supersedes the 2026-06-25 snapshot for this run's scope.

| System | Research (query · mode) | Tests written | Findings | Confidence |
|--------|-------------------------|---------------|----------|------------|
| Metric Collector hot path | "EMA vs sliding window mean memory tradeoff" · mode 2/3 | none (tooling definitive: 0 unused-member warnings, grep-confirmed consumers, 170 tests green) | F1, F2, F3 (all verified-clean) | high |
| Hook install / RAM | "tModLoader MonoMod IL hook instrumentation overhead" · mode 1 | none (B4 reclaim diagnostic ships in production as the measurement) | F4 (verified, documented) | high |
| Cross-session persistence | reasoned omission (root cause from live crash stack) | none (runtime stacks + exhaustive predicate grep) | F5 (class closed) | high |

## Data Layout / Memory Access applicability

- **Metric Collector hot path:** analysed — the B1 EMA redesign IS the data-layout win (dropped a
  1.8 GB per-tick working set + two array walks). No further Data-Layout findings; the arrays are
  now flat, contiguous, and SIMD-walked.
- **Hook install / RAM:** analysed — the residual is object-graph retention (SourceCloneIl), not a
  layout issue; correctly retained (Invariant 4).
- **Cross-session persistence:** not applicable — I/O-bound LiteDB queries, no hot-loop layout surface.

## Certain-set non-regression

The certain-bar was not used as a downgrade dodge: the one suspicion (harvest fusion) is genuinely
judgment-dependent (new API + fourth unmeasured hot-path change on an unverified path), correctly
filed as potential, not demoted from certain. Zero findings moved from `findings.md` to
`potential-issues.md` to dodge a proof chain.

## Outcome

Clean bill of health. The rework introduced no dead code / orphans / unused members / per-tick
allocations; the 2026-06-25 free-findings backlog is fully implemented. No free findings to apply.
