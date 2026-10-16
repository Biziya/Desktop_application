# Project Overview — SPP CT-007 Measurement Studio

## Purpose

A Windows desktop application for **drill-core gamma measurements** with a CT007-M detector. The detector records continuously into the vendor application **Gamma Guard**, which writes a local SQLite database. The operator:

1. picks a **drillhole**, using reference data from a corporate SQL Server through a small HTTP backend;
2. enters a starting depth and a step;
3. **triggers captures**. Each capture reads the detector samples recorded during a short time window, averages them into one row per depth interval, and stores the row locally;
4. reviews, deletes and **exports the rows as CSV**.

## Requirement sources and how they are labelled

| Source | What it is | Used as |
|---|---|---|
| Root [`README.md`](../../README.md) §2 table, decision log (§16) and fundamental rules (§17) | A design document written **before implementation**. Its §2 table has a "Confirmed requirement" column; its decision log marks some decisions "Confirmed" | **Confirmed** (documented). Whether a stakeholder formally approved them is **unknown** |
| The rest of the root README (§3–§15) | Design proposals ("proposed", "candidate", "recommended") | **Proposed** |
| Source code (`Frontend/`, `Backend/`) | The implemented behaviour, verified in QA Phases 1–9 | **Inferred**: what the app does, not necessarily what it should do |
| [`qa/planning/unresolved-requirements.md`](../planning/unresolved-requirements.md) (UR-01…30) | Open questions from test planning | **Unresolved** |

Every requirement in [functional-requirements.md](functional-requirements.md) and [nonfunctional-requirements.md](nonfunctional-requirements.md) carries one of these four labels.

**Important.** The implementation **diverges** from two Confirmed requirements: the trigger and the capture start delay (see [unresolved-requirements.md](unresolved-requirements.md) §1). The README predates most implemented features (drillholes, depths, sessions), so those features have only Inferred requirements.

## Scope summary

| In scope (implemented) | Not implemented / out of scope |
|---|---|
| Drillhole reference data (API + offline cache) | Device acquisition control (README: out of scope) |
| Device Status: CT007-M Bluetooth (informational), app DB, Gamma Guard DB | Bluetooth **trigger** button (README: Confirmed; not implemented, right-click used instead) |
| Measurement sessions per drillhole; depth progression | Configurable trigger delay (README: Confirmed +3 s; not implemented) |
| Capture from Gamma Guard: window, averaging, validation | Authentication / authorisation (none) |
| Local persistence (SQLite, schema v5, migrations v1–v5) | Formats other than CSV (README: later) |
| Results table, delete, CSV export, depth chart | Restore last session on start-up (implemented, not wired: UR-06) |

## Where to go next

- **Requirements:**
  - [functional-requirements.md](functional-requirements.md)
  - [nonfunctional-requirements.md](nonfunctional-requirements.md)
  - [business-rules.md](business-rules.md)
  - [unresolved-requirements.md](unresolved-requirements.md)
- **Architecture:** [../architecture/architecture-overview.md](../architecture/architecture-overview.md)
- **Testing:** [../planning/test-inventory.md](../planning/test-inventory.md), [../effectiveness/eff-20261016/effectiveness-summary.md](../effectiveness/eff-20261016/effectiveness-summary.md)
