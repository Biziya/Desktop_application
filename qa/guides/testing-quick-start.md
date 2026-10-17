# Testing Quick Start

The fastest way to run the automated tests of SPP CT-007 Measurement Studio, both frontend (WPF) and backend (ASP.NET Core). The commands were checked against the project files but **not executed** while this guide was written.

## You need

- Windows 10 2004 or later (the projects target `net10.0-windows10.0.19041.0`).
- The .NET 10 SDK: `dotnet --version` must print `10.0.x`.
- The repository open in VS Code, with a PowerShell terminal (**Terminal → New Terminal**).
- The CT007 app **closed**: a running `Frontend.exe` blocks the build.

No database server, backend, detector or Gamma Guard installation is needed for the default tests. They use temporary SQLite files and an in-process fake backend.

## Run

From the repository root:

```powershell
dotnet --version
dotnet run --project Tests\CT007.Tests\CT007.Tests.csproj
```

This builds Frontend, Backend and the test harness, then runs every test except the opt-in categories (`Timing`, `Sql`, `E2E`).

To also run the real-time tests (about 70 s more) and keep a results file:

```powershell
$tests = ".\Tests\CT007.Tests\bin\Debug\net10.0-windows10.0.19041.0\CT007.Tests.exe"
& $tests "--include=Timing" "--results=$env:TEMP\ct007-results.csv"
```

## Read the result

The last line is a summary. For the default run with today's 110 tests, expect `110 tests: 94 passed, 2 failed, 14 skipped.` (the 14 skipped are the 9 Timing, 1 Sql and 4 E2E tests).

- **Two failures are expected:** `FE-005` and `BE-030` are confirmed defects DEF-001 and DEF-002, listed in [`Tests/ci/known-failures.json`](../../Tests/ci/known-failures.json).
- **Any other `FAIL` line is new.** See [testing-troubleshooting.md](testing-troubleshooting.md).
- **`SKIP` lines** are opt-in tests whose category was not included. That is normal.

## More

| I want to… | Read |
|---|---|
| Run only frontend, backend, integration, E2E, one module or one test | [testing-commands.md](testing-commands.md) |
| Follow a full step-by-step walkthrough | [manual-test-execution.md](manual-test-execution.md) |
| Understand PASS / FAIL / SKIP, result files and logs | [testing-results-guide.md](testing-results-guide.md) |
| Fix a failing run | [testing-troubleshooting.md](testing-troubleshooting.md) |
| Run the tests in GitHub Actions | [ci-cd-testing-guide.md](ci-cd-testing-guide.md) |
