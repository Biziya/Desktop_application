# CI Setup Requirements

## 1. Runtimes and tools

| Need | Provided by |
|---|---|
| Windows (WPF and WinRT targets `net10.0-windows10.0.19041.0`) | `windows-latest` GitHub-hosted runner |
| .NET SDK 10.0.x | `actions/setup-dotnet@v4` (`dotnet-version: 10.0.x`). The repository has no `global.json`; add one to pin an exact SDK |
| PowerShell 7 for workflow steps; Windows PowerShell 5.1 or 7 for the scripts | Both are preinstalled on `windows-latest`. Steps use `shell: pwsh`, because 5.1's `>>` would write `GITHUB_OUTPUT` as UTF-16 |
| NuGet packages (Microsoft.Data.Sqlite, Microsoft.Data.SqlClient, Swashbuckle; the ASP.NET Core shared framework) | Restored from nuget.org and cached with `actions/cache` (key: hash of `*.csproj`). No new dependency was added |
| SQL Server LocalDB, only for BE-006 | Used if the runner image has `sqllocaldb` and `sqlcmd`. Otherwise BE-006 is reported as skipped. Not verified on the current image |
| An interactive desktop, only for E2E | GitHub-hosted Windows runners. **Not verified** for UI Automation and mouse input |

## 2. Environment and isolation

- **Copy, never in place.** Tests run in a copy of the checkout under `RUNNER_TEMP\ct007\<run-id>`, so tracked `obj/` files are never touched.
- **Temp data only.** All test data is in per-test `%TEMP%\ct007-tests\<guid>` folders, an in-process backend on `127.0.0.1` with fake drillholes, and a SQLite stand-in for Gamma Guard.
- **No production configuration reaches the tests.** `Backend/appsettings.json`, with the committed SQL credentials (SUS-01), is deleted from the test output and never loaded. No test contacts the corporate SQL Server.
- **Sql:** a throwaway LocalDB instance `ct007` with the database `CT007_Test`, using integrated security and no secret. BE-006 refuses any non-local server or any other database name.
- **E2E:** `CT007_E2E_DEDICATED_ACCOUNT=1` is set only when `runner.environment == 'github-hosted'` (an ephemeral VM). **Never set it on a self-hosted runner that has a real user profile.**
- **Separate jobs.** Each job has its own runner VM, so concurrent runs cannot share test data.
- **Path limit (observed in Phase 7).** SQLite's native DLL fails to load once its path reaches about 252 characters. `Invoke-Regression.ps1` refuses a work folder deeper than that. `RUNNER_TEMP` (`D:\a\_temp`) is well inside the limit.

## 3. Permissions and security

- **Workflow token:** `permissions: contents: read` for both jobs. There are no write scopes, and `persist-credentials: false` is set on checkout.
- **Secrets:** only `ANTHROPIC_API_KEY`, which is optional and used only by `ai-analysis`. That job never runs for fork PRs, and it runs the analysis script from the default branch rather than from the PR. The test job uses no secrets.
- **Actions used:**
  - `actions/checkout@v4`
  - `actions/setup-dotnet@v4`
  - `actions/cache@v4`
  - `actions/upload-artifact@v4`
  - `actions/download-artifact@v4`

  These are pinned to major-version tags. The repository has no stated pinning policy. **Recommendation:** pin them to full commit SHAs, for example with Dependabot for actions.
- **Artifacts** hold exception messages from synthetic test data. The fixtures contain no real credentials, and the AI step masks secret-like patterns before sending anything.

## 4. Manual GitHub configuration (repository admin)

1. Settings → Branches → protect `main` and `development`, and require the status check **`Build and test (Windows)`**.
2. Optional AI triage: Settings → Secrets and variables → Actions:
   - add the secret `ANTHROPIC_API_KEY`;
   - add the variable `CT007_AI_ANALYSIS=true`.
3. Settings → Actions → General:
   - require approval for workflows from first-time or outside contributors;
   - keep the default `GITHUB_TOKEN` permission read-only.
4. Optional: add `global.json` (pins the SDK), and Dependabot for `github-actions`.

## 5. Validation performed (local, 2026-10-16)

| Check | Result |
|---|---|
| Workflow syntax | No YAML validator or `actionlint` is installed (none was installed). Structural checks passed: no tabs, consistent indentation, balanced `${{ }}`, all referenced script paths exist. **Not parsed by GitHub yet** |
| "Decide scope" step logic | Simulated for PR, new branch, unknown `before`, normal push, manual `changed` and `full`: outputs as intended. Local quirk: the checkout's git "dubious ownership" error had to be overridden; GitHub runners don't have it. The step's `pwsh` output encoding was emulated, because `pwsh` is not installed locally |
| "Build and run" step | Hashtable splatting into `Invoke-Regression.ps1` verified (`-SelectOnly`) |
| End-to-end dry run of steps 5 and 6 | Full suite, `-WarnAsError`, offline package source: build succeeded with 0 warnings; **100 passed, 2 failed (known defects), 5 skipped**, the same as Phase 4. The publish step passed the check with 2 warnings. Evidence: [`validation/`](validation/) |
| Publisher failure handling | Verified: a new failure exits 1 with an `::error` annotation; missing results exit 1; a known defect that now passes gives an `::notice`; `junit.xml` parses |
| AI script | `-DryRun` payload checked: correct model, effort and fallbacks, and secrets masked. Without an API key it skips with exit 0. **No API call was made** |
| Remote GitHub run | **Not performed.** Nothing was pushed and no remote pipeline was triggered |

**Found during validation:**
- the UTF-16 `GITHUB_OUTPUT` issue (fixed by using `pwsh`);
- non-ASCII characters breaking Windows PowerShell 5.1 parsing (scripts are now ASCII-only);
- the SQLite path-length limit (guard added);
- test-folder cleanup skipping deletion when SQLite failed to load (fixed in `Fixtures/TestFolder.cs`).
