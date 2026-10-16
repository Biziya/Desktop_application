namespace Ct007.Tests.FrontendTests;

/// <summary>FE-001…FE-008: API client results, settings and the backend connection monitor (F01, F05).</summary>
public static class ApiClientTests
{
    private static (ApiClient Api, List<bool> Reached) Client(StubBackend backend, double timeoutSeconds = 5)
    {
        var api = new ApiClient(new ApiSettings("http://stub.test", TimeSpan.FromSeconds(timeoutSeconds), TimeSpan.FromMinutes(5)), backend);
        var reached = new List<bool>();
        api.Reached += reached.Add;
        return (api, reached);
    }

    [Test("FE-001", "A 200 JSON array maps to Ok")]
    public static async Task OkResponse()
    {
        var backend = new StubBackend { DrillholesJson = """[{"holeId":"DH-001","projectCode":"P1","campaign":"2024"}]""" };
        var (api, reached) = Client(backend);

        var result = await new DrillholesClient(api).GetAsync(CancellationToken.None);

        Assert.Equal(ApiStatus.Ok, result.Status, "status");
        Assert.Equal(1, result.Value!.Count, "items");
        Assert.Equal(new DrillholeResponse("DH-001", "P1", "2024"), result.Value[0], "item");
        Assert.Sequence(new[] { true }, reached, "Reached events");
    }

    [Test("FE-002", "A non-success status maps to HttpError with the code; the backend counts as reached")]
    public static async Task HttpErrors()
    {
        foreach (var status in new[] { HttpStatusCode.NotFound, HttpStatusCode.InternalServerError, HttpStatusCode.ServiceUnavailable })
        {
            var backend = new StubBackend
            {
                DrillholesStatus = status,
                DrillholesJson = """{"title":"The drillhole database is unavailable.","status":503}""",
                ContentType = "application/problem+json"
            };
            var (api, reached) = Client(backend);

            var result = await new DrillholesClient(api).GetAsync(CancellationToken.None);

            Assert.Equal(ApiStatus.HttpError, result.Status, $"{(int)status} status");
            Assert.Contains($"Backend error {(int)status}", result.Error, $"{(int)status} message");
            Assert.False(result.Error.Contains("drillhole database", StringComparison.OrdinalIgnoreCase), $"{(int)status}: body is not shown");
            Assert.Sequence(new[] { true }, reached, $"{(int)status} Reached events");
        }
    }

    [Test("FE-003", "Refused connection and timeout map to Unreachable without throwing")]
    public static async Task Unreachable()
    {
        var refused = new StubBackend { Offline = true };
        var (api, reached) = Client(refused);
        var result = await new DrillholesClient(api).GetAsync(CancellationToken.None);
        Assert.Equal(ApiStatus.Unreachable, result.Status, "refused: status");
        Assert.Sequence(new[] { false }, reached, "refused: Reached events");

        var slow = new StubBackend { DrillholesDelay = TimeSpan.FromSeconds(10) };
        var (slowApi, slowReached) = Client(slow, timeoutSeconds: 1);
        var timedOut = await new DrillholesClient(slowApi).GetAsync(CancellationToken.None);
        Assert.Equal(ApiStatus.Unreachable, timedOut.Status, "timeout: status");
        Assert.Contains("did not respond in time", timedOut.Error, "timeout: message");
        Assert.Sequence(new[] { false }, slowReached, "timeout: Reached events");
    }

    [Test("FE-004", "Malformed or unexpected payloads map to InvalidResponse")]
    public static async Task InvalidPayloads()
    {
        foreach (var body in new[] { "{not json", """{"holeId":"x"}""", "", "<html>error</html>" })
        {
            var (api, reached) = Client(new StubBackend { DrillholesJson = body });

            var result = await new DrillholesClient(api).GetAsync(CancellationToken.None);

            Assert.Equal(ApiStatus.InvalidResponse, result.Status, $"'{body}': status");
            Assert.Equal("Backend returned unexpected data.", result.Error, $"'{body}': message");
            Assert.Sequence(new[] { true }, reached, $"'{body}': Reached events");
        }

        // A JSON null is not an empty list: it is reported as invalid too (message differs: "no data").
        var (nullApi, _) = Client(new StubBackend { DrillholesJson = "null" });
        var nothing = await new DrillholesClient(nullApi).GetAsync(CancellationToken.None);
        Assert.Equal(ApiStatus.InvalidResponse, nothing.Status, "null: status");
    }

    [Test("FE-005", "ApiSettings.Load falls back to defaults for missing or invalid values")]
    public static void SettingsDefaults()
    {
        using var folder = new TestFolder();
        var defaults = ApiSettings.Default;
        Assert.Equal(new ApiSettings("http://localhost:5002", TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5)), defaults, "defaults");

        var cases = new (string Name, string? Json)[]
        {
            ("missing file", null),
            ("invalid JSON", "{ baseUrl: "),
            ("zero timeout", """{"requestTimeoutSeconds":0}"""),
            ("negative timeout", """{"requestTimeoutSeconds":-1}"""),
            ("text timeout", """{"requestTimeoutSeconds":"abc"}"""),
            ("relative URL", """{"baseUrl":"api/backend"}""")
        };

        foreach (var (name, json) in cases)
        {
            var path = folder.Path(Guid.NewGuid() + ".json");

            if (json is not null)
            {
                File.WriteAllText(path, json);
            }

            Assert.Equal(defaults, ApiSettings.Load(path), name);
        }

        var valid = folder.Path("valid.json");
        File.WriteAllText(valid, """{"baseUrl":"http://host:6000","requestTimeoutSeconds":10,"statusIntervalSeconds":2}""");
        Assert.Equal(new ApiSettings("http://host:6000", TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(2)), ApiSettings.Load(valid), "valid file");
    }

    [Test("FE-006", "Probe polling runs on the interval with HEAD only; overlapping checks are shared", Category = "Timing")]
    public static async Task ProbePolling()
    {
        var backend = new StubBackend();
        var (api, _) = Client(backend);
        using var connection = new BackendConnection(api, TimeSpan.FromMilliseconds(100));
        Assert.Equal(BackendState.Checking, connection.State, "initial state");

        connection.Start();
        await Task.Delay(1000);
        var probes = backend.Calls("/");
        Assert.True(probes is >= 5 and <= 15, $"about 10 probes in 1 s at 100 ms (was {probes})");
        Assert.Equal(BackendState.Online, connection.State, "404 on HEAD / counts as Online");
        Assert.True(backend.Requests.All(r => r == "HEAD /"), $"only HEAD / requests ({string.Join("; ", backend.Requests.Distinct())})");

        connection.Dispose();
        var held = new StubBackend { HoldProbe = new TaskCompletionSource() };
        var (heldApi, _) = Client(held);
        using var manual = new BackendConnection(heldApi, TimeSpan.FromMinutes(5));
        var first = manual.CheckNowAsync();
        var second = manual.CheckNowAsync();
        var third = manual.CheckNowAsync();
        await Assert.Eventually(() => held.Calls("/") == 1, "one probe in flight");
        Assert.True(ReferenceEquals(first, second) && ReferenceEquals(second, third), "callers share the running check");
        held.HoldProbe!.SetResult();
        await first;
        Assert.Equal(1, held.Calls("/"), "probes sent");

        held.HoldProbe = null;
        held.Offline = true;
        await manual.CheckNowAsync();
        Assert.Equal(BackendState.Offline, manual.State, "refused probe");
    }

    [Test("FE-007", "CameOnline reloads the drillhole list only after a failed load")]
    public static async Task ReloadAfterRecovery()
    {
        using var folder = new TestFolder();

        var failing = new StubBackend { Offline = true };
        var form = Make.CoreForm(failing, folder.Path("a.db"), new MemoryRepository(folder.Path("a.db")), out var connection);
        await form.LoadDrillholesAsync();
        Assert.Equal(1, failing.Calls(DrillholesClient.Path), "first load");
        Assert.Equal(BackendState.Offline, connection.State, "offline after the failed load");
        failing.Offline = false;
        await connection.CheckNowAsync();
        await Assert.Eventually(() => failing.Calls(DrillholesClient.Path) == 2 && Make.Ids(form).Length == 3, "reload after coming online");

        var healthy = new StubBackend();
        var other = Make.CoreForm(healthy, folder.Path("b.db"), new MemoryRepository(folder.Path("b.db")), out var otherConnection);
        await other.LoadDrillholesAsync();
        healthy.Offline = true;
        await otherConnection.CheckNowAsync();
        healthy.Offline = false;
        await otherConnection.CheckNowAsync();
        await Assert.Remains(() => healthy.Calls(DrillholesClient.Path) == 1, "no reload when the last load succeeded");
    }

    [Test("FE-008", "Background polling stops after MainViewModel.Dispose", Category = "Timing")]
    public static async Task LoopsStopOnDispose()
    {
        using var folder = new TestFolder();
        var backend = new StubBackend();
        var main = Make.Shell(new MemoryRepository(folder.Path("m.db")), backend, new ControlledMeasurements(), folder.Path("gg.sqlite"),
            TimeSpan.FromMilliseconds(50));
        await Assert.Eventually(() => backend.Calls("/") >= 2, "polling runs");

        main.Dispose();
        await Task.Delay(100);
        var after = backend.Calls("/");
        await Task.Delay(300);
        Assert.Equal(after, backend.Calls("/"), "no probes after dispose");
    }
}
