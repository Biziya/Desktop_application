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
        var form = new CoreInformationViewModel(core => saved = core);
        form.SaveCommand.Execute(null);
        Check(saved is null && form.DrillholeIdError.Length > 0, "Required field validation");
        form.DrillholeId = "DH-001"; form.DrillholeName = "North Ridge"; form.CoreId = "C-001";
        form.DepthFrom = "150"; form.DepthTo = "150";
        Check(!form.IsValid, "Zero length rejected");
        form.DepthTo = "149"; Check(!form.IsValid, "Reversed range rejected");
        form.DepthTo = "151.25"; form.SaveCommand.Execute(null);
        Check(saved is { CoreDepth: 1.25m } && form.CoreDepth == "1.25", "Calculated depth and save");
        Check(!CoreInformationViewModel.TryDepth("invalid",out _), "Invalid numeric input");
        var core = saved ?? throw new InvalidOperationException("No saved core");
        var fake = new ControlledMeasurements(); var dialogs = new FakeDialogs(); var export = new FakeExport();
        var store = new MemoryRepository();
        using var vm = new MeasurementViewModel(fake, export, dialogs, store);
        Check(!vm.ToggleMeasurementCommand.CanExecute(null), "Capture requires saved core");
        Check(vm.MeasurementSessionId is null, "No session before Start");
        Check(!vm.ExportCommand.CanExecute(null), "Empty export disabled");
        vm.TriggerCommand.Execute(null);
        Check(fake.Calls == 0, "Idle triggers ignored");
        vm.SetCore(core);
        vm.ToggleMeasurementCommand.Execute(null);
        Check(vm.State == MeasurementState.Ready && vm.IsMeasurementModeActive && fake.Calls == 0 &&
            vm.Results.Count == 0 && vm.Countdown == "00:05" && vm.CaptureProgress == 0, "Start arms without capturing");
        var firstSession = vm.MeasurementSessionId ?? throw new InvalidOperationException("Start creates a session");
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
        var first = vm.Results[0]; var second = vm.Results[1]; var third = vm.Results[2];
        vm.Move(third, first, false);
        Check(vm.Results[0] == third && third.Number == 1 && third.CaptureNumber == 3, "Reorder keeps capture identity");
        vm.Move(third, second, true);
        Check(vm.Results[2] == third, "Drop after target");
        dialogs.Confirm = false; vm.DeleteCommand.Execute(second);
        Check(vm.Results.Count == 3, "Cancel deletion");
        dialogs.Confirm = true; vm.DeleteCommand.Execute(second);
        Check(vm.Results.Count == 2 && third.Number == 2 && third.CaptureNumber == 3, "Deletion renumbers display only");
        vm.TriggerCommand.Execute(null); fake.Complete(); await Until(() => vm.CanTrigger);
        Check(vm.Results[2].CaptureNumber == 4, "Deleted capture numbers are not reused");
        Check(vm.MeasurementSessionId == firstSession && vm.Results.All(r => r.MeasurementSessionId == firstSession),
            "One session ID for every capture of the session");
        await Until(() => store.Rows(firstSession).Select(r => r.CaptureNumber).SequenceEqual(vm.Results.Select(r => r.CaptureNumber)));
        Check(store.SaveCount > store.Rows(firstSession).Count, "Re-saving the same rows does not duplicate them");
        vm.Move(vm.Results[2], first, false);
        vm.ExportCommand.Execute(null); await Until(() => vm.ExportCommand.CanExecute(null));
        Check(export.Rows?.Select(r => r.CaptureNumber).SequenceEqual(new[] {4,1,3}) == true, "Export keeps display order");
        await Until(() => store.Rows(firstSession).Select(r => r.CaptureNumber).SequenceEqual(new[] {4,1,3}));
        Check(store.Rows(firstSession).Select(r => r.Number).SequenceEqual(new[] {1,2,3}), "Reorder updates stored No");
        var csv = CsvExportService.Serialize(vm.Results.ToArray());
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Check(lines[0] == "\"No\",\"Capture No\",\"Start Time\",\"End Time\",\"Sample\",\"ts_ms\",\"count0\",\"count0_raw\"," +
            "\"hdm\",\"hdm_raw\",\"dose_rate\",\"MeasurementSessionId\"", "CSV header order and session ID last");
        Check(lines[1].StartsWith("\"1\",\"4\",") && lines[1].EndsWith($",\"{firstSession}\"") && lines.Length == 4,
            "CSV display/capture numbers and session ID");
        Check(CsvExportService.Serialize(new[] {first}).Contains(triggerTime.ToString("O") + "\",\"" + triggerTime.AddSeconds(5).ToString("O")),
            "CSV retains fractional seconds and offset for start/end");
        Check(!csv.Contains("Average") && !csv.Contains("Completed At"), "CSV has only configured columns");
        var reading = new DeviceReading(1760152570851, 0.1 + 0.2, 125, 1.5e-7, -3, 12.25);
        var withReading = new MeasurementResult { MeasurementSessionId=firstSession, CaptureNumber=99, StartTime=first.StartTime,
            EndTime=first.EndTime, SampleCount=5, Reading=reading };
        Check(CsvExportService.Serialize(new[] {withReading}).Contains(
            "\"1760152570851\",\"0.30000000000000004\",\"125\",\"1.5E-07\",\"-3\",\"12.25\""), "CSV device values keep full precision");
        Check(CsvExportService.Serialize(new[] {first}).Contains("\"5\",\"\",\"\",\"\",\"\",\"\",\"\","), "Missing device values are empty cells");
        var otherSession = new MeasurementResult { MeasurementSessionId=Guid.NewGuid(), CaptureNumber=1, StartTime=first.StartTime,
            EndTime=first.EndTime, SampleCount=5 };
        try { CsvExportService.Serialize(new[] {first, otherSession}); throw new Exception("Mixed-session export accepted"); }
        catch (ArgumentException) { }
        Check(MeasurementColumns.All.Where(c => c.ShowInTable).Select(c => c.Label).SequenceEqual(
            new[] {"No","Capture No","Sample","ts_ms","count0","count0_raw","hdm","hdm_raw","dose_rate"}), "Table column order; times and session hidden");
        Check(MeasurementColumns.All.All(c => Resolve(c.Binding, withReading) is { } bound && Equals(bound, c.Value(withReading))),
            "Every binding resolves to the same value the CSV writes");
        Check(MeasurementColumns.All.Last() is { Label: "Session", CsvHeader: "MeasurementSessionId", ShowInTable: false },
            "Labels are independent of CSV headers");
        var count = vm.Results.Count;
        vm.ToggleMeasurementCommand.Execute(null);
        Check(vm.State == MeasurementState.Idle && vm.Results.Count == count, "Stop keeps completed rows");
        vm.TriggerCommand.Execute(null); Check(vm.Results.Count == count, "Stopped trigger ignored");
        vm.ToggleMeasurementCommand.Execute(null);
        var secondSession = vm.MeasurementSessionId;
        Check(secondSession is not null && secondSession != firstSession && vm.Results.Count == 0 &&
            store.Rows(firstSession).Count == count, "Start begins a new session; the previous one stays stored");
        count = 0;
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
        vm.SetCore(null); Check(!vm.ToggleMeasurementCommand.CanExecute(null) && vm.State == MeasurementState.Idle, "Editing invalidates saved form and disarms");

        // A future service may return or report progress after cancellation. Ignore it and prevent overlap.
        var late = new ControlledMeasurements { HonorCancellation = false };
        using (var lateVm = new MeasurementViewModel(late, export, dialogs, new MemoryRepository()))
        {
            lateVm.SetCore(core); lateVm.ToggleMeasurementCommand.Execute(null); lateVm.TriggerCommand.Execute(null);
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
            main.Measurement.SetCore(core); main.Measurement.ToggleMeasurementCommand.Execute(null);
            main.Measurement.TriggerCommand.Execute(null); main.ShowCoreCommand.Execute(null);
            var navigationSession = main.Measurement.MeasurementSessionId;
            main.ShowMeasurementCommand.Execute(null); main.ShowCoreCommand.Execute(null);
            Check(main.Measurement.IsCapturing && main.CurrentPage == retainedCore &&
                main.Measurement.MeasurementSessionId == navigationSession, "Navigation does not duplicate, reset or start a session");
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
        var closingVm = new MeasurementViewModel(closingService, export, dialogs, new MemoryRepository()); closingVm.SetCore(core);
        closingVm.ToggleMeasurementCommand.Execute(null); closingVm.TriggerCommand.Execute(null); closingVm.Dispose(); await Until(() => !closingVm.IsCapturing);
        Check(closingVm.Results.Count == 0, "Closing cancels without partial result");

        // Failed saves are reported and the rows stay in the table.
        var failing = new MemoryRepository { Fail = true };
        var failingService = new ControlledMeasurements();
        using (var failingVm = new MeasurementViewModel(failingService, export, dialogs, failing))
        {
            failingVm.SetCore(core); failingVm.ToggleMeasurementCommand.Execute(null);
            failingVm.TriggerCommand.Execute(null); failingService.Complete();
            await Until(() => failingVm.Notification.StartsWith("Saving to the database failed"));
            Check(failingVm.Results.Count == 1, "Save failure keeps the captured row");
            failing.Fail = false;
            failingVm.TriggerCommand.Execute(null); failingService.Complete(); await Until(() => failingVm.CanTrigger);
            var id = failingVm.MeasurementSessionId!.Value;
            await Until(() => failing.Rows(id).Count == 2);
        }

        await SqliteChecks(core);
    }

    private static async Task SqliteChecks(CoreSnapshot core)
    {
        var path = Path.Combine(Path.GetTempPath(), $"ct007-smoke-{Guid.NewGuid():N}.db");

        try
        {
            var a = MeasurementSession.New(); var b = MeasurementSession.New();
            var start = new DateTimeOffset(2026,10,11,11,16,10,851,TimeSpan.FromHours(8)).AddTicks(1234);
            MeasurementResult Row(MeasurementSession s, int capture, DeviceReading reading) => new()
            { MeasurementSessionId=s.Id, CaptureNumber=capture, StartTime=start, EndTime=start.AddSeconds(5), SampleCount=5, Reading=reading };
            var a1 = Row(a, 1, new DeviceReading(1760152570851, 0.1 + 0.2, 125, 1.5e-7, -3, 12.25));
            var a2 = Row(a, 2, DeviceReading.Empty);
            var store = new SqliteMeasurementRepository(path);
            await store.SaveAsync(a, new[] {a1, a2});
            await store.SaveAsync(a, new[] {a2, a1});
            await store.SaveAsync(b, new[] {Row(b, 1, DeviceReading.Empty)});
            try { await store.SaveAsync(b, new[] {a1}); throw new Exception("Cross-session save accepted"); }
            catch (ArgumentException) { }

            var reopened = new SqliteMeasurementRepository(path);
            var loaded = await reopened.LoadAsync(a.Id);
            Check(loaded.Select(r => r.CaptureNumber).SequenceEqual(new[] {2,1}) && loaded[0].Number == 1, "Upsert: no duplicates, display order kept");
            Check(loaded[1].StartTime == start && loaded[1].StartTime.Offset == start.Offset && loaded[1].EndTime == start.AddSeconds(5) &&
                loaded[1].Reading == a1.Reading && loaded[0].Reading == DeviceReading.Empty && loaded[1].SampleCount == 5,
                "Stored values, precision and nulls survive a reopen");
            Check((await reopened.LoadAsync(b.Id)).All(r => r.MeasurementSessionId == b.Id) && (await reopened.LoadAsync(b.Id)).Count == 1,
                "Loading one session excludes others");
            Check((await reopened.LoadLatestSessionAsync())?.Id == b.Id, "Latest session");
            await reopened.DeleteAsync(a.Id, 2);
            Check((await reopened.LoadAsync(a.Id)).Count == 1, "Delete removes only that capture");

            // Restart: a new ViewModel shows the last session without creating one; Start then creates a new one.
            var service = new ControlledMeasurements();
            Guid captured;
            using (var vm = new MeasurementViewModel(service, new FakeExport(), new FakeDialogs(), reopened))
            {
                vm.SetCore(core); vm.ToggleMeasurementCommand.Execute(null);
                captured = vm.MeasurementSessionId!.Value;
                for (int i=0; i<2; i++) { vm.TriggerCommand.Execute(null); service.Complete(); await Until(() => vm.CanTrigger); }
            }
            using (var restarted = new MeasurementViewModel(new ControlledMeasurements(), new FakeExport(), new FakeDialogs(),
                       new SqliteMeasurementRepository(path)))
            {
                await restarted.RestoreLatestSessionAsync();
                Check(restarted.MeasurementSessionId == captured && restarted.Results.Count == 2 && restarted.State == MeasurementState.Idle &&
                    restarted.Results.Select(r => r.CaptureNumber).SequenceEqual(new[] {1,2}), "Records available after restart");
                restarted.SetCore(core); restarted.ToggleMeasurementCommand.Execute(null);
                Check(restarted.MeasurementSessionId != captured && restarted.Results.Count == 0, "New session after restart gets a new ID");
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
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
        private readonly object gate = new();
        private readonly Dictionary<(Guid, int), MeasurementResult> rows = new();
        private readonly Dictionary<(Guid, int), int> numbers = new();
        public volatile bool Fail;
        public int SaveCount { get; private set; }
        public List<MeasurementResult> Rows(Guid session)
        { lock (gate) return rows.Where(p => p.Key.Item1 == session).OrderBy(p => numbers[p.Key]).Select(p => p.Value).ToList(); }
        public Task SaveAsync(MeasurementSession session, IReadOnlyList<MeasurementResult> list)
        {
            if (Fail) throw new IOException("disk full");
            lock (gate) for (int i=0; i<list.Count; i++) { rows[(session.Id, list[i].CaptureNumber)] = list[i]; numbers[(session.Id, list[i].CaptureNumber)] = i+1; SaveCount++; }
            return Task.CompletedTask;
        }
        public Task DeleteAsync(Guid session, int capture) { lock (gate) { rows.Remove((session, capture)); numbers.Remove((session, capture)); } return Task.CompletedTask; }
        public Task<MeasurementSession?> LoadLatestSessionAsync() => Task.FromResult<MeasurementSession?>(null);
        public Task<IReadOnlyList<MeasurementResult>> LoadAsync(Guid session) => Task.FromResult<IReadOnlyList<MeasurementResult>>(Rows(session));
    }
    private sealed class FakeExport : ICsvExportService
    {
        public IReadOnlyList<MeasurementResult>? Rows { get; private set; }
        public Task<bool> ExportAsync(IReadOnlyList<MeasurementResult> rows, CancellationToken token)
        { Rows=rows; return Task.FromResult(true); }
    }
    private sealed class ControlledMeasurements : IMeasurementService
    {
        public TimeSpan Duration => TimeSpan.FromSeconds(5);
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
        public void Complete() => pending?.SetResult(new CaptureData(start,start.AddSeconds(5),new double[] {1,2,3,4,5},"cps",start.AddSeconds(5).AddMilliseconds(20)));
    }
}
