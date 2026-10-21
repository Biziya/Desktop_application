# API connection configuration — SPP CT-007 Measurement Studio

The WPF frontend (`Frontend/`) talks to the ASP.NET Core backend over HTTP. The backend address, request timeout
and reachability-check interval come from **one `.env` file**, chosen by how the application was built:

| Build | How you run it | File it reads |
|---|---|---|
| Debug (development) | `dotnet watch run` / `dotnet run` / Visual Studio *Debug* in `Frontend\` | `Frontend\.env.local` of the source checkout |
| Release (production) | Installed with `CT007.MeasurementStudio-win-Setup.exe` (built by `build-installer.ps1`) | `%LOCALAPPDATA%\CT007_Measurement_Studio\Config\.env.production` |

There is no other source and no fallback: a production build never reads `.env.local` or connects to localhost, and
a development build never reads the production file. Process environment variables are not consulted. No address is
compiled into the application — only the environment name (`local`/`production`, the `AppEnvironment` MSBuild
property: Debug → `local`, everything else → `production`).

The loader is `Frontend/Api/ApiSettings.cs`. It runs once at start-up and its result is passed to the single
`ApiClient` used by every API call (`DrillholesClient`, the Online/Offline indicator).

> **API base URL ≠ database connection string.** `API_BASE_URL` is the HTTP address of the backend API. The
> frontend never connects to SQL Server; the backend's SQL Server connection string (`Backend/appsettings.json`,
> `ConnectionStrings:DefaultConnection`) is configured on the backend and is not part of these files. The local
> SQLite measurement database stays at `%LOCALAPPDATA%\CT007_Measurement_Studio\measurements.db`. Never put database
> passwords, tokens or connection strings in a `.env` file.

## Settings

| Variable | Required | Purpose | Rule |
|---|---|---|---|
| `APP_ENV` | no | Consistency check only: `local` in `.env.local`, `production` in `.env.production`. A mismatch is a configuration error. The build, not this line, selects the file. | `local` or `production` |
| `API_BASE_URL` | **yes** | Backend API base address. Endpoint paths such as `/api/core-information/drillholes` are appended to it. | Absolute `http://` or `https://` URL, no user name/password |
| `API_TIMEOUT_SECONDS` | no (5) | Seconds before a backend request is abandoned (*Backend did not respond in time* / *Offline*). | Number > 0 |
| `API_RETRY_INTERVAL_SECONDS` | no (5) | Seconds between backend reachability checks (sidebar *Online/Offline*, a `HEAD` request to the base URL). | Number > 0 |

Format: one `KEY=value` per line; blank lines and `#` comments are ignored; surrounding quotes are removed. Template:
`Frontend/.env.example`. All `.env*` files except `.env.example` are ignored by Git.

## Development

`Frontend\.env.local` (next to `Frontend.csproj`), for example:

```dotenv
APP_ENV=local
API_BASE_URL=http://localhost:5002
API_TIMEOUT_SECONDS=5
API_RETRY_INTERVAL_SECONDS=5
```

Point `API_BASE_URL` at whichever backend you develop against (`http://localhost:5002` is the backend's `http`
profile in `Backend/Properties/launchSettings.json`). Then:

```powershell
cd Frontend
dotnet watch run
```

The Debug build finds `.env.local` by walking up from its output folder (`bin\Debug\...`) to the folder holding
`Frontend.csproj`. Edit the file and restart the app (with `dotnet watch`, press Ctrl+R) — no rebuild needed.

## Production

### What the installer contains

`build-installer.ps1` validates `Frontend\.env.production` and ships it inside the installer as
`Config\.env.production.template` (in `%LOCALAPPDATA%\CT007.MeasurementStudio\current\` after installation). It is
the only `.env` file packaged. Example:

```dotenv
APP_ENV=production
API_BASE_URL=https://ct007-api.example.com
API_TIMEOUT_SECONDS=30
API_RETRY_INTERVAL_SECONDS=10
```

(`https://ct007-api.example.com` is a placeholder; use the company backend address.)

### First launch: the file is initialized automatically

At every start the installed app:

1. Checks `%LOCALAPPDATA%\CT007_Measurement_Studio\Config\.env.production`
   (e.g. `C:\Users\<user>\AppData\Local\CT007_Measurement_Studio\Config\.env.production`).
2. **If it exists, reads it as is — it is never overwritten.**
3. If it does not exist (first launch, or someone deleted it), creates the `Config` folder and copies the shipped
   template there, once. Per user, no administrator rights needed.
4. Validates the settings (`API_BASE_URL` required) and continues the normal start-up.

If the file is missing *and* the installation has no template (an installer built without
`Frontend\.env.production`), or any value is invalid, the app shows a *configuration error* naming the file and key
and closes. It never invents an address.

### Why updates keep your settings

The per-user `Config` folder is outside Velopack's install folder (`%LOCALAPPDATA%\CT007.MeasurementStudio\`), which
is the only folder installs, updates and uninstalls replace. The app copies the template only when the file is
missing, so a newer template in a later release does **not** replace an existing, possibly edited, file. To roll a
new address out to computers that already have the file, edit their file (or delete it so the next start copies
the new template).

### Change the production API address (no rebuild)

1. Edit `API_BASE_URL` in `%LOCALAPPDATA%\CT007_Measurement_Studio\Config\.env.production`, e.g.
   `notepad "$env:LOCALAPPDATA\CT007_Measurement_Studio\Config\.env.production"`.
2. **Restart the application.** Settings are read once at start-up.

To change the default for *new* installations, edit `Frontend\.env.production` and rebuild the installer.

### Rebuild the installer

```powershell
.\build-installer.ps1
```

Before building, the script checks `Frontend\.env.production`: it must exist, contain only `APP_ENV`,
`API_BASE_URL`, `API_TIMEOUT_SECONDS` and `API_RETRY_INTERVAL_SECONDS` (anything else — e.g. a password or
connection string — fails the build with an explanation, because the file is packaged), `APP_ENV` must be
`production` if present, and `API_BASE_URL` must be an absolute http(s) URL without credentials or tokens. After
publishing it checks that `Config\.env.production.template` is exactly that file and that no other `.env` file is in
`publish\`. See [INSTALLER_GUIDE.md](INSTALLER_GUIDE.md).

A plain `dotnet publish -c Release` also includes the template when `Frontend\.env.production` exists (and warns
when it does not), but does not run the script's validation.

## Testing the configuration

- **Development:** `dotnet watch run` in `Frontend\` opens the main window; the sidebar indicator turns *Online*
  when the `.env.local` backend is reachable (hover it to see the address).
- **Production publish:** `dotnet publish Frontend/Frontend.csproj -c Release -r win-x64 --self-contained true -o ./publish`,
  then check that `publish\Config\.env.production.template` exists and that no `.env.local` is in `publish\`.
- **First launch:** delete (or rename) `%LOCALAPPDATA%\CT007_Measurement_Studio\Config\.env.production`, start the
  installed app: the file reappears with the template's content.
- **Persistence:** change `API_BASE_URL`, restart: the tooltip of the indicator shows the new address; restart again:
  the file is unchanged.
- **Errors:** an empty file, `API_BASE_URL=10.0.0.1:7047` (no scheme) or `APP_ENV=local` in the production file must
  each show a *configuration error*, not a crash.
- Automated: `dotnet run --project Tests/CT007.Tests -- FE-005`.

## Troubleshooting

| Message / symptom | Cause / fix |
|---|---|
| *…\Config\.env.production was not found, and this installation has no configuration template* | The installer was built without `Frontend\.env.production`. Create the per-user file by hand (see the example above) or rebuild the installer with the file in place. |
| *…\Frontend\.env.local was not found* | Development: create `Frontend\.env.local` from `.env.example`. |
| *This is a development (Debug) build: it reads Frontend\.env.local and must run from the source checkout* | A Debug build was copied elsewhere. Run it from the checkout, or install a build made with `build-installer.ps1`. |
| *…does not set API_BASE_URL* | The file is empty or the line is missing/blank; add `API_BASE_URL=https://…`. |
| *API_BASE_URL must be an absolute http:// or https:// URL…* | Include the scheme (`http://10.0.0.1:7047`, not `10.0.0.1:7047`); no `user:password@`. |
| *APP_ENV=local, but this file is the production configuration* | Set `APP_ENV=production` in the production file (or remove the line). |
| `build-installer.ps1`: *'X' is not an API setting* | Remove that line from `Frontend\.env.production`; secrets must never be packaged. |
| Indicator stays *Offline* | Backend not running, wrong address/port, or firewall/VPN. Open `<API_BASE_URL>/swagger` in a browser on that computer. |
| *Online*, but the drillhole list shows *Backend error 500* | The backend is reachable but cannot read SQL Server: a backend configuration issue, not this file. |
| Installed app still uses an old address after an update | By design the per-user file is kept; edit it (see *Change the production API address*). |

## Upgrading from earlier versions

Earlier builds read `Api\api-settings.json`, `%LOCALAPPDATA%\CT007_Measurement_Studio\.env`, a per-user
`Config\.env.local` and `APP_ENV`/`API_*` environment variables. None of these is read any more.
