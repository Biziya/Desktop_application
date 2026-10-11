# SPP CT-007 Measurement Studio

A frontend-only WPF application built from the supplied .NET 10 starter and reference mockup. No external NuGet dependencies, backend, database, Bluetooth, HTTP server, or real device communication.

## Run on Windows

Install the .NET 10 SDK with Windows desktop development support, then run from the extracted archive directory:

```powershell
dotnet restore Frontend/Frontend.csproj
dotnet build Frontend/Frontend.csproj
dotnet run --project Frontend/Frontend.csproj
```

The project name, root namespace, target framework, and project path remain `Frontend`, `Frontend`, `net10.0-windows`, and `Frontend/Frontend.csproj`. If using an existing solution, replace its Frontend source folder with this one. Keep your existing Backend and solution files unchanged. Old bin/obj files from the uploaded starter are excluded.

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

```text
Frontend/
├── Pages/
│   ├── CoreInformation/
│   │   ├── CoreInformationPage.xaml
│   │   ├── CoreInformationPage.xaml.cs
│   │   └── CoreInformationViewModel.cs
│   └── Measurement/
│       ├── MeasurementPage.xaml
│       ├── MeasurementPage.xaml.cs
│       ├── MeasurementViewModel.cs
│       ├── MeasurementResult.cs
│       ├── MockMeasurementService.cs
│       ├── CsvExportService.cs
│       └── MeasurementStyles.xaml
├── Windows/
│   └── ConfirmationWindow/
│       ├── DeleteConfirmationWindow.xaml
│       ├── DeleteConfirmationWindow.xaml.cs
│       └── DialogService.cs
├── Shared/
│   ├── Infrastructure/
│   │   ├── ObservableObject.cs
│   │   └── RelayCommand.cs
│   ├── Models/
│   │   └── CoreInformation.cs
│   ├── Services/
│   │   └── IDialogService.cs
│   └── Styles/
│       └── Theme.xaml
├── ViewModels/
│   └── MainViewModel.cs
├── MainWindow.xaml
├── MainWindow.xaml.cs
├── App.xaml
├── App.xaml.cs
├── AssemblyInfo.cs
├── Frontend.csproj
└── README.md
SmokeTests/
├── SmokeTests.csproj
└── Program.cs
```

CoreInformation owns form UI, validation and depth calculation. Measurement owns capture state, table interactions, simulation, result model, export and table styles. ConfirmationWindow owns the existing delete dialog and its implementation service. No extra nesting project or empty folders were added.

CoreInformation is a shared data model because both the form and captured results use it. IDialogService is the shared contract between Measurement and the dialog implementation; Measurement does not reference the concrete window. Shared infrastructure and general styles support all views. MainViewModel remains outside the page folders and owns navigation, retained page instances and service composition.

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

## Measurement UI cleanup

Removed the Capture No. and End Time display columns, capture instructions, table footer instructions, sidebar subtitle/preview text and duration panel. The countdown and Start button share a balanced two-column layout. Conditional form validation guidance, the empty-table message, and action notifications remain.

Rows are now 36 pixels high (previously 60); headers are 34 pixels high (previously 48). Cell vertical padding remains zero, header/button padding is reduced, and text/drag/delete controls are vertically centered. Flexible data columns fill the table width with minimum widths for readability. Font sizes and alternating backgrounds are preserved.

Capture No. and End Time remain in result models and CSV exports. All C# source, including measurement timing, averaging, commands, navigation, export and drag/drop handlers, is unchanged by this cleanup. That earlier cleanup changed only the main-window XAML, measurement-page XAML, theme and README; the files have since been relocated as described below.

Cleanup verification: XML/resource/event-handler and column-order checks passed; all C# files were compared byte-for-byte with the previous implementation. Windows build and runtime interaction checks remain unexecuted because this environment has no .NET SDK or Windows desktop runtime.

## Page-folder refactor

### Files moved

- Core Information view, code-behind and ViewModel: `Views/` and `ViewModels/` → `Pages/CoreInformation/`.
- Measurement view, code-behind, ViewModel, result model and mock/export services: `Views/`, `ViewModels/`, `Models/` and `Services/` → `Pages/Measurement/`.
- Delete dialog XAML/code-behind and DialogService: `Views/` and `Services/` → `Windows/ConfirmationWindow/`; existing filenames retained.
- CoreInformation model → `Shared/Models/`; observable/command helpers → `Shared/Infrastructure/`; general theme → `Shared/Styles/`.
- IDialogService was extracted unchanged from DialogService.cs into `Shared/Services/IDialogService.cs` to keep the measurement view model independent of the concrete dialog.
- Five table-specific style definitions were moved unchanged into `Pages/Measurement/MeasurementStyles.xaml` and merged by MeasurementPage.
- Old empty Views, Models, Services, Infrastructure and Styles folders were removed. No duplicate source files remain.

### References updated

C# namespaces follow the new folders: `Frontend.Pages.CoreInformation`, `Frontend.Pages.Measurement`, `Frontend.Windows.ConfirmationWindow` and `Frontend.Shared.*`. MainViewModel remains `Frontend.ViewModels`. Using directives in the frontend and smoke tests were updated. A `CoreInformationData` type alias refers to the same shared CoreInformation model to avoid ambiguity with the new CoreInformation namespace; the actual class and its properties are unchanged.

View x:Class values now match their relocated code-behind namespaces. MainWindow uses separate core and measurement XAML namespace declarations and updated DataTemplates. App.xaml loads `Shared/Styles/Theme.xaml`. MeasurementPage merges the adjacent `MeasurementStyles.xaml`. StartupUri remains `MainWindow.xaml`.

The SDK-style project still uses default WPF globs for XAML Page and C# Compile items. No explicit old-path includes or pack URIs were present, so the project file and build settings remain unchanged. App.xaml retains its default ApplicationDefinition role.

### Validation results

- PASS: all C# implementation bodies match the prior version after normalizing imports, namespaces, the model alias and the extracted interface.
- PASS: all existing view element trees/layouts and style definitions match the prior version after accounting for relocated namespace/resource declarations.
- PASS: XML parsing, XAML x:Class/code-behind correspondence, namespace and DataTemplate type references, event handlers, static resource names and relative dictionary source paths.
- PASS: no obsolete moved source files or empty folders; archive integrity verified.
- BLOCKED: `dotnet build Frontend/Frontend.csproj` could not start because `dotnet` is not installed in the execution environment.
- NOT EXECUTED: Windows launch, live bindings/resource loading, navigation, countdown, averaging, drag/drop, delete dialog and CSV interaction checks. The provided smoke tests remain available for Windows; their imports were updated without changing test logic.

When replacing the previous Frontend directory, remove or move the old directory first rather than overlaying the new files. Overlaying leaves the old source paths behind and can cause duplicate classes/resources. Preserve the separate Backend project and existing solution files.

## UI polish and collapsible sidebar

- Sidebar expands to 224 pixels and collapses to a 72-pixel icon rail. The toggle remains at the same top-left position, with an action-specific chevron, tooltip and accessible name.
- Both navigation items retain icons, tooltips and selected-state highlighting. Labels/branding hide when collapsed. Content receives the freed width automatically through an Auto-sized sidebar column.
- MainViewModel owns the in-memory sidebar state and command. It survives page switches; no disk preference or new page lifecycle is introduced. Toggle changes do not replace page ViewModels, alter CurrentPage or restart a capture.
- Shared palette now provides navy text, soft-blue selection, neutral surfaces, green primary measurement actions and red destructive actions. Cards use 12-pixel corners; inputs/buttons use 8-pixel corners. Vector navigation icons replace text glyphs.
- Existing buttons have hover/pressed/disabled feedback and a visible keyboard focus ring. Inputs use a consistent minimum height and read-only surface; existing error strings also control error borders without modifying validation.
- Form spacing/headings, measurement controls, table header/selection states and the existing confirmation dialog interior are refined. Native window controls remain intact. The seven existing table columns and 36-pixel rows are preserved.
- Sidebar resizing is immediate; no width animation, expensive shadows, new packages, additional workflows or restored instructional panels were added.

Changed files: MainWindow.xaml, ViewModels/MainViewModel.cs, Shared/Styles/Theme.xaml, Pages/CoreInformation/CoreInformationPage.xaml, Pages/Measurement/MeasurementPage.xaml, Pages/Measurement/MeasurementStyles.xaml, Windows/ConfirmationWindow/DeleteConfirmationWindow.xaml, SmokeTests/Program.cs, and this README.

Validation completed here: all existing non-shell C# files unchanged; existing navigation logic unchanged after excluding new sidebar members; XML/class/resource/event checks passed; form input bindings and measurement column headers/order/bindings preserved; compact row height and project dependencies preserved. These are source checks, not runtime rendering verification.

Build attempt: `dotnet build Frontend/Frontend.csproj` could not start (`dotnet: command not found`). Windows build, launch, visual rendering, live binding/resource resolution and interaction tests remain unverified. Smoke tests include new assertions for collapse/expand, tooltip labels, retained page instances/form values and sidebar state across navigation; those tests have not been executed here.

Additional Windows checks: toggle on both pages; navigate using icons; toggle during an active capture; resize in both sidebar states at the minimum size and maximized; check keyboard focus, validation errors and 125%/150% display scaling; drag/delete/export rows; cancel/confirm the existing dialog. The existing run commands above are unchanged.
