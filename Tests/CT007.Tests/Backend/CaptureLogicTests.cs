namespace Ct007.Tests.BackendTests;

/// <summary>BE-020…BE-026: capture window, averaging, time basis, detector pick and the capture service (F15).</summary>
public static class CaptureLogicTests
{
    private const long T = 1_760_600_000_000;

    [Test("BE-020", "CaptureWindow: T < ts ≤ T+d, frozen serial, each source row once, duplicate readings ignored (UR-01)")]
    public static void WindowMembership()
    {
        var window = new CaptureWindow(Samples.Serial, T, 5000);
        Assert.Equal(T + 5000, window.EndMs, "window end");

        var cases = new (string Name, DeviceSample Row, bool Accepted)[]
        {
            ("ts = T (the click itself)", Samples.At(1, T), false),
            ("ts = T+1", Samples.At(2, T + 1), true),
            ("ts = T+5000", Samples.At(3, T + 5000), true),
            ("ts = T+5001", Samples.At(4, T + 5001), false),
            ("ts = T-1", Samples.At(5, T - 1), false),
            ("serial in another case", Samples.At(6, T + 100, serial: "sn-1001"), false),
            ("other serial", Samples.At(7, T + 100, serial: "SN-2002"), false),
            ("null ts", Samples.At(8, null), false),
            ("same source id again", Samples.At(2, T + 2), false),
            ("reading with session A, seq 7", Samples.At(9, T + 1500, session: "A", seq: 7), true),
            ("same session/seq/ts, new id (duplicate write)", Samples.At(10, T + 1500, session: "A", seq: 7), false),
            ("same seq/ts in another device session", Samples.At(11, T + 1500, session: "B", seq: 7), true),
            ("no seq, same ts, new id", Samples.At(12, T + 1500, seq: null), true)
        };

        // One row per Add, in order: later rows are checked against the rows accepted before them.
        foreach (var (name, row, accepted) in cases)
        {
            Assert.Equal(accepted ? 1 : 0, window.Add([row]), name);
        }

        Assert.Equal(5, window.Count, "accepted rows");
        Assert.Sequence(new long[] { 2, 9, 11, 12, 3 }, window.Accepted.Select(s => s.SourceId), "accepted in ts, then id order");
    }

    [Test("BE-021", "Expected samples: one per whole second, at least one (UR-02)")]
    public static void ExpectedSamples()
    {
        foreach (var (ms, expected) in new[] { (2000L, 2), (5000L, 5), (10_000L, 10), (2500L, 2), (999L, 1), (0L, 1) })
        {
            Assert.Equal(expected, new CaptureWindow("S", T, ms).ExpectedSamples, $"{ms} ms");
        }
    }

    [Test("BE-022", "SampleAveraging: finite values only, nulls never zero, first ts, uniform device session")]
    public static void Averaging()
    {
        var summary = SampleAveraging.Summarize(new[]
        {
            Samples.At(5, T + 2000, count0: 10, doseRate: 0.1, temperature: null),
            Samples.At(3, T + 1000, count0: 20, doseRate: 0.2, temperature: double.NaN),
            Samples.At(4, T + 1000, count0: null, temperature: double.PositiveInfinity),
            Samples.At(6, null, count0: double.NaN)
        });

        Assert.Equal(4, summary.SampleCount, "every accepted sample counts");
        Assert.Near(15, summary.Count0, 1e-12, "count0 mean over 10 and 20");
        Assert.Near(0.15, summary.DoseRate, 1e-12, "dose_rate mean of 0.1 and 0.2");
        Assert.True(summary.Temperature is null, "no finite temperature → null");
        Assert.True(summary.Hdm is null, "field never provided → null");
        Assert.Equal(T + 1000, summary.FirstTsMs, "earliest ts (tie → lower id)");
        Assert.Equal("41", summary.DeviceSessionId, "uniform device session kept");

        var mixed = SampleAveraging.Summarize(new[] { Samples.At(1, T, session: "A"), Samples.At(2, T + 1, session: "B") });
        Assert.True(mixed.DeviceSessionId is null, "mixed device sessions → null");

        var empty = SampleAveraging.Summarize([]);
        Assert.Equal(0, empty.SampleCount, "empty: count");
        Assert.True(empty.Count0 is null && empty.FirstTsMs is null && empty.DeviceSessionId is null, "empty: all null");
    }

    [Test("BE-023", "DeviceTime: Unix milliseconds within 12 h of the laptop clock (UR-10)")]
    public static void TimeBasis()
    {
        const long now = T;
        const long twelveHours = 43_200_000;
        Assert.Contains("is not in Unix milliseconds", DeviceTime.Problem(99_999_999_999, now), "below 1e11");
        Assert.Contains("is not in Unix milliseconds", DeviceTime.Problem(1_760_600_000, now), "seconds");
        Assert.Contains("away from this laptop's clock", DeviceTime.Problem(100_000_000_000, now), "1e11 but decades off");
        Assert.True(DeviceTime.Problem(now - twelveHours, now) is null, "exactly 12 h accepted");
        Assert.Contains("12 h away from this laptop's clock", DeviceTime.Problem(now - twelveHours - 1, now), "12 h + 1 ms behind");
        Assert.True(DeviceTime.Problem(now + twelveHours + 1, now) is not null, "12 h + 1 ms ahead");
        Assert.True(DeviceTime.Problem(now, now) is null, "same clock");
    }

    [Test("BE-024", "Detector pick: newest serial; refused when none or two within 5 s (UR-10)")]
    public static void DetectorPick()
    {
        var none = Assert.Throws<CaptureException>(() => GammaGuardCaptureDevices.Pick([]), "no rows");
        Assert.Equal("The Gamma Guard database has no samples yet. Check that Gamma Guard is recording the detector.", none.Message, "no rows message");
        Assert.Equal("S1", GammaGuardCaptureDevices.Pick([new RecentSerial("S1", T)]), "one serial");
        Assert.Equal("S1", GammaGuardCaptureDevices.Pick([new RecentSerial("S2", T - 5001), new RecentSerial("S1", T)]), "other serial 5001 ms older");

        var two = Assert.Throws<CaptureException>(() => GammaGuardCaptureDevices.Pick([new RecentSerial("S2", T - 5000), new RecentSerial("S1", T)]),
            "exactly 5 s apart");
        Assert.Equal("More than one detector is recording (S1, S2). Stop the others to measure.", two.Message, "simultaneous message");

        var three = Assert.Throws<CaptureException>(() => GammaGuardCaptureDevices.Pick(
            [new RecentSerial("S3", T - 60_000), new RecentSerial("S2", T - 100), new RecentSerial("S1", T)]), "three serials");
        Assert.Equal("More than one detector is recording (S1, S2). Stop the others to measure.", three.Message, "only the simultaneous ones are named");
    }

    [Test("BE-024", "Resolve refuses when the Gamma Guard database is not Available")]
    public static async Task ResolveNeedsAvailableDatabase()
    {
        using var folder = new TestFolder();
        using var devices = Make.Monitor(folder.Path("m.db"), folder.Path("missing.sqlite"));
        await devices.CheckGammaGuardDatabaseAsync();

        var error = Assert.Throws<CaptureException>(() => new GammaGuardCaptureDevices(devices).Resolve(), "missing database");
        Assert.Contains("The Gamma Guard database is not available (", error.Message, "message");
    }

    private static (DeviceCaptureService Service, ScriptedSampleSource Source, FakeCaptureClock Clock, List<TimeSpan> Reports) Service()
    {
        var clock = new FakeCaptureClock(T);
        var source = new ScriptedSampleSource(clock);
        return (new DeviceCaptureService(new FixedDevice(new CaptureDevice(Samples.Detector, source)), clock), source, clock, []);
    }

    private static Task<CaptureData> Run(DeviceCaptureService service, List<TimeSpan> reports, CancellationToken token = default) =>
        service.CaptureAsync(DateTimeOffset.FromUnixTimeMilliseconds(T), TimeSpan.FromSeconds(5), reports.Add, token);

    [Test("BE-025", "Capture service: window samples, 250 ms polls, bounded final read, lookback, empty start, other serials")]
    public static async Task PollingAndFinalRead()
    {
        // a) one sample per second, written on time
        var (service, source, clock, reports) = Service();
        for (var i = 1; i <= 5; i++) source.Add(Samples.At(i, T + i * 1000), T + i * 1000);
        var data = await Run(service, reports);
        Assert.Equal(5, data.DeviceSamples!.Count, "a: samples");
        Assert.Equal(5, data.ExpectedSamples, "a: expected");
        Assert.Equal(TimeSpan.FromSeconds(5), reports[0], "a: first report is the full duration");
        Assert.True(reports.Zip(reports.Skip(1)).All(p => p.Second <= p.First), "a: countdown never increases");
        Assert.Equal(TimeSpan.Zero, reports[^1], "a: ends at 0");
        Assert.True(clock.Delays.Where(d => d.AtMs < T + 5000).All(d => d.Delay == TimeSpan.FromMilliseconds(250)), "a: 250 ms polling");
        Assert.True(clock.Delays.All(d => d.AtMs < T + 5000), "d: no final-read wait once every expected sample is in");

        // b) the last sample is written 1.2 s late: still inside the 1.5 s final read
        (service, source, clock, reports) = Service();
        for (var i = 1; i <= 4; i++) source.Add(Samples.At(i, T + i * 1000), T + i * 1000);
        source.Add(Samples.At(5, T + 5000), T + 6200);
        Assert.Equal(5, (await Run(service, reports)).DeviceSamples!.Count, "b: late row within the final read is included");

        // c) written 2 s late: outside the final read, the window does not grow
        (service, source, clock, reports) = Service();
        for (var i = 1; i <= 4; i++) source.Add(Samples.At(i, T + i * 1000), T + i * 1000);
        source.Add(Samples.At(5, T + 5000), T + 7000);
        var late = await Run(service, reports);
        Assert.Equal(4, late.DeviceSamples!.Count, "c: too late");
        Assert.True(clock.NowMs <= T + 5000 + 1500 + 250, "c: final read bounded");

        // e) a row written just before the click (id below the baseline, within the 200-row lookback) with ts after the click
        (service, source, clock, reports) = Service();
        for (var id = 1; id <= 300; id++) if (id != 250) source.Add(Samples.At(id, T - 10_000, serial: "OTHER"), 0);
        source.Add(Samples.At(250, T + 500), 0);
        var lookback = await Run(service, reports);
        Assert.True(lookback.DeviceSamples!.Any(s => s.SourceId == 250), "e: lookback row included");

        // f) empty table at the click
        (service, source, clock, reports) = Service();
        for (var i = 1; i <= 5; i++) source.Add(Samples.At(i, T + i * 1000), T + i * 1000);
        Assert.Equal(5, (await Run(service, reports)).DeviceSamples!.Count, "f: rows written after an empty start");

        // g) 500 rows of another detector interleaved
        (service, source, clock, reports) = Service();
        for (var i = 1; i <= 500; i++) source.Add(Samples.At(1000 + i, T + i * 9, serial: "OTHER"), T + i * 9);
        for (var i = 1; i <= 5; i++) source.Add(Samples.At(i, T + i * 1000), T + i * 1000);
        var mixed = await Run(service, reports);
        Assert.True(mixed.DeviceSamples!.Count == 5 && mixed.DeviceSamples.All(s => s.Serial == Samples.Serial), "g: only the frozen detector");
    }

    [Test("BE-026", "Capture service: time-basis refusal before reading, cancellation ends without a result (UR-29)")]
    public static async Task TimeCheckAndCancellation()
    {
        var (service, source, _, reports) = Service();
        source.LatestOverride = () => 1_760_600_000;
        var refused = await Assert.ThrowsAsync<CaptureException>(() => Run(service, reports), "seconds-based ts_ms");
        Assert.Contains("is not in Unix milliseconds", refused.Message, "message");
        Assert.Equal(0, source.ReadCalls, "refused before any sample read");

        // No sample of this detector yet: there is nothing to check, the capture proceeds (UR-29).
        (service, source, _, reports) = Service();
        source.LatestOverride = () => null;
        await Run(service, reports);
        Assert.True(source.ReadCalls > 0, "capture proceeds without a time check");

        FakeCaptureClock clock;
        (service, source, clock, reports) = Service();
        using var cancel = new CancellationTokenSource();
        clock.Advanced = now => { if (now >= T + 2000) cancel.Cancel(); };
        await Assert.ThrowsAsync<OperationCanceledException>(() => Run(service, reports, cancel.Token), "cancelled during the window");
        var reads = source.ReadCalls;
        await Task.Delay(100);
        Assert.Equal(reads, source.ReadCalls, "no reads after cancellation");
    }
}
