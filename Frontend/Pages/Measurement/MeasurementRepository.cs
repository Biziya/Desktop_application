using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;

namespace Frontend.Pages.Measurement;

/// <summary>One measurement session: every capture between one Start and the next Start.</summary>
public sealed record MeasurementSession(Guid Id, DateTimeOffset CreatedAt)
{
    public static MeasurementSession New() => new(Guid.NewGuid(), DateTimeOffset.Now);
}

public interface IMeasurementRepository
{
    /// <summary>Inserts new rows and updates the display number of rows already stored. Rows are in display order.</summary>
    Task SaveAsync(MeasurementSession session, IReadOnlyList<MeasurementResult> rows);

    Task DeleteAsync(Guid sessionId, int captureNumber);

    /// <summary>The session that received the most recent stored row, or null when nothing is stored.</summary>
    Task<MeasurementSession?> LoadLatestSessionAsync();

    /// <summary>Rows of one session only, in display order.</summary>
    Task<IReadOnlyList<MeasurementResult>> LoadAsync(Guid sessionId);
}

/// <summary>
/// SQLite storage for measurement rows. All work runs off the UI thread. Database column names are stable and
/// independent of the table labels and CSV headers in MeasurementColumns.cs.
/// </summary>
public sealed class SqliteMeasurementRepository : IMeasurementRepository
{
    // Raise when the schema changes, and add a step to Migrate that alters existing tables without dropping data.
    private const int SchemaVersion = 1;

    private const string RecordColumns =
        "DisplayNo, CaptureNo, StartTime, EndTime, Sample, TsMs, Count0, Count0Raw, Hdm, HdmRaw, DoseRate";

    private readonly string databasePath;
    private readonly string connectionString;
    private readonly object schemaGate = new();
    private bool schemaReady;

    public SqliteMeasurementRepository(string databasePath)
    {
        this.databasePath = Path.GetFullPath(databasePath);
        connectionString = new SqliteConnectionStringBuilder { DataSource = this.databasePath, ForeignKeys = true }.ToString();
    }

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CT007 Measurement Studio",
        "measurements.db");

    public Task SaveAsync(MeasurementSession session, IReadOnlyList<MeasurementResult> rows) => Run(connection =>
    {
        using var transaction = connection.BeginTransaction();

        using (var insertSession = connection.CreateCommand())
        {
            insertSession.Transaction = transaction;
            insertSession.CommandText =
                "INSERT OR IGNORE INTO MeasurementSessions (MeasurementSessionId, CreatedAt) VALUES ($session, $createdAt)";
            insertSession.Parameters.AddWithValue("$session", Key(session.Id));
            insertSession.Parameters.AddWithValue("$createdAt", Time(session.CreatedAt));
            insertSession.ExecuteNonQuery();
        }

        // (MeasurementSessionId, CaptureNo) is unique, so saving the same row again only refreshes its display number.
        using var upsert = connection.CreateCommand();
        upsert.Transaction = transaction;
        upsert.CommandText = $"""
            INSERT INTO MeasurementRecords (MeasurementSessionId, {RecordColumns})
            VALUES ($session, $no, $captureNo, $startTime, $endTime, $sample, $tsMs, $count0, $count0Raw, $hdm, $hdmRaw, $doseRate)
            ON CONFLICT (MeasurementSessionId, CaptureNo) DO UPDATE SET DisplayNo = excluded.DisplayNo
            """;

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];

            if (row.MeasurementSessionId != session.Id)
            {
                throw new ArgumentException($"Capture {row.CaptureNumber} belongs to another measurement session.", nameof(rows));
            }

            upsert.Parameters.Clear();
            upsert.Parameters.AddWithValue("$session", Key(session.Id));
            upsert.Parameters.AddWithValue("$no", i + 1);
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
            upsert.ExecuteNonQuery();
        }

        transaction.Commit();
        return true;
    });

    public Task DeleteAsync(Guid sessionId, int captureNumber) => Run(connection =>
    {
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM MeasurementRecords WHERE MeasurementSessionId = $session AND CaptureNo = $captureNo";
        command.Parameters.AddWithValue("$session", Key(sessionId));
        command.Parameters.AddWithValue("$captureNo", captureNumber);
        command.ExecuteNonQuery();
        return true;
    });

    public Task<MeasurementSession?> LoadLatestSessionAsync() => Run(connection =>
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.MeasurementSessionId, s.CreatedAt
            FROM MeasurementRecords r
            JOIN MeasurementSessions s ON s.MeasurementSessionId = r.MeasurementSessionId
            ORDER BY r.MeasurementRecordId DESC
            LIMIT 1
            """;
        using var reader = command.ExecuteReader();

        return reader.Read()
            ? new MeasurementSession(Guid.Parse(reader.GetString(0)), ParseTime(reader.GetString(1)))
            : null;
    });

    public Task<IReadOnlyList<MeasurementResult>> LoadAsync(Guid sessionId) => Run<IReadOnlyList<MeasurementResult>>(connection =>
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {RecordColumns}
            FROM MeasurementRecords
            WHERE MeasurementSessionId = $session
            ORDER BY DisplayNo, CaptureNo
            """;
        command.Parameters.AddWithValue("$session", Key(sessionId));
        using var reader = command.ExecuteReader();
        var rows = new List<MeasurementResult>();

        while (reader.Read())
        {
            rows.Add(new MeasurementResult
            {
                Number = reader.GetInt32(0),
                MeasurementSessionId = sessionId,
                CaptureNumber = reader.GetInt32(1),
                StartTime = ParseTime(reader.GetString(2)),
                EndTime = ParseTime(reader.GetString(3)),
                SampleCount = reader.GetInt32(4),
                Reading = new DeviceReading(
                    reader.IsDBNull(5) ? null : reader.GetInt64(5),
                    NullableDouble(reader, 6),
                    NullableDouble(reader, 7),
                    NullableDouble(reader, 8),
                    NullableDouble(reader, 9),
                    NullableDouble(reader, 10))
            });
        }

        return rows;
    });

    private Task<T> Run<T>(Func<SqliteConnection, T> work) => Task.Run(() =>
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
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
                Migrate(connection);
                schemaReady = true;
            }
        }
    }

    // Applies only the missing steps, in one transaction, so existing data is kept.
    private static void Migrate(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA user_version";
        var version = Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);

        if (version > SchemaVersion)
        {
            throw new InvalidOperationException(
                $"The measurement database was created by a newer version of the application (schema {version}).");
        }

        if (version < 1)
        {
            // Times are ISO 8601 round-trip text ("O"): full sub-second precision and the original UTC offset.
            // The unique index prevents duplicate rows and also serves lookups by MeasurementSessionId.
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS MeasurementSessions (
                    MeasurementSessionId TEXT NOT NULL PRIMARY KEY,
                    CreatedAt            TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS MeasurementRecords (
                    MeasurementRecordId  INTEGER PRIMARY KEY AUTOINCREMENT,
                    MeasurementSessionId TEXT    NOT NULL REFERENCES MeasurementSessions (MeasurementSessionId),
                    DisplayNo            INTEGER NOT NULL,
                    CaptureNo            INTEGER NOT NULL,
                    StartTime            TEXT    NOT NULL,
                    EndTime              TEXT    NOT NULL,
                    Sample               INTEGER NOT NULL,
                    TsMs                 INTEGER,
                    Count0               REAL,
                    Count0Raw            REAL,
                    Hdm                  REAL,
                    HdmRaw               REAL,
                    DoseRate             REAL
                );
                CREATE UNIQUE INDEX IF NOT EXISTS UX_MeasurementRecords_Session_CaptureNo
                    ON MeasurementRecords (MeasurementSessionId, CaptureNo);
                """;
            command.ExecuteNonQuery();
        }

        command.CommandText = $"PRAGMA user_version = {SchemaVersion}";
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private static string Key(Guid id) => id.ToString("D");

    private static string Time(DateTimeOffset time) => time.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTime(string text) =>
        DateTimeOffset.ParseExact(text, "O", CultureInfo.InvariantCulture, DateTimeStyles.None);

    private static object OrDbNull<T>(T? value) where T : struct => value.HasValue ? value.Value : DBNull.Value;

    private static double? NullableDouble(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);
}
