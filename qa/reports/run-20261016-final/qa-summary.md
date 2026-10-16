# QA Summary — SPP CT-007 Measurement Studio

| | |
|---|---|
| **Run ID** | `run-20261016-final`. Phase 4 recorded no run ID, so this ID was assigned in Phase 5 for the final run, whose evidence is in [`qa/execution/evidence/run-final-*`](../../execution/evidence/) (written 2026-10-16 20:27 +08:00) |
| **Branch / code tested** | `main`; production code at commit `277d581`. The later commits `152561b` and `cc031a5` add only `Tests/` and `qa/` files, so production code at `cc031a5` is identical to what was tested |
| **Test suite** | `Tests/CT007.Tests` as committed in `cc031a5`, including the Phase 4 test-code fixes T1–T3 |
| **Execution environment** | Windows 11 Pro 10.0.26200, .NET SDK 10.0.402. Built and run from a scratch copy, with packages restored offline ([execution report §2](../../execution/test-execution-report.md)) |
| **Overall assessment** | **Conditional** (see §4) |

## 1. Application and scope

A WPF desktop app for drill-core gamma measurements, with a small ASP.NET Core API that serves drillhole reference data from SQL Server. Captures read samples from the vendor's Gamma Guard SQLite database and store averaged rows in the app's own SQLite database.

**Evaluated by automated tests:**
- frontend ViewModels and the API client;
- capture and averaging logic;
- the SQLite repository and its migrations;
- CSV serialisation;
- the backend drillhole API, through an in-process host;
- cross-layer workflows over HTTP and the capture → storage pipeline.

**Not evaluated:**
- the desktop UI (E2E not run);
- a real SQL Server;
- real Gamma Guard or detector hardware;
- the 17 manual scenarios;
- 14 planned scenarios that were not implemented.

## 2. Key metrics (from verified evidence)

| Metric | Value |
|---|---|
| Planned scenarios | 135 (118 automated-eligible, 17 manual) |
| Implemented automated scenarios / test methods | 104 / 107 |
| Executed (test methods) | 102 (100 pass, 2 fail); 5 skipped because their environment was unavailable |
| **Execution pass rate** | **98.0 %** = 100 / (100 + 2) test methods. By scenario: 97 / 99 = 98.0 % |
| **Automation implementation rate** | **88.1 %** = 104 / 118 |
| Requirement coverage | **Not available.** There are no approved requirements (UR-01); every expectation is inferred from code |
| Code coverage | **Not available.** No coverage tool was run |
| Critical-priority scenarios (9) | 6 passed; 1 not implemented (BE-010); 2 manual, not executed (MAN-001, MAN-002) |

Details: [detailed-test-results.md](detailed-test-results.md), [coverage-analysis.md](coverage-analysis.md).

## 3. Critical findings

| ID | Severity | Finding | Evidence |
|---|---|---|---|
| DEF-001 | High | `ApiSettings.Load` throws an uncaught exception for a non-numeric value in `api-settings.json`, instead of falling back to the defaults. Because it runs while the window is built, this would probably stop the app from starting (inferred) | FE-005 FAIL |
| DEF-002 | Medium | `CaptureDurationSettings.Load` does the same for a non-numeric `captureDurationSeconds` | BE-030 FAIL |
| SUS-01 | Critical (security) | Plain-text SQL Server credentials are committed in `Backend/appsettings.json`. Found by static review; not covered by an executed test | Phase 1 review; BE-010 not implemented |

Seven other suspected issues need investigation: [defect-report.md](defect-report.md).

**Passed critical-path evidence.** These tests pass against the current implementation:
- the capture window, polling and averaging;
- the Gamma Guard file is never modified;
- the record, samples and progress save together, all or nothing;
- schema migrations v1–v4 → v5, including refusals and rollback;
- capture → storage → reload;
- capture while Gamma Guard writes concurrently;
- session isolation, and save-failure retries;
- HTTP error, timeout and offline recovery.

## 4. Overall assessment: Conditional

No critical defect was confirmed by execution, and the essential automated workflows pass. Acceptance is still not supported, because:
1. **Two confirmed defects** (DEF-001, DEF-002) can each probably stop start-up because of one bad value in a settings file.
2. **Every expectation is inferred from code.** No approved requirements exist, so a pass shows the code is consistent with itself, not that it is correct.
3. **The main operator workflow was never run through the real UI.** E2E-001…004 were skipped, and the right-click trigger is manual only (MAN-006).
4. **No real external systems.** Gamma Guard was simulated, SQL Server was not used (BE-006 skipped), and hardware accuracy (MAN-001…003) is unverified.
5. **Committed credentials and open security questions** (SUS-01, UR-14).

This is a QA recommendation, not a release approval.

## 5. Recommended next actions

1. Fix DEF-001 and DEF-002, then rerun FE-005 and BE-030.
2. Remove and rotate the committed SQL credentials (SUS-01).
3. Run BE-006 against a local `CT007_Test` database, and E2E-001…004 on a dedicated Windows test account.
4. Run the Critical and High manual cases on the hardware bench: MAN-001, 002, 003, 006, 007.
5. Get product-owner answers to the open requirements, starting with UR-01, 02, 04, 17 and 23.

The full list is in [recommendations.md](recommendations.md).
