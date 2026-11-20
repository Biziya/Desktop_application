using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Ct007.Tests.EndToEnd;

// GUIDE-001: the screenshots in docs/Gamma-Core-Log-User-Guide.docx, taken from the built app with fake data (DesktopBench).
// Opt-in twice: --include=Guide and the E2E account variable; CT007_GUIDE_IMAGES names the output folder.
public static class GuideScreenshots
{
    [Test("GUIDE-001", "User guide screenshots: Device Status, Measurement setup, measuring, table", Category = "Guide", TimeoutSeconds = 300)]
    public static async Task Take()
    {
        var output = Environment.GetEnvironmentVariable("CT007_GUIDE_IMAGES") ?? "";
        if (output.Length == 0)
        {
            Assert.Skip("Set CT007_GUIDE_IMAGES to the folder for the user guide images.");
        }

        Directory.CreateDirectory(output);
        await using var bench = await DesktopBench.CreateAsync();
        var settings = Path.Combine(bench.AppDirectory, "Calculation", "Core-Information", "device-settings.json");
        var working = File.ReadAllText(settings);

        // 1. A problem: the selected Gamma Guard database was moved (a path that reads like a real install but does not
        //    exist), and the configured fallback is that same path, so nothing usable is found.
        var missing = @"C:\GammaGuard\CT008 1.9.0\Database\ct008-log.sqlite";
        File.WriteAllText(settings, JsonSerializer.Serialize(new { gammaGuardDatabasePath = missing, checkIntervalSeconds = 1 }));
        var selection = GammaGuardSelectionFile();
        Directory.CreateDirectory(Path.GetDirectoryName(selection)!);
        File.WriteAllText(selection, JsonSerializer.Serialize(new { gammaGuardDatabasePath = missing }));

        await using (var app = await bench.LaunchAsync())
        {
            Maximize(app);
            await WaitText(app, "moved or deleted");
            await Task.Delay(ToastsGone);
            Save(app, output, "device-status-problem.png", 18, await app.Find("Gamma Guard SQLite Database"), await app.Find("Gamma Guard database status"),
                await app.Find("Gamma Guard SQLite database path"), await app.FindButtonContaining("Select Database"));
        }

        File.Delete(selection);
        File.WriteAllText(settings, working);

        // 2. Everything else, with the simulated Gamma Guard available.
        await using (var app = await bench.LaunchAsync())
        {
            Maximize(app);
            await WaitText(app, "Online");      // the sidebar status; the drillhole list loads once the server answers
            await Task.Delay(3000);
            await Pick(app, "DH-001");
            await app.SaveAndContinue();
            await Task.Delay(ToastsGone);
            DesktopApp.SetText(await app.Find("Starting depth in metres"), "10.00");
            DesktopApp.SetText(await app.Find("Depth step in centimetres"), "25");
            await Task.Delay(800);
            // The chart has no automation peer: the crop runs from the inputs to the table's right edge (the card's width).
            var right = TableRight(app);
            Save(app, output, "setting-up.png", 24, [await app.Find("Starting depth in metres"), await app.Find(Countdown), await app.Find("Start measurement")], null, right);
            Save(app, output, "start-button.png", 14, await app.Find("Start measurement"));

            DesktopApp.Invoke(await app.Find("Start measurement"));
            await app.Find("Stop measurement");

            for (var row = 1; row <= 3; row++)
            {
                await app.Capture(row);
            }

            // Waiting for the next capture: Stop and the blue "right-click anywhere" banner under the inputs.
            await Task.Delay(ToastsGone);
            var hint = await DesktopApp.WaitAsync(() => app.Window.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text))
                .Cast<AutomationElement>().FirstOrDefault(e => e.Current.Name.StartsWith("Measurement in progress", StringComparison.Ordinal)), "capture hint");
            Save(app, output, "waiting.png", 24, [await app.Find("Starting depth in metres"), await app.Find(Countdown), hint], null, right);

            // Mid-countdown: Stop, the running countdown and the bars so far.
            RightClick(app);
            await Task.Delay(2200);
            Save(app, output, "measuring.png", 24, [await app.Find("Starting depth in metres"), await app.Find(Countdown), await app.Find("Stop measurement")], null, right);
            await app.WaitRows(4);
            await Task.Delay(500);

            var grid = app.Window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.DataGrid))
                       ?? throw new AssertionException("results table not found");
            var rows = grid.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.DataItem)).Cast<AutomationElement>().ToArray();
            Save(app, output, "table-rows.png", 4, [grid.FindFirst(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Header)) ?? grid, .. rows],
                limitTo: grid.Current.BoundingRectangle);
            DesktopApp.Invoke(await app.Find("Stop measurement"));
        }
    }

    // Opens the Drillhole list and selects the entry through UI Automation: no keystrokes, so focus does not matter.
    private static async Task Pick(DesktopApp app, string holeId)
    {
        var box = await app.Find("Drillhole", ControlType.ComboBox);
        ((ExpandCollapsePattern)box.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
        var item = await DesktopApp.WaitAsync(() => box.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem))
            .Cast<AutomationElement>().FirstOrDefault(i => i.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, holeId)) is not null), holeId, 15_000);
        ((SelectionItemPattern)item.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        ((ExpandCollapsePattern)box.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Collapse();
        var project = await app.Find("Project", ControlType.Edit);
        await DesktopApp.WaitAsync(() => DesktopApp.Text(project) == "North Ridge" ? project : null, "Project filled");
    }

    private static string GammaGuardSelectionFile() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CT007_Measurement_Studio", "Config", "gamma-guard-database.json");

    // Toasts (bottom right) close by themselves; wait them out so they do not cover a screenshot.
    private const int ToastsGone = 7000;

    private static void Maximize(DesktopApp app)
    {
        ShowWindow(new IntPtr(app.Window.Current.NativeWindowHandle), 3);
        SetForegroundWindow(new IntPtr(app.Window.Current.NativeWindowHandle));
    }

    // A status detail line: a TextBlock without an automation name, so its name is its text.
    private static Task WaitText(DesktopApp app, string text) => DesktopApp.WaitAsync(() =>
        app.Window.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text)).Cast<AutomationElement>()
            .FirstOrDefault(e => e.Current.Name.Contains(text, StringComparison.Ordinal)), text, 20_000);

    private static void RightClick(DesktopApp app)
    {
        var grid = app.Window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.DataGrid))!;
        var bounds = grid.Current.BoundingRectangle;
        SetForegroundWindow(new IntPtr(app.Window.Current.NativeWindowHandle));
        SetCursorPos((int)(bounds.Left + bounds.Width / 2), (int)(bounds.Top + bounds.Height / 2));
        mouse_event(0x0008, 0, 0, 0, UIntPtr.Zero);
        mouse_event(0x0010, 0, 0, 0, UIntPtr.Zero);
    }

    /// <summary>PrintWindow of the main window, cropped to the union of the elements plus a margin (screen pixels).</summary>
    private const string Countdown = "Capture countdown. Click to change the capture duration";

    private static double TableRight(DesktopApp app) =>
        (app.Window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.DataGrid))
         ?? throw new AssertionException("results table not found")).Current.BoundingRectangle.Right;

    private static void Save(DesktopApp app, string folder, string name, int margin, params AutomationElement[] elements) =>
        Save(app, folder, name, margin, elements, null);

    private static void Save(DesktopApp app, string folder, string name, int margin, AutomationElement[] elements, Rect? limitTo, double? right = null)
    {
        var handle = new IntPtr(app.Window.Current.NativeWindowHandle);
        GetWindowRect(handle, out var window);
        var width = window.Right - window.Left;
        var height = window.Bottom - window.Top;

        var union = Rect.Empty;
        foreach (var element in elements)
        {
            union.Union(element.Current.BoundingRectangle);
        }

        union.Inflate(margin, margin);
        if (right is { } edge)
        {
            union.Width = Math.Max(1, edge - union.Left);
        }

        if (limitTo is { } limit)
        {
            union.Intersect(limit);
        }

        var screen = IntPtr.Zero;
        var memory = IntPtr.Zero;
        var bitmap = IntPtr.Zero;

        try
        {
            screen = GetDC(IntPtr.Zero);
            memory = CreateCompatibleDC(screen);
            bitmap = CreateCompatibleBitmap(screen, width, height);
            var old = SelectObject(memory, bitmap);
            PrintWindow(handle, memory, 2);
            SelectObject(memory, old);

            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            var x = Math.Max(0, (int)(union.Left - window.Left));
            var y = Math.Max(0, (int)(union.Top - window.Top));
            var crop = new Int32Rect(x, y, Math.Min(width - x, (int)union.Width), Math.Min(height - y, (int)union.Height));
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(new CroppedBitmap(source, crop)));
            using var file = File.Create(Path.Combine(folder, name));
            encoder.Save(file);
        }
        finally
        {
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (memory != IntPtr.Zero) DeleteDC(memory);
            if (screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screen);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out RECT rect);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extraInfo);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr item);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr item);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
}
