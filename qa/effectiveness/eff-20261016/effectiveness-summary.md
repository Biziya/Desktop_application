# Test Effectiveness Summary — eff-20261016

| | |
|---|---|
| Code under test | `main` @ `c4586de`; production code unchanged since the tested baseline `277d581` |
| Working tree | Phase 8 and Phase 9 test-only changes, uncommitted (see the [improvement log](improvement-log.md)) |
| Baseline before mutation | Phase 8 run: 100 passed, 2 failed (known defects DEF-001, DEF-002), 5 skipped. The mutation baseline (each pass) was re-run on the disposable copy |
| Environment | Windows 11, .NET SDK 10.0.402, offline restore. No SQL Server; no E2E desktop account; no coverage tool; no mutation tool |

## Assessment: effective for the tested logic, with known blind spots

**What the tests catch.** They reliably detect faults in the high-risk business and data logic:
- capture and averaging;
- time and detector rules;
- depth arithmetic and ordering;
- persistence, migrations and delete;
- CSV safety;
- the measurement state and validation rules;
- API mapping, and status and announcement logic.

Of 77 controlled mutants, **71 are killed**: 65 by the tests as they stood, and 6 more after small test-only fixes. **4 are equivalent** (proven or evidence-backed), **1 has no coverage** (needs SQL Server), and **1 survives** (a redundant guard; low priority).

| Metric | Value |
|---|---|
| Modules mutated | 23 files: capture (4), depth and order (2), persistence (1), CSV (1), Measurement ViewModel and result/format (3), API client and settings and connection (3), catalog and Core form (2), Device Status checks (4), status and toasts (2), backend controller and repository (2) |
| Tests evaluated | All 109 methods were run as the baseline; per mutant, the CI selection (16–41 test IDs) |
| Mutation score before improvements | **90.3 %** (65 / 72; 84.4 % raw, 65 / 77) |
| Mutation score after improvements | **98.6 %** (71 / 72; excludes 4 equivalent and 1 no-coverage) |
| Code coverage | Not measured (no tool) |

## Main weaknesses found (and fixed)

1. **Known defects masked other checks.** BE-030 and FE-005 stopped at their DEF case, so later boundary and parsing checks never ran. Fixed by splitting the defect cases into their own methods.
2. **CI selection gaps.** The impact map missed FE-046 and FE-007 for two files, so CI would not have run the detecting test on a change. Fixed in the map.
3. **A missing tie-break scenario** in FE-014. Assertion added.
4. **Unique detection in Timing tests** (M56 via FE-006). They stay in every CI run. Fast local runs without Timing miss it.

## Critical remaining gaps

1. **No requirement-based correctness.** Every expectation is inferred from code (UR-01). The tests detect *changes*, not *wrong original behaviour*.
2. **Real systems untested:** the SQL query (M77), the desktop UI (E2E), Gamma Guard, and the hardware capture accuracy (MAN-001…003).
3. **Security** (credentials, authentication): BE-009 and BE-010 not implemented.
4. **Known defects** DEF-001 and DEF-002 are still open.

## Recommended next actions

1. Fix DEF-001 and DEF-002 in production code, then remove them from `Tests/ci/known-failures.json`.
2. Run BE-006 with LocalDB (this covers M77), and E2E on a dedicated account.
3. If approved, install Stryker.NET for a broader, tool-generated mutation run, and coverlet for coverage. Then compare with these manual results.
4. Rerun `Tests/mutation/Invoke-MutationRun.ps1` after changes to capture, persistence or CSV code. It takes about 17 min for all 77 mutants without Timing.

**Documents:**
- [mutation-results.md](mutation-results.md)
- [assertion-quality.md](assertion-quality.md)
- [requirement-traceability.md](requirement-traceability.md)
- [improvement-log.md](improvement-log.md)
- [artifacts/](artifacts/): per-pass `baseline.csv`, `mutation-results.json`, per-mutant result CSVs and build logs, `final-classification.json`, and the validation run
