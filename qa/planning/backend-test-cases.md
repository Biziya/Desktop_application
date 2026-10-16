# Backend Test Cases (BE)

Scope:
- **Part 1** is the ASP.NET Core project `Backend/`.
- **Part 2** is the non-UI business and data layer that runs inside the WPF process (`Frontend/Calculation/**`, `Frontend/Pages/Measurement/MeasurementRepository.cs`, `CsvExportService.Serialize`, `DrillholeCache`), plus `tools/check-db.ps1`.

For the reasoning behind this split, see [test-plan.md](test-plan.md) §2.1. The conventions are the same as [frontend-test-cases.md](frontend-test-cases.md): a **Shared** line per group, ENV/TD references, *Existing coverage*, and an expectation status that is **Inferred** unless it says **Unresolved**.

---

# Part 1 — ASP.NET Core backend

**Shared.** Environment: ENV-API (an in-memory host through `WebApplicationFactory<Program>`, with `IDrillholeRepository` replaced by a fake; no SQL Server). Preconditions: a new `Backend.Tests` project (none exists, UR-24). `Program` must be reachable from the test project; for top-level statements that needs `public partial class Program {}`, or the generated accessibility must be confirmed (UR-27). Source: `Backend/Program.cs`, `Controllers/CoreInformation/Drillhole/DrillholeController.cs`, `Repositories/CoreInformation/Drillhole/*`, `Models/CoreInformation/Drillhole/DrillholeResponse.cs`. Frontend consumer: `Frontend/Api/Core-information/drillholes/DrillholesClient.cs`. Existing coverage: none.

<a id="be-001"></a>

### BE-001 · `GET /api/core-information/drillholes` returns the camelCase contract
- API · Medium (4×2) · Conditional (ENV-API) · Inferred
- **Input:** the fake repository returns `[("DH-1","P1","2024"), ("DH-2","","")]`.
- **Expected:**
  - 200 with `Content-Type: application/json; charset=utf-8`.
  - The body is exactly `[{"holeId":"DH-1","projectCode":"P1","campaign":"2024"},{"holeId":"DH-2","projectCode":"","campaign":""}]`. The property order and names are camelCase, and there are no extra fields.
  - An empty repository result gives 200 `[]`.

<a id="be-002"></a>

### BE-002 · `SqlException` maps to 503 ProblemDetails
- API · Medium (4×2) · Conditional (constructing a `SqlException` needs reflection; or use BE-006's real instance with the server stopped) · Inferred
- **Expected:**
  - 503 with `Content-Type: application/problem+json`; `title` or `detail` contains "The drillhole database is unavailable."
  - **No** connection string, server name, user name or stack trace appears in the body.
  - One Error-level log entry.

<a id="be-003"></a>

### BE-003 · A missing connection string
- API · High (3×3) · Conditional · **Unresolved** (UR-12)
- **Preconditions:** the host is configured with `ConnectionStrings:DefaultConnection` removed. Use the real `DrillholeRepository`, not the fake.
- **Expected (current, Inferred):** the first GET returns 500, because DI activation fails outside the controller's `try`. The desired status (503, or fail-fast at start-up) is unresolved. In the Production environment the body must not contain an exception stack (see BE-009).

<a id="be-004"></a>

### BE-004 · Repository exceptions other than SQL
- API · Medium (3×2) · Conditional · **Unresolved** (UR-12)
- **Input:** the fake throws `InvalidOperationException` or `InvalidCastException`.
- **Expected (current, Inferred):** 500, with no stack in the body under Production. Whether these should become 503 is UR-12.

<a id="be-005"></a>

### BE-005 · Request cancellation reaches the repository
- API · Medium (2×2) · Conditional · Inferred
- **Actions:** the fake repository awaits `Task.Delay(Infinite, token)`. The client aborts after 200 ms.
- **Expected:** the repository observes cancellation within 1 s; no 503 is logged as a database outage.

<a id="be-006"></a>

### BE-006 · SQL projection and NULL handling against a disposable SQL Server
- Integration · High (4×3) · Conditional (ENV-SQL, a LocalDB or container instance; **never** the corporate server) · Inferred, with the campaign rule **Unresolved** (UR-08)
- **Preconditions:** a table `ZOOVCH_COLLAR_LIST(HOLEID, PROJECTCODE, PROSPECT)` seeded with TD-SQL.
- **Input and expected:**

  | HOLEID | PROJECTCODE | PROSPECT | Expected holeId / projectCode / campaign |
  |---|---|---|---|
  | `DH-1` | `P1` | `ZOV-2024` | `DH-1` / `P1` / `2024` |
  | `DH-2` | NULL | NULL | `DH-2` / `""` / `""` |
  | `DH-3` | `P3` | `24` (shorter than 4) | `DH-3` / `P3` / `24` (T-SQL `RIGHT` returns the whole string; rule UR-08) |
  | `DH-4` | `P4` | `ABC-2024 ` (trailing space) | campaign = `024 `, Inferred; flag as a data-quality question in UR-08 |
  | `ДН-5` | `П5` | `Кампания` | Unicode preserved (requires NVARCHAR, real column types Unknown) |
  | NULL | `P6` | `X` | `holeId` `""` (the frontend drops it, FE-013) |
- **Also:** the row count equals the table row count (no filtering).

<a id="be-007"></a>

### BE-007 · Large-table response time
- Performance · Medium (3×2) · Conditional (ENV-SQL) · **Unresolved** (UR-21: table size and target)
- **Input:** 50,000 rows.
- **Expected:** a proposed value of < 5 s end-to-end, which is the frontend timeout. Record the measured time and payload size. If it is slow, refer it to UR-21 (paging).

<a id="be-008"></a>

### BE-008 · OpenAPI document
- API · Medium (2×2) · Conditional · Inferred
- **Expected:** `/swagger/v1/swagger.json` returns 200. It lists exactly three GET paths (`drillholes`, `bluetooth-device`, `sqlite-database`) tagged "Core Information". The `DrillholeResponse` schema has three string properties. Snapshot it as the contract baseline for INT-005.

<a id="be-009"></a>

### BE-009 · Authentication posture and error-detail leakage
- Security · High (4×3) · Conditional · **Unresolved** (UR-14: intended deployment and authentication)
- **Actions:**
  1. Call every endpoint with no credentials.
  2. Run under `ASPNETCORE_ENVIRONMENT=Production` and force a 500 (BE-004).
  3. Request `/swagger` under Production.
- **Expected (current):**
  1. 200 (no authentication), recorded as an observation.
  2. No stack trace, connection string or file path appears in the body.
  3. Swagger is currently exposed (Inferred). The desired setting is pending UR-14.
- **Also:** check whether Kestrel binds only to localhost. The launch profile uses `localhost:5002`, but deployment binding is Unknown.

<a id="be-010"></a>

### BE-010 · No credentials in the repository
- Security (static) · **Critical (override; 5×5)** · Conditional (needs a secret scanner such as gitleaks; installing it is Phase 3+) · **Unresolved** (UR-14: secret-management policy)
- **Actions:** scan the working tree and the git history for connection strings and passwords.
- **Expected:** no plaintext credentials. The current state is a known failure: `Backend/appsettings.json` contains a SQL Server user and password. Test reports must **not** reproduce the values.

<a id="be-011"></a>

### BE-011 · Routing contract for reachability
- API · Medium (3×2) · Conditional · Inferred
- **Expected:**
  - `HEAD /` gives 404. The frontend counts any HTTP answer as Online, so this status is part of the contract.
  - `POST`/`PUT`/`DELETE` on `/api/core-information/drillholes` give 405.
  - An unknown path under `/api` gives 404.

<a id="be-012"></a>

### BE-012 · Bluetooth endpoint (unused, F21)
- API · Low (1×2) · Conditional (Windows; the result depends on the host's adapter) · **Unresolved** (UR-07)
- **Expected (current):**
  - With no adapter, the radio off, or no connected CT007-M: 200 `{"connected":false,"deviceName":""}`.
  - It never returns 5xx.
  - It responds within 10 s.
- **Source:** `Backend/**/BluetoothDevice/*`.

<a id="be-013"></a>

### BE-013 · CT008 SQLite selection rule (unused, F22)
- Unit · Low (1×2) · **Blocked** (`SelectMostRecent` and `DiscoverDatabasePath` are `internal`, and there is no `InternalsVisibleTo`; UR-27) · **Unresolved** (UR-07)
- **Expected (current):** the newest `LastWriteTimeUtc` wins, and a tie goes to the full path. Reparse points and noisy folders are skipped. **Source:** `Backend/Repositories/CoreInformation/SqliteDatabase/SqliteDatabaseRepository.cs`.

<a id="be-014"></a>

### BE-014 · CT008 discovery caching, timeout and concurrency (unused)
- API · Low (1×2) · Conditional (ENV-FS on the backend host; scanning the real profile is slow) · **Unresolved** (UR-07)
- **Expected (current):**
  - A second call within 1 min returns the cached path without rescanning.
  - Deleting the file invalidates the cache.
  - 5 parallel requests run one search.
  - A search over 20 s returns partial results with 200.

---

# Part 2 — In-process business and data layer (inside `Frontend/`)

## 2A. Depth arithmetic and ordering (F12, F16)

**Shared.** Environment: ENV-PURE. Source: `Frontend/Calculation/Measurement/Depth.cs`, `MeasurementOrder.cs`. Existing coverage: `MeasurementUnitsAndOrderChecks`, `AutofillAndDepthInputChecks`.

<a id="be-015"></a>

### BE-015 · `Depth.TryParseMetres`
- Unit · High (5×3) · Automatable · Inferred
- **Input → expected (mm, or rejected):**

  | Input | Result |
  |---|---|
  | `0` | 0 |
  | `10.25` | 10250 |
  | `10,25` | 10250 |
  | ` 10.5 ` | 10500 (whitespace allowed) |
  | `10.255` | 10255 |
  | `10.2555` | rejected (finer than 1 mm) |
  | `10.2550` | 10255 (the trailing zero is still whole mm) |
  | `-0.001` | rejected |
  | `-0` | rejected (sign not allowed) |
  | `+5` | rejected |
  | `1e3` | rejected |
  | `1,000.5` | rejected (two separators after replacement) |
  | `.5` | 500 (Inferred; confirm with the product owner) |
  | `10.` | 10000 (Inferred) |
  | `""`, `abc`, `١٠` (Arabic-Indic digits) | rejected |
  | `9999999999999999.999` | 9999999999999999999 → rejected if > `long.MaxValue/2` |
- **Run** under `InvariantCulture`, `fr-FR` and `ru-RU`; the results must be identical.

<a id="be-016"></a>

### BE-016 · `Depth.TryParseCentimetres`
- Unit · High (5×3) · Automatable · Inferred
- **Input → expected (mm):**
  - `25` → 250; `2.5` → 25; `2,5` → 25.
  - `0` → **0, accepted by the parser** (the ViewModel rejects it, FE-024).
  - `0.1` → 1.
  - `0.05`, `2.55` and `-1` → rejected.
  - `1000` → 10000.
  - The same culture matrix as BE-015.

<a id="be-017"></a>

### BE-017 · `Depth.Format` and `FormatCentimetres`
- Unit · Medium (4×2) · Automatable · Inferred
- **Expected:**
  - `Format`: 0 → `0.00`; 10500 → `10.50`; 11255 → `11.255`; 1 → `0.001`.
  - `FormatCentimetres`: 250 → `25`; 25 → `2.5`; 5 → `0.5`.
  - The output always uses `.` under `fr-FR`.
  - Round-trip: `TryParseMetres(Format(x)) == x` for x in 0…100000 (sampled).

<a id="be-018"></a>

### BE-018 · No cumulative drift over many intervals
- Unit · High (5×2) · Automatable · Inferred
- **Actions:** start at 0 mm and add a step of `2.5` cm 1000 times.
- **Expected:** the final From is exactly 25000 mm and `Format` gives `25.00`. Every `to − from` is 25.

<a id="be-019"></a>

### BE-019 · `MeasurementOrder` sorting and `InsertIndex`
- Unit · Medium (4×2) · Automatable · Inferred
- **Expected:**
  - Rows are sorted by From ascending, with null From last.
  - Equal From: earlier StartTime first, then stored rows (with an ID) before unsaved ones, then by ID, then by CaptureNo.
  - Property check: for 1000 random rows (fixed seed), inserting each at `InsertIndex` gives the same order as a full sort.

## 2B. Capture window, averaging and time basis (F15)

**Shared.** Environment: ENV-PURE. Source: `Frontend/Calculation/Measurement/DeviceSamples.cs`, `Calculation/Core-Information/GammaGuardDatabase/GammaGuardCaptureDevices.cs`. Data: TD-GG streams. Existing coverage: `DeviceCaptureChecks`.

<a id="be-020"></a>

### BE-020 · `CaptureWindow` membership and de-duplication
- Unit · **Critical** (5×4) · Automatable · Inferred (the window rule differs from the README's 3 s delay design, **UR-01**)
- **Preconditions:** serial `S1`, trigger T = 1,760,600,000,000, duration 5000.
- **Input → accepted?**

  | Row | Accepted |
  |---|---|
  | ts = T | no (the click itself is excluded) |
  | ts = T+1 | yes |
  | ts = T+5000 | yes |
  | ts = T+5001 | no |
  | ts = T−1 | no |
  | serial `s1` (different case) | no (ordinal comparison) |
  | serial `S2` | no |
  | ts null | no |
  | same `SourceId` twice | first only |
  | same (session `A`, seq 7, ts) with a different id | first only |
  | same seq/ts but session `B` | yes |
  | seq null, same ts, different id | yes (no seq de-dup) |
- **Expected also:** `Add` returns the number of new rows. `Accepted` is ordered by ts, then by `SourceId`.

<a id="be-021"></a>

### BE-021 · Expected sample count
- Unit · Medium (4×2) · Automatable · Inferred (assumes 1 Hz; **UR-02**)
- **Input → `ExpectedSamples`:** 2000 → 2; 5000 → 5; 10000 → 10; 2500 → 2; 999 → 1; 0 → 1.

<a id="be-022"></a>

### BE-022 · `SampleAveraging.Summarize`
- Unit · High (5×3) · Automatable · Inferred
- **Input → expected:**
  - Count0 `[10, 20, null, NaN, +∞]` gives a mean of 15 (only finite values count).
  - A field where every value is null gives null.
  - An empty list gives `SampleCount` 0 and every field null.
  - `[0.1, 0.2]` gives 0.15 within 1e-12.
  - `FirstTsMs` is the minimum ts; on a tie, the lower `SourceId`; it skips rows with a null ts.
  - The `DeviceSessionId` is `A` when all samples are `A`; null for `A` and `B`; null when they are all null.
  - `SampleCount` counts every accepted sample, even those whose fields are all null.

<a id="be-023"></a>

### BE-023 · `DeviceTime.Problem`
- Unit · High (5×3) · Automatable · Inferred (the 12 h tolerance is **UR-10**)
- **Input → expected (now = 1,760,600,000,000):**

  | latestTsMs | Result |
  |---|---|
  | 99,999,999,999 | the message "…is not in Unix milliseconds…" |
  | 1,760,600,000 (seconds) | the same message |
  | 100,000,000,000 | the clock message, because it is far from now |
  | now − 43,200,000 (exactly 12 h) | null (accepted) |
  | now − 43,200,001 | the message "…12 h away from this laptop's clock…" |
  | now + 43,200,001 | rejected (symmetric) |
  | now | null |

<a id="be-024"></a>

### BE-024 · Detector pick
- Unit · High (5×3) · Automatable · Inferred (the 5 s window is **UR-10**)
- **Input → expected:**
  - No recent rows: `CaptureException` "The Gamma Guard database has no samples yet. Check that Gamma Guard is recording the detector."
  - One serial: that serial.
  - `S1` last at t and `S2` at t − 5001: `S1`.
  - `S2` at t − 5000 (exactly 5 s): refused, "More than one detector is recording (S1, S2). Stop the others to measure."
  - Three serials, two within 5 s: the message lists the newest and the simultaneous ones only.
  - The Gamma Guard status is not Available: "The Gamma Guard database is not available (…)" (`Resolve`).

## 2C. Capture service against a sample source (F15)

**Shared.** Environment: ENV-PURE or ENV-SQLITE. Preconditions: `DeviceCaptureService` with `FakeCaptureClock` (TD-CLOCK) and either a scripted `ISampleSource` or `FakeGammaGuard` (real SQLite, TD-GG). Source: `Frontend/Calculation/Measurement/DeviceCaptureService.cs`. Existing coverage: `DeviceCaptureChecks`.

<a id="be-025"></a>

### BE-025 · Polling, the bounded final read and lookback
- Unit · **Critical** (5×4) · Automatable · Inferred
- **Scenarios:**

  | # | Script | Expected |
  |---|---|---|
  | a | 1 sample/s at T+1000…T+5000, available immediately | 5 accepted samples; ExpectedSamples 5; returns within the 5 s window plus at most one poll; `reportRemaining` starts with 5 s, decreases monotonically and ends at 0 |
  | b | The last sample (T+5000) is written at T+6200 | Included (the final read lasts up to 1.5 s) |
  | c | The last sample is written at T+7000 | Excluded; 4 samples (the window never grows) |
  | d | All 5 arrive by T+5000 | The final read stops early (no delays after the count is reached) |
  | e | A row with ts T+500 was written just before the click, with an id below the baseline but within the 200-row lookback | Included |
  | f | The baseline `MaxSampleId` is null (empty table) | `afterId` is 0; the samples written afterwards are collected |
  | g | 500 rows from another serial are interleaved | Ignored; the `S1` samples are complete |
- **Also:** the poll interval is 250 ms. Assert it through `FakeCaptureClock.Delay` calls, not wall time.

<a id="be-026"></a>

### BE-026 · Time check and cancellation
- Unit · High (5×3) · Automatable · Inferred
- **Expected:**
  - A `LatestTsMs` in seconds: `CaptureException` with the DeviceTime message, before any `ReadSamplesAfter` call.
  - A `LatestTsMs` of null: no time check, and the capture proceeds. Record this as an observation: an empty detector history skips the check.
  - Cancellation during the window or the final read: `OperationCanceledException`, no `CaptureData` returned, and no further source calls.

<a id="be-027"></a>

### BE-027 · The Gamma Guard reader never modifies the vendor DB
- Integration · **Critical (override; 5×3)** · Automatable · Inferred (the real vendor schema and journal mode are **UR-15**)
- **Environment:** ENV-SQLITE. **Source:** `GammaGuardDatabase/GammaGuardDeviceDatabase.cs`, `GammaGuardDatabaseCheck.cs`, `SqliteFileCheck.cs`.
- **Actions:** compute a SHA-256 of the GG file before; run `MaxSampleId`, `ReadSamplesAfter`, `LatestTsMs`, `RecentSerials`, `FindDetector`, `SqliteFileCheck` and discovery. Repeat in rollback-journal mode and in WAL mode.
- **Expected:**
  - The SHA-256 and the `LastWriteTimeUtc` are unchanged.
  - In rollback mode no `-journal`, `-wal` or `-shm` file is created. In WAL mode, record which files appear; read-only opens can create `-shm`.
  - Non-numeric text in `count0` reads as null.
  - A missing `samples` table gives a `CaptureException` (or an error state) with a readable message, not a crash.
  - With a writer holding an exclusive lock for 3 s: Busy, or a `CaptureException`, after about 2 s (the busy timeout).

## 2D. Local file checks and settings (F07, F08, F13)

**Shared.** Environment: ENV-SQLITE + ENV-FS. Source: `Frontend/Calculation/Core-Information/{SqliteFileCheck,LocalStatus}.cs`, `GammaGuardDatabase/GammaGuardDatabaseCheck.cs`, `Calculation/Measurement/CaptureDurationSettings.cs`. Existing coverage: `LocalDeviceStatusChecks`, `CaptureDurationChecks`.

<a id="be-028"></a>

### BE-028 · `SqliteFileCheck` states
- Integration · Medium (3×2) · Automatable · Inferred
- **Input → state:**
  - A missing path: `Missing`, and the file is still absent afterwards.
  - A valid DB: `Available`.
  - A text file renamed `.sqlite`: `Inaccessible`.
  - A DB held with `BEGIN EXCLUSIVE` by another connection: `Busy`.
  - A file with ACL deny-read: `Inaccessible` (Conditional; it needs ACL setup).
- **Every case:** the file bytes are unchanged.

<a id="be-029"></a>

### BE-029 · Gamma Guard discovery
- Integration · High (3×3) · Automatable · Inferred (the real layout is **UR-16**)
- **Input:** a temp root tree (TD-FS-GG):
  - `Root/Apps/Gamma Guard/Database/a.sqlite` (older);
  - `Root/Apps/GammaGuard2/Database/b.sqlite` (newer);
  - `Root/Apps/GammaGuard/Logs/c.sqlite` (not under `Database`);
  - `Root/x/y/z/w/v/GammaGuard/Database/d.sqlite` (too deep);
  - an inaccessible folder.
- **Expected:**
  - The result is `b.sqlite`; `c` and `d` are never chosen; the inaccessible folder is skipped without an error.
  - A configured `gammaGuardDatabasePath` (with environment variables) is used as-is, even if it is missing (state `Missing`).
  - Deleting `b` brings a re-search on the next check, which returns `a`.
  - Retry forces rediscovery.

<a id="be-030"></a>

### BE-030 · `CaptureDurationSettings`
- Unit · Medium (3×2) · Automatable · Inferred (the 2–10 s range is **UR-03**)
- **Load:**
  - A missing file, invalid JSON, `{"captureDurationSeconds":1}`, `11`, `5.5`, `"7"` (a string) or a missing property: 5.
  - `2` gives 2; `10` gives 10.
  - A file locked for reading gives 5.
- **TryParse:** `" 7 "` → 7. `7.0`, `+7`, `-3`, `07x`, `١` and `""` → false. `"010"` → 10 (Inferred: `NumberStyles.None` allows leading zeros).
- **Save:** creates the directory. Afterwards the file holds `{"captureDurationSeconds":N}` and no `.tmp` file remains. A path of null makes it a no-op.

## 2E. App database repository and migrations (F11, F17, F18)

**Shared.** Environment: ENV-SQLITE (a temp folder per test). Preconditions: `SqliteMeasurementRepository(tempPath)`. Data: TD-APPDB. Source: `Frontend/Pages/Measurement/MeasurementRepository.cs`. Existing coverage: `SqliteChecks`, `ProgressMigrationChecks`.

<a id="be-031"></a>

### BE-031 · Session resolution
- Integration · High (5×3) · Automatable · Inferred
- **Expected:**
  - `ResolveSessionAsync(DH-1, metadata M1)` creates one session.
  - A second call with M2 returns the same id with M1's metadata (the stored values win).
  - `FindSessionAsync(DH-9)` returns null and creates nothing.
  - 10 parallel `ResolveSessionAsync(DH-2)` calls on two repository instances over the same file give 1 row and identical ids.
- **Drillhole ID case:** `dh-1` vs `DH-1`. Record whether SQLite's default case-sensitive UNIQUE gives 2 sessions. If it does, raise it against UR-19, because the catalog de-duplicates case-insensitively.

<a id="be-032"></a>

### BE-032 · Atomic save (record + samples + progress)
- Integration · **Critical** (5×4) · Automatable · Inferred
- **Actions:**
  - (a) Save a row with 5 samples and progress 10.25/25.
  - (b) Save the same row again with a new SortOrder and changed values.
  - (c) Force a failure after the record insert. For example, include a sample with a duplicate `source_id` and a conflicting record, or make the DB read-only between statements with a trigger fixture.
- **Expected:**
  - (a) 1 `MeasurementRecords` row, 5 `CaptureSamples` rows and 1 `MeasurementProgress` row with `next_from_cm` 1025.0 and `step_cm` 25.0.
  - (b) Only `SortOrder` changes, the values are unchanged, and the samples are still 5 (insert-or-ignore).
  - (c) No partial state: the record count, the sample count and the progress all equal their pre-save values.

<a id="be-033"></a>

### BE-033 · Session isolation and Capture No uniqueness
- Integration · High (5×2) · Automatable · Inferred
- **Expected:**
  - Saving into session A a row whose `SessionId` is B gives `ArgumentException`, and nothing is written.
  - Inserting a different record with an existing (session, CaptureNo) violates the UNIQUE constraint, and the original is intact.
  - Loading session A never returns B's rows.

<a id="be-034"></a>

### BE-034 · Delete
- Integration · High (5×3) · Automatable · Inferred
- **Expected:**
  - `DeleteAsync(A, 2)` removes the record and its samples in one transaction; the other records and sessions are untouched; `PRAGMA foreign_key_check` is empty.
  - Deleting a missing Capture No: record whether this is a no-op or an error.
  - A failure mid-transaction (a locked DB) leaves both the record and the samples present.

<a id="be-035"></a>

### BE-035 · Round-trip precision
- Integration · High (5×3) · Automatable · Inferred
- **Expected after save → new repository instance → load:**
  - Doubles are bit-identical (`0.1`, `1e-300`, `123456.789012345`, null).
  - `StartTime` keeps its value and offset (+08:00, −05:00, +00:00).
  - The mm values are exact.
  - Progress: `next_from_cm` 1234.5 → 12345 mm; `step_cm` 2.5 → 25 mm; 0.1 cm → 1 mm.
  - Long text (Notes of 10,000 characters, emoji, quotes) round-trips (see BE-046).

<a id="be-036"></a>

### BE-036 · Migrations v1–v4 → v5
- Integration · High (5×3) · Automatable · Inferred · Existing coverage: `SqliteChecks`, `ProgressMigrationChecks`
- **Input:** fixtures TD-APPDB-v1, -v2, -v3 and -v4, each with 2 sessions, 3 records, samples where the version supports them, and progress in mm.
- **Expected:**
  - `user_version` = 5, and every v5 table and index exists.
  - The row counts are preserved.
  - Progress is converted: mm 10250 → `next_from_cm` 1025.0, step 250 → 25.0.
  - `foreign_key_check` is empty; WAL mode is on.
  - The record values are equal to the source.

<a id="be-037"></a>

### BE-037 · Migration refusals and rollback
- Integration · High (5×3) · Automatable · Inferred
- **Expected:**
  - A v4 file with duplicate DrillholeIDs: refused with an error naming the duplicates, and the file's SHA-256 is unchanged.
  - `user_version` = 6: refused as "newer than the app", and the file is unchanged.
  - A failure injected mid-migration (for example a corrupted table in the fixture): the version and data are unchanged.

<a id="be-038"></a>

### BE-038 · Migration idempotence and concurrency
- Integration · Medium (4×2) · Conditional (concurrency is timing-dependent) · Inferred
- **Expected:**
  - Opening a v5 file twice makes no schema or data change.
  - Two repository instances opening the same v4 file concurrently give one consistent v5 result, with no duplicate migration and no error surfaced to the second instance. If it does error, record it and refer it to UR-28.

<a id="be-039"></a>

### BE-039 · Measured drillholes ordering
- Integration · Medium (2×2) · Automatable · Inferred · Existing coverage: `DrillholeListChecks`
- **Expected:** a session last measured at 10:00+08:00 (02:00Z) sorts *after* one at 03:00+00:00, because instants are compared, not local text. Sessions without records are excluded (Inferred; confirm in code during Phase 3).

<a id="be-040"></a>

### BE-040 · `DrillholeCache`
- Integration · Medium (4×2) · Automatable · Inferred · Existing coverage: `DrillholeListChecks`
- **Source:** `Frontend/Api/Core-information/drillholes/DrillholeCache.cs`.
- **Expected:**
  - `ReplaceAsync([a, b])` over `[x]` gives exactly `[a, b]`.
  - `ReplaceAsync([])` keeps `[x]`.
  - A replace failing mid-transaction keeps `[x]`.
  - A corrupt table or file makes `LoadAsync` return empty without throwing.
  - Unicode IDs round-trip.
  - The measurement tables in the same file are untouched.

<a id="be-041"></a>

### BE-041 · Legacy data folder move
- Integration · Medium (4×2) · **Blocked** (`ResolveDefaultPath` is internal and targets the real `%LocalAppData%`; UR-27) · Inferred
- **Expected (when unblocked):**
  - `CT007 Measurement Studio\measurements.db` and its `-wal`/`-shm` files move to `CT007_Measurement_Studio\` exactly once.
  - An IO error mid-move rolls back, leaving the original intact.
  - An existing new folder is not overwritten.

<a id="be-046"></a>

### BE-046 · Text fields: length, Unicode and quoting
- Integration · Medium (3×2) · Automatable · Inferred
- **Input:** Operator/Notes = 10,000 characters; `Ünïcødé 😀`; `a"b'c`; `Robert'); DROP TABLE MeasurementSessions;--`; embedded CR/LF.
- **Expected:** stored and loaded verbatim; every table still exists. Parameterised SQL is already Confirmed in `../analysis/testing-constraints.md` §8, so this is a regression guard.

## 2F. CSV serialisation and file export (F19)

**Shared.** Environment: ENV-PURE (Serialize) or ENV-FS (ExportAsync). Source: `Frontend/Pages/Measurement/CsvExportService.cs`, `MeasurementColumns.cs`. Data: TD-CSV golden files. Existing coverage: Run (`Serialize`).

<a id="be-042"></a>

### BE-042 · CSV structure and formats
- Unit · High (4×3) · Automatable · Inferred (the column set is **UR-04**)
- **Expected:**
  - **Header:** `"No","Capture No","Start Time","End Time","Sample","ts_ms","count0","count0_raw","hdm","hdm_raw","dose_rate","Drillhole","Project","Campaign","Operator","Notes","session_id"`, in exactly this order. There are no From/To/Step, temperature or pressure columns.
  - Every cell is quoted, and the line ending is CRLF, including after the last row.
  - A null device value gives `""`.
  - Doubles use `R` (`0.1` → `"0.1"`; `1E-300`) under the `fr-FR` culture, with no comma decimal.
  - `StartTime` uses the `O` format with its offset.
  - An empty row list gives the header only.
  - The rows keep the order they were given in.
- **Golden-file comparison:** byte-for-byte, excluding the BOM, which BE-044 covers.

<a id="be-043"></a>

### BE-043 · CSV injection and session mixing
- Security/Unit · High (4×3) · Automatable · Inferred, with tab/CR prefixes **Unresolved** (UR-18)
- **Input:** Notes/Operator/Drillhole = `=1+1`, `+A`, `-2`, `@SUM(A1)`, `  =x` (leading spaces), `a,b`, `line1\r\nline2`, `"q"`, `\t=1`, `\r=1`.
- **Expected:**
  - The first five values are written as `"'=1+1"`, `"'+A"`, `"'-2"`, `"'@SUM(A1)"` and `"'  =x"`.
  - Commas and line breaks stay inside one quoted cell; `"` is doubled.
  - `\t=1` and `\r=1`: the current behaviour isn't neutralised (Inferred). The expected treatment is pending UR-18.
  - The measurement cells are never prefixed.
  - Rows from two sessions give `ArgumentException`, and no output.

<a id="be-044"></a>

### BE-044 · `ExportAsync` file handling
- Integration · Medium (4×2) · Conditional (`SaveFileDialog` isn't injectable; it needs UI automation or a manual run, MAN-013) · Inferred
- **Expected:**
  - The file starts with the UTF-8 BOM `EF BB BF` and is otherwise identical to `Serialize`.
  - An existing target is overwritten.
  - After success, failure or cancellation, no `*.tmp` remains beside the target.
  - A read-only folder raises an exception, which becomes the FE-039 error toast.
  - The default file name matches `CT007-measurements-\d{8}-\d{6}\.csv`.

## 2G. Support tooling (F23)

<a id="be-045"></a>

### BE-045 · `tools/check-db.ps1` against a v5 database
- Integration · High (2×5) · Conditional (ENV-PS, Windows PowerShell 5.1 with `winsqlite3.dll`) · **Unresolved** (UR-11: is the script maintained?)
- **Preconditions:** a copy of TD-APPDB-v5 in a temp folder; the script pointed at it (check how it locates the DB first).
- **Expected:**
  - The script finishes with exit code 0, and the version line expects 5.
  - The session summary prints next-from and step values.
  - The DB's SHA-256 is unchanged.
- **Current (Inferred, confirmed by reading lines 142 and 144):** the script expects version 4 and selects `p.next_from`/`p.step`, which don't exist in v5, so the session summary is expected to fail.
