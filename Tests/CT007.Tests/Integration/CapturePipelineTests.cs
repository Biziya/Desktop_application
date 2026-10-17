namespace Ct007.Tests.IntegrationTests;

/// <summary>
/// INT-007…INT-017: Measurement page + real DeviceCaptureService + real Gamma Guard reader + real SQLite repository
/// (F07, F08, F11, F12, F15–F19). The Gamma Guard database is a real SQLite file written by the test (FakeGammaGuard);
/// it stands in for the vendor application and does not verify the real Gamma Guard behaviour (MAN-001).
/// </summary>
public static class CapturePipelineTests
{
    private const long T = 1_760_600_000_000;

    /// <summary>The Measurement page wired to a Gamma Guard file through the production capture classes.</summary>
    private sealed class Pipeline : IDisposable
    {
        public Pipeline(TestFolder folder, ICaptureClock? clock = null, bool wal = false, string databaseName = "m.db")
        {
            GammaGuard = new FakeGammaGuard(folder.Path("gg.sqlite"), wal);
            GammaGuard.Detector(Samples.Serial, "CT007-M-0001", "00:11:22:AA:BB:01", T);
            Repository = new SqliteMeasurementRepository(folder.Path(databaseName));
            Devices = Make.Monitor(Repository.DatabasePath, GammaGuard.Path);
            Clock = clock ?? new FakeCaptureClock(T);
            ViewModel = new MeasurementViewModel(new DeviceCaptureService(new GammaGuardCaptureDevices(Devices), Clock), Export, Dialogs,
                Repository, false, Toasts, new CaptureDurationSettings(null));
        }

        public FakeGammaGuard GammaGuard { get; }
        public SqliteMeasurementRepository Repository { get; }
        public DeviceStatusMonitor Devices { get; }
        public ICaptureClock Clock { get; }
        public FakeExport Export { get; } = new();
        public FakeDialogs Dialogs { get; } = new();
        public ToastService Toasts { get; } = new();
        public MeasurementViewModel ViewModel { get; }

        public async Task Arm(string startDepth = "10.00")
        {
            await Devices.CheckGammaGuardDatabaseAsync();
            Assert.Equal(LocalState.Available, Devices.GammaGuardDatabase.State, "Gamma Guard database available");
            await Make.Arm(ViewModel, Make.Core, startDepth);
        }

        /// <summary>A click at the clock's current time; waits until the page is Ready again.</summary>
        public async Task Click(int timeoutMs = 3000)
        {
            ViewModel.TriggerCommand.Execute(DateTimeOffset.FromUnixTimeMilliseconds(Clock.NowMs));
            await Assert.Eventually(() => ViewModel.State == MeasurementState.Ready, "capture finished", timeoutMs);
        }

        public void Dispose()
        {
            ViewModel.Dispose();
            Devices.Dispose();
            GammaGuard.Dispose();
        }
    }

    /// <summary>Delays every save of an otherwise real repository (a slow disk).</summary>
    private sealed class SlowSaves(IMeasurementRepository inner, TimeSpan delay) : IMeasurementRepository
    {
        public string DatabasePath => inner.DatabasePath;
        public Task<MeasurementSession> ResolveSessionAsync(MeasurementSession candidate) => inner.ResolveSessionAsync(candidate);
        public async Task SaveAsync(MeasurementSession session, IReadOnlyList<MeasurementResult> rows, DepthProgress? progress)
        {
            await Task.Delay(delay);
            await inner.SaveAsync(session, rows, progress);
        }
        public Task<DepthProgress?> LoadProgressAsync(Guid sessionId) => inner.LoadProgressAsync(sessionId);
        public Task DeleteAsync(Guid sessionId, int captureNumber) => inner.DeleteAsync(sessionId, captureNumber);
        public Task<MeasurementSession?> FindSessionAsync(string drillholeId) => inner.FindSessionAsync(drillholeId);
        public Task<IReadOnlyList<MeasurementResult>> LoadAsync(Guid sessionId) => inner.LoadAsync(sessionId);
        public Task<IReadOnlyList<MeasuredDrillhole>> LoadMeasuredDrillholesAsync() => inner.LoadMeasuredDrillholesAsync();
    }

    private static CaptureData DeviceCapture(ControlledMeasurements d, int count, int firstId, double? doseRate = 0.1) =>
        Samples.Capture(d.LastStart, d.LastDuration, 5, Enumerable.Range(1, count)
            .Select(i => Samples.At(firstId + i, d.LastStart.ToUnixTimeMilliseconds() + i * 1000, count0: 10 * i, doseRate: doseRate)).ToList());

    [Test("INT-007", "One capture: averaged row shown, stored with its samples and progress, identical after reload; vendor file untouched")]
    public static async Task CaptureToStorage()
    {
        using var folder = new TestFolder();
        using var pipeline = new Pipeline(folder);
        for (var i = 1; i <= 5; i++)
        {
            pipeline.GammaGuard.Sample(Samples.Serial, T + i * 1000, count0: 10 * i, doseRate: 0.1 * i, temperature: i == 3 ? null : 20 + i);
        }

        var vendorHash = TestFolder.Sha256(pipeline.GammaGuard.Path);
        await pipeline.Arm();
        await pipeline.Click();

        var row = pipeline.ViewModel.Results.Single();
        Assert.Equal(5, row.SampleCount, "samples");
        Assert.Near(30, row.Reading.Count0, 1e-12, "count0 mean");
        Assert.Near(0.3, row.Reading.DoseRate, 1e-12, "dose_rate mean");
        Assert.Near(23, row.Temperature, 1e-12, "temperature mean of the 4 valid values");
        Assert.Equal(T + 1000, row.Reading.TsMs, "ts_ms of the first sample");
        Assert.Equal("10.25", pipeline.ViewModel.NextFrom, "next Starting Depth");

        var path = pipeline.Repository.DatabasePath;
        await Assert.Eventually(() => AppDatabase.Count(path, "MeasurementRecords") == 1, "record stored");
        Assert.Sequence(new[] { "10000|10250|250|5000|5|SN-1001|CT007-M-0001" },
            AppDatabase.Rows(path, "SELECT \"from\", \"to\", step, duration_ms, expected_samples, device_serial, device_name FROM MeasurementRecords"),
            "stored record");
        Assert.Equal(5L, AppDatabase.Count(path, "CaptureSamples"), "raw samples stored");
        Assert.Sequence(new[] { "1025|25" }, AppDatabase.Rows(path, "SELECT next_from_cm, step_cm FROM MeasurementProgress"), "progress");

        using var reopened = Make.Measurement(new SqliteMeasurementRepository(path));
        reopened.SetCore(Make.Core);
        await Assert.Eventually(() => reopened.Results.Count == 1, "reloaded");
        Assert.Equal(row.Reading, reopened.Results[0].Reading, "same values after reload");
        Assert.Equal("10.25", reopened.NextFrom, "same next depth after reload");
        Assert.Equal(vendorHash, TestFolder.Sha256(pipeline.GammaGuard.Path), "Gamma Guard file unchanged");
    }

    [Test("INT-008", "Captures while Gamma Guard writes 1 sample/s (WAL): complete windows, writer never blocked (UR-15)",
        Category = "Timing", TimeoutSeconds = 180)]
    public static async Task ConcurrentWriter()
    {
        using var folder = new TestFolder();
        using var pipeline = new Pipeline(folder, SystemCaptureClock.Instance, wal: true);
        pipeline.GammaGuard.Sample(Samples.Serial, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        using var stop = new CancellationTokenSource();
        var written = 1;
        var writerErrors = 0;
        var writer = Task.Run(async () =>
        {
            using var connection = new SqliteConnection($"Data Source={pipeline.GammaGuard.Path};Pooling=False;Default Timeout=2");
            connection.Open();

            while (!stop.IsCancellationRequested)
            {
                try
                {
                    using var command = connection.CreateCommand();
                    command.CommandText = "INSERT INTO samples (serial, session_id, seq, ts_ms, count0) VALUES ($s, 41, $q, $t, 12)";
                    command.Parameters.AddWithValue("$s", Samples.Serial);
                    command.Parameters.AddWithValue("$q", written);
                    command.Parameters.AddWithValue("$t", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                    command.ExecuteNonQuery();
                    Interlocked.Increment(ref written);
                }
                catch (SqliteException)
                {
                    Interlocked.Increment(ref writerErrors);
                }

                await Task.Delay(1000);
            }
        });

        await pipeline.Arm();
        for (var capture = 1; capture <= 10; capture++)
        {
            var click = DateTimeOffset.Now;
            pipeline.ViewModel.TriggerCommand.Execute(click);
            await Assert.Eventually(() => pipeline.ViewModel.State == MeasurementState.Ready, $"capture {capture} finished", 10_000);
            var row = pipeline.ViewModel.Results.Single(r => r.CaptureNumber == capture);
            Assert.True(row.SampleCount is >= 4 and <= 5, $"capture {capture}: {row.SampleCount} of 5 samples");
            // Saving runs after the page is Ready again (serial persistence queue): wait for this capture's samples.
            IReadOnlyList<DeviceSample> samples = [];
            for (var wait = 0; wait < 100 && samples.Count != row.SampleCount; wait++)
            {
                await Task.Delay(50);
                samples = await pipeline.Repository.LoadSamplesAsync(pipeline.ViewModel.SessionId!.Value, capture);
            }

            Assert.Equal(row.SampleCount, samples.Count, $"capture {capture}: samples stored within 5 s");
            Assert.True(samples.All(s => s.TsMs > click.ToUnixTimeMilliseconds() && s.TsMs <= click.ToUnixTimeMilliseconds() + 5000),
                $"capture {capture}: samples inside the window");
        }

        stop.Cancel();
        await writer;
        Assert.Equal(0, writerErrors, "the writer was never blocked");
        Assert.Equal((long)written, AppDatabase.Count(pipeline.GammaGuard.Path, "samples"), "only the writer added rows");
        Assert.Equal(1L, AppDatabase.Count(pipeline.GammaGuard.Path, "detector_status"), "detector table untouched");
    }

    [Test("INT-009", "Gamma Guard locked or gone during a capture: error toast, no row, next capture works")]
    public static async Task GammaGuardUnavailable()
    {
        using var folder = new TestFolder();
        using var pipeline = new Pipeline(folder);

        // Samples for the window of the next click (the fake clock moves on with every completed capture).
        void NextWindow(FakeGammaGuard gg)
        {
            for (var i = 1; i <= 5; i++) gg.Sample(Samples.Serial, pipeline.Clock.NowMs + i * 1000);
        }

        NextWindow(pipeline.GammaGuard);
        await pipeline.Arm();

        pipeline.GammaGuard.Execute("BEGIN EXCLUSIVE");
        await pipeline.Click(10_000);
        Assert.Equal(0, pipeline.ViewModel.Results.Count, "locked: no row");
        Assert.Equal("10.00", pipeline.ViewModel.NextFrom, "locked: no depth advance");
        // The reason is not shown: a locked database ends in the generic message (observation, see test-implementation-status.md).
        Assert.True(pipeline.Toasts.Items.Any(t => t.Kind == ToastKind.Error), "locked: error toast");
        pipeline.GammaGuard.Execute("ROLLBACK");

        await pipeline.Click();
        Assert.Equal(1, pipeline.ViewModel.Results.Count, "after release: capture works");

        pipeline.GammaGuard.Dispose();
        SqliteConnection.ClearAllPools();
        var moved = pipeline.GammaGuard.Path + ".away";
        File.Move(pipeline.GammaGuard.Path, moved);
        pipeline.Toasts.Items.Clear();
        await pipeline.Click();
        Assert.Equal(1, pipeline.ViewModel.Results.Count, "missing: no new row");
        Assert.True(pipeline.Toasts.Items.Any(t => t.Kind == ToastKind.Error), "missing: error toast");

        File.Move(moved, pipeline.GammaGuard.Path);
        using (var restored = new FakeGammaGuard(pipeline.GammaGuard.Path))
        {
            NextWindow(restored);
        }

        await pipeline.Devices.CheckGammaGuardDatabaseAsync(rediscover: true);
        await pipeline.Click();
        Assert.Equal(2, pipeline.ViewModel.Results.Count, "restored: capture works");
    }

    [Test("INT-010", "App database read-only during captures: rows kept with an error; saved exactly once afterwards")]
    public static async Task AppDatabaseFailureAndRecovery()
    {
        using var folder = new TestFolder();
        var repository = new SqliteMeasurementRepository(folder.Path("m.db"));
        var device = new ControlledMeasurements();
        var toasts = new ToastService();
        using var vm = Make.Measurement(repository, device, toasts: toasts);
        await Make.Arm(vm, Make.Core);
        await Assert.Eventually(() => File.Exists(repository.DatabasePath), "database created");

        File.SetAttributes(repository.DatabasePath, FileAttributes.ReadOnly);
        SqliteConnection.ClearAllPools();
        await Make.Capture(vm, device);
        await Make.Capture(vm, device);
        await Assert.Eventually(() => Make.ToastedStart(toasts, ToastKind.Error, "Saving to the database failed ("), "save error shown");
        Assert.Equal(2, vm.Results.Count, "rows kept in the table");

        File.SetAttributes(repository.DatabasePath, FileAttributes.Normal);
        SqliteConnection.ClearAllPools();
        await Make.Capture(vm, device);

        await Assert.Eventually(() => AppDatabase.Count(repository.DatabasePath, "MeasurementRecords") == 3, "all rows stored");
        Assert.Sequence(new[] { "1", "2", "3" }, AppDatabase.Rows(repository.DatabasePath, "SELECT CaptureNo FROM MeasurementRecords ORDER BY CaptureNo"),
            "each capture once");
        Assert.Equal(new DepthProgress(10_750, 250), await repository.LoadProgressAsync(vm.SessionId!.Value), "progress of the last capture");
    }

    [Test("INT-011", "Reopening a measured drillhole restores sorted rows, depth inputs and numbering")]
    public static async Task ReopenMeasuredDrillhole()
    {
        using var folder = new TestFolder();
        var repository = new SqliteMeasurementRepository(folder.Path("m.db"));
        var session = await repository.ResolveSessionAsync(Make.Session("DH-001"));
        await repository.SaveAsync(session, [Make.Row(session.Id, 1, 10_500), Make.Row(session.Id, 2, 10_000), Make.Row(session.Id, 3, 10_250)],
            new DepthProgress(10_750, 250));

        var device = new ControlledMeasurements();
        using var vm = Make.Measurement(repository, device);
        vm.SetCore(Make.Core);
        await Assert.Eventually(() => vm.Results.Count == 3, "stored rows shown");

        Assert.Sequence(new long?[] { 10_000, 10_250, 10_500 }, vm.Results.Select(r => r.FromMm), "sorted by From");
        Assert.Sequence(new[] { 1, 2, 3 }, vm.Results.Select(r => r.Number), "numbered");
        Assert.Equal("10.75", vm.NextFrom, "Starting Depth from progress");
        Assert.Equal("25", vm.Step, "Step from progress (cm)");

        vm.ToggleMeasurementCommand.Execute(null);
        await Make.Capture(vm, device);
        var added = vm.Results.Single(r => r.CaptureNumber == 4);
        Assert.Equal(10_750L, added.FromMm, "next capture continues the session");

        var form = Make.CoreForm(new StubBackend(), repository.DatabasePath, repository, out _);
        await form.LoadDrillholesAsync();
        await form.RefreshMeasuredAsync();
        Assert.Sequence(new[] { "Measured drillholes", "DH-001" }, form.DrillholeOptions.Take(2).Select(o => o.IsHeader ? o.Header : o.HoleId),
            "listed first under Measured drillholes");
    }

    [Test("INT-012", "Switching drillholes during a slow save never mixes sessions")]
    public static async Task NoSessionMixing()
    {
        using var folder = new TestFolder();
        var real = new SqliteMeasurementRepository(folder.Path("m.db"));
        var device = new ControlledMeasurements();
        using var vm = Make.Measurement(new SlowSaves(real, TimeSpan.FromMilliseconds(300)), device);
        await Make.Arm(vm, Make.Core);
        await Make.Capture(vm, device);
        vm.ToggleMeasurementCommand.Execute(null);

        var second = Make.Core with { DrillholeId = "DH-002" };
        await Make.Arm(vm, second, startDepth: "50.00");
        await Make.Capture(vm, device);
        await Assert.Eventually(() => AppDatabase.Count(real.DatabasePath, "MeasurementRecords") == 2, "both rows stored", 5000);

        var first = (await real.FindSessionAsync("DH-001"))!;
        var other = (await real.FindSessionAsync("DH-002"))!;
        Assert.Sequence(new long?[] { 10_000 }, (await real.LoadAsync(first.Id)).Select(r => r.FromMm), "DH-001 rows");
        Assert.Sequence(new long?[] { 50_000 }, (await real.LoadAsync(other.Id)).Select(r => r.FromMm), "DH-002 rows");
        Assert.Equal(new DepthProgress(10_250, 250), await real.LoadProgressAsync(first.Id), "DH-001 progress");
        Assert.Equal(new DepthProgress(50_250, 250), await real.LoadProgressAsync(other.Id), "DH-002 progress");
        var csv = CsvExportService.Serialize(await real.LoadAsync(other.Id), other);
        Assert.False(csv.Contains(first.Id.ToString(), StringComparison.Ordinal), "DH-002 export has no DH-001 row");
    }

    [Test("INT-013", "Delete → stored record and samples gone → reload and export show the rest; numbers not reused")]
    public static async Task DeleteReloadExport()
    {
        using var folder = new TestFolder();
        var repository = new SqliteMeasurementRepository(folder.Path("m.db"));
        var device = new ControlledMeasurements();
        var toasts = new ToastService();
        using var vm = Make.Measurement(repository, device, toasts: toasts);
        await Make.Arm(vm, Make.Core);
        for (var i = 0; i < 3; i++)
        {
            await Make.Capture(vm, device, d => d.CompleteWith(DeviceCapture(d, 5, i * 100)));
        }

        await Assert.Eventually(() => AppDatabase.Count(repository.DatabasePath, "CaptureSamples") == 15, "three captures stored");
        vm.DeleteCommand.Execute(vm.Results.Single(r => r.CaptureNumber == 2));
        await Assert.Eventually(() => Make.Toasted(toasts, ToastKind.Success, "Measurement deleted."), "deleted");

        var session = vm.SessionId!.Value;
        var reloaded = await new SqliteMeasurementRepository(repository.DatabasePath).LoadAsync(session);
        Assert.Sequence(new[] { 1, 3 }, reloaded.Select(r => r.CaptureNumber).Order(), "stored records");
        Assert.Equal(10L, AppDatabase.Count(repository.DatabasePath, "CaptureSamples"), "samples of the deleted capture removed");
        Assert.Equal(0, (await repository.LoadSamplesAsync(session, 2)).Count, "no samples for capture 2");
        Assert.Sequence(new[] { 1, 2 }, vm.Results.Select(r => r.Number), "renumbered");

        await Make.Capture(vm, device);
        Assert.Equal(4, vm.Results.Max(r => r.CaptureNumber), "next Capture No");
        var csvRows = CsvExportService.Serialize(reloaded, (await repository.FindSessionAsync("DH-001"))!).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, csvRows.Length, "export after reload: header + 2 rows");
    }

    [Test("INT-014", "Displayed, stored and exported values agree for complete, incomplete and null-field rows (UR-17)")]
    public static async Task ValuesAgreeAcrossLayers()
    {
        using var folder = new TestFolder();
        var repository = new SqliteMeasurementRepository(folder.Path("m.db"));
        var device = new ControlledMeasurements();
        using var vm = Make.Measurement(repository, device);
        await Make.Arm(vm, Make.Core);
        await Make.Capture(vm, device, d => d.CompleteWith(DeviceCapture(d, 5, 0, doseRate: 0.123456789)));
        await Make.Capture(vm, device, d => d.CompleteWith(DeviceCapture(d, 3, 100, doseRate: 1.0 / 3)));
        await Make.Capture(vm, device, d => d.CompleteWith(DeviceCapture(d, 5, 200, doseRate: null)));
        await Assert.Eventually(() => AppDatabase.Count(repository.DatabasePath, "MeasurementRecords") == 3, "stored");

        var session = (await repository.FindSessionAsync("DH-001"))!;
        var stored = (await new SqliteMeasurementRepository(repository.DatabasePath).LoadAsync(session.Id)).OrderBy(r => r.CaptureNumber).ToList();
        var csv = CsvExportService.Serialize(stored, session).Split("\r\n");
        var header = csv[0].Split(',');
        string Cell(int row, string column) => csv[row + 1].Split(',')[Array.IndexOf(header, $"\"{column}\"")].Trim('"');

        for (var i = 0; i < 3; i++)
        {
            var shown = vm.Results.Single(r => r.CaptureNumber == i + 1);
            Assert.Equal(shown.Reading, stored[i].Reading, $"row {i + 1}: stored = captured");
            Assert.Equal(MeasurementCellFormat.Decimal(shown.Reading.DoseRate), MeasurementCellFormat.Decimal(stored[i].Reading.DoseRate),
                $"row {i + 1}: displayed dose_rate");
            Assert.Equal(stored[i].Reading.DoseRate?.ToString("R", CultureInfo.InvariantCulture) ?? "", Cell(i, "dose_rate"), $"row {i + 1}: CSV dose_rate");
            Assert.Equal(stored[i].SampleCount.ToString(CultureInfo.InvariantCulture), Cell(i, "Sample"), $"row {i + 1}: CSV Sample");
            Assert.Equal(shown.SampleDisplay, stored[i].SampleDisplay, $"row {i + 1}: Sample column after reload");
        }

        Assert.Equal("3 of 5", stored[1].SampleDisplay, "incomplete row");
        Assert.Equal("", Cell(2, "dose_rate"), "null field exported empty");
        Assert.True(MeasurementCellFormat.Decimal(stored[2].Reading.DoseRate) is null, "null field shown empty");
    }

    [Test("INT-015", "Device Status and capture agree: no Gamma Guard database → refused; found after Retry → capture works")]
    public static async Task StatusAndCaptureAgree()
    {
        using var folder = new TestFolder();
        var root = folder.Path("roots");
        Directory.CreateDirectory(root);
        var settings = DeviceSettings.Default;
        using var devices = new DeviceStatusMonitor(new Ct007BluetoothCheck(settings, () => []), new MeasurementDatabaseCheck(folder.Path("m.db")),
            new GammaGuardDatabaseCheck(settings, new[] { (root, 2) }), TimeSpan.FromMinutes(5));
        var clock = new FakeCaptureClock(T);
        var toasts = new ToastService();
        using var vm = new MeasurementViewModel(new DeviceCaptureService(new GammaGuardCaptureDevices(devices), clock), new FakeExport(), new FakeDialogs(),
            new SqliteMeasurementRepository(folder.Path("m.db")), false, toasts, new CaptureDurationSettings(null));
        await devices.CheckGammaGuardDatabaseAsync();
        Assert.Equal(LocalState.NotFound, devices.GammaGuardDatabase.State, "status row: not found");
        await Make.Arm(vm, Make.Core);

        vm.TriggerCommand.Execute(DateTimeOffset.FromUnixTimeMilliseconds(T));
        await Assert.Eventually(() => vm.State == MeasurementState.Ready, "refused capture finished");
        Assert.True(Make.Toasted(toasts, ToastKind.Error, "The Gamma Guard database is not available (Not found)."), "refusal names the status");
        Assert.Equal(0, vm.Results.Count, "no row");

        Directory.CreateDirectory(Path.Combine(root, "GammaGuard", "Database"));
        using (var gg = new FakeGammaGuard(Path.Combine(root, "GammaGuard", "Database", "gg.sqlite")))
        {
            for (var i = 1; i <= 5; i++) gg.Sample(Samples.Serial, T + i * 1000);
        }

        await devices.CheckGammaGuardDatabaseAsync(rediscover: true);
        Assert.Equal(LocalState.Available, devices.GammaGuardDatabase.State, "status row after Retry");
        vm.TriggerCommand.Execute(DateTimeOffset.FromUnixTimeMilliseconds(T));
        await Assert.Eventually(() => vm.State == MeasurementState.Ready && vm.Results.Count == 1, "capture after Retry");
        Assert.Equal(5, vm.Results[0].SampleCount, "samples");
    }

    [Test("INT-016", "Measurement DB status follows a real exclusive lock on the app database", Category = "Timing")]
    public static async Task DatabaseStatusUnderLock()
    {
        using var folder = new TestFolder();
        var repository = new SqliteMeasurementRepository(folder.Path("m.db"));
        await repository.ResolveSessionAsync(Make.Session());
        SqliteConnection.ClearAllPools();
        using var devices = Make.Monitor(repository.DatabasePath, folder.Path("gg.sqlite"));
        using var backendApi = new ApiClient(ApiSettings.Default, new StubBackend());
        using var backend = new BackendConnection(backendApi, TimeSpan.FromMinutes(5));
        var toasts = new ToastService();
        using var announcements = new StatusAnnouncements(backend, devices, toasts);
        await devices.CheckMeasurementDatabaseAsync();
        Assert.Equal(LocalState.Available, devices.MeasurementDatabase.State, "available");

        // WAL lets readers in during a normal write transaction; another program holding the file exclusively does not.
        var holder = new SqliteConnection($"Data Source={repository.DatabasePath};Pooling=False");
        holder.Open();
        using (var command = holder.CreateCommand())
        {
            command.CommandText = "PRAGMA locking_mode = EXCLUSIVE; BEGIN EXCLUSIVE; UPDATE MeasurementSessions SET comment = comment;";
            command.ExecuteNonQuery();
        }

        await devices.CheckMeasurementDatabaseAsync();
        Assert.Equal(LocalState.Busy, devices.MeasurementDatabase.State, "busy while held");
        Assert.True(Make.Toasted(toasts, ToastKind.Warning, "Measurement database: busy."), "busy announced");

        holder.Dispose();
        await devices.CheckMeasurementDatabaseAsync();
        Assert.Equal(LocalState.Available, devices.MeasurementDatabase.State, "available after release");
        Assert.True(Make.Toasted(toasts, ToastKind.Information, "Measurement database available."), "recovery announced");
    }

    [Test("INT-017", "A schema-4 database is upgraded on start-up; its drillhole continues with the converted depth")]
    public static async Task UpgradeThenMeasure()
    {
        using var folder = new TestFolder();
        var path = folder.Path("m.db");
        AppDatabase.Legacy(path, 4);
        var device = new ControlledMeasurements();
        var backend = new StubBackend { DrillholesJson = """[{"holeId":"DH-A","projectCode":"North Ridge","campaign":"2024"}]""" };
        using var main = Make.Shell(new SqliteMeasurementRepository(path), backend, device, folder.Path("gg.sqlite"));
        await main.Core.LoadDrillholesAsync();

        await Make.Pick(main.Core, "DH-A");
        main.Core.SaveCommand.Execute(null);
        await Assert.Eventually(() => main.Measurement.Results.Count == 2, "migrated rows shown");

        Assert.Equal(5L, AppDatabase.UserVersion(path), "upgraded");
        Assert.Equal("10.25", main.Measurement.NextFrom, "Starting Depth from the converted progress");
        Assert.Equal("25", main.Measurement.Step, "Step from the converted progress");

        main.Measurement.ToggleMeasurementCommand.Execute(null);
        await Make.Capture(main.Measurement, device);
        var added = main.Measurement.Results.Single(r => r.CaptureNumber == 3);
        Assert.Equal(10_250L, added.FromMm, "continues at the migrated next depth");
        await Assert.Eventually(() => AppDatabase.Count(path, "MeasurementRecords") == 4, "stored next to the migrated rows");
    }
}
