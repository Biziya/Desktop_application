# SPP CT-007 — Measurement Studio

**Independent Windows Desktop Project — Continuous Measurement Selection, Synchronization, Processing, and Export**

> **Status:** Architecture and requirements draft — design only, not an implemented system.  
> **Device:** SPP CT-007 (device capabilities and SQLite schema to be verified)  
> **Project scope:** Standalone new project; no connection to prior Acquire logging, drilling, or other applications.  
> **Target platform:** Windows  
> **Selected stack:** WPF + XAML frontend; C# / .NET backend; SQLite; Bluetooth trigger  
> **Primary goals:** Reliable measurement alignment, fast and responsive operation, simplicity, low maintenance, and reproducible exports.

## 1. Project overview

SPP CT-007 Measurement Studio is a new, standalone Windows desktop application for selecting useful measurement data from a device that **records continuously** and has **no Start/Stop controls**. A separate Bluetooth button provides a user-triggered event. The application uses that event to define a *logical capture window*: selection begins **3 seconds after the physical button press** and continues for a **configurable duration**. The application reads the corresponding records from the measurement device's **SQLite database on the same Windows PC**, converts them to structured tables, supports filtering/processing and review, and exports CSV (and potentially other formats later).

The SPP CT-007 measurement device continues running throughout this workflow; the desktop application does **not** start or stop its acquisition.

### Goals

- Separate WPF presentation from .NET acquisition, synchronization, and processing logic.
- Capture trigger events quickly without blocking the UI.
- Correctly associate trigger-defined time windows with raw SQLite measurements.
- Preserve source records, timing settings, and processing history for reprocessing/audit.
- Be fast for the user while avoiding interference with the device's database writer.
- Favor a small, maintainable Windows deployment over unnecessary distributed services.

### Out of scope for the initial release

- Controlling the measurement device's acquisition or adding physical Start/Stop behavior.
- Mobile, macOS, or Linux support.
- A cloud backend or distributed message broker.
- Guaranteeing sub-millisecond accuracy without device specifications and calibration evidence.

## 2. Confirmed requirements versus design proposals

| Area | Confirmed requirement | Proposed design / decision to validate |
|---|---|---|
| Operating system | Windows only | Ship one Windows installer |
| Frontend | WPF + C# + .NET | XAML, MVVM, CommunityToolkit.Mvvm |
| Backend separation | Frontend and backend are separate parts | Separate projects/assemblies; **same process initially**, optional background service later |
| Measurement device | SPP CT-007; continuous recording as described | Verify device specifications, timestamp behavior, and external SQLite access |
| Measurement source | Device records continuously | External, read-only SQLite source |
| Database location | Device SQLite file is on the same Windows PC | Direct local connection, subject to locking/access tests |
| Trigger | Separate Bluetooth button | Event-driven Bluetooth listener; protocol still unknown |
| Capture start | 3 seconds after physical trigger press | Configurable delay with a default of 3 s |
| Capture end | After a defined duration | User-configurable duration; no final default confirmed |
| Data output | Table and CSV or other formats | CSV first, additional formats later |
| Performance | Fast and low maintenance | Asynchronous acquisition, bounded reads, virtualization, profiling |
| Accuracy | Align the correct measurements to trigger | Calibration, uncertainty tracking, validation and manual review |

**Critical open question:** Does each SQLite record contain a reliable *acquisition timestamp* (not merely insertion time), and at what precision/frequency? The actual database schema has not yet been supplied.

## 3. High-level architecture

The frontend and backend should be **independent in source-code responsibilities**, but separate processes are not a requirement for the first release. An in-process design minimizes deployment complexity and inter-process overhead.

```text
                    +---------------------------+
                    |     WPF Frontend (MVVM)   |
                    | Live / Sessions / Review  |
                    | Synchronization / Export  |
                    +-------------+-------------+
                                  |
                     Typed interfaces / contracts
                                  |
                    +-------------v-------------+
                    |       .NET Backend        |
                    |                           |
  Bluetooth Button -> Trigger Listener           |
                    |    |                      |
                    |    v                      |
                    | Trigger & Session Manager |
                    |    |                      |
                    |    v                      |
                    | Synchronization Engine    |
                    |    |                      |
                    |    v                      |
                    | Measurement Selector <-------- SQLite Reader
                    |    |                      |          ^
                    |    v                      |          |
                    | Validation / Processing   |    Device SQLite
                    |    |                      |    (local, external,
                    |    v                      |     read-only)
                    | Session Storage / Export  |
                    +-------------+-------------+
                                  |
                         Application SQLite
                      (sessions, selected raw
                       samples, processing info)
```

### Frontend responsibilities

- Show device/database connection state and acquisition health.
- Display trigger receipt, scheduled intervals, capture/processing states, and quality flags.
- Configure delay, capture duration, approved processing settings, and exports.
- Present virtualized raw and processed measurement tables; support sorting, review and manual correction workflows.
- Never implement independent timestamp matching or duplicate backend business logic.

### Backend responsibilities

- Subscribe to Bluetooth trigger events and timestamp receipt promptly.
- Debounce unintended duplicates while permitting intentional repeated presses.
- Create immutable session definitions and persist important trigger/session state.
- Read SQLite incrementally and/or query historical time intervals as needed.
- Map PC trigger times into the device's acquisition time domain.
- Select records, validate quality/completeness, process into output rows, export.
- Support deterministic replay and tests without connected devices.

### Deployment choice

**Version 1:** Separate WPF, Core, Infrastructure, and Contracts projects loaded in one Windows application process. No ASP.NET Core localhost API is required. A local HTTP API adds configuration, deployment, and failure modes without clear benefit here.

**Future option:** Move the acquisition backend into a Windows Service with a local IPC transport (e.g., named pipes) **only if acquisition must continue when the UI is closed**, or if independent backend lifecycle/recovery becomes necessary.

## 4. Proposed solution layout

```text
MeasurementStudio/
├── README.md
├── Frontend/
│   └── MeasurementStudio.Wpf/
│       ├── Views/
│       ├── ViewModels/
│       ├── Controls/
│       └── Styles/
├── Backend/
│   ├── MeasurementStudio.Core/
│   │   ├── Models/
│   │   ├── Interfaces/
│   │   └── Services/
│   │       ├── TriggerManagement/
│   │       ├── Synchronization/
│   │       ├── CaptureSessions/
│   │       ├── MeasurementSelection/
│   │       └── DataProcessing/
│   └── MeasurementStudio.Infrastructure/
│       ├── Bluetooth/
│       ├── SQLite/
│       ├── Persistence/
│       └── Export/
├── Shared/
│   └── MeasurementStudio.Contracts/
└── Tests/
    ├── Backend.Tests/
    └── Integration.Tests/
```

Proposed supporting technologies: .NET 10, CommunityToolkit.Mvvm, Microsoft.Data.Sqlite, optionally Dapper, and xUnit. These are selections for planning and should be verified with the team's environment and lifecycle requirements.

## 5. End-to-end workflow

1. **Preparation:** User launches the application; backend verifies Bluetooth trigger connection, device SQLite accessibility, and synchronization/calibration status.
2. **Continuous acquisition:** Measurement device continues recording into local SQLite, independent of the app.
3. **Trigger:** User presses the Bluetooth button; backend records the notification timestamp, a monotonic tick/reference, and trigger identity as promptly as possible.
4. **Session creation:** Backend creates a session, freezes its configured 3-second delay and capture duration, and calculates *logical* start/end boundaries.
5. **Clock alignment:** Synchronization engine translates those boundaries to the measurement device's acquisition time domain, with recorded calibration and uncertainty.
6. **Logical capture:** Samples with acquisition timestamps in the window qualify for the session, regardless of when SQL queries actually execute.
7. **Data availability:** Reader waits for relevant committed SQLite rows, based on a validated completeness/watermark rule or a conservative timeout. It does not assume all data appears immediately.
8. **Selection:** Backend retrieves matching raw records, sorts by acquisition timestamp plus stable record/sequence ID, checks for gaps/duplicates and preserves provenance.
9. **Processing:** Backend applies selected filtering and aggregation rules; the user can review the raw and processed table.
10. **Persistence:** Save session boundaries, source identifiers, selected raw values as needed, calibration/version, processing settings, outcomes and quality indicators.
11. **Export:** Export reproducible CSV output; additional formats can be implemented later.

### Session lifecycle

```text
Trigger Received
       |
       v
Scheduled (3-second offset)
       |
       v
Capture Window Active (logical interval)
       |
       v
Awaiting Data / Completeness
       |
       v
Processing and Validation
       |
       v
Ready for Review --> Approved --> Exported
       |
       +--> Incomplete / Failed / Cancelled / Needs Review
```

A timed-out or partially populated session must **not** silently become `Ready`.

## 6. Capture timing semantics

Define the desired interval using the **physical button-press time**, not the application callback time:

```text
Physical press                 Target start              Target end
     |                             |                         |
     v                             v                         v
     0s -------------------------- 3s ---------------------- 3s + D
                delay = 3s                duration = D
                         [ include start, exclude end )
```

- `D` = capture duration configured for the session.
- Start-inclusive/end-exclusive semantics: `[T_start, T_end)`.
- Example only: press at `10:00:00.000`, duration `5 s` -> desired window `10:00:03.000` through but excluding `10:00:08.000`.
- **Selecting a window is not the same as issuing a query exactly when the window begins.** Querying later can still yield the correct rows if source acquisition timestamps are reliable and retained.
- The duration and timing settings are frozen **per session** at trigger receipt. Subsequent UI setting changes do not rewrite existing sessions.

### Overlapping captures

Allow intentional consecutive triggers to create separate sessions, even when their time windows overlap. A source measurement may belong to more than one session. Use a configurable debounce policy to filter button contact bounce or duplicate notifications, but never silently discard valid rapid presses.

Example: trigger at 0 s -> 3–8 s; trigger at 2 s -> 5–10 s. The samples from 5–8 s may validly belong to both sessions.

## 7. Synchronization, latency, and uncertainty

### Distinct clocks and timestamps

Record and distinguish:

1. **Physical trigger time:** the actual human button press (may be unobservable directly).
2. **Bluetooth event timestamp:** when the OS or app observes the notification.
3. **PC monotonic time:** reliable for local elapsed durations and ordering; not a cross-device wall clock.
4. **PC wall-clock time:** human-readable, subject to adjustments.
5. **Device acquisition timestamp:** when the measurement occurred (if actually stored).
6. **SQLite commit/visibility time:** when the row becomes readable; not equivalent to acquisition time.

The Windows Bluetooth API may provide an OS-side notification timestamp for certain Bluetooth LE GATT events, but that still does not establish the exact time of the physical press. The trigger protocol/model must be verified; not every Bluetooth button is a BLE GATT device.

### Proposed simple clock model

Let:
- `T_received,PC` = observed trigger time on the PC clock.
- `L` = estimated latency from physical press to the observed trigger time.
- `O` = estimated device clock minus PC clock offset, measured consistently.
- `D` = capture duration.

Then, assuming offsets are reasonably constant and all times are represented in compatible domains:

```text
T_start,device ≈ T_received,PC - L + 3 seconds + O
T_end,device   ≈ T_start,device + D
```

For drifting device clocks, replace the constant-offset approximation with a calibrated mapping (offset plus rate/drift) for each appropriate calibration interval. Do not add a device timestamp to a PC monotonic counter without establishing their relationship.

### Accuracy warning

Without a device-side trigger timestamp, shared timing reference, or external calibration setup, the PC **cannot infer true physical press latency solely from when a Bluetooth notification arrives**. Applying an invented constant latency would create false precision.

Similarly, a fast SQLite reader cannot repair bad timestamps or clock differences. Record **estimated uncertainty** and mark sessions as requiring review when the synchronization evidence is weak.

### Proposed calibration plan

| Measurement | Purpose | Method / prerequisite |
|---|---|---|
| Physical press → notification latency | Estimate trigger delay and jitter | Shared hardware reference, instrumented button, or controlled external measurement |
| Sample interval and jitter | Understand temporal resolution | Inspect acquisition timestamps across many samples |
| Clock offset/drift | Map device time to PC time | Common time reference or identifiable cross-device events |
| Acquisition → SQLite visibility delay | Establish data availability policy | Observe actual sample timestamps and first readable commits during recording |
| Repeatability | Establish uncertainty bounds | Repeat across runs, reconnects, and operating conditions |

Store calibration versions and their validity periods. Expose configured offsets, uncertainties, and last calibration result in the UI. Provide manual review/alignment for sessions that cannot be automatically trusted.

### Sampling resolution

Even perfectly aligned clocks cannot select a measurement at a nonexistent timestamp. If a device samples every 100 ms, the nearest valid sample may not land exactly on the target boundary. Favor **window-based selection** of all records in `[start,end)`; nearest-sample matching is optional and must have a maximum tolerated difference.

## 8. Reading the device SQLite file safely

The device's SQLite file is **externally owned**. The application should open its own read-only connection and must not change device tables, indexes, journal mode, permissions, or database files without validated vendor support.

### Proposed acquisition strategy

- Use bounded asynchronous reads and short transactions; do not hold long snapshots open.
- Prefer incremental batches when schema provides safe stable IDs or timestamps.
- Persist a reader checkpoint only when rows are durably processed.
- Detect database replacement, ID resets, missing rows, duplicate records and new measurement sessions.
- Preserve the ability to query historical windows (a session may reference earlier records).
- If the writer updates rows after insertion or inserts out of timestamp order, use an overlap/reconciliation policy instead of a naive `ID > last_seen` approach.
- Never run source database work on the WPF UI thread.

### Concurrent read/write behavior

- SQLite's WAL journal mode generally allows concurrent readers and a writer, though lengthy readers can affect checkpointing.
- Rollback journal mode can produce reader/writer contention.
- An exclusive writer/locking strategy may prevent direct access.
- **Actual device behavior must be tested while acquisition is active**. Do not assume WAL or change journaling yourself.
- If direct reading is unsupported, an alternative vendor-approved integration or controlled snapshot process may be necessary.

### Late-arriving records

Example:

```text
10:00:03  Desired capture window starts
10:00:08  Desired capture window ends
10:00:09  Last matching sample appears in SQLite
10:00:09+ Backend can complete once completeness is established
```

The backend should not finalize at exactly 10:00:08 simply because the logical window ended. Use, in order of reliability:

1. A source-provided watermark/commit indication proving records up to the end time are available.
2. A measured source write-buffering delay plus validated expected-count/sequence checks.
3. A bounded wait/timeout, producing `Incomplete` / `Needs Review` when completeness is unprovable.

Avoid assuming a fixed extra one-second wait is always sufficient.

### Retention and recovery

Device data may be rotated, overwritten or deleted. Determine actual retention. Preserve enough selected raw rows in the application's database to support later review and recalculation even if original rows disappear. Record the source database identity, path/reference, capture range, and source row IDs. Recovery procedures should handle a PC restart, Bluetooth disconnect, and device database replacement.

## 9. Measurement selection and processing

### Selection algorithm (design, not code)

1. Obtain an immutable session definition.
2. Convert PC-observed trigger timing to calibrated device acquisition timing.
3. Determine `[start, end)`.
4. Query all relevant **committed** source measurements in that interval.
5. Sort deterministically: acquisition timestamp ascending, then stable record/sequence ID.
6. Detect duplicate identifiers, duplicate timestamps, gaps, unreasonable intervals and missing values.
7. Keep selected raw records traceable; process a separate derived result.
8. Store rules/settings, calibration, quality findings and output.

Do **not** sort by SQLite insert order alone unless device testing confirms that insert order equals acquisition order. SQLite timestamps may be text, integers or real values; their format, precision, and timezone/epoch must be established.

### Processing modes (candidates)

| Mode | Output | Notes |
|---|---|---|
| Raw window | Every valid sample becomes a row | Best first mode for inspection |
| Average | One or more aggregated numeric values per capture | Define invalid-value handling and units |
| Median | One or more robust aggregated values | May reduce impact of isolated outliers |
| Filtered selection | Subset based on business rules | Rules to be determined with actual measurement fields |
| Nearest sample | One reading closest to a target | Optional; explicit maximum allowed time difference |

The application's exact selection and aggregation rules are **not yet chosen**. Avoid automatically reducing data to one row until the measurement semantics are known.

### Quality dimensions

Track separately:

- **Synchronization quality:** timing uncertainty, calibration validity, clock-offset confidence.
- **Data completeness:** expected versus observed sample count or other validated watermark rule.
- **Data validity:** invalid values, duplicate samples, bad timestamps, discontinuities.
- **Processing validity:** transformation completed, parameters documented, export reproducible.

A session with all expected rows but uncertain clock alignment should not be labeled simply `Success` without qualification.

## 10. Session metadata design

Proposed session information (logical model; not finalized database schema):

| Field | Description |
|---|---|
| Session ID | Unique ID for each intentional trigger |
| Trigger ID | Button event identity, if available |
| Trigger observed UTC time | PC wall-clock reference |
| Trigger monotonic timestamp | PC elapsed-time reference |
| Trigger source/protocol | Which Bluetooth device/event produced it |
| Configured delay | Default 3,000 ms |
| Capture duration | User-selected duration |
| Calibrated latency & clock mapping | Estimates and versions used |
| Timing uncertainty | Supported bound/estimate and its basis |
| Device start/end | Calculated acquisition-time interval |
| Source database identity | Which device database / generation |
| Raw record identifiers and values | Traceability and reprocessing support |
| Processing mode and parameters | Exact derivation rules |
| Completeness and validation status | Missing records, invalid values, warnings |
| Session lifecycle status | Scheduled, active, incomplete, ready, etc. |
| Export metadata | Format, path/reference, time and status |

Keep **device SQLite** separate from **application SQLite**; never mix derived or session records into the device-owned database.

## 11. WPF frontend UX design

Suggested navigation:

### A. Live Capture
- Bluetooth connectivity and last trigger time.
- Device SQLite connection/write-health status.
- Delay (default 3 s), duration, calibration state and uncertainties.
- Recent trigger/session list with Scheduled → Active → Awaiting Data → Ready states.
- Indication that capture is a *logical selection*, not device Start/Stop.

### B. Measurements
- Virtualized raw selected rows and processed output table.
- Timestamp, sample ID, values, warnings and source provenance.
- Sorting, filtering and inspection of missing/duplicate records.
- Ability to compare the intended interval against actual timestamps.

### C. Synchronization
- Current clock mapping, Bluetooth latency calibration, drift and uncertainty.
- Timeline visualization showing observed press, estimated physical press, desired capture window, samples and database visibility delay.
- Test/calibration results and manual review adjustments with audit history.

### D. Export & History
- Session search and history.
- Processing setting/version and data quality summary.
- CSV export first; other formats later.
- Reprocess previously captured raw samples when timing settings are revised.

UX principles: clean Windows-native appearance, clear error wording, sensible default settings, confirmation for destructive actions, visible progress and no blocking UI during long reads/exports.

## 12. Performance and maintenance strategy

1. **Short event handlers:** Timestamp incoming Bluetooth notifications immediately; hand off work to a bounded internal queue.
2. **Responsive UI:** Use async operations and dispatcher-safe batched UI updates; never bind huge datasets naively.
3. **Incremental data access:** Read only needed ranges / new batches; avoid full-table scans where the schema enables efficient access.
4. **Virtualized WPF tables:** Render visible rows instead of the complete raw record collection.
5. **Limited memory:** Keep bounded working buffers; persist session outcomes and source excerpts as needed.
6. **No unnecessary network boundary:** Keep backend logic in separate libraries in the same process initially.
7. **Failure isolation:** Make acquisition/selection resilient to timeouts, temporary locked databases, lost Bluetooth, and restarts.
8. **Observability:** Measure trigger-handler latency, database query time, source write delay, number of selected/missing rows, processing time and export duration.
9. **Testable architecture:** Core logic independent from UI and hardware, with simulated triggers and fabricated timestamp streams.
10. **Avoid premature optimization:** Set performance budgets from the real device's row rate, file growth, typical session duration and target PC hardware.

No concrete millisecond latency or throughput SLA is established yet. Measure with actual devices before promising one.

## 13. Error and edge-case policies

| Scenario | Expected behavior |
|---|---|
| Bluetooth disconnect | Display clear status; preserve already accepted sessions; log missing trigger capability |
| Duplicate notification/button bounce | Configurable debounce; log suppression reason |
| Multiple intentional presses | Separate IDs and intervals; overlapping windows allowed |
| SQLite temporarily locked | Retry safely without blocking UI or writing to device DB |
| Rows committed after window end | Wait for validated completeness; otherwise mark incomplete |
| Missing or duplicate sample timestamps | Preserve raw records, flag validation issue; deterministic tie-break |
| Clock jumps / daylight saving changes | Use monotonic PC intervals and explicit clock-domain mapping, not naive local-clock math |
| Device timestamp changes or drifts | Recalibrate/flag uncertainty, preserve original timestamps |
| Device DB resets or rotates | Recognize new source generation; do not apply old checkpoints blindly |
| Application crash/restart | Restore durable sessions/checkpoints; never assert export success without completed output |
| Source retention expires | Warn if historical data can no longer be reselected; rely on preserved session copies when available |
| Large measurement volume | Bounded queries, background processing, virtualized UI and benchmarked storage policies |

## 14. Verification and test plan

### Device investigation (before implementation)

- Determine measurement device make/model and manufacturer database guidance.
- Inspect an anonymized SQLite schema and representative records.
- Check actual timestamp format, timezone, precision, row ID, measurement units and frequency.
- Observe whether the writer inserts continuously, in batches, or updates existing rows.
- Read concurrently under live recording; identify journal and locking behavior.
- Determine data retention, file rotation and read permissions.
- Identify Bluetooth trigger make/model, transport (BLE GATT/HID/etc.), whether device timestamp exists, notification repeat patterns and reconnect behavior.

### Automated core tests

- Three-second delay and arbitrary duration boundaries.
- Inclusivity at the start and exclusivity at the end.
- Repeated and overlapping triggers; debounce behavior.
- Conversion between clock domains, drift and uncertainty reporting.
- Out-of-order records and identical acquisition timestamps.
- Missing samples, irregular sampling and invalid measurements.
- Late SQL commits, timeouts and safe retries.
- Idempotent session processing and reproducible exports.

### Integration and reliability tests

- Live write/read contention on the real SQLite file.
- Bluetooth reconnect and rapid triggering.
- Long-duration continuous measurement tests.
- Shutdown/restart/crash recovery during an active capture.
- Benchmark source query latency, processing throughput, UI responsiveness and CSV export using representative volume.
- Calibration tests with a **known physical timing reference** to estimate physical button-to-notification latency and jitter.

## 15. Phased delivery roadmap

**Phase 0 — Validate hardware and data contracts**  
Get device/trigger specifications, sample SQLite schema/data, timestamp semantics, sampling rate, and concurrent access behavior. Define initial accuracy target and acceptable uncertainty.

**Phase 1 — Acquisition prototype (headless/backend)**  
Read device SQLite safely and receive/record Bluetooth triggers. Log timing diagnostics. No polished UI required.

**Phase 2 — Synchronization and capture engine**  
Implement immutable capture windows, clock-domain calibration, interval matching, completeness and retry states. Verify against recorded test datasets.

**Phase 3 — WPF application**  
Build Live Capture, Measurements, Synchronization, History/Export screens with MVVM and responsive/virtualized tables.

**Phase 4 — Processing and export**  
Define per-field filtering/aggregation; preserve raw sources; release CSV output with validation and reprocessing support.

**Phase 5 — Field validation and optimization**  
Measure real-world timing error, Bluetooth jitter, database visibility delay, failure recovery, UI responsiveness and end-to-end session turnaround; optimize only measured bottlenecks.

## 16. Unresolved questions / decision log

These items must be answered before precise implementation commitments:

1. **Measurement SQLite schema:** Tables, columns, example rows, indexes, timestamp storage format, acquisition-vs-insertion semantics.
2. **Sampling behavior:** Nominal/actual sample rate, regular versus irregular acquisition, missing samples, units and data validation requirements.
3. **Bluetooth trigger:** Make/model, BLE GATT versus HID/other protocol, notification format, repeat behavior, timestamp availability.
4. **Synchronization reference:** How to measure physical-to-PC latency and relate the device acquisition clock to PC time; required acceptable error.
5. **Capture duration:** Common default, valid range, whether operators may change it per capture.
6. **Completeness evidence:** Device buffering delay, source-provided watermark/sequence, timeout/error policy.
7. **Processing rules:** Which measurements/columns are needed; raw-only, average, median, filtering, or domain-specific computation.
8. **Storage policy:** How long raw data remains in the source file; how much selected source data should the app retain.
9. **Independent backend lifecycle:** Must measurement capture continue while the WPF interface is closed? If yes, choose a service + IPC later.
10. **Export contract:** Required CSV column names, time format, units, encoding, ordering and any additional downstream formats.
11. **Performance targets:** Expected database size, samples/sec, number of daily sessions, acceptable end-to-end result time and target PC specifications.

### Decision log

| Decision | Status | Rationale |
|---|---|---|
| Windows-only WPF frontend | Chosen | Simple, mature Windows UI approach |
| C#/.NET backend | Chosen | Consistent language/ecosystem |
| Frontend/backend separation | Chosen | Maintainability and testability |
| Initial in-process deployment | Recommended | Lower complexity than a local web server |
| Local source SQLite | Confirmed | Direct database access is potentially feasible |
| Separate app SQLite | Recommended | Protect raw source; track sessions/history |
| Capture begins +3 seconds | Confirmed | User workflow |
| Configurable capture duration | Confirmed | User workflow |
| Exact calibration method | Open | Hardware details missing |
| Record matching based on acquisition time | Recommended / conditional | Requires reliable timestamps or an alternate clock mapping |
| CSV as first export | Recommended | Simple and broadly compatible |

## 17. Fundamental rules

> **Never confuse trigger receipt time, physical press time, sample acquisition time and database insertion time.**

> **Never modify the device-owned SQLite database to simplify our reader without vendor validation.**

> **Capture boundaries must be reproducible, even if SQLite records arrive late.**

> **Never claim timing accuracy beyond what calibration and sample resolution can support.**

> **Keep the WPF UI free of acquisition, clock-mapping, and measurement-selection business logic.**

---

### Next recommended step

Inspect a **representative anonymized SQLite schema and approximately 20–50 consecutive measurements while the device is running**, alongside the Bluetooth trigger model/protocol. That will let us validate whether direct timestamp-based selection is possible and turn this design into an implementable technical specification.
