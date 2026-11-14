using Frontend.Pages.Monitoring;

namespace Ct007.Tests.FrontendTests;

/// <summary>FE-MON: the Monitoring page (CT008 Monitor port), one test per rule.</summary>
public static class MonitoringTests
{
    private const long Now = 1_760_600_000_000;
    private const string A = "SN-A";
    private const string B = "SN-B";

    private static MonitorEngine Engine() => new(MonitoringSettings.Default);

    private static DetectorStatusRow Status(string serial, string state = "logging", long? updated = Now, long? lastSample = Now,
        long? responseTime = 1, int? type = 14) =>
        new(serial, $"Meter {serial}", "00:11", "1.0", type, "s1", state, responseTime, lastSample, updated);

    private static MonitorSnapshot Snapshot(long maxId, IReadOnlyList<DeviceSample> samples, params DetectorStatusRow[] detectors) =>
        new(3, maxId, detectors, samples);

    [Test("FE-MON-01", "First read shows each detector's newest reading; later reads process only rows after the cursor, in id order")]
    public static void CursorReadsOnlyNewRows()
    {
        var engine = Engine();
        engine.Apply(Snapshot(6, [Samples.At(5, Now, 50, A), Samples.At(6, Now, 60, B)], Status(A), Status(B)), Now);

        Assert.Equal(6L, engine.Cursor, "cursor starts at the newest id");
        Assert.Equal(2, engine.Detectors.Count, "both detectors shown");
        Assert.Near(50, engine.Detectors.Single(d => d.Serial == A).Latest?.Count0, 0, "A's newest reading");
        Assert.Equal(0, engine.RecentReadings.Count, "the seed rows are not 'recent readings'");

        engine.Apply(Snapshot(8, [Samples.At(7, Now + 1000, 70, A), Samples.At(8, Now + 1000, 80, B)], Status(A), Status(B)), Now + 1000);

        Assert.Equal(8L, engine.Cursor, "cursor follows the last processed id");
        Assert.Sequence(new long[] { 8, 7 }, engine.RecentReadings.Select(r => r.Id), "recent readings, newest first");
        Assert.Near(70, engine.Detectors.Single(d => d.Serial == A).Latest?.Count0, 0, "A updated in place");
    }

    [Test("FE-MON-02", "A recreated database (ids went backwards) resets the cursor and cancels running samples")]
    public static void RecreatedDatabaseResetsCursor()
    {
        var engine = Engine();
        engine.Apply(Snapshot(100, [], Status(A)), Now);
        engine.StartSample(A, Now);

        engine.Apply(Snapshot(3, [], Status(A)), Now + 1000);

        Assert.Equal(3L, engine.Cursor, "cursor follows the new file");
        Assert.False(engine.HasRunningSamples, "running sample cancelled");
        Assert.Contains("Cancelled", engine.Detectors.Single().SampleStatus, "the row says why");
    }

    [Test("FE-MON-03", "State rules: Stale after the heartbeat limit (except disconnected), No data when logging without readings")]
    public static void StateRules()
    {
        var engine = Engine();
        var stale = Now - 6000;
        engine.Apply(Snapshot(0, [],
            Status("live"),
            Status("stale", updated: stale),
            Status("gone", state: "disconnected", updated: stale),
            Status("quiet", lastSample: stale),
            Status("idle", state: "connected")), Now);

        string StateOf(string serial) => engine.Detectors.Single(d => d.Serial == serial).StateText;
        Assert.Equal("Logging", StateOf("live"), "fresh heartbeat and readings");
        Assert.Equal("Stale", StateOf("stale"), "heartbeat older than 5 s");
        Assert.Equal("Disconnected", StateOf("gone"), "disconnected is never stale");
        Assert.Equal("No data", StateOf("quiet"), "logging but no reading for 5 s");
        Assert.Equal("Connected", StateOf("idle"), "other states are shown as written");
        Assert.Equal(StatusTone.Danger, engine.Detectors.Single(d => d.Serial == "stale").Tone, "stale is red");
    }

    [Test("FE-MON-04", "A 5-second sample discards the first reading after the request and averages the next 5")]
    public static void SampleDiscardsFirstAndAveragesFive()
    {
        var engine = Engine();
        engine.Apply(Snapshot(0, [], Status(A)), Now);
        Assert.True(engine.StartSample(A, Now), "sample started");
        Assert.False(engine.StartSample(A, Now), "a second request while running is ignored");

        // A reading from before the request is not part of the sample at all.
        engine.Apply(Snapshot(1, [Samples.At(1, Now - 200, 999, A)], Status(A)), Now);
        engine.Apply(Snapshot(2, [Samples.At(2, Now + 400, 1000, A)], Status(A)), Now + 400);
        Assert.Equal("Collecting 0 of 5", engine.Detectors.Single().SampleStatus, "anchor taken, nothing averaged yet");

        var readings = Enumerable.Range(1, 5).Select(i => Samples.At(2 + i, Now + 400 + i * 1000, i * 10, A)).ToList();
        engine.Apply(Snapshot(7, readings, Status(A)), Now + 5400);

        var result = engine.Samples.Single();
        Assert.True(result.IsComplete, "5 of 5 readings");
        Assert.Near(30, result.Cpm, 1e-12, "mean of 10..50; the discarded 1000 and the earlier 999 are not included");
        Assert.Near(5.0, result.CoverageSeconds, 1e-9, "coverage runs from the discarded reading to the last one");
        Assert.True(engine.Detectors.Single().CanSample, "the detector can be sampled again");
    }

    [Test("FE-MON-05", "A sample that does not get 5 readings within 12 s ends incomplete")]
    public static void SampleTimesOut()
    {
        var engine = Engine();
        engine.Apply(Snapshot(0, [], Status(A)), Now);
        engine.StartSample(A, Now);
        engine.Apply(Snapshot(3, [Samples.At(1, Now + 100, 1, A), Samples.At(2, Now + 1100, 20, A), Samples.At(3, Now + 2100, 40, A)], Status(A)), Now + 2100);

        engine.Tick(Now + MonitorEngine.SampleTimeoutMs);

        var result = engine.Samples.Single();
        Assert.False(result.IsComplete, "incomplete");
        Assert.Equal(2, result.Readings, "two readings after the discarded one");
        Assert.Near(30, result.Cpm, 1e-12, "mean of what arrived");
        Assert.Contains("Incomplete", engine.Detectors.Single().SampleStatus, "row says so");
    }

    [Test("FE-MON-06", "Response time warning: only live detectors whose response time is known and not 1 s")]
    public static void ResponseTimeWarning()
    {
        var engine = Engine();
        engine.Apply(Snapshot(0, [],
            Status("ok"),
            Status("slow", responseTime: 2),
            Status("auto", responseTime: 0),
            Status("unknown", responseTime: null),
            Status("off", state: "disconnected", responseTime: 3),
            Status("dead", updated: Now - 60_000, responseTime: 3)), Now);

        Assert.Sequence(new[] { "Meter slow (2 s)", "Meter auto (Auto)" }, engine.ResponseTimeWarnings, "warned detectors");
        Assert.Equal("–", engine.Detectors.Single(d => d.Serial == "unknown").ResponseTimeText, "not reported yet");
    }

    [Test("FE-MON-07", "The monitor reads the CT008 database without changing it and never creates a missing file")]
    public static void ReaderIsReadOnly()
    {
        using var folder = new TestFolder();
        var path = folder.Path("ct008-log.sqlite");

        using (var gg = new FakeGammaGuard(path))
        {
            gg.Detector(A, "Meter A", "00:11", Now);
            gg.Detector(B, "Meter B", "00:12", Now);
            for (var i = 1; i <= 4; i++) gg.Sample(i % 2 == 0 ? A : B, Now + i * 1000, count0: i);
        }

        SqliteConnection.ClearAllPools();
        var hash = TestFolder.Sha256(path);
        var reader = new GammaGuardDeviceDatabase(path);

        var first = reader.ReadMonitorSnapshot(null, 100);
        Assert.Equal(4L, first.MaxSampleId, "max id");
        Assert.Sequence(new long[] { 3, 4 }, first.Samples.Select(s => s.SourceId), "newest sample per detector");
        Assert.Equal(2, first.Detectors.Count, "detector rows");
        Assert.Equal(null, first.Detectors[0].DeviceType, "a non-numeric device_type reads as unknown");

        var next = reader.ReadMonitorSnapshot(2, 100);
        Assert.Sequence(new long[] { 3, 4 }, next.Samples.Select(s => s.SourceId), "rows after the cursor, in id order");

        SqliteConnection.ClearAllPools();
        Assert.Equal(hash, TestFolder.Sha256(path), "bytes unchanged");

        var missing = folder.Path("missing.sqlite");
        Assert.Throws<SqliteException>(() => new GammaGuardDeviceDatabase(missing).ReadMonitorSnapshot(null, 10), "missing file is an error");
        Assert.False(File.Exists(missing), "missing file not created");
    }

    [Test("FE-MON-08", "Page states: no database, connected after a poll, read error kept visible and retried")]
    public static async Task ViewModelStates()
    {
        string? path = null;
        var fail = false;
        var shown = false;
        var vm = new MonitoringViewModel(
            () => path, MonitoringSettings.Default, () => shown = true,
            read: (_, after, _) => fail
                ? throw new InvalidOperationException("database is locked.")
                : new MonitorSnapshot(3, 5, [Status(A)], after is null ? [Samples.At(5, Now, 10, A)] : []),
            clock: () => Now);

        await vm.PollOnceAsync();
        Assert.Equal(MonitoringViewModel.ConnectionState.NoDatabase, vm.Connection, "no database yet");
        Assert.False(vm.HasDatabase, "empty state shown");
        vm.ShowCoreInformationCommand.Execute(null);
        Assert.True(shown, "empty state links to Core Information");

        path = "C:/ct008-log.sqlite";
        await vm.PollOnceAsync();
        Assert.Equal(MonitoringViewModel.ConnectionState.Connected, vm.Connection, "connected");
        Assert.Equal("", vm.ErrorText, "no error text while connected");
        Assert.Equal(1, vm.Engine.Detectors.Count, "detector shown");

        fail = true;
        await vm.PollOnceAsync();
        Assert.Equal(MonitoringViewModel.ConnectionState.Error, vm.Connection, "error state");
        Assert.Contains("database is locked", vm.ErrorText, "error message shown");
        Assert.Equal(1, vm.Engine.Detectors.Count, "rows kept while retrying");

        fail = false;
        await vm.PollOnceAsync();
        Assert.Equal(MonitoringViewModel.ConnectionState.Connected, vm.Connection, "recovers on the next poll");
        vm.Dispose();
    }

    [Test("FE-MON-09", "Monitoring polls only while shown or while a sample runs")]
    public static async Task PollsOnlyWhenNeeded()
    {
        var reads = 0;
        var settings = new MonitoringSettings { PollIntervalMs = 20, StaleAfterMs = 5000, RecentReadingsShown = 50, SamplesShown = 100 };
        var vm = new MonitoringViewModel(() => "C:/db.sqlite", settings, () => { },
            read: (_, _, _) => { reads++; return new MonitorSnapshot(3, 0, [Status(A)], []); },
            clock: () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        await Task.Delay(100);
        Assert.Equal(0, reads, "nothing read before the page is shown");

        vm.OnShown();
        await Assert.Eventually(() => reads >= 3, "polls while shown");
        vm.OnHidden();
        await Assert.Eventually(() => !vm.IsPolling, "stops after hiding");
        var stopped = reads;
        await Task.Delay(100);
        Assert.Equal(stopped, reads, "no reads while hidden");
        vm.Dispose();
    }
}
