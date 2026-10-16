# Project Map — SPP CT-007 Measurement Studio

Phase 1 analysis (read-only). Generated 2026-10-16 from the working tree on branch `main`.
Paths are relative to the repository root. Legend used across all `qa/analysis` documents:

- **Confirmed**: read directly in source.
- **Inferred**: deduced from source, not executed.
- **Observation**: possible mismatch or risk, not a confirmed defect.
- **Unknown**: needs clarification from the team.

## 1. Overview

A Windows desktop application for drill-core gamma measurements with a CT007-M detector.

1. An operator picks a **drillhole** on the *Core Information* page. The list comes from a corporate SQL Server through a small ASP.NET Core backend.
2. On the *Measurement* page the operator arms measurement mode and **right-clicks anywhere in the main window** to capture.
3. Each capture reads the detector samples that the vendor software **Gamma Guard** writes to its own local SQLite database. It averages them into one result row per depth interval (From/To/Step), stores the row in the app's own SQLite database, and can export the rows to CSV.

Almost all business logic, data access and device integration lives in the **WPF frontend process**. The backend is a thin reference-data API. Only one of its three endpoints is used by the frontend.

## 2. Technology stack

| Part | Technology (Confirmed) | Key packages |
|---|---|---|
| Solution | `CT007.slnx` (Backend, Frontend, SmokeTests) | — |
| Frontend | WPF, C#, `net10.0-windows10.0.19041.0`, `WinExe`, hand-rolled MVVM (no toolkit) | `Microsoft.Data.Sqlite` 10.0.0, Windows Runtime Bluetooth API (via TFM) |
| Backend | ASP.NET Core Web API (controllers), `net10.0-windows10.0.19041.0` | `Microsoft.Data.SqlClient` 6.1.1, `Swashbuckle.AspNetCore` 9.0.6, Windows Runtime Bluetooth API |
| Tests | Console smoke-test harness, `net10.0-windows`, `UseWPF` | Project reference to Frontend only |
| Tooling | `tools/check-db.ps1`, a PowerShell script that reads the app DB through `winsqlite3.dll` | — |
| Data stores | SQL Server (`ZOOVCH_COLLAR_LIST`), app SQLite (`measurements.db`), Gamma Guard SQLite (external, read-only) | — |

## 3. Directory structure (source only)

```text
CT007.slnx
README.md                    design/requirements draft (pre-implementation; diverges from code, see testing-constraints.md)
Images/                      UI mock images (not referenced by code)
tools/check-db.ps1           read-only inspector for measurements.db
Backend/
  Program.cs                 host, DI, Swagger, controllers
  appsettings*.json          SQL Server connection string, logging
  Properties/launchSettings.json   http://localhost:5002, https://localhost:7244
  Backend.http               manual request samples
  Controllers/CoreInformation/{Drillhole,BluetoothDevice,SqliteDatabase}/*Controller.cs
  Models/CoreInformation/**/…Response.cs          response records
  Repositories/CoreInformation/**/…Repository.cs  SQL Server, WinRT Bluetooth, file-system search
  Controllers|Models|Repositories/Measurement/.gitkeep   empty placeholders
Frontend/
  App.xaml(.cs), MainWindow.xaml(.cs)    entry, shell, window-wide right-click trigger
  ViewModels/                MainViewModel (composition root), StatusAnnouncements
  Api/                       ApiClient, ApiResult, ApiSettings, BackendConnection, api-settings.json
  Api/Core-information/drillholes/   DrillholesClient, DrillholeResponse, DrillholeCache (SQLite)
  Calculation/Core-Information/      Device Status: DeviceStatusMonitor, LocalStatus, SqliteFileCheck,
                                     DeviceSettings (+device-settings.json), BluetoothDevice/, GammaGuardDatabase/,
                                     MeasurementDatabase/
  Calculation/Measurement/           Depth, MeasurementOrder, CaptureDurationSettings, DeviceCaptureService,
                                     DeviceSamples (window, averaging, time check)
  Pages/CoreInformation/             page, ViewModel, DrillholeCatalog, DrillholeOption
  Pages/Measurement/                 page, ViewModel, MeasurementRepository (SQLite), CsvExportService,
                                     MeasurementColumns/Layout/CellFormat, DepthBarChart, CountdownRing, …
  Shared/Infrastructure/             ObservableObject, RelayCommand/AsyncCommand, ToastService, BulkObservableCollection, …
  Shared/Models/CoreSnapshot.cs, Shared/Styles/Theme.xaml
  Windows/ConfirmationWindow/        delete-confirmation dialog + DialogService
SmokeTests/
  Program.cs                 ~2,100 lines, 359 Check() assertions, fakes and stubs
```

Excluded: `bin/`, `obj/`, `.git/`. The `bin/Debug` folders exist for all three projects; they were inspected only as build evidence.

## 4. Entry points

| Entry | File | Notes |
|---|---|---|
| Frontend process | `Frontend/App.xaml` (`StartupUri="MainWindow.xaml"`) | No custom `App` startup logic |
| Composition root | `Frontend/MainWindow.xaml.cs` → `new MainViewModel()` → `Frontend/ViewModels/MainViewModel.cs` | Default ctor loads settings and creates every service; a second ctor accepts test doubles |
| Capture trigger | `MainWindow.xaml.cs` `OnMeasurementMouseDown` (window-level `PreviewMouseDown`, `handledEventsToo`) | Right mouse button only, while armed |
| Backend process | `Backend/Program.cs` | Top-level statements; `app.Run()` |
| Smoke tests | `SmokeTests/Program.cs` `Main` (STA, WPF dispatcher) | `dotnet run --project SmokeTests/SmokeTests.csproj` |
| DB inspector | `tools/check-db.ps1` | Manual tool |

## 5. Major modules

| Module | Side | Core files |
|---|---|---|
| Shell, navigation, toasts | FE | `MainWindow.*`, `ViewModels/*`, `Shared/Infrastructure/ToastService.cs` |
| Backend connectivity | FE | `Api/ApiClient.cs`, `Api/BackendConnection.cs` |
| Drillhole reference data | FE + BE | `Api/Core-information/drillholes/*`, `Pages/CoreInformation/*`, `Backend/**/Drillhole/*` |
| Local Device Status | FE | `Calculation/Core-Information/**` |
| Measurement workflow | FE | `Pages/Measurement/MeasurementViewModel.cs`, `Calculation/Measurement/*` |
| Device capture (Gamma Guard) | FE | `Calculation/Measurement/DeviceCaptureService.cs`, `DeviceSamples.cs`, `Calculation/Core-Information/GammaGuardDatabase/*` |
| Persistence (app SQLite) | FE | `Pages/Measurement/MeasurementRepository.cs`, `Api/Core-information/drillholes/DrillholeCache.cs` |
| Export and visualisation | FE | `CsvExportService.cs`, `MeasurementColumns*.cs`, `DepthBarChart.cs` |
| Unused diagnostic endpoints | BE | `Backend/**/BluetoothDevice/*`, `Backend/**/SqliteDatabase/*` |

## 6. High-level architecture

```text
 Operator ──right-click / keyboard / mouse──▶ WPF Frontend (single process, UI thread + thread pool)
                                              │
     ┌────────────────────────────────────────┼──────────────────────────────────────────┐
     │ HTTP (HEAD /, GET /api/core-information/drillholes)                                  │
     ▼                                        ▼ Microsoft.Data.Sqlite                       ▼ WinRT
 ASP.NET Core Backend (:5002)          measurements.db (app-owned, WAL)              Windows Bluetooth
   └─ SqlClient ─▶ SQL Server          %LocalAppData%\CT007_Measurement_Studio\        (CT007-M status,
      ZOOVCH_COLLAR_LIST                + capture-settings.json                        informational only)
                                              ▼ Microsoft.Data.Sqlite (Mode=ReadOnly)
                                       Gamma Guard SQLite (vendor-owned; samples, detector_status)
                                       ◀── written continuously by Gamma Guard ◀── CT007-M detector
```

Detailed views are in `frontend-analysis.md`, `backend-analysis.md` and `integration-map.md`.
