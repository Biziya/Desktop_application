# Requirement Traceability — eff-20261016

There are **no approved requirements** (UR-01). The rows below are the business rules identified in Phase 1, inferred from code and listed as features F01–F23. They trace to the Phase 2 test IDs, the latest execution ([validation run](artifacts/validation/results.csv): 102 passed, 2 failed (known defects), 5 skipped) and the mutation evidence ([mutation results](mutation-results.md)). "Effective" means at least one mutant of the rule was killed by these tests.

| Requirement (inferred rule) | Test IDs | Execution status | Effectiveness evidence |
|---|---|---|---|
| Capture window `click < ts ≤ click+d`, frozen detector, de-duplication (F15) | BE-020, BE-025, INT-007, INT-008 | Pass | Effective: M07, M08, M10, M11, M20, M21 killed |
| Averaging over finite values; first ts; device session (F15) | BE-022, INT-007, INT-014 | Pass | Effective: M12, M13, M14 killed |
| Time basis: Unix ms within 12 h (F15) | BE-023, BE-026 | Pass | Effective: M15, M16, M22 killed |
| One detector; refuse if two within 5 s (F15) | BE-024, MAN-004 | Pass (MAN not run) | Effective: M17, M18, M19 killed |
| Depth input in whole mm; `.`/`,`; step > 0 (F12) | BE-015…018, FE-024, FE-029 | Pass | Effective: M02–M06, M43 killed; M01 equivalent (dead check) |
| Depth advances once, unless edited during capture (F12) | FE-029, INT-011 | Pass | Effective: M42 killed |
| Row order by From, then time, then stored, then capture (F16) | BE-019, FE-033 | Pass | Effective: M23, M24 killed; M25 equivalent |
| Capture duration 2–10 s, whole seconds (F13) | BE-030, FE-032 | Pass, except DEF-002 case | Effective **after the split**: M26, M27 killed in pass 3; M44 killed |
| Record, samples and progress save together; re-save changes only the order (F18) | BE-032, BE-033, FE-035, INT-010 | Pass | Effective: M28, M29, M32, M48 killed; M31 equivalent |
| One session per Drillhole ID; stored metadata kept (F11) | BE-031, FE-037 | Pass | Effective: M33 killed |
| Migration refuses newer / duplicate / orphan data; rolls back (F18) | BE-036, BE-037, INT-017 | Pass | Effective: M30, M34, M35 killed |
| Delete removes record and samples only (F17) | BE-034, FE-034, INT-013 | Pass | Effective: M36 killed |
| Late loads after a selection change are discarded (F11) | FE-036 | Pass | Effective: M47 killed; **M46 survived** (redundant guard) |
| CSV: quoted cells, formula neutralisation, invariant numbers, one session (F19) | BE-042, BE-043, INT-014 | Pass | Effective: M37–M41 killed |
| Incomplete capture shown as "n of m"; warning (F14/F16) | FE-030, FE-046, INT-014 | Pass | Effective: M45 killed; M49 killed **after the map fix** |
| HTTP status mapping and messages (F01/F05) | FE-001…004, INT-002…004 | Pass | Effective: M52, M53 killed |
| Settings fall back to defaults when invalid (F05) | FE-005 | Pass, except DEF-001 case | Effective: M54 killed |
| Reachability: no overlapping probes; reload only after a failed load (F05) | FE-006, FE-007, INT-003 | Pass | Effective: M56 killed (Timing), M62 killed after the map fix; M55 equivalent |
| Drillhole catalog: clean, de-duplicate, rank, tie-break, never replaced by an empty list (F01–F03) | FE-011, FE-013, FE-014 | Pass | Effective: M57, M58, M59, M61 killed; M60 killed **after the added tie assertion** |
| Selection lock, required drillhole, Operator autofill (F03/F04) | FE-019, FE-020, FE-022 | Pass | Effective: M63, M64, M65 killed |
| Device Status: Busy, discovery rules, Bluetooth address (F06–F08) | BE-028, BE-029, FE-043, FE-044 | Pass | Effective: M66–M70 killed |
| Announcements and toasts (F09) | FE-041, FE-042 | Pass | Effective: M71–M74 killed |
| Backend: SQL outage → 503 ProblemDetails (F01) | BE-002, INT-002 | Pass | Effective: M75, M76 killed |
| Backend: campaign = `RIGHT(PROSPECT, 4)`, NULL → "" (F01) | BE-006 | **Skipped** (no SQL Server) | **No Coverage**: M77 |

## Requirements without effective automated evidence

| Rule | Why | Needed |
|---|---|---|
| Real SQL projection (F01) | BE-006 needs SQL Server | `CT007_TEST_SQL` (LocalDB) |
| Right-click trigger, real dialogs, persistence across restarts through the UI (F14, F17, F19) | E2E-001…004 not executed | Dedicated desktop test account |
| Measurement accuracy, timing alignment, sample rate on the real detector (F15) | Hardware only | MAN-001…003 |
| Security posture: credentials, authentication, Swagger exposure | BE-009 and BE-010 not implemented (UR-14) | Security decision |
| Chart semantics, restore on start-up, unused endpoints, `check-db.ps1` (F20–F23) | Unresolved or Blocked (UR-05, 06, 07, 11) | Requirement answers |

## Tests without trustworthy expected behaviour

Every executed expectation is **Inferred**. These depend on open questions:
- UR-09 (FE-021), UR-17 (FE-030, INT-014), UR-23 (FE-028, FE-048), UR-30 (FE-037);
- UR-01 (the BE-020 window versus the draft's 3 s delay), UR-02 (BE-021), UR-10 (BE-023, BE-024), UR-03 (BE-030).

Mutation testing shows these tests detect *changes*. It cannot show that the current behaviour is what the business needs.
