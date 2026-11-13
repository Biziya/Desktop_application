using System.Globalization;
using System.IO;
using Frontend.Calculation.CoreInformation;
using Microsoft.Data.Sqlite;

namespace Frontend.Pages.SavedData;

/// <summary>One measurement session (one drillhole) with its record count and newest capture.</summary>
public sealed record SessionSummary(
    string Id,
    DateTimeOffset CreatedAt,
    string? DrillholeId,
    string? DrillholeName,
    string? Project,
    string? Operator,
    string? Comment,
    int RecordCount,
    DateTimeOffset? LastCapture)
{
    public string Title => DrillholeId ?? "No drillhole";

    public DateTimeOffset LastActivity => LastCapture ?? CreatedAt;
}

/// <summary>One saved capture. Depths are millimetres as stored; Count0 values are counts per second.</summary>
public sealed record RecordRow(
    long Id,
    int CaptureNo,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    int SampleCount,
    int? ExpectedSamples,
    long? FromMm,
    long? ToMm,
    double? Cps,
    double? CpsRaw,
    double? Hdm,
    double? DoseRate,
    double? Temperature,
    string? DeviceSerial)
{
    public string Interval => FromMm is { } from && ToMm is { } to
        ? $"{from / 1000.0:0.00} – {to / 1000.0:0.00}"
        : "–";

    public string Samples => ExpectedSamples is { } expected ? $"{SampleCount} / {expected}" : SampleCount.ToString(CultureInfo.CurrentCulture);
}

/// <summary>One raw device sample stored with a capture (CaptureSamples). Count values are per minute, as recorded.</summary>
public sealed record RawSampleRow(
    long SourceId,
    string Serial,
    long? Seq,
    string Time,
    double? Count0,
    double? Count0Raw,
    double? Hdm,
    double? DoseRate,
    double? Temperature);

public sealed record RecordPage(IReadOnlyList<RecordRow> Rows, int Total);

/// <summary>
/// Read-only queries over the app's own measurements.db for the Saved Data page. Every call opens the file read-only
/// and closes it again, so the page never holds the database open or locks out a capture being saved.
/// </summary>
public sealed class SavedDataQueries(string databasePath)
{
    public string DatabasePath { get; } = databasePath;

    /// <summary>All sessions, most recently active first. Empty when the database does not exist yet.</summary>
    public IReadOnlyList<SessionSummary> LoadSessions()
    {
        if (!File.Exists(DatabasePath))
        {
            return [];
        }

        using var connection = SqliteFileCheck.OpenReadOnly(DatabasePath);

        if (!HasTable(connection, "MeasurementSessions"))
        {
            return [];
        }

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.id, s.CreatedAt, s.DrillholeID, s.DrillholeName, s.Project, s."operator", s.comment,
                   COUNT(r.id), MAX(r.EndTime)
            FROM MeasurementSessions s
            LEFT JOIN MeasurementRecords r ON r.session_id = s.id
            GROUP BY s.id
            """;
        using var reader = command.ExecuteReader();
        var sessions = new List<SessionSummary>();

        while (reader.Read())
        {
            sessions.Add(new SessionSummary(
                reader.GetString(0),
                ParseTime(reader.GetString(1)) ?? DateTimeOffset.MinValue,
                Text(reader, 2), Text(reader, 3), Text(reader, 4), Text(reader, 5), Text(reader, 6),
                reader.GetInt32(7),
                reader.IsDBNull(8) ? null : ParseTime(reader.GetString(8))));
        }

        return sessions.OrderByDescending(s => s.LastActivity).ToList();
    }

    /// <summary>One page of a session's records, ordered by depth (From), then capture number.</summary>
    public RecordPage LoadRecords(string sessionId, int offset, int limit)
    {
        if (!File.Exists(DatabasePath))
        {
            return new RecordPage([], 0);
        }

        using var connection = SqliteFileCheck.OpenReadOnly(DatabasePath);
        using var command = connection.CreateCommand();
        command.Parameters.AddWithValue("$session", sessionId);

        command.CommandText = "SELECT COUNT(*) FROM MeasurementRecords WHERE session_id = $session";
        var total = Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);

        command.CommandText = """
            SELECT id, CaptureNo, StartTime, EndTime, Sample, expected_samples, "from", "to",
                   Count0, Count0Raw, Hdm, DoseRate, temperature, device_serial
            FROM MeasurementRecords
            WHERE session_id = $session
            ORDER BY "from" IS NULL, "from", CaptureNo
            LIMIT $limit OFFSET $offset
            """;
        command.Parameters.AddWithValue("$limit", limit);
        command.Parameters.AddWithValue("$offset", offset);
        using var reader = command.ExecuteReader();
        var rows = new List<RecordRow>();

        while (reader.Read())
        {
            rows.Add(new RecordRow(
                reader.GetInt64(0),
                reader.GetInt32(1),
                ParseTime(reader.GetString(2)) ?? DateTimeOffset.MinValue,
                ParseTime(reader.GetString(3)) ?? DateTimeOffset.MinValue,
                reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetInt32(5),
                Long(reader, 6), Long(reader, 7),
                Number(reader, 8), Number(reader, 9), Number(reader, 10), Number(reader, 11), Number(reader, 12),
                Text(reader, 13)));
        }

        return new RecordPage(rows, total);
    }

    /// <summary>The raw device samples stored with one capture, in time order (none for databases before version 5).</summary>
    public IReadOnlyList<RawSampleRow> LoadRawSamples(long recordId)
    {
        if (!File.Exists(DatabasePath))
        {
            return [];
        }

        using var connection = SqliteFileCheck.OpenReadOnly(DatabasePath);

        if (!HasTable(connection, "CaptureSamples"))
        {
            return [];
        }

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT source_id, serial, seq, ts_ms, count0, count0_raw, hdm, dose_rate, temperature
            FROM CaptureSamples WHERE record_id = $record ORDER BY ts_ms, source_id
            """;
        command.Parameters.AddWithValue("$record", recordId);
        using var reader = command.ExecuteReader();
        var rows = new List<RawSampleRow>();

        while (reader.Read())
        {
            var time = Long(reader, 3) is { } ms
                ? DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.CurrentCulture)
                : "–";
            rows.Add(new RawSampleRow(
                reader.GetInt64(0), Text(reader, 1) ?? "", Long(reader, 2), time,
                Number(reader, 4), Number(reader, 5), Number(reader, 6), Number(reader, 7), Number(reader, 8)));
        }

        return rows;
    }

    internal static bool HasTable(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name";
        command.Parameters.AddWithValue("$name", table);
        return command.ExecuteScalar() is not null;
    }

    private static DateTimeOffset? ParseTime(string text) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time) ? time : null;

    private static string? Text(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static long? Long(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);

    private static double? Number(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);
}
