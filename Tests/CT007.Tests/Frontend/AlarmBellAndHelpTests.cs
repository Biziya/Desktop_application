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

    [Test("FE-HELP-02", "An invalid help.yaml fails start-up naming the file and the key")]
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
