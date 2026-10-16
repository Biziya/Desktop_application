# Defect Report — run-20261016-final

Severity reflects the impact on users and data. It is separate from the Phase 2 test priority. Nothing here has been fixed.

## Confirmed defects (failing test with a documented expected result)

<a id="def-001"></a>

### DEF-001 — Non-numeric value in `api-settings.json` throws instead of falling back to defaults

| | |
|---|---|
| **Severity** | **High.** The file sits beside the exe and is documented as user-editable. One mistyped value would probably stop the app from starting (inferred; the app was not launched). Workaround: correct the file |
| **Module** | Frontend: `Frontend/Api/ApiSettings.cs` (F05 backend connectivity) |
| **Related tests** | [FE-005](../../planning/frontend-test-cases.md#fe-005) — FAIL in all 4 full runs |
| **Reproduction** | `ApiSettings.Load(path)` where the file contains `{"requestTimeoutSeconds":"abc"}`. The same happens for `statusIntervalSeconds` |
| **Expected** | `ApiSettings.Default` (`http://localhost:5002`, 5 s, 5 s). Source of the expectation: the class's own documentation ("Missing or invalid values fall back to the defaults") and the equivalent handling in `DeviceSettings.Load` |
| **Actual** | `InvalidOperationException: The requested operation requires an element of type 'Number', but the target element has type 'String'.` |
| **Cause** | Confirmed by reading the code. Line 36 calls `JsonElement.TryGetDouble` on a string element, which throws rather than returning false. The `catch` at line 27 handles only IO, access and JSON exceptions. `Load` is called in the `MainViewModel()` constructor (`ViewModels/MainViewModel.cs:25`) |
| **Related, not executed** | A non-string `baseUrl` (for example `5`) reaches `GetString()` at lines 22–23 and would throw the same exception (inferred) |
| **Evidence** | [`run-final-results.csv`](../../execution/evidence/run-final-results.csv). A diagnostic run moved this case last, and all other FE-005 cases passed ([execution report §4.1](../../execution/test-execution-report.md)) |
| **Diagnosis confidence** | High for the exception; medium for the start-up impact |
| **Action** | Treat non-matching JSON value kinds as invalid: check `ValueKind`, or catch `InvalidOperationException` as `DeviceSettings.Load` does. Rerun FE-005 |

<a id="def-002"></a>

### DEF-002 — Non-numeric `captureDurationSeconds` throws instead of falling back to 5 s

| | |
|---|---|
| **Severity** | **Medium.** It has the same probable start-up impact, but the file lives in `%LocalAppData%` and the app only ever writes a number there. It needs a manual edit or corruption to trigger. Workaround: delete the file |
| **Module** | Frontend: `Frontend/Calculation/Measurement/CaptureDurationSettings.cs` (F13 capture duration) |
| **Related tests** | [BE-030](../../planning/backend-test-cases.md#be-030) — FAIL in all 4 full runs |
| **Reproduction** | `new CaptureDurationSettings(path).Load()` where the file contains `{"captureDurationSeconds":"7"}` |
| **Expected** | 5. The class documentation says an invalid saved value falls back to 5 s |
| **Actual** | The same `InvalidOperationException` as DEF-001 |
| **Cause** | Line 35 calls `TryGetInt32` on a string element; the `catch` at line 38 misses `InvalidOperationException`. `Load()` runs in the `MeasurementViewModel` constructor (line 56) |
| **Evidence** | Results CSV and the diagnostic run (all other BE-030 cases passed) |
| **Diagnosis confidence** | High for the exception; medium for the start-up impact |
| **Action** | Use the same fix pattern as DEF-001. Rerun BE-030 |

DEF-001 and DEF-002 share one root-cause pattern. They are listed separately because they affect different files with different exposure.

## Suspected issues (not confirmed by an executed failing test)

| ID | Proposed severity | Issue | Basis | Confidence | Action |
|---|---|---|---|---|---|
| SUS-01 | **Critical (security)** | Plain-text SQL Server credentials committed in `Backend/appsettings.json`; no backend authentication; Swagger enabled in every environment | Phase 1 static review. BE-010 and BE-009 not implemented (UR-14) | High (static fact) | Rotate the credentials, move them to a secret store, decide the deployment posture |
| SUS-02 | Medium | `tools/check-db.ps1` still expects schema v4 and queries columns that v5 removed | Static reading of lines 142 and 144. BE-045 not implemented (UR-11) | High | Update or retire the script |
| SUS-03 | Medium | A locked or missing Gamma Guard file during a capture shows only "Capture failed. Right-click to try again.", with no reason | Observed in passing test INT-009 (Phase 3 D13) | High (observed); requirement unclear | Decide whether a reason must be shown; if so, map `SqliteException` to `CaptureException` |
| SUS-04 | Medium (data loss) | If a save hangs, closing waits 5 s and then drops unsaved rows. Stop during the 100 ms processing hold discards a completed capture | Observed in passing tests FE-048 and FE-028, which document current behaviour (UR-23) | High (observed); acceptability unresolved | Product decision (UR-23) |
| SUS-05 | Medium | A missing connection string probably gives HTTP 500 rather than 503; other non-SQL exceptions also give 500 | Phase 1 inference. BE-003 and BE-004 not implemented (UR-12) | Medium | Decide the intended status, then test |
| SUS-06 | Medium | The clock/time-basis check is skipped for a detector's first capture, so a seconds-based `ts_ms` would silently give 0 samples | Observed in passing test BE-026 (UR-29) | High (observed) | Confirm whether this is intended |
| SUS-07 | Low | A refused migration (newer schema, duplicates) still switches the file to WAL before refusing, which changes the file header | Observed while building BE-037 (Phase 3 D10); content is unchanged | High | Run the version check before `PRAGMA journal_mode` |
| SUS-08 | Low / unknown | Drillhole IDs that differ only in case may create two sessions (SQLite UNIQUE is case-sensitive; the catalog is not); CSV formula neutralisation misses leading tab/CR | Static (UR-19, UR-18); not asserted by tests | Medium | Clarify the requirements, then add tests |

Test-code defects found during execution (T1–T3) are not application defects. They were fixed in `Tests/` and are recorded in the [execution report §4.2](../../execution/test-execution-report.md).
