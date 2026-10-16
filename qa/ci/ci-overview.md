# CI Overview

| | |
|---|---|
| Platform | **GitHub Actions**. The repository is `github.com/Biziya/Desktop_application`; no CI configuration existed before |
| Workflow | [`.github/workflows/tests.yml`](../../.github/workflows/tests.yml): one file, two jobs |
| Status | **Locally validated, not yet run on GitHub** (see [setup-requirements.md](setup-requirements.md) §5) |
| Deploys or releases | Never |

## Triggers

| Event | When | Scope |
|---|---|---|
| `pull_request` | Opened or updated, targeting `main` or `development` | Change-based: diff against the PR base commit |
| `push` | To `main` or `development` (skipped when only `qa/**`, `*.md` or `Images/**` changed) | Change-based: diff against `github.event.before`; full suite for a new branch or a force-push |
| `workflow_dispatch` | Manual, from the Actions tab | `scope`: `full` (default) or `changed` (against `HEAD~1`); `categories` default `Timing,Sql,E2E` |
| `workflow_call` | From a future release workflow (none exists) | Release validation: `scope: full` by default |

**No duplicate runs.**
- Feature-branch pushes don't trigger the workflow; their pull requests do.
- `concurrency` cancels a superseded run of the same PR or branch.

## Jobs and stages

```text
tests (windows-latest, ~5–15 min)                         ai-analysis (ubuntu-latest, optional)
 1 checkout (full history, no persisted token)              runs only if tests failed AND
 2 setup .NET 10 + NuGet cache                              vars.CT007_AI_ANALYSIS == 'true' AND
 3 decide scope (baseline, full?, categories)               the PR is not from a fork
 4 [Sql] disposable LocalDB   [E2E] enable bench flag       → checkout Tests/ci from the default branch
 5 Tests/regression/Invoke-Regression.ps1                   → download the test artifacts
      select tests from impact-map.json (+ critical set)    → Tests/ci/Invoke-FailureAnalysis.ps1
      isolated copy → restore → build -warnaserror → run    → ai-analysis.md + job summary
 6 Tests/ci/Publish-TestResults.ps1 → junit.xml, summary, annotations, pass/fail decision
 7 upload artifacts (always)
```

- **One build covers frontend and backend.** All FE, BE, INT and E2E tests are in the single `Tests/CT007.Tests` harness, which builds Frontend and Backend once. Separate frontend and backend jobs would build the same code twice for no extra coverage.
- **Selection is deterministic.** It comes from the [impact map](../../Tests/regression/impact-map.json), not from AI. Shared infrastructure, project files, test code and CI files select the **full** suite. A production file the map does not cover also selects the full suite.

## Artifacts

GitHub Actions artifact `test-results-ci-<run_id>-<attempt>`, kept for 14 days:

| File | Content |
|---|---|
| `junit.xml` | Standard JUnit XML (ID, method, status, duration, failure or skip message) |
| `results.csv` | Native runner output (`id, test, outcome, ms, message`) |
| `summary.md` | Same as the job summary |
| `selection.json` | Changed files, the rules matched, the selected IDs, the reason for a full run |
| `console.log`, `build.log` | Raw runner and build output |

Optional artifact `ai-analysis-…`: `ai-analysis.md`.

Coverage files and screenshots are not produced: no coverage tool is installed, and the E2E bench does not capture screenshots.

## AI-assisted analysis rules

- **Off by default.** It runs only for failed runs and needs the opt-in variable and a secret. The test pipeline never depends on it.
- **Minimal context.** It sends at most 25 failing rows (messages truncated to 400 characters, with secret-like patterns masked), the changed-file list and the known-defect list. No logs, source code or environment variables are sent.
- **Model.** `claude-opus-5-5` at `effort: low` with server-side `fallbacks: "default"`, one request per failed run. Its output is labelled "suggestions only". It never changes code, tests, the check result, or PR state.
- **Untrusted input.** The analysis script is checked out from the default branch, so pull-request code never runs with the API key. Fork PRs are excluded. The prompt tells the model to treat CI data as data and never to suggest running commands taken from it.
