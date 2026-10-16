# Assertion Quality — eff-20261016

Builds on the Phase 8 review ([test-quality-review](../../maintenance/maint-20261016/test-quality-review.md)). This phase adds **evidence from mutation testing**: each finding below comes from a mutant that survived.

## Weaknesses found by mutation testing

| Finding | Mutants | Tests | Cause | Fixed? |
|---|---|---|---|---|
| **Masking by a known defect.** One method held many cases, and the DEF-002 case threw first, so the 2–10 s limit checks and the editor-parsing checks after it never ran | M26 (10 s rejected), M27 (signed or whitespace input accepted) | BE-030 | Several cases in one method, with a failing case early | **Yes.** The defect case has its own method (`DurationSettingsNonNumericValue`). Both mutants are now killed |
| Same pattern in FE-005: the DEF-001 case hid the relative-URL case after it | — (no mutant targeted it) | FE-005 | Same | **Yes.** Defect case split into `SettingsNonNumericValue`; the relative-URL case now runs and passes |
| **Impact-map selection gap.** A test that detects the fault exists, but CI would not select it for that file | M49 (complete capture shown as "5 of 5") → FE-046; M62 (reload on every CameOnline) → FE-007 | FE-046, FE-007 | Map rules built per module missed cross-module consumers | **Yes.** `MeasurementResult.cs` now selects FE-046, and `Pages/CoreInformation/` selects FE-007. Both killed |
| **Missing boundary (tie-break).** No case had two equally good matches, so the "listed first wins" rule was untested | M60 | FE-014 | Test data had a unique best match | **Yes.** Assertion added: query "a" with three prefix matches → "AB". Killed |
| **Timing tests carry unique detection** | M56 (overlapping probes) killed only by FE-006 (Timing) | FE-006 | — | No change. CI runs Timing on every run; a local fast run without Timing misses it |

## Assertions confirmed effective

These 65 mutants were killed in pass 1 without any change:
- the capture window boundaries (T, T+d), serial filter, de-duplication, averaging, NaN/∞, first ts and session uniformity;
- the time-basis limits and the detector pick (including the exact 5 s boundary);
- polling, lookback and the final read;
- depth parsing, conversion and formatting, and ordering;
- atomic save, session isolation, upsert scope, migration refusals and rollback, delete-with-samples;
- the CSV neutralisation, quoting and culture checks, and the mixed-session refusal;
- the state machine and depth advance, the duration editor lock, incomplete-capture feedback, and save retry;
- API status mapping, catalog cleaning and ranking, the selection lock, Operator autofill and the required drillhole;
- file-check and discovery rules, Bluetooth address check, announcements, toast rules, and the backend 503 mapping.

## Remaining limits of the assertions

- **Inferred expectations.** All expected results are inferred from code (UR-01). A killed mutant shows the tests detect a *change* in behaviour, not that the original behaviour is correct.
- **Real external systems.** The backend's real SQL query (M77, No Coverage), the real UI, Gamma Guard and hardware are not asserted by any executed automated test.
- **Not part of this mutant set.** Presentation and XAML, `WindowsBluetooth` (WinRT), the E2E bench, `Program.cs`, and the unused endpoints were left out by risk priority.
- **Redundant guard.** M46 survives; see the [mutation results](mutation-results.md).
