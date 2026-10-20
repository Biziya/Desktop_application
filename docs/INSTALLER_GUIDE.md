# Windows installer guide — SPP CT-007 Measurement Studio

How to build the Windows installer for the WPF frontend (`Frontend/`) with [Velopack](https://velopack.io), and how to
check an installed copy. The installer contains the **frontend only**; the ASP.NET Core backend (`Backend/`) is
deployed separately and is not changed by anything in this guide.

## Configuration overview

| What | Where | Used by |
|---|---|---|
| Application id, name, version, description | `Frontend/Config/application.yaml` (single source of truth) | `Frontend.csproj` (compiled version), `build-installer.ps1` (Velopack id, title, version), the app (window title) |
| Frontend → backend API URL, timeout, retry interval | `Frontend/Api/api-settings.json` (shipped defaults), per-user `.env`, `API_*` environment variables | Frontend `ApiSettings` |
| Backend → SQL Server connection string | `Backend/appsettings.json`, `ConnectionStrings:DefaultConnection` | Backend `DrillholeRepository` — **out of scope, not changed** |

### Application metadata (`application.yaml`)

```yaml
application:
  id: "CT007.MeasurementStudio"        # Velopack package id - never change it after release, updates follow it
  name: "SPP CT-007 Measurement Studio" # window title, installer title, assembly product name
  version: "1.0.0"                      # SemVer; the only place the version is written
  description: "CT-007 Measurement Desktop Application"
```

- **To release a new version, change `version` here only.** `Frontend.csproj` reads the file at build time, so
  `dotnet build`, Visual Studio and the installer script all compile the same version; the script also passes it to
  `vpk`. There is no other hard-coded version.
- The file is copied next to `Frontend.exe` and read at start-up. A missing file, missing key, invalid id or a
  version that is not SemVer stops the app with a message naming the file and key.
- The window title shows `name` and `version`, e.g. `SPP CT-007 Measurement Studio 1.0.0`.

### Frontend API connection (`.env`)

Lookup order — a later source overrides an earlier one, key by key:

1. `Api\api-settings.json` next to `Frontend.exe` — the shipped defaults (`http://localhost:5002`, 5 s, 5 s). This
   file keeps its existing behaviour: missing or invalid values fall back to those defaults.
2. `%LocalAppData%\CT007_Measurement_Studio\.env` — per-user settings. This folder (also holding
   `measurements.db`) is **outside** the Velopack install folder (`%LocalAppData%\CT007.MeasurementStudio\`), so
   updates do not overwrite it. Start from `Frontend/.env.example`.
3. Environment variables `API_BASE_URL`, `API_TIMEOUT_SECONDS`, `API_RETRY_INTERVAL_SECONDS`.

| Key | Meaning | Rule |
|---|---|---|
| `API_BASE_URL` | Backend base URL | Absolute `http://` or `https://` URL, no user name/password |
| `API_TIMEOUT_SECONDS` | Seconds before a request is abandoned | Number > 0 |
| `API_RETRY_INTERVAL_SECONDS` | Seconds between backend reachability checks (sidebar Online/Offline) | Number > 0 |

A value in the `.env` file or an environment variable that breaks these rules stops the app with a
"configuration error" message naming the source and key; the app never switches to another backend on its own.
The URL is not repeated in the message or anywhere in logs. Use `https://` for a deployed backend. The API base URL
is **not** a database connection string: the frontend never connects to SQL Server.

Real `.env` files are ignored by Git and never copied to the build output, so they cannot reach an installer
(the script also refuses to package one).

### Backend database connection (not changed)

The backend reads the SQL Server connection string `ConnectionStrings:DefaultConnection` through the standard
ASP.NET Core configuration (`Backend/appsettings.json`, overridable by the usual ASP.NET Core sources such as the
`ConnectionStrings__DefaultConnection` environment variable) in
`Backend/Repositories/CoreInformation/Drillhole/DrillholeRepository.cs`. This guide, the installer and the script do
not read, move or change it. Configure it on the backend server as before.

> **Security note:** `Backend/appsettings.json` currently contains a SQL Server user name and password and is
> committed to Git. It was left unchanged on purpose (out of scope); consider moving the credentials to a secret
> store or server-side environment variable and rotating the password.

## Prerequisites

- Windows 10 version 2004 (build 19041) or later, x64 — the app uses the Windows Runtime Bluetooth API.
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (`dotnet --version` → `10.0.x`).
- PowerShell — Windows PowerShell 5.1 (built in) or PowerShell 7.
- Velopack NuGet package — already referenced by `Frontend/Frontend.csproj` (`Velopack` **1.2.161**); restored
  automatically. `App.Main` calls `VelopackApp.Build().Run()` before WPF starts, which Velopack requires.
- Velopack CLI `vpk`, **same version as the NuGet package**:

  ```powershell
  dotnet tool install -g vpk --version 1.2.161
  # or, to align an existing install:
  dotnet tool update -g vpk --version 1.2.161
  ```

The target computer needs nothing pre-installed: the app is published self-contained (the .NET runtime is included).

## Quick start

From the repository root:

```powershell
.\build-installer.ps1
```

If scripts are blocked: `powershell -ExecutionPolicy Bypass -File .\build-installer.ps1`.

The script:

1. Reads and validates `id`, `name` and `version` from `Frontend/Config/application.yaml`.
2. Checks that `vpk` is installed and empties `publish\`.
3. `dotnet restore`, `dotnet build` and `dotnet publish` for `win-x64` (self-contained), passing the YAML version as
   `-p:Version`.
4. Verifies that `publish\Frontend.exe` carries the YAML version and that `Config\application.yaml` is published,
   and refuses to continue if any `.env` file is in `publish\`.
5. Runs `vpk pack` with the YAML metadata, output in `Releases\`.
6. Prints the installer path and version.

Every step stops the script on failure. Optional parameters: `-Configuration` (default `Release`), `-Runtime`
(default `win-x64`), `-OutputDir` (default `Releases`).

The manual steps below are what the script does.

## Build application

```powershell
dotnet restore Frontend/Frontend.csproj
dotnet build Frontend/Frontend.csproj -c Release
```

The compiled version comes from `application.yaml` (check: `(Get-Item Frontend\bin\Release\net10.0-windows10.0.19041.0\Frontend.exe).VersionInfo.ProductVersion`).

## Publish application

```powershell
dotnet publish Frontend/Frontend.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o ./publish
```

`publish\` then contains `Frontend.exe`, the runtime, and the configuration files next to the executable:
`Config\application.yaml`, `Api\api-settings.json`, `Calculation\Core-Information\device-settings.json` and
`Pages\**\*.yaml`.

## Generate installer

Read the values from `application.yaml` (the script does this for you) and pass them to `vpk pack`:

```powershell
vpk pack `
    --packId CT007.MeasurementStudio `
    --packVersion 1.0.0 `
    --packTitle "SPP CT-007 Measurement Studio" `
    --packDir .\publish `
    --mainExe Frontend.exe `
    --icon .\Frontend\Icons\icon.ico `
    --runtime win-x64 `
    --outputDir .\Releases
```

| Option | Value | Source |
|---|---|---|
| `--packId` | `CT007.MeasurementStudio` | `application.id` |
| `--packTitle` | `SPP CT-007 Measurement Studio` | `application.name` |
| `--packVersion` | `1.0.0` | `application.version` |
| `--mainExe` | `Frontend.exe` | project output name |
| `--icon` | `Frontend\Icons\icon.ico` | same icon as the exe |
| `--packDir` | `publish\` | `dotnet publish -o` |
| `--outputDir` | `Releases\` | Velopack default |

Velopack verifies that `Main` calls `VelopackApp.Run()`, builds the full package, a portable zip and the setup
executable. Files are not code-signed unless you add `--signParams` / `--signTemplate` (Windows SmartScreen may
warn about unsigned installers).

**Velopack refuses to pack a version that is not greater than the newest one already in `Releases\`**
("There is a release in channel win which is equal or greater to the current version"). Increase `version` in
`application.yaml`. Keep earlier releases in `Releases\`: Velopack uses them to build delta update packages.

## Installer output

Output of a `1.0.0` build (as produced by `build-installer.ps1`; `assets.win.json` lists the files of the last pack):

| File | Type | Purpose |
|---|---|---|
| `CT007.MeasurementStudio-win-Setup.exe` | Installer | **Give this file to end users.** Installs per user (no admin rights), creates Desktop and Start-menu shortcuts and starts the app. |
| `CT007.MeasurementStudio-win-Portable.zip` | Portable | Runs without installing (extract and start `Frontend.exe`). |
| `CT007.MeasurementStudio-1.0.0-full.nupkg` | Full package | Update package, downloaded by installed copies when updating. |
| `CT007.MeasurementStudio-<version>-delta.nupkg` | Delta package | Only from the second release on: smaller update from the previous version. |
| `releases.win.json`, `RELEASES` | Update feed | Index of releases for the Velopack updater (upload together with the `.nupkg` files if you host updates). |
| `assets.win.json` | Build manifest | Files created by the last `vpk pack`. |

`Releases\` and `publish\` are build output and are ignored by Git.

## Installation verification

On a second Windows 10 (2004+) / 11 x64 computer:

1. **Install.** Copy `CT007.MeasurementStudio-win-Setup.exe` and run it. It installs to
   `%LocalAppData%\CT007.MeasurementStudio\` and starts the app.
2. **Start.** Close the app and start it again from the Desktop or Start-menu shortcut
   *SPP CT-007 Measurement Studio*.
3. **Name and version.** The window title reads `SPP CT-007 Measurement Studio 1.0.0` (the YAML values). Also
   *Settings → Apps → Installed apps* shows the same name and version.
4. **Configure the API URL.** Create `%LocalAppData%\CT007_Measurement_Studio\.env` from `Frontend/.env.example`
   and set `API_BASE_URL` to the backend (for example `https://ct007-api.example.com`). Restart the app.
   A deliberate mistake (e.g. `API_TIMEOUT_SECONDS=abc`) must show a *configuration error* message and close the
   app; restore the value afterwards.
5. **API connectivity.** The sidebar indicator turns *Online* within the retry interval; its tooltip shows the
   configured backend URL. On *Core Information*, the drillhole list loads. If it shows *Offline*, check the URL,
   firewall and that the backend is running (`<API_BASE_URL>/swagger` opens in a browser).
6. **Backend database connectivity (connection string unchanged).** The drillhole list is read by the backend from
   SQL Server, so a loaded list with current drillholes confirms the backend's existing connection. Do not edit the
   backend connection string for this test; if the list is empty or the backend returns an error, check the backend
   server's log and configuration with the backend administrator.
7. **SQLite and Bluetooth.** *Core Information → Device Status* shows the measurement database
   (`%LocalAppData%\CT007_Measurement_Studio\measurements.db`, created on first start), the Gamma Guard database
   and the paired CT-007 Bluetooth device. Take a test measurement and confirm it appears in the table after a
   restart.
8. **Uninstall.** *Settings → Apps → Installed apps → SPP CT-007 Measurement Studio → Uninstall*. This removes
   `%LocalAppData%\CT007.MeasurementStudio\`. User data in `%LocalAppData%\CT007_Measurement_Studio\`
   (`measurements.db`, `.env`) is kept on purpose; delete that folder manually for a clean machine.

## Troubleshooting

| Symptom | Cause / fix |
|---|---|
| `Velopack CLI 'vpk' not found` | Install it: `dotnet tool install -g vpk --version 1.2.161`. |
| `equal or greater to the current version` | Increase `version` in `application.yaml`. |
| `application.version '…' is not a SemVer version` | Use `MAJOR.MINOR.PATCH`, optionally `-prerelease` (e.g. `1.1.0-beta.1`). |
| App shows *configuration error* at start | The message names the file/variable and key: fix `application.yaml`, the `.env` file or the `API_*` variable. |
| Sidebar stays *Offline* | Wrong `API_BASE_URL`, backend not running, or firewall; see step 5. |
