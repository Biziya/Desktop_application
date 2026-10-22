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

## Measurement upload API (Upload Data page)

`POST /api/measurements/upload` (tag **Measurement**) stores one measurement session imported from a CSV in the
central database (`ConnectionStrings:DefaultConnection`).

```json
{
  "measurementInformation": {
    "sessionId": "8c4e…",            // session_id of the CSV (the desktop app's measurement session)
    "drillhole": "DH-001", "project": "Project A", "campaign": "2026",
    "operator": "DOMAIN\\operator", "notes": "…",
    "createdUsername": "DOMAIN\\creator",   // from the CSV; null for older exports
    "uploadingUsername": "DOMAIN\\uploader" // Windows account running the desktop app
  },
  "records": [
    { "captureNumber": 1, "startTime": "2026-10-16T09:00:00.1234567+08:00", "endTime": "2026-10-16T09:00:05.1234567+08:00",
      "sampleCount": 5, "depthFrom": 0.0, "depthTo": 0.1, "tsMs": 1792115769651,
      "count0": 125.0, "count0Raw": 125.0, "hdm": 10.5, "hdmRaw": 10.5, "doseRate": 0.12 }
  ]
}
```

`count0` is the CPS (counts per second) value shown in the table; depths are metres; `tsMs` is the device's Unix
milliseconds. Device values may be null (stored as NULL — nothing is invented).

| Answer | When | Body |
|---|---|---|
| `201 Created` | stored | `{ "uploadId", "recordCount", "alreadyUploaded": false, "uploadedAt" }` |
| `200 OK` | this exact dataset was stored before (retry, or the same file again) — nothing inserted | same, `alreadyUploaded: true`, describing the earlier upload |
| `400` | invalid request | ProblemDetails; `detail` names the problem and row, e.g. `Row 3: depthTo must be greater than depthFrom.` |
| `409` | some captures of this session were stored by an earlier, different upload — nothing inserted | ProblemDetails listing the Capture No values |
| `503` | SQL Server unreachable or failing — nothing inserted | ProblemDetails without internal details (the error is logged) |

**Validation (server side, whatever the client checked):** session id; Drillhole, Operator and Uploading Username
required; field lengths; 1–10,000 records; Capture No > 0 and unique; start/end present, end ≥ start, plausible dates;
sample count ≥ 0; depths 0–100,000 m with at most 3 decimals and `depthTo > depthFrom`; `tsMs` ≥ 0; finite numbers.

**Duplicate prevention.** The server computes a SHA-256 fingerprint of the dataset (session id, metadata and every
record; not the uploading user) and `DATASET_HASH` is unique, so a request repeated after a client timeout, or the same
file uploaded again by anyone, returns the stored upload instead of inserting it twice. Independently,
`(SOURCE_SESSION_ID, CAPTURE_NO)` is unique, so a capture is stored once even if it arrives in another file (e.g. a later
export of the same drillhole session that also contains new captures): that upload is refused with 409 and the user
removes the already uploaded rows on the Upload Data page. Drillhole alone is not used, because one drillhole can have
several sessions. Lookups run under `UPDLOCK, HOLDLOCK`, so concurrent identical requests are serialised.

**Transaction.** The session row and all its records are inserted in one transaction (parameterized SQL); any failure
rolls everything back.

**Identity.** The backend has no authentication of its own. `createdUsername` and `uploadingUsername` are stored as
*declared by the app* and are not treated as an authenticated identity. If the backend is hosted with Windows
authentication (e.g. IIS / Negotiate), the authenticated name is stored separately in `UPLOADED_BY_VERIFIED`;
otherwise that column is NULL.

### Database migration (required once)

Run `Database/Migrations/001_CreateMeasurementUploadTables.sql` on the central database (`ACQMN01`) with an account
allowed to create tables. It creates `dbo.CT007_MEASUREMENT_UPLOAD` (one row per upload: fingerprint, source session,
metadata, user names, time, record count) and `dbo.CT007_MEASUREMENT_UPLOAD_RECORD` (the rows, with original
timestamps, depths, computed `STEP_CM`, `TS_MS` and device values; `COUNT0` = CPS). It is safe to run again. The
backend's login then needs `SELECT` and `INSERT` on both tables. Until the script has run, uploads answer 503.

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
