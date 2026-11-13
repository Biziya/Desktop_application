using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Frontend.Pages.Measurement;

/// <summary>Schema creation and upgrades of measurements.db (PRAGMA user_version). Every step keeps existing data.</summary>
public sealed partial class SqliteMeasurementRepository
{
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

        if (version < 5)
        {
            AddDeviceCapture(command);
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

    // Version 5 (real device capture). Runs once, inside the migration transaction:
    // * MeasurementProgress: next_from / step (millimetres) become next_from_cm / step_cm (centimetres, REAL), each
    //   value divided by 10 exactly once (the table is rebuilt, so the old mm columns cannot be read as cm later).
    // * MeasurementRecords: capture window, expected sample count, averaged temperature/pressure and the source detector
    //   (serial, name, MAC) plus the device's own session id. Existing rows keep NULL there — nothing is invented.
    // * CaptureSamples: the raw Gamma Guard samples of each capture, linked to its MeasurementRecords row.
    private static void AddDeviceCapture(SqliteCommand command)
    {
        command.CommandText = """
            CREATE TABLE MeasurementProgress_v5 (
                session_id   TEXT NOT NULL PRIMARY KEY REFERENCES MeasurementSessions (id),
                next_from_cm REAL NOT NULL,
                step_cm      REAL NOT NULL
            );
            INSERT INTO MeasurementProgress_v5 (session_id, next_from_cm, step_cm)
                SELECT session_id, next_from / 10.0, step / 10.0 FROM MeasurementProgress;
            DROP TABLE MeasurementProgress;
            ALTER TABLE MeasurementProgress_v5 RENAME TO MeasurementProgress;
            """;
        command.ExecuteNonQuery();

        AddMissingColumns(command, "MeasurementRecords",
        [
            "duration_ms INTEGER", "expected_samples INTEGER", "temperature REAL", "pressure REAL",
            "device_serial TEXT", "device_name TEXT", "device_mac TEXT", "device_session_id TEXT"
        ]);

        command.CommandText = """
            CREATE TABLE IF NOT EXISTS CaptureSamples (
                id                INTEGER PRIMARY KEY AUTOINCREMENT,
                record_id         INTEGER NOT NULL REFERENCES MeasurementRecords (id),
                source_id         INTEGER NOT NULL,
                serial            TEXT    NOT NULL,
                device_session_id TEXT,
                seq               INTEGER,
                ts_ms             INTEGER,
                count0            REAL,
                count0_raw        REAL,
                hdm               REAL,
                hdm_raw           REAL,
                dose_rate         REAL,
                temperature       REAL,
                pressure          REAL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS UX_CaptureSamples_Record_Source ON CaptureSamples (record_id, source_id);
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
}
