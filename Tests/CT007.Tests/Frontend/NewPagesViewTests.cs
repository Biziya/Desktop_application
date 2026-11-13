using Frontend.Config;
using Frontend.Pages.Monitoring;
using Frontend.Pages.SavedData;

namespace Ct007.Tests.FrontendTests;

/// <summary>FE-UI: the Monitoring and Saved Data pages lay out with live data and without binding errors.</summary>
public static class NewPagesViewTests
{
    private const long Now = 1_760_600_000_000;

    [Test("FE-UI-01", "Monitoring page renders detectors, a warning and samples without binding errors")]
    public static async Task MonitoringPageRenders()
    {
        Wpf.EnsureResources();
        var now = Now + 6000;
        var vm = new MonitoringViewModel(
            () => @"C:\GammaGuard\CT008 1.9.0\Database\ct008-log.sqlite", MonitoringSettings.Default, () => { },
            read: (_, after, _) => new MonitorSnapshot(3, 12,
            [
                new DetectorStatusRow("MR-24017", "MicroR North", "00:1A:7D:DA:71:13", "3.4.2", 14, "s-88", "logging", 1, now, now),
                new DetectorStatusRow("MR-24018", "MicroR South", "00:1A:7D:DA:71:14", "3.4.2", 14, "s-89", "logging", 2, now, now),
                new DetectorStatusRow("MR-23990", null, null, null, 14, null, "connected", null, Now - 60_000, Now - 60_000)
            ],
            after is null
                ? [Samples.At(10, now, 182, "MR-24017", doseRate: 0.112, temperature: 21.4), Samples.At(12, now, 240, "MR-24018", doseRate: 0.151, temperature: 22.0)]
                : []),
            clock: () => now);

        await vm.PollOnceAsync();
        vm.Engine.StartSample("MR-24017", now);
        var errors = await Wpf.Render(new MonitoringPage { DataContext = vm }, "monitoring");

        Assert.True(vm.WarningText.Contains("MicroR South"), "warning names the detector");
        Assert.Sequence(Array.Empty<string>(), errors, "binding errors");
        vm.Dispose();

        var empty = new MonitoringViewModel(() => null, MonitoringSettings.Default, () => { }, read: (_, _, _) => throw new InvalidOperationException());
        await empty.PollOnceAsync();
        Assert.Sequence(Array.Empty<string>(), await Wpf.Render(new MonitoringPage { DataContext = empty }, "monitoring-empty"), "binding errors (empty)");
    }

    [Test("FE-UI-02", "Saved Data page renders sessions, records and raw samples without binding errors")]
    public static async Task SavedDataPageRenders()
    {
        Wpf.EnsureResources();
        using var folder = new TestFolder();
        var path = folder.Path("measurements.db");
        var repository = new SqliteMeasurementRepository(path);
        var start = DateTimeOffset.Now.AddHours(-3);

        foreach (var (drillhole, captures) in new[] { ("GTZO_0042_1", 12), ("GTZO_0043_1", 4), ("KHAN_0007_2", 7) })
        {
            var session = await repository.ResolveSessionAsync(Make.Session(drillhole) with { CreatedAt = start });
            await repository.SaveAsync(session, Enumerable.Range(1, captures).Select(i => Make.Row(session.Id, i, 1000 + i * 250L, start: start.AddMinutes(i))).ToList(), null);
        }

        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO CaptureSamples (record_id, source_id, serial, seq, ts_ms, count0, count0_raw, hdm, dose_rate, temperature)
                SELECT r.id, r.id * 10 + n.k, 'MR-24017', n.k, 1760600000000 + n.k * 1000, 600 + n.k, 610 + n.k, 1.2, 0.11, 21.5
                FROM MeasurementRecords r, (SELECT 1 AS k UNION SELECT 2 UNION SELECT 3 UNION SELECT 4 UNION SELECT 5) n
                """;
            command.ExecuteNonQuery();
        }

        var settings = new DataRetentionSettings { Enabled = true, RetentionDays = 30, CheckIntervalHours = 24 };
        var vm = new SavedDataViewModel(new SavedDataQueries(path), settings.Description);
        await vm.RefreshAsync();
        vm.SelectedSession = vm.Sessions.First();
        await Assert.Eventually(() => vm.Records.Count > 0, "records load");
        vm.SelectedRecord = vm.Records[1];
        await Assert.Eventually(() => vm.RawSamples.Count == 5, "raw samples load");

        var errors = await Wpf.Render(new SavedDataPage { DataContext = vm }, "saved-data");
        Assert.Sequence(Array.Empty<string>(), errors, "binding errors");
    }
}
