namespace Ct007.Tests.BackendTests;

/// <summary>BE-031…BE-041, BE-046: SQLite app database — sessions, atomic saves, delete, precision, migrations, cache (F02, F11, F17, F18).</summary>
public static class MeasurementRepositoryTests
{
    private static MeasurementResult WithSamples(MeasurementResult row, int count) => new()
    {
        SessionId = row.SessionId,
        CaptureNumber = row.CaptureNumber,
        StartTime = row.StartTime,
        EndTime = row.EndTime,
        SampleCount = count,
        ExpectedSamples = row.ExpectedSamples,
        Reading = row.Reading,
        FromMm = row.FromMm,
        ToMm = row.ToMm,
        StepMm = row.StepMm,
        Samples = Enumerable.Range(1, count).Select(i => Samples.At(100 * row.CaptureNumber + i, 1_760_600_000_000 + i * 1000)).ToList()
    };

    [Test("BE-031", "Sessions: one per Drillhole ID, stored metadata wins, lookups never create, concurrent resolves agree")]
    public static async Task SessionResolution()
    {
        using var folder = new TestFolder();
        var path = folder.Path("m.db");
        var repository = new SqliteMeasurementRepository(path);

        var first = await repository.ResolveSessionAsync(Make.Session("DH-1", operatorName: "Ann"));
        var second = await repository.ResolveSessionAsync(Make.Session("DH-1", operatorName: "Bob"));
        Assert.Equal(first.Id, second.Id, "same session");
        Assert.Equal("Ann", second.Operator, "stored metadata kept");
        Assert.True(await repository.FindSessionAsync("DH-9") is null, "unknown drillhole");
        Assert.Equal(1L, AppDatabase.Count(path, "MeasurementSessions"), "lookups create nothing");
        await Assert.ThrowsAsync<ArgumentException>(() => repository.ResolveSessionAsync(Make.Session(" ")), "a session needs a Drillhole ID");

        var other = new SqliteMeasurementRepository(path);
        var resolved = await Task.WhenAll(Enumerable.Range(0, 10).Select(i =>
            (i % 2 == 0 ? repository : other).ResolveSessionAsync(Make.Session("DH-2", operatorName: $"op{i}"))));
        Assert.Equal(1, resolved.Select(s => s.Id).Distinct().Count(), "parallel resolves return one session");
        Assert.Equal(1L, Convert.ToInt64(AppDatabase.Scalar(path, "SELECT COUNT(*) FROM MeasurementSessions WHERE DrillholeID = 'DH-2'")), "one row");
    }

    [Test("BE-032", "Save: record, samples and progress commit together; re-save changes only the order; failure leaves nothing")]
    public static async Task AtomicSave()
    {
        using var folder = new TestFolder();
        var path = folder.Path("m.db");
        var repository = new SqliteMeasurementRepository(path);
        var session = await repository.ResolveSessionAsync(Make.Session());
        var row = WithSamples(Make.Row(session.Id, 1, 10_000), 5);

        await repository.SaveAsync(session, [row], new DepthProgress(10_250, 250));
        Assert.Equal(1L, AppDatabase.Count(path, "MeasurementRecords"), "a: record");
        Assert.Equal(5L, AppDatabase.Count(path, "CaptureSamples"), "a: samples");
        Assert.Sequence(new[] { "1025|25" }, AppDatabase.Rows(path, "SELECT next_from_cm, step_cm FROM MeasurementProgress"), "a: progress in cm");

        var earlier = Make.Row(session.Id, 2, 9_750);
        var changed = WithSamples(Make.Row(session.Id, 1, 10_000, reading: new DeviceReading(1, 999, 999, 999, 999, 999)), 5);
        await repository.SaveAsync(session, [earlier, changed], null);
        var stored = AppDatabase.Rows(path, "SELECT CaptureNo, SortOrder, Count0 FROM MeasurementRecords ORDER BY CaptureNo");
        Assert.Sequence(new[] { "1|2|10.5", "2|1|10.5" }, stored, "b: only SortOrder of the stored row changed");
        Assert.Equal(5L, AppDatabase.Count(path, "CaptureSamples"), "b: samples not duplicated");

        var foreign = Make.Row(Guid.NewGuid(), 9, 0);
        await Assert.ThrowsAsync<ArgumentException>(
            () => repository.SaveAsync(session, [WithSamples(Make.Row(session.Id, 3, 10_250), 3), foreign], new DepthProgress(99_000, 500)),
            "c: a row of another session");
        Assert.Equal(2L, AppDatabase.Count(path, "MeasurementRecords"), "c: no record of the failed save");
        Assert.Equal(5L, AppDatabase.Count(path, "CaptureSamples"), "c: no samples of the failed save");
        Assert.Equal(new DepthProgress(10_250, 250), await repository.LoadProgressAsync(session.Id), "c: progress unchanged");
    }

    [Test("BE-033", "Session isolation; a second row with a stored Capture No never replaces the stored one")]
    public static async Task IsolationAndUniqueCaptureNumbers()
    {
        using var folder = new TestFolder();
        var path = folder.Path("m.db");
        var repository = new SqliteMeasurementRepository(path);
        var a = await repository.ResolveSessionAsync(Make.Session("DH-A"));
        var b = await repository.ResolveSessionAsync(Make.Session("DH-B"));
        await repository.SaveAsync(a, [Make.Row(a.Id, 1, 1_000)], null);
        await repository.SaveAsync(b, [Make.Row(b.Id, 1, 2_000), Make.Row(b.Id, 2, 2_250)], null);

        await Assert.ThrowsAsync<ArgumentException>(() => repository.SaveAsync(a, [Make.Row(b.Id, 3, 0)], null), "row of session B saved into A");
        await repository.SaveAsync(a, [Make.Row(a.Id, 1, 5_000, reading: new DeviceReading(Count0: 1))], null);

        var rowsA = await repository.LoadAsync(a.Id);
        Assert.Equal(1, rowsA.Count, "session A rows");
        Assert.Equal(1_000L, rowsA[0].FromMm, "stored row unchanged");
        Assert.True((await repository.LoadAsync(b.Id)).All(r => r.SessionId == b.Id) && (await repository.LoadAsync(b.Id)).Count == 2, "session B rows only");
    }

    [Test("BE-034", "Delete removes the record and its samples only")]
    public static async Task Delete()
    {
        using var folder = new TestFolder();
        var path = folder.Path("m.db");
        var repository = new SqliteMeasurementRepository(path);
        var a = await repository.ResolveSessionAsync(Make.Session("DH-A"));
        var b = await repository.ResolveSessionAsync(Make.Session("DH-B"));
        await repository.SaveAsync(a, Enumerable.Range(1, 3).Select(i => WithSamples(Make.Row(a.Id, i, i * 250), 2)).ToList(), null);
        await repository.SaveAsync(b, [WithSamples(Make.Row(b.Id, 2, 0), 2)], null);

        await repository.DeleteAsync(a.Id, 2);

        Assert.Sequence(new[] { 1, 3 }, (await repository.LoadAsync(a.Id)).Select(r => r.CaptureNumber).Order(), "remaining A rows");
        Assert.Equal(0, (await repository.LoadSamplesAsync(a.Id, 2)).Count, "samples of the deleted capture");
        Assert.Equal(2, (await repository.LoadSamplesAsync(a.Id, 3)).Count, "other samples kept");
        Assert.Equal(1, (await repository.LoadAsync(b.Id)).Count, "session B untouched (same Capture No)");
        Assert.Equal(2, (await repository.LoadSamplesAsync(b.Id, 2)).Count, "session B samples untouched");
        Assert.Equal(0, AppDatabase.Rows(path, "PRAGMA foreign_key_check").Count, "no orphans");

        await repository.DeleteAsync(a.Id, 42);
        Assert.Equal(2, (await repository.LoadAsync(a.Id)).Count, "deleting a missing capture changes nothing");
    }

    [Test("BE-035", "Round trip: doubles, offsets, millimetres and cm progress survive a new repository instance")]
    public static async Task RoundTripPrecision()
    {
        using var folder = new TestFolder();
        var path = folder.Path("m.db");
        var writer = new SqliteMeasurementRepository(path);
        var session = await writer.ResolveSessionAsync(Make.Session());
        var times = new[] { TimeSpan.FromHours(8), TimeSpan.FromHours(-5), TimeSpan.Zero };
        var rows = times.Select((offset, i) => new MeasurementResult
        {
            SessionId = session.Id,
            CaptureNumber = i + 1,
            StartTime = new DateTimeOffset(2026, 10, 16, 9, 0, 0, offset).AddTicks(1234567),
            EndTime = new DateTimeOffset(2026, 10, 16, 9, 0, 5, offset),
            SampleCount = 5,
            ExpectedSamples = 5,
            DurationMs = 5000,
            Reading = new DeviceReading(1_760_600_000_123, 0.1, 1e-300, 123456.789012345, null, double.Epsilon),
            Temperature = 21.123456789,
            Pressure = null,
            FromMm = 12_345 + i,
            ToMm = 12_370 + i,
            StepMm = 25,
            DeviceSerial = "SN-1001"
        }).ToList();

        await writer.SaveAsync(session, rows, new DepthProgress(12_345, 25));
        SqliteConnection.ClearAllPools();
        var reader = new SqliteMeasurementRepository(path);
        var loaded = (await reader.LoadAsync(session.Id)).OrderBy(r => r.CaptureNumber).ToList();

        for (var i = 0; i < rows.Count; i++)
        {
            Assert.Equal(rows[i].StartTime, loaded[i].StartTime, $"row {i}: start instant");
            Assert.Equal(rows[i].StartTime.Offset, loaded[i].StartTime.Offset, $"row {i}: offset kept");
            Assert.Equal(rows[i].Reading, loaded[i].Reading, $"row {i}: device values bit-identical");
            Assert.Equal(rows[i].Temperature, loaded[i].Temperature, $"row {i}: temperature");
            Assert.True(loaded[i].Pressure is null, $"row {i}: null stays null");
            Assert.Equal(rows[i].FromMm, loaded[i].FromMm, $"row {i}: From mm");
            Assert.Equal(rows[i].ToMm, loaded[i].ToMm, $"row {i}: To mm");
        }

        foreach (var (next, step) in new[] { (12_345L, 25L), (1L, 1L), (999_999_999L, 3L) })
        {
            await writer.SaveAsync(session, rows, new DepthProgress(next, step));
            Assert.Equal(new DepthProgress(next, step), await reader.LoadProgressAsync(session.Id), $"progress {next}/{step} mm");
        }
    }

    [Test("BE-036", "Migrations from schema 1, 2, 3 and 4 keep every row and convert progress mm → cm")]
    public static async Task Migrations()
    {
        foreach (var version in new[] { 1, 2, 3, 4 })
        {
            using var folder = new TestFolder();
            var path = folder.Path($"v{version}.db");
            AppDatabase.Legacy(path, version);
            var repository = new SqliteMeasurementRepository(path);

            var rowsA = await repository.LoadAsync(AppDatabase.SessionA);
            var rowsB = await repository.LoadAsync(AppDatabase.SessionB);

            Assert.Equal(5L, AppDatabase.UserVersion(path), $"v{version}: schema version");
            Assert.Equal(2, rowsA.Count, $"v{version}: session A rows");
            Assert.Equal(1, rowsB.Count, $"v{version}: session B rows");
            var first = rowsA.Single(r => r.CaptureNumber == 1);
            Assert.Equal(new DeviceReading(1_767_229_200_000, 10.5, 21, 1.25, 2.5, 0.125), first.Reading, $"v{version}: values kept");
            Assert.Equal(new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.FromHours(8)), first.StartTime, $"v{version}: time kept");
            Assert.Sequence(new[] { 2, 1 }, rowsA.Select(r => r.CaptureNumber), $"v{version}: stored order kept");
            Assert.True(rowsA.Single(r => r.CaptureNumber == 2).Reading.Count0 is null, $"v{version}: nulls kept");
            Assert.Equal("wal", AppDatabase.Scalar(path, "PRAGMA journal_mode") as string, $"v{version}: WAL");
            Assert.Equal(0, AppDatabase.Rows(path, "PRAGMA foreign_key_check").Count, $"v{version}: foreign keys");
            Assert.True(AppDatabase.HasColumn(path, "MeasurementProgress", "next_from_cm") && AppDatabase.HasColumn(path, "CaptureSamples", "record_id"),
                $"v{version}: v5 tables");

            if (version == 4)
            {
                Assert.Equal(new DepthProgress(10_250, 250), await repository.LoadProgressAsync(AppDatabase.SessionA), "v4: progress converted once");
                Assert.Sequence(new[] { "1025|25" }, AppDatabase.Rows(path, "SELECT next_from_cm, step_cm FROM MeasurementProgress"), "v4: stored in cm");
                Assert.Equal("Ann", (await repository.FindSessionAsync("DH-A"))!.Operator, "v4: metadata kept");
                Assert.Equal(10_250L, first.ToMm, "v4: depths kept");
            }
            else
            {
                Assert.True(first.FromMm is null && (await repository.FindSessionAsync("DH-A")) is null, $"v{version}: no depths or IDs invented");
            }
        }
    }

    [Test("BE-037", "Migration refuses duplicates and newer files, and rolls back completely on failure")]
    public static async Task MigrationRefusals()
    {
        using var folder = new TestFolder();

        // Duplicate Drillhole IDs can only exist in a version 3 file changed outside the app (version 4 adds the unique index).
        var duplicates = folder.Path("duplicates.db");
        AppDatabase.Legacy(duplicates, 3);
        AppDatabase.Execute(duplicates, "ALTER TABLE MeasurementSessions ADD COLUMN DrillholeID TEXT; UPDATE MeasurementSessions SET DrillholeID = 'DH-9';");
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => new SqliteMeasurementRepository(duplicates).LoadAsync(AppDatabase.SessionA),
            "duplicate Drillhole IDs");
        Assert.Contains("more than one session per Drillhole ID: DH-9 (2 sessions)", refused.Message, "duplicates named");
        Assert.Equal(3L, AppDatabase.UserVersion(duplicates), "duplicates: version unchanged");
        Assert.False(AppDatabase.HasColumn(duplicates, "MeasurementSessions", "DrillholeName"), "duplicates: columns added by the step rolled back");
        Assert.Equal(0L, AppDatabase.Count(duplicates, "sqlite_master WHERE name = 'MeasurementProgress'"), "duplicates: no table added");
        Assert.Equal(3L, AppDatabase.Count(duplicates, "MeasurementRecords"), "duplicates: rows unchanged");

        var newer = folder.Path("newer.db");
        AppDatabase.Execute(newer, "CREATE TABLE future (x INTEGER); INSERT INTO future VALUES (1); PRAGMA user_version = 6;");
        var future = await Assert.ThrowsAsync<InvalidOperationException>(() => new SqliteMeasurementRepository(newer).LoadAsync(Guid.NewGuid()),
            "newer schema");
        Assert.Contains("newer version of the application (schema 6)", future.Message, "newer: message");
        Assert.Equal(6L, AppDatabase.UserVersion(newer), "newer: version unchanged");
        Assert.Equal("future:CREATE TABLE future (x INTEGER)", AppDatabase.Schema(newer), "newer: schema unchanged");

        // A record pointing at no session fails the final foreign-key check: the whole upgrade is rolled back.
        var orphan = folder.Path("orphan.db");
        AppDatabase.Legacy(orphan, 3);
        AppDatabase.Execute(orphan, """
            INSERT INTO MeasurementRecords (session_id, SortOrder, CaptureNo, StartTime, EndTime, Sample)
            VALUES ('99999999-9999-9999-9999-999999999999', 1, 1, '2026-01-01T00:00:00.0000000+00:00', '2026-01-01T00:00:05.0000000+00:00', 1)
            """);
        var schemaBefore = AppDatabase.Schema(orphan);
        var broken = await Assert.ThrowsAsync<InvalidOperationException>(() => new SqliteMeasurementRepository(orphan).LoadAsync(AppDatabase.SessionA),
            "orphan record");
        Assert.Contains("records without a session", broken.Message, "orphan: message");
        Assert.Equal(3L, AppDatabase.UserVersion(orphan), "orphan: version unchanged");
        Assert.Equal(schemaBefore, AppDatabase.Schema(orphan), "orphan: no partial schema change");
        Assert.Equal(4L, AppDatabase.Count(orphan, "MeasurementRecords"), "orphan: rows unchanged");
    }

    [Test("BE-038", "Opening a current database again changes nothing; two instances upgrading one file agree (UR-28)")]
    public static async Task MigrationIdempotence()
    {
        using var folder = new TestFolder();
        var path = folder.Path("m.db");
        AppDatabase.Legacy(path, 4);
        await new SqliteMeasurementRepository(path).LoadAsync(AppDatabase.SessionA);
        SqliteConnection.ClearAllPools();
        var schema = AppDatabase.Schema(path);
        var rows = AppDatabase.Rows(path, "SELECT * FROM MeasurementRecords ORDER BY id");

        await new SqliteMeasurementRepository(path).LoadAsync(AppDatabase.SessionA);
        SqliteConnection.ClearAllPools();
        Assert.Equal(schema, AppDatabase.Schema(path), "schema unchanged on reopen");
        Assert.Sequence(rows, AppDatabase.Rows(path, "SELECT * FROM MeasurementRecords ORDER BY id"), "rows unchanged on reopen");

        var shared = folder.Path("shared.db");
        AppDatabase.Legacy(shared, 4);
        var results = await Task.WhenAll(
            new SqliteMeasurementRepository(shared).LoadAsync(AppDatabase.SessionA),
            new SqliteMeasurementRepository(shared).LoadAsync(AppDatabase.SessionA));
        Assert.True(results.All(r => r.Count == 2), "both instances read the migrated rows");
        Assert.Equal(5L, AppDatabase.UserVersion(shared), "migrated once");
        Assert.Sequence(new[] { "1025|25" }, AppDatabase.Rows(shared, "SELECT next_from_cm, step_cm FROM MeasurementProgress"), "progress converted exactly once");
    }

    [Test("BE-039", "Measured drillholes: newest capture first, compared as instants; sessions without rows left out")]
    public static async Task MeasuredOrdering()
    {
        using var folder = new TestFolder();
        var repository = new SqliteMeasurementRepository(folder.Path("m.db"));
        var a = await repository.ResolveSessionAsync(Make.Session("DH-A"));
        var b = await repository.ResolveSessionAsync(Make.Session("DH-B"));
        await repository.ResolveSessionAsync(Make.Session("DH-C"));
        await repository.SaveAsync(a, [Make.Row(a.Id, 1, 0, start: new DateTimeOffset(2026, 10, 16, 10, 0, 0, TimeSpan.FromHours(8)))], null);
        await repository.SaveAsync(b, [Make.Row(b.Id, 1, 0, start: new DateTimeOffset(2026, 10, 16, 3, 0, 0, TimeSpan.Zero))], null);

        var measured = await repository.LoadMeasuredDrillholesAsync();

        Assert.Sequence(new[] { "DH-B", "DH-A" }, measured.Select(m => m.DrillholeId), "03:00Z is newer than 10:00+08:00 (02:00Z)");
    }

    [Test("BE-040", "Drillhole cache: replace-all transaction, failed replace keeps the old copy, shares the file safely")]
    public static async Task DrillholeCacheReplace()
    {
        using var folder = new TestFolder();
        var path = folder.Path("m.db");
        var repository = new SqliteMeasurementRepository(path);
        var session = await repository.ResolveSessionAsync(Make.Session("DH-1"));
        var cache = new DrillholeCache(path);

        await cache.ReplaceAsync([new DrillholeResponse("X", "p", "c")]);
        await cache.ReplaceAsync([new DrillholeResponse("A", null, "1"), new DrillholeResponse("ДН-7", "Проект", null)]);
        var loaded = await cache.LoadAsync();
        Assert.Sequence(new[] { new DrillholeResponse("A", null, "1"), new DrillholeResponse("ДН-7", "Проект", null) },
            loaded.OrderBy(d => d.HoleId, StringComparer.Ordinal), "replaced, nulls and Unicode kept");

        await Assert.ThrowsAsync<Exception>(() => cache.ReplaceAsync([new DrillholeResponse("B", "p", "c"), new DrillholeResponse(null, "p", "c")]),
            "an entry that cannot be stored");
        Assert.Equal(2, (await cache.LoadAsync()).Count, "failed replace keeps the previous copy");
        Assert.Equal(session.Id, (await repository.FindSessionAsync("DH-1"))!.Id, "measurement data in the same file untouched");

        // An unreadable cache never blocks the form: the server list is still shown (the page treats it as empty).
        var corrupt = folder.Path("corrupt.db");
        File.WriteAllText(corrupt, "not a database ................................................................................");
        var form = Make.CoreForm(new StubBackend(), corrupt, new MemoryRepository(corrupt), out _);
        await form.LoadDrillholesAsync();
        Assert.Sequence(new[] { "DH-001", "DH-002", "DH-003" }, Make.Ids(form), "server list shown with a corrupt cache");
    }

    [Test("BE-046", "Long, Unicode, quoted and SQL-like text is stored verbatim")]
    public static async Task TextFields()
    {
        using var folder = new TestFolder();
        var path = folder.Path("m.db");
        var repository = new SqliteMeasurementRepository(path);
        var values = new[] { new string('n', 10_000), "Ünïcødé 😀", "a\"b'c", "Robert'); DROP TABLE MeasurementSessions;--", "line1\r\nline2" };

        for (var i = 0; i < values.Length; i++)
        {
            await repository.ResolveSessionAsync(Make.Session($"DH-T{i}", operatorName: values[i], comment: values[^(i + 1)]));
        }

        for (var i = 0; i < values.Length; i++)
        {
            var stored = (await repository.FindSessionAsync($"DH-T{i}"))!;
            Assert.Equal(values[i], stored.Operator, $"value {i}: Operator");
            Assert.Equal(values[^(i + 1)], stored.Comment, $"value {i}: Notes");
        }

        Assert.Equal(5L, AppDatabase.Count(path, "MeasurementSessions"), "every table still there");
    }
}
