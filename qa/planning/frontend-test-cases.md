# Frontend Test Cases (FE)

Scope: presentation, UI state, ViewModels, the API client and display formatting in `Frontend/`. The non-UI business and data classes of `Frontend/` are in [backend-test-cases.md](backend-test-cases.md) (see the layer convention in [test-plan.md](test-plan.md) §2.1).

**Conventions**
- Each group's **Shared** line gives the environment, preconditions and source paths for every case in it. A case only states what differs.
- Environments (ENV-*) and test data (TD-*) are defined in [test-data-requirements.md](test-data-requirements.md).
- *Existing coverage* names the `SmokeTests/Program.cs` section to port or reuse.
- *Status* is the expectation status. Every expectation here is **Inferred** from code unless it says **Unresolved** (see [unresolved-requirements.md](unresolved-requirements.md)).
- Toast texts are quoted from source. A toast "kind" is Error, Warning, Success or Information.
- "No backend path" means the case touches no `Backend/` code.

---

## A. API client and backend connectivity (F01 client side, F05)

**Shared.** Environment: ENV-STA. Preconditions: `ApiClient` is built with a `StubBackend`-style `HttpMessageHandler` (TD-HTTP) and base URL `http://localhost:5002`. Source: `Frontend/Api/ApiClient.cs`, `ApiResult.cs`, `BackendConnection.cs`, `ApiSettings.cs`, `Api/Core-information/drillholes/DrillholesClient.cs`. Backend contract: `Backend/Models/CoreInformation/Drillhole/DrillholeResponse.cs`.

<a id="fe-001"></a>

### FE-001 · `GetAsync` maps a 200 JSON array to `Ok`
- Unit · Medium (3×2) · Automatable · Inferred · Existing coverage: Run (refresh)
- **Objective:** a valid response is deserialised and reported as reachable.
- **Input:** 200 `[{"holeId":"DH-001","projectCode":"P1","campaign":"2024"}]`, `Content-Type: application/json`.
- **Actions:** call `DrillholesClient.GetAsync`.
- **Expected:** the status is `Ok`; there is 1 item with `HoleId="DH-001"`, `ProjectCode="P1"` and `Campaign="2024"`; `Reached(true)` is raised exactly once.
- **Evidence:** `ApiClient.GetAsync<T>`, `JsonSerializerDefaults.Web` (`../analysis/frontend-analysis.md` §6).

<a id="fe-002"></a>

### FE-002 · Non-success status maps to `HttpError` with the code in the message
- Unit · Medium (3×2) · Automatable · Inferred · Existing coverage: Run (HTTP 500)
- **Input:** a data table of status codes: 404, 500 and 503 (with a ProblemDetails body).
- **Expected:**
  - The status is `HttpError`, and the message contains "Backend error {code}".
  - The ProblemDetails body is ignored.
  - `Reached(true)` is raised, because the server answered.
- **Evidence:** `../analysis/integration-map.md` §5.

<a id="fe-003"></a>

### FE-003 · Connection refused and timeout map to `Unreachable`
- Unit · High (4×3) · Automatable · Inferred · Existing coverage: Run (offline)
- **Input:**
  - (a) The handler throws `HttpRequestException`.
  - (b) The handler delays past `requestTimeoutSeconds` (use 1 s in the test settings).
- **Expected:**
  - Both cases return `Unreachable` and raise `Reached(false)`.
  - In (b) the message contains "did not respond in time".
  - No exception reaches the caller.
- **Evidence:** `../analysis/integration-map.md` §5–6 (requests use `CancellationToken.None`; the timeout comes from `HttpClient.Timeout`).

<a id="fe-004"></a>

### FE-004 · Malformed or unexpected payloads map to `InvalidResponse`
- Unit · Medium (3×2) · Automatable · Inferred
- **Input:** a data table: `{not json`, `{"holeId":"x"}` (an object instead of an array), the literal `null`, an empty body, and HTML with a 200 status.
- **Expected:** every input gives `InvalidResponse` with the message "Backend returned unexpected data."; `Reached(true)` is raised.
  - For `null`: if the client treats it as an empty list, record the actual result and raise it against UR-04/UR-21. Don't make the test pass silently.

<a id="fe-005"></a>

### FE-005 · `ApiSettings.Load` falls back to defaults
- Unit · Medium (3×2) · Conditional (needs a way to point `Load` at a temp file; Phase 3 must check its signature) · Inferred · Existing coverage: none
- **Input:** a data table:
  - the file is missing;
  - the JSON is invalid;
  - `requestTimeoutSeconds` is 0, -1 or `"abc"`;
  - `baseUrl` is not absolute;
  - the values are valid (`http://host:6000`, 10, 2).
- **Expected:** the invalid cases give the defaults `http://localhost:5002`, 5 s and 5 s, and the valid file is read verbatim. No exception is thrown.

<a id="fe-006"></a>

### FE-006 · `BackendConnection` polls on the interval, never overlaps, and treats any HTTP answer as Online
- Component · High (3×3) · Automatable · Inferred · Existing coverage: Run (polling count, overlap reuse)
- **Preconditions:** status interval 100 ms (test settings); a stub with a controllable HEAD delay.
- **Actions:**
  1. Run for 1 s with immediate 404 answers.
  2. Hold one probe for 500 ms and call `CheckNowAsync` 3 times meanwhile.
  3. Switch the stub to refused.
- **Expected:**
  1. About 10 probes (±3), all of them `HEAD /`, and none to `/api/core-information/bluetooth-device` or `/api/core-information/sqlite-database`. The state goes Checking → Online.
  2. Only one probe is in flight; all callers await the same check.
  3. The state is Offline after the next probe.
- **Note:** treating any HTTP answer as Online is the current design. Whether a foreign service on port 5002 should count is UR-13.

<a id="fe-007"></a>

### FE-007 · `CameOnline` reloads the drillholes only after a failed load
- Component · High (3×3) · Automatable · Inferred · Existing coverage: Run (recovery)
- **Actions:**
  - (a) The first load fails (refused), then the stub goes online.
  - (b) The first load succeeds, then the probe goes Offline → Online.
- **Expected:** (a) exactly one extra `GET /api/core-information/drillholes`, after which the catalog updates; (b) no extra GET.

<a id="fe-008"></a>

### FE-008 · The probe loop stops after dispose
- Component · Medium (2×2) · Automatable · Inferred
- **Actions:** start a `MainViewModel` through the injectable constructor, dispose it, and wait 3× the interval.
- **Expected:** no probe and no Bluetooth or DB status check is made after dispose; nothing is thrown on the dispatcher.

---

## B. Drillhole list, cache and search (F01–F03)

**Shared.** Environment: ENV-STA + ENV-SQLITE. Preconditions: `CoreInformationViewModel` with `StubBackend`, a `DrillholeCache` in a temp `measurements.db`, `searchDelay` = 0 or 150 ms as stated, and `MemoryRepository` for the measured drillholes. Data: TD-DH. Source: `Frontend/Pages/CoreInformation/{CoreInformationViewModel,DrillholeCatalog,DrillholeOption}.cs`, `Api/Core-information/drillholes/DrillholeCache.cs`, `Shared/Infrastructure/BulkObservableCollection.cs`. Backend: `GET /api/core-information/drillholes`.

<a id="fe-009"></a>

### FE-009 · The cached list shows first, then the server list replaces it
- Component · Medium (4×2) · Automatable · Inferred · Existing coverage: `DrillholeListChecks`
- **Preconditions:** the cache holds `DH-OLD`; the server returns `DH-001, DH-002` after a 300 ms delay.
- **Expected:**
  - Before the response, the options contain only `DH-OLD`.
  - After it, they contain `DH-001` and `DH-002`, and `DH-OLD` is gone.
  - The cache table holds exactly the two server rows.
  - `DrillholesMessage` is empty and `CanRetryDrillholes` is false.

<a id="fe-010"></a>

### FE-010 · A failed refresh keeps the list, the selection and the form
- Component · High (4×3) · Automatable · Inferred · Existing coverage: Run (HTTP 500, offline)
- **Preconditions:** a catalog is loaded; `DH-001` is picked; Operator is "A" and Notes is "N".
- **Actions:** Retry while the stub returns 503, then refused.
- **Expected:**
  - The options, the pick, Project/Campaign, Operator and Notes are unchanged.
  - With 503, the message starts with "Showing the saved drillhole list. Could not refresh:" and contains "503".
  - `CanRetryDrillholes` is true; the cache is unchanged.

<a id="fe-011"></a>

### FE-011 · An empty server list never replaces a non-empty catalog
- Component · Medium (4×2) · Automatable · Inferred · Existing coverage: `DrillholeListChecks`
- **Input:** a cache with 3 entries; the server returns 200 `[]`.
- **Expected:** the 3 entries stay displayed and in the cache, and the message says the backend "returned no drillholes; showing the saved list".
- **Also:** an empty cache plus `[]` shows an empty list with a message (record the actual text).

<a id="fe-012"></a>

### FE-012 · Overlapping load requests reuse the running load
- Component · Medium (2×2) · Automatable · Inferred
- **Actions:** while one GET is held, trigger Retry twice and `CameOnline` once.
- **Expected:** the stub counts exactly 1 drillhole GET; all callers complete after it is released.

<a id="fe-013"></a>

### FE-013 · Catalog building cleans the API data
- Unit · Medium (4×2) · Automatable · Inferred · Existing coverage: `DrillholeListChecks`
- **Input to `DrillholeCatalog.Build`:**
  - `""`, `"   "` and `null` HoleIds;
  - `" DH-1 "`;
  - both `"dh-2"` and `"DH-2"`;
  - null ProjectCode/Campaign.
- **Expected:**
  - Empty or whitespace IDs are dropped.
  - `" DH-1 "` becomes `"DH-1"`.
  - Only one of `dh-2`/`DH-2` remains. Record which one; the expected rule is the first occurrence (Inferred).
  - Null fields become `""`.

<a id="fe-014"></a>

### FE-014 · Search ranking and grouping
- Unit · Medium (3×2) · Automatable · Inferred · Existing coverage: `DrillholeSelectionChecks`
- **Input:** the catalog `AB1, XAB, AB, ab2, ZZ`; measured `XAB` (2026-10-01) and `ZZ` (2026-10-05); the query `ab`.
- **Expected:**
  - The closest match is `AB` (exact, case-insensitive), then the prefix matches, then the partial matches.
  - The "Measured drillholes" group lists `XAB` only (`ZZ` doesn't match the query). In the empty-query view the group order is `ZZ` before `XAB` (newest first).
  - The "All drillholes" group is A–Z.
  - A measured ID missing from the catalog is not shown.

<a id="fe-015"></a>

### FE-015 · Typing never picks; a mismatch clears the pick; the rebuild echo is ignored
- Component · High (4×3) · Automatable · Inferred · Existing coverage: `DrillholeSelectionChecks`, `AutofillAndDepthInputChecks`
- **Actions:**
  1. Type `DH-001` exactly without pressing Enter.
  2. Pick `DH-001`, then type `DH-0019`.
  3. Simulate a ComboBox `SelectedItem` echo while the options are being rebuilt.
- **Expected:**
  1. `DrillholeId` stays empty, so Save fails as in FE-019.
  2. The pick is cleared and Project and Campaign are emptied.
  3. The pick is unchanged.
- **Why High:** a wrong implicit pick files measurements under the wrong drillhole.

<a id="fe-016"></a>

### FE-016 · Debounce and stale filter results
- Component · High (3×3) · Automatable · Inferred · Existing coverage: `DropdownPerformanceChecks`
- **Preconditions:** `searchDelay` is 150 ms; the catalog has 20,000 entries (TD-DH-LARGE).
- **Actions:** type `D`, `DH`, `DH-1`, `DH-19` at 30 ms intervals, then await `WhenOptionsUpdated()`.
- **Expected:**
  - Filtering runs at most twice (the first keystroke may run, and the final one does).
  - The final options match `DH-19` only.
  - No intermediate result is applied after the final one (optionsVersion).

<a id="fe-017"></a>

### FE-017 · Dropdown responsiveness with a large catalog
- Performance · Medium (3×2) · Conditional (a dedicated, quiet machine) · **Unresolved** (UR-21: no target)
- **Input:** 20,000 entries (and 100,000 as a stress level).
- **Actions:** measure the UI-thread time per keystroke over 20 keystrokes.
- **Expected:** a proposed value of ≤ 50 ms at the 95th percentile, pending UR-21. Report the actual numbers. Tag as Timing.

<a id="fe-018"></a>

### FE-018 · Unicode and special characters in drillhole data
- Component · Medium (3×2) · Automatable · Inferred
- **Input:** the IDs `ДН-001`, `孔-7`, `A/B 01`, `O'Brien-1`, `DH"Q"`, `Ä-1` and `ä-2`.
- **Expected:**
  - Each ID is searchable by a substring (`дн` matches `ДН-001`, case-insensitive) and displayed unchanged.
  - Each ID can be picked and saved.
  - It round-trips through `DrillholeCache` byte-for-byte.

---

## C. Core Information form and navigation (F04, F10)

**Shared.** Environment: ENV-STA. Preconditions: `MainViewModel` from the injectable constructor with fakes (`MemoryRepository`, `StubBackend`, `ControlledMeasurements`, `FakeDialogs`, `FakeExport`, and a fake Bluetooth list). Source: `Frontend/Pages/CoreInformation/CoreInformationViewModel.cs`, `ViewModels/MainViewModel.cs`, `Shared/Models/CoreSnapshot.cs`, `Shared/Infrastructure/SidebarItem.cs`.

<a id="fe-019"></a>

### FE-019 · Save without a picked drillhole is refused
- Component · Medium (4×2) · Automatable · Inferred · Existing coverage: Run
- **Actions:** execute `SaveCommand` with no pick (and again with typed but unpicked text).
- **Expected:**
  - The drillhole error flag is set (red border).
  - Exactly one Error toast, "Select a drillhole from the list.", lasting 5 s.
  - `CurrentPage` stays Core Information, and `Measurement.SetCore` is not called.

<a id="fe-020"></a>

### FE-020 · Successful Save & Continue
- Component · Medium (4×2) · Automatable · Inferred · Existing coverage: Run, `AutofillAndDepthInputChecks`
- **Input:** pick `DH-001`; Operator `"  "`; Notes `"  note  "`.
- **Expected:**
  - The snapshot has Operator = the Windows account in the form `DOMAIN\user` (or `user`) and Notes `"note"` (trimmed).
  - Navigation goes to Measurement, and a session for `DH-001` is resolved once.
  - A typed Operator `" Ann "` is saved as `"Ann"`.

<a id="fe-021"></a>

### FE-021 · Editing Operator or Notes after saving disarms measurement
- Component · Medium (3×2) · Automatable · Inferred (by design per comments; whether it's wanted is **UR-09**)
- **Preconditions:** saved, armed (state Ready).
- **Actions:** change Notes by one character.
- **Expected:** `Measurement.SetCore(null)` is called, the state becomes Idle, and any active capture is cancelled without a row. Save & Continue is needed again before Start is enabled.

<a id="fe-022"></a>

### FE-022 · The drillhole can't change while measuring
- Component · High (5×2) · Automatable · Inferred · Existing coverage: `DrillholeSelectionChecks`
- **Preconditions:** measurement mode is active (Ready or Capturing).
- **Actions:** pick `DH-002` in the Core form.
- **Expected:**
  - An Error toast, "Stop the measurement before changing the drillhole."
  - The selection reverts to `DH-001`, and `Measurement.SelectDrillhole` is not called.
  - After Stop, the change is accepted.

<a id="fe-023"></a>

### FE-023 · Navigation keeps state and doesn't duplicate background loops
- Component · Medium (2×2) · Automatable · Inferred · Existing coverage: Run (`MainViewModel` checks)
- **Actions:** switch Core → Measurement → Core 5 times; collapse and expand the sidebar.
- **Expected:**
  - The sidebar selection always matches `CurrentPage`.
  - The form values and measurement rows are retained.
  - Probe and status-check counts per interval are the same as before navigating.

---

## D. Measurement page state machine and capture flow (F11–F14)

**Shared.** Environment: ENV-STA. Preconditions: `MeasurementViewModel` (through `MainViewModel` or directly) with `ControlledMeasurements` (manual complete/fail/report), `MemoryRepository`, `FakeDialogs` and `FakeExport`; a saved core `DH-001`; the session resolved. Source: `Frontend/Pages/Measurement/{MeasurementViewModel,MeasurementState,MeasurementResult}.cs`, `Calculation/Measurement/Depth.cs`. No backend path.

<a id="fe-024"></a>

### FE-024 · Depth input validation and messages
- Component · High (4×3) · Automatable · Inferred · Existing coverage: `AutofillAndDepthInputChecks`
- **Input:** a data table of (Starting Depth, Step) → (flags, message, Start enabled):

  | Starting Depth | Step | Expected |
  |---|---|---|
  | `10.25` | `25` | valid, message `""`, Start enabled |
  | `10,25` | `2,5` | valid (from 10250 mm, step 25 mm) |
  | `10.2555` | `25` | `IsNextFromInvalid`; "Starting depth: enter a depth in metres (at most 3 decimals)." |
  | `-1` | `25` | invalid starting depth (same message) |
  | `10` | `0` | `IsStepInvalid`; "Step: enter a step greater than 0 cm (at most 1 decimal)." (the parser accepts 0; the ViewModel rejects it, `MeasurementViewModel.cs:463`) |
  | `10` | `2.55` | invalid step |
  | `""` | `""` | no red border; "Enter the starting depth and step to enable Start."; Start disabled |
  | `1,000.5` | `25` | invalid (group separators aren't accepted) |
- **Expected also:** on LostFocus or Start with an invalid value, one Error toast with the same message.

<a id="fe-025"></a>

### FE-025 · Start (Idle → Ready) needs valid depths and a session matching the saved core
- Component · Medium (4×2) · Automatable · Inferred · Existing coverage: Run
- **Actions:**
  - (a) Start with valid depths.
  - (b) Start with invalid depths.
  - (c) Start while the session for the saved core is still opening (repository lookup delayed).
- **Expected:** (a) Ready, `IsMeasurementModeActive` true, and the Core selection locked; (b) stays Idle with a toast; (c) Start is unavailable until the session opens, then works.

<a id="fe-026"></a>

### FE-026 · A successful capture cycle, and duplicate triggers ignored
- Component · High (5×3) · Automatable · Inferred · Existing coverage: Run
- **Actions:** in Ready, trigger at T; trigger again 3 times during Capturing; complete `ControlledMeasurements` with 5 samples; await the queue.
- **Expected:**
  - The states go Ready → Capturing → Processing → Ready. While Capturing or Processing, `CanTrigger` is false, and the extra triggers make no extra `CaptureAsync` calls (exactly 1).
  - One row is added, with From/To = 10.00/10.25 m and Capture No = 1.
  - `MemoryRepository` received exactly one save containing the row and the progress (next from 10.25 m).
  - The countdown resets to the full duration.

<a id="fe-027"></a>

### FE-027 · Stop during Capturing produces no row
- Component · High (4×3) · Automatable · Inferred · Existing coverage: Run
- **Actions:** trigger, report a remaining time of 2 s, then Stop; afterwards let the controlled service return data.
- **Expected:**
  - The state is Idle and the capture token is cancelled.
  - No row; the depth inputs are unchanged; no save; no toast.
  - The late data is ignored.

<a id="fe-028"></a>

### FE-028 · Stop during Processing (the 100 ms hold) discards the captured data
- Component · High (4×3) · Automatable (timing-sensitive: the hold is a real `Task.Delay(100)`) · Inferred (whether discarding is acceptable is **UR-23**)
- **Actions:** complete the capture, and call Stop within 50 ms.
- **Expected:** Idle, no row, no depth advance and no save. Repeat 20 times; the result is the same every time, with no row appearing late.

<a id="fe-029"></a>

### FE-029 · Depth progression is frozen at the click and advances once
- Component · High (5×3) · Automatable · Inferred · Existing coverage: `AutofillAndDepthInputChecks`, `MeasurementUnitsAndOrderChecks`
- **Actions:**
  - (a) From 10.00, step 25 cm: capture.
  - (b) During a capture, change Step to 50 cm.
  - (c) During a capture, change Starting Depth to 20.00.
- **Expected:**
  - (a) The row is 10.00–10.25, and the next From is 10.25.
  - (b) The row still uses 25 cm. Next From = 10.25, and the next capture uses 50 cm (10.25–10.75).
  - (c) The row is 10.00–10.25, and next From stays 20.00 (no advance).

<a id="fe-030"></a>

### FE-030 · Capture outcome feedback
- Component · High (4×3) · Automatable · Inferred · Existing coverage: Run, `DeviceCaptureChecks`
- **Input:** a data table of controlled outcomes:

  | Outcome | Expected |
  |---|---|
  | 3 of 5 samples | Row added; Sample shows "3 of 5"; Warning toast "Incomplete capture: 3 of 5 samples received. The result averages the 3 received."; depth advances |
  | 0 samples (service throws `CaptureException`) | No row, no advance; Error toast with the exception message verbatim; state Ready |
  | `CaptureException("Gamma Guard database is busy …")` | Same as the row above |
  | `InvalidOperationException` | No row; Error toast "Capture failed. Right-click to try again."; state Ready |
  | `OperationCanceledException` without a Stop | Generic toast as in the row above (token not cancelled) |
- **Note:** whether an incomplete row should be saved at all, or needs a minimum count, is UR-17.

<a id="fe-031"></a>

### FE-031 · Late progress callbacks are ignored
- Component · High (3×3) · Automatable · Inferred
- **Actions:** capture 1 is cancelled by Stop; Start; capture 2 begins. The service then invokes capture 1's `reportRemaining(1 s)`.
- **Expected:** `Countdown` and `CaptureProgress` reflect capture 2 only.

<a id="fe-032"></a>

### FE-032 · Capture duration editor
- Component · Medium (3×2) · Automatable · Inferred · Existing coverage: `CaptureDurationChecks`
- **Actions and expected:**
  - Click the countdown while Idle or Ready: the editor opens. While Capturing it doesn't open.
  - Enter `7`: the editor closes, `CaptureDuration` is 7 s, and the countdown shows 7 s. The file `capture-settings.json` contains `{"captureDurationSeconds":7}`.
  - Enter `1`, `11`, `7.5` or `abc`: an Error toast "Capture duration: enter whole seconds from 2 to 10." The editor stays open and the duration is unchanged.
  - Escape: closes with no change.
  - The settings path is unwritable: the duration is used for the session, with an Error toast "The capture duration is used now but could not be saved (…)".
  - A duration changed after a capture started applies only to the next capture.

<a id="fe-033"></a>

### FE-033 · Rows go to their From position and are renumbered
- Component · Medium (4×2) · Automatable · Inferred · Existing coverage: `MeasurementUnitsAndOrderChecks`
- **Actions:** capture at From 10.50, then set Starting Depth to 10.00 and capture, then 10.25.
- **Expected:** the display order is 10.00, 10.25, 10.50; `Number` is 1, 2, 3; `CaptureNumber` is 2, 3, 1; the SortOrder saved matches the displayed order.

<a id="fe-034"></a>

### FE-034 · Delete flow
- Component · High (5×2) · Automatable · Inferred · Existing coverage: Run (cancel, renumber, no reuse, failed delete)
- **Actions and expected:**
  1. `FakeDialogs` returns false: no change and no repository call.
  2. Confirm deleting row 2 of 3: the row is removed, the rows are renumbered 1–2, `DeleteAsync(session, CaptureNo)` is called once, and a Success toast "Measurement deleted." appears only after the repository completes.
  3. The repository fails: the row reappears in its original position, with an Error toast "Deleting the measurement failed (…). It is still stored and stays in the table."
  4. The next capture after deleting the highest Capture No gets max + 1, never a reused number.

<a id="fe-035"></a>

### FE-035 · A save failure keeps the rows and retries
- Component · High (5×3) · Automatable · Inferred · Existing coverage: Run (save failures)
- **Actions:** `MemoryRepository` fails; capture one row; restore the repository; then trigger each retry point separately:
  - (a) the next capture;
  - (b) Stop;
  - (c) switching the drillhole;
  - (d) Dispose.
- **Expected:**
  - After the failure: the row stays visible, with an Error toast "Saving to the database failed (…). Rows stay in the table and are saved again on the next change or Stop."
  - After each retry point, the repository holds the row exactly once.
  - Writes reach the repository in request order (the serial queue).

<a id="fe-036"></a>

### FE-036 · Switching drillholes saves first and ignores late loads
- Component · High (5×3) · Automatable · Inferred · Existing coverage: `DrillholeSelectionChecks`
- **Actions:**
  1. Capture on `DH-001` while saves are delayed by 500 ms.
  2. Switch to `DH-002` (which has stored rows), and immediately back to `DH-001`, with `DH-002`'s load delayed by 1 s.
- **Expected:**
  - The `DH-001` row is saved to `DH-001`'s session.
  - The view ends showing `DH-001` only; `DH-002` rows never appear (`viewVersion`).
  - Switching to a drillhole with no stored session creates no session.
  - If the earlier rows are still unsaved when Save & Continue runs: an Error toast "Measurements of the previous drillhole are not saved yet. Retrying — click Save & Continue again once saving succeeds." No navigation to the new session's data.

<a id="fe-037"></a>

### FE-037 · Differing stored session metadata is kept, with a warning
- Component · Medium (3×2) · Automatable · Inferred (whether stored values should win is **UR-30**)
- **Preconditions:** the stored session `DH-001` has Operator "Ann". The form saves Operator "Bob".
- **Expected:** a Warning toast "Drillhole DH-001 already exists. Its stored project, campaign, operator and notes are kept." The session shows "Ann", and CSV/rows use "Ann".

<a id="fe-038"></a>

### FE-038 · New Measurement
- Component · Medium (4×2) · Automatable · Inferred · Existing coverage: Run
- **Expected:**
  - With unsaved rows: refused, with an Error toast "Previous measurements are not saved yet. Retrying — try New Measurement again once saving succeeds."; the session is kept.
  - Otherwise: the rows and session are cleared, the saved core is null and the state is Idle. An Information toast "Enter the next drillhole in Core Information and click Save & Continue. Earlier measurements remain stored." The stored rows stay in the repository.

<a id="fe-039"></a>

### FE-039 · Export command
- Component · Medium (3×2) · Automatable · Inferred · Existing coverage: Run (`FakeExport`)
- **Expected:**
  - Disabled with 0 rows.
  - `FakeExport` receives the rows in displayed order and the current session.
  - It returns true: a Success toast "CSV exported."
  - It returns false (dialog cancelled): no toast.
  - It throws `IOException`: an Error toast "Export failed. Check the file location and try again."

<a id="fe-040"></a>

### FE-040 · Saved measurements refresh the "Measured drillholes" group
- Component · Medium (2×2) · Automatable · Inferred
- **Expected:** after the first saved capture on `DH-005`, the Core dropdown (empty query) lists `DH-005` first under "Measured drillholes".

<a id="fe-041"></a>

### FE-041 · Toast service rules
- Unit · Medium (2×2) · Automatable · Inferred · Existing coverage: `ToastChecks`
- **Preconditions:** an injected controllable delay.
- **Expected:**
  - Error toasts expire at 5 s and others at 3 s.
  - An identical visible toast is restarted rather than duplicated (count stays 1).
  - A 5th distinct toast removes the oldest (at most 4).

<a id="fe-042"></a>

### FE-042 · Status announcements
- Component · Medium (2×2) · Automatable · Inferred · Existing coverage: `ToastChecks`
- **Expected:**
  - Nothing is announced during start-up.
  - Backend: Offline is announced once; Online is announced only after an Offline.
  - DB rows: announced from the second known state onward.
  - Bluetooth: the first confirmed state after a selection, then each change.
  - A repeated identical state isn't announced again.

---

## E. Device Status page (F06–F08)

**Shared.** Environment: ENV-STA + ENV-SQLITE + ENV-FS. Preconditions: `DeviceStatusMonitor` with `FakeBluetoothReader`, a fake device list (TD-BT), injected Gamma Guard search roots, and temp DB files. Source: `Frontend/Calculation/Core-Information/{DeviceStatusMonitor,LocalStatus,DeviceSettings}.cs`, `BluetoothDevice/Ct007BluetoothCheck.cs`, `MeasurementDatabase/MeasurementDatabaseCheck.cs`, `GammaGuardDatabase/GammaGuardDatabaseCheck.cs`.

<a id="fe-043"></a>

### FE-043 · CT007-M identification, selection and status mapping
- Component · Medium (2×3) · Automatable · Inferred · Existing coverage: `LocalDeviceStatusChecks`
- **Input:** a device list containing:
  - `CT007-M-0001` (connected);
  - `ct007-m-0002` (paired only);
  - `CT007-X` (no match);
  - an unnamed device with the configured `ct007Address`;
  - a duplicate entry with the same address.
- **Expected:**
  - The dropdown lists 3 CT007-M entries (de-duplicated by address).
  - Nothing is auto-selected.
  - Selecting 0001 gives "Connected". Selecting 0002 gives "Disconnected". A reader timing out after 3 s gives "Status unavailable".
  - Only the selected device is polled (1 s).
  - A result arriving after the selection changed is discarded.
  - The list refreshes every 5 s, and on dropdown open, without losing the selection.
  - Capture behaviour is unaffected by any of these states.

<a id="fe-044"></a>

### FE-044 · Database status rows and Retry
- Component · Medium (3×2) · Automatable · Inferred · Existing coverage: `LocalDeviceStatusChecks`
- **Expected:**
  - The rows show the `LocalState` from `SqliteFileCheck` (cases in BE-028).
  - Gamma Guard "not found" shows the amber state.
  - Retry checks both rows immediately and rediscovers Gamma Guard (a newly created DB file under the roots is found).
  - Checks never create or modify a file.

<a id="fe-045"></a>

### FE-045 · `DeviceSettings.Load`
- Unit · Medium (3×2) · Automatable · Inferred · Existing coverage: `LocalDeviceStatusChecks`
- **Input:** a missing or invalid file; negative intervals; `gammaGuardDatabasePath` = `%TEMP%\gg.sqlite`; an empty prefix.
- **Expected:** invalid values fall back to the defaults; environment variables are expanded; the configured path is used as-is.

---

## F. Presentation (F16, F20)

**Shared.** Environment: ENV-PURE (formatting) or ENV-STA (layout/chart). Source: `Frontend/Pages/Measurement/{MeasurementCellFormat,MeasurementColumns,MeasurementColumnLayout,DepthBarChart,MeasurementResult}.cs`.

<a id="fe-046"></a>

### FE-046 · Cell formatting
- Unit · Medium (3×2) · Automatable · Inferred · Existing coverage: `TableFormattingChecks`
- **Input:** a data table:
  - `1.005`, `2.0`, `123456.789`, `null` and `NaN` (if it can occur);
  - `ts_ms` = 1760600000000 under time zones UTC and UTC+08:00;
  - `SampleCount` 3 with an expected count of 5.
- **Expected:**
  - At most 2 decimals, using the documented rounding. Record whether the mode is banker's rounding or away-from-zero, e.g. for `1.005`.
  - Null gives an empty cell.
  - `ts_ms` is shown as `yyyy-MM-dd HH:mm:ss` in the local zone (the two zones differ by 8 h).
  - Sample shows "3 of 5".

<a id="fe-047"></a>

### FE-047 · Column layout
- Unit · Low (2×1) · Automatable · Inferred · Existing coverage: Run (layout minimum widths)
- **Expected:** the column order equals the `MeasurementColumns.All` order; hidden columns follow `MeasurementColumnLayout`; every visible column's width is ≥ its minimum.

<a id="fe-050"></a>

### FE-050 · Chart data mapping
- Component · Medium (2×2) · Conditional (only if `DepthBarChart` exposes its geometry or tick computation; otherwise manual MAN-008) · **Unresolved** (UR-05: what the chart should plot)
- **Expected (current behaviour):**
  - One bar per row spanning From–To on x.
  - Rows without depths or `DisplayTsMs` aren't drawn.
  - The ticks are "nice numbers" covering the data range.
  - The y-quantity assertion is pending UR-05.

---

## G. Lifecycle, demo mode, restore

<a id="fe-048"></a>

### FE-048 · Closing during a capture or with pending saves
- Component · High (5×3) · Conditional (timing; `Dispose` blocks for up to 5 s) · Inferred (whether this is acceptable is **UR-23**)
- **Environment:** ENV-STA. **Source:** `MeasurementViewModel.Dispose`, `MainWindow.xaml.cs`.
- **Actions:**
  - (a) Dispose during Capturing.
  - (b) Dispose with a 1 s queued save.
  - (c) Dispose with a save that never completes.
- **Expected:**
  - (a) The capture is cancelled, no row and no toast.
  - (b) The save completes before `Dispose` returns.
  - (c) `Dispose` returns after about 5 s (±1 s), and the unsaved row is lost. This is the current behaviour; flag it for UR-23.

<a id="fe-049"></a>

### FE-049 · Demo `ts_ms` is never saved or exported
- Component · Low (3×1) · Automatable · Inferred · Existing coverage: Run (demo `ts_ms`)
- **Expected:** with `ShowDemoTsMs` true, `DisplayTsMs` shows 500–5000. The stored `TsMs` and the CSV `ts_ms` are the device value (or empty). With the default `false`, no demo values appear.

<a id="fe-051"></a>

### FE-051 · Restoring the latest session on start-up
- Component · Low (3×1) · **Blocked** · **Unresolved** (UR-06)
- **Note:** `RestoreLatestSessionAsync` is implemented and smoke-tested but never called. Don't write an app-level test until UR-06 decides whether start-up should restore.
