using System.Diagnostics;
using System.Globalization;
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
        using var vm = new MeasurementViewModel(fake, export, dialogs);
        Check(!vm.ToggleMeasurementCommand.CanExecute(null), "Capture requires saved core");
        Check(!vm.ExportCommand.CanExecute(null), "Empty export disabled");
        vm.TriggerCommand.Execute(null);
        Check(fake.Calls == 0, "Idle triggers ignored");
        vm.SetCore(core);
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
        vm.Move(vm.Results[2], first, false);
        vm.ExportCommand.Execute(null); await Until(() => vm.ExportCommand.CanExecute(null));
        Check(export.Rows?.Select(r => r.CaptureNumber).SequenceEqual(new[] {4,1,3}) == true, "Export keeps display order");
        var csv = CsvExportService.Serialize(vm.Results.ToArray());
        Check(csv.Split('\n')[1].StartsWith("\"1\",\"4\","), "CSV display and capture numbers");
        var escaped = new MeasurementResult { CaptureNumber=99, StartTime=first.StartTime, EndTime=first.EndTime, CompletedAt=first.CompletedAt,
            SampleCount=5, AverageValue=3.5, Unit="cps", Core=core with { Notes="=SUM(1,2)", DrillholeName="A \"quoted\" name" } };
        Check(CsvExportService.Serialize(new[] {escaped}).Contains("\"'=SUM(1,2)\""), "CSV formula escaping");
        Check(CsvExportService.Serialize(new[] {escaped}).Contains("A \"\"quoted\"\" name"), "CSV quote escaping");
        Check(CsvExportService.Serialize(new[] {first}).Contains(triggerTime.ToString("O")), "CSV retains fractional seconds and offset");
        Check(csv.Contains("Completed At"), "CSV includes completion timestamp");
        var count = vm.Results.Count;
        vm.ToggleMeasurementCommand.Execute(null);
        Check(vm.State == MeasurementState.Idle && vm.Results.Count == count, "Stop keeps completed rows");
        vm.TriggerCommand.Execute(null); Check(vm.Results.Count == count, "Stopped trigger ignored");
        vm.ToggleMeasurementCommand.Execute(null); vm.TriggerCommand.Execute(null);
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
        using (var lateVm = new MeasurementViewModel(late, export, dialogs))
        {
            lateVm.SetCore(core); lateVm.ToggleMeasurementCommand.Execute(null); lateVm.TriggerCommand.Execute(null);
            lateVm.ToggleMeasurementCommand.Execute(null); lateVm.ToggleMeasurementCommand.Execute(null);
            late.Report(TimeSpan.FromSeconds(1)); lateVm.TriggerCommand.Execute(null);
            Check(!lateVm.CanTrigger && late.Calls == 1 && lateVm.Countdown == "00:05" && lateVm.CaptureProgress == 0,
                "Late progress and rearm cannot overlap cancellation cleanup");
            late.Complete(); await Until(() => lateVm.CanTrigger);
            Check(lateVm.Results.Count == 0, "Late cancelled result discarded");
        }
        using (var main = new MainViewModel())
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
            Check(main.Measurement.IsCapturing && main.CurrentPage == retainedCore, "Navigation does not duplicate or reset running capture");
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
        var closingVm = new MeasurementViewModel(closingService, export, dialogs); closingVm.SetCore(core);
        closingVm.ToggleMeasurementCommand.Execute(null); closingVm.TriggerCommand.Execute(null); closingVm.Dispose(); await Until(() => !closingVm.IsCapturing);
        Check(closingVm.Results.Count == 0, "Closing cancels without partial result");
    }
    private sealed class FakeDialogs : IDialogService
    { public bool Confirm { get; set; } public bool ConfirmDelete() => Confirm; }
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
