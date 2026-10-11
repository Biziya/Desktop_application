using System.Diagnostics;
using System.Globalization;
using System.Windows.Threading;
using Frontend.Models;
using Frontend.Services;
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
        CoreInformation? saved = null;
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
        Check(!vm.StartCommand.CanExecute(null), "Capture requires saved core");
        Check(!vm.ExportCommand.CanExecute(null), "Empty export disabled");
        vm.SetCore(core);
        vm.StartCommand.Execute(null); vm.StartCommand.Execute(null);
        Check(fake.Calls == 1 && vm.IsCapturing && !vm.StartCommand.CanExecute(null), "Overlapping captures blocked");
        fake.Complete(); await Until(() => vm.StartCommand.CanExecute(null));
        Check(vm.Results.Count == 1 && vm.Results[0].AverageValue == 3 && vm.Results[0].SampleCount == 5, "Exactly one averaged result");
        for (int i=0; i<2; i++) { vm.StartCommand.Execute(null); fake.Complete(); await Until(() => vm.StartCommand.CanExecute(null)); }
        var first = vm.Results[0]; var second = vm.Results[1]; var third = vm.Results[2];
        vm.Move(third, first, false);
        Check(vm.Results[0] == third && third.Number == 1 && third.CaptureNumber == 3, "Reorder keeps capture identity");
        vm.Move(third, second, true);
        Check(vm.Results[2] == third, "Drop after target");
        dialogs.Confirm = false; vm.DeleteCommand.Execute(second);
        Check(vm.Results.Count == 3, "Cancel deletion");
        dialogs.Confirm = true; vm.DeleteCommand.Execute(second);
        Check(vm.Results.Count == 2 && third.Number == 2 && third.CaptureNumber == 3, "Deletion renumbers display only");
        vm.StartCommand.Execute(null); fake.Complete(); await Until(() => vm.StartCommand.CanExecute(null));
        Check(vm.Results[2].CaptureNumber == 4, "Deleted capture numbers are not reused");
        vm.Move(vm.Results[2], first, false);
        vm.ExportCommand.Execute(null); await Until(() => vm.ExportCommand.CanExecute(null));
        Check(export.Rows?.Select(r => r.CaptureNumber).SequenceEqual(new[] {4,1,3}) == true, "Export keeps display order");
        var csv = CsvExportService.Serialize(vm.Results.ToArray());
        Check(csv.Split('\n')[1].StartsWith("\"1\",\"4\","), "CSV display and capture numbers");
        var escaped = new MeasurementResult { CaptureNumber=99, StartTime=first.StartTime, EndTime=first.EndTime,
            SampleCount=5, AverageValue=3.5, Unit="cps", Core=core with { Notes="=SUM(1,2)", DrillholeName="A \"quoted\" name" } };
        Check(CsvExportService.Serialize(new[] {escaped}).Contains("\"'=SUM(1,2)\""), "CSV formula escaping");
        Check(CsvExportService.Serialize(new[] {escaped}).Contains("A \"\"quoted\"\" name"), "CSV quote escaping");
        vm.SetCore(null); Check(!vm.StartCommand.CanExecute(null), "Editing invalidates saved form");
        using (var main = new MainViewModel())
        {
            main.Core.DrillholeId="retained"; main.ShowMeasurementCommand.Execute(null); main.ShowCoreCommand.Execute(null);
            Check(main.CurrentPage == main.Core && main.Core.DrillholeId == "retained", "Navigation retains form");
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
        closingVm.StartCommand.Execute(null); closingVm.Dispose(); await Until(() => !closingVm.IsCapturing);
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
        public int Calls { get; private set; }
        private TaskCompletionSource<CaptureData>? pending;
        private DateTimeOffset start;
        public async Task<CaptureData> CaptureAsync(DateTimeOffset started, Action<TimeSpan> report, CancellationToken token)
        {
            Calls++; start=started; pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = token.Register(() => pending.TrySetCanceled(token));
            return await pending.Task;
        }
        public void Complete() => pending?.SetResult(new CaptureData(start,start.AddSeconds(5),new double[] {1,2,3,4,5},"cps"));
    }
}
