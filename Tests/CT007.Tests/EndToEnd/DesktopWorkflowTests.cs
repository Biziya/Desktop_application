using System.Windows.Automation;

namespace Ct007.Tests.EndToEnd;

/// <summary>
/// E2E-001…E2E-004: the built Frontend.exe driven through UI Automation and real mouse input (opt-in category E2E,
/// dedicated Windows test account; see DesktopBench). Gamma Guard is simulated and the backend serves fake drillholes:
/// these tests do not verify the real detector or SQL Server (MAN-001, BE-006).
/// </summary>
public static class DesktopWorkflowTests
{
    private static async Task MeasureThree(DesktopApp app)
    {
        await app.PickDrillhole("DH-0", "North Ridge");
        await app.SaveAndContinue();
        await app.Start("10.00", "25");

        for (var row = 1; row <= 3; row++)
        {
            await app.Capture(row);
        }
    }

    [Test("E2E-001", "Main journey: pick, Save & Continue, three right-click captures, CSV export", Category = "E2E", TimeoutSeconds = 180)]
    public static async Task MainJourney()
    {
        await using var bench = await DesktopBench.CreateAsync();
        await using var app = await bench.LaunchAsync();
        await WaitBackend(app, "Online", 15_000);

        await MeasureThree(app);

        Assert.Equal("10.75", DesktopApp.Text(await app.Find("Starting depth in metres")), "Starting Depth advanced three steps");
        Assert.Sequence(new[] { "10000|10250", "10250|10500", "10500|10750" },
            AppDatabase.Rows(DesktopBench.Database, "SELECT \"from\", \"to\" FROM MeasurementRecords ORDER BY CaptureNo"), "stored intervals");

        var target = bench.Folder.Path("export.csv");
        DesktopApp.Invoke(await app.FindButtonContaining("Export CSV"));
        var dialog = await app.Dialog("Export measurements");
        DesktopApp.SetText(dialog.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "1001"))
            ?? throw new AssertionException("file name box"), target);
        DesktopApp.Invoke(dialog.FindFirst(TreeScope.Descendants, new AndCondition(new PropertyCondition(AutomationElement.AutomationIdProperty, "1"), new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)))
            ?? throw new AssertionException("Save button"));
        await DesktopApp.WaitAsync(() => File.Exists(target) ? app.Window : null, "exported file");
        await Task.Delay(500);

        var lines = File.ReadAllText(target).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(BackendTests.CsvExportTests.Header, lines[0].TrimStart('﻿'), "CSV header");
        Assert.Equal(4, lines.Length, "header + 3 rows");
        Assert.True(lines.Skip(1).All(l => l.Contains(",\"DH-001\",\"North Ridge\",\"2024\",", StringComparison.Ordinal)), "rows carry the drillhole");
    }

    [Test("E2E-002", "Restart: the drillhole's rows and next depth come back", Category = "E2E", TimeoutSeconds = 240)]
    public static async Task RestartPersistence()
    {
        await using var bench = await DesktopBench.CreateAsync();

        await using (var first = await bench.LaunchAsync())
        {
            await MeasureThree(first);
            Invoke(await first.Find("Stop measurement"));
        }

        await using var app = await bench.LaunchAsync();
        await app.PickDrillhole("DH-001", "North Ridge");
        await app.SaveAndContinue();
        await app.WaitRows(3);

        Assert.Equal("10.75", DesktopApp.Text(await app.Find("Starting depth in metres")), "next Starting Depth restored");
        Assert.Equal("25", DesktopApp.Text(await app.Find("Depth step in centimetres")), "Step restored");
        Assert.Equal(3L, AppDatabase.Count(DesktopBench.Database, "MeasurementRecords"), "stored rows unchanged by the restart");
    }

    [Test("E2E-003", "Offline: the saved list works without the backend; it turns Online when the backend starts", Category = "E2E", TimeoutSeconds = 240)]
    public static async Task OfflineOperation()
    {
        await using var bench = await DesktopBench.CreateAsync();

        await using (var first = await bench.LaunchAsync())
        {
            await first.PickDrillhole("DH-002", "North Ridge");
        }

        await bench.StopBackendAsync();
        await using var app = await bench.LaunchAsync();
        await WaitBackend(app, "Offline", 15_000);

        await app.PickDrillhole("DH-001", "North Ridge");
        await app.SaveAndContinue();
        await app.Start("5.00", "50");
        await app.Capture(1);
        Assert.Equal(1L, AppDatabase.Count(DesktopBench.Database, "MeasurementRecords"), "capture stored while offline");

        await bench.StartBackendAsync();
        await WaitBackend(app, "Online", 5_000); // status interval is 1 s in the test settings
    }

    [Test("E2E-004", "Delete through the real confirmation dialog: Cancel keeps, Delete removes, restart agrees", Category = "E2E", TimeoutSeconds = 240)]
    public static async Task DeleteWithDialog()
    {
        await using var bench = await DesktopBench.CreateAsync();

        await using (var first = await bench.LaunchAsync())
        {
            await MeasureThree(first);
            Invoke(await first.Find("Stop measurement"));

            AutomationElement SecondDelete() => first.Window.FindAll(TreeScope.Descendants, new AndCondition(
                    new PropertyCondition(AutomationElement.NameProperty, "Delete measurement"),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)))
                .Cast<AutomationElement>().ElementAt(1);

            Invoke(SecondDelete());
            var dialog = await first.Dialog("Delete measurement");
            Invoke(DesktopApp.Button(dialog, name => name == "Cancel"));
            await Task.Delay(500);
            Assert.Equal(3, first.RowCount(), "Cancel keeps the row");

            Invoke(SecondDelete());
            dialog = await first.Dialog("Delete measurement");
            Invoke(DesktopApp.Button(dialog, name => name == "Delete"));
            await first.WaitRows(2);
            await first.Find("Measurement deleted.");
        }

        await using var app = await bench.LaunchAsync();
        await app.PickDrillhole("DH-001", "North Ridge");
        await app.SaveAndContinue();
        await app.WaitRows(2);
        Assert.Sequence(new[] { "1", "3" }, AppDatabase.Rows(DesktopBench.Database, "SELECT CaptureNo FROM MeasurementRecords ORDER BY CaptureNo"),
            "stored rows after restart");
    }

    private static void Invoke(AutomationElement element) => DesktopApp.Invoke(element);

    // The sidebar dot's accessible name is the tooltip "Backend <url>: Online|Offline (checked …)".
    private static Task<AutomationElement> WaitBackend(DesktopApp app, string state, int timeoutMs) => DesktopApp.WaitAsync(() =>
        app.Window.FindAll(TreeScope.Descendants, Condition.TrueCondition).Cast<AutomationElement>()
            .FirstOrDefault(e => e.Current.Name.StartsWith("Backend http://", StringComparison.Ordinal) && e.Current.Name.Contains(": " + state, StringComparison.Ordinal)),
        $"backend indicator {state}", timeoutMs);
}
