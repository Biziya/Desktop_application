# Backend Analysis

Project: `Backend/Backend.csproj` (ASP.NET Core Web API, `net10.0-windows10.0.19041.0`, nullable enabled).
All files under `Backend/` were read: 3 controllers, 3 models, 4 repository files, `Program.cs`, the config files and the README.

## 1. Architecture

`Controller → Repository → external source`. There is no service layer, no EF Core, no domain logic and no persistence of its own. The `Measurement` folders contain only `.gitkeep`.

| Concern | Implementation (Confirmed) |
|---|---|
| Hosting | `WebApplication.CreateBuilder`; `AddControllers`, `AddSwaggerGen`; `UseSwagger`/`UseSwaggerUI` **always on** (not gated by environment); logs the Swagger URL on start (`Program.cs`) |
| DI | `IDrillholeRepository → DrillholeRepository` (scoped); `BluetoothDeviceRepository` (scoped, concrete); `SqliteDatabaseRepository` (**singleton**, so its in-memory cache survives between requests) |
| Config | `appsettings.json`: `ConnectionStrings:DefaultConnection` (SQL Server, SQL authentication); `appsettings.Development.json`: logging only; `launchSettings.json`: `http://localhost:5002`, `https://localhost:7244` |
| Auth | None. No authentication or authorization middleware; `AllowedHosts: *` |
| CORS / HTTPS redirect | None |
| Error format | `Problem(...)` (RFC 7807) only in `DrillholeController` |

## 2. API endpoints

| Endpoint | Controller | Repository | Response | Errors |
|---|---|---|---|---|
| `GET /api/core-information/drillholes` | `Controllers/CoreInformation/Drillhole/DrillholeController.cs` | `Repositories/CoreInformation/Drillhole/DrillholeRepository.cs` | `200 [ { holeId, projectCode, campaign } ]` | `SqlException` → `503` ProblemDetails "The drillhole database is unavailable." (logged) |
| `GET /api/core-information/bluetooth-device` | `…/BluetoothDevice/BluetoothDeviceController.cs` | `…/BluetoothDevice/BluetoothDeviceRepository.cs` | `200 { connected, deviceName }` | Never fails: every non-cancellation exception → `NotConnected` (logged as a warning) |
| `GET /api/core-information/sqlite-database` | `…/SqliteDatabase/SqliteDatabaseController.cs` | `…/SqliteDatabase/SqliteDatabaseRepository.cs` | `200 { databasePath }` (`""` when not found) | Inaccessible folders are skipped; the search times out after 20 s with partial results |

Swagger: `/swagger`, `/swagger/v1/swagger.json`. All endpoints are tagged "Core Information". Every action accepts a `CancellationToken` (request abort).

## 3. Business logic per repository

### DrillholeRepository (SQL Server)
- Query: `SELECT HOLEID, PROJECTCODE, RIGHT(PROSPECT, 4) AS CAMPAIGN FROM ZOOVCH_COLLAR_LIST;`. No `WHERE`, no `ORDER BY`, no paging, so the whole table is returned on each call.
- Transformation: DB `NULL` → `""`; values are converted with `Convert.ToString(…, InvariantCulture)`. Campaign is the **last 4 characters of PROSPECT** (business rule embedded in SQL).
- The connection string is read in the constructor. If it is missing, the constructor throws `InvalidOperationException`. That happens during DI activation, outside the controller's `try`. **Inferred:** the result is an unhandled 500, not a 503.
- Only `SqlException` is mapped to 503. **Inferred:** other failures (for example `InvalidCastException`, or a connection-pool `InvalidOperationException` timeout) surface as 500.

### BluetoothDeviceRepository (WinRT, backend host machine)
- Steps: default adapter → radio must be `On`. Then Classic and LE devices with `ConnectionStatus.Connected` whose name starts with `CT007-M` (case-insensitive). Each match is re-opened and its live status checked.
- Several matches → the first in case-insensitive name order.
- **Observation:** the check describes the *backend's* computer. The frontend no longer calls this endpoint (it has its own local check, see `frontend-analysis.md`).

### SqliteDatabaseRepository (file system, backend host machine)
- Looks for `ct008-log.sqlite`. Phase 1 scans depth 2 for folders containing "CT008" under Downloads (`SHGetKnownFolderPath` P/Invoke), Desktop, Documents, the profile, AppData, Program Files (x64/x86), ProgramData and the system drive root, then searches inside those folders to depth 8. Phase 2, only if nothing was found, searches Downloads, Desktop and Documents to depth 6.
- Skips reparse points, inaccessible folders and noisy names (`Windows`, `node_modules`, `.git`, `Temp`, …).
- Picks the newest `LastWriteTimeUtc`; the full path breaks ties (`SelectMostRecent`, `internal static`).
- Concurrency: `SemaphoreSlim(1,1)` serialises searches. The result is cached for 1 min and the cache is invalidated if the file disappears. The search has a 20 s linked timeout. A timeout keeps partial candidates; a caller cancellation propagates.
- `IsDatabaseFile` (public static) checks file metadata only and never opens the DB. A comment says it is reserved for a "future manual selection".
- **Observation:** the discovery rule ("CT008" folder, fixed file name) differs from the frontend's Gamma Guard discovery ("GammaGuard" folder, `Database\*.sqlite`). Both appear to target the same vendor DB. The frontend does not call this endpoint.

## 4. Data models (contracts)

| Record | File | Fields |
|---|---|---|
| `DrillholeResponse` | `Models/CoreInformation/Drillhole/DrillholeResponse.cs` | `string HoleId, ProjectCode, Campaign` (non-null) |
| `BluetoothDeviceResponse` | `Models/CoreInformation/BluetoothDevice/BluetoothDeviceResponse.cs` | `bool Connected, string DeviceName`; static `NotConnected` |
| `SqliteDatabaseResponse` | `Models/CoreInformation/SqliteDatabase/SqliteDatabaseResponse.cs` | `string DatabasePath` |

Serialization uses the ASP.NET Core defaults (camelCase JSON). There are no request bodies, query parameters or validation attributes; every endpoint is a parameterless GET.

## 5. Validation and error handling summary

- No input validation exists because there are no inputs.
- Errors are handled per repository: SQL → 503; Bluetooth → swallowed to `NotConnected`; file search → swallowed, with an empty path on failure.
- There is no global exception handler or `UseExceptionHandler`. **Inferred:** in Development the framework's developer exception page applies; in Production it is a plain 500.

## 6. Asynchrony and concurrency

- All actions are `async`. WinRT calls use `.AsTask(cancellationToken)`. The file search runs in `Task.Run` with cooperative cancellation through `ShouldRecursePredicate`.
- `SqliteDatabaseRepository` is a singleton holding mutable state (`_cachedPath`, `_cachedAtUtc`) guarded by the semaphore.

## 7. External dependencies

| Dependency | Used by | Notes |
|---|---|---|
| SQL Server `ZOOVCH_COLLAR_LIST` (corporate host named in config) | Drillholes | **Security observation:** the connection string with plain-text SQL credentials is committed in `Backend/appsettings.json` (values deliberately not reproduced here) |
| Windows Bluetooth stack (WinRT) | Bluetooth endpoint | Requires Windows 10 1809+ and an adapter |
| Local file system, `shell32.dll` | SQLite endpoint | Scans can be slow on large profiles |

## 8. Testability notes

- `IDrillholeRepository` is the only interface. The controller can be tested with a fake repository, or the API with `WebApplicationFactory` and a substituted service.
- `BluetoothDeviceRepository` and `SqliteDatabaseRepository` are concrete classes with static WinRT, file-system and P/Invoke calls and no seams. The `internal` methods `DiscoverDatabasePath(roots…)` and `SelectMostRecent` accept injected roots, but there is no `InternalsVisibleTo`.
- There are **no backend tests** of any kind (see `testing-infrastructure.md`).
