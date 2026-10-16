# Business Rules

Concrete rules and values as implemented. All are **Inferred** from code, verified in the source and covered by the tests listed. The **Confirmed** column notes where the root README documents something about the same topic. Values marked Unresolved need a decision (see [unresolved-requirements.md](unresolved-requirements.md)).

| ID | Rule | Value / logic | Source | Confirmed? | Tests |
|---|---|---|---|---|---|
| BR-001 | Depth unit | Stored and calculated as integer **millimetres**; shown as metres (depths) and centimetres (step) | `Depth.cs` | — | BE-015…018 |
| BR-002 | Starting Depth input | ≥ 0 m, at most 3 decimals (whole mm); `.` or `,`; no sign, exponent or grouping | `Depth.TryParseMetres` | — | BE-015, FE-024 |
| BR-003 | Step input | > 0 cm, at most 1 decimal (whole mm). The parser accepts 0; the page rejects it | `Depth.TryParseCentimetres`, `MeasurementViewModel` (line ~463) | — | BE-016, FE-024 |
| BR-004 | Interval | `to = from + step`, fixed when the capture starts | `MeasurementViewModel.CaptureAsync` | — | FE-029 |
| BR-005 | Depth advance | Next From = this capture's To, unless Starting Depth changed during the capture | same | — | FE-029 |
| BR-006 | Capture duration | Whole seconds **2–10**, default **5**; an invalid saved value falls back to 5 | `CaptureDurationSettings.cs` | README: duration configurable; range and default not confirmed (Q5, UR-03) | BE-030, FE-032 |
| BR-007 | Capture window | Accept samples with `click < ts_ms ≤ click + duration` | `CaptureWindow` | **Conflicts**: README confirms a start **3 s after** the press | BE-020 |
| BR-008 | Expected samples | `max(1, duration_ms / 1000)`, which assumes 1 Hz | `CaptureWindow.ExpectedSamples` | Sample rate unknown (README Q2, UR-02) | BE-021 |
| BR-009 | Detector choice | The serial with the newest `ts_ms` among the last **200** rows; refuse if another serial is within **5 s** | `GammaGuardCaptureDevices` | Values Unresolved (UR-10) | BE-024 |
| BR-010 | Time basis | Latest `ts_ms` must be ≥ 1e11 (Unix ms) and within **12 h** of the PC clock | `DeviceTime` | README: distinct clock domains must not be confused (§17) | BE-023 |
| BR-011 | Reading samples | Start **200** rows before the newest id; poll every **250 ms**; only rows with a larger id | `DeviceCaptureService` | README proposes bounded, read-only reads | BE-025 |
| BR-012 | Late rows | After the window, read again for up to **1.5 s**, or until the expected count is reached; the window never grows | same | README rule: boundaries reproducible despite late records | BE-025 |
| BR-013 | De-duplication | Each source row once; a second row with the same (device session, seq, ts) is ignored; other serials ignored | `CaptureWindow.Add` | — | BE-020 |
| BR-014 | Averaging | Arithmetic mean per field over finite values only; missing values never count as 0; a field with no values → empty | `SampleAveraging` | README: processing rules open (Q7) | BE-022 |
| BR-015 | Incomplete capture | Fewer samples than expected → the row is kept, shown as "n of m", with a warning. Zero samples → no row and an error | `MeasurementViewModel`, `MeasurementResult` | README: "mark incomplete" (Proposed policy); UR-17 | FE-030 |
| BR-016 | Row order | From ascending (empty depth last), then start time, then stored rows by id, then capture number | `MeasurementOrder` | — | BE-019 |
| BR-017 | Capture numbering | Per session, increasing, never reused after a delete | `MeasurementViewModel` | — | FE-034 |
| BR-018 | Session identity | One session per Drillhole ID; existing metadata is never overwritten | `MeasurementRepository.ResolveSessionAsync` | UR-30; case sensitivity UR-19 | BE-031 |
| BR-019 | Progress storage | Next From and Step stored in **centimetres** (REAL); converted back to whole mm | `MeasurementRepository` | — | BE-035 |
| BR-020 | Campaign | `RIGHT(PROSPECT, 4)`; NULL fields → `""` | `DrillholeRepository` | UR-08 | BE-006 (skipped) |
| BR-021 | Drillhole list cleaning | Drop empty Hole IDs; trim; de-duplicate case-insensitively (first wins) | `DrillholeCatalog.Build` | — | FE-013 |
| BR-022 | CSV neutralisation | User text starting (after spaces) with `=`, `+`, `-` or `@` is prefixed with `'` | `CsvExportService.Text` | UR-18 | BE-043 |
| BR-023 | CSV columns | `No, Capture No, Start Time, End Time, Sample, ts_ms, count0, count0_raw, hdm, hdm_raw, dose_rate, Drillhole, Project, Campaign, Operator, Notes, session_id`. Depth, step, temperature and pressure are not exported | `MeasurementColumns`, `CsvExportService` | README Q10, UR-04 | BE-042 |
| BR-024 | Status toasts | Errors 5 s, others 3 s; at most 4; identical toasts merged; status announced on transitions only | `ToastService`, `StatusAnnouncements` | — | FE-041, FE-042 |
| BR-025 | CT007-M identification | The name starts with `CT007-M-` (case-insensitive) or, if `ct007Address` is set, that address only | `Ct007BluetoothCheck` | — | FE-043 |
