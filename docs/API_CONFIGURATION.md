# API connection configuration — SPP CT-007 Measurement Studio

The WPF frontend (`Frontend/`) talks to the ASP.NET Core backend over HTTP. Where it connects to, how long it waits
and how often it re-checks the connection come from a `.env` file chosen by the **environment** (`APP_ENV`):
`local` for development, `production` for employee computers. The same build and the same code serve both; no
address is compiled into the application. Changing the backend address only means editing a text file and
restarting the app — no rebuild, no new installer.

The loader is `Frontend/Api/ApiSettings.cs`. It is read once at start-up and passed to the single `ApiClient`
used by every API call (`DrillholesClient`, the Online/Offline indicator).

> `API_BASE_URL` is an **HTTP address**, not a database connection string. The frontend never connects to SQL
> Server; the backend's SQL Server connection string (`Backend/appsettings.json`) is separate and not covered here.
> The local SQLite measurement database stays at `%LOCALAPPDATA%\CT007_Measurement_Studio\measurements.db`.

## How the configuration is resolved

1. **Environment (`APP_ENV`)**
   1. The `APP_ENV` process environment variable, when set.
   2. Otherwise the environment compiled into the build (`AppEnvironment` in `Frontend.csproj`):
      **Debug builds → `local`**, **Release builds (including the installer) → `production`**.
      Override at build time with `-p:AppEnvironment=local|production` (or `build-installer.ps1 -AppEnvironment`).

   Only `local` and `production` are accepted (lower case); anything else is a configuration error. `APP_ENV` is
   never taken from a `.env` file, because the file is chosen by it. An `APP_ENV=` line inside the file may only
   confirm the choice: if it names the other environment, the app stops with an error.

2. **Settings file `.env.<APP_ENV>`** — the **first existing** file of:
   1. `%LOCALAPPDATA%\CT007_Measurement_Studio\Config\.env.<APP_ENV>` (per user; used by installed copies)
   2. `Frontend\.env.<APP_ENV>` in the source checkout — only when the app runs from source (the folder holding
      `Frontend.csproj` above the executable). An installed copy has no such folder and skips this step.

   Only that one file is read; the two are not merged.

3. **Process environment variables** `API_BASE_URL`, `API_TIMEOUT_SECONDS`, `API_RETRY_INTERVAL_SECONDS` override
   the file, key by key.

4. **Defaults** for the optional values only: `API_TIMEOUT_SECONDS=5`, `API_RETRY_INTERVAL_SECONDS=5`.
   `API_BASE_URL` has no default.

There is no fallback between environments: a production build never connects to localhost because a production
file is missing, and a local run never connects to production. Any problem — no file, no `API_BASE_URL`, an invalid
value, an unreadable file, a malformed line — shows a **configuration error** dialog naming the file and key, and
the app closes. The URL itself is never repeated in messages (it could contain credentials).

## Configuration reference

| Variable | Required | Purpose | Rule |
|---|---|---|---|
| `APP_ENV` | — | Selects `local` or `production`. Set it as an **environment variable** to override the build's choice; inside a `.env` file it is only a consistency check. | `local` or `production` |
| `API_BASE_URL` | yes | Backend API base address. Endpoint paths such as `/api/core-information/drillholes` are appended to it. | Absolute `http://` or `https://` URL, no user name/password |
| `API_TIMEOUT_SECONDS` | no (5) | Seconds before a backend request is abandoned (the app then shows *Backend did not respond in time* / *Offline*). | Number > 0 |
| `API_RETRY_INTERVAL_SECONDS` | no (5) | Seconds between backend reachability checks (the sidebar *Online/Offline* indicator, a `HEAD` request to the base URL). When the backend comes back, failed lists reload. | Number > 0 |

File format: one `KEY=value` per line; blank lines and lines starting with `#` are ignored; surrounding quotes are
removed; unknown keys are ignored. `Frontend/.env.example` is the template.

**Never put passwords, tokens or other secrets in these files.** The backend currently needs none from the
frontend.

## Local development

1. **Create `.env.local`** next to `Frontend.csproj` (it is ignored by Git):

   ```powershell
   Copy-Item Frontend\.env.example Frontend\.env.local
   ```

   ```dotenv
   APP_ENV=local

   API_BASE_URL=http://localhost:5002
   API_TIMEOUT_SECONDS=5
   API_RETRY_INTERVAL_SECONDS=5
   ```

   `http://localhost:5002` is the backend's `http` profile in `Backend/Properties/launchSettings.json`. To use the
   `https` profile instead, set `API_BASE_URL=https://localhost:7244` (requires a trusted dev certificate:
   `dotnet dev-certs https --trust`).

   A file in `%LOCALAPPDATA%\CT007_Measurement_Studio\Config\.env.local` would take precedence over this one; normally
   leave that folder without a `.env.local` on a development machine.

2. **Select the local environment.** Nothing to do for a Debug build (Visual Studio *Debug*, `dotnet run`,
   `dotnet build`). For a Release build run from source, set the variable first:

   ```powershell
   $env:APP_ENV = 'local'
   ```

3. **Start backend and frontend:**

   ```powershell
   dotnet run --project Backend --launch-profile http
   dotnet run --project Frontend
   ```

4. **Check the connection.** The sidebar indicator turns *Online* within the retry interval; hover it to see the
   address in use. The *Core Information* drillhole list loads.

### Troubleshooting

| Symptom | Cause / fix |
|---|---|
| *Configuration error: No configuration file was found for APP_ENV=local* | Create `Frontend\.env.local` (step 1). The message lists every path that was checked. |
| *…was selected for APP_ENV=production* / app asks for `.env.production` while developing | You are running a Release build. Set `$env:APP_ENV='local'` or use a Debug build. |
| *API_BASE_URL must be an absolute http:// or https:// URL…* | Include the scheme: `http://localhost:5002`, not `localhost:5002`. |
| Indicator stays *Offline* | Backend not running, wrong port, or firewall. Open `<API_BASE_URL>/swagger` in a browser; compare the port with `launchSettings.json`. Check that no `API_BASE_URL` environment variable overrides the file (`Get-ChildItem env:API_*`). |
| *Backend did not respond in time* | Backend is slow or hanging; increase `API_TIMEOUT_SECONDS` or check the backend log. |
| *Online* but drillhole list shows *Backend error 500* | The backend is reachable but cannot read SQL Server — a backend configuration issue, not this file. |

## Production installation

The installer built by `build-installer.ps1` is a Release build, so installed copies use **`APP_ENV=production`**
and read:

```text
%LOCALAPPDATA%\CT007_Measurement_Studio\Config\.env.production
```

e.g. `C:\Users\<user>\AppData\Local\CT007_Measurement_Studio\Config\.env.production`.

This folder is outside Velopack's install folder (`%LOCALAPPDATA%\CT007.MeasurementStudio\`), so installs,
updates and uninstalls never create, overwrite or delete it. The installer ships **no** `.env` file and the app
never creates one, so there is no fabricated production address: until the file exists, the app shows a
configuration error naming the expected path.

### Configure an employee computer

After installing (once per Windows user):

```powershell
$dir = "$env:LOCALAPPDATA\CT007_Measurement_Studio\Config"
New-Item -ItemType Directory -Force $dir | Out-Null
@'
APP_ENV=production

API_BASE_URL=https://ct007-api.example.com
API_TIMEOUT_SECONDS=30
API_RETRY_INTERVAL_SECONDS=10
'@ | Set-Content -Encoding ascii "$dir\.env.production"
```

Replace `https://ct007-api.example.com` with the company backend address (an example, not a real server). Use
`https://` for a deployed backend. Then start the app from the Start menu; the sidebar indicator should turn
*Online* and the drillhole list load.

To roll the file out to many computers, copy the same `.env.production` into each user's `Config` folder (for
example with a logon script or software-distribution tool). Alternatively set `API_BASE_URL` as a user or machine
environment variable — it overrides the file.

### Change the API server later

1. Edit `API_BASE_URL` (or the timings) in `%LOCALAPPDATA%\CT007_Measurement_Studio\Config\.env.production`.
2. **Restart the application.** Settings are read once at start-up; a running app keeps its current address.

No rebuild, reinstall or new release is needed. The change survives application updates.

### Upgrading from an earlier version

Earlier versions read `%LOCALAPPDATA%\CT007_Measurement_Studio\.env` (no `Config` folder, no environment suffix)
and `Api\api-settings.json` next to the executable. Neither is read any more. If the old `.env` still exists and no
new file is found, the configuration error says so: move its `API_*` lines into
`Config\.env.production`.

## Examples

Development (`Frontend\.env.local`):

```dotenv
APP_ENV=local
API_BASE_URL=http://localhost:5002
API_TIMEOUT_SECONDS=5
API_RETRY_INTERVAL_SECONDS=5
```

Production (`%LOCALAPPDATA%\CT007_Measurement_Studio\Config\.env.production`):

```dotenv
APP_ENV=production
API_BASE_URL=https://ct007-api.example.com
API_TIMEOUT_SECONDS=30
API_RETRY_INTERVAL_SECONDS=10
```

Temporary one-off override (PowerShell, current session only):

```powershell
$env:API_BASE_URL = 'http://test-server:5002'
& "$env:LOCALAPPDATA\CT007.MeasurementStudio\current\Frontend.exe"
```

Run an installed (production) build against a local backend for testing:

```powershell
$env:APP_ENV = 'local'   # then reads %LOCALAPPDATA%\CT007_Measurement_Studio\Config\.env.local
```

## Git and packaging

- `.gitignore` excludes `.env` and `.env.*` everywhere except `.env.example`; only the template is committed.
- `Frontend.csproj` removes `.env*` from the project items, so no `.env` file is copied to the build or publish
  output; `build-installer.ps1` additionally refuses to package if any `.env*` file is found in `publish\`.
- Only the environment *name* (`local`/`production`) is compiled into the assembly, never an address.
