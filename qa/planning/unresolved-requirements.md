# Unresolved Requirements, Conflicts and Blockers

Each item names the affected tests and source files, and what clarification is needed. Tests whose expectation is **Unresolved** must not be implemented as passing assertions until their item is answered. Tests marked Inferred that cite an item can be implemented against the current behaviour, but they must carry a `// UR-xx` reference so the assertion is revisited.

**Owner key:** PO = product owner or the measurement lead; DEV = development team; QA = test lead; SEC = security or IT.

## A. Missing or ambiguous requirements

| ID | Question | Current behaviour (source) | Affects | Owner |
|---|---|---|---|---|
| UR-01 | **Which document is authoritative?** The root `README.md` is a "design only" draft: it describes a 3 s delay after the trigger, a Bluetooth-button trigger, an in-process backend and `Tests/` projects. The code differs on all four. | Window `click < ts ≤ click+d`, right-click, HTTP backend (`../analysis/testing-constraints.md` §10.1) | All expectations; this is why none is Confirmed. BE-020, MAN-002 | PO |
| UR-02 | What is the real CT007-M sample rate? Expected samples = duration ÷ 1 s. | `CaptureWindow.ExpectedSamples` (`Frontend/Calculation/Measurement/DeviceSamples.cs`) | BE-021, FE-030, MAN-001, MAN-003 | PO + hardware check |
| UR-03 | Is 2–10 s (default 5 s) the right capture-duration range? | `CaptureDurationSettings` constants | BE-030, FE-032 | PO |
| UR-04 | **CSV contract.** Should From/To/Step be exported? Temperature and pressure are stored but not exported or shown. Is a `,`-separated UTF-8-BOM file right for users whose Excel uses `;`? | `MeasurementColumns.cs` lines 43–50 (`IncludeInCsv: false`, intentional per the comment) | BE-042, INT-014, MAN-010 | PO + downstream consumer |
| UR-05 | What should the depth bar chart plot? Today its y-axis is `ts_ms` (a Unix timestamp). | `DepthBarChart.cs`, `MeasurementResult.DisplayTsMs` | FE-050 | PO |
| UR-06 | Should the app restore the latest session on start-up? | `RestoreLatestSessionAsync` is implemented but never called | FE-051 | PO |
| UR-07 | Should the unused endpoints `bluetooth-device` and `sqlite-database` be kept, tested or removed? Their CT008 discovery rule conflicts with the frontend's Gamma Guard rule. | `Backend/**/BluetoothDevice/*`, `Backend/**/SqliteDatabase/*` | BE-012…BE-014 | PO + DEV |
| UR-08 | Campaign = `RIGHT(PROSPECT, 4)`. Is this valid for every PROSPECT value (shorter values, trailing spaces, NULL)? What are the real column types and lengths? | `DrillholeRepository.cs` SQL | BE-006 | PO + data owner |
| UR-09 | Should editing Operator or Notes after Save & Continue disarm measurement mode? | `CoreInformationViewModel` `Edited` → `SetCore(null)` (by design per the comments) | FE-021 | PO |
| UR-10 | What is the source of the 5 s "simultaneous detectors" threshold and the 12 h clock tolerance? The boundaries are exactly 5000 ms (refused) and exactly 12 h (accepted). | `GammaGuardCaptureDevices.SimultaneousWindow`, `DeviceTime.MaxClockDifference` | BE-023, BE-024, MAN-004 | PO |
| UR-13 | Should reachability use a real health endpoint? Today any HTTP answer at the base URL counts as Online, including from a foreign service. | `ApiClient.ProbeAsync`, `BackendConnection` | FE-006, BE-011, MAN-015 | DEV |
| UR-15 | What are the real Gamma Guard schema, journal mode (WAL or rollback), write cadence and `ts_ms` basis? None of these has been verified against a real device DB. | Assumed schema in `../analysis/integration-map.md` §3 | BE-027, INT-008, MAN-001 | DEV + hardware check |
| UR-16 | What is the Gamma Guard install layout on the target laptops? The frontend searches a "GammaGuard" folder + `Database\*.sqlite`; the backend searches "CT008" + `ct008-log.sqlite`. | `GammaGuardDatabaseCheck.cs`, `SqliteDatabaseRepository.cs` | BE-029, MAN-014 | DEV + field IT |
| UR-17 | Should an **incomplete capture** (fewer samples than expected) be saved? Is there a minimum sample count, or a flag in the CSV? | Saved with a warning toast; `Sample` shows "3 of 5" (`MeasurementViewModel.cs` ~line 691) | FE-030, INT-014 | PO |
| UR-21 | **Non-functional targets** are undocumented: drillhole table size, dropdown latency, backend response time, session length, and accessibility level (WCAG). | Smoke tests use 20k entries; no targets | FE-017, BE-007, MAN-009, MAN-016 | PO |
| UR-22 | Which time format do downstream consumers need? The CSV uses ISO-8601 `O` with the local offset at capture time; the table shows local time. | `CsvExportService.Format`, `MeasurementCellFormat` | BE-042, MAN-011 | PO + consumer |
| UR-23 | **Is data loss acceptable** in these cases? (a) Stop during the 100 ms Processing hold discards a completed capture. (b) Close waits at most 5 s for queued saves, then gives up. (c) The process is killed with unsaved rows. | `MeasurementViewModel.CaptureAsync` (the hold is cancellable), `Dispose` | FE-028, FE-048, MAN-007 | PO |
| UR-28 | Are two app instances at once (two windows, or two users on one PC) supported? Both would share `measurements.db`. | No single-instance guard was found in Phase 1 | BE-038 | PO + DEV |
| UR-30 | When an existing session's stored metadata (Project, Campaign, Operator, Notes) differs from the form, should the stored values win? | Stored values win, with a warning toast (`MeasurementViewModel.cs` line 392) | FE-037 | PO |

## B. Conflicts between implementation, documentation and Phase 1

| ID | Conflict | Evidence | Affects | Clarification needed |
|---|---|---|---|---|
| UR-11 | `tools/check-db.ps1` targets schema **v4**: line 142 says "expected 4", and line 144 selects `p.next_from` / `p.step`. The repository writes **v5**, with `next_from_cm` / `step_cm`. A likely defect, if the script is still maintained. | Read in Phase 2 | BE-045 | DEV: update or retire the script |
| UR-12 | What should the backend return for each failure? A missing connection string probably gives **500** (the DI constructor throws outside the controller's `try`), and so do non-SQL exceptions. Only `SqlException` gives 503. Should a misconfiguration fail at start-up or give 503? | `DrillholeRepository` constructor, `DrillholeController` | BE-003, BE-004 | DEV |
| UR-14 | **Security posture.** There is no authentication; `AllowedHosts: *`; Swagger is enabled in every environment; **plaintext SQL credentials are committed** in `Backend/appsettings.json`. What is the intended deployment (localhost only, or LAN), and what is the secret-management policy? | `Backend/Program.cs`, `appsettings.json` | BE-009, BE-010 | SEC. The committed credentials should be rotated whatever the answer |
| UR-18 | **CSV formula neutralisation scope.** Only metadata starting with `= + - @` (after leading whitespace) is prefixed with `'`. Values starting with a tab or CR are not covered (OWASP lists both). Measurement cells are not covered either (they are app-generated numbers). | `CsvExportService.Text` | BE-043, MAN-012 | SEC + DEV |
| UR-19 | **Drillhole ID case sensitivity.** The catalog de-duplicates case-insensitively, but the `DrillholeID UNIQUE` index in SQLite is case-sensitive by default (Inferred), so `dh-1` and `DH-1` could become two sessions. | `DrillholeCatalog.Build`, `MeasurementRepository.cs` schema | BE-031 | PO: are IDs case-insensitive? |
| UR-20 | **Phase 1 inaccuracy (recorded here; the Phase 1 docs are unchanged).** `../analysis/frontend-analysis.md` §5 and `feature-inventory.md` F12 attribute "Step > 0" to `Depth.TryParseCentimetres`. The parser actually accepts `0` (it only rejects `< 0`). The `> 0` rule is in `MeasurementViewModel.cs` line 463. The behaviour is the same, but unit tests of the parser must expect 0 to be accepted. | `Depth.cs`, `MeasurementViewModel.cs:463` | BE-016, FE-024 | None; recorded for accuracy |
| UR-29 | **New observation.** The clock/time-basis check runs only when the detector already has a sample (`LatestTsMs` not null). For a detector's first-ever capture it is skipped, so a seconds-based `ts_ms` would silently yield 0 samples instead of the explanatory error. Is that intended? | `DeviceCaptureService.CaptureAsync` | BE-026 | DEV |

## C. Testing blockers and prerequisites

| ID | Blocker | Impact | Needed to unblock |
|---|---|---|---|
| UR-24 | **No frontend test can build.** `SmokeTests.csproj` targets `net10.0-windows`; `Frontend.csproj` targets `net10.0-windows10.0.19041.0` (NU1201, re-confirmed in Phase 2). There is no test framework or per-test reporting, and no backend test project. | Every automated FE, BE Part 2 and INT case | A decision on whether to retarget the smoke harness or create xUnit projects (`test-plan.md` §6). Permission for Phase 3 to create test projects and add test-only NuGet packages |
| UR-25 | There is no disposable SQL Server, and the real `ZOOVCH_COLLAR_LIST` schema (types, lengths, collation, row count) is unknown. | BE-006, BE-007 | A LocalDB or container instance; the DDL from the data owner |
| UR-26 | Hardware availability: CT007-M detectors (2 for MAN-004), a Gamma Guard licence or installation, a check source, and target laptop images. | MAN-001…MAN-005, MAN-014 | Bench booking; a contact person |
| UR-27 | **Test seams that would need production changes** (not allowed without approval): an `InternalsVisibleTo` for `SelectMostRecent`, `DiscoverDatabasePath` and `ResolveDefaultPath`; `public partial class Program` for `WebApplicationFactory` (if it isn't already accessible); an injectable data folder for the real exe (`%LocalAppData%` comes from a known-folder API, Inferred); an injectable `SaveFileDialog`. | BE-013, BE-041, BE-044, ENV-API, ENV-UIA | DEV approval for minimal test seams, or accepting the workarounds in this plan (a dedicated test account, manual export tests) |

Count: **30 items** (UR-01…UR-30). Section A has 19, B has 7, C has 4.
