using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Frontend.Api.Measurements;
using Frontend.Pages.Measurement;
using Frontend.Shared.Help;

namespace Ct007.Tests.FrontendTests;

/// <summary>
/// PERF-001: repeatable numbers for performance work, measured in-process off-screen with fake devices and a real SQLite
/// measurement database. Opt-in, prints its numbers and never fails on them:
/// <code>dotnet run -c Release --project Tests -- PERF --include=Perf</code>
/// </summary>
public static class PerfMeasurements
{
    private const int NavigationCycles = 20;
    private const int Captures = 200;

    [Test("PERF-001", "Window start, page switch, memory growth and capture time (prints numbers)", Category = "Perf", TimeoutSeconds = 600)]
    public static async Task Measure()
    {
        Wpf.EnsureResources();
        using var folder = new TestFolder();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        using (var gg = new FakeGammaGuard(folder.Path("gg.sqlite")))
        {
            gg.Detector("SN-1001", "CT007-M-0001", "00:11:22:AA:BB:01", now);
        }

        var device = new ControlledMeasurements();
        var start = Stopwatch.StartNew();
        var repository = new TimedRepository(new SqliteMeasurementRepository(folder.Path("m.db")));
        var main = new MainViewModel(repository,
            new ApiSettings("http://stub.test", TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(5)),
            Make.Devices(folder.Path("gg.sqlite")), new StubBackend(),
            captureService: device, dialogs: new FakeDialogs(), helpSettings: HelpSettings.Load(AppContext.BaseDirectory),
            liveCps: new FixedLiveCps(100));
        var window = new Frontend.MainWindow(main)
        {
            Width = 1320, Height = 870, ShowActivated = false, ShowInTaskbar = false,
            WindowStyle = WindowStyle.None, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20_000, Top = 0
        };

        try
        {
            window.Show();
            await Idle();
            var windowMs = start.Elapsed.TotalMilliseconds;

            // Page switches: every page, NavigationCycles times; the first cycle warms up (JIT, XAML parsing).
            var pages = main.Pages.Select(p => p.Page).OfType<Frontend.Shared.Infrastructure.ObservableObject>().ToList();
            await Cycle(main, pages);
            var afterOne = Memory();
            var switches = new List<double>();

            for (var i = 1; i < NavigationCycles; i++)
            {
                switches.AddRange(await Cycle(main, pages));
            }

            var afterAll = Memory();

            // Captures into a real SQLite database, through the Measurement page as a user would.
            await Make.Pick(main.Core, "DH-001");
            main.Core.SaveCommand.Execute(null);
            await Assert.Eventually(() => main.Measurement.ToggleMeasurementCommand.CanExecute(null), "session open");
            main.ShowPage(main.Measurement);
            main.Measurement.NextFrom = "0.00";
            main.Measurement.Step = "10";
            main.Measurement.ToggleMeasurementCommand.Execute(null);
            var captureTimes = new List<double>();

            for (var i = 0; i < Captures; i++)
            {
                var one = Stopwatch.StartNew();
                main.Measurement.TriggerCommand.Execute(DateTimeOffset.Now);
                device.CompleteWith(new CaptureData(device.LastStart, device.LastStart + device.LastDuration, [100 + i % 50], "",
                    DateTimeOffset.Now, Reading: new DeviceReading(TsMs: DateTimeOffset.Now.ToUnixTimeMilliseconds(), Count0: 100 + i % 50)));
                await Assert.Eventually(() => main.Measurement.State == MeasurementState.Ready, "capture finished");
                await Idle();
                captureTimes.Add(one.Elapsed.TotalMilliseconds);
            }

            await Assert.Eventually(() => repository.Saves.Count >= Captures, "all captures saved", timeoutMs: 120_000);
            main.Measurement.ToggleMeasurementCommand.Execute(null);

            // Monitoring catching up on a 5,000-reading backlog (the most one poll reads) with the page on screen.
            main.ShowPage(main.Monitoring);
            await Idle();
            var engine = main.Monitoring.Engine;
            string[] serials = ["SN-A", "SN-B", "SN-C", "SN-D"];
            var statuses = serials.Select(s => new Frontend.Calculation.CoreInformation.GammaGuardDatabase.DetectorStatusRow(s, $"Meter {s}", "00:11", "1.0", 14, "s1", "logging", 1, now, now)).ToArray();
            engine.Apply(new Frontend.Calculation.CoreInformation.GammaGuardDatabase.MonitorSnapshot(3, 1, statuses, [Samples.At(1, now, 10, serials[0])]), now);
            var backlog = Enumerable.Range(2, 5000).Select(i => Samples.At(i, now + i * 250L, 10 + i % 7, serials[i % 4])).ToList();
            var monitorWatch = Stopwatch.StartNew();
            engine.Apply(new Frontend.Calculation.CoreInformation.GammaGuardDatabase.MonitorSnapshot(3, 5001, statuses, backlog), now + 5001 * 250L);
            await Idle();
            var monitorMs = monitorWatch.Elapsed.TotalMilliseconds;

            Console.WriteLine($"PERF window shown + idle: {windowMs:F0} ms");
            Console.WriteLine($"PERF page switch: mean {switches.Average():F1} ms, p95 {Percentile(switches, 0.95):F1} ms ({switches.Count} switches)");
            Console.WriteLine($"PERF managed memory after 1 cycle: {afterOne / 1024.0 / 1024:F1} MB, after {NavigationCycles}: {afterAll / 1024.0 / 1024:F1} MB (growth {(afterAll - afterOne) / 1024.0 / 1024:F2} MB)");
            Console.WriteLine($"PERF Monitoring 5,000-reading backlog applied and rendered: {monitorMs:F0} ms");
            Console.WriteLine($"PERF capture: first 20 mean {captureTimes.Take(20).Average():F1} ms, last 20 mean {captureTimes.TakeLast(20).Average():F1} ms");
            Console.WriteLine($"PERF save (background): first 20 mean {repository.Saves.Take(20).Average():F1} ms, last 20 mean {repository.Saves.TakeLast(20).Average():F1} ms; measured-drillholes reload: {repository.Reloads.Count} calls, last 20 mean {repository.Reloads.TakeLast(20).DefaultIfEmpty().Average():F1} ms");
        }
        finally
        {
            window.Close();
        }
    }

    private static async Task<List<double>> Cycle(MainViewModel main, IEnumerable<Frontend.Shared.Infrastructure.ObservableObject> pages)
    {
        var times = new List<double>();

        foreach (var page in pages)
        {
            var watch = Stopwatch.StartNew();
            main.ShowPage(page);
            await Idle();
            times.Add(watch.Elapsed.TotalMilliseconds);
        }

        return times;
    }

    // Layout, render and data binding have all run once the dispatcher reaches ContextIdle.
    private static Task Idle() => Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle).Task;

    private static long Memory()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        return GC.GetTotalMemory(forceFullCollection: true);
    }

    private sealed class FixedLiveCps(double value) : ILiveCpsReader
    {
        public double? ReadLatestCps() => value;
    }

    // Times the saves and measured-drillhole reloads, which run in the background after each capture.
    private sealed class TimedRepository(IMeasurementRepository inner) : IMeasurementRepository
    {
        public List<double> Saves { get; } = [];

        public List<double> Reloads { get; } = [];

        public string DatabasePath => inner.DatabasePath;

        public Task<MeasurementSession> ResolveSessionAsync(MeasurementSession candidate) => inner.ResolveSessionAsync(candidate);

        public async Task SaveAsync(MeasurementSession session, IReadOnlyList<MeasurementResult> rows, DepthProgress? progress)
        {
            var watch = Stopwatch.StartNew();
            await inner.SaveAsync(session, rows, progress);
            lock (Saves) Saves.Add(watch.Elapsed.TotalMilliseconds);
        }

        public Task<DepthProgress?> LoadProgressAsync(Guid sessionId) => inner.LoadProgressAsync(sessionId);

        public Task DeleteAsync(Guid sessionId, int captureNumber) => inner.DeleteAsync(sessionId, captureNumber);

        public Task<MeasurementSession?> FindSessionAsync(string drillholeId) => inner.FindSessionAsync(drillholeId);

        public Task<IReadOnlyList<MeasurementResult>> LoadAsync(Guid sessionId) => inner.LoadAsync(sessionId);

        public async Task<IReadOnlyList<MeasuredDrillhole>> LoadMeasuredDrillholesAsync()
        {
            var watch = Stopwatch.StartNew();
            var result = await inner.LoadMeasuredDrillholesAsync();
            lock (Reloads) Reloads.Add(watch.Elapsed.TotalMilliseconds);
            return result;
        }
    }

    private static double Percentile(List<double> values, double p)
    {
        var sorted = values.Order().ToList();
        return sorted[(int)Math.Min(sorted.Count - 1, Math.Ceiling(p * sorted.Count) - 1)];
    }
}
