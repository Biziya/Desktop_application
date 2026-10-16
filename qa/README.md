# QA Documentation — SPP CT-007 Measurement Studio

This is the entry point for all quality-assurance documentation. The product overview for developers is the root [README.md](../README.md); the test harness itself is described in [Tests/README.md](../Tests/README.md).

## Reading order

1. [requirements/project-overview.md](requirements/project-overview.md): what the product is and who uses it.
2. [architecture/architecture-overview.md](architecture/architecture-overview.md): the system picture.
3. [requirements/functional-requirements.md](requirements/functional-requirements.md) and [business-rules.md](requirements/business-rules.md): what is tested, and why.
4. [requirements/unresolved-requirements.md](requirements/unresolved-requirements.md): the open questions and discrepancies that block sign-off.
5. [reports/run-20261016-final/qa-summary.md](reports/run-20261016-final/qa-summary.md): the current quality assessment.
6. The phase folders below, as needed.

## Requirements and architecture

| Folder | Contents |
|---|---|
| [requirements/](requirements/) | [Project overview](requirements/project-overview.md), [functional (FR-xxx)](requirements/functional-requirements.md), [non-functional (NFR-xxx)](requirements/nonfunctional-requirements.md), [business rules (BR-xxx)](requirements/business-rules.md), [unresolved items](requirements/unresolved-requirements.md). Each item is Confirmed / Inferred / Proposed / Unresolved and traced to tests |
| [architecture/](architecture/) | [Overview](architecture/architecture-overview.md), [frontend](architecture/frontend-architecture.md), [backend](architecture/backend-architecture.md), [database](architecture/database-architecture.md), [integration](architecture/integration-architecture.md), [feature workflows](architecture/feature-workflows.md), [technical decisions](architecture/technical-decisions.md) |

## QA phases

| Phase | Folder | Key documents |
|---|---|---|
| 1. Analysis | [analysis/](analysis/) | [project-map](analysis/project-map.md), [feature-inventory](analysis/feature-inventory.md), [testing-constraints](analysis/testing-constraints.md) |
| 2. Planning | [planning/](planning/) | [test-plan](planning/test-plan.md), [test-inventory](planning/test-inventory.md) (135 scenarios), [unresolved-requirements](planning/unresolved-requirements.md) (UR-01…30) |
| 3. Generation | [generation/](generation/) | [test-implementation-status](generation/test-implementation-status.md) |
| 4. Execution | [execution/](execution/) | [test-execution-report](execution/test-execution-report.md), [evidence/](execution/evidence/) |
| 5. Reporting | [reports/run-20261016-final/](reports/run-20261016-final/) | [qa-summary](reports/run-20261016-final/qa-summary.md), [defect-report](reports/run-20261016-final/defect-report.md), [manual-testing-checklist](reports/run-20261016-final/manual-testing-checklist.md) |
| 6. Regression | [regression/](regression/) | [baseline.json](regression/baseline.json), [reg-20261016-01/](regression/reg-20261016-01/) |
| 7. CI | [ci/](ci/) | [ci-overview](ci/ci-overview.md), [failure-policy](ci/failure-policy.md), [setup-requirements](ci/setup-requirements.md) |
| 8. Maintenance | [maintenance/maint-20261016/](maintenance/maint-20261016/) | [test-quality-review](maintenance/maint-20261016/test-quality-review.md), [flaky-test-analysis](maintenance/maint-20261016/flaky-test-analysis.md) |
| 9. Effectiveness | [effectiveness/eff-20261016/](effectiveness/eff-20261016/) | [effectiveness-summary](effectiveness/eff-20261016/effectiveness-summary.md), [mutation-results](effectiveness/eff-20261016/mutation-results.md), [requirement-traceability](effectiveness/eff-20261016/requirement-traceability.md) |

## Tooling

| Path | Purpose |
|---|---|
| [Tests/CT007.Tests/](../Tests/CT007.Tests/) | Console test harness (`[Test("ID", …)]`) |
| [Tests/regression/](../Tests/regression/) | `Invoke-Regression.ps1`, `impact-map.json` |
| [Tests/ci/](../Tests/ci/) | `Publish-TestResults.ps1`, `Invoke-FailureAnalysis.ps1`, `known-failures.json` |
| [Tests/mutation/](../Tests/mutation/) | `generate_mutants.py`, `mutants.json`, `Invoke-MutationRun.ps1` |
| [.github/workflows/tests.yml](../.github/workflows/tests.yml) | CI workflow |

## Maintenance guidance

- **IDs are stable.** Never renumber FR / NFR / BR / TD / test IDs; mark retired items as *Withdrawn* instead.
- **When production code changes:** update the affected architecture page and requirement rows (implementation and test columns), then run the impact-based regression (`Tests/regression/`) and add a new `reg-YYYYMMDD-NN` folder.
- **When a requirement is decided** (for example DIS-01/02 or a README Q): change its status to Confirmed, move it out of [unresolved-requirements.md](requirements/unresolved-requirements.md), and adjust the related tests.
- **When a defect is fixed:** remove it from `Tests/ci/known-failures.json` and the defect report together.
- **Dated folders** (`run-…`, `reg-…`, `maint-…`, `eff-…`) are historical snapshots; add new ones rather than editing old ones.
- **Secrets:** never copy connection strings or credentials into these documents.
