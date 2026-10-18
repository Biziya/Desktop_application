# Manual Test Execution (Windows PowerShell)

A step-by-step walkthrough for running the automated tests by hand. Each command is explained once in [testing-commands.md](testing-commands.md); this guide puts them in order.

**Verified on 2026-10-17** by running every command below in Windows PowerShell 5.1 with .NET SDK 10.0.402, on a machine without SQL Server LocalDB and not signed in to a dedicated test account. Results quoted below are from those runs.

Paste and run **one command at a time**. A `>>` prompt means PowerShell is waiting for the rest of a command: press **Ctrl+C** and enter the command again.

## Testing flow

```text
 1. Open the repository in VS Code, PowerShell terminal          → Step 1
 2. Check .NET 10 SDK (and optionally LocalDB)                   → Step 2
 3. Stop the app AND any "dotnet watch" session                  → Step 3.1   (otherwise the build fails: file locked)
 4. LocalDB installed?               yes → prepare the Sql test  → Step 3.2   no → skip, BE-006 shows SKIP
 5. Dedicated Windows test account?  yes → enable E2E            → Step 3.3   no → skip, E2E-001…004 show SKIP
 6. Build the test harness (builds Frontend and Backend too)     → Step 4.1
 7. Run tests: by area, one test, or all eligible                → Step 4.2–4.4
 8. Read the summary; compare failures with the known defects    → Step 5
 9. Investigate anything new                                     → Step 6
10. Clean up (obj\ files, variables, LocalDB)                    → Step 7
```

## Step 1 — Open the project

1. Start VS Code.
2. **File → Open Folder…** and choose the repository folder, the one containing `CT007.slnx`, `Frontend\`, `Backend\` and `Tests\`.
3. Open a terminal with **Terminal → New Terminal**.
4. If the terminal is not PowerShell, choose **PowerShell** from the drop-down next to the **+** in the terminal panel.
5. Check that you are in the repository root:

   ```powershell
   Test-Path .\CT007.slnx
   ```

   It must print `True`. Every later command runs from this folder.

## Step 2 — Verify prerequisites

```powershell
dotnet --version
```

It must print `10.0.x` (verified with `10.0.402`). If `dotnet` is not recognised, install the .NET 10 SDK and open a new terminal.

Optional checks:

```powershell
git --version
Get-Command sqllocaldb, sqlcmd -ErrorAction SilentlyContinue
```

- **Git** is needed only for the regression script (Step 4.4).
- **`Get-Command`** prints the two tools if SQL Server LocalDB is installed. **If it prints nothing, LocalDB is not installed:** skip Step 3.2. BE-006 then shows `SKIP` and every other test still runs.

The first build downloads the NuGet packages (Microsoft.Data.Sqlite, YamlDotNet, Microsoft.Data.SqlClient, Swashbuckle.AspNetCore), so it needs internet access or a local package cache that already holds them.

## Step 3 — Prepare the test environment

The default tests need no database, backend, detector or Gamma Guard installation:
- every test works in its own `%TEMP%\ct007-tests\<guid>` folder;
- the tests start an in-process fake backend on `127.0.0.1`;
- Gamma Guard is simulated with a SQLite file the test writes itself;
- `Backend\appsettings.json` (the corporate SQL Server connection) is deleted from the test output and never used.

### 3.1 Stop the app and `dotnet watch` (always)

A running `Frontend.exe` locks the file that the build must replace. The build then fails with `MSB3027` / `MSB3021`, "The file is locked by: Frontend (<id>)".

1. Close the CT007 Measurement Studio window.
2. **If you started the app with `dotnet watch`, stop it** by pressing **Ctrl+C** in the terminal where it runs. While `dotnet watch` is running it starts the app again whenever files change, so closing the window is not enough.
3. Check that nothing is left. Both commands must print nothing:

   ```powershell
   Get-Process Frontend -ErrorAction SilentlyContinue
   Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" | Where-Object CommandLine -match 'watch' | Select-Object ProcessId, CommandLine
   ```

   If the second command lists a `dotnet watch run` process and you cannot find its terminal, stop it by its ID, for example `Stop-Process -Id 21912`. This ends that development session.

Use an idle machine if you will include the `Timing` tests, because they measure real waits.

### 3.2 Sql test (only if Step 2 found `sqllocaldb` and `sqlcmd`)

This uses a **disposable local** database, never a shared or production server:

```powershell
sqllocaldb create ct007 -s
sqlcmd -S "(localdb)\ct007" -E -b -Q "IF DB_ID('CT007_Test') IS NULL CREATE DATABASE CT007_Test"
$env:CT007_TEST_SQL = 'Server=(localdb)\ct007;Database=CT007_Test;Integrated Security=true;TrustServerCertificate=true'
```

BE-006 creates and drops the table `dbo.ZOOVCH_COLLAR_LIST` in that database. It refuses any database whose name does not start with `CT007_Test`.

### 3.3 E2E tests (only on a dedicated Windows test account)

**Never run the E2E tests on your own account.** The E2E bench moves `%LocalAppData%\CT007_Measurement_Studio`, where the real measurement database lives, aside for the test and restores it afterwards.

The account also needs an unlocked desktop and 100 % display scaling. Do not touch the mouse during the run.

```powershell
dotnet build Frontend\Frontend.csproj
$env:CT007_E2E_DEDICATED_ACCOUNT = "1"
```

If you set the variable by mistake, clear it:

```powershell
Remove-Item Env:CT007_E2E_DEDICATED_ACCOUNT -ErrorAction SilentlyContinue
```

## Step 4 — Execute tests

### 4.1 Build once and define the runner shortcut

```powershell
dotnet build Tests\CT007.Tests\CT007.Tests.csproj
$tests = ".\Tests\CT007.Tests\bin\Debug\net10.0-windows10.0.19041.0\CT007.Tests.exe"
Test-Path $tests
```

- The build must end with `0 Error(s)`; the `NU1903` and `NU1902` warnings about vulnerable packages are expected (see [troubleshooting](testing-troubleshooting.md#package-vulnerability-warnings-nu1902--nu1903)).
- `Test-Path` must print `True`.
- If the build fails with "file is locked", go back to Step 3.1.

### 4.2 Run tests by area

```powershell
& $tests FE-
& $tests BE-
& $tests INT- "--include=Timing"
& $tests E2E- "--include=E2E"
```

| Command | Runs | Result on 2026-10-17 |
|---|---|---|
| `& $tests FE-` | Frontend tests (51) | 2 failed (FE-005 known defect; FE-047, see [troubleshooting](testing-troubleshooting.md#fe-047-fails-header-ts_ms-changed-to-date)), 5 skipped (Timing) |
| `& $tests BE-` | Backend and local-logic tests (38) | 1 failed (BE-030 known defect), 1 skipped (Sql) |
| `& $tests INT- "--include=Timing"` | Integration tests (17) | All passed |
| `& $tests E2E- "--include=E2E"` | Desktop E2E (4) | 4 skipped without a dedicated account (Step 3.3) |

### 4.3 Run one test or one test file

```powershell
& $tests FE-047
& $tests FE-001 FE-002 FE-003 FE-004 FE-005 FE-006 FE-007 FE-008 "--include=Timing"
```

The first command runs one test, the second one test file (`ApiClientTests.cs`). Other files and their IDs: [testing-commands.md](testing-commands.md#running-tests).

### 4.4 Run all eligible tests, with evidence

All tests, with a results file:

```powershell
& $tests "--include=Timing,Sql,E2E" "--results=$env:TEMP\ct007-results.csv"
```

Verified result: `110 tests: 102 passed, 3 failed, 5 skipped.` (about 3 minutes). The failures were FE-005, BE-030 and FE-047. The skips were BE-006 and E2E-001…004, whose environment was not prepared.

The same run as CI does it, in an isolated copy of the repository, with saved artifacts:

```powershell
powershell -File Tests\regression\Invoke-Regression.ps1 -RunId reg-20261017-01
```

- Use a new `-RunId` each time; it becomes the folder `qa\regression\<RunId>\artifacts\`.
- At the end it **updates the Excel reports** `qa\excel-reports\Test Results.xlsx` and `Bug List.xlsx`, also when tests fail, and copies them into the artifacts. Close the workbooks in Excel first; an open workbook is skipped with a warning.
- To run and report only some tests: `powershell -File Tests\regression\Invoke-Regression.ps1 -RunId reg-20261018-02 -Tests FE-047,BE-030`.
- The direct runs above (`& $tests …`) are for quick checks only: they do **not** update the Excel reports.
- This script builds under `%TEMP%`, so a running app or `dotnet watch` does not block it.
- Verified result: the full suite (110 tests), 102 passed, 3 failed, 5 skipped.

## Step 5 — View results

1. The last terminal line is the summary, for example `110 tests: 102 passed, 3 failed, 5 skipped.`
2. List the failures. Only these are expected:
   - FE-005 and BE-030, the known defects in [`Tests/ci/known-failures.json`](../../Tests/ci/known-failures.json);
   - FE-047, until the test is updated (see troubleshooting).
3. `$LASTEXITCODE` is `0` when nothing failed and `1` otherwise.
4. To summarise a results file:

   ```powershell
   Import-Csv "$env:TEMP\ct007-results.csv" | Group-Object outcome | Select-Object Name, Count
   ```

What each line means, the result files and how to inspect an error: [testing-results-guide.md](testing-results-guide.md).

## Step 6 — Troubleshoot failures

1. Is the failing ID FE-005 or BE-030? Then it is a known defect.
2. Did the build fail? Read the first `error` line before looking at tests.
3. Run the failing test alone (`& $tests <ID>`) to see whether it fails on its own.
4. Use [testing-troubleshooting.md](testing-troubleshooting.md) to tell an application defect from an environment or test problem.

Do not edit tests or `known-failures.json` to make a run pass ([`qa/ci/failure-policy.md`](../ci/failure-policy.md)).

## Step 7 — Clean up

1. **Restore files:** a local build rewrites tracked NuGet restore files in `Frontend\obj\`, `Backend\obj\` and `Tests\CT007.Tests\obj\`. Check with `git status` before committing. If those are the only changes and they came from your build, restore them:

   ```powershell
   git checkout -- Frontend/obj Backend/obj Tests/CT007.Tests/obj
   ```

2. **Variables:** remove the test variables. This is safe even if they were never set:

   ```powershell
   Remove-Item Env:CT007_TEST_SQL, Env:CT007_E2E_DEDICATED_ACCOUNT -ErrorAction SilentlyContinue
   ```

3. **LocalDB:** if you created it only for testing (Step 3.2), remove it with `sqllocaldb stop ct007` and then `sqllocaldb delete ct007`.
