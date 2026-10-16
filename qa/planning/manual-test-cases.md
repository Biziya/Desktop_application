# Manual Test Cases (MAN)

These checks need physical hardware, a real Windows desktop, human judgement, or third-party software (Gamma Guard, Excel, Narrator). The root `README.md` §Validation lists 14 earlier manual checks; MAN-006 and MAN-008 supersede and extend them.

**Common setup (ENV-HW and ENV-UIA in [test-data-requirements.md](test-data-requirements.md)):**
- A Windows 10 2004+ or 11 laptop matching the field configuration.
- A build of `Frontend.exe` and a local backend in test configuration. **Never point it at the corporate SQL Server** unless the owner explicitly approves a read-only staging instance.
- A dedicated Windows test account, so that `%LocalAppData%\CT007_Measurement_Studio\` is disposable.
- Before each session, back up and then delete that folder.

**Recording.** For each run, record the tester, date, build commit, Windows version, display scaling, locale and time zone, Gamma Guard version, detector serial, the result (Pass, Fail or Blocked), and evidence (screenshots, a CSV, or a copy of the DB).

Expectation status is **Inferred** unless it says **Unresolved**.

---

<a id="man-001"></a>

### MAN-001 · Live capture accuracy against Gamma Guard
- **Priority:** Critical (5×4) · **Status:** Unresolved (UR-02, UR-15: no accuracy tolerance or reference is specified)
- **Equipment:** a CT007-M detector; Gamma Guard recording; Bluetooth paired.
- **Steps:**
  1. Confirm the Gamma Guard row is Available in Device Status.
  2. Measure 10 intervals at 5 s.
  3. For each row, export the CSV and query Gamma Guard's `samples` for the same serial and the window `click < ts_ms ≤ click+5000`. Use a read-only copy of the DB, made **after** the session.
- **Expected:**
  - For each row: Sample = the number of rows in the window.
  - count0, hdm and dose_rate equal the arithmetic mean of those rows (±1e-9).
  - `device_serial` matches.
  - The Gamma Guard DB has no modification by the app: compare the file hash before and after, with Gamma Guard closed both times.

<a id="man-002"></a>

### MAN-002 · Click-time alignment (timing calibration)
- **Priority:** Critical (5×4) · **Status:** Unresolved (UR-01, UR-10: the design draft says a 3 s delay; the code uses the click time)
- **Equipment:** the detector; a check source or another physical reference that changes the count rate at a known instant; a stopwatch or video.
- **Steps:** right-click exactly when the source is brought to the detector, over 10 trials at 5 s.
- **Expected:**
  - Rows show the raised rate for the samples after the click.
  - The latency between the click and the first `ts_ms` is ≤ 1 s (proposed).
  - Record the latency distribution, and record any systematic offset between the laptop and device clocks.

<a id="man-003"></a>

### MAN-003 · Sample rate
- **Priority:** High (5×3) · **Status:** Unresolved (UR-02)
- **Steps:** with Gamma Guard recording for 60 s, count the rows per serial and the `ts_ms` deltas. Then capture at 2, 5 and 10 s.
- **Expected:** about 1 row/s (deltas 1000 ± 100 ms), and Sample = the duration in seconds. If the rate differs, the expected-sample formula (BE-021) is wrong.

<a id="man-004"></a>

### MAN-004 · Two detectors recording at once
- **Priority:** Medium (4×2) · **Status:** Inferred
- **Equipment:** two CT007-M detectors, both recorded by Gamma Guard.
- **Expected:** a capture is refused with the toast "More than one detector is recording (S1, S2). Stop the others to measure." and no row. With one stopped for more than 5 s, the capture succeeds from the remaining one.

<a id="man-005"></a>

### MAN-005 · Bluetooth status states
- **Priority:** Medium (2×3) · **Status:** Inferred
- **Steps:** select the CT007-M in Device Status. Then, in turn:
  - (a) Connected.
  - (b) Power the detector off.
  - (c) Turn the Bluetooth radio off.
  - (d) Unpair it.
  - (e) Move it out of range.
  - (f) Pair a second CT007-M.
- **Expected:**
  - The status shows Connected / Disconnected / Status unavailable within about 1–3 s of each change, with one toast per change and no freeze of the UI. (Status unavailable is acceptable for c, or for timeouts.)
  - (d) The device disappears from the list within about 5 s, or on reopening the dropdown.
  - (f) Both are listed; nothing is auto-selected.
  - Capture is unaffected in every state.

<a id="man-006"></a>

### MAN-006 · Right-click trigger matrix
- **Priority:** High (5×3) · **Status:** Inferred
- **Preconditions:** armed (Ready) unless stated; a simulated or real Gamma Guard.
- **Steps and expected** (✔ = exactly one capture; ✘ = no capture):

  | Where or when | Expected |
  |---|---|
  | Right-click on the DataGrid, a text box, the chart, the countdown ring, the sidebar, a scrollbar, an empty area | ✔, with no context menu (including the text-box context menu) |
  | Left or middle click anywhere | ✘ |
  | Not armed (Idle) | ✘; the normal context menus work |
  | Window inactive (another app focused; click into the app) | ✘ for the activating click, Inferred: activation comes first. Record the actual result |
  | Minimised, or a modal dialog (Delete confirmation or Save dialog) open | ✘ |
  | Duration editor popup open | ✘ |
  | Double right-click within 100 ms | Exactly one capture |
  | Right-click during Capturing or Processing | No second capture |
  | Touchpad two-finger tap, and touch long-press | Record the behaviour (Unresolved: is this supported?) |

<a id="man-007"></a>

### MAN-007 · Closing and crashing during work
- **Priority:** High (5×3) · **Status:** Inferred (whether this is acceptable is **UR-23**)
- **Steps:**
  - (a) Close the window during a capture.
  - (b) Close immediately after a capture completes.
  - (c) Kill the process (Task Manager) after 3 saved captures.
  - (d) Close with the DB locked by `tools/check-db.ps1` or another SQLite tool holding a write lock.
- **Expected:**
  - (a) No crash; no row for the interrupted capture.
  - (b) After a relaunch, the row is present.
  - (c) The 3 rows are present and the DB passes `PRAGMA integrity_check`.
  - (d) The UI freezes for ≤ 5 s, then closes. Record any data loss for UR-23.

<a id="man-008"></a>

### MAN-008 · Layout and rendering
- **Priority:** Medium (2×3) · **Status:** Inferred
- **Steps:** at 100, 125, 150 and 200 % scaling, at the minimum window size and maximised, and on a 1366×768 display: view both pages and the toasts; hover over chart bars; open the duration editor; collapse the sidebar; add 200 rows.
- **Expected:**
  - No clipped labels or buttons, and no overlapping toasts.
  - The DataGrid columns stay readable at their minimum widths.
  - The chart axis labels don't overlap, and the tooltip shows the hovered row.
  - The countdown ring animates smoothly.
  - Scrolling with 200 rows has no visible lag (subjective; record it).

<a id="man-009"></a>

### MAN-009 · Keyboard use and accessibility
- **Priority:** High (3×3) · **Status:** Inferred (no accessibility requirement is documented; see **UR-21**)
- **Steps:** operate the whole Core Information → Measurement flow with the keyboard only (except the right-click trigger). Use Narrator. Check the contrast of the status colours with a contrast analyser.
- **Expected:**
  - A logical Tab order.
  - In the drillhole box, Enter picks the closest match and the arrow keys move through the matches.
  - The caret doesn't jump while typing.
  - Escape closes the duration editor.
  - Narrator announces the control names and the status row texts.
  - Status is not conveyed by colour alone; the text accompanies the coloured dot.
  - Text contrast ≥ 4.5:1 (WCAG AA, a proposed target).

<a id="man-010"></a>

### MAN-010 · Regional settings
- **Priority:** High (4×3) · **Status:** Inferred
- **Steps:** set the Windows format to `fr-FR`, then `ru-RU`, then `de-CH`. Enter Starting Depth `10,25` and `10.25`, and Step `2,5`. Capture, export, and open the CSV in Excel set to the same locale.
- **Expected:**
  - Both separators are accepted, and the table shows `.` decimals (invariant).
  - The chart labels follow the current culture (Inferred).
  - CSV numbers use `.`.
  - Record whether Excel in a `;`-separator locale splits the columns. This is a usability question for UR-04.

<a id="man-011"></a>

### MAN-011 · Time zone and DST
- **Priority:** High (3×3) · **Status:** Inferred
- **Steps:**
  - Capture in UTC+08:00, then change the time zone to UTC−05:00 and relaunch.
  - Capture across a DST transition (set the clock 2 min before the change).
- **Expected:**
  - Start Time and ts_ms show local time for the current zone; the stored values keep their original offset.
  - The CSV `O` timestamps contain the offset at capture time.
  - The Measured order is still by instant.
  - A capture spanning DST still has a 5 s window (UTC-based).

<a id="man-012"></a>

### MAN-012 · CSV in Excel
- **Priority:** High (4×3) · **Status:** Inferred, with tab/CR prefixes **Unresolved** (UR-18)
- **Steps:** set Notes to `=1+1`, `@SUM(A1)`, `-5`, `+A`, `\t=1` (pasted tab), `Line1⏎Line2`, `Ünïcødé 😀`, `a,b`. Export, then open by double-clicking the file.
- **Expected:**
  - No formula executes; the cells show the literal text with a leading `'` hidden, or visible, depending on Excel.
  - The line break stays inside one cell, and Unicode displays correctly (BOM).
  - Record the result for the tab-prefixed value.

<a id="man-013"></a>

### MAN-013 · Real Save dialog
- **Priority:** Medium (3×2) · **Status:** Inferred
- **Steps:** export each of these ways: Cancel; to an existing file (accept the overwrite); to a read-only folder; to a network share; to a path longer than 260 characters; to a OneDrive folder.
- **Expected:**
  - Cancel gives no toast and no file.
  - An overwrite replaces the file atomically (never a partial file).
  - A read-only folder gives the error toast "Export failed. Check the file location and try again."
  - No `*.tmp` remains in any case.
  - Record the outcomes for the network and long-path cases.

<a id="man-014"></a>

### MAN-014 · Gamma Guard discovery on target laptops
- **Priority:** High (4×3) · **Status:** Unresolved (UR-16)
- **Steps:** on each target laptop image, with the default `device-settings.json`, open Device Status and record the path that is found. Compare it with the actual Gamma Guard DB.
- **Expected:** the discovered path equals the DB that Gamma Guard writes. Record the folder layout so that UR-16 can be answered.

<a id="man-015"></a>

### MAN-015 · A foreign service on the backend port
- **Priority:** Medium (3×2) · **Status:** Unresolved (UR-13)
- **Steps:** stop the backend; start another HTTP server on `localhost:5002` (any static server); launch the app.
- **Expected (current, Inferred):** the sidebar shows Online, the drillhole refresh fails with an error message, and the cached list is kept. Whether "Online" is acceptable here is UR-13.

<a id="man-016"></a>

### MAN-016 · Long session soak
- **Priority:** Medium (3×2) · **Status:** Unresolved (UR-21: no performance target)
- **Steps:** with the simulated GG writer, do 500 captures in one session at a 2 s duration (an automated clicker is allowed). Record memory, handles, the DB size and the UI responsiveness every 100 captures.
- **Expected (proposed):**
  - No growth in memory and handles beyond what is linear in the row count.
  - Capture-to-row latency is ≤ 1 s after the window throughout.
  - Export of 500 rows takes < 2 s.
  - No toast errors.

<a id="man-017"></a>

### MAN-017 · Delete confirmation dialog usability
- **Priority:** Medium (2×2) · **Status:** Inferred
- **Steps:** open Delete; try Escape, Enter, Alt+F4, Tab between the buttons, and clicking outside the dialog.
- **Expected:**
  - Escape and Alt+F4 cancel.
  - Record which button is the default for Enter. The safe expectation is that Enter doesn't delete by default (Unresolved preference).
  - The dialog is modal and centred over the main window.
  - The text names the row (record the text).
