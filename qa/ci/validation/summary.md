## CT007 tests: passed

Scope: full suite (.github/ (CI pipeline or test-selection tooling changed); Tests/ci/ (CI pipeline or test-selection tooling changed); Tests/regression/impact-map.json (CI pipeline or test-selection tooling changed))

| Passed | Failed (blocking) | Known defects | Informational | Skipped |
|---|---|---|---|---|
| 100 | 0 | 2 | 0 | 5 |

| Test | Classification | Message |
|---|---|---|
| BE-030 `LocalFilesTests.DurationSettings` | known defect DEF-002 | InvalidOperationException: The requested operation requires an element of type 'Number', but the target element has type 'String'. |
| FE-005 `ApiClientTests.SettingsDefaults` | known defect DEF-001 | InvalidOperationException: The requested operation requires an element of type 'Number', but the target element has type 'String'. |

Skipped (not passed): 1x category Sql not included; 4x category E2E not included
