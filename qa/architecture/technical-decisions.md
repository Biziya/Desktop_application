# Technical Decisions and Constraints

Decisions visible in the repository. **Rationale** is quoted only where the README decision log or a code comment states it; otherwise it is marked *unknown*.

| ID | Decision | Evidence | Rationale | Benefits / trade-offs | Limitations |
|---|---|---|---|---|---|
| TD-01 | Windows-only WPF + C#/.NET (10) | `Frontend.csproj`; README decision log "Chosen" | README: "Simple, mature Windows UI approach"; "Consistent language/ecosystem" | Native desktop; WinRT Bluetooth | Windows 10 2004+ only; tests need Windows |
| TD-02 | Measurement logic in the WPF process; the HTTP backend only for drillhole data | `Frontend/Calculation/**`, `MeasurementRepository.cs`; `Backend/` has drillholes only | Partly in the README: "Initial in-process deployment — Recommended: lower complexity than a local web server". Why an HTTP backend was added for drillholes: *unknown* | Works offline; capture has no network latency | Differs from "UI free of acquisition logic" (DIS-03); no capture without the app open (README Q9) |
| TD-03 | Hand-written MVVM, no DI container in the frontend | `Shared/Infrastructure/*`, `MainViewModel` as composition root | *Unknown* (the README proposed CommunityToolkit.Mvvm) | No dependency | `RelayCommand` refreshes manually; `AsyncCommand` is `async void` |
| TD-04 | Separate app SQLite; vendor DB read-only | `MeasurementRepository.cs`, `SqliteFileCheck.OpenReadOnly` | README: "Separate app SQLite — Recommended: protect raw source"; rule §17 | Raw source protected; history kept | Two SQLite files; vendor schema assumed |
| TD-05 | WAL journal for the app DB | `EnsureSchema` comment | Code comment: lets other tools read while the app writes | Concurrent readers | `-wal`/`-shm` files; the WAL switch happens before the version check |
| TD-06 | Depths as integer millimetres | `Depth.cs` comment | Code comment: intervals add up exactly | No floating-point drift | Finer than 1 mm rejected |
| TD-07 | Capture window from the click time (no delay) | `CaptureWindow`, `DeviceCaptureService` comments | *Unknown*. Conflicts with the README's +3 s (DIS-02) | Simple, reproducible boundaries | Requires PC and device clocks to agree (12 h check only) |
| TD-08 | Right-click trigger | `MainWindow.xaml.cs` | *Unknown*. Conflicts with the Bluetooth button (DIS-01) | No extra hardware protocol | Needs the window active; mouse-dependent |
| TD-09 | Serial background write queue | `MeasurementViewModel.Persist` comment | Code comment: writes in request order, never faults | Ordered, non-blocking | Close waits at most 5 s (UR-23) |
| TD-10 | JSON settings files beside the exe / DB | `ApiSettings`, `DeviceSettings`, `CaptureDurationSettings` comments | Code comment: editable without rebuilding | Field-configurable | Non-numeric values throw (DEF-001/002) |
| TD-11 | Errors shown as toasts; no frontend logging | `ToastService`, no `ILogger` in `Frontend/` | *Unknown* | Simple UX | No diagnostics trail (NFR-013) |
| TD-12 | Backend: controllers + repositories, ProblemDetails, Swagger always on, no auth | `Program.cs`, `DrillholeController.cs` | *Unknown* | Small API | Security open (UR-14, SUS-01) |
| TD-13 | Invariant culture for storage and CSV; ISO times with offset | `MeasurementRepository.Time`, `CsvExportService.Format` | Code comments | Locale-independent data | Excel in `;` locales (UR-04) |
| TD-14 | Separate QA test harness (console, no framework packages), GitHub Actions CI | `Tests/`, `.github/workflows/tests.yml` | QA Phases 3 and 7: no test packages available offline | No new dependencies | No IDE test integration |

## Constraints

- **Hardware and vendor software** (CT007-M, Gamma Guard) are required for real capture; the vendor schema and sample rate are unverified.
- **The SQL Server** is corporate and must not be used by tests.
- **Data folder:** the app always writes to `%LocalAppData%`; there is no setting to change it.
- **Build output is tracked in git:** `Frontend/obj` and `Backend/obj` change when building in place.
