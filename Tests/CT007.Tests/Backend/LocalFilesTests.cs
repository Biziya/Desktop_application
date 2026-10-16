using System.Diagnostics;

namespace Ct007.Tests.BackendTests;

/// <summary>BE-027…BE-030: Gamma Guard reader (read-only), SQLite file checks, discovery, capture duration file (F07, F08, F13, F15).</summary>
public static class LocalFilesTests
{
    private const long T = 1_760_600_000_000;

    private static void ReadEverything(string path)
    {
        var reader = new GammaGuardDeviceDatabase(path);
        reader.MaxSampleId();
        reader.ReadSamplesAfter(Samples.Serial, 0);
        reader.LatestTsMs(Samples.Serial);
        reader.RecentSerials(GammaGuardCaptureDevices.RecentRows);
        reader.FindDetector(Samples.Serial);
        SqliteFileCheck.Check(path, "missing");
    }

    [Test("BE-027", "The Gamma Guard reader never modifies the vendor database (rollback and WAL journal)")]
    public static void VendorDatabaseReadOnly()
    {
        using var folder = new TestFolder();

        // Rollback journal: Gamma Guard closed, nothing but the file itself.
        var rollback = folder.Path("rollback.sqlite");
        using (var gg = new FakeGammaGuard(rollback))
        {
            gg.Detector(Samples.Serial, "CT007-M-0001", "00:11:22:AA:BB:01", T);
            for (var i = 1; i <= 5; i++) gg.Sample(Samples.Serial, T + i * 1000, count0: i);
            gg.Execute("INSERT INTO samples (serial, ts_ms, count0) VALUES ($s, $t, 'n/a')", ("$s", Samples.Serial), ("$t", T + 6000));
        }

        SqliteConnection.ClearAllPools();
        var hash = TestFolder.Sha256(rollback);
        var written = File.GetLastWriteTimeUtc(rollback);
        ReadEverything(rollback);
        var reader = new GammaGuardDeviceDatabase(rollback);
        Assert.Equal(6L, reader.MaxSampleId(), "max id");
        Assert.Equal(T + 6000, reader.LatestTsMs(Samples.Serial), "latest ts");
        Assert.True(reader.ReadSamplesAfter(Samples.Serial, 5).Single().Count0 is null, "non-numeric count0 reads as missing");
        Assert.Equal("CT007-M-0001", reader.FindDetector(Samples.Serial)!.Identity.Name, "detector name");
        SqliteConnection.ClearAllPools();
        Assert.Equal(hash, TestFolder.Sha256(rollback), "rollback: bytes unchanged");
        Assert.Equal(written, File.GetLastWriteTimeUtc(rollback), "rollback: write time unchanged");
        Assert.Sequence(new[] { "rollback.sqlite" }, Directory.GetFiles(folder.Root, "rollback.sqlite*").Select(f => Path.GetFileName(f)), "rollback: no journal/-wal/-shm left");

        // WAL: Gamma Guard keeps its writer open while the app reads.
        var wal = folder.Path("wal.sqlite");
        using var live = new FakeGammaGuard(wal, wal: true);
        for (var i = 1; i <= 5; i++) live.Sample(Samples.Serial, T + i * 1000);
        live.Execute("PRAGMA wal_checkpoint(TRUNCATE)");
        var walHash = TestFolder.Sha256(wal);
        ReadEverything(wal);
        Assert.Equal(5, new GammaGuardDeviceDatabase(wal).ReadSamplesAfter(Samples.Serial, 0).Count, "WAL: rows readable");
        Assert.Equal(walHash, TestFolder.Sha256(wal), "WAL: main file unchanged by the reader");
    }

    [Test("BE-027", "A missing samples table or a locked database gives a readable error, not a crash")]
    public static void BrokenOrLockedDatabase()
    {
        using var folder = new TestFolder();
        var empty = folder.Path("empty.sqlite");
        using (var connection = new SqliteConnection($"Data Source={empty};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE other (x INTEGER)";
            command.ExecuteNonQuery();
        }

        var missing = Assert.Throws<SqliteException>(() => new GammaGuardDeviceDatabase(empty).MaxSampleId(), "no samples table");
        Assert.Contains("no such table: samples", missing.Message, "message names the table");

        var locked = folder.Path("locked.sqlite");
        using var gg = new FakeGammaGuard(locked);
        gg.Sample(Samples.Serial, T);
        gg.Execute("BEGIN EXCLUSIVE");
        var watch = Stopwatch.StartNew();
        var busy = Assert.Throws<SqliteException>(() => new GammaGuardDeviceDatabase(locked).MaxSampleId(), "exclusive lock");
        Assert.Equal(5, busy.SqliteErrorCode, "SQLITE_BUSY");
        Assert.True(watch.Elapsed.TotalSeconds is >= 1.5 and < 6, $"gave up after the 2 s busy timeout ({watch.Elapsed.TotalSeconds:0.0} s)");
        gg.Execute("ROLLBACK");
    }

    [Test("BE-028", "SqliteFileCheck: Missing (not created), Available, Inaccessible, Busy — file unchanged")]
    public static void FileCheckStates()
    {
        using var folder = new TestFolder();

        var absent = folder.Path("absent.db");
        Assert.Equal(LocalState.Missing, SqliteFileCheck.Check(absent, "x").State, "missing");
        Assert.False(File.Exists(absent), "missing file is not created");

        var valid = folder.Path("valid.db");
        using (var gg = new FakeGammaGuard(valid))
        {
        }

        var hash = TestFolder.Sha256(valid);
        Assert.Equal(LocalState.Available, SqliteFileCheck.Check(valid, "x").State, "valid database");
        Assert.Equal(hash, TestFolder.Sha256(valid), "valid: unchanged");

        var text = folder.Path("notes.sqlite");
        File.WriteAllText(text, "this is not a database, just text long enough to fill a header page ................................");
        var textHash = TestFolder.Sha256(text);
        Assert.Equal(LocalState.Inaccessible, SqliteFileCheck.Check(text, "x").State, "text file");
        Assert.Equal(textHash, TestFolder.Sha256(text), "text: unchanged");

        using var locker = new FakeGammaGuard(folder.Path("busy.db"));
        locker.Execute("BEGIN EXCLUSIVE");
        locker.Execute("INSERT INTO samples (serial) VALUES ('x')");
        Assert.Equal(LocalState.Busy, SqliteFileCheck.Check(locker.Path, "x").State, "exclusive lock");
        locker.Execute("ROLLBACK");
    }

    [Test("BE-029", "Gamma Guard discovery: 'GammaGuard' folder, Database subfolder, newest wins, cached, rediscovered (UR-16)")]
    public static void Discovery()
    {
        using var folder = new TestFolder();
        var root = folder.Path("root");

        string Create(string relative, DateTime writeTime)
        {
            var path = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (new FakeGammaGuard(path))
            {
            }

            SqliteConnection.ClearAllPools();
            File.SetLastWriteTimeUtc(path, writeTime);
            return path;
        }

        var older = Create(Path.Combine("Apps", "Gamma Guard", "Database", "a.sqlite"), new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var newer = Create(Path.Combine("Apps", "GammaGuard2", "Database", "b.sqlite"), new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));
        Create(Path.Combine("Apps", "GammaGuard", "Logs", "c.sqlite"), new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc));
        Create(Path.Combine("x", "y", "z", "w", "v", "GammaGuard", "Database", "d.sqlite"), new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc));

        var check = new GammaGuardDatabaseCheck(DeviceSettings.Default, new[] { (root, 2), (folder.Path("does-not-exist"), 2) });
        Assert.Equal(newer, check.Discover(), "newest file under a Database folder, not too deep");
        Assert.Equal(newer, check.Check().Path, "check uses the discovered file");

        Create(Path.Combine("Apps", "GammaGuard3", "Database", "e.sqlite"), new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(newer, check.Check().Path, "discovery result is cached while the file exists");
        var rediscovered = check.Check(rediscover: true).Path;
        Assert.True(rediscovered.EndsWith("e.sqlite", StringComparison.Ordinal), $"Retry rediscovers ({rediscovered})");

        File.Delete(rediscovered);
        File.Delete(newer);
        Assert.Equal(older, check.Check().Path, "a vanished file triggers a new search");

        var configured = new GammaGuardDatabaseCheck(Make.Devices(folder.Path("configured.sqlite")), new[] { (root, 2) });
        var status = configured.Check();
        Assert.Equal(LocalState.Missing, status.State, "configured path used as-is, even if missing");
        Assert.Equal(folder.Path("configured.sqlite"), status.Path, "configured path");
    }

    // The non-numeric case is its own test: its known defect (DEF-002) used to end this test early and hide the limit,
    // parsing and save checks below from mutation testing (qa/effectiveness/eff-20261016).
    [Test("BE-030", "A non-numeric saved duration falls back to 5 s (DEF-002)")]
    public static void DurationSettingsNonNumericValue()
    {
        using var folder = new TestFolder();
        var path = folder.Path("text.json");
        File.WriteAllText(path, """{"captureDurationSeconds":"7"}""");
        Assert.Equal(CaptureDurationSettings.DefaultSeconds, new CaptureDurationSettings(path).Load(), "text value");
    }

    [Test("BE-030", "CaptureDurationSettings: load, parse and atomic save (UR-03)")]
    public static void DurationSettings()
    {
        using var folder = new TestFolder();

        foreach (var (name, json) in new (string, string?)[]
                 {
                     ("missing", null), ("invalid JSON", "{"), ("below range", """{"captureDurationSeconds":1}"""),
                     ("above range", """{"captureDurationSeconds":11}"""), ("fraction", """{"captureDurationSeconds":5.5}"""),
                     ("no property", "{}")
                 })
        {
            var path = folder.Path(Guid.NewGuid() + ".json");
            if (json is not null) File.WriteAllText(path, json);
            Assert.Equal(CaptureDurationSettings.DefaultSeconds, new CaptureDurationSettings(path).Load(), name);
        }

        foreach (var seconds in new[] { 2, 10 })
        {
            var path = folder.Path($"{seconds}.json");
            File.WriteAllText(path, $$"""{"captureDurationSeconds":{{seconds}}}""");
            Assert.Equal(seconds, new CaptureDurationSettings(path).Load(), $"{seconds} s loaded");
        }

        var lockedPath = folder.Path("locked.json");
        File.WriteAllText(lockedPath, """{"captureDurationSeconds":7}""");
        using (new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Equal(5, new CaptureDurationSettings(lockedPath).Load(), "unreadable file");
        }

        foreach (var (text, expected) in new (string, int?)[]
                 {
                     (" 7 ", 7), ("010", 10), ("2", 2), ("10", 10), ("7.0", null), ("+7", null), ("-3", null), ("07x", null),
                     ("١", null), ("", null), ("1", null), ("11", null)
                 })
        {
            var ok = CaptureDurationSettings.TryParse(text, out var seconds);
            Assert.Equal(expected.HasValue, ok, $"'{text}' accepted");
            if (expected is { } value) Assert.Equal(value, seconds, $"'{text}' value");
        }

        var target = Path.Combine(folder.Root, "new-folder", "capture-settings.json");
        new CaptureDurationSettings(target).Save(8);
        using (var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(target)))
        {
            Assert.Equal(8, json.RootElement.GetProperty("captureDurationSeconds").GetInt32(), "saved value");
        }

        Assert.False(File.Exists(target + ".tmp"), "no temporary file left");
        Assert.Equal(8, new CaptureDurationSettings(target).Load(), "saved value loads");
        new CaptureDurationSettings(null).Save(9);
        Assert.Equal(5, new CaptureDurationSettings(null).Load(), "no path: default, nothing stored");
    }
}
