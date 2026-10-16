# Backend Architecture

**Project:** [`Backend/Backend.csproj`](../../Backend/Backend.csproj). ASP.NET Core Web API (controllers), `net10.0-windows10.0.19041.0` (Windows only, because of the WinRT Bluetooth endpoint). Packages: `Microsoft.Data.SqlClient` 6.1.1, `Swashbuckle.AspNetCore` 9.0.6.

## Structure

`Controller → Repository → external source`. There is **no service layer, ORM or own persistence**; the `Controllers/Models/Repositories/Measurement/` folders are empty placeholders.

| Concern | Implementation | File |
|---|---|---|
| Entry point | Top-level statements: `AddControllers`, `AddSwaggerGen`, `UseSwagger`/`UseSwaggerUI` (always on), `MapControllers`, `Run` | [`Program.cs`](../../Backend/Program.cs) |
| DI | `IDrillholeRepository → DrillholeRepository` (scoped); `BluetoothDeviceRepository` (scoped); `SqliteDatabaseRepository` (**singleton**: keeps a 1-minute cache) | `Program.cs` |
| Configuration | `appsettings.json`: `ConnectionStrings:DefaultConnection` (SQL authentication; **credentials committed**, SUS-01); `launchSettings.json`: `http://localhost:5002`, `https://localhost:7244` | `appsettings*.json`, `Properties/` |
| Authentication, CORS, HTTPS redirect | None | — |

## API

| Endpoint | Controller → repository | Response | Errors | Used by the frontend |
|---|---|---|---|---|
| `GET /api/core-information/drillholes` | `DrillholeController` → `DrillholeRepository` | `200 [{holeId, projectCode, campaign}]` (camelCase) | `SqlException` → `503` ProblemDetails "The drillhole database is unavailable." (logged). Other exceptions → 500 (UR-12) | **Yes** |
| `GET /api/core-information/bluetooth-device` | `BluetoothDeviceController` → `BluetoothDeviceRepository` (WinRT) | `200 {connected, deviceName}` | Swallowed → not connected | No (UR-07) |
| `GET /api/core-information/sqlite-database` | `SqliteDatabaseController` → `SqliteDatabaseRepository` (file search for `ct008-log.sqlite`) | `200 {databasePath}` | Swallowed → `""`; 20 s search timeout | No (UR-07) |
| `/swagger`, `/swagger/v1/swagger.json` | Swashbuckle | OpenAPI | — | No |

## Data access

- **`DrillholeRepository`:** `SELECT HOLEID, PROJECTCODE, RIGHT(PROSPECT, 4) AS CAMPAIGN FROM ZOOVCH_COLLAR_LIST`. No filter, ordering or paging. NULL → `""`. The connection string is read in the constructor; if it's missing, the constructor throws during DI → HTTP 500.
- **`SqliteDatabaseRepository`:** a two-phase folder search with the newest file winning, a `SemaphoreSlim`-serialised search, and the cached result.
- **`BluetoothDeviceRepository`:** default adapter → a connected device whose name starts with `CT007-M`.

## Models

| Record | Fields |
|---|---|
| `DrillholeResponse` | `string HoleId, ProjectCode, Campaign` (non-null) |
| `BluetoothDeviceResponse` | `bool Connected, string DeviceName` |
| `SqliteDatabaseResponse` | `string DatabasePath` |

The frontend has its **own copy** of `DrillholeResponse` (nullable strings). The two are kept aligned by name only; the test INT-005 guards this.

## Asynchrony and errors

- **Async:** all actions are async and take the request `CancellationToken`.
- **Error handling:** there is no global exception handler; error handling is per controller.
- **Logging:** the default ASP.NET Core logging; one explicit `LogError` on SQL failure.
