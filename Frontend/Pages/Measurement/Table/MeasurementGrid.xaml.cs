using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Frontend.Pages.Measurement.Table;

// View only: builds the measurement columns and scrolls a new row into view. Row order (by From) belongs to the VM.
public partial class MeasurementGrid : UserControl
{
    public static readonly DependencyProperty EmptyTextProperty =
        DependencyProperty.Register(nameof(EmptyText), typeof(string), typeof(MeasurementGrid), new PropertyMetadata(""));

    public MeasurementGrid()
    {
        InitializeComponent();
        AddMeasurementColumns();

        // The grid's own item view follows whatever Results the DataContext provides, for as long as the grid exists.
        ((INotifyCollectionChanged)ResultsGrid.Items).CollectionChanged += OnResultsChanged;
    }

    /// <summary>Shown over the empty table.</summary>
    public string EmptyText
    {
        get => (string)GetValue(EmptyTextProperty);
        set => SetValue(EmptyTextProperty, value);
    }

    // The visible columns of MeasurementTable.yaml, in its order, before the Delete column (last) declared in XAML.
    private void AddMeasurementColumns()
    {
        var columns = (IReadOnlyList<MeasurementTableColumn>)FindResource("MeasurementTableColumns");
        var cellText = (Style)FindResource("CellText");
        var header = (Style)FindResource("CenteredTableHeader");
        var index = 0;

        foreach (var column in columns.Where(c => c.Visible))
        {
            ResultsGrid.Columns.Insert(index++, new DataGridTextColumn
            {
                Header = column.Header,
                Binding = new Binding(column.Column.Binding)
                {
                    Mode = BindingMode.OneWay,
                    Converter = column.Format is { } format ? new CellFormatter(format) : null
                },
                IsReadOnly = true,
                Width = column.Width,
                MinWidth = column.MinWidth,
                MaxWidth = column.MaxWidth,
                ElementStyle = new Style(typeof(TextBlock), cellText)
                {
                    Setters = { new Setter(TextBlock.TextAlignmentProperty, column.Alignment) }
                },
                HeaderStyle = header
            });
        }
    }

    private void OnResultsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is { Count: > 0 })
        {
            ResultsGrid.ScrollIntoView(e.NewItems[0]);
        }
    }

    // Table-only display formatting of a column; the bound value itself is never changed.
    private sealed class CellFormatter(Func<object?, string?> format) : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => format(value) ?? "";

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
    }
}
