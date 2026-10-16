# Failure Policy

## Required check

**Required check:** `Tests / Build and test (Windows)`. It must be made required by a repository admin (see setup-requirements §4).

The check **fails** when any of the following happens:
- The test project, Frontend or Backend fails to build, including on a compiler warning (`-warnaserror`).
- Setup fails before results exist: restore, copy, or a work folder that is too deep. This shows as "No test results were produced".
- Any test fails that is not listed in [`Tests/ci/known-failures.json`](../../Tests/ci/known-failures.json) and is not informational. This covers unit, integration, critical-regression and Timing tests, and BE-006 when it runs.

The check **passes, with warnings**, when:
- **A listed known defect fails.** Currently FE-005 (DEF-001) and BE-030 (DEF-002). Matching is by test method, so a new failure in another test with the same ID still blocks.
- **An `E2E-*` test fails.** E2E is informational until the desktop bench has been verified on GitHub-hosted runners. Then remove `-InformationalPrefixes 'E2E-'` from the workflow.

Every failure appears as an annotation and in the job summary. Nothing is hidden.

## Blocked and skipped tests

- **Skipped tests are always reported as skipped, never as passed:** for example BE-006 without LocalDB, or E2E outside the E2E category. The summary lists the reason.
- **Unresolved and Blocked scenarios are not implemented,** so they cannot pass or fail. They are tracked in `qa/generation/test-implementation-status.md`.

## Known defects list

- **Adding an entry** needs a confirmed defect ID in a QA defect report. Never add one to make a PR green.
- **When a listed test starts passing,** CI posts a notice. Remove the entry in the same PR that fixed the defect, so the test becomes blocking again.

## Retries and flaky tests

- **No automatic retries.** A failure is investigated, not retried until it passes.
- **Re-running a job** ("Re-run failed jobs") is allowed once, for a suspected runner or environment problem. The rerun's results get a new artifact (`run_attempt`), and both remain available.
- **A test that fails intermittently** under comparable conditions is recorded as **intermittent** in the next QA regression report, with both result files. Fix it in test code, as with INT-008 in Phase 4. Do not weaken its assertion.
- **The Timing tests** use generous bounds. If one is flaky on hosted runners, raise it as a test-infrastructure finding rather than excluding it silently.

## AI failure analysis

AI analysis runs only when all of these hold:
- the `tests` job failed;
- `vars.CT007_AI_ANALYSIS == 'true'`;
- `ANTHROPIC_API_KEY` is set;
- the event is not a fork PR.

Its output is a suggestion. A defect counts as confirmed only after a person checks the evidence (the failing test, its expected result, and reproduction), as in Phases 4–6.

## Manual review

These always need a person:
- changes to `known-failures.json`;
- changing a test's expected result (it must be backed by an approved requirement or contract; see `qa/planning/unresolved-requirements.md`);
- E2E results before they are made blocking;
- the release sign-off, which also needs the manual Critical and High cases (MAN-001, 002, 003, 006, 007).
