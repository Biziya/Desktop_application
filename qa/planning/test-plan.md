# Test Plan — SPP CT-007 Measurement Studio

Phase 2 (planning only). Written 2026-10-16 from the Phase 1 documents in `../analysis/`, plus a targeted reading of
`Depth.cs`, `DeviceSamples.cs`, `DeviceCaptureService.cs`, `CaptureDurationSettings.cs`, `CsvExportService.cs`,
`MeasurementColumns.cs`, `SmokeTests.csproj` and `tools/check-db.ps1`. No tests were written or run. No production
code, existing tests, databases or configuration were changed.

| Document | Content |
|---|---|
| [test-inventory.md](test-inventory.md) | All planned scenarios, with ID, level, priority, automation and expectation status |
| [frontend-test-cases.md](frontend-test-cases.md) | FE-001…FE-051: ViewModels, UI state, API client, presentation |
| [backend-test-cases.md](backend-test-cases.md) | BE-001…BE-046: ASP.NET API, plus the in-process business and data layer |
| [integration-test-cases.md](integration-test-cases.md) | INT-001…INT-017 and E2E-001…E2E-004 |
| [manual-test-cases.md](manual-test-cases.md) | MAN-001…MAN-017: hardware, visual, locale and judgement checks |
| [test-data-requirements.md](test-data-requirements.md) | Environments (ENV-*), test data (TD-*), isolation and cleanup |
| [unresolved-requirements.md](unresolved-requirements.md) | UR-01…UR-30: open questions, conflicts, blockers, Phase 1 corrections |

## 1. Objectives

1. **Protect measurement data.** Every capture must produce the right averaged values for the right depth interval. It must be saved once, under the right drillhole session. It must survive restarts, migrations, failures and exports.
2. **Protect the vendor database.** The Gamma Guard SQLite file must never be modified.
3. **Verify the core workflow:** select drillhole → Save & Continue → arm → right-click capture → table/persist → delete/export.
4. **Verify degradation:** the backend offline or failing, Gamma Guard missing or locked, the app DB locked, invalid input.
5. **Give the backend its first tests** (there are none today) and lock down the duplicated drillhole JSON contract.
6. **Make open requirements visible** instead of encoding current behaviour as correct.

## 2. Scope

### 2.1 In scope

| Area | Features (`../analysis/feature-inventory.md`) | Documents |
|---|---|---|
| Frontend presentation and state (ViewModels, page logic, toasts, Device Status, formatting) | F03–F14, F16, F17, F20 | FE |
| Frontend API client and connectivity | F01, F02 (client side), F05 | FE |
| ASP.NET Core backend | F01 (server), F21, F22 | BE-001…BE-014 |
| In-process business and data layer (capture, averaging, depth, ordering, SQLite repository, migrations, Gamma Guard reading, CSV) | F02, F07, F08, F12, F13, F15, F16, F18, F19, F23 | BE-015…BE-046 |
| Cross-layer and end-to-end workflows | All | INT, E2E |
| Hardware, visual, locale, accessibility | F06, F14, F15, F19, F20 | MAN |

**Layer convention.** In this product most of the "backend" runs inside the WPF process (see `../analysis/frontend-analysis.md` §1).
- **FE** covers code whose job is presentation or UI state.
- **BE** covers both the ASP.NET project (`Backend/`) and the non-UI business and data classes inside `Frontend/`.

Each case's *Source* line names the actual project.

### 2.2 Exclusions

- The production SQL Server and any other live corporate system. Real SQL queries are tested only against a disposable instance (BE-006, BE-007).
- Gamma Guard internals and the CT007-M firmware. Only their observable output is checked (MAN-001…MAN-004).
- Load testing of the backend beyond one large-table check. It is a single-user, local service.
- Pixel-exact visual regression. Visual checks are manual (MAN-008).
- Code that isn't used (F21/F22) gets only Low-priority tests until UR-07 is answered.
- Implementing test projects, fixtures or CI. That is Phase 3 and later.

## 3. Methodology

- **Risk-based.** Effort goes to capture correctness, persistence, migrations, session isolation and vendor-DB safety first.
- **Evidence-based expectations.** Evidence ranks as follows:
  1. Approved requirements.
  2. Documented contracts.
  3. Validated tests.
  4. The implementation.

  Rung 1 doesn't exist (the root `README.md` is an unapproved design draft that conflicts with the code, UR-01). Rung 3 isn't valid either: the smoke tests can't currently be built, so their results are unverified (UR-24). So **no expectation is Confirmed**: each one is either **Inferred** (from code, cited) or **Unresolved** (linked to a UR item). Promote an expectation to Confirmed only after the product owner signs it off.
- **Reuse.** About 359 existing smoke-test assertions already exercise many of these behaviours. Each case lists *Existing coverage* so Phase 3 can port that coverage rather than re-invent it.
- **Data-driven cases.** Parsers and boundary checks are written as one case with an input table, not one case per value.

## 4. Risk and priority rules

`Risk = Impact (1–5) × Likelihood (1–5)`. The score is planning guidance, not a measured probability.

| Score | Priority |
|---|---|
| 16–25 | Critical |
| 9–15 | High |
| 4–8 | Medium |
| 1–3 | Low |

| Impact | Meaning here |
|---|---|
| 5 | A wrong, lost or misattributed measurement; vendor DB modified; credentials exposed |
| 4 | The core workflow is blocked, or wrong reference data is shown, without data loss |
| 3 | A degraded workflow with a workaround; a misleading status or message |
| 2 | A cosmetic issue, or a secondary feature |
| 1 | Unused code |

Likelihood rises with these factors:
- Complexity: async, cancellation, migrations, time arithmetic.
- External dependencies: the vendor DB, OS, network or hardware.
- No existing coverage, or code that changed recently (the capture and Bluetooth code changed on 2026-10-16).
- Ambiguous requirements.

**Overrides.** BE-010 (committed credentials) and BE-027 (vendor DB never written) are Critical regardless of score.

## 5. Test levels

| Level | Used for | Notes |
|---|---|---|
| Unit | Pure logic: `Depth`, `MeasurementOrder`, `CaptureWindow`, `SampleAveraging`, `DeviceTime`, the detector pick, `DrillholeCatalog`, cell formatting, `CsvExportService.Serialize`, settings parsing | The fastest feedback; most boundary cases live here |
| Component | ViewModels with fakes (`MemoryRepository`, `ControlledMeasurements`, `FakeDialogs`, `StubBackend`, `FakeCaptureClock`) | Needs an STA thread and a dispatcher (ENV-STA) |
| Integration | Real SQLite files; a fake Gamma Guard DB; ViewModel + real repository; in-memory ASP.NET host + real `ApiClient` | Temp folders only |
| API | Backend endpoints through `WebApplicationFactory` with a substituted `IDrillholeRepository` | No real SQL Server |
| E2E | Full app with UI automation, a local backend, a simulated Gamma Guard writer | Few; only the main journeys |
| Security | Secret scanning, auth posture, error-body leakage, CSV injection | Partly static |
| Performance | The 20k drillhole dropdown, a large backend table, a long session | Thresholds unresolved (UR-21) |
| Manual | Hardware, timing calibration, the right-click input matrix, DPI, locale, accessibility | `manual-test-cases.md` |

A scenario gets more than one level only when the levels catch different defects. For example, BE-020 unit-tests the window rule, INT-007 tests the same rule wired to real SQLite, and MAN-001 tests it with the real device.

## 6. Automation strategy

- **Global prerequisite (blocker, UR-24).** `SmokeTests.csproj` targets `net10.0-windows`, but `Frontend.csproj` targets `net10.0-windows10.0.19041.0`, which gives restore error NU1201. No frontend test can build until a test project targets `net10.0-windows10.0.19041.0`. "Automatable" in this plan assumes that fix.
- **Recommended structure for Phase 3:**
  - `Tests/Frontend.Tests`: xUnit with an STA runner, targeting the Windows 10.0.19041 framework.
  - `Tests/Backend.Tests`: xUnit + `Microsoft.AspNetCore.Mvc.Testing`.
  - `Tests/Integration.Tests`: the same framework as `Frontend.Tests`.

  The root README already proposes this layout, and it gives per-test results, which the current stop-at-first-failure harness can't (`../analysis/testing-infrastructure.md` §2). Whether to migrate or extend the smoke harness is a decision for the team (UR-24).
- Reuse the existing fakes from `SmokeTests/Program.cs`.
- Replace wall-clock assertions with `FakeCaptureClock` or injectable delays wherever a seam exists. Elsewhere, use generous timeouts and mark the test `[Trait("Category","Timing")]`.
- E2E uses UI automation (FlaUI/UIA3 is suggested) on a dedicated Windows test account or VM. The reason: `MainViewModel()` resolves `%LocalAppData%` through the shell, which probably can't be redirected by an environment variable (Inferred, UR-27).
- Do not add `InternalsVisibleTo` or other seams in Phase 3 without approval, because that changes production code (UR-27). Cases that need such seams are marked **Blocked**.

## 7. Environment requirements

See [test-data-requirements.md](test-data-requirements.md) §1. In summary:
- Windows 10 2004+ or Windows 11, the .NET 10 SDK, and an STA test runner.
- Temp-folder SQLite.
- An in-memory ASP.NET host.
- Optionally, a disposable SQL Server instance (LocalDB or a container).
- A UI-automation desktop session.
- A hardware bench with a CT007-M detector, Gamma Guard and Bluetooth.

## 8. Entry and exit criteria

**Entry (Phase 3 / Phase 4):**
- UR-24 resolved (a test project builds against Frontend).
- The approach to the remaining Blocked items agreed.
- The fixtures in `test-data-requirements.md` §2 available.
- No production connection strings in test configuration.

**Exit (release candidate):**
- 100 % of Critical and High automatable cases pass on Windows.
- No open Critical/High defect without a written waiver.
- MAN-001, MAN-002, MAN-006 and MAN-007 run on the target hardware, with signed results.
- Every Unresolved expectation that a passed test relies on has been answered in `unresolved-requirements.md`, or is listed as an accepted risk.
- No test reports claim requirement coverage, because no approved requirements exist (UR-01).

## 9. Execution strategy

| Stage | Content | When |
|---|---|---|
| **Fast checks** (< 2 min) | `dotnet build` of the solution with warnings visible; a secret scan (BE-010); all **Unit** cases; Component cases without the Timing trait | Locally before every commit; on every PR |
| **Integration checks** (< 10 min) | SQLite repository and migrations; fake Gamma Guard capture pipeline; API tests with `WebApplicationFactory`; INT-001…INT-017 except the Conditional ones that need SQL Server | On every PR; locally when touching `Pages/Measurement`, `Calculation/**`, `Api/**` or `Backend/**` |
| **Full regression** | Everything above + Timing-trait tests + E2E-001…E2E-004 + BE-006/BE-007 against a disposable SQL Server + BE-045 (check-db script) | Nightly, and before every release |
| **Manual** | MAN-001…MAN-017 | Before a release; after changes to capture, Bluetooth, the input trigger, layout or export; after Gamma Guard or firmware updates |

This plan does not implement CI (see `../analysis/testing-infrastructure.md` §5: none exists).

## 10. Completion check (Phase 2)

- **Frontend features considered:** F03–F14, F16, F17 and F20 are all mapped in the inventory's feature column, plus the frontend parts of F01, F02 and F05.
- **Backend features considered:** F01, F21 and F22, and the in-process F02, F07, F08, F12, F13, F15, F18, F19 and F23.
- **Cross-layer workflows considered:** drillhole fetch/cache/offline, capture → persist → reload, delete, export, migration on start, and error propagation (INT/E2E).
- **IDs are unique and contiguous** in every group (verified by a script: 135 IDs, 135 inventory rows, every link resolves).
- **Totals:** 135 scenarios, 18 with Unresolved expectations, and 30 UR items.
- **No requirements coverage is claimed.** Every expectation is Inferred or Unresolved.
