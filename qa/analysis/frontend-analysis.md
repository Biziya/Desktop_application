# Frontend Analysis

Project: `Frontend/Frontend.csproj` (WPF `WinExe`, `net10.0-windows10.0.19041.0`, nullable enabled, `Microsoft.Data.Sqlite` 10.0.0).
All `.cs` files under `Frontend/` were read in full. `MainWindow.xaml` was read in full. In `CoreInformationPage.xaml` and `MeasurementPage.xaml`, the bindings, commands and event hooks were read. In `Theme.xaml`, `MeasurementStyles.xaml` and `MeasurementUiConstants.xaml`, only the triggers and converters were scanned; they are visual only.

## 1. Architecture

MVVM without a framework. There is **no DI container**: `ViewModels/MainViewModel.cs` is the composition root. It creates one instance of each service for the window's lifetime and exposes `Core` and `Measurement` page ViewModels. Pages are chosen through `DataTemplate`s in `MainWindow.xaml` (`CurrentPage`).

The frontend contains the real "backend" of the product: device capture, averaging, persistence, migrations and export. The folder names do not match the layers strictly. `Pages/Measurement/MeasurementRepository.cs` is the SQLite data-access layer; `Calculation/Core-Information/GammaGuardDatabase/GammaGuardDeviceDatabase.cs` is data access to the vendor DB.

| Layer | Files |
|---|---|
| Views (XAML + code-behind) | `MainWindow.xaml(.cs)`, `Pages/*/…Page.xaml(.cs)`, `Windows/ConfirmationWindow/*`, controls `Pages/Measurement/CountdownRing.cs`, `DepthBarChart.cs`, `ScaleExtension.cs` |
| ViewModels | `ViewModels/MainViewModel.cs`, `Pages/CoreInformation/CoreInformationViewModel.cs`, `Pages/Measurement/MeasurementViewModel.cs`, `ViewModels/StatusAnnouncements.cs` |
| Domain/calculation (pure) | `Calculation/Measurement/Depth.cs`, `MeasurementOrder.cs`, `DeviceSamples.cs` (`CaptureWindow`, `SampleAveraging`, `DeviceTime`), `Pages/CoreInformation/DrillholeCatalog.cs`, `Pages/Measurement/MeasurementCellFormat.cs`, `CaptureDurationSettings.TryParse/IsValid` |
| Services | `DeviceCaptureService`, `GammaGuardCaptureDevices`, `DeviceStatusMonitor`, `Ct007BluetoothCheck`, `CsvExportService`, `ToastService`, `BackendConnection`, `DialogService` |
| Data access | `SqliteMeasurementRepository`, `DrillholeCache`, `GammaGuardDeviceDatabase`, `SqliteFileCheck`, `WindowsBluetooth` |
| API client | `Api/ApiClient.cs`, `ApiResult.cs`, `ApiSettings.cs`, `Api/Core-information/drillholes/DrillholesClient.cs` |
| Infrastructure | `Shared/Infrastructure/*` (`ObservableObject`, `RelayCommand`, `AsyncCommand` (async void), `BulkObservableCollection`, `SameItemConverter`, `SidebarItem`) |

### Test seams (Confirmed)

The interfaces are `IMeasurementRepository`, `IMeasurementService`, `ICsvExportService`, `IDialogService`, `ICaptureDeviceResolver`, `ICaptureClock`, `ISampleSource` and `IBluetoothReader`. Other injectable seams:

- `HttpMessageHandler` for `ApiClient`.
- A `Func<…>` Bluetooth device list.
- The `ToastService` delay function.
- `GammaGuardDatabaseCheck` search roots.
- `CoreInformationViewModel.searchDelay`.
- Settings objects.

`MainViewModel` has a second constructor that accepts these doubles.

## 2. Pages and windows

| View | ViewModel | Purpose |
|---|---|---|
| `MainWindow` | `MainViewModel` | Sidebar (collapse, navigation, backend Online/Offline dot), page host, toast overlay, **window-wide right-click capture trigger**, context menu suppressed while armed |
| `CoreInformationPage` | `CoreInformationViewModel` + `DeviceStatusMonitor` | Drillhole searchable ComboBox, read-only Project/Campaign, Operator, Notes, Save & Continue; Device Status rows (Bluetooth dropdown, measurement DB, Gamma Guard DB, Retry) |
| `MeasurementPage` | `MeasurementViewModel` | Starting Depth (m), Step (cm), Start/Stop toggle, countdown ring (click to edit duration, popup editor), depth bar chart, results DataGrid (columns built from `MeasurementColumns`), Delete per row, New Measurement, Export CSV |
| `ConfirmationWindow` | — | Modal delete confirmation (`DialogService.ConfirmDelete`) |

The code-behind is view-only. `CoreInformationPage.xaml.cs` handles caret preservation, Enter → `CommitClosest`, scrolling the closest match into view, and refreshing the device list when the dropdown opens. `MeasurementPage.xaml.cs` builds the columns, scrolls to new rows, and commits depths on `LostFocus`.

## 3. State management

- `MainViewModel` wires the cross-page events:
  - `Core.saved(core)` → `Measurement.SetCore(core)` and navigate to Measurement.
  - `Core.Edited` → `Measurement.SetCore(null)`, which disarms.
  - `Core.DrillholeChanged` → `Measurement.SelectDrillhole(id)`.
  - `Measurement.IsMeasurementModeActive` → `Core.IsSelectionLocked`.
  - `Measurement.MeasurementsSaved` → `Core.RefreshMeasuredAsync()`.
- **Measurement state machine** (`Pages/Measurement/MeasurementState.cs`): `Idle → Ready` (Start, needs valid depths and a session matching the saved core) → `Capturing` (trigger) → `Processing` (after data, 100 ms display hold) → `Ready`. Stop or `SetCore(null)` returns to `Idle` from any state and cancels the active capture.
- **Persistence queue**: `MeasurementViewModel.persistence` chains every DB write in request order. `hasUnsavedRows` drives retries on the next change, on Stop, on close, or on a drillhole switch.
- **Version counters guard against stale async results:** `viewVersion` (session loads), `optionsVersion` (dropdown filtering), and `selection` (Bluetooth checks).
- Long-lived singletons per window: `ApiClient`, `BackendConnection`, `DeviceStatusMonitor`, `ToastService`, `StatusAnnouncements`, both page ViewModels. Pages are recreated on navigation; the ViewModels are not.

## 4. Main user workflows

1. **Select a drillhole.** The dropdown shows the cached list first, then the backend list.
   - Typing filters the list (150 ms debounce, filtering on the thread pool).
   - The closest match (exact > prefix > partial) is highlighted.
   - Enter or a click picks an entry, which fills Project and Campaign.
   - "Measured drillholes" (newest first) are listed before "All drillholes" (A–Z).
2. **Save & Continue.** A drillhole is required. An empty Operator is replaced with the Windows account. A `CoreSnapshot` is passed to Measurement, which resolves or creates the drillhole's session, loads its rows and progress, and navigates to Measurement.
3. **Measure.** Enter the Starting Depth (m) and Step (cm), click Start, then right-click to capture.
   - During the duration countdown the service polls Gamma Guard.
   - On completion the averaged row is inserted in From order and the depth advances by the step. Row and progress are saved in one transaction.
   - An incomplete capture gives a warning toast; no samples gives an error toast and no row.
4. **Stop, delete, export, new measurement.** Delete asks for confirmation and restores the row if the DB delete fails. Export goes through a `SaveFileDialog` and a temp file + move. New Measurement closes the session and clears the core.
5. **Device Status.**
   - The user explicitly picks a CT007-M from the paired/connected list. It is then polled every 1 s and has no effect on capture.
   - Both DB rows are checked every 5 s and on Retry.

## 5. Validation (all client-side)

| Input | Rule | Feedback |
|---|---|---|
| Drillhole | Required; must be picked from the list (typing never picks) | Red border + 5 s error toast on Save |
| Starting Depth | `Depth.TryParseMetres`: non-negative; `.` or `,` as decimal separator; ≤ 3 decimals (whole mm) | Red border while typing; toast on LostFocus or Start |
| Step (cm) | `Depth.TryParseCentimetres`: > 0, ≤ 1 decimal (whole mm) | Same as Starting Depth |
| Capture duration | Whole seconds 2–10 (`CaptureDurationSettings.TryParse`); an invalid saved value falls back to 5 | Toast; editor stays open |
| Selection lock | No drillhole change while measuring | Error toast; the box reverts |
| Config files | Invalid or missing values fall back to defaults (`ApiSettings.Load`, `DeviceSettings.Load`) | Silent |

## 6. Backend communication

- One `HttpClient` (`Api/ApiClient.cs`) with the base URL and timeout from `Api/api-settings.json` (defaults: `http://localhost:5002`, 5 s timeout, 5 s status interval). JSON uses `JsonSerializerDefaults.Web`.
- `GetAsync<T>` → `ApiResult<T>` with status `Ok | Unreachable | HttpError | InvalidResponse`. It raises `Reached(bool)` after every request.
- `ProbeAsync` sends `HEAD` to the base URL. Any HTTP answer counts as reachable. `BackendConnection` polls it at the status interval, never runs overlapping checks, and raises `CameOnline`.
- The only data endpoint used is `GET /api/core-information/drillholes` (`DrillholesClient`). Details are in `integration-map.md`.

## 7. Local integrations (the frontend's own "backend")

| Integration | Files | Notes |
|---|---|---|
| App SQLite `measurements.db` | `Pages/Measurement/MeasurementRepository.cs`, `Api/Core-information/drillholes/DrillholeCache.cs` | `%LocalAppData%\CT007_Measurement_Studio\`; a legacy folder with spaces is moved once; WAL; schema v5 with migrations v1→v5. Schema is in `feature-inventory.md` F18 |
| Gamma Guard SQLite (vendor) | `GammaGuardDeviceDatabase.cs`, `GammaGuardDatabaseCheck.cs`, `SqliteFileCheck.cs` | Always `Mode=ReadOnly`, no pooling, 2 s busy timeout, no PRAGMA |
| Windows Bluetooth (WinRT) | `BluetoothDevice/WindowsBluetooth.cs`, `Ct007BluetoothCheck.cs` | Read-only, 3 s timeout per call, runs on the thread pool |
| File system | `CaptureDurationSettings` (`capture-settings.json` beside the DB), `CsvExportService`, config JSONs beside the exe | Atomic temp-file + move |
| Windows identity | `CoreInformationViewModel.CurrentWindowsAccount` | `DOMAIN\user` or `user` |
| Clock / time zone | `SystemCaptureClock`, `DateTimeOffset.Now`, `MeasurementCellFormat.UnixMillisecondsLocal` | Local-time display; stored with offset |

## 8. Error handling patterns

- Every async failure is turned into a toast (`ToastService`: error 5 s, others 3 s, duplicates merged, at most 4 visible). Nothing is thrown to the UI thread.
- `CaptureException` carries user-facing messages. Other exceptions become "Capture failed. Right-click to try again."
- Save failure: the rows stay in the table and are retried later. Delete failure: the row is re-inserted.
- Device Status: each check maps exceptions to a `LocalState` (`Missing`, `NotFound`, `Inaccessible`, `Busy`, `Unavailable`).
- `StatusAnnouncements` toasts each state transition once; start-up states are not announced.

## 9. Important dependencies and risks for testing

- `MainViewModel()` (default) touches the real `%LocalAppData%`, Bluetooth, the file system and the network on construction, and starts polling loops immediately.
- `MeasurementViewModel.Dispose` blocks the calling (UI) thread for up to 5 s waiting for queued saves.
- `AsyncCommand.Execute` is `async void`. `RelayCommand.CanExecuteChanged` is raised only by manual `Refresh()`.
- `MeasurementViewModel.RestoreLatestSessionAsync` exists and is tested, but **the app never calls it** (Observation).
- `ShowDemoTsMs` is `false` in the app, so demo `ts_ms` appears only in tests.
- The non-device capture path in `BuildResult` (`CaptureData.Samples`, `AverageValue`) is used only by test services.
