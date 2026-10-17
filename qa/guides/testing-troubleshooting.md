# Testing Troubleshooting

Common problems when running the CT007 tests, their likely causes, and safe ways to investigate them. Entries marked _Seen 2026-10-17_ were reproduced while verifying these guides.

Investigate first and fix deliberately. Do not change tests, expected results or [`Tests/ci/known-failures.json`](../../Tests/ci/known-failures.json) to make a run pass ([`qa/ci/failure-policy.md`](../ci/failure-policy.md)).

## 1. Is it the application, the environment or the test?

| Signal | Most likely | How to confirm |
|---|---|---|
| Build fails, no test lines | Environment (a locked file, the SDK, packages) or code that does not compile | Read the first `error` line |
| Many unrelated tests fail with the same exception (`TypeInitializationException`, `DllNotFoundException`), or the backend answers 500 instead of 503 | Environment: native DLL path too long | See "Native DLL path too long" below |
| Only Timing tests fail, by small margins | Environment (busy machine) | Re-run on an idle machine; if it fails again in the same way, it is real |
| One test fails with `expected <x>, actual <y>`, also when run alone | **Application defect**, or a test whose expectation is wrong | Compare with the scenario in `qa/planning/*-test-cases.md` and the requirement it cites. FE-005 and BE-030 are confirmed defects |
| A test fails only when run with others, and passes alone | Test-implementation problem (shared state, timing) | Run it alone with `& $tests <ID>`; record it as intermittent ([failure-policy.md](../ci/failure-policy.md#retries-and-flaky-tests)) |
| `SKIP` where you expected a run | Configuration (category not included, or a variable missing) | Read the skip reason ([testing-results-guide.md](testing-results-guide.md#1-terminal-output)) |

## 2. Build problems

### `Frontend.exe` is locked

_Seen 2026-10-17._

- **Symptom:** `MSB3026` retries, then `MSB3027` / `MSB3021` "The file is locked by: Frontend (6484)".
- **Cause:** CT007 Measurement Studio is running from `Frontend\bin\Debug\…`. Often the cause is a **`dotnet watch run`** session: it starts the app again whenever files change, so closing the window does not help.
- **Steps:**
  1. Find what is running:

     ```powershell
     Get-Process Frontend -ErrorAction SilentlyContinue
     Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" | Where-Object CommandLine -match 'watch' | Select-Object ProcessId, CommandLine
     ```

  2. Stop `dotnet watch` with **Ctrl+C** in its terminal, or by ID (`Stop-Process -Id <ProcessId of "dotnet watch run">`). Then close the app.
  3. Run both commands again; they must print nothing.
  4. Build again.
- **Alternative:** the regression script (`Invoke-Regression.ps1`) builds in a copy under `%TEMP%` and is not affected.

### Package vulnerability warnings (NU1902 / NU1903)

_Seen 2026-10-17._

- **Symptom:** `warning NU1903: Package 'SQLitePCLRaw.lib.e_sqlite3' 2.1.11 has a known high severity vulnerability` (Frontend and Tests), and `NU1902` / `NU1903` for `System.Security.Cryptography.Pkcs` 9.0.4 (Backend).
- **Normal build:** these are warnings only; the build succeeds.
- **With `-warnaserror`** (the static check, `Invoke-Regression.ps1 -WarnAsError`, and **the CI workflow**): they become 4 errors, and the build fails before any test runs.
- **Seen without `-WarnAsError`:** a regression run from the offline package cache passed, because that source provides no vulnerability data.
- **What it is:** a real dependency finding, not a test problem. It is resolved by updating the vulnerable packages (`Frontend.csproj`, `Backend.csproj`). Until then, the CI check fails at the build step.

### Missing SDK or dependencies

- **Symptom:** `dotnet` is not recognised; `NETSDK1045` (the SDK is too old); `NU1101` / `NU1301` (a package cannot be found).
- **Check:**

  ```powershell
  dotnet --list-sdks
  dotnet nuget list source
  ```

- **Steps:**
  - Install the .NET 10 SDK, or connect to the internet for the first restore.
  - `Invoke-Regression.ps1` restores **offline** from `%USERPROFILE%\.nuget\packages` by default. If a package is not cached yet, add `-PackageSource https://api.nuget.org/v3/index.json`.

### Compilation failures (`error CS…`)

- **Check:** the first error names the file and line.
- **Next step:** build the project that fails on its own:

  ```powershell
  dotnet build Frontend\Frontend.csproj
  dotnet build Backend\Backend.csproj
  ```

- **Not a valid test:** `dotnet build CT007.slnx` fails because of the legacy `SmokeTests` project (NU1201). That is not a regression.

### Script blocked by execution policy ("running scripts is disabled")

Run it once with `powershell -ExecutionPolicy Bypass -File Tests\regression\Invoke-Regression.ps1 -RunId <RunId>`. This changes the policy for that one process only.

## 3. Test failures

### FE-047 fails (header ts_ms changed to Date)

_Seen 2026-10-17._

- **Symptom:** `column order: expected [… Sample, ts_ms, count0 …], actual [… Sample, Date, count0 …]`.
- **Cause:** `Frontend\Pages\Measurement\Table\MeasurementTable.yaml` now gives the `tsMs` column the header `"Date"`, but FE-047 still expects the original header text `ts_ms`.
- **What it is:** a **test-implementation problem**, not an application defect. The header is meant to be editable in the YAML, but the test pins the shipped text.
- **Effect:** the publish step reports FE-047 as a new failure (`::error`), so the CI check fails until the test or the YAML is aligned. This needs a decision by the team: update the test to check column ids instead of header text, or restore the header.

### Native DLL path too long

_Seen 2026-10-17._

- **Symptom A:** every SQLite test fails with `TypeInitializationException` / `DllNotFoundException`.
- **Symptom B:** BE-002 fails with `expected <ServiceUnavailable>, actual <InternalServerError>`, and INT-002 fails with a different backend error message.
- **Cause:** the repository or work folder is too deep. Native DLLs fail to load once their full path reaches about 252 characters.
  - SQLite: `…\runtimes\win-x64\native\e_sqlite3.dll`.
  - SQL Server client: `…\Microsoft.Data.SqlClient.SNI.dll`; a 259-character path was seen.
- **Steps:**
  - Clone the repository to a short path, for example `C:\src\Desktop_application`.
  - With the scripts, use a short `-WorkRoot` (for example `-WorkRoot C:\ct007`).
  - The same tests passed in the normal location.

### Database connection errors (BE-006 only)

- **Check:**

  ```powershell
  Get-Command sqllocaldb, sqlcmd -ErrorAction SilentlyContinue
  sqllocaldb info ct007
  $env:CT007_TEST_SQL
  ```

- **If `Get-Command` prints nothing (seen 2026-10-17):** LocalDB is not installed. The setup commands then fail with "`sqllocaldb` is not recognized". Skip them: BE-006 shows `SKIP` and no other test needs SQL Server. To run it anyway, install SQL Server Express LocalDB and `sqlcmd`, open a new terminal, and follow [Step 3.2](manual-test-execution.md#32-sql-test-only-if-step-2-found-sqllocaldb-and-sqlcmd).
- **Otherwise:** the instance is not started, or the variable is not set in **this** terminal.
- **Rule:** never point `CT007_TEST_SQL` at a shared or production server. The test only accepts a database named `CT007_Test…`.

### Missing environment variables

| Variable | Needed by | Without it |
|---|---|---|
| `CT007_TEST_SQL` | BE-006 (`Sql`) | `SKIP` |
| `CT007_E2E_DEDICATED_ACCOUNT=1` | E2E-001…004 | `SKIP` |
| `CT007_FRONTEND_DIR` (optional) | E2E, for a build other than `Frontend\bin\Debug\net10.0-windows10.0.19041.0` | E2E uses the default path, or skips if `Frontend.exe` is missing there |

Variables set with `$env:` exist only in the current terminal. To remove them safely: `Remove-Item Env:CT007_TEST_SQL, Env:CT007_E2E_DEDICATED_ACCOUNT -ErrorAction SilentlyContinue`. Without `-ErrorAction`, `Remove-Item` reports an error for a variable that is not set.

### Fewer tests than expected (selection)

- **Cause:** filters are ID **prefixes**, so `FE-04` also matches FE-040…049, and a typo matches nothing.
- **Note:** one ID can have more than one test method. The 15 IDs of the critical set run 19 tests.
- **Step:** run without a filter and look up the IDs in [testing-commands.md](testing-commands.md#running-tests).

### `Invoke-Regression.ps1` selects the full suite

Open `selection.json` and read `fullReasons`. An unmapped production file, a `*.csproj` file, `obj\` restore files, test code or CI files force a full run by design ([`Tests/regression/impact-map.json`](../../Tests/regression/impact-map.json)).

### Git "detected dubious ownership"

The repository folder belongs to another Windows user. The scripts already pass `safe.directory`. For your own commands, use `git -c safe.directory=<repo path with forward slashes> status`.

### Unavailable external dependencies

The default tests need **no** backend, SQL Server, Gamma Guard, Bluetooth device or detector; all are faked.
- INT-003 and INT-004 deliberately simulate an offline or slow backend.
- The real CT007-M detector is covered only by the manual cases MAN-001…017.

### Failed assertions

1. Read `expected <x>, actual <y>` in the message.
2. Find the scenario and its requirement: `[Test("ID", "title")]`, then `qa/planning/*-test-cases.md`.
3. Decide:
   - the application is wrong: a **defect**, to be reported with the test ID and message;
   - the expectation has no approved basis: a **test problem**, to be raised in [`qa/planning/unresolved-requirements.md`](../planning/unresolved-requirements.md) rather than changed silently.

### E2E tests fail or misbehave

E2E is informational in CI. It needs all of the following:
- a dedicated account;
- an unlocked desktop and 100 % scaling;
- no mouse movement during the run;
- a fresh `dotnet build Frontend\Frontend.csproj`, including the section YAML files under `Frontend\bin\…\Pages\`.

If the app cannot start because of a configuration error, a dialog naming the YAML file and key is shown.

## 4. Cleanup checks

```powershell
git status
Get-ChildItem "$env:TEMP\ct007-tests" -ErrorAction SilentlyContinue
```

- **`git status`:** shows tracked `obj\` restore files changed by a local build. Restore them with `git checkout -- Frontend/obj Backend/obj Tests/CT007.Tests/obj`.
- **`Get-ChildItem`:** lists leftover test folders, which are safe to delete once no test is running.
