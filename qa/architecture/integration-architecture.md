# Integration Architecture

How actions cross layers. The component picture is in [architecture-overview.md](architecture-overview.md).

## 1. Drillhole list (the only HTTP data flow)

```text
App start / Retry / backend comes back online (only if the last load failed)
  ↓ CoreInformationViewModel.LoadDrillholesAsync        (a running load is reused)
  ↓ first time only: DrillholeCache.LoadAsync → DrillholeCatalog.Build → dropdown
  ↓ DrillholesClient.GetAsync → ApiClient.GetAsync  ── HTTP GET /api/core-information/drillholes (timeout 5 s)
  ↓ DrillholeController.Get → DrillholeRepository.GetAllAsync → SQL Server ZOOVCH_COLLAR_LIST
  ↑ 200 JSON | 503 ProblemDetails | refused / timeout
  ↑ ApiResult: Ok | HttpError("Backend error 503 (…)") | Unreachable | InvalidResponse
  ↑ Ok with ≥ 1 entry → catalog replaced + DrillholeCache.ReplaceAsync (one transaction)
    Ok but empty with a list shown → keep the list ("returned no drillholes; showing the saved list")
    failure → keep the list and form; message + Retry
  ↑ every request → ApiClient.Reached(bool) → BackendConnection Online/Offline → StatusAnnouncements toast
```

## 2. Capture (local; the core workflow)

```text
Right mouse down in MainWindow (active, enabled, not minimised, measurement mode on)
  ↓ TriggerCommand.CanExecute → CanTrigger (Ready, session = saved drillhole, valid depths, no capture, editor closed)
  ↓ MeasurementViewModel.CaptureAsync(click time): freeze from/to/step/duration; State = Capturing
  ↓ DeviceCaptureService.CaptureAsync (database calls on the thread pool)
      GammaGuardCaptureDevices.Resolve: Gamma Guard DB status must be Available → newest serial (refuse if another ≤ 5 s)
      baseline id − 200; DeviceTime check; poll ReadSamplesAfter every 250 ms until click + duration;
      CaptureWindow filters (window, serial, duplicates); final read ≤ 1.5 s
  ↓ State = Processing (100 ms hold) → SampleAveraging → MeasurementResult
  ↓ insert in From order; advance the depth unless edited
  ↓ persistence queue → SqliteMeasurementRepository.SaveAsync (progress + record + samples, one transaction)
  ↑ MeasurementsSaved → Core page refreshes "Measured drillholes"
Errors: CaptureException → error toast, no row; other → "Capture failed…"; cancellation → silent
```

## 3. Save & Continue → session

```text
SaveCommand → drillhole required → Operator autofill → CoreSnapshot (trimmed)
  ↓ MainViewModel: Measurement.SetCore(core); navigate to Measurement
  ↓ OpenDrillholeAsync: await queued saves (refuse if rows are still unsaved)
  ↓ ResolveSessionAsync (stored session wins) → LoadAsync + LoadProgressAsync → rows sorted, depth inputs restored
  ↑ warning toast if the stored metadata differs from the form
```

## 4. Contracts between layers

| Contract | Producer | Consumer | Notes |
|---|---|---|---|
| Drillhole JSON `{holeId, projectCode, campaign}` | Backend `DrillholeResponse` (non-null) | Frontend `DrillholeResponse` (nullable) | Duplicated types aligned by name; INT-005 guards it |
| Field mapping | `HOLEID` / `PROJECTCODE` / `RIGHT(PROSPECT,4)` | Form: Drillhole / Project / Campaign → SQLite `DrillholeID` / `DrillholeName` / `Project` → CSV Drillhole / Project / Campaign | Labels renamed in the UI; storage names unchanged (INT-001) |
| Error payload | ProblemDetails (503) | Status code only; body ignored | — |
| Reachability | Any HTTP answer to `HEAD /` (404 expected) | `BackendConnection` | UR-13 |
| Gamma Guard schema | Vendor | `GammaGuardDeviceDatabase` | Assumed; not verified (UR-15) |
| CSV | `CsvExportService` | Downstream users (unknown) | [BR-023](../requirements/business-rules.md); contract Unresolved (UR-04) |

## 5. Error propagation

| Origin | Backend | HTTP | Frontend result | User sees |
|---|---|---|---|---|
| SQL Server down | `SqlException` → 503 | 503 | `HttpError`, still Online | "Showing the saved drillhole list. Could not refresh: Backend error 503 …" + Retry |
| Backend process down / timeout | — | — | `Unreachable`, Offline | Offline indicator, one warning toast, saved list kept |
| Malformed JSON | — | 200 | `InvalidResponse` | "Backend returned unexpected data." |
| Missing connection string | DI constructor throws | 500 | `HttpError` | "Backend error 500 …" |
| Gamma Guard missing / locked | — | — | Status row; capture refused or failed | Error toast (a reason only when the status isn't Available) |
| App DB write failure | — | — | Save fails; rows kept | Error toast; retried later |
