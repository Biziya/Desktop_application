# Manual Test Execution (Windows PowerShell)

A step-by-step walkthrough for running the automated tests by hand. Each command is explained once in [testing-commands.md](testing-commands.md); this guide puts them in order. The commands were checked against the project files but **not executed** while this guide was written.

## Step 1 — Open the project

1. Start VS Code.
2. **File → Open Folder…** and choose the repository folder, the one containing `CT007.slnx`, `Frontend\`, `Backend\` and `Tests\`.
3. Open a terminal with **Terminal → New Terminal**.
4. If the terminal is not PowerShell, choose **PowerShell** from the drop-down next to the **+** in the terminal panel.
5. Check that you are in the repository root:

   ```powershell
   Get-Location
   Test-Path .\CT007.slnx
   ```

   `Test-Path` must print `True`. Every later command runs from this folder.

## Step 2 — Verify prerequisites

```powershell
dotnet --version                  # must be 10.0.x
dotnet --list-sdks                # the 10.0 SDK must be listed
$PSVersionTable.PSVersion         # 5.1 or 7.x; both work
git --version                     # needed only for the regression script (change detection)
```

| Tool | Needed for | If missing |
|---|---|---|
| .NET 10 SDK, with Windows desktop support | Everything | Install the .NET 10 SDK; the projects target `net10.0-windows10.0.19041.0` |
| NuGet packages (Microsoft.Data.Sqlite, YamlDotNet, Microsoft.Data.SqlClient, Swashbuckle.AspNetCore) | Everything | Restored automatically on the first build. This needs internet access or a local package cache that already contains them |
| Git | `Invoke-Regression.ps1` only | Install Git, or run the tests directly (Step 4) |
| SQL Server LocalDB, `sqllocaldb` and `sqlcmd` | Only the `Sql` test BE-006 | Skip that test; it reports `SKIP` |
| A dedicated Windows test account | Only the `E2E` tests | Skip them; they report `SKIP` |

Optional checks for the opt-in categories:

```powershell
Get-Command sqllocaldb, sqlcmd -ErrorAction SilentlyContinue   # Sql category
```

## Step 3 — Prepare the test environment

**Default tests need no preparation:**
- **Test data:** every test creates its own folder under `%TEMP%\ct007-tests\<guid>` and deletes it afterwards. Fixtures are in `Tests\CT007.Tests\Fixtures\`.
- **Backend:** the tests start an in-process fake backend on `127.0.0.1` with synthetic drillholes (`Fixtures\TestBackend.cs`), or a stub HTTP handler. The real backend does not need to be running.
- **Gamma Guard:** simulated with a SQLite file the test writes itself. No detector or Gamma Guard install is used.
- **Production configuration is never used.** `Backend\appsettings.json`, which holds the corporate SQL Server connection, is deleted from the test output after every build. No test contacts that server.

Before you start:
1. Close CT007 Measurement Studio if it is running. A running `Frontend.exe` locks the file the build has to replace.
2. Use an idle machine if you include the `Timing` tests, because they measure real waits.

Optional, for the `Sql` test only. This uses a **disposable local** database and never a shared or production server:

```powershell
sqllocaldb create ct007 -s
sqlcmd -S "(localdb)\ct007" -E -b -Q "IF DB_ID('CT007_Test') IS NULL CREATE DATABASE CT007_Test"
$env:CT007_TEST_SQL = 'Server=(localdb)\ct007;Database=CT007_Test;Integrated Security=true;TrustServerCertificate=true'
```

BE-006 creates and drops the table `dbo.ZOOVCH_COLLAR_LIST` in that database, and refuses any database whose name does not start with `CT007_Test`.

Optional, for the `E2E` tests only. **Run these only while signed in to a dedicated Windows test account, never on your own account.**
- The E2E bench moves `%LocalAppData%\CT007_Measurement_Studio`, where the real measurement database lives, aside for the test and restores it afterwards.
- The account needs an unlocked desktop and 100 % display scaling. Do not touch the mouse during the run.

```powershell
dotnet build Frontend\Frontend.csproj
$env:CT007_E2E_DEDICATED_ACCOUNT = "1"
```

Variables set with `$env:` disappear when the terminal closes. Remove them earlier with `Remove-Item Env:CT007_TEST_SQL, Env:CT007_E2E_DEDICATED_ACCOUNT`.

## Step 4 — Execute tests

1. Build once, and define the runner shortcut:

   ```powershell
   dotnet build Tests\CT007.Tests\CT007.Tests.csproj
   $tests = ".\Tests\CT007.Tests\bin\Debug\net10.0-windows10.0.19041.0\CT007.Tests.exe"
   ```

   The build must end with `0 Error(s)`. Building the test harness also builds `Frontend` and `Backend`.

2. Run what you need:

   ```powershell
   & $tests FE-                                   # 1. frontend tests
   & $tests BE-                                   # 2. backend and local-logic tests
   & $tests INT- "--include=Timing"               # 3. integration tests
   & $tests E2E- "--include=E2E"                  # 4. desktop E2E (dedicated account only, see Step 3)
   & $tests "--include=Timing,Sql,E2E"            # 5. all eligible tests
   ```

3. To run one test, give its ID, for example `& $tests FE-047`. For a whole test file, list its IDs (see the module table in [testing-commands.md](testing-commands.md#running-tests)).

4. To keep evidence, add a results file to any run:

   ```powershell
   & $tests "--include=Timing" "--results=$env:TEMP\ct007-results.csv"
   ```

5. To run the tests the way CI does, in an isolated copy with change-based selection and saved artifacts:

   ```powershell
   powershell -File Tests\regression\Invoke-Regression.ps1 -RunId reg-20261017-01
   ```

   Use a new `-RunId` each time; it becomes the folder name `qa\regression\<RunId>\`.

Quote any argument that contains a comma (`"--include=Timing,Sql"`). Without quotes, PowerShell splits it into two arguments.

## Step 5 — View results

1. **Terminal:** one line per test (`PASS`, `FAIL` or `SKIP`, then the ID, the test method and the time), and a summary as the last line.
2. **Exit code:** `$LASTEXITCODE` is `0` when nothing failed and `1` otherwise.
3. **Files:** the `--results=` CSV, or `qa\regression\<RunId>\artifacts\` for script runs.

What each line means, how to read the files and how to inspect an error: [testing-results-guide.md](testing-results-guide.md).

## Step 6 — Troubleshoot failures

1. Check whether the failing ID is a known defect: FE-005 and BE-030 are listed in [`Tests/ci/known-failures.json`](../../Tests/ci/known-failures.json).
2. If the build failed, read the compiler error before looking at tests.
3. Run the failing test alone (`& $tests <ID>`) to see whether it fails on its own as well.
4. Use [testing-troubleshooting.md](testing-troubleshooting.md) to tell an application defect from an environment or test problem.

Do not edit tests or `known-failures.json` to make a run pass; see [`qa/ci/failure-policy.md`](../ci/failure-policy.md).

## Afterwards

- **Tracked `obj\` files.** A local `dotnet build` or `dotnet run` rewrites the NuGet restore files in `Frontend\obj\`, `Backend\obj\` and `Tests\CT007.Tests\obj\`, which are tracked in Git. Check `git status` before you commit. `Invoke-Regression.ps1` builds in a copy and never touches them.
- **LocalDB.** If you created it only for testing, remove it with `sqllocaldb stop ct007` and then `sqllocaldb delete ct007`.
