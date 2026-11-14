using Frontend.Config;
using Frontend.Pages.Monitoring;

namespace Ct007.Tests.FrontendTests;

/// <summary>FE-UI: the Monitoring page lays out with live data and without binding errors (Data Viewer: FE-DV-04).</summary>
public static class NewPagesViewTests
{
    private const long Now = 1_760_600_000_000;

    [Test("FE-UI-01", "Monitoring page renders detectors, a warning and samples without binding errors")]
    public static async Task MonitoringPageRenders()
    {
        Wpf.EnsureResources();
        var now = Now + 6000;
        var vm = new MonitoringViewModel(
            () => @"C:\GammaGuard\CT008 1.9.0\Database\ct008-log.sqlite", MonitoringSettings.Default, () => { },
            read: (_, after, _) => new MonitorSnapshot(3, 12,
            [
                new DetectorStatusRow("MR-24017", "MicroR North", "00:1A:7D:DA:71:13", "3.4.2", 14, "s-88", "logging", 1, now, now),
                new DetectorStatusRow("MR-24018", "MicroR South", "00:1A:7D:DA:71:14", "3.4.2", 14, "s-89", "logging", 2, now, now),
                new DetectorStatusRow("MR-23990", null, null, null, 14, null, "connected", null, Now - 60_000, Now - 60_000)
            ],
            after is null
                ? [Samples.At(10, now, 182, "MR-24017", doseRate: 0.112, temperature: 21.4), Samples.At(12, now, 240, "MR-24018", doseRate: 0.151, temperature: 22.0)]
                : []),
            clock: () => now);

        await vm.PollOnceAsync();
        vm.Engine.StartSample("MR-24017", now);
        var errors = await Wpf.Render(new MonitoringPage { DataContext = vm }, "monitoring");

        Assert.True(vm.WarningText.Contains("MicroR South"), "warning names the detector");
        Assert.Sequence(Array.Empty<string>(), errors, "binding errors");
        vm.Dispose();

        var empty = new MonitoringViewModel(() => null, MonitoringSettings.Default, () => { }, read: (_, _, _) => throw new InvalidOperationException());
        await empty.PollOnceAsync();
        Assert.Sequence(Array.Empty<string>(), await Wpf.Render(new MonitoringPage { DataContext = empty }, "monitoring-empty"), "binding errors (empty)");
    }
}

/// <summary>FE-UI-03: the whole window: fixed sidebar (order, name, version), Help panel and the alarm bell.</summary>
public static class MainWindowViewTests
{
    [Test("FE-UI-03", "Main window renders the sidebar, the Help panel and the alarm bell without binding errors")]
    public static async Task MainWindowRenders()
    {
        Wpf.EnsureResources();
        using var folder = new TestFolder();
        var main = new MainViewModel(new MemoryRepository(folder.Path("m.db")),
            new ApiSettings("http://stub.test", TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(5)),
            Make.Devices(folder.Path("gg.sqlite")), new StubBackend(), bluetoothDevices: () => [],
            captureService: new ControlledMeasurements(), dialogs: new FakeDialogs(),
            helpSettings: Frontend.Shared.Help.HelpSettings.Load(AppContext.BaseDirectory));
        main.Measurement.Threshold = "150";

        var errors = await Wpf.RenderWindow(new Frontend.MainWindow(main), "main-measurement-help", beforeCapture: () =>
        {
            main.ShowPage(main.Measurement);
            main.ShowHelp();
        });
        Assert.Sequence(Array.Empty<string>(), errors, "binding errors (Measurement + Help)");
        Assert.Equal(AlarmState.Armed, main.Measurement.Alarm, "bell armed");

        main = new MainViewModel(new MemoryRepository(folder.Path("m2.db")),
            new ApiSettings("http://stub.test", TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(5)),
            Make.Devices(folder.Path("gg.sqlite")), new StubBackend(), bluetoothDevices: () => [],
            captureService: new ControlledMeasurements(), dialogs: new FakeDialogs());
        errors = await Wpf.RenderWindow(new Frontend.MainWindow(main), "main-core");
        Assert.Sequence(Array.Empty<string>(), errors, "binding errors (Core Information)");
    }
}
