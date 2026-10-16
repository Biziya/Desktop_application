# Regression Test Results — reg-20261016-01

**Environment.**
- Same machine and SDK as the baseline: Windows 11 Pro 10.0.26200, .NET SDK 10.0.402, user `user`.
- Built and run by [`Invoke-Regression.ps1`](../../../Tests/regression/Invoke-Regression.ps1) in an isolated copy (`Frontend/`, `Backend/`, `Tests/`, `CT007.slnx`, without `bin`/`obj`), with an offline restore from the existing package cache.
- The build had 0 errors and 0 warnings ([`artifacts/build.log`](artifacts/build.log)).
- Isolation and test data were the same as Phase 4: temp folders, a loopback backend and a simulated Gamma Guard file. No temp folders were left over.

**Comparability.** Production code, test code and environment are the same as the baseline. Only the set of selected tests differs.

**Evidence.**
- [`artifacts/results.csv`](artifacts/results.csv) and [`artifacts/console.log`](artifacts/console.log) (current run).
- [`run-final-results.csv`](../../execution/evidence/run-final-results.csv) (previous run).

The run was executed twice with the same selection: the second time to confirm the script's UTF-8 logging fix. Both gave identical outcomes. The artifacts are from the second execution.

| Test ID | Test method | Feature | Previous Status | Current Status | Change |
|---|---|---|---|---|---|
| BE-020 | CaptureLogicTests.WindowMembership | F15 capture window | PASS | PASS | — |
| BE-025 | CaptureLogicTests.PollingAndFinalRead | F15 polling/final read | PASS | PASS | — |
| BE-027 | LocalFilesTests.VendorDatabaseReadOnly | F15/F08 vendor DB read-only | PASS | PASS | — |
| BE-027 | LocalFilesTests.BrokenOrLockedDatabase | F15/F08 | PASS | PASS | — |
| BE-030 | LocalFilesTests.DurationSettings | F13 duration settings | FAIL | FAIL | Known defect DEF-002 (same message) |
| BE-032 | MeasurementRepositoryTests.AtomicSave | F18 atomic save | PASS | PASS | — |
| BE-036 | MeasurementRepositoryTests.Migrations | F18 migrations | PASS | PASS | — |
| BE-037 | MeasurementRepositoryTests.MigrationRefusals | F18 refusals/rollback | PASS | PASS | — |
| FE-005 | ApiClientTests.SettingsDefaults | F05 API settings | FAIL | FAIL | Known defect DEF-001 (same message) |
| FE-026 | MeasurementWorkflowTests.CaptureCycle | F14 capture cycle | PASS | PASS | — |
| FE-035 | MeasurementWorkflowTests.SaveFailureRetry | F18 save retry | PASS | PASS | — |
| FE-036 | MeasurementWorkflowTests.DrillholeSwitch | F11 drillhole switch | PASS | PASS | — |
| FE-036 | MeasurementWorkflowTests.SwitchRefusedWhileUnsaved | F11 | PASS | PASS | — |
| INT-007 | CapturePipelineTests.CaptureToStorage | F15/F18 pipeline | PASS | PASS | — |
| INT-008 | CapturePipelineTests.ConcurrentWriter | F15 concurrent writer | PASS | PASS | — |
| INT-010 | CapturePipelineTests.AppDatabaseFailureAndRecovery | F18 DB failure recovery | PASS | PASS | — |
| INT-012 | CapturePipelineTests.NoSessionMixing | F11 session isolation | PASS | PASS | — |

**Totals:** 17 executed — 15 passed, 2 failed, 0 skipped, 0 blocked. 90 implemented tests were not selected (see the [impact analysis](impact-analysis.md)).
