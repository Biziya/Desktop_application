using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Frontend.Api.Measurements;
using Frontend.Shared.Help;

namespace Ct007.Tests.FrontendTests;

/// <summary>
/// DOCS-001: the page pictures used by the Help drawer (Frontend/Help/Images, 2x, via CT007_HELP_IMAGES) and the user
/// guide (docs/guide-images, 1x), and the check that the numbered markers in help.yaml still sit on their controls,
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
        MarkerReport.Clear();
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
        var dialogs = new FakeDialogs();
        var backend = new DocsBackend();
        var main = new MainViewModel(new MemoryRepository(folder.Path("m.db")),
            new ApiSettings("http://stub.test", TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(5)),
            Make.Devices(folder.Path("gg.sqlite")), backend,
            bluetoothDevices: () => [new KnownBluetoothDevice("CT007-M-0001", 0x0011223344A1, Connected: true, Paired: true)],
            captureService: device, dialogs: dialogs, helpSettings: HelpSettings.Load(AppContext.BaseDirectory),
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
            await SnapPage(window, output, "core-information", "Core Information");

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
            await SnapPage(window, output, "measurement", "Measurement");

            // The Help drawer over the Measurement page (no dim there), at the top of the Measurement help.
            main.ToggleHelp();
            await Task.Delay(400);
            await Snap(window, output, "help-drawer");
            main.Help.Close();
            await Task.Delay(300);

            main.Measurement.ToggleMeasurementCommand.Execute(null);   // Stop, so the other pages are not locked
            main.Toasts.Items.Clear();

            main.ShowPage(main.Monitoring);
            await Task.Delay(1500);
            await Task.Delay(300);
            main.Toasts.Items.Clear();
            await SnapPage(window, output, "monitoring", "Monitoring");

            // Upload Data: a CSV exported from Measurement, imported and ready to upload.
            var session = new Frontend.Pages.Measurement.MeasurementSession(Guid.NewGuid(), DateTimeOffset.Now.AddHours(-1), "DH-001",
                "North Ridge", "2024", "DOMAIN\\operator", "Core box 3");
            var csv = folder.Path("DH-001.csv");
            File.WriteAllText(csv, Frontend.Pages.Measurement.CsvExportService.Serialize(
                [.. new[] { 96.5, 104.2, 121.8, 162.4 }.Select((cps, i) => Make.Row(session.Id, i + 1, 12_000 + 250 * i,
                    reading: new DeviceReading(null, cps, null, null, null, null)))], session, "DOMAIN\\operator"));
            dialogs.CsvFile = csv;
            main.ShowPage(main.UploadData);
            main.UploadData.ImportCommand.Execute(null);
            await Assert.Eventually(() => main.UploadData.HasImport, "CSV imported");
            await Task.Delay(300);
            main.Toasts.Items.Clear();
            await SnapPage(window, output, "upload-data", "Upload Data");

            main.ShowPage(main.DataViewer);
            await main.DataViewer.RefreshAsync();
            main.DataViewer.PickedDrillhole = main.DataViewer.DrillholeOptions[0];
            await Assert.Eventually(() => main.DataViewer.Results.Count > 0, "uploaded rows");
            main.Toasts.Items.Clear();
            await Task.Delay(300);
            main.Toasts.Items.Clear();
            await SnapPage(window, output, "data-viewer", "Data Viewer");
        }
        finally
        {
            window.Close();
        }

        // help.yaml markers must sit on the controls they point at (positions measured above).
        var moved = MarkerReport.Where(line => line.StartsWith("MOVED", StringComparison.Ordinal)).ToList();
        var report = Path.Combine(Path.GetTempPath(), "help-markers.txt");
        File.WriteAllLines(report, MarkerReport);
        Assert.True(moved.Count == 0, $"help.yaml markers match the controls (positions measured now: {report}):\n{string.Join("\n", moved)}");
    }

    // Where each numbered marker of a page's help points: marker number and the control (id:, name:, text:, button:,
    // row: first row of the named table, type: first element of that type).
    private static readonly Dictionary<string, (int N, string Target)[]> MarkerTargets = new()
    {
        ["Core Information"] =
        [
            (1, "id:DrillholeModeSwitch"), (2, "name:Drillhole"), (3, "id:ProjectValue"), (4, "name:Operator"),
            (5, "id:SaveAndContinueButton"), (6, "text:Device Status"), (7, "button:Change Database")
        ],
        ["Measurement"] =
        [
            (1, "button:Stop"), (2, "name:Starting depth in metres"), (3, "name:Depth step in centimetres"),
            (4, "name:Capture countdown. Click to change the capture duration"), (5, "name:CPS alarm threshold"), (6, "id:LiveBox"),
            (7, "id:ThresholdLineToggle"), (8, "button:Export CSV"), (9, "button:New Measurement"), (10, "name:Delete measurement")
        ],
        ["Monitoring"] =
        [
            (1, "text:State"), (2, "button:Take sample"), (3, "row:Detectors"), (4, "name:Show recent readings"), (5, "text:Response")
        ],
        ["Upload Data"] = [(1, "name:Import CSV"), (2, "text:Uploading User"), (3, "name:Upload Data"), (4, "name:Delete measurement")],
        ["Data Viewer"] = [(1, "name:Drillhole"), (2, "name:Refresh"), (3, "name:Project"), (4, "type:DataGrid")]
    };

    private static readonly List<string> MarkerReport = [];

    // A page picture: 1x for the guide, 2x for Help (CT007_HELP_IMAGES), and its markers checked against help.yaml.
    private static async Task SnapPage(Window window, string output, string name, string page)
    {
        await Snap(window, output, name);

        if (Environment.GetEnvironmentVariable("CT007_HELP_IMAGES") is { Length: > 0 } help)
        {
            Directory.CreateDirectory(help);
            await Snap(window, help, name, scale: 2);
        }

        var content = (FrameworkElement)window.Content;
        var yaml = HelpSettings.Load(AppContext.BaseDirectory).For(page)!;
        MarkerReport.Add($"{page}:");
        MarkerReport.Add("    markers:");

        foreach (var (n, target) in MarkerTargets[page])
        {
            var element = Find(window, target) ?? throw new InvalidOperationException($"{page}: marker {n} target '{target}' not found");
            var box = element.TransformToAncestor(content).TransformBounds(new Rect(element.RenderSize));
            // Left edge, vertically centred (near the top for tall elements such as tables).
            var x = Math.Round((box.Left + Math.Min(4, box.Width / 2)) / content.ActualWidth, 3);
            var y = Math.Round((box.Height > 80 ? box.Top + 22 : box.Top + box.Height / 2) / content.ActualHeight, 3);
            MarkerReport.Add($"      - {{n: {n}, x: {x:0.000}, y: {y:0.000}}}   # {target}");

            var marker = yaml.Markers.FirstOrDefault(m => m.N == n);

            if (marker is null || Math.Abs(marker.X - x) > 0.01 || Math.Abs(marker.Y - y) > 0.01)
            {
                MarkerReport.Add($"MOVED {page} marker {n} ({target}): help.yaml has {(marker is null ? "none" : $"{marker.X}, {marker.Y}")}, control at {x}, {y}");
            }
        }
    }

    private static FrameworkElement? Find(Window window, string target)
    {
        var (kind, value) = (target[..target.IndexOf(':')], target[(target.IndexOf(':') + 1)..]);
        // On the page, not in the sidebar (whose entries share the page names).
        var visible = Descendants(window).OfType<FrameworkElement>()
            .Where(e => e.IsVisible && e.ActualWidth > 0 && e.TransformToAncestor(window).Transform(default).X >= 230).ToList();

        return kind switch
        {
            "id" => visible.FirstOrDefault(e => AutomationProperties.GetAutomationId(e) == value),
            "name" => visible.FirstOrDefault(e => AutomationProperties.GetName(e) == value),
            "text" => visible.OfType<TextBlock>().FirstOrDefault(t => t.Text == value),
            "button" => visible.OfType<System.Windows.Controls.Primitives.ButtonBase>().FirstOrDefault(b =>
                (b.Content as string)?.Contains(value, StringComparison.Ordinal) == true
                || Descendants(b).OfType<TextBlock>().Any(t => t.Text.Contains(value, StringComparison.Ordinal))),
            "row" => visible.OfType<DataGrid>().Where(g => AutomationProperties.GetName(g) == value)
                .SelectMany(g => Descendants(g).OfType<DataGridRow>()).FirstOrDefault(r => r.IsVisible),
            "type" => visible.FirstOrDefault(e => e.GetType().Name == value),
            _ => throw new ArgumentException($"unknown marker target '{target}'")
        };
    }

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

    private static async Task Snap(Window window, string folder, string name, double width = Width, double height = Height, int scale = 1)
    {
        await Task.Delay(250);
        window.UpdateLayout();
        var visual = window.Content as FrameworkElement ?? (FrameworkElement)window;
        var size = new Size(visual.ActualWidth > 0 ? visual.ActualWidth : width, visual.ActualHeight > 0 ? visual.ActualHeight : height);
        var bitmap = new RenderTargetBitmap((int)size.Width * scale, (int)size.Height * scale, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
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
