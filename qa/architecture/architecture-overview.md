# Architecture Overview

Verified against the source at `277d581` (production code unchanged up to `d46181e`). The detailed views are in the layer documents linked at the end; this page is the only full-system picture.

## System context

```text
                         Operator (mouse / keyboard)
                                   │
                ┌──────────────────▼──────────────────────────────────────────┐
                │  Frontend.exe — WPF, single process                          │
                │   Views (XAML) ─▶ ViewModels ─▶ services / domain / data      │
                │   ├─ Core Information page (drillhole, operator, notes)       │
                │   ├─ Measurement page (depths, capture, table, chart, CSV)    │
                │   └─ Device Status rows (Bluetooth, app DB, Gamma Guard DB)   │
                └───┬────────────────┬────────────────────┬──────────────┬─────┘
       HTTP (HEAD /, GET drillholes) │ Microsoft.Data.Sqlite │ read-only     │ WinRT
                    ▼                ▼                       ▼              ▼
  Backend (ASP.NET Core, :5002)   measurements.db          Gamma Guard DB   Windows Bluetooth
   DrillholeController            (app-owned, WAL,          (vendor-owned,   (CT007-M status,
   └─ DrillholeRepository ─SqlClient─▶ SQL Server            written by       informational)
                                  ZOOVCH_COLLAR_LIST         Gamma Guard ◀── CT007-M detector)
```

## Components

| Component | Technology | Responsibility | Details |
|---|---|---|---|
| `Frontend/` | WPF, C#, .NET 10 (`net10.0-windows10.0.19041.0`) | All UI, plus capture, averaging, local persistence, migrations, CSV export, device status | [frontend-architecture.md](frontend-architecture.md) |
| `Backend/` | ASP.NET Core Web API (controllers), .NET 10, SqlClient, Swashbuckle | Drillhole reference data from SQL Server; two unused diagnostic endpoints | [backend-architecture.md](backend-architecture.md) |
| App database | SQLite `%LocalAppData%\CT007_Measurement_Studio\measurements.db` | Sessions, records, raw samples, depth progress, drillhole cache | [database-architecture.md](database-architecture.md) |
| Gamma Guard database | SQLite owned by the vendor app | Detector samples (read-only for this app) | [database-architecture.md](database-architecture.md) §3 |
| Corporate SQL Server | `ZOOVCH_COLLAR_LIST` table | Drillhole master data (read by the backend) | [database-architecture.md](database-architecture.md) §4 |
| `Tests/` | Console test harness, CI scripts, mutation tooling | Automated QA (not part of the product) | [`Tests/README.md`](../../Tests/README.md) |

## Key characteristics

- **Most business logic lives in the WPF process.** The HTTP backend is a thin reference-data service; the measurement workflow runs entirely locally and works offline. (This differs from the README's separation goal: [DIS-03](../requirements/unresolved-requirements.md).)
- **Two clocks meet at capture time.** The PC click time and the device `ts_ms` (Unix ms) are compared directly, after a sanity check ([BR-010](../requirements/business-rules.md)).
- **There are three data stores with different owners.** The app writes only its own SQLite file. The vendor database is read-only. The SQL Server is only reached through the backend.
- **There is no dependency-injection container in the frontend.** `MainViewModel` is the composition root. The backend uses ASP.NET Core DI.
- **No authentication** on any interface.

## Document map

| Topic | Document |
|---|---|
| Frontend layers, pages, state, validation | [frontend-architecture.md](frontend-architecture.md) |
| Backend API, DI, data access, errors | [backend-architecture.md](backend-architecture.md) |
| Schemas, keys, transactions, migrations | [database-architecture.md](database-architecture.md) |
| Cross-layer flows and contracts | [integration-architecture.md](integration-architecture.md) |
| Feature workflows and state machine | [feature-workflows.md](feature-workflows.md) |
| Decisions and constraints | [technical-decisions.md](technical-decisions.md) |
