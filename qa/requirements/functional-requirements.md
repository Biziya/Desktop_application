# Functional Requirements

**Status labels:** **Confirmed** (documented in the root README §2, the decision log or the §17 rules), **Inferred** (from the code), **Proposed** (not approved), **Unresolved**. See [project-overview.md](project-overview.md).

**Implementation:** Implemented / Partial / Not implemented / Diverges (implemented differently from a Confirmed requirement).

**Tests:** IDs from the [test inventory](../planning/test-inventory.md). Results are from the latest full run ([validation results.csv](../effectiveness/eff-20261016/artifacts/validation/results.csv): all pass except the two known-defect tests; BE-006 and E2E skipped; MAN not run). "Mutation" means the rule's faults were killed in [mutation testing](../effectiveness/eff-20261016/mutation-results.md).

Source paths are relative to the repository root: FE = `Frontend/`, BE = `Backend/`. Rule values are in [business-rules.md](business-rules.md).

## A. Drillhole reference data and Core Information

| ID | Requirement | Expected behaviour | Status | Impl. | Source files | Tests / evidence |
|---|---|---|---|---|---|---|
| FR-001 | Provide the drillhole list | `GET /api/core-information/drillholes` returns `[{holeId, projectCode, campaign}]` from SQL Server `ZOOVCH_COLLAR_LIST`; the form shows Hole ID, Project and Campaign | Inferred | Implemented | BE `Controllers/…/Drillhole/DrillholeController.cs`, `Repositories/…/Drillhole/DrillholeRepository.cs`; FE `Api/Core-information/drillholes/DrillholesClient.cs` | FE-001, BE-001, INT-001, INT-005: pass |
| FR-002 | Derive Campaign | Campaign = the last 4 characters of `PROSPECT`; SQL NULL → `""` (BR-020) | Inferred; correctness Unresolved (UR-08) | Implemented | BE `DrillholeRepository.cs` | BE-006: **skipped** (needs SQL Server); mutation M77: no coverage |
| FR-003 | Work offline with the last list | The last successful non-empty list is cached in the app DB and shown first; an empty or failed response never replaces it | Inferred | Implemented | FE `DrillholeCache.cs`, `CoreInformationViewModel.cs` | FE-009, FE-011, BE-040, INT-006: pass; M61 killed |
| FR-004 | Keep working when a refresh fails | List, pick and form values are kept; a message and Retry are shown (HTTP error / offline / timeout / invalid data) | Inferred | Implemented | FE `ApiClient.cs`, `CoreInformationViewModel.cs` | FE-002…004, FE-010, INT-002…004: pass; M52, M53 killed |
| FR-005 | Reload when the backend returns | After a failed load, the first successful probe reloads the list once | Inferred | Implemented | FE `BackendConnection.cs`, `CoreInformationViewModel.cs` | FE-007, INT-003: pass; M62 killed |
| FR-006 | Searchable drillhole selection | Case-insensitive *contains* search; ranked exact > prefix > partial; "Measured drillholes" (newest first) before "All drillholes" (A–Z); the closest match is highlighted but never picked automatically | Inferred | Implemented | FE `DrillholeCatalog.cs`, `CoreInformationViewModel.cs`, `CoreInformationPage.xaml(.cs)` | FE-013…016, FE-018, FE-040: pass; M57–M60 killed |
| FR-007 | Lock the drillhole while measuring | Changing or clearing the drillhole while measurement mode is on is refused with an error message | Inferred | Implemented | FE `CoreInformationViewModel.cs`, `MainViewModel.cs` | FE-022: pass; M63 killed |
| FR-008 | Save & Continue | Requires a picked drillhole; an empty Operator gets the Windows account; values are trimmed; opens the Measurement page | Inferred | Implemented | FE `CoreInformationViewModel.cs`, `Shared/Models/CoreSnapshot.cs` | FE-019, FE-020, INT-001: pass; M64, M65 killed |
| FR-009 | Disarm on form edit | Editing Operator or Notes after saving stops measurement mode until saved again | Inferred (by design per code comment); intent Unresolved (UR-09) | Implemented | FE `MainViewModel.cs` | FE-021: pass |

## B. Device and connection status

| ID | Requirement | Expected behaviour | Status | Impl. | Source files | Tests / evidence |
|---|---|---|---|---|---|---|
| FR-010 | Show backend connectivity | Online/Offline from a periodic `HEAD /` probe; any HTTP answer counts as Online | Inferred; "any answer = Online" Unresolved (UR-13) | Implemented | FE `BackendConnection.cs`, `ApiClient.cs`, `MainWindow.xaml` | FE-006, FE-008, BE-011: pass; M56 killed |
| FR-011 | Monitor the selected CT007-M (Bluetooth) | Lists CT007-M devices (name prefix, or a configured address); the user picks one; shows Connected / Disconnected / Status unavailable; **no effect on capture** | Inferred. README: Bluetooth was meant for the **trigger** (Confirmed), not monitoring | Implemented (monitoring only) | FE `Calculation/Core-Information/BluetoothDevice/*`, `DeviceStatusMonitor.cs` | FE-043: pass; M70 killed; real hardware MAN-005 (not run) |
| FR-012 | Show app database status | Missing / Available / Busy / Inaccessible, checked read-only, without creating the file | Inferred | Implemented | FE `SqliteFileCheck.cs`, `MeasurementDatabase/MeasurementDatabaseCheck.cs` | FE-044, BE-028, INT-016: pass; M66 killed |
| FR-013 | Find and show the Gamma Guard database | A configured path, or a discovered `…GammaGuard…\Database\*.sqlite` (newest wins); Retry rediscovers | Inferred; install layout Unresolved (UR-16) | Implemented | FE `GammaGuardDatabase/GammaGuardDatabaseCheck.cs`, `DeviceSettings.cs` | BE-029, FE-044, INT-015: pass; M67–M69 killed; MAN-014 not run |
| FR-014 | Announce status changes | One toast per state transition (not at start-up) | Inferred | Implemented | FE `ViewModels/StatusAnnouncements.cs`, `Shared/Infrastructure/ToastService.cs` | FE-041, FE-042: pass; M71–M74 killed |

## C. Measurement sessions and depths

| ID | Requirement | Expected behaviour | Status | Impl. | Source files | Tests / evidence |
|---|---|---|---|---|---|---|
| FR-015 | One session per drillhole | Save & Continue reuses the drillhole's session; stored metadata is kept (warning if different) | Inferred; "stored wins" Unresolved (UR-30) | Implemented | FE `MeasurementViewModel.cs`, `MeasurementRepository.cs` | BE-031, FE-037: pass; M33 killed |
| FR-016 | Show a drillhole's stored measurements | Selecting a drillhole shows its stored rows and depth progress without creating a session; late loads are discarded | Inferred | Implemented | FE `MeasurementViewModel.SelectDrillhole` | FE-036, INT-011, INT-012: pass; M47 killed; M46 survived (redundant guard) |
| FR-017 | Start a new measurement | Closes the current drillhole; refused while rows are unsaved | Inferred | Implemented | FE `MeasurementViewModel.cs` | FE-038: pass |
| FR-018 | Restore the last session on start-up | — | Unresolved (UR-06) | Partial (method exists, not called) | FE `MeasurementViewModel.RestoreLatestSessionAsync` | FE-051: not implemented (blocked) |
| FR-019 | Validate depth inputs | Starting Depth: metres, ≥ 0, ≤ 3 decimals; Step: cm, > 0, ≤ 1 decimal; `.` or `,` (BR-001…003) | Inferred | Implemented | FE `Calculation/Measurement/Depth.cs`, `MeasurementViewModel.cs` | BE-015…017, FE-024: pass; M02–M06, M43 killed |
| FR-020 | Advance the depth per capture | The interval is fixed at the trigger (to = from + step); the next From = to, unless Starting Depth was edited during the capture | Inferred | Implemented | FE `MeasurementViewModel.CaptureAsync` | FE-029, BE-018, INT-011: pass; M42 killed |

## D. Capture

| ID | Requirement | Expected behaviour | Status | Impl. | Source files | Tests / evidence |
|---|---|---|---|---|---|---|
| FR-021 | Measurement mode | Start → Ready (needs a valid session and depths); Stop cancels any capture without a row | Inferred | Implemented | FE `MeasurementViewModel.cs`, `MeasurementState.cs` | FE-025…028, FE-031: pass |
| FR-022 | Capture trigger | **Confirmed:** "separate Bluetooth button". **Implemented:** right mouse button anywhere in the active, armed window | Confirmed (README §2) | **Diverges** | FE `MainWindow.xaml.cs` | FE-026 (ViewModel only); real input MAN-006, E2E-001 not run |
| FR-023 | Capture window timing | **Confirmed:** selection starts **3 s after the press** (configurable delay, default 3 s proposed). **Implemented:** window `click < ts_ms ≤ click + duration`, no delay | Confirmed (README §2, decision log) | **Diverges** | FE `Calculation/Measurement/DeviceSamples.cs` (`CaptureWindow`) | BE-020 (asserts the implemented window): pass; M07, M08 killed; MAN-002 not run |
| FR-024 | Configurable capture duration | Duration editable (whole seconds 2–10, default 5) and saved | Confirmed: configurable duration (README); range and default Inferred (UR-03) | Implemented | FE `CaptureDurationSettings.cs`, `MeasurementViewModel.cs` | FE-032, BE-030: pass except the DEF-002 case; M26, M27, M44 killed |
| FR-025 | Use exactly one detector | The serial with the newest sample; refused when none, when another detector recorded within 5 s, or when the Gamma Guard DB is not Available | Inferred; 5 s value Unresolved (UR-10) | Implemented | FE `GammaGuardCaptureDevices.cs` | BE-024: pass; M17–M19 killed; MAN-004 not run |
| FR-026 | Check the device time basis | Refuse a capture unless the latest `ts_ms` is Unix milliseconds within 12 h of the PC clock | Inferred; 12 h Unresolved (UR-10); skipped for a detector's first capture (UR-29) | Implemented | FE `DeviceSamples.cs` (`DeviceTime`), `DeviceCaptureService.cs` | BE-023, BE-026: pass; M15, M16, M22 killed |
| FR-027 | Collect the window's samples | Poll every 250 ms from 200 rows before the click; accept only the window, the frozen detector, and each row once; bounded final read for late rows (BR-010…013) | Inferred. README: "boundaries must be reproducible even if records arrive late" (Confirmed rule) | Implemented | FE `DeviceCaptureService.cs`, `DeviceSamples.cs` | BE-025, INT-007, INT-008: pass; M10, M11, M20, M21 killed |
| FR-028 | Compute the result row | Per-field mean of finite values; first `ts_ms`; device session if uniform; sample count; an incomplete capture shows "n of m" with a warning; no samples → error, no row | Inferred; incomplete-row policy Unresolved (UR-17) | Implemented | FE `DeviceSamples.cs` (`SampleAveraging`), `MeasurementViewModel.BuildResult`, `MeasurementResult.cs` | BE-022, FE-030, INT-014: pass; M12–M14, M45, M49 killed; accuracy vs device MAN-001 not run |
| FR-029 | Never modify the device database | All Gamma Guard access is read-only | **Confirmed** (README §17 rule) | Implemented | FE `GammaGuardDeviceDatabase.cs`, `SqliteFileCheck.cs` | BE-027, INT-007: pass (file hash unchanged) |

## E. Results, persistence and export

| ID | Requirement | Expected behaviour | Status | Impl. | Source files | Tests / evidence |
|---|---|---|---|---|---|---|
| FR-030 | Results table | Rows sorted by From (BR-016), renumbered; device values ≤ 2 decimals; `ts_ms` as local time | Confirmed: "table output" (README §2); details Inferred | Implemented | FE `MeasurementOrder.cs`, `MeasurementColumns*.cs`, `MeasurementCellFormat.cs`, `MeasurementPage.xaml` | BE-019, FE-033, FE-046, FE-047: pass; M23, M24, M50, M51 killed |
| FR-031 | Delete a measurement | Confirmation dialog; the row and its raw samples are removed in one transaction; capture numbers never reused; the row is restored if the delete fails | Inferred | Implemented | FE `MeasurementViewModel.Delete*`, `MeasurementRepository.DeleteAsync`, `Windows/ConfirmationWindow/*` | FE-034, BE-034, INT-013: pass; M36 killed; real dialog E2E-004 not run |
| FR-032 | Persist measurements | Row, raw samples and depth progress saved in one transaction through a serial queue; a failed save keeps the rows and retries on the next change, Stop, switch or close | Inferred. README: "preserve source records" (Goal) | Implemented | FE `MeasurementRepository.SaveAsync`, `MeasurementViewModel.Persist` | BE-032, BE-033, BE-035, FE-035, INT-010: pass; M28, M29, M32, M48 killed |
| FR-033 | Upgrade older databases | Migrate schema v1–v4 → v5 in one transaction; refuse newer schemas and duplicate Drillhole IDs; roll back on failure | Inferred | Implemented | FE `MeasurementRepository.Migrate` | BE-036…038, INT-017: pass; M30, M34, M35 killed |
| FR-034 | Export CSV | Header and column order fixed; every cell quoted; spreadsheet formulas neutralised in user text; invariant numbers; ISO times with offset; UTF-8 BOM; written atomically; one session only | Confirmed: CSV output (README §2); column contract Unresolved (UR-04, README Q10) | Implemented | FE `CsvExportService.cs`, `MeasurementColumns.cs` | BE-042, BE-043, FE-039, INT-014: pass; M37–M41 killed; file dialog BE-044 / MAN-013 not run |
| FR-035 | Depth bar chart | Bars from From to To; height = `ts_ms` | Inferred; meaning Unresolved (UR-05) | Implemented | FE `DepthBarChart.cs` | FE-049 pass; FE-050 not implemented |

## F. Settings, lifecycle and backend extras

| ID | Requirement | Expected behaviour | Status | Impl. | Source files | Tests / evidence |
|---|---|---|---|---|---|---|
| FR-036 | Settings with safe defaults | `api-settings.json`, `device-settings.json` and `capture-settings.json`: invalid or missing values fall back to defaults | Inferred (documented in code comments) | **Partial**: non-numeric values throw (DEF-001, DEF-002) | FE `ApiSettings.cs`, `DeviceSettings.cs`, `CaptureDurationSettings.cs` | FE-005, FE-045, BE-030; the DEF tests **fail** |
| FR-037 | Close safely | Closing cancels a running capture and waits up to 5 s for queued saves | Inferred; data-loss tolerance Unresolved (UR-23) | Implemented | FE `MeasurementViewModel.Dispose`, `MainWindow.xaml.cs` | FE-048: pass; MAN-007 not run |
| FR-038 | Report a backend database outage | SQL Server failure → HTTP 503 ProblemDetails, without exposing connection details | Inferred; other failures → 500 (UR-12) | Implemented | BE `DrillholeController.cs` | BE-002, INT-002: pass; M75, M76 killed |
| FR-039 | Backend diagnostic endpoints | `GET …/bluetooth-device`, `GET …/sqlite-database` (not used by the frontend) | Unresolved (UR-07) | Implemented, unused | BE `Controllers/…/BluetoothDevice`, `…/SqliteDatabase` | BE-008 only (OpenAPI listing) |
| FR-040 | Authentication / authorisation | None exists | Unresolved (UR-14) | Not implemented | BE `Program.cs` | No test |

Requirements without executed test evidence: FR-002, FR-018, FR-039 (partial), FR-040, and the real-input and real-device parts of FR-022, FR-023, FR-028, FR-031 and FR-034.
