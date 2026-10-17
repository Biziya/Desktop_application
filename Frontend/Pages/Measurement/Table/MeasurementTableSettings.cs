using System.IO;
using System.Windows;
using System.Windows.Controls;
using Frontend.Shared.Infrastructure;

namespace Frontend.Pages.Measurement.Table;

/// <summary>How the table shows one known column (from MeasurementTable.yaml).</summary>
public sealed record MeasurementTableColumn(
    MeasurementColumn Column,
    string Header,
    bool Visible,
    DataGridLength Width,
    double MinWidth,
    double MaxWidth,
    Func<object?, string?>? Format,
    TextAlignment Alignment);

/// <summary>MeasurementTable.yaml: the table's columns and sizes. A size with a multiplier is used as size × multiplier.</summary>
public sealed class MeasurementTableSettings
{
    public const string File = "Pages/Measurement/Table/MeasurementTable.yaml";

    public required List<ColumnSettings> Columns { get; init; }

    public required MultiplierSettings Multipliers { get; init; }

    public required SizeSettings Sizes { get; init; }

    /// <summary>
    /// The resources MeasurementTableView.xaml uses: final sizes (multipliers applied) and, as "MeasurementTableColumns",
    /// the columns in table order.
    /// </summary>
    public void AddTo(ResourceDictionary resources)
    {
        resources.Add("MeasurementTableColumns", TableColumns());

        resources.Add("TableHeaderFontSize", Sizes.HeaderFontSize * Multipliers.HeaderFontSize);
        resources.Add("TableRowFontSize", Sizes.RowFontSize * Multipliers.RowFontSize);
        resources.Add("TableHeaderMinHeight", Sizes.HeaderMinHeight * Multipliers.HeaderHeight);
        resources.Add("TableRowHeight", Sizes.RowHeight * Multipliers.RowHeight);
        resources.Add("TableHeaderPadding", Scaled(Sizes.HeaderPadding, Multipliers.CellPadding));
        resources.Add("TableCellPadding", Scaled(Sizes.CellPadding, Multipliers.CellPadding));
        resources.Add("TableDeleteIconFontSize", Sizes.DeleteIconFontSize * Multipliers.DeleteIcon);
        resources.Add("TableDeleteButtonSize", Sizes.DeleteButtonSize * Multipliers.DeleteIcon);
        resources.Add("TableDeleteColumnWidth", new DataGridLength(Sizes.DeleteColumnWidth * Multipliers.DeleteIcon));
        resources.Add("TableHeadingFontSize", Sizes.HeadingFontSize * Multipliers.HeadingFontSize);
        resources.Add("TableActionButtonFontSize", Sizes.ActionButtonFontSize * Multipliers.ActionButtonFontSize);
        resources.Add("TableActionButtonMinWidth", Sizes.ActionButtonMinWidth * Multipliers.ActionButtonWidth);
        resources.Add("TableActionButtonMinHeight", Sizes.ActionButtonMinHeight * Multipliers.ActionButtonHeight);
        resources.Add("TableEmptyTextFontSize", Sizes.EmptyTextFontSize * Multipliers.EmptyTextFontSize);
    }

    /// <summary>The columns in this file's order. Every known column must be listed exactly once.</summary>
    public IReadOnlyList<MeasurementTableColumn> TableColumns()
    {
        var known = MeasurementTableColumns.All.ToDictionary(c => c.Id);
        var listed = new HashSet<string>();
        var columns = new List<MeasurementTableColumn>();

        for (var i = 0; i < Columns.Count; i++)
        {
            var entry = Columns[i];
            var key = $"columns[{i}]";

            if (!known.TryGetValue(entry.Id, out var column))
            {
                throw Invalid($"{key}.id", $"unknown column '{entry.Id}'. Known columns: {string.Join(", ", known.Keys)}.");
            }

            if (!listed.Add(entry.Id))
            {
                throw Invalid($"{key}.id", $"column '{entry.Id}' is listed more than once.");
            }

            if (entry.MaxWidth < entry.MinWidth)
            {
                throw Invalid($"{key}.maxWidth", $"must not be smaller than minWidth ({entry.MinWidth}).");
            }

            var format = entry.Format is null ? null : MeasurementCellFormat.Named(entry.Format)
                ?? throw Invalid($"{key}.format", $"'{entry.Format}' is not supported. Use one of: {string.Join(", ", MeasurementCellFormat.Names)}.");

            var alignment = entry.Alignment switch
            {
                "left" => TextAlignment.Left,
                "center" => TextAlignment.Center,
                "right" => TextAlignment.Right,
                _ => throw Invalid($"{key}.alignment", $"'{entry.Alignment}' is not supported. Use left, center or right.")
            };

            columns.Add(new MeasurementTableColumn(column, entry.Header, entry.Visible, entry.Width, entry.MinWidth,
                entry.MaxWidth ?? double.PositiveInfinity, format, alignment));
        }

        if (known.Keys.Where(id => !listed.Contains(id)).ToList() is { Count: > 0 } missing)
        {
            throw Invalid("columns", $"missing {string.Join(", ", missing)}. List every column; use visible: false to hide one.");
        }

        return columns;
    }

    private static InvalidDataException Invalid(string key, string message) => YamlSettings.Invalid(File, key, message);

    private static Thickness Scaled(Thickness t, double multiplier) =>
        new(t.Left * multiplier, t.Top * multiplier, t.Right * multiplier, t.Bottom * multiplier);

    public sealed class ColumnSettings
    {
        public required string Id { get; init; }
        public required string Header { get; init; }
        public required bool Visible { get; init; }
        public required DataGridLength Width { get; init; }
        public required double MinWidth { get; init; }
        public double? MaxWidth { get; init; }
        public string? Format { get; init; }
        public required string Alignment { get; init; }
    }

    public sealed class MultiplierSettings
    {
        public required double HeaderFontSize { get; init; }
        public required double RowFontSize { get; init; }
        public required double HeaderHeight { get; init; }
        public required double RowHeight { get; init; }
        public required double CellPadding { get; init; }
        public required double DeleteIcon { get; init; }
        public required double HeadingFontSize { get; init; }
        public required double ActionButtonFontSize { get; init; }
        public required double ActionButtonWidth { get; init; }
        public required double ActionButtonHeight { get; init; }
        public required double EmptyTextFontSize { get; init; }
    }

    public sealed class SizeSettings
    {
        public required double HeaderFontSize { get; init; }
        public required double RowFontSize { get; init; }
        public required double HeaderMinHeight { get; init; }
        public required double RowHeight { get; init; }
        public required Thickness HeaderPadding { get; init; }
        public required Thickness CellPadding { get; init; }
        public required double DeleteIconFontSize { get; init; }
        public required double DeleteButtonSize { get; init; }
        public required double DeleteColumnWidth { get; init; }
        public required double HeadingFontSize { get; init; }
        public required double ActionButtonFontSize { get; init; }
        public required double ActionButtonMinWidth { get; init; }
        public required double ActionButtonMinHeight { get; init; }
        public required double EmptyTextFontSize { get; init; }
    }
}
