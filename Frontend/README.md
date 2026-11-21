# Frontend

The WPF desktop app, shown to users as **Gamma Core Log** (formerly SPP CT007). Internal identifiers keep the old
name on purpose: the exe (`Frontend.exe`), the Velopack package id and the data folder, so installed copies keep
updating and keep their data. It targets `net10.0-windows10.0.19041.0` and uses
Microsoft.Data.Sqlite, YamlDotNet and Velopack.

## Run

The Debug build reads the backend address from `Frontend\.env.local`, which is not committed. Create it once from the
example, then run the app:

```powershell
Copy-Item Frontend\.env.example Frontend\.env.local
dotnet run --project Frontend
```

Start the backend too (`cd Backend; dotnet run`) if you need the drillhole list, uploads or the Data Viewer. The rest
of the app works without it.

## Pages

- Core Information: pick a drillhole (searchable list from the backend, cached locally) or create a new one with
  **New drillhole** (Drillhole ID, Campaign and Project are all required; it is stored locally and reaches the central
  database with its uploaded measurements). Check operator and notes, and see the Device Status of this laptop: the
  CT007 Bluetooth device (any variant, e.g. CT007-M or CT007-T), the measurement database and the Gamma Guard
  database. Save & Continue opens the drillhole's measurement session.
- Measurement: set Starting Depth, Step and an optional CPS alarm threshold, press Start, then right-click anywhere
  on the Measurement page to capture. Right-clicks and device data are ignored on every other page, and leaving the
  page cancels a running capture without saving it. Each capture (2 to 10 s, default 5 s; click the countdown to
  change it) averages the Gamma Guard samples written during that time into one row. The Live box above the graph
  shows the newest reading every second while the page is shown, in CPS or CPM (CPS × 60; the unit choice lasts for
  the session). The graph always draws the alarm threshold as a dashed line, before, during and after measuring;
  the eye icon beside the Live box shows or hides it. Rows can be deleted and exported to CSV.
- Monitoring: live status of every detector writing to the Gamma Guard (CT008) database, with a 5-second sample
  button. Ported from EIC's CT008 Monitor in `SHIBA/`.
- Upload Data: import an exported CSV, review it, and upload it to the central database.
- Data Viewer: read-only view of what has been uploaded for a drillhole.

Help (the ? button top right, or F1) opens one Help window with a section per page, scrolled to the page you are on.
F1 toggles it, Esc closes it, and both work wherever the keyboard focus is. Pressing ? while Help is open brings it
to the front. The text lives in `Config/help.yaml`, its pictures in `Help/Images/`.

## Configuration files

All of these are copied next to `Frontend.exe` and read once at start-up. Edit the copy beside the exe and restart
the app, or edit the source file and rebuild. A missing or invalid value stops the app with a message naming the file
and key.

| File | What it sets |
|---|---|
| `Config/application.yaml` | Package id, name, version, description. The only place the version is written. |
| `Config/data-retention.yaml` | How long local measurements are kept |
| `Config/help.yaml` | Help panel text, one section per page |
| `Calculation/Core-Information/device-settings.json` | Device Status rules: check intervals, CT007 name prefix, optional fixed Gamma Guard path |
| `Pages/CoreInformation/DrillholeInformation/DrillholeInformation.yaml` | Labels, widths and spacing of the drillhole form |
| `Pages/CoreInformation/DeviceStatus/DeviceStatus.yaml` | Labels and layout of the Device Status rows |
| `Pages/Measurement/CapturePanel/CapturePanel.yaml` | Capture panel labels and sizes, chart axes, alarm threshold and tone |
| `Pages/Measurement/Table/MeasurementTable.yaml` | Table columns (header, visibility, width, format, alignment), also used by Upload Data and Data Viewer |
| `Pages/Monitoring/Monitoring.yaml` | Poll interval, stale timeout, how many readings and samples to keep on screen |

To release a new version, change `version` in `application.yaml`. `Frontend.csproj`, the window title and the
installer all read it from there. Never change `id` after a release, or installed copies stop receiving updates.

## Backend address (.env)

The app reads one `.env` file, chosen by the build:

| Build | File |
|---|---|
| Debug (`dotnet run`, Visual Studio) | `Frontend\.env.local` |
| Installed (Release) | `%LocalAppData%\CT007_Measurement_Studio\Config\.env.production` |

Only four keys are allowed:

```dotenv
APP_ENV=production                         # optional; local or production, must match the file
API_BASE_URL=https://ct007-api.example.com # required; absolute http(s) URL
API_TIMEOUT_SECONDS=30                     # optional, default 5
API_RETRY_INTERVAL_SECONDS=10              # optional, default 5
```

`API_BASE_URL` is the backend's HTTP address. The app never connects to SQL Server itself, so no connection strings or
passwords belong here. There is no fallback: a missing or invalid value shows a configuration error and the app closes.

## Local data

Everything is stored per Windows user in `%LocalAppData%\CT007_Measurement_Studio\`:

- `measurements.db`: sessions (one per drillhole), captures, raw samples and depth progress. It uses WAL mode, so
  DBeaver or `tools\check-db.ps1` can read it while the app runs. Copy the `-wal` and `-shm` files with it.
- `capture-settings.json`: the capture duration.
- `Config\gamma-guard-database.json`: the Gamma Guard database picked with Select/Change Database.
- `Config\.env.production`: the backend address (installed app only).

The Gamma Guard database is only ever opened read-only.

Retention is set in `Config/data-retention.yaml` (default: enabled, 30 days, checked every 24 hours and at start-up).
A drillhole session with no capture in that time is deleted as a whole, with its captures and samples. The session
open on the Measurement page is never deleted.

## Building the installer

The installer contains the frontend only. The backend is deployed separately.

1. Install the Velopack CLI (same version as the NuGet package):
   ```powershell
   dotnet tool install -g vpk --version 1.2.161
   ```
2. Create `Frontend\.env.production` from `.env.example` with `APP_ENV=production` and the real backend address.
   It is not committed.
3. Raise `version` in `Config/application.yaml`. Velopack refuses a version that is not higher than the newest one
   already in `release\installer`.
4. Run the script from `Frontend\`:
   ```powershell
   .\build-installer.ps1
   # if scripts are blocked:
   powershell -ExecutionPolicy Bypass -File .\build-installer.ps1
   ```

The script validates `application.yaml` and `.env.production` (any key other than the four above fails the build),
publishes a self-contained win-x64 build to `release\publish`, checks the exe version and the packaged config, and
runs `vpk pack` into `release\installer`. That folder then holds:

- `GammaCoreLog-Setup.exe`: give this to users. It installs per user, no admin rights needed. It is a copy of
  `CT007.MeasurementStudio-win-Setup.exe` (Velopack names that one after the package id).
- `CT007.MeasurementStudio-win-Portable.zip`: runs without installing.
- `CT007.MeasurementStudio-<version>-full.nupkg` (and `-delta.nupkg` from the second release on), plus
  `releases.win.json` and `RELEASES` for the update feed.

Keep older packages in `release\installer`. Velopack builds delta updates from them.

## On the installed PC

The app installs to `%LocalAppData%\CT007.MeasurementStudio\`. On first start it copies the packaged template to
`%LocalAppData%\CT007_Measurement_Studio\Config\.env.production`. That file is never overwritten, and it sits outside
the install folder, so updates keep it. To point the app at another backend, edit it and restart:

```powershell
notepad "$env:LOCALAPPDATA\CT007_Measurement_Studio\Config\.env.production"
```

Changing `Frontend\.env.production` and rebuilding only affects new installs, or PCs where the file was deleted.
Uninstalling removes the install folder but leaves `CT007_Measurement_Studio\` (measurements and settings) in place.

## Troubleshooting

| Problem | What to check |
|---|---|
| `Velopack CLI 'vpk' not found` | Install it, see above. |
| `equal or greater to the current version` | Raise `version` in `application.yaml`. |
| Script rejects `.env.production` | Only the four API keys, `APP_ENV=production`, `API_BASE_URL` with `http://` or `https://`. |
| Configuration error at start-up | The message names the file and key. "No configuration template" means the installer was built without `.env.production`. |
| Debug build says `.env.local` was not found | Create it from `.env.example`. |
| Sidebar stays Offline | Backend not running, wrong `API_BASE_URL`, or a firewall. Open `<API_BASE_URL>/swagger` in a browser on that PC. |
| Online, but the drillhole list shows a backend error | The backend can't reach SQL Server. See `Backend/README.md`. |
