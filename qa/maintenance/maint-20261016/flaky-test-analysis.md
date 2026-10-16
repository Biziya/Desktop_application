# Flaky Test Analysis — maint-20261016

**Evidence:** outcomes and durations of every recorded run.

| Run | Source |
|---|---|
| P4 run-0 | [`run-0-console-before-helper-fixes.log`](../../execution/evidence/run-0-console-before-helper-fixes.log) |
| P4 run-2 | [`run-2-console-before-INT-008-fix.log`](../../execution/evidence/run-2-console-before-INT-008-fix.log) |
| P4 final | [`run-final-results.csv`](../../execution/evidence/run-final-results.csv) |
| P6 | [`reg-20261016-01`](../../regression/reg-20261016-01/artifacts/results.csv) |
| P7 | [`qa/ci/validation`](../../ci/validation/results.csv) |
| P8 run-01 and run-02 | [`artifacts/`](artifacts/) |

The P4 runs 1 and 3 are summarised in the Phase 4 report; their logs were not kept.

## Tests whose outcome varied between runs

| Test | Pattern | Cause | Classification | Status |
|---|---|---|---|---|
| **INT-008** ConcurrentWriter | Passed P4 run 1; failed P4 runs 2 and 3 at capture 8 ("samples stored: expected 5, actual 0"); passed ×3 after the fix and in every run since (P4 final, P6, P7, P8 ×2) | The test read stored samples before the asynchronous save finished (a race) | **Intermittent, test defect: fixed** in Phase 4 (bounded wait of up to 5 s) | Stable: 7 consecutive passes |
| BE-027, BE-037, INT-007 | Failed only in P4 run-0 | Test defects T1 (hash without file sharing) and T2 (fixture with foreign keys on) | Deterministic test defects, fixed in Phase 4. Not flaky | Pass in all later runs |
| 70 SQLite-using tests | Failed only in one P7 local run (`TypeInitializationException`) | **Environment:** SQLite's native DLL path at or above about 252 characters | Environment failure, not flaky. Guarded in `Invoke-Regression.ps1` | — |
| FE-006, 008, 023, 028, 048; INT-003, 004, 008, 016 | SKIP in P4 run-0 | `Timing` category not included in that run | Not a failure | — |

## Timing-sensitive tests (no failure observed; watch list)

| Test | Risk | Mitigation already in place |
|---|---|---|
| FE-028 | A 100 ms window caught by polling, 20 times | Polling every 5 ms, generous `Eventually`. Same outcome in 6 of 6 full runs |
| FE-006, FE-023 | Probe-count ranges in wall-clock windows | Loose bounds (5–15 probes per second; rate ≤ 1.5× + 2) |
| FE-048 | Close-timeout window 4–7 s for a 5 s timeout | ±1–2 s tolerance |
| INT-008 | Real-time 1 Hz writer | 4–5 samples accepted per window; bounded save wait |

None of these failed in the recorded runs, so none is classified as flaky. They stay in the `Timing` category, which runs in CI on every run.

## Corrections in this phase

None were needed for flakiness. The cleanup reliability fix (folders left behind, below) changes no test outcome.

## Remaining reliability issue (not a flaky test)

**One temp folder per run cannot be deleted** (`corrupt.db`, created by BE-040). The app's `DrillholeCache.Open()` leaks the SQLite connection when `CREATE TABLE` throws on a corrupt file (see SUS-09 in [test-quality / coverage](coverage-review.md)). The lock survives a forced GC, so it can't be released from test code. The runner now **reports** it ("1 temp folder(s) could not be deleted") instead of leaving it silently.
