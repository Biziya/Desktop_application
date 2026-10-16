using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Data.Sqlite;
using Frontend.Api;
using Frontend.Api.CoreInformation.Drillholes;
using Frontend.Calculation.CoreInformation;
using Frontend.Calculation.CoreInformation.BluetoothDevice;
using Frontend.Calculation.CoreInformation.GammaGuardDatabase;
using Frontend.Calculation.CoreInformation.MeasurementDatabase;
using Frontend.Calculation.Measurement;
using System.Windows.Threading;
using Frontend.Shared.Infrastructure;
using Frontend.Shared.Models;
using Frontend.Pages.Measurement;
using Frontend.Pages.CoreInformation;
using Frontend.ViewModels;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var exitCode = 0;
        var dispatcher = Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        dispatcher.BeginInvoke(new Action(async () =>
        {
            try { await Run(); Console.WriteLine("All smoke checks passed."); }
            catch (Exception e) { Console.Error.WriteLine(e); exitCode = 1; }
            finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal); }
        }));
        Dispatcher.Run();
        return exitCode;
    }
    // Short search pause for tests (the app uses 150 ms).
    private static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(15);
    // Local Device Status checks with a fake Bluetooth list (no CT-007) and no Gamma Guard search folders.
    private static DeviceStatusMonitor NewDevices(string measurementDatabase, Func<IReadOnlyList<KnownBluetoothDevice>>? bluetooth = null) =>
        new(new Ct007BluetoothCheck(DeviceSettings.Default, bluetooth ?? (() => [])), new MeasurementDatabaseCheck(measurementDatabase),
            new GammaGuardDatabaseCheck(DeviceSettings.Default, []), TimeSpan.FromMinutes(5));
    // A toast with this kind whose message starts with the text is (or was just) shown.
    private static bool Toasted(ToastService toasts, ToastKind kind, string start) => toasts.Items.Any(t => t.Kind == kind && t.Message.StartsWith(start));
    // Types, waits for the filter, then presses Enter (picks the highlighted closest match).
    private static async Task Pick(CoreInformationViewModel form, string text) { await Search(form, text); Check(form.CommitClosest(), $"Closest match for '{text}' picked"); await form.WhenOptionsUpdated(); }
    // Types into the Drillhole box and waits for the (debounced, background) filter.
    private static async Task Search(CoreInformationViewModel form, string text) { form.DrillholeSearch = text; await form.WhenOptionsUpdated(); }
    // Drillholes (not group headings) in the dropdown, and the group headings, in display order.
    private static string[] Ids(CoreInformationViewModel form) => form.DrillholeOptions.Where(o => !o.IsHeader).Select(o => o.HoleId).ToArray();
    private static string[] Headers(CoreInformationViewModel form) => form.DrillholeOptions.Where(o => o.IsHeader).Select(o => o.Header).ToArray();
    private static string TempDb(string name) => Path.Combine(Path.GetTempPath(), $"{name}-{Guid.NewGuid():N}.db");
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static async Task Until(Func<bool> predicate)
    {
        var timeout = Stopwatch.StartNew();
        while (!predicate())
        {
            if (timeout.Elapsed.TotalSeconds > 3) throw new TimeoutException("Command did not settle.");
            await Task.Delay(10);
        }
    }
    private static async Task Run()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CoreSnapshot? saved = null;
        var backendStub = new StubBackend();
        var settings = ApiSettings.Default with { RequestTimeout = TimeSpan.FromSeconds(2) };
        using var api = new ApiClient(settings, backendStub);
        using var backend = new BackendConnection(api, TimeSpan.FromMilliseconds(30));
        Check(backend.State == BackendState.Checking && backend.StateText == "Checking…", "Indicator starts as Checking…");
        var formCache = new DrillholeCache(TempDb("ct007-cache"));
        using var formDevices = NewDevices(@"C:\data\measurements.db");
        var form = new CoreInformationViewModel(core => saved = core, @"C:\data\measurements.db",
            new DrillholesClient(api), backend, formCache, new MemoryRepository(), formDevices, SearchDelay);
        Check(form.DatabasePath == @"C:\data\measurements.db" && form.Devices.Bluetooth.State == LocalState.NotSelected &&
            form.Devices.MeasurementDatabase is { State: LocalState.Checking, Path: @"C:\data\measurements.db" }, "Measurement DB path local; device status checking; Bluetooth: No device selected");
        await Until(() => Ids(form).Length == 3);
        Check(backend.State == BackendState.Online, "Page requests mark the backend Online");
        Check(form.DrillholeId == "" && form.DrillholeName == "" && form.ProjectArea == "" && form.PickedDrillhole is null &&
            form.DrillholesMessage == "", "Drillholes loaded; none selected automatically");
        Check(Headers(form).SequenceEqual(new[] {"All drillholes"}) && form.DrillholeOptions[0].IsHeader &&
            Ids(form).SequenceEqual(new[] {"DH-001", "GTZO_0001_1", "GTZO_0002_1"}), "No history: only All drillholes, alphabetical");

        // Searchable dropdown: partial filter, pick fills Project and Campaign (projectCode / campaign).
        await Search(form, "0002");
        Check(Ids(form).SequenceEqual(new[] {"GTZO_0002_1"}) && form.DrillholeId == "", "Partial text filters, selects nothing");
        form.PickedDrillhole = null; await form.WhenOptionsUpdated();
        Check(form.DrillholeId == "", "A dropdown clearing its selection changes nothing");
        form.PickedDrillhole = form.DrillholeOptions[0]; await form.WhenOptionsUpdated();
        Check(form.DrillholeOptions[0].IsHeader && form.DrillholeId == "", "A group heading cannot be selected");
        form.PickedDrillhole = form.DrillholeOptions.First(o => !o.IsHeader); await form.WhenOptionsUpdated();
        Check(form is { DrillholeId: "GTZO_0002_1", DrillholeName: "ZOOVCH", ProjectArea: "2016", DrillholeSearch: "GTZO_0002_1" } &&
            Ids(form).Length == 3 && form.PickedDrillhole?.HoleId == "GTZO_0002_1", "Picking fills Drillhole, Project (projectCode) and Campaign (campaign)");
        Check(typeof(CoreInformationViewModel).GetProperty(nameof(CoreInformationViewModel.DrillholeName))!.CanWrite == false &&
            typeof(CoreInformationViewModel).GetProperty(nameof(CoreInformationViewModel.ProjectArea))!.CanWrite == false, "Project and Campaign are read-only");
        await Search(form, "GTZO");
        Check(form.DrillholeId == "" && form.DrillholeName == "" && Ids(form).Length == 2, "Editing the text away from the pick clears it");
        await Search(form, "gtzo_0001_1");
        Check(form is { DrillholeId: "", DrillholeName: "", ProjectArea: "", DrillholeSearch: "gtzo_0001_1" } && form.ClosestOption?.HoleId == "GTZO_0001_1",
            "Typing a full Hole ID highlights it as the closest match without picking it or rewriting the text");
        Check(form.CommitClosest() && form is { DrillholeId: "GTZO_0001_1", DrillholeName: "ZOOVCH", ProjectArea: "2015" }, "Enter picks the highlighted match");
        await form.WhenOptionsUpdated();
        form.Notes = "unsaved note";

        // Refresh keeps the selection and form values; a drillhole missing from the new list is flagged, not dropped.
        backendStub.DrillholesJson = """[{"holeId":"GTZO_0002_1","projectCode":"ZOOVCH","campaign":"2016"}]""";
        await form.LoadDrillholesAsync();
        Check(form is { DrillholeId: "GTZO_0001_1", DrillholeName: "ZOOVCH", ProjectArea: "2015", Notes: "unsaved note" } &&
            form.DrillholesMessage == "GTZO_0001_1 is not in the latest drillhole list; its saved details are kept.", "Refresh keeps selection; missing entry flagged");
        backendStub.DrillholesStatus = HttpStatusCode.InternalServerError;
        await form.LoadDrillholesAsync();
        Check(form.DrillholesMessage.StartsWith("Showing the saved drillhole list. Could not refresh: Backend error 500") && form.CanRetryDrillholes &&
            backend.State == BackendState.Online && form.DrillholeId == "GTZO_0001_1" && Ids(form).SequenceEqual(new[] {"GTZO_0002_1"}),
            "HTTP error: endpoint error shown, last list kept, backend still Online, values kept");
        backendStub.DrillholesStatus = HttpStatusCode.OK; backendStub.DrillholesJson = StubBackend.DefaultDrillholes;

        // Offline → recovery: requests fail as unreachable, then everything that failed reloads by itself.
        backendStub.Offline = true;
        await form.LoadDrillholesAsync(); await backend.CheckNowAsync();
        Check(backend.State == BackendState.Offline && backend.StateText == "Offline" &&
            form.DrillholesMessage.Contains("Backend is offline") && form.DrillholeId == "GTZO_0001_1" && form.Notes == "unsaved note",
            "Offline: indicator Offline, form values kept");
        var drillholeCalls = backendStub.Calls(DrillholesClient.Path);
        backendStub.Offline = false;
        await backend.CheckNowAsync();
        await Until(() => backendStub.Calls(DrillholesClient.Path) == drillholeCalls + 1 && form.DrillholesMessage == "");
        Check(backend.State == BackendState.Online && Ids(form).Length == 3, "Recovery: Online again and failed resources reload once");
        Check(backend.Tooltip.StartsWith("Backend http://localhost:5002: Online"), "Indicator tooltip");

        // Checks never overlap: concurrent requests share one call; the periodic loop runs once even if started twice.
        backendStub.Hold = new TaskCompletionSource();
        var probes = backendStub.Calls("/");
        var checkA = backend.CheckNowAsync(); var checkB = backend.CheckNowAsync(); var checkC = backend.CheckNowAsync();
        Check(checkA == checkB && checkB == checkC, "Overlapping checks reuse the running one");
        backendStub.Hold.SetResult(); backendStub.Hold = null;
        await checkA;
        Check(backendStub.Calls("/") == probes + 1 && backendStub.Methods.Contains("HEAD /"), "One HEAD probe of the base URL for three overlapping checks");
        Check(backendStub.Calls("/api/core-information/bluetooth-device") == 0 && backendStub.Calls("/api/core-information/sqlite-database") == 0,
            "The superseded backend status endpoints are never called");
        probes = backendStub.Calls("/");
        backend.Start(); backend.Start();
        await Task.Delay(330);
        var polled = backendStub.Calls("/") - probes;
        Check(polled is >= 3 and <= 13, $"Periodic check runs once per interval, not doubled ({polled} calls in 330 ms at 30 ms)");
        Check(backendStub.Calls(DrillholesClient.Path) == drillholeCalls + 1, "Connectivity checks never download the drillhole list");

        var account = CoreInformationViewModel.CurrentWindowsAccount();
        Check(account.Length > 0 && account.EndsWith(Environment.UserName) &&
            (account == Environment.UserName || account == $"{Environment.UserDomainName}\\{Environment.UserName}") &&
            account != Environment.MachineName, "Windows account: DOMAIN\\user or user, never the computer name alone");
        Check(form.Operator == account, "Operator autofilled for a new form");
        await Search(form, "");
        form.SaveCommand.Execute(null);
        Check(saved is null && form.DrillholeIdError == "Select a drillhole from the list.", "Saving needs a selected drillhole");
        await Search(form, "dh-0"); form.PickedDrillhole = form.DrillholeOptions.Single(o => !o.IsHeader); await form.WhenOptionsUpdated();
        Check(form.IsValid && typeof(CoreInformationViewModel).GetProperty("CoreId") is null && typeof(CoreSnapshot).GetProperty("CoreId") is null,
            "Valid with a selected drillhole; no Core ID or depth fields");
        form.Operator = "Field Tech"; form.SaveCommand.Execute(null);
        Check(saved is { DrillholeId: "DH-001", DrillholeName: "North Ridge", ProjectArea: "Campaign 7", Operator: "Field Tech" },
            "Renamed fields save to their original mappings; edited Operator kept");
        form.Operator = "  "; form.SaveCommand.Execute(null);
        Check(saved?.Operator == account && form.Operator == account, "Empty Operator autofilled again on save");
        form.Operator = "Field Tech"; form.SaveCommand.Execute(null);
        Check(typeof(CoreInformationViewModel).GetProperty("DepthFrom") is null && typeof(CoreInformationViewModel).GetProperty("DepthTo") is null &&
            typeof(CoreSnapshot).GetProperty("DepthFrom") is null, "Core Information has no From/To fields");
        var core = saved ?? throw new InvalidOperationException("No saved core");
        var fake = new ControlledMeasurements(); var dialogs = new FakeDialogs(); var export = new FakeExport();
        var store = new MemoryRepository();
        using var vm = new MeasurementViewModel(fake, export, dialogs, store);
        Check(!vm.ToggleMeasurementCommand.CanExecute(null), "Capture requires saved core");
        Check(vm.SessionId is null, "No session before Start");
        Check(!vm.ExportCommand.CanExecute(null), "Empty export disabled");
        vm.TriggerCommand.Execute(null);
        Check(fake.Calls == 0, "Idle triggers ignored");
        vm.SetCore(core);
        var firstSession = vm.SessionId ?? throw new InvalidOperationException("Saving the core opens the drillhole session");
        Check(vm.NextFrom == "" && vm.Step == "", "A new session has no saved progress");
        vm.ToggleMeasurementCommand.Execute(null);
        Check(!vm.IsMeasurementModeActive && Toasted(vm.Toasts, ToastKind.Error, "Enter the starting depth and step"),
            "Start without depths does not arm; a 5 s error toast says what is missing");
        vm.NextFrom = "150"; vm.ToggleMeasurementCommand.Execute(null);
        Check(!vm.IsMeasurementModeActive, "Start still needs a step");
        vm.Step = "50";   // centimetres
        Check(vm.ToggleMeasurementCommand.CanExecute(null) && vm.DepthError == "", "Starting depth and step entered");
        Check(!vm.IsMeasurementModeActive, "Toggle shows Start while stopped");
        vm.ToggleMeasurementCommand.Execute(null);
        Check(vm.IsMeasurementModeActive && vm.ToggleMeasurementCommand.CanExecute(null), "Same toggle shows Stop and stays enabled while measuring");
        Check(vm.State == MeasurementState.Ready && vm.IsMeasurementModeActive && fake.Calls == 0 &&
            vm.Results.Count == 0 && vm.Countdown == "00:05" && vm.CaptureProgress == 0, "Start arms without capturing");
        var triggerTime = new DateTimeOffset(2026,10,11,11,16,10,851,TimeSpan.FromHours(8));
        vm.TriggerCommand.Execute(triggerTime); vm.TriggerCommand.Execute(triggerTime);
        Check(fake.Calls == 1 && vm.IsCapturing && !vm.TriggerCommand.CanExecute(null) &&
            vm.ToggleMeasurementCommand.CanExecute(null), "Duplicate capture blocked, Stop remains enabled");
        fake.Report(TimeSpan.FromSeconds(2.5));
        Check(vm.Countdown == "00:03" && vm.CaptureProgress == 0.5, "Elapsed progress drives ring and text");
        fake.Complete(); await Until(() => vm.State == MeasurementState.Processing);
        Check(vm.CaptureProgress == 1 && vm.Countdown == "00:00" && !vm.CanTrigger, "Processing shows zero and blocks triggers");
        await Until(() => vm.CanTrigger);
        Check(vm.Results.Count == 1 && vm.Results[0].AverageValue == 3 && vm.Results[0].SampleCount == 5, "Exactly one averaged result");
        Check(vm.Results[0].StartTime == triggerTime && vm.Results[0].EndTime == triggerTime.AddSeconds(5) &&
            vm.Results[0].CompletedAt == triggerTime.AddSeconds(5).AddMilliseconds(20), "Independent precise trigger/end/completion timestamps");
        Check(vm.Countdown == "00:05" && vm.CaptureProgress == 0 && vm.IsMeasurementModeActive, "Reset while staying armed");
        for (int i=0; i<2; i++) { vm.TriggerCommand.Execute(null); fake.Complete(); await Until(() => vm.CanTrigger); }
        Check(vm.Results.Select(r => (r.FromMm, r.ToMm, r.StepMm)).SequenceEqual(new (long?, long?, long?)[]
            { (150000, 150500, 500), (150500, 151000, 500), (151000, 151500, 500) }) && vm.NextFrom == "151.50",
            "Each capture stores from, to = from + step, and advances once");
        var first = vm.Results[0]; var second = vm.Results[1]; var third = vm.Results[2];
        await Until(() => store.Rows(firstSession).Count == 3);
        Check(typeof(MeasurementViewModel).GetMethod("Move") is null && typeof(IDialogService).GetMethod("ConfirmMove") is null,
            "Manual row reordering and its confirmation are removed");
        dialogs.Confirm = false; vm.DeleteCommand.Execute(second);
        Check(vm.Results.Count == 3, "Cancel deletion");
        dialogs.Confirm = true; vm.DeleteCommand.Execute(second);
        Check(vm.Results.Count == 2 && third.Number == 2 && third.CaptureNumber == 3, "Deletion renumbers display only");
        vm.TriggerCommand.Execute(null); vm.Step = "25"; fake.Complete(); await Until(() => vm.CanTrigger);
        Check(vm.Results[2].CaptureNumber == 4, "Deleted capture numbers are not reused");
        Check(vm.Results[2] is { FromMm: 151500, ToMm: 152000, StepMm: 500 } && vm.NextFrom == "152.00" && vm.Step == "25",
            "A step change during a capture applies to the next capture only");
        Check(vm.SessionId == firstSession && vm.Results.All(r => r.SessionId == firstSession),
            "One session ID for every capture of the session");
        await Until(() => store.Rows(firstSession).Select(r => r.CaptureNumber).SequenceEqual(vm.Results.Select(r => r.CaptureNumber)));
        Check(store.SaveCount > store.Rows(firstSession).Count, "Re-saving the same rows does not duplicate them");
        vm.ExportCommand.Execute(null); await Until(() => vm.ExportCommand.CanExecute(null));
        Check(export.Rows?.Select(r => r.CaptureNumber).SequenceEqual(new[] {1,3,4}) == true, "Export keeps display order");
        var exported = export.Session ?? throw new InvalidOperationException("Export receives the session");
        Check(exported.Id == firstSession && exported.DrillholeId == "DH-001" && exported.DrillholeName == "North Ridge" &&
            exported.Project == "Campaign 7" && exported.Operator == "Field Tech", "Export receives the exported session's metadata");
        await Until(() => store.Rows(firstSession).Select(r => r.CaptureNumber).SequenceEqual(new[] {1,3,4}));
        Check(store.Rows(firstSession).Select(r => r.Number).SequenceEqual(new[] {1,2,3}), "Stored No follows the displayed order");
        Check(store.Rows(firstSession).Select(r => (r.CaptureNumber, r.FromMm)).SequenceEqual(new (int, long?)[] {(1,150000),(3,151000),(4,151500)}),
            "Rows keep their depths");
        Check(store.Progress(firstSession) == new DepthProgress(152000, 250), "Next depth and step saved with the session");
        var csv = CsvExportService.Serialize(vm.Results.ToArray(), exported);
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Check(lines[0] == "\"No\",\"Capture No\",\"Start Time\",\"End Time\",\"Sample\",\"ts_ms\",\"count0\",\"count0_raw\"," +
            "\"hdm\",\"hdm_raw\",\"dose_rate\",\"Drillhole\",\"Project\",\"Campaign\",\"Operator\",\"Notes\",\"session_id\"",
            "CSV header: measurement columns, session metadata (Campaign once), session ID last");
        Check(lines[0].Split(',').Count(h => h == "\"Campaign\"") == 1, "Campaign exported once");
        Check(lines.Skip(1).All(l => l.EndsWith($"\"DH-001\",\"North Ridge\",\"Campaign 7\",\"Field Tech\",\"unsaved note\",\"{firstSession}\"")),
            "Session metadata repeated on every row");
        Check(lines[1].StartsWith("\"1\",\"1\",") && lines[3].StartsWith("\"3\",\"4\",") && lines.Length == 4, "CSV display/capture numbers in displayed order");
        var notes = exported with { Comment = "Broken zone, \"fault\" at 151 m\nre-check", Operator = "=cmd" };
        var escapedCsv = CsvExportService.Serialize(new[] {vm.Results[0]}, notes);
        Check(escapedCsv.Contains("\"Broken zone, \"\"fault\"\" at 151 m\nre-check\"") && escapedCsv.Contains("\"'=cmd\""),
            "Notes keep commas, quotes and line breaks inside one quoted cell; formulas are neutralised");
        Check(CsvExportService.Serialize(new[] {first}, exported).Contains(triggerTime.ToString("O") + "\",\"" + triggerTime.AddSeconds(5).ToString("O")),
            "CSV retains fractional seconds and offset for start/end");
        Check(!csv.Contains("Average") && !csv.Contains("Completed At"), "CSV has only configured columns");
        var reading = new DeviceReading(1760152570851, 0.1 + 0.2, 125, 1.5e-7, -3, 12.25);
        var withReading = new MeasurementResult { SessionId=firstSession, CaptureNumber=99, StartTime=first.StartTime,
            EndTime=first.EndTime, SampleCount=5, Reading=reading, FromMm=1000, ToMm=1500, StepMm=500 };
        Check(CsvExportService.Serialize(new[] {withReading}, exported).Contains(
            "\"1760152570851\",\"0.30000000000000004\",\"125\",\"1.5E-07\",\"-3\",\"12.25\""), "CSV device values keep full precision");
        Check(CsvExportService.Serialize(new[] {first}, exported).Contains("\"5\",\"\",\"\",\"\",\"\",\"\",\"\","), "Missing device values are empty cells");
        var otherSession = new MeasurementResult { SessionId=Guid.NewGuid(), CaptureNumber=1, StartTime=first.StartTime,
            EndTime=first.EndTime, SampleCount=5 };
        try { CsvExportService.Serialize(new[] {first, otherSession}, exported); throw new Exception("Mixed-session export accepted"); }
        catch (ArgumentException) { }
        Check(MeasurementColumns.All.Where(c => MeasurementColumnLayout.For(c).Visible).Select(c => c.Label).SequenceEqual(
            new[] {"No","From (m)","To (m)","Step (cm)","Sample","ts_ms","count0","count0_raw","hdm","hdm_raw","dose_rate"}),
            "Table column order; From/To/Step shown; Capture No, times and session hidden");
        var visibleLayouts = MeasurementColumns.All.Select(MeasurementColumnLayout.For).Where(l => l.Visible).ToList();
        Check(visibleLayouts.All(l => l.Width.EndsWith('*')) && visibleLayouts.Sum(l => l.MinWidth) + 64 <= 650,
            $"Visible columns share the table width (star widths); minimums {visibleLayouts.Sum(l => l.MinWidth)} px + 64 px Delete fit the narrowest layout (~650 px)");
        Check(MeasurementColumns.All.Where(c => c.IncludeInCsv).Count() == 12 && lines[0].Contains("\"Capture No\",\"Start Time\""),
            "Hidden columns stay in CSV; CSV columns unchanged");
        Check(third is { FromMetres: 151.0m, ToMetres: 151.5m, StepMetres: 0.5m, StepCentimetres: 50m } &&
            new[] { MeasurementColumns.From, MeasurementColumns.To, MeasurementColumns.Step }.All(c =>
                c.Binding != c.Label && typeof(MeasurementResult).GetProperty(c.Binding)?.CanWrite == false),
            "Depth columns show saved depths, are read-only and keep labels separate from field names");
        Check(MeasurementColumns.All.Where(c => c != MeasurementColumns.Step && c != MeasurementColumns.Sample).All(c => Resolve(c.Binding, withReading) is { } bound && Equals(bound, c.Value(withReading))),
            "Every other binding resolves to the same value the CSV writes");
        Check(Equals(Resolve(MeasurementColumns.Step.Binding, withReading), 50m) && Equals(MeasurementColumns.Step.Value(withReading), 0.5m) &&
            MeasurementColumns.Step.CsvHeader == "step_m", "Step column shows centimetres (50); its CSV value stays in metres (step_m 0.5)");
        Check(MeasurementColumns.All.Last() is { Label: "Session", CsvHeader: "session_id" },
            "Labels are independent of CSV headers");
        var count = vm.Results.Count;
        vm.ToggleMeasurementCommand.Execute(null);
        Check(vm.State == MeasurementState.Idle && vm.Results.Count == count, "Stop keeps completed rows");
        vm.TriggerCommand.Execute(null); Check(vm.Results.Count == count, "Stopped trigger ignored");
        Check(vm.NewMeasurementCommand.CanExecute(null), "New Measurement available while stopped");
        vm.ToggleMeasurementCommand.Execute(null);
        Check(vm.SessionId == firstSession && vm.Results.Count == count && !vm.NewMeasurementCommand.CanExecute(null),
            "Stop then Start keeps the session and its rows");
        vm.TriggerCommand.Execute(null); fake.Complete(); await Until(() => vm.CanTrigger);
        count++;
        Check(vm.Results[^1] is { FromMm: 152000, ToMm: 152250, StepMm: 250 } && vm.NextFrom == "152.25",
            "Stop → Start keeps the next depth and step");
        Check(vm.Results.Count == count && vm.Results[^1].CaptureNumber == 5 && vm.Results[^1].Number == count &&
            vm.Results.Select(r => r.CaptureNumber).Take(3).SequenceEqual(new[] {1,3,4}), "Restart continues after the stored rows");
        await Until(() => store.Rows(firstSession).Select(r => r.CaptureNumber).SequenceEqual(new[] {1,3,4,5}));
        Check(store.Rows(firstSession).Count == count, "No duplicates after Stop/Start");
        vm.TriggerCommand.Execute(null);
        vm.ToggleMeasurementCommand.Execute(null);
        Check(vm.State == MeasurementState.Idle && vm.Countdown == "00:05" && vm.CaptureProgress == 0, "Stop immediately resets active capture");
        vm.ToggleMeasurementCommand.Execute(null); await Until(() => vm.CanTrigger);
        Check(vm.Results.Count == count, "Cancelled capture creates no row after rapid rearm");
        vm.TriggerCommand.Execute(null); fake.Complete(); await Until(() => vm.State == MeasurementState.Processing);
        vm.ToggleMeasurementCommand.Execute(null); vm.ToggleMeasurementCommand.Execute(null); await Until(() => vm.CanTrigger);
        Check(vm.Results.Count == count, "Stop in Processing prevents row commit");
        vm.TriggerCommand.Execute(null); fake.Fail(); await Until(() => vm.CanTrigger);
        Check(vm.Results.Count == count && vm.Countdown == "00:05" && Toasted(vm.Toasts, ToastKind.Error, "Capture failed"), "Failure recovers Ready without row");
        Check(vm.NextFrom == "152.25", "Cancelled and failed captures do not advance the depth");
        vm.TriggerCommand.Execute(null); vm.NextFrom = "160"; fake.Complete(); await Until(() => vm.CanTrigger);
        count++;
        Check(vm.Results[^1] is { FromMm: 152250, ToMm: 152500 } && vm.NextFrom == "160", "A manual starting depth applies to the next capture");
        vm.TriggerCommand.Execute(null); fake.Complete(); await Until(() => vm.CanTrigger);
        count++;
        Check(vm.Results[^1] is { FromMm: 160000, ToMm: 160250 } && vm.NextFrom == "160.25", "Next capture starts at the manual depth");
        vm.SetCore(null); Check(!vm.ToggleMeasurementCommand.CanExecute(null) && vm.State == MeasurementState.Idle, "Editing invalidates saved form and disarms");
        Check(vm.SessionId == firstSession && vm.Results.Count == count, "Disarming keeps the session");
        await Until(() => store.Rows(firstSession).Count == count);
        vm.NewMeasurementCommand.Execute(null);
        Check(vm.SessionId is null && vm.Results.Count == 0 && vm.NeedsCore && store.Rows(firstSession).Count == count,
            "New Measurement closes the drillhole; its rows stay stored");
        vm.SetCore(core); await Until(() => vm.SessionId is not null);
        Check(vm.SessionId == firstSession && vm.Results.Count == count && vm.NextFrom == "160.25" && vm.Step == "25",
            "The same Drillhole ID reopens the same session with its progress");
        vm.NewMeasurementCommand.Execute(null);
        var core2 = core with { DrillholeId = "DH-002" };
        vm.SetCore(core2); await Until(() => vm.SessionId is not null);
        var secondSession = vm.SessionId!.Value;
        Check(secondSession != firstSession && vm.Results.Count == 0 && vm.NextFrom == "" && vm.Step == "",
            "Another Drillhole ID gets its own session with its own (empty) progress");
        vm.NextFrom = "10"; vm.Step = "50"; vm.ToggleMeasurementCommand.Execute(null);
        vm.TriggerCommand.Execute(null); fake.Complete(); await Until(() => vm.CanTrigger);
        Check(vm.Results.Single() is { CaptureNumber: 1, FromMm: 10000, ToMm: 10500 } && vm.Results[0].SessionId == secondSession,
            "New session numbers from 1 with its own depths");
        await Until(() => store.Rows(secondSession).Count == 1);
        Check(store.Rows(firstSession).Count == count && store.Progress(firstSession) == new DepthProgress(160250, 250),
            "Sessions and their progress stay isolated");
        vm.ToggleMeasurementCommand.Execute(null);

        // A future service may return or report progress after cancellation. Ignore it and prevent overlap.
        var late = new ControlledMeasurements { HonorCancellation = false };
        using (var lateVm = new MeasurementViewModel(late, export, dialogs, new MemoryRepository()))
        {
            lateVm.SetCore(core); lateVm.NextFrom = "150"; lateVm.Step = "50"; lateVm.ToggleMeasurementCommand.Execute(null); lateVm.TriggerCommand.Execute(null);
            lateVm.ToggleMeasurementCommand.Execute(null); lateVm.ToggleMeasurementCommand.Execute(null);
            late.Report(TimeSpan.FromSeconds(1)); lateVm.TriggerCommand.Execute(null);
            Check(!lateVm.CanTrigger && late.Calls == 1 && lateVm.Countdown == "00:05" && lateVm.CaptureProgress == 0,
                "Late progress and rearm cannot overlap cancellation cleanup");
            late.Complete(); await Until(() => lateVm.CanTrigger);
            Check(lateVm.Results.Count == 0, "Late cancelled result discarded");
        }
        var mainBackend = new StubBackend();
        var bluetoothChecks = 0;
        var mainDevices = DeviceSettings.Default with { CheckInterval = TimeSpan.FromMinutes(5), BluetoothCheckInterval = TimeSpan.FromMilliseconds(30) };
        var mainBtConnected = true;
        var mainBtFails = false;
        var mainCapture = new ControlledMeasurements();
        IReadOnlyList<KnownBluetoothDevice> MainBluetooth()
        {
            Interlocked.Increment(ref bluetoothChecks);
            if (mainBtFails)
            {
                throw new InvalidOperationException("Bluetooth is turned off.");
            }

            return [new KnownBluetoothDevice("CT007-M-A1", 0x001122AABBCC, mainBtConnected, true)];
        }
        using (var main = new MainViewModel(new MemoryRepository(), settings with { StatusInterval = TimeSpan.FromMilliseconds(30) }, mainDevices, mainBackend,
                   MainBluetooth, mainCapture))
        {
            Check(main.CurrentPage == main.Core && main.IsCoreSelected && !main.IsMeasurementSelected, "Sidebar: Core Information selected at start");
            await Until(() => Ids(main.Core).Length == 3 && main.Backend.State == BackendState.Online && main.Devices.BluetoothDevices.Count == 1);
            await Task.Delay(100);
            Check(main.Devices.Bluetooth.State == LocalState.NotSelected && bluetoothChecks <= 2, "App: no device selected and no connection checks until the user selects one");
            main.Core.Devices.SelectedBluetoothDevice = main.Core.Devices.BluetoothDevices[0];
            await Until(() => main.Devices.Bluetooth.State == LocalState.Connected);
            Check(main.Core.Devices == main.Devices && main.Devices.Bluetooth.Detail == "CT007-M-A1" &&
                Toasted(main.Toasts, ToastKind.Success, "CT007-M-A1 (Bluetooth) connected."), "App: the selected device is monitored and its first state announced");
            await Pick(main.Core, "GTZO_0002_1"); main.Core.Notes = "retained";
            for (int i = 0; i < 5; i++)
            {
                main.ShowMeasurementCommand.Execute(null);
                Check(main.IsMeasurementSelected && !main.IsCoreSelected, "Sidebar: exactly Measurement selected");
                main.ShowCoreCommand.Execute(null);
                Check(main.IsCoreSelected && !main.IsMeasurementSelected, "Sidebar: exactly Core Information selected");
            }
            Check(main.CurrentPage == main.Core && main.Core.DrillholeId == "GTZO_0002_1" && main.Core.Notes == "retained", "Navigation retains form");
            var before = mainBackend.Calls("/");
            var localBefore = bluetoothChecks;
            await Task.Delay(330);
            var calls = mainBackend.Calls("/") - before;
            var localCalls = bluetoothChecks - localBefore;
            Check(calls is >= 3 and <= 13 && localCalls is >= 3 and <= 13 && mainBackend.Calls(DrillholesClient.Path) == 1,
                $"Navigating does not multiply polling ({calls} backend probes, {localCalls} local checks in 330 ms) or reload drillholes");
            mainBackend.Offline = true;
            await Until(() => main.Backend.State == BackendState.Offline);
            await Task.Delay(100);
            Check(main.Core.DrillholeId == "GTZO_0002_1" && main.Devices.Bluetooth.State == LocalState.Connected &&
                main.Devices.MeasurementDatabase.State == LocalState.Available, "Offline keeps the form; local Device Status keeps working");
            mainBackend.Offline = false;
            await Until(() => main.Backend.State == BackendState.Online);
            var retainedCore = main.Core;
            var retainedMeasurement = main.Measurement;
            Check(main.IsSidebarExpanded, "Sidebar initially expanded");
            main.ToggleSidebarCommand.Execute(null);
            Check(!main.IsSidebarExpanded && main.SidebarToggleLabel == "Expand sidebar", "Collapse sidebar");
            Check(main.CurrentPage == retainedCore && main.Core.Notes == "retained", "Collapse retains page/form");
            main.ShowMeasurementCommand.Execute(null);
            Check(!main.IsSidebarExpanded && main.CurrentPage == retainedMeasurement && main.IsMeasurementSelected,
                "Navigation retains collapsed state and measurement instance");
            main.ToggleSidebarCommand.Execute(null);
            Check(main.IsSidebarExpanded && main.SidebarToggleLabel == "Collapse sidebar" && main.CurrentPage == retainedMeasurement &&
                main.IsMeasurementSelected, "Expand retains selected page");
            main.ShowCoreCommand.Execute(null);
            main.Core.SaveCommand.Execute(null);   // Save & Continue navigates outside the sidebar
            Check(main.CurrentPage == retainedMeasurement && main.IsMeasurementSelected && !main.IsCoreSelected,
                "Sidebar follows navigation triggered outside it (Save & Continue)");
            // Bluetooth monitoring is informational: a failed check does not block Start, the trigger or the countdown.
            mainBtFails = true;
            await Until(() => main.Devices.Bluetooth.State == LocalState.Unavailable);
            main.Measurement.SetCore(core); main.Measurement.NextFrom = "150"; main.Measurement.Step = "50"; main.Measurement.ToggleMeasurementCommand.Execute(null);
            Check(main.Measurement.CanTrigger, "Start arms while the Bluetooth status is unavailable");
            main.Measurement.TriggerCommand.Execute(null); main.ShowCoreCommand.Execute(null);
            var navigationSession = main.Measurement.SessionId;
            main.ShowMeasurementCommand.Execute(null); main.ShowCoreCommand.Execute(null);
            Check(main.Measurement.IsCapturing && main.CurrentPage == retainedCore &&
                main.Measurement.SessionId == navigationSession, "Navigation does not duplicate, reset or start a session");
            mainBtFails = false; mainBtConnected = false;
            await Until(() => main.Devices.Bluetooth.State == LocalState.Disconnected);
            Check(main.Measurement.IsCapturing && Toasted(main.Toasts, ToastKind.Warning, "CT007-M-A1 (Bluetooth) disconnected."),
                "A disconnect during a capture is announced but does not cancel it");
            mainCapture.Complete();
            await Until(() => main.Measurement.Results.Count == 1 && main.Measurement.CanTrigger);
            Check(main.Measurement.NextFrom == "150.50" && main.Measurement.ExportCommand.CanExecute(null),
                "With the device disconnected the capture completes, depth advances once and export stays available");
            main.Measurement.ToggleMeasurementCommand.Execute(null);
        }
        var closingService = new ControlledMeasurements();
        var closingVm = new MeasurementViewModel(closingService, export, dialogs, new MemoryRepository()); closingVm.SetCore(core); closingVm.NextFrom = "150"; closingVm.Step = "50";
        closingVm.ToggleMeasurementCommand.Execute(null); closingVm.TriggerCommand.Execute(null); closingVm.Dispose(); await Until(() => !closingVm.IsCapturing);
        Check(closingVm.Results.Count == 0, "Closing cancels without partial result");

        // Failed saves are reported and the rows stay in the table.
        var failing = new MemoryRepository();
        var failingService = new ControlledMeasurements();
        using (var failingVm = new MeasurementViewModel(failingService, export, dialogs, failing))
        {
            failingVm.SetCore(core); failingVm.NextFrom = "150"; failingVm.Step = "50";
            await Until(() => failing.Progress(failingVm.SessionId!.Value) is not null);
            failing.Fail = true;
            failingVm.ToggleMeasurementCommand.Execute(null);
            failingVm.TriggerCommand.Execute(null); failingService.Complete();
            await Until(() => Toasted(failingVm.Toasts, ToastKind.Error, "Saving to the database failed"));
            Check(failingVm.Results.Count == 1, "Save failure keeps the captured row");
            failingVm.ToggleMeasurementCommand.Execute(null);
            failingVm.NewMeasurementCommand.Execute(null);
            Check(failingVm.Results.Count == 1, "New Measurement refuses to drop unsaved rows");
            failing.Fail = false;
            failingVm.ToggleMeasurementCommand.Execute(null);   // Start
            failingVm.ToggleMeasurementCommand.Execute(null);   // Stop retries the failed save
            await Until(() => failing.Rows(failingVm.SessionId!.Value).Count == 1);
            failingVm.ToggleMeasurementCommand.Execute(null);   // Start again
            failingVm.TriggerCommand.Execute(null); failingService.Complete(); await Until(() => failingVm.CanTrigger);
            var id = failingVm.SessionId!.Value;
            await Until(() => failing.Rows(id).Count == 2);
        }

        // DEMO ts_ms: random preview values for rows without a device value, kept in memory only.
        var demoService = new ControlledMeasurements();
        using (var demoVm = new MeasurementViewModel(demoService, export, dialogs, new MemoryRepository(), showDemoTsMs: true))
        {
            demoVm.SetCore(core with { DrillholeId = "DEMO-1" }); demoVm.NextFrom = "10"; demoVm.Step = "50";
            demoVm.ToggleMeasurementCommand.Execute(null);
            demoVm.TriggerCommand.Execute(null); demoService.Complete(); await Until(() => demoVm.CanTrigger);
            demoVm.TriggerCommand.Execute(null); demoVm.Step = "25"; demoService.Complete(); await Until(() => demoVm.CanTrigger);
            demoVm.TriggerCommand.Execute(null); demoService.Complete(); await Until(() => demoVm.CanTrigger);
            demoService.Reading = new DeviceReading(TsMs: 4321);
            demoVm.TriggerCommand.Execute(null); demoService.Complete(); await Until(() => demoVm.CanTrigger);
            var demoRows = demoVm.Results.ToList();
            var shown = demoRows.Select(r => r.DisplayTsMs).ToList();
            Check(demoVm.ShowDemoTsMs && demoRows.Take(3).All(r => r.IsDemoTsMs && r.Reading.TsMs is null && r.DisplayTsMs is >= 500 and <= 5000),
                "Demo ts_ms generated for rows without a device value");
            Check(demoRows[3] is { IsDemoTsMs: false, DemoTsMs: null, DisplayTsMs: 4321 }, "A device ts_ms is shown as is, never replaced by a demo value");
            Check(demoRows.All(r => Equals(Resolve(MeasurementColumns.TsMs.Binding, r), r.DisplayTsMs)),
                "Table ts_ms column shows the same value as the chart bar height (DisplayTsMs)");
            Check(demoRows.Select(r => (r.FromMetres, r.ToMetres, r.StepMetres)).SequenceEqual(new (decimal?, decimal?, decimal?)[]
                { (10.0m, 10.5m, 0.5m), (10.5m, 11.0m, 0.5m), (11.0m, 11.25m, 0.25m), (11.25m, 11.5m, 0.25m) }),
                "Bar edges are from/to; a step change makes the following bars narrower");
            Check(demoVm.Results.Select(r => r.DisplayTsMs).SequenceEqual(shown), "Demo values are generated once per row");
            var demoCsv = CsvExportService.Serialize(demoVm.Results.ToArray(), new MeasurementSession(demoVm.SessionId!.Value, DateTimeOffset.Now));
            var tsColumn = Array.IndexOf(demoCsv.Split("\r\n")[0].Split(','), "\"ts_ms\"");
            var tsCells = demoCsv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(l => l.Split(',')[tsColumn]).ToList();
            Check(tsCells.SequenceEqual(new[] {"\"\"", "\"\"", "\"\"", "\"4321\""}), "CSV exports the device ts_ms only, never demo values");
        }
        using (var plain = new MeasurementViewModel(new ControlledMeasurements(), export, dialogs, new MemoryRepository()))
        {
            Check(!plain.ShowDemoTsMs, "Demo mode is off unless enabled");
        }

        // A failed delete puts the row back in its place, so the table matches the stored data.
        var unreliable = new MemoryRepository();
        var unreliableService = new ControlledMeasurements();
        using (var rollbackVm = new MeasurementViewModel(unreliableService, export, dialogs, unreliable))
        {
            rollbackVm.SetCore(core with { DrillholeId = "ROLLBACK-1" }); rollbackVm.NextFrom = "0"; rollbackVm.Step = "100";
            rollbackVm.ToggleMeasurementCommand.Execute(null);
            for (int i=0; i<3; i++) { rollbackVm.TriggerCommand.Execute(null); unreliableService.Complete(); await Until(() => rollbackVm.CanTrigger); }
            var id = rollbackVm.SessionId!.Value;
            await Until(() => unreliable.Rows(id).Count == 3);
            var original = rollbackVm.Results.ToList();
            unreliable.Fail = true;
            dialogs.Confirm = true; rollbackVm.DeleteCommand.Execute(original[1]);
            await Until(() => Toasted(rollbackVm.Toasts, ToastKind.Error, "Deleting the measurement failed"));
            Check(!Toasted(rollbackVm.Toasts, ToastKind.Success, "Measurement deleted"), "No success toast when the delete failed in storage");
            Check(rollbackVm.Results.SequenceEqual(original) && original.Select(r => r.Number).SequenceEqual(new[] {1,2,3}) &&
                unreliable.Rows(id).Count == 3, "Failed delete shows the still-stored row again in its place");
            unreliable.Fail = false;
            rollbackVm.DeleteCommand.Execute(original[1]);
            await Until(() => unreliable.Rows(id).Count == 2);
            Check(rollbackVm.Results.SequenceEqual(new[] {original[0], original[2]}) && original[2].FromMm == 2000 && original[2].Number == 2,
                "Confirmed delete removes the row; other rows keep their values and depths");
            rollbackVm.ToggleMeasurementCommand.Execute(null);
        }

        await DrillholeListChecks(core, settings);
        await AutofillAndDepthInputChecks(core, settings);
        await MeasurementUnitsAndOrderChecks(core);
        await LocalDeviceStatusChecks();
        await DropdownPerformanceChecks(core, settings);
        await ToastChecks(settings);
        await DrillholeSelectionChecks(core, settings);
        await CaptureDurationChecks(core);
        await DeviceCaptureChecks(core);
        await ProgressMigrationChecks(core);
        await SqliteChecks(core);
    }

    /// <summary>A Gamma Guard database with the supplied schema, written by a separate connection (like the device software).</summary>
    private sealed class FakeGammaGuard : IDisposable
    {
        public string Path { get; } = TempDb("gammaguard");
        private readonly SqliteConnection writer;
        public FakeGammaGuard()
        {
            writer = new SqliteConnection($"Data Source={Path}");
            writer.Open();
            Scalar(writer, """
                CREATE TABLE samples (id INTEGER PRIMARY KEY AUTOINCREMENT, serial TEXT, session_id INTEGER, seq INTEGER, ts_ms INTEGER,
                    count0 REAL, count0_raw REAL, hdm REAL, hdm_raw REAL, dose_rate REAL, temperature REAL, pressure REAL)
                """);
            Scalar(writer, """
                CREATE TABLE detector_status (serial TEXT, name TEXT, mac TEXT, firmware TEXT, device_type TEXT, session_id INTEGER,
                    state TEXT, response_time REAL, last_sample_ms INTEGER, updated_ms INTEGER)
                """);
        }
        public void Detector(string serial, string name, string mac, long updatedMs) =>
            Scalar(writer, $"INSERT INTO detector_status VALUES ('{serial}', '{name}', '{mac}', '2.1.0', 'CT007-M', 41, 'connected', 0.2, {updatedMs}, {updatedMs})");
        public void Sample(string serial, long seq, long? ts, double? count0 = null, double? hdm = null, double? temperature = 21.5, long session = 41)
        {
            string N(double? v) => v is { } x ? x.ToString(CultureInfo.InvariantCulture) : "NULL";
            Scalar(writer, $"INSERT INTO samples (serial, session_id, seq, ts_ms, count0, count0_raw, hdm, hdm_raw, dose_rate, temperature, pressure) " +
                $"VALUES ('{serial}', {session}, {seq}, {(ts is { } t ? t : "NULL")}, {N(count0)}, {N(count0 * 2)}, {N(hdm)}, {N(hdm)}, {N(hdm / 10)}, {N(temperature)}, 1013.25)");
        }
        public byte[] Bytes() => File.ReadAllBytes(Path);
        public void Dispose() => writer.Dispose();
    }

    /// <summary>Test clock: waiting advances "now" and lets the fake device write what it would have written by then.</summary>
    private sealed class FakeCaptureClock(long start) : ICaptureClock
    {
        public long NowMs { get; private set; } = start;
        public Action<long>? Advanced;
        public bool Frozen;   // waits without time passing (to test Stop during a capture)
        public async Task Delay(TimeSpan delay, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (Frozen) { await Task.Delay(20, token); return; }
            await Task.Yield();
            NowMs += (long)delay.TotalMilliseconds;
            Advanced?.Invoke(NowMs);
        }
    }

    private sealed class FixedDevice(CaptureDevice? device, string? refusal = null) : ICaptureDeviceResolver
    {
        public CaptureDevice Resolve() => device ?? throw new CaptureException(refusal ?? "No device");
    }

    /// <summary>Real capture from the Gamma Guard samples table: window, device filter, averaging, saving and linking.</summary>
    private static async Task DeviceCaptureChecks(CoreSnapshot core)
    {
        const long T = 1_760_000_000_000;   // click time (Unix ms)
        var detector = new DetectorIdentity("SN-1001", "CT007-M-0001", "00:11:22:AA:BB:01", "2.1.0", "CT007-M");

        // Averaging: per field over valid values only; null stays null; ids and timestamps are not averaged.
        var summary = SampleAveraging.Summarize(new[]
        {
            new DeviceSample(10, "SN", "41", 1, T + 1000, 10, null, 1, 2, null, 20, double.NaN),
            new DeviceSample(11, "SN", "41", 2, T + 2000, null, null, 3, 4, null, 22, 1000),
            new DeviceSample(12, "SN", "41", 3, T + 3000, 40, null, 5, 6, null, null, 1002)
        });
        Check(summary is { SampleCount: 3, FirstTsMs: T + 1000, Count0: 25, Count0Raw: null, Hdm: 3, HdmRaw: 4, DoseRate: null, Temperature: 21, Pressure: 1001, DeviceSessionId: "41" },
            "Means use each field's valid values (count0 (10+40)/2 = 25, not /3); all-null fields stay null; NaN ignored; first ts_ms kept, not averaged");
        Check(DeviceTime.Problem(1_760_000_000, T) is { } basisProblem && basisProblem.Contains("not in Unix milliseconds") &&
            DeviceTime.Problem(T - 3_600_000, T) is null && DeviceTime.Problem(T - 86_400_000, T) is not null, "ts_ms time basis: ms checked; seconds or a far-off clock refused");

        // Window per duration: trigger < ts <= trigger + duration; one sample per second; boundaries.
        foreach (var seconds in new[] {2, 5, 10})
        {
            using var gg = new FakeGammaGuard();
            gg.Sample("SN-1001", 1, T - 500, 99);         // before the click
            gg.Sample("SN-1001", 2, T, 99);               // exactly at the click
            for (var i = 1; i <= 12; i++) gg.Sample("SN-1001", 2 + i, T + i * 1000, i);
            gg.Sample("SN-1001", 50, T + seconds * 1000 + 1, 99);   // one ms after the window
            var untouched = gg.Bytes();
            var clock = new FakeCaptureClock(T);
            var service = new DeviceCaptureService(new FixedDevice(new CaptureDevice(detector, new GammaGuardDeviceDatabase(gg.Path))), clock);
            var data = await service.CaptureAsync(DateTimeOffset.FromUnixTimeMilliseconds(T), TimeSpan.FromSeconds(seconds), _ => { }, CancellationToken.None);
            Check(gg.Bytes().SequenceEqual(untouched) && !File.Exists(gg.Path + "-wal") && !File.Exists(gg.Path + "-journal"),
                "Source database unchanged by a capture (byte-identical, no journal created)");
            Check(data.DeviceSamples!.Count == seconds && data.ExpectedSamples == seconds &&
                data.DeviceSamples.First().TsMs == T + 1000 && data.DeviceSamples.Last().TsMs == T + seconds * 1000 &&
                data.EndTime - data.StartTime == TimeSpan.FromSeconds(seconds),
                $"{seconds} s window: samples after the click up to and including +{seconds} s; the click itself and later rows excluded");
        }

        // Device filter, late writes, duplicates, nulls, a missing second — written while the capture runs.
        using (var gg = new FakeGammaGuard())
        {
            for (var i = 0; i < 300; i++) gg.Sample("SN-1001", i, T - 400_000 + i * 1000, 7);   // history before the click
            var clock = new FakeCaptureClock(T);
            var planned = new List<(long WriteAt, Action Write)>
            {
                (T + 1100, () => gg.Sample("SN-1001", 401, T + 1000, 10, 1)),
                (T + 1150, () => gg.Sample("SN-2002", 900, T + 1100, 500, 50)),            // another detector
                (T + 2100, () => gg.Sample("SN-1001", 402, T + 2000, null, 3)),           // count0 missing
                (T + 2150, () => gg.Sample("SN-1001", 402, T + 2000, null, 3)),           // duplicate write of seq 402
                (T + 3100, () => gg.Sample("SN-1001", 403, null, 1000, 1000)),            // no timestamp
                // seq 404 (T+4000) never arrives
                (T + 5900, () => gg.Sample("SN-1001", 405, T + 5000, 40, 5)),             // written late, inside the window
                (T + 9000, () => gg.Sample("SN-1001", 406, T + 4500, 77, 77)),            // too late: after the final read
            };
            clock.Advanced = now =>
            {
                foreach (var p in planned.Where(p => p.WriteAt <= now).ToList()) { p.Write(); planned.Remove(p); }
            };
            var remaining = new List<TimeSpan>();
            var service = new DeviceCaptureService(new FixedDevice(new CaptureDevice(detector, new GammaGuardDeviceDatabase(gg.Path))), clock);
            var data = await service.CaptureAsync(DateTimeOffset.FromUnixTimeMilliseconds(T), TimeSpan.FromSeconds(5), remaining.Add, CancellationToken.None);
            var got = data.DeviceSamples!;
            Check(got.Select(s => s.Seq).SequenceEqual(new long?[] {401, 402, 405}) && got.All(s => s.Serial == "SN-1001"),
                "Only the selected detector's samples inside the window; duplicate and timestamp-less rows dropped; late write inside the window kept");
            Check(clock.NowMs <= T + 5000 + (long)DeviceCaptureService.FinalReadLimit.TotalMilliseconds + 250 && remaining[0] == TimeSpan.FromSeconds(5) && remaining.Last() == TimeSpan.Zero,
                "The final read is bounded (window end + 1.5 s); the countdown runs from the duration to zero");
            var s2 = SampleAveraging.Summarize(got);
            Check(s2 is { SampleCount: 3, Count0: 25, Hdm: 3 } && got.All(s => s.Temperature == 21.5 && s.Pressure == 1013.25 && s.DeviceSessionId == "41"),
                "Averages over the 3 received samples (count0 over its 2 values); temperature, pressure and device session copied");
        }

        // The ViewModel with the real capture: result row, raw samples saved and linked, retries, restart, Stop.
        var dbPath = TempDb("ct007-device");
        var toasts = new ToastService();
        using (var gg = new FakeGammaGuard())
        {
            var clock = new FakeCaptureClock(DateTimeOffset.Now.ToUnixTimeMilliseconds());
            long click = 0;
            var writes = new List<(long At, long Seq, double Count0)>();
            clock.Advanced = now =>
            {
                foreach (var w in writes.Where(w => click + w.At <= now).ToList()) { gg.Sample("SN-1001", w.Seq, click + w.At, w.Count0, 2); writes.Remove(w); }
            };
            gg.Sample("SN-1001", 1, clock.NowMs - 2000, 999);   // newest sample before the click (time basis check)
            var source = new GammaGuardDeviceDatabase(gg.Path);
            var service = new DeviceCaptureService(new FixedDevice(new CaptureDevice(detector, source)), clock);
            var repository = new SqliteMeasurementRepository(dbPath);
            Guid sessionId;
            using (var vm = new MeasurementViewModel(service, new FakeExport(), new FakeDialogs(), repository, toasts: toasts,
                       durationSettings: new CaptureDurationSettings(null)))
            {
                vm.SetCore(core with { DrillholeId = "DEV-1" }); await Until(() => vm.SessionId is not null);
                sessionId = vm.SessionId!.Value;
                vm.NextFrom = "10"; vm.Step = "25"; vm.ToggleMeasurementCommand.Execute(null);

                // Complete capture: 5 of 5 samples.
                click = clock.NowMs;
                writes.AddRange(new[] { (1000L, 11L, 10.0), (2000L, 12L, 20.0), (3000L, 13L, 30.0), (4000L, 14L, 40.0), (5000L, 15L, 50.0) });
                vm.TriggerCommand.Execute(DateTimeOffset.FromUnixTimeMilliseconds(click));
                vm.TriggerCommand.Execute(DateTimeOffset.FromUnixTimeMilliseconds(click + 10));   // ignored: capture running
                await Until(() => vm.Results.Count == 1 && vm.CanTrigger);
                var row = vm.Results[0];
                Check(row is { SampleCount: 5, ExpectedSamples: 5, IsIncomplete: false, DurationMs: 5000, DeviceSerial: "SN-1001", DeviceName: "CT007-M-0001", DeviceSessionId: "41",
                        Temperature: 21.5, Pressure: 1013.25, FromMm: 10000, ToMm: 10250 } &&
                    row.Reading is { Count0: 30, Count0Raw: 60, Hdm: 2, TsMs: var firstTs } && firstTs == click + 1000 && !row.IsDemoTsMs &&
                    row.StartTime.ToUnixTimeMilliseconds() == click && vm.NextFrom == "10.25",
                    "Result: averages of the 5 samples, actual count, window, device, depth interval; extra click ignored; depth advanced once");

                // Incomplete capture: 3 of 5 — averaged over 3, marked, warning toast.
                await Until(() => !vm.IsCapturing);
                click = clock.NowMs + 7;
                writes.AddRange(new[] { (1000L, 21L, 1.0), (2000L, 22L, 2.0), (5000L, 25L, 6.0) });
                vm.TriggerCommand.Execute(DateTimeOffset.FromUnixTimeMilliseconds(click));
                await Until(() => vm.Results.Count == 2 && vm.CanTrigger);
                var partial = vm.Results.Single(r => r.CaptureNumber == 2);
                Check(partial is { SampleCount: 3, ExpectedSamples: 5, IsIncomplete: true, SampleDisplay: "3 of 5" } && partial.Reading.Count0 == 3 &&
                    Toasted(toasts, ToastKind.Warning, "Incomplete capture: 3 of 5 samples received"), "Incomplete capture: average of the 3 received (3, not /5), marked 3 of 5, warning toast");

                // No samples: error toast, no row, no depth advance.
                var depth = vm.NextFrom;
                click = clock.NowMs + 7;
                vm.TriggerCommand.Execute(DateTimeOffset.FromUnixTimeMilliseconds(click));
                await Until(() => Toasted(toasts, ToastKind.Error, "No samples were received") && vm.CanTrigger);
                Check(vm.Results.Count == 2 && vm.NextFrom == depth, "No samples: error toast; nothing saved; depth not advanced");

                // Stop during a capture: cancelled, no row, no depth advance; completed rows kept.
                clock.Frozen = true;
                vm.TriggerCommand.Execute(DateTimeOffset.FromUnixTimeMilliseconds(clock.NowMs));
                await Until(() => vm.IsCapturing);
                await Task.Delay(60);
                vm.ToggleMeasurementCommand.Execute(null);
                await Task.Delay(100);
                Check(vm.Results.Count == 2 && vm.NextFrom == depth && !vm.IsCapturing, "Stop cancels the capture: no completed row, no depth advance, earlier rows kept");
                clock.Frozen = false;

                // Saving again (a later change) does not duplicate the record or its samples.
                await Task.Delay(100);
                vm.Step = "50"; vm.Step = "25";
                await Task.Delay(150);
            }

            var reopened = new SqliteMeasurementRepository(dbPath);
            var rows = await reopened.LoadAsync(sessionId);
            var raw = await reopened.LoadSamplesAsync(sessionId, 1);
            var raw2 = await reopened.LoadSamplesAsync(sessionId, 2);
            Check(rows.Count == 2 && rows.Single(r => r.CaptureNumber == 1) is { SampleCount: 5, ExpectedSamples: 5, DurationMs: 5000, DeviceSerial: "SN-1001", DeviceMac: "00:11:22:AA:BB:01" } &&
                rows.Single(r => r.CaptureNumber == 2) is { SampleCount: 3, ExpectedSamples: 5, IsIncomplete: true },
                "After restart: results with count, expected count, duration and device");
            Check(raw.Select(s => s.Seq).SequenceEqual(new long?[] {11, 12, 13, 14, 15}) && raw.All(s => s is { Serial: "SN-1001", DeviceSessionId: "41", Temperature: 21.5, Pressure: 1013.25 }) &&
                raw.Select(s => s.Count0).SequenceEqual(new double?[] {10, 20, 30, 40, 50}) && raw2.Count == 3,
                "Raw samples stored with source id, serial, device session, seq, ts_ms and every value; linked to their capture after restart");
            using (var check = new SqliteConnection($"Data Source={dbPath}"))
            {
                check.Open();
                Check(Scalar(check, "SELECT COUNT(*) FROM CaptureSamples") == "8" && Scalar(check, "SELECT COUNT(*) FROM MeasurementRecords") == "2" &&
                    Scalar(check, "SELECT COUNT(DISTINCT source_id) FROM CaptureSamples") == "8",
                    "Re-saving never duplicated records or samples; only accepted samples were copied (not the device history)");
                Check(Scalar(check, "SELECT session_id FROM MeasurementRecords WHERE CaptureNo = 1") == sessionId.ToString("D") &&
                    Scalar(check, "SELECT device_session_id FROM MeasurementRecords WHERE CaptureNo = 1") == "41",
                    "App session_id and the device's session id are separate columns");
            }
            await reopened.DeleteAsync(sessionId, 2);
            Check((await reopened.LoadSamplesAsync(sessionId, 2)).Count == 0 && (await reopened.LoadSamplesAsync(sessionId, 1)).Count == 5,
                "Deleting a capture deletes its raw samples (only its own)");
        }

        // Device resolution at the click comes from the Gamma Guard samples alone — never from Bluetooth monitoring.
        using (var gg = new FakeGammaGuard())
        {
            gg.Detector("SN-OLD", "CT007-M-0001", "00-11-22-aa-bb-01", 100);
            gg.Detector("SN-1001", "CT007-M-0001", "00:11:22:AA:BB:01", 200);
            gg.Detector("SN-1001", "CT007-M-0001 (old row)", "00:11:22:AA:BB:01", 150);
            gg.Sample("SN-OLD", 1, T - 600_000, 1);
            gg.Sample("SN-1001", 1, T - 1000, 1);
            var bluetooth = new List<KnownBluetoothDevice>();   // nothing paired or connected
            using var monitor = new DeviceStatusMonitor(new Ct007BluetoothCheck(DeviceSettings.Default, () => bluetooth.ToList()),
                new MeasurementDatabaseCheck(TempDb("x")), new GammaGuardDatabaseCheck(DeviceSettings.Default with { GammaGuardDatabasePath = gg.Path }, []),
                TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(1));
            await monitor.CheckAllAsync();
            var resolver = new GammaGuardCaptureDevices(monitor);
            Check(monitor.Bluetooth.State == LocalState.NotSelected && resolver.Resolve().Identity is { Serial: "SN-1001", Name: "CT007-M-0001" },
                "No Bluetooth selection or connection: the recording detector is still resolved (newest samples; newest detector_status row)");
            monitor.SelectBluetoothDevice(new BluetoothDeviceOption(0x001122AABB09, "CT007-M-0009"));
            await Until(() => monitor.Bluetooth.State == LocalState.Disconnected);
            Check(resolver.Resolve().Identity.Serial == "SN-1001", "A disconnected selected device neither blocks nor changes the data source");
            bluetooth.Add(new("CT007-M-0009", 0x001122AABB09, true, true));
            await monitor.CheckBluetoothAsync();
            await Until(() => monitor.Bluetooth.State == LocalState.Connected);
            Check(resolver.Resolve().Identity.Serial == "SN-1001", "Selecting/connecting another device does not switch the data source");
            string Refusal() { try { resolver.Resolve(); return ""; } catch (CaptureException e) { return e.Message; } }
            gg.Sample("SN-2002", 1, T - 500, 1);
            Check(Refusal().StartsWith("More than one detector is recording (SN-2002, SN-1001)"), "Two detectors recording at once: refused, never mixed");
            gg.Sample("SN-3003", 1, T + 60_000, 1);
            Check(resolver.Resolve().Identity is { Serial: "SN-3003", Name: null }, "A detector without detector_status row is used by serial (metadata stays empty)");
        }
        using (var empty = new FakeGammaGuard())
        {
            using var monitor = new DeviceStatusMonitor(new Ct007BluetoothCheck(DeviceSettings.Default, () => throw new InvalidOperationException("Bluetooth is turned off.")),
                new MeasurementDatabaseCheck(TempDb("x")), new GammaGuardDatabaseCheck(DeviceSettings.Default with { GammaGuardDatabasePath = empty.Path }, []),
                TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(1));
            await monitor.CheckAllAsync();
            try { new GammaGuardCaptureDevices(monitor).Resolve(); throw new Exception("empty database accepted"); }
            catch (CaptureException e) when (e.Message.StartsWith("The Gamma Guard database has no samples yet")) { }
        }

        // A device whose ts_ms is not in milliseconds is refused instead of mis-matched with the click.
        using (var gg = new FakeGammaGuard())
        {
            gg.Sample("SN-1001", 1, T / 1000, 5);
            var service = new DeviceCaptureService(new FixedDevice(new CaptureDevice(detector, new GammaGuardDeviceDatabase(gg.Path))), new FakeCaptureClock(T));
            try { await service.CaptureAsync(DateTimeOffset.FromUnixTimeMilliseconds(T), TimeSpan.FromSeconds(2), _ => { }, CancellationToken.None); throw new Exception("seconds accepted"); }
            catch (CaptureException e) when (e.Message.Contains("not in Unix milliseconds")) { }
        }
    }

    /// <summary>MeasurementProgress moves from millimetres to centimetres exactly once, keeping the physical depth.</summary>
    private static async Task ProgressMigrationChecks(CoreSnapshot core)
    {
        var path = TempDb("ct007-v4");
        var session = Guid.Parse("33333333-3333-3333-3333-333333333333");
        using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            connection.Open();
            Scalar(connection, """
                CREATE TABLE MeasurementSessions (id TEXT NOT NULL PRIMARY KEY, CreatedAt TEXT NOT NULL, DrillholeID TEXT, DrillholeName TEXT,
                    Project TEXT, "operator" TEXT, comment TEXT);
                """);
            Scalar(connection, """
                CREATE TABLE MeasurementRecords (id INTEGER PRIMARY KEY AUTOINCREMENT, session_id TEXT NOT NULL REFERENCES MeasurementSessions (id),
                    SortOrder INTEGER NOT NULL, CaptureNo INTEGER NOT NULL, StartTime TEXT NOT NULL, EndTime TEXT NOT NULL, Sample INTEGER NOT NULL,
                    TsMs INTEGER, Count0 REAL, Count0Raw REAL, Hdm REAL, HdmRaw REAL, DoseRate REAL, "from" INTEGER, "to" INTEGER, step INTEGER)
                """);
            Scalar(connection, "CREATE UNIQUE INDEX UX_MeasurementRecords_Session_CaptureNo ON MeasurementRecords (session_id, CaptureNo)");
            Scalar(connection, "CREATE UNIQUE INDEX UX_MeasurementSessions_DrillholeID ON MeasurementSessions (DrillholeID)");
            Scalar(connection, "CREATE TABLE MeasurementProgress (session_id TEXT NOT NULL PRIMARY KEY REFERENCES MeasurementSessions (id), next_from INTEGER NOT NULL, step INTEGER NOT NULL)");
            Scalar(connection, $"INSERT INTO MeasurementSessions VALUES ('{session}', '2026-10-11T11:16:10.8510000+08:00', 'MIG-1', 'P', 'C', 'Op', '')");
            Scalar(connection, $"INSERT INTO MeasurementRecords (session_id, SortOrder, CaptureNo, StartTime, EndTime, Sample, Count0, \"from\", \"to\", step) " +
                $"VALUES ('{session}', 1, 1, '2026-10-11T11:16:10.8510000+08:00', '2026-10-11T11:16:15.8510000+08:00', 5, 12.5, 151000, 151250, 250)");
            Scalar(connection, $"INSERT INTO MeasurementProgress VALUES ('{session}', 151255, 255)");
            Scalar(connection, "PRAGMA user_version = 4");
        }
        var repository = new SqliteMeasurementRepository(path);
        Check(await repository.LoadProgressAsync(session) == new DepthProgress(151255, 255), "v4 → v5: progress keeps its physical depth (151.255 m, 25.5 cm)");
        using (var check = new SqliteConnection($"Data Source={path}"))
        {
            check.Open();
            Check(Scalar(check, "SELECT next_from_cm || '/' || step_cm FROM MeasurementProgress") == "15125.5/25.5" &&
                Scalar(check, "SELECT COUNT(*) FROM pragma_table_info('MeasurementProgress') WHERE name IN ('next_from', 'step')") == "0" &&
                Scalar(check, "SELECT \"from\" || '-' || \"to\" || '/' || step FROM MeasurementRecords") == "151000-151250/250" &&
                Scalar(check, "PRAGMA user_version") == "5", "Progress stored as next_from_cm / step_cm (÷10 once); records stay in millimetres");
        }
        var again = new SqliteMeasurementRepository(path);
        Check(await again.LoadProgressAsync(session) == new DepthProgress(151255, 255), "Reopening does not convert again (no 100× error)");
        using (var vm = new MeasurementViewModel(new ControlledMeasurements(), new FakeExport(), new FakeDialogs(), again))
        {
            vm.SetCore(core with { DrillholeId = "MIG-1" }); await Until(() => vm.SessionId == session && vm.NextFrom == "151.255");
            Check(vm.NextFrom == "151.255" && vm.Step == "25.5" && vm.Results.Single() is { FromMm: 151000, StepMm: 250, DeviceSerial: null, Samples: null },
                "Starting Depth 151.255 m and Step 25.5 cm shown; old records keep their values with no device data invented");
            vm.NextFrom = "152"; vm.Step = "30";
        }
        await Task.Delay(100);
        Check(await new SqliteMeasurementRepository(path).LoadProgressAsync(session) == new DepthProgress(152000, 300), "Saved and restored after restart: 152 m / 30 cm");
    }

    /// <summary>Toast durations, de-duplication, dismissal and once-per-transition connection announcements.</summary>
    private static async Task ToastChecks(ApiSettings settings)
    {
        // Durations: success / information / warning 3 s, errors 5 s; identical toasts are not stacked; × dismisses.
        var waits = new List<(TimeSpan Duration, TaskCompletionSource Done)>();
        var toasts = new ToastService((duration, token) =>
        {
            var done = new TaskCompletionSource();
            token.Register(() => done.TrySetCanceled());
            waits.Add((duration, done));
            return done.Task;
        });
        toasts.Success("Measurement deleted."); toasts.Information("Backend online again."); toasts.Warning("Backend offline.");
        toasts.Error("Select a drillhole from the list.");
        Check(toasts.Items.Select(t => t.Duration.TotalSeconds).SequenceEqual(new[] {3.0, 3.0, 3.0, 5.0}) &&
            waits.Select(w => w.Duration.TotalSeconds).SequenceEqual(new[] {3.0, 3.0, 3.0, 5.0}), "Success/info/warning 3 s; errors 5 s");
        toasts.Error("Select a drillhole from the list."); toasts.Error("Select a drillhole from the list.");
        Check(toasts.Items.Count(t => t.Message == "Select a drillhole from the list.") == 1, "Repeated identical toasts are shown once");
        waits[0].Done.SetResult(); await Task.Yield(); await Task.Delay(10);
        Check(!toasts.Items.Any(t => t.Message == "Measurement deleted."), "A toast disappears after its duration");
        var warning = toasts.Items.Single(t => t.Kind == ToastKind.Warning);
        toasts.DismissCommand.Execute(warning);
        Check(!toasts.Items.Contains(warning), "× dismisses a toast");

        // Announcements: backend Online/Offline transitions and device changes, once each, never per check.
        var stub = new StubBackend();
        using var api = new ApiClient(settings, stub);
        using var backend = new BackendConnection(api, TimeSpan.FromMinutes(5));
        var bluetooth = new List<KnownBluetoothDevice> { new("CT007-M-A1", 1, true, true) };
        using var devices = new DeviceStatusMonitor(new Ct007BluetoothCheck(DeviceSettings.Default, () => bluetooth.ToList()),
            new MeasurementDatabaseCheck(TempDb("ct007-announce")), new GammaGuardDatabaseCheck(DeviceSettings.Default, []), TimeSpan.FromMinutes(5));
        var announced = new ToastService();
        using var announcements = new Frontend.ViewModels.StatusAnnouncements(backend, devices, announced);
        await backend.CheckNowAsync(); await devices.CheckAllAsync();
        Check(announced.Items.Count == 0, "Start-up results (backend online, device connected) are not announced");
        for (int i = 0; i < 3; i++) { await backend.CheckNowAsync(); await devices.CheckAllAsync(); }
        Check(announced.Items.Count == 0, "Repeated checks with the same state announce nothing");
        stub.Offline = true;
        for (int i = 0; i < 3; i++) await backend.CheckNowAsync();
        Check(announced.Items.Count == 1 && announced.Items[0] is { Kind: ToastKind.Warning, Message: "Backend offline. The saved drillhole list stays available." },
            "Backend offline announced once (3 s warning), not per check");
        stub.Offline = false;
        for (int i = 0; i < 3; i++) await backend.CheckNowAsync();
        Check(announced.Items.Count(t => t.Message == "Backend online again.") == 1, "Backend back online announced once");
        Check(announced.Items.Count == 2 && devices.Bluetooth.State == LocalState.NotSelected, "No Bluetooth selection: nothing checked, nothing announced");

        // Bluetooth toasts after a selection: the first confirmed state once, then each transition once. Every toast
        // that is shown is counted (the toasts expire at once here, so a repeat would be visible).
        var shown = new List<(ToastKind Kind, string Message)>();
        var btToasts = new ToastService((_, _) => Task.CompletedTask);
        btToasts.Items.CollectionChanged += (_, e) => { foreach (Toast t in e.NewItems ?? Array.Empty<Toast>()) shown.Add((t.Kind, t.Message)); };
        using var btAnnouncements = new Frontend.ViewModels.StatusAnnouncements(backend, devices, btToasts);
        var a1 = new BluetoothDeviceOption(1, "CT007-M-A1");
        devices.SelectBluetoothDevice(a1);
        await Until(() => devices.Bluetooth.State == LocalState.Connected);
        for (int i = 0; i < 3; i++) await devices.CheckBluetoothAsync();
        Check(shown.SequenceEqual(new[] { (ToastKind.Success, "CT007-M-A1 (Bluetooth) connected.") }) && ToastService.DurationOf(ToastKind.Success) == TimeSpan.FromSeconds(3),
            "After selection the first confirmed state is announced once (3 s); unchanged checks say nothing");
        bluetooth[0] = new KnownBluetoothDevice("CT007-M-A1", 1, false, true);
        for (int i = 0; i < 3; i++) await devices.CheckBluetoothAsync();
        Check(shown.Count(t => t == (ToastKind.Warning, "CT007-M-A1 (Bluetooth) disconnected.")) == 1 && devices.Bluetooth.Detail.Contains("paired with this laptop, not connected") &&
            !shown.Any(t => t.Message.StartsWith("Backend", StringComparison.Ordinal)),
            "Connected → Disconnected announced once (paired, not connected), named as the Bluetooth device, separate from the backend");
        bluetooth[0] = new KnownBluetoothDevice("CT007-M-A1", 1, true, true);
        for (int i = 0; i < 3; i++) await devices.CheckBluetoothAsync();
        Check(shown.Count(t => t.Message == "CT007-M-A1 (Bluetooth) connected.") == 2, "Disconnected → Connected announced once");
        var failure = new InvalidOperationException("Bluetooth is turned off.");
        var failing = true;
        using var flaky = new DeviceStatusMonitor(new Ct007BluetoothCheck(DeviceSettings.Default, () => failing ? throw failure : bluetooth.ToList()),
            new MeasurementDatabaseCheck(TempDb("ct007-flaky")), new GammaGuardDatabaseCheck(DeviceSettings.Default, []), TimeSpan.FromMinutes(5));
        var flakyShown = new List<(ToastKind Kind, string Message)>();
        var flakyToasts = new ToastService((_, _) => Task.CompletedTask);
        flakyToasts.Items.CollectionChanged += (_, e) => { foreach (Toast t in e.NewItems ?? Array.Empty<Toast>()) flakyShown.Add((t.Kind, t.Message)); };
        using var flakyAnnouncements = new Frontend.ViewModels.StatusAnnouncements(backend, flaky, flakyToasts);
        flaky.SelectBluetoothDevice(a1);
        await Until(() => flaky.Bluetooth.State == LocalState.Unavailable);
        for (int i = 0; i < 3; i++) await flaky.CheckBluetoothAsync();
        Check(flaky.Bluetooth is { Text: "Status unavailable", Detail: "Bluetooth is turned off." } &&
            flakyShown.SequenceEqual(new[] { (ToastKind.Error, "CT007-M-A1 (Bluetooth): status unavailable — Bluetooth is turned off.") }) &&
            ToastService.DurationOf(ToastKind.Error) == TimeSpan.FromSeconds(5),
            "A failed check is Status unavailable (never Disconnected), announced once as a 5 s error");
        failing = false;
        await flaky.CheckBluetoothAsync();
        await Until(() => flaky.Bluetooth.State == LocalState.Connected);
        Check(flakyShown.Last() == (ToastKind.Success, "CT007-M-A1 (Bluetooth) connected.") && flakyShown.Count == 2, "Recovery announced once");
    }

    /// <summary>Clear button, closest match, selection lock, and the Measurement page following the selected drillhole.</summary>
    private static async Task DrillholeSelectionChecks(CoreSnapshot core, ApiSettings settings)
    {
        var stub = new StubBackend
        {
            DrillholesJson = """
                [{"holeId":"AB-10","projectCode":"P1","campaign":"2020"},{"holeId":"XAB-1","projectCode":"P2","campaign":"2021"},
                 {"holeId":"AB-1","projectCode":"P3","campaign":"2022"},{"holeId":"AB-100","projectCode":"P4","campaign":"2023"},
                 {"holeId":"ZZ-9","projectCode":"P5","campaign":"2024"}]
                """
        };
        using var api = new ApiClient(settings, stub);
        using var backend = new BackendConnection(api, TimeSpan.FromMinutes(5));
        var cachePath = TempDb("ct007-select");
        var store = new MemoryRepository();
        var toasts = new ToastService();
        var form = new CoreInformationViewModel(_ => { }, cachePath, new DrillholesClient(api), backend, new DrillholeCache(cachePath), store,
            NewDevices(cachePath), SearchDelay, toasts);
        await form.LoadDrillholesAsync();

        // Closest match: exact, then prefix, then partial; typed text is never replaced; nothing picked while typing.
        await Search(form, "ab-1");
        Check(Ids(form).SequenceEqual(new[] {"AB-1", "AB-10", "AB-100", "XAB-1"}) && form.ClosestOption?.HoleId == "AB-1" &&
            form is { DrillholeId: "", DrillholeName: "", DrillholeSearch: "ab-1" }, "Exact first, then prefixes, then partial; closest highlighted, not picked");
        await Search(form, "ab-10");
        Check(form.ClosestOption?.HoleId == "AB-10" && Ids(form).SequenceEqual(new[] {"AB-10", "AB-100"}), "Exact AB-10 is closest");
        await Search(form, "b-10");
        Check(form.ClosestOption?.HoleId == "AB-10" && form.DrillholeId == "", "Partial only: the first partial match is closest");
        await Search(form, "zz");
        Check(form.ClosestOption?.HoleId == "ZZ-9" && form.CommitClosest() &&
            form is { DrillholeId: "ZZ-9", DrillholeName: "P5", ProjectArea: "2024", DrillholeSearch: "ZZ-9" }, "Enter picks the closest; Project and Campaign filled");
        await form.WhenOptionsUpdated();
        Check(Ids(form).Length == 5 && form.ClosestOption is null, "After picking, the full list is back and nothing is highlighted");

        // The Measurement page follows the selection (wired as in MainViewModel).
        var service = new ControlledMeasurements();
        using var vm = new MeasurementViewModel(service, new FakeExport(), new FakeDialogs { Confirm = true }, store, toasts: toasts);
        form.DrillholeChanged += vm.SelectDrillhole;
        form.Edited += () => vm.SetCore(null);
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(MeasurementViewModel.IsMeasurementModeActive)) form.IsSelectionLocked = vm.IsMeasurementModeActive; };
        CoreSnapshot Saved() => new(form.DrillholeId, form.DrillholeName, form.ProjectArea, form.Operator, form.Notes);

        // Measure ZZ-9 (two rows), then measure AB-1 (one row).
        vm.SetCore(Saved()); await Until(() => vm.SessionId is not null);
        var zzSession = vm.SessionId!.Value;
        vm.NextFrom = "10"; vm.Step = "50"; vm.ToggleMeasurementCommand.Execute(null);
        for (int i = 0; i < 2; i++) { vm.TriggerCommand.Execute(null); service.Complete(); await Until(() => vm.CanTrigger); }

        // Locked while measuring: no typing, picking or clearing until Stop.
        Check(form.IsSelectionLocked && !form.CanClearDrillhole && !form.ClearDrillholeCommand.CanExecute(null), "While measuring the drillhole is locked; × hidden");
        form.DrillholeSearch = "AB";
        form.ClearDrillholeCommand.Execute(null);
        Check(form is { DrillholeId: "ZZ-9", DrillholeSearch: "ZZ-9" } && vm.SessionId == zzSession && vm.IsMeasurementModeActive &&
            Toasted(toasts, ToastKind.Error, "Stop the measurement before changing the drillhole."), "Changing or clearing while measuring is refused with an error toast");
        vm.ToggleMeasurementCommand.Execute(null);
        Check(!form.IsSelectionLocked && form.CanClearDrillhole, "Stop unlocks the drillhole");

        // × clears the drillhole: Project/Campaign empty, Operator and Notes kept, Measurement page empty, data kept.
        form.Operator = "Op"; form.Notes = "keep me";
        await Until(() => store.Rows(zzSession).Count == 2);
        form.ClearDrillholeCommand.Execute(null); await form.WhenOptionsUpdated();
        Check(form is { DrillholeId: "", DrillholeName: "", ProjectArea: "", DrillholeSearch: "", Operator: "Op", Notes: "keep me" } &&
            !form.CanClearDrillhole && Ids(form).Length == 5, "× clears search, drillhole, Project and Campaign; keeps Operator and Notes; list reset");
        Check(vm.SessionId is null && vm.Results.Count == 0 && vm.NeedsCore && !vm.ToggleMeasurementCommand.CanExecute(null) &&
            !vm.ExportCommand.CanExecute(null) && !vm.NewMeasurementCommand.CanExecute(null) && vm.NextFrom == "" && vm.Step == "",
            "Empty selection: empty table and chart, Start/export/new measurement unavailable, warning shown");
        vm.TriggerCommand.Execute(null);
        Check(store.Rows(zzSession).Count == 2 && service.Calls == 2, "Clearing deletes nothing; nothing is captured without a drillhole");
        form.SaveCommand.Execute(null);
        Check(form.HasDrillholeError && Toasted(toasts, ToastKind.Error, "Select a drillhole from the list."), "Save without a drillhole: red border + 5 s toast");

        // Pick AB-1 and measure it; reselect ZZ-9: only its own rows, progress and session come back (sorted by From).
        await Pick(form, "AB-1");
        await Until(() => !vm.ToggleMeasurementCommand.CanExecute(null) && vm.SessionId is null);
        vm.SetCore(Saved()); await Until(() => vm.SessionId is not null);
        var abSession = vm.SessionId!.Value;
        vm.NextFrom = "3"; vm.Step = "25"; vm.ToggleMeasurementCommand.Execute(null);
        vm.TriggerCommand.Execute(null); service.Complete(); await Until(() => vm.CanTrigger);
        vm.ToggleMeasurementCommand.Execute(null);
        await Until(() => store.Rows(abSession).Count == 1);
        await Pick(form, "ZZ-9");
        await Until(() => vm.SessionId == zzSession);
        Check(vm.Results.Count == 2 && vm.Results.All(r => r.SessionId == zzSession) && vm.Results.Select(r => r.FromMm).SequenceEqual(new long?[] {10000, 10500}) &&
            vm.NextFrom == "11.00" && vm.Step == "50" && vm.NeedsCore, "Reselecting shows only that drillhole's rows (by From), its progress and session");
        vm.SetCore(Saved());
        Check(vm.SessionId == zzSession && vm.ToggleMeasurementCommand.CanExecute(null), "Save & Continue continues the same session");

        // A late load for an earlier selection cannot fill the view of a newer one.
        var slow = new MemoryRepository();
        foreach (var (id, session) in new[] { ("AB-1", abSession), ("ZZ-9", zzSession) })
            await slow.ResolveSessionAsync(new MeasurementSession(session, DateTimeOffset.Now, id));
        await slow.SaveAsync(new MeasurementSession(abSession, DateTimeOffset.Now, "AB-1"), store.Rows(abSession), null);
        using var lateVm = new MeasurementViewModel(new ControlledMeasurements(), new FakeExport(), new FakeDialogs(), slow);
        slow.Delay = new TaskCompletionSource();
        lateVm.SelectDrillhole("AB-1");      // still loading …
        lateVm.SelectDrillhole("");          // … when the selection is cleared
        slow.Delay.SetResult(); slow.Delay = null;
        await Task.Delay(50);
        Check(lateVm.SessionId is null && lateVm.Results.Count == 0, "A late result for an earlier selection is discarded");
        lateVm.SelectDrillhole("AB-1"); await Until(() => lateVm.SessionId == abSession);
        Check(lateVm.Results.Single().FromMm == 3000, "Selecting again loads that drillhole");
    }

    /// <summary>Editable capture duration: validation, persistence, the next capture's window, no accidental capture.</summary>
    private static async Task CaptureDurationChecks(CoreSnapshot core)
    {
        Check(CaptureDurationSettings.TryParse("2", out var two) && two == 2 && CaptureDurationSettings.TryParse(" 10 ", out var ten) && ten == 10 &&
            !new[] {"", " ", "0", "1", "-3", "abc", "2.5", "1e1", "11", "600", "+5"}.Any(t => CaptureDurationSettings.TryParse(t, out _)),
            "Duration: whole seconds 2–10 only (empty, 0, 1, negative, non-numeric, decimals and > 10 rejected)");
        var settingsPath = Path.Combine(Path.GetTempPath(), $"ct007-duration-{Guid.NewGuid():N}", "capture-settings.json");
        Check(new CaptureDurationSettings(settingsPath).Load() == 5, "Default 5 s when nothing is saved");

        var service = new ControlledMeasurements();
        var store = new MemoryRepository();
        var toasts = new ToastService();
        using (var vm = new MeasurementViewModel(service, new FakeExport(), new FakeDialogs(), store, toasts: toasts,
                   durationSettings: new CaptureDurationSettings(settingsPath)))
        {
            Check(vm.Countdown == "00:05" && vm.CaptureDuration == TimeSpan.FromSeconds(5), "Countdown shows the default 5 s");
            vm.SetCore(core with { DrillholeId = "DUR-1" }); await Until(() => vm.SessionId is not null);
            vm.NextFrom = "1"; vm.Step = "10";

            // Opening and saving the editor never starts measuring or captures; invalid input keeps it open with a 5 s toast.
            vm.EditDurationCommand.Execute(null);
            Check(vm.IsEditingDuration && vm.DurationText == "5" && !vm.IsMeasurementModeActive && service.Calls == 0, "Editor opens with the current seconds");
            foreach (var bad in new[] {"", "0", "1", "-1", "abc", "11"})
            {
                vm.DurationText = bad; vm.SaveDurationCommand.Execute(null);
                Check(vm.IsEditingDuration && vm.CaptureDuration == TimeSpan.FromSeconds(5) && toasts.Items.Last() is { Kind: ToastKind.Error, Duration.TotalSeconds: 5 },
                    $"Invalid duration '{bad}' rejected with a 5 s error toast; editor stays open");
            }
            vm.DurationText = "10"; vm.CancelDurationCommand.Execute(null);
            Check(!vm.IsEditingDuration && vm.CaptureDuration == TimeSpan.FromSeconds(5), "Cancel (Escape) keeps the old duration");
            vm.EditDurationCommand.Execute(null); vm.DurationText = "8"; vm.SaveDurationCommand.Execute(null);
            Check(!vm.IsEditingDuration && vm.CaptureDuration == TimeSpan.FromSeconds(8) && vm.Countdown == "00:08" && !vm.IsMeasurementModeActive && service.Calls == 0,
                "Save (Enter) sets 8 s; countdown shows 00:08; nothing started or captured");
            Check(new CaptureDurationSettings(settingsPath).Load() == 8, "Duration persisted locally");

            // While the editor is open, captures are refused (no accidental right-click capture).
            vm.ToggleMeasurementCommand.Execute(null);
            vm.EditDurationCommand.Execute(null);
            vm.TriggerCommand.Execute(null);
            Check(service.Calls == 0 && !vm.CanTrigger, "No capture while the duration editor is open");
            vm.CancelDurationCommand.Execute(null);

            // The capture uses the configured window; ring progress and countdown follow it; a running capture keeps it.
            vm.TriggerCommand.Execute(null);
            Check(service.Calls == 1 && service.LastDuration == TimeSpan.FromSeconds(8) && !vm.EditDurationCommand.CanExecute(null),
                "Capture runs with 8 s; the duration cannot be edited while capturing");
            service.Report(TimeSpan.FromSeconds(2));
            Check(vm.Countdown == "00:02" && vm.CaptureProgress == 0.75, "Countdown and ring use the 8 s window (6 of 8 s elapsed = 75 %)");
            service.Complete(); await Until(() => vm.CanTrigger);
            Check(vm.Results[0] is { SampleCount: 8, AverageValue: 4.5 } && vm.Results[0].EndTime - vm.Results[0].StartTime == TimeSpan.FromSeconds(8) &&
                vm.Countdown == "00:08" && vm.CaptureProgress == 0, "Row from the 8 collected samples (average 4.5); timer reset to 8 s");

            // Stop cancels an active capture and keeps completed rows; the timer resets to the configured duration.
            vm.TriggerCommand.Execute(null); service.Report(TimeSpan.FromSeconds(6));
            vm.ToggleMeasurementCommand.Execute(null);
            Check(vm.Results.Count == 1 && vm.Countdown == "00:08" && vm.CaptureProgress == 0 && !vm.IsMeasurementModeActive,
                "Stop cancels the capture, keeps the completed row and resets the timer");
            vm.EditDurationCommand.Execute(null); vm.DurationText = "10"; vm.SaveDurationCommand.Execute(null);
            Check(vm.Countdown == "00:10", "10 s is the maximum");
        }
        using (var restarted = new MeasurementViewModel(new ControlledMeasurements(), new FakeExport(), new FakeDialogs(), new MemoryRepository(),
                   durationSettings: new CaptureDurationSettings(settingsPath)))
        {
            Check(restarted.CaptureDuration == TimeSpan.FromSeconds(10) && restarted.Countdown == "00:10", "Duration restored after restart");
        }
        File.WriteAllText(settingsPath, """{"captureDurationSeconds": 0}""");
        Check(new CaptureDurationSettings(settingsPath).Load() == 5, "An invalid saved value falls back to 5 s");
        File.WriteAllText(settingsPath, """{"captureDurationSeconds": 90}""");
        Check(new CaptureDurationSettings(settingsPath).Load() == 5, "A saved value above the new 10 s maximum falls back to 5 s");
    }

    /// <summary>Step in centimetres at the UI boundary; rows always sorted by From; depth progression from the capture state.</summary>
    private static async Task MeasurementUnitsAndOrderChecks(CoreSnapshot core)
    {
        // Unit conversion (Calculation/Measurement/Depth.cs): Step cm ↔ stored mm.
        Check(Depth.TryParseCentimetres("25", out var cm25) && cm25 == 250 && Depth.TryParseCentimetres("2.5", out var cm2_5) && cm2_5 == 25 &&
            Depth.TryParseCentimetres("2,5", out var comma) && comma == 25 && Depth.TryParseCentimetres("100", out var metre) && metre == 1000 &&
            !Depth.TryParseCentimetres("0.25", out _) && !Depth.TryParseCentimetres("abc", out _) && !Depth.TryParseCentimetres("-5", out _),
            "Step cm → mm: 25 cm = 250 mm, 2.5 cm = 25 mm, 100 cm = 1 m; finer than 1 mm rejected");
        Check(Depth.FormatCentimetres(250) == "25" && Depth.FormatCentimetres(25) == "2.5" && Depth.FormatCentimetres(500) == "50" &&
            Depth.FormatCentimetres(1000) == "100", "Stored mm shown as cm: 250 → 25, 25 → 2.5, 1000 → 100");

        // Ordering rule (Calculation/Measurement/MeasurementOrder.cs).
        var t0 = new DateTimeOffset(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);
        MeasurementResult R(int capture, long? from, int seconds, long? record) => new()
        { SessionId = Guid.Empty, CaptureNumber = capture, StartTime = t0.AddSeconds(seconds), EndTime = t0.AddSeconds(seconds + 5),
          SampleCount = 5, FromMm = from, ToMm = from + 250, StepMm = from is null ? null : 250, RecordId = record };
        var mixed = new[] { R(1, 20000, 1, 10), R(2, null, 0, 11), R(3, 9000, 2, 12), R(4, 20000, 1, 9), R(5, 20000, 0, null), R(6, 100000, 3, 13), R(7, 20000, 1, null) };
        Check(MeasurementOrder.Sort(mixed).Select(r => r.CaptureNumber).SequenceEqual(new[] {3, 5, 4, 1, 7, 6, 2}),
            "Sort: numeric From (9 m < 20 m < 100 m), then capture time, then record ID (new rows after stored), rows without From last");

        // 10 m + 25 cm → 10.25 m; a re-measured shallower interval goes to its From position; progression follows the capture.
        var service = new ControlledMeasurements();
        var store = new MemoryRepository();
        var dialogs = new FakeDialogs();
        var export = new FakeExport();
        using var vm = new MeasurementViewModel(service, export, dialogs, store);
        vm.SetCore(core with { DrillholeId = "ORDER-1" }); await Until(() => vm.SessionId is not null);
        vm.NextFrom = "10"; vm.Step = "25"; vm.ToggleMeasurementCommand.Execute(null);
        Check(vm.DepthError == "", "Starting Depth 10 m and Step 25 cm are valid");
        for (int i = 0; i < 3; i++) { vm.TriggerCommand.Execute(null); service.Complete(); await Until(() => vm.CanTrigger); }
        Check(vm.Results[0] is { FromMm: 10000, ToMm: 10250, StepMm: 250, FromMetres: 10.0m, ToMetres: 10.25m, StepCentimetres: 25m } &&
            vm.NextFrom == "10.75", "to_m = from_m + step_cm / 100: 10 m + 25 cm = 10.25 m; each capture advances 0.25 m");
        vm.NextFrom = "5"; vm.Step = "50";
        vm.TriggerCommand.Execute(null); service.Complete(); await Until(() => vm.CanTrigger);
        Check(vm.Results.Select(r => r.CaptureNumber).SequenceEqual(new[] {4, 1, 2, 3}) && vm.Results.Select(r => r.Number).SequenceEqual(new[] {1, 2, 3, 4}) &&
            vm.Results[0] is { FromMm: 5000, ToMm: 5500 }, "A completed capture is placed by From (5 m first); No matches the displayed position");
        Check(vm.NextFrom == "5.50", "Next depth comes from the capture itself (5.5 m), not from the last displayed row (10.75 m)");
        vm.NextFrom = "10";
        vm.TriggerCommand.Execute(null); service.Complete(); await Until(() => vm.CanTrigger);
        Check(vm.Results.Select(r => r.CaptureNumber).SequenceEqual(new[] {4, 1, 5, 2, 3}) && vm.Results[2] is { FromMm: 10000, Number: 3 },
            "Equal From: the earlier capture first");
        await Until(() => store.Rows(vm.SessionId!.Value).Select(r => r.CaptureNumber).SequenceEqual(new[] {4, 1, 5, 2, 3}));
        vm.ExportCommand.Execute(null); await Until(() => vm.ExportCommand.CanExecute(null));
        var csv = CsvExportService.Serialize(export.Rows!.ToArray(), export.Session!).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Check(export.Rows!.Select(r => r.CaptureNumber).SequenceEqual(new[] {4, 1, 5, 2, 3}) && csv[1].StartsWith("\"1\",\"4\",") &&
            !csv[0].Contains("step") && !csv[0].Contains("from"), "CSV rows in the same From order; CSV columns (and units) unchanged");
        dialogs.Confirm = true; vm.DeleteCommand.Execute(vm.Results[1]);
        Check(vm.Results.Select(r => r.CaptureNumber).SequenceEqual(new[] {4, 5, 2, 3}) && vm.Results.Select(r => r.Number).SequenceEqual(new[] {1, 2, 3, 4}),
            "Delete keeps the From order and renumbers");
        Check(vm.Results.All(r => r.StepMm is 250 or 500) && vm.Results.Single(r => r.CaptureNumber == 4).StepMm == 500,
            "Sorting never changes intervals or values");
        vm.ToggleMeasurementCommand.Execute(null);
    }

    /// <summary>Device Status checks of this laptop: Bluetooth list, measurement database, Gamma Guard discovery.</summary>
    private static async Task LocalDeviceStatusChecks()
    {
        // Bluetooth: dropdown list (identification rule), the selected device by address, connected vs paired, failure.
        var settings = DeviceSettings.Default;
        IReadOnlyList<BluetoothDeviceOption> List(params KnownBluetoothDevice[] devices) => new Ct007BluetoothCheck(settings, () => devices).ListDevices();
        LocalStatus Bt(BluetoothDeviceOption selected, params KnownBluetoothDevice[] devices) => new Ct007BluetoothCheck(settings, () => devices).CheckSelected(selected);
        var other = new KnownBluetoothDevice("Headphones", 1, true, true);
        Check(List(other, new KnownBluetoothDevice("ct007-m-0012", 2, false, true), new KnownBluetoothDevice("SPP CT007-M-1", 3, true, true), new KnownBluetoothDevice("CT007-X-1", 4, true, true),
                new KnownBluetoothDevice("CT-007", 5, true, true), new KnownBluetoothDevice("", 6, true, true), new KnownBluetoothDevice("CT007-M-0001", 7, true, true), new KnownBluetoothDevice("CT007-M-0001", 7, false, true))
              .Select(o => (o.Address, o.Name)).SequenceEqual(new[] { (7UL, "CT007-M-0001"), (2UL, "ct007-m-0012") }),
            "Dropdown: paired or connected devices whose name BEGINS with CT007-M- (any case), once per address, by name");
        var a1 = new BluetoothDeviceOption(0x001122AABBCC, "CT007-M-A1");
        Check(a1.Display == "CT007-M-A1  (00:11:22:AA:BB:CC)" && a1.Equals(new BluetoothDeviceOption(0x001122AABBCC, "renamed")) &&
            !a1.Equals(new BluetoothDeviceOption(0x001122AABBCD, "CT007-M-A1")), "A device is identified by its Bluetooth address, not its display name");
        Check(Bt(a1, new KnownBluetoothDevice("CT007-M-A1", 0x001122AABBCC, true, true)) is { State: LocalState.Connected, Text: "Connected", Detail: "CT007-M-A1" },
            "Selected device actively connected → Connected with its name");
        Check(Bt(a1, new KnownBluetoothDevice("CT007-M-A1", 0x001122AABBCC, false, true), new KnownBluetoothDevice("CT007-M-A1", 0x001122AABBCD, true, true)) is { State: LocalState.Disconnected } paired &&
            paired.Detail == "CT007-M-A1 — paired with this laptop, not connected",
            "Paired is not connected; another device with the same name being connected does not count");
        Check(Bt(a1, other).Detail == "CT007-M-A1 — no longer paired with or known to this laptop", "Selected device unknown to Windows → Disconnected (confirmed)");
        Check(new Ct007BluetoothCheck(settings, () => throw new InvalidOperationException("Bluetooth is turned off.")).CheckSelected(a1)
            is { State: LocalState.Unavailable, Text: "Status unavailable", Detail: "Bluetooth is turned off." }, "Check failure → Status unavailable, not Disconnected");
        Check(new Ct007BluetoothCheck(settings with { Ct007Address = "00:11:22:AA:BB:CC" }, () => [new KnownBluetoothDevice("", 0x001122AABBCC, true, true)]).ListDevices()
            .Single().Display == "Name unavailable  (00:11:22:AA:BB:CC)", "Configured address identifies a listed device; missing name shown as Name unavailable");

        // Settings file.
        var settingsFile = Path.Combine(Path.GetTempPath(), $"device-settings-{Guid.NewGuid():N}.json");
        File.WriteAllText(settingsFile, """{"ct007NamePrefix":"GAMMA7-","ct007Address":"aa-bb","gammaGuardDatabasePath":"%TEMP%\\gg.sqlite","checkIntervalSeconds":2}""");
        var loaded = DeviceSettings.Load(settingsFile);
        Check(loaded.Ct007NamePrefix == "GAMMA7-" && loaded.GammaGuardDatabasePath.EndsWith("gg.sqlite") && loaded.CheckInterval == TimeSpan.FromSeconds(2) &&
            loaded.BluetoothCheckInterval == TimeSpan.FromSeconds(1) && DeviceSettings.Default.Ct007NamePrefix == "CT007-M-" &&
            loaded.GammaGuardFileName == "*.sqlite" && DeviceSettings.Load("missing.json") == DeviceSettings.Default, "device-settings.json is read; defaults fill gaps");

        // Measurement database: missing (never created by the check), available, not SQLite, busy.
        var root = Path.Combine(Path.GetTempPath(), $"ct007-local-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "measurements.db");
        Check(new MeasurementDatabaseCheck(dbPath).Check() is { State: LocalState.Missing, Path: var missingPath } && missingPath == dbPath && !File.Exists(dbPath),
            "Missing measurement database is reported and NOT created");
        var repository = new SqliteMeasurementRepository(dbPath);
        await repository.ResolveSessionAsync(new MeasurementSession(Guid.NewGuid(), DateTimeOffset.Now, "LOCAL-1", "P", "C", "Op", ""));
        var before = File.ReadAllBytes(dbPath);
        Check(new MeasurementDatabaseCheck(repository.DatabasePath).Check() is { State: LocalState.Available, Text: "Available" } available &&
            available.Path == repository.DatabasePath, "Measurement database at the repository's path: Available");
        Check(File.ReadAllBytes(dbPath).SequenceEqual(before), "The status check does not modify the database");
        var notDb = Path.Combine(root, "notes.sqlite");
        File.WriteAllText(notDb, new string('x', 4096));
        Check(SqliteFileCheck.Check(notDb, "") is { State: LocalState.Inaccessible, Detail: "The file is not a SQLite database." }, "A non-SQLite file is Inaccessible");
        var busyDb = Path.Combine(root, "busy.sqlite");
        using (var writer = new SqliteConnection($"Data Source={busyDb}"))
        {
            writer.Open();
            Scalar(writer, "CREATE TABLE t (x INTEGER)");
            Scalar(writer, "BEGIN EXCLUSIVE");
            Scalar(writer, "INSERT INTO t VALUES (1)");
            var watch = Stopwatch.StartNew();
            Check(SqliteFileCheck.Check(busyDb, "") is { State: LocalState.Busy, Text: "Busy" } && watch.Elapsed < TimeSpan.FromSeconds(5),
                "A database locked by another program is Busy (short timeout), not inaccessible");
            Scalar(writer, "ROLLBACK");
        }

        // Gamma Guard: discovered under a search root by the install layout; configured path; nothing found.
        var documents = Path.Combine(root, "Documents");
        var install = Path.Combine(documents, "Workspace", "Projects", "GammaGuard", "CT008 1.9.0", "Database");
        Directory.CreateDirectory(install);
        var gammaDb = Path.Combine(install, "ct008-log.sqlite");
        using (var create = new SqliteConnection($"Data Source={gammaDb}")) { create.Open(); Scalar(create, "CREATE TABLE log (x INTEGER)"); }
        File.WriteAllText(Path.Combine(documents, "Workspace", "unrelated.sqlite"), "not in a GammaGuard folder");
        var gammaBefore = File.ReadAllBytes(gammaDb);
        var gamma = new GammaGuardDatabaseCheck(settings, [(documents, 4)]);
        Check(gamma.Check() is { State: LocalState.Available } found && found.Path == gammaDb, "Gamma Guard database discovered on this laptop (no fixed path)");
        Check(File.ReadAllBytes(gammaDb).SequenceEqual(gammaBefore), "The Gamma Guard database is not modified");
        Check(new GammaGuardDatabaseCheck(settings, [(Path.Combine(root, "empty"), 4)]).Check() is { State: LocalState.NotFound, Path: "" },
            "Nothing found → Not found");
        Check(new GammaGuardDatabaseCheck(settings with { GammaGuardDatabasePath = Path.Combine(root, "nope.sqlite") }, []).Check()
            is { State: LocalState.Missing } configured && configured.Path.EndsWith("nope.sqlite"), "Configured path that does not exist → File not found with that path");
        File.Delete(gammaDb);
        Check(gamma.Check() is { State: LocalState.NotFound }, "A discovered database that disappears is searched again");

        // Monitor: nothing checked before a selection; then only the selected device, async, no overlap, results of a
        // previous selection discarded, published on the caller's context, stopped on Dispose.
        var gate = new ManualResetEventSlim(true);
        var runs = 0;
        var known = new List<KnownBluetoothDevice> { new("CT007-M-1", 1, true, true), new("CT007-M-2", 2, false, true) };
        using (var monitor = new DeviceStatusMonitor(
                   new Ct007BluetoothCheck(settings, () => { Interlocked.Increment(ref runs); gate.Wait(TimeSpan.FromSeconds(2)); lock (known) return known.ToList(); }),
                   new MeasurementDatabaseCheck(dbPath), new GammaGuardDatabaseCheck(settings, []), TimeSpan.FromMinutes(5)))
        {
            var thread = Environment.CurrentManagedThreadId;
            await monitor.CheckBluetoothAsync();
            Check(runs == 0 && monitor.Bluetooth is { State: LocalState.NotSelected, Text: "No device selected" } && monitor.SelectedBluetoothDevice is null,
                "Before a selection: No device selected, and no connection check runs");
            await monitor.RefreshBluetoothDevicesAsync();
            Check(runs == 1 && monitor.BluetoothDevices.Select(d => d.Name).SequenceEqual(new[] {"CT007-M-1", "CT007-M-2"}) && monitor.SelectedBluetoothDevice is null &&
                monitor.Bluetooth.State == LocalState.NotSelected, "The list is filled but nothing is selected automatically (even with a connected device)");
            gate.Reset();
            monitor.SelectBluetoothDevice(monitor.BluetoothDevices[0]);
            var a = monitor.CheckBluetoothAsync(); var b = monitor.CheckBluetoothAsync();
            Check(a == b && monitor.Bluetooth is { State: LocalState.Checking, Detail: "CT007-M-1" }, "A running check is reused, never doubled; the UI is not blocked meanwhile");

            // Switch while the check of CT007-M-1 is still running: its (Connected) result must not appear for CT007-M-2.
            monitor.SelectBluetoothDevice(monitor.BluetoothDevices[1]);
            gate.Set(); await a;
            Check(monitor.Bluetooth.State != LocalState.Connected, "A late result for the previous device is discarded");
            await Until(() => monitor.Bluetooth.State == LocalState.Disconnected);
            Check(monitor.Bluetooth.Detail.StartsWith("CT007-M-2") && monitor.SelectedBluetoothDevice!.Name == "CT007-M-2" && Environment.CurrentManagedThreadId == thread,
                "The new device's own state is shown (paired, not connected); published on the UI context");

            // The list refresh never changes the selection: the device stays selected (and listed) after it disappears.
            lock (known) known.RemoveAt(1);
            await monitor.RefreshBluetoothDevicesAsync(); await monitor.CheckBluetoothAsync();
            monitor.SelectedBluetoothDevice = null;   // what a ComboBox writes while its items are replaced
            Check(monitor.SelectedBluetoothDevice?.Address == 2 && monitor.BluetoothDevices.Any(d => d.Address == 2) &&
                monitor.Bluetooth.Detail.Contains("no longer paired"), "Selection kept when the device disappears or the list refreshes; never switched");
            var listBefore = monitor.BluetoothDevices;
            await monitor.RefreshBluetoothDevicesAsync();
            Check(ReferenceEquals(listBefore, monitor.BluetoothDevices), "An unchanged device list is not republished (the dropdown is not reset every refresh)");
            await monitor.CheckAllAsync();
            Check(monitor.MeasurementDatabase.State == LocalState.Available && monitor.GammaGuardDatabase.State == LocalState.NotFound, "All rows checked");
        }

        // Every second, only the selected device; one loop per monitor; stops on Dispose.
        var polls = 0;
        var poller = new DeviceStatusMonitor(new Ct007BluetoothCheck(settings, () => { Interlocked.Increment(ref polls); return [new("CT007-M-1", 1, true, true)]; }),
            new MeasurementDatabaseCheck(dbPath), new GammaGuardDatabaseCheck(settings, []), TimeSpan.FromMinutes(5), TimeSpan.FromMilliseconds(50));
        poller.Start(); poller.Start();
        await Task.Delay(300);
        var listOnly = polls;   // one list read at start, no connection checks before a selection
        poller.SelectBluetoothDevice(new BluetoothDeviceOption(1, "CT007-M-1"));
        await Task.Delay(520);
        var selectedPolls = polls - listOnly;
        poller.Dispose();
        var afterDispose = polls;
        await Task.Delay(200);
        Check(listOnly == 1 && selectedPolls is >= 6 and <= 13 && polls == afterDispose && poller.Bluetooth.State == LocalState.Connected,
            $"Polling: none before a selection ({listOnly} list read), then one check per interval ({selectedPolls} in 520 ms at 50 ms, Start twice), none after Dispose");
    }

    /// <summary>Large drillhole lists: background filtering, debounce, no stale results, few collection notifications.</summary>
    private static async Task DropdownPerformanceChecks(CoreSnapshot core, ApiSettings settings)
    {
        const int Total = 20000;
        var stub = new StubBackend
        {
            DrillholesJson = System.Text.Json.JsonSerializer.Serialize(Enumerable.Range(1, Total)
                .Select(i => new { holeId = $"BIG_{i:00000}", projectCode = $"P{i % 40}", campaign = $"{2000 + i % 25}" }))
        };
        using var api = new ApiClient(settings, stub);
        using var backend = new BackendConnection(api, TimeSpan.FromMinutes(5));
        var cachePath = TempDb("ct007-big");
        var history = new MemoryRepository();
        var load = Stopwatch.StartNew();
        var form = new CoreInformationViewModel(_ => { }, cachePath, new DrillholesClient(api), backend, new DrillholeCache(cachePath), history,
            NewDevices(cachePath), TimeSpan.FromMilliseconds(60));
        await form.LoadDrillholesAsync();
        load.Stop();
        Check(form.DrillholeOptions.Count == Total + 1, $"All {Total} drillholes listed under one heading, none truncated (loaded in {load.ElapsedMilliseconds} ms)");

        var notifications = 0;
        form.DrillholeOptions.CollectionChanged += (_, _) => notifications++;
        var typing = Stopwatch.StartNew();
        var slowest = TimeSpan.Zero;
        foreach (var text in new[] {"B", "BI", "BIG", "BIG_", "BIG_1", "BIG_12", "BIG_123"})
        {
            var key = Stopwatch.StartNew();
            form.DrillholeSearch = text;
            if (key.Elapsed > slowest) slowest = key.Elapsed;
        }
        await form.WhenOptionsUpdated();
        typing.Stop();
        Check(slowest < TimeSpan.FromMilliseconds(30), $"A keystroke costs the UI thread {slowest.TotalMilliseconds:0.0} ms (filtering runs in the background)");
        Check(notifications == 1 && form.DrillholeOptions.Count(o => !o.IsHeader) == 100 &&
            form.DrillholeOptions.Where(o => !o.IsHeader).All(o => o.HoleId.Contains("BIG_123")),
            $"Debounced: 7 fast keystrokes → 1 list update ({notifications} notifications); only the latest text's result is applied");

        // A stale (older, slower) result never replaces a newer one.
        form.DrillholeSearch = "BIG_0";
        await Task.Delay(5);
        form.DrillholeSearch = "BIG_1999";
        await form.WhenOptionsUpdated();
        Check(form.DrillholeOptions.Where(o => !o.IsHeader).Select(o => o.HoleId).SequenceEqual(Enumerable.Range(19990, 10).Select(i => $"BIG_{i}")),
            "Only the newest search's result is shown");

        // Pick, then clear the filter: selection kept; the change is one bulk update, not 20,000 item notifications.
        notifications = 0;
        form.PickedDrillhole = form.DrillholeOptions.First(o => !o.IsHeader); await form.WhenOptionsUpdated();
        Check(form is { DrillholeId: "BIG_19990", DrillholeName: "P30", ProjectArea: "2015" } && form.DrillholeOptions.Count == Total + 1 &&
            notifications == 1 && form.PickedDrillhole?.HoleId == "BIG_19990", "Picking fills Project and Campaign; full list back in ONE update; selection kept");

        // Measured group with a large list; refresh with identical data changes nothing.
        var measuring = new ControlledMeasurements();
        using (var vm = new MeasurementViewModel(measuring, new FakeExport(), new FakeDialogs(), history))
        {
            vm.SetCore(core with { DrillholeId = "BIG_15000" }); await Until(() => vm.SessionId is not null);
            vm.NextFrom = "1"; vm.Step = "10"; vm.ToggleMeasurementCommand.Execute(null);
            vm.TriggerCommand.Execute(null); measuring.Complete(); await Until(() => vm.CanTrigger);
            vm.ToggleMeasurementCommand.Execute(null);
        }
        await form.RefreshMeasuredAsync();
        Check(form.DrillholeOptions[0].Header == "Measured drillholes" && form.DrillholeOptions[1].HoleId == "BIG_15000" &&
            form.DrillholeOptions.Count == Total + 2 && form.DrillholeOptions.Count(o => o.HoleId == "BIG_15000") == 1 && form.DrillholeId == "BIG_19990",
            "Measured group with 20,000 entries: each drillhole once, selection kept");
        notifications = 0;
        await form.LoadDrillholesAsync();
        Check(notifications == 0 && form.DrillholeId == "BIG_19990", "Refreshing with the same list causes no dropdown update at all");
    }

    /// <summary>Drillhole autofill with a dropdown that echoes like WPF's, Operator autofill, and the depth inputs.</summary>
    private static async Task AutofillAndDepthInputChecks(CoreSnapshot core, ApiSettings settings)
    {
        var cachePath = TempDb("ct007-autofill");
        var history = new MemoryRepository();
        var stub = new StubBackend();
        using var api = new ApiClient(settings, stub);
        using var backend = new BackendConnection(api, TimeSpan.FromMinutes(5));
        CoreInformationViewModel NewForm() => new(_ => { }, cachePath, new DrillholesClient(api), backend,
            new DrillholeCache(cachePath), history, NewDevices(cachePath), SearchDelay);

        // An editable WPF ComboBox clears its selection when its items are replaced and then writes "" into its Text
        // binding. Reproduce that so the ViewModel is tested against the real control behaviour.
        static void EchoLikeComboBox(CoreInformationViewModel f) =>
            f.DrillholeOptions.CollectionChanged += (_, e) =>
            {
                if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
                {
                    f.PickedDrillhole = null;
                    f.DrillholeSearch = "";
                }
            };

        var form = NewForm();
        EchoLikeComboBox(form);
        await Until(() => form.DrillholeOptions.Count(o => !o.IsHeader) == 3 && !form.CanRetryDrillholes);
        Check(form.Operator == CoreInformationViewModel.CurrentWindowsAccount() && form.Operator.Length > 0, "Operator shown on a new form");

        // Pick from a filtered list (the pick rebuilds the list to show everything again).
        await Search(form, "0002");
        form.PickedDrillhole = form.DrillholeOptions.Single(o => !o.IsHeader); await form.WhenOptionsUpdated();
        Check(form is { DrillholeId: "GTZO_0002_1", DrillholeName: "ZOOVCH", ProjectArea: "2016", DrillholeSearch: "GTZO_0002_1" } &&
            form.DrillholeOptions.Count(o => !o.IsHeader) == 3 && form.PickedDrillhole?.Drillhole == new DrillholeResponse("GTZO_0002_1", "ZOOVCH", "2016"),
            "All drillholes: pick after filtering fills Project and Campaign (rebuild echo ignored)");
        form.Operator = "Manual Op";

        // A later list (API after cache) and a Measured-group refresh rebuild the list: the pick and Operator stay.
        stub.DrillholesJson = """[{"holeId":"GTZO_0002_1","projectCode":"ZOOVCH","campaign":"2016"},{"holeId":"DH-001","projectCode":"North Ridge","campaign":"Campaign 7"}]""";
        await form.LoadDrillholesAsync();
        Check(form is { DrillholeId: "GTZO_0002_1", DrillholeName: "ZOOVCH", ProjectArea: "2016", Operator: "Manual Op" },
            "Late list response keeps the pick, Project, Campaign and a manual Operator");
        var measuring = new ControlledMeasurements();
        using (var vm = new MeasurementViewModel(measuring, new FakeExport(), new FakeDialogs(), history))
        {
            vm.SetCore(core with { DrillholeId = "DH-001" }); await Until(() => vm.SessionId is not null);
            vm.NextFrom = "0"; vm.Step = "100"; vm.ToggleMeasurementCommand.Execute(null);
            vm.TriggerCommand.Execute(null); measuring.Complete(); await Until(() => vm.CanTrigger);
            vm.ToggleMeasurementCommand.Execute(null);
        }
        await form.RefreshMeasuredAsync();
        Check(form.DrillholeOptions.First().Header == "Measured drillholes" &&
            form is { DrillholeId: "GTZO_0002_1", DrillholeName: "ZOOVCH", ProjectArea: "2016", DrillholeSearch: "GTZO_0002_1" },
            "Measured group refresh keeps the pick");

        // Pick from the Measured drillholes group, then from All drillholes again.
        form.PickedDrillhole = form.DrillholeOptions.First(o => !o.IsHeader); await form.WhenOptionsUpdated();
        Check(form is { DrillholeId: "DH-001", DrillholeName: "North Ridge", ProjectArea: "Campaign 7" }, "Measured drillholes: pick fills Project and Campaign");
        await Search(form, "gtzo");
        form.PickedDrillhole = form.DrillholeOptions.Single(o => !o.IsHeader); await form.WhenOptionsUpdated();
        Check(form is { DrillholeId: "GTZO_0002_1", DrillholeName: "ZOOVCH", ProjectArea: "2016", Operator: "Manual Op" }, "Switching the pick refills both fields");

        // Offline restart: the same autofill from the cache.
        stub.Offline = true;
        var cached = NewForm();
        EchoLikeComboBox(cached);
        await Until(() => cached.DrillholeOptions.Count(o => !o.IsHeader) == 2 && cached.CanRetryDrillholes);
        await Search(cached, "dh");
        cached.PickedDrillhole = cached.DrillholeOptions.Single(o => !o.IsHeader); await cached.WhenOptionsUpdated();
        Check(cached is { DrillholeId: "DH-001", DrillholeName: "North Ridge", ProjectArea: "Campaign 7" } && cached.Operator.Length > 0,
            "Cached list (offline): pick fills Project and Campaign; Operator autofilled");
        stub.Offline = false;

        // Decimal metres with a point (or comma) are accepted whatever the Windows number format.
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
            Check(Depth.TryParseMetres("10.25", out var dot) && dot == 10250 && Depth.TryParseMetres("10,25", out var comma) && comma == 10250 &&
                Depth.TryParseMetres("10", out var whole) && whole == 10000 && !Depth.TryParseMetres("10.2501", out _) &&
                Depth.Format(10250) == "10.25", "Comma-decimal locale: 10.25 and 10,25 both accepted; shown as 10.25");
        }
        finally { CultureInfo.CurrentCulture = culture; }

        // Depth inputs keep exactly what is typed (also intermediate text) and commit valid values.
        var service = new ControlledMeasurements();
        var store = new MemoryRepository();
        using (var vm = new MeasurementViewModel(service, new FakeExport(), new FakeDialogs(), store))
        {
            vm.SetCore(core with { DrillholeId = "INPUT-1" }); await Until(() => vm.SessionId is not null);
            foreach (var typed in new[] {"1", "10", "10.", "10.2", "10.25"}) vm.NextFrom = typed;
            foreach (var typed in new[] {"0", "0.", "2", "2.", "2.5", "25"})
            {
                vm.Step = typed;
                Check(vm.Step == typed && (typed is "0" or "0." ? vm.DepthError.StartsWith("Step:") : vm.DepthError == ""),
                    $"Step (cm) shows '{typed}' while typing; zero is reported, not cleared");
            }
            vm.Step = "0.25";
            Check(vm.DepthError == "Step: enter a step greater than 0 cm (at most 1 decimal).", "A step finer than 1 mm (0.25 cm) is reported");
            vm.Step = "25";
            Check(vm.NextFrom == "10.25" && vm.DepthError == "" && !vm.IsNextFromInvalid && !vm.IsStepInvalid, "Starting Depth 10.25 kept and valid");
            var toastsBefore = vm.Toasts.Items.Count;
            vm.Step = "abc";
            Check(vm.Step == "abc" && vm.IsStepInvalid && !vm.IsNextFromInvalid && vm.Toasts.Items.Count == toastsBefore,
                "Invalid step stays visible with a red border; no toast while typing");
            vm.CommitDepthInputs();
            Check(Toasted(vm.Toasts, ToastKind.Error, "Step: enter a step") && vm.Toasts.Items.Last().Duration == TimeSpan.FromSeconds(5),
                "Leaving the box with invalid input shows a 5 s error toast");
            vm.ToggleMeasurementCommand.Execute(null);
            Check(!vm.IsMeasurementModeActive, "Start refuses invalid input");
            vm.Step = "25";
            vm.ToggleMeasurementCommand.Execute(null);
            vm.TriggerCommand.Execute(null);
            vm.NextFrom = "20"; vm.Step = "50";   // during the capture: applies to the next one only
            service.Complete(); await Until(() => vm.CanTrigger);
            Check(vm.Results[0] is { FromMm: 10250, ToMm: 10500, StepMm: 250 }, "Completed capture uses the committed 10.25 m / 25 cm");
            Check(vm.NextFrom == "20" && vm.Step == "50", "Edits made during a capture stay for the next capture");
            vm.TriggerCommand.Execute(null); service.Complete(); await Until(() => vm.CanTrigger);
            Check(vm.Results[1] is { FromMm: 20000, ToMm: 20500, StepMm: 500 } && vm.Results[0] is { FromMm: 10250, StepMm: 250 },
                "Next capture uses the edited values; the completed row is unchanged");
            vm.Step = "100";
            vm.ToggleMeasurementCommand.Execute(null); vm.ToggleMeasurementCommand.Execute(null);
            Check(vm.NextFrom == "20.50" && vm.Step == "100", "Stop → Start keeps next depth and selected step");
            vm.ToggleMeasurementCommand.Execute(null);
            await Until(() => store.Progress(vm.SessionId!.Value) == new DepthProgress(20500, 1000));
        }
        using (var reopened = new MeasurementViewModel(service, new FakeExport(), new FakeDialogs(), store))
        {
            reopened.SetCore(core with { DrillholeId = "INPUT-1" }); await Until(() => reopened.SessionId is not null);
            Check(reopened.NextFrom == "20.50" && reopened.Step == "100", "Next depth and step (cm) restored for the drillhole");
        }

        // Typing while the drillhole's session is still loading is not overwritten by the load.
        var slow = new MemoryRepository { Delay = new TaskCompletionSource() };
        using (var vm = new MeasurementViewModel(service, new FakeExport(), new FakeDialogs(), slow))
        {
            vm.SetCore(core with { DrillholeId = "SLOW-1" });
            vm.NextFrom = "12.5"; vm.Step = "50";
            slow.Delay.SetResult();
            await Until(() => vm.SessionId is not null && vm.ToggleMeasurementCommand.CanExecute(null) && vm.DepthError == "");
            Check(vm.NextFrom == "12.5" && vm.Step == "50", "Values typed while the session loads stay visible");
            await Until(() => slow.Progress(vm.SessionId!.Value) == new DepthProgress(12500, 500));
        }
    }

    /// <summary>Drillhole cache (SQLite, separate table) and the Measured / All drillholes groups.</summary>
    private static async Task DrillholeListChecks(CoreSnapshot core, ApiSettings settings)
    {
        var cachePath = TempDb("ct007-cache");
        var cache = new DrillholeCache(cachePath);
        var history = new MemoryRepository();
        var stub = new StubBackend();
        using var api = new ApiClient(settings, stub);
        using var backend = new BackendConnection(api, TimeSpan.FromMinutes(5));
        CoreInformationViewModel NewForm() => new(_ => { }, cachePath, new DrillholesClient(api), backend,
            new DrillholeCache(cachePath), history, NewDevices(cachePath), SearchDelay);
        async Task<string[]> Cached() => (await cache.LoadAsync()).Select(d => d.HoleId!).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        static string Layout(CoreInformationViewModel f) => string.Join(",", f.DrillholeOptions.Select(o => o.IsHeader ? "#" + o.Header : o.HoleId));

        // First start with the backend offline and no cache: clear empty state with Retry.
        stub.Offline = true;
        var empty = NewForm();
        await Until(() => empty.CanRetryDrillholes);
        Check(empty.DrillholeOptions.Count == 0 && empty.DrillholesMessage.StartsWith("No drillholes available") &&
            empty.DrillholesMessage.Contains("Backend is offline"), "No cache + backend unavailable: empty state with Retry");
        stub.Offline = false;
        empty.RefreshDrillholesCommand.Execute(null);
        await Until(() => Ids(empty).Length == 3 && !empty.CanRetryDrillholes);
        Check((await Cached()).SequenceEqual(new[] {"DH-001", "GTZO_0001_1", "GTZO_0002_1"}), "A successful response is cached");
        Check((await cache.LoadAsync()).Single(d => d.HoleId == "DH-001") == new DrillholeResponse("DH-001", "North Ridge", "Campaign 7"),
            "Cached Hole ID, project and campaign match the API");
        using (var db = new SqliteConnection($"Data Source={cachePath}"))
        {
            db.Open();
            Check(Scalar(db, "SELECT COUNT(*) FROM sqlite_master WHERE name = 'DrillholeCache'") == "1" &&
                Scalar(db, "SELECT COUNT(*) FROM sqlite_master WHERE name LIKE 'Measurement%'") == "0",
                "Cache is its own table, separate from measurement data");
        }

        // Restart with the backend offline: the cached list is shown right away; selection and unsaved values survive.
        stub.Offline = true;
        var offline = NewForm();
        await Until(() => Ids(offline).Length == 3 && offline.CanRetryDrillholes);
        Check(offline.DrillholesMessage.StartsWith("Showing the saved drillhole list. Could not refresh:") &&
            Ids(offline).SequenceEqual(new[] {"DH-001", "GTZO_0001_1", "GTZO_0002_1"}), "Restart offline: cached list shown, Retry offered");
        await Pick(offline, "GTZO_0002_1"); offline.Notes = "draft"; offline.Operator = "Night shift";
        Check(offline is { DrillholeId: "GTZO_0002_1", DrillholeName: "ZOOVCH", ProjectArea: "2016" }, "Cached entry fills Project and Campaign");

        // Invalid, empty and failed refreshes keep the cache and the form.
        stub.Offline = false; stub.DrillholesJson = "not json";
        await offline.LoadDrillholesAsync();
        Check(offline.DrillholesMessage.Contains("unexpected data") && (await Cached()).Length == 3, "Invalid response keeps the cache");
        stub.DrillholesJson = "[]";
        await offline.LoadDrillholesAsync();
        Check(offline.DrillholesMessage == "The backend returned no drillholes; showing the saved list." && Ids(offline).Length == 3 &&
            (await Cached()).Length == 3, "Empty response does not wipe the cache");
        stub.DrillholesStatus = HttpStatusCode.GatewayTimeout; stub.DrillholesJson = StubBackend.DefaultDrillholes;
        await offline.LoadDrillholesAsync();
        Check((await Cached()).Length == 3 && Ids(offline).Length == 3 &&
            offline is { DrillholeId: "GTZO_0002_1", Notes: "draft", Operator: "Night shift" }, "Failed refresh keeps cache, selection and unsaved values");

        // A successful refresh replaces the cache (removed entries go; the selection stays, flagged as not in the list).
        stub.DrillholesStatus = HttpStatusCode.OK;
        stub.DrillholesJson = """[{"holeId":"DH-001","projectCode":"North Ridge","campaign":"Campaign 8"},{"holeId":"NEW-5","projectCode":"P","campaign":"2026"},{"holeId":"new-5","projectCode":"dup","campaign":"x"}]""";
        await offline.LoadDrillholesAsync();
        Check((await Cached()).SequenceEqual(new[] {"DH-001", "NEW-5"}) && Ids(offline).SequenceEqual(new[] {"DH-001", "NEW-5"}) &&
            offline is { DrillholeId: "GTZO_0002_1", ProjectArea: "2016", Notes: "draft" } &&
            offline.DrillholesMessage == "GTZO_0002_1 is not in the latest drillhole list; its saved details are kept.",
            "Valid response replaces the cache; duplicates listed once; selection kept");

        // Replace is all or nothing: a failing write leaves the previous cache untouched.
        var replaced = false;
        try { await cache.ReplaceAsync(new[] { new DrillholeResponse("ZZ-1", "p", "c"), new DrillholeResponse(null, "p", "c") }); replaced = true; }
        catch (Exception) { }
        Check(!replaced && (await Cached()).SequenceEqual(new[] {"DH-001", "NEW-5"}), "Cache replace runs in one transaction");
        stub.DrillholesJson = StubBackend.DefaultDrillholes;

        // Groups: Measured drillholes from saved history (newest measurement first), then All drillholes (A–Z).
        var form = NewForm();
        await Until(() => Ids(form).Length == 3 && !form.CanRetryDrillholes);
        Check(Headers(form).SequenceEqual(new[] {"All drillholes"}), "No measurement history: Measured drillholes hidden");
        form.Notes = "unsaved";
        var service = new ControlledMeasurements();
        async Task Measure(string drillhole)
        {
            using var vm = new MeasurementViewModel(service, new FakeExport(), new FakeDialogs(), history);
            vm.MeasurementsSaved += () => _ = form.RefreshMeasuredAsync(); // as MainViewModel wires it
            vm.SetCore(core with { DrillholeId = drillhole }); await Until(() => vm.SessionId is not null);
            vm.NextFrom = "0"; vm.Step = "100"; vm.ToggleMeasurementCommand.Execute(null);
            vm.TriggerCommand.Execute(null); service.Complete(); await Until(() => vm.CanTrigger);
            vm.ToggleMeasurementCommand.Execute(null);
            await Task.Delay(5);
        }
        await Measure("GTZO_0002_1");
        await Until(() => Headers(form).Length == 2);
        Check(Layout(form) == "#Measured drillholes,GTZO_0002_1,#All drillholes,DH-001,GTZO_0001_1" &&
            !form.DrillholeOptions[0].ShowSeparator && form.DrillholeOptions[2].ShowSeparator,
            "After a save the drillhole moves to Measured drillholes; listed once; separator before All drillholes");
        await Measure("OLD-9");   // measured, but not in the API list
        await Measure("DH-001");
        await Until(() => Ids(form)[0] == "DH-001");
        Check(Layout(form) == "#Measured drillholes,DH-001,GTZO_0002_1,#All drillholes,GTZO_0001_1" && form.Notes == "unsaved",
            "Measured: newest measurement first; history not in the API list is not offered; form values kept");

        // Search covers both groups; a heading stays only while its group has matches.
        await Search(form, "gtzo");
        Check(Layout(form) == "#Measured drillholes,GTZO_0002_1,#All drillholes,GTZO_0001_1", "Search filters both groups");
        await Search(form, "0001");
        Check(Layout(form) == "#All drillholes,GTZO_0001_1" && !form.DrillholeOptions[0].ShowSeparator, "A group without matches loses its heading");
        await Search(form, "zzz");
        Check(form.DrillholeOptions.Count == 0, "No matches: no headings");
        await Search(form, "dh-0");
        form.PickedDrillhole = form.DrillholeOptions.Single(o => !o.IsHeader); await form.WhenOptionsUpdated();
        Check(form is { DrillholeId: "DH-001", DrillholeName: "North Ridge", ProjectArea: "Campaign 7" } && form.PickedDrillhole?.HoleId == "DH-001",
            "Picking from Measured drillholes fills Project and Campaign");

        // Restart: the groups come back from the saved history.
        var restarted = NewForm();
        await Until(() => Headers(restarted).Length == 2);
        Check(Ids(restarted).SequenceEqual(new[] {"DH-001", "GTZO_0002_1", "GTZO_0001_1"}), "Measured drillholes restored after restart");
        // The app itself: saved history shows Measured drillholes on start.
        using (var main = new MainViewModel(history, settings with { StatusInterval = TimeSpan.FromMinutes(5) }, DeviceSettings.Default, new StubBackend(), () => []))
        {
            await Until(() => Headers(main.Core).Length == 2);
            Check(Ids(main.Core).SequenceEqual(new[] {"DH-001", "GTZO_0002_1", "GTZO_0001_1"}), "App start: Measured drillholes from history");
        }
    }

    private static async Task SqliteChecks(CoreSnapshot core)
    {
        var path = Path.Combine(Path.GetTempPath(), $"ct007-smoke-{Guid.NewGuid():N}.db");

        try
        {
            var store = new SqliteMeasurementRepository(path);
            MeasurementSession Candidate(string drillhole, string name = "Alpha") =>
                new(Guid.NewGuid(), DateTimeOffset.Now, drillhole, name, "Project X", "Ann", "first pass");
            var aCandidate = Candidate("A-1");
            var a = await store.ResolveSessionAsync(aCandidate);
            var b = await store.ResolveSessionAsync(Candidate("B-2", "Beta"));
            Check(a == aCandidate && b.Id != a.Id, "A new Drillhole ID creates a session");
            var again = await new SqliteMeasurementRepository(path).ResolveSessionAsync(Candidate("A-1", "Renamed") with { CreatedAt = DateTimeOffset.Now.AddDays(1) });
            Check(again.Id == a.Id && again.CreatedAt == a.CreatedAt && again.DrillholeName == "Alpha" && again.MetadataDiffers(Candidate("A-1", "Renamed")),
                "Same Drillhole ID → same session; CreatedAt and metadata kept");
            var racers = await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
                new SqliteMeasurementRepository(path).ResolveSessionAsync(Candidate("C-3"))));
            Check(racers.Select(r => r.Id).Distinct().Count() == 1, "Concurrent lookups of one Drillhole ID create one session");

            var start = new DateTimeOffset(2026,10,11,11,16,10,851,TimeSpan.FromHours(8)).AddTicks(1234);
            MeasurementResult Row(MeasurementSession s, int capture, DeviceReading reading, long? from = null, long? step = null) => new()
            { SessionId=s.Id, CaptureNumber=capture, StartTime=start, EndTime=start.AddSeconds(5), SampleCount=5, Reading=reading,
              FromMm=from, ToMm=from + step, StepMm=step };
            var a1 = Row(a, 1, new DeviceReading(1760152570851, 0.1 + 0.2, 125, 1.5e-7, -3, 12.25), 10000, 500);
            var a2 = Row(a, 2, DeviceReading.Empty);
            await store.SaveAsync(a, new[] {a1, a2}, new DepthProgress(10500, 500));
            await store.SaveAsync(a, new[] {a2, a1}, new DepthProgress(10500, 250));
            await store.SaveAsync(b, new[] {Row(b, 1, DeviceReading.Empty)}, null);
            try { await store.SaveAsync(b, new[] {a1}, null); throw new Exception("Cross-session save accepted"); }
            catch (ArgumentException) { }

            var reopened = new SqliteMeasurementRepository(path);
            var loaded = await reopened.LoadAsync(a.Id);
            Check(loaded.Select(r => r.CaptureNumber).SequenceEqual(new[] {2,1}) && loaded[0].Number == 1, "Upsert: no duplicates, saved order kept");
            Check(loaded[1].StartTime == start && loaded[1].StartTime.Offset == start.Offset && loaded[1].EndTime == start.AddSeconds(5) &&
                loaded[1].Reading == a1.Reading && loaded[0].Reading == DeviceReading.Empty && loaded[1].SampleCount == 5,
                "Stored values, precision and nulls survive a reopen");
            Check(loaded[1] is { FromMm: 10000, ToMm: 10500, StepMm: 500 } && loaded[0] is { FromMm: null, ToMm: null, StepMm: null },
                "Depth intervals stored in millimetres; reordering kept them");
            Check(await reopened.LoadProgressAsync(a.Id) == new DepthProgress(10500, 250) && await reopened.LoadProgressAsync(b.Id) is null,
                "Progress stored per session");
            Check((await reopened.LoadAsync(b.Id)).All(r => r.SessionId == b.Id) && (await reopened.LoadAsync(b.Id)).Count == 1,
                "Loading one session excludes others");
            Check((await reopened.LoadLatestSessionAsync()) is { DrillholeId: "B-2", DrillholeName: "Beta" } latestB && latestB.Id == b.Id,
                "Latest session with its metadata");
            await reopened.DeleteAsync(a.Id, 2);
            Check((await reopened.LoadAsync(a.Id)).Count == 1, "Delete removes only that capture");

            // Measuring, Stop → Start and restart keep rows, depths and progress (core = Drillhole DH-001 from 150 m).
            var service = new ControlledMeasurements();
            Guid captured;
            using (var vm = new MeasurementViewModel(service, new FakeExport(), new FakeDialogs(), reopened))
            {
                vm.SetCore(core); await Until(() => vm.SessionId is not null);
                vm.NextFrom = "150"; vm.Step = "50"; vm.ToggleMeasurementCommand.Execute(null);
                captured = vm.SessionId!.Value;
                for (int i=0; i<2; i++) { vm.TriggerCommand.Execute(null); service.Complete(); await Until(() => vm.CanTrigger); }
                vm.ToggleMeasurementCommand.Execute(null); vm.Step = "25"; vm.ToggleMeasurementCommand.Execute(null);
                vm.TriggerCommand.Execute(null); service.Complete(); await Until(() => vm.CanTrigger);
                vm.ToggleMeasurementCommand.Execute(null);
            }
            await Task.Delay(50);
            // A database from the manual-reordering version keeps its old SortOrder (no migration); the display ignores it.
            using (var legacy = new SqliteConnection($"Data Source={path}"))
            {
                legacy.Open();
                Scalar(legacy, $"UPDATE MeasurementRecords SET SortOrder = 4 - CaptureNo WHERE session_id = '{captured}'");
            }
            using (var restarted = new MeasurementViewModel(new ControlledMeasurements(), new FakeExport(), new FakeDialogs(),
                       new SqliteMeasurementRepository(path)))
            {
                await restarted.RestoreLatestSessionAsync();
                Check(restarted.SessionId == captured && restarted.Results.Count == 3 && restarted.State == MeasurementState.Idle &&
                    restarted.Results.Select(r => r.CaptureNumber).SequenceEqual(new[] {1,2,3}) &&
                    restarted.Results.Select(r => r.Number).SequenceEqual(new[] {1,2,3}) && restarted.Results.All(r => r.RecordId > 0),
                    "After restart rows are sorted by From (an old manual SortOrder is ignored); No follows; record IDs loaded");
                Check(restarted.Results.Select(r => (r.FromMm, r.ToMm, r.StepMm)).SequenceEqual(new (long?, long?, long?)[]
                    { (150000, 150500, 500), (150500, 151000, 500), (151000, 151250, 250) }), "Depths restored unchanged");
                Check(restarted.NextFrom == "151.25" && restarted.Step == "25", "Next depth and step restored after restart; stored 250 mm shows as 25 cm");
                var restoredSession = await new SqliteMeasurementRepository(path).LoadLatestSessionAsync();
                var restoredCsv = CsvExportService.Serialize(restarted.Results.ToArray(), restoredSession!).Split("\r\n");
                Check(restoredCsv[1].StartsWith("\"1\",\"1\",") && restoredCsv[3].StartsWith("\"3\",\"3\","), "CSV follows the From order");
                restarted.SetCore(core); restarted.ToggleMeasurementCommand.Execute(null);
                Check(restarted.SessionId == captured && restarted.State == MeasurementState.Ready, "Start after restart continues the restored session");
            }
            using (var continued = new MeasurementViewModel(service, new FakeExport(), new FakeDialogs(), new SqliteMeasurementRepository(path)))
            {
                // Without restoring first: saving the same core still finds the same session by Drillhole ID.
                continued.SetCore(core); await Until(() => continued.SessionId is not null);
                Check(continued.SessionId == captured && continued.NextFrom == "151.25" && continued.Step == "25",
                    "Same Drillhole ID after restart → same session and progress");
                continued.ToggleMeasurementCommand.Execute(null);
                continued.TriggerCommand.Execute(null); service.Complete(); await Until(() => continued.CanTrigger);
                Check(continued.Results.Select(r => r.CaptureNumber).SequenceEqual(new[] {1,2,3,4}) &&
                    continued.Results[^1] is { FromMm: 151250, ToMm: 151500 }, "Appends after restored rows, depth continues");
                continued.ToggleMeasurementCommand.Execute(null);
            }
            // Demo values never reach SQLite; a device ts_ms is stored.
            var demoService = new ControlledMeasurements();
            Guid demoSession;
            using (var demoVm = new MeasurementViewModel(demoService, new FakeExport(), new FakeDialogs(), new SqliteMeasurementRepository(path), showDemoTsMs: true))
            {
                demoVm.SetCore(core with { DrillholeId = "DEMO-2" }); await Until(() => demoVm.SessionId is not null);
                demoSession = demoVm.SessionId!.Value;
                demoVm.NextFrom = "1"; demoVm.Step = "50"; demoVm.ToggleMeasurementCommand.Execute(null);
                demoVm.TriggerCommand.Execute(null); demoService.Complete(); await Until(() => demoVm.CanTrigger);
                demoService.Reading = new DeviceReading(TsMs: 777);
                demoVm.TriggerCommand.Execute(null); demoService.Complete(); await Until(() => demoVm.CanTrigger);
                Check(demoVm.Results[0].IsDemoTsMs, "Demo value shown for the first row");
            }
            var demoStored = await new SqliteMeasurementRepository(path).LoadAsync(demoSession);
            Check(demoStored.Count == 2 && demoStored[0].Reading.TsMs is null && demoStored[0].DemoTsMs is null && demoStored[1].Reading.TsMs == 777,
                "SQLite holds no demo values; device ts_ms is saved");

            // Measured drillholes: newest capture Start Time first, one entry per drillhole, sessions without rows left out.
            var measured = await new SqliteMeasurementRepository(path).LoadMeasuredDrillholesAsync();
            Check(measured.Select(m => m.DrillholeId).Take(2).SequenceEqual(new[] {"DEMO-2", "DH-001"}) &&
                measured.Skip(2).Select(m => m.DrillholeId).Order().SequenceEqual(new[] {"A-1", "B-2"}) &&
                measured.Single(m => m.DrillholeId == "A-1").LastMeasured == start, "Measured drillholes ordered by measurement time");

            var final = await new SqliteMeasurementRepository(path).LoadAsync(captured);
            Check(final.Select(r => r.CaptureNumber).SequenceEqual(new[] {1,2,3,4}), "Stored order (SortOrder = displayed From order) and rows complete, no duplicates");
            Check(await new SqliteMeasurementRepository(path).LoadProgressAsync(captured) == new DepthProgress(151500, 250),
                "Progress advanced exactly once per capture");

            // Schema v3 on a new database: short ID names, enforced foreign key, index on session_id.
            using (var check = new SqliteConnection($"Data Source={path};Foreign Keys=True"))
            {
                check.Open();
                Check(Scalar(check, "PRAGMA user_version") == "5", "Schema version 5");
                Check(Scalar(check, "SELECT COUNT(*) FROM MeasurementSessions WHERE DrillholeID = 'C-3'") == "1", "One row per Drillhole ID");
                Check(Scalar(check, "SELECT DrillholeID || '|' || DrillholeName || '|' || Project || '|' || \"operator\" FROM MeasurementSessions WHERE id = '" + captured + "'")
                    == "DH-001|North Ridge|Campaign 7|Field Tech", "Drillhole / Project / Campaign / Operator stored in their original columns");
                Check(Scalar(check, "SELECT group_concat(name) FROM pragma_index_info('UX_MeasurementSessions_DrillholeID')") == "DrillholeID",
                    "DrillholeID is unique");
                Check(Scalar(check, "SELECT \"from\" || '-' || \"to\" || '/' || step FROM MeasurementRecords WHERE CaptureNo = 4 AND session_id = '" + captured + "'")
                    == "151250-151500/250", "from/to/step columns hold millimetres");
                Check(Scalar(check, "SELECT \"table\" || '.' || \"to\" || ' <- ' || \"from\" FROM pragma_foreign_key_list('MeasurementRecords')")
                    == "MeasurementSessions.id <- session_id", "MeasurementRecords.session_id -> MeasurementSessions.id");
                Check(Scalar(check, "SELECT on_delete FROM pragma_foreign_key_list('MeasurementRecords')") == "NO ACTION", "No cascading delete");
                Check(Column(check, "PRAGMA foreign_key_check", 0).Count == 0, "Every record references an existing session");
                Check(Scalar(check, "SELECT COUNT(*) FROM (SELECT id FROM MeasurementRecords GROUP BY id HAVING COUNT(*) > 1)") == "0" &&
                      Scalar(check, "SELECT COUNT(*) FROM (SELECT id FROM MeasurementSessions GROUP BY id HAVING COUNT(*) > 1)") == "0",
                    "Unique session and record IDs");
                Check(Scalar(check, "SELECT group_concat(name) FROM pragma_index_info('UX_MeasurementRecords_Session_CaptureNo')") == "session_id,CaptureNo" &&
                      string.Join(" ", Column(check, "EXPLAIN QUERY PLAN SELECT * FROM MeasurementRecords WHERE session_id = 'x'", 3))
                        .Contains("USING INDEX UX_MeasurementRecords_Session_CaptureNo"), "session_id is indexed and lookups use the index");
                try
                {
                    Scalar(check, "INSERT INTO MeasurementRecords (session_id, SortOrder, CaptureNo, StartTime, EndTime, Sample) VALUES ('missing', 1, 1, 'a', 'b', 5)");
                    throw new Exception("Orphan record accepted");
                }
                catch (SqliteException) { }
            }

            // Older databases upgrade in place: v1 (DisplayNo, long ID names) and v2 (SortOrder, long ID names).
            foreach (var (version, orderColumn) in new[] { (1, "DisplayNo"), (2, "SortOrder") })
            {
                var legacy = Path.Combine(Path.GetTempPath(), $"ct007-legacy-{Guid.NewGuid():N}.db");
                using (var connection = new SqliteConnection($"Data Source={legacy}"))
                {
                    connection.Open();
                    using var create = connection.CreateCommand();
                    create.CommandText = $"""
                        CREATE TABLE MeasurementSessions (MeasurementSessionId TEXT NOT NULL PRIMARY KEY, CreatedAt TEXT NOT NULL);
                        CREATE TABLE MeasurementRecords (MeasurementRecordId INTEGER PRIMARY KEY AUTOINCREMENT,
                            MeasurementSessionId TEXT NOT NULL REFERENCES MeasurementSessions (MeasurementSessionId),
                            {orderColumn} INTEGER NOT NULL, CaptureNo INTEGER NOT NULL, StartTime TEXT NOT NULL, EndTime TEXT NOT NULL,
                            Sample INTEGER NOT NULL, TsMs INTEGER, Count0 REAL, Count0Raw REAL, Hdm REAL, HdmRaw REAL, DoseRate REAL);
                        CREATE UNIQUE INDEX UX_MeasurementRecords_Session_CaptureNo ON MeasurementRecords (MeasurementSessionId, CaptureNo);
                        INSERT INTO MeasurementSessions VALUES ('11111111-1111-1111-1111-111111111111', '2026-10-11T11:16:10.8510000+08:00');
                        INSERT INTO MeasurementSessions VALUES ('22222222-2222-2222-2222-222222222222', '2026-10-12T09:00:00.0000000+08:00');
                        INSERT INTO MeasurementRecords (MeasurementRecordId, MeasurementSessionId, {orderColumn}, CaptureNo, StartTime, EndTime, Sample, Count0) VALUES
                            (7, '11111111-1111-1111-1111-111111111111', 2, 1, '2026-10-11T11:16:10.8510000+08:00', '2026-10-11T11:16:15.8510000+08:00', 5, 12.5),
                            (9, '11111111-1111-1111-1111-111111111111', 1, 2, '2026-10-11T11:16:20.8510000+08:00', '2026-10-11T11:16:25.8510000+08:00', 5, NULL),
                            (12, '22222222-2222-2222-2222-222222222222', 1, 1, '2026-10-12T09:00:00.0000000+08:00', '2026-10-12T09:00:05.0000000+08:00', 5, 3);
                        PRAGMA user_version = {version};
                        """;
                    create.ExecuteNonQuery();
                }
                try
                {
                    var upgraded = new SqliteMeasurementRepository(legacy);
                    var first = await upgraded.LoadAsync(Guid.Parse("11111111-1111-1111-1111-111111111111"));
                    Check(first.Select(r => r.CaptureNumber).SequenceEqual(new[] {2,1}) && first[1].Reading.Count0 == 12.5 &&
                        (await upgraded.LoadAsync(Guid.Parse("22222222-2222-2222-2222-222222222222"))).Single().Reading.Count0 == 3,
                        $"v{version} database upgraded with values, order and session isolation kept");
                    using var check = new SqliteConnection($"Data Source={legacy}");
                    check.Open();
                    Check(Scalar(check, "PRAGMA user_version") == "5" &&
                      Scalar(check, "SELECT COUNT(*) FROM MeasurementSessions WHERE DrillholeID IS NULL") == "2" &&
                      Scalar(check, "SELECT COUNT(*) FROM MeasurementRecords WHERE \"from\" IS NULL AND \"to\" IS NULL AND step IS NULL") == "3" &&
                          Scalar(check, "SELECT group_concat(id || ':' || session_id || ':' || CaptureNo, ' ') FROM (SELECT * FROM MeasurementRecords ORDER BY id)") ==
                          "7:11111111-1111-1111-1111-111111111111:1 9:11111111-1111-1111-1111-111111111111:2 12:22222222-2222-2222-2222-222222222222:1" &&
                          Scalar(check, "SELECT group_concat(id, ' ') FROM (SELECT id FROM MeasurementSessions ORDER BY id)") ==
                          "11111111-1111-1111-1111-111111111111 22222222-2222-2222-2222-222222222222",
                        $"v{version} upgrade keeps record IDs, session IDs and links");
                    Check(Scalar(check, "SELECT \"to\" FROM pragma_foreign_key_list('MeasurementRecords')") == "id", $"v{version} foreign key renamed");
                    // Reopening must not run the migration again.
                    Check((await new SqliteMeasurementRepository(legacy).LoadAsync(Guid.Parse("11111111-1111-1111-1111-111111111111"))).Count == 2,
                        "Migration runs only once");
                }
                finally { SqliteConnection.ClearAllPools(); File.Delete(legacy); }
            }

            // A database whose DrillholeID column already has duplicates (changed outside the app) is not upgraded.
            var duplicated = Path.Combine(Path.GetTempPath(), $"ct007-dup-{Guid.NewGuid():N}.db");
            using (var connection = new SqliteConnection($"Data Source={duplicated}"))
            {
                connection.Open();
                using var create = connection.CreateCommand();
                create.CommandText = """
                    CREATE TABLE MeasurementSessions (id TEXT NOT NULL PRIMARY KEY, CreatedAt TEXT NOT NULL, DrillholeID TEXT, DrillholeName TEXT);
                    CREATE TABLE MeasurementRecords (id INTEGER PRIMARY KEY AUTOINCREMENT, session_id TEXT NOT NULL REFERENCES MeasurementSessions (id),
                        SortOrder INTEGER NOT NULL, CaptureNo INTEGER NOT NULL, StartTime TEXT NOT NULL, EndTime TEXT NOT NULL,
                        Sample INTEGER NOT NULL, TsMs INTEGER, Count0 REAL, Count0Raw REAL, Hdm REAL, HdmRaw REAL, DoseRate REAL);
                    INSERT INTO MeasurementSessions VALUES ('11111111-1111-1111-1111-111111111111', '2026-10-11T11:16:10.8510000+08:00', 'DH-9', 'North');
                    INSERT INTO MeasurementSessions VALUES ('22222222-2222-2222-2222-222222222222', '2026-10-12T09:00:00.0000000+08:00', 'DH-9', 'South');
                    INSERT INTO MeasurementRecords (session_id, SortOrder, CaptureNo, StartTime, EndTime, Sample)
                        VALUES ('22222222-2222-2222-2222-222222222222', 1, 1, 'a', 'b', 5);
                    PRAGMA user_version = 3;
                    """;
                create.ExecuteNonQuery();
            }
            try
            {
                try { await new SqliteMeasurementRepository(duplicated).LoadLatestSessionAsync(); throw new Exception("Duplicate Drillhole IDs accepted"); }
                catch (InvalidOperationException error) when (error.Message.Contains("DH-9 (2 sessions, conflicting metadata)")) { }
                using var check = new SqliteConnection($"Data Source={duplicated}");
                check.Open();
                Check(Scalar(check, "PRAGMA user_version") == "3" && Scalar(check, "SELECT COUNT(*) FROM MeasurementRecords") == "1" &&
                      Scalar(check, "SELECT COUNT(*) FROM pragma_table_info('MeasurementRecords') WHERE name = 'step'") == "0",
                    "Refused upgrade reports the duplicates and changes nothing");
            }
            finally { SqliteConnection.ClearAllPools(); File.Delete(duplicated); }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    private static string Scalar(SqliteConnection connection, string sql) => Column(connection, sql, 0).FirstOrDefault() ?? "";

    private static List<string> Column(SqliteConnection connection, string sql, int ordinal)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var values = new List<string>();
        while (reader.Read()) values.Add(reader.IsDBNull(ordinal) ? "" : Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture)!);
        return values;
    }

    // Follows a binding path the way WPF does, to prove the configured table bindings exist.
    private static object? Resolve(string path, object? source)
    {
        foreach (var part in path.Split('.'))
        {
            source = source?.GetType().GetProperty(part)?.GetValue(source);
        }

        return source;
    }
    private sealed class FakeDialogs : IDialogService
    {
        public bool Confirm { get; set; }
        public bool ConfirmDelete() => Confirm;
    }
    private sealed class MemoryRepository : IMeasurementRepository
    {
        public string DatabasePath { get; } = TempDb("ct007-memory");
        private readonly object gate = new();
        private readonly Dictionary<(Guid, int), MeasurementResult> rows = new();
        private readonly Dictionary<(Guid, int), int> numbers = new();
        private readonly Dictionary<string, MeasurementSession> sessions = new();
        private readonly Dictionary<Guid, DepthProgress> progress = new();
        public volatile bool Fail;
        public int SaveCount { get; private set; }
        public List<MeasurementResult> Rows(Guid session)
        { lock (gate) return rows.Where(p => p.Key.Item1 == session).OrderBy(p => numbers[p.Key]).Select(p => p.Value).ToList(); }
        public DepthProgress? Progress(Guid session) { lock (gate) return progress.GetValueOrDefault(session); }
        public Task<IReadOnlyList<MeasuredDrillhole>> LoadMeasuredDrillholesAsync()
        {
            lock (gate)
            {
                IReadOnlyList<MeasuredDrillhole> list = sessions.Values
                    .Select(s => (s.DrillholeId, Times: rows.Where(r => r.Key.Item1 == s.Id).Select(r => r.Value.StartTime).ToList()))
                    .Where(x => x.DrillholeId is not null && x.Times.Count > 0)
                    .Select(x => new MeasuredDrillhole(x.DrillholeId!, x.Times.Max()))
                    .OrderByDescending(m => m.LastMeasured).ToList();
                return Task.FromResult(list);
            }
        }
        public TaskCompletionSource? Delay;   // holds session lookups (a slow disk)
        public async Task<MeasurementSession> ResolveSessionAsync(MeasurementSession candidate)
        {
            if (Delay is { } delay) await delay.Task;
            lock (gate) return sessions.TryGetValue(candidate.DrillholeId!, out var found) ? found : sessions[candidate.DrillholeId!] = candidate;
        }
        public Task SaveAsync(MeasurementSession session, IReadOnlyList<MeasurementResult> list, DepthProgress? next)
        {
            if (Fail) throw new IOException("disk full");
            lock (gate)
            {
                for (int i=0; i<list.Count; i++) { rows[(session.Id, list[i].CaptureNumber)] = list[i]; numbers[(session.Id, list[i].CaptureNumber)] = i+1; SaveCount++; }
                if (next is not null) progress[session.Id] = next;
            }
            return Task.CompletedTask;
        }
        public Task<DepthProgress?> LoadProgressAsync(Guid session) => Task.FromResult(Progress(session));
        public Task DeleteAsync(Guid session, int capture)
        {
            if (Fail) throw new IOException("database locked");
            lock (gate) { rows.Remove((session, capture)); numbers.Remove((session, capture)); }
            return Task.CompletedTask;
        }
        public Task<MeasurementSession?> LoadLatestSessionAsync() => Task.FromResult<MeasurementSession?>(null);
        public async Task<MeasurementSession?> FindSessionAsync(string drillholeId)
        {
            if (Delay is { } delay) await delay.Task;
            lock (gate) return sessions.GetValueOrDefault(drillholeId);
        }
        public Task<IReadOnlyList<MeasurementResult>> LoadAsync(Guid session) => Task.FromResult<IReadOnlyList<MeasurementResult>>(Rows(session));
    }
    /// <summary>Stub backend: answers the drillhole endpoint (and 404 elsewhere), or fails like a real network.</summary>
    private sealed class StubBackend : HttpMessageHandler
    {
        public const string DefaultDrillholes = """
            [{"holeId":"GTZO_0001_1","projectCode":"ZOOVCH","campaign":"2015"},
             {"holeId":"GTZO_0002_1","projectCode":"ZOOVCH","campaign":"2016"},
             {"holeId":"DH-001","projectCode":"North Ridge","campaign":"Campaign 7"},
             {"holeId":"","projectCode":"ignored","campaign":"x"}]
            """;
        private readonly Dictionary<string, int> calls = new();
        public volatile bool Offline;
        public string DrillholesJson = DefaultDrillholes;
        public HttpStatusCode DrillholesStatus = HttpStatusCode.OK;
        public TaskCompletionSource? Hold;   // holds reachability probes
        public HashSet<string> Methods { get; } = new();
        public int Calls(string path) { lock (calls) return calls.GetValueOrDefault(path); }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            lock (calls) { calls[path] = calls.GetValueOrDefault(path) + 1; Methods.Add($"{request.Method} {path}"); }
            await Task.Yield();
            if (path == "/" && Hold is { } hold) await hold.Task;
            if (Offline) throw new HttpRequestException("Connection refused");
            var (status, json) = path == DrillholesClient.Path ? (DrillholesStatus, DrillholesJson) : (HttpStatusCode.NotFound, "");
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
    private sealed class FakeExport : ICsvExportService
    {
        public IReadOnlyList<MeasurementResult>? Rows { get; private set; }
        public MeasurementSession? Session { get; private set; }
        public Task<bool> ExportAsync(IReadOnlyList<MeasurementResult> rows, MeasurementSession session, CancellationToken token)
        { Rows=rows; Session=session; return Task.FromResult(true); }
    }
    private sealed class ControlledMeasurements : IMeasurementService
    {
        public TimeSpan LastDuration { get; private set; } = TimeSpan.FromSeconds(5);
        public int Calls { get; private set; }
        public bool HonorCancellation { get; init; } = true;
        private TaskCompletionSource<CaptureData>? pending;
        private Action<TimeSpan>? reportRemaining;
        private DateTimeOffset start;
        public async Task<CaptureData> CaptureAsync(DateTimeOffset started, TimeSpan duration, Action<TimeSpan> report, CancellationToken token)
        {
            Calls++; start=started; reportRemaining=report; LastDuration=duration;
            var operation = new TaskCompletionSource<CaptureData>(TaskCreationOptions.RunContinuationsAsynchronously);
            pending=operation;
            using var registration = token.Register(() => { if (HonorCancellation) operation.TrySetCanceled(token); });
            return await operation.Task;
        }
        public void Report(TimeSpan remaining) => reportRemaining?.Invoke(remaining);
        public void Fail() => pending?.SetException(new InvalidOperationException("Simulated failure"));
        public DeviceReading? Reading { get; set; }
        // One sample per second of the capture's own duration (1, 2, 3, …), like the real sample rate.
        public void Complete() => pending?.SetResult(new CaptureData(start, start + LastDuration,
            Enumerable.Range(1, Math.Max(1, (int)LastDuration.TotalSeconds)).Select(i => (double)i).ToArray(), "cps", start + LastDuration + TimeSpan.FromMilliseconds(20), Reading));
    }
}
