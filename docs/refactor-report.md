# Refactor report: performance and code quality (branch `refactor/performance-quality`)

The app looks and behaves the same as before.
- **Tests:** after every commit, the full suite with `--include=Timing` shows 0 failures. That is 211 tests: 202 passed and 9 skipped. The 2 new tests are FE-PERF-04 and FE-PERF-05.
- **Rendering:** all page pictures were rendered before and after and compared pixel by pixel. The only differences are clock times and random temp-folder names.

## Before / after

**Method:** Release builds on the same laptop.
- **In-app numbers:** `PERF-001`, an opt-in test run with `dotnet run -c Release --project Tests -- PERF --include=Perf`. It renders the real window off-screen, uses fake devices and writes to a real SQLite database.
- **Startup:** time from launching the Release exe to its first idle window, 6 launches each.

| Measure | Before | After |
|---|---|---|
| Startup, first launch after build (cold) | 1.65 s | 1.73 s (one sample each; within run-to-run noise) |
| Startup, warm (mean of 5) | 0.57 s | 0.57 s |
| Working set 3 s after start | ~172 MB | ~172 MB |
| Window shown + idle, in-process | 0.71 s | 0.68 s |
| Page switch, mean / p95 | 63 / 97 ms | 55 / 86 ms |
| Capture to row on screen | 121–126 ms | 116–120 ms (100 ms of it is the deliberate "00:00" display hold) |
| Background save per capture (200-row session) | 4.6–6.2 ms | 4.8–5.7 ms |
| Monitoring: 5,000-reading backlog applied and drawn | 13 ms | 16 ms |
| Memory growth over 19 navigation cycles (empty chart) | 0.26 MB | 0.26 MB |
| Release output folder | 58 MB | 58 MB |
| `Frontend.dll` | 638 KB | 659 KB (+21 KB from the new classes) |

**Leak:** the old code kept every Measurement page's chart alive (and its page with it) for as long as the app ran. FE-PERF-04 shows that a chart taken off screen is now garbage-collected, and the same test fails on the old code. The memory row above doesn't show this, because its chart is empty; with real data, each visit used to leave its chart and page in memory.

**No change needed** (measured, so these items from the plan were left alone):
- **Capture saves** already skip unchanged rows (`ON CONFLICT … WHERE SortOrder <> …`) and cost about 5 ms off the UI thread.
- **Monitoring backlogs** take 16 ms even at the 5,000-reading maximum.
- **`DrillholeCatalog.Build`** runs on the UI thread only when you save a new drillhole.
- **Settings files and the Help image check** are sub-millisecond operations, done on user actions.

## What changed (13 commits, one concern each)

**Removed**
- 6 unused Theme resources: `ButtonPrimary`, `HelpIcon`, `PlayIcon`, `StopIcon`, `ZeroLengthVisibility`, and the `AppIcon` drawing. The window uses `Icons/icon.ico`.
- `MainViewModel.ShowHelp()`, which only called `ToggleHelp()`.
- `MeasurementTableView`, a UserControl with an empty code-behind used once. Its card now sits in `MeasurementPage.xaml`, one level less in the visual tree.
- 6 unused `using` lines and the unused `args` of `App.Main`.

**Merged or shared**
- **`StrokeIcon` style** (Theme): replaces 8 copies of the 1.8 px rounded-stroke icon settings.
- **`FieldPanel`**: defined once in Theme instead of in both Core Information and Data Viewer.
- **Measurement table row style**: now `BasedOn` Theme's `DataTableRow`, which it had copied.
- **`ComboBoxTyping.OpenListKeepingCaret`**: one copy of the drillhole search-box typing code, used by Core Information and Data Viewer.
- **`Polling.RunAsync`**: one run/wait/cancel loop, used by `BackendConnection`, `DeviceStatusMonitor` and Monitoring.

**Performance**
- `DepthBarChart` subscribes to the rows only while on screen (the leak fix above), and reuses one frozen transform for the y title.

**Layers (MVVM, with the ViewModel as the presenter)**
- **Measurement model classes**, moved out of `MeasurementViewModel` into `Pages/Measurement/Model/`:
  - `MeasurementResultFactory`: sample averaging into a row.
  - `SerialWriteQueue`: database writes in request order.
  - `LiveCpsPoller`: reads off the UI thread.
- **Device Status** has its own `DeviceStatusViewModel`; it was part of `CoreInformationViewModel`.
- **Measurement data classes** are grouped in `Pages/Measurement/Model/`: repository, schema, capture types, result row and retention.

**Tooling**
- `.editorconfig` makes IDE0005, IDE0051, IDE0052 and IDE0060 (unused usings, members and parameters) build warnings in Frontend. The build is clean.
- The XML documentation file those rules need is not published.

**Naming**
- The `Api/Core-information/drillholes` and `Calculation/Core-Information` folders are now `CoreInformation/Drillholes` and `CoreInformation`, matching their namespaces.
- `device-settings.json` still deploys to `Calculation\Core-Information\`, so installed copies and the README stay valid.

## Folder layout

```
Frontend/
  Api/                      HTTP client, backend connection, DTOs   (Model)
    CoreInformation/Drillholes/
    Measurements/
  Calculation/              device checks, sample maths, no WPF     (Model)
    CoreInformation/  Measurement/
  Pages/<Feature>/          one folder per page
    <Feature>Page.xaml      View
    <Feature>ViewModel.cs   ViewModel (presenter)
    Model/                  that page's data and rules (Measurement so far)
    <Section>/              sub-views with their settings (CapturePanel, Table, DeviceStatus, …)
  Shared/                   Theme, Help drawer, infrastructure (commands, converters, helpers)
  ViewModels/               MainViewModel (composition root), navigation
```
Each feature has one namespace, and its folders are split by layer. Classes in `Model/` keep the feature's namespace.

## Left for review (not changed)
- **Pages are re-created on every navigation**, and the table's scroll position is lost. Caching the views would be faster, but it would keep the scroll position, which is a visible change.
- **Monitoring's Detectors grid sits inside the page's ScrollViewer**, so it isn't virtualized. It has a few rows; changing it would change the page's scrolling.
- **`MeasurementViewModel.Dispose` waits up to 5 s for queued saves when the app closes.** It protects unsaved captures, so it was kept.
- **ViewModels are still large:**
  - `MeasurementViewModel`: 1,086 lines, from 1,173.
  - `CoreInformationViewModel`: 870 lines, from 905.

  The next splits are the alarm/threshold block and the depth inputs (Measurement), and the drillhole list's cache-then-server loading (Core Information). Those change binding paths and the order of UI updates, so they need their own reviewed step.
- **`DrillholesClient` and `MeasurementDatabaseCheck`** look like pass-throughs, but they are the typed API and check boundaries used by tests. Kept.
- **Repeated inline font sizes (11/12/13) and margins** were not replaced with tokens. They vary by context, and a blanket token would invite unintended changes.
- **Members only tests use:** `ShowDemoTsMs`/`DemoTsMs`, `LoadSamplesAsync`, `HelpSettings.For`, `GammaGuardDatabaseCheck.SelectedPath`, `IsShown`, `CaptureDuration`, `IsPolling`.
- **`Frontend/.env` (untracked)** is never read by the app.
- **DOCS-001 already failed before this branch:** Help marker 7 on Core Information sits about 1.5% higher than `help.yaml` says. That probably comes from the earlier text-box padding fix on `main`. Re-run DOCS-001 to update `help.yaml`.
- **The 3 E2E tests and the SQL tests** need a dedicated account and a database, and were not run.

## Repeating the measurements
- **In-app numbers:**
  ```powershell
  dotnet run -c Release --project Tests -- PERF --include=Perf
  ```
- **Startup:** launch the Release exe several times and time it to the first idle window.
- **Profiler checks.** Run these by hand on the Release build:
  - Visual Studio Performance Profiler: CPU Usage and .NET Object Allocation while capturing and switching pages.
  - Diagnostic Tools, Memory Usage: take snapshots before and after visiting Measurement 10 times. No `DepthBarChart` instances should be left.
  - Live Visual Tree: check the depth of each page.
