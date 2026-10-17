# Testing Results Guide

How to read what the test runner (`Tests/CT007.Tests`) and the scripts produce. The formats below are taken from `TestRunner.cs`, `Invoke-Regression.ps1` and `Publish-TestResults.ps1`.

## 1. Terminal output

The runner prints one line per test, in ID order, and a summary as the last line:

```text
PASS FE-047 StatusAndPresentationTests.ColumnLayouts (311 ms)
FAIL FE-005 ApiClientTests.SettingsNonNumericValue (2 ms) — InvalidOperationException: The requested operation requires ...
SKIP BE-006 DrillholeApiTests.SqlProjection (0 ms) — category Sql not included
110 tests: 93 passed, 3 failed, 14 skipped.
```

| Part | Meaning |
|---|---|
| `PASS` / `FAIL` / `SKIP` | Outcome. The runner has no other outcomes |
| `FE-047` | Scenario ID. FE = frontend, BE = backend and local logic, INT = integration, E2E = desktop end-to-end. It is defined in [`qa/planning/test-inventory.md`](../planning/test-inventory.md) |
| `StatusAndPresentationTests.ColumnLayouts` | Test class and method: the file is `Tests/CT007.Tests/<area>/<class>.cs` |
| `(311 ms)` | Duration |
| Text after `—` | The failure message, or the reason for a skip. Depending on the console encoding, the dash may show as `-` or `?` |
| `N temp folder(s) could not be deleted` (on the summary line) | A file under `%TEMP%\ct007-tests\` was still locked. Cleanup only; it does not change any outcome |

- **Passed:** `PASS`, and the summary counts it.
- **Failed:** `FAIL` with a message, and the process exit code is 1 (`$LASTEXITCODE`).
- **Skipped:** `SKIP` with a reason. Skipped tests never count as passed.

| Skip reason | What it means |
|---|---|
| `category Timing not included` (also Sql, E2E) | An opt-in test; add `"--include=<Category>"` to run it |
| `Set CT007_TEST_SQL to a disposable local SQL Server database …` | Sql was included, but no LocalDB connection was configured |
| `Desktop E2E needs a dedicated Windows test account …` | E2E was included without `CT007_E2E_DEDICATED_ACCOUNT=1` |
| `Frontend.exe not found in …` | E2E needs `dotnet build Frontend\Frontend.csproj`, or `CT007_FRONTEND_DIR` |
| `A legacy 'CT007 Measurement Studio' data folder exists …` | E2E refuses to run on an account with old app data |

**"Blocked"** scenarios (planned but not implementable yet, for example FE-050, FE-051 and BE-003) do not appear in the output at all, because no test method exists for them. They are listed in [`qa/generation/test-implementation-status.md`](../generation/test-implementation-status.md).

**Timeouts** appear as `FAIL … Timed out after N s`. Each test has its own limit: 60 s by default, up to 240 s for E2E.

## 2. Is a failure new?

1. Look up the ID in [`Tests/ci/known-failures.json`](../../Tests/ci/known-failures.json). Today it lists FE-005 (DEF-001) and BE-030 (DEF-002).
2. A listed test that fails is a **known defect**: expected, and reported as a warning in CI.
3. Any other failure is **new**. Investigate it with [testing-troubleshooting.md](testing-troubleshooting.md).
4. If a listed test **passes**, the defect may be fixed. CI reports this as a notice.

## 3. Result files

| Source | File | Content |
|---|---|---|
| `--results=<file>.csv` (any runner command) | the file you named | Header `id,test,outcome,ms,message`; one row per selected test, all values quoted |
| `Invoke-Regression.ps1 -RunId <RunId>` | `qa\regression\<RunId>\artifacts\selection.json` | Changed files, matched impact-map rules, selected IDs, and the reason for a full run |
| | `…\build.log` | `dotnet restore` and `dotnet build` output (compiler errors are here) |
| | `…\console.log` | The runner's full terminal output |
| | `…\results.csv` | As above |
| `Publish-TestResults.ps1 -Artifacts <folder>` | `<folder>\junit.xml` | JUnit XML (IDs, status, duration, messages) for tools that read JUnit |
| | `<folder>\summary.md` | Totals, new failures, known defects, skips |
| GitHub Actions | artifact `test-results-ci-<run_id>-<attempt>` | All of the above; see [ci-cd-testing-guide.md](ci-cd-testing-guide.md) |

Useful PowerShell one-liners for a results file:

```powershell
$r = Import-Csv "$env:TEMP\ct007-results.csv"
$r | Group-Object outcome | Select-Object Name, Count       # totals
$r | Where-Object outcome -eq 'FAIL' | Format-List id, test, message
$r | Sort-Object { [int]$_.ms } -Descending | Select-Object -First 5 id, ms   # slowest tests
```

## 4. Inspecting an error

1. Read the message on the `FAIL` line. The test harness writes assertion failures as `<what was checked>: expected <x>, actual <y>` (from `Fixtures/Assert.cs`).
2. Open the test: search the code for the ID, for example `"FE-047"` in `Tests/CT007.Tests`. The text after the ID in `[Test("…", "…")]` states what the test expects.
3. Read the scenario in [`qa/planning/frontend-test-cases.md`](../planning/frontend-test-cases.md), [`backend-test-cases.md`](../planning/backend-test-cases.md) or [`integration-test-cases.md`](../planning/integration-test-cases.md).
4. Run the test alone, for example `& $tests FE-047`, to see whether it fails without the other tests.
5. If the build failed, there are no test lines at all: read the `error CS…` lines in the terminal or in `build.log`.

## 5. Earlier execution evidence

| What | Where |
|---|---|
| Final QA run (summary, defects, coverage analysis) | [`qa/reports/run-20261016-final/`](../reports/run-20261016-final/) |
| Its raw results and console logs | [`qa/execution/evidence/`](../execution/evidence/) |
| First regression run and its artifacts | [`qa/regression/reg-20261016-01/`](../regression/reg-20261016-01/) |
| CI dry-run artifacts | [`qa/ci/validation/`](../ci/validation/) |
| Mutation-testing results | [`qa/effectiveness/eff-20261016/`](../effectiveness/eff-20261016/) |
| The commit last verified as a regression baseline | [`qa/regression/baseline.json`](../regression/baseline.json) |

Code coverage reports do not exist: no coverage tool is configured.
