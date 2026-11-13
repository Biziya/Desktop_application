using Frontend.Config;
using Frontend.Pages.SavedData;

namespace Ct007.Tests.FrontendTests;

/// <summary>FE-RET: 30-day retention of measurements.db. FE-SAV: the Saved Data page's queries and filters.</summary>
public static class SavedDataAndRetentionTests
{
    private static readonly DateTimeOffset Today = new(2026, 11, 13, 12, 0, 0, TimeSpan.FromHours(8));

    private static DataRetentionSettings Retention(bool enabled = true, int days = 30) =>
        new() { Enabled = enabled, RetentionDays = days, CheckIntervalHours = 24 };

    // A measurements.db with one session per (drillhole, capture start), each with one record, and one raw sample per record.
    private static async Task<(string Path, Dictionary<string, Guid> Sessions)> Database(TestFolder folder, params (string Drillhole, DateTimeOffset Start, int Captures)[] sessions)
    {
        var path = folder.Path("measurements.db");
        var repository = new SqliteMeasurementRepository(path);
        var ids = new Dictionary<string, Guid>();

        foreach (var (drillhole, start, captures) in sessions)
        {
            var session = await repository.ResolveSessionAsync(Make.Session(drillhole) with { CreatedAt = start });
            var rows = Enumerable.Range(1, captures).Select(i => Make.Row(session.Id, i, i * 250L, start: start.AddMinutes(i))).ToList();
            await repository.SaveAsync(session, rows, new DepthProgress(captures * 250L + 250, 250));
            ids[drillhole] = session.Id;
        }

        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO CaptureSamples (record_id, source_id, serial, ts_ms, count0) SELECT id, id, 'SN-1', 1, 600 FROM MeasurementRecords";
        command.ExecuteNonQuery();
        return (path, ids);
    }

    [Test("FE-RET-01", "Sessions with no capture within the retention period are deleted with their records, raw samples and progress")]
    public static async Task DeletesOldSessions()
    {
        using var folder = new TestFolder();
        var (path, ids) = await Database(folder, ("OLD", Today.AddDays(-40), 3), ("NEW", Today.AddDays(-2), 2));

        var removed = new DataRetention(path, Retention(), () => Today).Run(keepSessionId: null);

        Assert.Equal(1, removed, "one session removed");
        Assert.Sequence(new[] { ids["NEW"].ToString("D") }, AppDatabase.Rows(path, "SELECT id FROM MeasurementSessions"), "the recent session stays");
        Assert.Equal(2L, AppDatabase.Count(path, "MeasurementRecords"), "only the recent records stay");
        Assert.Equal(2L, AppDatabase.Count(path, "CaptureSamples"), "raw samples of deleted records are gone");
        Assert.Equal(1L, AppDatabase.Count(path, "MeasurementProgress"), "progress of the deleted session is gone");
        Assert.Equal(0L, Convert.ToInt64(AppDatabase.Scalar(path, "SELECT COUNT(*) FROM pragma_foreign_key_check"), CultureInfo.InvariantCulture), "no orphans");
    }

    [Test("FE-RET-02", "A session is kept while any capture is recent; the open session is never deleted")]
    public static async Task KeepsRecentAndActiveSessions()
    {
        using var folder = new TestFolder();
        var (path, ids) = await Database(folder, ("MIXED", Today.AddDays(-40), 2), ("ACTIVE", Today.AddDays(-60), 1));

        // One recent capture in the old session.
        var recent = Today.AddDays(-1).ToString("O", CultureInfo.InvariantCulture);
        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE MeasurementRecords SET EndTime = $t WHERE session_id = $s AND CaptureNo = 2";
            command.Parameters.AddWithValue("$t", recent);
            command.Parameters.AddWithValue("$s", ids["MIXED"].ToString("D"));
            command.ExecuteNonQuery();
        }

        var removed = new DataRetention(path, Retention(), () => Today).Run(keepSessionId: ids["ACTIVE"]);

        Assert.Equal(0, removed, "nothing removed");
        Assert.Equal(3L, AppDatabase.Count(path, "MeasurementRecords"), "all records kept, including the old one of the mixed session");
    }

    [Test("FE-RET-03", "Retention off, or no database yet, changes nothing")]
    public static async Task DisabledOrMissing()
    {
        using var folder = new TestFolder();
        var (path, _) = await Database(folder, ("OLD", Today.AddDays(-400), 1));
        SqliteConnection.ClearAllPools();
        var hash = TestFolder.Sha256(path);

        Assert.Equal(0, new DataRetention(path, Retention(enabled: false), () => Today).Run(null), "disabled");
        Assert.Equal(hash, TestFolder.Sha256(path), "file unchanged");

        var missing = folder.Path("none.db");
        Assert.Equal(0, new DataRetention(missing, Retention(), () => Today).Run(null), "no database");
        Assert.False(File.Exists(missing), "no database created");
    }

    [Test("FE-RET-04", "data-retention.yaml: the shipped file loads (30 days); 0 days is rejected with file and key")]
    public static void RetentionYaml()
    {
        var shipped = DataRetentionSettings.Load(AppContext.BaseDirectory);
        Assert.True(shipped.Enabled, "enabled by default");
        Assert.Equal(30, shipped.RetentionDays, "30 days");

        using var folder = new TestFolder();
        Directory.CreateDirectory(folder.Path("Config"));
        File.WriteAllText(folder.Path(DataRetentionSettings.File), "enabled: true\nretentionDays: 0\ncheckIntervalHours: 24\n");
        var error = Assert.Throws<InvalidDataException>(() => DataRetentionSettings.Load(folder.Root), "0 days rejected");
        Assert.Contains("data-retention.yaml: retentionDays", error.Message, "names file and key");
    }

    [Test("FE-SAV-01", "Saved Data lists sessions with record counts, newest activity first; an absent database is empty")]
    public static async Task ListsSessions()
    {
        using var folder = new TestFolder();
        var (path, _) = await Database(folder, ("DH-OLD", Today.AddDays(-5), 2), ("DH-NEW", Today.AddDays(-1), 3));

        var sessions = new SavedDataQueries(path).LoadSessions();

        Assert.Sequence(new[] { "DH-NEW", "DH-OLD" }, sessions.Select(s => s.DrillholeId), "newest first");
        Assert.Sequence(new[] { 3, 2 }, sessions.Select(s => s.RecordCount), "record counts");
        Assert.Equal(0, new SavedDataQueries(folder.Path("none.db")).LoadSessions().Count, "no database: no sessions");
        Assert.False(File.Exists(folder.Path("none.db")), "not created");
    }

    [Test("FE-SAV-02", "Records are paged in depth order; raw samples load per record")]
    public static async Task PagesRecords()
    {
        using var folder = new TestFolder();
        var (path, ids) = await Database(folder, ("DH-1", Today, 5));
        var queries = new SavedDataQueries(path);

        var first = queries.LoadRecords(ids["DH-1"].ToString("D"), 0, 2);
        var last = queries.LoadRecords(ids["DH-1"].ToString("D"), 4, 2);

        Assert.Equal(5, first.Total, "total");
        Assert.Sequence(new[] { 1, 2 }, first.Rows.Select(r => r.CaptureNo), "first page");
        Assert.Sequence(new[] { 5 }, last.Rows.Select(r => r.CaptureNo), "last page");
        Assert.Equal("0.25 – 0.50", first.Rows[0].Interval, "depth shown in metres");
        Assert.Near(600, queries.LoadRawSamples(first.Rows[0].Id).Single().Count0, 0, "raw sample");
    }

    [Test("FE-SAV-03", "Search filters by drillhole, project or operator and keeps the selection while it still matches")]
    public static async Task SearchFilters()
    {
        using var folder = new TestFolder();
        var (path, _) = await Database(folder, ("GTZO_0001", Today, 1), ("GTZO_0002", Today, 1), ("KHAN_0001", Today, 1));
        var vm = new SavedDataViewModel(new SavedDataQueries(path), Retention().Description);

        await vm.RefreshAsync();
        Assert.Equal(3, vm.Sessions.Count, "all sessions");
        Assert.Contains("30 days", vm.RetentionNote, "retention note");

        vm.SelectedSession = vm.Sessions.Single(s => s.DrillholeId == "GTZO_0002");
        await Assert.Eventually(() => vm.Records.Count == 1, "records of the selected session load");

        vm.SearchText = "gtzo";
        Assert.Sequence(new[] { "GTZO_0001", "GTZO_0002" }, vm.Sessions.Select(s => s.DrillholeId).Order(), "case-insensitive search");
        Assert.Equal("GTZO_0002", vm.SelectedSession?.DrillholeId, "selection kept");

        vm.SearchText = "khan";
        Assert.Equal(null, vm.SelectedSession, "selection cleared when filtered out");
        Assert.Equal("1 of 3 sessions", vm.SessionsSummary, "summary");
    }
}
