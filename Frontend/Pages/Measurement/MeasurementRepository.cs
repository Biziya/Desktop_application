using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;

namespace Frontend.Pages.Measurement;

/// <summary>One measurement session: every capture until the user starts a new measurement.</summary>
public sealed record MeasurementSession(Guid Id, DateTimeOffset CreatedAt)
{
    public static MeasurementSession New() => new(Guid.NewGuid(), DateTimeOffset.Now);
}

public interface IMeasurementRepository
{
    /// <summary>Full path of the database file in use.</summary>
    string DatabasePath { get; }

    /// <summary>
    /// Inserts new rows and stores the user-defined order (SortOrder = position in <paramref name="rows"/>) of rows
    /// already stored. Nothing else of a stored row changes.
    /// </summary>
    Task SaveAsync(MeasurementSession session, IReadOnlyList<MeasurementResult> rows);

    Task DeleteAsync(Guid sessionId, int captureNumber);

    /// <summary>The session that received the most recent stored row, or null when nothing is stored.</summary>
    Task<MeasurementSession?> LoadLatestSessionAsync();

    /// <summary>Rows of one session only, in saved order.</summary>
    Task<IReadOnlyList<MeasurementResult>> LoadAsync(Guid sessionId);
}

/// <summary>
/// SQLite storage for measurement rows. All work runs off the UI thread. Database column names are stable and
/// independent of the table labels and CSV headers in MeasurementColumns.cs.
/// </summary>
public sealed class SqliteMeasurementRepository : IMeasurementRepository
{
    // Raise when the schema changes, and add a step to Migrate that alters existing tables without dropping data.
    private const int SchemaVersion = 3;

    private const string RecordColumns =
        "SortOrder, CaptureNo, StartTime, EndTime, Sample, TsMs, Count0, Count0Raw, Hdm, HdmRaw, DoseRate";

    private readonly string connectionString;
    private readonly object schemaGate = new();
    private bool schemaReady;

    public SqliteMeasurementRepository(string databasePath)
    {
        DatabasePath = Path.GetFullPath(databasePath);
        connectionString = new SqliteConnectionStringBuilder { DataSource = DatabasePath, ForeignKeys = true }.ToString();
    }

    public string DatabasePath { get; }

    /// <summary>%LocalAppData%\CT007_Measurement_Studio\measurements.db — no spaces, so it is easy to use in tools.</summary>
    public static string DefaultPath { get; } = ResolveDefaultPath(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    // Earlier versions used a folder name with spaces. Their database (with its WAL files) is moved once into the
    // new folder. If the move is not possible (e.g. another program has the file open), the old location keeps
    // being used, so no stored measurements are ever left behind.
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

    public Task SaveAsync(MeasurementSession session, IReadOnlyList<MeasurementResult> rows) => Run(connection =>
    {
        using var transaction = connection.BeginTransaction();

        using (var insertSession = connection.CreateCommand())
        {
            insertSession.Transaction = transaction;
            insertSession.CommandText =
                "INSERT OR IGNORE INTO MeasurementSessions (id, CreatedAt) VALUES ($session, $createdAt)";
            insertSession.Parameters.AddWithValue("$session", Key(session.Id));
            insertSession.Parameters.AddWithValue("$createdAt", Time(session.CreatedAt));
            insertSession.ExecuteNonQuery();
        }

        // (session_id, CaptureNo) is unique: saving a stored row again never duplicates it and only
        // rewrites its SortOrder when the user moved it. IDs, capture numbers, times and values stay unchanged.
        using var upsert = connection.CreateCommand();
        upsert.Transaction = transaction;
        upsert.CommandText = $"""
            INSERT INTO MeasurementRecords (session_id, {RecordColumns})
            VALUES ($session, $sortOrder, $captureNo, $startTime, $endTime, $sample, $tsMs, $count0, $count0Raw, $hdm, $hdmRaw, $doseRate)
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
            upsert.ExecuteNonQuery();
        }

        transaction.Commit();
        return true;
    });

    public Task DeleteAsync(Guid sessionId, int captureNumber) => Run(connection =>
    {
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM MeasurementRecords WHERE session_id = $session AND CaptureNo = $captureNo";
        command.Parameters.AddWithValue("$session", Key(sessionId));
        command.Parameters.AddWithValue("$captureNo", captureNumber);
        command.ExecuteNonQuery();
        return true;
    });

    public Task<MeasurementSession?> LoadLatestSessionAsync() => Run(connection =>
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.id, s.CreatedAt
            FROM MeasurementRecords r
            JOIN MeasurementSessions s ON s.id = r.session_id
            ORDER BY r.id DESC
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
                // WAL lets other tools (DBeaver, check-db.ps1) read the file while the app writes, and the app keep
                // writing while they read. The setting is stored in the file; it cannot change inside a transaction.
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
            // Original (version 1) schema; later steps rename columns. Times are ISO 8601 round-trip text ("O"):
            // full sub-second precision and the original UTC offset. The unique index (session, CaptureNo) prevents
            // duplicate rows and, because the session column comes first, is also the index for lookups by session.
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

        if (version < 2)
        {
            // Explicit user-defined row order. Renaming keeps the order already stored by version 1.
            command.CommandText = "ALTER TABLE MeasurementRecords RENAME COLUMN DisplayNo TO SortOrder";
            command.ExecuteNonQuery();
        }

        if (version < 3)
        {
            // Short, consistent ID names: MeasurementSessions.id, MeasurementRecords.id and
            // MeasurementRecords.session_id -> MeasurementSessions.id. RENAME COLUMN keeps every value and row in place,
            // and SQLite updates the foreign key and the unique index to the new names (no table is recreated).
            command.CommandText = """
                ALTER TABLE MeasurementSessions RENAME COLUMN MeasurementSessionId TO id;
                ALTER TABLE MeasurementRecords RENAME COLUMN MeasurementRecordId TO id;
                ALTER TABLE MeasurementRecords RENAME COLUMN MeasurementSessionId TO session_id;
                """;
            command.ExecuteNonQuery();
        }

        // Every record must point at an existing session; otherwise roll the upgrade back rather than store bad links.
        command.CommandText = "PRAGMA foreign_key_check";

        using (var orphans = command.ExecuteReader())
        {
            if (orphans.Read())
            {
                throw new InvalidOperationException("The measurement database has records without a session.");
            }
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
