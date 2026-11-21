using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Frontend.Pages.Measurement.CapturePanel;
using Frontend.Shared.Help;

namespace Ct007.Tests.FrontendTests;

/// <summary>
/// FE-DH-NEW (Existing / New drillhole), FE-PAGE, FE-LIVE, FE-THRESH, FE-HELP-05…07: the Gamma Core Log updates (new drillhole, Measurement-page
/// only capture, live CPS, threshold line, single Help window with F1 / Esc).
/// </summary>
public static partial class GammaCoreLogUpdateTests
{
    private static async Task<(MainViewModel Main, ControlledMeasurements Device)> Shell(TestFolder folder, HelpSettings? help = null)
    {
        var device = new ControlledMeasurements();
        var main = new MainViewModel(new MemoryRepository(folder.Path("m.db")),
            new ApiSettings("http://stub.test", TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(5)),
            Make.Devices(folder.Path("gg.sqlite")), new StubBackend(), bluetoothDevices: () => [], captureService: device,
            dialogs: new FakeDialogs(), helpSettings: help);
        await main.Core.LoadDrillholesAsync();
        return (main, device);
    }

    // Core Information → Save & Continue → Start on the Measurement page.
    private static async Task Arm(MainViewModel main)
    {
        await Make.Pick(main.Core, "DH-001");
        main.Core.SaveCommand.Execute(null);
        Assert.True(main.IsMeasurementPageActive, "Save & Continue opens the Measurement page");
        await Assert.Eventually(() => main.Measurement.ToggleMeasurementCommand.CanExecute(null), "session open");
        main.Measurement.NextFrom = "10.00";
        main.Measurement.Step = "25";
        main.Measurement.ToggleMeasurementCommand.Execute(null);
        Assert.Equal(MeasurementState.Ready, main.Measurement.State, "armed");
    }

    [Test("FE-DH-NEW-01", "New mode clears the picked drillhole; Save & Continue stays disabled until ID, Project and Campaign are filled")]
    public static async Task NewModeRequiresAllThree()
    {
        using var folder = new TestFolder();
        var db = folder.Path("m.db");
        var form = Make.CoreForm(new StubBackend(), db, new MemoryRepository(db), out _, dialogs: new FakeDialogs());
        await form.LoadDrillholesAsync();
        await Make.Pick(form, "DH-001");
        Assert.True(form.SaveCommand.CanExecute(null), "Existing: Save & Continue enabled");

        form.IsNewModeSelected = true;
        Assert.True(form.IsNewMode && !form.IsExistingModeSelected, "New mode");
        Assert.Equal("", form.DrillholeId, "the picked drillhole is cleared");
        Assert.Equal("", form.DrillholeName, "its Project is cleared");
        Assert.Equal("", form.ProjectArea, "its Campaign is cleared");
        Assert.False(form.SaveCommand.CanExecute(null), "disabled while everything is empty");
        Assert.Equal("", form.NewDrillholeIdError, "no message before a field is touched");
        Assert.Equal("", form.NewProject.Error, "no Project message yet");
        Assert.Sequence(new[] { "North Ridge", "South Pit" }, form.NewProject.Choices.Where(c => !c.IsAddNew).Select(c => c.Name), "projects, once each, A–Z");
        Assert.Sequence(new[] { "2024", "2025" }, form.NewCampaign.Choices.Where(c => !c.IsAddNew).Select(c => c.Name), "campaigns");
        Assert.True(form.NewProject.Choices[^1].IsAddNew && form.NewCampaign.Choices[^1].IsAddNew, "+ Add new … is the last entry");

        form.MarkTouched("id");
        form.MarkTouched("project");
        form.MarkTouched("campaign");
        Assert.Equal("Drillhole ID is required.", form.NewDrillholeIdError, "ID message");
        Assert.Equal("Project is required.", form.NewProject.Error, "Project message");
        Assert.Equal("Campaign is required.", form.NewCampaign.Error, "Campaign message");

        form.NewDrillholeId = "   ";
        form.NewProject.Picked = form.NewProject.Choices[0];
        form.NewCampaign.Picked = form.NewCampaign.Choices[1];
        Assert.Equal("", form.NewProject.Error + form.NewCampaign.Error, "picked: no message");
        Assert.False(form.SaveCommand.CanExecute(null), "spaces-only ID: disabled");
        Assert.Equal("Drillhole ID is required.", form.NewDrillholeIdError, "spaces-only ID is empty");

        form.NewDrillholeId = "DH-NEW-1";
        Assert.True(form.SaveCommand.CanExecute(null), "all three filled: enabled");

        form.NewCampaign.Picked = form.NewCampaign.Choices[^1];
        Assert.True(form.NewCampaign.IsAdding, "+ Add new campaign: a text box");
        Assert.False(form.SaveCommand.CanExecute(null), "empty new campaign: disabled");
        Assert.False(await form.SaveAsync(), "refused");
        Assert.Equal("Campaign is required.", form.NewCampaign.Error, "Campaign message");
        Assert.Sequence(Array.Empty<string>(), (await new DrillholeCache(db).LoadLocalAsync()).Select(d => d.HoleId!), "nothing stored");
        Assert.True(form.IsNewMode, "still in New mode");
    }

    [Test("FE-DH-NEW-02", "New drillhole: one Save & Continue stores it locally, selects it, continues; listed as Local after a restart")]
    public static async Task NewDrillholeIsStoredSelectedAndContinued()
    {
        using var folder = new TestFolder();
        var db = folder.Path("m.db");
        var backend = new StubBackend();
        var saves = new List<CoreSnapshot>();
        var form = Make.CoreForm(backend, db, new MemoryRepository(db), out _, dialogs: new FakeDialogs(), saved: saves.Add);
        await form.LoadDrillholesAsync();

        form.IsNewModeSelected = true;
        form.NewDrillholeId = " DH-NEW-7 ";
        form.NewProject.Picked = form.NewProject.Choices.First(c => c.Name == "South Pit");
        form.NewCampaign.Picked = form.NewCampaign.Choices[^1];
        form.NewCampaign.Text = " 2026 ";
        Assert.True(form.SaveCommand.CanExecute(null), "enabled");
        Assert.True(await form.SaveAsync(), "created and continued in one step");

        Assert.False(form.IsNewMode, "back to Existing");
        Assert.Equal("DH-NEW-7", form.DrillholeId, "selected (trimmed)");
        Assert.Equal("South Pit", form.DrillholeName, "Project");
        Assert.Equal("2026", form.ProjectArea, "Campaign (trimmed)");
        Assert.Equal(1, saves.Count, "continued once");
        Assert.Equal(new CoreSnapshot("DH-NEW-7", "South Pit", "2026", form.Operator, ""), saves[0], "Save & Continue snapshot");
        Assert.True(Make.Toasted(form.Toasts, ToastKind.Success, "Drillhole DH-NEW-7 created."), "toast");
        Assert.Sequence(new[] { "DH-NEW-7|South Pit|2026" },
            (await new DrillholeCache(db).LoadLocalAsync()).Select(d => $"{d.HoleId}|{d.ProjectCode}|{d.Campaign}"), "stored on this computer");
        Assert.False((await new DrillholeCache(db).LoadAsync()).Any(d => d.HoleId == "DH-NEW-7"), "not mixed into the backend cache");
        Assert.True(form.DrillholeOptions.Single(o => o.HoleId == "DH-NEW-7").IsLocal, "tagged Local");
        Assert.False(form.DrillholeOptions.Single(o => o.HoleId == "DH-001").IsLocal, "backend drillholes are not");
        Assert.True(form.NewCampaign.Choices.Any(c => c.Name == "2026"), "the new campaign is offered right away");

        backend.Offline = true;
        await form.LoadDrillholesAsync();
        Assert.True(Make.Ids(form).Contains("DH-NEW-7"), "kept when the refresh fails");

        // Next start-up, still offline: listed from the local table, tagged, with its Project and Campaign.
        var again = Make.CoreForm(backend, db, new MemoryRepository(db), out _, dialogs: new FakeDialogs());
        await again.LoadDrillholesAsync();
        await Make.Pick(again, "DH-NEW-7");
        Assert.Equal("South Pit", again.DrillholeName, "Project after restart");
        Assert.Equal("2026", again.ProjectArea, "Campaign after restart");
        Assert.True(again.DrillholeOptions.Single(o => o.HoleId == "DH-NEW-7").IsLocal, "still tagged Local");
        Assert.True(again.NewCampaign.Choices.Any(c => c.Name == "2026"), "new campaign listed after restart");
    }

    [Test("FE-DH-NEW-03", "New drillhole: an existing ID (any case) is blocked; the link switches to Existing and selects it")]
    public static async Task DuplicateIdLinksToExisting()
    {
        using var folder = new TestFolder();
        var db = folder.Path("m.db");
        var dialogs = new FakeDialogs();
        var form = Make.CoreForm(new StubBackend(), db, new MemoryRepository(db), out _, dialogs: dialogs);
        await form.LoadDrillholesAsync();

        form.IsNewModeSelected = true;
        form.NewDrillholeId = " dh-001 ";
        form.NewProject.Picked = form.NewProject.Choices[0];
        form.NewCampaign.Picked = form.NewCampaign.Choices[0];
        Assert.True(form.IsNewDrillholeIdDuplicate, "duplicate found");
        Assert.Equal("Drillhole already exists — switch to Existing to select it.", form.NewDrillholeIdError, "message");
        Assert.False(form.IsNewDrillholeIdRequired, "not shown as 'required'");
        Assert.False(form.SaveCommand.CanExecute(null), "Save & Continue disabled");
        Assert.False(await form.SaveAsync(), "refused");

        Assert.True(form.SelectExistingDuplicateCommand.CanExecute(null), "link available");
        form.SelectExistingDuplicateCommand.Execute(null);
        await form.WhenOptionsUpdated();
        Assert.False(form.IsNewMode, "Existing mode");
        Assert.Equal(0, dialogs.DiscardAsked, "the link does not ask to discard");
        Assert.Equal("DH-001", form.DrillholeId, "DH-001 selected");
        Assert.Equal("North Ridge", form.DrillholeName, "its Project");
        Assert.Equal("", form.NewDrillholeId, "New inputs cleared");
        Assert.Sequence(Array.Empty<string>(), (await new DrillholeCache(db).LoadLocalAsync()).Select(d => d.HoleId!), "nothing stored");
    }

    [Test("FE-DH-NEW-04", "New drillhole: an added Project or Campaign that already exists (any case, extra spaces) is blocked")]
    public static async Task DuplicateProjectAndCampaignNames()
    {
        using var folder = new TestFolder();
        var db = folder.Path("m.db");
        var form = Make.CoreForm(new StubBackend(), db, new MemoryRepository(db), out _, dialogs: new FakeDialogs());
        await form.LoadDrillholesAsync();

        form.IsNewModeSelected = true;
        form.NewDrillholeId = "DH-NEW-9";
        form.NewProject.Picked = form.NewProject.Choices[^1];
        form.NewProject.Text = "north ridge";
        form.NewCampaign.Picked = form.NewCampaign.Choices[^1];
        form.NewCampaign.Text = "2024 ";
        Assert.Equal("Project «North Ridge» already exists — pick it from the list.", form.NewProject.Error, "Project duplicate");
        Assert.Equal("Campaign «2024» already exists — pick it from the list.", form.NewCampaign.Error, "Campaign duplicate");
        Assert.False(form.SaveCommand.CanExecute(null), "disabled");

        form.NewProject.Text = "Zoovch";
        Assert.Equal("", form.NewProject.Error, "a new name is accepted");
        Assert.True(form.NewCampaign.CancelAddCommand.CanExecute(null), "× available while adding");
        form.NewCampaign.CancelAddCommand.Execute(null);
        Assert.False(form.NewCampaign.IsAdding, "× returns to the list");
        Assert.True(form.NewCampaign.Picked is null && form.NewCampaign.Value == "", "typed text dropped");
        form.NewCampaign.Picked = form.NewCampaign.Choices.First(c => c.Name == "2025");
        Assert.True(form.SaveCommand.CanExecute(null), "enabled");
        Assert.True(await form.SaveAsync(), "saved with the new project");

        form.IsNewModeSelected = true;
        Assert.True(form.NewProject.Choices.Any(c => c.Name == "Zoovch"), "new project listed");
        form.NewProject.Picked = form.NewProject.Choices[^1];
        form.NewProject.Text = "ZOOVCH";
        Assert.Equal("Project «Zoovch» already exists — pick it from the list.", form.NewProject.Error, "now a duplicate too");
    }

    [Test("FE-DH-NEW-05", "Leaving New mode with input asks to discard; Keep editing keeps it; no switching while measuring")]
    public static async Task LeavingNewModeAsksToDiscard()
    {
        using var folder = new TestFolder();
        var db = folder.Path("m.db");
        var dialogs = new FakeDialogs { DiscardNewDrillhole = false };
        var form = Make.CoreForm(new StubBackend(), db, new MemoryRepository(db), out _, dialogs: dialogs);
        await form.LoadDrillholesAsync();

        form.IsNewModeSelected = true;
        form.IsExistingModeSelected = true;
        Assert.False(form.IsNewMode, "nothing entered: back without asking");
        Assert.Equal(0, dialogs.DiscardAsked, "no prompt");

        form.IsNewModeSelected = true;
        form.NewDrillholeId = "DH-X";
        form.IsExistingModeSelected = true;
        Assert.Equal(1, dialogs.DiscardAsked, "asked");
        Assert.True(form.IsNewMode && form.IsNewModeSelected, "Keep editing stays in New");
        Assert.Equal("DH-X", form.NewDrillholeId, "input kept");

        dialogs.DiscardNewDrillhole = true;
        form.IsExistingModeSelected = true;
        Assert.Equal(2, dialogs.DiscardAsked, "asked again");
        Assert.False(form.IsNewMode, "Discard: Existing");
        form.IsNewModeSelected = true;
        Assert.Equal("", form.NewDrillholeId, "input discarded");
        form.IsExistingModeSelected = true;

        await Make.Pick(form, "DH-002");
        form.IsSelectionLocked = true;
        Assert.False(form.CanSwitchMode, "switch disabled while measuring");
        form.IsNewModeSelected = true;
        Assert.False(form.IsNewMode, "refused while measuring");
        Assert.Equal("DH-002", form.DrillholeId, "drillhole kept");
        Assert.True(Make.Toasted(form.Toasts, ToastKind.Error, "Stop the measurement before changing the drillhole."), "toast");
        form.IsSelectionLocked = false;
        Assert.True(form.CanSwitchMode, "available after Stop");
    }

    [Test("FE-DH-NEW-06", "Core Information view: one Project/Campaign set per mode, one save button, no New drillhole panel")]
    public static async Task NewModeView()
    {
        Wpf.EnsureResources();
        using var folder = new TestFolder();
        var (main, _) = await Shell(folder);
        var window = new Frontend.MainWindow(main) { ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20_000 };

        try
        {
            window.Show();
            main.ShowPage(main.Core);
            window.UpdateLayout();

            var all = Ids(window, visibleOnly: false);
            Assert.False(all.Contains("NewDrillholeButton") || all.Contains("NewDrillholeForm") || all.Contains("SaveNewDrillholeButton"),
                "the old New drillhole link, panel and button are gone");
            Assert.False(Descendants(window).OfType<Button>().Any(b => b.Content is string text && text.Contains("Save drillhole")), "no Save drillhole");
            Assert.Equal(1, all.Count(id => id == "SaveAndContinueButton"), "one Save & Continue");

            var radios = Descendants(window).OfType<RadioButton>().Where(r => r.GroupName == "DrillholeMode").ToList();
            Assert.Sequence(new[] { "Existing", "New" }, radios.Select(r => r.Content as string), "Existing | New switch");
            Assert.True(radios[0].IsChecked == true, "Existing by default");

            var visible = Ids(window);
            Assert.True(visible.Contains("ProjectValue") && visible.Contains("CampaignValue"), "Existing: read-only Project and Campaign");
            Assert.False(visible.Contains("NewProjectBox") || visible.Contains("NewCampaignBox") || visible.Contains("NewDrillholeIdBox"),
                "Existing: no New inputs");
            Assert.True(Named<ComboBox>(window, "DrillholeBox").IsVisible, "Drillhole dropdown");

            radios[1].IsChecked = true;
            window.UpdateLayout();
            Assert.True(main.Core.IsNewMode, "New chosen in the view");
            visible = Ids(window);
            Assert.True(visible.Contains("NewDrillholeIdBox") && visible.Contains("NewProjectBox") && visible.Contains("NewCampaignBox"), "New inputs");
            Assert.False(visible.Contains("ProjectValue") || visible.Contains("CampaignValue"), "New: no read-only Project/Campaign");
            Assert.False(Named<ComboBox>(window, "DrillholeBox").IsVisible, "no Drillhole dropdown");
            Assert.False(Descendants(window).OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "SaveAndContinueButton").IsEnabled,
                "Save & Continue disabled while empty");

            double Left(string id) => Descendants(window).OfType<FrameworkElement>().First(e => AutomationProperties.GetAutomationId(e) == id)
                .TransformToAncestor(window).Transform(new Point()).X;
            Assert.True(Left("NewDrillholeIdBox") < Left("NewProjectBox") && Left("NewProjectBox") < Left("NewCampaignBox"),
                "Drillhole → Project → Campaign");

            main.Core.NewProject.Picked = main.Core.NewProject.Choices[^1];
            window.UpdateLayout();
            visible = Ids(window);
            Assert.True(visible.Contains("NewProjectText") && visible.Contains("CancelAddProject") && !visible.Contains("NewProjectBox"),
                "+ Add new project: text box with ×");

            radios[0].IsChecked = true;
            window.UpdateLayout();
            Assert.False(main.Core.IsNewMode, "back to Existing (nothing entered)");
            Assert.True(Ids(window).Contains("ProjectValue"), "read-only Project again");
        }
        finally
        {
            window.Close();
        }
    }

    [Test("FE-DH-NEW-07", "New drillhole while the backend is offline: saved locally, and nothing is ever sent for it")]
    public static async Task NewDrillholeOfflineIsNeverSynced()
    {
        using var folder = new TestFolder();
        var db = folder.Path("m.db");
        var backend = new StubBackend();
        var form = Make.CoreForm(backend, db, new MemoryRepository(db), out _, dialogs: new FakeDialogs());
        await form.LoadDrillholesAsync();

        backend.Offline = true;
        await form.LoadDrillholesAsync();
        form.IsNewModeSelected = true;
        form.NewDrillholeId = "DH-OFF-1";
        form.NewProject.Picked = form.NewProject.Choices[0];
        form.NewCampaign.Picked = form.NewCampaign.Choices[0];
        Assert.True(await form.SaveAsync(), "created offline");
        Assert.Equal("DH-OFF-1", form.DrillholeId, "selected");

        backend.Offline = false;
        await form.LoadDrillholesAsync();
        Assert.True(form.DrillholeOptions.Single(o => o.HoleId == "DH-OFF-1").IsLocal, "still Local once online");
        Assert.True(backend.Requests.All(r => r.StartsWith("GET ", StringComparison.Ordinal) || r.StartsWith("HEAD ", StringComparison.Ordinal)),
            $"only reads were sent: {string.Join(", ", backend.Requests.Distinct())}");
        Assert.Sequence(new[] { "DH-OFF-1" }, (await new DrillholeCache(db).LoadLocalAsync()).Select(d => d.HoleId!), "stored locally");
    }

    [Test("FE-PAGE-01", "Right-click capture is only accepted on the Measurement page; other pages add no row")]
    public static async Task CaptureOnlyOnMeasurementPage()
    {
        using var folder = new TestFolder();
        var (main, device) = await Shell(folder);
        using var _main = main;
        await Arm(main);
        Assert.True(main.Measurement.TriggerCommand.CanExecute(DateTimeOffset.Now), "accepted on the Measurement page");

        foreach (var page in new Frontend.Shared.Infrastructure.ObservableObject[] { main.Core, main.Monitoring, main.UploadData, main.DataViewer })
        {
            main.ShowPage(page);
            Assert.False(main.IsMeasurementPageActive, "another page");
            Assert.False(main.Measurement.TriggerCommand.CanExecute(DateTimeOffset.Now), $"refused on {page.GetType().Name}");
            Assert.Equal(MeasurementState.Ready, main.Measurement.State, "measurement mode stays on");
        }

        Assert.Equal(0, device.Calls, "no capture started");
        Assert.Equal(0, main.Measurement.Results.Count, "no row");

        main.ShowPage(main.Measurement);
        await Make.Capture(main.Measurement, device);
        Assert.Equal(1, main.Measurement.Results.Count, "captures again back on the Measurement page");
    }

    [Test("FE-PAGE-02", "Leaving the Measurement page during a capture cancels it; late device data is not recorded")]
    public static async Task LeavingCancelsCapture()
    {
        using var folder = new TestFolder();
        var (main, device) = await Shell(folder);
        using var _main = main;
        await Arm(main);

        main.Measurement.TriggerCommand.Execute(DateTimeOffset.Now);
        Assert.Equal(MeasurementState.Capturing, main.Measurement.State, "capturing");
        main.ShowPage(main.Core);
        device.Complete();   // the device answers after the page was left

        await Assert.Eventually(() => main.Measurement.State == MeasurementState.Ready, "back to Ready");
        await Assert.Remains(() => main.Measurement.Results.Count == 0, "no row recorded");
        Assert.True(Make.ToastedStart(main.Toasts, ToastKind.Warning, "Capture cancelled because you left the Measurement page"), "told why");
    }

    private sealed class FakeLiveCps : ILiveCpsReader
    {
        public double? Value { get; set; }

        public int Reads;

        public double? ReadLatestCps()
        {
            Interlocked.Increment(ref Reads);
            return Value;
        }
    }

    [Test("FE-LIVE-01", "Live CPS follows the newest reading while the Measurement page is shown and stops elsewhere")]
    public static async Task LiveCpsFollowsReadings()
    {
        using var folder = new TestFolder();
        var reader = new FakeLiveCps { Value = 12.345 };
        var vm = new MeasurementViewModel(new ControlledMeasurements(), new FakeExport(), new FakeDialogs(),
            new MemoryRepository(folder.Path("m.db")), liveCps: reader, liveCpsInterval: TimeSpan.FromMilliseconds(20));
        using var _vm = vm;

        await Assert.Eventually(() => vm.LiveValue == "12.35", "first reading (two decimals, CPS only)");
        reader.Value = 80;
        await Assert.Eventually(() => vm.LiveValue == "80.00", "updates in real time");
        reader.Value = null;
        await Assert.Eventually(() => vm.LiveValue == MeasurementViewModel.NoLiveCps, "no recent reading");

        reader.Value = 5;
        vm.OnHidden();
        Assert.Equal(MeasurementViewModel.NoLiveCps, vm.LiveValue, "cleared when the page is left");
        var reads = reader.Reads;
        await Task.Delay(150);
        Assert.True(reader.Reads <= reads + 1, $"no device reads outside the page ({reads} → {reader.Reads})");
        Assert.Equal(MeasurementViewModel.NoLiveCps, vm.LiveValue, "nothing shown outside the page");

        vm.OnShown();
        await Assert.Eventually(() => vm.LiveValue == "5.00", "resumes on return");

        // CPS | CPM switch: CPM = CPS × 60 (whole counts), applied at once and kept across page switches.
        Assert.Equal(LiveRateUnit.Cps, vm.LiveUnit, "CPS by default");
        vm.IsLiveUnitCpm = true;
        Assert.Equal("300", vm.LiveValue, "5 CPS = 300 CPM, without waiting for the next reading");
        Assert.True(vm.IsLiveUnitCpm && !vm.IsLiveUnitCps, "one unit selected");
        reader.Value = 118.42;
        await Assert.Eventually(() => vm.LiveValue == "7105", "118.42 CPS = 7105 CPM");
        vm.OnHidden();
        vm.OnShown();
        Assert.Equal(LiveRateUnit.Cpm, vm.LiveUnit, "unit kept when the page is left and shown again");
        await Assert.Eventually(() => vm.LiveValue == "7105", "still CPM after returning");
        vm.IsLiveUnitCps = true;
        Assert.Equal("118.42", vm.LiveValue, "back to CPS");
        Assert.Equal(MeasurementViewModel.NoLiveCps, MeasurementViewModel.FormatLiveValue(null, LiveRateUnit.Cpm), "no reading in CPM either");
    }

    [Test("FE-LIVE-02", "GammaGuardLiveCps: newest sample of the detector as CPS; stale or missing data gives none")]
    public static void LiveCpsReader()
    {
        var now = 1_760_600_010_000L;
        var samples = new StubSamples
        {
            Rows =
            [
                new DeviceSample(1, "SN-1", null, 1, now - 2000, 600, null, null, null, null, null, null),
                new DeviceSample(2, "SN-1", null, 2, now - 1000, 1200, null, null, null, null, null, null),
            ]
        };
        var clock = new StubClock(now);
        var reader = new GammaGuardLiveCps(new StubResolver(samples), clock);
        Assert.Equal(20.0, reader.ReadLatestCps(), "1200 counts/min = 20 CPS (newest sample)");

        clock.NowMs = now + 10_000;
        Assert.True(reader.ReadLatestCps() is null, "stale reading");

        Assert.True(new GammaGuardLiveCps(new FailingResolver()).ReadLatestCps() is null, "no detector: none, never throws");
    }

    private sealed class StubSamples : ISampleSource
    {
        public List<DeviceSample> Rows { get; init; } = [];

        public long? MaxSampleId() => Rows.Count == 0 ? null : Rows.Max(r => r.SourceId);

        public IReadOnlyList<DeviceSample> ReadSamplesAfter(string serial, long afterId) =>
            Rows.Where(r => r.Serial == serial && r.SourceId > afterId).ToList();

        public long? LatestTsMs(string serial) => Rows.Max(r => r.TsMs);
    }

    private sealed class StubClock(long now) : ICaptureClock
    {
        public long NowMs { get; set; } = now;

        public Task Delay(TimeSpan delay, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StubResolver(ISampleSource samples) : ICaptureDeviceResolver
    {
        public CaptureDevice Resolve() => new(new DetectorIdentity("SN-1", "CT007-M", null, null, null), samples);
    }

    private sealed class FailingResolver : ICaptureDeviceResolver
    {
        public CaptureDevice Resolve() => throw new CaptureException("The Gamma Guard database is not available.");
    }

    [Test("FE-THRESH-01", "Graph: the threshold is a horizontal line at its CPS; Show/Hide toggles it")]
    public static void ThresholdLine()
    {
        Wpf.EnsureResources();
        var session = Guid.NewGuid();
        var rows = new[]
        {
            Make.Row(session, 1, 0, reading: new DeviceReading(Count0: 50)),
            Make.Row(session, 2, 250, reading: new DeviceReading(Count0: 100)),
        };
        var chart = new DepthBarChart { ItemsSource = rows, Threshold = 150, ShowThreshold = true };

        double? Render()
        {
            chart.Measure(new Size(600, 300));
            chart.Arrange(new Rect(0, 0, 600, 300));
            new RenderTargetBitmap(600, 300, 96, 96, PixelFormats.Pbgra32).Render(chart);
            return chart.DrawnThresholdY;
        }

        var y = Render();
        Assert.True(y is not null, "line drawn");
        Assert.True(y > 8 && y < 300 - 34, $"inside the plot although above every bar ({y})");

        chart.Threshold = 75;
        var lower = Render();
        Assert.True(lower > y, $"a lower threshold is drawn lower ({lower} > {y})");

        chart.ShowThreshold = false;
        Assert.True(Render() is null, "hidden");

        chart.ShowThreshold = true;
        chart.Threshold = double.NaN;
        Assert.True(Render() is null, "no threshold, no line");

        // Every graph state: empty (before Start), with rows (measuring / after Stop), cleared (New Measurement).
        var live = new ObservableCollection<MeasurementResult>();
        chart.ItemsSource = live;
        chart.Threshold = 150;
        Assert.True(Render() is { } empty && empty > 8 && empty < 300 - 34, "empty graph before measuring: line drawn inside the plot");
        live.Add(rows[0]);
        Assert.True(Render() is not null, "during measurement");
        live.Add(rows[1]);
        Assert.True(Render() is not null, "after more captures (rescaled)");
        live.Clear();
        Assert.True(Render() is not null, "after the graph is cleared");
        chart.ShowThreshold = false;
        Assert.True(Render() is null, "hidden on the empty graph too");

        // The view model drives it: the Threshold box and the Show/Hide button.
        using var folder = new TestFolder();
        using var vm = Make.Measurement(new MemoryRepository(folder.Path("m.db")));
        vm.Threshold = "150";
        Assert.Equal(150.0, vm.ThresholdCps, "threshold for the graph");
        Assert.True(vm.IsThresholdLineVisible, "shown by default");
        Assert.Equal("Hide threshold", vm.ThresholdLineButtonText, "button text");
        vm.ToggleThresholdLineCommand.Execute(null);
        Assert.False(vm.IsThresholdLineVisible, "hidden");
        Assert.Equal("Show threshold", vm.ThresholdLineButtonText, "button text after hiding");
        vm.Threshold = "";
        Assert.True(double.IsNaN(vm.ThresholdCps), "empty threshold: no line");
    }

    [Test("FE-HELP-05", "Help: ? opens or brings to front, F1 toggles, Esc closes; one Help window only")]
    public static async Task HelpToggleSingleInstance()
    {
        Wpf.EnsureResources();
        using var folder = new TestFolder();
        var (main, _) = await Shell(folder, HelpSettings.Load(AppContext.BaseDirectory));
        var window = new Frontend.MainWindow(main) { ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20_000 };

        try
        {
            window.Show();
            Assert.True(window.HelpWindowInstance is null, "no Help window before it is asked for");

            main.ShowHelp();                                    // ? button
            var help = window.HelpWindowInstance;
            Assert.True(help is { IsVisible: true }, "? opens Help");
            Assert.Equal("Core Information", main.Help.Page?.Page, "at the current page");

            main.ShowHelp();                                    // ? again while open
            Assert.True(ReferenceEquals(help, window.HelpWindowInstance), "same window, no second copy");
            Assert.True(help!.IsVisible && main.Help.IsOpen, "? never closes it");
            Assert.Equal(1, Application.Current.Windows.OfType<HelpWindow>().Count(w => w.IsVisible), "one Help window on screen");

            main.ToggleHelp(HelpTrigger.Key);                   // F1 while open
            Assert.False(main.Help.IsOpen || help.IsVisible, "F1 closes");
            main.ToggleHelp(HelpTrigger.Key);                   // F1 while closed
            Assert.True(main.Help.IsOpen && help.IsVisible, "F1 opens");
            Assert.True(ReferenceEquals(help, window.HelpWindowInstance), "reopened, not recreated");

            Assert.True(Frontend.MainWindow.HandleHelpShortcut(Key.Escape, main.Help, main.ToggleHelp), "Esc handled while open");
            Assert.False(help.IsVisible, "Esc closes");
            Assert.False(Frontend.MainWindow.HandleHelpShortcut(Key.Escape, main.Help, main.ToggleHelp), "Esc left alone while Help is closed");

            help.Close();                                       // the window's ×
            Assert.True(help.IsLoaded && !help.IsVisible, "× only hides");
        }
        finally
        {
            window.Close();
        }
    }

    [Test("FE-HELP-06", "F1 and Esc reach Help whatever has keyboard focus, in the main window and in Help, on every page")]
    public static async Task HelpShortcutsAnyFocus()
    {
        Wpf.EnsureResources();
        using var folder = new TestFolder();
        var (main, _) = await Shell(folder, HelpSettings.Load(AppContext.BaseDirectory));
        var window = new Frontend.MainWindow(main) { ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20_000 };

        static void Press(UIElement target, Key key)
        {
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target)!, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            target.RaiseEvent(args);
        }

        try
        {
            window.Show();

            foreach (var page in main.Pages)
            {
                main.ShowPage(page.Page);
                window.UpdateLayout();
                var inner = (UIElement?)Descendants(window).OfType<TextBox>().FirstOrDefault()
                    ?? Descendants(window).OfType<Button>().First();

                Press(inner, Key.F1);
                Assert.True(main.Help.IsOpen, $"{page.Title}: F1 from a control inside the page opens Help");
                Assert.Equal(page.Title, main.Help.Page?.Page, $"{page.Title}: Help at this page");

                var help = window.HelpWindowInstance!;
                help.UpdateLayout();
                Press(help.CloseHelpButton, Key.F1);
                Assert.False(main.Help.IsOpen, $"{page.Title}: F1 inside Help closes it");

                Press(inner, Key.F1);
                Press(help.CloseHelpButton, Key.Escape);
                Assert.False(main.Help.IsOpen, $"{page.Title}: Esc inside Help closes it");

                Press(inner, Key.F1);
                Press(inner, Key.Escape);
                Assert.False(main.Help.IsOpen, $"{page.Title}: Esc in the main window closes it");
            }

            main.ShowPage(main.Core);
            main.ShowHelp();
            window.HelpWindowInstance!.CloseHelpButton.Command.Execute(null);
            Assert.False(main.Help.IsOpen, "the Close button closes Help");
            Assert.True(window.HelpWindowInstance.CloseHelpButton.MinWidth >= 120 && window.HelpWindowInstance.CloseHelpButton.Height >= 40, "Close button is large");
        }
        finally
        {
            window.Close();
        }
    }

    [Test("FE-HELP-07", "Help is context-sensitive: it scrolls to the section of the page on screen; every page has a section")]
    public static async Task HelpScrollsToPage()
    {
        Wpf.EnsureResources();
        using var folder = new TestFolder();
        var settings = HelpSettings.Load(AppContext.BaseDirectory);
        var (main, _) = await Shell(folder, settings);
        var window = new Frontend.MainWindow(main) { ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20_000 };

        try
        {
            window.Show();
            main.ShowHelp();
            var help = window.HelpWindowInstance!;
            var offsets = new List<double>();

            foreach (var page in main.Pages)
            {
                main.ShowPage(page.Page);
                await Task.Delay(60);   // the scroll runs once layout is done
                Assert.Equal(page.Title, main.Help.Page?.Page, $"{page.Title}: section selected");
                offsets.Add(help.LastSectionOffset);
            }

            Assert.Equal(0.0, offsets[0], "first section at the top");
            Assert.True(offsets.Zip(offsets.Skip(1)).All(p => p.Second > p.First), $"each later page scrolls further ({string.Join(", ", offsets)})");
            Assert.True(settings.HasShortcuts && settings.Shortcuts.Any(s => s.Keys == "F1") && settings.Shortcuts.Any(s => s.Keys == "Esc"),
                "keyboard shortcuts listed");
        }
        finally
        {
            window.Close();
        }
    }

    [Test("FE-THRESH-02", "Threshold eye: the Show/Hide state survives Start, captures, Stop, New Measurement and page switches")]
    public static async Task ThresholdStatePersists()
    {
        Wpf.EnsureResources();
        using var folder = new TestFolder();
        var (main, device) = await Shell(folder);
        var window = new Frontend.MainWindow(main) { ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20_000 };

        try
        {
            window.Show();
            await Arm(main);
            main.Measurement.Threshold = "150";
            window.UpdateLayout();

            var eye = Named<Button>(window, "ThresholdLineToggle");
            Assert.Equal("Show or hide the threshold line", eye.ToolTip as string, "tooltip");
            var icon = (System.Windows.Shapes.Path)eye.Content;
            Assert.True(ReferenceEquals(icon.Data, Application.Current.FindResource("EyeIcon")), "open eye while shown");

            eye.Command.Execute(null);
            Assert.False(main.Measurement.IsThresholdLineVisible, "eye hides the line");
            Assert.True(ReferenceEquals(icon.Data, Application.Current.FindResource("EyeOffIcon")), "crossed-out eye while hidden");

            await Make.Capture(main.Measurement, device);
            Assert.False(main.Measurement.IsThresholdLineVisible, "still hidden after a capture");
            main.ShowPage(main.Monitoring);
            main.ShowPage(main.Measurement);
            Assert.False(main.Measurement.IsThresholdLineVisible, "still hidden after a page switch");
            main.Measurement.ToggleMeasurementCommand.Execute(null);   // Stop
            Assert.False(main.Measurement.IsThresholdLineVisible, "still hidden after Stop");
            main.Measurement.NewMeasurementCommand.Execute(null);
            Assert.False(main.Measurement.IsThresholdLineVisible, "still hidden after New Measurement");

            main.ShowPage(main.Measurement);
            window.UpdateLayout();
            eye = Named<Button>(window, "ThresholdLineToggle");
            eye.Command.Execute(null);
            Assert.True(main.Measurement.IsThresholdLineVisible, "eye shows it again");
            window.UpdateLayout();
            var chart = Descendants(window).OfType<DepthBarChart>().Single();
            new RenderTargetBitmap(Math.Max(1, (int)chart.ActualWidth), Math.Max(1, (int)chart.ActualHeight), 96, 96, PixelFormats.Pbgra32).Render(chart);
            Assert.True(chart.DrawnThresholdY is not null, "line drawn on the cleared graph");
        }
        finally
        {
            window.Close();
        }
    }

    [Test("FE-LIVE-03", "Live value sits left of the eye icon on the graph's header row, with a CPS | CPM switch")]
    public static async Task LiveBoxBesideEye()
    {
        Wpf.EnsureResources();
        using var folder = new TestFolder();
        var (main, _) = await Shell(folder);
        var window = new Frontend.MainWindow(main) { ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20_000 };

        try
        {
            window.Show();
            main.ShowPage(main.Measurement);
            window.UpdateLayout();

            var live = Named<Border>(window, "LiveBox");
            var eye = Named<Button>(window, "ThresholdLineToggle");
            var header = Named<StackPanel>(window, "GraphHeader");
            Assert.True(ReferenceEquals(VisualTreeHelper.GetParent(live), header) && ReferenceEquals(VisualTreeHelper.GetParent(eye), header), "same row");
            var liveBox = live.TransformToAncestor(window).TransformBounds(new Rect(live.RenderSize));
            var eyeBox = eye.TransformToAncestor(window).TransformBounds(new Rect(eye.RenderSize));
            Assert.True(liveBox.Right <= eyeBox.Left, $"live box left of the eye ({liveBox.Right} ≤ {eyeBox.Left})");
            Assert.True(Math.Abs((liveBox.Top + liveBox.Bottom) / 2 - (eyeBox.Top + eyeBox.Bottom) / 2) < 2, "vertically aligned");

            var units = Descendants(live).OfType<RadioButton>().ToList();
            Assert.Sequence(new[] { "CPS", "CPM" }, units.Select(u => u.Content as string), "unit switch");
            units[1].IsChecked = true;
            Assert.Equal(LiveRateUnit.Cpm, main.Measurement.LiveUnit, "CPM chosen in the view");
            units[0].IsChecked = true;
            Assert.Equal(LiveRateUnit.Cps, main.Measurement.LiveUnit, "CPS chosen in the view");
        }
        finally
        {
            window.Close();
        }
    }

    [Test("FE-HELP-08", "Help window is owned by the main window and moves with it")]
    public static async Task HelpFollowsMainWindow()
    {
        Wpf.EnsureResources();
        using var folder = new TestFolder();
        var (main, _) = await Shell(folder, HelpSettings.Load(AppContext.BaseDirectory));
        var window = new Frontend.MainWindow(main)
        {
            ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20_000, Top = 100, Width = 1200, Height = 800
        };

        try
        {
            window.Show();
            main.ShowHelp();
            var help = window.HelpWindowInstance!;
            Assert.True(ReferenceEquals(help.Owner, window), "owned by the main window (minimizes and restores with it)");

            // The user puts Help somewhere, then moves the main window.
            help.Left = -19_500;
            help.Top = 180;
            var before = new Point(help.Left, help.Top);
            window.Left += 100;
            window.Top += 50;
            Assert.True(Math.Abs(before.X + 100 - help.Left) < 1, $"moved right with the main window ({help.Left})");
            Assert.True(Math.Abs(before.Y + 50 - help.Top) < 1, $"moved down with the main window ({help.Top})");

            // Closed while the main window moves: it reopens at the same place relative to the main window. (Help is put on
            // the screen first, so opening does not pull it back from off-screen as it would from the test window's place.)
            help.Left = SystemParameters.VirtualScreenLeft + 100;
            main.Help.Close();
            window.Left -= 40;
            main.ShowHelp();
            var expected = SystemParameters.VirtualScreenLeft + 60;
            Assert.True(Math.Abs(expected - help.Left) < 1, $"reopened relative to the main window ({help.Left}, expected {expected})");
        }
        finally
        {
            window.Close();
        }
    }

    [Test("FE-CHART-01", "Graph: the Y-axis title sits right next to the tick labels, whatever their width")]
    public static void YAxisTitleNextToTicks()
    {
        Wpf.EnsureResources();
        var session = Guid.NewGuid();

        foreach (var cps in new[] { 5.0, 120.0, 25_000.0 })
        {
            var chart = new DepthBarChart
            {
                ItemsSource = new[] { Make.Row(session, 1, 0, reading: new DeviceReading(Count0: cps)) },
                YAxisLabel = "CPS",
                Threshold = double.NaN,
            };
            chart.Measure(new Size(600, 300));
            chart.Arrange(new Rect(0, 0, 600, 300));
            new RenderTargetBitmap(600, 300, 96, 96, PixelFormats.Pbgra32).Render(chart);

            var gap = chart.DrawnTickLabelsLeft!.Value - chart.DrawnYTitleRight!.Value;
            Assert.True(gap >= 0 && gap <= 8, $"{cps} CPS: title {gap:0.0} px from the tick labels");
        }
    }

    private static T Named<T>(DependencyObject root, string name) where T : FrameworkElement =>
        Descendants(root).OfType<T>().FirstOrDefault(e => e.Name == name) ?? throw new InvalidOperationException($"{name} not found");

    // AutomationIds of the (visible) elements under root.
    private static string[] Ids(DependencyObject root, bool visibleOnly = true) => Descendants(root).OfType<FrameworkElement>()
        .Where(e => !visibleOnly || e.IsVisible).Select(AutomationProperties.GetAutomationId).Where(id => id.Length > 0).ToArray();

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;

            foreach (var nested in Descendants(child))
            {
                yield return nested;
            }
        }
    }
}
