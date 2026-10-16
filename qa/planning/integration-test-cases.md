# Integration and End-to-End Test Cases (INT, E2E)

Cross-layer scenarios only. Each one checks something that single-layer tests in [frontend-test-cases.md](frontend-test-cases.md) and [backend-test-cases.md](backend-test-cases.md) can't: the wiring, contract alignment, persistence across instances, and error propagation. The conventions are the same as those documents. Expectation status is **Inferred** unless it says **Unresolved**.

---

## A. Frontend ↔ HTTP backend (F01, F02, F05)

**Shared.** Environment: ENV-API + ENV-STA. Preconditions: an in-memory backend host (`WebApplicationFactory<Program>`) with a fake `IDrillholeRepository`. **Its `Server.CreateHandler()` is passed to the frontend `ApiClient` as the `HttpMessageHandler`**, so the real client talks to the real pipeline with no sockets. Source: `Frontend/Api/**`, `Frontend/Pages/CoreInformation/CoreInformationViewModel.cs`, `Backend/Controllers/CoreInformation/Drillhole/*`, `Backend/Models/CoreInformation/Drillhole/DrillholeResponse.cs`.

<a id="int-001"></a>

### INT-001 · Field mapping from the SQL projection to the form
- Integration · High (4×3) · Conditional · Inferred
- **Input:** the fake repository returns `("DH-1","PRJ-A","2024")` and `("DH-2","","")`.
- **Actions:** load the drillholes in `CoreInformationViewModel`, and pick `DH-1`.
- **Expected:**
  - `DrillholeId` = `DH-1`.
  - `DrillholeName` (labelled Project) = `PRJ-A`.
  - `ProjectArea` (labelled Campaign) = `2024`.
  - After Save & Continue, the SQLite session has `DrillholeName=PRJ-A` and `Project=2024`.
  - The CSV has `Project=PRJ-A` and `Campaign=2024`.
  - `DH-2` shows empty Project and Campaign.
- **Evidence:** `../analysis/integration-map.md` §3 (labels renamed, storage names not).

<a id="int-002"></a>

### INT-002 · A backend 503 reaches the user as a refresh error, and the backend stays Online
- Integration · High (3×3) · Conditional · Inferred
- **Input:** the fake repository throws a `SqlException`, or a test-only middleware returns 503 ProblemDetails if a `SqlException` can't be constructed.
- **Expected:**
  - The status is `HttpError`; the message contains "Backend error 503".
  - The sidebar state is **Online** (the server answered).
  - The cached list is shown and `CanRetryDrillholes` is true.
  - No ProblemDetails text is shown verbatim.

<a id="int-003"></a>

### INT-003 · Offline start and recovery
- Integration · High (4×3) · Conditional · Inferred
- **Preconditions:** the cache contains 3 drillholes; the handler initially throws `HttpRequestException`. Use a switchable wrapper around the in-memory handler.
- **Actions:** start; wait for Offline; switch the handler to the in-memory server; wait one status interval (100 ms in the test settings).
- **Expected:**
  - The 3 cached entries are shown immediately; Offline; exactly one Warning toast.
  - On recovery: Online, an Online announcement, exactly one drillhole reload (`CameOnline`), the catalog and cache replaced with the server list, and the pick preserved if it still exists.

<a id="int-004"></a>

### INT-004 · Server slower than the client timeout
- Integration · High (3×3) · Conditional · Inferred
- **Input:** the fake repository delays 3 s; `requestTimeoutSeconds` = 1.
- **Expected:** `Unreachable` with "did not respond in time"; Offline. The server-side request is not cancelled by the client token (`CancellationToken.None`); record this as an observation. A later fast response recovers as in INT-003.

<a id="int-005"></a>

### INT-005 · Contract drift guard between the two `DrillholeResponse` definitions
- Integration (contract) · High (4×3) · Automatable · Inferred
- **Actions:**
  1. Serialise the backend record with ASP.NET's default options and deserialise it into the frontend record with `JsonSerializerDefaults.Web`.
  2. Compare the property sets by reflection.
  3. Compare against the BE-008 OpenAPI snapshot.
- **Expected:** every backend property maps to a frontend property of a compatible type. Renaming either side fails the test. An extra unknown field (`"extra":1`) is ignored by the frontend.
- **Evidence:** `../analysis/integration-map.md` §7.4.

<a id="int-006"></a>

### INT-006 · Server list → cache → offline restart
- Integration · Medium (4×2) · Automatable · Inferred · Existing coverage: `DrillholeListChecks`
- **Environment:** ENV-STA + ENV-SQLITE (`StubBackend` is enough here).
- **Actions:** load 5 drillholes online; dispose; create a new ViewModel on the same temp DB with the backend offline.
- **Expected:** the same 5 entries, in the same display order, with no network call succeeding.

---

## B. Capture pipeline and persistence (F11–F18)

**Shared.** Environment: ENV-STA + ENV-SQLITE. Preconditions:
- `MeasurementViewModel` (through the `MainViewModel` test constructor) with the **real** `DeviceCaptureService`, `GammaGuardCaptureDevices` and `SqliteMeasurementRepository`;
- `FakeGammaGuard` (TD-GG), a real SQLite file;
- `FakeCaptureClock` (TD-CLOCK);
- `FakeDialogs` and `FakeExport`;
- the Gamma Guard status forced to Available at the fake path.

Source: `Frontend/Pages/Measurement/*`, `Frontend/Calculation/**`.

<a id="int-007"></a>

### INT-007 · One capture gives a correct averaged, persisted, reloadable row
- Integration · **Critical** (5×4) · Automatable · Inferred · Existing coverage: `DeviceCaptureChecks` (partial)
- **Input:** a detector `S1` with samples at T+1000…T+5000: count0 = 10, 20, 30, 40, 50; dose_rate = 0.1 … 0.5; one null temperature. From 10.00 m, step 25 cm, duration 5 s.
- **Expected:**
  - The displayed row has From 10.00, To 10.25, count0 30 and dose_rate 0.3 (±1e-12); temperature is the mean of the 4 valid values; Sample is 5; `ts_ms` = T+1000.
  - The DB has 1 record (`from`=10000, `to`=10250, `step`=250, `duration_ms`=5000, `expected_samples`=5, `device_serial`=S1), 5 `CaptureSamples` and progress 1025.0/25.0.
  - A new repository and ViewModel on the same file show the identical row and next From 10.25.
  - The Gamma Guard file's SHA-256 is unchanged (BE-027).

<a id="int-008"></a>

### INT-008 · Capture while Gamma Guard is writing concurrently
- Integration · **Critical** (5×4) · Conditional (a writer thread or process; WAL; real time, Timing trait) · Inferred (the real journal mode is **UR-15**)
- **Preconditions:** a writer inserts 1 row/s for `S1` (with `ts_ms` = its own clock) into a WAL-mode GG DB. The real `SystemCaptureClock` is used.
- **Actions:** run 10 consecutive 5 s captures.
- **Expected:**
  - Every capture accepts 4–5 samples, all with ts in the window. Report the distribution; 5 is expected when the writer is timely.
  - The writer is never blocked for more than 2 s, and never sees `SQLITE_BUSY`.
  - No duplicates.
  - The app never writes to the GG DB (checked with a writer-side change counter or `sqlite3_changes`).
- **Repeat** in rollback-journal mode, and record any Busy errors.

<a id="int-009"></a>

### INT-009 · Gamma Guard locked or unavailable during a capture
- Integration · High (4×3) · Automatable · Inferred
- **Actions:**
  - (a) Hold an exclusive lock on the GG DB for 5 s, starting before the trigger.
  - (b) Delete or rename the GG file between Start and trigger.
- **Expected:** an Error toast with a readable reason (busy/locked, or not available); no row; no depth advance; the state returns to Ready; the next capture succeeds after the lock is released or the file restored (Retry may be needed for b).

<a id="int-010"></a>

### INT-010 · App DB write failure, then recovery, with no duplicates
- Integration · High (5×3) · Conditional (lock or read-only toggling on a real file) · Inferred
- **Actions:** take an exclusive lock on `measurements.db` from a second connection; capture 2 rows; release; capture a 3rd.
- **Expected:**
  - While locked: the Error toast from FE-035, and 2 rows visible.
  - After the 3rd capture: the DB has exactly 3 records with Capture No 1–3, no duplicates, and progress = the 3rd row's `to`.
  - The DB-status row showed Busy while it was locked.

<a id="int-011"></a>

### INT-011 · Reopening a measured drillhole restores its rows and progress
- Integration · High (5×3) · Automatable · Inferred
- **Preconditions:** a stored session `DH-1` with 3 rows (unsorted SortOrder) and progress 10.75/25.
- **Actions:** pick `DH-1` and Save & Continue.
- **Expected:** the rows are displayed sorted by From and numbered 1–3; Starting Depth `10.75` and Step `25`; `DH-1` is first under Measured drillholes; the next capture gets Capture No 4 and From 10.75.

<a id="int-012"></a>

### INT-012 · Fast drillhole switching never mixes sessions
- Integration · High (5×3) · Automatable · Inferred
- **Actions:** capture on `DH-1` with a 300 ms delay injected into the repository. Before the save completes, Stop, switch to `DH-2`, Save & Continue, Start, and capture.
- **Expected:** the DB has the `DH-1` row only in session `DH-1` and the `DH-2` row only in session `DH-2`. Each session's progress is its own. The CSV for each contains only its own row.

<a id="int-013"></a>

### INT-013 · Delete, persist and reload
- Integration · Medium (4×2) · Automatable · Inferred
- **Actions:** capture 3 rows; delete row 2 (confirmed); reload in a new instance; export.
- **Expected:** 2 records and their samples remain, with Capture No 1 and 3; the deleted record's `CaptureSamples` are gone; numbering is 1–2; the next capture is Capture No 4; the CSV has 2 rows.

<a id="int-014"></a>

### INT-014 · Displayed, stored and exported values agree
- Integration · High (5×3) · Automatable · Inferred
- **Input:** a session with a complete row, an incomplete row (3 of 5), and a row whose dose_rate is null for every sample.
- **Expected:** for each row and field:
  - CSV value == the stored value (round-trip format).
  - The displayed value == the stored value formatted to ≤ 2 decimals.
  - The incomplete row shows "3 of 5" in the table, and its CSV `Sample` is `3`. Record the exact CSV value.
  - The null field is empty in both the table and the CSV.
- **Golden file:** TD-CSV-SESSION.

<a id="int-015"></a>

### INT-015 · Device Status and capture agree on Gamma Guard availability
- Integration · High (3×3) · Automatable · Inferred
- **Actions:** start with no GG DB under the roots; trigger a capture; create the GG DB; Retry; capture.
- **Expected:** the first capture is refused with "The Gamma Guard database is not available (…)" and the status row shows not found. After Retry: Available, and the capture succeeds.

<a id="int-016"></a>

### INT-016 · Measurement DB status reflects a real lock on the app DB
- Integration · Medium (2×2) · Automatable · Inferred
- **Expected:** an exclusive lock from another connection gives the DB row Busy within one 5 s check interval, plus one announcement. Releasing it gives Available plus one announcement.

<a id="int-017"></a>

### INT-017 · Upgrade on start-up, then continued measuring
- Integration · High (5×3) · Conditional (the app DB path must be injected through the test constructor; the default constructor uses the real profile) · Inferred
- **Preconditions:** the TD-APPDB-v4 fixture copied to the temp path.
- **Actions:** construct the `MainViewModel` test instance; open the `DH-1` session from the fixture; capture.
- **Expected:** the DB is migrated to v5; the old rows are shown with the correct depths (mm→cm progress conversion); the new row gets Capture No = max + 1 and From = the migrated next-from.

---

## C. End-to-end (full application)

**Shared.** Environment: ENV-UIA. The real `Frontend.exe` runs under a **dedicated Windows test account or VM**, because `%LocalAppData%` can't be redirected (UR-27). The settings files sit beside a copy of the exe:
- `api-settings.json` points to a local backend started with test configuration and a fake repository or ENV-SQL;
- `device-settings.json` sets `gammaGuardDatabasePath` to a simulated GG DB with a 1 Hz writer (TD-GG-WRITER).

Right-clicks come from UI automation. All E2E cases are **Conditional** and run in full regression only.

<a id="e2e-001"></a>

### E2E-001 · Main journey
- E2E · High (5×3) · Conditional · Inferred
- **Actions:**
  1. Launch.
  2. Type `DH-0` in the drillhole box and pick `DH-001`.
  3. Save & Continue.
  4. Enter Starting Depth `10.00` and Step `25`; click Start.
  5. Right-click the table area 3 times, waiting for each capture to finish (about 6.5 s).
  6. Export to a temp folder.
- **Expected:**
  - The Online dot shows.
  - 3 rows: 10.00–10.25, 10.25–10.50 and 10.50–10.75, each with Sample 5 (or a warning toast if the writer lagged).
  - Starting Depth shows `10.75`.
  - The CSV has the header from BE-042 and 3 data rows with Drillhole `DH-001`.
  - No context menu appeared during the captures.

<a id="e2e-002"></a>

### E2E-002 · Restart persistence
- E2E · High (5×3) · Conditional · Inferred
- **Actions:** after E2E-001, close the app normally, relaunch, pick `DH-001`, and Save & Continue.
- **Expected:** the same 3 rows with identical values; Starting Depth `10.75` and Step `25`; `DH-001` first under Measured drillholes.

<a id="e2e-003"></a>

### E2E-003 · Offline operation
- E2E · High (4×3) · Conditional · Inferred
- **Preconditions:** the cache was populated in an earlier run, and the backend isn't started.
- **Expected:** the Offline indicator; the cached drillholes are selectable; one capture saves successfully; starting the backend later turns it Online within 2 status intervals and refreshes the list.

<a id="e2e-004"></a>

### E2E-004 · Delete through the real confirmation dialog
- E2E · Medium (3×2) · Conditional · Inferred
- **Actions:** click Delete on row 2. First choose Cancel in `ConfirmationWindow`, then repeat and confirm.
- **Expected:** Cancel leaves 3 rows. Confirm leaves 2 rows, renumbered, and shows the "Measurement deleted." toast. After a restart there are still 2 rows.
