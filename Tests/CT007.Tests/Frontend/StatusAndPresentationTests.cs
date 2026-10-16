namespace Ct007.Tests.FrontendTests;

/// <summary>FE-041…FE-047: toasts, status announcements, Device Status page, settings, table formatting (F06–F09, F16).</summary>
public static class StatusAndPresentationTests
{
    private const ulong AddressA = 0x0011223344A1;
    private const ulong AddressB = 0x0011223344A2;
    private const ulong AddressX = 0x0011223344A3;

    private static void CreateSqlite(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE t (x INTEGER)";
            command.ExecuteNonQuery();
        }
    }

    [Test("FE-041", "Toasts: 5 s errors, 3 s others, duplicates restarted, at most four")]
    public static async Task ToastRules()
    {
        var delays = new List<(TimeSpan Duration, CancellationToken Token, TaskCompletionSource Expire)>();
        var toasts = new ToastService((duration, token) =>
        {
            var expire = new TaskCompletionSource();
            token.Register(() => expire.TrySetCanceled());
            delays.Add((duration, token, expire));
            return expire.Task;
        });

        toasts.Error("e");
        toasts.Warning("w");
        toasts.Success("s");
        toasts.Information("i");
        Assert.Sequence(new[] { 5.0, 3, 3, 3 }, delays.Select(d => d.Duration.TotalSeconds), "durations");

        delays[1].Expire.SetResult();
        await Assert.Eventually(() => toasts.Items.All(t => t.Message != "w"), "expired toast removed");

        toasts.Error("e");
        Assert.Equal(1, toasts.Items.Count(t => t.Message == "e"), "identical toast not duplicated");
        Assert.True(delays[0].Token.IsCancellationRequested, "first timer restarted");
        Assert.Equal(ToastService.ErrorDuration, delays[^1].Duration, "restarted with the full duration");

        toasts.Warning("a");
        toasts.Warning("b");
        Assert.Equal(4, toasts.Items.Count, "at most four visible");
        Assert.Sequence(new[] { "i", "e", "a", "b" }, toasts.Items.Select(t => t.Message), "oldest dismissed first");
    }

    [Test("FE-042", "Status announcements: transitions only, Online only after Offline")]
    public static async Task Announcements()
    {
        using var folder = new TestFolder();
        var db = folder.Path("m.db");
        var stub = new StubBackend { Offline = true };
        using var api = new ApiClient(new ApiSettings("http://stub.test", TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(5)), stub);
        using var connection = new BackendConnection(api, TimeSpan.FromMinutes(5));
        var reader = new FakeBluetoothReader();
        reader.Set(new KnownBluetoothDevice("CT007-M-0001", AddressA, Connected: true, Paired: true));
        var settings = Make.Devices(folder.Path("gg.sqlite"));
        using var devices = new DeviceStatusMonitor(new Ct007BluetoothCheck(settings, reader), new MeasurementDatabaseCheck(db),
            new GammaGuardDatabaseCheck(settings, []), TimeSpan.FromMinutes(5));
        var toasts = new ToastService();
        using var announcements = new StatusAnnouncements(connection, devices, toasts);

        await devices.CheckMeasurementDatabaseAsync();
        Assert.Equal(LocalState.Missing, devices.MeasurementDatabase.State, "first DB state");
        Assert.Equal(0, toasts.Items.Count, "first known DB state is not announced");

        await connection.CheckNowAsync();
        await connection.CheckNowAsync();
        Assert.Equal(1, toasts.Items.Count(t => t is { Kind: ToastKind.Warning, Message: "Backend offline. The saved drillhole list stays available." }),
            "Offline announced once");
        stub.Offline = false;
        await connection.CheckNowAsync();
        Assert.True(Make.Toasted(toasts, ToastKind.Success, "Backend online again."), "Online announced after Offline");

        CreateSqlite(db);
        await devices.CheckMeasurementDatabaseAsync();
        Assert.True(Make.Toasted(toasts, ToastKind.Information, "Measurement database available."), "DB available announced");
        File.Delete(db);
        await devices.CheckMeasurementDatabaseAsync();
        Assert.True(Make.Toasted(toasts, ToastKind.Warning, "Measurement database: file not found."), "DB missing announced");

        devices.SelectBluetoothDevice(new BluetoothDeviceOption(AddressA, "CT007-M-0001"));
        await Assert.Eventually(() => Make.Toasted(toasts, ToastKind.Success, "CT007-M-0001 (Bluetooth) connected."), "first device state");
        reader.Set(new KnownBluetoothDevice("CT007-M-0001", AddressA, Connected: false, Paired: true));
        await devices.CheckBluetoothAsync();
        Assert.True(Make.Toasted(toasts, ToastKind.Warning, "CT007-M-0001 (Bluetooth) disconnected."), "change announced");
        var count = toasts.Items.Count;
        await devices.CheckBluetoothAsync();
        Assert.Equal(count, toasts.Items.Count, "unchanged state not announced again");

        var quiet = new StubBackend();
        using var quietApi = new ApiClient(new ApiSettings("http://stub.test", TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(5)), quiet);
        using var quietConnection = new BackendConnection(quietApi, TimeSpan.FromMinutes(5));
        var quietToasts = new ToastService();
        using var quietAnnouncements = new StatusAnnouncements(quietConnection, devices, quietToasts);
        await quietConnection.CheckNowAsync();
        Assert.Equal(BackendState.Online, quietConnection.State, "online at start-up");
        Assert.False(quietToasts.Items.Any(t => t.Message.StartsWith("Backend", StringComparison.Ordinal)), "Online at start-up is not announced");
    }

    [Test("FE-043", "CT007-M identification, status mapping, no auto-select, stale results discarded")]
    public static async Task BluetoothSelection()
    {
        var reader = new FakeBluetoothReader();
        reader.Set(
            new KnownBluetoothDevice("CT007-M-0001", AddressA, Connected: true, Paired: true),
            new KnownBluetoothDevice("ct007-m-0002", AddressB, Connected: false, Paired: true),
            new KnownBluetoothDevice("CT007-X", AddressX, Connected: true, Paired: true),
            new KnownBluetoothDevice("CT007-M-0001", AddressA, Connected: false, Paired: true, LowEnergy: true));
        var check = new Ct007BluetoothCheck(DeviceSettings.Default, reader);

        var list = check.ListDevices();
        Assert.Sequence(new[] { "CT007-M-0001", "ct007-m-0002" }, list.Select(d => d.Name), "prefix match, de-duplicated by address");
        Assert.Equal(LocalState.Connected, check.CheckSelected(list[0]).State, "connected device");
        var paired = check.CheckSelected(list[1]);
        Assert.Equal(LocalState.Disconnected, paired.State, "paired only");
        Assert.Contains("paired with this laptop, not connected", paired.Detail, "paired detail");

        reader.Read = _ => throw new TimeoutException("Bluetooth did not answer");
        var failed = check.CheckSelected(list[0]);
        Assert.Equal(LocalState.Unavailable, failed.State, "timeout");
        Assert.Equal("Status unavailable", failed.Text, "timeout text");

        reader.Read = _ => new KnownBluetoothDevice("CT007-M-0001", AddressX, Connected: true, Paired: true);
        Assert.Equal(LocalState.Disconnected, check.CheckSelected(list[0]).State, "a device under the same ID but another address is not the selected one");
        reader.Read = null;

        // With a configured address, the address alone identifies the detector (the name prefix is not used).
        var byAddress = new Ct007BluetoothCheck(DeviceSettings.Default with { Ct007Address = "00:11:22:33:44:A3" }, reader);
        Assert.Sequence(new[] { "CT007-X" }, byAddress.ListDevices().Select(d => d.Name), "configured address");

        using var folder = new TestFolder();
        using var devices = new DeviceStatusMonitor(check, new MeasurementDatabaseCheck(folder.Path("m.db")),
            new GammaGuardDatabaseCheck(Make.Devices(folder.Path("gg.sqlite")), []), TimeSpan.FromMinutes(5));
        await devices.RefreshBluetoothDevicesAsync();
        Assert.Equal(2, devices.BluetoothDevices.Count, "listed");
        Assert.True(devices.SelectedBluetoothDevice is null, "never auto-selected");
        Assert.Equal(LocalState.NotSelected, devices.Bluetooth.State, "not selected");

        var releaseA = new ManualResetEventSlim();
        reader.Read = option =>
        {
            if (option.Address == AddressA)
            {
                releaseA.Wait(TimeSpan.FromSeconds(5));
                return new KnownBluetoothDevice("CT007-M-0001", AddressA, Connected: true, Paired: true);
            }

            return new KnownBluetoothDevice("ct007-m-0002", AddressB, Connected: false, Paired: true);
        };
        devices.SelectBluetoothDevice(devices.BluetoothDevices[0]);
        devices.SelectBluetoothDevice(devices.BluetoothDevices[1]);
        releaseA.Set();
        await Assert.Eventually(() => devices.Bluetooth.State == LocalState.Disconnected, "status of the device selected last");
        await Assert.Remains(() => devices.Bluetooth.State == LocalState.Disconnected && devices.Bluetooth.Detail.StartsWith("ct007-m-0002", StringComparison.Ordinal),
            "the earlier device's result is discarded");

        reader.Set(new KnownBluetoothDevice("CT007-M-0001", AddressA, Connected: true, Paired: true));
        await devices.RefreshBluetoothDevicesAsync();
        Assert.True(devices.BluetoothDevices.Any(d => d.Address == AddressB), "the selected device stays listed after a refresh");
        Assert.Equal(AddressB, devices.SelectedBluetoothDevice!.Address, "selection kept");
    }

    [Test("FE-044", "Database status rows: Missing without creating the file; Retry rediscovers Gamma Guard")]
    public static async Task DatabaseRowsAndRetry()
    {
        using var folder = new TestFolder();
        var measurementDb = folder.Path("m.db");
        var root = folder.Path("roots");
        Directory.CreateDirectory(root);
        var settings = DeviceSettings.Default;
        using var devices = new DeviceStatusMonitor(new Ct007BluetoothCheck(settings, () => []), new MeasurementDatabaseCheck(measurementDb),
            new GammaGuardDatabaseCheck(settings, new[] { (root, 2) }), TimeSpan.FromMinutes(5));

        await devices.CheckMeasurementDatabaseAsync();
        await devices.CheckGammaGuardDatabaseAsync();
        Assert.Equal(LocalState.Missing, devices.MeasurementDatabase.State, "measurement DB missing");
        Assert.False(File.Exists(measurementDb), "the check does not create the file");
        Assert.Equal(LocalState.NotFound, devices.GammaGuardDatabase.State, "Gamma Guard not found");
        Assert.True(devices.GammaGuardDatabase.CanRetry, "Retry offered");

        var gammaGuard = Path.Combine(root, "Gamma Guard", "Database", "gg.sqlite");
        CreateSqlite(gammaGuard);
        var hash = TestFolder.Sha256(gammaGuard);
        await devices.CheckGammaGuardDatabaseAsync(rediscover: true);

        Assert.Equal(LocalState.Available, devices.GammaGuardDatabase.State, "found after Retry");
        Assert.Equal(gammaGuard, devices.GammaGuardDatabase.Path, "path shown");
        Assert.Equal(hash, TestFolder.Sha256(gammaGuard), "the check does not modify the file");
    }

    [Test("FE-045", "DeviceSettings.Load: invalid values fall back; the Gamma Guard path expands variables at check time")]
    public static void DeviceSettingsLoad()
    {
        using var folder = new TestFolder();
        var defaults = DeviceSettings.Default;

        var cases = new (string Name, string? Json)[]
        {
            ("missing file", null),
            ("invalid JSON", "{"),
            ("negative interval", """{"checkIntervalSeconds":-1,"bluetoothCheckIntervalSeconds":0}"""),
            ("text interval", """{"checkIntervalSeconds":"abc"}"""),
            ("empty prefix and file mask", """{"ct007NamePrefix":" ","gammaGuardFileName":""}""")
        };

        foreach (var (name, json) in cases)
        {
            var path = folder.Path(Guid.NewGuid() + ".json");

            if (json is not null)
            {
                File.WriteAllText(path, json);
            }

            Assert.Equal(defaults, DeviceSettings.Load(path), name);
        }

        var configured = folder.Path("configured.json");
        File.WriteAllText(configured, """{"gammaGuardDatabasePath":"%CT007_TEST_GG_DIR%\\gg.sqlite","checkIntervalSeconds":2}""");
        var settings = DeviceSettings.Load(configured);
        Assert.Equal(TimeSpan.FromSeconds(2), settings.CheckInterval, "valid interval");

        Environment.SetEnvironmentVariable("CT007_TEST_GG_DIR", folder.Root);
        try
        {
            var status = new GammaGuardDatabaseCheck(settings, []).Check();
            Assert.Equal(Path.Combine(folder.Root, "gg.sqlite"), status.Path, "environment variable expanded");
            Assert.Equal(LocalState.Missing, status.State, "configured path used as-is even when missing");
        }
        finally
        {
            Environment.SetEnvironmentVariable("CT007_TEST_GG_DIR", null);
        }
    }

    [Test("FE-046", "Table formatting: at most two decimals, local timestamp, incomplete sample count")]
    public static void CellFormatting()
    {
        Assert.Equal("2", MeasurementCellFormat.Decimal(2.0), "whole number");
        Assert.Equal("123456.79", MeasurementCellFormat.Decimal(123456.789), "two decimals");
        Assert.Equal("0.13", MeasurementCellFormat.Decimal(0.125), "midpoint rounds away from zero (0.125 is exact in binary)");
        Assert.Equal("0", MeasurementCellFormat.Decimal(-0.001), "no negative zero");
        Assert.True(MeasurementCellFormat.Decimal(null) is null, "null stays empty");

        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
        try
        {
            Assert.Equal("1.5", MeasurementCellFormat.Decimal(1.5), "invariant decimal point under fr-FR");
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }

        const long ms = 1_760_600_000_000;
        var text = MeasurementCellFormat.UnixMillisecondsLocal(ms)!;
        var local = DateTime.ParseExact(text, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(ms - ms % 1000).UtcDateTime, local.ToUniversalTime(), "local time of the same instant");

        var incomplete = Make.Row(Guid.NewGuid(), 1, 0, samples: 3, expected: 5);
        Assert.Equal("3 of 5", incomplete.SampleDisplay, "incomplete capture");
        Assert.Equal("5", Make.Row(Guid.NewGuid(), 2, 0).SampleDisplay, "complete capture");
    }

    [Test("FE-047", "Every measurement column has a layout; column order is stable")]
    public static void ColumnLayouts()
    {
        Assert.Sequence(new[] { "No", "Capture No", "Start Time", "End Time", "From (m)", "To (m)", "Step (cm)", "Sample", "ts_ms", "count0",
                "count0_raw", "hdm", "hdm_raw", "dose_rate", "Session" },
            MeasurementColumns.All.Select(c => c.Label), "column order");

        foreach (var column in MeasurementColumns.All)
        {
            var layout = MeasurementColumnLayout.For(column);
            Assert.True(layout.MinWidth > 0, $"{column.Label}: minimum width");
        }

        Assert.Sequence(new[] { "Capture No", "Start Time", "End Time", "Session" },
            MeasurementColumns.All.Where(c => !MeasurementColumnLayout.For(c).Visible).Select(c => c.Label), "hidden columns");
    }
}
