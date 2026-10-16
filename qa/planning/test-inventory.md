# Test Inventory

The one central list of planned scenarios. Details are in the linked documents; this table doesn't repeat them.

- **Priority:** Impact × Likelihood, mapped as described in [test-plan.md](test-plan.md) §4. **Score** is I×L; `ovr` marks a Critical override.
- **Automation:** Automatable assumes UR-24 (a buildable test project) is resolved. Conditional, Blocked and Manual are defined in the test plan.
- **Expectation:** **Inferred** (from code) or **Unresolved**. None is Confirmed (UR-01). **UR** lists the items in [unresolved-requirements.md](unresolved-requirements.md) that the case depends on.
- **Features** are F01–F23 from `../analysis/feature-inventory.md`.

## Summary

| Group | Count | Critical | High | Medium | Low | Automatable | Conditional | Blocked | Manual | Unresolved expectations |
|---|---|---|---|---|---|---|---|---|---|---|
| FE | 51 | 0 | 18 | 30 | 3 | 46 | 4 | 1 | 0 | 3 |
| BE | 46 | 5 | 20 | 18 | 3 | 28 | 16 | 2 | 0 | 9 |
| INT | 17 | 2 | 12 | 3 | 0 | 10 | 7 | 0 | 0 | 0 |
| E2E | 4 | 0 | 3 | 1 | 0 | 0 | 4 | 0 | 0 | 0 |
| MAN | 17 | 2 | 8 | 7 | 0 | 0 | 0 | 0 | 17 | 6 |
| **Total** | **135** | **9** | **61** | **59** | **6** | **84** | **31** | **3** | **17** | **18** |

## Frontend — [frontend-test-cases.md](frontend-test-cases.md)

| Test ID | Feature | Test Level | Priority (score) | Automation | Expectation Status | UR |
|---|---|---|---|---|---|---|
| [FE-001](frontend-test-cases.md#fe-001) | F01 API client: Ok mapping | Unit | Medium (6) | Automatable | Inferred | |
| [FE-002](frontend-test-cases.md#fe-002) | F01 API client: HttpError | Unit | Medium (6) | Automatable | Inferred | |
| [FE-003](frontend-test-cases.md#fe-003) | F01 API client: Unreachable/timeout | Unit | High (12) | Automatable | Inferred | |
| [FE-004](frontend-test-cases.md#fe-004) | F01 API client: InvalidResponse | Unit | Medium (6) | Automatable | Inferred | |
| [FE-005](frontend-test-cases.md#fe-005) | F05 ApiSettings defaults | Unit | Medium (6) | Conditional | Inferred | |
| [FE-006](frontend-test-cases.md#fe-006) | F05 Probe polling/overlap | Component | High (9) | Automatable | Inferred | 13 |
| [FE-007](frontend-test-cases.md#fe-007) | F05 CameOnline reload | Component | High (9) | Automatable | Inferred | |
| [FE-008](frontend-test-cases.md#fe-008) | F05 Loops stop on dispose | Component | Medium (4) | Automatable | Inferred | |
| [FE-009](frontend-test-cases.md#fe-009) | F01/F02 Cache then server | Component | Medium (8) | Automatable | Inferred | |
| [FE-010](frontend-test-cases.md#fe-010) | F01 Failed refresh keeps state | Component | High (12) | Automatable | Inferred | |
| [FE-011](frontend-test-cases.md#fe-011) | F01/F02 Empty list never overwrites | Component | Medium (8) | Automatable | Inferred | |
| [FE-012](frontend-test-cases.md#fe-012) | F01 Load reuse | Component | Medium (4) | Automatable | Inferred | |
| [FE-013](frontend-test-cases.md#fe-013) | F03 Catalog cleaning | Unit | Medium (8) | Automatable | Inferred | 19 |
| [FE-014](frontend-test-cases.md#fe-014) | F03 Ranking and groups | Unit | Medium (6) | Automatable | Inferred | |
| [FE-015](frontend-test-cases.md#fe-015) | F03 No implicit pick / echo | Component | High (12) | Automatable | Inferred | |
| [FE-016](frontend-test-cases.md#fe-016) | F03 Debounce, stale results | Component | High (9) | Automatable | Inferred | |
| [FE-017](frontend-test-cases.md#fe-017) | F03 Large-catalog latency | Performance | Medium (6) | Conditional | **Unresolved** | 21 |
| [FE-018](frontend-test-cases.md#fe-018) | F03 Unicode IDs | Component | Medium (6) | Automatable | Inferred | |
| [FE-019](frontend-test-cases.md#fe-019) | F04 Save without pick | Component | Medium (8) | Automatable | Inferred | |
| [FE-020](frontend-test-cases.md#fe-020) | F04 Save success, autofill | Component | Medium (8) | Automatable | Inferred | |
| [FE-021](frontend-test-cases.md#fe-021) | F04/F14 Edit disarms | Component | Medium (6) | Automatable | Inferred | 09 |
| [FE-022](frontend-test-cases.md#fe-022) | F03/F11 Selection lock | Component | High (10) | Automatable | Inferred | |
| [FE-023](frontend-test-cases.md#fe-023) | F10 Navigation state | Component | Medium (4) | Automatable | Inferred | |
| [FE-024](frontend-test-cases.md#fe-024) | F12 Depth input validation | Component | High (12) | Automatable | Inferred | 20 |
| [FE-025](frontend-test-cases.md#fe-025) | F14 Idle → Ready | Component | Medium (8) | Automatable | Inferred | |
| [FE-026](frontend-test-cases.md#fe-026) | F14 Capture cycle, no double trigger | Component | High (15) | Automatable | Inferred | |
| [FE-027](frontend-test-cases.md#fe-027) | F14 Stop during Capturing | Component | High (12) | Automatable | Inferred | |
| [FE-028](frontend-test-cases.md#fe-028) | F14 Stop during Processing | Component | High (12) | Automatable | Inferred | 23 |
| [FE-029](frontend-test-cases.md#fe-029) | F12 Depth frozen and advance | Component | High (15) | Automatable | Inferred | |
| [FE-030](frontend-test-cases.md#fe-030) | F14/F15 Capture outcome feedback | Component | High (12) | Automatable | Inferred | 17 |
| [FE-031](frontend-test-cases.md#fe-031) | F14 Late progress ignored | Component | High (9) | Automatable | Inferred | |
| [FE-032](frontend-test-cases.md#fe-032) | F13 Duration editor | Component | Medium (6) | Automatable | Inferred | 03 |
| [FE-033](frontend-test-cases.md#fe-033) | F16 Sorted insert, renumber | Component | Medium (8) | Automatable | Inferred | |
| [FE-034](frontend-test-cases.md#fe-034) | F17 Delete flow | Component | High (10) | Automatable | Inferred | |
| [FE-035](frontend-test-cases.md#fe-035) | F18 Save failure and retry | Component | High (15) | Automatable | Inferred | |
| [FE-036](frontend-test-cases.md#fe-036) | F11 Drillhole switch | Component | High (15) | Automatable | Inferred | |
| [FE-037](frontend-test-cases.md#fe-037) | F11 Stored metadata wins | Component | Medium (6) | Automatable | Inferred | 30 |
| [FE-038](frontend-test-cases.md#fe-038) | F11 New Measurement | Component | Medium (8) | Automatable | Inferred | |
| [FE-039](frontend-test-cases.md#fe-039) | F19 Export command | Component | Medium (6) | Automatable | Inferred | |
| [FE-040](frontend-test-cases.md#fe-040) | F03/F11 Measured group refresh | Component | Medium (4) | Automatable | Inferred | |
| [FE-041](frontend-test-cases.md#fe-041) | F09 Toast rules | Unit | Medium (4) | Automatable | Inferred | |
| [FE-042](frontend-test-cases.md#fe-042) | F09 Status announcements | Component | Medium (4) | Automatable | Inferred | |
| [FE-043](frontend-test-cases.md#fe-043) | F06 Bluetooth identification/status | Component | Medium (6) | Automatable | Inferred | |
| [FE-044](frontend-test-cases.md#fe-044) | F07/F08 DB status rows, Retry | Component | Medium (6) | Automatable | Inferred | |
| [FE-045](frontend-test-cases.md#fe-045) | F06/F08 DeviceSettings.Load | Unit | Medium (6) | Automatable | Inferred | |
| [FE-046](frontend-test-cases.md#fe-046) | F16 Cell formatting | Unit | Medium (6) | Automatable | Inferred | |
| [FE-047](frontend-test-cases.md#fe-047) | F16 Column layout | Unit | Low (2) | Automatable | Inferred | |
| [FE-048](frontend-test-cases.md#fe-048) | F14/F18 Close during work | Component | High (15) | Conditional | Inferred | 23 |
| [FE-049](frontend-test-cases.md#fe-049) | F20 Demo ts_ms isolation | Component | Low (3) | Automatable | Inferred | |
| [FE-050](frontend-test-cases.md#fe-050) | F20 Chart data mapping | Component | Medium (4) | Conditional | **Unresolved** | 05 |
| [FE-051](frontend-test-cases.md#fe-051) | F11 Restore on start-up | Component | Low (3) | Blocked | **Unresolved** | 06 |

## Backend — [backend-test-cases.md](backend-test-cases.md)

| Test ID | Feature | Test Level | Priority (score) | Automation | Expectation Status | UR |
|---|---|---|---|---|---|---|
| [BE-001](backend-test-cases.md#be-001) | F01 Drillholes 200 contract | API | Medium (8) | Conditional | Inferred | 24 |
| [BE-002](backend-test-cases.md#be-002) | F01 SqlException → 503 | API | Medium (8) | Conditional | Inferred | |
| [BE-003](backend-test-cases.md#be-003) | F01 Missing connection string | API | High (9) | Conditional | **Unresolved** | 12 |
| [BE-004](backend-test-cases.md#be-004) | F01 Non-SQL exceptions | API | Medium (6) | Conditional | **Unresolved** | 12 |
| [BE-005](backend-test-cases.md#be-005) | F01 Request cancellation | API | Medium (4) | Conditional | Inferred | |
| [BE-006](backend-test-cases.md#be-006) | F01 SQL projection, NULLs, campaign | Integration | High (12) | Conditional | Inferred | 08, 25 |
| [BE-007](backend-test-cases.md#be-007) | F01 Large table | Performance | Medium (6) | Conditional | **Unresolved** | 21, 25 |
| [BE-008](backend-test-cases.md#be-008) | F01/F21/F22 OpenAPI document | API | Medium (4) | Conditional | Inferred | |
| [BE-009](backend-test-cases.md#be-009) | Auth posture, error leakage | Security | High (12) | Conditional | **Unresolved** | 14 |
| [BE-010](backend-test-cases.md#be-010) | Committed credentials | Security | Critical (25, ovr) | Conditional | **Unresolved** | 14 |
| [BE-011](backend-test-cases.md#be-011) | F05 Routing/reachability contract | API | Medium (6) | Conditional | Inferred | 13 |
| [BE-012](backend-test-cases.md#be-012) | F21 Bluetooth endpoint | API | Low (2) | Conditional | **Unresolved** | 07 |
| [BE-013](backend-test-cases.md#be-013) | F22 CT008 selection rule | Unit | Low (2) | Blocked | **Unresolved** | 07, 27 |
| [BE-014](backend-test-cases.md#be-014) | F22 Cache, timeout, concurrency | API | Low (2) | Conditional | **Unresolved** | 07 |
| [BE-015](backend-test-cases.md#be-015) | F12 Parse metres | Unit | High (15) | Automatable | Inferred | |
| [BE-016](backend-test-cases.md#be-016) | F12 Parse centimetres | Unit | High (15) | Automatable | Inferred | 20 |
| [BE-017](backend-test-cases.md#be-017) | F12 Depth formatting | Unit | Medium (8) | Automatable | Inferred | |
| [BE-018](backend-test-cases.md#be-018) | F12 No cumulative drift | Unit | High (10) | Automatable | Inferred | |
| [BE-019](backend-test-cases.md#be-019) | F16 MeasurementOrder | Unit | Medium (8) | Automatable | Inferred | |
| [BE-020](backend-test-cases.md#be-020) | F15 Capture window | Unit | Critical (20) | Automatable | Inferred | 01 |
| [BE-021](backend-test-cases.md#be-021) | F15 Expected samples | Unit | Medium (8) | Automatable | Inferred | 02 |
| [BE-022](backend-test-cases.md#be-022) | F15 Averaging | Unit | High (15) | Automatable | Inferred | |
| [BE-023](backend-test-cases.md#be-023) | F15 Time basis check | Unit | High (15) | Automatable | Inferred | 10 |
| [BE-024](backend-test-cases.md#be-024) | F15 Detector pick | Unit | High (15) | Automatable | Inferred | 10 |
| [BE-025](backend-test-cases.md#be-025) | F15 Polling, final read, lookback | Unit | Critical (20) | Automatable | Inferred | |
| [BE-026](backend-test-cases.md#be-026) | F15 Time check order, cancellation | Unit | High (15) | Automatable | Inferred | 29 |
| [BE-027](backend-test-cases.md#be-027) | F15/F08 Vendor DB never modified | Integration | Critical (15, ovr) | Automatable | Inferred | 15 |
| [BE-028](backend-test-cases.md#be-028) | F07 SqliteFileCheck states | Integration | Medium (6) | Automatable | Inferred | |
| [BE-029](backend-test-cases.md#be-029) | F08 Gamma Guard discovery | Integration | High (9) | Automatable | Inferred | 16 |
| [BE-030](backend-test-cases.md#be-030) | F13 Duration settings | Unit | Medium (6) | Automatable | Inferred | 03 |
| [BE-031](backend-test-cases.md#be-031) | F11 Session resolution | Integration | High (15) | Automatable | Inferred | 19 |
| [BE-032](backend-test-cases.md#be-032) | F18 Atomic save | Integration | Critical (20) | Automatable | Inferred | |
| [BE-033](backend-test-cases.md#be-033) | F18 Session isolation, unique CaptureNo | Integration | High (10) | Automatable | Inferred | |
| [BE-034](backend-test-cases.md#be-034) | F17 Delete transaction | Integration | High (15) | Automatable | Inferred | |
| [BE-035](backend-test-cases.md#be-035) | F18 Round-trip precision | Integration | High (15) | Automatable | Inferred | |
| [BE-036](backend-test-cases.md#be-036) | F18 Migrations v1–v4 → v5 | Integration | High (15) | Automatable | Inferred | |
| [BE-037](backend-test-cases.md#be-037) | F18 Migration refusals, rollback | Integration | High (15) | Automatable | Inferred | |
| [BE-038](backend-test-cases.md#be-038) | F18 Migration idempotence/concurrency | Integration | Medium (8) | Conditional | Inferred | 28 |
| [BE-039](backend-test-cases.md#be-039) | F03/F11 Measured ordering | Integration | Medium (4) | Automatable | Inferred | |
| [BE-040](backend-test-cases.md#be-040) | F02 DrillholeCache | Integration | Medium (8) | Automatable | Inferred | |
| [BE-041](backend-test-cases.md#be-041) | F18 Legacy folder move | Integration | Medium (8) | Blocked | Inferred | 27 |
| [BE-042](backend-test-cases.md#be-042) | F19 CSV structure | Unit | High (12) | Automatable | Inferred | 04, 22 |
| [BE-043](backend-test-cases.md#be-043) | F19 CSV injection, session mixing | Security | High (12) | Automatable | Inferred | 18 |
| [BE-044](backend-test-cases.md#be-044) | F19 Export file handling | Integration | Medium (8) | Conditional | Inferred | 27 |
| [BE-045](backend-test-cases.md#be-045) | F23 check-db.ps1 on v5 | Integration | High (10) | Conditional | **Unresolved** | 11 |
| [BE-046](backend-test-cases.md#be-046) | F18 Text fields | Integration | Medium (6) | Automatable | Inferred | |

## Integration and E2E — [integration-test-cases.md](integration-test-cases.md)

| Test ID | Feature | Test Level | Priority (score) | Automation | Expectation Status | UR |
|---|---|---|---|---|---|---|
| [INT-001](integration-test-cases.md#int-001) | F01/F04/F19 Field mapping end to end | Integration | High (12) | Conditional | Inferred | |
| [INT-002](integration-test-cases.md#int-002) | F01/F05 503 propagation | Integration | High (9) | Conditional | Inferred | |
| [INT-003](integration-test-cases.md#int-003) | F01/F02/F05 Offline and recovery | Integration | High (12) | Conditional | Inferred | |
| [INT-004](integration-test-cases.md#int-004) | F01/F05 Server slower than timeout | Integration | High (9) | Conditional | Inferred | |
| [INT-005](integration-test-cases.md#int-005) | F01 Contract drift guard | Integration | High (12) | Automatable | Inferred | |
| [INT-006](integration-test-cases.md#int-006) | F02 Cache → offline restart | Integration | Medium (8) | Automatable | Inferred | |
| [INT-007](integration-test-cases.md#int-007) | F15/F18 Capture → persist → reload | Integration | Critical (20) | Automatable | Inferred | |
| [INT-008](integration-test-cases.md#int-008) | F15 Concurrent Gamma Guard writer | Integration | Critical (20) | Conditional | Inferred | 15 |
| [INT-009](integration-test-cases.md#int-009) | F15/F08 GG locked/missing | Integration | High (12) | Automatable | Inferred | |
| [INT-010](integration-test-cases.md#int-010) | F18/F07 App DB failure and recovery | Integration | High (15) | Conditional | Inferred | |
| [INT-011](integration-test-cases.md#int-011) | F11/F12 Reopen measured drillhole | Integration | High (15) | Automatable | Inferred | |
| [INT-012](integration-test-cases.md#int-012) | F11 No session mixing | Integration | High (15) | Automatable | Inferred | |
| [INT-013](integration-test-cases.md#int-013) | F17 Delete → reload → export | Integration | Medium (8) | Automatable | Inferred | |
| [INT-014](integration-test-cases.md#int-014) | F16/F19 Display = DB = CSV | Integration | High (15) | Automatable | Inferred | 17, 22 |
| [INT-015](integration-test-cases.md#int-015) | F08/F15 Status vs capture | Integration | High (9) | Automatable | Inferred | |
| [INT-016](integration-test-cases.md#int-016) | F07 DB status under lock | Integration | Medium (4) | Automatable | Inferred | |
| [INT-017](integration-test-cases.md#int-017) | F18 Upgrade then measure | Integration | High (15) | Conditional | Inferred | |
| [E2E-001](integration-test-cases.md#e2e-001) | Main journey | E2E | High (15) | Conditional | Inferred | 27 |
| [E2E-002](integration-test-cases.md#e2e-002) | Restart persistence | E2E | High (15) | Conditional | Inferred | 27 |
| [E2E-003](integration-test-cases.md#e2e-003) | Offline operation | E2E | High (12) | Conditional | Inferred | 27 |
| [E2E-004](integration-test-cases.md#e2e-004) | Delete via real dialog | E2E | Medium (6) | Conditional | Inferred | 27 |

## Manual — [manual-test-cases.md](manual-test-cases.md)

| Test ID | Feature | Test Level | Priority (score) | Automation | Expectation Status | UR |
|---|---|---|---|---|---|---|
| [MAN-001](manual-test-cases.md#man-001) | F15 Live capture accuracy | Manual | Critical (20) | Manual | **Unresolved** | 02, 15, 26 |
| [MAN-002](manual-test-cases.md#man-002) | F14/F15 Click-time alignment | Manual | Critical (20) | Manual | **Unresolved** | 01, 10, 26 |
| [MAN-003](manual-test-cases.md#man-003) | F15 Sample rate | Manual | High (15) | Manual | **Unresolved** | 02, 26 |
| [MAN-004](manual-test-cases.md#man-004) | F15 Two detectors | Manual | Medium (8) | Manual | Inferred | 10, 26 |
| [MAN-005](manual-test-cases.md#man-005) | F06 Bluetooth states | Manual | Medium (6) | Manual | Inferred | 26 |
| [MAN-006](manual-test-cases.md#man-006) | F14 Right-click matrix | Manual | High (15) | Manual | Inferred | |
| [MAN-007](manual-test-cases.md#man-007) | F14/F18 Close/kill during work | Manual | High (15) | Manual | Inferred | 23 |
| [MAN-008](manual-test-cases.md#man-008) | F16/F20 Layout, DPI, rendering | Manual | Medium (6) | Manual | Inferred | |
| [MAN-009](manual-test-cases.md#man-009) | F03/F04 Keyboard and accessibility | Manual | High (9) | Manual | Inferred | 21 |
| [MAN-010](manual-test-cases.md#man-010) | F12/F19 Regional settings | Manual | High (12) | Manual | Inferred | 04 |
| [MAN-011](manual-test-cases.md#man-011) | F16/F19 Time zone, DST | Manual | High (9) | Manual | Inferred | 22 |
| [MAN-012](manual-test-cases.md#man-012) | F19 CSV in Excel | Manual | High (12) | Manual | Inferred | 18 |
| [MAN-013](manual-test-cases.md#man-013) | F19 Real Save dialog | Manual | Medium (6) | Manual | Inferred | |
| [MAN-014](manual-test-cases.md#man-014) | F08 GG discovery on targets | Manual | High (12) | Manual | **Unresolved** | 16, 26 |
| [MAN-015](manual-test-cases.md#man-015) | F05 Foreign service on port | Manual | Medium (6) | Manual | **Unresolved** | 13 |
| [MAN-016](manual-test-cases.md#man-016) | F11/F18 Long-session soak | Performance | Medium (6) | Manual | **Unresolved** | 21 |
| [MAN-017](manual-test-cases.md#man-017) | F17 Delete dialog usability | Manual | Medium (4) | Manual | Inferred | |

## Feature coverage cross-check

| Feature | Cases |
|---|---|
| F01 | FE-001…004, 009–012; BE-001…009, 011; INT-001…005 |
| F02 | FE-009, 011; BE-040; INT-003, 006 |
| F03 | FE-013…018, 022, 040; BE-039; MAN-009 |
| F04 | FE-019…021; INT-001; MAN-009 |
| F05 | FE-005…008; BE-011; INT-002…004; MAN-015 |
| F06 | FE-043, 045; MAN-005 |
| F07 | FE-044; BE-028; INT-010, 016 |
| F08 | FE-044, 045; BE-027, 029; INT-009, 015; MAN-014 |
| F09 | FE-041, 042 |
| F10 | FE-023 |
| F11 | FE-022, 036…038, 040, 051; BE-031, 039; INT-011, 012; MAN-016 |
| F12 | FE-024, 029; BE-015…018; INT-011; MAN-010 |
| F13 | FE-032; BE-030 |
| F14 | FE-021, 025…028, 030, 031, 048; MAN-002, 006, 007 |
| F15 | FE-030; BE-020…027; INT-007…009, 015; MAN-001…004 |
| F16 | FE-033, 046, 047; BE-019; INT-014; MAN-008, 011 |
| F17 | FE-034; BE-034; INT-013; E2E-004; MAN-017 |
| F18 | FE-035, 048; BE-032, 033, 035…038, 041, 046; INT-007, 010, 017; MAN-007 |
| F19 | FE-039; BE-042…044; INT-001, 014; MAN-010…013 |
| F20 | FE-049, 050; MAN-008 |
| F21 | BE-008, 012 |
| F22 | BE-008, 013, 014 |
| F23 | BE-045 |
| Cross-cutting security | BE-009, 010, 043, 046; MAN-012 |
| Full workflows | E2E-001…004 |
