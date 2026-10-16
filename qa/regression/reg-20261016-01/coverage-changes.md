# Coverage Changes — reg-20261016-01

| | |
|---|---|
| New test scenarios | None. No new functionality to cover |
| Updated tests | None. `Tests/CT007.Tests/` is unchanged since `cc031a5` |
| Obsolete scenarios | None |
| Reduced coverage | None in the code. In this run, only 15 of the 104 implemented scenarios were re-executed. The rest keep their `run-20261016-final` results, which were not revalidated here |
| New manual requirements | None |
| Code coverage | Not measured (no coverage tool, as in Phase 4) |

## New regression tooling (test area only)

- **[`Tests/regression/impact-map.json`](../../../Tests/regression/impact-map.json):** maps each production path to its direct and dependent test IDs, built from `qa/analysis/integration-map.md` and the test inventory. It also defines the critical regression set and the paths that force a full run, and it notes manual follow-ups (for example: a XAML change → MAN-006, MAN-008 and MAN-009; a `Program.cs` change → update the `TestBackend` mirror).
- **[`Tests/regression/Invoke-Regression.ps1`](../../../Tests/regression/Invoke-Regression.ps1):**
  1. computes the Git changes since [`baseline.json`](../baseline.json), including uncommitted and untracked files;
  2. selects the tests;
  3. builds and runs them in an isolated copy, so tracked `obj/` files are never touched;
  4. writes `selection.json`, `results.csv`, `console.log` and `build.log`.

  It never commits and never updates the baseline.

## Remaining gaps

These are unchanged from [coverage-analysis.md](../../reports/run-20261016-final/coverage-analysis.md):
- no executed E2E or real-UI evidence;
- no real SQL Server, Gamma Guard or hardware;
- no security tests;
- 14 scenarios not implemented and 17 manual cases not executed.
