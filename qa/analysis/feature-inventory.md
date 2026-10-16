# Feature Inventory

23 features were identified: 20 user-facing or workflow features (F01–F20), 2 backend endpoints not used by the frontend (F21–F22) and 1 tool (F23).
Status values: **Confirmed** (implemented and wired), **Partial** (implemented but not wired, or incomplete), **Unused**.
The "Smoke coverage" column names sections of `SmokeTests/Program.cs` (see `testing-infrastructure.md`). Paths: FE = `Frontend/`, BE = `Backend/`.

## 1. Summary table

| ID | Feature | Frontend files | Backend files | Core logic | Dependencies | Status |
|---|---|---|---|---|---|---|
| F01 | Drillhole list retrieval | `Api/Core-information/drillholes/DrillholesClient.cs`, `Api/ApiClient.cs`, `Pages/CoreInformation/CoreInformationViewModel.cs` | `Controllers/…/Drillhole/DrillholeController.cs`, `Repositories/…/Drillhole/*`, `Models/…/DrillholeResponse.cs` | SQL projection (BE); `ApiResult` mapping and messages (FE) | SQL Server, HTTP | Confirmed |
| F02 | Offline drillhole cache | `Api/Core-information/drillholes/DrillholeCache.cs` | — | Replace-all transaction; empty or failed responses never overwrite | `measurements.db` | Confirmed |
| F03 | Searchable drillhole selection | `Pages/CoreInformation/{DrillholeCatalog,DrillholeOption,CoreInformationViewModel,CoreInformationPage.xaml(.cs)}.cs`, `Shared/Infrastructure/{BulkObservableCollection,SameItemConverter}.cs` | — | Filter, rank, group, diff-apply, debounce, versioning | Measurement history | Confirmed |
| F04 | Core Information form + Save & Continue | `CoreInformationViewModel.cs`, `Shared/Models/CoreSnapshot.cs`, `ViewModels/MainViewModel.cs` | — | Required drillhole, Operator autofill, `CoreSnapshot` | Windows identity | Confirmed |
| F05 | Backend connectivity indicator | `Api/BackendConnection.cs`, `Api/ApiClient.cs`, `Api/ApiSettings.cs`, `MainWindow.xaml` | (any HTTP answer) | HEAD probe loop, no overlapping checks, `CameOnline` | Network | Confirmed |
| F06 | CT007-M Bluetooth selection and monitoring | `Calculation/Core-Information/BluetoothDevice/*`, `DeviceStatusMonitor.cs`, `DeviceSettings.cs`, `device-settings.json` | — | Identification (prefix/address), connected vs. paired, stale-result discard | Windows Bluetooth | Confirmed (informational only) |
| F07 | Measurement DB status | `Calculation/Core-Information/{MeasurementDatabase/MeasurementDatabaseCheck,SqliteFileCheck,LocalStatus}.cs` | — | Read-only open → `LocalState` | `measurements.db` | Confirmed |
| F08 | Gamma Guard DB discovery + status | `Calculation/Core-Information/GammaGuardDatabase/GammaGuardDatabaseCheck.cs`, `SqliteFileCheck.cs` | — | Configured path or depth-limited search, newest wins | File system, vendor DB | Confirmed |
| F09 | Toasts and status announcements | `Shared/Infrastructure/ToastService.cs`, `ViewModels/StatusAnnouncements.cs`, `MainWindow.xaml` | — | De-duplication, max 4, durations, announce on transition only | Timers | Confirmed |
| F10 | Navigation and sidebar | `ViewModels/MainViewModel.cs`, `MainWindow.xaml`, `Shared/Infrastructure/SidebarItem.cs` | — | `CurrentPage`-derived selection | — | Confirmed |
| F11 | Drillhole measurement sessions | `Pages/Measurement/MeasurementViewModel.cs` (`SetCore`, `SelectDrillhole`, `OpenDrillholeAsync`, `NewMeasurement`, `RestoreLatestSessionAsync`), `MeasurementRepository.cs` | — | One session per Drillhole ID; selection lock | `measurements.db` | Confirmed; restore-on-start **Partial** (not wired) |
| F12 | Depth inputs and progression | `Calculation/Measurement/Depth.cs`, `MeasurementViewModel.cs` (`DepthInputsChanged`, `CaptureAsync`) | — | m/cm ↔ mm integer arithmetic; advance once per capture | `MeasurementProgress` | Confirmed |
| F13 | Capture duration setting | `Calculation/Measurement/CaptureDurationSettings.cs`, `MeasurementViewModel.cs`, `MeasurementPage.xaml(.cs)` | — | 2–10 s whole seconds, atomic save, invalid → 5 | `capture-settings.json` | Confirmed |
| F14 | Measurement mode and right-click trigger | `MainWindow.xaml.cs`, `MeasurementViewModel.cs`, `MeasurementState.cs`, `CountdownRing.cs` | — | State machine, `CanTrigger` gate, countdown/progress, cancellation | Mouse input, clock | Confirmed |
| F15 | Device capture from Gamma Guard | `Calculation/Measurement/{DeviceCaptureService,DeviceSamples}.cs`, `GammaGuardDatabase/{GammaGuardCaptureDevices,GammaGuardDeviceDatabase}.cs` | — | Detector resolution, window, polling, de-duplication, averaging, time-basis check | Vendor DB, clock | Confirmed |
| F16 | Results table (order, numbering, formatting) | `Calculation/Measurement/MeasurementOrder.cs`, `Pages/Measurement/{MeasurementColumns,MeasurementColumnLayout,MeasurementCellFormat,MeasurementResult,MeasurementPage.xaml.cs,ScaleExtension}.cs` | — | From order with tie-breaks; display-only formatting | Local time zone | Confirmed |
| F17 | Delete measurement | `MeasurementViewModel.cs` (`Delete`, `DeleteAsync`), `Windows/ConfirmationWindow/*`, `IDialogService.cs` | — | Confirm → optimistic remove → restore on failure | `measurements.db` | Confirmed |
| F18 | Persistence and schema migration | `Pages/Measurement/MeasurementRepository.cs` | — | Upserts, transactions, migrations v1→v5, legacy path move | SQLite file, WAL | Confirmed |
| F19 | CSV export | `Pages/Measurement/CsvExportService.cs`, `MeasurementColumns.cs` | — | Column set, quoting, formula neutralisation, invariant formats | `SaveFileDialog`, file system | Confirmed |
| F20 | Depth bar chart (+ demo ts_ms) | `Pages/Measurement/DepthBarChart.cs`, `MeasurementResult.cs` (`DisplayTsMs`), `MeasurementViewModel.AssignDemoTsMs` | — | Axis scaling, hit-testing tooltip | — | Confirmed (demo mode off in the app) |
| F21 | Backend Bluetooth probe endpoint | — | `…/BluetoothDevice/*` | Connected CT007-M on the server host | WinRT | **Unused** by FE |
| F22 | Backend CT008 SQLite discovery endpoint | — | `…/SqliteDatabase/*` | Two-phase search, 1 min cache, 20 s timeout | File system | **Unused** by FE |
| F23 | DB inspection script | `tools/check-db.ps1` | — | Read-only queries through `winsqlite3.dll` | `measurements.db` | Confirmed; **Observation:** written for schema v4 |

## 2. Feature details

Each entry lists the flow, inputs/outputs, state changes, failure areas, smoke coverage and open questions.

### F01 Drillhole list retrieval
- **Flow:** `integration-map.md` §2.1.
- **In:** none. **Out:** `[{holeId, projectCode, campaign}]`. Campaign = `RIGHT(PROSPECT,4)`.
- **State:** `drillholesStatus`, `drillholesError`, catalog; `DrillholesMessage` and `CanRetryDrillholes` are derived from them.
- **Failure areas:** SQL unavailable (503), missing connection string (Inferred: 500), timeout, malformed JSON, empty list, NULL columns, very large table (no paging).
- **Smoke coverage:** FE only, through `StubBackend` (Run: refresh, HTTP 500, offline, recovery). **No backend tests.**
- **Unknown:** whether the 4-character campaign rule holds for every PROSPECT value, and the expected table size.

### F02 Offline drillhole cache
- **Flow:** first load reads `DrillholeCache`. A successful response with ≥1 entry replaces it (`DELETE` + upsert in one transaction).
- **Failure areas:** unreadable cache (treated as empty), a failed replace (old copy kept), the cache sharing its file with measurement data.
- **Smoke coverage:** `DrillholeListChecks` (caching, restart offline, invalid/empty response, transaction).

### F03 Searchable drillhole selection
- **In:** typed text, pick, Enter. **Out:** `DrillholeId`, `DrillholeName` (Project), `ProjectArea` (Campaign).
- **Logic:** `DrillholeCatalog.Options`: case-insensitive *contains*; rank exact > prefix > partial; groups "Measured drillholes" (from `LoadMeasuredDrillholesAsync`, newest first; only IDs present in the catalog) and "All drillholes" (A–Z).
  - Typing never picks an entry.
  - Text that no longer matches the pick clears it.
  - The ComboBox "echo" during a rebuild is ignored (`rebuildingOptions`).
  - Selection is locked while measuring.
- **Failure areas:** stale async filter results, ComboBox echo, 20k-entry performance, caret handling in code-behind (UI-only).
- **Smoke coverage:** Run, `DrillholeSelectionChecks`, `DropdownPerformanceChecks`, `AutofillAndDepthInputChecks`, `DrillholeListChecks`. Keyboard/caret code-behind is untested.

### F04 Core Information form + Save & Continue
- **In:** picked drillhole, Operator (autofilled with the Windows account), Notes. **Out:** `CoreSnapshot` (trimmed values).
- **Validation:** drillhole required → red border + toast "Select a drillhole from the list."
- **State:** editing Operator or Notes raises `Edited`, which calls `Measurement.SetCore(null)` and disarms measurement.
- **Smoke coverage:** Run (lines ~150–170), `ToastChecks`.
- **Unknown:** whether editing Notes while armed should really disarm (current behaviour, by design per the comments).

### F05 Backend connectivity indicator
- **Logic:** HEAD to the base URL every `statusIntervalSeconds`; every request also reports reachability. States: Checking / Online / Offline. `CameOnline` triggers a drillhole reload when the last load failed.
- **Failure areas:** overlapping checks, false Online from an unrelated server, loop lifetime after dispose.
- **Smoke coverage:** Run (polling count, overlap reuse, no data download, legacy endpoints never called), `ToastChecks`.

### F06 CT007-M Bluetooth selection and monitoring
- **Logic:** a device is a CT007-M if its name starts with the prefix `CT007-M-` (case-insensitive), or if its address matches the configured `ct007Address`. Entries are de-duplicated by address. Status values:
  - **Connected:** the live `ConnectionStatus` is Connected.
  - **Disconnected:** the device is only paired or unknown.
  - **Status unavailable:** an exception or timeout occurred.
- The device is never auto-selected. Only the selected device is polled (1 s); the list refreshes every 5 s and when the dropdown opens. Results for a previous selection are discarded.
- **Explicitly has no effect on capture** (`GammaGuardCaptureDevices` remarks).
- **Failure areas:** WinRT timeouts (3 s per call), radio off, a device ID that resolves to another address, list refresh keeping the selection.
- **Smoke coverage:** `LocalDeviceStatusChecks` with `FakeBluetoothReader`/list; `ToastChecks`. **`WindowsBluetooth` (real WinRT) is untested** and needs hardware.

### F07 Measurement DB status
- **Logic:** `SqliteFileCheck.Check`: missing → `Missing` (never created); a read-only open + count of tables → `Available`; SQLITE_BUSY/LOCKED → `Busy`; NOTADB → `Inaccessible`.
- **Smoke coverage:** `LocalDeviceStatusChecks` (missing not created, non-SQLite, exclusive lock = Busy, unmodified).
- **Observation:** `DrillholeCache` opens (and creates) the same file at start-up, so `Missing` is short-lived in practice (Inferred).

### F08 Gamma Guard DB discovery + status
- **Logic:**
  1. When `gammaGuardDatabasePath` is set (environment variables expanded), that path is used as-is.
  2. Otherwise the code searches for folders whose name contains "GammaGuard" (spaces ignored) under 8 roots, at depths 2–4. Inside them it looks for `gammaGuardFileName` files whose parent folder is named `Database`, to depth 3. The newest file wins.
  3. The result is cached until the file disappears or Retry is used (rediscover).
- **Smoke coverage:** `LocalDeviceStatusChecks` (discovery with injected roots, not found, configured missing, disappear → re-search, not modified).
- **Unknown:** the real install layout on target laptops. It differs from the backend's "CT008" rule.

### F09 Toasts and status announcements
- **Logic:** Error 5 s, others 3 s. An identical visible toast is restarted rather than duplicated. At most 4 toasts; the oldest goes first.
  - Backend: announces Offline, and Online again only after being Offline.
  - DB rows: announced from the second known state onward.
  - Bluetooth: the first confirmed state after a selection, then every change.
- **Smoke coverage:** `ToastChecks` (controllable delay injected).

### F10 Navigation and sidebar
- **Logic:** `ShowCore/ShowMeasurement` commands; Save & Continue navigates; sidebar selection is derived from `CurrentPage`; collapse/expand. ViewModels persist across navigation, and the pages are recreated.
- **Smoke coverage:** Run (`MainViewModel` checks: selection, retained form, no duplicated polling). XAML templates/triggers are UI-only.

### F11 Drillhole measurement sessions
- **Logic:**
  - `ResolveSessionAsync`: insert-or-ignore on the unique `DrillholeID`; the stored metadata wins, with a warning toast when it differs.
  - `SelectDrillhole`: empties the view, saves unsaved rows first, and loads the stored session without creating one.
  - `NewMeasurement`: refused while there are unsaved rows; otherwise clears the session and the core.
  - A version counter discards late loads. Rows typed in while a session is opening are preserved (`depthsTypedWhileOpening`).
- **Partial:** `RestoreLatestSessionAsync` is implemented and smoke-tested but never called by `MainViewModel`. **Unknown:** whether restore-on-start is required.
- **Smoke coverage:** Run, `DrillholeSelectionChecks`, `SqliteChecks` (concurrent resolve → one session).

### F12 Depth inputs and progression
- **Logic:**
  - Starting Depth: metres, ≥ 0, with ≤ 3 decimals. Step: centimetres, > 0, with ≤ 1 decimal. Both accept `.` or `,`.
  - Depths are stored as integer millimetres. `to = from + step` is fixed at the click.
  - The depth advances to `to` only if Starting Depth was not edited during the capture.
  - Progress is stored as cm (REAL) in `MeasurementProgress` and converted back to mm with rounding.
- **Failure areas:** locale parsing, rounding mm↔cm, edits during a capture, progress saved in the same transaction as the row, migration of mm→cm (v4→v5).
- **Smoke coverage:** `MeasurementUnitsAndOrderChecks`, `AutofillAndDepthInputChecks`, `ProgressMigrationChecks`.

### F13 Capture duration setting
- **Logic:** click the countdown (only when not capturing) → popup editor. Enter saves, Escape cancels. The value is saved to `capture-settings.json` (temp file + move). The running capture keeps its own duration.
- **Smoke coverage:** `CaptureDurationChecks`.

### F14 Measurement mode and right-click trigger
- **Logic:** `MainWindow` handles `PreviewMouseDown` with `handledEventsToo`, right button only. Clicks are accepted only when the window is active, enabled, not minimized and measurement mode is on. The handler marks the event handled and suppresses the context menu. `CanTrigger` guards against duplicate triggers. Stop cancels the capture without producing a row.
- **Failure areas:**
  - Rapid clicks and re-arming.
  - Cancellation during Processing (100 ms hold).
  - Late progress callbacks.
  - Input routing over different controls, and inactive/minimized windows.
  - Closing the window during a capture.
- **Smoke coverage:** Run, using `ControlledMeasurements` against the ViewModel only. **The `MainWindow` input handler is untested**; the README lists 14 manual Windows checks.

### F15 Device capture from Gamma Guard
- **Logic:** `integration-map.md` §2.2.
  - Detector = the serial with the newest `ts_ms` among the last 200 rows. Refused when another serial is within 5 s, or when the DB is empty or unavailable.
  - Time basis: `ts_ms` ≥ 1e11 and within 12 h of the laptop clock.
  - Window `T < ts ≤ T+d`. Expected samples = d / 1 s.
  - Means use each field's valid values; null and NaN are ignored. The device session is kept only when it is uniform across samples.
- **Failure areas:**
  - Clock skew.
  - Two detectors recording at once.
  - Late writes, duplicates (same id, or same session/seq/ts), null `ts_ms`, non-numeric fields.
  - DB locked (2 s busy timeout).
  - The vendor DB must never be modified.
- **Smoke coverage:** `DeviceCaptureChecks` (real SQLite fake GG DB, `FakeCaptureClock`, byte-identical source check).
- **Unknown:** the real device sample rate (1 Hz assumed) and the real Gamma Guard schema and journal mode.

### F16 Results table
- **Logic:**
  - `MeasurementOrder` sorts by From ascending; nulls go last. Ties are broken by start time, then by record ID (stored rows first), then by capture number. `InsertIndex` uses a binary search.
  - `Number` is renumbered on each change.
  - Display formatting: ≤ 2 decimals for device values; `ts_ms` shown as local `yyyy-MM-dd HH:mm:ss`.
  - Column visibility and widths come from `MeasurementColumnLayout`.
  - Incomplete rows show "3 of 5".
- **Smoke coverage:** `TableFormattingChecks`, `MeasurementUnitsAndOrderChecks`, Run (layout minimum widths).

### F17 Delete measurement
- **Logic:** modal confirmation → remove from the UI → `DeleteAsync` (deletes the samples and then the record in one transaction). On failure the row is re-inserted with an error toast. On success the session is re-saved (SortOrder) and a success toast is shown. Capture numbers are never reused.
- **Smoke coverage:** Run (cancel, renumber, numbers not reused, failed delete restores). `ConfirmationWindow` UI is untested.

### F18 Persistence and schema migration
- **Location:** `%LocalAppData%\CT007_Measurement_Studio\measurements.db`. A legacy `CT007 Measurement Studio` folder (with its WAL/SHM files) is moved once, with rollback on an IO error.
- **Schema v5:**
  - `MeasurementSessions(id PK, CreatedAt, DrillholeID UNIQUE, DrillholeName, Project, "operator", comment)`.
  - `MeasurementRecords(id PK, session_id FK, SortOrder, CaptureNo, StartTime, EndTime, Sample, TsMs, Count0, Count0Raw, Hdm, HdmRaw, DoseRate, "from", "to", step [mm], duration_ms, expected_samples, temperature, pressure, device_serial, device_name, device_mac, device_session_id)`, with UNIQUE `(session_id, CaptureNo)`.
  - `MeasurementProgress(session_id PK FK, next_from_cm REAL, step_cm REAL)`.
  - `CaptureSamples(id PK, record_id FK, source_id, serial, …, pressure)`, with UNIQUE `(record_id, source_id)`.
  - `DrillholeCache(hole_id PK, project_code, campaign)`.
- **Migration:**
  - WAL is enabled. Migration runs once per repository instance (lock + flag) inside a single transaction.
  - v1 → v5, including column renames and a table rebuild for cm conversion.
  - Refused when duplicate DrillholeIDs exist (v4), or when the file is newer than the app (> v5).
  - A `foreign_key_check` is run before commit.
- **Write rules:**
  - Saves are upserts; an existing record only has its `SortOrder` updated.
  - A row from another session is rejected with `ArgumentException`.
  - All writes run off the UI thread and go through the serial persistence queue.
- **Smoke coverage:** `SqliteChecks` (v1–v4 legacy upgrades, duplicate refusal, isolation, precision, foreign keys, indexes), `ProgressMigrationChecks`. **`ResolveDefaultPath` legacy move is untested** (internal).

### F19 CSV export
- **Logic:**
  - A `SaveFileDialog` opens with a default name `CT007-measurements-yyyyMMdd-HHmmss.csv`.
  - Rows are serialised before awaiting, written as UTF-8 with BOM to a temp file, then moved into place.
  - Columns: every `MeasurementColumns` entry with `IncludeInCsv`, then Drillhole, Project, Campaign, Operator, Notes, then `session_id` last.
  - Every cell is quoted. Metadata starting with `= + - @` is prefixed with `'`.
  - Times use `O` format, doubles `R`, invariant culture.
  - Mixed-session rows are rejected.
- **Observation:** From/To/Step are **not exported** (`IncludeInCsv: false`, intentional per the comment). **Unknown:** whether the downstream consumer needs the depths.
- **Smoke coverage:** `Serialize` is extensively covered. `ExportAsync` (dialog, file I/O, cancellation) is untested; tests use `FakeExport`.

### F20 Depth bar chart + demo ts_ms
- **Logic:** x = depth (m) from From to To; y = `DisplayTsMs`; nice-number ticks; hover tooltip. Rows without depths or `ts_ms` are not drawn. Demo mode (random 500–5000) is in-memory only and is never saved or exported. It is off in the app.
- **Unknown:** the business meaning of plotting a Unix-ms timestamp as the bar height (likely a placeholder for a measurement value).
- **Smoke coverage:** demo-value and CSV checks in Run. The rendering is untested.

### F21 / F22 Unused backend endpoints
- See `backend-analysis.md` §3.
- **Smoke coverage:** a frontend check asserts they are *never called*. No backend tests.
- **Unknown:** whether to keep, test or remove them.

### F23 check-db.ps1
- **Observation (Inferred, not executed):** the script prints "expected 4" for the schema version and selects `p.next_from` / `p.step`. The v5 migration replaced those columns with `next_from_cm` / `step_cm`, so the summary query would likely fail against a v5 database.
