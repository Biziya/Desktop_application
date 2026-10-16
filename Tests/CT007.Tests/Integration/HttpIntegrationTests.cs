using System.Reflection;
using System.Text.Json;
using BackendDrillhole = Backend.Models.CoreInformation.Drillhole.DrillholeResponse;

namespace Ct007.Tests.IntegrationTests;

/// <summary>INT-001…INT-006: real frontend API client and pages against the in-process backend over HTTP (F01, F02, F04, F05, F19).</summary>
public static class HttpIntegrationTests
{
    private const string RefusedConnection =
        "Server=tcp:127.0.0.1,1;Database=CT007_Test;User ID=ct007_test_user;Password=NotARealPassword-1;Connect Timeout=3;ConnectRetryCount=0;Encrypt=False";

    private static MainViewModel Shell(string baseUrl, IMeasurementRepository repository, IMeasurementService capture, string gammaGuardPath,
        TimeSpan? statusInterval = null, double timeoutSeconds = 5) =>
        new(repository, new ApiSettings(baseUrl, TimeSpan.FromSeconds(timeoutSeconds), statusInterval ?? TimeSpan.FromMinutes(5)),
            Make.Devices(gammaGuardPath), handler: null, bluetoothDevices: () => [], captureService: capture);

    [Test("INT-001", "SQL projection fields reach the form, the stored session and the CSV under the right labels")]
    public static async Task FieldMapping()
    {
        var drillholes = new FakeDrillholeRepository { Rows = [new BackendDrillhole("DH-1", "PRJ-A", "2024"), new BackendDrillhole("DH-2", "", "")] };
        await using var backend = await TestBackend.StartAsync(drillholes);
        using var folder = new TestFolder();
        var repository = new SqliteMeasurementRepository(folder.Path("m.db"));
        var device = new ControlledMeasurements();
        using var main = Shell(backend.BaseUrl, repository, device, folder.Path("gg.sqlite"));
        await main.Core.LoadDrillholesAsync();

        await Make.Pick(main.Core, "DH-2");
        Assert.Equal("", main.Core.DrillholeName, "DH-2: empty Project");
        Assert.Equal("", main.Core.ProjectArea, "DH-2: empty Campaign");

        await Make.Pick(main.Core, "DH-1");
        Assert.Equal("PRJ-A", main.Core.DrillholeName, "Project = projectCode");
        Assert.Equal("2024", main.Core.ProjectArea, "Campaign = campaign");
        main.Core.SaveCommand.Execute(null);
        await Assert.Eventually(() => main.Measurement.ToggleMeasurementCommand.CanExecute(null), "session open");

        var session = (await repository.FindSessionAsync("DH-1"))!;
        Assert.Equal("PRJ-A", session.DrillholeName, "stored DrillholeName = Project");
        Assert.Equal("2024", session.Project, "stored Project = Campaign");

        main.Measurement.NextFrom = "1.00";
        main.Measurement.Step = "25";
        main.Measurement.ToggleMeasurementCommand.Execute(null);
        await Make.Capture(main.Measurement, device);
        var csv = CsvExportService.Serialize(main.Measurement.Results.ToList(), session).Split("\r\n");
        var header = csv[0].Split(',');
        var cells = csv[1].Split(',');
        Assert.Equal("\"PRJ-A\"", cells[Array.IndexOf(header, "\"Project\"")], "CSV Project");
        Assert.Equal("\"2024\"", cells[Array.IndexOf(header, "\"Campaign\"")], "CSV Campaign");
        Assert.Equal("\"DH-1\"", cells[Array.IndexOf(header, "\"Drillhole\"")], "CSV Drillhole");
    }

    [Test("INT-002", "A 503 from the backend is a refresh error with the saved list kept; the backend stays Online")]
    public static async Task ServiceUnavailablePropagation()
    {
        await using var backend = await TestBackend.StartAsync(settings: new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = RefusedConnection
        });
        using var folder = new TestFolder();
        var db = folder.Path("m.db");
        await new DrillholeCache(db).ReplaceAsync([new DrillholeResponse("C-1", "Cached", "1999")]);
        var form = Make.CoreForm(null, db, new MemoryRepository(db), out var connection, baseUrl: backend.BaseUrl);

        await form.LoadDrillholesAsync();

        Assert.Equal("Showing the saved drillhole list. Could not refresh: Backend error 503 (Service Unavailable).", form.DrillholesMessage, "message");
        Assert.Equal(BackendState.Online, connection.State, "backend answered, so it is Online");
        Assert.True(form.CanRetryDrillholes, "Retry offered");
        Assert.Sequence(new[] { "C-1" }, Make.Ids(form), "saved list kept");
        Assert.False(form.DrillholesMessage.Contains("drillhole database is unavailable", StringComparison.Ordinal), "ProblemDetails text not shown");
    }

    [Test("INT-003", "Offline start with a saved list, then recovery: Online, one reload, pick kept", Category = "Timing")]
    public static async Task OfflineStartAndRecovery()
    {
        using var folder = new TestFolder();
        var repository = new SqliteMeasurementRepository(folder.Path("m.db"));
        await new DrillholeCache(repository.DatabasePath).ReplaceAsync(
            new[] { "C-1", "C-2", "C-3" }.Select(id => new DrillholeResponse(id, "Cached", "1999")).ToList());
        var port = TestBackend.FreePort();
        using var main = Shell($"http://127.0.0.1:{port}", repository, new ControlledMeasurements(), folder.Path("gg.sqlite"),
            TimeSpan.FromMilliseconds(100), timeoutSeconds: 2);

        await Assert.Eventually(() => Make.Ids(main.Core).Length == 3 && main.Backend.State == BackendState.Offline, "saved list shown, Offline");
        await Make.Pick(main.Core, "C-2");
        Assert.Equal(1, main.Toasts.Items.Count(t => t.Kind == ToastKind.Warning && t.Message.StartsWith("Backend offline", StringComparison.Ordinal)),
            "one offline warning");

        var drillholes = new FakeDrillholeRepository
        {
            Rows = [new BackendDrillhole("C-2", "Server", "2025"), new BackendDrillhole("S-1", "Server", "2025")]
        };
        await using var backend = await TestBackend.StartAsync(drillholes, port: port);

        await Assert.Eventually(() => main.Backend.State == BackendState.Online && Make.Ids(main.Core).SequenceEqual(new[] { "C-2", "S-1" }),
            "Online and server list shown", 5000);
        Assert.True(Make.Toasted(main.Toasts, ToastKind.Success, "Backend online again."), "online announcement");
        Assert.Equal("C-2", main.Core.DrillholeId, "pick kept");
        await Assert.Remains(() => drillholes.Calls == 1, "exactly one reload", 500);
        Assert.Sequence(new[] { "C-2", "S-1" }, (await new DrillholeCache(repository.DatabasePath).LoadAsync()).Select(d => d.HoleId!).Order(StringComparer.Ordinal),
            "cache replaced");
    }

    [Test("INT-004", "A backend slower than the client timeout counts as offline; a fast answer recovers", Category = "Timing")]
    public static async Task SlowBackend()
    {
        var drillholes = new FakeDrillholeRepository { Delay = TimeSpan.FromSeconds(3), Rows = [new BackendDrillhole("S-1", "P", "C")] };
        await using var backend = await TestBackend.StartAsync(drillholes);
        using var folder = new TestFolder();
        var db = folder.Path("m.db");
        var form = Make.CoreForm(null, db, new MemoryRepository(db), out var connection, baseUrl: backend.BaseUrl, timeoutSeconds: 1);

        await form.LoadDrillholesAsync();
        Assert.Contains("Backend did not respond in time.", form.DrillholesMessage, "timeout message");
        Assert.Equal(BackendState.Offline, connection.State, "Offline after the timeout");

        drillholes.Delay = TimeSpan.Zero;
        await form.LoadDrillholesAsync();
        Assert.Sequence(new[] { "S-1" }, Make.Ids(form), "list after recovery");
        Assert.Equal(BackendState.Online, connection.State, "Online again");
    }

    [Test("INT-005", "Contract guard: the backend and frontend DrillholeResponse stay aligned")]
    public static void ContractAlignment()
    {
        string[] Names(Type type) => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length == 0 && p.DeclaringType == type && p.Name != "EqualityContract")
            .Select(p => p.Name).Order(StringComparer.Ordinal).ToArray();

        Assert.Sequence(Names(typeof(BackendDrillhole)), Names(typeof(DrillholeResponse)), "same property names");

        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var json = JsonSerializer.Serialize(new[] { new BackendDrillhole("DH-1", "P", "2024") }, web);
        var parsed = JsonSerializer.Deserialize<IReadOnlyList<DrillholeResponse>>(json, web)!;
        Assert.Equal(new DrillholeResponse("DH-1", "P", "2024"), parsed.Single(), "backend JSON read by the frontend");

        var extended = JsonSerializer.Deserialize<IReadOnlyList<DrillholeResponse>>("""[{"holeId":"A","projectCode":"P","campaign":"C","extra":1}]""", web)!;
        Assert.Equal(new DrillholeResponse("A", "P", "C"), extended.Single(), "an extra field is ignored");
    }

    [Test("INT-006", "A list loaded online is available after a restart while offline")]
    public static async Task CacheAcrossRestart()
    {
        using var folder = new TestFolder();
        var db = folder.Path("m.db");
        var online = new StubBackend
        {
            DrillholesJson = System.Text.Json.JsonSerializer.Serialize(new[] { "E", "B", "D", "A", "C" }.Select(id => new { holeId = id, projectCode = "P", campaign = "C" }))
        };
        var first = Make.CoreForm(online, db, new MemoryRepository(db), out _);
        await first.LoadDrillholesAsync();
        var shown = Make.Ids(first);

        var offline = new StubBackend { Offline = true };
        var restarted = Make.CoreForm(offline, db, new MemoryRepository(db), out var connection);
        await restarted.LoadDrillholesAsync();

        Assert.Sequence(shown, Make.Ids(restarted), "same entries in the same order");
        Assert.Sequence(new[] { "A", "B", "C", "D", "E" }, Make.Ids(restarted), "alphabetical");
        Assert.Equal(BackendState.Offline, connection.State, "no successful request");
    }
}
