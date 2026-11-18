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
        Assert.Contains("above 150 CPS", vm.AlarmToolTip, "tooltip names the threshold");

        vm.Threshold = "";
        Assert.Equal(AlarmState.Off, vm.Alarm, "empty: off");
        vm.Threshold = "abc";
        Assert.Equal(AlarmState.Off, vm.Alarm, "invalid: off");
        vm.Threshold = "200";
        Assert.Equal(AlarmState.Armed, vm.Alarm, "valid: armed");

        vm.ToggleAlarmMuteCommand.Execute(null);
        Assert.Equal(AlarmState.Muted, vm.Alarm, "click: muted");
        Assert.Contains("unmute", vm.AlarmToolTip, "tooltip says how to unmute");
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
        Assert.Contains("Sound muted", vm.AlarmToolTip, "tooltip says the sound is muted");

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

    [Test("FE-HELP-01", "help.yaml has a non-empty section for every sidebar page; the panel follows navigation")]
    public static void HelpCoversEveryPage()
    {
        var help = HelpSettings.Load(AppContext.BaseDirectory);
        using var folder = new TestFolder();
        using var main = Start(folder, help);

        foreach (var page in main.Pages)
        {
            var section = help.For(page.Title);
            Assert.True(section is { Items.Count: > 0 } && section.Purpose.Length > 0, $"help for '{page.Title}'");
        }

        main.ShowHelp();
        Assert.True(main.Help.IsOpen, "opened");
        Assert.Equal("Core Information", main.Help.Page?.Page, "current page's help");
        main.ShowPage(main.DataViewer);
        Assert.Equal("Data Viewer", main.Help.Page?.Page, "follows navigation");
        main.Help.CloseCommand.Execute(null);
        Assert.False(main.Help.IsOpen, "closed");
    }

    [Test("FE-HELP-03", "Blocking information comes first: Measurement's important notes and Start / Stop lead its help")]
    public static void HelpImportantFirst()
    {
        var help = HelpSettings.Load(AppContext.BaseDirectory);
        var measurement = help.For("Measurement")!;

        Assert.True(measurement.HasImportant, "Measurement has important notes");
        Assert.True(measurement.Important.Any(n => n.Contains("right-click", StringComparison.OrdinalIgnoreCase)), "names the right-click capture");
        Assert.Equal("Start / Stop", measurement.Items[0].Control, "most important control first");

        using var folder = new TestFolder();
        Directory.CreateDirectory(folder.Path("Config"));
        File.WriteAllText(folder.Path(HelpSettings.File), "pages:\n  - page: Measurement\n    purpose: x\n    items:\n      - control: Start\n        does: y\n");
        Assert.False(HelpSettings.Load(folder.Root).Pages[0].HasImportant, "'important' is optional");
    }

    [Test("FE-HELP-04", "Help stays short: every 'does' is at most 90 characters, every purpose and important line at most 100")]
    public static void HelpIsShort()
    {
        var help = HelpSettings.Load(AppContext.BaseDirectory);

        foreach (var page in help.Pages)
        {
            Assert.True(page.Purpose.Length <= 100, $"{page.Page} purpose is {page.Purpose.Length} characters");
            Assert.True(page.Important.Count <= 2, $"{page.Page} has at most 2 important lines");

            foreach (var line in page.Important)
            {
                Assert.True(line.Length <= 100, $"{page.Page} important line is {line.Length} characters: {line}");
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

        File.WriteAllText(folder.Path(HelpSettings.File), "pages:\n  - page: Measurement\n    purpose: x\n    items: []\n");
        Assert.Contains("help.yaml: pages[0].items", Assert.Throws<InvalidDataException>(() => HelpSettings.Load(folder.Root), "empty items").Message, "names the key");

        File.WriteAllText(folder.Path(HelpSettings.File), "pages:\n  - page: Measurement\n    purpose: x\n    items:\n      - control: Start\n");
        Assert.Contains("help.yaml", Assert.Throws<InvalidDataException>(() => HelpSettings.Load(folder.Root), "missing 'does'").Message, "names the file");
    }

    private static MainViewModel Start(TestFolder folder, HelpSettings help) =>
        new(new MemoryRepository(folder.Path("m.db")), new ApiSettings("http://stub.test", TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(5)),
            Make.Devices(folder.Path("gg.sqlite")), new StubBackend(), bluetoothDevices: () => [], captureService: new ControlledMeasurements(),
            dialogs: new FakeDialogs(), helpSettings: help);
}
