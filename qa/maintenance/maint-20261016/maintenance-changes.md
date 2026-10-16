# Maintenance Changes — maint-20261016

**Test-only changes.** They are uncommitted, against `c4586de`. No production, solution, CI-workflow or deployment file changed.

| File | Change | Reason | Test IDs |
|---|---|---|---|
| `Tests/CT007.Tests/Fixtures/TestBackend.cs` | Added `RefusedSqlConnection` (one definition), with `Connect Timeout=1` instead of 3 | Removes duplication; about 4 s faster per run (measured) | BE-002, INT-002 |
| `Tests/CT007.Tests/Backend/DrillholeApiTests.cs` | Uses `TestBackend.RefusedSqlConnection`; local constant removed | Duplication | BE-002 |
| `Tests/CT007.Tests/Integration/HttpIntegrationTests.cs` | Same | Duplication | INT-002 |
| `Tests/CT007.Tests/Backend/CsvExportTests.cs` | `Header` is now `internal`, with a doc comment | One CSV-contract definition | BE-042, BE-043 |
| `Tests/CT007.Tests/EndToEnd/DesktopWorkflowTests.cs` | Uses `CsvExportTests.Header`; its own copy removed | Duplication. Compiles; E2E itself is not executed here | E2E-001 |
| `Tests/CT007.Tests/Fixtures/AppDatabase.cs` | A private `Open(path, foreignKeys)` replaces three copies of the connection setup | Duplication; behaviour unchanged (unpooled; foreign keys off only for raw fixture SQL) | BE-031…040, INT-007…017 (helpers) |
| `Tests/CT007.Tests/Fixtures/Fakes.cs` | Removed the unused `MemoryRepository.SaveCalls` (and its counter) and `FakeGammaGuard.Writer` | Dead code | — |
| `Tests/CT007.Tests/Frontend/ApiClientTests.cs` | FE-008 now also asserts that Device Status checks stop after dispose (counting device-list function, 50 ms intervals) | **Assertion strengthened** to match the Phase 2 expectation | FE-008 |
| `Tests/CT007.Tests/Fixtures/TestFolder.cs` | Remembers every folder it creates; `DeleteLeftovers()` runs a GC pass and deletes any that still exist after all tests | Folders recreated by a finished test's background load, or locked by a leaked connection, were left in `%TEMP%` silently | All tests using `TestFolder` |
| `Tests/CT007.Tests/TestRunner.cs` | Calls `TestFolder.DeleteLeftovers()` at the end and reports any folder that remains | Visible cleanup result | — |
| `Tests/README.md` | Added "Where to start (maintainers and AI agents)" | Reusable entry points; avoids repository rescans | — |

**Removed or consolidated tests:** none. **Assertions weakened:** none (the diff shows one assertion added and one moved to the shared constant).

## Unresolved limitations

- `corrupt.db` from BE-040 remains locked until the process exits (SUS-09, production code). This is now reported by the runner.
- The E2E fixed waits and all E2E code remain unexecuted (no dedicated desktop account).
- The CI workflow was reviewed (see [test-optimization](test-optimization.md)). No change was needed, and it has still not run on GitHub.
