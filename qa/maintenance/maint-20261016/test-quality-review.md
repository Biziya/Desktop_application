# Test Quality Review — maint-20261016

**Scope:**
- All 23 C# files of `Tests/CT007.Tests`: runner, 7 fixtures, 5 FE, 6 BE, 2 INT and 2 E2E files; 4,942 lines.
- The 3 PowerShell scripts, `impact-map.json`, `known-failures.json`, `.github/workflows/tests.yml` and `Tests/README.md`.

That is 30 files in total.

**Method:**
- Scripted scans: the number of assertions per test method, always-true patterns, fixture members nothing references, duplicated constants, fixed delays, and hardcoded user paths.
- Outcome and duration comparison across all six recorded runs (Phases 4, 6, 7).
- A manual review of the findings.

## Organisation

- **Sound.** There is one file per area (API client, selection, workflow, capture logic, repository, CSV, API, HTTP integration, capture pipeline, E2E). Fixtures are separate from tests, and namespaces don't shadow the `Frontend`/`Backend` product namespaces.
- **Naming:** every test has `[Test("<ID>", "<observable behaviour>")]` with a descriptive method name. Three IDs have two methods each (BE-024, BE-027, FE-036), each covering a distinct aspect. No test lacks an ID, and no ID is unknown to the inventory.
- **No hardcoded user paths.** All data lives in per-test temp folders. The E2E bench locates `Frontend.exe` relative to the build output, or through `CT007_FRONTEND_DIR`.
- **Shared state:** only immutable statics (`Make.Core`, `Samples.Detector`). The culture is reset by the runner, and the one environment variable a test sets (`CT007_TEST_GG_DIR`) is restored in `finally`.

## Assertion quality

| Finding | Tests | Assessment | Action |
|---|---|---|---|
| Few assertion calls per method | BE-015, 016, 019, 021, 039; BE-005; BE-024 (second method); E2E-003, 004 | Not weak: they are data tables asserted in a loop, or their waits (`Eventually`, `WaitAsync`) are themselves assertions | None |
| An assertion narrower than the Phase 2 expectation | **FE-008** checked only that HTTP probes stop after dispose. FE-008 also expects Device Status checks to stop | Weak (incomplete) | **Strengthened:** a counting Bluetooth list function plus short Device Status intervals; asserts no Device Status check after dispose |
| Asserts current behaviour pending a requirement | FE-021, FE-028, FE-037, FE-048 (UR-09, 23, 30); INT-009 (any error toast, D13); BE-031 case sensitivity not asserted (UR-19) | Documented as Inferred in Phases 3–5 | None. Changing them needs the UR answers |
| Status asserted, message not | FE-004 JSON `null` (Phase 3 D1) | Message differs from the plan; status is the contract | None |
| Reproduces configuration | FE-047 (hidden columns) | Low value but cheap; documents the layout | Kept |
| Always-true / no-op patterns (`\|\| true`, `Assert.True(true)`, seed checks) | — | None found | — |

No assertion was weakened or removed in this phase (diff reviewed).

## Duplication and abstraction

| Finding | Action |
|---|---|
| Refused SQL connection string defined twice (DrillholeApiTests, HttpIntegrationTests) | **Moved** to one `TestBackend.RefusedSqlConnection` |
| CSV header defined twice (CsvExportTests, DesktopWorkflowTests) | **One definition**: `CsvExportTests.Header` (internal), reused by E2E-001 |
| Three copies of the connection setup in `AppDatabase` | **One** private `Open(path, foreignKeys)` |
| `MemoryRepository.SaveCalls` and `FakeGammaGuard.Writer` never used | **Removed** |
| `Page` (FE workflow tests) and `Pipeline` (INT capture tests) look similar | **Kept.** They wire different layers (a fake service and repository versus the real capture service and SQLite); merging them would hide the difference |
| `Page.Toasted` is a one-line wrapper around `Make.Toasted` | Kept: it removes a repeated argument at about 40 call sites |
| `TestBackend` mirrors `Backend/Program.cs` | Necessary (no `WebApplicationFactory` package). The impact map flags `Program.cs` changes for review |

The helper depth is at most one level (test → `Make` / fake). No extra abstraction was added.

## Recommended further improvements (not done)

- **E2E fixed waits.** E2E uses fixed waits (`Task.Delay(500)` after typing and after the export). Replace them with event-based waits once E2E can be executed. They were not changed now, because E2E cannot run here and an edit would be unverified.
- **xUnit.** If NuGet packages are ever approved, migrating the harness to xUnit would give IDE integration. The current harness is adequate and dependency-free.
