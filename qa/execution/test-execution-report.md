# Test Execution Report (Phase 4)

Executed on 2026-10-16 on Windows 11 Pro 10.0.26200 with .NET SDK 10.0.402, as Windows user `user`. The tests are the Phase 3 suite, `Tests/CT007.Tests` (mapping in `qa/generation/test-implementation-status.md`).

> The Phase 4 instructions received were cut off after "2. Verify Test Environment". This report follows the stated objective: run everything eligible, classify the failures, and save the evidence for Phase 5.

## 1. Result

**Final run** (`evidence/run-final-results.csv`, `evidence/run-final-console.log`), with every category included:

| Outcome | Tests | Scenarios |
|---|---|---|
| PASS | 100 | 97 |
| FAIL | 2 | 2 (FE-005, BE-030) — **application defects** |
| SKIP | 5 | 5 (BE-006, E2E-001…004) — environment not available |
| **Total** | **107** | **104** |

| Group | Pass | Fail | Skip |
|---|---|---|---|
| FE (48 scenarios / 49 tests) | 48 tests | 1 (FE-005) | 0 |
| BE (35 scenarios / 37 tests) | 35 tests | 1 (BE-030) | 1 (BE-006) |
| INT (17) | 17 | 0 | 0 |
| E2E (4) | 0 | 0 | 4 |
| Timing category (9) | 9 | 0 | — |

The final run took about 106 s in total. INT-008 alone took about 54 s.

The 31 planned scenarios outside the suite were **not executed**: 17 manual, 11 Unresolved, 2 Blocked and 1 Conditional (see the Phase 3 status document).

## 2. How the tests were run

- **Isolation from the repository.** `Frontend/obj` and `Backend/obj` are tracked in git, and a build rewrites them. So `Frontend/`, `Backend/`, `Tests/` and `CT007.slnx` were copied to a scratch folder, and the copy was restored, built and executed there. The repository's tracked files were never built or changed.
- **Offline restore.** Packages were restored with `--source C:\Users\Bek\.nuget\packages`, the existing local cache, into a scratch package folder. Nothing was downloaded and no new package was added.
- **Commands** (run in the copy):
  ```text
  dotnet restore Tests/CT007.Tests/CT007.Tests.csproj --source C:\Users\Bek\.nuget\packages
  dotnet build   Tests/CT007.Tests/CT007.Tests.csproj --no-restore     → 0 errors, 0 warnings (first compile)
  CT007.Tests.exe --include=Timing,Sql,E2E --results=<csv>
  ```
- **Test data.** Every test used its own `%TEMP%\ct007-tests\<guid>` folder, an in-process backend on 127.0.0.1, and a SQLite Gamma Guard file that the test wrote itself. No production service or database was contacted. `appsettings*.json` was absent from the test output, as designed.
- **Not run.**
  - **BE-006 (Sql):** `CT007_TEST_SQL` is not set and LocalDB is not installed.
  - **E2E-001…004:** they need `CT007_E2E_DEDICATED_ACCOUNT=1`. This account is not confirmed as a dedicated test account, and the E2E bench would move its real `%LocalAppData%\CT007_Measurement_Studio` folder aside, so it was not opted in.

## 3. Run history

| Run | Scope | Result | Notes |
|---|---|---|---|
| 0 | Default set | 88 pass, 5 fail, 14 skip | Before the helper fixes (`evidence/run-0-console-before-helper-fixes.log`) |
| — | BE-027, BE-037, INT-007 after fixes T1/T2 | 4/4 pass | |
| — | FE-005, BE-030 diagnostic | Fail only at the moved text-value case | Done in the scratch copy only; originals restored |
| 1 | All categories | 100 pass, 2 fail, 5 skip | |
| 2, 3 | All categories | 99 pass, 3 fail, 5 skip | INT-008 failed at capture 8 (`evidence/run-2-console-before-INT-008-fix.log`) |
| — | INT-008 after fix T3, ×3 | 3/3 pass | |
| Final | All categories | **100 pass, 2 fail, 5 skip** | Evidence of record |

**Stability.** Excluding INT-008 before fix T3, every test gave the same result in all four full runs. That includes the 8 other Timing tests.

## 4. Failure analysis

### 4.1 Application defects (confirmed by execution)

**DEF-001 — `ApiSettings.Load` throws for a non-numeric value, where the documentation promises defaults.** Test: FE-005. Severity: High (proposed).

- **Observed.** `{"requestTimeoutSeconds":"abc"}` throws `InvalidOperationException: The requested operation requires an element of type 'Number', but the target element has type 'String'.`
- **Cause.** `Frontend/Api/ApiSettings.cs:36` calls `JsonElement.TryGetDouble` on a string element. That method throws rather than returning false. The `catch` at line 27 handles only `IOException`, `UnauthorizedAccessException` and `JsonException`.
- **Expected.** The class's own documentation says "Missing or invalid values fall back to the defaults". `DeviceSettings.Load` handles the same case by also catching `InvalidOperationException`.
- **Impact (Inferred, the app was not launched).** `ApiSettings.Load` runs in the `MainViewModel()` constructor (`ViewModels/MainViewModel.cs:25`), which `MainWindow` creates. A mistyped `api-settings.json` next to the exe would therefore stop the application from starting.
- **Isolation.** In a diagnostic run with this case moved last, every other FE-005 case passed: missing file, invalid JSON, zero and negative timeouts, a relative URL, and a valid file.
- **Same pattern (Inferred, not executed).** A non-string `baseUrl` (for example `5`) calls `GetString()` at lines 22–23, which throws the same exception.

**DEF-002 — `CaptureDurationSettings.Load` throws for a non-numeric value, where the documentation promises 5 s.** Test: BE-030. Severity: High (proposed).

- **Observed.** `{"captureDurationSeconds":"7"}` throws the same `InvalidOperationException`.
- **Cause.** `Frontend/Calculation/Measurement/CaptureDurationSettings.cs:35` calls `TryGetInt32` on a string element. The `catch` at line 38 does not include `InvalidOperationException`.
- **Expected.** The class documentation: "A saved value outside the limits … falls back to 5 s".
- **Impact (Inferred).** `Load()` runs in the `MeasurementViewModel` constructor (`Pages/Measurement/MeasurementViewModel.cs:56`), so a hand-edited or corrupted `capture-settings.json` in `%LocalAppData%\CT007_Measurement_Studio` would stop the app from starting. The app itself only ever writes a number.
- **Isolation.** All other BE-030 cases passed in the diagnostic run.

Neither defect was fixed. Both are reported for Phase 5.

### 4.2 Test implementation errors (fixed in `Tests/` only)

| ID | Test | Symptom | Cause | Fix |
|---|---|---|---|---|
| T1 | BE-027 `VendorDatabaseReadOnly`, INT-007 | `IOException: … being used by another process` | `TestFolder.Sha256` used `File.ReadAllBytes`, which refuses to share with the SQLite writer connection that is still open | `Fixtures/TestFolder.cs`: hash through a `FileStream` with `FileShare.ReadWrite \| Delete` |
| T2 | BE-037 `MigrationRefusals` | `SqliteException 19: FOREIGN KEY constraint failed` | The fixture's raw SQL connection enforced foreign keys (the SQLite build's default), so the deliberately orphaned record could not be inserted | `Fixtures/AppDatabase.cs` `Execute`: `ForeignKeys = false`, as an older app version or another tool would write |
| T3 | INT-008 `ConcurrentWriter` | Runs 2 and 3: "capture 8: samples stored: expected 5, actual 0" | The test read the stored samples as soon as the page was Ready, but saving is asynchronous (the serial persistence queue) | `Integration/CapturePipelineTests.cs`: poll for up to 5 s for the capture's samples. Then passed 3/3 and in the final run |

No assertion was weakened. Each fix corrects how the test sets up or observes the system.

**Note on T3.** With the wait in place, every capture's samples did appear within 5 s, so no data was lost. The delay between "row shown" and "row stored" is the queue's designed behaviour, already covered by FE-035 and FE-048.

### 4.3 Hygiene observation (no effect on results)

About two `%TEMP%\ct007-tests\<guid>` folders per run were not deleted. They held `corrupt.db` (BE-040), `m.db`, or `gg.sqlite` with WAL files. `TestFolder.Dispose` gives up after 5 attempts while a background task or pooled connection still has the file open. The leftovers were removed after the run. Suggested follow-up: longer retries, or cleanup at runner exit.

## 5. Coverage actually achieved by execution

These Critical and High scenarios passed:
- BE-020 and BE-025 (capture window and polling).
- BE-027 (vendor DB never modified, rollback and WAL).
- BE-032 (atomic save) and BE-036/BE-037 (migrations v1–v4 → v5, refusals, rollback).
- INT-007 (capture → storage → reload) and INT-008 (capture during concurrent 1 Hz writes, writer never blocked, after fix T3).
- FE-026…FE-036 (state machine, cancellation, retries, session isolation).
- INT-001…INT-005 (real HTTP against the backend pipeline, 503/timeout/offline recovery, contract alignment).

What a passing result does **not** cover (unchanged from the earlier phases):
- Every expectation is **Inferred** from code (UR-01), so a pass shows consistency with the implementation, not with an approved requirement.
- Gamma Guard was simulated, and the backend's `Program.cs` was mirrored rather than executed (Phase 3 D8).
- No real SQL Server (BE-006 not run), no desktop UI (E2E not run), no hardware (MAN-001…005).

## 6. Evidence (in `qa/execution/evidence/`)

| File | Content |
|---|---|
| `run-final-results.csv` | One row per test: id, method, outcome, ms, message |
| `run-final-console.log` | Console output of the final run |
| `run-0-console-before-helper-fixes.log` | First default run (T1/T2 failures visible) |
| `run-2-console-before-INT-008-fix.log` | Run showing the T3 race |

## 7. Changes made in this phase

- **Test code only:** `Tests/CT007.Tests/Fixtures/TestFolder.cs`, `Fixtures/AppDatabase.cs`, `Integration/CapturePipelineTests.cs` (T1–T3).
- **New:** `qa/execution/` (this report and the evidence).
- **Unchanged:** all production code under `Frontend/` and `Backend/`, `SmokeTests/`, `CT007.slnx`, and the `qa/analysis`, `qa/planning` and `qa/generation` documents. `git status` lists only the untracked `Tests/`, `qa/generation/` and `qa/execution/`.

## 8. Open items for Phase 5

1. Report DEF-001 and DEF-002, and decide on the fix owner.
2. Run BE-006 with a local `CT007_Test` database, and E2E-001…004 on a dedicated test account. Both are prerequisites for claiming database and UI workflow coverage.
3. The 13 Unresolved and Blocked scenarios still wait on UR answers. The manual cases MAN-001…017 need hardware and a tester.
4. Optional: fix the temp-folder cleanup weakness (§4.3).
