namespace Ct007.Tests.Fixtures;

/// <summary>
/// Direct SQLite access to the app database for assertions, and builders for databases written by older app versions
/// (schema versions 1–4). The legacy DDL is taken from the migration steps in Frontend/Pages/Measurement/MeasurementRepository.cs:
/// data is inserted at version 1 and upgraded the way earlier app versions did.
/// </summary>
public static class AppDatabase
{
    public static readonly Guid SessionA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid SessionB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public static object? Scalar(string path, string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    public static long Count(string path, string table) => Convert.ToInt64(Scalar(path, $"SELECT COUNT(*) FROM {table}"), CultureInfo.InvariantCulture);

    public static long UserVersion(string path) => Convert.ToInt64(Scalar(path, "PRAGMA user_version"), CultureInfo.InvariantCulture);

    public static bool HasColumn(string path, string table, string column) =>
        Convert.ToInt64(Scalar(path, $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = '{column}'"), CultureInfo.InvariantCulture) > 0;

    public static string Schema(string path) => string.Join("\n", Rows(path, "SELECT name || ':' || IFNULL(sql, '') FROM sqlite_master ORDER BY name"));

    public static List<string> Rows(string path, string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var rows = new List<string>();

        while (reader.Read())
        {
            rows.Add(string.Join("|", Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? "NULL" : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture))));
        }

        return rows;
    }

    /// <summary>Raw SQL as an older app version or another tool would run it: foreign keys are not enforced (fixtures
    /// may contain deliberately broken links).</summary>
    public static void Execute(string path, string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false, ForeignKeys = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// A database as left by app version <paramref name="version"/> (1–4): sessions A and B, three records (two in A),
    /// and for version 4 Drillhole IDs DH-A/DH-B and progress in millimetres (A: next 10250, step 250).
    /// </summary>
    public static void Legacy(string path, int version)
    {
        Execute(path, $"""
            CREATE TABLE MeasurementSessions (MeasurementSessionId TEXT NOT NULL PRIMARY KEY, CreatedAt TEXT NOT NULL);
            CREATE TABLE MeasurementRecords (
                MeasurementRecordId INTEGER PRIMARY KEY AUTOINCREMENT,
                MeasurementSessionId TEXT NOT NULL REFERENCES MeasurementSessions (MeasurementSessionId),
                DisplayNo INTEGER NOT NULL, CaptureNo INTEGER NOT NULL, StartTime TEXT NOT NULL, EndTime TEXT NOT NULL,
                Sample INTEGER NOT NULL, TsMs INTEGER, Count0 REAL, Count0Raw REAL, Hdm REAL, HdmRaw REAL, DoseRate REAL);
            CREATE UNIQUE INDEX UX_MeasurementRecords_Session_CaptureNo ON MeasurementRecords (MeasurementSessionId, CaptureNo);
            INSERT INTO MeasurementSessions VALUES ('{SessionA:D}', '2026-01-01T08:00:00.0000000+08:00'),
                                                   ('{SessionB:D}', '2026-01-02T08:00:00.0000000+00:00');
            INSERT INTO MeasurementRecords (MeasurementSessionId, DisplayNo, CaptureNo, StartTime, EndTime, Sample, TsMs, Count0, Count0Raw, Hdm, HdmRaw, DoseRate)
            VALUES ('{SessionA:D}', 2, 1, '2026-01-01T09:00:00.0000000+08:00', '2026-01-01T09:00:05.0000000+08:00', 5, 1767229200000, 10.5, 21, 1.25, 2.5, 0.125),
                   ('{SessionA:D}', 1, 2, '2026-01-01T09:01:00.0000000+08:00', '2026-01-01T09:01:05.0000000+08:00', 5, NULL, NULL, NULL, NULL, NULL, NULL),
                   ('{SessionB:D}', 1, 1, '2026-01-02T09:00:00.0000000+00:00', '2026-01-02T09:00:05.0000000+00:00', 4, 1767344400000, 7, 14, 1, 2, 0.1);
            PRAGMA user_version = 1;
            """);

        if (version >= 2)
        {
            Execute(path, "ALTER TABLE MeasurementRecords RENAME COLUMN DisplayNo TO SortOrder; PRAGMA user_version = 2;");
        }

        if (version >= 3)
        {
            Execute(path, """
                ALTER TABLE MeasurementSessions RENAME COLUMN MeasurementSessionId TO id;
                ALTER TABLE MeasurementRecords RENAME COLUMN MeasurementRecordId TO id;
                ALTER TABLE MeasurementRecords RENAME COLUMN MeasurementSessionId TO session_id;
                PRAGMA user_version = 3;
                """);
        }

        if (version >= 4)
        {
            Execute(path, $"""
                ALTER TABLE MeasurementSessions ADD COLUMN DrillholeID TEXT;
                ALTER TABLE MeasurementSessions ADD COLUMN DrillholeName TEXT;
                ALTER TABLE MeasurementSessions ADD COLUMN Project TEXT;
                ALTER TABLE MeasurementSessions ADD COLUMN "operator" TEXT;
                ALTER TABLE MeasurementSessions ADD COLUMN comment TEXT;
                ALTER TABLE MeasurementRecords ADD COLUMN "from" INTEGER;
                ALTER TABLE MeasurementRecords ADD COLUMN "to" INTEGER;
                ALTER TABLE MeasurementRecords ADD COLUMN step INTEGER;
                CREATE UNIQUE INDEX UX_MeasurementSessions_DrillholeID ON MeasurementSessions (DrillholeID);
                CREATE TABLE MeasurementProgress (session_id TEXT NOT NULL PRIMARY KEY REFERENCES MeasurementSessions (id),
                    next_from INTEGER NOT NULL, step INTEGER NOT NULL);
                UPDATE MeasurementSessions SET DrillholeID = 'DH-A', DrillholeName = 'North Ridge', Project = '2024', "operator" = 'Ann', comment = ''
                    WHERE id = '{SessionA:D}';
                UPDATE MeasurementSessions SET DrillholeID = 'DH-B' WHERE id = '{SessionB:D}';
                UPDATE MeasurementRecords SET "from" = 10000, "to" = 10250, step = 250 WHERE session_id = '{SessionA:D}' AND CaptureNo = 1;
                INSERT INTO MeasurementProgress VALUES ('{SessionA:D}', 10250, 250);
                PRAGMA user_version = 4;
                """);
        }
    }
}
