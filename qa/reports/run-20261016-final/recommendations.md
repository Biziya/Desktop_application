# Recommendations — run-20261016-final

Each action is in priority order and traces to evidence in this report set.

## 1. Confirmed defects requiring developer attention

| # | Action | Evidence | Verify with |
|---|---|---|---|
| 1.1 | Make `ApiSettings.Load` treat wrong JSON value kinds as invalid (check `ValueKind`, or catch `InvalidOperationException`), including a non-string `baseUrl` | [DEF-001](defect-report.md#def-001) | FE-005 |
| 1.2 | Apply the same fix to `CaptureDurationSettings.Load` | [DEF-002](defect-report.md#def-002) | BE-030 |

## 2. Suspected defects requiring investigation

| # | Action | Evidence |
|---|---|---|
| 2.1 | Remove the SQL credentials from `Backend/appsettings.json`, rotate them, use a secret store; decide on authentication, Swagger exposure and host binding | SUS-01, UR-14 |
| 2.2 | Update `tools/check-db.ps1` to schema v5, or retire it | SUS-02, UR-11 |
| 2.3 | Decide whether a capture refused because Gamma Guard is locked or missing must show its reason | SUS-03 (INT-009) |
| 2.4 | Decide whether data loss on a hung save at close, and a Stop during the processing hold, is acceptable | SUS-04 (FE-048, FE-028), UR-23 |
| 2.5 | Decide the HTTP status for a missing connection string and other non-SQL failures | SUS-05, UR-12 |
| 2.6 | Confirm the time check may be skipped on a detector's first capture | SUS-06 (BE-026), UR-29 |
| 2.7 | Run the schema-version check before switching the file to WAL | SUS-07 |

## 3. Missing or insufficient automated tests

| # | Action | Gap |
|---|---|---|
| 3.1 | Implement the 11 Unresolved scenarios once their UR items are answered | FE-017, FE-050, FE-051, BE-003, BE-004, BE-007, BE-009, BE-010, BE-012, BE-014, BE-045 |
| 3.2 | Decide whether to allow minimal test seams (`InternalsVisibleTo`, an injectable data folder, an injectable Save dialog). They would unblock BE-013, BE-041 and BE-044, and E2E without a dedicated account | UR-27 |
| 3.3 | Add the partial cases left out in Phase 3: the ACL deny-read case (BE-028), a locked delete (BE-034), and rollback-journal concurrency (INT-008) | Implementation status notes |
| 3.4 | Add the security-adjacent cases once requirements exist: Drillhole ID case handling, and CSV tab/CR neutralisation | SUS-08, UR-18, UR-19 |

## 4. Testing infrastructure limitations

| # | Action | Reason |
|---|---|---|
| 4.1 | Provide a local `CT007_Test` SQL database (LocalDB or a container), then run BE-006 | BE-006 skipped |
| 4.2 | Provide a dedicated Windows test account with an interactive desktop, then run E2E-001…004 with `CT007_E2E_DEDICATED_ACCOUNT=1` | E2E skipped; the app always writes to `%LocalAppData%` |
| 4.3 | Stop tracking `Frontend/obj` and `Backend/obj` in git, so builds and CI can run in place (Phase 4 had to build from a copy) | Execution report §2 |
| 4.4 | Add `Tests/CT007.Tests` to `CT007.slnx`, decide the future of `SmokeTests` (it still cannot build, NU1201), and set up CI with the default set on PRs and Timing nightly | UR-24 |
| 4.5 | Add a code-coverage tool, such as coverlet (needs package approval). Code coverage is currently unavailable | Coverage analysis §2 |
| 4.6 | Make temp-folder cleanup robust (longer retries or cleanup at runner exit) | Execution report §4.3 |
| 4.7 | Before relying on it, check whether `TestBackend` still matches `Backend/Program.cs` | Phase 3 D8 |

## 5. Manual testing requirements

Execute the [manual checklist](manual-testing-checklist.md), starting with MAN-001, 002, 006 and 007 (Critical/High; no automated substitute). Book bench time with the CT007-M detectors and Gamma Guard (UR-26).

## 6. Unresolved business requirements

Get product-owner answers to the 30 items in [unresolved-requirements.md](../../planning/unresolved-requirements.md). The ones with the most impact on test validity:
- **UR-01:** the authoritative specification. It decides whether any expectation can become Confirmed.
- **UR-02:** the sample rate.
- **UR-04:** CSV columns.
- **UR-17:** incomplete captures.
- **UR-23:** data-loss tolerance.
- **UR-14:** the security posture.

## 7. Future testing improvements

- Once UR-01 is answered, mark the expectations that are confirmed, so results can report requirement coverage.
- After recommendations 1.1 and 1.2 are fixed, rerun the full suite, including Timing. Keep INT-008 in the nightly set: it is the only automated test of concurrent Gamma Guard writes.
