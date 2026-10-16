# Test Execution Matrix

| Test category | IDs | Trigger | Runner | Required |
|---|---|---|---|---|
| Build and static check (`dotnet build -warnaserror` of the test project, Frontend and Backend) | — | Every run | `windows-latest` | **Yes** |
| Frontend unit and component (ViewModels, API client, formatting) | FE-001…049 | PR / push (change-selected); manual / release (all) | `windows-latest` (WPF needs Windows) | **Yes** |
| Backend unit (capture logic, depth, ordering, CSV, settings) | BE-015…030, BE-042/043 | Same | `windows-latest` | **Yes** |
| Backend API (in-process ASP.NET host, fake repository) | BE-001, 002, 005, 008, 011 | Same | `windows-latest` | **Yes** |
| Database integration (temp SQLite files, migrations) | BE-031…040, BE-046 | Same | `windows-latest` | **Yes** |
| Cross-layer integration (HTTP and the capture pipeline) | INT-001…017 | Same | `windows-latest` | **Yes** |
| Critical regression set | BE-020, 025, 027, 032, 036, 037; INT-007, 008, 010, 012; FE-026, 035, 036; FE-005 / BE-030 (known defects) | Every run, whatever changed | `windows-latest` | **Yes** (known defects: reported, not blocking) |
| Timing category (real-time waits, about 70 s) | FE-006, 008, 023, 028, 048; INT-003, 004, 008, 016 | Every run (`Timing` is always included) | `windows-latest` | **Yes** |
| SQL Server projection | BE-006 | Manual / release (`Sql` category) | `windows-latest` + LocalDB, if the image has it; otherwise **skipped** | Conditional: a failure blocks; skipped when LocalDB is absent |
| Desktop E2E (real `Frontend.exe`, UI Automation, mouse input) | E2E-001…004 | Manual / release (`E2E` category) | `windows-latest`, ephemeral account only | **Informational** (failures are warnings) until verified on hosted runners |
| Full regression | All 107 tests | Manual (`scope: full`) / `workflow_call` | `windows-latest` | **Yes for release** |
| Hardware and manual | MAN-001…017 | Not in CI | Bench with a CT007-M, Gamma Guard and a tester | Manual sign-off ([checklist](../reports/run-20261016-final/manual-testing-checklist.md)) |
| Not implemented | FE-017, 050, 051; BE-003, 004, 007, 009, 010, 012…014, 041, 044, 045 | — | — | — (Unresolved or Blocked, Phase 3) |
| Legacy `SmokeTests` | — | Not in CI | — | — (cannot build, NU1201) |

A change-based run always includes the critical set. When the impact is uncertain, it widens to the full suite: an unmapped production file, shared infrastructure, a `*.csproj` file, test code, or the CI files themselves.
