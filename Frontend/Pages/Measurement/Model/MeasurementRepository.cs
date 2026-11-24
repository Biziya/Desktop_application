using System.Globalization;
using System.IO;
using Frontend.Calculation.Measurement;
using Microsoft.Data.Sqlite;

namespace Frontend.Pages.Measurement;

/// <summary>One measurement session per drillhole. Sessions stored before drillhole metadata existed have null metadata.</summary>
public sealed record MeasurementSession(
    Guid Id,
    DateTimeOffset CreatedAt,
    string? DrillholeId = null,
    string? DrillholeName = null,
    string? Project = null,
    string? Operator = null,
    string? Comment = null)
{
    /// <summary>True when name, project, operator or comment differ from <paramref name="other"/>.</summary>
    public bool MetadataDiffers(MeasurementSession other) =>
        DrillholeName != other.DrillholeName || Project != other.Project || Operator != other.Operator || Comment != other.Comment;
}

/// <summary>A drillhole with saved measurements and the Start Time of its most recent capture.</summary>
public sealed record MeasuredDrillhole(string DrillholeId, DateTimeOffset LastMeasured);

/// <summary>Next capture's start depth and step in mm (stored in MeasurementProgress as cm).</summary>
public sealed record DepthProgress(long NextFromMm, long StepMm);

public interface IMeasurementRepository
{
    string DatabasePath { get; }

    /// <summary>Returns the stored session of the candidate's DrillholeId, or stores and returns the candidate.</summary>
    Task<MeasurementSession> ResolveSessionAsync(MeasurementSession candidate);

    /// <summary>One transaction: inserts new rows, updates SortOrder of stored rows, and saves progress when given.</summary>
    Task SaveAsync(MeasurementSession session, IReadOnlyList<MeasurementResult> rows, DepthProgress? progress);

    Task<DepthProgress?> LoadProgressAsync(Guid sessionId);

    Task DeleteAsync(Guid sessionId, int captureNumber);

    /// <summary>The stored session of a Drillhole ID, or null. Never creates one.</summary>
    Task<MeasurementSession?> FindSessionAsync(string drillholeId);

    Task<IReadOnlyList<MeasurementResult>> LoadAsync(Guid sessionId);

    /// <summary>Drillholes with saved records, most recent capture Start Time first.</summary>
    Task<IReadOnlyList<MeasuredDrillhole>> LoadMeasuredDrillholesAsync();
}

/// <summary>SQLite storage for measurement rows, off the UI thread. Column names are independent of table/CSV headers.</summary>
public sealed partial class SqliteMeasurementRepository : IMeasurementRepository
{
    // Raise when the schema changes, and add a step to Migrate that alters existing tables without dropping data.
    private const int SchemaVersion = 5;

    // "from" and "to" are SQL keywords, so they are always quoted. Record depths are whole millimetres.
    private const string RecordColumns =
        "SortOrder, CaptureNo, StartTime, EndTime, Sample, TsMs, Count0, Count0Raw, Hdm, HdmRaw, DoseRate, \"from\", \"to\", step, " +
        "duration_ms, expected_samples, temperature, pressure, device_serial, device_name, device_mac, device_session_id";

    // Raw device samples of a capture (version 5). source_id/serial/device_session_id/seq/ts_ms are the Gamma Guard
    // samples row as recorded; record_id links to the capture result in MeasurementRecords.
    private const string SampleColumns =
        "record_id, source_id, serial, device_session_id, seq, ts_ms, count0, count0_raw, hdm, hdm_raw, dose_rate, temperature, pressure";

    private const string SessionColumns = "id, CreatedAt, DrillholeID, DrillholeName, Project, \"operator\", comment";

    private readonly string connectionString;
    private readonly object schemaGate = new();
    private bool schemaReady;

    public SqliteMeasurementRepository(string databasePath)
    {
        DatabasePath = Path.GetFullPath(databasePath);
        connectionString = new SqliteConnectionStringBuilder { DataSource = DatabasePath, ForeignKeys = true }.ToString();
    }

    public string DatabasePath { get; }

    /// <summary>%LocalAppData%\CT007_Measurement_Studio\measurements.db</summary>
    public static string DefaultPath { get; } = ResolveDefaultPath(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    // Moves a database from the old folder name (with spaces) once; if that fails the old location keeps being used.
    internal static string ResolveDefaultPath(string localAppData)
    {
        var path = Path.Combine(localAppData, "CT007_Measurement_Studio", "measurements.db");
        var legacy = Path.Combine(localAppData, "CT007 Measurement Studio", "measurements.db");

        if (File.Exists(path) || !File.Exists(legacy))
        {
            return path;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            foreach (var suffix in new[] { "-wal", "-shm" })
            {
                if (File.Exists(legacy + suffix))
                {
                    File.Move(legacy + suffix, path + suffix);
                }
            }

            File.Move(legacy, path);
            return path;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Put back anything already moved so the old database stays complete.
            foreach (var suffix in new[] { "-wal", "-shm" })
            {
                if (File.Exists(path + suffix) && !File.Exists(legacy + suffix))
                {
                    File.Move(path + suffix, legacy + suffix);
                }
            }

            return legacy;
        }
    }

    public Task<MeasurementSession> ResolveSessionAsync(MeasurementSession candidate) => Run(connection =>
    {
        if (string.IsNullOrWhiteSpace(candidate.DrillholeId))
        {
            throw new ArgumentException("A measurement session needs a Drillhole ID.", nameof(candidate));
        }

        // The unique index on DrillholeID decides: if another insert (this or another process) got there first,
        // DO NOTHING keeps that row, and the SELECT returns it. Existing rows are never updated.
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT INTO MeasurementSessions ({SessionColumns})
            VALUES ($id, $createdAt, $drillholeId, $drillholeName, $project, $operator, $comment)
            ON CONFLICT (DrillholeID) DO NOTHING;
            SELECT {SessionColumns} FROM MeasurementSessions WHERE DrillholeID = $drillholeId;
            """;
        command.Parameters.AddWithValue("$id", Key(candidate.Id));
        command.Parameters.AddWithValue("$createdAt", Time(candidate.CreatedAt));
        command.Parameters.AddWithValue("$drillholeId", candidate.DrillholeId);
        command.Parameters.AddWithValue("$drillholeName", OrDbNull(candidate.DrillholeName));
        command.Parameters.AddWithValue("$project", OrDbNull(candidate.Project));
        command.Parameters.AddWithValue("$operator", OrDbNull(candidate.Operator));
        command.Parameters.AddWithValue("$comment", OrDbNull(candidate.Comment));

        MeasurementSession session;

        using (var reader = command.ExecuteReader())
        {
            if (!reader.Read())
            {
                throw new InvalidOperationException($"Drillhole {candidate.DrillholeId} could not be stored.");
            }

            session = ReadSession(reader);
        }

        transaction.Commit();
        return session;
    });

    public Task<DepthProgress?> LoadProgressAsync(Guid sessionId) => Run(connection =>
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT next_from_cm, step_cm FROM MeasurementProgress WHERE session_id = $session";
        command.Parameters.AddWithValue("$session", Key(sessionId));
        using var reader = command.ExecuteReader();
        return reader.Read() ? new DepthProgress(CentimetresToMm(reader.GetDouble(0)), CentimetresToMm(reader.GetDouble(1))) : null;
    });

    public Task SaveAsync(MeasurementSession session, IReadOnlyList<MeasurementResult> rows, DepthProgress? progress) => Run(connection =>
    {
        // The session row already exists (ResolveSessionAsync or an earlier version created it).
        using var transaction = connection.BeginTransaction();

        if (progress is not null)
        {
            // Saved with the rows it follows from, so a capture and the depth advance it causes commit together.
            using var saveProgress = connection.CreateCommand();
            saveProgress.Transaction = transaction;
            saveProgress.CommandText = """
                INSERT INTO MeasurementProgress (session_id, next_from_cm, step_cm) VALUES ($session, $nextFrom, $step)
                ON CONFLICT (session_id) DO UPDATE SET next_from_cm = excluded.next_from_cm, step_cm = excluded.step_cm
                """;
            saveProgress.Parameters.AddWithValue("$session", Key(session.Id));
            saveProgress.Parameters.AddWithValue("$nextFrom", progress.NextFromMm / 10.0);
            saveProgress.Parameters.AddWithValue("$step", progress.StepMm / 10.0);
            saveProgress.ExecuteNonQuery();
        }

        // (session_id, CaptureNo) is unique: re-saving a stored row only rewrites its SortOrder.
        using var upsert = connection.CreateCommand();
        upsert.Transaction = transaction;
        upsert.CommandText = $"""
            INSERT INTO MeasurementRecords (session_id, {RecordColumns})
            VALUES ($session, $sortOrder, $captureNo, $startTime, $endTime, $sample, $tsMs, $count0, $count0Raw, $hdm, $hdmRaw, $doseRate,
                    $from, $to, $step, $durationMs, $expectedSamples, $temperature, $pressure, $deviceSerial, $deviceName, $deviceMac,
                    $deviceSessionId)
            ON CONFLICT (session_id, CaptureNo) DO UPDATE SET SortOrder = excluded.SortOrder
                WHERE SortOrder <> excluded.SortOrder
            """;

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];

            if (row.SessionId != session.Id)
            {
                throw new ArgumentException($"Capture {row.CaptureNumber} belongs to another measurement session.", nameof(rows));
            }

            upsert.Parameters.Clear();
            upsert.Parameters.AddWithValue("$session", Key(session.Id));
            upsert.Parameters.AddWithValue("$sortOrder", i + 1);
            upsert.Parameters.AddWithValue("$captureNo", row.CaptureNumber);
            upsert.Parameters.AddWithValue("$startTime", Time(row.StartTime));
            upsert.Parameters.AddWithValue("$endTime", Time(row.EndTime));
            upsert.Parameters.AddWithValue("$sample", row.SampleCount);
            upsert.Parameters.AddWithValue("$tsMs", OrDbNull(row.Reading.TsMs));
            upsert.Parameters.AddWithValue("$count0", OrDbNull(row.Reading.Count0));
            upsert.Parameters.AddWithValue("$count0Raw", OrDbNull(row.Reading.Count0Raw));
            upsert.Parameters.AddWithValue("$hdm", OrDbNull(row.Reading.Hdm));
            upsert.Parameters.AddWithValue("$hdmRaw", OrDbNull(row.Reading.HdmRaw));
            upsert.Parameters.AddWithValue("$doseRate", OrDbNull(row.Reading.DoseRate));
            upsert.Parameters.AddWithValue("$from", OrDbNull(row.FromMm));
            upsert.Parameters.AddWithValue("$to", OrDbNull(row.ToMm));
            upsert.Parameters.AddWithValue("$step", OrDbNull(row.StepMm));
            upsert.Parameters.AddWithValue("$durationMs", OrDbNull(row.DurationMs));
            upsert.Parameters.AddWithValue("$expectedSamples", OrDbNull(row.ExpectedSamples));
            upsert.Parameters.AddWithValue("$temperature", OrDbNull(row.Temperature));
            upsert.Parameters.AddWithValue("$pressure", OrDbNull(row.Pressure));
            upsert.Parameters.AddWithValue("$deviceSerial", OrDbNull(row.DeviceSerial));
            upsert.Parameters.AddWithValue("$deviceName", OrDbNull(row.DeviceName));
            upsert.Parameters.AddWithValue("$deviceMac", OrDbNull(row.DeviceMac));
            upsert.Parameters.AddWithValue("$deviceSessionId", OrDbNull(row.DeviceSessionId));
            upsert.ExecuteNonQuery();

            if (row.Samples is { Count: > 0 } samples)
            {
                SaveSamples(connection, transaction, session.Id, row.CaptureNumber, samples);
            }
        }

        transaction.Commit();
        return true;
    });

    // The capture's raw samples are deleted with it, in the same transaction.
    public Task DeleteAsync(Guid sessionId, int captureNumber) => Run(connection =>
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM CaptureSamples WHERE record_id IN
                (SELECT id FROM MeasurementRecords WHERE session_id = $session AND CaptureNo = $captureNo);
            DELETE FROM MeasurementRecords WHERE session_id = $session AND CaptureNo = $captureNo;
            """;
        command.Parameters.AddWithValue("$session", Key(sessionId));
        command.Parameters.AddWithValue("$captureNo", captureNumber);
        command.ExecuteNonQuery();
        transaction.Commit();
        return true;
    });

    /// <summary>The raw device samples saved with one capture (in time order).</summary>
    public Task<IReadOnlyList<DeviceSample>> LoadSamplesAsync(Guid sessionId, int captureNumber) => Run<IReadOnlyList<DeviceSample>>(connection =>
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.source_id, c.serial, c.device_session_id, c.seq, c.ts_ms, c.count0, c.count0_raw, c.hdm, c.hdm_raw,
                   c.dose_rate, c.temperature, c.pressure
            FROM CaptureSamples c JOIN MeasurementRecords r ON r.id = c.record_id
            WHERE r.session_id = $session AND r.CaptureNo = $captureNo
            ORDER BY c.ts_ms, c.source_id
            """;
        command.Parameters.AddWithValue("$session", Key(sessionId));
        command.Parameters.AddWithValue("$captureNo", captureNumber);
        using var reader = command.ExecuteReader();
        var samples = new List<DeviceSample>();

        while (reader.Read())
        {
            samples.Add(new DeviceSample(reader.GetInt64(0), reader.GetString(1), NullableString(reader, 2), NullableLong(reader, 3),
                NullableLong(reader, 4), NullableDouble(reader, 5), NullableDouble(reader, 6), NullableDouble(reader, 7),
                NullableDouble(reader, 8), NullableDouble(reader, 9), NullableDouble(reader, 10), NullableDouble(reader, 11)));
        }

        return samples;
    });

    // (record_id, source_id) is unique, so a retried save never duplicates a sample.
    private static void SaveSamples(SqliteConnection connection, SqliteTransaction transaction, Guid sessionId, int captureNumber,
        IReadOnlyList<DeviceSample> samples)
    {
        using var find = connection.CreateCommand();
        find.Transaction = transaction;
        find.CommandText = "SELECT id FROM MeasurementRecords WHERE session_id = $session AND CaptureNo = $captureNo";
        find.Parameters.AddWithValue("$session", Key(sessionId));
        find.Parameters.AddWithValue("$captureNo", captureNumber);
        var recordId = Convert.ToInt64(find.ExecuteScalar(), CultureInfo.InvariantCulture);

        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = $"""
            INSERT INTO CaptureSamples ({SampleColumns})
            VALUES ($record, $source, $serial, $deviceSession, $seq, $ts, $count0, $count0Raw, $hdm, $hdmRaw, $doseRate, $temperature, $pressure)
            ON CONFLICT (record_id, source_id) DO NOTHING
            """;

        foreach (var sample in samples)
        {
            insert.Parameters.Clear();
            insert.Parameters.AddWithValue("$record", recordId);
            insert.Parameters.AddWithValue("$source", sample.SourceId);
            insert.Parameters.AddWithValue("$serial", sample.Serial);
            insert.Parameters.AddWithValue("$deviceSession", OrDbNull(sample.DeviceSessionId));
            insert.Parameters.AddWithValue("$seq", OrDbNull(sample.Seq));
            insert.Parameters.AddWithValue("$ts", OrDbNull(sample.TsMs));
            insert.Parameters.AddWithValue("$count0", OrDbNull(sample.Count0));
            insert.Parameters.AddWithValue("$count0Raw", OrDbNull(sample.Count0Raw));
            insert.Parameters.AddWithValue("$hdm", OrDbNull(sample.Hdm));
            insert.Parameters.AddWithValue("$hdmRaw", OrDbNull(sample.HdmRaw));
            insert.Parameters.AddWithValue("$doseRate", OrDbNull(sample.DoseRate));
            insert.Parameters.AddWithValue("$temperature", OrDbNull(sample.Temperature));
            insert.Parameters.AddWithValue("$pressure", OrDbNull(sample.Pressure));
            insert.ExecuteNonQuery();
        }
    }

    public Task<MeasurementSession?> FindSessionAsync(string drillholeId) => Run(connection =>
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SessionColumns} FROM MeasurementSessions WHERE DrillholeID = $drillholeId";
        command.Parameters.AddWithValue("$drillholeId", drillholeId);
        using var reader = command.ExecuteReader();

        return reader.Read() ? ReadSession(reader) : null;
    });

    public Task<IReadOnlyList<MeasurementResult>> LoadAsync(Guid sessionId) => Run<IReadOnlyList<MeasurementResult>>(connection =>
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {RecordColumns}, id
            FROM MeasurementRecords
            WHERE session_id = $session
            ORDER BY SortOrder, CaptureNo
            """;
        command.Parameters.AddWithValue("$session", Key(sessionId));
        using var reader = command.ExecuteReader();
        var rows = new List<MeasurementResult>();

        while (reader.Read())
        {
            rows.Add(new MeasurementResult
            {
                Number = reader.GetInt32(0),
                SessionId = sessionId,
                CaptureNumber = reader.GetInt32(1),
                StartTime = ParseTime(reader.GetString(2)),
                EndTime = ParseTime(reader.GetString(3)),
                SampleCount = reader.GetInt32(4),
                Reading = new DeviceReading(
                    NullableLong(reader, 5),
                    NullableDouble(reader, 6),
                    NullableDouble(reader, 7),
                    NullableDouble(reader, 8),
                    NullableDouble(reader, 9),
                    NullableDouble(reader, 10)),
                FromMm = NullableLong(reader, 11),
                ToMm = NullableLong(reader, 12),
                StepMm = NullableLong(reader, 13),
                DurationMs = NullableLong(reader, 14),
                ExpectedSamples = NullableLong(reader, 15) is { } expected ? (int)expected : null,
                Temperature = NullableDouble(reader, 16),
                Pressure = NullableDouble(reader, 17),
                DeviceSerial = NullableString(reader, 18),
                DeviceName = NullableString(reader, 19),
                DeviceMac = NullableString(reader, 20),
                DeviceSessionId = NullableString(reader, 21),
                RecordId = reader.GetInt64(22)
            });
        }

        return rows;
    });

    public Task<IReadOnlyList<MeasuredDrillhole>> LoadMeasuredDrillholesAsync() => Run<IReadOnlyList<MeasuredDrillhole>>(connection =>
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.DrillholeID, r.StartTime
            FROM MeasurementRecords r
            JOIN MeasurementSessions s ON s.id = r.session_id
            WHERE s.DrillholeID IS NOT NULL
            """;
        using var reader = command.ExecuteReader();
        var latest = new Dictionary<string, DateTimeOffset>();

        // Start times are stored as round-trip text with their offset, so they are compared as instants here.
        while (reader.Read())
        {
            var drillhole = reader.GetString(0);
            var start = ParseTime(reader.GetString(1));

            if (!latest.TryGetValue(drillhole, out var known) || start > known)
            {
                latest[drillhole] = start;
            }
        }

        return latest.Select(p => new MeasuredDrillhole(p.Key, p.Value)).OrderByDescending(m => m.LastMeasured).ToList();
    });

    private Task<T> Run<T>(Func<SqliteConnection, T> work) => Task.Run(() =>
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        EnsureSchema(connection);
        return work(connection);
    });

    private void EnsureSchema(SqliteConnection connection)
    {
        lock (schemaGate)
        {
            if (!schemaReady)
            {
                // WAL lets other tools read while the app writes. It cannot be set inside a transaction.
                using (var wal = connection.CreateCommand())
                {
                    wal.CommandText = "PRAGMA journal_mode = WAL";
                    wal.ExecuteNonQuery();
                }

                Migrate(connection);
                schemaReady = true;
            }
        }
    }

    // Stored centimetres back to whole millimetres (exact for every value written from millimetres).
    private static long CentimetresToMm(double centimetres) => (long)Math.Round(centimetres * 10, MidpointRounding.AwayFromZero);

    private static MeasurementSession ReadSession(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        ParseTime(reader.GetString(1)),
        NullableString(reader, 2),
        NullableString(reader, 3),
        NullableString(reader, 4),
        NullableString(reader, 5),
        NullableString(reader, 6));

    private static string? NullableString(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static long? NullableLong(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);

    private static object OrDbNull(string? value) => value is null ? DBNull.Value : value;

    private static string Key(Guid id) => id.ToString("D");

    private static string Time(DateTimeOffset time) => time.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTime(string text) =>
        DateTimeOffset.ParseExact(text, "O", CultureInfo.InvariantCulture, DateTimeStyles.None);

    private static object OrDbNull<T>(T? value) where T : struct => value.HasValue ? value.Value : DBNull.Value;

    private static double? NullableDouble(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);
}
