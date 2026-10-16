using System.Globalization;
using Frontend.Calculation.Measurement;
using Microsoft.Data.Sqlite;

namespace Frontend.Calculation.CoreInformation.GammaGuardDatabase;

/// <summary>A detector_status row: the detector's identity plus supporting status (not proof of a live connection).</summary>
public sealed record DetectorStatus(
    DetectorIdentity Identity,
    string? DeviceSessionId,
    string? State,
    long? LastSampleMs,
    long? UpdatedMs);

/// <summary>A serial seen in the newest samples, with its newest ts_ms.</summary>
public sealed record RecentSerial(string Serial, long LastTsMs);

/// <summary>
/// The only code that reads the Gamma Guard database (samples, detector_status). Every call opens the file read-only
/// through <see cref="SqliteFileCheck.OpenReadOnly"/> and closes it again: no writes, no PRAGMAs, no journal changes.
/// Sample reads go forward by samples.id (the table's key), so a poll touches only new rows, never the whole history.
/// </summary>
public sealed class GammaGuardDeviceDatabase(string path) : ISampleSource
{
    private const string SampleColumns =
        "id, serial, session_id, seq, ts_ms, count0, count0_raw, hdm, hdm_raw, dose_rate, temperature, pressure";

    public string Path { get; } = path;

    public long? MaxSampleId()
    {
        using var connection = SqliteFileCheck.OpenReadOnly(Path);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT MAX(id) FROM samples";
        return command.ExecuteScalar() is { } value and not DBNull ? Convert.ToInt64(value, CultureInfo.InvariantCulture) : null;
    }

    public IReadOnlyList<DeviceSample> ReadSamplesAfter(string serial, long afterId)
    {
        using var connection = SqliteFileCheck.OpenReadOnly(Path);
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SampleColumns} FROM samples WHERE id > $after AND serial = $serial ORDER BY id";
        command.Parameters.AddWithValue("$after", afterId);
        command.Parameters.AddWithValue("$serial", serial);
        using var reader = command.ExecuteReader();
        var rows = new List<DeviceSample>();

        while (reader.Read())
        {
            rows.Add(new DeviceSample(
                Long(reader, 0) ?? 0,
                Text(reader, 1) ?? serial,
                Text(reader, 2),
                Long(reader, 3),
                Long(reader, 4),
                Number(reader, 5),
                Number(reader, 6),
                Number(reader, 7),
                Number(reader, 8),
                Number(reader, 9),
                Number(reader, 10),
                Number(reader, 11)));
        }

        return rows;
    }

    public long? LatestTsMs(string serial)
    {
        using var connection = SqliteFileCheck.OpenReadOnly(Path);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ts_ms FROM samples WHERE serial = $serial AND ts_ms IS NOT NULL ORDER BY id DESC LIMIT 1";
        command.Parameters.AddWithValue("$serial", serial);
        return command.ExecuteScalar() is { } value and not DBNull ? Convert.ToInt64(value, CultureInfo.InvariantCulture) : null;
    }

    /// <summary>
    /// Serials that wrote the most recent <paramref name="rows"/> samples (by samples.id), each with its newest ts_ms.
    /// Only the tail of the table is read, never the whole history.
    /// </summary>
    public IReadOnlyList<RecentSerial> RecentSerials(int rows)
    {
        using var connection = SqliteFileCheck.OpenReadOnly(Path);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT serial, MAX(ts_ms) FROM samples
            WHERE id > (SELECT COALESCE(MAX(id), 0) FROM samples) - $rows AND serial IS NOT NULL AND ts_ms IS NOT NULL
            GROUP BY serial
            """;
        command.Parameters.AddWithValue("$rows", rows);
        using var reader = command.ExecuteReader();
        var serials = new List<RecentSerial>();

        while (reader.Read())
        {
            if (Text(reader, 0) is { Length: > 0 } serial && Long(reader, 1) is { } last)
            {
                serials.Add(new RecentSerial(serial, last));
            }
        }

        return serials;
    }

    /// <summary>The detector_status row of a serial (the most recently updated one if there are several), or null.</summary>
    public DetectorStatus? FindDetector(string serial)
    {
        using var connection = SqliteFileCheck.OpenReadOnly(Path);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT serial, name, mac, firmware, device_type, session_id, state, last_sample_ms, updated_ms
            FROM detector_status WHERE serial = $serial
            """;
        command.Parameters.AddWithValue("$serial", serial);
        using var reader = command.ExecuteReader();
        var rows = new List<DetectorStatus>();

        while (reader.Read())
        {
            rows.Add(new DetectorStatus(
                new DetectorIdentity(Text(reader, 0) ?? serial, Text(reader, 1), Text(reader, 2), Text(reader, 3), Text(reader, 4)),
                Text(reader, 5), Text(reader, 6), Long(reader, 7), Long(reader, 8)));
        }

        return rows.OrderByDescending(r => r.UpdatedMs ?? long.MinValue).FirstOrDefault();
    }

    private static string? Text(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture);

    private static long? Long(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetValue(ordinal) switch
        {
            long l => l,
            double d when d == Math.Floor(d) => (long)d,
            string s when long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) => l,
            var other => Convert.ToInt64(other, CultureInfo.InvariantCulture)
        };

    // Measurement fields: a value that is not a number (text, blob) counts as missing, never as zero.
    private static double? Number(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetValue(ordinal) switch
        {
            long l => l,
            double d => d,
            string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
            _ => null
        };
}
