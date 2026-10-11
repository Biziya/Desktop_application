# SPP CT-007 Measurement Studio

A frontend-only WPF application built from the supplied .NET 10 starter and reference mockup. No external NuGet dependencies, backend, database, Bluetooth, HTTP server, or real device communication.

## Run on Windows

Install the .NET 10 SDK with Windows desktop development support, then run from the extracted archive directory:

```powershell
dotnet restore Frontend/Frontend.csproj
dotnet build Frontend/Frontend.csproj
dotnet run --project Frontend/Frontend.csproj
```

The project name, namespace, target framework, and project path remain `Frontend`, `Frontend`, `net10.0-windows`, and `Frontend/Frontend.csproj`. If using an existing solution, replace its Frontend source folder with this one. Keep your existing Backend and solution files unchanged. Old bin/obj files from the uploaded starter are excluded.

## Implemented

- Compact navy/blue sidebar, reusable light-theme card/control styles, selected navigation state, resizeable window, scrollable form and virtualized results grid.
- Required drillhole ID/name, core ID and decimal depths; live depth calculation; per-field validation; numeric typing/paste filtering using Windows culture.
- Optional project, operator and multiline notes. Simulated device indicators.
- Save & Continue retains a validated core snapshot and opens Measurement. Editing the form requires saving again before another capture; existing results retain their original core snapshots.
- One immediate five-second capture per Start click. Stopwatch elapsed time, asynchronous waits, five synthetic samples, arithmetic mean, fixed `cps` unit. No trigger delay or overlapping sessions.
- Full-precision click timestamp retained; table shows milliseconds. End time is the logical five-second boundary. UI scheduling may render completion slightly later; it does not stretch the capture window. Countdown briefly shows zero then returns to 00:05.
- Capture continues safely when navigating. Results and row order remain until the application closes. This implementation has no disk persistence of session state.
- Drag only from the handle, move above/below a row using a blue insertion edge, edge scrolling, stable capture IDs and updated visible numbering. Sorting is disabled so row order and exports agree.
- Delete/Cancel confirmation and short deletion/export notifications.
- CSV save dialog, UTF-8 BOM, invariant numeric values, ISO 8601 timestamps with offsets, CSV quoting/formula protection, metadata snapshots and current display order. Export writes a temporary file before replacing the destination.
- Window closure cancels outstanding work. Incomplete captures are not added.

## Structure

- `MainWindow.xaml`: application shell and view templates.
- `Views/`: form, measurement table, confirmation dialog. Code-behind is limited to view input/drag/drop/dialog behavior.
- `ViewModels/`: retained navigation/form/measurement state, commands, validation, ordering.
- `Models/`: validated core snapshot and measurement result.
- `Services/`: replaceable measurement, CSV export and dialog interfaces plus mock/frontend implementations.
- `Infrastructure/`: small observable object and command helpers.
- `Styles/Theme.xaml`: shared colors and control templates.

The unused Settings/About/Help links in the illustrative mockup are omitted because no behavior was requested. The large top-level page headings and measurement summary/status cards are omitted as requested; form section headings remain.

## Verification status

Source XML, XAML resource references, event-handler references, and archive contents were checked in the implementation environment. No .NET SDK or Windows desktop runtime was available there, so compilation, WPF rendering, runtime interaction checks, and smoke tests are **not yet executed**. Do not treat static validation as proof of a successful Windows build.

An executable, dependency-free smoke-test project is provided alongside this folder:

```powershell
dotnet run --project SmokeTests/SmokeTests.csproj
```

It checks form validation and calculation, capture overlap protection, averaging, repeat captures, stable capture numbers, reorder/delete semantics, export order/escaping, navigation state, five-second duration/countdown, cancellation and closure.

Windows visual/interaction checks:

1. Build and launch; resize to 960 × 650 and maximize. Check tab focus and readable table scrolling.
2. Save an empty form; enter valid required values and verify Save & Continue.
3. Start; switch pages during capture; return and verify exactly one row and five samples.
4. Repeat, drag rows above/below each other, scroll during dragging, cancel/confirm deletion.
5. Export and inspect CSV order, metadata and local timestamp offsets. Cancel the file dialog; try an unwritable location.
6. Close during capture and reopen; confirm a fresh session (memory-only state is intentional).
