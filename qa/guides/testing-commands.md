# Testing Commands Reference

Every command below comes from the project's own files: `Tests/CT007.Tests/TestRunner.cs`, `Tests/README.md`, `Tests/regression/Invoke-Regression.ps1`, `Tests/ci/Publish-TestResults.ps1`, `Tests/mutation/Invoke-MutationRun.ps1` and `.github/workflows/tests.yml`. **None of them was executed while this guide was written** (static verification only).

How to use the commands step by step: [manual-test-execution.md](manual-test-execution.md). Reading the output: [testing-results-guide.md](testing-results-guide.md).

## Conventions

- **Working directory:** the repository root (the folder that contains `CT007.slnx`) for every command, unless the table says otherwise.
- **Shell:** Windows PowerShell 5.1 or PowerShell 7 (the VS Code terminal).
- **Quote every argument that contains a comma**, as shown. Unquoted, PowerShell splits `--include=Timing,Sql` into two arguments, and `Sql` would then be treated as a test-ID filter.
- **The test runner.** `Tests/CT007.Tests` is one console program; it is not xUnit or NUnit, so `dotnet test` does not apply. It contains all frontend (`FE-`), backend (`BE-`), integration (`INT-`) and desktop end-to-end (`E2E-`) tests: 110 `[Test]` methods at the time of writing (51 FE, 38 BE, 17 INT, 4 E2E). After a build, the commands use this shortcut:

  ```powershell
  $tests = ".\Tests\CT007.Tests\bin\Debug\net10.0-windows10.0.19041.0\CT007.Tests.exe"
  ```

- **Runner arguments** (from `TestRunner.cs`):
  - ID prefixes select tests (`FE-` = every frontend test; `FE-047` = one test; `BE-03` = BE-030…039).
  - `--include=Timing,Sql,E2E` adds opt-in categories.
  - `--results=<file>.csv` writes a results file.
  - Exit code 0 = no failure, 1 = at least one failure.

## Build and static checks

| Test Category | Working Directory | Command | Output |
|---|---|---|---|
| Build the test harness (also builds Frontend and Backend) | repo root | `dotnet build Tests\CT007.Tests\CT007.Tests.csproj` | `Build succeeded` and `0 Error(s)`; the runner in `Tests\CT007.Tests\bin\Debug\net10.0-windows10.0.19041.0\` |
| Frontend build validation | repo root | `dotnet build Frontend\Frontend.csproj` | `Frontend\bin\Debug\net10.0-windows10.0.19041.0\Frontend.exe`, plus the four section `*.yaml` files under `Pages\` |
| Backend build validation | repo root | `dotnet build Backend\Backend.csproj` | `Backend\bin\Debug\net10.0-windows10.0.19041.0\Backend.dll` |
| Static analysis: compiler warnings as errors (the CI static check) | repo root | `dotnet restore Tests\CT007.Tests\CT007.Tests.csproj`<br>`dotnet build Tests\CT007.Tests\CT007.Tests.csproj --no-restore -warnaserror` | Fails on any compiler warning in the test project, Frontend or Backend. Same sequence as `Invoke-Regression.ps1 -WarnAsError` |
| Type checking | — | Part of every C# build above (nullable reference types are enabled) | No separate tool |

## Running tests

| Test Category | Working Directory | Command | Output |
|---|---|---|---|
| Default set: everything except opt-in categories | repo root | `dotnet run --project Tests\CT007.Tests\CT007.Tests.csproj` | One `PASS`/`FAIL`/`SKIP` line per test, then `N tests: x passed, y failed, z skipped.` |
| Same, without rebuilding | repo root | `& $tests` | Same |
| All frontend tests (ViewModels, API client, formatting, YAML settings) | repo root | `& $tests FE-` | FE lines only |
| All backend tests (see note 1) | repo root | `& $tests BE-` | BE lines only |
| Backend API tests (in-process ASP.NET host) | repo root | `& $tests BE-001 BE-002 BE-005 BE-008 BE-011` | 5 tests |
| Database tests (temporary SQLite files, migrations) | repo root | `& $tests BE-031 BE-032 BE-033 BE-034 BE-035 BE-036 BE-037 BE-038 BE-039 BE-040 BE-046` | 11 tests |
| SQL Server projection test (needs LocalDB, see the environment section below) | repo root | `& $tests BE-006 "--include=Sql"` | `SKIP` unless `CT007_TEST_SQL` is set |
| Integration tests (HTTP and capture pipeline) | repo root | `& $tests INT- "--include=Timing"` | 17 tests; INT-003, 004, 008 and 016 need `Timing` |
| Desktop E2E: real `Frontend.exe`, UI Automation, mouse input (**dedicated Windows test account only**) | repo root | `$env:CT007_E2E_DEDICATED_ACCOUNT = "1"`<br>`& $tests E2E- "--include=E2E"` | 4 tests, about 2–4 minutes each; `SKIP` with a reason when prerequisites are missing |
| Timing category (real-time waits, about 70 s) | repo root | `& $tests "--include=Timing"` | Default set plus FE-006, 008, 023, 028, 048 and INT-003, 004, 008, 016 |
| One test | repo root | `& $tests FE-047` | One line |
| One module (test file, see note 2) | repo root | for example `& $tests FE-001 FE-002 FE-003 FE-004 FE-005 FE-006 FE-007 FE-008 "--include=Timing"` | That file's tests |
| Critical regression set (the IDs CI runs on every change) | repo root | `& $tests BE-020 BE-025 BE-027 BE-032 BE-036 BE-037 INT-007 INT-008 INT-010 INT-012 FE-026 FE-035 FE-036 FE-005 BE-030 "--include=Timing"` | 15 tests; FE-005 and BE-030 are known defects and fail |
| All eligible tests | repo root | `& $tests "--include=Timing,Sql,E2E"` | Sql and E2E tests run only if their environment is prepared; otherwise they show as `SKIP` |
| Any run, with a results file | repo root | append `"--results=$env:TEMP\ct007-results.csv"` to any `& $tests` command | CSV file `id,test,outcome,ms,message` |

**Note 1.** In this harness, "BE" means backend and local logic. BE-001…011 test the ASP.NET Core `Backend` project. The other BE tests cover the frontend's non-UI logic (`Frontend/Calculation/`, `MeasurementRepository.cs`, CSV export, settings files).

**Note 2.** There is no filter by file name, only by ID prefix. Test files and their IDs:

| Module (file in `Tests/CT007.Tests/`) | IDs |
|---|---|
| `Frontend/ApiClientTests.cs` | FE-001…008 |
| `Frontend/DrillholeSelectionTests.cs` | FE-009…016, FE-018, FE-040 |
| `Frontend/CoreInformationTests.cs` | FE-019…023 |
| `Frontend/MeasurementWorkflowTests.cs` | FE-024…039, FE-048, FE-049 |
| `Frontend/StatusAndPresentationTests.cs` | FE-041…047, FE-052 |
| `Backend/DrillholeApiTests.cs` | BE-001, 002, 005, 006, 008, 011 |
| `Backend/DepthAndOrderTests.cs` | BE-015…019 |
| `Backend/CaptureLogicTests.cs` | BE-020…026 |
| `Backend/LocalFilesTests.cs` | BE-027…030 |
| `Backend/MeasurementRepositoryTests.cs` | BE-031…040, BE-046 |
| `Backend/CsvExportTests.cs` | BE-042, BE-043 |
| `Integration/HttpIntegrationTests.cs` | INT-001…006 |
| `Integration/CapturePipelineTests.cs` | INT-007…017 |
| `EndToEnd/DesktopWorkflowTests.cs` | E2E-001…004 |

A prefix that is too short selects more than one file. For example, `FE-00` also matches FE-009, so list the IDs.

## Change-based regression and reports (scripts)

| Test Category | Working Directory | Command | Output |
|---|---|---|---|
| Preview which tests a change selects (no build) | repo root | `powershell -File Tests\regression\Invoke-Regression.ps1 -RunId reg-20261017-01 -SelectOnly` | `qa\regression\reg-20261017-01\artifacts\selection.json` |
| Change-based regression run (as CI does it) | repo root | `powershell -File Tests\regression\Invoke-Regression.ps1 -RunId reg-20261017-01` | `selection.json`, `build.log`, `console.log` and `results.csv` in `qa\regression\<RunId>\artifacts\` |
| Full regression with every category, warnings as errors, packages from nuget.org | repo root | `powershell -File Tests\regression\Invoke-Regression.ps1 -RunId reg-20261017-02 -Full -Include "Timing,Sql,E2E" -WarnAsError -PackageSource https://api.nuget.org/v3/index.json` | Same files |
| JUnit XML and Markdown summary from a run | repo root | `powershell -File Tests\ci\Publish-TestResults.ps1 -Artifacts qa\regression\reg-20261017-01\artifacts` | Adds `junit.xml` and `summary.md`; exit 1 for a new (not known) failure |
| Mutation testing (QA effectiveness, slow) | repo root | `powershell -File Tests\mutation\Invoke-MutationRun.ps1 -Output qa\effectiveness\mut-20261017\artifacts` | `baseline.csv`, `mutants\<id>.csv`, `mutation-results.json` in `-Output` |

Script facts:
- **Defaults:** `Invoke-Regression.ps1` uses `-Include Timing` and the offline package source `%USERPROFILE%\.nuget\packages`.
- **Isolation:** it builds in `%TEMP%\ct007-regression\<RunId>`, so the repository's tracked `obj\` files are not touched.
- **Baseline:** it compares against `qa/regression/baseline.json` unless `-Baseline <commit>` is given.

## Environment for the opt-in categories

| Category | Needs | Commands (taken from `.github/workflows/tests.yml` and the tests) |
|---|---|---|
| `Sql` (BE-006) | SQL Server LocalDB and `sqlcmd`. The database name must start with `CT007_Test`; the test refuses any other name or server | `sqllocaldb create ct007 -s`<br>`sqlcmd -S "(localdb)\ct007" -E -b -Q "IF DB_ID('CT007_Test') IS NULL CREATE DATABASE CT007_Test"`<br>`$env:CT007_TEST_SQL = 'Server=(localdb)\ct007;Database=CT007_Test;Integrated Security=true;TrustServerCertificate=true'` |
| `E2E` | A **dedicated Windows test account** with an unlocked desktop and 100 % display scaling, and a built `Frontend\bin\Debug\net10.0-windows10.0.19041.0\Frontend.exe` (or `$env:CT007_FRONTEND_DIR`). The bench moves `%LocalAppData%\CT007_Measurement_Studio` aside and restores it afterwards | `dotnet build Frontend\Frontend.csproj`<br>`$env:CT007_E2E_DEDICATED_ACCOUNT = "1"` |
| `Timing` | An idle machine; no extra setup | — |

Environment variables set with `$env:` last only for the current terminal.

## Not available in this project

| Capability | Status |
|---|---|
| `dotnet test` | **Not applicable**: the tests are a console program, not a test-framework project |
| Code coverage | **Not configured**: no coverage tool is referenced (`qa/ci/ci-overview.md` says the same) |
| Separate linter or analyzer configuration (`.editorconfig`, analyzer packages) | **Not configured**: static checking is compiler warnings as errors only |
| `dotnet build CT007.slnx` | **Not usable**: the solution includes the legacy `SmokeTests` project, which cannot build (NU1201; see `qa/ci/test-execution-matrix.md`). The solution does not include `Tests/CT007.Tests` |
| `SmokeTests` | **Legacy, cannot build**; not part of CI |
| Hardware tests with a CT007-M detector | **Manual only**: MAN-001…017, see [manual-testing-checklist.md](../reports/run-20261016-final/manual-testing-checklist.md) |
| Separate frontend "component" tests (rendering) | **None automated**: ViewModels are tested directly; the UI is covered only by E2E and the manual checklist |
