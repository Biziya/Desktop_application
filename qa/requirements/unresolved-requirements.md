# Unresolved Requirements and Discrepancies

This file consolidates the open requirement questions. The detailed list from test planning stays in [`qa/planning/unresolved-requirements.md`](../planning/unresolved-requirements.md) (UR-01…30); it is referenced here, not copied.

## 1. Implementation versus Confirmed requirements (README)

| ID | Confirmed requirement (root README) | Implementation | Affected | Decision needed |
|---|---|---|---|---|
| DIS-01 | Trigger = **separate Bluetooth button** (§2) | Right mouse click anywhere in the armed window (`MainWindow.xaml.cs`). Bluetooth is used only to monitor the detector's connection | FR-022, FR-011 | Is the right-click an accepted replacement, an interim solution, or must the Bluetooth trigger be built? |
| DIS-02 | Capture starts **3 s after** the physical press (§2; decision log "Confirmed") | The window starts at the click: `click < ts ≤ click + duration` (`CaptureWindow`) | FR-023, BR-007 | Is the 3 s delay still required? If so, implementing it is a production change |
| DIS-03 | Frontend and backend separated; UI free of acquisition and selection logic (§2, §17) | Separate projects, but capture, persistence and export run in the frontend process. The backend serves drillhole data only | NFR-003 | Accept the in-process placement (the README itself recommends in-process at first), or move logic to the backend? |
| DIS-04 | Proposed: CommunityToolkit.Mvvm (§2 proposal column) | Hand-written MVVM | NFR-002 | None required (it was only a proposal) |

## 2. Questions the README lists as open (§16): current status

| README question | Status now | Related |
|---|---|---|
| Q1 Measurement SQLite schema | Assumed from the code (`samples`, `detector_status`); not verified against a real device | UR-15, MAN-001 |
| Q2 Sampling behaviour | 1 Hz assumed | UR-02, MAN-003 |
| Q3 Bluetooth trigger protocol | Not implemented | DIS-01 |
| Q4 Synchronisation reference and acceptable error | No calibration | NFR-006, MAN-002 |
| Q5 Capture duration default and range | 2–10 s, default 5 (implemented, not confirmed) | UR-03 |
| Q6 Completeness evidence | 1.5 s final read; incomplete rows kept | UR-17 |
| Q7 Processing rules | Arithmetic mean per field | BR-014 |
| Q8 Storage policy | Raw samples of each capture kept (`CaptureSamples`) | — |
| Q9 Independent backend lifecycle | No: capture runs only while the app is open | DIS-03 |
| Q10 Export contract | Fixed columns, without depths (BR-023) | UR-04, UR-22 |
| Q11 Performance targets | None defined | UR-21 |

## 3. Behaviour needing a product decision (from QA phases)

The full descriptions are in [`qa/planning/unresolved-requirements.md`](../planning/unresolved-requirements.md). The most important:

| UR | Topic | FR / NFR |
|---|---|---|
| UR-01 | Which document is authoritative (README draft vs code) | All |
| UR-06 | Restore the last session on start-up? | FR-018 |
| UR-09 | Disarm when Notes or Operator is edited? | FR-009 |
| UR-12 | HTTP status for non-SQL backend failures | FR-038 |
| UR-13 | "Any HTTP answer = Online" | FR-010 |
| UR-14 | Security posture, committed credentials | NFR-011, FR-040 |
| UR-17 | Incomplete captures: keep, mark or reject? | BR-015 |
| UR-23 | Data-loss tolerance on close or Stop | FR-037 |
| UR-30 | Stored session metadata wins over the form? | FR-015 |

## 4. Known defects and suspected issues (not requirements, but they affect them)

| ID | Summary | Requirement affected | Source |
|---|---|---|---|
| DEF-001 | A non-numeric value in `api-settings.json` throws instead of using defaults | FR-036 | [defect report](../reports/run-20261016-final/defect-report.md) |
| DEF-002 | The same for `captureDurationSeconds` | FR-036, FR-024 | same |
| SUS-01 | Committed SQL credentials | NFR-011 | same |
| SUS-02 | `tools/check-db.ps1` still targets schema v4 | NFR-019 | same |
| SUS-09 | `DrillholeCache` leaks a connection on a corrupt database | NFR-009 | [Phase 8 coverage review](../maintenance/maint-20261016/coverage-review.md) |
