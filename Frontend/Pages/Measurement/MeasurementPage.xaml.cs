using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace Frontend.Pages.Measurement;

// Only pointer hit testing, drop feedback and scrolling live in the view; collection changes belong to the VM.
public partial class MeasurementPage : UserControl
{
    // While dragging, the grid scrolls when the pointer is within these distances of its top/bottom edge.
    // The top zone is larger because it also covers the column header row.
    private const double EdgeScrollTopZone = 70;
    private const double EdgeScrollBottomZone = 35;

    private Point dragStart;
    private MeasurementResult? candidate;
    private DataGridRow? highlighted;
    private ScrollViewer? scrollViewer;
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
        Loaded += OnLoaded;
        Unloaded += (_, _) => StopObservingResults();
    }

    // Visible columns, their order, labels and bindings come from MeasurementColumns.cs; sizes from MeasurementUiConstants.xaml.
    // They go between the drag handle (first) and Delete (last) columns declared in XAML.
    private void AddMeasurementColumns()
    {
        var index = 1;

        foreach (var column in MeasurementColumns.All.Where(c => c.ShowInTable))
        {
            ResultsGrid.Columns.Insert(index++, new DataGridTextColumn
            {
                Header = column.Label,
                Binding = new Binding(column.Binding) { Mode = BindingMode.OneWay, StringFormat = column.DisplayFormat },
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                MinWidth = ScaleExtension.Size("TableValueColumnMinWidth"),
                ElementStyle = (Style)FindResource("CenteredTableText"),
                HeaderStyle = (Style)FindResource("CenteredTableHeader")
            });
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        scrollViewer ??= FindScrollViewer(ResultsGrid);
        StartObservingResults();
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

    private void OnResultsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is { Count: > 0 })
        {
            ResultsGrid.ScrollIntoView(e.NewItems[0]);
        }
    }

    private void HandleDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MeasurementResult row })
        {
            candidate = row;
            dragStart = e.GetPosition(ResultsGrid);
        }
    }

    private void EndPointer(object sender, MouseButtonEventArgs e) => candidate = null;

    private void BeginDrag(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            candidate = null;
            return;
        }

        if (candidate is not { } row)
        {
            return;
        }

        var position = e.GetPosition(ResultsGrid);

        if (Math.Abs(position.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        candidate = null;

        try
        {
            DragDrop.DoDragDrop(ResultsGrid, new DataObject(typeof(MeasurementResult), row), DragDropEffects.Move);
        }
        finally
        {
            ClearFeedback();
        }
    }

    private static T? Ancestor<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element is not null)
        {
            if (element is T found)
            {
                return found;
            }

            element = element is FrameworkContentElement content ? content.Parent : VisualTreeHelper.GetParent(element);
        }

        return null;
    }

    private void ClearFeedback()
    {
        highlighted?.ClearValue(Control.BorderBrushProperty);
        highlighted?.ClearValue(Control.BorderThicknessProperty);
        highlighted = null;
    }

    private void RowDragLeave(object sender, DragEventArgs e) => ClearFeedback();

    private void RowDragOver(object sender, DragEventArgs e)
    {
        ClearFeedback();
        e.Effects = DragDropEffects.None;
        e.Handled = true;

        if (!e.Data.GetDataPresent(typeof(MeasurementResult)) ||
            DataContext is not MeasurementViewModel vm ||
            e.Data.GetData(typeof(MeasurementResult)) is not MeasurementResult source ||
            !vm.Results.Contains(source))
        {
            return;
        }

        var target = Ancestor<DataGridRow>(e.OriginalSource as DependencyObject);

        if (target?.Item is not MeasurementResult)
        {
            return;
        }

        highlighted = target;
        target.BorderBrush = Brushes.DodgerBlue;
        target.BorderThickness = e.GetPosition(target).Y > target.ActualHeight / 2
            ? new Thickness(0, 0, 0, 3)
            : new Thickness(0, 3, 0, 0);
        e.Effects = DragDropEffects.Move;

        // Edge scroll makes rows outside the currently virtualized viewport reachable.
        var point = e.GetPosition(ResultsGrid);
        scrollViewer ??= FindScrollViewer(ResultsGrid);

        if (point.Y < EdgeScrollTopZone)
        {
            scrollViewer?.LineUp();
        }
        else if (point.Y > ResultsGrid.ActualHeight - EdgeScrollBottomZone)
        {
            scrollViewer?.LineDown();
        }
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject element)
    {
        if (element is ScrollViewer viewer)
        {
            return viewer;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
        {
            if (FindScrollViewer(VisualTreeHelper.GetChild(element, i)) is { } result)
            {
                return result;
            }
        }

        return null;
    }

    private void RowDrop(object sender, DragEventArgs e)
    {
        ClearFeedback();
        e.Handled = true;

        var target = Ancestor<DataGridRow>(e.OriginalSource as DependencyObject);

        if (DataContext is MeasurementViewModel vm &&
            e.Data.GetData(typeof(MeasurementResult)) is MeasurementResult source &&
            target?.Item is MeasurementResult destination)
        {
            vm.Move(source, destination, e.GetPosition(target).Y > target.ActualHeight / 2);
        }
    }
}
