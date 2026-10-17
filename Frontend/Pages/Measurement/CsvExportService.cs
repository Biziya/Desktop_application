using System.Globalization;
using System.IO;
using System.Text;
using Frontend.Pages.Measurement.Table;
using Microsoft.Win32;

namespace Frontend.Pages.Measurement;

public interface ICsvExportService
{
    Task<bool> ExportAsync(IReadOnlyList<MeasurementResult> rows, MeasurementSession session, CancellationToken cancellationToken);
}

public sealed class CsvExportService : ICsvExportService
{
    public async Task<bool> ExportAsync(IReadOnlyList<MeasurementResult> rows, MeasurementSession session, CancellationToken cancellationToken)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export measurements",
            Filter = "CSV files (*.csv)|*.csv",
            DefaultExt = ".csv",
            AddExtension = true,
            FileName = $"CT007-measurements-{DateTime.Now:yyyyMMdd-HHmmss}.csv"
        };

        if (dialog.ShowDialog(System.Windows.Application.Current.MainWindow) != true)
        {
            return false;
        }

        // Snapshot serialization happens before yielding so a later capture or delete cannot affect this export.
        var csv = Serialize(rows, session);
        var temporary = dialog.FileName + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            await File.WriteAllTextAsync(temporary, csv, new UTF8Encoding(true), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, dialog.FileName, true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }

        return true;
    }

    // Session metadata repeated on every row: CSV header (= Core Information label) → stored session field.
    // Placed just before session_id, which stays the last column.
    private static readonly (string Header, Func<MeasurementSession, string?> Value)[] SessionColumns =
    [
        ("Drillhole", s => s.DrillholeId),
        ("Project", s => s.DrillholeName),
        ("Campaign", s => s.Project),
        ("Operator", s => s.Operator),
        ("Notes", s => s.Comment)
    ];

    /// <summary>
    /// Measurement columns, order and headers come from MeasurementTableColumns.cs, followed by the metadata of
    /// <paramref name="session"/> and session_id. Rows are written in the given (displayed, From-sorted) order and must all belong to
    /// <paramref name="session"/>. ts_ms is the device value only — demo values are never exported.
    /// </summary>
    public static string Serialize(IReadOnlyList<MeasurementResult> rows, MeasurementSession session)
    {
        if (rows.Any(r => r.SessionId != session.Id))
        {
            throw new ArgumentException("A CSV export must contain only rows of the exported measurement session.", nameof(rows));
        }

        var columns = MeasurementTableColumns.All.Where(c => c.IncludeInCsv && c != MeasurementTableColumns.SessionId).ToArray();
        var metadata = SessionColumns.Select(c => Text(c.Value(session) ?? "")).ToArray();
        var csv = new StringBuilder();
        var headers = columns.Select(c => c.CsvHeader)
            .Concat(SessionColumns.Select(c => c.Header))
            .Append(MeasurementTableColumns.SessionId.CsvHeader);
        csv.Append(string.Join(",", headers.Select(Quote))).Append("\r\n");

        foreach (var row in rows)
        {
            var cells = columns.Select(c => Quote(Format(c.CsvValue(row))))
                .Concat(metadata)
                .Append(Quote(Format(MeasurementTableColumns.SessionId.CsvValue(row))));
            csv.Append(string.Join(",", cells)).Append("\r\n");
        }

        return csv.ToString();
    }

    // Invariant culture; timestamps keep sub-second precision and offset ("O"); doubles round-trip ("R");
    // a missing device value is an empty cell.
    private static string Format(object? value) => value switch
    {
        null => "",
        DateTimeOffset time => time.ToString("O", CultureInfo.InvariantCulture),
        double number => number.ToString("R", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? ""
    };

    // Every cell is quoted and embedded quotes are doubled, so commas and line breaks (e.g. in Notes) stay in one cell.
    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

    // User-entered text is also kept from being read as a spreadsheet formula.
    private static string Text(string value) =>
        Quote(value.TrimStart() is { Length: > 0 } trimmed && trimmed[0] is '=' or '+' or '-' or '@' ? "'" + value : value);
}
