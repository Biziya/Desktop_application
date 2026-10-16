# Coverage Analysis — run-20261016-final

All numbers come from the [test inventory](../../planning/test-inventory.md), the [implementation status](../../generation/test-implementation-status.md) and [`run-final-results.csv`](../../execution/evidence/run-final-results.csv). They count scenarios unless a row says otherwise.

## 1. Planned → implemented → executed

| Stage | Count | Note |
|---|---|---|
| Planned | 135 | 118 automated-eligible + 17 manual |
| Implemented | 104 (107 methods) | 88.1 % of 118 |
| Not implemented | 14 | 11 Unresolved, 2 Blocked (BE-013, BE-041), 1 Conditional (BE-044) |
| Executed | 99 (102 methods) | 5 implemented scenarios skipped: BE-006, E2E-001…004 |
| Passed / failed | 97 / 2 | Pass rate 98.0 % (97/99); 100/102 by method |
| Manual executed | 0 of 17 | |

**By priority:**

| Priority | Planned | Pass | Fail | Skip | Not implemented | Manual |
|---|---|---|---|---|---|---|
| Critical | 9 | 6 | 0 | 0 | 1 | 2 |
| High | 61 | 46 | 0 | 4 | 3 | 8 |
| Medium | 59 | 43 | 2 | 1 | 6 | 7 |
| Low | 6 | 2 | 0 | 0 | 4 | 0 |

## 2. Requirement and code coverage

- **Requirement coverage: not available.** No approved requirements exist (UR-01). Every planned scenario is linked to a feature, but "linked" here means linked to inferred behaviour, not to a verified requirement.
- **Code coverage: not available.** No coverage tool ran in Phase 4. No percentage is estimated.

## 3. Feature coverage (Phase 1 features F01–F23)

A scenario linked to several features counts once for each of them. "Executed pass" means tested by an automated scenario that passed.

| Feature | Planned | Pass | Fail | Skip | Not impl. | Manual | Assessment |
|---|---|---|---|---|---|---|---|
| F01 Drillhole list (API) | 21 | 17 | 0 | 1 | 3 | 0 | Good. The real SQL query was not executed (BE-006) |
| F02 Offline cache | 5 | 5 | 0 | 0 | 0 | 0 | Good |
| F03 Drillhole search/selection | 10 | 8 | 0 | 0 | 1 | 1 | Good; latency target missing (FE-017) |
| F04 Core form / Save & Continue | 5 | 4 | 0 | 0 | 0 | 1 | Good |
| F05 Connectivity indicator | 9 | 7 | **1** | 0 | 0 | 1 | DEF-001 |
| F06 Bluetooth monitoring | 3 | 2 | 0 | 0 | 0 | 1 | Logic only; real WinRT Bluetooth untested |
| F07 Measurement DB status | 4 | 4 | 0 | 0 | 0 | 0 | Good |
| F08 Gamma Guard discovery/status | 7 | 6 | 0 | 0 | 0 | 1 | Real install layout unverified (MAN-014) |
| F09 Toasts/announcements | 2 | 2 | 0 | 0 | 0 | 0 | Good |
| F10 Navigation | 1 | 1 | 0 | 0 | 0 | 0 | ViewModel only; XAML untested |
| F11 Sessions | 11 | 9 | 0 | 0 | 1 | 1 | Restore-on-start untested (UR-06) |
| F12 Depth inputs | 8 | 7 | 0 | 0 | 0 | 1 | Good |
| F13 Capture duration | 2 | 1 | **1** | 0 | 0 | 0 | DEF-002 |
| F14 Measurement mode / right-click trigger | 11 | 8 | 0 | 0 | 0 | 3 | **The real mouse trigger is untested** (E2E skipped; MAN-006 manual) |
| F15 Device capture | 17 | 13 | 0 | 0 | 0 | 4 | **Simulated Gamma Guard only**; real accuracy unverified |
| F16 Results table | 7 | 5 | 0 | 0 | 0 | 2 | Formatting logic only; rendering manual |
| F17 Delete | 4 | 3 | 0 | 0 | 0 | 1 | Real dialog untested (E2E-004 skipped) |
| F18 Persistence/migrations | 15 | 12 | 0 | 0 | 1 | 2 | Good; legacy folder move blocked (BE-041) |
| F19 CSV export | 10 | 5 | 0 | 0 | 1 | 4 | Serialisation good; **real file dialog untested** (BE-044, MAN-013) |
| F20 Chart / demo ts_ms | 3 | 1 | 0 | 0 | 1 | 1 | Chart semantics unresolved (UR-05) |
| F21 Bluetooth endpoint (unused) | 2 | 1 | 0 | 0 | 1 | 0 | Only OpenAPI listing tested |
| F22 CT008 discovery endpoint (unused) | 3 | 1 | 0 | 0 | 2 | 0 | Only OpenAPI listing tested |
| F23 check-db.ps1 | 1 | 0 | 0 | 0 | 1 | 0 | **Untested** (SUS-02) |
| Cross-cutting (security, E2E) | 6 | 0 | 0 | 4 | 2 | 0 | **No executed evidence** |

No Phase 1 feature is without planned tests. **F23** and the cross-cutting security and E2E scenarios have no executed evidence.

## 4. Coverage by layer (executed evidence only)

| Layer | Evidence | Limits |
|---|---|---|
| Frontend logic (ViewModels, client, formatting) | 47 FE scenarios pass, 1 fails | No XAML, rendering or input testing |
| Business and data logic | Capture window, averaging, time basis, depth arithmetic, ordering, CSV: all pass | Based on the inferred rules (UR-01, 02, 10) |
| Database (app SQLite) | CRUD, transactions, rollback, migrations v1–v5, precision, concurrency: all pass | Real Gamma Guard schema unverified (UR-15) |
| Backend API | Contract, 503 mapping, cancellation, routing, OpenAPI: pass | `Program.cs` mirrored, not executed (Phase 3 D8); no real SQL Server; auth/security untested |
| Integration | 17/17 pass, including real HTTP and the capture → storage pipeline | Simulated Gamma Guard writer |
| End-to-end | **None executed** | All 4 skipped |

## 5. High-risk gaps (in priority order)

1. **The operator workflow through the real UI.** E2E-001…004 and MAN-006 have never run, so the right-click trigger, the file dialog and the delete dialog are unverified.
2. **Measurement accuracy against real hardware.** MAN-001, MAN-002 and MAN-003 are Critical/High and not executed; capture rules are only checked against a simulation.
3. **Security.** BE-009 and BE-010 are not implemented, and SUS-01 is open.
4. **The real SQL query** (BE-006 skipped) and the backend error statuses (BE-003/004 Unresolved).
5. **Requirement-based expectations.** All 104 implemented scenarios assert inferred behaviour; 13 scenarios are Unresolved or Blocked on UR items.
6. **Lower risk:** performance targets (FE-017, BE-007, MAN-016), the support script (BE-045), the legacy data move (BE-041), chart semantics (FE-050).
