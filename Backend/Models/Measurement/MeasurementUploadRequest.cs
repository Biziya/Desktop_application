using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Backend.Models.Measurement;

/// <summary>
/// POST /api/measurements/upload: one measurement session imported from a CSV on the Upload Data page. Every field is
/// nullable so that a missing value is reported by <see cref="Validate"/> with a clear message instead of becoming 0.
/// </summary>
public sealed record MeasurementUploadRequest(MeasurementUploadInformation? MeasurementInformation, IReadOnlyList<MeasurementUploadRecord>? Records)
{
    public const int MaxRecords = 10_000;
    public const int MaxCodeLength = 100;
    public const int MaxNameLength = 256;
    public const int MaxNotesLength = 4000;

    // DECIMAL(10,3) in SQL Server; deeper than any drillhole.
    private const decimal MaxDepthMetres = 100_000m;

    /// <summary>The first problem of the request in user-facing words (rows numbered from 1, as in the table), or null.</summary>
    public string? Validate()
    {
        if (MeasurementInformation is not { } info)
        {
            return "measurementInformation is required.";
        }

        if (info.SessionId is null || info.SessionId == Guid.Empty)
        {
            return "measurementInformation.sessionId is required.";
        }

        var text = Text("Drillhole", info.Drillhole, MaxCodeLength, required: true)
            ?? Text("Project", info.Project, MaxCodeLength, required: false)
            ?? Text("Campaign", info.Campaign, MaxCodeLength, required: false)
            ?? Text("Operator", info.Operator, MaxNameLength, required: true)
            ?? Text("Notes", info.Notes, MaxNotesLength, required: false)
            ?? Text("Created Username", info.CreatedUsername, MaxNameLength, required: false)
            ?? Text("Uploading Username", info.UploadingUsername, MaxNameLength, required: true);

        if (text is not null)
        {
            return text;
        }

        if (Records is null || Records.Count == 0)
        {
            return "At least one measurement is required.";
        }

        if (Records.Count > MaxRecords)
        {
            return $"At most {MaxRecords} measurements can be uploaded at once.";
        }

        var captures = new HashSet<int>();
        var latest = DateTimeOffset.UtcNow.AddDays(1);

        for (var i = 0; i < Records.Count; i++)
        {
            var row = $"Row {i + 1}";

            if (Records[i] is not { } record)
            {
                return $"{row}: the measurement is missing.";
            }

            if (record.CaptureNumber is not > 0)
            {
                return $"{row}: captureNumber must be greater than 0.";
            }

            if (!captures.Add(record.CaptureNumber.Value))
            {
                return $"{row}: capture {record.CaptureNumber} appears more than once.";
            }

            if (record.StartTime is not { } start || record.EndTime is not { } end)
            {
                return $"{row}: startTime and endTime are required.";
            }

            if (start.Year < 2000 || end > latest)
            {
                return $"{row}: the measurement time {start:O} is not plausible.";
            }

            if (end < start)
            {
                return $"{row}: endTime is before startTime.";
            }

            if (record.SampleCount is not >= 0)
            {
                return $"{row}: sampleCount is required and cannot be negative.";
            }

            if (record.DepthFrom is not { } from || record.DepthTo is not { } to)
            {
                return $"{row}: depthFrom and depthTo are required.";
            }

            if (from < 0 || to > MaxDepthMetres || decimal.Round(from, 3) != from || decimal.Round(to, 3) != to)
            {
                return $"{row}: depths must be between 0 and {MaxDepthMetres} m with at most 3 decimals.";
            }

            if (to <= from)
            {
                return $"{row}: depthTo must be greater than depthFrom.";
            }

            if (record.TsMs is < 0)
            {
                return $"{row}: tsMs cannot be negative.";
            }

            if (new[] { record.Count0, record.Count0Raw, record.Hdm, record.HdmRaw, record.DoseRate }.Any(v => v is { } value && !double.IsFinite(value)))
            {
                return $"{row}: device values must be finite numbers.";
            }
        }

        return null;
    }

    /// <summary>
    /// SHA-256 (hex) of the dataset's content: session, metadata and every record (in capture order). The uploading
    /// user is left out, so the same data uploaded again by anyone, or retried after a timeout, has the same fingerprint.
    /// Call only after <see cref="Validate"/> returned null.
    /// </summary>
    public string Fingerprint()
    {
        var info = MeasurementInformation!;
        var canonical = new StringBuilder();

        void Field(object? value) => canonical.Append(value switch
        {
            null => "␀",
            DateTimeOffset time => time.UtcTicks.ToString(CultureInfo.InvariantCulture),
            decimal number => number.ToString("0.000", CultureInfo.InvariantCulture),
            double number => number.ToString("R", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        }).Append('␟');

        foreach (var value in new object?[] { info.SessionId, info.Drillhole, info.Project, info.Campaign, info.Operator, info.Notes, info.CreatedUsername })
        {
            Field(value);
        }

        foreach (var r in Records!.OrderBy(r => r.CaptureNumber))
        {
            canonical.Append('␞');

            foreach (var value in new object?[] { r.CaptureNumber, r.StartTime, r.EndTime, r.SampleCount, r.DepthFrom, r.DepthTo, r.TsMs, r.Count0, r.Count0Raw, r.Hdm, r.HdmRaw, r.DoseRate })
            {
                Field(value);
            }
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private static string? Text(string name, string? value, int maxLength, bool required) =>
        required && string.IsNullOrWhiteSpace(value) ? $"{name} is required." :
        value?.Length > maxLength ? $"{name} is longer than {maxLength} characters." :
        null;
}

/// <summary>
/// Metadata of the uploaded session. CreatedUsername and UploadingUsername are declared by the desktop app and stored as
/// declared; they are not an authenticated identity (see MeasurementUploadController).
/// </summary>
public sealed record MeasurementUploadInformation(
    Guid? SessionId,
    string? Drillhole,
    string? Project,
    string? Campaign,
    string? Operator,
    string? Notes,
    string? CreatedUsername,
    string? UploadingUsername);

/// <summary>One measurement row. Depths in metres; Count0 is the CPS (counts per second); TsMs the device's Unix ms.</summary>
public sealed record MeasurementUploadRecord(
    int? CaptureNumber,
    DateTimeOffset? StartTime,
    DateTimeOffset? EndTime,
    int? SampleCount,
    decimal? DepthFrom,
    decimal? DepthTo,
    long? TsMs,
    double? Count0,
    double? Count0Raw,
    double? Hdm,
    double? HdmRaw,
    double? DoseRate);
