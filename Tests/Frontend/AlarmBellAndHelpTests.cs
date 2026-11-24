using Frontend.Shared.Help;

namespace Ct007.Tests.FrontendTests;

/// <summary>FE-ALM: the alarm bell beside Threshold. FE-HELP: the Help panel and help.yaml.</summary>
public static class AlarmBellAndHelpTests
{
    [Test("FE-ALM-01", "The bell follows the threshold: off when empty or invalid, armed when valid, muted on click")]
    public static void BellFollowsThreshold()
    {
        using var folder = new TestFolder();
        using var vm = new MeasurementViewModel(new ControlledMeasurements(), new FakeExport(), new FakeDialogs(), new MemoryRepository(folder.Path("m.db")),
            durationSettings: new CaptureDurationSettings(null), alarm: new CpsAlarm(() => { }, TimeSpan.Zero), defaultThreshold: 150);

        Assert.Equal(AlarmState.Armed, vm.Alarm, "default threshold: armed");
        Assert.Contains("above 150 CPS", vm.AlarmDescription, "tooltip names the threshold");

        vm.Threshold = "";
        Assert.Equal(AlarmState.Off, vm.Alarm, "empty: off");
        vm.Threshold = "abc";
        Assert.Equal(AlarmState.Off, vm.Alarm, "invalid: off");
        vm.Threshold = "200";
        Assert.Equal(AlarmState.Armed, vm.Alarm, "valid: armed");

        vm.ToggleAlarmMuteCommand.Execute(null);
        Assert.Equal(AlarmState.Muted, vm.Alarm, "click: muted");
        Assert.Contains("unmute", vm.AlarmDescription, "tooltip says how to unmute");
        vm.ToggleAlarmMuteCommand.Execute(null);
        Assert.Equal(AlarmState.Armed, vm.Alarm, "click again: armed");
    }

    [Test("FE-ALM-02", "A row above the threshold turns the bell red; muted, no tone plays; a row below clears it")]
    public static async Task BellTriggers()
    {
        using var folder = new TestFolder();
        var device = new ControlledMeasurements();
        var played = 0;
        using var vm = new MeasurementViewModel(device, new FakeExport(), new FakeDialogs(), new MemoryRepository(folder.Path("m.db")),
            durationSettings: new CaptureDurationSettings(null), alarm: new CpsAlarm(() => played++, TimeSpan.Zero), defaultThreshold: 150);
        await Make.Arm(vm, Make.Core);

        static void Complete(ControlledMeasurements d, double cps) => d.CompleteWith(new CaptureData(
            d.LastStart, d.LastStart + d.LastDuration, [1.0], "cps", d.LastStart + d.LastDuration, new DeviceReading(Count0: cps)));

        await Make.Capture(vm, device, d => Complete(d, 175));
        Assert.Equal(AlarmState.Triggered, vm.Alarm, "above: triggered");
        Assert.Equal(1, played, "tone played");

        await Make.Capture(vm, device, d => Complete(d, 100));
        Assert.Equal(AlarmState.Armed, vm.Alarm, "below: cleared");

        vm.IsAlarmMuted = true;
        await Make.Capture(vm, device, d => Complete(d, 180));
        Assert.Equal(AlarmState.Triggered, vm.Alarm, "muted: still shown");
        Assert.Equal(1, played, "muted: no tone");
        Assert.Contains("Sound muted", vm.AlarmDescription, "tooltip says the sound is muted");

        vm.Threshold = "300";
        Assert.Equal(AlarmState.Muted, vm.Alarm, "threshold change clears the trigger");
    }

    [Test("FE-ALM-03", "The bell icon: crossed when muted, also after the alarm went off; plain bell again when unmuted")]
    public static async Task BellIconShowsMute()
    {
        Wpf.EnsureResources();
        using var folder = new TestFolder();
        var device = new ControlledMeasurements();
        using var vm = new MeasurementViewModel(device, new FakeExport(), new FakeDialogs(), new MemoryRepository(folder.Path("m.db")),
            durationSettings: new CaptureDurationSettings(null), alarm: new CpsAlarm(() => { }, TimeSpan.Zero), defaultThreshold: 150);
        await Make.Arm(vm, Make.Core);
        var panel = new Frontend.Pages.Measurement.CapturePanel.CapturePanelView { DataContext = vm };
        var window = new System.Windows.Window
        {
            Content = panel, Width = 900, Height = 400, ShowActivated = false, WindowStyle = System.Windows.WindowStyle.None, Left = -10_000
        };
        var bell = System.Windows.Application.Current.FindResource("BellIcon");
        var bellOff = System.Windows.Application.Current.FindResource("BellOffIcon");

        try
        {
            window.Show();
            await Task.Delay(50);
            var button = Descendants<System.Windows.Controls.Button>(panel).Single(b => b.Command == vm.ToggleAlarmMuteCommand);
            var icon = Descendants<System.Windows.Shapes.Path>(button).Single();

            Assert.True(ReferenceEquals(bell, icon.Data), "armed: bell");
            vm.ToggleAlarmMuteCommand.Execute(null);
            Assert.True(ReferenceEquals(bellOff, icon.Data), "muted: crossed bell");
            vm.ToggleAlarmMuteCommand.Execute(null);

            await Make.Capture(vm, device, d => d.CompleteWith(new CaptureData(
                d.LastStart, d.LastStart + d.LastDuration, [1.0], "cps", d.LastStart + d.LastDuration, new DeviceReading(Count0: 175))));
            Assert.Equal(AlarmState.Triggered, vm.Alarm, "above the threshold");
            Assert.True(ReferenceEquals(bell, icon.Data), "went off: bell");

            vm.ToggleAlarmMuteCommand.Execute(null);
            Assert.True(ReferenceEquals(bellOff, icon.Data), "went off, then muted: crossed bell");
            vm.ToggleAlarmMuteCommand.Execute(null);
            Assert.True(ReferenceEquals(bell, icon.Data), "unmuted: bell again");
        }
        finally
        {
            window.Close();
        }
    }

    private static IEnumerable<T> Descendants<T>(System.Windows.DependencyObject root) where T : System.Windows.DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);

            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in Descendants<T>(child))
            {
                yield return nested;
            }
        }
    }

    [Test("FE-HELP-01", "help.yaml follows the 6-part template for every sidebar page; the drawer follows navigation")]
    public static void HelpCoversEveryPage()
    {
        var help = HelpSettings.Load(AppContext.BaseDirectory);
        using var folder = new TestFolder();
        using var main = Start(folder, help);

        foreach (var page in main.Pages)
        {
            var section = help.For(page.Title);
            Assert.True(section is not null, $"help for '{page.Title}'");
            Assert.True(section!.Purpose.Length > 0 && section.HasBeforeYouStart && section.HasImage, $"{page.Title}: purpose, Before you start, screenshot");
            Assert.True(section.Steps.Count >= 3 && section.Items.Count > 0 && section.Troubleshooting.Count >= 3,
                $"{page.Title}: steps, buttons and fields, troubleshooting");
            Assert.True(section.Markers.Count >= 3, $"{page.Title}: numbered markers on the screenshot");
        }

        main.ToggleHelp();
        Assert.True(main.Help.IsOpen, "opened");
        Assert.Equal("Core Information", main.Help.Page?.Page, "current page's help");
        main.ShowPage(main.DataViewer);
        Assert.Equal("Data Viewer", main.Help.Page?.Page, "follows navigation");
        main.Help.CloseCommand.Execute(null);
        Assert.False(main.Help.IsOpen, "closed");
    }

    [Test("FE-HELP-03", "Blocking information comes first: Measurement's Before you start, the right-click warning, Start / Stop first")]
    public static void HelpImportantFirst()
    {
        var help = HelpSettings.Load(AppContext.BaseDirectory);
        var measurement = help.For("Measurement")!;

        Assert.True(measurement.HasBeforeYouStart, "Measurement has Before you start");
        Assert.True(measurement.StepsCallouts.Any(c => c.Kind == "warning" && c.Text.Contains("right-click", StringComparison.OrdinalIgnoreCase)),
            "a Warning names the right-click capture");
        Assert.Equal("Start / Stop", measurement.Items[0].Control, "most important control first");

        using var folder = new TestFolder();
        Directory.CreateDirectory(folder.Path("Config"));
        File.WriteAllText(folder.Path(HelpSettings.File), Minimal(""));
        var page = HelpSettings.Load(folder.Root).Pages[0];
        Assert.False(page.HasBeforeYouStart || page.HasImage || page.Callouts.Count > 0, "Before you start, image and callouts are optional");
    }

    [Test("FE-HELP-04", "Help stays short: one plain sentence per line (does 90 characters, the rest 100)")]
    public static void HelpIsShort()
    {
        var help = HelpSettings.Load(AppContext.BaseDirectory);

        foreach (var page in help.Pages)
        {
            Assert.True(page.Purpose.Length <= 100, $"{page.Page} purpose is {page.Purpose.Length} characters");
            Assert.True(page.BeforeYouStart.Count <= 2, $"{page.Page} has at most 2 Before you start lines");

            foreach (var line in page.BeforeYouStart.Concat(page.Steps).Concat(page.Callouts.Select(c => c.Text))
                         .Concat(page.Troubleshooting.SelectMany(t => new[] { t.Problem, t.Fix })))
            {
                Assert.True(line.Length <= 100, $"{page.Page}: {line.Length} characters: {line}");
            }

            foreach (var item in page.Items)
            {
                Assert.True(item.Does.Length <= 90, $"{page.Page} / {item.Control} is {item.Does.Length} characters");
            }
        }
    }

    [Test("FE-HELP-02","An invalid help.yaml fails start-up naming the file and the key")]
    public static void InvalidHelp()
    {
        using var folder = new TestFolder();
        Directory.CreateDirectory(folder.Path("Config"));

        void Fails(string yaml, string key, string what)
        {
            File.WriteAllText(folder.Path(HelpSettings.File), yaml);
            Assert.Contains(key, Assert.Throws<InvalidDataException>(() => HelpSettings.Load(folder.Root), what).Message, $"{what}: names the key");
        }

        Fails("pages:\n  - page: Measurement\n    purpose: x\n    steps: [a]\n    items: []\n    troubleshooting: [{problem: p, fix: f}]\n",
            "help.yaml: pages[0].items", "empty items");
        Fails("pages:\n  - page: Measurement\n    purpose: x\n    steps: []\n    items: [{control: c, does: d}]\n    troubleshooting: [{problem: p, fix: f}]\n",
            "help.yaml: pages[0].steps", "empty steps");
        Fails(Minimal("    markers: [{n: 2, x: 0.5, y: 0.5}]\n"), "pages[0].markers[0].n", "marker without an item");
        Fails(Minimal("    markers: [{n: 1, x: 1.5, y: 0.5}]\n"), "pages[0].markers[0]", "marker outside the picture");
        Fails(Minimal("    callouts: [{kind: hint, section: steps, text: t}]\n"), "pages[0].callouts[0].kind", "unknown callout kind");
        Fails(Minimal("    callouts: [{kind: tip, section: footer, text: t}]\n"), "pages[0].callouts[0].section", "unknown callout section");
        Fails("pages:\n  - page: Measurement\n    purpose: x\n    steps: [a]\n    items:\n      - control: Start\n    troubleshooting: [{problem: p, fix: f}]\n",
            "help.yaml", "missing 'does'");
    }

    // One valid page, with extra keys appended.
    private static string Minimal(string extra) =>
        "pages:\n  - page: Measurement\n    purpose: x\n    steps: [a]\n    items: [{control: c, does: d, marker: 1}]\n"
        + "    troubleshooting: [{problem: p, fix: f}]\n" + extra;

    private static MainViewModel Start(TestFolder folder, HelpSettings help) =>
        new(new MemoryRepository(folder.Path("m.db")), new ApiSettings("http://stub.test", TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(5)),
            Make.Devices(folder.Path("gg.sqlite")), new StubBackend(), bluetoothDevices: () => [], captureService: new ControlledMeasurements(),
            dialogs: new FakeDialogs(), helpSettings: help);
}
