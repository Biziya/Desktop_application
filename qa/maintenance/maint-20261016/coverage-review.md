# Coverage Review — maint-20261016

The baseline is the [Phase 5 coverage analysis](../../reports/run-20261016-final/coverage-analysis.md); figures are scenario counts from the inventory.

## Unchanged in this phase

| | Count |
|---|---|
| Planned scenarios | 135 (118 automatable, 17 manual) |
| Implemented | 104 scenarios / 107 methods: **no test added, removed or consolidated** |
| Executed and passing (latest runs) | 97 scenarios / 100 methods; 2 known-defect failures (FE-005, BE-030); 5 skipped (BE-006, E2E-001…004, environment) |

Requirement coverage remains **not available**, because there are no approved requirements (UR-01). Code coverage remains **not measured**, because no tool is installed.

## Redundancy and obsolete tests

- **Same requirement, different levels — intentional:**
  - the capture window rule: BE-020 (unit), INT-007 (pipeline), MAN-001 (hardware);
  - CSV: BE-042 (serialiser), INT-014 (stored = exported), E2E-001 (real dialog);
  - delete: FE-034 (ViewModel), BE-034 (transaction), INT-013 (cross-layer), E2E-004 (real dialog).

  Each level catches different defects. **Nothing was removed.**
- **No obsolete tests.** No production code changed since the baseline, so every expectation still applies.

## Weakly covered features (by executed evidence)

| Feature | Weakness | Priority |
|---|---|---|
| F14 right-click trigger, F17/F19 real dialogs | Only ViewModel-level tests; E2E not executed | High |
| F15 device capture | Simulated Gamma Guard only; accuracy unverified (MAN-001…003) | Critical |
| Security (credentials, auth, Swagger exposure) | BE-009 and BE-010 not implemented (UR-14) | Critical |
| F01 real SQL projection | BE-006 skipped: no LocalDB here | High |
| F06 real Bluetooth (WinRT) | Logic only (FE-043); hardware manual (MAN-005) | Medium |
| F23 `check-db.ps1` | No test (BE-045 Unresolved); SUS-02 open | Medium |
| F20 chart | Semantics unresolved (FE-050, UR-05) | Low |

## New finding from this phase

**SUS-09 (suspected; proposed Low severity) — `DrillholeCache` leaks a SQLite connection on a corrupt database.**
- **Code:** `Frontend/Api/Core-information/drillholes/DrillholeCache.cs`. `Open()` opens the connection, then runs `CREATE TABLE IF NOT EXISTS`. If that throws (for example `SQLITE_NOTADB`), the connection is never disposed.
- **Evidence:** BE-040 (corrupt-cache case) leaves `corrupt.db` locked until the process exits, in every run, and the lock survives `GC.Collect` plus `ClearAllPools` ([run-02 console](artifacts/run-02/console.log)).
- **Impact (inferred):** the app keeps a handle on a corrupt `measurements.db` until it exits, which could block a repair or replacement of the file while the app runs.
- **Not fixed** (production code). Recommended: dispose the connection when initialisation fails, then add an assertion to BE-040 that the file can be deleted after the page handled the corrupt cache.

## High-priority gaps (unchanged from Phase 5)

1. E2E and the real-UI workflow.
2. Hardware capture accuracy.
3. Security tests.
4. The real SQL query.
5. The 13 Unresolved or Blocked scenarios waiting on requirement answers.
