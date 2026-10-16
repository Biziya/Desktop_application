# Testing Constraints

This document lists dependencies and limitations that affect automated testing. It records them without proposing solutions; test design belongs to Phase 2.

## 1. Environment and operating system

- Both applications target `net10.0-windows10.0.19041.0`. They need **Windows 10 2004+** (backend minimum 1809) and the .NET 10 SDK. The WinRT Bluetooth projection is Windows-only, so neither project builds on Linux or macOS as configured.
- WPF code needs an STA thread and a `Dispatcher`. Many ViewModel behaviours rely on the WPF `SynchronizationContext` (continuations return to the UI thread).
- The test project's target framework is incompatible with the Frontend's (restore error NU1201, see `testing-infrastructure.md` §4), so no frontend test currently builds.
- Locale and time zone affect behaviour:
  - Depth parsing accepts `.` and `,`.
  - The chart uses `CurrentCulture`.
  - `ts_ms` is shown in local time.
  - `StartTime` is stored with the local offset.
  - `MeasuredDrillhole` recency compares offsets as instants.

## 2. Configuration and environment variables

| Item | Location | Effect |
|---|---|---|
| `Api/api-settings.json` | Beside `Frontend.exe` | `baseUrl`, `requestTimeoutSeconds`, `statusIntervalSeconds`; invalid → defaults |
| `Calculation/Core-Information/device-settings.json` | Beside `Frontend.exe` | CT007 prefix/address, Gamma Guard path (environment variables expanded) and file mask, check intervals |
| `capture-settings.json` | Beside `measurements.db` | Capture duration |
| `ConnectionStrings:DefaultConnection` | `Backend/appsettings.json` | SQL Server; required at the first drillhole request |
| `ASPNETCORE_ENVIRONMENT` | `launchSettings.json` (Development) | Logging and exception page only; Swagger is always on |
| `%LocalAppData%`, `%USERPROFILE%`, known folders | Both projects | DB location; discovery roots |

## 3. Databases and persistent storage

| Store | Ownership | Constraints for testing |
|---|---|---|
| SQL Server `ZOOVCH_COLLAR_LIST` | Corporate, **production-like**, remote host named in config | Must not be used by automated tests. Needs a substitute (fake `IDrillholeRepository`, or a disposable SQL instance with the table and columns `HOLEID`, `PROJECTCODE`, `PROSPECT`). The real schema and data volume are **Unknown** beyond these columns |
| `measurements.db` (app) | App-owned, `%LocalAppData%\CT007_Measurement_Studio\` | The default constructor targets the real user profile. Tests must inject a temp path. WAL mode leaves `-wal`/`-shm` files. Migrations are one-way (v5); a newer file is refused |
| Gamma Guard SQLite | **Vendor-owned**, written continuously by Gamma Guard | Must never be written (byte-identical check exists). The real schema, journal mode, locking behaviour, sample rate and `ts_ms` basis have not been verified against a real device database (README) |
| `DrillholeCache` table | Inside `measurements.db` | Shares the file with measurement data, which links the cache and Measurement DB status states |

## 4. Hardware and external applications

- **CT007-M detector + Bluetooth:** live connected/paired states, radio off and timeouts need real hardware (hardware-in-the-loop) or the `IBluetoothReader` seam. The backend Bluetooth endpoint has no seam.
- **Gamma Guard application:** the producer of the sample data. End-to-end capture accuracy (click time ↔ device `ts_ms`, late writes, two detectors) can only be fully validated with Gamma Guard recording live.
- **Mouse input:** the capture trigger is a right mouse button press at window level. Verifying it needs UI automation or manual testing (active, minimized, modal-dialog and over-control cases).
- **Modal dialogs:** `SaveFileDialog` and `ConfirmationWindow` block without UI automation; seams exist (`ICsvExportService`, `IDialogService`).

## 5. External services and network

- The backend must listen on `http://localhost:5002` by default. The frontend treats any HTTP response at the base URL as Online.
- There is no health endpoint and no authentication. Swagger is exposed in every environment.
- Do not call the corporate SQL Server or other live systems from automated tests.

## 6. Background tasks, timing and concurrency

| Mechanism | Interval / limit | Testing implication |
|---|---|---|
| `BackendConnection` probe | 5 s (configurable) | Loop starts in the `MainViewModel` constructor |
| `DeviceStatusMonitor` | Bluetooth 1 s, list/DBs 5 s | Loops start in the constructor; WinRT calls time out after 3 s |
| Drillhole search debounce | 150 ms (injectable) | `WhenOptionsUpdated()` is the sync point |
| Capture polling | 250 ms, final read ≤ 1.5 s, 100 ms display hold | `ICaptureClock` is injectable; the 100 ms hold uses a real `Task.Delay` |
| Toast expiry | 3 s / 5 s | Delay is injectable |
| Backend SQLite search | 20 s timeout, 1 min cache (singleton) | Real file-system scan; no root injection through public API |
| `MeasurementViewModel.Dispose` | Blocks up to 5 s | Can stall test teardown or the UI thread |
| Persistence queue | Serial `Task` chain | Assertions must await the queue or poll for it (tests use `Until`) |

Race-sensitive areas: cancellation versus completion in Capturing/Processing, late session loads after reselection, concurrent `ResolveSessionAsync` (relies on the unique index), and stale Bluetooth results after a selection change.

## 7. Shared or global state

- `SqliteMeasurementRepository.DefaultPath` is a **static** value resolved on first access, and it may move legacy files at that moment.
- Static singletons: `DrillholeCatalog.Empty`, `DrillholeOption` headers, `SystemCaptureClock.Instance`, `MeasurementOrder.Instance`, the `ScaleExtension` resource dictionary (loaded once).
- `Random.Shared` is used for demo `ts_ms`.
- `Application.Current.MainWindow` and `FindResource` are used by `DialogService`/`CsvExportService`, so they require a running WPF `Application`.
- Backend singleton `SqliteDatabaseRepository` cache persists across requests.

## 8. Security considerations

- Plain-text SQL Server credentials are committed in `Backend/appsettings.json`. Do not copy them into test assets or reports.
- The backend has no authentication or authorization, `AllowedHosts: *`, and Swagger UI enabled outside Development.
- CSV export neutralises formula prefixes only in the session metadata cells (Notes etc.). Measurement cells are numeric or timestamp values produced by the app.
- All SQL in both projects is parameterised or constant. The only interpolations are fixed table and column names in `MeasurementRepository.AddMissingColumns` and the migration code.

## 9. Potential automation limitations (summary)

| Area | Automatable with existing seams | Needs special environment | Likely manual or hardware-in-the-loop |
|---|---|---|---|
| Pure logic (`Depth`, `MeasurementOrder`, `CaptureWindow`, `SampleAveraging`, `DeviceTime`, `DrillholeCatalog`, `MeasurementCellFormat`, `CsvExportService.Serialize`) | Yes | — | — |
| ViewModels (Core, Measurement, Main) | Yes (injectable ctor, fakes) | STA + Dispatcher | — |
| App SQLite repository and migrations | Yes (temp files) | — | — |
| Gamma Guard reading | Yes (fake SQLite file) | — | Real Gamma Guard behaviour under live writes |
| Backend drillholes API | Yes (`IDrillholeRepository` fake / `WebApplicationFactory`) | SQL Server test instance for real queries | — |
| Backend Bluetooth / SQLite endpoints | Limited (no seams; static OS calls) | Windows with or without an adapter; controlled folders | Real CT007-M |
| Frontend Bluetooth (`WindowsBluetooth`) | — | Windows | Paired/connected CT007-M |
| Right-click trigger, dialogs, XAML layout, chart/ring rendering, DPI scaling | — | UI automation on Windows | Manual (README §Validation, 14 checks) |
| End-to-end timing accuracy | — | — | Calibration with a physical reference (README §14) |

## 10. Ambiguous requirements and unknowns

1. **Design document versus implementation.** The root `README.md` is a "design only" draft. It describes a 3-second delay after the trigger, a Bluetooth button trigger, an in-process backend without HTTP, and `Tests/` projects. The code instead uses:
   - the window `click < ts_ms ≤ click + duration` with no delay;
   - a right mouse click as the trigger;
   - an ASP.NET Core HTTP backend;
   - no xUnit projects.

   Which document is authoritative is **Unknown**.
2. **Sample rate.** Expected samples = duration in seconds, which assumes 1 Hz. This is unverified against the device.
3. **Capture duration range.** 2–10 s with a default of 5 s; the README lists the duration as an open question.
4. **CSV contract.** From/To/Step are excluded from the export (intentional per code comments). It is unclear whether downstream users need the depths, temperature and pressure.
5. **Chart semantics.** The y-axis is `ts_ms` (a Unix timestamp); the intended plotted quantity is unclear.
6. **Restore on start-up.** `RestoreLatestSessionAsync` exists but is not wired; the intended start-up behaviour is unclear.
7. **Unused backend endpoints** (`bluetooth-device`, `sqlite-database`): keep, test or remove? The two discovery rules for the vendor DB also differ.
8. **Campaign rule.** `RIGHT(PROSPECT, 4)`: unverified for all data.
9. **Disarm on edit.** Editing Operator or Notes after saving disarms measurement mode; confirm that this is intended.
10. **Two detectors window.** The 5 s "simultaneous" threshold and the 12 h clock-tolerance values are design choices without a documented source.
11. **`tools/check-db.ps1`** targets schema v4. It is unclear whether it is still maintained.

## 11. Incomplete analysis

- XAML style dictionaries (`Theme.xaml`, `MeasurementStyles.xaml`, `MeasurementUiConstants.xaml`) were scanned for triggers and converters only, not read line by line. They are visual and contain no logic beyond triggers.
- `Images/*.png` and the full narrative of the root and Frontend READMEs were sampled, not read exhaustively.
- `SmokeTests/Program.cs` was analysed through its structure, helpers, the opening ~120 lines and all assertion messages. Not every test body line was read.
- No code was built, run or tested. Runtime behaviour statements marked *Inferred* are unverified.
