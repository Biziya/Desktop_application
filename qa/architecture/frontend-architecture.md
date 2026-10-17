# Frontend Architecture

**Project:** [`Frontend/Frontend.csproj`](../../Frontend/Frontend.csproj). WPF `WinExe`, `net10.0-windows10.0.19041.0`, nullable enabled. Its only package is `Microsoft.Data.Sqlite` 10.0.0. The Windows Runtime APIs come with the target framework.

## Entry points

| Entry | File | Notes |
|---|---|---|
| Application | [`App.xaml`](../../Frontend/App.xaml) | `App.OnStartup` loads the section YAML files (`Pages/PageSettings.cs`), then opens `MainWindow`; a configuration error is shown and the app exits |
| Shell | [`MainWindow.xaml(.cs)`](../../Frontend/MainWindow.xaml.cs) | Creates `MainViewModel()`; sidebar, page host, toast overlay; **window-wide right-click capture trigger** (`PreviewMouseDown`, handled events too); context menu suppressed while armed |
| Composition root | [`ViewModels/MainViewModel.cs`](../../Frontend/ViewModels/MainViewModel.cs) | Builds every service once, wires the page events, starts the polling loops. A second constructor accepts test doubles |

## Layers (by responsibility; folder names don't map 1:1)

| Layer | Main types | Location |
|---|---|---|
| Views | `MainWindow`, `CoreInformationPage`, `MeasurementPage` (composition) with section views `DrillholeInformationView`, `DeviceStatusView`, `CapturePanelView`, `MeasurementTableView`, `ConfirmationWindow`; controls `DepthBarChart`, `CountdownRing`, `ScaleExtension` | `MainWindow.*`, `Pages/*/…Page.xaml(.cs)`, `Pages/*/<Section>/` (view + configuration file), `Windows/ConfirmationWindow/` |
| ViewModels | `MainViewModel`, `CoreInformationViewModel`, `MeasurementViewModel`, `StatusAnnouncements` | `ViewModels/`, `Pages/*/` |
| Domain (pure logic) | `Depth`, `MeasurementOrder`, `CaptureWindow`, `SampleAveraging`, `DeviceTime`, `DrillholeCatalog`, `MeasurementCellFormat`, `CaptureDurationSettings` (rules) | `Calculation/Measurement/`, `Pages/…` |
| Services | `DeviceCaptureService`, `GammaGuardCaptureDevices`, `DeviceStatusMonitor`, `Ct007BluetoothCheck`, `CsvExportService`, `ToastService`, `BackendConnection`, `DialogService` | `Calculation/**`, `Pages/Measurement/`, `Shared/Infrastructure/`, `Api/` |
| Data access | `SqliteMeasurementRepository`, `DrillholeCache`, `GammaGuardDeviceDatabase`, `SqliteFileCheck`, `WindowsBluetooth` | `Pages/Measurement/MeasurementRepository.cs`, `Api/Core-information/drillholes/`, `Calculation/Core-Information/**` |
| API client | `ApiClient`, `ApiResult<T>`, `ApiSettings`, `DrillholesClient` | `Api/` |
| Infrastructure | `ObservableObject`, `RelayCommand`, `AsyncCommand`, `BulkObservableCollection`, `SameItemConverter`, `SidebarItem` | `Shared/Infrastructure/` |

**Seams (interfaces):**
- `IMeasurementRepository`, `IMeasurementService`, `ICsvExportService`, `IDialogService`;
- `ICaptureDeviceResolver`, `ICaptureClock`, `ISampleSource`, `IBluetoothReader`;
- injectable `HttpMessageHandler`, delays and search roots.

## Pages and navigation

| Page | ViewModel | Content |
|---|---|---|
| Core Information | `CoreInformationViewModel` (+ `DeviceStatusMonitor`) | Drillhole search box, read-only Project/Campaign, Operator, Notes, Save & Continue; Device Status rows with Retry |
| Measurement | `MeasurementViewModel` | Starting Depth (m), Step (cm), Start/Stop, countdown ring (click → duration editor), depth chart, results grid (Delete per row), New Measurement, Export CSV |

- **Navigation:** sidebar commands `ShowCore` / `ShowMeasurement`, and Save & Continue → Measurement. The sidebar selection is derived from `CurrentPage`.
- **Lifetime:** the ViewModels live for the window's lifetime; the page views are recreated on navigation.

## State management

- **Measurement state machine:** `Idle → Ready → Capturing → Processing → Ready`; Stop or a cleared core → `Idle` ([feature-workflows](feature-workflows.md) §2).
- **Cross-page events** (wired in `MainViewModel`):

  | Event | Effect |
  |---|---|
  | `Core.saved` | `Measurement.SetCore` + navigate |
  | `Core.Edited` | `SetCore(null)` |
  | `Core.DrillholeChanged` | `SelectDrillhole` |
  | `IsMeasurementModeActive` | `Core.IsSelectionLocked` |
  | `MeasurementsSaved` | `Core.RefreshMeasuredAsync` |

- **Stale-result guards:** version counters `viewVersion` (session loads), `optionsVersion` (search) and `selection` (Bluetooth).
- **Persistence queue:** every write is chained in request order; `hasUnsavedRows` triggers retries.

## Validation (client-side; the backend has no inputs)

The rules are in [business-rules](../requirements/business-rules.md) BR-002/003/006.
- **Display:** a red border while typing, and an error toast on LostFocus or Start.
- **Drillhole:** required and must be picked from the list (red border + toast).
- **Selection lock:** the drillhole can't change while measuring.
- **Settings files:** fall back to defaults when values are invalid, except for the defects DEF-001 and DEF-002.

## Asynchrony and threading

- **UI thread:** continuations return to it through the WPF `SynchronizationContext`.
- **Thread pool:** SQLite, file scans, filtering and Gamma Guard reads run there (`Task.Run`).
- **Polling loops:**

  | Loop | Interval |
  |---|---|
  | Backend probe | 5 s |
  | Bluetooth check | 1 s |
  | Database status checks | 5 s |
  | Capture polling | 250 ms |

- **Cancellation:** Stop, form edits, drillhole switches and closing the app cancel the active capture through a linked token.

## Error handling

- **No exception reaches the UI.** Failures become toasts: errors last 5 s, others 3 s, with at most 4 visible.
- **Capture errors:** a `CaptureException` carries user text; any other exception shows "Capture failed. Right-click to try again."
- **Failed operations:** a failed save keeps its rows and retries; a failed delete restores the row.
- **No frontend logging.**

## Backend communication

- **One `HttpClient`** with the base URL and timeout from `Api/api-settings.json` (defaults `http://localhost:5002`, 5 s).
- **Calls used:** `HEAD /` (reachability) and `GET /api/core-information/drillholes`.
- **Results** come back as `ApiResult<T>`: `Ok`, `Unreachable`, `HttpError` or `InvalidResponse` ([integration-architecture](integration-architecture.md)).
