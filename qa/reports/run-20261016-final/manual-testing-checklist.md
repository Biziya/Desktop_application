# Manual Testing Checklist — run-20261016-final

Condensed from [manual-test-cases.md](../../planning/manual-test-cases.md); see that file for the full steps.

**No manual case has been executed.** All result fields are blank.

**General setup:**
- Windows 10 2004+ or 11 laptop matching the field configuration.
- The `Frontend.exe` build from commit `cc031a5`.
- A local backend in test configuration. Never use the corporate SQL Server.
- A dedicated Windows test account. Back up and clear `%LocalAppData%\CT007_Measurement_Studio` before each session.

**Record for each run:** tester, date, build, Windows version, scaling, locale and time zone, Gamma Guard version, detector serial.

**Result values:** Pass / Fail / Blocked.

## A. Detector and Gamma Guard (hardware bench)

| ID | Priority | Preconditions / equipment | Steps (short) | Expected result | Result | Notes |
|---|---|---|---|---|---|---|
| MAN-001 | Critical | CT007-M, Gamma Guard recording, Bluetooth paired | 10 captures at 5 s; compare each row with a copy of Gamma Guard's `samples` for the window `click < ts ≤ click+5000` | Sample = rows in the window; means match to ±1e-9; serial matches; Gamma Guard file hash unchanged by the app. *Tolerance unresolved (UR-02, UR-15)* | | |
| MAN-002 | Critical | Detector + check source + stopwatch or video | Right-click as the source reaches the detector, 10 times | The raised rate appears in samples after the click; record latency (≤ 1 s proposed) and clock offset. *Design draft conflicts (UR-01, UR-10)* | | |
| MAN-003 | High | Detector recording | Count rows/s over 60 s; capture at 2, 5 and 10 s | About 1 row/s (deltas 1000 ± 100 ms); Sample = seconds. *UR-02* | | |
| MAN-004 | Medium | Two detectors recording | Capture; stop one for more than 5 s; capture again | First refused: "More than one detector is recording (…)"; then a capture from the remaining one | | |
| MAN-005 | Medium | Selected CT007-M | Connected → power off → radio off → unpair → out of range → second device | Status changes within 1–3 s with one toast; nothing auto-selected; capture unaffected | | |
| MAN-014 | High | Each target laptop image, default `device-settings.json` | Open Device Status; record the discovered path | Path = the DB Gamma Guard writes. *UR-16* | | |

## B. Capture trigger and lifecycle

| ID | Priority | Preconditions | Steps (short) | Expected result | Result | Notes |
|---|---|---|---|---|---|---|
| MAN-006 | High | Armed, Gamma Guard real or simulated | Right-click over the grid, a text box, the chart, the ring, the sidebar, a scrollbar, an empty area; left/middle click; not armed; inactive or minimised window; dialog or duration editor open; double right-click; right-click during a capture | Exactly one capture where allowed, none elsewhere; no context menu while armed. Record the result for touch/touchpad | | |
| MAN-007 | High | 3 saved captures, etc. | Close during a capture; close right after one; kill the process; close with the DB write-locked | No crash; saved rows present after restart; `integrity_check` ok; freeze ≤ 5 s; record any loss (UR-23, SUS-04) | | |

## C. Display, accessibility, locale

| ID | Priority | Preconditions | Steps (short) | Expected result | Result | Notes |
|---|---|---|---|---|---|---|
| MAN-008 | Medium | 100/125/150/200 % scaling, 1366×768 | Both pages, toasts, chart hover, editor, collapsed sidebar, 200 rows | No clipping or overlap; readable columns; tooltip correct; smooth scrolling | | |
| MAN-009 | High | Keyboard only, Narrator, contrast tool | Complete Core → Measurement by keyboard | Logical Tab order; Enter and arrow keys in the drillhole box; Escape closes the editor; names announced; contrast ≥ 4.5:1 (proposed). *UR-21* | | |
| MAN-010 | High | fr-FR, ru-RU, de-CH formats | Enter `10,25`, `10.25`, `2,5`; capture; export; open in Excel | Both separators accepted; `.` in the table and CSV; record how Excel splits columns (UR-04) | | |
| MAN-011 | High | Time zones +08:00 / −05:00, a DST change | Capture, change the zone, relaunch; capture across DST | Local display; the stored offset kept; window still 5 s | | |

## D. Export and backend

| ID | Priority | Preconditions | Steps (short) | Expected result | Result | Notes |
|---|---|---|---|---|---|---|
| MAN-012 | High | Notes `=1+1`, `@SUM(A1)`, `-5`, `+A`, tab-prefixed, multi-line, Unicode, `a,b` | Export; open in Excel | No formula runs; the line break stays in one cell; Unicode correct; record the tab case (UR-18) | | |
| MAN-013 | Medium | Real Save dialog | Cancel; overwrite; read-only folder; network share; path over 260 characters; OneDrive | Cancel is silent; the overwrite is atomic; "Export failed. Check the file location and try again."; no `*.tmp` left | | |
| MAN-015 | Medium | Another HTTP server on `localhost:5002` | Launch the app | Currently shows Online while the refresh fails, keeping the cached list. Acceptability: UR-13 | | |
| MAN-016 | Medium | Simulated writer, 2 s captures | 500 captures; sample resources every 100 | No unbounded growth; latency ≤ 1 s; export of 500 rows < 2 s (proposed, UR-21) | | |
| MAN-017 | Medium | Delete dialog open | Escape, Enter, Alt+F4, Tab, click outside | Escape and Alt+F4 cancel; record the Enter default; modal and centred | | |

**Run first:** MAN-001, MAN-002, MAN-006 and MAN-007. The automated suite has no real-hardware or real-UI evidence for these areas.
