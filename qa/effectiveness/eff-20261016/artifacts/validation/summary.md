## CT007 tests: passed

Scope: full suite (Tests/ci/known-failures.json (CI pipeline or test-selection tooling changed); Tests/CT007.Tests/Backend/CsvExportTests.cs (test code or fixtures changed); Tests/CT007.Tests/Backend/DrillholeApiTests.cs (test code or fixtures changed))

| Passed | Failed (blocking) | Known defects | Informational | Skipped |
|---|---|---|---|---|
| 102 | 0 | 2 | 0 | 5 |

| Test | Classification | Message |
|---|---|---|
| BE-030 `LocalFilesTests.DurationSettingsNonNumericValue` | known defect DEF-002 | InvalidOperationException: The requested operation requires an element of type 'Number', but the target element has type 'String'. |
| FE-005 `ApiClientTests.SettingsNonNumericValue` | known defect DEF-001 | InvalidOperationException: The requested operation requires an element of type 'Number', but the target element has type 'String'. |

Skipped (not passed): 1x category Sql not included; 4x category E2E not included
