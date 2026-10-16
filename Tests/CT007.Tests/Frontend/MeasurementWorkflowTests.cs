namespace Ct007.Tests.FrontendTests;

/// <summary>FE-024…FE-039, FE-048, FE-049: Measurement page state machine, capture flow, rows, delete, save, export (F11–F19).</summary>
public static class MeasurementWorkflowTests
{
    private sealed class Page : IDisposable
    {
        public Page(TestFolder folder, bool showDemoTsMs = false, string? durationPath = null)
        {
            Repository = new MemoryRepository(folder.Path("m.db"));
            ViewModel = Make.Measurement(Repository, Device, Dialogs, Export, Toasts, durationPath, showDemoTsMs);
        }

        public MemoryRepository Repository { get; }
        public ControlledMeasurements Device { get; } = new();
        public FakeDialogs Dialogs { get; } = new();
        public FakeExport Export { get; } = new();
        public ToastService Toasts { get; } = new();
        public MeasurementViewModel ViewModel { get; }

        public Guid SessionId => ViewModel.SessionId!.Value;

        public bool Toasted(ToastKind kind, string message) => Make.Toasted(Toasts, kind, message);

        public void Dispose() => ViewModel.Dispose();
    }

    private const string SaveFailed = "Saving to the database failed (disk full). Rows stay in the table and are saved again on the next change or Stop.";

    [Test("FE-024", "Depth inputs: flags and messages for valid and invalid values")]
    public static async Task DepthValidation()
    {
        using var folder = new TestFolder();
        using var page = new Page(folder);
        var vm = page.ViewModel;
        const string fromError = "Starting depth: enter a depth in metres (at most 3 decimals).";
        const string stepError = "Step: enter a step greater than 0 cm (at most 1 decimal).";
        const string missing = "Enter the starting depth and step to enable Start.";

        var cases = new (string From, string Step, bool FromInvalid, bool StepInvalid, string Message)[]
        {
            ("10.25", "25", false, false, ""),
            ("10,25", "2,5", false, false, ""),
            ("10.2555", "25", true, false, fromError),
            ("-1", "25", true, false, fromError),
            ("10", "0", false, true, stepError),     // the parser accepts 0 cm; the page rejects it (UR-20)
            ("10", "2.55", false, true, stepError),
            ("1,000.5", "25", true, false, fromError),
            ("", "", false, false, missing)
        };

        foreach (var (start, step, fromInvalid, stepInvalid, message) in cases)
        {
            vm.NextFrom = start;
            vm.Step = step;
            Assert.Equal(fromInvalid, vm.IsNextFromInvalid, $"'{start}'/'{step}': Starting Depth red border");
            Assert.Equal(stepInvalid, vm.IsStepInvalid, $"'{start}'/'{step}': Step red border");
            Assert.Equal(message, vm.DepthError, $"'{start}'/'{step}': message");
        }

        vm.NextFrom = "10.2555";
        vm.CommitDepthInputs();
        Assert.True(page.Toasted(ToastKind.Error, fromError), "leaving an invalid box shows the message as an error toast");

        // Start with invalid depths explains instead of starting (Start itself stays enabled once the session is open).
        vm.SetCore(Make.Core);
        await Assert.Eventually(() => vm.ToggleMeasurementCommand.CanExecute(null), "session open");
        vm.NextFrom = "";
        vm.Step = "";
        page.Toasts.Items.Clear();
        vm.ToggleMeasurementCommand.Execute(null);
        Assert.Equal(MeasurementState.Idle, vm.State, "not started");
        Assert.True(page.Toasted(ToastKind.Error, missing), "Start explains what is missing");
    }

    [Test("FE-025", "Start needs valid depths and the saved drillhole's session")]
    public static async Task StartRequirements()
    {
        using var folder = new TestFolder();
        using var page = new Page(folder);
        var vm = page.ViewModel;
        Assert.False(vm.ToggleMeasurementCommand.CanExecute(null), "Start unavailable before Save & Continue");

        var gate = new TaskCompletionSource();
        page.Repository.BeforeLookup = _ => gate.Task;
        vm.SetCore(Make.Core);
        Assert.False(vm.ToggleMeasurementCommand.CanExecute(null), "Start unavailable while the session opens");
        gate.SetResult();
        await Assert.Eventually(() => vm.ToggleMeasurementCommand.CanExecute(null), "Start available once the session is open");

        vm.NextFrom = "10.00";
        vm.Step = "25";
        vm.ToggleMeasurementCommand.Execute(null);
        Assert.Equal(MeasurementState.Ready, vm.State, "Ready");
        Assert.True(vm.IsMeasurementModeActive, "measurement mode on");
        Assert.True(vm.TriggerCommand.CanExecute(DateTimeOffset.Now), "trigger accepted");
    }

    [Test("FE-026", "A capture cycle: Ready → Capturing → Processing → Ready, one row, extra triggers ignored")]
    public static async Task CaptureCycle()
    {
        using var folder = new TestFolder();
        using var page = new Page(folder);
        var vm = page.ViewModel;
        await Make.Arm(vm, Make.Core);
        var states = new List<MeasurementState>();
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.State)) states.Add(vm.State); };

        vm.TriggerCommand.Execute(DateTimeOffset.Now);
        for (var i = 0; i < 3; i++)
        {
            Assert.False(vm.TriggerCommand.CanExecute(DateTimeOffset.Now), "trigger refused while capturing");
            vm.TriggerCommand.Execute(DateTimeOffset.Now);
        }

        page.Device.Complete();
        await Assert.Eventually(() => vm.State == MeasurementState.Ready && vm.Results.Count == 1, "capture done");

        Assert.Equal(1, page.Device.Calls, "capture calls");
        Assert.Sequence(new[] { MeasurementState.Capturing, MeasurementState.Processing, MeasurementState.Ready }, states, "state sequence");
        var row = vm.Results[0];
        Assert.Equal(10_000L, row.FromMm, "From (mm)");
        Assert.Equal(10_250L, row.ToMm, "To (mm)");
        Assert.Equal(1, row.CaptureNumber, "Capture No");
        Assert.Equal("10.25", vm.NextFrom, "next Starting Depth");
        Assert.Equal("00:05", vm.Countdown, "countdown reset");
        await Assert.Eventually(() => page.Repository.Rows(page.SessionId).Count == 1, "row saved");
        Assert.Equal(new DepthProgress(10_250, 250), page.Repository.Progress(page.SessionId), "progress saved with the row");
    }

    [Test("FE-027", "Stop during Capturing produces no row, no advance, no save of a row")]
    public static async Task StopWhileCapturing()
    {
        using var folder = new TestFolder();
        using var page = new Page(folder);
        var vm = page.ViewModel;
        await Make.Arm(vm, Make.Core);

        vm.TriggerCommand.Execute(DateTimeOffset.Now);
        page.Device.Reporters[0](TimeSpan.FromSeconds(2));
        Assert.Equal("00:02", vm.Countdown, "countdown follows the capture");
        vm.ToggleMeasurementCommand.Execute(null);
        page.Device.Complete();

        Assert.Equal(MeasurementState.Idle, vm.State, "Idle after Stop");
        await Assert.Remains(() => vm.Results.Count == 0, "no row");
        Assert.Equal("10.00", vm.NextFrom, "no depth advance");
        Assert.Equal(0, page.Repository.Rows(page.SessionId).Count, "nothing stored");
        Assert.False(page.Toasts.Items.Any(t => t.Kind == ToastKind.Error), "no error toast");
    }

    [Test("FE-028", "Stop during the 100 ms Processing hold discards the capture (UR-23)", Category = "Timing", TimeoutSeconds = 120)]
    public static async Task StopWhileProcessing()
    {
        using var folder = new TestFolder();
        using var page = new Page(folder);
        var vm = page.ViewModel;
        await Make.Arm(vm, Make.Core);

        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (vm.State == MeasurementState.Idle)
            {
                vm.ToggleMeasurementCommand.Execute(null);
            }

            // The cancelled capture's cleanup runs as a continuation; the trigger reopens after it.
            await Assert.Eventually(() => vm.TriggerCommand.CanExecute(DateTimeOffset.Now), $"attempt {attempt}: trigger available");
            vm.TriggerCommand.Execute(DateTimeOffset.Now);
            page.Device.Complete();
            await Assert.Eventually(() => vm.State == MeasurementState.Processing, $"attempt {attempt}: Processing reached", 1000);
            vm.ToggleMeasurementCommand.Execute(null);
            await Assert.Remains(() => vm.Results.Count == 0, $"attempt {attempt}: no row", 200);
        }

        Assert.Equal("10.00", vm.NextFrom, "no depth advance");
        Assert.Equal(0, page.Repository.Rows(page.SessionId).Count, "nothing stored");
    }

    [Test("FE-029", "The interval is frozen at the click; depth advances once, unless edited meanwhile")]
    public static async Task DepthProgression()
    {
        using var folder = new TestFolder();

        using (var page = new Page(folder))
        {
            await Make.Arm(page.ViewModel, Make.Core);
            page.ViewModel.TriggerCommand.Execute(DateTimeOffset.Now);
            page.ViewModel.Step = "50";
            page.Device.Complete();
            await Assert.Eventually(() => page.ViewModel.Results.Count == 1 && page.ViewModel.State == MeasurementState.Ready, "first capture");
            Assert.Equal(10_250L, page.ViewModel.Results[0].ToMm, "Step edited during the capture does not change it");
            Assert.Equal("10.25", page.ViewModel.NextFrom, "advanced to this capture's To");

            await Make.Capture(page.ViewModel, page.Device);
            var second = page.ViewModel.Results.Single(r => r.CaptureNumber == 2);
            Assert.Equal(10_250L, second.FromMm, "second From");
            Assert.Equal(10_750L, second.ToMm, "second capture uses the new 50 cm step");
        }

        using (var page = new Page(folder))
        {
            await Make.Arm(page.ViewModel, Make.Core);
            page.ViewModel.TriggerCommand.Execute(DateTimeOffset.Now);
            page.ViewModel.NextFrom = "20.00";
            page.Device.Complete();
            await Assert.Eventually(() => page.ViewModel.Results.Count == 1 && page.ViewModel.State == MeasurementState.Ready, "capture");
            Assert.Equal(10_000L, page.ViewModel.Results[0].FromMm, "row keeps the clicked interval");
            Assert.Equal("20.00", page.ViewModel.NextFrom, "a Starting Depth typed during the capture wins (no advance)");
        }
    }

    [Test("FE-030", "Capture outcomes: incomplete warning, no-sample error, device error, generic failure")]
    public static async Task CaptureOutcomes()
    {
        using var folder = new TestFolder();

        DeviceSample[] Three(DateTimeOffset start) => Enumerable.Range(1, 3)
            .Select(i => Samples.At(i, start.ToUnixTimeMilliseconds() + i * 1000, count0: i * 10)).ToArray();

        using (var page = new Page(folder))
        {
            await Make.Arm(page.ViewModel, Make.Core);
            await Make.Capture(page.ViewModel, page.Device,
                d => d.CompleteWith(Samples.Capture(d.LastStart, d.LastDuration, 5, Three(d.LastStart))));
            var row = page.ViewModel.Results.Single();
            Assert.Equal("3 of 5", row.SampleDisplay, "Sample column");
            Assert.Near(20, row.Reading.Count0, 1e-12, "average of the received samples");
            Assert.True(page.Toasted(ToastKind.Warning, "Incomplete capture: 3 of 5 samples received. The result averages the 3 received."),
                "incomplete warning");
            Assert.Equal("10.25", page.ViewModel.NextFrom, "depth advances");
        }

        var failures = new (string Name, Action<ControlledMeasurements> Complete, string Toast)[]
        {
            ("no device samples", d => d.CompleteWith(Samples.Capture(d.LastStart, d.LastDuration, 5, [])),
                "No samples were received from CT007-M-0001 during the 5 s capture. Nothing was saved."),
            ("capture exception", d => d.Fail(new CaptureException("The Gamma Guard database is busy.")), "The Gamma Guard database is busy."),
            ("unexpected exception", d => d.Fail(new InvalidOperationException("boom")), "Capture failed. Right-click to try again."),
            ("cancellation not requested by the user", d => d.Fail(new OperationCanceledException()), "Capture failed. Right-click to try again.")
        };

        foreach (var (name, complete, toast) in failures)
        {
            using var page = new Page(folder);
            await Make.Arm(page.ViewModel, Make.Core);
            await Make.Capture(page.ViewModel, page.Device, complete);
            Assert.Equal(0, page.ViewModel.Results.Count, $"{name}: no row");
            Assert.Equal("10.00", page.ViewModel.NextFrom, $"{name}: no depth advance");
            Assert.Equal(MeasurementState.Ready, page.ViewModel.State, $"{name}: ready for the next click");
            Assert.True(page.Toasted(ToastKind.Error, toast), $"{name}: error toast '{toast}'");
        }
    }

    [Test("FE-031", "Progress reported late by a cancelled capture is ignored")]
    public static async Task LateProgressIgnored()
    {
        using var folder = new TestFolder();
        using var page = new Page(folder);
        var vm = page.ViewModel;
        await Make.Arm(vm, Make.Core);

        vm.TriggerCommand.Execute(DateTimeOffset.Now);
        vm.ToggleMeasurementCommand.Execute(null);
        vm.ToggleMeasurementCommand.Execute(null);
        await Assert.Eventually(() => vm.TriggerCommand.CanExecute(DateTimeOffset.Now), "cancelled capture cleaned up");
        vm.TriggerCommand.Execute(DateTimeOffset.Now);
        Assert.Equal(2, page.Device.Reporters.Count, "two captures started");

        page.Device.Reporters[1](TimeSpan.FromSeconds(3));
        var progress = vm.CaptureProgress;
        page.Device.Reporters[0](TimeSpan.FromSeconds(1));

        Assert.Equal("00:03", vm.Countdown, "countdown of the current capture");
        Assert.Equal(progress, vm.CaptureProgress, "progress of the current capture");
    }

    [Test("FE-032", "Capture duration editor: save, invalid input, cancel, locked while capturing, save failure")]
    public static async Task DurationEditor()
    {
        using var folder = new TestFolder();
        var settingsPath = folder.Path("capture-settings.json");
        using var page = new Page(folder, durationPath: settingsPath);
        var vm = page.ViewModel;

        Assert.True(vm.EditDurationCommand.CanExecute(null), "editor opens when idle");
        vm.EditDurationCommand.Execute(null);
        Assert.True(vm.IsEditingDuration, "editor open");
        Assert.Equal("5", vm.DurationText, "current value shown");
        vm.DurationText = "7";
        vm.SaveDurationCommand.Execute(null);
        Assert.False(vm.IsEditingDuration, "editor closed");
        Assert.Equal(TimeSpan.FromSeconds(7), vm.CaptureDuration, "duration");
        Assert.Equal("00:07", vm.Countdown, "countdown shows the new duration");
        using (var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(settingsPath)))
        {
            Assert.Equal(7, json.RootElement.GetProperty("captureDurationSeconds").GetInt32(), "saved file");
        }

        foreach (var text in new[] { "1", "11", "7.5", "abc" })
        {
            page.Toasts.Items.Clear();
            vm.EditDurationCommand.Execute(null);
            vm.DurationText = text;
            vm.SaveDurationCommand.Execute(null);
            Assert.True(page.Toasted(ToastKind.Error, "Capture duration: enter whole seconds from 2 to 10."), $"'{text}': error toast");
            Assert.True(vm.IsEditingDuration, $"'{text}': editor stays open");
            Assert.Equal(TimeSpan.FromSeconds(7), vm.CaptureDuration, $"'{text}': duration unchanged");
            vm.CancelDurationCommand.Execute(null);
            Assert.False(vm.IsEditingDuration, "Escape closes the editor");
        }

        await Make.Arm(vm, Make.Core);
        vm.EditDurationCommand.Execute(null);
        Assert.False(vm.TriggerCommand.CanExecute(DateTimeOffset.Now), "no capture while the editor is open");
        vm.CancelDurationCommand.Execute(null);
        vm.TriggerCommand.Execute(DateTimeOffset.Now);
        Assert.False(vm.EditDurationCommand.CanExecute(null), "editor locked while capturing");
        Assert.Equal(TimeSpan.FromSeconds(7), page.Device.LastDuration, "capture uses the saved duration");
        page.Device.Complete(7);
        await Assert.Eventually(() => vm.State == MeasurementState.Ready, "capture done");

        // A settings path that cannot be written: the duration is still used.
        var blocker = folder.Path("blocker");
        File.WriteAllText(blocker, "a file where the settings folder should be");
        using var other = new Page(folder, durationPath: Path.Combine(blocker, "capture-settings.json"));
        other.ViewModel.EditDurationCommand.Execute(null);
        other.ViewModel.DurationText = "3";
        other.ViewModel.SaveDurationCommand.Execute(null);
        Assert.Equal(TimeSpan.FromSeconds(3), other.ViewModel.CaptureDuration, "duration used although not saved");
        Assert.True(Make.ToastedStart(other.Toasts, ToastKind.Error, "The capture duration is used now but could not be saved ("), "save error toast");
    }

    [Test("FE-033", "Rows are placed by From and renumbered; saved order follows the table")]
    public static async Task SortedInsert()
    {
        using var folder = new TestFolder();
        using var page = new Page(folder);
        var vm = page.ViewModel;
        await Make.Arm(vm, Make.Core, startDepth: "10.50");
        await Make.Capture(vm, page.Device);
        vm.NextFrom = "10.00";
        await Make.Capture(vm, page.Device);
        vm.NextFrom = "10.25";
        await Make.Capture(vm, page.Device);

        Assert.Sequence(new long?[] { 10_000, 10_250, 10_500 }, vm.Results.Select(r => r.FromMm), "displayed From order");
        Assert.Sequence(new[] { 1, 2, 3 }, vm.Results.Select(r => r.Number), "No column");
        Assert.Sequence(new[] { 2, 3, 1 }, vm.Results.Select(r => r.CaptureNumber), "Capture No column");
        await Assert.Eventually(() => page.Repository.Rows(page.SessionId).Select(r => r.CaptureNumber).SequenceEqual(new[] { 2, 3, 1 }),
            "saved order = displayed order");
    }

    [Test("FE-034", "Delete: cancel, confirm, restore on failure, capture numbers never reused")]
    public static async Task DeleteFlow()
    {
        using var folder = new TestFolder();
        using var page = new Page(folder);
        var vm = page.ViewModel;
        await Make.Arm(vm, Make.Core);
        for (var i = 0; i < 3; i++)
        {
            await Make.Capture(vm, page.Device);
        }

        await Assert.Eventually(() => page.Repository.Rows(page.SessionId).Count == 3, "three rows saved");

        page.Dialogs.Confirm = false;
        vm.DeleteCommand.Execute(vm.Results[1]);
        Assert.Equal(3, vm.Results.Count, "cancel keeps the row");
        Assert.Equal(3, page.Repository.Rows(page.SessionId).Count, "cancel deletes nothing");

        page.Dialogs.Confirm = true;
        page.Repository.FailDelete = true;
        var failing = vm.Results[1];
        vm.DeleteCommand.Execute(failing);
        await Assert.Eventually(() => page.Toasted(ToastKind.Error,
            "Deleting the measurement failed (database locked). It is still stored and stays in the table."), "delete error toast");
        Assert.True(ReferenceEquals(failing, vm.Results[1]), "row restored in its place");
        Assert.Sequence(new[] { 1, 2, 3 }, vm.Results.Select(r => r.Number), "numbering after restore");

        page.Repository.FailDelete = false;
        vm.DeleteCommand.Execute(vm.Results[1]);
        await Assert.Eventually(() => page.Toasted(ToastKind.Success, "Measurement deleted."), "success toast after the delete");
        Assert.Sequence(new[] { 1, 3 }, vm.Results.Select(r => r.CaptureNumber), "remaining rows");
        Assert.Sequence(new[] { 1, 2 }, vm.Results.Select(r => r.Number), "renumbered");
        Assert.Equal(2, page.Repository.Rows(page.SessionId).Count, "stored rows");

        vm.DeleteCommand.Execute(vm.Results.Single(r => r.CaptureNumber == 3));
        await Assert.Eventually(() => vm.Results.Count == 1, "highest capture deleted");
        await Make.Capture(vm, page.Device);
        Assert.Equal(4, vm.Results.Max(r => r.CaptureNumber), "next capture number is never reused");
    }

    [Test("FE-035", "A failed save keeps the rows; the next change, Stop, switch or close saves them exactly once")]
    public static async Task SaveFailureRetry()
    {
        using var folder = new TestFolder();
        var retries = new (string Name, Func<Page, Task> Retry, int ExpectedRows)[]
        {
            ("next capture", p => Make.Capture(p.ViewModel, p.Device), 2),
            ("Stop", p => { p.ViewModel.ToggleMeasurementCommand.Execute(null); return Task.CompletedTask; }, 1),
            ("drillhole switch", p => { p.ViewModel.SelectDrillhole("DH-002"); return Task.CompletedTask; }, 1),
            ("close", p => { p.ViewModel.Dispose(); return Task.CompletedTask; }, 1)
        };

        foreach (var (name, retry, expectedRows) in retries)
        {
            using var page = new Page(folder);
            await Make.Arm(page.ViewModel, Make.Core);
            var session = page.SessionId;
            page.Repository.FailSave = true;
            await Make.Capture(page.ViewModel, page.Device);
            await Assert.Eventually(() => page.Toasted(ToastKind.Error, SaveFailed), $"{name}: save error toast");
            Assert.Equal(1, page.ViewModel.Results.Count, $"{name}: row stays in the table");

            page.Repository.FailSave = false;
            await retry(page);

            await Assert.Eventually(() => page.Repository.Rows(session).Count == expectedRows, $"{name}: rows saved");
            Assert.Equal(expectedRows, page.Repository.Rows(session).Select(r => r.CaptureNumber).Distinct().Count(), $"{name}: each row once");
        }
    }

    [Test("FE-036", "Switching drillholes saves first, ignores late loads and creates no session")]
    public static async Task DrillholeSwitch()
    {
        using var folder = new TestFolder();
        using var page = new Page(folder);
        var vm = page.ViewModel;
        var otherSession = page.Repository.Seed(Make.Session("DH-002"));
        page.Repository.Seed(otherSession, Make.Row(otherSession.Id, 1, 5_000, recordId: 1));

        await Make.Arm(vm, Make.Core);
        var first = page.SessionId;
        page.Repository.SaveDelay = TimeSpan.FromMilliseconds(500);
        await Make.Capture(vm, page.Device);

        var slowOther = new TaskCompletionSource();
        page.Repository.BeforeLookup = id => id == "DH-002" ? slowOther.Task : Task.CompletedTask;
        vm.SelectDrillhole("DH-002");
        vm.SelectDrillhole("DH-001");
        slowOther.SetResult();

        await Assert.Eventually(() => vm.SessionId == first && vm.Results.Count == 1, "DH-001 shown with its saved row", 5000);
        await Assert.Remains(() => vm.Results.All(row => row.SessionId == first), "DH-002 rows never shown", 500);
        Assert.Equal(1, page.Repository.Rows(first).Count, "DH-001 row saved to DH-001");
        Assert.Equal(1, page.Repository.Rows(otherSession.Id).Count, "DH-002 unchanged");

        var sessions = page.Repository.SessionCount;
        vm.SelectDrillhole("DH-777");
        await Task.Delay(100);
        Assert.Equal(sessions, page.Repository.SessionCount, "selecting a drillhole without a session creates none");
    }

    [Test("FE-036", "Save & Continue for another drillhole is refused while rows are unsaved")]
    public static async Task SwitchRefusedWhileUnsaved()
    {
        using var folder = new TestFolder();
        using var page = new Page(folder);
        var vm = page.ViewModel;
        await Make.Arm(vm, Make.Core);
        page.Repository.FailSave = true;
        await Make.Capture(vm, page.Device);
        await Assert.Eventually(() => page.Toasted(ToastKind.Error, SaveFailed), "save failed");
        vm.ToggleMeasurementCommand.Execute(null);

        vm.SetCore(Make.Core with { DrillholeId = "DH-002" });

        await Assert.Eventually(() => page.Toasted(ToastKind.Error,
            "Measurements of the previous drillhole are not saved yet. Retrying — click Save & Continue again once saving succeeds."),
            "refusal toast");
        Assert.True(page.Repository.Session("DH-002") is null, "no session for the new drillhole yet");
        Assert.Equal(1, vm.Results.Count, "unsaved row still shown");
    }

    [Test("FE-037", "An existing session keeps its stored metadata, with a warning (UR-30)")]
    public static async Task StoredMetadataWins()
    {
        using var folder = new TestFolder();
        using var page = new Page(folder);
        var vm = page.ViewModel;
        var stored = page.Repository.Seed(Make.Session("DH-001", operatorName: "Ann"));

        await Make.Arm(vm, Make.Core with { Operator = "Bob" });

        Assert.Equal(stored.Id, vm.SessionId, "the stored session is used");
        Assert.True(page.Toasted(ToastKind.Warning, "Drillhole DH-001 already exists. Its stored project, campaign, operator and notes are kept."),
            "warning toast");
        await Make.Capture(vm, page.Device);
        vm.ExportCommand.Execute(null);
        await Assert.Eventually(() => page.Export.Calls == 1, "exported");
        Assert.Equal("Ann", page.Export.Session!.Operator, "export uses the stored Operator");
    }

    [Test("FE-038", "New Measurement: refused with unsaved rows; otherwise clears the page and keeps stored rows")]
    public static async Task NewMeasurementFlow()
    {
        using var folder = new TestFolder();

        using (var page = new Page(folder))
        {
            await Make.Arm(page.ViewModel, Make.Core);
            page.Repository.FailSave = true;
            await Make.Capture(page.ViewModel, page.Device);
            await Assert.Eventually(() => page.Toasted(ToastKind.Error, SaveFailed), "save failed");
            page.ViewModel.ToggleMeasurementCommand.Execute(null);

            page.ViewModel.NewMeasurementCommand.Execute(null);

            Assert.True(page.Toasted(ToastKind.Error, "Previous measurements are not saved yet. Retrying — try New Measurement again once saving succeeds."),
                "refusal toast");
            Assert.True(page.ViewModel.SessionId is not null, "session kept");
            Assert.Equal(1, page.ViewModel.Results.Count, "row kept");
        }

        using (var page = new Page(folder))
        {
            var saved = false;
            page.ViewModel.MeasurementsSaved += () => saved = true;
            await Make.Arm(page.ViewModel, Make.Core);
            await Make.Capture(page.ViewModel, page.Device);
            await Assert.Eventually(() => saved, "row saved");
            var session = page.SessionId;
            page.ViewModel.ToggleMeasurementCommand.Execute(null);

            Assert.True(page.ViewModel.NewMeasurementCommand.CanExecute(null), "New Measurement available after Stop");
            page.ViewModel.NewMeasurementCommand.Execute(null);

            Assert.Equal(0, page.ViewModel.Results.Count, "table cleared");
            Assert.True(page.ViewModel.SessionId is null, "session closed");
            Assert.True(page.ViewModel.NeedsCore, "Core Information needed again");
            Assert.Equal(MeasurementState.Idle, page.ViewModel.State, "Idle");
            Assert.True(page.Toasted(ToastKind.Information,
                "Enter the next drillhole in Core Information and click Save & Continue. Earlier measurements remain stored."), "information toast");
            Assert.Equal(1, page.Repository.Rows(session).Count, "stored rows remain");
        }
    }

    [Test("FE-039", "Export command: disabled without rows, success toast, cancel silent, failure toast")]
    public static async Task ExportCommandFlow()
    {
        using var folder = new TestFolder();
        using var page = new Page(folder);
        var vm = page.ViewModel;
        await Make.Arm(vm, Make.Core);
        Assert.False(vm.ExportCommand.CanExecute(null), "disabled with no rows");

        await Make.Capture(vm, page.Device);
        await Make.Capture(vm, page.Device);
        Assert.True(vm.ExportCommand.CanExecute(null), "enabled with rows");

        vm.ExportCommand.Execute(null);
        await Assert.Eventually(() => page.Toasted(ToastKind.Success, "CSV exported."), "success toast");
        Assert.Sequence(vm.Results.Select(r => r.CaptureNumber), page.Export.Rows!.Select(r => r.CaptureNumber), "rows in displayed order");
        Assert.Equal(vm.SessionId, page.Export.Session!.Id, "current session exported");

        page.Toasts.Items.Clear();
        page.Export.Result = false;
        vm.ExportCommand.Execute(null);
        await Assert.Eventually(() => page.Export.Calls == 2, "second export attempted");
        await Assert.Remains(() => page.Toasts.Items.Count == 0, "a cancelled dialog shows no toast");

        page.Export.Throw = new IOException("access denied");
        vm.ExportCommand.Execute(null);
        await Assert.Eventually(() => page.Toasted(ToastKind.Error, "Export failed. Check the file location and try again."), "failure toast");
    }

    [Test("FE-048", "Closing: cancels a capture, waits for queued saves, gives up after about 5 s (UR-23)", Category = "Timing", TimeoutSeconds = 90)]
    public static async Task CloseDuringWork()
    {
        using var folder = new TestFolder();

        var capturing = new Page(folder);
        await Make.Arm(capturing.ViewModel, Make.Core);
        capturing.ViewModel.TriggerCommand.Execute(DateTimeOffset.Now);
        capturing.ViewModel.Dispose();
        capturing.Device.Complete();
        await Assert.Remains(() => capturing.ViewModel.Results.Count == 0, "no row for the interrupted capture");
        Assert.False(capturing.Toasts.Items.Any(t => t.Kind == ToastKind.Error), "no error toast");

        var slow = new Page(folder);
        await Make.Arm(slow.ViewModel, Make.Core);
        var slowSession = slow.SessionId;
        slow.Repository.SaveDelay = TimeSpan.FromSeconds(1);
        await Make.Capture(slow.ViewModel, slow.Device);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        slow.ViewModel.Dispose();
        Assert.True(watch.ElapsedMilliseconds >= 500, $"close waited for the queued save ({watch.ElapsedMilliseconds} ms)");
        Assert.Equal(1, slow.Repository.Rows(slowSession).Count, "queued save completed before close returned");

        var hung = new Page(folder);
        await Make.Arm(hung.ViewModel, Make.Core);
        var hungSession = hung.SessionId;
        hung.Repository.SaveGate = new TaskCompletionSource();
        await Make.Capture(hung.ViewModel, hung.Device);
        watch.Restart();
        hung.ViewModel.Dispose();
        Assert.True(watch.Elapsed.TotalSeconds is >= 4 and <= 7, $"close gives up after about 5 s ({watch.Elapsed.TotalSeconds:0.0} s)");
        Assert.Equal(0, hung.Repository.Rows(hungSession).Count, "row not stored when the disk hangs (data loss, UR-23)");
        hung.Repository.SaveGate!.SetResult();
    }

    [Test("FE-049", "Demo ts_ms is shown only, never saved or exported")]
    public static async Task DemoTsMs()
    {
        using var folder = new TestFolder();
        using var page = new Page(folder, showDemoTsMs: true);
        await Make.Arm(page.ViewModel, Make.Core);
        await Make.Capture(page.ViewModel, page.Device);
        var row = page.ViewModel.Results.Single();

        Assert.True(row.DemoTsMs is >= 500 and <= 5000, $"demo value in 500–5000 ({row.DemoTsMs})");
        Assert.Equal(row.DemoTsMs, row.DisplayTsMs, "table shows the demo value");
        Assert.True(row.IsDemoTsMs, "marked as demo");
        await Assert.Eventually(() => page.Repository.Rows(page.SessionId).Count == 1, "saved");
        Assert.True(page.Repository.Rows(page.SessionId)[0].Reading.TsMs is null, "stored ts_ms is the device value (none)");

        var csv = CsvExportService.Serialize(page.ViewModel.Results.ToList(), page.Repository.Session("DH-001")!).Split("\r\n");
        var column = csv[0].Split(',').ToList().IndexOf("\"ts_ms\"");
        Assert.Equal("\"\"", csv[1].Split(',')[column], "exported ts_ms is empty");

        using var normal = new Page(folder);
        await Make.Arm(normal.ViewModel, Make.Core);
        await Make.Capture(normal.ViewModel, normal.Device);
        Assert.True(normal.ViewModel.Results.Single().DemoTsMs is null, "no demo value when demo mode is off");
    }
}
