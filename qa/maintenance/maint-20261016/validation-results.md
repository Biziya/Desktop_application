# Validation Results — maint-20261016

**Environment.**
- Windows 11 Pro 10.0.26200, .NET SDK 10.0.402.
- Built through `Tests/regression/Invoke-Regression.ps1 -Full -Include Timing -WarnAsError`, in an isolated copy under `%TEMP%\ct007-maint`, with an offline restore from the existing package cache.
- The build had 0 warnings.

**Scope.** The full suite, because test code and fixtures changed.
- Not runnable here: the Sql category (no LocalDB) and the E2E category (no dedicated account). Their 5 tests are reported as skipped.
- E2E code changes were verified to compile only.

| Run | Passed | Failed | Skipped | Blocked | Leftover temp folders | Evidence |
|---|---|---|---|---|---|---|
| Phase 4 final (baseline) | 100 | 2 | 5 | 0 | ≈2 (not reported then) | [run-final-results.csv](../../execution/evidence/run-final-results.csv) |
| maint run-01 (before the folder-sweep fix) | 100 | 2 | 5 | 0 | 3 (1 reported) | [run-01](artifacts/run-01/) |
| maint run-02 (final) | **100** | **2** | **5** | 0 | **1 (reported: SUS-09)** | [run-02](artifacts/run-02/) |

**Comparison with the baseline.**
- **Identical outcome for every test method** in both maintenance runs.
- **Failures:** the same two known defects, FE-005 (DEF-001) and BE-030 (DEF-002), with the same `InvalidOperationException`. No new failure, no resolved failure.
- **FE-008** (strengthened) passed in both runs.
- **BE-002 / INT-002** passed in both runs: 1.2 s and 1.1 s, down from 3.1 s each. They still get a real `SqlException` and assert the 503 mapping and the absence of secrets.

**Confirmations.**
- **Production unchanged:** `git diff` of `Frontend/`, `Backend/`, `SmokeTests/`, `tools/`, `CT007.slnx` and `.github/` is empty.
- **Only test files changed:** all 11 modified files are under `Tests/`, and the only new folder is `qa/maintenance/`.
- **No assertion weakened, no test removed, no test skipped or disabled.**

**Limitations.**
- **No new GitHub CI run;** the workflow is still only locally validated.
- **E2E and Sql** are unexecuted.
- **Durations are from single runs per configuration.** INT-008 varies by about ±2 s between runs.
