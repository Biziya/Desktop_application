# Nonfunctional Requirements

The status labels are the same as in [functional-requirements.md](functional-requirements.md). **No numeric threshold is confirmed anywhere.** Where a number appears below, it is the current implementation (Inferred) or a test bound, not an acceptance criterion.

| ID | Area | Requirement | Status | Current implementation / evidence | Tests |
|---|---|---|---|---|---|
| NFR-001 | Compatibility | Windows only | **Confirmed** (README §2) | `net10.0-windows10.0.19041.0` (Windows 10 2004+), WPF + WinRT Bluetooth | Whole suite runs on Windows only |
| NFR-002 | Technology | WPF + C# + .NET frontend; C#/.NET backend | **Confirmed** (README §2, decision log) | .NET 10; WPF with hand-written MVVM (the README's proposed CommunityToolkit.Mvvm is not used); ASP.NET Core backend | — |
| NFR-003 | Maintainability | Frontend and backend are separate parts; the UI holds no acquisition, clock-mapping or selection logic | **Confirmed** (README §2, §17 rule) | **Partially met.** The projects are separate, but capture, persistence and export logic all run inside the `Frontend` process (in non-UI classes under `Calculation/`, `Pages/Measurement/*Repository*`). The `Backend` serves only drillhole data. See [technical-decisions](../architecture/technical-decisions.md) TD-02 | — |
| NFR-004 | Responsiveness | Capture triggers quickly without blocking the UI | **Confirmed** (README goal) | Async I/O; database and filter work on the thread pool; 150 ms search debounce; serial background save queue | FE-016, FE-026; dropdown latency target Unresolved (FE-017, UR-21) |
| NFR-005 | Performance | "Fast and low maintenance" | Confirmed (qualitative); **targets Unresolved** (UR-21, README Q11) | Tested with a 20,000-entry drillhole list; no backend paging; capture = duration + ≤ 1.5 s final read | FE-016; BE-007 and MAN-016 not implemented or run |
| NFR-006 | Accuracy | Align the correct measurements with the trigger; never claim accuracy beyond calibration | **Confirmed** (README §2, §17) | No calibration done; sample rate assumed 1 Hz (UR-02) | MAN-001…003 not run |
| NFR-007 | Data integrity | Measurements are stored completely and once | Inferred (README goal "preserve source records") | Single-transaction saves; UNIQUE (session, CaptureNo), (record, source); foreign keys on; migration in one transaction with an FK check | BE-031…038, INT-007, INT-010, INT-012; mutation-tested |
| NFR-008 | Source protection | Never write to the device-owned database | **Confirmed** (README §17) | Read-only, unpooled connections with a 2 s busy timeout | BE-027 (hash unchanged) |
| NFR-009 | Reliability | Keep working when the backend or a database is unavailable | Inferred | Cached drillhole list; status rows; save retries; capture errors are reported as toasts, never crashes | FE-010, FE-035, INT-003, INT-009, INT-010 |
| NFR-010 | Reliability | Restore sessions after a crash or restart | Proposed (README §13); per-drillhole restore Inferred | Data is durable (WAL SQLite); automatic restore of the last session not wired (UR-06) | E2E-002 not run; MAN-007 |
| NFR-011 | Security | Secrets not stored in the repository; access control as needed | **Unresolved** (UR-14) | Plain-text SQL credentials committed in `Backend/appsettings.json`; no authentication; Swagger always on | BE-009 and BE-010 not implemented |
| NFR-012 | Security | CSV export safe to open in spreadsheets | Inferred | Formula prefixes `= + - @` neutralised in user text; tab/CR not covered (UR-18) | BE-043; MAN-012 |
| NFR-013 | Diagnostics | Log important failures | Proposed (README §13 "log …") | **Backend:** `ILogger` error on SQL failure. **Frontend:** no logging; errors only as toasts | BE-002 (log asserted) |
| NFR-014 | Usability | Clear validation and status feedback | Inferred | Red borders; error toasts 5 s, others 3 s; at most 4 visible | FE-019, FE-024, FE-041 |
| NFR-015 | Accessibility | — | **Unresolved** (no target, UR-21) | `AutomationProperties.Name` on the main controls | MAN-009 not run |
| NFR-016 | Localisation | Correct under different Windows number formats and time zones | Inferred | Inputs accept `.` and `,`; storage and CSV use the invariant culture; times stored with their offset | BE-015/016 (3 cultures), BE-042 (fr-FR), MAN-010/011 |
| NFR-017 | Availability | The backend is optional for measuring | Inferred | Capture and persistence are local; only drillhole refresh needs the backend | E2E-003 not run; INT-003 |
| NFR-018 | Scalability | Single user, single PC | Inferred (README: no cloud or distributed services) | No multi-instance guard (UR-28); full drillhole table per request | BE-038 |
| NFR-019 | Compatibility | Open databases from older app versions | Inferred | Migrations v1–v5; newer schemas refused | BE-036, BE-037, INT-017 |
| NFR-020 | Interoperability | Agreed export contract (columns, units, time format, encoding) | **Unresolved** (README Q10, UR-04, UR-22) | See FR-034 | BE-042 asserts the current contract |
