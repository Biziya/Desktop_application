using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;

namespace Frontend.Pages.Measurement;

/// <summary>
/// One measurement session per drillhole (MeasurementSessions row). Sessions stored before drillhole metadata existed
/// have null metadata.
/// </summary>
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

/// <summary>Where the next capture of a session starts and the selected step, both in millimetres (see Depth.cs).</summary>
public sealed record DepthProgress(long NextFromMm, long StepMm);

public interface IMeasurementRepository
{
    /// <summary>Full path of the database file in use.</summary>
    string DatabasePath { get; }

    /// <summary>
    /// Returns the session of <paramref name="candidate"/>'s DrillholeId: the stored one when it exists (its id,
    /// CreatedAt and metadata are kept), otherwise <paramref name="candidate"/> is stored and returned.
    /// </summary>
    Task<MeasurementSession> ResolveSessionAsync(MeasurementSession candidate);

    /// <summary>
    /// In one transaction: inserts new rows, stores the user-defined order (SortOrder = position in
    /// <paramref name="rows"/>) of rows already stored, and stores <paramref name="progress"/> when given.
    /// Nothing else of a stored row changes.
    /// </summary>
    Task SaveAsync(MeasurementSession session, IReadOnlyList<MeasurementResult> rows, DepthProgress? progress);

    /// <summary>Saved next depth and step of a session, or null when none is stored.</summary>
    Task<DepthProgress?> LoadProgressAsync(Guid sessionId);

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
    private const int SchemaVersion = 4;

    // "from" and "to" are SQL keywords, so they are always quoted. Depths are whole millimetres.
    private const string RecordColumns =
        "SortOrder, CaptureNo, StartTime, EndTime, Sample, TsMs, Count0, Count0Raw, Hdm, HdmRaw, DoseRate, \"from\", \"to\", step";

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
        command.CommandText = "SELECT next_from, step FROM MeasurementProgress WHERE session_id = $session";
        command.Parameters.AddWithValue("$session", Key(sessionId));
        using var reader = command.ExecuteReader();
        return reader.Read() ? new DepthProgress(reader.GetInt64(0), reader.GetInt64(1)) : null;
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
                INSERT INTO MeasurementProgress (session_id, next_from, step) VALUES ($session, $nextFrom, $step)
                ON CONFLICT (session_id) DO UPDATE SET next_from = excluded.next_from, step = excluded.step
                """;
            saveProgress.Parameters.AddWithValue("$session", Key(session.Id));
            saveProgress.Parameters.AddWithValue("$nextFrom", progress.NextFromMm);
            saveProgress.Parameters.AddWithValue("$step", progress.StepMm);
            saveProgress.ExecuteNonQuery();
        }

        // (session_id, CaptureNo) is unique: saving a stored row again never duplicates it and only
        // rewrites its SortOrder when the user moved it. IDs, capture numbers, times and values stay unchanged.
        using var upsert = connection.CreateCommand();
        upsert.Transaction = transaction;
        upsert.CommandText = $"""
            INSERT INTO MeasurementRecords (session_id, {RecordColumns})
            VALUES ($session, $sortOrder, $captureNo, $startTime, $endTime, $sample, $tsMs, $count0, $count0Raw, $hdm, $hdmRaw, $doseRate,
                    $from, $to, $step)
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
            SELECT s.id, s.CreatedAt, s.DrillholeID, s.DrillholeName, s.Project, s."operator", s.comment
            FROM MeasurementRecords r
            JOIN MeasurementSessions s ON s.id = r.session_id
            ORDER BY r.id DESC
            LIMIT 1
            """;
        using var reader = command.ExecuteReader();

        return reader.Read() ? ReadSession(reader) : null;
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
                    NullableLong(reader, 5),
                    NullableDouble(reader, 6),
                    NullableDouble(reader, 7),
                    NullableDouble(reader, 8),
                    NullableDouble(reader, 9),
                    NullableDouble(reader, 10)),
                FromMm = NullableLong(reader, 11),
                ToMm = NullableLong(reader, 12),
                StepMm = NullableLong(reader, 13)
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

        if (version < 4)
        {
            AddDrillholeMetadataAndDepths(command);
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

    // Version 4. Only adds columns, one unique index and the progress table: existing ids, links and values stay.
    // Sessions stored earlier keep null metadata and their records null depths — no drillhole IDs, intervals or steps
    // are invented. (Earlier schemas never had session-level from/to, so there is nothing to map onto records.)
    private static void AddDrillholeMetadataAndDepths(SqliteCommand command)
    {
        AddMissingColumns(command, "MeasurementSessions",
            ["DrillholeID TEXT", "DrillholeName TEXT", "Project TEXT", "\"operator\" TEXT", "comment TEXT"]);
        AddMissingColumns(command, "MeasurementRecords", ["\"from\" INTEGER", "\"to\" INTEGER", "step INTEGER"]);

        // A DrillholeID column can only already hold values if the file was changed outside this app. Merging such
        // duplicates would mean renumbering their captures, so the upgrade stops (and rolls back) and names them.
        command.CommandText = """
            SELECT DrillholeID || ' (' || COUNT(*) || ' sessions' ||
                   CASE WHEN COUNT(DISTINCT IFNULL(DrillholeName, '') || '|' || IFNULL(Project, '') || '|' ||
                                      IFNULL("operator", '') || '|' || IFNULL(comment, '')) > 1
                        THEN ', conflicting metadata' ELSE '' END || ')'
            FROM MeasurementSessions WHERE DrillholeID IS NOT NULL GROUP BY DrillholeID HAVING COUNT(*) > 1
            """;
        var duplicates = new List<string>();

        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                duplicates.Add(reader.GetString(0));
            }
        }

        if (duplicates.Count > 0)
        {
            throw new InvalidOperationException(
                "The measurement database has more than one session per Drillhole ID: " + string.Join(", ", duplicates) +
                ". Nothing was changed. Merge or rename these sessions, then restart the app.");
        }

        // next_from and step: where the session's next capture starts and the selected step (millimetres).
        command.CommandText = """
            CREATE UNIQUE INDEX IF NOT EXISTS UX_MeasurementSessions_DrillholeID ON MeasurementSessions (DrillholeID);
            CREATE TABLE IF NOT EXISTS MeasurementProgress (
                session_id TEXT    NOT NULL PRIMARY KEY REFERENCES MeasurementSessions (id),
                next_from  INTEGER NOT NULL,
                step       INTEGER NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    private static void AddMissingColumns(SqliteCommand command, string table, string[] definitions)
    {
        command.CommandText = $"SELECT name FROM pragma_table_info('{table}')";
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                existing.Add(reader.GetString(0));
            }
        }

        foreach (var definition in definitions)
        {
            if (!existing.Contains(definition.Split(' ')[0].Trim('"')))
            {
                command.CommandText = $"ALTER TABLE {table} ADD COLUMN {definition}";
                command.ExecuteNonQuery();
            }
        }
    }

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
