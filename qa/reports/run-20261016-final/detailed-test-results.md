# Detailed Test Results — run-20261016-final

One row per planned scenario from the [test inventory](../../planning/test-inventory.md), in inventory order:
- **Priority** is the Phase 2 test priority (Impact × Likelihood). It is not defect severity.
- **Status** comes from [`run-final-results.csv`](../../execution/evidence/run-final-results.csv) for implemented tests, and from the [implementation status](../../generation/test-implementation-status.md) otherwise.
- **UR** lists the open requirement items an expectation depends on.
- Every executed expectation is **Inferred** from code (UR-01). No result verifies an approved requirement.

## Status totals

| Status | FE | BE | INT | E2E | MAN | Total |
|---|---|---|---|---|---|---|
| PASS | 47 | 33 | 17 | 0 | — | 97 |
| FAIL | 1 | 1 | 0 | 0 | — | 2 |
| SKIPPED (environment) | 0 | 1 | 0 | 4 | — | 5 |
| Not implemented (Unresolved 11, Blocked 2, Conditional 1) | 3 | 11 | 0 | 0 | — | 14 |
| Not executed (manual) | — | — | — | — | 17 | 17 |
| **Scenarios** | 51 | 46 | 17 | 4 | 17 | **135** |

Three scenarios have two test methods each, and both passed: FE-036, BE-024 and BE-027. That makes 107 methods in total: 100 pass, 2 fail, 5 skipped. No invalid results remain in the final run. Three test-code defects found earlier in Phase 4 were fixed before it (see the [execution report §4.2](../../execution/test-execution-report.md)).

## Failures

| Test ID | Message (abridged) | Classification | Defect |
|---|---|---|---|
| FE-005 | `InvalidOperationException: … requires an element of type 'Number', but the target element has type 'String'` | Confirmed application defect | [DEF-001](defect-report.md#def-001) |
| BE-030 | Same message | Confirmed application defect | [DEF-002](defect-report.md#def-002) |

## Skipped

| Test ID | Reason (from the runner) | Classification |
|---|---|---|
| BE-006 | `CT007_TEST_SQL` not set; no LocalDB | Environment unavailable |
| E2E-001…004 | No dedicated Windows test account (`CT007_E2E_DEDICATED_ACCOUNT` not set) | Environment unavailable |

## All scenarios

| Test ID | Module | Test Type | Status | Priority | UR | Evidence |
|---|---|---|---|---|---|---|
| [FE-001](../../planning/frontend-test-cases.md#fe-001) | F01 API client: Ok mapping | Unit | PASS | Medium (6) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [FE-002](../../planning/frontend-test-cases.md#fe-002) | F01 API client: HttpError | Unit | PASS | Medium (6) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [FE-003](../../planning/frontend-test-cases.md#fe-003) | F01 API client: Unreachable/timeout | Unit | PASS | High (12) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [FE-004](../../planning/frontend-test-cases.md#fe-004) | F01 API client: InvalidResponse | Unit | PASS | Medium (6) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [FE-005](../../planning/frontend-test-cases.md#fe-005) | F05 ApiSettings defaults | Unit | FAIL | Medium (6) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [FE-006](../../planning/frontend-test-cases.md#fe-006) | F05 Probe polling/overlap | Component | PASS | High (9) | UR-13 | [results CSV](../../execution/evidence/run-final-results.csv) |
| [FE-007](../../planning/frontend-test-cases.md#fe-007) | F05 CameOnline reload | Component | PASS | High (9) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [FE-008](../../planning/frontend-test-cases.md#fe-008) | F05 Loops stop on dispose | Component | PASS | Medium (4) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [FE-009](../../planning/frontend-test-cases.md#fe-009) | F01/F02 Cache then server | Component | PASS | Medium (8) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [FE-010](../../planning/frontend-test-cases.md#fe-010) | F01 Failed refresh keeps state | Component | PASS | High (12) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [FE-011](../../planning/frontend-test-cases.md#fe-011) | F01/F02 Empty list never overwrites | Component | PASS | Medium (8) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [FE-012](../../planning/frontend-test-cases.md#fe-012) | F01 Load reuse | Component | PASS | Medium (4) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [FE-013](../../planning/frontend-test-cases.md#fe-013) | F03 Catalog cleaning | Unit | PASS | Medium (8) | UR-19 | [results CSV](../../execution/evidence/run-final-results.csv) |
| [FE-014](../../planning/frontend-test-cases.md#fe-014) | F03 Ranking and groups | Unit | PASS | Medium (6) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [FE-015](../../planning/frontend-test-cases.md#fe-015) | F03 No implicit pick / echo | Component | PASS | High (12) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [FE-016](../../planning/frontend-test-cases.md#fe-016) | F03 Debounce, stale results | Component | PASS | High (9) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [FE-017](../../planning/frontend-test-cases.md#fe-017) | F03 Large-catalog latency | Performance | Not implemented — Unresolved | Medium (6) | UR-21 | [implementation status](../../generation/test-implementation-status.md) |
| [FE-018](../../planning/frontend-test-cases.md#fe-018) | F03 Unicode IDs | Component | PASS | Medium (6) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [FE-019](../../planning/frontend-test-cases.md#fe-019) | F04 Save without pick | Component | PASS | Medium (8) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [FE-020](../../planning/frontend-test-cases.md#fe-020) | F04 Save success, autofill | Component | PASS | Medium (8) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [FE-021](../../planning/frontend-test-cases.md#fe-021) | F04/F14 Edit disarms | Component | PASS | Medium (6) | UR-09 | [results CSV](../../execution/evidence/run-final-results.csv) |
| [FE-022](../../planning/frontend-test-cases.md#fe-022) | F03/F11 Selection lock | Component | PASS | High (10) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [FE-023](../../planning/frontend-test-cases.md#fe-023) | F10 Navigation state | Component | PASS | Medium (4) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [FE-024](../../planning/frontend-test-cases.md#fe-024) | F12 Depth input validation | Component | PASS | High (12) | UR-20 | [results CSV](../../execution/evidence/run-final-results.csv) |
| [FE-025](../../planning/frontend-test-cases.md#fe-025) | F14 Idle → Ready | Component | PASS | Medium (8) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [FE-026](../../planning/frontend-test-cases.md#fe-026) | F14 Capture cycle, no double trigger | Component | PASS | High (15) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [FE-027](../../planning/frontend-test-cases.md#fe-027) | F14 Stop during Capturing | Component | PASS | High (12) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [FE-028](../../planning/frontend-test-cases.md#fe-028) | F14 Stop during Processing | Component | PASS | High (12) | UR-23 | [results CSV](../../execution/evidence/run-final-results.csv) |
| [FE-029](../../planning/frontend-test-cases.md#fe-029) | F12 Depth frozen and advance | Component | PASS | High (15) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [FE-030](../../planning/frontend-test-cases.md#fe-030) | F14/F15 Capture outcome feedback | Component | PASS | High (12) | UR-17 | [results CSV](../../execution/evidence/run-final-results.csv) |
| [FE-031](../../planning/frontend-test-cases.md#fe-031) | F14 Late progress ignored | Component | PASS | High (9) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [FE-032](../../planning/frontend-test-cases.md#fe-032) | F13 Duration editor | Component | PASS | Medium (6) | UR-03 | [results CSV](../../execution/evidence/run-final-results.csv) |
| [FE-033](../../planning/frontend-test-cases.md#fe-033) | F16 Sorted insert, renumber | Component | PASS | Medium (8) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [FE-034](../../planning/frontend-test-cases.md#fe-034) | F17 Delete flow | Component | PASS | High (10) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [FE-035](../../planning/frontend-test-cases.md#fe-035) | F18 Save failure and retry | Component | PASS | High (15) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [FE-036](../../planning/frontend-test-cases.md#fe-036) | F11 Drillhole switch | Component | PASS (2 tests) | High (15) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [FE-037](../../planning/frontend-test-cases.md#fe-037) | F11 Stored metadata wins | Component | PASS | Medium (6) | UR-30 | [results CSV](../../execution/evidence/run-final-results.csv) |
| [FE-038](../../planning/frontend-test-cases.md#fe-038) | F11 New Measurement | Component | PASS | Medium (8) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [FE-039](../../planning/frontend-test-cases.md#fe-039) | F19 Export command | Component | PASS | Medium (6) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [FE-040](../../planning/frontend-test-cases.md#fe-040) | F03/F11 Measured group refresh | Component | PASS | Medium (4) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [FE-041](../../planning/frontend-test-cases.md#fe-041) | F09 Toast rules | Unit | PASS | Medium (4) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [FE-042](../../planning/frontend-test-cases.md#fe-042) | F09 Status announcements | Component | PASS | Medium (4) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [FE-043](../../planning/frontend-test-cases.md#fe-043) | F06 Bluetooth identification/status | Component | PASS | Medium (6) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [FE-044](../../planning/frontend-test-cases.md#fe-044) | F07/F08 DB status rows, Retry | Component | PASS | Medium (6) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [FE-045](../../planning/frontend-test-cases.md#fe-045) | F06/F08 DeviceSettings.Load | Unit | PASS | Medium (6) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [FE-046](../../planning/frontend-test-cases.md#fe-046) | F16 Cell formatting | Unit | PASS | Medium (6) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [FE-047](../../planning/frontend-test-cases.md#fe-047) | F16 Column layout | Unit | PASS | Low (2) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [FE-048](../../planning/frontend-test-cases.md#fe-048) | F14/F18 Close during work | Component | PASS | High (15) | UR-23 | [results CSV](../../execution/evidence/run-final-results.csv) |
| [FE-049](../../planning/frontend-test-cases.md#fe-049) | F20 Demo ts_ms isolation | Component | PASS | Low (3) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [FE-050](../../planning/frontend-test-cases.md#fe-050) | F20 Chart data mapping | Component | Not implemented — Unresolved | Medium (4) | UR-05 | [implementation status](../../generation/test-implementation-status.md) |
| [FE-051](../../planning/frontend-test-cases.md#fe-051) | F11 Restore on start-up | Component | Not implemented — Unresolved | Low (3) | UR-06 | [implementation status](../../generation/test-implementation-status.md) |
| [BE-001](../../planning/backend-test-cases.md#be-001) | F01 Drillholes 200 contract | API | PASS | Medium (8) | UR-24 | [results CSV](../../execution/evidence/run-final-results.csv) |
| [BE-002](../../planning/backend-test-cases.md#be-002) | F01 SqlException → 503 | API | PASS | Medium (8) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [BE-003](../../planning/backend-test-cases.md#be-003) | F01 Missing connection string | API | Not implemented — Unresolved | High (9) | UR-12 | [implementation status](../../generation/test-implementation-status.md) |
| [BE-004](../../planning/backend-test-cases.md#be-004) | F01 Non-SQL exceptions | API | Not implemented — Unresolved | Medium (6) | UR-12 | [implementation status](../../generation/test-implementation-status.md) |
| [BE-005](../../planning/backend-test-cases.md#be-005) | F01 Request cancellation | API | PASS | Medium (4) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [BE-006](../../planning/backend-test-cases.md#be-006) | F01 SQL projection, NULLs, campaign | Integration | SKIPPED (environment) | High (12) | UR-08, UR-25 | [results CSV](../../execution/evidence/run-final-results.csv) |
| [BE-007](../../planning/backend-test-cases.md#be-007) | F01 Large table | Performance | Not implemented — Unresolved | Medium (6) | UR-21, UR-25 | [implementation status](../../generation/test-implementation-status.md) |
| [BE-008](../../planning/backend-test-cases.md#be-008) | F01/F21/F22 OpenAPI document | API | PASS | Medium (4) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [BE-009](../../planning/backend-test-cases.md#be-009) | Auth posture, error leakage | Security | Not implemented — Unresolved | High (12) | UR-14 | [implementation status](../../generation/test-implementation-status.md) |
| [BE-010](../../planning/backend-test-cases.md#be-010) | Committed credentials | Security | Not implemented — Unresolved | Critical (25) | UR-14 | [implementation status](../../generation/test-implementation-status.md) |
| [BE-011](../../planning/backend-test-cases.md#be-011) | F05 Routing/reachability contract | API | PASS | Medium (6) | UR-13 | [results CSV](../../execution/evidence/run-final-results.csv) |
| [BE-012](../../planning/backend-test-cases.md#be-012) | F21 Bluetooth endpoint | API | Not implemented — Unresolved | Low (2) | UR-07 | [implementation status](../../generation/test-implementation-status.md) |
| [BE-013](../../planning/backend-test-cases.md#be-013) | F22 CT008 selection rule | Unit | Not implemented — Blocked | Low (2) | UR-07, UR-27 | [implementation status](../../generation/test-implementation-status.md) |
| [BE-014](../../planning/backend-test-cases.md#be-014) | F22 Cache, timeout, concurrency | API | Not implemented — Unresolved | Low (2) | UR-07 | [implementation status](../../generation/test-implementation-status.md) |
| [BE-015](../../planning/backend-test-cases.md#be-015) | F12 Parse metres | Unit | PASS | High (15) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [BE-016](../../planning/backend-test-cases.md#be-016) | F12 Parse centimetres | Unit | PASS | High (15) | UR-20 | [results CSV](../../execution/evidence/run-final-results.csv) |
| [BE-017](../../planning/backend-test-cases.md#be-017) | F12 Depth formatting | Unit | PASS | Medium (8) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [BE-018](../../planning/backend-test-cases.md#be-018) | F12 No cumulative drift | Unit | PASS | High (10) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [BE-019](../../planning/backend-test-cases.md#be-019) | F16 MeasurementOrder | Unit | PASS | Medium (8) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [BE-020](../../planning/backend-test-cases.md#be-020) | F15 Capture window | Unit | PASS | Critical (20) | UR-01 | [results CSV](../../execution/evidence/run-final-results.csv) |
| [BE-021](../../planning/backend-test-cases.md#be-021) | F15 Expected samples | Unit | PASS | Medium (8) | UR-02 | [results CSV](../../execution/evidence/run-final-results.csv) |
| [BE-022](../../planning/backend-test-cases.md#be-022) | F15 Averaging | Unit | PASS | High (15) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [BE-023](../../planning/backend-test-cases.md#be-023) | F15 Time basis check | Unit | PASS | High (15) | UR-10 | [results CSV](../../execution/evidence/run-final-results.csv) |
| [BE-024](../../planning/backend-test-cases.md#be-024) | F15 Detector pick | Unit | PASS (2 tests) | High (15) | UR-10 | [results CSV](../../execution/evidence/run-final-results.csv) |
| [BE-025](../../planning/backend-test-cases.md#be-025) | F15 Polling, final read, lookback | Unit | PASS | Critical (20) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [BE-026](../../planning/backend-test-cases.md#be-026) | F15 Time check order, cancellation | Unit | PASS | High (15) | UR-29 | [results CSV](../../execution/evidence/run-final-results.csv) |
| [BE-027](../../planning/backend-test-cases.md#be-027) | F15/F08 Vendor DB never modified | Integration | PASS (2 tests) | Critical (15) | UR-15 | [results CSV](../../execution/evidence/run-final-results.csv) |
| [BE-028](../../planning/backend-test-cases.md#be-028) | F07 SqliteFileCheck states | Integration | PASS | Medium (6) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [BE-029](../../planning/backend-test-cases.md#be-029) | F08 Gamma Guard discovery | Integration | PASS | High (9) | UR-16 | [results CSV](../../execution/evidence/run-final-results.csv) |
| [BE-030](../../planning/backend-test-cases.md#be-030) | F13 Duration settings | Unit | FAIL | Medium (6) | UR-03 | [results CSV](../../execution/evidence/run-final-results.csv) |
| [BE-031](../../planning/backend-test-cases.md#be-031) | F11 Session resolution | Integration | PASS | High (15) | UR-19 | [results CSV](../../execution/evidence/run-final-results.csv) |
| [BE-032](../../planning/backend-test-cases.md#be-032) | F18 Atomic save | Integration | PASS | Critical (20) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [BE-033](../../planning/backend-test-cases.md#be-033) | F18 Session isolation, unique CaptureNo | Integration | PASS | High (10) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [BE-034](../../planning/backend-test-cases.md#be-034) | F17 Delete transaction | Integration | PASS | High (15) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [BE-035](../../planning/backend-test-cases.md#be-035) | F18 Round-trip precision | Integration | PASS | High (15) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [BE-036](../../planning/backend-test-cases.md#be-036) | F18 Migrations v1–v4 → v5 | Integration | PASS | High (15) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [BE-037](../../planning/backend-test-cases.md#be-037) | F18 Migration refusals, rollback | Integration | PASS | High (15) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [BE-038](../../planning/backend-test-cases.md#be-038) | F18 Migration idempotence/concurrency | Integration | PASS | Medium (8) | UR-28 | [results CSV](../../execution/evidence/run-final-results.csv) |
| [BE-039](../../planning/backend-test-cases.md#be-039) | F03/F11 Measured ordering | Integration | PASS | Medium (4) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [BE-040](../../planning/backend-test-cases.md#be-040) | F02 DrillholeCache | Integration | PASS | Medium (8) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [BE-041](../../planning/backend-test-cases.md#be-041) | F18 Legacy folder move | Integration | Not implemented — Blocked | Medium (8) | UR-27 | [implementation status](../../generation/test-implementation-status.md) |
| [BE-042](../../planning/backend-test-cases.md#be-042) | F19 CSV structure | Unit | PASS | High (12) | UR-04, UR-22 | [results CSV](../../execution/evidence/run-final-results.csv) |
| [BE-043](../../planning/backend-test-cases.md#be-043) | F19 CSV injection, session mixing | Security | PASS | High (12) | UR-18 | [results CSV](../../execution/evidence/run-final-results.csv) |
| [BE-044](../../planning/backend-test-cases.md#be-044) | F19 Export file handling | Integration | Not implemented — Conditional | Medium (8) | UR-27 | [implementation status](../../generation/test-implementation-status.md) |
| [BE-045](../../planning/backend-test-cases.md#be-045) | F23 check-db.ps1 on v5 | Integration | Not implemented — Unresolved | High (10) | UR-11 | [implementation status](../../generation/test-implementation-status.md) |
| [BE-046](../../planning/backend-test-cases.md#be-046) | F18 Text fields | Integration | PASS | Medium (6) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [INT-001](../../planning/integration-test-cases.md#int-001) | F01/F04/F19 Field mapping end to end | Integration | PASS | High (12) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [INT-002](../../planning/integration-test-cases.md#int-002) | F01/F05 503 propagation | Integration | PASS | High (9) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [INT-003](../../planning/integration-test-cases.md#int-003) | F01/F02/F05 Offline and recovery | Integration | PASS | High (12) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [INT-004](../../planning/integration-test-cases.md#int-004) | F01/F05 Server slower than timeout | Integration | PASS | High (9) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [INT-005](../../planning/integration-test-cases.md#int-005) | F01 Contract drift guard | Integration | PASS | High (12) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [INT-006](../../planning/integration-test-cases.md#int-006) | F02 Cache → offline restart | Integration | PASS | Medium (8) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [INT-007](../../planning/integration-test-cases.md#int-007) | F15/F18 Capture → persist → reload | Integration | PASS | Critical (20) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [INT-008](../../planning/integration-test-cases.md#int-008) | F15 Concurrent Gamma Guard writer | Integration | PASS | Critical (20) | UR-15 | [results CSV](../../execution/evidence/run-final-results.csv) |
| [INT-009](../../planning/integration-test-cases.md#int-009) | F15/F08 GG locked/missing | Integration | PASS | High (12) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [INT-010](../../planning/integration-test-cases.md#int-010) | F18/F07 App DB failure and recovery | Integration | PASS | High (15) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [INT-011](../../planning/integration-test-cases.md#int-011) | F11/F12 Reopen measured drillhole | Integration | PASS | High (15) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [INT-012](../../planning/integration-test-cases.md#int-012) | F11 No session mixing | Integration | PASS | High (15) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [INT-013](../../planning/integration-test-cases.md#int-013) | F17 Delete → reload → export | Integration | PASS | Medium (8) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [INT-014](../../planning/integration-test-cases.md#int-014) | F16/F19 Display = DB = CSV | Integration | PASS | High (15) | UR-17, UR-22 | [results CSV](../../execution/evidence/run-final-results.csv) |
| [INT-015](../../planning/integration-test-cases.md#int-015) | F08/F15 Status vs capture | Integration | PASS | High (9) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [INT-016](../../planning/integration-test-cases.md#int-016) | F07 DB status under lock | Integration | PASS | Medium (4) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [INT-017](../../planning/integration-test-cases.md#int-017) | F18 Upgrade then measure | Integration | PASS | High (15) | — | [results CSV](../../execution/evidence/run-final-results.csv) |
| [E2E-001](../../planning/integration-test-cases.md#e2e-001) | Main journey | E2E | SKIPPED (environment) | High (15) | UR-27 | [results CSV](../../execution/evidence/run-final-results.csv) |
| [E2E-002](../../planning/integration-test-cases.md#e2e-002) | Restart persistence | E2E | SKIPPED (environment) | High (15) | UR-27 | [results CSV](../../execution/evidence/run-final-results.csv) |
| [E2E-003](../../planning/integration-test-cases.md#e2e-003) | Offline operation | E2E | SKIPPED (environment) | High (12) | UR-27 | [results CSV](../../execution/evidence/run-final-results.csv) |
| [E2E-004](../../planning/integration-test-cases.md#e2e-004) | Delete via real dialog | E2E | SKIPPED (environment) | Medium (6) | UR-27 | [results CSV](../../execution/evidence/run-final-results.csv) |
| [MAN-001](../../planning/manual-test-cases.md#man-001) | F15 Live capture accuracy | Manual | Not executed (manual) | Critical (20) | UR-02, UR-15, UR-26 | — |
| [MAN-002](../../planning/manual-test-cases.md#man-002) | F14/F15 Click-time alignment | Manual | Not executed (manual) | Critical (20) | UR-01, UR-10, UR-26 | — |
| [MAN-003](../../planning/manual-test-cases.md#man-003) | F15 Sample rate | Manual | Not executed (manual) | High (15) | UR-02, UR-26 | — |
| [MAN-004](../../planning/manual-test-cases.md#man-004) | F15 Two detectors | Manual | Not executed (manual) | Medium (8) | UR-10, UR-26 | — |
| [MAN-005](../../planning/manual-test-cases.md#man-005) | F06 Bluetooth states | Manual | Not executed (manual) | Medium (6) | UR-26 | — |
| [MAN-006](../../planning/manual-test-cases.md#man-006) | F14 Right-click matrix | Manual | Not executed (manual) | High (15) | — | — |
| [MAN-007](../../planning/manual-test-cases.md#man-007) | F14/F18 Close/kill during work | Manual | Not executed (manual) | High (15) | UR-23 | — |
| [MAN-008](../../planning/manual-test-cases.md#man-008) | F16/F20 Layout, DPI, rendering | Manual | Not executed (manual) | Medium (6) | — | — |
| [MAN-009](../../planning/manual-test-cases.md#man-009) | F03/F04 Keyboard and accessibility | Manual | Not executed (manual) | High (9) | UR-21 | — |
| [MAN-010](../../planning/manual-test-cases.md#man-010) | F12/F19 Regional settings | Manual | Not executed (manual) | High (12) | UR-04 | — |
| [MAN-011](../../planning/manual-test-cases.md#man-011) | F16/F19 Time zone, DST | Manual | Not executed (manual) | High (9) | UR-22 | — |
| [MAN-012](../../planning/manual-test-cases.md#man-012) | F19 CSV in Excel | Manual | Not executed (manual) | High (12) | UR-18 | — |
| [MAN-013](../../planning/manual-test-cases.md#man-013) | F19 Real Save dialog | Manual | Not executed (manual) | Medium (6) | — | — |
| [MAN-014](../../planning/manual-test-cases.md#man-014) | F08 GG discovery on targets | Manual | Not executed (manual) | High (12) | UR-16, UR-26 | — |
| [MAN-015](../../planning/manual-test-cases.md#man-015) | F05 Foreign service on port | Manual | Not executed (manual) | Medium (6) | UR-13 | — |
| [MAN-016](../../planning/manual-test-cases.md#man-016) | F11/F18 Long-session soak | Performance | Not executed (manual) | Medium (6) | UR-21 | — |
| [MAN-017](../../planning/manual-test-cases.md#man-017) | F17 Delete dialog usability | Manual | Not executed (manual) | Medium (4) | — | — |