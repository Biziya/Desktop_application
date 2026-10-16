# Change Summary — reg-20261016-01

| | |
|---|---|
| Branch / HEAD | `main` @ `6c47ad8` ("reports", 2026-10-16 20:34 +08:00) |
| Previous verified baseline | `run-20261016-final` (see [qa-summary](../../reports/run-20261016-final/qa-summary.md)): production code of `277d581` tested with `Tests/` as committed in `cc031a5`. Recorded in [`qa/regression/baseline.json`](../baseline.json) |
| Comparison | `git diff cc031a5..6c47ad8`, plus the working tree (`git status`). At the start of the phase the working tree was clean |
| Selection evidence | [`artifacts/selection.json`](artifacts/selection.json) |

## Changed files

| Change | File(s) | Area |
|---|---|---|
| Committed in `6c47ad8` | `qa/reports/run-20261016-final/` — 6 files, added | QA documentation |
| Added this phase (uncommitted) | `Tests/regression/Invoke-Regression.ps1`, `Tests/regression/impact-map.json` | Test tooling |
| Added this phase (uncommitted) | `qa/regression/baseline.json`, `qa/regression/reg-20261016-01/` | QA documentation and evidence |
| Updated this phase (uncommitted) | `Tests/README.md` (new "Regression runs" section) | Test documentation |

**Frontend files changed: 0. Backend files changed: 0.** There were also no changes to configuration, dependencies (`*.csproj`), the database schema or migrations, `tools/`, `SmokeTests/`, or the test code in `Tests/CT007.Tests/`.

`git diff --stat 277d581..6c47ad8 -- Frontend Backend SmokeTests tools CT007.slnx` is empty. The production code under test is byte-identical to the previous baseline.

## Features affected

None. No application feature (F01–F23) has a changed implementation, and no API contract, data model or validation rule changed.
