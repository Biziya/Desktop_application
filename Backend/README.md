# Backend

ASP.NET Core API used by the desktop app. It reads the drillhole list from SQL Server, stores uploaded measurements,
and serves them back to the Data Viewer page. It targets `net10.0-windows10.0.19041.0` because the Bluetooth endpoint
uses the Windows Runtime API.

## Run

```powershell
cd Backend
dotnet run
```

Swagger is at http://localhost:5002/swagger. Use `dotnet run --launch-profile https` to also listen on
https://localhost:7244.

If Swagger shows "Fetch error - Not Found", an old build is still running. Stop it, delete `bin` and `obj`, and run
again.

## Endpoints

| Endpoint | What it does |
|---|---|
| `GET /api/core-information/drillholes` | Drillholes from `ZOOVCH_COLLAR_LIST`: `holeId`, `projectCode`, `campaign` |
| `GET /api/core-information/bluetooth-device` | Whether a `CT007-M` Bluetooth device is connected to the server PC |
| `GET /api/core-information/sqlite-database` | Path of the newest `ct008-log.sqlite` found on the server PC |
| `POST /api/measurements/upload` | Stores one measurement session imported from a CSV |
| `GET /api/measurements/uploaded/drillholes` | Drillholes that have uploads, with counts and last upload time |
| `GET /api/measurements/uploaded/records?drillhole=DH-001` | Uploaded rows for one drillhole, by depth, at most 10,000 |

The desktop app no longer calls the bluetooth-device and sqlite-database endpoints; it checks those on the laptop
itself.

Upload answers:

- `201` stored, `200` with `alreadyUploaded: true` if the same dataset was stored before (safe to retry).
- `400` invalid request; the message names the field and row.
- `409` some captures of this session were already stored by a different upload. Nothing is inserted.
- `503` SQL Server is unreachable, or the tables below have not been created yet.

Duplicates are detected by a SHA-256 hash of the dataset and by a unique `(SOURCE_SESSION_ID, CAPTURE_NO)`. Each upload
is one call to a stored procedure in one transaction, so a failure stores nothing.

The API has no authentication of its own. User names in an upload are stored as the app sent them. If you host it
with Windows authentication, the authenticated name goes into `UPLOADED_BY_VERIFIED`.

## Connection string

`appsettings.json` leaves `ConnectionStrings:DefaultConnection` empty on purpose. Set it with user-secrets on a dev PC:

```powershell
dotnet user-secrets set ConnectionStrings:DefaultConnection "Data Source=...;Initial Catalog=...;User ID=...;Password=...;Encrypt=True;" --project Backend
```

On a server, set the environment variable `ConnectionStrings__DefaultConnection` instead. Use
`TrustServerCertificate=True` only on dev.

## Database setup

The API never creates or alters database objects. Run `Database/Migrations/001_CreateSppCt007.sql` once in SSMS on
`ACQMN01_DEV`. It is safe to run again. It creates:

- `dbo.SPPCT007_BEK`, one row per capture (skipped if the table already exists)
- the indexes, the `dbo.SPPCT007_BEK_UPLOAD_SEQ` sequence and the `dbo.SPPCT007_BEK_Upload` procedure
- role `SPPCT007_APP` with `EXECUTE` on the procedure and `SELECT` on the table, and adds `useracquire` to it

The backend's login should be in that role only, so it can't insert, update or delete rows directly. The object names
are also in `Repositories/Measurement/SppCt007Database.cs`; rename both together.

## Build and deploy

```powershell
cd Backend
.\build-backend.ps1
```

This publishes a framework-dependent win-x64 Release build to `Backend\release\`. The server needs the ASP.NET Core 10
runtime and the `ConnectionStrings__DefaultConnection` variable. Then point the desktop app's `API_BASE_URL` at it
(see `Frontend/README.md`).
