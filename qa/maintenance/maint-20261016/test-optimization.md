# Test Optimization — maint-20261016

**Measurements:** per-test durations from `results.csv`. The baseline is the Phase 4 final run; this phase is `run-01` and `run-02`. The machine and SDK are the same; the suite is the full set, with Timing included.

| | Phase 4 final | maint run-01 | maint run-02 |
|---|---|---|---|
| Sum of test durations | 106.0 s | 104.6 s | 107.4 s |

Totals vary by a few seconds from run to run (INT-008 alone varies by about ±2 s), so the measurable gain is per test (below), not in the total.

## Where the time goes (Phase 4 final)

| Test | Time | Why | Reducible? |
|---|---|---|---|
| INT-008 ConcurrentWriter | 54 s (51 %) | 10 real 5 s captures against a 1 Hz writer, plus the up-to-1.5 s final read | Only by weakening the planned scenario: fewer or shorter captures mean less concurrency evidence. **Kept.** It is the only automated check of capture during live Gamma Guard writes |
| FE-048 CloseDuringWork | 7.5 s | Asserts the app's 1 s save wait and its **5 s** close timeout | No: the timeout under test is 5 s |
| FE-028 StopWhileProcessing | 4.5 s | 20 repetitions of a 100 ms race | Fewer repetitions would weaken it. Kept |
| INT-003 OfflineStartAndRecovery | 3.2 s | Real offline polling and recovery | No |
| **BE-002 SqlFailureIs503** | 3.1 s | `Connect Timeout=3` against a refused local port | **Yes → 1.2 s** |
| **INT-002 ServiceUnavailablePropagation** | 3.1 s | Same connection string | **Yes → 1.1 s** |
| INT-009, BE-027, BE-028, INT-016 | 2.1–2.5 s each | The app's own 2 s SQLite busy timeout | No: it is the behaviour under test |

## Applied

| Change | Effect (measured) |
|---|---|
| The shared refused-connection string uses `Connect Timeout=1` (a real `SqlException` is still produced; the 503 mapping is still asserted) | BE-002 3.1 → 1.2 s; INT-002 3.1 → 1.1 s; **about 4 s less per full run** |

The total did not drop measurably. The roughly 4 s saved is within the run-to-run variation of the whole suite (104.6–107.4 s).

## Considered and not applied

- **Parallel test execution: not safe.** Tests share the WPF dispatcher thread, set `CultureInfo.CurrentCulture`, and one sets a process environment variable. The Timing tests measure wall-clock behaviour. The runner stays sequential.
- **Excluding Timing or INT-008 from PR runs:** rejected. INT-008 is in the critical set, and the Phase 7 policy is not to skip critical tests to save time.
- **Skipping the isolated copy in CI** (`robocopy` of `Frontend/`, `Backend/`, `Tests/`): it saves only seconds and would add a second code path. Kept.
- **Restore:** it is already cached in CI (`actions/cache` keyed on `*.csproj`), and done offline from the local cache when run locally.

## Remaining opportunities

- **Compile time** dominates small selected runs: about 1–2 min to build Frontend, Backend and tests from a clean copy. Persisting `obj/` between CI runs (incremental build cache) could cut this; it is not done, to keep CI simple.
- **INT-008:** if the team accepts a lighter scenario, 5 captures instead of 10 would save about 27 s. That needs a Phase 2 plan change (it is a coverage decision, not a test-code one).
