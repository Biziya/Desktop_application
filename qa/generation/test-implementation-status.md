# Test Implementation Status (Phase 3)

Generated 2026-10-16 from `qa/planning/test-inventory.md`.

**No test has been compiled or executed.** "Implemented" means the test code exists. Whether it compiles and whether it passes are both unknown until Phase 4. No production file, existing test, solution file, Phase 1 document or Phase 2 document was changed.

## 1. Framework and layout

**Framework decision.** The repository has no test framework. `SmokeTests` is a custom console harness, and the local NuGet cache holds no xUnit, NUnit, MSTest, Mvc.Testing or FlaUI package, which could not be installed in this phase. The new project therefore follows the SmokeTests convention:
- a console executable with an STA thread and a WPF dispatcher;
- reusing its fake designs;
- adding a small runner (`TestRunner.cs`) that gives one PASS/FAIL/SKIP result per test, optional ID filters, opt-in categories and a CSV results file.

It needs **no new package**. It uses:
- the ASP.NET Core shared framework, which ships with the SDK;
- WPF's built-in UI Automation client;
- the packages Frontend and Backend already reference (Microsoft.Data.Sqlite, Microsoft.Data.SqlClient, Swashbuckle).

**Why a new project.** `SmokeTests.csproj` cannot build against the Frontend (NU1201, UR-24), and existing tests must not be modified. The new project targets `net10.0-windows10.0.19041.0`. `SmokeTests` is untouched.

```text
Tests/
├── README.md                       how to run, opt-in categories, isolation
├── .gitignore                      bin/ obj/
└── CT007.Tests/
    ├── CT007.Tests.csproj          references Frontend + Backend; removes Backend appsettings*.json from its output
    ├── TestRunner.cs               [Test("ID", title, Category, TimeoutSeconds)] + runner
    ├── GlobalUsings.cs
    ├── Fixtures/                   Assert, TestFolder, Fakes (HTTP, repository, capture, clock, Bluetooth, Gamma Guard file),
    │                               Make (builders), TestBackend (in-process Kestrel), AppDatabase (SQLite helpers, schema 1–4 fixtures)
    ├── Frontend/                   FE tests (ViewModels, API client, presentation)
    ├── Backend/                    BE tests (API, capture logic, repository/migrations, CSV, local files)
    ├── Integration/                INT tests (HTTP, capture pipeline)
    └── EndToEnd/                   E2E tests (DesktopBench, DesktopWorkflowTests)
```

**Traceability.** Every test method carries `[Test("<ID>", "<title>")]` with its inventory ID. BE-024, BE-027 and FE-036 each have two methods (two aspects of one scenario).

**Interpretation of the selection rule.** Phase 2 has **no Confirmed expectations**, because no approved requirements exist (UR-01).
- Scenarios whose expectation is **Inferred** from code were implemented as regression tests. Assertions that rest on an open question name the UR item in the test title or a comment.
- Scenarios with **Unresolved** expectations were not implemented.
- A passing test therefore shows the code matches the *inferred* behaviour, not that a requirement is met.

## 2. Summary

| | Count |
|---|---|
| Planned scenarios (inventory) | 135 |
| Planned automatable/conditional/blocked (non-manual) | 118 |
| **Implemented** scenarios / test methods | **104 / 107** |
| — Frontend (FE) | 48 |
| — Backend (BE) | 35 |
| — Integration (INT) | 17 |
| — End-to-end (E2E) | 4 |
| Existing (counted as covered by SmokeTests) | 0 (the overlap is noted per row; SmokeTests cannot build, so it gives no runnable coverage) |
| Unresolved (not implemented) | 11 |
| Blocked | 2 |
| Conditional (not implemented) | 1 |
| Manual | 17 |

Implemented tests run in the default set unless they carry a category:
- **Timing** (9): FE-006, FE-008, FE-023, FE-028, FE-048, INT-003, INT-004, INT-008, INT-016.
- **Sql** (1): BE-006.
- **E2E** (4): E2E-001…004.

The default run therefore has 90 scenarios.

## 3. Mapping

Paths are relative to `Tests/CT007.Tests/`. Notes give the method name, the overlapping SmokeTests section (all of which cannot currently build), and deviations from the planned case. The deviations are explained in §4.

| Test ID | Test File | Implementation Status | Notes |
|---|---|---|---|
| FE-001 | Frontend/ApiClientTests.cs | Implemented | `OkResponse`; overlaps SmokeTests Run |
| FE-002 | Frontend/ApiClientTests.cs | Implemented | `HttpErrors` (404/500/503) |
| FE-003 | Frontend/ApiClientTests.cs | Implemented | `Unreachable`; the timeout case waits about 1 s |
| FE-004 | Frontend/ApiClientTests.cs | Implemented | `InvalidPayloads`; JSON `null` gives a different message (D1) |
| FE-005 | Frontend/ApiClientTests.cs | Implemented | `SettingsDefaults`; the text-timeout case is expected to fail (suspected defect D2) |
| FE-006 | Frontend/ApiClientTests.cs | Implemented | `ProbePolling`; Timing |
| FE-007 | Frontend/ApiClientTests.cs | Implemented | `ReloadAfterRecovery` |
| FE-008 | Frontend/ApiClientTests.cs | Implemented | `LoopsStopOnDispose`; Timing; probes only (the Bluetooth list is not counted) |
| FE-009 | Frontend/DrillholeSelectionTests.cs | Implemented | `CacheThenServer`; overlaps `DrillholeListChecks` |
| FE-010 | Frontend/DrillholeSelectionTests.cs | Implemented | `FailedRefreshKeepsState` |
| FE-011 | Frontend/DrillholeSelectionTests.cs | Implemented | `EmptyListKeepsSaved` |
| FE-012 | Frontend/DrillholeSelectionTests.cs | Implemented | `LoadIsReused` |
| FE-013 | Frontend/DrillholeSelectionTests.cs | Implemented | `CatalogCleaning`; the first duplicate is kept (UR-19) |
| FE-014 | Frontend/DrillholeSelectionTests.cs | Implemented | `RankingAndGroups` |
| FE-015 | Frontend/DrillholeSelectionTests.cs | Implemented | `NoImplicitPick`; the ComboBox echo is simulated from `CollectionChanged` |
| FE-016 | Frontend/DrillholeSelectionTests.cs | Implemented | `DebounceAndStaleResults`; the filter-run count is not observable (D3) |
| FE-017 | — | Unresolved | No latency target (UR-21) |
| FE-018 | Frontend/DrillholeSelectionTests.cs | Implemented | `UnicodeIds` |
| FE-019 | Frontend/CoreInformationTests.cs | Implemented | `SaveWithoutPick` |
| FE-020 | Frontend/CoreInformationTests.cs | Implemented | `SaveAndContinue` |
| FE-021 | Frontend/CoreInformationTests.cs | Implemented | `EditDisarms` (UR-09) |
| FE-022 | Frontend/CoreInformationTests.cs | Implemented | `SelectionLock`; checks both typing and picking |
| FE-023 | Frontend/CoreInformationTests.cs | Implemented | `NavigationKeepsState`; Timing |
| FE-024 | Frontend/MeasurementWorkflowTests.cs | Implemented | `DepthValidation`; Start stays enabled and explains in a toast (D4) |
| FE-025 | Frontend/MeasurementWorkflowTests.cs | Implemented | `StartRequirements` |
| FE-026 | Frontend/MeasurementWorkflowTests.cs | Implemented | `CaptureCycle` |
| FE-027 | Frontend/MeasurementWorkflowTests.cs | Implemented | `StopWhileCapturing` |
| FE-028 | Frontend/MeasurementWorkflowTests.cs | Implemented | `StopWhileProcessing`; Timing (real 100 ms hold); 20 repetitions; UR-23 |
| FE-029 | Frontend/MeasurementWorkflowTests.cs | Implemented | `DepthProgression` |
| FE-030 | Frontend/MeasurementWorkflowTests.cs | Implemented | `CaptureOutcomes`; the no-sample message comes from the ViewModel (D5); UR-17 |
| FE-031 | Frontend/MeasurementWorkflowTests.cs | Implemented | `LateProgressIgnored` |
| FE-032 | Frontend/MeasurementWorkflowTests.cs | Implemented | `DurationEditor` |
| FE-033 | Frontend/MeasurementWorkflowTests.cs | Implemented | `SortedInsert` |
| FE-034 | Frontend/MeasurementWorkflowTests.cs | Implemented | `DeleteFlow`; overlaps SmokeTests Run |
| FE-035 | Frontend/MeasurementWorkflowTests.cs | Implemented | `SaveFailureRetry`; all 4 retry points |
| FE-036 | Frontend/MeasurementWorkflowTests.cs | Implemented | `DrillholeSwitch`, `SwitchRefusedWhileUnsaved` |
| FE-037 | Frontend/MeasurementWorkflowTests.cs | Implemented | `StoredMetadataWins` (UR-30) |
| FE-038 | Frontend/MeasurementWorkflowTests.cs | Implemented | `NewMeasurementFlow` |
| FE-039 | Frontend/MeasurementWorkflowTests.cs | Implemented | `ExportCommandFlow` |
| FE-040 | Frontend/DrillholeSelectionTests.cs | Implemented | `MeasuredGroupRefresh`; checks the MainViewModel wiring itself (no manual refresh) |
| FE-041 | Frontend/StatusAndPresentationTests.cs | Implemented | `ToastRules`; the delay is injected |
| FE-042 | Frontend/StatusAndPresentationTests.cs | Implemented | `Announcements` |
| FE-043 | Frontend/StatusAndPresentationTests.cs | Implemented | `BluetoothSelection`; the configured address replaces prefix matching (D6) |
| FE-044 | Frontend/StatusAndPresentationTests.cs | Implemented | `DatabaseRowsAndRetry` |
| FE-045 | Frontend/StatusAndPresentationTests.cs | Implemented | `DeviceSettingsLoad`; variables are expanded at check time (D7) |
| FE-046 | Frontend/StatusAndPresentationTests.cs | Implemented | `CellFormatting`; the local-time check uses the machine time zone |
| FE-047 | Frontend/StatusAndPresentationTests.cs | Implemented | `ColumnLayouts` |
| FE-048 | Frontend/MeasurementWorkflowTests.cs | Implemented | `CloseDuringWork`; Timing; documents data loss after 5 s (UR-23) |
| FE-049 | Frontend/MeasurementWorkflowTests.cs | Implemented | `DemoTsMs` |
| FE-050 | — | Unresolved | Chart semantics (UR-05); geometry is not exposed, so it is manual (MAN-008) |
| FE-051 | — | Unresolved | Restore on start-up (UR-06); also Blocked in the plan |
| FE-060 | Frontend/UploadDataTests.cs | Implemented | `ImportRoundTrip`: exported CSV → import (metadata, timestamps, depths, values, order) |
| FE-061 | Frontend/UploadDataTests.cs | Implemented | `ImportStructureErrors`: empty file, header only, missing/duplicate columns |
| FE-062 | Frontend/UploadDataTests.cs | Implemented | `ImportValueErrors`: invalid numbers/times/depths with row and line |
| FE-063 | Frontend/UploadDataTests.cs | Implemented | `ImportCsvSyntax`: quoting, line breaks, LF, BOM, reordered and unknown columns |
| FE-064 | Frontend/UploadDataTests.cs | Implemented | `LegacyCsv`: exports without depths / Created Username |
| FE-065 | Frontend/UploadDataTests.cs | Implemented | `ImportAndDelete`: Upload Data import, confirmed delete, CSV file unchanged |
| FE-066 | Frontend/UploadDataTests.cs | Implemented | `UploadFlow`: request body, busy state, failure and retry, no repeat after success |
| FE-067 | Frontend/UploadDataTests.cs | Implemented | `UploadPreconditions` |
| FE-068 | Frontend/UploadDataTests.cs | Implemented | `AlarmRules`: CPS threshold below/equal/above, no overlap, reset |
| FE-069 | Frontend/UploadDataTests.cs | Implemented | `AlarmDuringMeasurement`: one alarm per completed row; capture continues |
| FE-070 | Frontend/UploadDataTests.cs | Implemented | `ChartShowsCps` |
| FE-071 | Frontend/UploadDataTests.cs | Implemented | `InputAlignmentAndNumberColumn` (WPF views) |
| BE-001 | Backend/DrillholeApiTests.cs | Implemented | `DrillholesContract`; in-process host (D8) |
| BE-002 | Backend/DrillholeApiTests.cs | Implemented | `SqlFailureIs503`; a real `SqlException` from a refused localhost port |
| BE-003 | — | Unresolved | Expected status unknown (UR-12) |
| BE-004 | — | Unresolved | Expected status unknown (UR-12) |
| BE-005 | Backend/DrillholeApiTests.cs | Implemented | `RequestCancellation` |
| BE-006 | Backend/DrillholeApiTests.cs | Implemented | `SqlProjection`; category Sql; skipped without `CT007_TEST_SQL`; refuses non-local servers; trailing-space row not asserted (UR-08) |
| BE-007 | — | Unresolved | No size or time target (UR-21); needs SQL Server (UR-25) |
| BE-008 | Backend/DrillholeApiTests.cs | Implemented | `OpenApiDocument` |
| BE-009 | — | Unresolved | Intended auth and deployment posture unknown (UR-14) |
| BE-010 | — | Unresolved | Secret-management policy (UR-14) and a scanner tool, which is not installed; the failure is already known (credentials committed) |
| BE-011 | Backend/DrillholeApiTests.cs | Implemented | `RoutingContract` (UR-13) |
| BE-012 | — | Unresolved | Fate of the unused endpoint (UR-07); host-dependent |
| BE-013 | — | Blocked | `internal` methods with no `InternalsVisibleTo` (UR-27); also UR-07 |
| BE-014 | — | Unresolved | UR-07; it would scan the real user profile |
| BE-015 | Backend/DepthAndOrderTests.cs | Implemented | `ParseMetres`; 3 cultures |
| BE-016 | Backend/DepthAndOrderTests.cs | Implemented | `ParseCentimetres` (UR-20) |
| BE-017 | Backend/DepthAndOrderTests.cs | Implemented | `Formatting` |
| BE-018 | Backend/DepthAndOrderTests.cs | Implemented | `NoCumulativeDrift` |
| BE-019 | Backend/DepthAndOrderTests.cs | Implemented | `Ordering`; property check with seed 42 |
| BE-020 | Backend/CaptureLogicTests.cs | Implemented | `WindowMembership` (UR-01) |
| BE-021 | Backend/CaptureLogicTests.cs | Implemented | `ExpectedSamples` (UR-02) |
| BE-022 | Backend/CaptureLogicTests.cs | Implemented | `Averaging` |
| BE-023 | Backend/CaptureLogicTests.cs | Implemented | `TimeBasis` (UR-10) |
| BE-024 | Backend/CaptureLogicTests.cs | Implemented | `DetectorPick`, `ResolveNeedsAvailableDatabase` (UR-10) |
| BE-025 | Backend/CaptureLogicTests.cs | Implemented | `PollingAndFinalRead`; scenarios a–g |
| BE-026 | Backend/CaptureLogicTests.cs | Implemented | `TimeCheckAndCancellation` (UR-29) |
| BE-027 | Backend/LocalFilesTests.cs | Implemented | `VendorDatabaseReadOnly`, `BrokenOrLockedDatabase`; rollback and WAL; the busy case waits 2 s |
| BE-028 | Backend/LocalFilesTests.cs | Implemented | `FileCheckStates`; the ACL deny-read case was not implemented (it needs ACL setup) |
| BE-029 | Backend/LocalFilesTests.cs | Implemented | `Discovery` (UR-16) |
| BE-030 | Backend/LocalFilesTests.cs | Implemented | `DurationSettings`; the text-value case is expected to fail (suspected defect D2) |
| BE-031 | Backend/MeasurementRepositoryTests.cs | Implemented | `SessionResolution`; case sensitivity not asserted (UR-19) |
| BE-032 | Backend/MeasurementRepositoryTests.cs | Implemented | `AtomicSave`; the failure is injected with a row from another session |
| BE-033 | Backend/MeasurementRepositoryTests.cs | Implemented | `IsolationAndUniqueCaptureNumbers` (D9) |
| BE-034 | Backend/MeasurementRepositoryTests.cs | Implemented | `Delete`; the locked-DB mid-transaction case was not implemented |
| BE-035 | Backend/MeasurementRepositoryTests.cs | Implemented | `RoundTripPrecision` |
| BE-036 | Backend/MeasurementRepositoryTests.cs | Implemented | `Migrations`; schema 1–4 fixtures are built in code (`AppDatabase.Legacy`) |
| BE-037 | Backend/MeasurementRepositoryTests.cs | Implemented | `MigrationRefusals`; duplicate fixture is schema 3; contents asserted instead of bytes (D10) |
| BE-038 | Backend/MeasurementRepositoryTests.cs | Implemented | `MigrationIdempotence`; a failure of the concurrent part is evidence for UR-28 |
| BE-039 | Backend/MeasurementRepositoryTests.cs | Implemented | `MeasuredOrdering` |
| BE-040 | Backend/MeasurementRepositoryTests.cs | Implemented | `DrillholeCacheReplace`; the empty-list and corrupt-cache handling are in the page (D11) |
| BE-041 | — | Blocked | `ResolveDefaultPath` is `internal` and targets the real profile (UR-27) |
| BE-042 | Backend/CsvExportTests.cs | Implemented | `Structure` (UR-04, UR-22); golden text in code |
| BE-043 | Backend/CsvExportTests.cs | Implemented | `InjectionAndMixing`; tab/CR prefixes not asserted (UR-18) |
| BE-044 | — | Conditional | `SaveFileDialog` is not injectable; the file path is covered by E2E-001 and MAN-013 |
| BE-045 | — | Unresolved | Whether the script is maintained (UR-11); suspected defect already recorded |
| BE-046 | Backend/MeasurementRepositoryTests.cs | Implemented | `TextFields` |
| BE-050 | Backend/MeasurementUploadApiTests.cs | Implemented | `SuccessfulUpload` |
| BE-051 | Backend/MeasurementUploadApiTests.cs | Implemented | `InvalidRequests` |
| BE-052 | Backend/MeasurementUploadApiTests.cs | Implemented | `Fingerprint` |
| BE-053 | Backend/MeasurementUploadApiTests.cs | Implemented | `Duplicates` (in-memory repository with the same duplicate rules) |
| BE-054 | Backend/MeasurementUploadApiTests.cs | Implemented | `DatabaseUnavailable` |
| BE-055 | Backend/MeasurementUploadApiTests.cs | Implemented | `UploaderIdentity` |
| BE-056 | Backend/MeasurementUploadApiTests.cs | Implemented | `SqlStorage`; category Sql (CT007_TEST_SQL): transaction, rollback, timestamps, retry |
| INT-001 | Integration/HttpIntegrationTests.cs | Implemented | `FieldMapping`; real HTTP to the in-process backend |
| INT-002 | Integration/HttpIntegrationTests.cs | Implemented | `ServiceUnavailablePropagation` |
| INT-003 | Integration/HttpIntegrationTests.cs | Implemented | `OfflineStartAndRecovery`; Timing; backend started later on the same port |
| INT-004 | Integration/HttpIntegrationTests.cs | Implemented | `SlowBackend`; Timing (D12) |
| INT-005 | Integration/HttpIntegrationTests.cs | Implemented | `ContractAlignment` |
| INT-006 | Integration/HttpIntegrationTests.cs | Implemented | `CacheAcrossRestart` |
| INT-007 | Integration/CapturePipelineTests.cs | Implemented | `CaptureToStorage`; simulated Gamma Guard file |
| INT-008 | Integration/CapturePipelineTests.cs | Implemented | `ConcurrentWriter`; Timing; WAL only (rollback-mode repeat not implemented); UR-15 |
| INT-009 | Integration/CapturePipelineTests.cs | Implemented | `GammaGuardUnavailable`; asserts only that an error toast appears (D13) |
| INT-010 | Integration/CapturePipelineTests.cs | Implemented | `AppDatabaseFailureAndRecovery`; read-only file instead of a lock (D14) |
| INT-011 | Integration/CapturePipelineTests.cs | Implemented | `ReopenMeasuredDrillhole` |
| INT-012 | Integration/CapturePipelineTests.cs | Implemented | `NoSessionMixing`; slow saves come from a wrapper around the real repository |
| INT-013 | Integration/CapturePipelineTests.cs | Implemented | `DeleteReloadExport` |
| INT-014 | Integration/CapturePipelineTests.cs | Implemented | `ValuesAgreeAcrossLayers` (UR-17); expected cells computed, no golden file |
| INT-015 | Integration/CapturePipelineTests.cs | Implemented | `StatusAndCaptureAgree` |
| INT-016 | Integration/CapturePipelineTests.cs | Implemented | `DatabaseStatusUnderLock`; Timing (D15) |
| INT-017 | Integration/CapturePipelineTests.cs | Implemented | `UpgradeThenMeasure` |
| INT-020 | Integration/UploadIntegrationTests.cs | Implemented | `ExportImportUploadWithTimeout`: retry after a client timeout stores once |
| E2E-001 | EndToEnd/DesktopWorkflowTests.cs | Implemented | `MainJourney`; category E2E (D16); context-menu suppression left to MAN-006 |
| E2E-002 | EndToEnd/DesktopWorkflowTests.cs | Implemented | `RestartPersistence`; the Measured-group order is covered by INT-011 instead |
| E2E-003 | EndToEnd/DesktopWorkflowTests.cs | Implemented | `OfflineOperation` |
| E2E-004 | EndToEnd/DesktopWorkflowTests.cs | Implemented | `DeleteWithDialog` |
| MAN-001…MAN-017 | `qa/planning/manual-test-cases.md` | Manual | Not automated in this phase; MAN-001/002/003/014/015/016 also depend on Unresolved items |

## 4. Discrepancies between Phase 2 and the code (recorded here; the planning documents are unchanged)

| # | Case | Finding | Effect on the test |
|---|---|---|---|
| D1 | FE-004 | A JSON `null` body returns `InvalidResponse` with "Backend returned no data.", not "…unexpected data." | Status asserted, message not |
| D2 | FE-005, BE-030 | **Suspected defects.** `ApiSettings.Load` and `CaptureDurationSettings.Load` call `JsonElement.TryGetDouble`/`TryGetInt32` on a string value. That throws `InvalidOperationException`, which neither method catches (`DeviceSettings.Load` does). A settings file with `"requestTimeoutSeconds":"abc"` or `"captureDurationSeconds":"7"` would likely crash start-up instead of falling back to the defaults. | The documented fallback is asserted, so these tests are expected to fail until the code is fixed. Not fixed here (out of scope). |
| D3 | FE-016 | How many times filtering runs is not observable without internals. | The final list and "no stale result applied later" are asserted |
| D4 | FE-024 | By design (a code comment), Start is **not** disabled by invalid or empty depths. Clicking it shows a toast. The plan expected it to be disabled. | Toast and Idle asserted |
| D5 | FE-030 | "No samples" comes from `MeasurementViewModel.BuildResult` (after the 100 ms hold), with the text "No samples were received from CT007-M-0001 during the 5 s capture. Nothing was saved." | That text is asserted |
| D6 | FE-043 | When `ct007Address` is configured, it **replaces** prefix matching (the address only). The plan said prefix *or* address. | The current rule is asserted |
| D7 | FE-045 | `DeviceSettings.Load` keeps `%VAR%` as written. Expansion happens in `GammaGuardDatabaseCheck.Check`. | Expansion checked through the check |
| D8 | BE-001…011 | `WebApplicationFactory` is unavailable (no package). Instead, `TestBackend` builds the same pipeline as `Backend/Program.cs` on a real loopback port. `Program.cs` itself is not executed; if its registrations change, `TestBackend` must change too. | Recorded limitation |
| D9 | BE-033 | Saving a different row with an existing Capture No does **not** fail: `ON CONFLICT … DO UPDATE SET SortOrder` keeps the stored row. | "Stored row unchanged" asserted, no exception expected |
| D10 | BE-037 | (a) Duplicate Drillhole IDs can only exist in a schema-3 file, because schema 4 adds a unique index. (b) The repository runs `PRAGMA journal_mode = WAL` before it checks the version, so even a refused file has its header changed. | Schema-3 fixture; version, schema and rows asserted instead of SHA-256 |
| D11 | BE-040 | `DrillholeCache.ReplaceAsync([])` would clear the cache, and a corrupt cache makes `LoadAsync` throw. The "never replace with empty" and "unreadable = empty" rules are in `CoreInformationViewModel`. | Those rules are tested at page level (FE-011, BE-040 last part) |
| D12 | INT-004 | The client timeout aborts the HTTP request, so the server-side request is cancelled too. Phase 2 assumed it would keep running. | Not asserted |
| D13 | INT-009 | A locked or missing Gamma Guard file during a capture throws `SqliteException`. The user then sees only the generic "Capture failed. Right-click to try again." The plan expected "a readable reason". | "An error toast" asserted. Whether a reason is required is a clarification to add to UR-17/UR-23 in a later planning update. |
| D14 | INT-010 | An exclusive lock makes Microsoft.Data.Sqlite wait up to 30 s per save, so the test makes the file read-only instead (a deterministic, immediate failure). The DB-status part moved to INT-016. | Implemented as a variant |
| D15 | INT-016 | In WAL mode a normal exclusive transaction does **not** block readers, so the status stays Available. The test holds the file with `locking_mode = EXCLUSIVE`, as another program could. | Recorded |
| D16 | E2E | No FlaUI package is available, so the tests use `System.Windows.Automation` (part of WPF) and `user32` mouse and keyboard input. They need a dedicated test account because the app always writes to `%LocalAppData%` (UR-27). Selectors are the accessible names in the XAML. | Category E2E; skipped unless opted in |
| D17 | General | `MainViewModel`'s test constructor always creates a real `DialogService` and `CsvExportService`. | Delete and export tests use `MeasurementViewModel` directly with fakes |

## 5. Dependencies and environment for Phase 4

- **SDK.** .NET SDK 10.0.402 is installed. Restoring needs only packages already in the local cache (from the Frontend/Backend builds). No new NuGet packages.
- **Default run.** Windows with the WPF runtime; temp-folder access; loopback TCP (in-process backend on `127.0.0.1`, random ports).
- **Timing.** An idle machine. Some tests wait 2–6 s each (SQLite busy timeouts, close timeout); INT-008 runs for about 70 s.
- **Sql.** `CT007_TEST_SQL`, pointing at a local `CT007_Test*` database (LocalDB or a container). The test refuses any other server.
- **E2E.** A dedicated Windows account with `CT007_E2E_DEDICATED_ACCOUNT=1`; a built `Frontend.exe`; an interactive, unlocked desktop at 100 % scaling; no legacy `CT007 Measurement Studio` folder.

## 6. Manual actions needed before Phase 4

1. **Build approval.** `dotnet build Tests/CT007.Tests` also builds Frontend and Backend, which rewrites their `obj/` files. Those files are **tracked in git** (15 files). Decide whether to accept that, or to untrack `obj/` and `bin/` first. That would be a repository change outside this phase.
2. **Expect a first round of compile fixes.** The tests were written without a compiler. Any errors must be fixed inside `Tests/` only.
3. **Solution file.** The test project was not added to `CT007.slnx` (production solution configuration). Add it if the team wants it built with the solution.
4. **SmokeTests.** It still cannot build (NU1201). Decide whether to retarget it, retire it, or keep it alongside `CT007.Tests`.
5. **Opt-in environments.** Provide `CT007_TEST_SQL` and the E2E test account if those categories should run.
6. **Unresolved items.** Answer the UR items behind the 11 Unresolved and 2 Blocked scenarios. Also review D2 (suspected defects) and D13.

## 7. Verification performed (static only)

- Every implemented test maps to an inventory ID. The IDs were checked against the inventory by script: there are 104 distinct IDs and no unknown IDs.
- Production files are unchanged: `git status` lists only `Tests/` and `qa/generation/` as new. `SmokeTests/`, `Frontend/`, `Backend/`, `CT007.slnx` and the `qa/analysis` and `qa/planning` documents are untouched.
- Fixtures use only temp folders, synthetic data, loopback HTTP and a fake Gamma Guard file. The Backend's real connection string is never loaded: the content root is an empty temp folder, and `appsettings*.json` is deleted from the test output.
- Not verified: compilation, runtime behaviour, timing stability. These are all left for Phase 4.
