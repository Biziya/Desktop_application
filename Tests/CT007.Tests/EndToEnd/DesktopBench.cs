using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Automation;
using BackendDrillhole = Backend.Models.CoreInformation.Drillhole.DrillholeResponse;

namespace Ct007.Tests.EndToEnd;

/// <summary>
/// Environment for one desktop E2E test: a copy of the built Frontend.exe with test settings, the in-process backend on
/// a fixed local port (fake drillholes, never SQL Server), and a simulated Gamma Guard database written at 1 sample/s.
/// The app always stores its data in %LocalAppData%\CT007_Measurement_Studio, so the tests run only on a dedicated
/// Windows test account (CT007_E2E_DEDICATED_ACCOUNT=1); an existing data folder is moved aside and restored afterwards.
/// </summary>
public sealed class DesktopBench : IAsyncDisposable
{
    public const string Serial = "SN-E2E-1";

    private static readonly string DataFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CT007_Measurement_Studio");

    private readonly CancellationTokenSource stopWriter = new();
    private readonly Task writer;
    private readonly string? backup;

    private DesktopBench(TestFolder folder, string appDirectory, int port, string gammaGuardPath, string? backup)
    {
        Folder = folder;
        AppDirectory = appDirectory;
        Port = port;
        GammaGuardPath = gammaGuardPath;
        this.backup = backup;
        writer = Task.Run(WriteSamplesAsync);
    }

    public TestFolder Folder { get; }

    public string AppDirectory { get; }

    public int Port { get; }

    public string GammaGuardPath { get; }

    public TestBackend? Backend { get; private set; }

    public static string Database => Path.Combine(DataFolder, "measurements.db");

    public static IReadOnlyList<BackendDrillhole> Drillholes { get; } =
        [new("DH-001", "North Ridge", "2024"), new("DH-002", "North Ridge", "2025"), new("DH-003", "South Pit", "2025")];

    /// <summary>Skips the test unless the run is explicitly marked as a dedicated test account with a built app.</summary>
    public static async Task<DesktopBench> CreateAsync(bool startBackend = true)
    {
        if (Environment.GetEnvironmentVariable("CT007_E2E_DEDICATED_ACCOUNT") != "1")
        {
            Assert.Skip("Desktop E2E needs a dedicated Windows test account: set CT007_E2E_DEDICATED_ACCOUNT=1 (it uses %LocalAppData%).");
        }

        var source = Environment.GetEnvironmentVariable("CT007_FRONTEND_DIR") ??
                     Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Frontend", "bin", "Debug", "net10.0-windows10.0.19041.0"));

        if (!File.Exists(Path.Combine(source, "Frontend.exe")))
        {
            Assert.Skip($"Frontend.exe not found in {source}: build Frontend or set CT007_FRONTEND_DIR.");
        }

        if (Directory.Exists(Path.Combine(Path.GetDirectoryName(DataFolder)!, "CT007 Measurement Studio")))
        {
            Assert.Skip("A legacy 'CT007 Measurement Studio' data folder exists; the app would move it. Use a clean test account.");
        }

        SetProcessDpiAwarenessContext(new IntPtr(-4)); // per-monitor v2: UIA rectangles and cursor positions agree
        var folder = new TestFolder();
        string? backup = null;

        if (Directory.Exists(DataFolder))
        {
            // Outside the test folder on purpose: it is never deleted automatically.
            backup = Path.Combine(Path.GetTempPath(), $"ct007-e2e-backup-{Guid.NewGuid():N}");
            Directory.Move(DataFolder, backup);
        }

        var appDirectory = folder.Path("app");
        Copy(source, appDirectory);
        var port = TestBackend.FreePort();
        var gammaGuard = folder.Path("gammaguard.sqlite");

        using (var gg = new FakeGammaGuard(gammaGuard, wal: true))
        {
            gg.Detector(Serial, "CT007-M-E2E", "00:11:22:AA:BB:EE", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            gg.Sample(Serial, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }

        // The copied Debug build reads .env.local of the "source checkout" above it: the test folder, marked as one by
        // a placeholder Frontend.csproj (ApiSettings.LocalFile), so the repository's own .env.local is never used.
        File.WriteAllText(folder.Path("Frontend.csproj"), "<Project />");
        File.WriteAllText(folder.Path(".env.local"),
            $"APP_ENV=local\nAPI_BASE_URL=http://127.0.0.1:{port}\nAPI_TIMEOUT_SECONDS=5\nAPI_RETRY_INTERVAL_SECONDS=1\n");
        File.WriteAllText(Path.Combine(appDirectory, "Calculation", "Core-Information", "device-settings.json"),
            JsonSerializer.Serialize(new { gammaGuardDatabasePath = gammaGuard, checkIntervalSeconds = 1 }));

        var bench = new DesktopBench(folder, appDirectory, port, gammaGuard, backup);

        if (startBackend)
        {
            await bench.StartBackendAsync();
        }

        return bench;
    }

    public async Task StartBackendAsync() =>
        Backend ??= await TestBackend.StartAsync(new FakeDrillholeRepository { Rows = Drillholes }, port: Port);

    public async Task StopBackendAsync()
    {
        if (Backend is { } running)
        {
            Backend = null;
            await running.DisposeAsync();
        }
    }

    public async Task<DesktopApp> LaunchAsync()
    {
        var process = Process.Start(new ProcessStartInfo(Path.Combine(AppDirectory, "Frontend.exe")) { WorkingDirectory = AppDirectory, UseShellExecute = false })!;
        var window = await DesktopApp.WaitAsync(() => AutomationElement.RootElement.FindFirst(TreeScope.Children,
            new PropertyCondition(AutomationElement.ProcessIdProperty, process.Id)), "main window", 30_000);
        return new DesktopApp(process, window);
    }

    // Gamma Guard stand-in: one sample per second for the detector, ts_ms = this laptop's UTC clock.
    private async Task WriteSamplesAsync()
    {
        using var connection = new SqliteConnection($"Data Source={GammaGuardPath};Pooling=False");
        connection.Open();
        var seq = 0;

        while (!stopWriter.IsCancellationRequested)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "INSERT INTO samples (serial, session_id, seq, ts_ms, count0, dose_rate) VALUES ($s, 41, $q, $t, 12.5, 0.2)";
                command.Parameters.AddWithValue("$s", Serial);
                command.Parameters.AddWithValue("$q", ++seq);
                command.Parameters.AddWithValue("$t", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                command.ExecuteNonQuery();
            }

            try
            {
                await Task.Delay(1000, stopWriter.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        stopWriter.Cancel();
        await writer;
        await StopBackendAsync();
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(DataFolder))
        {
            Directory.Delete(DataFolder, recursive: true);
        }

        if (backup is not null)
        {
            Directory.Move(backup, DataFolder);
        }

        Folder.Dispose();
    }

    private static void Copy(string source, string target)
    {
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(directory.Replace(source, target, StringComparison.OrdinalIgnoreCase));
        }

        Directory.CreateDirectory(target);

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, file.Replace(source, target, StringComparison.OrdinalIgnoreCase), overwrite: true);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);
}

/// <summary>The running app as seen through UI Automation (names from AutomationProperties.Name / button text).</summary>
public sealed class DesktopApp(Process process, AutomationElement window) : IAsyncDisposable
{
    public AutomationElement Window { get; } = window;

    public static async Task<AutomationElement> WaitAsync(Func<AutomationElement?> find, string what, int timeoutMs = 10_000)
    {
        var watch = Stopwatch.StartNew();

        while (true)
        {
            if (find() is { } found)
            {
                return found;
            }

            if (watch.ElapsedMilliseconds > timeoutMs)
            {
                throw new AssertionException($"UI element not found within {timeoutMs} ms: {what}");
            }

            await Task.Delay(100);
        }
    }

    public Task<AutomationElement> Find(string name, ControlType? type = null, int timeoutMs = 10_000) => WaitAsync(() =>
        Window.FindFirst(TreeScope.Descendants, type is null
            ? new PropertyCondition(AutomationElement.NameProperty, name)
            : new AndCondition(new PropertyCondition(AutomationElement.NameProperty, name), new PropertyCondition(AutomationElement.ControlTypeProperty, type))),
        name, timeoutMs);

    public Task<AutomationElement> FindButtonContaining(string text) => WaitAsync(() =>
        Window.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)).Cast<AutomationElement>()
            .FirstOrDefault(b => b.Current.Name.Contains(text, StringComparison.Ordinal)), text);

    public static void SetText(AutomationElement element, string text) => ((ValuePattern)element.GetCurrentPattern(ValuePattern.Pattern)).SetValue(text);

    public static string Text(AutomationElement element) => ((ValuePattern)element.GetCurrentPattern(ValuePattern.Pattern)).Current.Value;

    public static void Invoke(AutomationElement element) => ((InvokePattern)element.GetCurrentPattern(InvokePattern.Pattern)).Invoke();

    public int RowCount() => ((GridPattern)Grid().GetCurrentPattern(GridPattern.Pattern)).Current.RowCount;

    private AutomationElement Grid() => Window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.DataGrid))
        ?? throw new AssertionException("results table not found");

    public async Task WaitRows(int count, int timeoutMs = 15_000) =>
        await WaitAsync(() => ((GridPattern)Grid().GetCurrentPattern(GridPattern.Pattern)).Current.RowCount == count ? Grid() : null,
            $"{count} result rows", timeoutMs);

    /// <summary>Types into the Drillhole box and presses Enter (picks the closest match), as an operator does.</summary>
    public async Task PickDrillhole(string text, string expectedProject)
    {
        var box = await Find("Drillhole", ControlType.ComboBox);
        SetText(box, text);
        await Task.Delay(500); // the page filters 150 ms after the last keystroke
        box.SetFocus();
        Key(0x0D);
        var project = await Find("Project");
        await WaitAsync(() => Text(project) == expectedProject ? project : null, $"Project '{expectedProject}' filled");
    }

    public async Task SaveAndContinue() => Invoke(await FindButtonContaining("Save & Continue"));

    public async Task Start(string startDepth, string step)
    {
        SetText(await Find("Starting depth in metres"), startDepth);
        SetText(await Find("Depth step in centimetres"), step);
        Invoke(await Find("Start measurement"));
        await Find("Stop measurement");
    }

    /// <summary>A real right mouse click on the results table (the capture trigger), then waits for the new row.</summary>
    public async Task Capture(int expectedRows)
    {
        var grid = Grid();
        var bounds = grid.Current.BoundingRectangle;
        SetForegroundWindow(new IntPtr(Window.Current.NativeWindowHandle));
        await Task.Delay(200);
        SetCursorPos((int)(bounds.Left + bounds.Width / 2), (int)(bounds.Top + bounds.Height / 2));
        mouse_event(0x0008, 0, 0, 0, UIntPtr.Zero); // right down
        mouse_event(0x0010, 0, 0, 0, UIntPtr.Zero); // right up
        await WaitRows(expectedRows);
        await Task.Delay(300); // processing hold and save
    }

    public async Task<AutomationElement> Dialog(string title) => await WaitAsync(() =>
        Window.FindFirst(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window), new PropertyCondition(AutomationElement.NameProperty, title)))
        ?? AutomationElement.RootElement.FindFirst(TreeScope.Children, new AndCondition(
            new PropertyCondition(AutomationElement.ProcessIdProperty, process.Id), new PropertyCondition(AutomationElement.NameProperty, title))), title);

    public static AutomationElement Button(AutomationElement dialog, Func<string, bool> name) =>
        dialog.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)).Cast<AutomationElement>()
            .FirstOrDefault(b => name(b.Current.Name)) ?? throw new AssertionException("dialog button not found");

    public async ValueTask DisposeAsync()
    {
        if (!process.HasExited)
        {
            try
            {
                ((WindowPattern)Window.GetCurrentPattern(WindowPattern.Pattern)).Close();
            }
            catch (ElementNotAvailableException)
            {
            }

            if (!await Task.Run(() => process.WaitForExit(15_000)))
            {
                process.Kill();
            }
        }

        process.Dispose();
    }

    private static void Key(byte virtualKey)
    {
        keybd_event(virtualKey, 0, 0, UIntPtr.Zero);
        keybd_event(virtualKey, 0, 0x0002, UIntPtr.Zero);
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extraInfo);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte virtualKey, byte scan, uint flags, UIntPtr extraInfo);
}
