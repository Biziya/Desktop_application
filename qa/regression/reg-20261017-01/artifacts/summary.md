## CT007 tests: FAILED

Scope: full suite (.github/workflows/tests.yml (CI pipeline or test-selection tooling changed); unmapped production file Backend/obj/Backend.csproj.nuget.dgspec.json; Backend/obj/Backend.csproj.nuget.g.props (build or dependency configuration))

| Passed | Failed (blocking) | Known defects | Informational | Skipped |
|---|---|---|---|---|
| 102 | 1 | 2 | 0 | 5 |

| Test | Classification | Message |
|---|---|---|
| BE-030 `LocalFilesTests.DurationSettingsNonNumericValue` | known defect DEF-002 | InvalidOperationException: The requested operation requires an element of type 'Number', but the target element has type 'String'. |
| FE-005 `ApiClientTests.SettingsNonNumericValue` | known defect DEF-001 | InvalidOperationException: The requested operation requires an element of type 'Number', but the target element has type 'String'. |
| FE-047 `StatusAndPresentationTests.ColumnLayouts` | **blocking** | AssertionException: column order: expected [No, Capture No, Start Time, End Time, From (m), To (m), Step (cm), Sample, ts_ms, count0, count0_raw, hdm, hdm_raw, dose_rate, Session], actual [No, Capture... |

Skipped (not passed): 1x category Sql not included; 4x category E2E not included
