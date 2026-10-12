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
    private const string Header =
        "No.,Capture No.,Start Time,End Time,Sample Count,Average Value,Unit,Drillhole ID,Drillhole Name," +
        "Project / Area,Core ID,Depth From (m),Depth To (m),Core Depth (m),Operator,Notes,Completed At\r\n";

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

    public static string Serialize(IReadOnlyList<MeasurementResult> rows)
    {
        var csv = new StringBuilder(Header);

        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];

            // Numbers and dates are quoted only; free text is also guarded against spreadsheet formulas.
            string[] cells =
            [
                Quote((i + 1).ToString(CultureInfo.InvariantCulture)),
                Quote(r.CaptureNumber.ToString(CultureInfo.InvariantCulture)),
                Quote(r.StartTime.ToString("O")),
                Quote(r.EndTime.ToString("O")),
                Quote(r.SampleCount.ToString(CultureInfo.InvariantCulture)),
                Quote(r.AverageValue.ToString("F4", CultureInfo.InvariantCulture)),
                Text(r.Unit),
                Text(r.Core.DrillholeId),
                Text(r.Core.DrillholeName),
                Text(r.Core.ProjectArea),
                Text(r.Core.CoreId),
                Quote(r.Core.DepthFrom.ToString(CultureInfo.InvariantCulture)),
                Quote(r.Core.DepthTo.ToString(CultureInfo.InvariantCulture)),
                Quote(r.Core.CoreDepth.ToString(CultureInfo.InvariantCulture)),
                Text(r.Core.Operator),
                Text(r.Core.Notes),
                Quote(r.CompletedAt.ToString("O"))
            ];

            csv.AppendLine(string.Join(",", cells));
        }

        return csv.ToString();
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

    /// <summary>Quotes user-entered text and prevents it being interpreted as a spreadsheet formula.</summary>
    private static string Text(string value)
    {
        var trimmed = value.TrimStart();

        if (trimmed.Length > 0 && trimmed[0] is '=' or '+' or '-' or '@')
        {
            value = "'" + value;
        }

        return Quote(value);
    }
}
