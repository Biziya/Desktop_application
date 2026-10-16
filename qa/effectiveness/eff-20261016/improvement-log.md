# Improvement Log — eff-20261016

All changes are in test code, test configuration or test tooling, and uncommitted. They come on top of the Phase 8 changes, which are also uncommitted. No production file changed.

| File | Change | Reason (evidence) | Test IDs | Validation |
|---|---|---|---|---|
| `Tests/CT007.Tests/Backend/LocalFilesTests.cs` | The non-numeric case moved into a new method `DurationSettingsNonNumericValue`; the rest of `DurationSettings` unchanged | DEF-002 ended the method early and masked the limit, parsing and save checks (M26 and M27 survived) | BE-030 | `DurationSettings` passes; the new method fails with the known DEF-002 message; M26 and M27 killed (pass 3) |
| `Tests/CT007.Tests/Frontend/ApiClientTests.cs` | The non-numeric case moved into a new method `SettingsNonNumericValue` | DEF-001 masked the cases after it | FE-005 | `SettingsDefaults` passes (its relative-URL case now actually runs); the new method fails with the known DEF-001 message |
| `Tests/CT007.Tests/Frontend/DrillholeSelectionTests.cs` | Added a tie-break assertion: query "a" → closest "AB" (the first listed of three prefix matches) | M60 survived; the rule is stated in the `DrillholeCatalog.Options` documentation | FE-014 | Passes; M60 killed (pass 3) |
| `Tests/ci/known-failures.json` | The entries now name the new defect-only methods | The known defects moved to their own methods; matching is by method | FE-005, BE-030 | CI publisher on the validation run: check passed, 2 known-defect warnings, 0 blocking |
| `Tests/regression/impact-map.json` | `MeasurementResult.cs` (with the ViewModel rule) now also selects FE-046; `Pages/CoreInformation/` selects FE-007 | M49 and M62 survived because CI would not have selected the detecting test | FE-046, FE-007 | M49 and M62 killed (pass 3) |
| `Tests/mutation/generate_mutants.py`, `mutants.json`, `Invoke-MutationRun.ps1` (new) | Repeatable controlled mutation testing in a disposable copy | No mutation tool available (no Stryker.NET; installing it needs approval) | 77 mutants | Used for passes 1–3 |

**Assertions weakened or tests removed: none.** Each split moved an existing case, unchanged, into its own method, so it now runs independently. The total went from 107 to 109 test methods.

## Validation

**Full suite, Timing included, through `Invoke-Regression.ps1` and the CI publisher:** [artifacts/validation/](artifacts/validation/)

| Passed | Failed | Skipped | Check |
|---|---|---|---|
| 102 | 2 (the two isolated known-defect methods) | 5 (Sql, E2E: environment) | Passed |

Compared with Phase 8: the same outcome for every previously existing case. The two extra methods account for the extra passes.

- **Production code:** `git diff` of `Frontend/`, `Backend/`, `SmokeTests/`, `tools/`, `CT007.slnx` and `.github/` is empty.

## Remaining limitations

- The mutation run is manual and targeted (77 mutants on high-risk logic). It is not a tool-generated exhaustive run. A Stryker.NET run would need approval to install the tool.
- M46 survives (low priority), and M77 has no coverage until BE-006 can run.
- **Leftover temp folders.** The mutation runs left about 16 temporary test folders under `%TEMP%\ct007-tests`, from test processes that stopped early on a failing test; the in-run cleanup only covers its own process. They were deleted manually. The SUS-09 lock (1 folder per run) persists, as documented in Phase 8.
