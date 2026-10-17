namespace Ct007.Tests.Fixtures;

/// <summary>Builders for the objects most tests need, wired like MainViewModel does, with fakes at the boundaries.</summary>
public static class Make
{
    public static readonly CoreSnapshot Core = new("DH-001", "North Ridge", "2024", "Ann", "");

    /// <summary>Device settings that never search the user's real folders: the Gamma Guard path is fixed.</summary>
    public static DeviceSettings Devices(string gammaGuardPath) => DeviceSettings.Default with { GammaGuardDatabasePath = gammaGuardPath };

    public static DeviceStatusMonitor Monitor(string measurementDatabase, string gammaGuardPath,
        Func<IReadOnlyList<KnownBluetoothDevice>>? bluetooth = null)
    {
        var settings = Devices(gammaGuardPath);
        return new DeviceStatusMonitor(new Ct007BluetoothCheck(settings, bluetooth ?? (() => [])),
            new MeasurementDatabaseCheck(measurementDatabase), new GammaGuardDatabaseCheck(settings, []), TimeSpan.FromMinutes(5));
    }

    public static MeasurementViewModel Measurement(IMeasurementRepository repository, IMeasurementService? service = null,
        FakeDialogs? dialogs = null, FakeExport? export = null, ToastService? toasts = null, string? durationPath = null,
        bool showDemoTsMs = false) =>
        new(service ?? new ControlledMeasurements(), export ?? new FakeExport(), dialogs ?? new FakeDialogs(), repository,
            showDemoTsMs, toasts ?? new ToastService(), new CaptureDurationSettings(durationPath));

    /// <summary>Save &amp; Continue equivalent for the Measurement page, then Start with the given depths.</summary>
    public static async Task Arm(MeasurementViewModel measurement, CoreSnapshot core, string startDepth = "10.00", string step = "25")
    {
        measurement.SetCore(core);
        await Assert.Eventually(() => measurement.SessionId is not null && measurement.ToggleMeasurementCommand.CanExecute(null),
            "drillhole session opened");
        measurement.NextFrom = startDepth;
        measurement.Step = step;
        measurement.ToggleMeasurementCommand.Execute(null);
        Assert.Equal(MeasurementState.Ready, measurement.State, "state after Start");
    }

    /// <summary>One right-click capture completed by <paramref name="complete"/> (default: 5 plain samples).</summary>
    public static async Task Capture(MeasurementViewModel measurement, ControlledMeasurements device, Action<ControlledMeasurements>? complete = null)
    {
        Assert.True(measurement.TriggerCommand.CanExecute(DateTimeOffset.Now), "trigger accepted");
        measurement.TriggerCommand.Execute(DateTimeOffset.Now);
        Assert.Equal(MeasurementState.Capturing, measurement.State, "state after trigger");
        (complete ?? (d => d.Complete()))(device);
        await Assert.Eventually(() => measurement.State == MeasurementState.Ready, "capture finished");
    }

    /// <summary>MainViewModel through its test constructor: stub HTTP, fake Bluetooth list, controlled capture.</summary>
    public static MainViewModel Shell(IMeasurementRepository repository, StubBackend backend, IMeasurementService capture,
        string gammaGuardPath, TimeSpan? statusInterval = null) =>
        new(repository, new ApiSettings("http://stub.test", TimeSpan.FromSeconds(5), statusInterval ?? TimeSpan.FromMinutes(5)),
            Devices(gammaGuardPath), backend, () => [], capture);

    /// <param name="handler">A <see cref="StubBackend"/>, or null for real HTTP to <paramref name="baseUrl"/> (in-process TestBackend).</param>
    public static CoreInformationViewModel CoreForm(HttpMessageHandler? handler, string databasePath, IMeasurementRepository history,
        out BackendConnection connection, TimeSpan? searchDelay = null, string baseUrl = "http://stub.test", double timeoutSeconds = 5)
    {
        var api = new ApiClient(new ApiSettings(baseUrl, TimeSpan.FromSeconds(timeoutSeconds), TimeSpan.FromMinutes(5)), handler);
        connection = new BackendConnection(api, TimeSpan.FromMinutes(5));
        return new CoreInformationViewModel(_ => { }, new DrillholesClient(api), connection, new DrillholeCache(databasePath),
            history, Monitor(databasePath, databasePath + ".gg"), searchDelay ?? TimeSpan.Zero);
    }

    /// <summary>Types into the Drillhole box, waits for the filter and presses Enter.</summary>
    public static async Task Pick(CoreInformationViewModel form, string text)
    {
        form.DrillholeSearch = text;
        await form.WhenOptionsUpdated();
        Assert.True(form.CommitClosest(), $"a closest match for '{text}' exists");
        await form.WhenOptionsUpdated();
    }

    public static string[] Ids(CoreInformationViewModel form) =>
        form.DrillholeOptions.Where(o => !o.IsHeader).Select(o => o.HoleId).ToArray();

    public static bool Toasted(ToastService toasts, ToastKind kind, string message) =>
        toasts.Items.Any(t => t.Kind == kind && t.Message == message);

    public static bool ToastedStart(ToastService toasts, ToastKind kind, string start) =>
        toasts.Items.Any(t => t.Kind == kind && t.Message.StartsWith(start, StringComparison.Ordinal));

    /// <summary>A stored-looking result row of <paramref name="session"/>.</summary>
    public static MeasurementResult Row(Guid session, int captureNumber, long? fromMm, long stepMm = 250, long? recordId = null,
        DateTimeOffset? start = null, DeviceReading? reading = null, int samples = 5, int? expected = 5)
    {
        var started = start ?? new DateTimeOffset(2026, 10, 16, 9, 0, 0, TimeSpan.FromHours(8)).AddMinutes(captureNumber);
        return new MeasurementResult
        {
            SessionId = session,
            CaptureNumber = captureNumber,
            RecordId = recordId,
            StartTime = started,
            EndTime = started.AddSeconds(5),
            SampleCount = samples,
            ExpectedSamples = expected,
            DurationMs = 5000,
            Reading = reading ?? new DeviceReading(1_760_600_000_000 + captureNumber, 10.5, 21, 1.25, 2.5, 0.125),
            FromMm = fromMm,
            ToMm = fromMm + stepMm,
            StepMm = fromMm is null ? null : stepMm
        };
    }

    public static MeasurementSession Session(string drillholeId = "DH-001", string? operatorName = "Ann", string? comment = "") =>
        new(Guid.NewGuid(), new DateTimeOffset(2026, 10, 16, 8, 0, 0, TimeSpan.FromHours(8)), drillholeId, "North Ridge", "2024",
            operatorName, comment);
}
