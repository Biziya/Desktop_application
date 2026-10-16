# Impact Analysis — reg-20261016-01

## Change-impact map

```text
qa/reports/**, qa/regression/**, Tests/README.md   (documentation)
        ↓  no production or test-code dependency
Tests/regression/** (tooling: selection + isolated run; not referenced by Tests/CT007.Tests)
        ↓  no effect on application behaviour or on test code
Affected features: none   →   Direct tests: none   →   Dependency tests: none
```

The rules in [`Tests/regression/impact-map.json`](../../../Tests/regression/impact-map.json) apply as follows:
- Every changed path matched the documentation rule, or none at all (`Tests/regression/` is outside `Tests/CT007.Tests/`).
- No rule forced the full suite.

**Map completeness check.** All 91 tracked files under `Frontend/`, `Backend/` and `tools/` were checked against the map. None is unmapped. Four of the mapped kinds force a full run:
- the `*.csproj` files;
- the shared WPF infrastructure: `ObservableObject`, `RelayCommand`, `BulkObservableCollection`, `SameItemConverter`;
- `AssemblyInfo.cs`;
- any change under `Tests/CT007.Tests/`.

## Regression selection

| Category | Tests | Reason |
|---|---|---|
| Direct | — | No changed functionality |
| Dependency | — | No changed module |
| Critical regression | BE-020, BE-025, BE-027, BE-032, BE-036, BE-037, INT-007, INT-008, INT-010, INT-012, FE-026, FE-035, FE-036 | Capture window and polling, vendor-DB read-only, atomic save, migrations, capture → storage, concurrent writer, DB failure recovery, session isolation, state machine, save retry, drillhole switch |
| Known defects | FE-005 (DEF-001), BE-030 (DEF-002) | Track the previously confirmed failures |

That is 15 scenarios and 17 test methods. Category Timing is included, because INT-008 is Timing.

## Deliberately excluded

- **The other 89 implemented scenarios**, including all UI-adjacent ViewModel, API and HTTP integration tests. Nothing they cover changed, and their last results are in `run-20261016-final`. Those results were **not revalidated** in this run, and this run does **not** amount to a full regression.
- **BE-006 (Sql) and E2E-001…004:** the environment is still unavailable, as in Phase 4.
- **Manual MAN-001…017:** no code change requires them.
