using System.Diagnostics;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
using System.Windows.Threading;
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
        var link = new ControlledMeasurements();
        var form = new CoreInformationViewModel(core => saved = core, link, @"C:\data\measurements.db");
        Check(form.DatabasePath == @"C:\data\measurements.db", "Device Status shows the configured database path");
        Check(!form.IsDeviceConnected && form.DeviceStatus == "Disconnected" && form.DeviceIdentifier == "", "Clear disconnected state");
        link.Connect(new DeviceConnection("CT007-A1", "00:1B:44:11:3A:B7"));
        Check(form.IsDeviceConnected && form.DeviceName == "CT007-A1" && form.DeviceIdentifier == "00:1B:44:11:3A:B7", "Connected device name and ID");
        link.Connect(null);
        Check(!form.IsDeviceConnected && form.DeviceName == "No CT007 device connected" && form.DeviceIdentifier == "", "No stale device details");
        Check(form.GammGuardDatabasePath == "Not available", "Gamm Guard path: Not available without a device");
        link.Connect(new DeviceConnection("CT007-A1", "00:1B:44:11:3A:B7", @"D:\GammGuard\data\gammguard.sqlite"));
        Check(form.GammGuardDatabasePath == @"D:\GammGuard\data\gammguard.sqlite" && form.DatabasePath == @"C:\data\measurements.db",
            "Gamm Guard path reported by the device, separate from the measurement database");
        link.Connect(new DeviceConnection("CT007-B2", "00:1B:44:11:3A:C8"));
        Check(form.GammGuardDatabasePath == "Not available", "A device without a reported path shows Not available, not a stale path");
        link.Connect(null);
        var account = CoreInformationViewModel.CurrentWindowsAccount();
        Check(account.Length > 0 && account.EndsWith(Environment.UserName) &&
            (account == Environment.UserName || account == $"{Environment.UserDomainName}\\{Environment.UserName}") &&
            account != Environment.MachineName, "Windows account: DOMAIN\\user or user, never the computer name alone");
        Check(form.Operator == account, "Operator autofilled for a new form");
        form.SaveCommand.Execute(null);
        Check(saved is null && form.DrillholeIdError == "Enter a drillhole." && form.DrillholeNameError == "Enter a project.",
            "Required field validation uses the new labels");
        form.DrillholeId = "DH-001"; form.DrillholeName = "North Ridge"; form.ProjectArea = "Campaign 7";
        Check(form.IsValid && typeof(CoreInformationViewModel).GetProperty("CoreId") is null && typeof(CoreSnapshot).GetProperty("CoreId") is null,
            "Valid with only Drillhole and Project; no Core ID or depth fields");
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
        Check(vm.NextFrom == "" && vm.Step == "" && !vm.ToggleMeasurementCommand.CanExecute(null),
            "A new session has no saved progress: Start needs a starting depth and step");
        vm.NextFrom = "150"; Check(!vm.ToggleMeasurementCommand.CanExecute(null), "Start still needs a step");
        vm.Step = "0.5";
        Check(vm.ToggleMeasurementCommand.CanExecute(null) && vm.DepthError == "", "Starting depth and step entered");
        vm.ToggleMeasurementCommand.Execute(null);
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
        vm.Move(third, first, false);
        Check(vm.Results[0] == third && third.Number == 1 && third.CaptureNumber == 3, "Reorder keeps capture identity");
        vm.Move(third, second, true);
        Check(vm.Results[2] == third, "Drop after target");
        dialogs.Confirm = false; vm.DeleteCommand.Execute(second);
        Check(vm.Results.Count == 3, "Cancel deletion");
        dialogs.Confirm = true; vm.DeleteCommand.Execute(second);
        Check(vm.Results.Count == 2 && third.Number == 2 && third.CaptureNumber == 3, "Deletion renumbers display only");
        vm.TriggerCommand.Execute(null); vm.Step = "0.25"; fake.Complete(); await Until(() => vm.CanTrigger);
        Check(vm.Results[2].CaptureNumber == 4, "Deleted capture numbers are not reused");
        Check(vm.Results[2] is { FromMm: 151500, ToMm: 152000, StepMm: 500 } && vm.NextFrom == "152.00" && vm.Step == "0.25",
            "A step change during a capture applies to the next capture only");
        Check(vm.SessionId == firstSession && vm.Results.All(r => r.SessionId == firstSession),
            "One session ID for every capture of the session");
        await Until(() => store.Rows(firstSession).Select(r => r.CaptureNumber).SequenceEqual(vm.Results.Select(r => r.CaptureNumber)));
        Check(store.SaveCount > store.Rows(firstSession).Count, "Re-saving the same rows does not duplicate them");
        vm.Move(vm.Results[2], first, false);
        vm.ExportCommand.Execute(null); await Until(() => vm.ExportCommand.CanExecute(null));
        Check(export.Rows?.Select(r => r.CaptureNumber).SequenceEqual(new[] {4,1,3}) == true, "Export keeps display order");
        var exported = export.Session ?? throw new InvalidOperationException("Export receives the session");
        Check(exported.Id == firstSession && exported.DrillholeId == "DH-001" && exported.DrillholeName == "North Ridge" &&
            exported.Project == "Campaign 7" && exported.Operator == "Field Tech", "Export receives the exported session's metadata");
        await Until(() => store.Rows(firstSession).Select(r => r.CaptureNumber).SequenceEqual(new[] {4,1,3}));
        Check(store.Rows(firstSession).Select(r => r.Number).SequenceEqual(new[] {1,2,3}), "Reorder updates stored No");
        Check(store.Rows(firstSession).Select(r => (r.CaptureNumber, r.FromMm)).SequenceEqual(new (int, long?)[] {(4,151500),(1,150000),(3,151000)}),
            "Reordering keeps each row's depths");
        Check(store.Progress(firstSession) == new DepthProgress(152000, 250), "Next depth and step saved with the session");
        var csv = CsvExportService.Serialize(vm.Results.ToArray(), exported);
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Check(lines[0] == "\"No\",\"Capture No\",\"Start Time\",\"End Time\",\"Sample\",\"ts_ms\",\"count0\",\"count0_raw\"," +
            "\"hdm\",\"hdm_raw\",\"dose_rate\",\"Drillhole\",\"Project\",\"Campaign\",\"Operator\",\"Notes\",\"session_id\"",
            "CSV header: measurement columns, session metadata (Campaign once), session ID last");
        Check(lines[0].Split(',').Count(h => h == "\"Campaign\"") == 1, "Campaign exported once");
        Check(lines.Skip(1).All(l => l.EndsWith($"\"DH-001\",\"North Ridge\",\"Campaign 7\",\"Field Tech\",\"\",\"{firstSession}\"")),
            "Session metadata repeated on every row");
        Check(lines[1].StartsWith("\"1\",\"4\",") && lines.Length == 4, "CSV display/capture numbers in saved order");
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
            new[] {"No","From (m)","To (m)","Step (m)","Sample","ts_ms","count0","count0_raw","hdm","hdm_raw","dose_rate"}),
            "Table column order; From/To/Step shown; Capture No, times and session hidden");
        Check(MeasurementColumns.All.Where(c => c.IncludeInCsv).Count() == 12 && lines[0].Contains("\"Capture No\",\"Start Time\""),
            "Hidden columns stay in CSV; CSV columns unchanged");
        Check(third is { FromMetres: 151.0m, ToMetres: 151.5m, StepMetres: 0.5m } &&
            new[] { MeasurementColumns.From, MeasurementColumns.To, MeasurementColumns.Step }.All(c =>
                c.Binding != c.Label && typeof(MeasurementResult).GetProperty(c.Binding)?.CanWrite == false),
            "Depth columns show saved metres, are read-only and keep labels separate from field names");
        Check(MeasurementColumns.All.All(c => Resolve(c.Binding, withReading) is { } bound && Equals(bound, c.Value(withReading))),
            "Every binding resolves to the same value the CSV writes");
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
            vm.Results.Select(r => r.CaptureNumber).Take(3).SequenceEqual(new[] {4,1,3}), "Restart appends after the saved order");
        await Until(() => store.Rows(firstSession).Select(r => r.CaptureNumber).SequenceEqual(new[] {4,1,3,5}));
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
        Check(vm.Results.Count == count && vm.Countdown == "00:05" && vm.Notification.StartsWith("Capture failed"), "Failure recovers Ready without row");
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
        Check(vm.SessionId == firstSession && vm.Results.Count == count && vm.NextFrom == "160.25" && vm.Step == "0.25",
            "The same Drillhole ID reopens the same session with its progress");
        vm.NewMeasurementCommand.Execute(null);
        var core2 = core with { DrillholeId = "DH-002" };
        vm.SetCore(core2); await Until(() => vm.SessionId is not null);
        var secondSession = vm.SessionId!.Value;
        Check(secondSession != firstSession && vm.Results.Count == 0 && vm.NextFrom == "" && vm.Step == "",
            "Another Drillhole ID gets its own session with its own (empty) progress");
        vm.NextFrom = "10"; vm.Step = "0.5"; vm.ToggleMeasurementCommand.Execute(null);
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
            lateVm.SetCore(core); lateVm.NextFrom = "150"; lateVm.Step = "0.5"; lateVm.ToggleMeasurementCommand.Execute(null); lateVm.TriggerCommand.Execute(null);
            lateVm.ToggleMeasurementCommand.Execute(null); lateVm.ToggleMeasurementCommand.Execute(null);
            late.Report(TimeSpan.FromSeconds(1)); lateVm.TriggerCommand.Execute(null);
            Check(!lateVm.CanTrigger && late.Calls == 1 && lateVm.Countdown == "00:05" && lateVm.CaptureProgress == 0,
                "Late progress and rearm cannot overlap cancellation cleanup");
            late.Complete(); await Until(() => lateVm.CanTrigger);
            Check(lateVm.Results.Count == 0, "Late cancelled result discarded");
        }
        using (var main = new MainViewModel(new MemoryRepository()))
        {
            main.Core.DrillholeId="retained"; main.ShowMeasurementCommand.Execute(null); main.ShowCoreCommand.Execute(null);
            Check(main.CurrentPage == main.Core && main.Core.DrillholeId == "retained", "Navigation retains form");
            var retainedCore = main.Core;
            var retainedMeasurement = main.Measurement;
            Check(main.IsSidebarExpanded, "Sidebar initially expanded");
            main.ToggleSidebarCommand.Execute(null);
            Check(!main.IsSidebarExpanded && main.SidebarToggleLabel == "Expand sidebar", "Collapse sidebar");
            Check(main.CurrentPage == retainedCore && main.Core.DrillholeId == "retained", "Collapse retains page/form");
            main.ShowMeasurementCommand.Execute(null);
            Check(!main.IsSidebarExpanded && main.CurrentPage == retainedMeasurement && main.IsMeasurementSelected,
                "Navigation retains collapsed state and measurement instance");
            main.ToggleSidebarCommand.Execute(null);
            Check(main.IsSidebarExpanded && main.SidebarToggleLabel == "Collapse sidebar" && main.CurrentPage == retainedMeasurement,
                "Expand retains selected page");
            main.Measurement.SetCore(core); main.Measurement.NextFrom = "150"; main.Measurement.Step = "0.5"; main.Measurement.ToggleMeasurementCommand.Execute(null);
            main.Measurement.TriggerCommand.Execute(null); main.ShowCoreCommand.Execute(null);
            var navigationSession = main.Measurement.SessionId;
            main.ShowMeasurementCommand.Execute(null); main.ShowCoreCommand.Execute(null);
            Check(main.Measurement.IsCapturing && main.CurrentPage == retainedCore &&
                main.Measurement.SessionId == navigationSession, "Navigation does not duplicate, reset or start a session");
            main.Measurement.ToggleMeasurementCommand.Execute(null);
        }
        var service = new MockMeasurementService(); var start = DateTimeOffset.Now; var elapsed = Stopwatch.StartNew();
        var remaining = new List<TimeSpan>();
        var capture = await service.CaptureAsync(start, remaining.Add, CancellationToken.None);
        Check(elapsed.Elapsed.TotalSeconds >= 5 && capture.Samples.Count == 5, "Five-second simulation");
        Check(capture.EndTime-start == TimeSpan.FromSeconds(5) && remaining[^1] == TimeSpan.Zero, "Exact interval and countdown zero");
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        try { await service.CaptureAsync(start, _ => {}, cancel.Token); throw new Exception("Cancellation ignored"); }
        catch (OperationCanceledException) { }
        var closingService = new ControlledMeasurements();
        var closingVm = new MeasurementViewModel(closingService, export, dialogs, new MemoryRepository()); closingVm.SetCore(core); closingVm.NextFrom = "150"; closingVm.Step = "0.5";
        closingVm.ToggleMeasurementCommand.Execute(null); closingVm.TriggerCommand.Execute(null); closingVm.Dispose(); await Until(() => !closingVm.IsCapturing);
        Check(closingVm.Results.Count == 0, "Closing cancels without partial result");

        // Failed saves are reported and the rows stay in the table.
        var failing = new MemoryRepository();
        var failingService = new ControlledMeasurements();
        using (var failingVm = new MeasurementViewModel(failingService, export, dialogs, failing))
        {
            failingVm.SetCore(core); failingVm.NextFrom = "150"; failingVm.Step = "0.5";
            await Until(() => failing.Progress(failingVm.SessionId!.Value) is not null);
            failing.Fail = true;
            failingVm.ToggleMeasurementCommand.Execute(null);
            failingVm.TriggerCommand.Execute(null); failingService.Complete();
            await Until(() => failingVm.Notification.StartsWith("Saving to the database failed"));
            Check(failingVm.Results.Count == 1, "Save failure keeps the captured row");
            failingVm.ToggleMeasurementCommand.Execute(null);
            failingVm.NewMeasurementCommand.Execute(null);
            Check(failingVm.Results.Count == 1, "New Measurement refuses to drop unsaved rows");
            failing.Fail = false;
            failingVm.ToggleMeasurementCommand.Execute(null);
            await Until(() => failing.Rows(failingVm.SessionId!.Value).Count == 1);
            failingVm.TriggerCommand.Execute(null); failingService.Complete(); await Until(() => failingVm.CanTrigger);
            var id = failingVm.SessionId!.Value;
            await Until(() => failing.Rows(id).Count == 2);
        }

        // DEMO ts_ms: random preview values for rows without a device value, kept in memory only.
        var demoService = new ControlledMeasurements();
        using (var demoVm = new MeasurementViewModel(demoService, export, dialogs, new MemoryRepository(), showDemoTsMs: true))
        {
            demoVm.SetCore(core with { DrillholeId = "DEMO-1" }); demoVm.NextFrom = "10"; demoVm.Step = "0.5";
            demoVm.ToggleMeasurementCommand.Execute(null);
            demoVm.TriggerCommand.Execute(null); demoService.Complete(); await Until(() => demoVm.CanTrigger);
            demoVm.TriggerCommand.Execute(null); demoVm.Step = "0.25"; demoService.Complete(); await Until(() => demoVm.CanTrigger);
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
            demoVm.Move(demoRows[3], demoRows[0], false);
            Check(demoRows.Select(r => r.DisplayTsMs).SequenceEqual(shown) && demoVm.Results[0] == demoRows[3],
                "Demo values stay the same after reordering (generated once)");
            var demoCsv = CsvExportService.Serialize(demoVm.Results.ToArray(), new MeasurementSession(demoVm.SessionId!.Value, DateTimeOffset.Now));
            var tsColumn = Array.IndexOf(demoCsv.Split("\r\n")[0].Split(','), "\"ts_ms\"");
            var tsCells = demoCsv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(l => l.Split(',')[tsColumn]).ToList();
            Check(tsCells.SequenceEqual(new[] {"\"4321\"", "\"\"", "\"\"", "\"\""}), "CSV exports the device ts_ms only, never demo values");
        }
        using (var plain = new MeasurementViewModel(new ControlledMeasurements(), export, dialogs, new MemoryRepository()))
        {
            Check(!plain.ShowDemoTsMs, "Demo mode is off unless enabled");
        }

        await SqliteChecks(core);
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
                vm.NextFrom = "150"; vm.Step = "0.5"; vm.ToggleMeasurementCommand.Execute(null);
                captured = vm.SessionId!.Value;
                for (int i=0; i<2; i++) { vm.TriggerCommand.Execute(null); service.Complete(); await Until(() => vm.CanTrigger); }
                vm.ToggleMeasurementCommand.Execute(null); vm.Step = "0.25"; vm.ToggleMeasurementCommand.Execute(null);
                vm.TriggerCommand.Execute(null); service.Complete(); await Until(() => vm.CanTrigger);
                vm.Move(vm.Results[2], vm.Results[0], false);
            }
            using (var restarted = new MeasurementViewModel(new ControlledMeasurements(), new FakeExport(), new FakeDialogs(),
                       new SqliteMeasurementRepository(path)))
            {
                await restarted.RestoreLatestSessionAsync();
                Check(restarted.SessionId == captured && restarted.Results.Count == 3 && restarted.State == MeasurementState.Idle &&
                    restarted.Results.Select(r => r.CaptureNumber).SequenceEqual(new[] {3,1,2}) &&
                    restarted.Results.Select(r => r.Number).SequenceEqual(new[] {1,2,3}), "Saved order restored after restart");
                Check(restarted.Results.Select(r => (r.FromMm, r.ToMm, r.StepMm)).SequenceEqual(new (long?, long?, long?)[]
                    { (151000, 151250, 250), (150000, 150500, 500), (150500, 151000, 500) }), "Depths restored unchanged by reordering");
                Check(restarted.NextFrom == "151.25" && restarted.Step == "0.25", "Next depth and step restored after restart");
                var restoredSession = await new SqliteMeasurementRepository(path).LoadLatestSessionAsync();
                Check(CsvExportService.Serialize(restarted.Results.ToArray(), restoredSession!).Split("\r\n")[1].StartsWith("\"1\",\"3\","), "CSV follows saved order");
                restarted.SetCore(core); restarted.ToggleMeasurementCommand.Execute(null);
                Check(restarted.SessionId == captured && restarted.State == MeasurementState.Ready, "Start after restart continues the restored session");
            }
            using (var continued = new MeasurementViewModel(service, new FakeExport(), new FakeDialogs(), new SqliteMeasurementRepository(path)))
            {
                // Without restoring first: saving the same core still finds the same session by Drillhole ID.
                continued.SetCore(core); await Until(() => continued.SessionId is not null);
                Check(continued.SessionId == captured && continued.NextFrom == "151.25" && continued.Step == "0.25",
                    "Same Drillhole ID after restart → same session and progress");
                continued.ToggleMeasurementCommand.Execute(null);
                continued.TriggerCommand.Execute(null); service.Complete(); await Until(() => continued.CanTrigger);
                Check(continued.Results.Select(r => r.CaptureNumber).SequenceEqual(new[] {3,1,2,4}) &&
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
                demoVm.NextFrom = "1"; demoVm.Step = "0.5"; demoVm.ToggleMeasurementCommand.Execute(null);
                demoVm.TriggerCommand.Execute(null); demoService.Complete(); await Until(() => demoVm.CanTrigger);
                demoService.Reading = new DeviceReading(TsMs: 777);
                demoVm.TriggerCommand.Execute(null); demoService.Complete(); await Until(() => demoVm.CanTrigger);
                Check(demoVm.Results[0].IsDemoTsMs, "Demo value shown for the first row");
            }
            var demoStored = await new SqliteMeasurementRepository(path).LoadAsync(demoSession);
            Check(demoStored.Count == 2 && demoStored[0].Reading.TsMs is null && demoStored[0].DemoTsMs is null && demoStored[1].Reading.TsMs == 777,
                "SQLite holds no demo values; device ts_ms is saved");

            var final = await new SqliteMeasurementRepository(path).LoadAsync(captured);
            Check(final.Select(r => r.CaptureNumber).SequenceEqual(new[] {3,1,2,4}), "Stored order and rows complete, no duplicates");
            Check(await new SqliteMeasurementRepository(path).LoadProgressAsync(captured) == new DepthProgress(151500, 250),
                "Progress advanced exactly once per capture");

            // Schema v3 on a new database: short ID names, enforced foreign key, index on session_id.
            using (var check = new SqliteConnection($"Data Source={path};Foreign Keys=True"))
            {
                check.Open();
                Check(Scalar(check, "PRAGMA user_version") == "4", "Schema version 4");
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
                    Check(Scalar(check, "PRAGMA user_version") == "4" &&
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
    { public bool Confirm { get; set; } public bool ConfirmDelete() => Confirm; }
    private sealed class MemoryRepository : IMeasurementRepository
    {
        public string DatabasePath => ":memory:";
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
        public Task<MeasurementSession> ResolveSessionAsync(MeasurementSession candidate)
        { lock (gate) return Task.FromResult(sessions.TryGetValue(candidate.DrillholeId!, out var found) ? found : sessions[candidate.DrillholeId!] = candidate); }
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
        public Task DeleteAsync(Guid session, int capture) { lock (gate) { rows.Remove((session, capture)); numbers.Remove((session, capture)); } return Task.CompletedTask; }
        public Task<MeasurementSession?> LoadLatestSessionAsync() => Task.FromResult<MeasurementSession?>(null);
        public Task<IReadOnlyList<MeasurementResult>> LoadAsync(Guid session) => Task.FromResult<IReadOnlyList<MeasurementResult>>(Rows(session));
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
        public TimeSpan Duration => TimeSpan.FromSeconds(5);
        public DeviceConnection? ConnectedDevice { get; private set; }
        public event EventHandler? ConnectionChanged;
        public void Connect(DeviceConnection? device) { ConnectedDevice = device; ConnectionChanged?.Invoke(this, EventArgs.Empty); }
        public int Calls { get; private set; }
        public bool HonorCancellation { get; init; } = true;
        private TaskCompletionSource<CaptureData>? pending;
        private Action<TimeSpan>? reportRemaining;
        private DateTimeOffset start;
        public async Task<CaptureData> CaptureAsync(DateTimeOffset started, Action<TimeSpan> report, CancellationToken token)
        {
            Calls++; start=started; reportRemaining=report;
            var operation = new TaskCompletionSource<CaptureData>(TaskCreationOptions.RunContinuationsAsynchronously);
            pending=operation;
            using var registration = token.Register(() => { if (HonorCancellation) operation.TrySetCanceled(token); });
            return await operation.Task;
        }
        public void Report(TimeSpan remaining) => reportRemaining?.Invoke(remaining);
        public void Fail() => pending?.SetException(new InvalidOperationException("Simulated failure"));
        public DeviceReading? Reading { get; set; }
        public void Complete() => pending?.SetResult(new CaptureData(start,start.AddSeconds(5),new double[] {1,2,3,4,5},"cps",start.AddSeconds(5).AddMilliseconds(20),Reading));
    }
}
