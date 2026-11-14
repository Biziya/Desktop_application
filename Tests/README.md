# Tests

`CT007.Tests.csproj` is a plain console test harness for the frontend and backend. It uses no test framework package
and prints one result per test. The exit code is 1 if any test fails.

```powershell
dotnet run --project Tests                                    # default set
dotnet run --project Tests -- FE- BE-02                       # only tests whose ID starts with these
dotnet run --project Tests -- --include=Timing,Sql,E2E        # add opt-in categories
dotnet run --project Tests -- --results=results.csv           # also write results to a CSV file
```

Test IDs start with `FE-` (frontend), `BE-` (backend logic and API), `INT-` (integration) or `E2E-`. Tests live in `Frontend/`, `Backend/`, `Integration/` and
`EndToEnd/`; shared helpers are in `Fixtures/`.

## Opt-in categories

These are skipped unless named with `--include`.

| Category | Needs |
|---|---|
| `Timing` | Real-time waits. Run on an idle machine. |
| `Sql` | `CT007_TEST_SQL` set to a disposable local SQL Server database whose name starts with `CT007_Test` (LocalDB works). The tests create and drop their own tables. |
| `E2E` | A dedicated Windows test account with `CT007_E2E_DEDICATED_ACCOUNT=1`, an unlocked desktop at 100% scaling, and a Debug build of the frontend (or `CT007_FRONTEND_DIR` pointing at one). The app's `%LocalAppData%\CT007_Measurement_Studio` folder is moved aside and restored afterwards. |

CI (`.github/workflows/tests.yml`) builds `CT007.slnx` and runs the harness with `--include=Timing`.

## Isolation

- Each test gets its own folder under `%TEMP%\ct007-tests\`, deleted afterwards.
- Gamma Guard databases are SQLite files the test writes itself; no real installation is used.
- `Backend/appsettings*.json` is removed from the test output, so no test can reach the real SQL Server.
- Set `CT007_SCREENSHOTS` to a folder to save PNGs of the windows the UI tests render.
