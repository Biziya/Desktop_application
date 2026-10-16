# Mutation Results — eff-20261016

## Method

- **No mutation tool.** None is installed: Stryker.NET is neither a global tool nor in the package cache, and installing one needs approval. Instead, a controlled manual mutation run uses test-only tooling in [`Tests/mutation/`](../../../Tests/mutation/):
  - `generate_mutants.py` defines 77 single-site mutants and checks each target fragment exists.
  - `Invoke-MutationRun.ps1` runs them.
- **Isolation.** The repo is copied (`Frontend/`, `Backend/`, `Tests/`, `CT007.slnx`) to a disposable `%TEMP%\ct007-mut`. One mutant at a time is applied to the copy, then it is built and the selected tests run. The file is restored from its original bytes and its hash re-checked, and the workspace is deleted at the end. The repository was never written: its `git status` was unchanged apart from the new report folders.
- **Selection.** The tests that CI would run for the mutated file: the `impact-map.json` direct and dependent tests, plus the critical set.
- **Verdict** against a baseline run of the unmutated copy:
  - **Killed:** a test that passed now fails, or a known failure now fails differently.
  - **Survived:** every selected test has its baseline result.
  - **No Coverage:** no relevant test can execute.
  - **Compile Error** and **Timeout:** none occurred.

| Pass | Scope | Result | Evidence |
|---|---|---|---|
| 1 | All 77 mutants, without the Timing category (as fast as possible) | 65 killed, 12 survived | [pass1/](artifacts/pass1/) |
| 2 | The 12 survivors, with Timing | 3 killed (M56 by Timing test FE-006; M49 and M62 because the run had already picked up the improved impact map, which added one test each; see the selection counts in its results JSON) | [pass2-timing/](artifacts/pass2-timing/) |
| 3 | M26, M27, M49, M60, M62 against the improved tests and map | 5 / 5 killed | [pass3-improved/](artifacts/pass3-improved/) |

## Scores

`Mutation score = Killed / (Killed + Survived) × 100`. Equivalent and No Coverage mutants are excluded and listed separately.

| | Killed | Survived | Equivalent | No Coverage | Score |
|---|---|---|---|---|---|
| Before improvements (pass 1, CI selection without Timing) | 65 | 7 | 4 | 1 | **90.3 %** (65 / 72) |
| Same, counting every pass-1 survivor as survived (raw) | 65 | 12 | — | — | 84.4 % (65 / 77) |
| **After improvements (final)** | **71** | **1** | 4 | 1 | **98.6 %** (71 / 72) |

The equivalent verdicts are backed by evidence (see the table). The scores measure the tests' ability to detect *these 77 faults* in high-risk logic. They do not show that the requirements are correct; see the [requirement traceability](requirement-traceability.md).

## All mutants

| ID | Module | Mutation: changed behaviour | Final status | Killed by / evidence |
|---|---|---|---|---|
| M01 | Depth | relational: negative starting depth accepted | Equivalent | unreachable: NumberStyles has no AllowLeadingSign, so a negative value never parses |
| M02 | Depth | arithmetic: metres converted with the wrong factor | Killed | BE-015; BE-017; FE-026; FE-029; FE-033; INT-007; INT-010; INT-011; INT-012; INT-017 |
| M03 | Depth | remove condition: sub-millimetre depth accepted | Killed | BE-015; FE-024 |
| M04 | Depth | boundary: parser rejects a 0 cm step | Killed | BE-016; BE-017 |
| M05 | Depth | arithmetic: step converted with the wrong factor | Killed | BE-016; BE-017; BE-018; FE-024; FE-026; FE-029; INT-007; INT-010; INT-012 |
| M06 | Depth | format: step shown without its decimal | Killed | BE-017 |
| M07 | DeviceSamples | boundary: sample at the click itself accepted | Killed | BE-020 |
| M08 | DeviceSamples | boundary: sample at the window end rejected | Killed | BE-020; BE-025; INT-007; INT-015 |
| M09 | DeviceSamples | constant: expected samples can be 0 | Killed | BE-021 |
| M10 | DeviceSamples | remove condition: samples of another detector accepted | Killed | BE-020 |
| M11 | DeviceSamples | remove condition: duplicate readings accepted | Killed | BE-020 |
| M12 | DeviceSamples | predicate: infinite values averaged | Killed | BE-022 |
| M13 | DeviceSamples | relational: mixed device sessions reported as one | Killed | BE-022 |
| M14 | DeviceSamples | ordering: last instead of first ts_ms | Killed | BE-022; INT-007 |
| M15 | DeviceSamples | constant: some non-millisecond time bases accepted | Killed | BE-023 |
| M16 | DeviceSamples | boundary: exactly 12 h difference refused | Killed | BE-023 |
| M17 | GammaGuardCaptureDevices | boundary: detectors exactly 5 s apart not refused | Killed | BE-024 |
| M18 | GammaGuardCaptureDevices | ordering: oldest detector picked | Killed | BE-024 |
| M19 | GammaGuardCaptureDevices | condition: capture allowed from a missing/busy database | Killed | BE-024 |
| M20 | DeviceCaptureService | remove lookback: rows written just before the click missed | Killed | BE-025; INT-007; INT-009; INT-015 |
| M21 | DeviceCaptureService | remove branch: no final read for late rows | Killed | BE-025 |
| M22 | DeviceCaptureService | remove condition: time-basis check skipped | Killed | BE-026 |
| M23 | MeasurementOrder | return value: rows without depth sorted first | Killed | BE-019 |
| M24 | MeasurementOrder | negation: equal depths ordered newest first | Killed | BE-019 |
| M25 | MeasurementOrder | boundary: insert position among equal rows | Equivalent | distinct rows never compare equal (CaptureNo unique per session) |
| M26 | CaptureDurationSettings | boundary: 10 s duration rejected | Killed (pass3 (improved tests/map)) | BE-030 |
| M27 | CaptureDurationSettings | validation: signed/whitespace durations accepted | Killed (pass3 (improved tests/map)) | BE-030 |
| M28 | MeasurementRepository | remove validation: row of another session saved | Killed | BE-032; BE-033 |
| M29 | MeasurementRepository | update scope: stored values overwritten on re-save | Killed | BE-032 |
| M30 | MeasurementRepository | boundary: newer schema accepted | Killed | BE-037 |
| M31 | MeasurementRepository | rounding: cm -> mm truncated instead of rounded | Equivalent | (mm/10.0)*10 is exact for every integer mm (0..200000 exhaustive, 2M random up to 1e12) |
| M32 | MeasurementRepository | arithmetic: progress stored in the wrong unit | Killed | BE-032; BE-035; INT-007; INT-010; INT-011; INT-012 |
| M33 | MeasurementRepository | conflict rule: stored session metadata overwritten | Killed | BE-031 |
| M34 | MeasurementRepository | remove check: orphan records accepted by migration | Killed | BE-037 |
| M35 | MeasurementRepository | boundary: single duplicate Drillhole ID accepted | Killed | BE-037 |
| M36 | MeasurementRepository | remove statement: samples not deleted with their record | Killed | BE-034; INT-013 |
| M37 | CsvExportService | remove case: '-' formula prefix not neutralised | Killed | BE-043 |
| M38 | CsvExportService | remove trim: formula after leading spaces not neutralised | Killed | BE-043 |
| M39 | CsvExportService | remove escaping: embedded quotes not doubled | Killed | BE-043 |
| M40 | CsvExportService | remove validation: mixed-session export allowed | Killed | BE-043 |
| M41 | CsvExportService | culture: numbers exported in the local culture | Killed | BE-042 |
| M42 | MeasurementViewModel | condition: depth advances although edited during capture | Killed | FE-029 |
| M43 | MeasurementViewModel | remove validation: 0 cm step accepted | Killed | FE-024 |
| M44 | MeasurementViewModel | remove condition: capture while the duration editor is open | Killed | FE-032 |
| M45 | MeasurementViewModel | negation: incomplete warning on complete captures | Killed | FE-030 |
| M46 | MeasurementViewModel | remove condition: late session open not discarded | Survived | Survived |
| M47 | MeasurementViewModel | remove condition: late stored-drillhole load shown | Killed | FE-036 |
| M48 | MeasurementViewModel | constant: failed save not retried | Killed | FE-035; FE-036; FE-038 |
| M49 | MeasurementResult | boundary: complete capture shown as incomplete | Killed (pass3 (improved tests/map)) | FE-046 |
| M50 | MeasurementCellFormat | rounding: display rounding mode | Killed | FE-046 |
| M51 | MeasurementCellFormat | remove clean-up: '-0' shown | Killed | FE-046 |
| M52 | ApiClient | remove branch: HTTP errors parsed as data | Killed | FE-002; FE-010; INT-002 |
| M53 | ApiClient | swap messages: timeout and refusal messages swapped | Killed | FE-003; FE-010 |
| M54 | ApiSettings | boundary: 0 s timeout accepted | Killed | FE-005 |
| M55 | BackendConnection | condition: CameOnline raised on every successful probe | Equivalent | only listener re-checks the load status; no observable change |
| M56 | BackendConnection | remove condition: overlapping probes | Killed (pass2 (Timing)) | FE-006 |
| M57 | DrillholeCatalog | predicate: blank Hole IDs kept | Killed | FE-013 |
| M58 | DrillholeCatalog | comparer: case-variant duplicates kept | Killed | FE-013 |
| M59 | DrillholeCatalog | constant: exact match ranked last | Killed | FE-014 |
| M60 | DrillholeCatalog | boundary: closest match tie-break | Killed (pass3 (improved tests/map)) | FE-014 |
| M61 | CoreInformationViewModel | condition: empty server list replaces the saved list | Killed | FE-011 |
| M62 | CoreInformationViewModel | condition: reload on every CameOnline | Killed (pass3 (improved tests/map)) | FE-007 |
| M63 | CoreInformationViewModel | remove lock: typing changes the drillhole while measuring | Killed | FE-022 |
| M64 | CoreInformationViewModel | predicate: blank Operator not autofilled | Killed | FE-020 |
| M65 | CoreInformationViewModel | remove validation: save without a drillhole | Killed | FE-019 |
| M66 | SqliteFileCheck | remove case: busy database not reported as Busy | Killed | BE-028 |
| M67 | GammaGuardDatabaseCheck | constant: wrong subfolder searched | Killed | BE-029; FE-044; INT-015 |
| M68 | GammaGuardDatabaseCheck | ordering: oldest database chosen | Killed | BE-029 |
| M69 | GammaGuardDatabaseCheck | remove normalisation: 'Gamma Guard' folder not recognised | Killed | BE-029; FE-044 |
| M70 | Ct007BluetoothCheck | remove check: device with another address accepted | Killed | FE-043 |
| M71 | StatusAnnouncements | negation: online announcement condition inverted | Killed | FE-042 |
| M72 | StatusAnnouncements | remove condition: first known state announced | Killed | FE-042 |
| M73 | ToastService | boundary: five toasts visible | Killed | FE-041 |
| M74 | ToastService | constant: errors shown for 3 s | Killed | FE-019; FE-041 |
| M75 | DrillholeController | exception type: SQL outage becomes HTTP 500 | Killed | BE-002; INT-002 |
| M76 | DrillholeController | constant: outage status 500 instead of 503 | Killed | BE-002; INT-002 |
| M77 | DrillholeRepository | sql: campaign taken from the wrong end | NoCoverage | only BE-006 (Sql, skipped) executes the query |

## Surviving and excluded mutants

| ID | Location | Why not killed | Assessment | Action |
|---|---|---|---|---|
| M46 | `MeasurementViewModel.OpenDrillholeAsync`: `version != viewVersion` removed | The guard is redundant in the app: every change of selection also clears or changes `savedCore`, which the remaining condition checks. Only a direct `SetCore(A)` + `SelectDrillhole(B)` sequence, without a core change, would show the difference | **Survived, low priority.** Not proven equivalent, because the ViewModel API allows the sequence. No user-observable scenario through `MainViewModel` was found | Optional ViewModel-level test if the guard should be kept as a contract |
| M01 | `Depth.TryParseMetres` `metres < 0` | Unreachable: `NumberStyles` lacks `AllowLeadingSign`, so negatives never parse | Equivalent (dead check) | None (a code-quality note for developers) |
| M25 | `MeasurementOrder.InsertIndex` `<= 0` → `< 0` | `Compare` never returns 0 for two different rows (CaptureNo is unique per session) | Equivalent | None |
| M31 | `CentimetresToMm` rounding → truncation | `(n/10.0)*10` is exact for every integer n tested (0–200 000 exhaustively, 2 M random values up to 10¹²) | Equivalent for every value the app writes | None |
| M55 | `BackendConnection` `CameOnline` on every probe | The only listener reloads only when the last load failed | Equivalent for current consumers | None |
| M77 | `DrillholeRepository` SQL `RIGHT` → `LEFT` | Only BE-006 runs the query, and it needs SQL Server (skipped) | **No Coverage** (environment) | Run BE-006 with `CT007_TEST_SQL` |
