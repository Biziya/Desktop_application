# Test Data and Environment Requirements

These are planning requirements only. Nothing was created or set up in Phase 2. The IDs are referenced from every test-case document.

## 1. Environments

| ID | Description | Needed by | Notes |
|---|---|---|---|
| ENV-PURE | A plain .NET 10 test process on Windows | Unit cases | The test project must target `net10.0-windows10.0.19041.0` to reference Frontend (UR-24) |
| ENV-STA | The same as ENV-PURE, plus an STA thread with a WPF `Dispatcher` and `DispatcherSynchronizationContext` | ViewModel and component cases | Reuse the pattern in `SmokeTests/Program.cs` `Main`. Never use the default `MainViewModel()` constructor; it touches `%LocalAppData%`, Bluetooth, the network, and starts loops |
| ENV-SQLITE | A unique temp folder per test (`%TEMP%\ct007-tests\<guid>\`) for SQLite files | Repository, migration, cache, GG reader | Delete the folder afterwards, including `-wal`/`-shm` files. Today's harness doesn't clean up (`../analysis/testing-infrastructure.md` §2) |
| ENV-FS | A controlled folder tree under the test temp folder | GG discovery, CSV file export, settings files | For ACL cases, create the deny entries on the temp folder only, and remove them in teardown |
| ENV-API | An in-memory ASP.NET host (`Microsoft.AspNetCore.Mvc.Testing`) with `IDrillholeRepository` replaced; configuration overridden so that **no real connection string** is present | BE Part 1, INT-001…INT-005 | `Program` accessibility may need `public partial class Program {}` (a production change, UR-27) |
| ENV-SQL | A disposable SQL Server (LocalDB or a Docker `mssql/server` container) with the database name `CT007_Test` | BE-006, BE-007 (and optionally E2E) | **Never** the corporate server named in `Backend/appsettings.json`. Real column types are Unknown, so use NVARCHAR(100) and record that assumption (UR-25) |
| ENV-PS | Windows PowerShell 5.1 with `winsqlite3.dll` (built into Windows) | BE-045 | Runs the script against a fixture copy only |
| ENV-UIA | A Windows 10/11 desktop session under a **dedicated test account or VM**; UI-automation library (FlaUI/UIA3 suggested); `Frontend.exe` copied to a test folder with test `api-settings.json` and `device-settings.json` | E2E, MAN-006/008/009/013 | `%LocalAppData%` can't be redirected for the real exe (Inferred, UR-27). Reset `CT007_Measurement_Studio\` before each run |
| ENV-HW | ENV-UIA plus a CT007-M detector (or two, for MAN-004), Gamma Guard installed and recording, a Bluetooth adapter, and a check source for MAN-002 | MAN-001…MAN-005, MAN-014 | Book hardware time. Record the firmware and Gamma Guard versions |

**Clock and time zone control.** Use `FakeCaptureClock` for capture logic. Run selected suites under `TZ` overrides: on Windows, use `tzutil` on a VM, or run the formatting functions with an injected `TimeZoneInfo` if one is available. Use cultures `InvariantCulture`, `fr-FR`, `ru-RU` and `en-US` by setting `CultureInfo.CurrentCulture` for each test.

**Determinism.**
- Use fixed seeds for any generated data (BE-019 property test, large catalogs).
- `Random.Shared` (demo `ts_ms`) is not seedable. Assert ranges only (FE-049).
- Tag real-time tests `Timing` and keep them out of the fast stage.

## 2. Test data and fixtures

| ID | Content | Format | Used by |
|---|---|---|---|
| TD-HTTP | Canned responses: a valid drillhole array (3 items); `[]`; 404/500/503 ProblemDetails; malformed JSON variants (FE-004 table); a delayed response; refused (throw); probe responses (HEAD 404) | In-code stub handler, adapted from `StubBackend` | FE-001…FE-012, INT-003/004 |
| TD-DH-SMALL | 10 drillholes with mixed case, duplicates (`dh-2`/`DH-2`), whitespace, empty/null IDs, null fields | JSON + C# builder | FE-013/014/015 |
| TD-DH-UNICODE | IDs from FE-018 (Cyrillic, CJK, `/`, `'`, `"`, umlauts) | JSON | FE-018, BE-040 |
| TD-DH-LARGE | 20,000 and 100,000 generated IDs `DH-000001…`, seed 42 | Generator | FE-016/017, BE-007 |
| TD-SQL | `ZOOVCH_COLLAR_LIST` DDL plus the BE-006 rows; a 50,000-row variant | SQL script | BE-006/007 |
| TD-GG | A fake Gamma Guard DB builder: the `samples` and `detector_status` schema per `../analysis/integration-map.md` §3; helpers for one serial at 1 Hz, two serials, late rows, duplicate ids, duplicate (session, seq, ts), null ts, non-numeric text, seconds-based ts, an empty table, a missing table; rollback and WAL journal modes | C# builder writing real SQLite (port `FakeGammaGuard`) | BE-020…BE-027, INT-007…INT-009/015 |
| TD-GG-WRITER | A background writer inserting 1 row/s with `ts_ms = UtcNow`, configurable serials | Console helper or test thread | INT-008, E2E, MAN-016 |
| TD-CLOCK | `FakeCaptureClock` (`NowMs` set and advanced by `Delay`; frozen mode) | C# | BE-025/026, INT-007 |
| TD-APPDB-v1…v4 | One fixture DB per historical schema version, each with 2 sessions, 3 records, samples where supported, progress in mm; plus a v4 with duplicate DrillholeIDs, a v6 (future) and a corrupted file | Binary `.db` files under `Tests/Fixtures/` (to be created in Phase 3 from the DDL in `MeasurementRepository.cs`'s migration code, or built by SQL scripts at test time) | BE-036…BE-038, INT-017 |
| TD-APPDB-v5 | A current-schema DB with 2 sessions, 5 records, progress | Generated by the repository at test time | BE-045, E2E reset baselines |
| TD-FS-GG | The folder tree from BE-029 | Generated at test time | BE-029, FE-044, INT-015 |
| TD-SET | Settings files: valid, missing, invalid JSON, out-of-range, environment-variable path; for `api-settings.json`, `device-settings.json` and `capture-settings.json` | JSON | FE-005, FE-045, BE-030, E2E |
| TD-BT | Fake Bluetooth device lists and reader outcomes (connected, paired, timeout, throw, duplicate address) | C# (`FakeBluetoothReader`) | FE-043 |
| TD-CSV | Golden CSV files: one simple session; TD-CSV-SESSION (complete, incomplete and null-field rows); the injection set (BE-043) | `.csv`, compared byte-for-byte after stripping the BOM | BE-042/043, INT-014 |
| TD-TEXT | Long, Unicode, quote and SQL-like strings (BE-046) | C# constants | BE-046, MAN-012 |

**Sensitive data.** No fixture may contain the real connection string, real credentials, or production drillhole data. Use synthetic IDs only. If real data is ever needed (UR-08), get approval and anonymise it.

## 3. Isolation rules

1. Each test owns a fresh temp folder (ENV-SQLITE/ENV-FS). Nothing is shared between tests except read-only fixtures, which are copied before use, never opened in place, since SQLite may write `-wal`/`-shm` even on read.
2. Never construct `MainViewModel()` (the default constructor) or call `SqliteMeasurementRepository.DefaultPath` in automated tests. Both resolve the real user profile, and the latter may move legacy folders (`../analysis/testing-constraints.md` §7).
3. Dispose ViewModels and services in teardown. Wait for the persistence queue with bounded polling (`Until`, 3 s), not `Thread.Sleep`.
4. Backend tests get a fresh `WebApplicationFactory` per class. `SqliteDatabaseRepository` is a singleton with a 1 min cache, so BE-014 needs its own factory.
5. Tests that change culture or time zone restore them in `finally`.
6. Hardware and UI runs use the dedicated account. Back up `CT007_Measurement_Studio\` before a session and restore it afterwards.

## 4. Cleanup

- Delete the temp folders, including `-wal`, `-shm`, `*.tmp` and exported CSVs. Before deleting a SQLite file, close every connection and call `SqliteConnection.ClearAllPools()`, or the files stay locked.
- Remove any ACL deny entries before deleting.
- Stop background writers (TD-GG-WRITER) and in-memory hosts.
- Restore the culture, the time zone and the settings files copied beside the test exe.
- For the SQL test instance, drop the `CT007_Test` database after the run.
