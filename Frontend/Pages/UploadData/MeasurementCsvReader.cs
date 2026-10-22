using System.Globalization;
using System.Text;
using Frontend.Calculation.Measurement;
using Frontend.Pages.Measurement;
using Frontend.Pages.Measurement.Table;

namespace Frontend.Pages.UploadData;

/// <summary>
/// Metadata of an imported CSV: the same on every row. A column missing from the file (older exports have no
/// Created Username; the oldest no metadata at all) is null, never guessed.
/// </summary>
public sealed record ImportedMetadata(
    Guid SessionId,
    string? Drillhole,
    string? Project,
    string? Campaign,
    string? Operator,
    string? Notes,
    string? CreatedUsername);

/// <summary>An imported CSV: its metadata and its rows in display order (sorted by From, numbered from 1).</summary>
public sealed record ImportedMeasurements(ImportedMetadata Metadata, IReadOnlyList<MeasurementResult> Rows);

/// <summary>The CSV cannot be imported; the message says why (with the row for a value problem).</summary>
public sealed class CsvImportException(string message) : Exception(message);

/// <summary>
/// Reads a CSV exported by the Measurement page (CsvExportService) back into measurement rows. Columns are found by
/// header name, so their order does not matter and unknown columns are ignored. Quoted values may contain commas,
/// quotes and line breaks; CRLF and LF line endings are accepted. Required: Capture No, Start Time, End Time, Sample and
/// session_id. Depths (from_m, to_m, step_m), device values and metadata are read when present and stay empty when the
/// file is older than the column. Reading never changes the file.
/// </summary>
public static class MeasurementCsvReader
{
    private static readonly string[] Required =
    [
        MeasurementTableColumns.CaptureNumber.CsvHeader,
        MeasurementTableColumns.StartTime.CsvHeader,
        MeasurementTableColumns.EndTime.CsvHeader,
        MeasurementTableColumns.Sample.CsvHeader,
        MeasurementTableColumns.SessionId.CsvHeader
    ];

    public static ImportedMeasurements Read(string text)
    {
        var records = Records(text.TrimStart('﻿'));

        if (records.Count == 0)
        {
            throw new CsvImportException("The file is empty.");
        }

        var header = records[0].Fields.Select(h => h.Trim()).ToList();
        var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < header.Count; i++)
        {
            if (header[i].Length > 0 && !columns.TryAdd(header[i], i))
            {
                throw new CsvImportException($"The column '{header[i]}' appears more than once.");
            }
        }

        if (Required.Where(name => !columns.ContainsKey(name)).ToList() is { Count: > 0 } missing)
        {
            throw new CsvImportException(
                $"This is not a measurement CSV: missing column{(missing.Count > 1 ? "s" : "")} {string.Join(", ", missing)}.");
        }

        if (records.Count == 1)
        {
            throw new CsvImportException("The file contains no measurements.");
        }

        var rows = new List<MeasurementResult>();
        ImportedMetadata? metadata = null;
        var captures = new HashSet<int>();

        for (var r = 1; r < records.Count; r++)
        {
            var row = new Row(r, records[r].Line, records[r].Fields, columns);

            if (records[r].Fields.Count != header.Count)
            {
                throw row.Error($"expected {header.Count} values, found {records[r].Fields.Count}.");
            }

            var rowMetadata = new ImportedMetadata(
                row.Guid(MeasurementTableColumns.SessionId.CsvHeader),
                row.Text(CsvExportService.DrillholeHeader),
                row.Text(CsvExportService.ProjectHeader),
                row.Text(CsvExportService.CampaignHeader),
                row.Text(CsvExportService.OperatorHeader),
                row.Text(CsvExportService.NotesHeader),
                row.Text(CsvExportService.CreatedUsernameHeader));

            // One export holds one measurement session, so the metadata repeated on each row must agree.
            if (metadata is not null && metadata != rowMetadata)
            {
                throw row.Error("its session or Drillhole, Project, Campaign, Operator, Notes or Created Username differs from the first row. " +
                                "Import one exported measurement session per file.");
            }

            metadata = rowMetadata;
            var result = Measurement(row, rowMetadata.SessionId);

            if (!captures.Add(result.CaptureNumber))
            {
                throw row.Error($"Capture No {result.CaptureNumber} appears more than once.");
            }

            rows.Add(result);
        }

        // Same order and numbering as the Measurement page.
        var sorted = MeasurementOrder.Sort(rows);

        for (var i = 0; i < sorted.Count; i++)
        {
            sorted[i].Number = i + 1;
        }

        return new ImportedMeasurements(metadata!, sorted);
    }

    private static MeasurementResult Measurement(Row row, Guid sessionId)
    {
        var start = row.Time(MeasurementTableColumns.StartTime.CsvHeader);
        var end = row.Time(MeasurementTableColumns.EndTime.CsvHeader);

        if (end < start)
        {
            throw row.Error("End Time is before Start Time.");
        }

        var captureNumber = row.Integer(MeasurementTableColumns.CaptureNumber.CsvHeader) ?? throw row.Missing(MeasurementTableColumns.CaptureNumber.CsvHeader);

        if (captureNumber <= 0)
        {
            throw row.Error($"{MeasurementTableColumns.CaptureNumber.CsvHeader} must be greater than 0.");
        }

        var samples = row.Integer(MeasurementTableColumns.Sample.CsvHeader) ?? throw row.Missing(MeasurementTableColumns.Sample.CsvHeader);

        if (samples < 0)
        {
            throw row.Error($"{MeasurementTableColumns.Sample.CsvHeader} cannot be negative.");
        }

        var fromMm = row.Metres(MeasurementTableColumns.From.CsvHeader);
        var toMm = row.Metres(MeasurementTableColumns.To.CsvHeader);
        var stepMm = row.Metres(MeasurementTableColumns.Step.CsvHeader);

        if (fromMm is not null && toMm is not null && toMm <= fromMm)
        {
            throw row.Error("to_m must be greater than from_m.");
        }

        return new MeasurementResult
        {
            SessionId = sessionId,
            CaptureNumber = captureNumber,
            StartTime = start,
            EndTime = end,
            SampleCount = samples,
            DurationMs = (long)(end - start).TotalMilliseconds,
            Reading = new DeviceReading(
                row.Long(MeasurementTableColumns.TsMs.CsvHeader),
                row.Number(MeasurementTableColumns.Count0.CsvHeader),
                row.Number(MeasurementTableColumns.Count0Raw.CsvHeader),
                row.Number(MeasurementTableColumns.Hdm.CsvHeader),
                row.Number(MeasurementTableColumns.HdmRaw.CsvHeader),
                row.Number(MeasurementTableColumns.DoseRate.CsvHeader)),
            FromMm = fromMm,
            ToMm = toMm,
            StepMm = stepMm ?? toMm - fromMm
        };
    }

    // One data row: values by header name, and errors that name the row (and the line where it starts).
    private sealed class Row(int number, int line, List<string> fields, Dictionary<string, int> columns)
    {
        public CsvImportException Error(string message) => new($"Row {number} (line {line}): {message}");

        public CsvImportException Missing(string column) => Error($"{column} is empty.");

        // Null when the file has no such column; "" when the cell is empty.
        private string? Value(string column) => columns.TryGetValue(column, out var index) ? fields[index] : null;

        // Text written by CsvExportService.Text: its "'" in front of =, +, - or @ (against spreadsheet formulas) is removed.
        public string? Text(string column) => Value(column) is { } value
            && value.Length > 1 && value[0] == '\'' && value[1..].TrimStart() is { Length: > 0 } rest && rest[0] is '=' or '+' or '-' or '@'
                ? value[1..]
                : Value(column);

        public Guid Guid(string column) => System.Guid.TryParse(Value(column), out var id) && id != System.Guid.Empty
            ? id
            : throw Error($"{column} '{Value(column)}' is not a session ID.");

        public DateTimeOffset Time(string column)
        {
            var value = Value(column)?.Trim() ?? "";

            if (value.Length == 0)
            {
                throw Missing(column);
            }

            return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
                ? time
                : throw Error($"{column} '{value}' is not a date and time.");
        }

        public int? Integer(string column) => Parse(column, (string s, out int v) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v), "a whole number");

        public long? Long(string column) => Parse(column, (string s, out long v) => long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v), "a whole number");

        public double? Number(string column) => Parse(column,
            (string s, out double v) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && double.IsFinite(v), "a number");

        // Metres to whole millimetres, exactly as the Measurement page stores them (at most 3 decimals, not negative).
        public long? Metres(string column) => Parse(column, (string s, out long v) => Depth.TryParseMetres(s, out v),
            "a depth in metres (not negative, at most 3 decimals)");

        private delegate bool TryParse<T>(string text, out T value);

        // An empty cell (or a missing optional column) is null: a value the device or an older export did not provide.
        private T? Parse<T>(string column, TryParse<T> parse, string expected) where T : struct
        {
            var value = Value(column)?.Trim();

            if (string.IsNullOrEmpty(value))
            {
                return null;
            }

            return parse(value, out var parsed) ? parsed : throw Error($"{column} '{value}' is not {expected}.");
        }
    }

    // RFC 4180 records with the line each starts on. Blank lines are skipped.
    private static List<(int Line, List<string> Fields)> Records(string text)
    {
        var records = new List<(int, List<string>)>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var fieldWasQuoted = false;
        var line = 1;
        var recordLine = 1;
        var i = 0;

        void EndRecord()
        {
            fields.Add(field.ToString());

            if (fields.Count > 1 || fields[0].Length > 0 || fieldWasQuoted)
            {
                records.Add((recordLine, fields));
            }

            fields = [];
            field.Clear();
            fieldWasQuoted = false;
        }

        while (i < text.Length)
        {
            var c = text[i];

            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i += 2;
                    continue;
                }

                if (c == '"')
                {
                    quoted = false;
                    i++;

                    if (i < text.Length && text[i] is not (',' or '\r' or '\n'))
                    {
                        throw new CsvImportException($"Line {line}: unexpected text after a closing quote.");
                    }

                    continue;
                }

                if (c == '\n')
                {
                    line++;
                }

                field.Append(c);
                i++;
                continue;
            }

            switch (c)
            {
                case '"' when field.Length == 0 && !fieldWasQuoted:
                    quoted = true;
                    fieldWasQuoted = true;
                    i++;
                    break;
                case '"':
                    throw new CsvImportException($"Line {line}: a quote inside an unquoted value.");
                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    fieldWasQuoted = false;
                    i++;
                    break;
                case '\r' or '\n':
                    EndRecord();
                    i += c == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1;
                    line++;
                    recordLine = line;
                    break;
                default:
                    field.Append(c);
                    i++;
                    break;
            }
        }

        if (quoted)
        {
            throw new CsvImportException($"Line {recordLine}: a quoted value is not closed.");
        }

        if (field.Length > 0 || fields.Count > 0 || fieldWasQuoted)
        {
            EndRecord();
        }

        return records;
    }
}
