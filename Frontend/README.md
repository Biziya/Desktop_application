# SPP CT-007 Measurement Studio

WPF / .NET 10 application. Captures read real CT007-M samples from the local Gamma Guard SQLite database (read-only); there is no simulated acquisition path. Measurement rows are stored in a local SQLite database (`Microsoft.Data.Sqlite`, the only NuGet dependency).

## Run on Windows

Install the .NET 10 SDK with Windows desktop support. From the extracted archive:

```powershell
dotnet restore Frontend/Frontend.csproj
dotnet build Frontend/Frontend.csproj
dotnet run --project Frontend/Frontend.csproj
```

Replace the old Frontend folder rather than overlaying it, because earlier versions used different source paths. Preserve your Backend project and solution files. The project path, project name and target remain Frontend/Frontend.csproj, Frontend and net10.0-windows. No compiled binaries are included.

## Measurement workflow

1. Select a drillhole on Core Information (its stored rows and progress appear on the Measurement page) and click Save & Continue.
2. Click green **Start** to activate measurement mode. The button becomes red **Stop**. Nothing is captured yet. While measuring, the drillhole cannot be changed or cleared (Stop first).
3. Right-click anywhere in the active **main application window** to begin a capture of the configured duration (2–10 s, default 5 s; click the countdown while no capture runs to change it), including the panel, table, sidebar or other main-window controls.
4. The ring displays elapsed progress clockwise from the top while the centered timer counts down from the duration (e.g. 00:05) to 00:00.
5. The device samples written after the click (about one per second, so up to 5 for 5 s) are averaged into one row (see "Real device capture"). The ring and timer reset to the duration and measurement mode stays active.
6. Right-click again for another capture, or click Stop to leave measurement mode. Start again later continues the same measurement; click **New Measurement** (while stopped) to begin a separate one.

A single PreviewMouseDown registration on MainWindow with handledEventsToo enabled detects right-button-down events while measurement mode is active. It works across the main window, including when navigating between pages. Inactive, minimized or disabled main windows, separate dialogs and other applications never trigger captures. There are no global hooks or per-page subscriptions to leak or duplicate. While armed, right-click is reserved for measurement triggering; while Idle, normal right-click behavior is untouched. Additional right-clicks during capture/processing are consumed and ignored. Left-click navigation, Start/Stop and other controls continue working normally.

Stop immediately resets the timer/ring and cancels an unfinished capture. No partial row is created. Completed rows stay intact. Stop remains available during capture and processing. Rapid Stop/Start cannot overlap the old capture: new triggers remain blocked until its cancellation cleanup completes. Late progress or results from a cancelled service are ignored.

Navigation retains the page ViewModels, measurement mode and results. An already-running capture can finish while another page is displayed; right-click can also trigger another capture from that page while measurement mode remains active. Editing Core Information invalidates its saved snapshot, stops measurement mode and cancels an unfinished capture; Save & Continue enables starting again. Existing rows retain their original core metadata.

## States and timing

MeasurementState contains Idle, Ready, Capturing and Processing. State transitions belong to MeasurementViewModel. Start moves Idle to Ready; a trigger moves Ready to Capturing; successful acquisition moves to Processing; a single committed row returns to Ready. Stop or disposal returns to Idle. Capture errors produce a 5 s error toast and return to Ready without a row.

`DeviceCaptureService` (`Calculation/Measurement/`) polls the Gamma Guard database every 250 ms and reports the countdown from the laptop clock (`ICaptureClock`). Rendering/animation never determines completion. A 100 ms display hold shows 00:00 during Processing; this does not extend the capture boundary. The capture length is the configured duration, frozen when the capture starts; it drives the countdown, the ring and `EndTime = StartTime + duration`. The average is computed from the samples actually accepted, never from the duration.

CountdownRing is a lightweight native WPF FrameworkElement. Its Progress property is elapsed / duration (0 to 1); it draws a neutral track and a clockwise arc with round ends, handling empty/full circles separately. It owns no timer or subscriptions.

## Timestamps and export

All timestamps are DateTimeOffset values with their original subsecond precision and offset:

- StartTime: the accepted right-button-down timestamp, for example 2026-10-11 11:16:10.851.
- EndTime: the intended capture boundary (StartTime + the capture's duration), for example 2026-10-11 11:16:15.851 for 5 s.
- CompletedAt: when the measurement service actually completed, independently recorded. It can be later than EndTime if UI scheduling is delayed.

Start Time and End Time are stored and exported but hidden in the table. CompletedAt, the average, unit and core snapshot stay on the in-memory row only; they are not stored or exported.

## Columns

How the table shows each column — order, header, visibility, width (star share, Auto or pixels), minimum and maximum width, display format and body-cell alignment — is set in `Pages/Measurement/Table/MeasurementTable.yaml`, together with the table's fonts, heights, paddings and multipliers (see "Section configuration"). The columns themselves are fixed in `Table/MeasurementTableColumns.cs`: each stable id (`count0`, `tsMs`, …) maps to a model property, a CSV header, CSV inclusion and the value CSV export writes, in CSV order. Changing a header or hiding a column changes only the table: it never renames a model or SQLite field, never removes a value from the CSV, and never changes the CSV column order.

| Column | Table | CSV | Model field | SQLite column |
|---|---|---|---|---|
| No | Yes (52 px) | Yes | `Number` (displayed row position, From order) | `SortOrder` |
| Capture No | No | Yes | `CaptureNumber` | `CaptureNo` |
| Start Time | No | Yes | `StartTime` | `StartTime` |
| End Time | No | Yes | `EndTime` | `EndTime` |
| Sample | Yes | Yes | `SampleCount` (samples in the capture) | `Sample` |
| ts_ms | Yes | Yes | `Reading.TsMs` | `TsMs` |
| count0 | Yes | Yes | `Reading.Count0` | `Count0` |
| count0_raw | Yes | Yes | `Reading.Count0Raw` | `Count0Raw` |
| hdm | Yes | Yes | `Reading.Hdm` | `Hdm` |
| hdm_raw | Yes | Yes | `Reading.HdmRaw` | `HdmRaw` |
| dose_rate | Yes | Yes | `Reading.DoseRate` | `DoseRate` |
| session_id | No | Yes (last) | `SessionId` | `session_id` |

**Device values.** ts_ms, count0, count0_raw, hdm, hdm_raw and dose_rate come from the accepted Gamma Guard samples (`DeviceReading` on `CaptureData`); raw and processed values stay separate fields. **Sample** shows "accepted of expected" (e.g. "3 of 5") for an incomplete capture; CSV keeps the accepted count. Rows stored before this version keep blank device values — nothing is invented.

The table builds these columns in `Table/MeasurementTableView.xaml.cs` before the Delete column. All visible columns use star widths (`MeasurementTable.yaml`, in the proportions of the earlier compact layout), so they always share the table width — no horizontal scrolling with the sidebar expanded or collapsed, and at every window size down to the 960 px minimum (minimum widths 576 px + 64 px Delete fit the ~650 px available there). Headers wrap onto two lines when a column is narrow; a value wider than its column ends in "…" and the full value is its tooltip, so nothing is cut off silently. The horizontal scrollbar stays enabled only as a safety net and is not needed in any supported layout; vertical scrolling is unchanged. Vertical grid lines separate every column and continue through the header, in the same `Line` brush as the row lines.

**Cell display** (`Table/MeasurementCellFormat.cs`, set per column with `format` in `MeasurementTable.yaml`; table only): ts_ms (Unix milliseconds, UTC) is shown as this computer's local time `yyyy-MM-dd HH:mm:ss` (empty when missing, the raw number if out of range); count0, count0_raw, hdm, hdm_raw and dose_rate show at most two decimals (1.777777 → 1.78, 5 → 5, 1.5 → 1.5; empty when null). Body cells are right-aligned (set per column with `Alignment`); headers stay centred. Models, calculations, SQLite and CSV keep the full-precision values and Unix milliseconds. From/To keep their millimetre precision (up to three decimals in metres), since rounding would misstate the interval. ts_ms has the widest share of the table; at the narrowest window it may end in "…" with the full value as tooltip.

## Measurement sessions and SQLite

- **One session per drillhole.** Save & Continue on Core Information looks up the Drillhole ID in `MeasurementSessions`: an existing session is reused (same `id`, original `CreatedAt`, stored metadata kept — differing name/project/operator/comment are reported, not written), otherwise a session is created with DrillholeID, DrillholeName, Project, operator and comment. A unique index on DrillholeID plus `INSERT … ON CONFLICT (DrillholeID) DO NOTHING` means concurrent saves can never create two sessions for one drillhole. The same Drillhole ID always resolves to the same session, also after a restart; the table then shows that session's rows.
- **Start / Stop** only start and stop acquisition. Stop keeps every row; Start again continues the same session, appending rows (Capture No continues) from the saved next depth and step.
- **New Measurement** (table header, enabled while stopped) closes the current drillhole; enter the next drillhole in Core Information and click Save & Continue. Saving the same Drillhole ID again reopens the same session.
- Database: `%LocalAppData%\CT007_Measurement_Studio\measurements.db` (shown on the Core Information page). A database in the old `CT007 Measurement Studio` folder is moved there automatically on first start; if it cannot be moved (file open in another program), the old location keeps being used. One shared table for all sessions and one for all records: `MeasurementSessions (id, CreatedAt)` and `MeasurementRecords (id, session_id, SortOrder, CaptureNo, ...)`, with `MeasurementRecords.session_id → MeasurementSessions.id` (foreign keys enforced, no cascading delete). The unique index `UX_MeasurementRecords_Session_CaptureNo (session_id, CaptureNo)` prevents duplicates and, with session_id first, is the index used for lookups by session.
- **Row order: always by From.** Rows are shown in ascending numeric From; equal From values by capture Start Time, then by record ID (`MeasurementRecords.id`; rows not stored yet after stored ones), then Capture No; rows without depths (stored before depths existed) last — `Calculation/Measurement/MeasurementOrder.cs`. Loaded rows are sorted, a completed capture is inserted at its place, and No is renumbered to the displayed positions. Manual reordering (the drag-handle column, drag-and-drop handlers and the move confirmation) is removed; column-header sorting stays disabled, so nothing overrides the order. CSV rows are written in the same order. `SortOrder` is kept and simply stores the displayed position (no migration; an old manual order in an existing database is ignored on load). Record IDs, capture numbers, intervals, times and values never change. After each capture and delete the ViewModel queues a save of the table; in one transaction it inserts new rows and rewrites `SortOrder` only where it changed.
- The database uses WAL journal mode, so external tools (DBeaver, `tools/check-db.ps1`) can read it while the app is running and the app keeps saving meanwhile. SQLite has no users or permissions: any program running as the same Windows user can open the file. Keep `measurements.db`, `measurements.db-wal` and `measurements.db-shm` together if you copy it.
- Saves run in order on the thread pool, never on the UI thread, with parameterized SQL. Delete removes the row from the database too.
- If a save fails, a 5 s error toast shows the error and the rows stay in the table. The next change, Stop or closing saves them again. New Measurement refuses to switch sessions until they are saved. A failed delete is reported but not retried. Closing waits up to five seconds for queued saves.
- The Measurement page follows the drillhole selected on Core Information. At startup nothing is selected, so it is empty; selecting a drillhole shows its stored rows (sorted by From), session and progress — also after a restart — without creating a session (Save & Continue creates one when needed). Clearing the selection empties the view (no data is deleted). A load that finishes after a newer selection is discarded. Start continues the shown session; New Measurement starts another. Earlier sessions can be loaded by ID through the repository (`LoadAsync`); there is no session picker in the UI.
- Schema version is kept in `PRAGMA user_version` (now 5 — see "Real device capture" for version 5; version 4 adds the session metadata columns, the DrillholeID unique index, the record depth columns and `MeasurementProgress`, all without changing existing rows. If a DrillholeID column already existed with duplicate values — only possible if the file was edited outside the app — the upgrade stops, names them and changes nothing, because merging would require renumbering captures). Migrations add only missing steps inside a transaction, so existing data is kept: version 2 renames `DisplayNo` to `SortOrder`; version 3 renames `MeasurementSessionId`/`MeasurementRecordId` to `id` and `session_id` with `ALTER TABLE ... RENAME COLUMN`, keeping every ID, value, link and the index. After migrating, a foreign-key check must find no record without a session, or the upgrade is rolled back. A database from a newer app version is refused rather than changed.
- Timestamps are stored as ISO 8601 round-trip text (O) with full sub-second precision and offset; device values as INTEGER/REAL.

## Depth intervals

- **Units.** Depths are stored and calculated as whole millimetres (INTEGER), exactly as before — nothing stored is reinterpreted. Conversion happens only at the UI boundary (`Calculation/Measurement/Depth.cs`): **Starting Depth, From, To and the chart depth axis are in metres** (1 m = 1000 mm); **Step is in centimetres** (1 cm = 10 mm), so `to_m = from_m + step_cm / 100` — Starting Depth 10 m with Step 25 cm gives To 10.25 m. Saved steps are shown converted (250 mm → "25"). Input finer than 1 mm is rejected (metres: at most 3 decimals; centimetres: at most 1), so intervals add up exactly with no floating-point drift. A point or comma is accepted as decimal separator. `"from"`, `"to"` and `step` in SQLite are millimetres; CSV units are unchanged (the depth columns are not exported; if enabled, Step would export as `step_m` in metres).
- **Inputs.** The Measurement page control area is one row: a control column (**Starting Depth (m)**, **Step (cm)** and the **Start / Stop** toggle — same width, top to bottom), the bar chart in the middle (remaining width, about 205 px at the smallest window) and the capture countdown on the right. The single toggle shows a green Start while stopped and a red Stop while measuring; it is exactly as tall as the two input boxes. There is no To input: `to = from + step`. Both inputs start from the session's saved progress; a session without progress starts empty. Start is enabled once a drillhole is saved for measuring; if a depth is missing or invalid, clicking it shows a 5 s error toast instead of arming. Invalid text gets a red border while typing and an error toast when the box is left. Depth entry is only on this page.
- **Per capture.** When a capture begins, its interval is fixed: `from` = Starting Depth, `to = from + step`, `step` = Step. The completed row stores all three; Starting Depth then becomes that `to` — once per completed capture (one row per capture). The next depth always comes from this capture state, never from the last displayed row (the table is sorted by From, so re-measuring a shallower interval puts that row first and continues from its `to`). Changing Step or Starting Depth during a capture applies to the next capture; a starting depth typed during the capture is not overwritten by the advance. Cancelled, stopped or failed captures do not advance.
- **Progress.** Starting Depth and Step are saved per session in `MeasurementProgress (session_id, next_from_cm, step_cm)` (centimetres, REAL; converted from the earlier millimetre columns once by migration 5) — in the same transaction as the rows, so a capture and its depth advance commit together and a retried save cannot advance twice. They are restored after Stop → Start, when reopening the drillhole and after a restart. (There was no earlier settings storage to reuse.)
- Sorting never changes depths. Rows stored before depths existed keep null `from`/`to`/`step` and sessions null metadata — nothing is invented. The table shows each row's saved depths as read-only **From (m)**, **To (m)** and **Step (cm)** columns (fields `FromMetres`/`ToMetres` = mm ÷ 1000 and `StepCentimetres` = mm ÷ 10, exact; layout in `Table/MeasurementTable.yaml`). The chart tooltip shows the step in cm too. They are not exported, so the CSV columns are unchanged (one `IncludeInCsv` flag each to change that).

## Export

CSV contains the measurement columns above in order (including the hidden Capture No, Start Time and End Time), then the session metadata — **Drillhole, Project, Campaign, Operator, Notes** (stored as `DrillholeID`, `DrillholeName`, `Project`, `operator`, `comment`; Campaign appears once) repeated on every row — and session_id last, for the current session only, in the saved row order. Timestamps use the round-trip format (O); numbers use invariant culture and round-trip precision; missing device values are empty cells. Every cell is quoted with embedded quotes doubled, so commas, quotes and line breaks in Notes stay in one cell; user text starting with = + - @ is prefixed with ' so spreadsheets do not run it. Lines end in CRLF; UTF-8 BOM and temporary-file replacement are unchanged. Export rejects rows of another session. ts_ms is the device value only: demo values are never exported.

## Bar chart and demo ts_ms

- `DepthBarChart.cs` draws one vertical bar per table row: left edge at `from`, right edge at `to` (so bar widths follow the actual interval and step changes), height from 0 to the row's ts_ms. Bars are placed by depth, so reordering rows does not move them; rows without depths or ts_ms are skipped. Axes: Depth (m) and ts_ms, with round tick values (large ts_ms shortened, e.g. 1.76T). Hovering a bar shows From, To, Step and ts_ms. Colours, text and grid lines use the theme brushes.
- **Demo mode is off** (`showDemoTsMs: false`): the table and chart show the device ts_ms only. ts_ms is a Unix epoch time in milliseconds, so the bars of neighbouring captures are almost equally tall; plotting another value (e.g. count0 or dose_rate) is a one-line change in `DepthBarChart.cs` once you decide which one.

## Icon

`Icons/icon.svg` is the source icon. WPF cannot render SVG files, so `Theme.xaml` (`AppIcon`) draws the same shapes for the sidebar brand area, and `Icons/icon.ico` (16–256 px, rendered from the same geometry) is the exe and window icon.

## Backend API (Core Information)

Base URL and timings are in `Api/api-settings.json` (copied next to the executable; edit it there, no rebuild): `baseUrl` (default `http://localhost:5002`), `requestTimeoutSeconds` (5) and `statusIntervalSeconds` (5).

```text
Api/
├── api-settings.json, ApiSettings.cs   configuration
├── ApiClient.cs, ApiResult.cs          the one HttpClient (base URL, timeout, JSON) and typed results
├── BackendConnection.cs                Online/Offline monitor (HEAD probe of the base URL)
└── Core-information/
    └── drillholes/         DrillholesClient + DrillholeResponse            GET /api/core-information/drillholes
                            DrillholeCache (last good list, local SQLite)
```

The frontend no longer calls `/api/core-information/bluetooth-device` or `/api/core-information/sqlite-database` (their clients were removed; the backend endpoints are untouched). Device Status is checked on this laptop — see "Device Status (local checks)".

- **Results.** Every request returns `ApiResult<T>`: Ok, Unreachable (refused / network failure / timeout), HttpError (the backend answered with an error status) or InvalidResponse. Requests are asynchronous; no HTTP code is in code-behind.
- **Drillhole** is a searchable dropdown fed by `/drillholes`: typing filters by any part of the Hole ID (case-insensitive), the closest match (exact, then prefix, then partial) is highlighted and Enter picks it, arrow keys + Enter or the mouse pick any entry, Escape closes the list, and typing never picks or rewrites the text. Picking fills **Project** (`projectCode` → `DrillholeName` → SQLite `DrillholeName`) and **Campaign** (`campaign` → `ProjectArea` → SQLite `Project`); both are read-only. These are the existing mappings, so saved sessions, CSV metadata and the "same DrillholeID → same session" rule are unchanged. Changing the text away from the picked Hole ID clears the pick. A refresh never changes the pick or other form values; if the picked drillhole is missing from the new list its details are kept and a note says so. Drillhole is the only required field.
- **Drillhole cache.** `DrillholeCache` keeps the last good `/drillholes` list (holeId, projectCode, campaign) in its own `DrillholeCache` table in the app's SQLite file — no IndexedDB, and separate from `MeasurementSessions`/`MeasurementRecords` (nothing joins them). On start the cached list is shown at once, then the backend is asked in the background. Only a successful response with at least one drillhole replaces the cache, in one transaction (delete + insert, all or nothing). A failure, timeout, invalid JSON or empty answer keeps the cache and shows "Showing the saved drillhole list. Could not refresh: …" with Retry. The selection and unsaved form values are never touched. With no cache and no backend the box is empty and the message says no drillholes are available, with Retry.
- **Groups in the dropdown.** **Measured drillholes** come first: drillholes that have saved measurement rows, newest first by the latest capture Start Time (`IMeasurementRepository.LoadMeasuredDrillholesAsync`, not table order). The group is hidden when there is no history, comes back after a restart, and is refreshed after every successful save (`MeasurementViewModel.MeasurementsSaved`). **All drillholes** follows with the rest, A–Z. Each drillhole appears once. A measured drillhole missing from the current API/cache list is not offered (an already picked one keeps its details, with the "not in the latest list" note). Headings are not selectable, focusable or highlighted, and a thin line separates the groups. Search filters both groups and keeps a heading only while its group has matches. Mouse, arrow keys and Enter work as before, and picking still fills Project and Campaign.
- **Dropdown performance (many drillholes).** The cause of the freeze was the list being rebuilt item by item on the UI thread for every keystroke and pick (one collection notification per drillhole) into a dropdown whose popup did not virtualize. Now: the list is prepared once per load off the UI thread (`DrillholeCatalog`: index by Hole ID, A–Z order, one reusable entry object per drillhole); typing is debounced (150 ms) and filtered on the thread pool; every request has a version, so a slower, older result can never replace a newer one; only the difference is applied to the bound collection (a few inserts/removes keep the selection; a large change is one Reset via `BulkObservableCollection`); a refresh with identical data changes nothing. The ComboBox uses a recycling `VirtualizingStackPanel` with item scrolling (`CanContentScroll`, also when grouping), so only visible rows get controls; the popup is limited to 280 px and scrolls. All matches stay listed (no truncation). Measured: 20,000 drillholes load in about 0.2 s; a keystroke costs the UI thread under 0.1 ms; seven fast keystrokes cause one list update.
- **Sidebar indicator** (bottom of the sidebar, both pages): grey "Checking…" until the first answer, green **Online** when the backend answered (also with an HTTP error status, which is shown on the affected field instead), yellow **Offline** on refusal, network failure or timeout. Collapsed, the dot stays and the tooltip gives the status, URL and last check time. It reflects the backend only and is independent of the local Device Status (a disconnected CT-007 still shows Online; Offline never changes the device rows).
- **Polling.** There is no health endpoint, so `BackendConnection` sends a HEAD request to the base URL every `statusIntervalSeconds` (any HTTP answer, even 404/405, means reachable; nothing is downloaded). Every other request also updates Online/Offline, and the drillhole list is never downloaded just to check connectivity. Checks never overlap (a running one is reused), the loop starts once and stops when the window closes. Navigation does not create new monitors.
- **Loading and recovery.** The drillhole list shows its own loading / empty / error message and a Retry button. When the backend comes back Online it loads again automatically if it had failed. Form values and measurements are never cleared by a failure, and no path, device or drillhole is ever made up.

## Device Status (local checks)

The backend may run on another computer, so Device Status describes **this laptop** and works without the backend. `DeviceStatusMonitor` (one per app) runs the three checks on the thread pool at start, every `checkIntervalSeconds` and on Retry; a check never overlaps another of its kind, results are published on the UI thread, and everything stops when the window closes. Rules are in `Calculation/Core-Information/device-settings.json` (next to the executable, editable).

```text
Calculation/
├── Core-Information/
│   ├── LocalStatus.cs, SqliteFileCheck.cs, DeviceSettings.cs (+ device-settings.json), DeviceStatusMonitor.cs
│   ├── BluetoothDevice/      Ct007BluetoothCheck (identification, status), WindowsBluetooth (Windows Bluetooth API)
│   ├── MeasurementDatabase/  MeasurementDatabaseCheck
│   └── GammaGuardDatabase/   GammaGuardDatabaseCheck (discovery + read-only check)
└── Measurement/              Depth (m / cm / mm conversion), MeasurementOrder (From order)
```

- **CT007-M Device — Bluetooth (select, then monitor).** A dropdown lists the CT007-M devices paired with or connected to this laptop (name beginning with `ct007NamePrefix` "CT007-M-", case ignored, or the address `ct007Address` when set), shown as name + Bluetooth address and refreshed every `checkIntervalSeconds` and whenever the dropdown opens. Before a choice the row says **No device selected** (grey) and no connection check runs. The user picks the device explicitly; it is identified by its **Bluetooth address** (the name is only shown) and is never selected or switched automatically — a list refresh, a vanished device or a failed read keeps the selection. Selecting only starts monitoring: no pairing, connection or inquiry scan. From then on only that device is checked, every `bluetoothCheckIntervalSeconds` (1 s), off the UI thread, never overlapping. Detection uses the logic of the former backend's `BluetoothDeviceRepository`, which worked on the measurement laptop (`BluetoothDevice/WindowsBluetooth.cs`, Windows Runtime API — hence the `net10.0-windows10.0.19041.0` target): the radio must be on; Bluetooth Classic and LE devices are enumerated (paired and connected selectors) and every device's state is re-read from its live `ConnectionStatus`; the selected device is opened by its Windows device ID each second; each Windows call is limited to 3 s. **Connected** only when that live status is Connected; **Disconnected** when the check worked and the device is only paired ("paired with this laptop, not connected") or no longer known; **Status unavailable** when the check itself fails (no adapter, Bluetooth off, API error) — never Disconnected then. Changing the selection shows Checking at once and discards a late result for the previous device. The one monitor lives for the whole app (navigation never adds a loop) and stops when the window closes. There is no Retry button. The selection is kept in memory for the session (not saved). **Informational only:** Bluetooth status never enables, blocks, cancels or changes a capture, saving or export (`Calculation/Core-Information/BluetoothDevice/`, `DeviceStatusMonitor`).
- **Measurement SQLite Database.** The path comes from the measurement persistence service (`IMeasurementRepository.DatabasePath`). Status: Available (opened read-only and its schema read), File not found (not created yet — the check never creates it), Cannot be opened / Cannot be read (permissions, not a SQLite file), Busy (locked by another program; checked again shortly).
- **Gamma Guard SQLite Database.** `gammaGuardDatabasePath` when set (environment variables expanded). Otherwise discovered on this laptop from the Gamma Guard install layout `…\GammaGuard\<version>\Database\*.sqlite` (e.g. `GammaGuard\CT008 1.9.0\Database\ct008-log.sqlite`) under Documents, Desktop, the user profile, LocalAppData, AppData, ProgramData and Program Files — depth-limited, skipping folders it may not read; the most recently written file wins; the result is reused until the file disappears or Retry searches again. No developer path is hard-coded. Statuses: Available, File not found (configured path missing), Not found (discovery found nothing), Cannot be opened, Busy. Checked read-only (`Mode=ReadOnly`, no pooling, 2 s busy timeout): the database is never modified.
- Every path is shown in a selectable, copyable read-only box. Rows are green (Connected/Available), red (Disconnected), amber (missing, not found, inaccessible, busy, unavailable) or grey (checking), with Retry when useful.

## Toasts, drillhole selection and capture duration

- **Toasts** (`Shared/Infrastructure/ToastService.cs`, shown by MainWindow top-right over both pages: 16 px from the top and right edges of the content area beside the sidebar, stacked downwards 8 px apart; part of the window, so they follow resizing, sidebar collapse and page switches). Success, information and warning toasts last 3 s; errors — validation and failed operations — 5 s; × closes one at once. They take no layout space and the empty area around them is not clickable, so they never move the page or block it. A toast identical to one already shown is not added again. White card with a coloured bar and glyph: green success, blue information, amber warning, red error.
  - **Results:** Measurement deleted (only after the database delete succeeded), CSV exported.
  - **Connection changes** (`ViewModels/StatusAnnouncements.cs`), once per transition, never per check: "Backend offline …" / "Backend online again." (the backend, as the sidebar dot) and "Measurement database …", "Gamma Guard database …" (the local Device Status rows). Start-up results are not announced, except an offline backend. **Bluetooth:** after a device is selected, its first confirmed state is announced once, then each change — "CT007-M-A1 (Bluetooth) connected." / "… disconnected." (3 s) and a failed check "… status unavailable — reason" (5 s error); an unchanged state says nothing.
  - **Validation errors (5 s):** Save & Continue without a drillhole (the Drillhole box also gets a red border); Start with a missing/invalid depth; leaving a depth box with invalid text (the box border is red while it is invalid); an invalid capture duration; changing the drillhole while measuring.
  - **Other errors (5 s):** capture failed, save/delete/export failed, session could not be opened or loaded.
  - Replaced inline labels: the Drillhole validation text, the depth error line and the Measurement page notification line. Kept: field border colours, Device Status colours, device names and paths, the drillhole warning under the Drillhole box (loading / saved list / not in the list) and the Measurement page warning "Complete Core Information …". Delete is still confirmed in its dialog first.
- **Drillhole box.** The × inside the box (left of the chevron, shown only when there is text or a selection) clears the search text, the selected drillhole, Project and Campaign and resets the list; Operator, Notes and all stored sessions/measurements stay. Typing filters as before; the closest match is outlined in blue; Enter picks it (unless the arrow keys chose another entry), a click picks any entry, Escape closes the list. Project and Campaign are filled only by a pick. The Measured / All groups, caching and virtualization are unchanged.
- **Empty selection.** With no drillhole selected the Measurement table and chart are empty, Start, Export and New Measurement are unavailable, and the "Complete Core Information …" warning shows; nothing is captured or saved without a session. While measuring, the Drillhole box is disabled (tooltip: stop first) and the ViewModel refuses changes with an error toast, so a running capture can never move to another drillhole.
- **Capture duration.** Click the countdown (or Tab to it and press Enter/Space) while no capture runs: a compact editor below it shows the current seconds; Enter or Save applies, Escape or Cancel closes. Whole seconds from 2 to 10 (`Calculation/Measurement/CaptureDurationSettings.cs` — the one place the limits are defined; a saved value outside them falls back to 5 s). Invalid input shows a 5 s error toast and keeps the editor open. Opening or saving never starts measuring or captures, and right-click captures are refused while the editor is open. The new duration applies from the next capture; a running capture keeps its own. It is stored in `capture-settings.json` beside `measurements.db` and restored after a restart (5 s when nothing is saved). Historical records are unchanged.

## Device Status and shared styling

- Device Status rows: CT007-M Device — Bluetooth, Measurement SQLite Database, Gamma Guard SQLite Database — all checked locally (see "Device Status (local checks)"). Paths are selectable read-only boxes that wrap long paths.
- Shared styles in `Shared/Styles/Theme.xaml`: palette brushes (including `LabelText`, `WarningSurface`), `FieldLabel` (every input label on both pages), `RequiredStar`, `CopyableValue`, `StatusMessage`, cards, section titles, inputs and the button states below. Section-only styles live in the section's view (see "Section configuration"). Core Information has two sections — Drillhole Information (row 1: Drillhole, Project, Campaign, Operator; row 2: Notes; Save & Continue at the end) and Device Status (three rows: CT-007 Bluetooth, Measurement SQLite Database, Gamma Guard SQLite Database).

- The Bluetooth Trigger indicator is no longer shown. The trigger itself (right-click in the main window while armed) is unchanged.
- All buttons share the base button template in `Shared/Styles/Theme.xaml`: hovering an enabled button draws a 2 px navy outer ring as an overlay, so size and content never move. Pressed (darker fill), keyboard focus (inner ring) and disabled (faded, no ring) remain distinct.
- **Look (after the Shopeers reference).** White sidebar with a hairline divider on a light grey page (`Canvas`); white section cards with hairline borders and 12 px corners; 40 px inputs and dropdowns with light borders, 8 px corners, a darker border on hover and blue on focus. The sidebar has the brand (icon + name) top-left. The blue SPP CT-007 icon is the collapse/expand control for the sidebar (not the window), and there is no separate toggle button. It stays in the 40 px icon column, so it is visible and clickable in both states. It has a grey hover tint, a blue keyboard-focus ring (Tab, then Enter/Space) and a "Collapse sidebar" / "Expand sidebar" tooltip and accessible name; navigation items are rounded, grey on hover, and the page currently shown has a soft blue fill, blue icon and semibold text and a blue accent bar at the sidebar edge. **Current page highlight:** the selection is derived from `MainViewModel.CurrentPage` (`IsCoreSelected` / `IsMeasurementSelected`, raised whenever the page changes, so exactly one item is selected — at start-up Core Information, and after any navigation including Save & Continue). It reaches the button template through a typed attached property (`Shared/Infrastructure/SidebarItem.IsSelected`) and a plain property trigger declared after the hover trigger, so it stays on regardless of the pointer or focus and stays distinct from hover; collapsed, the fill and accent bar stay around the icon. (Before, the template compared an untyped `Tag` through a relative-source binding inside the template, which did not reliably match, so no item looked selected.) The Online/Offline indicator stays at the bottom, left-aligned under the navigation icons with the dot and text 8 px apart; collapsed, the dot and tooltip remain. Only the two existing pages are in the navigation. All colours and control styles are in `Shared/Styles/Theme.xaml` (palette at the top).
- **Autofill and input fixes.** (1) Picking a drillhole no longer gets undone. Replacing the dropdown's items (re-filtering after a pick, a list refresh, a Measured-group refresh) made the WPF ComboBox drop its selection and write "" into its Text binding, which cleared Drillhole, Project and Campaign. The ViewModel now ignores that echo while it rebuilds the list and then re-syncs the selection and text. In XAML, `Text` and `SelectedItem` are bound before `ItemsSource`, so leaving the page cannot push "" into the form either. (2) Text boxes have no vertical padding. The single text line is centred in the 40 px box at its natural height, so Operator, Project, Campaign, Starting Depth and Step text cannot be clipped by the fixed height. Notes keeps its 9 px top padding. (3) Depths accept a decimal point or a comma on any Windows number format and are shown with a point. (4) Depth values typed while a drillhole's session is still loading are kept on screen and stored as its progress, not overwritten by the load.
- **Field heights.** Text boxes and dropdowns share `ControlHeight` (40 px) in `Theme.xaml`, with the same 11 px left padding, font, 1 px border and 8 px corners. Drillhole, Project, Campaign and Operator are therefore exactly the same height. Notes sets its own height, and the wrapping path boxes size to their text. Width priorities are unchanged. In the dropdown list, hover and arrow-key highlight are grey, and the selected drillhole has a soft blue fill with blue text.
- **Measurement controls.** The Start / Stop toggle (still one button, below Starting Depth and Step) is `ControlHeight` tall, the same as those two boxes (box only, labels not counted). Its text is 16 px and its glyph 13 px so they fit. The control row is: inputs | countdown | chart (the chart takes the remaining width). The capture countdown ring, ring line and number are enlarged by 25% through the multipliers in `CapturePanel/CapturePanel.yaml` (175 px ring, 10 px line, 40 px number). The ring column is no taller than the input column, so the chart and table keep their space. Timing, trigger and reset logic are unchanged.
- **Drillhole dropdown** uses a themed `ComboBox` template: same frame as the text boxes, chevron on the right (turns blue and flips while open), and a white rounded list with hover/arrow-key highlight. Search, filtering, keyboard selection and Project/Campaign filling are unchanged. Row 1 widths: Drillhole widest (min 200 px), Project (min 120), Campaign 110 px, Operator (min 150); the minimums fit the smallest window with the sidebar open.

## Section configuration

Each page section has one YAML file next to its view. It is copied next to the executable (`Pages/<page>/<section>/<section>.yaml` in the output and publish folders) and read **once at start-up**: edit the copy beside the `.exe` (or the source file, then rebuild) and **restart the app** to see a change. Each file starts with comments explaining its values.

| File | What you can change |
|---|---|
| `Pages/CoreInformation/DrillholeInformation/DrillholeInformation.yaml` | Labels and tooltips (Drillhole, Project, Campaign, Operator, Notes, Save & Continue, Retry), field widths and minimum widths, Notes height, spacing, and the drillhole warning shown on the Measurement page (text, font size, margin) |
| `Pages/CoreInformation/DeviceStatus/DeviceStatus.yaml` | Labels of the Bluetooth and database rows, the status dot, Bluetooth dropdown width, row padding and spacing |
| `Pages/Measurement/Table/MeasurementTable.yaml` | Per column (by stable id): header, visible, width, minWidth / maxWidth, format (`dateTime`, `metres`, `centimetres`, `localTime`, `decimal2`), alignment (`left`, `center`, `right`); table order; fonts, row and header heights, paddings, Delete column and button sizes as size × multiplier |
| `Pages/Measurement/CapturePanel/CapturePanel.yaml` | Labels of Starting Depth, Step, the countdown, the duration editor and Start / Stop (with glyphs); countdown, Start / Stop and label sizes as size × multiplier; spacing; chart X- and Y-axis labels, empty text, minimum height and font sizes |

Rules: every key is required (except a column's `format` and `maxWidth`); sizes are pixels greater than 0; widths may be `2.3*` or `Auto`; margins are `all`, `left-right,top-bottom` or `left,top,right,bottom`. A missing file, a misspelled, unknown, duplicate or empty key, a bad number or margin, an unknown or repeated column id, a missing column, or an unsupported format or alignment stops the app at start-up with a message naming the file and the line or key, for example `Pages/Measurement/Table/MeasurementTable.yaml, line 59 ("rowHight: 36"): Property 'rowHight' not found …`.

How it is wired: `Shared/Infrastructure/YamlSettings.cs` (the one YAML loader, YamlDotNet) reads each file into its typed class (`DrillholeInformationSettings`, `DeviceStatusSettings`, `MeasurementTableSettings`, `CapturePanelSettings`, next to the YAML file), which adds the final values (multipliers applied) to the application resources; `Pages/PageSettings.cs` does this for all four from `App.OnStartup`, before the main window opens. The views use them as `{StaticResource …}`. Colours, control styles and templates stay in XAML (`Shared/Styles/Theme.xaml` and the views); calculations, capture timing, validation, unit conversion and persistence stay in the ViewModels, `Calculation/` and `MeasurementRepository.cs`.

## Existing features preserved

- Core Information page: two sections, **Drillhole Information** and **Device Status**. Labels Drillhole, Project and Campaign are display text for `DrillholeId`, `DrillholeName` and `ProjectArea` (SQLite `DrillholeID`, `DrillholeName`, `Project`); Drillhole and Project are required. There is no Core ID field (it was never stored). Required form validation and saved core snapshots. Operator is pre-filled with the signed-in Windows account (`DOMAIN\Username` for a domain account, otherwise the user name — never the computer name alone); it stays editable, an emptied field is filled again on Save & Continue, and a drillhole's stored operator is never overwritten.
- Blue/white theme, collapsible sidebar, active navigation, icon tooltips and retained sidebar state.
- Delete column after the configured measurement columns; alternating backgrounds. Compact table sizes (the temporary 2x enlargement was reverted): 36 px rows, 14 px row text, 12 px headers (34 px minimum), 8 px cell padding, 28 px delete button in a 64 px column — all multipliers 1.0 in `Table/MeasurementTable.yaml`.
- Measurement page sizes are size × multiplier values in `CapturePanel/CapturePanel.yaml` and `Table/MeasurementTable.yaml`. Every multiplier is 1.0 by default (the countdown's 1.25), which is the current look.
- Stable capture identifiers and CSV save dialog.
- **Delete is confirmed** in a compact dialog centred over the window (`Windows/ConfirmationWindow`): "Delete this measurement?" (Delete / Cancel). Nothing changes on Cancel. A confirmed delete is stored right away; if storing fails the row is put back in its place and an error is shown, so the table always matches the database. Selecting, scrolling and viewing never ask.
- Completed results remain across navigation and, through SQLite, across restarts.

## Source organization

```text
Frontend/
├── Api/                      (see "Backend API")
├── Calculation/              local logic (see "Device Status (local checks)")
│   ├── Core-Information/     BluetoothDevice/, MeasurementDatabase/, GammaGuardDatabase/, monitor + settings
│   │   └── GammaGuardDatabase/  GammaGuardDeviceDatabase.cs (read-only samples/detector_status), GammaGuardCaptureDevices.cs
│   └── Measurement/          Depth.cs, MeasurementOrder.cs, CaptureDurationSettings.cs, DeviceSamples.cs (window, averaging,
│                             time basis), DeviceCaptureService.cs (capture from the device database)
├── Pages/
│   ├── PageSettings.cs                    loads the four section YAML files at start-up
│   ├── CoreInformation/
│   │   ├── CoreInformationPage.xaml(.cs)   composition: the two sections below
│   │   ├── CoreInformationViewModel.cs     form, drillhole list and Device Status commands (one ViewModel for the page)
│   │   ├── DrillholeInformation/
│   │   │   ├── DrillholeInformationView.xaml(.cs)
│   │   │   ├── DrillholeInformation.yaml + DrillholeInformationSettings.cs
│   │   │   ├── DrillholeCatalog.cs  (prepared list: lookups, A–Z order, background filtering)
│   │   │   └── DrillholeOption.cs   (dropdown entry: group heading or drillhole)
│   │   └── DeviceStatus/
│   │       ├── DeviceStatusView.xaml(.cs)
│   │       └── DeviceStatus.yaml + DeviceStatusSettings.cs
│   └── Measurement/
│       ├── MeasurementPage.xaml(.cs)      composition: capture card (panel + drillhole warning) and table
│       ├── MeasurementViewModel.cs        (and MeasurementState)
│       ├── MeasurementResult.cs
│       ├── MeasurementService.cs          (IMeasurementService, CaptureData, CaptureException)
│       ├── IDialogService.cs
│       ├── CsvExportService.cs
│       ├── MeasurementRepository.cs
│       ├── Table/
│       │   ├── MeasurementTableView.xaml(.cs)
│       │   ├── MeasurementTable.yaml + MeasurementTableSettings.cs
│       │   ├── MeasurementTableColumns.cs (fixed column ids → binding and CSV)
│       │   └── MeasurementCellFormat.cs
│       └── CapturePanel/
│           ├── CapturePanelView.xaml(.cs)
│           ├── CapturePanel.yaml + CapturePanelSettings.cs
│           ├── CountdownRing.cs
│           └── DepthBarChart.cs
├── Windows/ConfirmationWindow/
│   ├── ConfirmationWindow.xaml
│   ├── ConfirmationWindow.xaml.cs
│   └── DialogService.cs
├── Shared/
│   ├── Infrastructure/ (ObservableObject, commands, BulkObservableCollection, SidebarItem, ToastService, SameItemConverter)
│   ├── Models/ (CoreSnapshot)
│   └── Styles/ (Theme.xaml)
├── ViewModels/MainViewModel.cs, StatusAnnouncements.cs
├── MainWindow.xaml + MainWindow.xaml.cs
├── App.xaml + App.xaml.cs
├── AssemblyInfo.cs
└── Frontend.csproj
SmokeTests/
├── SmokeTests.csproj
└── Program.cs
```

## Real device capture

**Source (read-only).** The Gamma Guard database found by Device Status (or `gammaGuardDatabasePath`) is opened with `Mode=ReadOnly`, no pooling, a 2 s busy timeout and no PRAGMA or write of any kind (`SqliteFileCheck.OpenReadOnly`). Only `samples` and `detector_status` are read.

**Device.** At the click the device is the detector whose serial wrote the newest samples (the last 200 rows by id), independent of Bluetooth monitoring; its `detector_status` row (newest `updated_ms`) supplies name, MAC and firmware as metadata only (empty when there is none). Refused with an error toast, nothing saved: the database is unavailable, it holds no samples, or a second serial wrote samples within 5 s of the newest one (two detectors recording; samples are never mixed). The serial is frozen for the whole capture, so selecting another Bluetooth device can never change the data source mid-capture. Bluetooth connection requirements, if wanted later, belong in `GammaGuardCaptureDevices` as a separate rule — none is active now.

**Window.** Trigger time T = the click, to the millisecond (laptop clock). Accepted: `serial = <device serial> AND T < ts_ms <= T + duration`. Before the click the newest sample's ts_ms must be Unix milliseconds within 12 h of the laptop clock, otherwise the capture is refused (seconds or a wrong clock would mis-match the window). Reading starts at `MAX(id)` minus 200 rows and then continues by `id` (never "latest N rows"); rows are de-duplicated by source `id` and by (device session, seq, ts_ms). Rows with a null ts_ms are ignored. After the window closes a bounded final read waits for late writes up to 1.5 s (or until the expected count arrives); the window itself never moves. Extra clicks during a capture are ignored; Stop cancels without a row or a depth advance.

**Count.** Expected = duration in seconds (one sample per second). Fewer accepted samples → the row is kept, Sample shows "3 of 5" and a warning toast says the capture is incomplete; zero → error toast, no row.

**Averaging** (`SampleAveraging`). Each numeric field (count0, count0_raw, hdm, hdm_raw, dose_rate, temperature, pressure) is the mean of that field's non-null, finite values over the accepted samples; a field with no values stays NULL. Identifiers, seq and timestamps are not averaged.

**Source → destination.**

| Gamma Guard | MeasurementRecords (one per capture) | CaptureSamples (one per accepted sample) |
|---|---|---|
| `samples.id` | — | `source_id` |
| `samples.serial` | `device_serial` | `serial` |
| `samples.session_id` | `device_session_id` (only if all samples share it, else NULL) | `device_session_id` |
| `samples.seq` | — | `seq` |
| `samples.ts_ms` | `TsMs` = first accepted ts_ms | `ts_ms` |
| count0, count0_raw, hdm, hdm_raw, dose_rate | `Count0` … `DoseRate` = mean | same columns, original values |
| temperature, pressure | `temperature`, `pressure` = mean | `temperature`, `pressure` |
| `detector_status.name`, `mac` | `device_name`, `device_mac` | — |
| click / click + duration | `StartTime` / `EndTime`, `duration_ms` | — |
| accepted / expected count | `Sample` / `expected_samples` | — |

The app's `session_id` (MeasurementSessions) is never mixed with the device `session_id`. `MeasurementSessions` keeps its reuse rule (one session per drillhole). Temperature and pressure are stored but not shown in the table or CSV (CSV columns unchanged).

**Saving.** The record, its samples and the depth progress are written in one transaction. Samples use `INSERT … ON CONFLICT (record_id, source_id) DO NOTHING`, so a retried save adds no duplicates and advances depth once; a failed save keeps the row in the table and is retried by the next change, Stop or close. Deleting a row deletes its samples in the same transaction. `LoadSamplesAsync(session, captureNo)` reads them back after a restart.

**Migration 4 → 5** (one transaction, existing rows kept):
- `MeasurementProgress` rebuilt as `(session_id, next_from_cm REAL, step_cm REAL)` from `next_from / 10.0, step / 10.0` — converted exactly once; the app still works in millimetres internally, so Starting Depth/From/To/chart stay in metres and Step in cm, with no 100× error on load, save, Stop → Start or restart.
- `MeasurementRecords` gains `duration_ms, expected_samples, temperature, pressure, device_serial, device_name, device_mac, device_session_id` (NULL for old rows). Record depths stay in millimetres.
- New `CaptureSamples(id, record_id → MeasurementRecords.id, source_id, serial, device_session_id, seq, ts_ms, count0, count0_raw, hdm, hdm_raw, dose_rate, temperature, pressure)` with unique index `(record_id, source_id)`.

## Validation

Bluetooth detection / table formatting revision: all smoke checks passed (including the app ViewModel checks) in the UTC, America/New_York and UTC+8 time zones; in UTC+8, 1792115769651 shows as 2026-10-16 09:56:09. Checked: decimal rounding and empty nulls, local timestamps and out-of-range values, alignment by column meaning, unchanged header labels, full precision in the model, SQLite and CSV, and the Bluetooth check with a stand-in for the Windows reader (CT007-M names only, read by device ID, live connected vs paired, timeouts as Status unavailable). The Windows Runtime reader was type-checked against stand-in declarations of the Windows API it calls; it needs the Windows SDK projection (on Windows) to build and run for real.

Bluetooth selection revision: non-WPF code compiled with warnings as errors and the smoke tests passed three times — this time including the `MainViewModel` app checks (compiled here with a stand-in for the WPF confirmation dialog). Covered: the dropdown lists only CT007-M devices (by prefix, once per address), nothing is auto-selected, no connection check runs before a selection, only the selected device is checked (by address, ~1 per second, one loop even when Start is called twice or pages change, none after Dispose), paired vs connected, failures shown as Status unavailable, a late result for the previous device discarded, the selection kept across list refreshes and when the device disappears, one toast per transition (3 s) and per failure (5 s), and measuring — Start, trigger, countdown, completion, depth advance and export — working while Bluetooth is unavailable or disconnected; the capture device resolves from the samples without any Bluetooth selection. Not run here: the WPF UI and the real Windows Bluetooth API.

Real device capture revision: same environment (WPF/XAML not compiled; XAML parses and every resource resolves). Non-WPF code compiled with warnings as errors; smoke tests passed three times against real SQLite files built with the Gamma Guard `samples`/`detector_status` schema from the task (no real device database was available). Covered: pre-click and at-click samples excluded, other serials excluded, 2/5/10 s boundaries, late/duplicate/null-ts writes, bounded final read, seconds-based ts_ms refused, averages over actual counts with nulls, incomplete (3 of 5) and empty captures, Stop cancel, retry without duplicates, records and samples linked after restart, delete cascade, device-resolver refusals, the v4 → v5 progress migration (151.255 m / 25.5 cm preserved, not converted twice), and the source file byte-identical after captures.

Device/table/persistence revision: same method — ViewModels, repository (including the v1 → v2 upgrade), column configuration and layout, and CSV export were compiled and the smoke tests passed on Linux with real SQLite; XAML, code-behind and styles were checked by review and XML parsing only. Build and run on Windows to confirm.

Columns/SQLite revision: the ViewModel, repository, column configuration, CSV export and models were compiled with the .NET 10 SDK (warnings as errors) and the full smoke-test sequence passed against a real SQLite database on Linux. That environment had no NuGet access or Windows desktop pack, so SQLite was reached through a test-only stand-in for the `Microsoft.Data.Sqlite` calls, and the WPF project itself (XAML, `MeasurementPage.xaml.cs`, `ScaleExtension.cs`) was not compiled. XAML files parse as XML. Run the build and smoke tests on Windows to confirm.

Units / sorting / dropdown / local Device Status / sidebar revision: same environment (no NuGet, no Windows desktop pack, so the WPF project and XAML were not compiled; XAML parses and every StaticResource resolves). All non-WPF code — including `Calculation/**` and the Windows Bluetooth P/Invoke declarations — compiled with warnings as errors, and the smoke tests passed (three runs) with real SQLite: 25 cm advances 0.25 m (10 m → 10.25 m), stored steps show in cm, rows and CSV sort by numeric From with the stated tie-breaks, progression follows the capture and not the last row, manual reordering is gone while No and delete confirmation remain, an old manual SortOrder is ignored, 20,000 drillholes stay responsive (one list update per typing pause, no stale results, selection kept), local Bluetooth/database checks (missing never created, not-SQLite, busy lock, discovery, read-only) work without the backend, and the backend probe never calls the removed status endpoints. The `MainViewModel` checks (sidebar selection at start / after navigation / after Save & Continue, local polling not multiplied by navigation, Device Status while Offline) and the real Windows Bluetooth call run only on Windows.

Toasts / drillhole selection / capture duration revision: same environment and method — all non-WPF code compiled with warnings as errors and the smoke tests passed (three runs). New checks: toast durations (3 s / 5 s), de-duplication, dismissal and expiry; backend and Bluetooth transitions announced once, not per check, and not at start-up; closest match ranking (exact, prefix, partial) without picking or rewriting the text; Enter picks; × clears selection, Project and Campaign but keeps Operator, Notes and stored rows; empty selection empties the Measurement view and disables Start/export; reselecting restores only that drillhole's rows, progress and session; a late load for an earlier selection is discarded; the drillhole is locked while measuring; duration validation, persistence, restart, the capture window (12 s → 12 samples, ring 75 % at 3 s left), no capture while the editor is open, and Stop resetting to the configured duration. The WPF XAML (toast overlay, × button, closest-match outline, duration popup, 2x table) parses and every resource resolves, but was not compiled or run here.

Compact table / toast position revision: table multipliers back to 1.0, star column widths (minimums checked by a smoke test to fit the narrowest layout), toasts moved top-right. XAML parses and every resource resolves; smoke tests pass. Not compiled or run on Windows here.

Run the updated smoke tests on Windows:

```powershell
dotnet run --project SmokeTests/SmokeTests.csproj
```

They cover arming without capture; Idle/duplicate triggers; elapsed ring progress; repeat captures; exact millisecond start/end values and separate completion; cancellation in Capturing and Processing; rapid rearm; late cancelled results; service failure; stable identifiers; reorder/delete/export; navigation/sidebar state; five-second duration; closure and cancellation; session IDs per Start and across navigation; CSV column order, precision and session column; stored No after reorder; no duplicate saves; save-failure reporting; SQLite session isolation, precision, nulls, delete and restart restore. They do not substitute for real mouse/UI tests.

Manual Windows checks:

1. Start and wait: Stop appears, timer stays at the configured duration, no row appears.
2. Right-click anywhere in the main window: one capture of the configured duration, full ring/zero, one averaged row, reset to Ready. Repeat and rapidly right-click during capture.
3. Stop mid-capture and during zero/Processing: no cancelled row. Restart and repeat. Previously completed rows stay unchanged.
4. While armed, verify main-window right-clicks on the panel, table, sidebar and Core Information each trigger only one capture when Ready. While Idle, verify none trigger captures. Verify another application or an open modal dialog never triggers the main window.
5. Switch pages/collapse the sidebar during capture, return, and confirm state/result continuity. Edit the core and verify mode disarms until saved again.
6. Verify the ring at 0%, 50%, 100%, resizing, and 125%/150% display scaling. Check the compact table remains scrollable at minimum window size.
7. Delete/export results (rows stay sorted by From). Compare table seconds-only display with precise CSV start/end/completion timestamps and offsets.
8. Close during capture; verify no uncaught cancellation errors.
9. Device Status with the backend stopped: "No device selected" until a CT007-M is picked in the dropdown (name + address; nothing is auto-selected). Pick the paired CT-007 (Disconnected, "paired … not connected", one toast), open its COM port in the device software (Connected, one toast), close it (Disconnected, one toast), turn Bluetooth off (Status unavailable, 5 s error toast). Measure throughout: Start, right-click captures, saving and export keep working in every Bluetooth state. Both database rows show local paths; rename the Gamma Guard file (Not found / File not found), Retry after restoring it.
10. Sidebar: Core Information highlighted at start; click Measurement, Save & Continue, collapse/expand, hover and Tab — exactly one item stays highlighted.
11. Drillhole dropdown with a large list (thousands of holes): typing stays fluid, the list scrolls, picking fills Project and Campaign.
12. Toasts: delete a row (green, 3 s), stop the backend (amber "Backend offline", once), restart it (once), disconnect the CT-007 (once), Save & Continue without a drillhole (red, 5 s, red border). Toasts never move the page and × closes them.
13. Drillhole box: type part of an ID (closest outlined, text untouched), Enter picks it; × clears it (Measurement page empty, Start disabled); pick it again (its rows and depths return). Try to change it while measuring (refused).
14. Click the countdown while stopped and while armed (no capture starts), enter 0 / abc (error toast), then 12 (countdown 00:12, next capture lasts 12 s); restart the app (still 12 s).

## Window-wide trigger revision

The latest revision only moves right-click routing from the countdown card to a single MainWindow input handler and removes the obsolete page handler/tooltip. Measurement state, ring, averaging, timestamps, table columns/layout, CSV export and all other commands remain unchanged. Source/reference checks passed. Build was attempted but could not start because dotnet is unavailable; Windows runtime/input validation remains unexecuted.

## Window input reliability

MainWindow registers one PreviewMouseDown handler with handledEventsToo enabled and removes it on closure. The handler does not discard an event merely because a child control marked it handled. Window-level tunneling covers the main window client area, including blank areas and existing controls, without page-specific or global subscriptions. Armed context-menu openings are suppressed; normal right-click behavior resumes after Stop. An explicit minimized-state guard supplements the active/enabled-window checks. The ViewModel still rejects busy triggers and owns the capture lifecycle.

Only MainWindow.xaml.cs and this README changed in this revision. Source checks verified those changes and unchanged measurement code. Windows build and runtime tests remain unexecuted because dotnet is not installed. On Windows, additionally check right-clicks over an input, table row, button and blank space while armed, and confirm minimized/inactive/Idle input never captures.
