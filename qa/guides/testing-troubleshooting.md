# Testing Troubleshooting

Common problems when running the CT007 tests, their likely causes, and safe ways to investigate them. Investigate first and fix deliberately. Do not change tests, expected results or [`Tests/ci/known-failures.json`](../../Tests/ci/known-failures.json) to make a run pass ([`qa/ci/failure-policy.md`](../ci/failure-policy.md)).

## 1. Is it the application, the environment or the test?

| Signal | Most likely | How to confirm |
|---|---|---|
| Build fails, no test lines | Environment (SDK, packages) or code that does not compile | Read the first `error` line |
| Many unrelated tests fail with the same exception (for example `TypeInitializationException`, `DllNotFoundException`) | Environment | See "SQLite native library" below |
| Only Timing tests fail, by small margins | Environment (busy machine) | Re-run on an idle machine; if it fails again in the same way, it is real |
| One test fails with `expected <x>, actual <y>` and passes nowhere | **Application defect**, or a test whose expectation is wrong | Compare with the scenario in `qa/planning/*-test-cases.md` and the requirement it cites; FE-005 and BE-030 are confirmed defects |
| A test fails only when run with others, and passes alone | Test-implementation problem (shared state, timing) | Run it alone with `& $tests <ID>`; record it as intermittent ([failure-policy.md](../ci/failure-policy.md#retries-and-flaky-tests)) |
| `SKIP` where you expected a run | Configuration (category not included, or a variable missing) | Read the skip reason ([testing-results-guide.md](testing-results-guide.md#1-terminal-output)) |

## 2. Problems and safe steps

### Missing SDK or dependencies

- **Symptom:** `dotnet` is not recognised; `NETSDK1045` (the SDK is too old); `NU1101` / `NU1301` (a package cannot be found).
- **Check:**

  ```powershell
  dotnet --list-sdks
  dotnet nuget list source
  ```

- **Cause and next steps:**
  - The .NET 10 SDK is missing.
  - Or there is no network: the first restore needs nuget.org or a cache that already holds the packages.
  - `Invoke-Regression.ps1` restores **offline** from `%USERPROFILE%\.nuget\packages` by default. If a package was added recently (for example YamlDotNet), add `-PackageSource https://api.nuget.org/v3/index.json`.

### Build cannot copy `Frontend.exe` (`MSB3021`, "being used by another process")

- **Cause:** CT007 Measurement Studio is running from `Frontend\bin\Debug\…`.
- **Step:** close the app and build again. The regression script avoids this, because it builds in a copy under `%TEMP%`.

### Compilation failures (`error CS…`)

- **Check:** the first error names the file and line. With `-warnaserror` (CI, or `Invoke-Regression.ps1 -WarnAsError`), any compiler warning also fails the build.
- **Next step:** build the project that fails on its own:

  ```powershell
  dotnet build Frontend\Frontend.csproj
  dotnet build Backend\Backend.csproj
  ```

- **Not a valid test:** `dotnet build CT007.slnx` fails because of the legacy `SmokeTests` project (NU1201). That is not a regression.

### Test discovery: "0 tests" or fewer tests than expected

- **Cause:** the ID filter matched nothing (filters are ID **prefixes**, for example `FE-` or `BE-03`), or an argument containing a comma was not quoted.
- **Example:** an unquoted `--include=Timing,Sql` becomes two arguments. `Sql` is then treated as an ID filter, and nothing matches.
- **Step:** run without a filter and look up the IDs in [testing-commands.md](testing-commands.md#running-tests).

### `dotnet run … -- FE-` runs everything

- **Cause:** some PowerShell versions drop `--` before native-command arguments.
- **Step:** run the built program directly:

  ```powershell
  & ".\Tests\CT007.Tests\bin\Debug\net10.0-windows10.0.19041.0\CT007.Tests.exe" FE-
  ```

### SQLite native library fails to load (every database test fails)

- **Cause:** the path to `e_sqlite3.dll` is too long (about 252 characters; [`Tests/README.md`](../../Tests/README.md)).
- **Step:** use a short `-WorkRoot` with the scripts, for example `-WorkRoot C:\ct007`, or clone the repository to a shorter path.

### Database connection errors (BE-006 only)

- **Symptom:** BE-006 fails with a connection error, or is skipped.
- **Check:**

  ```powershell
  sqllocaldb info ct007
  $env:CT007_TEST_SQL
  ```

- **Cause:** the instance is not started, or the variable is not set in **this** terminal.
- **Rule:** the test only accepts a database whose name starts with `CT007_Test`. Never point `CT007_TEST_SQL` at a shared or production server.
- **Other tests:** no other test uses SQL Server; they use temporary SQLite files.

### Missing environment variables

| Variable | Needed by | Without it |
|---|---|---|
| `CT007_TEST_SQL` | BE-006 (`Sql`) | `SKIP` |
| `CT007_E2E_DEDICATED_ACCOUNT=1` | E2E-001…004 | `SKIP` |
| `CT007_FRONTEND_DIR` (optional) | E2E, for a build other than `Frontend\bin\Debug\net10.0-windows10.0.19041.0` | E2E uses the default path, or skips if `Frontend.exe` is missing there |

Variables set with `$env:` exist only in the current terminal. A new terminal needs them again.

### Incorrect test configuration

- **Symptom:** `Invoke-Regression.ps1` selects the full suite unexpectedly.
- **Check:** open `selection.json` and read `fullReasons`. An unmapped production file, a `*.csproj` file, test code or CI files force a full run by design ([`Tests/regression/impact-map.json`](../../Tests/regression/impact-map.json)).

**Script blocked by execution policy** ("running scripts is disabled"):
- Run it once with `powershell -ExecutionPolicy Bypass -File Tests\regression\Invoke-Regression.ps1 -RunId <RunId>`. This changes the policy for that one process only.

**Git "detected dubious ownership"** (the repository folder belongs to another Windows user):
- The scripts already pass `safe.directory`. For your own `git` commands, use `git -c safe.directory=<repo path with forward slashes> status`.

### Unavailable external dependencies

The default tests need **no** backend, SQL Server, Gamma Guard, Bluetooth device or detector. All of these are faked. If a test seems to wait for one of them:
- **Network or backend:** INT-003 and INT-004 deliberately simulate an offline or slow backend.
- **Bluetooth:** only fake device lists are used. No test reads Windows Bluetooth.
- **Hardware:** the real CT007-M detector is covered only by the manual cases MAN-001…017.

### Failed assertions

1. Read `expected <x>, actual <y>` in the message.
2. Find the scenario and its requirement (`[Test("ID", "title")]`, then `qa/planning/*-test-cases.md`).
3. Decide:
   - the application is wrong: a **defect**, to be reported with the test ID and message;
   - or the expectation has no approved basis: a **test problem**, to be raised in [`qa/planning/unresolved-requirements.md`](../planning/unresolved-requirements.md) rather than changed silently.

### E2E tests fail or misbehave

E2E is informational in CI. It needs the following:
- a dedicated account;
- an unlocked desktop and 100 % scaling;
- no mouse movement during the run;
- a fresh `dotnet build Frontend\Frontend.csproj`, including the section YAML files under `Frontend\bin\…\Pages\`.

If the app cannot start because of a configuration error, a dialog naming the YAML file and key is shown.

## 3. Cleanup checks

```powershell
git status                                         # tracked obj\ restore files can change after a local build
Get-ChildItem "$env:TEMP\ct007-tests" -ErrorAction SilentlyContinue   # leftover test folders (safe to delete once no test runs)
```
