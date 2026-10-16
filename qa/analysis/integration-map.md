# Integration Map

How frontend actions reach the backend and other external systems. In this application the "backend" for most features is **in-process** (frontend services plus SQLite). The HTTP backend serves only drillhole reference data.

## 1. Connection overview

| Feature | Frontend entry point | Backend / service entry point | Database / dependency |
|---|---|---|---|
| Drillhole list | `CoreInformationViewModel.LoadDrillholesAsync` (ctor, Retry, `CameOnline`) → `DrillholesClient.GetAsync` | `GET /api/core-information/drillholes` → `DrillholeController.Get` → `DrillholeRepository.GetAllAsync` | SQL Server `ZOOVCH_COLLAR_LIST` |
| Drillhole cache | `CoreInformationViewModel.LoadDrillholesCoreAsync` | `DrillholeCache.LoadAsync/ReplaceAsync` (in-process) | `measurements.db` table `DrillholeCache` |
| Backend reachability | `BackendConnection.RunAsync/CheckNowAsync` → `ApiClient.ProbeAsync` | `HEAD /` (no route, so 404, which counts as reachable) | Network |
| Measured-drillhole groups | `CoreInformationViewModel.RefreshMeasuredAsync` | `IMeasurementRepository.LoadMeasuredDrillholesAsync` | `measurements.db` |
| Save & Continue / session | `CoreInformationViewModel.Save` → `MeasurementViewModel.SetCore` → `OpenDrillholeAsync` | `SqliteMeasurementRepository.ResolveSessionAsync`, `LoadAsync`, `LoadProgressAsync` | `measurements.db` |
| Drillhole switch | `CoreInformationViewModel.SetDrillhole` → `DrillholeChanged` → `MeasurementViewModel.SelectDrillhole` | `FindSessionAsync`, `LoadAsync`, `LoadProgressAsync` | `measurements.db` |
| Capture | `MainWindow.OnMeasurementMouseDown` → `TriggerCommand` → `MeasurementViewModel.CaptureAsync` | `DeviceCaptureService.CaptureAsync` → `GammaGuardCaptureDevices.Resolve` → `GammaGuardDeviceDatabase` | Gamma Guard SQLite (read-only) |
| Save rows/progress | `MeasurementViewModel.SaveSessionAsync` → `Persist` | `SqliteMeasurementRepository.SaveAsync` | `MeasurementRecords`, `CaptureSamples`, `MeasurementProgress` |
| Delete row | `DeleteCommand` → `DialogService.ConfirmDelete` → `DeleteAsync` | `SqliteMeasurementRepository.DeleteAsync` | `MeasurementRecords`, `CaptureSamples` |
| CSV export | `ExportCommand` → `CsvExportService.ExportAsync` | `CsvExportService.Serialize` | File system (`SaveFileDialog`) |
| Capture duration | `SaveDurationCommand` → `CaptureDurationSettings.Save` | — | `capture-settings.json` |
| Bluetooth status | Device dropdown → `DeviceStatusMonitor.SelectBluetoothDevice` | `Ct007BluetoothCheck` → `WindowsBluetooth` | Windows Bluetooth (WinRT) |
| Measurement DB status | `DeviceStatusMonitor.CheckMeasurementDatabaseAsync` | `MeasurementDatabaseCheck` → `SqliteFileCheck.Check` | `measurements.db` (read-only open) |
| Gamma Guard DB status | `DeviceStatusMonitor.CheckGammaGuardDatabaseAsync` | `GammaGuardDatabaseCheck.Check/Discover` | File system + Gamma Guard SQLite |
| *(unused)* Bluetooth on backend host | — | `GET /api/core-information/bluetooth-device` | WinRT on the server |
| *(unused)* CT008 DB path on backend host | — | `GET /api/core-information/sqlite-database` | File system on the server |

## 2. Request/response flows

### 2.1 Drillhole list (the only HTTP data flow)

```text
App start / Retry / backend CameOnline (only if the last result was not Ok)
  ↓ CoreInformationViewModel.LoadDrillholesAsync   (a running load is reused)
  ↓ [first time] DrillholeCache.LoadAsync → DrillholeCatalog.Build (thread pool) → dropdown
  ↓ DrillholesClient.GetAsync  → ApiClient.GetAsync<IReadOnlyList<DrillholeResponse>>("/api/core-information/drillholes")
  ↓ HTTP GET (timeout = requestTimeoutSeconds, CancellationToken.None)
  ↓ DrillholeController.Get → DrillholeRepository.GetAllAsync → SQL Server
  ↑ 200 JSON [{holeId, projectCode, campaign}]  |  503 ProblemDetails  |  refused/timeout
  ↑ ApiResult: Ok | HttpError("Backend error 503 (…)") | Unreachable | InvalidResponse
  ↑ Ok with ≥1 entry → catalog replaced + DrillholeCache.ReplaceAsync (one transaction)
    Ok but empty with a non-empty catalog → keep the old list, message "returned no drillholes; showing the saved list"
    Failure → keep the list; DrillholesMessage + Retry; the pick and form values are untouched
  ↑ ApiClient.Reached(bool) → BackendConnection.State Online/Offline → StatusAnnouncements toast
```

### 2.2 Capture (in-process; the core workflow)

```text
Right mouse down in MainWindow (active, enabled, not minimized, measurement mode on)
  ↓ e.Handled = true; TriggerCommand.CanExecute(timestamp) → CanTrigger
      (State == Ready, session matches the saved core, valid depths, no active capture, duration editor closed)
  ↓ MeasurementViewModel.CaptureAsync(start)  — freezes from/to/step/duration; State = Capturing
  ↓ DeviceCaptureService.CaptureAsync (thread pool for every DB call)
      Resolve(): Gamma Guard DB status must be Available → RecentSerials(200) → Pick (newest serial;
                 refuse if another serial is within 5 s) → FindDetector(serial)
      MaxSampleId → afterId = max − 200 ; LatestTsMs → DeviceTime.Problem (Unix ms, within 12 h)
      poll ReadSamplesAfter(serial, afterId) every 250 ms until click + duration; CaptureWindow.Add filters
      the window (T < ts ≤ T + d), serial, duplicate ids and duplicate (session, seq, ts)
      final read up to 1.5 s or until the expected count (= duration in s) is reached
  ↑ CaptureData(DeviceSamples, Device, ExpectedSamples)
  ↓ State = Processing; 100 ms hold; BuildResult → SampleAveraging.Summarize
  ↓ AddSorted (MeasurementOrder) ; depth advance if NextFrom was not edited meanwhile
  ↓ SaveSession → Persist (serial queue) → SqliteMeasurementRepository.SaveAsync (one transaction:
      progress upsert, record upsert by (session_id, CaptureNo), samples insert-or-ignore)
  ↑ MeasurementsSaved → CoreInformationViewModel.RefreshMeasuredAsync
  ↑ finally: State = Ready, countdown reset
Errors: CaptureException → error toast (no row, no advance); other exceptions → generic toast;
        cancellation (Stop / SetCore(null) / Dispose) → silent, no row.
```

### 2.3 Save & Continue / session resolution

```text
SaveCommand → validation (DrillholeId) → Operator autofill → CoreSnapshot(Trimmed)
  ↓ MainViewModel: Measurement.SetCore(core); Navigate(Measurement)
  ↓ OpenDrillholeAsync: await persistence queue; if unsaved rows remain → retry the save and abort with an error toast
  ↓ ResolveSessionAsync(candidate): INSERT … ON CONFLICT(DrillholeID) DO NOTHING; SELECT  (existing metadata wins)
  ↓ LoadAsync + LoadProgressAsync → ShowSession (sorted rows, depth inputs from progress)
  ↑ Warning toast when the stored metadata differs from the form ("… already exists. Its stored … are kept.")
```

## 3. Shared contracts

| Contract | Backend | Frontend | Notes |
|---|---|---|---|
| Drillhole JSON | `Backend/Models/CoreInformation/Drillhole/DrillholeResponse.cs` (`string` non-null; nulls sent as `""`) | `Frontend/Api/Core-information/drillholes/DrillholeResponse.cs` (`string?`) | Duplicated, not shared: no common assembly. Matched by name through camelCase / Web JSON defaults |
| Field mapping | `HOLEID`, `PROJECTCODE`, `RIGHT(PROSPECT,4)` | `holeId` → `DrillholeId` → SQLite `DrillholeID`; `projectCode` → `DrillholeName` (label "Project") → SQLite `DrillholeName`; `campaign` → `ProjectArea` (label "Campaign") → SQLite `Project` → CSV "Campaign" | Labels were renamed but storage names were not (comment in `CoreInformationViewModel.cs`) |
| Error payload | ProblemDetails 503 | Body ignored; only the status code is shown | — |
| Gamma Guard schema | — | `samples(id, serial, session_id, seq, ts_ms, count0, count0_raw, hdm, hdm_raw, dose_rate, temperature, pressure)`, `detector_status(serial, name, mac, firmware, device_type, session_id, state, last_sample_ms, updated_ms)` | Assumed from the vendor DB; no schema check beyond the SQL |

## 4. Validation responsibilities

- **Backend:** none (no inputs). Data cleaning is limited to NULL → `""`.
- **Frontend:** all user-input validation, plus cleaning of API data: `DrillholeCatalog.Build` drops empty Hole IDs, trims, and de-duplicates case-insensitively. Device-data validation covers the time basis, the window, duplicates and non-numeric → null.
- **Database:** unique indexes enforce one session per Drillhole ID, unique `(session_id, CaptureNo)` and unique `(record_id, source_id)`. Foreign keys are on (`ForeignKeys = true`). The migration also runs a `foreign_key_check`.

## 5. Error propagation across layers

| Origin | Backend | HTTP | Frontend result | User sees |
|---|---|---|---|---|
| SQL Server down | `SqlException` → 503 | 503 | `HttpError`, backend still Online | "Showing the saved drillhole list. Could not refresh: Backend error 503 …" + Retry |
| Backend process down | — | refused | `Unreachable`, Offline | Sidebar Offline, warning toast once, saved list kept |
| Timeout (> 5 s) | — | — | `Unreachable` ("did not respond in time") | Same as above |
| Malformed JSON | — | 200 | `InvalidResponse` | "Backend returned unexpected data." |
| Missing connection string | DI ctor throws (Inferred: 500) | 500 | `HttpError` | "Backend error 500 …" |
| Gamma Guard locked/missing | — | — | `CaptureException` / `LocalState.Busy`/`Missing` | Error toast / amber status row |
| App DB write failure | — | — | `Persist` returns false | Error toast; rows kept and retried |

## 6. Asynchrony, cancellation and state synchronisation

- HTTP requests use `CancellationToken.None`, except the probe, which uses the monitor's lifetime token. Cancellation therefore relies on the `HttpClient.Timeout`.
- Capture cancellation: a linked CTS (`lifetime` + per capture) is cancelled by Stop, by `SetCore(null)` (form edited), by `SelectDrillhole`, or by `Dispose`. Late progress callbacks are ignored by checking the capture's identity.
- Stale-result protection: `viewVersion`, `optionsVersion` and the Bluetooth `selection` counter.
- UI-thread marshalling relies on `await` continuations captured on the WPF `SynchronizationContext`. Heavy work runs in `Task.Run`.

## 7. Observations (unclear or mismatched interfaces; not confirmed bugs)

1. The backend exposes `bluetooth-device` and `sqlite-database` endpoints that the frontend no longer calls (`README.md` states the clients were removed). Their purpose and future are **Unknown**.
2. There is no health endpoint. The reachability probe treats **any** HTTP response at the base URL as Online, including a different service on port 5002.
3. The backend and frontend use different rules to discover the vendor SQLite: "CT008" folder + `ct008-log.sqlite` versus a "GammaGuard" folder + `Database\*.sqlite`.
4. The drillhole contract is defined twice and only kept aligned by naming convention. Nullability differs (non-null on the backend, nullable on the frontend).
5. The backend returns the full table without ordering or paging. The frontend handles sorting and large lists (tested to 20,000 entries).
