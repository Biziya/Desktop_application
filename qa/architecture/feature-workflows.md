# Feature Workflows

Major features only. The cross-layer call chains are in [integration-architecture.md](integration-architecture.md); the rule values are in [business-rules.md](../requirements/business-rules.md).

## 1. Select a drillhole and continue (FR-001…009)

| | |
|---|---|
| Purpose | Choose the drillhole being measured and record who is measuring |
| Preconditions | None. The list comes from the cache or the backend |
| Main flow | Type in the Drillhole box (filtered after 150 ms) → Enter or click picks the closest or chosen entry → Project/Campaign fill in → Operator (pre-filled with the Windows account) and Notes → **Save & Continue** → Measurement page opens the drillhole's session |
| Alternative flows | Backend offline: the cached list is used. Empty Operator: autofilled. Drillhole already measured: listed under "Measured drillholes"; its session and rows reload. Stored metadata differs: warning, stored values kept |
| Validation and errors | No pick → red border + "Select a drillhole from the list."; changes refused while measuring |
| Data | Reads `DrillholeCache` and the measured-drillhole history; resolves or creates `MeasurementSessions` |

## 2. Measure (FR-019…029)

| | |
|---|---|
| Purpose | Record one averaged result per depth interval |
| Preconditions | Saved Core Information; session open; valid Starting Depth and Step; Gamma Guard DB Available; a detector recording |
| Main flow | Enter depths → **Start** (Ready) → **right-click** → countdown for the duration → samples read and averaged → row added at its depth position → the depth advances → saved |
| Alternative flows | Fewer samples than expected: row kept as "n of m" with a warning. Stop during a capture: no row. Starting Depth edited during a capture: no advance. Duration edited (only when not capturing) |
| Errors | No or missing Gamma Guard DB, two detectors, wrong time basis, no samples → error toast, no row, no advance. Save failure → rows kept, retried |
| Data | Reads Gamma Guard `samples` / `detector_status`; writes `MeasurementRecords`, `CaptureSamples`, `MeasurementProgress` |

### Measurement state machine

```text
          Start (valid session + depths)          right-click (CanTrigger)
  Idle ───────────────────────────────▶ Ready ─────────────────────────▶ Capturing
   ▲  ◀── Stop / form edited / drillhole switch / close (cancels any capture) ──┤
   │                                     ▲                                     │ samples read
   │                                     │ row saved (or error)                ▼
   └─────────────────────────────────────┴──────────────────────────────── Processing (100 ms)
```

## 3. Delete a measurement (FR-031)

Delete on a row → confirmation dialog → the row is removed from the table, renumbered, and deleted from the DB (record + samples) → "Measurement deleted.". If the DB delete fails, the row reappears with an error. Capture numbers are not reused.

## 4. Export CSV (FR-034)

**Export CSV** (needs at least one row) → Save dialog (default `CT007-measurements-yyyyMMdd-HHmmss.csv`) → the rows are serialised immediately → written to a temporary file, then moved into place → "CSV exported.". Cancel → nothing. Failure → "Export failed. Check the file location and try again."

## 5. New measurement (FR-017)

After Stop, **New Measurement** closes the current drillhole (the data stays stored) and asks for Core Information again. It is refused while rows are unsaved.

## 6. Device Status (FR-010…014)

These run in the background from start-up:
- the backend probe every 5 s;
- the database checks every 5 s and on Retry;
- the selected Bluetooth device every 1 s, and the device list every 5 s and when the dropdown opens.

Each state change shows one toast. Bluetooth status never blocks capture.

## 7. Start-up and close (FR-033, FR-037)

| Phase | What happens |
|---|---|
| **Start-up** | Settings files are loaded (defaults on error, except DEF-001/002). `%LocalAppData%` is resolved, and a legacy folder moved. The app DB is created or migrated to v5 on first use. The polling loops start. No session is restored automatically (UR-06) |
| **Close** | A running capture is cancelled; queued saves are awaited for up to 5 s; the loops stop |
