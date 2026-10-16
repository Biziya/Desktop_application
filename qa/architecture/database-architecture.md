# Database Architecture

Three data stores. No production database contents were accessed for this document; the schemas come from the source code.

## 1. App database (`measurements.db`)

| | |
|---|---|
| Technology | SQLite via `Microsoft.Data.Sqlite` (connection pooling on, `ForeignKeys = true`) |
| Location | `%LocalAppData%\CT007_Measurement_Studio\measurements.db`, plus `capture-settings.json` beside it. A legacy `CT007 Measurement Studio` folder is moved once (`ResolveDefaultPath`) |
| Journal | WAL (set before migrating) |
| Schema version | `PRAGMA user_version = 5` |
| Code | [`Pages/Measurement/MeasurementRepository.cs`](../../Frontend/Pages/Measurement/MeasurementRepository.cs) (measurements), [`Api/Core-information/drillholes/DrillholeCache.cs`](../../Frontend/Api/Core-information/drillholes/DrillholeCache.cs) (cache table) |

### Tables (schema v5)

| Table | Columns (key ones) | Keys and constraints |
|---|---|---|
| `MeasurementSessions` | `id` TEXT (GUID), `CreatedAt` TEXT (ISO `O`), `DrillholeID`, `DrillholeName` (shown as Project), `Project` (shown as Campaign), `"operator"`, `comment` | PK `id`; **UNIQUE** index `UX_MeasurementSessions_DrillholeID` |
| `MeasurementRecords` | `id` INTEGER AUTOINCREMENT, `session_id`, `SortOrder`, `CaptureNo`, `StartTime`/`EndTime` TEXT, `Sample`, `TsMs`, `Count0`, `Count0Raw`, `Hdm`, `HdmRaw`, `DoseRate`, `"from"`/`"to"`/`step` INTEGER **mm**, `duration_ms`, `expected_samples`, `temperature`, `pressure`, `device_serial`/`_name`/`_mac`, `device_session_id` | PK `id`; FK `session_id → MeasurementSessions.id`; **UNIQUE** `(session_id, CaptureNo)` |
| `CaptureSamples` | `id`, `record_id`, `source_id` (the Gamma Guard row id), `serial`, `device_session_id`, `seq`, `ts_ms`, the measurement fields, `temperature`, `pressure` | PK `id`; FK `record_id → MeasurementRecords.id`; **UNIQUE** `(record_id, source_id)` |
| `MeasurementProgress` | `session_id`, `next_from_cm` REAL, `step_cm` REAL | PK + FK `session_id → MeasurementSessions.id` |
| `DrillholeCache` | `hole_id`, `project_code`, `campaign` | PK `hole_id`. Created on first use, outside the migration |

### Relationships

```text
MeasurementSessions 1 ──< MeasurementRecords 1 ──< CaptureSamples
        1 ──1 MeasurementProgress                (DrillholeCache: independent)
```

### Persistence and transactions

| Operation | Transaction contents | Notes |
|---|---|---|
| `ResolveSessionAsync` | `INSERT … ON CONFLICT(DrillholeID) DO NOTHING` + `SELECT` | The existing row wins; concurrent calls are safe |
| `SaveAsync` | Progress upsert; record upsert (`ON CONFLICT(session_id, CaptureNo)` updates **only** `SortOrder`); samples insert-or-ignore | All or nothing; a row of another session throws and rolls back |
| `DeleteAsync` | Delete the samples, then the record | One transaction |
| `ReplaceAsync` (cache) | `DELETE` everything + insert the list | One transaction; the page never calls it with an empty list |

The ViewModel serialises all writes through one queue (thread pool). Times are stored as ISO-8601 round-trip text with their offset; doubles as REAL.

### Migration strategy

`EnsureSchema` runs once per repository instance (with a lock): it sets WAL, then calls `Migrate` in **one transaction**.

| Version | Step |
|---|---|
| < 1 | Create the original tables |
| 2 | Rename `DisplayNo` → `SortOrder` |
| 3 | Rename the id columns |
| 4 | Add the drillhole metadata and depth columns; refuse duplicate DrillholeIDs; create the unique index and `MeasurementProgress` (mm) |
| 5 | Rebuild `MeasurementProgress` in cm; add the capture columns; create `CaptureSamples` |
| Final | `PRAGMA foreign_key_check` must be empty; set `user_version = 5` |

- **Refused:** `user_version > 5` (a newer app's database).
- **Rollback:** any failure rolls the whole upgrade back. The WAL switch happens before the version check (SUS-07).

## 2. Gamma Guard database (vendor-owned, read-only)

| | |
|---|---|
| Owner | Gamma Guard (vendor application); written continuously |
| Location | Configured (`device-settings.json` `gammaGuardDatabasePath`, environment variables expanded) or discovered: a `…GammaGuard…` folder (spaces ignored) → `Database\*.sqlite`, newest file wins |
| Access | `Mode=ReadOnly`, no pooling, 2 s busy timeout, no PRAGMAs. **Never written** (README §17 rule; test BE-027) |
| Schema used (assumed, UR-15) | `samples(id, serial, session_id, seq, ts_ms, count0, count0_raw, hdm, hdm_raw, dose_rate, temperature, pressure)`, `detector_status(serial, name, mac, firmware, device_type, session_id, state, last_sample_ms, updated_ms)` |
| Code | [`GammaGuardDeviceDatabase.cs`](../../Frontend/Calculation/Core-Information/GammaGuardDatabase/GammaGuardDeviceDatabase.cs), [`GammaGuardDatabaseCheck.cs`](../../Frontend/Calculation/Core-Information/GammaGuardDatabase/GammaGuardDatabaseCheck.cs) |

## 3. Corporate SQL Server (backend only)

- **Table:** `ZOOVCH_COLLAR_LIST`, read with `HOLEID`, `PROJECTCODE`, `PROSPECT` (the full schema is unknown, UR-25).
- **Access:** read-only `SELECT` through `Microsoft.Data.SqlClient` with SQL authentication. The frontend never connects to it directly.
