# Regression Findings — reg-20261016-01

| Category | Findings |
|---|---|
| New regressions (suspected or confirmed) | **None.** All 15 previously passing selected tests still pass on identical production code |
| Previously known failures | FE-005 → [DEF-001](../../reports/run-20261016-final/defect-report.md#def-001) and BE-030 → [DEF-002](../../reports/run-20261016-final/defect-report.md#def-002). Both still fail with the same `InvalidOperationException`; the production code is unchanged, so this is expected |
| Resolved previous failures | None |
| Environment blockers | Unchanged from the baseline: BE-006 (no `CT007_TEST_SQL` or LocalDB) and E2E-001…004 (no dedicated Windows test account). They were not selected |
| Test implementation defects | None in the tests. One tooling issue was found and fixed during this phase: the first version of `Invoke-Regression.ps1` wrote its logs as UTF-16 (PowerShell 5.1 `*>`), and that version produced this run's first set of logs. The script now writes UTF-8, and the rerun's logs replaced them. Test outcomes were unaffected |
| Unresolved behaviour | Unchanged: the open requirement items UR-01…UR-30 and the suspected issues SUS-01…08 from the Phase 5 report. Nothing in this run changes their status |

**Risk change since the previous report:** none. No code changed.
