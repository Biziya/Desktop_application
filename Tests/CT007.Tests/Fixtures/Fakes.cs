namespace Ct007.Tests.Fixtures;

/// <summary>HTTP stand-in for the backend: the drillhole endpoint (404 elsewhere), or failures like a real network.</summary>
public sealed class StubBackend : HttpMessageHandler
{
    public const string ThreeDrillholes = """
        [{"holeId":"DH-001","projectCode":"North Ridge","campaign":"2024"},
         {"holeId":"DH-002","projectCode":"North Ridge","campaign":"2025"},
         {"holeId":"DH-003","projectCode":"South Pit","campaign":"2025"}]
        """;

    private readonly object gate = new();
    private readonly List<string> requests = [];

    public volatile bool Offline;
    public string DrillholesJson { get; set; } = ThreeDrillholes;
    public HttpStatusCode DrillholesStatus { get; set; } = HttpStatusCode.OK;
    public string ContentType { get; set; } = "application/json";
    public TimeSpan DrillholesDelay { get; set; } = TimeSpan.Zero;

    /// <summary>Holds drillhole requests until completed (a slow backend).</summary>
    public TaskCompletionSource? HoldDrillholes { get; set; }

    /// <summary>Holds reachability probes (HEAD /) until completed.</summary>
    public TaskCompletionSource? HoldProbe { get; set; }

    public IReadOnlyList<string> Requests { get { lock (gate) return requests.ToList(); } }

    public int Calls(string path) { lock (gate) return requests.Count(r => r.EndsWith(" " + path, StringComparison.Ordinal)); }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        lock (gate) requests.Add($"{request.Method} {path}");
        await Task.Yield();

        if (path == "/" && HoldProbe is { } probe)
        {
            await probe.Task.WaitAsync(cancellationToken);
        }

        if (path == DrillholesClient.Path)
        {
            if (HoldDrillholes is { } hold)
            {
                await hold.Task.WaitAsync(cancellationToken);
            }

            if (DrillholesDelay > TimeSpan.Zero)
            {
                await Task.Delay(DrillholesDelay, cancellationToken);
            }
        }

        if (Offline)
        {
            throw new HttpRequestException("Connection refused");
        }

        return path == DrillholesClient.Path
            ? new HttpResponseMessage(DrillholesStatus) { Content = new StringContent(DrillholesJson, Encoding.UTF8, ContentType) }
            : new HttpResponseMessage(HttpStatusCode.NotFound);
    }
}

/// <summary>In-memory <see cref="IMeasurementRepository"/> with switchable failures and delays (thread-safe: the
/// ViewModel writes from the thread pool).</summary>
public sealed class MemoryRepository(string databasePath) : IMeasurementRepository
{
    private readonly object gate = new();
    private readonly Dictionary<string, MeasurementSession> sessions = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, List<MeasurementResult>> rows = new();
    private readonly Dictionary<Guid, DepthProgress> progress = new();

    public string DatabasePath { get; } = databasePath;

    public volatile bool FailSave;
    public volatile bool FailDelete;

    /// <summary>Delay inside every save (a slow disk).</summary>
    public TimeSpan SaveDelay { get; set; } = TimeSpan.Zero;

    /// <summary>Saves wait for this (never completing it simulates a hung disk).</summary>
    public TaskCompletionSource? SaveGate { get; set; }

    /// <summary>Awaited before a session lookup (Resolve/Find) of the given Drillhole ID.</summary>
    public Func<string, Task>? BeforeLookup { get; set; }

    public int SessionCount { get { lock (gate) return sessions.Count; } }

    public MeasurementSession? Session(string drillholeId) { lock (gate) return sessions.GetValueOrDefault(drillholeId); }

    /// <summary>Stored rows of a session in saved order (the order of the last save).</summary>
    public IReadOnlyList<MeasurementResult> Rows(Guid session) { lock (gate) return rows.GetValueOrDefault(session)?.ToList() ?? []; }

    public DepthProgress? Progress(Guid session) { lock (gate) return progress.GetValueOrDefault(session); }

    /// <summary>Stores a session (and rows, as if loaded from SQLite) before the test.</summary>
    public MeasurementSession Seed(MeasurementSession session, params MeasurementResult[] stored)
    {
        lock (gate)
        {
            sessions[session.DrillholeId!] = session;
            rows[session.Id] = stored.ToList();
            return session;
        }
    }

    public async Task<MeasurementSession> ResolveSessionAsync(MeasurementSession candidate)
    {
        if (BeforeLookup is { } wait) await wait(candidate.DrillholeId!);
        lock (gate) return sessions.TryGetValue(candidate.DrillholeId!, out var found) ? found : sessions[candidate.DrillholeId!] = candidate;
    }

    public async Task<MeasurementSession?> FindSessionAsync(string drillholeId)
    {
        if (BeforeLookup is { } wait) await wait(drillholeId);
        lock (gate) return sessions.GetValueOrDefault(drillholeId);
    }

    public async Task SaveAsync(MeasurementSession session, IReadOnlyList<MeasurementResult> list, DepthProgress? next)
    {
        if (SaveGate is { } gateTask) await gateTask.Task;
        if (SaveDelay > TimeSpan.Zero) await Task.Delay(SaveDelay);
        if (FailSave) throw new IOException("disk full");

        lock (gate)
        {
            var stored = rows.GetValueOrDefault(session.Id) ?? [];
            var byNumber = stored.ToDictionary(r => r.CaptureNumber);
            var ordered = list.Select(r => byNumber.GetValueOrDefault(r.CaptureNumber) ?? r).ToList();
            ordered.AddRange(stored.Where(r => list.All(n => n.CaptureNumber != r.CaptureNumber)));
            rows[session.Id] = ordered;

            if (next is not null)
            {
                progress[session.Id] = next;
            }
        }
    }

    public Task<DepthProgress?> LoadProgressAsync(Guid sessionId) => Task.FromResult(Progress(sessionId));

    public Task DeleteAsync(Guid sessionId, int captureNumber)
    {
        if (FailDelete) throw new IOException("database locked");
        lock (gate) rows.GetValueOrDefault(sessionId)?.RemoveAll(r => r.CaptureNumber == captureNumber);
        return Task.CompletedTask;
    }


    public Task<IReadOnlyList<MeasurementResult>> LoadAsync(Guid sessionId) => Task.FromResult(Rows(sessionId));

    public Task<IReadOnlyList<MeasuredDrillhole>> LoadMeasuredDrillholesAsync()
    {
        lock (gate)
        {
            IReadOnlyList<MeasuredDrillhole> measured = sessions.Values
                .Where(s => rows.GetValueOrDefault(s.Id) is { Count: > 0 })
                .Select(s => new MeasuredDrillhole(s.DrillholeId!, rows[s.Id].Max(r => r.StartTime)))
                .OrderByDescending(m => m.LastMeasured)
                .ToList();
            return Task.FromResult(measured);
        }
    }
}

/// <summary>Capture service driven by the test: each capture waits until Complete/CompleteWith/Fail.</summary>
public sealed class ControlledMeasurements : IMeasurementService
{
    private TaskCompletionSource<CaptureData>? pending;

    public int Calls { get; private set; }

    public DateTimeOffset LastStart { get; private set; }

    public TimeSpan LastDuration { get; private set; }

    /// <summary>The progress callback of every capture, in call order (to report late for an old capture).</summary>
    public List<Action<TimeSpan>> Reporters { get; } = [];

    public async Task<CaptureData> CaptureAsync(DateTimeOffset start, TimeSpan duration, Action<TimeSpan> reportRemaining,
        CancellationToken cancellationToken)
    {
        Calls++;
        LastStart = start;
        LastDuration = duration;
        Reporters.Add(reportRemaining);
        var operation = new TaskCompletionSource<CaptureData>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending = operation;
        using var registration = cancellationToken.Register(() => operation.TrySetCanceled(cancellationToken));
        return await operation.Task;
    }

    /// <summary>Completes with plain samples 1…n (the non-device path).</summary>
    public void Complete(int samples = 5) => pending?.TrySetResult(new CaptureData(
        LastStart, LastStart + LastDuration, Enumerable.Range(1, samples).Select(i => (double)i).ToArray(), "cps", LastStart + LastDuration));

    public void CompleteWith(CaptureData data) => pending?.TrySetResult(data);

    public void Fail(Exception error) => pending?.TrySetException(error);
}

public sealed class FakeDialogs : IDialogService
{
    public bool Confirm { get; set; } = true;

    public int Asked { get; private set; }

    public bool ConfirmDelete()
    {
        Asked++;
        return Confirm;
    }

    public bool ConfirmRemoveImported()
    {
        Asked++;
        return Confirm;
    }

    public bool ConfirmUpload(int recordCount, string drillhole)
    {
        Asked++;
        return Confirm;
    }

    /// <summary>The file "chosen" in the import dialog; null = cancelled.</summary>
    public string? CsvFile { get; set; }

    public string? PickCsvFile() => CsvFile;
}

public sealed class FakeExport : ICsvExportService
{
    public bool Result { get; set; } = true;

    public Exception? Throw { get; set; }

    public int Calls { get; private set; }

    public IReadOnlyList<MeasurementResult>? Rows { get; private set; }

    public MeasurementSession? Session { get; private set; }

    public Task<bool> ExportAsync(IReadOnlyList<MeasurementResult> rows, MeasurementSession session, CancellationToken cancellationToken)
    {
        Calls++;
        Rows = rows;
        Session = session;
        return Throw is { } error ? Task.FromException<bool>(error) : Task.FromResult(Result);
    }
}

/// <summary>Capture clock: waiting advances "now" instantly, so window timing is exact and fast.</summary>
public sealed class FakeCaptureClock(long startMs) : ICaptureClock
{
    private long now = startMs;

    public long NowMs => Interlocked.Read(ref now);

    /// <summary>Called after every advance with the new time (e.g. to cancel at a given moment).</summary>
    public Action<long>? Advanced { get; set; }

    /// <summary>Every Delay call: the time it was requested at and its length.</summary>
    public List<(long AtMs, TimeSpan Delay)> Delays { get; } = [];

    public async Task Delay(TimeSpan delay, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (Delays) Delays.Add((NowMs, delay));
        await Task.Yield();
        Interlocked.Add(ref now, (long)delay.TotalMilliseconds);
        Advanced?.Invoke(NowMs);
        cancellationToken.ThrowIfCancellationRequested();
    }
}

/// <summary>In-memory Gamma Guard samples; a row becomes visible when the clock reaches its write time.</summary>
public sealed class ScriptedSampleSource(ICaptureClock clock) : ISampleSource
{
    private readonly object gate = new();
    private readonly List<(long VisibleAtMs, DeviceSample Sample)> rows = [];
    private int readCalls;

    public int ReadCalls => Volatile.Read(ref readCalls);

    /// <summary>When set, LatestTsMs returns this value (e.g. a seconds-based ts_ms).</summary>
    public Func<long?>? LatestOverride { get; set; }

    public void Add(DeviceSample sample, long visibleAtMs)
    {
        lock (gate) rows.Add((visibleAtMs, sample));
    }

    private List<DeviceSample> Visible()
    {
        var now = clock.NowMs;
        lock (gate) return rows.Where(r => r.VisibleAtMs <= now).Select(r => r.Sample).ToList();
    }

    public long? MaxSampleId() => Visible().Select(s => (long?)s.SourceId).Max();

    public IReadOnlyList<DeviceSample> ReadSamplesAfter(string serial, long afterId)
    {
        Interlocked.Increment(ref readCalls);
        return Visible().Where(s => s.SourceId > afterId && s.Serial == serial).OrderBy(s => s.SourceId).ToList();
    }

    public long? LatestTsMs(string serial) =>
        LatestOverride is { } latest ? latest() :
        Visible().Where(s => s.Serial == serial && s.TsMs is not null).OrderByDescending(s => s.SourceId).FirstOrDefault()?.TsMs;
}

public sealed class FixedDevice(CaptureDevice device) : ICaptureDeviceResolver
{
    public CaptureDevice Resolve() => device;
}

public sealed class FakeBluetoothReader : IBluetoothReader
{
    private readonly object gate = new();
    private readonly List<KnownBluetoothDevice> devices = [];

    /// <summary>Replaces the live read (e.g. to block, throw or return another address).</summary>
    public Func<BluetoothDeviceOption, KnownBluetoothDevice?>? Read { get; set; }

    public void Set(params KnownBluetoothDevice[] list)
    {
        lock (gate)
        {
            devices.Clear();
            devices.AddRange(list);
        }
    }

    public IReadOnlyList<KnownBluetoothDevice> ListDevices(Func<string, bool> nameMatches)
    {
        lock (gate) return devices.Where(d => nameMatches(d.Name)).ToList();
    }

    public KnownBluetoothDevice? ReadDevice(BluetoothDeviceOption device)
    {
        if (Read is { } read) return read(device);
        lock (gate) return devices.Where(d => d.Address == device.Address).OrderByDescending(d => d.Connected).FirstOrDefault();
    }
}

/// <summary>A real SQLite file with the Gamma Guard tables the app reads (schema as assumed in integration-map.md §3).</summary>
public sealed class FakeGammaGuard : IDisposable
{
    private readonly SqliteConnection writer;

    public FakeGammaGuard(string path, bool wal = false)
    {
        Path = path;
        writer = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        writer.Open();
        Execute(wal ? "PRAGMA journal_mode = WAL" : "PRAGMA journal_mode = DELETE");
        Execute("""
            CREATE TABLE IF NOT EXISTS samples (id INTEGER PRIMARY KEY AUTOINCREMENT, serial TEXT, session_id INTEGER, seq INTEGER,
                ts_ms INTEGER, count0 REAL, count0_raw REAL, hdm REAL, hdm_raw REAL, dose_rate REAL, temperature REAL, pressure REAL);
            CREATE TABLE IF NOT EXISTS detector_status (serial TEXT, name TEXT, mac TEXT, firmware TEXT, device_type TEXT,
                session_id INTEGER, state TEXT, response_time REAL, last_sample_ms INTEGER, updated_ms INTEGER);
            """);
    }

    public string Path { get; }

    public void Detector(string serial, string name, string mac, long updatedMs) => Execute(
        "INSERT INTO detector_status VALUES ($s, $n, $m, '2.1.0', 'CT007-M', 41, 'connected', 0.2, $u, $u)",
        ("$s", serial), ("$n", name), ("$m", mac), ("$u", updatedMs));

    /// <summary>Inserts one sample and returns its id.</summary>
    public long Sample(string serial, long? tsMs, double? count0 = 10, double? doseRate = 0.1, double? temperature = 21.5,
        long? seq = null, object? session = null, double? hdm = 1)
    {
        using var command = writer.CreateCommand();
        command.CommandText = """
            INSERT INTO samples (serial, session_id, seq, ts_ms, count0, count0_raw, hdm, hdm_raw, dose_rate, temperature, pressure)
            VALUES ($serial, $session, $seq, $ts, $count0, $count0, $hdm, $hdm, $dose, $temperature, 1013.25) RETURNING id
            """;
        command.Parameters.AddWithValue("$serial", serial);
        command.Parameters.AddWithValue("$session", session ?? 41);
        command.Parameters.AddWithValue("$seq", (object?)seq ?? DBNull.Value);
        command.Parameters.AddWithValue("$ts", (object?)tsMs ?? DBNull.Value);
        command.Parameters.AddWithValue("$count0", (object?)count0 ?? DBNull.Value);
        command.Parameters.AddWithValue("$hdm", (object?)hdm ?? DBNull.Value);
        command.Parameters.AddWithValue("$dose", (object?)doseRate ?? DBNull.Value);
        command.Parameters.AddWithValue("$temperature", (object?)temperature ?? DBNull.Value);
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public void Execute(string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = writer.CreateCommand();
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        command.ExecuteNonQuery();
    }

    public void Dispose() => writer.Dispose();
}

/// <summary>Device sample and capture builders with the defaults most tests share.</summary>
public static class Samples
{
    public const string Serial = "SN-1001";

    public static readonly DetectorIdentity Detector = new(Serial, "CT007-M-0001", "00:11:22:AA:BB:01", "2.1.0", "CT007-M");

    public static DeviceSample At(long id, long? tsMs, double? count0 = 10, string serial = Serial, string? session = "41",
        long? seq = null, double? doseRate = null, double? temperature = null, double? pressure = null) =>
        new(id, serial, session, seq, tsMs, count0, count0, null, null, doseRate, temperature, pressure);

    public static CaptureData Capture(DateTimeOffset start, TimeSpan duration, int expected, IReadOnlyList<DeviceSample> samples) =>
        new(start, start + duration, [], "", start + duration, DeviceSamples: samples, Device: Detector, ExpectedSamples: expected);
}
