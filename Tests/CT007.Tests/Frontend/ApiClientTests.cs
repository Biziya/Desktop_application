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

    [Test("FE-005", "APP_ENV comes from the APP_ENV variable, otherwise the build; only local and production are accepted")]
    public static void SettingsEnvironmentSelection()
    {
        Func<string, string?> noVariables = _ => null;
        Assert.Equal("local", ApiSettings.SelectEnvironment(noVariables, "local"), "build default local");
        Assert.Equal("production", ApiSettings.SelectEnvironment(noVariables, "production"), "build default production");

        var variables = new Dictionary<string, string> { ["APP_ENV"] = "local" };
        Assert.Equal("local", ApiSettings.SelectEnvironment(variables.GetValueOrDefault, "production"), "variable wins over the build");

        foreach (var (name, variable, build) in new (string, string?, string?)[]
                 { ("unknown variable", "staging", "local"), ("wrong case", "Production", "local"), ("no value at all", null, null) })
        {
            var values = new Dictionary<string, string?> { ["APP_ENV"] = variable };
            var error = Assert.Throws<InvalidDataException>(() => ApiSettings.SelectEnvironment(values.GetValueOrDefault, build), name);
            Assert.Contains("local, production", error.Message, name);
        }

        // Debug builds of this repository compile AppEnvironment=local into the assembly (Frontend.csproj).
        Assert.True(ApiSettings.BuildEnvironment is "local" or "production", "the build compiles an environment in");
    }

    [Test("FE-005", "The per-user .env.<APP_ENV> wins over the project's; only the first existing file is read")]
    public static void SettingsFileSelection()
    {
        using var folder = new TestFolder();
        var config = folder.Path("Config");
        var project = folder.Path("Frontend");
        Directory.CreateDirectory(config);
        Directory.CreateDirectory(Path.Combine(project, "bin", "Debug"));
        File.WriteAllText(Path.Combine(project, "Frontend.csproj"), "<Project />");
        Func<string, string?> noVariables = _ => null;

        Assert.Equal(project, ApiSettings.ProjectDirectory(Path.Combine(project, "bin", "Debug")), "project found above bin");
        Assert.Equal(null, ApiSettings.ProjectDirectory(config), "installed: no project folder");

        var candidates = ApiSettings.EnvFileCandidates("production", config, project);
        Assert.Sequence([Path.Combine(config, ".env.production"), Path.Combine(project, ".env.production")], candidates, "lookup order");
        Assert.Sequence([Path.Combine(config, ".env.local")], ApiSettings.EnvFileCandidates("local", config, null), "installed: config folder only");

        File.WriteAllText(Path.Combine(project, ".env.production"), "API_BASE_URL=https://project.test\nAPI_TIMEOUT_SECONDS=9\n");
        Assert.Equal(new ApiSettings("https://project.test", TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(5)),
            ApiSettings.Load("production", candidates, noVariables), "project file; missing optional key uses its default");

        File.WriteAllText(Path.Combine(config, ".env.production"), "APP_ENV=production\nAPI_BASE_URL=https://user.test\n");
        Assert.Equal(new ApiSettings("https://user.test", TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5)),
            ApiSettings.Load("production", candidates, noVariables), "per-user file wins; the project file is not merged in");

        // The other environment's file is never a fallback.
        File.WriteAllText(Path.Combine(config, ".env.local"), "API_BASE_URL=http://localhost:5002\n");
        var missing = Assert.Throws<InvalidDataException>(
            () => ApiSettings.Load("production", ApiSettings.EnvFileCandidates("production", folder.Path("empty"), null), noVariables), "no file");
        Assert.Contains("API_BASE_URL", missing.Message, "no file: names the key");
        Assert.Contains(".env.production", missing.Message, "no file: names the expected file");

        var mismatch = Path.Combine(config, ".env.production");
        File.WriteAllText(mismatch, "APP_ENV=local\nAPI_BASE_URL=http://localhost:5002\n");
        Assert.Contains("APP_ENV=local", Assert.Throws<InvalidDataException>(
            () => ApiSettings.Load("production", candidates, noVariables), "APP_ENV line differs").Message, "APP_ENV line differs");

        File.WriteAllText(mismatch, "API_TIMEOUT_SECONDS=10\nAPI_BASE_URL=\n");
        Assert.Contains("does not set API_BASE_URL", Assert.Throws<InvalidDataException>(
            () => ApiSettings.Load("production", candidates, noVariables), "empty URL").Message, "empty URL");
    }

    [Test("FE-005", "API_* variables override the .env file key by key; invalid values are errors")]
    public static void SettingsEnvOverrides()
    {
        using var folder = new TestFolder();
        var env = folder.Path(".env.local");
        string[] candidates = [env];
        Func<string, string?> noVariables = _ => null;

        File.WriteAllText(env, "# comment\n\nAPI_BASE_URL=\"https://api.test\"\nAPI_TIMEOUT_SECONDS=30\nOTHER=x\n");
        Assert.Equal(new ApiSettings("https://api.test", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5)),
            ApiSettings.Load("local", candidates, noVariables), ".env values; missing key uses its default");

        var variables = new Dictionary<string, string> { ["API_BASE_URL"] = "http://override:7000", ["API_RETRY_INTERVAL_SECONDS"] = "10" };
        Assert.Equal(new ApiSettings("http://override:7000", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10)),
            ApiSettings.Load("local", candidates, variables.GetValueOrDefault), "environment variables win");
        Assert.Equal(new ApiSettings("http://override:7000", TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)),
            ApiSettings.Load("local", [folder.Path("missing")], variables.GetValueOrDefault), "variables alone, no file");

        var cases = new (string Name, string Content, string Message)[]
        {
            ("relative URL", "API_BASE_URL=api/backend", "API_BASE_URL"),
            ("ftp URL", "API_BASE_URL=ftp://host", "API_BASE_URL"),
            ("URL with credentials", "API_BASE_URL=https://user:secret@host", "API_BASE_URL"),
            ("zero timeout", "API_BASE_URL=http://host\nAPI_TIMEOUT_SECONDS=0", "API_TIMEOUT_SECONDS"),
            ("negative timeout", "API_BASE_URL=http://host\nAPI_TIMEOUT_SECONDS=-1", "API_TIMEOUT_SECONDS"),
            ("text interval", "API_BASE_URL=http://host\nAPI_RETRY_INTERVAL_SECONDS=abc", "API_RETRY_INTERVAL_SECONDS"),
            ("line without =", "API_BASE_URL", "line 1"),
            ("no URL", "API_TIMEOUT_SECONDS=5", "does not set API_BASE_URL")
        };

        foreach (var (name, content, message) in cases)
        {
            File.WriteAllText(env, content);
            var error = Assert.Throws<InvalidDataException>(() => ApiSettings.Load("local", candidates, noVariables), name);
            Assert.Contains(message, error.Message, name);
            Assert.False(error.Message.Contains("secret"), $"{name}: no credentials in the message");
        }
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
        var deviceListReads = 0;
        var devices = Make.Devices(folder.Path("gg.sqlite")) with
        {
            CheckInterval = TimeSpan.FromMilliseconds(50),
            BluetoothCheckInterval = TimeSpan.FromMilliseconds(50)
        };
        var main = new MainViewModel(new MemoryRepository(folder.Path("m.db")),
            new ApiSettings("http://stub.test", TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(50)), devices, backend,
            () => { Interlocked.Increment(ref deviceListReads); return []; }, new ControlledMeasurements());
        await Assert.Eventually(() => backend.Calls("/") >= 2 && Volatile.Read(ref deviceListReads) >= 2, "probe and Device Status loops run");

        main.Dispose();
        await Task.Delay(100);
        var probes = backend.Calls("/");
        var reads = Volatile.Read(ref deviceListReads);
        await Task.Delay(300);
        Assert.Equal(probes, backend.Calls("/"), "no probes after dispose");
        Assert.Equal(reads, Volatile.Read(ref deviceListReads), "no Device Status checks after dispose");
    }
}
