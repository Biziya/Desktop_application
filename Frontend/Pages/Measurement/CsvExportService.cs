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
        var dialog = new SaveFileDialog { Title = "Export measurements", Filter = "CSV files (*.csv)|*.csv",
            DefaultExt = ".csv", AddExtension = true, FileName = $"CT007-measurements-{DateTime.Now:yyyyMMdd-HHmmss}.csv" };
        if (dialog.ShowDialog(System.Windows.Application.Current.MainWindow) != true) return false;
        // Snapshot serialization happens before yielding so later reorder/delete cannot affect this export.
        var csv = Serialize(rows);
        var temporary = dialog.FileName + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, csv, new UTF8Encoding(true), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, dialog.FileName, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return true;
    }
    public static string Serialize(IReadOnlyList<MeasurementResult> rows)
    {
        var csv = new StringBuilder("No.,Capture No.,Start Time,End Time,Sample Count,Average Value,Unit,Drillhole ID,Drillhole Name,Project / Area,Core ID,Depth From (m),Depth To (m),Core Depth (m),Operator,Notes\r\n");
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            string[] cells = [(i+1).ToString(CultureInfo.InvariantCulture), r.CaptureNumber.ToString(CultureInfo.InvariantCulture),
                r.StartTime.ToString("O"), r.EndTime.ToString("O"), r.SampleCount.ToString(CultureInfo.InvariantCulture),
                r.AverageValue.ToString("F4", CultureInfo.InvariantCulture), r.Unit, r.Core.DrillholeId,
                r.Core.DrillholeName, r.Core.ProjectArea, r.Core.CoreId,
                r.Core.DepthFrom.ToString(CultureInfo.InvariantCulture), r.Core.DepthTo.ToString(CultureInfo.InvariantCulture),
                r.Core.CoreDepth.ToString(CultureInfo.InvariantCulture), r.Core.Operator, r.Core.Notes];
            csv.AppendLine(string.Join(",", cells.Select(Escape)));
        }
        return csv.ToString();
    }
    private static string Escape(string value)
    {
        // Prevent user-entered text being interpreted as a spreadsheet formula.
        if (value.TrimStart().StartsWith('=') || value.TrimStart().StartsWith('+') ||
            value.TrimStart().StartsWith('-') || value.TrimStart().StartsWith('@')) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
