# CT007 automated tests

`CT007.Tests` is a console test harness. It follows the same style as `SmokeTests`, with two differences:
- it reports **one result per test**;
- it targets the same framework as `Frontend.csproj` (`net10.0-windows10.0.19041.0`).

It needs no test-framework package. The plan, mapping and known limitations are in
`qa/generation/test-implementation-status.md`.

```powershell
dotnet run --project Tests/CT007.Tests                                    # default set
dotnet run --project Tests/CT007.Tests -- FE- BE-02                       # ID prefixes
dotnet run --project Tests/CT007.Tests -- --include=Timing,Sql,E2E --results=qa/results.csv
```

The exit code is 1 when any test fails.

## Opt-in categories

| Category | What it needs |
|---|---|
| `Timing` | Real-time waits (polling intervals, busy timeouts, a 1 Hz writer, close timeouts). Run them on an idle machine. |
| `Sql` | `CT007_TEST_SQL`: a connection string to a disposable, **local** SQL Server database whose name starts with `CT007_Test` (for example LocalDB). The test creates and drops `dbo.ZOOVCH_COLLAR_LIST`. |
| `E2E` | A dedicated Windows test account with `CT007_E2E_DEDICATED_ACCOUNT=1`, plus a built `Frontend/bin/Debug/net10.0-windows10.0.19041.0/Frontend.exe` (or point `CT007_FRONTEND_DIR` at another build). The account also needs an unlocked desktop and 100 % display scaling. The app's `%LocalAppData%\CT007_Measurement_Studio` folder is moved aside and restored afterwards. |

## Regression runs

```powershell
powershell -File Tests/regression/Invoke-Regression.ps1 -RunId reg-YYYYMMDD-NN [-Full] [-Include Timing,Sql,E2E] [-SelectOnly]
```

The script does four things:
1. It diffs `HEAD`, plus uncommitted files, against `qa/regression/baseline.json`.
2. It picks the direct and dependent tests from `Tests/regression/impact-map.json` and adds the critical set.
3. It builds and runs them in a copy under `%TEMP%`, so the tracked `obj/` folders are not changed.
4. It writes the artifacts to `qa/regression/<RunId>/artifacts/`.

A production file that the map does not cover selects the full suite. `-PackageSource` defaults to `%USERPROFILE%\.nuget\packages`.

After the results have been reviewed, update `baseline.json` by hand.

## Isolation

- Every test works in its own `%TEMP%\ct007-tests\<guid>` folder, and the folder is deleted afterwards.
- No test uses the default `MainViewModel()` constructor or a real Gamma Guard installation. Gamma Guard is always a SQLite file the test writes itself.
- No test uses the SQL Server in `Backend/appsettings.json`. That file is removed from the test output after every build.
