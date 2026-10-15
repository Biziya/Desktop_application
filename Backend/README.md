# Backend – CT-007 Desktop

## How to run

Requirements: Windows 10 (1809) or later, [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
cd Backend
dotnet restore
dotnet run
```

`dotnet run` uses the `http` profile from `Properties/launchSettings.json`. When the backend is ready
the terminal prints the Swagger address:

```text
info: Backend[0]
      Swagger UI: http://localhost:5002/swagger
```

Open that URL in a browser. To also listen on HTTPS (`https://localhost:7244`):

```powershell
dotnet run --launch-profile https
```

From Visual Studio: open `Backend.csproj` and press **F5**; Swagger opens automatically.

Stop the backend with **Ctrl+C**.

If Swagger shows `Fetch error – Not Found`, an old build is running: stop it, delete the `bin` and
`obj` folders, and run `dotnet run` again. The Swagger document is served at
`/swagger/v1/swagger.json`.

## Core Information APIs

| Endpoint | Source | Response |
|---|---|---|
| `GET /api/core-information/drillholes` | SQL Server (`ConnectionStrings:DefaultConnection`) | `[{ "holeId", "projectCode", "campaign" }]` — `503` if SQL Server is unreachable |
| `GET /api/core-information/bluetooth-device` | Windows Bluetooth (WinRT) | `{ "connected": bool, "deviceName": string }` |
| `GET /api/core-information/sqlite-database` | File system | `{ "databasePath": string }` (empty when not found) |

All three endpoints are grouped under the **Core Information** tag in Swagger.

## Windows requirements (Bluetooth API)

- The project targets `net10.0-windows10.0.19041.0`, which pulls in the Windows SDK projection
  (`Microsoft.Windows.SDK.NET.Ref`) automatically on restore. No extra package is needed.
- Minimum OS: Windows 10 1809 (`SupportedOSPlatformVersion 10.0.17763.0`).
- The backend runs as a normal unpackaged desktop process; it only *reads* Bluetooth state, so no
  app-manifest capability is required. Bluetooth must be enabled in Windows Settings; if the adapter is
  missing or turned off the endpoint returns `connected: false`.
- Detection: Classic and LE devices with `ConnectionStatus == Connected` whose name starts with
  `CT007-M` (case-insensitive). Each match is re-opened and its live status is checked on every request,
  so paired-but-disconnected probes are ignored. If several are connected, the first name in
  case-insensitive alphabetical order is returned.

## SQLite discovery (`ct008-log.sqlite`)

1. Shallow scan (depth 2) for folders whose name contains `CT008` under Downloads (known-folder
   lookup), Desktop, Documents, the user profile, AppData (Local/Roaming), Program Files (x64/x86),
   ProgramData and the system drive root; then a recursive search inside those folders.
2. Only if nothing is found: depth-limited search of Downloads, Desktop and Documents.

Inaccessible folders, junctions/symlinks and noisy folders (`Windows`, `node_modules`, `.git`, …) are
skipped. The search is cancellable, capped at 20 seconds, and cached for 1 minute. The most recently
modified file wins (full path breaks ties). The file is never opened.

`SqliteDatabaseRepository.IsDatabaseFile(path)` holds the validation rules so a future manual
"select file" endpoint can reuse them without touching the automatic search.
