using System.Globalization;
using System.IO;
using System.Text.Json;
using Frontend.Config;
using Microsoft.Data.Sqlite;

namespace Frontend.Pages.Measurement;

/// <summary>
/// Deletes old measurements from measurements.db (Config/data-retention.yaml). The unit is the session (one drillhole):
/// a session is deleted, with its records, raw samples and depth progress, when its newest capture — or, without
/// captures, its creation — is older than the retention period. Deleting whole sessions never leaves a drillhole with
/// gaps in its capture numbers. The session open on the Measurement page is always kept.
/// Only measurements.db is touched; the CT008 / Gamma Guard database is never written.
/// </summary>
public sealed class DataRetention(string databasePath, DataRetentionSettings settings, Func<DateTimeOffset>? clock = null)
{
    private readonly Func<DateTimeOffset> now = clock ?? (() => DateTimeOffset.Now);

    public DataRetentionSettings Settings { get; } = settings;

    /// <summary>
    /// Runs one check. Returns how many sessions were deleted (0 when disabled or the database does not exist yet).
    /// </summary>
    /// <param name="keepSessionId">The session in use on the Measurement page; never deleted.</param>
    public int Run(Guid? keepSessionId)
    {
        if (!Settings.Enabled || !File.Exists(databasePath))
        {
            return 0;
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            ForeignKeys = true,
            Pooling = false,
            DefaultTimeout = 10
        }.ToString());
        connection.Open();

        var tables = ExistingTables(connection);

        if (!tables.Contains("MeasurementSessions") || !tables.Contains("MeasurementRecords"))
        {
            return 0;
        }

        // Times are stored as ISO 8601 round-trip text with their UTC offset; julianday() reads that format.
        var cutoff = (now() - Settings.RetentionPeriod).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT s.id FROM MeasurementSessions s
            LEFT JOIN MeasurementRecords r ON r.session_id = s.id
            WHERE s.id <> $keep
            GROUP BY s.id
            HAVING COALESCE(MAX(julianday(r.EndTime)), julianday(s.CreatedAt)) < julianday($cutoff)
            """;
        command.Parameters.AddWithValue("$keep", keepSessionId?.ToString("D") ?? "");
        command.Parameters.AddWithValue("$cutoff", cutoff);
        var expired = new List<string>();

        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                expired.Add(reader.GetString(0));
            }
        }

        if (expired.Count == 0)
        {
            return 0;
        }

        command.Parameters.Clear();
        command.Parameters.AddWithValue("$ids", JsonSerializer.Serialize(expired));
        const string ids = "(SELECT value FROM json_each($ids))";

        // Children first: the foreign keys have no ON DELETE CASCADE.
        if (tables.Contains("CaptureSamples"))
        {
            Execute($"DELETE FROM CaptureSamples WHERE record_id IN (SELECT id FROM MeasurementRecords WHERE session_id IN {ids})");
        }

        Execute($"DELETE FROM MeasurementRecords WHERE session_id IN {ids}");

        if (tables.Contains("MeasurementProgress"))
        {
            Execute($"DELETE FROM MeasurementProgress WHERE session_id IN {ids}");
        }

        Execute($"DELETE FROM MeasurementSessions WHERE id IN {ids}");
        transaction.Commit();
        Compact(connection);
        return expired.Count;

        void Execute(string sql)
        {
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
    }

    // Give the space back once a quarter of the file is free. A failure (another connection busy) is harmless:
    // the space is reused by later captures and the next check tries again.
    private static void Compact(SqliteConnection connection)
    {
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA freelist_count";
            var free = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            command.CommandText = "PRAGMA page_count";
            var total = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);

            if (total > 0 && free * 4 >= total)
            {
                command.CommandText = "VACUUM";
                command.ExecuteNonQuery();
            }
        }
        catch (SqliteException)
        {
        }
    }

    private static HashSet<string> ExistingTables(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";
        using var reader = command.ExecuteReader();
        var names = new HashSet<string>(StringComparer.Ordinal);

        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }
}
