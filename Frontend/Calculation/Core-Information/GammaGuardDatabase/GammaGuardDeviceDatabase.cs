using System.Globalization;
using Frontend.Calculation.CoreInformation.BluetoothDevice;
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
    /// The detector_status row of a Bluetooth device: same MAC address (any separator, any case), otherwise same name.
    /// If several rows match, the most recently updated one (updated_ms) is used.
    /// </summary>
    public DetectorStatus? FindDetector(KnownBluetoothDevice device)
    {
        using var connection = SqliteFileCheck.OpenReadOnly(Path);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT serial, name, mac, firmware, device_type, session_id, state, last_sample_ms, updated_ms
            FROM detector_status
            """;
        using var reader = command.ExecuteReader();
        var address = device.Address.ToString("X12", CultureInfo.InvariantCulture);
        var candidates = new List<(int Rank, DetectorStatus Status)>();

        while (reader.Read())
        {
            if (Text(reader, 0) is not { Length: > 0 } serial)
            {
                continue;
            }

            var status = new DetectorStatus(
                new DetectorIdentity(serial, Text(reader, 1), Text(reader, 2), Text(reader, 3), Text(reader, 4)),
                Text(reader, 5), Text(reader, 6), Long(reader, 7), Long(reader, 8));
            var mac = new string((status.Identity.Mac ?? "").Where(Uri.IsHexDigit).Select(char.ToUpperInvariant).ToArray());

            if (mac.Length == 12 && mac == address)
            {
                candidates.Add((0, status));
            }
            else if (string.Equals(status.Identity.Name?.Trim(), device.Name.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add((1, status));
            }
        }

        return candidates.OrderBy(c => c.Rank).ThenByDescending(c => c.Status.UpdatedMs ?? long.MinValue).Select(c => c.Status).FirstOrDefault();
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
