# Testing Infrastructure

No tests were executed in Phase 1. The statements below come from source code, project files and existing build or restore artifacts.

## 1. Existing test projects

| Project | Type | Framework | Target | References |
|---|---|---|---|---|
| `SmokeTests/SmokeTests.csproj` | Console executable (custom harness) | **None**: no xUnit, NUnit or MSTest; uses its own `Check(bool, message)` that throws on failure | `net10.0-windows`, `UseWPF` | `../Frontend/Frontend.csproj` |

There are **no backend tests**, and there are no UI-automation, E2E, API or performance-test projects. The root `README.md` proposes `Tests/Backend.Tests` and `Tests/Integration.Tests`; **they do not exist**.

## 2. Smoke-test harness (`SmokeTests/Program.cs`, ~2,100 lines, 359 `Check` calls)

- **Execution model:**
  - `Main` is `[STAThread]` and installs a WPF `DispatcherSynchronizationContext`.
  - Every check runs sequentially inside one `Run()` method.
  - The first failing `Check` throws, which ends the run with exit code 1. The run stops at the first failure, so there are no per-test results.
- **Culture:** forced to `InvariantCulture`. The README reports earlier manual runs under UTC, America/New_York and UTC+8.
- **Isolation:**
  - Temporary SQLite files go in `%TEMP%` (`TempDb(name)` = name + GUID).
  - Nothing touches `%LocalAppData%` or the real Gamma Guard DB.
  - **Observation:** the code does not delete the temp files afterwards (Inferred from the helpers; there is no cleanup code).
- **Timing:**
  - `Until()` polls with a 3 s timeout.
  - Some checks count polling calls inside wall-clock windows (for example "330 ms at 30 ms"; "520 ms at 50 ms").
  - The UI-thread cost of a keystroke is measured with `Stopwatch`.
  - These checks are **potentially timing-sensitive**, which is a flakiness risk on slow or loaded machines.

### Test doubles provided

| Double | Replaces |
|---|---|
| `StubBackend : HttpMessageHandler` | Backend HTTP (drillholes JSON or status, offline, held probes, per-path call counts) |
| `MemoryRepository : IMeasurementRepository` | App SQLite (failure flag, delayed lookups) |
| `ControlledMeasurements : IMeasurementService` | Capture service (manual complete, fail, report) |
| `FakeGammaGuard` | Real SQLite file with the `samples` and `detector_status` schema |
| `FakeCaptureClock : ICaptureClock` | Wall clock and delays (also frozen) |
| `FixedDevice : ICaptureDeviceResolver` | Detector resolution |
| `FakeBluetoothReader : IBluetoothReader`, plus `Func<IReadOnlyList<KnownBluetoothDevice>>` | Windows Bluetooth |
| `FakeDialogs : IDialogService`, `FakeExport : ICsvExportService` | Modal dialog, file save |

The production `SqliteMeasurementRepository`, `DrillholeCache`, `GammaGuardDeviceDatabase`, `SqliteFileCheck` and `GammaGuardDatabaseCheck` are exercised against **real SQLite files**.

### Sections and their targets

| Section (called from `Run`) | Main targets |
|---|---|
| Inline `Run` body (lines ~66–520) | Drillhole load/refresh/errors, `BackendConnection`, form validation, the measurement state machine with `ControlledMeasurements`, export `Serialize`, column layout, delete, Stop/Start, sessions, `MainViewModel` navigation/sidebar/polling, save failures, demo `ts_ms` |
| `DrillholeListChecks` | `DrillholeCache`, Measured/All groups |
| `AutofillAndDepthInputChecks` | Operator autofill, ComboBox echo, depth parsing in comma locales, edits during a capture |
| `MeasurementUnitsAndOrderChecks` | `Depth`, `MeasurementOrder` |
| `LocalDeviceStatusChecks` | `Ct007BluetoothCheck`, `DeviceSettings.Load`, `SqliteFileCheck`, `GammaGuardDatabaseCheck`, `DeviceStatusMonitor` |
| `TableFormattingChecks` | `MeasurementCellFormat`, column alignment |
| `DropdownPerformanceChecks` | 20,000-entry catalog, debounce, stale results |
| `ToastChecks` | `ToastService`, `StatusAnnouncements` |
| `DrillholeSelectionChecks` | Closest-match ranking, selection lock, clear, reselection |
| `CaptureDurationChecks` | `CaptureDurationSettings`, editor flow |
| `DeviceCaptureChecks` | `DeviceCaptureService`, `CaptureWindow`, `SampleAveraging`, `DeviceTime`, `GammaGuardCaptureDevices`, raw-sample persistence |
| `ProgressMigrationChecks` | v4 → v5 mm→cm migration |
| `SqliteChecks` | `SqliteMeasurementRepository` CRUD, constraints, v1–v4 upgrades, duplicate refusal, restart restore |

## 3. Execution commands (documented, not run)

```powershell
dotnet run --project SmokeTests/SmokeTests.csproj   # Frontend/README.md "Validation"
dotnet build Frontend/Frontend.csproj
dotnet run   --project Frontend/Frontend.csproj
cd Backend; dotnet run                               # Backend/README.md; manual requests in Backend/Backend.http
```

## 4. Current build status of the tests (Confirmed from existing artifacts)

- `SmokeTests/obj/project.assets.json` records restore error **NU1201**: "Project Frontend is not compatible with net10.0-windows7.0 … Project Frontend supports: net10.0-windows10.0.19041". The cause is that `SmokeTests.csproj` targets `net10.0-windows`, while `Frontend.csproj` was moved to `net10.0-windows10.0.19041.0` to use WinRT Bluetooth.
- `SmokeTests/bin/Debug/net10.0-windows/` is **empty**, while `Frontend/bin/Debug/net10.0-windows10.0.19041.0/` contains `Frontend.dll`/`Frontend.exe`.
- **Conclusion:** the smoke tests cannot currently be restored or built as configured.
- The README's "Validation" history says earlier runs happened on Linux with stand-ins, with the WPF project and XAML not compiled. It also says the latest revisions were not built or run on Windows ("dotnet is not installed").
- **Existing pass/fail evidence is therefore historical and unverified for the current code.**

## 5. Coverage and CI

- **Coverage tooling:** none. There is no coverlet, `.runsettings` or report configuration.
- **CI/CD:** none. There is no `.github/`, pipeline YAML, `Directory.Build.props`, `global.json` or `nuget.config`.
- **Static analysis:** the README mentions "warnings as errors" for earlier manual compiles, but no project file sets `TreatWarningsAsErrors`. No `.editorconfig` exists.

## 6. Modules without identified automated tests

| Module | Reason it is untested / what it needs |
|---|---|
| Entire backend (`Backend/**`): controllers, `DrillholeRepository` SQL, `BluetoothDeviceRepository`, `SqliteDatabaseRepository`, `Program.cs` DI/Swagger | No backend test project exists |
| `Frontend/MainWindow.xaml.cs` right-click trigger and context-menu suppression | Needs WPF UI or input testing; README lists 14 manual checks |
| `CoreInformationPage.xaml.cs` (caret, Enter/arrow handling, scroll into view), `MeasurementPage.xaml.cs` (column building, LostFocus commit, duration popup focus) | Code-behind; UI-only |
| XAML bindings, styles, triggers, `ScaleExtension` | Not compiled in the previous validation environment |
| `DepthBarChart` rendering and tooltip, `CountdownRing` | Rendering controls |
| `WindowsBluetooth` (real WinRT) | Hardware or OS dependent |
| `CsvExportService.ExportAsync` (dialog, temp file, move, cancellation) | Replaced by `FakeExport` |
| `DialogService` / `ConfirmationWindow` | Replaced by `FakeDialogs` |
| `ApiSettings.Load` (file parsing and defaults) | No reference found in the tests |
| `SqliteMeasurementRepository.ResolveDefaultPath` (legacy folder move and rollback) | `internal`, with no `InternalsVisibleTo` |
| `MainViewModel()` default constructor (real paths, settings files) | Tests use the injectable constructor |
| `tools/check-db.ps1` | Script; not tested |

Modules **with** substantial smoke coverage are all ViewModels, `Calculation/**` (except `WindowsBluetooth`), `SqliteMeasurementRepository`, `DrillholeCache`, `ApiClient`/`BackendConnection`, `ToastService`, `StatusAnnouncements`, `CsvExportService.Serialize` and `MeasurementColumns`/`Layout`/`CellFormat`.
