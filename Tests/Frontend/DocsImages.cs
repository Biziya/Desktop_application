using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Frontend.Api.Measurements;
using Frontend.Shared.Help;

namespace Ct007.Tests.FrontendTests;

/// <summary>
/// DOCS-001: the page pictures used by the Help window (Frontend/Help/Images) and the user guide (docs/guide-images),
/// rendered off-screen from the real views with fake data. Opt-in:
/// <code>$env:CT007_DOCS_IMAGES="docs\guide-images"; dotnet run --project Tests -- DOCS --include=Docs</code>
/// Run it with TMP pointing at a neutral folder (for example C:\Users\Public\GammaCoreLog) so the database paths in the
/// pictures do not show a personal profile folder. Unlike GUIDE-001 it needs no dedicated account and never moves the mouse or takes focus.
/// </summary>
public static class DocsImages
{
    private const double Width = 1320;
    private const double Height = 870;

    [Test("DOCS-001", "Page pictures for Help and the user guide (off-screen, fake data)", Category = "Docs", TimeoutSeconds = 120)]
    public static async Task Render()
    {
        if (Environment.GetEnvironmentVariable("CT007_DOCS_IMAGES") is not { Length: > 0 } output)
        {
            Assert.Skip("Set CT007_DOCS_IMAGES to the output folder.");
            return;
        }

        Directory.CreateDirectory(output);
        Wpf.EnsureResources();
        using var folder = new TestFolder();

        // A recording detector, so Monitoring and the Live value show readings.
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        using (var gg = new FakeGammaGuard(folder.Path("gg.sqlite")))
        {
            gg.Detector("SN-1001", "CT007-M-0001", "00:11:22:AA:BB:01", now);

            for (var i = 40; i >= 0; i--)
            {
                gg.Sample("SN-1001", now - i * 1000L, count0: 7200 + 600 * Math.Sin(i / 4.0), seq: 1000 - i);
            }
        }

        var device = new ControlledMeasurements();
        var backend = new DocsBackend();
        var main = new MainViewModel(new MemoryRepository(folder.Path("m.db")),
            new ApiSettings("http://stub.test", TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(5)),
            Make.Devices(folder.Path("gg.sqlite")), backend,
            bluetoothDevices: () => [new KnownBluetoothDevice("CT007-M-0001", 0x0011223344A1, Connected: true, Paired: true)],
            captureService: device, dialogs: new FakeDialogs(), helpSettings: HelpSettings.Load(AppContext.BaseDirectory),
            liveCps: new FixedLiveCps(118.42));
        var window = new Frontend.MainWindow(main)
        {
            Width = Width, Height = Height, ShowActivated = false, ShowInTaskbar = false,
            WindowStyle = WindowStyle.None, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20_000, Top = 0
        };

        try
        {
            window.Show();
            await main.Core.LoadDrillholesAsync();
            await main.Devices.RefreshBluetoothDevicesAsync();
            main.Devices.SelectBluetoothDevice(main.Devices.BluetoothDevices[0]);

            // Core Information: a picked drillhole and the device status.
            await Make.Pick(main.Core, "DH-001");
            main.Core.Operator = "operator";   // never the account that renders the pictures
            main.Core.Notes = "Core box 3";
            await Task.Delay(300);
            main.Toasts.Items.Clear();
            await Snap(window, output, "core-information");

            // New mode: Drillhole ID typed, Project picked, "+ Add new campaign" chosen but left empty, so the Campaign
            // box shows what is missing and Save & Continue stays disabled.
            main.Core.IsNewModeSelected = true;
            main.Core.NewDrillholeId = "DH-NEW-01";
            main.Core.NewProject.Picked = main.Core.NewProject.Choices.First(c => c.Name == "North Ridge");
            main.Core.NewCampaign.Picked = main.Core.NewCampaign.Choices[^1];
            main.Core.MarkTouched("campaign");
            await Task.Delay(300);
            main.Toasts.Items.Clear();
            await Snap(window, output, "new-drillhole");
            main.Core.IsExistingModeSelected = true;   // FakeDialogs: Discard
            await Make.Pick(main.Core, "DH-001");

            // Measurement: armed, a few intervals captured, threshold line at 150 CPS, Live value and eye icon above the graph.
            main.Core.SaveCommand.Execute(null);
            await Assert.Eventually(() => main.Measurement.ToggleMeasurementCommand.CanExecute(null), "session open");
            main.Measurement.NextFrom = "12.00";
            main.Measurement.Step = "25";
            main.Measurement.Threshold = "150";
            main.Measurement.ToggleMeasurementCommand.Execute(null);

            foreach (var cps in new[] { 96.5, 104.2, 121.8, 162.4, 171.3, 139.9, 118.6, 101.7 })
            {
                main.Measurement.TriggerCommand.Execute(DateTimeOffset.Now);
                device.CompleteWith(new CaptureData(device.LastStart, device.LastStart + device.LastDuration, [cps], "", DateTimeOffset.Now,
                    Reading: new DeviceReading(TsMs: DateTimeOffset.Now.ToUnixTimeMilliseconds(), Count0: cps)));
                await Assert.Eventually(() => main.Measurement.State == MeasurementState.Ready, "capture finished");
            }

            await Assert.Eventually(() => main.Measurement.LiveValue != MeasurementViewModel.NoLiveCps, "live CPS shown");
            main.Toasts.Items.Clear();
            await Task.Delay(300);
            main.Toasts.Items.Clear();
            await Snap(window, output, "measurement");

            main.Measurement.ToggleMeasurementCommand.Execute(null);   // Stop, so the other pages are not locked
            main.Toasts.Items.Clear();

            main.ShowPage(main.Monitoring);
            await Task.Delay(1500);
            await Task.Delay(300);
            main.Toasts.Items.Clear();
            await Snap(window, output, "monitoring");

            main.ShowPage(main.UploadData);
            await Task.Delay(300);
            main.Toasts.Items.Clear();
            await Snap(window, output, "upload-data");

            main.ShowPage(main.DataViewer);
            await main.DataViewer.RefreshAsync();
            main.DataViewer.PickedDrillhole = main.DataViewer.DrillholeOptions[0];
            await Assert.Eventually(() => main.DataViewer.Results.Count > 0, "uploaded rows");
            main.Toasts.Items.Clear();
            await Task.Delay(300);
            main.Toasts.Items.Clear();
            await Snap(window, output, "data-viewer");
        }
        finally
        {
            window.Close();
        }

        // The Help window on its own (never activated, off-screen), at the Measurement section.
        var panel = new HelpPanel(HelpSettings.Load(AppContext.BaseDirectory));
        var help = new HelpWindow(panel, (_, _) => { }, () => false, (_, _) => { })
        {
            ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20_000, Top = 0
        };

        try
        {
            help.Show();
            panel.Show("Measurement");
            help.ScrollToCurrentSection();
            await Snap(help, output, "help-window", help.Width, help.Height);
        }
        finally
        {
            help.AllowClose = true;
            help.Close();
        }
    }

    private static async Task Snap(Window window, string folder, string name, double width = Width, double height = Height)
    {
        await Task.Delay(250);
        window.UpdateLayout();
        var visual = window.Content as FrameworkElement ?? (FrameworkElement)window;
        var size = new Size(visual.ActualWidth > 0 ? visual.ActualWidth : width, visual.ActualHeight > 0 ? visual.ActualHeight : height);
        var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();

        using (var context = background.RenderOpen())
        {
            context.DrawRectangle(window.Background ?? Brushes.White, null, new Rect(size));
        }

        bitmap.Render(background);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        await using var file = File.Create(Path.Combine(folder, name + ".png"));
        encoder.Save(file);
    }

    private sealed class FixedLiveCps(double value) : ILiveCpsReader
    {
        public double? ReadLatestCps() => value;
    }

    /// <summary>Backend for the pictures: online, three drillholes, one uploaded drillhole with a few records.</summary>
    private sealed class DocsBackend : HttpMessageHandler
    {
        private const string Uploaded = """
            [{"holeId":"DH-001","projectCode":"North Ridge","campaign":"2024","uploadCount":1,"recordCount":4,
              "lastUploadedAt":"2026-11-18T16:30:00+08:00","operator":"DOMAIN\\operator","notes":"Core box 3"}]
            """;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var json = path == DrillholesClient.Path ? StubBackend.ThreeDrillholes
                : path == UploadedMeasurementsClient.DrillholesPath ? Uploaded
                : path == UploadedMeasurementsClient.RecordsPath ? Records()
                : "{}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }

        private static string Records()
        {
            var rows = new[] { 96.5, 104.2, 121.8, 162.4 }.Select((cps, i) => $$"""
                {"sessionId":"8c4e0000-0000-0000-0000-000000000001","captureNumber":{{i + 1}},
                 "startTime":"2026-11-18T15:0{{i}}:00+08:00","endTime":"2026-11-18T15:0{{i}}:05+08:00","sampleCount":5,
                 "depthFrom":{{(12 + i * 0.25).ToString("0.000", CultureInfo.InvariantCulture)}},
                 "depthTo":{{(12.25 + i * 0.25).ToString("0.000", CultureInfo.InvariantCulture)}},
                 "tsMs":{{1_795_000_000_000L + i * 60_000}},"count0":{{cps.ToString(CultureInfo.InvariantCulture)}},
                 "count0Raw":{{cps.ToString(CultureInfo.InvariantCulture)}},"hdm":1.1,"hdmRaw":1.2,"doseRate":0.12,
                 "uploadId":8,"operator":"DOMAIN\\operator","uploadedAt":"2026-11-18T16:30:00+08:00"}
                """);
            return "[" + string.Join(",", rows) + "]";
        }
    }
}
