# SPP CT-007 Measurement Studio

WPF / .NET 10 application. Mock measurements only: no backend, Bluetooth or global mouse hooks. Measurement rows are stored in a local SQLite database (`Microsoft.Data.Sqlite`, the only NuGet dependency).

## Run on Windows

Install the .NET 10 SDK with Windows desktop support. From the extracted archive:

```powershell
dotnet restore Frontend/Frontend.csproj
dotnet build Frontend/Frontend.csproj
dotnet run --project Frontend/Frontend.csproj
```

Replace the old Frontend folder rather than overlaying it, because earlier versions used different source paths. Preserve your Backend project and solution files. The project path, project name and target remain Frontend/Frontend.csproj, Frontend and net10.0-windows. No compiled binaries are included.

## Measurement workflow

1. Enter required Core Information and click Save & Continue.
2. Click green **Start** to activate measurement mode. The button becomes red **Stop**. Nothing is captured yet.
3. Right-click anywhere in the active **main application window** to begin a five-second capture, including the panel, table, sidebar or other main-window controls.
4. The ring displays elapsed progress clockwise from the top while the centered timer counts down from 00:05 to 00:00.
5. Five simulated samples are averaged into one row. The ring and timer reset and measurement mode stays active.
6. Right-click again for another capture, or click Stop to leave measurement mode.

A single PreviewMouseDown registration on MainWindow with handledEventsToo enabled detects right-button-down events while measurement mode is active. It works across the main window, including when navigating between pages. Inactive, minimized or disabled main windows, separate dialogs and other applications never trigger captures. There are no global hooks or per-page subscriptions to leak or duplicate. While armed, right-click is reserved for measurement triggering; while Idle, normal right-click behavior is untouched. Additional right-clicks during capture/processing are consumed and ignored. Left-click navigation, Start/Stop and other controls continue working normally.

Stop immediately resets the timer/ring and cancels an unfinished capture. No partial row is created. Completed rows stay intact. Stop remains available during capture and processing. Rapid Stop/Start cannot overlap the old capture: new triggers remain blocked until its cancellation cleanup completes. Late progress or results from a cancelled service are ignored.

Navigation retains the page ViewModels, measurement mode and results. An already-running capture can finish while another page is displayed; right-click can also trigger another capture from that page while measurement mode remains active. Editing Core Information invalidates its saved snapshot, stops measurement mode and cancels an unfinished capture; Save & Continue enables starting again. Existing rows retain their original core metadata.

## States and timing

MeasurementState contains Idle, Ready, Capturing and Processing. State transitions belong to MeasurementViewModel. Start moves Idle to Ready; a trigger moves Ready to Capturing; successful acquisition moves to Processing; a single committed row returns to Ready. Stop or disposal returns to Idle. Capture errors produce a short notification and return to Ready without a row.

MockMeasurementService uses Stopwatch elapsed time and asynchronous delays with approximately 40 ms progress updates. Rendering/animation never determines completion. A 100 ms display hold shows 00:00 during Processing; this does not extend the capture boundary. The capture length comes from `IMeasurementService.Duration`, which drives the countdown text and ring. Stop can cancel before the result is committed. The implementation remains a simulation: delayed scheduling can generate missed synthetic sample values at completion, and is not a real device acquisition implementation.

CountdownRing is a lightweight native WPF FrameworkElement. Its Progress property is elapsed / duration (0 to 1); it draws a neutral track and a clockwise arc with round ends, handling empty/full circles separately. It owns no timer or subscriptions.

## Timestamps and export

All timestamps are DateTimeOffset values with their original subsecond precision and offset:

- StartTime: the accepted right-button-down timestamp, for example 2026-10-11 11:16:10.851.
- EndTime: the intended five-second boundary, for example 2026-10-11 11:16:15.851.
- CompletedAt: when the measurement service actually completed, independently recorded. It can be later than EndTime if UI scheduling is delayed.

Start Time and End Time are stored and exported but hidden in the table. CompletedAt, the average, unit and core snapshot stay on the in-memory row only; they are not stored or exported.

## Columns

`Pages/Measurement/MeasurementColumns.cs` is the single column definition. Each entry has a stable binding path, a table label, a CSV header, table visibility, CSV inclusion and the value reader used by CSV export; list order is the column order for both table and CSV. Changing a label changes only the table header: bindings, CSV headers and SQLite column names are separate.

| Column | Table | CSV | Model field | SQLite column |
|---|---|---|---|---|
| No | Yes | Yes | `Number` (display position) | `DisplayNo` |
| Capture No | Yes | Yes | `CaptureNumber` | `CaptureNo` |
| Start Time | No | Yes | `StartTime` | `StartTime` |
| End Time | No | Yes | `EndTime` | `EndTime` |
| Sample | Yes | Yes | `SampleCount` (samples in the capture) | `Sample` |
| ts_ms | Yes | Yes | `Reading.TsMs` | `TsMs` |
| count0 | Yes | Yes | `Reading.Count0` | `Count0` |
| count0_raw | Yes | Yes | `Reading.Count0Raw` | `Count0Raw` |
| hdm | Yes | Yes | `Reading.Hdm` | `Hdm` |
| hdm_raw | Yes | Yes | `Reading.HdmRaw` | `HdmRaw` |
| dose_rate | Yes | Yes | `Reading.DoseRate` | `DoseRate` |
| MeasurementSessionId | No | Yes (last) | `MeasurementSessionId` | `MeasurementSessionId` |

**Missing mappings.** The capture pipeline had no source for ts_ms, count0, count0_raw, hdm, hdm_raw or dose_rate. They are now an optional `DeviceReading` on `CaptureData`, which a device service fills; raw and processed values are separate fields. `MockMeasurementService` does not invent them, so they are blank in the table and CSV and NULL in SQLite until a real CT007 service supplies them. Averaging and timing are unchanged.

The table builds these columns in `MeasurementPage.xaml.cs` between the drag handle and Delete columns. Their minimum width (`TableValueColumnMinWidth`) and its multiplier are in `MeasurementUiConstants.xaml` with the other sizes.

## Measurement sessions and SQLite

- **Start** begins a new measurement session with a new GUID. Every capture until the next Start gets that `MeasurementSessionId`. Stop, navigation, editing Core Information and restoring stored rows never create one.
- The table shows the current session only. Start clears the table (the previous session stays in the database) and Capture No starts again at 1; within a session it is never reused after delete or reorder.
- Database: `%LocalAppData%\CT007 Measurement Studio\measurements.db`. Tables `MeasurementSessions (MeasurementSessionId, CreatedAt)` and `MeasurementRecords (MeasurementRecordId, MeasurementSessionId FK, ...)`. A unique index on `(MeasurementSessionId, CaptureNo)` prevents duplicates and serves lookups by session.
- After each capture, reorder and delete, the ViewModel queues a save of the whole table. Saves run in order on the thread pool, never on the UI thread, in one transaction each, with parameterized SQL. Saving a row again only updates its No. Delete removes the row from the database too.
- If a save fails, a notification shows the error, the rows stay in the table and the next change saves them again. A failed delete is reported but not retried. Closing waits up to five seconds for queued saves.
- On startup the most recently stored session is shown again (Idle; it can be reordered, deleted and exported). Start then begins a new session.
- Schema version is kept in `PRAGMA user_version`. Migrations add only missing steps inside a transaction, so existing data is kept; a database from a newer app version is refused rather than changed.
- Timestamps are stored as ISO 8601 round-trip text (O) with full sub-second precision and offset; device values as INTEGER/REAL.

## Export

CSV contains the columns above in order, with MeasurementSessionId last, for the current session only, in the table's display order. Timestamps use the round-trip format (O); numbers use invariant culture and round-trip precision; missing device values are empty cells. Every cell is quoted with embedded quotes doubled; lines end in CRLF; UTF-8 BOM and temporary-file replacement are unchanged. Export rejects rows from more than one session.

## Existing features preserved

- Required form validation, decimal typing/paste filtering, live depth calculation and saved core snapshots.
- Blue/white theme, collapsible sidebar, active navigation, icon tooltips and retained sidebar state.
- Drag handle and Delete columns around the configured measurement columns; 36-pixel rows (at the default 1.0 multipliers) and alternating backgrounds.
- Measurement page sizes are Default x Multiplier values in `Pages/Measurement/MeasurementUiConstants.xaml`; every multiplier is 1.0 by default, which is the original look. They are applied with `{local:Scale ...}` (`ScaleExtension.cs`).
- Reordering with insertion feedback/edge scrolling, stable capture identifiers, delete confirmation, notifications and CSV save dialog.
- Completed results remain across navigation and, through SQLite, across restarts.

## Source organization

```text
Frontend/
├── Pages/
│   ├── CoreInformation/
│   │   ├── CoreInformationPage.xaml
│   │   ├── CoreInformationPage.xaml.cs
│   │   └── CoreInformationViewModel.cs
│   └── Measurement/
│       ├── MeasurementPage.xaml
│       ├── MeasurementPage.xaml.cs
│       ├── MeasurementViewModel.cs
│       ├── MeasurementState.cs
│       ├── CountdownRing.cs
│       ├── MeasurementResult.cs
│       ├── MockMeasurementService.cs
│       ├── IDialogService.cs
│       ├── CsvExportService.cs
│       ├── MeasurementColumns.cs
│       ├── MeasurementRepository.cs
│       ├── MeasurementStyles.xaml
│       ├── MeasurementUiConstants.xaml
│       └── ScaleExtension.cs
├── Windows/ConfirmationWindow/
│   ├── DeleteConfirmationWindow.xaml
│   ├── DeleteConfirmationWindow.xaml.cs
│   └── DialogService.cs
├── Shared/
│   ├── Infrastructure/ (ObservableObject, commands)
│   ├── Models/ (CoreSnapshot)
│   └── Styles/ (Theme.xaml)
├── ViewModels/MainViewModel.cs
├── MainWindow.xaml + MainWindow.xaml.cs
├── App.xaml + App.xaml.cs
├── AssemblyInfo.cs
└── Frontend.csproj
SmokeTests/
├── SmokeTests.csproj
└── Program.cs
```

## Validation

Columns/SQLite revision: the ViewModel, repository, column configuration, CSV export and models were compiled with the .NET 10 SDK (warnings as errors) and the full smoke-test sequence passed against a real SQLite database on Linux. That environment had no NuGet access or Windows desktop pack, so SQLite was reached through a test-only stand-in for the `Microsoft.Data.Sqlite` calls, and the WPF project itself (XAML, `MeasurementPage.xaml.cs`, `ScaleExtension.cs`) was not compiled. XAML files parse as XML. Run the build and smoke tests on Windows to confirm.

Run the updated smoke tests on Windows:

```powershell
dotnet run --project SmokeTests/SmokeTests.csproj
```

They cover arming without capture; Idle/duplicate triggers; elapsed ring progress; repeat captures; exact millisecond start/end values and separate completion; cancellation in Capturing and Processing; rapid rearm; late cancelled results; service failure; stable identifiers; reorder/delete/export; navigation/sidebar state; five-second duration; closure and cancellation; session IDs per Start and across navigation; CSV column order, precision and session column; stored No after reorder; no duplicate saves; save-failure reporting; SQLite session isolation, precision, nulls, delete and restart restore. They do not substitute for real mouse/UI tests.

Manual Windows checks:

1. Start and wait: Stop appears, timer stays at five, no row appears.
2. Right-click anywhere in the main window: one five-second capture, full ring/zero, one averaged row, reset to Ready. Repeat and rapidly right-click during capture.
3. Stop mid-capture and during zero/Processing: no cancelled row. Restart and repeat. Previously completed rows stay unchanged.
4. While armed, verify main-window right-clicks on the panel, table, sidebar and Core Information each trigger only one capture when Ready. While Idle, verify none trigger captures. Verify another application or an open modal dialog never triggers the main window.
5. Switch pages/collapse the sidebar during capture, return, and confirm state/result continuity. Edit the core and verify mode disarms until saved again.
6. Verify the ring at 0%, 50%, 100%, resizing, and 125%/150% display scaling. Check the compact table remains scrollable at minimum window size.
7. Reorder/delete/export results. Compare table seconds-only display with precise CSV start/end/completion timestamps and offsets.
8. Close during capture; verify no uncaught cancellation errors.

## Window-wide trigger revision

The latest revision only moves right-click routing from the countdown card to a single MainWindow input handler and removes the obsolete page handler/tooltip. Measurement state, ring, averaging, timestamps, table columns/layout, CSV export and all other commands remain unchanged. Source/reference checks passed. Build was attempted but could not start because dotnet is unavailable; Windows runtime/input validation remains unexecuted.

## Window input reliability

MainWindow registers one PreviewMouseDown handler with handledEventsToo enabled and removes it on closure. The handler does not discard an event merely because a child control marked it handled. Window-level tunneling covers the main window client area, including blank areas and existing controls, without page-specific or global subscriptions. Armed context-menu openings are suppressed; normal right-click behavior resumes after Stop. An explicit minimized-state guard supplements the active/enabled-window checks. The ViewModel still rejects busy triggers and owns the capture lifecycle.

Only MainWindow.xaml.cs and this README changed in this revision. Source checks verified those changes and unchanged measurement code. Windows build and runtime tests remain unexecuted because dotnet is not installed. On Windows, additionally check right-clicks over an input, table row, button and blank space while armed, and confirm minimized/inactive/Idle input never captures.
