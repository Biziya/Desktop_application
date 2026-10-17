using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Frontend.Pages.Measurement.Table;

// View only: builds the measurement columns and scrolls a new row into view. Row order (by From) belongs to the VM.
public partial class MeasurementTableView : UserControl
{
    private ObservableCollection<MeasurementResult>? observedResults;

    public MeasurementTableView()
    {
        InitializeComponent();
        AddMeasurementColumns();

        DataContextChanged += (_, _) =>
        {
            StopObservingResults();

            if (IsLoaded)
            {
                StartObservingResults();
            }
        };
        Loaded += (_, _) => StartObservingResults();
        Unloaded += (_, _) => StopObservingResults();
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

    // The view is created per navigation while the ViewModel lives on, so the subscription must not outlive the view.
    private void StartObservingResults()
    {
        if (observedResults is null && DataContext is MeasurementViewModel vm)
        {
            observedResults = vm.Results;
            observedResults.CollectionChanged += OnResultsChanged;
        }
    }

    private void StopObservingResults()
    {
        if (observedResults is not null)
        {
            observedResults.CollectionChanged -= OnResultsChanged;
            observedResults = null;
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
