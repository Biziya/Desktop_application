# CI/CD Testing Guide

How the automated tests run in CI, and how to trigger and inspect runs. Source: [`.github/workflows/tests.yml`](../../.github/workflows/tests.yml). Design details and policies are in [`qa/ci/`](../ci/): [overview](../ci/ci-overview.md), [setup](../ci/setup-requirements.md), [failure policy](../ci/failure-policy.md) and [execution matrix](../ci/test-execution-matrix.md).

## 1. What is configured

| Item | Value |
|---|---|
| Platform | **GitHub Actions**, repository `github.com/Biziya/Desktop_application` (`origin`) |
| Workflow | **Tests** (`.github/workflows/tests.yml`). It tests only and never deploys or releases |
| Test job | **Build and test (Windows)** on `windows-latest` (GitHub-hosted, ephemeral), timeout 45 min. Covers frontend and backend in one build |
| Optional job | **AI failure triage (optional)** on `ubuntu-latest`. It runs only after a failed test job, when the variable `CT007_AI_ANALYSIS=true` and the secret `ANTHROPIC_API_KEY` exist, and never for fork PRs. It only suggests; it never changes the result |
| Status | `qa/ci/ci-overview.md` (2026-10-16) records the workflow as **validated locally, not yet run on GitHub**. Check the Actions tab for later runs |

## 2. Triggers

| Event | When | Tests run |
|---|---|---|
| `pull_request` | A PR to `main` or `development` is opened or updated | Change-based selection plus the critical set, with `Timing` |
| `push` | A push to `main` or `development`. **Skipped** when only `qa/**`, `*.md` or `Images/**` changed | Change-based selection (full suite for a new branch or a force-push), with `Timing` |
| `workflow_dispatch` | **Manual**, from the Actions tab | `scope`: `full` (default) or `changed`; `categories`: default `Timing,Sql,E2E` |
| `workflow_call` | Called by another workflow (none exists yet) | `scope: full` by default |

How the selection works:
1. The selection comes from [`Tests/regression/impact-map.json`](../../Tests/regression/impact-map.json), through the same script as a local run: `Tests/regression/Invoke-Regression.ps1`, with `-WarnAsError`.
2. `Sql` runs only if the runner has LocalDB; otherwise BE-006 is reported as skipped.
3. `E2E` is enabled only on GitHub-hosted runners.

## 3. Required and optional checks

| Check | Blocking? |
|---|---|
| Build of the test project, Frontend and Backend, with warnings as errors | **Yes** |
| Any failing test not listed in [`Tests/ci/known-failures.json`](../../Tests/ci/known-failures.json) (FE, BE, INT, critical set, Timing; BE-006 when it runs) | **Yes** |
| Listed known defects (FE-005, BE-030) | No: reported as warnings |
| `E2E-*` failures | No: informational until verified on hosted runners |
| AI failure triage | No: optional suggestions |

The check name is **`Tests / Build and test (Windows)`**. Branch protection must make it *required*. That is a repository-admin setting ([setup-requirements.md §4](../ci/setup-requirements.md#4-manual-github-configuration-repository-admin)); whether it is enabled cannot be seen from the repository files.

## 4. Working with runs (GitHub web interface)

### Open the CI interface

1. Go to `https://github.com/Biziya/Desktop_application`.
2. Click the **Actions** tab.

### Locate the testing workflow

- Choose **Tests** in the left-hand list. Every run is listed with its trigger, branch and commit.
- On a pull request, the check also appears in the PR's **Checks** tab and at the bottom of the **Conversation** tab.

### Trigger tests manually

You need write access to the repository.

1. Go to **Actions → Tests**.
2. Click **Run workflow** (top right of the run list).
3. Choose the branch.
4. Set **scope**: `full` (whole suite) or `changed` (only what the last commit affects).
5. Set **categories**: keep `Timing,Sql,E2E`, or enter `Timing` for a faster run without SQL and desktop tests.
6. Click **Run workflow**. The new run appears at the top of the list after a few seconds.

Pushing only `qa/` or Markdown changes to `main` or `development` does not start the workflow. Use **Run workflow**, or open a pull request.

### Inspect successful and failed runs

1. Open a run. A green check means the check passed; a red cross means it failed.
2. Open the **Summary** page. The job summary, written by `Tests/ci/Publish-TestResults.ps1`, shows:
   - totals;
   - new failures;
   - known defects;
   - skipped tests with reasons;
   - the scope and selected IDs.
3. Read the **Annotations** on the summary page:
   - `error`: a blocking failure;
   - `warning`: a known defect or an E2E failure;
   - `notice`: for example, a known defect that now passes.

The summary says the check passed even when there are warnings: known defects and E2E failures don't block. How to read the individual outcomes: [testing-results-guide.md](testing-results-guide.md).

### View execution logs

1. In a run, click the job **Build and test (Windows)**.
2. Expand the steps:
   - **Decide scope**: the baseline commit, whether the suite is full, and the categories.
   - **Build and run selected tests**: the regression script's output.
   - **Publish results and decide the check**: the pass or fail decision.

The **Build and run** step can show a warning icon even when the check passed. It is marked `continue-on-error`, and the publish step makes the final decision.

### Download test reports

1. Open a run and scroll to **Artifacts** at the bottom of its summary page.
2. Download `test-results-ci-<run_id>-<attempt>`. It contains `junit.xml`, `results.csv`, `summary.md`, `selection.json`, `console.log` and `build.log`. It is kept for 14 days.
3. If the optional triage ran, also download `ai-analysis-ci-<run_id>-<attempt>` (`ai-analysis.md`).
4. Unzip it and read the files as described in [testing-results-guide.md](testing-results-guide.md#3-result-files).

### Re-run

**Re-run failed jobs** is allowed once, for a suspected runner or environment problem ([failure-policy.md](../ci/failure-policy.md#retries-and-flaky-tests)). The re-run gets its own artifact, because the attempt number changes.

## 5. From a terminal (optional, unverified)

The GitHub CLI (`gh`) is **not installed** on the machine this guide was written on, so these commands were not checked against an actual run. They need `gh auth login` first.

```powershell
gh workflow run tests.yml --ref main -f scope=full -f categories=Timing
gh run list --workflow tests.yml --limit 5
gh run view <run-id> --log-failed
gh run download <run-id> --name test-results-ci-<run-id>-1
```

## 6. Running the CI pipeline locally

The workflow's test steps are two scripts that you can run yourself from the repository root (see [testing-commands.md](testing-commands.md#change-based-regression-and-reports-scripts)):

```powershell
powershell -File Tests\regression\Invoke-Regression.ps1 -RunId local-20261017-01 -WarnAsError
powershell -File Tests\ci\Publish-TestResults.ps1 -Artifacts qa\regression\local-20261017-01\artifacts -InformationalPrefixes E2E-
```

The second command applies the same pass or fail rule as CI. Its exit code 1 means a new failure.

## 7. Not configured

- **Coverage:** no coverage reports or coverage gates (no coverage tool is installed).
- **Deployment:** no release, deployment or nightly scheduled workflow.
- **Hardware:** no self-hosted runner and no hardware tests. The manual cases MAN-001…017 remain a manual sign-off.
- **Repository settings:** branch protection and the optional AI variable and secret must be set in GitHub; they cannot be verified from the repository.
