using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace Frontend.Pages.Measurement;

public interface ICsvExportService
{
    Task<bool> ExportAsync(IReadOnlyList<MeasurementResult> rows, CancellationToken cancellationToken);
}

public sealed class CsvExportService : ICsvExportService
{
    public async Task<bool> ExportAsync(IReadOnlyList<MeasurementResult> rows, CancellationToken cancellationToken)
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

        // Snapshot serialization happens before yielding so later reorder/delete cannot affect this export.
        var csv = Serialize(rows);
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

    /// <summary>
    /// Columns, order and headers come from MeasurementColumns.cs. Rows are written in the given (display) order and
    /// must belong to one measurement session; session_id is the last column.
    /// </summary>
    public static string Serialize(IReadOnlyList<MeasurementResult> rows)
    {
        if (rows.Select(r => r.SessionId).Distinct().Skip(1).Any())
        {
            throw new ArgumentException("A CSV export must contain a single measurement session.", nameof(rows));
        }

        var columns = MeasurementColumns.All.Where(c => c.IncludeInCsv).ToArray();
        var csv = new StringBuilder();
        csv.Append(string.Join(",", columns.Select(c => Quote(c.CsvHeader)))).Append("\r\n");

        foreach (var row in rows)
        {
            csv.Append(string.Join(",", columns.Select(c => Quote(Format(c.Value(row)))))).Append("\r\n");
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

    // Every cell is quoted and embedded quotes are doubled. All columns are numbers, timestamps or IDs,
    // so there is no free text that could be read as a spreadsheet formula.
    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
}
