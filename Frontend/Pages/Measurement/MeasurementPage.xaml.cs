using System.Globalization;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Frontend.Pages.Measurement;

// View only: builds the measurement columns, scrolls a new row into view and forwards focus events. Row order (by From) belongs to the VM.
public partial class MeasurementPage : UserControl
{
    private ObservableCollection<MeasurementResult>? observedResults;

    public MeasurementPage()
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

    // Columns, order, labels and bindings come from MeasurementColumns.cs; visibility and widths from
    // MeasurementColumnLayout.cs. They go before the Delete column (last) declared in XAML.
    private void AddMeasurementColumns()
    {
        var index = 0;
        var widths = new DataGridLengthConverter();

        foreach (var column in MeasurementColumns.All)
        {
            var layout = MeasurementColumnLayout.For(column);

            if (!layout.Visible)
            {
                continue;
            }

            ResultsGrid.Columns.Insert(index++, new DataGridTextColumn
            {
                Header = column.Label,
                Binding = new Binding(column.Binding)
                {
                    Mode = BindingMode.OneWay,
                    StringFormat = column.DisplayFormat,
                    Converter = column.Formatter is { } format ? new CellFormatter(format) : null
                },
                IsReadOnly = true,
                Width = (DataGridLength)widths.ConvertFromInvariantString(layout.Width)!,
                MinWidth = layout.MinWidth,
                MaxWidth = layout.MaxWidth,
                ElementStyle = (Style)FindResource(column.Kind == MeasurementCellKind.Number ? "NumberTableText" : "TextTableText"),
                HeaderStyle = (Style)FindResource("CenteredTableHeader")
            });
        }
    }

    // The page is created per navigation while the ViewModel lives on, so the subscription must not outlive the page.
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

    // Leaving Starting Depth or Step commits it: invalid text is reported in a toast (typing only colours the border).
    private void DepthCommitted(object sender, RoutedEventArgs e) => (DataContext as MeasurementViewModel)?.CommitDepthInputs();

    // The duration editor opens with its text selected, ready to type over.
    private void DurationEditorOpened(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        DurationBox.Focus();
        DurationBox.SelectAll();
    });

    private void OnResultsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is { Count: > 0 })
        {
            ResultsGrid.ScrollIntoView(e.NewItems[0]);
        }
    }

    // Table-only display formatting of a column (MeasurementCellFormat); the bound value itself is never changed.
    private sealed class CellFormatter(Func<object?, string?> format) : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => format(value) ?? "";

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
    }
}
